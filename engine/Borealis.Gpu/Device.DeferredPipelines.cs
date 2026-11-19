using Borealis.Gpu.Vulkan;
using System.Threading;
using System.Threading.Tasks;

namespace Borealis.Gpu;

/// <summary>The part manifest's lines read, parts queued from it, lines dropped and parts recorded (<see cref="Device.PartManifest"/>).</summary>
public readonly record struct PartManifestStats( int Loaded, int Queued, int Dropped, int Recorded );

/// <summary>Pipelines made on workers while their draws were skipped (<see cref="Device.DeferredPipelines"/>).</summary>
public readonly record struct DeferredPipelineStats( int Made, int Pending, int Failed, double AverageMs, double MaxMs, double AverageFrames, long SkippedDraws, long FramesWithSkips );

/// <summary>
/// Pipelines made on the builder threads while the draws that use them are skipped
/// (<see cref="GraphicsPipelineInfo.SkipUntilReady"/>), and parts made ahead (<see cref="Device.QueuePipelineParts"/>).
/// </summary>
public sealed unsafe partial class Device
{
	// What a draw is waiting for goes first, then parts a run linked before (the part manifest), then guesses; all are made
	// by the builder threads. A part queued from the manifest carries its line, dropped if the part fails to make
	readonly Queue<(PipelineHandle Handle, GraphicsPipelineInfo Info, ulong Layout, ulong Frame, long Requested)> buildQueue = new();
	readonly PriorityQueue<(GraphicsPipelineInfo Info, PipelineParts Parts, string Line), int> prepareQueue = new();
	readonly PriorityQueue<(GraphicsPipelineInfo Info, PipelineParts Parts, string Line), int> speculativeQueue = new();

	// A skipped pipeline's fragment part, made by another builder alongside the rest of it: goes before everything
	readonly Queue<(GraphicsPipelineInfo Info, PipelineParts Parts, string Line)> urgentQueue = new();

	/// <summary>Whether a skipped pipeline's fragment part is made on a builder of its own (<c>BOREALIS_SPLIT_PARTS=0</c> turns it off, to compare).</summary>
	static readonly bool splitParts = Environment.GetEnvironmentVariable( "BOREALIS_SPLIT_PARTS" ) != "0";
	Thread[] builders;
	int building;
	bool stopBuilding;
	int deferredMade, deferredFailed;
	long deferredTicks, deferredMaxTicks, deferredFrames;
	long skippedDraws, framesWithSkips;
	long lastSkipFrame = -1;

	/// <summary>
	/// Pipelines made on workers because a draw asked for one before its parts were made
	/// (<see cref="GraphicsPipelineInfo.SkipUntilReady"/>): how many, still pending and failed, how long and how many frames
	/// they took on average and at most, draws skipped meanwhile and the frames that skipped any.
	/// </summary>
	public DeferredPipelineStats DeferredPipelines
	{
		get
		{
			int pending;
			lock ( buildQueue ) pending = buildQueue.Count + building;
			var made = Volatile.Read( ref deferredMade );
			var ms = Interlocked.Read( ref deferredTicks ) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			return new DeferredPipelineStats( made, pending, Volatile.Read( ref deferredFailed ), made > 0 ? ms / made : 0,
				Interlocked.Read( ref deferredMaxTicks ) * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
				made > 0 ? Interlocked.Read( ref deferredFrames ) / (double)made : 0,
				Interlocked.Read( ref skippedDraws ), Interlocked.Read( ref framesWithSkips ) );
		}
	}

	/// <summary>
	/// Whether pipelines made with <see cref="GraphicsPipelineInfo.SkipUntilReady"/> are made on workers while their draws are
	/// skipped; off makes them where they're asked for, as every other pipeline is (tests that check a frame's pixels, say).
	/// On unless <c>-no-skip-draws</c> or <c>BOREALIS_SKIP_DRAWS=0</c>.
	/// </summary>
	public bool SkipDrawsUntilReady { get; set; } = !Environment.GetCommandLineArgs().Contains( "-no-skip-draws", StringComparer.OrdinalIgnoreCase )
		&& Environment.GetEnvironmentVariable( "BOREALIS_SKIP_DRAWS" ) != "0";

	/// <summary>Whether a pipeline can draw: false while workers make it (<see cref="GraphicsPipelineInfo.SkipUntilReady"/>).</summary>
	public bool IsReady( PipelineHandle pipeline ) => Volatile.Read( ref Get( pipeline ).Pipeline ) != 0;

	/// <summary>A command list skipped a draw whose pipeline isn't made yet.</summary>
	internal void CountSkippedDraw()
	{
		Interlocked.Increment( ref skippedDraws );
		var frame = (long)FrameNumber;
		if ( Interlocked.Exchange( ref lastSkipFrame, frame ) != frame ) Interlocked.Increment( ref framesWithSkips );
	}

	/// <summary>Whether the parts a pipeline links are all made, so linking it is quick.</summary>
	bool PartsReady( in GraphicsPipelineInfo info, ulong layout )
	{
		var preRasterization = PreRasterizationKey( info, layout );
		var fragment = FragmentKey( info, layout );
		var vertexInput = info.MeshShader.IsValid ? null : VertexInputKey( info );
		var output = OutputKey( info );
		lock ( partLock )
		{
			return Made( preRasterizationParts, preRasterization ) && Made( fragmentParts, fragment ) && (vertexInput is null || Made( vertexInputParts, vertexInput )) && Made( outputParts, output );
		}

		static bool Made( Dictionary<PartKey, PipelinePart> cache, PartKey key ) => cache.TryGetValue( key, out var part ) && part.Ready && part.Failed is null;
	}

	/// <summary>A handle whose pipeline the builder threads make; draws with it are skipped until then.</summary>
	PipelineHandle Defer( in GraphicsPipelineInfo info, ulong layout )
	{
		var (index, generation) = Pipelines.Add( new PipelineRecord
		{
			Layout = layout,
			BindPoint = Vulkan.Vk.BindPointGraphics,
			RenderState = RenderStateBits.Pack( info ),
			Pending = true,
		} );

		var handle = new PipelineHandle( index, generation );
		lock ( buildQueue )
		{
			// Its fragment part, the slowest, on another builder while this one makes the others; the link waits for it
			if ( splitParts && UsesLibraries( info ) && info.FragmentShader.IsValid ) urgentQueue.Enqueue( (info, PipelineParts.FragmentShader, null) );
			buildQueue.Enqueue( (handle, info, layout, FrameNumber, System.Diagnostics.Stopwatch.GetTimestamp()) );
			StartBuilders();
			Monitor.Pulse( buildQueue );
		}

		return handle;
	}

	/// <summary>
	/// Make a pipeline's parts on the builder threads, after any pipeline a draw is waiting for
	/// (<see cref="PreparePipelineParts"/> makes them on the calling thread). <paramref name="speculative"/> parts (a guess
	/// at what draws will ask for) go after the part manifest's, the lowest <paramref name="order"/> first. Does nothing
	/// without pipeline libraries.
	/// </summary>
	public void QueuePipelineParts( in GraphicsPipelineInfo info, PipelineParts parts = PipelineParts.All, bool speculative = false, int order = int.MaxValue ) => Queue( info, parts, null, speculative, order );

	// The manifest's parts by rank: the order the last run first linked them
	void Queue( in GraphicsPipelineInfo info, PipelineParts parts, string line, bool speculative, int rank )
	{
		if ( !UsesLibraries( info ) ) return;
		lock ( buildQueue )
		{
			if ( stopBuilding ) return;
			if ( speculative ) speculativeQueue.Enqueue( (info, parts, line), rank );
			else prepareQueue.Enqueue( (info, parts, line), rank );
			StartBuilders();
			Monitor.Pulse( buildQueue );
		}
	}

	//
	// The part manifest (PipelinePartManifest): parts linked in earlier runs, made on the builder threads as soon as they can be
	//

	PipelinePartManifest partManifest;

	// Binding layouts made for the manifest's parts, by what their slots are, kept until the device goes
	readonly Dictionary<string, BindingLayoutHandle> manifestLayouts = new();

	/// <summary>
	/// The part manifest (<c>pipeline-parts.txt</c> beside the pipeline cache): lines read from it, parts queued from it,
	/// lines dropped (their part failed) and parts recorded this run. Zero without pipeline libraries or a cache folder.
	/// </summary>
	public PartManifestStats PartManifest
	{
		get
		{
			var m = partManifest;
			return m is null ? default : new PartManifestStats( m.Loaded, m.Queued, m.Dropped, m.Recorded );
		}
	}

	/// <summary>Whether the device keeps a part manifest (<see cref="DeviceOptions.PipelinePartManifest"/>, with pipeline libraries and a cache folder).</summary>
	public bool PartManifestEnabled => partManifest is not null;

	/// <summary>
	/// Whether the part manifest has parts made from this shader's SPIR-V from an earlier run: they are queued as its module
	/// is made, so guessing its parts ahead is only extra work. Parts linked this run don't count: a first run guesses on.
	/// </summary>
	public bool PartsRecorded( ShaderHandle shader ) => partManifest is { } m && shader.IsValid && m.Has( ShaderHash( shader ) );

	void LoadPartManifest( DeviceOptions options )
	{
		if ( !pipelineLibraries || !options.PipelinePartManifest || options.PipelineCacheFolder is not { } folder ) return;
		partManifest = new PipelinePartManifest( System.IO.Path.Combine( folder, "pipeline-parts.txt" ) );
		spirvFolder = System.IO.Path.Combine( folder, "spirv" );

		// Vertex input and output parts need no shader: made now, before any draw
		foreach ( var entry in partManifest.TakeUnshaded() ) QueueRecorded( entry, default );

		// Shader parts from the SPIR-V kept beside the manifest, in the order the last run first needed them, so a first
		// frame's parts don't wait for its program to compile or its variant to be made: the program's own module finds them
		// by SPIR-V. On a worker, so making the device doesn't wait for the files
		var shaders = partManifest.ShadersByRank();
		if ( shaders.Count > 0 ) spirvLoad = Task.Run( () => LoadRecordedSpirv( shaders ) );
	}

	// SPIR-V of shaders the manifest has parts of, by hash (spirv/<hash>.spv beside it), and the modules made from it
	string spirvFolder;
	Task spirvLoad;
	readonly HashSet<UInt128> spirvKept = new();

	string SpirvPath( UInt128 hash ) => System.IO.Path.Combine( spirvFolder, $"{hash:x32}.spv" );

	void LoadRecordedSpirv( List<UInt128> shaders )
	{
		foreach ( var hash in shaders )
		{
			if ( disposed ) return;
			lock ( partLock )
			{
				// The program's own module came first
				if ( modulesAlive.ContainsKey( hash ) ) continue;
			}

			byte[] spirv;
			try
			{
				spirv = System.IO.File.ReadAllBytes( SpirvPath( hash ) );
			}
			catch ( Exception e ) when ( e is System.IO.IOException or UnauthorizedAccessException )
			{
				continue;
			}

			try
			{
				// A damaged file isn't the SPIR-V it's named by: dropped
				if ( spirv.Length < 20 || spirv.Length % 4 != 0 || SpirvHash( spirv ) != hash ) throw new ArgumentException( "damaged" );
				CreateShader( spirv, $"part manifest {(ulong)(hash >> 64):x16}" );
			}
			catch ( Exception e ) when ( e is ArgumentException or GpuException )
			{
				TryDelete( SpirvPath( hash ) );
			}
		}
	}

	/// <summary>A module was made with pipeline libraries and a manifest: its SPIR-V is kept beside the manifest, once.</summary>
	void KeepSpirv( UInt128 hash, ReadOnlySpan<byte> spirv )
	{
		if ( partManifest is null ) return;
		lock ( spirvKept )
		{
			if ( !spirvKept.Add( hash ) ) return;
		}

		var path = SpirvPath( hash );
		if ( System.IO.File.Exists( path ) ) return;
		var bytes = spirv.ToArray();
		lock ( moduleDestroys )
		{
			moduleDestroys.Add( Task.Run( () =>
			{
				var temporary = $"{path}.{Environment.ProcessId}.tmp";
				try
				{
					System.IO.Directory.CreateDirectory( spirvFolder );
					System.IO.File.WriteAllBytes( temporary, bytes );
					System.IO.File.Move( temporary, path, overwrite: true );
				}
				catch ( Exception e ) when ( e is System.IO.IOException or UnauthorizedAccessException )
				{
					TryDelete( temporary );
				}
			} ) );
		}
	}

	/// <summary>At shutdown: SPIR-V files no manifest line names any more go.</summary>
	void PruneSpirv()
	{
		if ( partManifest is null || !System.IO.Directory.Exists( spirvFolder ) ) return;
		var named = partManifest.Shaders();
		try
		{
			foreach ( var file in System.IO.Directory.EnumerateFiles( spirvFolder, "*.spv" ) )
			{
				var name = System.IO.Path.GetFileNameWithoutExtension( file );
				if ( !UInt128.TryParse( name, System.Globalization.NumberStyles.AllowHexSpecifier, null, out var hash ) || !named.Contains( hash ) ) TryDelete( file );
			}
		}
		catch ( Exception e ) when ( e is System.IO.IOException or UnauthorizedAccessException )
		{
			// Tried again next time
		}
	}

	static void TryDelete( string path )
	{
		try
		{
			System.IO.File.Delete( path );
		}
		catch ( Exception e ) when ( e is System.IO.IOException or UnauthorizedAccessException )
		{
			// Left for the next prune
		}
	}

	/// <summary>The manifest's parts made from a shader with this SPIR-V, queued for the builder threads with this module.</summary>
	void QueueRecordedParts( ShaderHandle shader, UInt128 hash )
	{
		if ( partManifest?.Take( hash ) is not { } entries ) return;
		foreach ( var entry in entries ) QueueRecorded( entry, shader );
	}

	void QueueRecorded( PipelinePartManifest.Entry entry, ShaderHandle shader )
	{
		if ( !PipelinePartManifest.Read( entry.Line, out var kind, out _, out var info, out var slots ) )
		{
			partManifest.Drop( entry.Line );
			return;
		}

		if ( kind is PipelineParts.PreRasterization or PipelinePartManifest.Compute ) info.VertexShader = shader;
		if ( kind == PipelineParts.FragmentShader ) info.FragmentShader = shader;
		if ( slots is not null ) info.Sets = ManifestSets( slots );
		info.Name = $"part manifest ({kind})";
		Queue( info, kind, entry.Line, speculative: false, entry.Rank );
	}

	//
	// Compute pipelines from the manifest: made whole on a builder, kept by their line until CreateComputePipeline asks
	//

	sealed class ComputeAhead
	{
		public ulong Pipeline;
		public bool Ready;
	}

	readonly Dictionary<string, ComputeAhead> computeAhead = new();

	// Lines CreateComputePipeline made itself this run: a builder that hadn't started one leaves it
	readonly HashSet<string> computeMadeHere = new();

	/// <summary>The manifest line for a compute pipeline, or null without a manifest (or when a set's layout went).</summary>
	string ComputeLine( in ComputePipelineInfo info )
	{
		if ( partManifest is null || shaderStatistics ) return null;
		try
		{
			return PipelinePartManifest.WriteCompute( info, ShaderHash( info.Shader ), SlotsOf( info.Sets ) );
		}
		catch ( ArgumentException )
		{
			return null;
		}
	}

	/// <summary>The pipeline a builder made ahead for this line (waiting if it's being made), or 0 to make it here.</summary>
	ulong TakeComputeAhead( string line )
	{
		lock ( computeAhead )
		{
			if ( !computeAhead.TryGetValue( line, out var ahead ) )
			{
				computeMadeHere.Add( line );
				return 0;
			}

			while ( !ahead.Ready ) Monitor.Wait( computeAhead );
			computeAhead.Remove( line );
			if ( ahead.Pipeline == 0 ) computeMadeHere.Add( line );
			return ahead.Pipeline;
		}
	}

	void MakeComputeAhead( in GraphicsPipelineInfo recorded, string line )
	{
		var ahead = new ComputeAhead();
		lock ( computeAhead )
		{
			if ( computeMadeHere.Contains( line ) || !computeAhead.TryAdd( line, ahead ) ) return;
		}

		var info = new ComputePipelineInfo { Shader = recorded.VertexShader, Entry = recorded.VertexEntry, Constants = recorded.VertexConstants, Sets = recorded.Sets, Name = recorded.Name };
		ulong pipeline = 0;
		var started = System.Diagnostics.Stopwatch.GetTimestamp();
		try
		{
			using ( Zone( ZonePipeline ) ) pipeline = MakeCompute( info, PipelineLayout( info.Sets ) );
			Interlocked.Add( ref computeTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started );
			Interlocked.Increment( ref computeMade );
		}
		finally
		{
			// A failure leaves 0: the pipeline is made where asked, which reports it
			lock ( computeAhead )
			{
				ahead.Pipeline = pipeline;
				ahead.Ready = true;
				Monitor.PulseAll( computeAhead );
			}
		}
	}

	/// <summary>At shutdown: compute pipelines made ahead that nothing asked for.</summary>
	void DestroyComputeAhead()
	{
		lock ( computeAhead )
		{
			foreach ( var ahead in computeAhead.Values )
			{
				if ( ahead.Pipeline != 0 ) Vk.vkDestroyPipeline( Vk.Device, ahead.Pipeline, null );
			}

			computeAhead.Clear();
		}
	}

	BindingLayoutHandle[] ManifestSets( BindingSlot[][] slots )
	{
		var sets = new BindingLayoutHandle[slots.Length];
		lock ( manifestLayouts )
		{
			for ( int s = 0; s < slots.Length; s++ )
			{
				if ( slots[s].Length == 0 ) continue;
				var key = string.Join( ';', slots[s] );
				if ( !manifestLayouts.TryGetValue( key, out sets[s] ) ) manifestLayouts[key] = sets[s] = CreateBindingLayout( slots[s] );
			}
		}

		return sets;
	}

	/// <summary>Wait until nothing is queued or being made on the builder threads, or the timeout passes. For tests and measuring.</summary>
	public bool WaitForPendingPipelines( TimeSpan timeout )
	{
		var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
		lock ( buildQueue )
		{
			while ( urgentQueue.Count + buildQueue.Count + prepareQueue.Count + speculativeQueue.Count + building > 0 )
			{
				var left = deadline - Environment.TickCount64;
				if ( left <= 0 ) return false;
				Monitor.Wait( buildQueue, (int)Math.Min( left, int.MaxValue ) );
			}
		}

		return true;
	}

	// Builders making parts ahead (the manifest's, guesses), and how many are kept for what draws wait for
	int buildingAhead;
	const int ReservedBuilders = 2;

	static readonly ThreadPriority builderPriority = Environment.GetEnvironmentVariable( "BOREALIS_BUILDER_PRIORITY" ) == "below" ? ThreadPriority.BelowNormal : ThreadPriority.Normal;

	// Under the build queue's lock
	void StartBuilders()
	{
		if ( builders is not null ) return;
		// All but two logical processors, at normal priority: below it, a loading map's threads starve them while draws wait
		// (BOREALIS_BUILDER_PRIORITY=below, to compare)
		builders = new Thread[int.TryParse( Environment.GetEnvironmentVariable( "BOREALIS_BUILDERS" ), out var count ) && count > 0 ? count : Math.Clamp( Environment.ProcessorCount - 2, 2, 12 )];
		for ( int i = 0; i < builders.Length; i++ )
		{
			builders[i] = new Thread( Build ) { IsBackground = true, Priority = builderPriority, Name = $"Borealis.Gpu pipeline builder {i}" };
			builders[i].Start();
		}
	}

	void Build()
	{
		while ( true )
		{
			(PipelineHandle Handle, GraphicsPipelineInfo Info, ulong Layout, ulong Frame, long Requested) job = default;
			(GraphicsPipelineInfo Info, PipelineParts Parts, string Line) prepare = default;
			bool isJob, ahead, guess = false;
			lock ( buildQueue )
			{
				// Parts made ahead never take the last builders: a draw's pipeline asked for meanwhile starts at once
				while ( urgentQueue.Count == 0 && buildQueue.Count == 0 && (prepareQueue.Count == 0 && speculativeQueue.Count == 0 || buildingAhead >= Math.Max( builders.Length - ReservedBuilders, 1 )) && !stopBuilding ) Monitor.Wait( buildQueue );
				if ( stopBuilding ) return;
				var urgent = urgentQueue.TryDequeue( out prepare );
				isJob = !urgent && buildQueue.TryDequeue( out job );
				ahead = !urgent && !isJob;
				if ( ahead && !prepareQueue.TryDequeue( out prepare, out _ ) )
				{
					prepare = speculativeQueue.Dequeue();
					guess = true;
				}

				building++;
				if ( ahead ) buildingAhead++;
			}

			try
			{
				if ( isJob ) MakeDeferred( job.Handle, job.Info, job.Layout, job.Frame, job.Requested );
				else if ( prepare.Parts == PipelinePartManifest.Compute ) MakeComputeAhead( prepare.Info, prepare.Line );
				else PrepareParts( prepare.Info, prepare.Parts, guess );
			}
			catch ( Exception e ) when ( e is GpuException or InvalidOperationException or ArgumentException )
			{
				// A part made ahead that fails is made again, and reported, by the pipeline that needs it (and a manifest's line
				// for it dropped; not when its shader went meanwhile); a pipeline that fails stays unready and its draws skipped
				if ( isJob ) Interlocked.Increment( ref deferredFailed );
				else if ( e is GpuException && prepare.Line is { } line ) partManifest?.Drop( line );
			}
			finally
			{
				lock ( buildQueue )
				{
					building--;
					if ( ahead ) buildingAhead--;
					Monitor.PulseAll( buildQueue );
				}
			}
		}
	}

	void MakeDeferred( PipelineHandle handle, in GraphicsPipelineInfo info, ulong layout, ulong frame, long requested )
	{
		if ( !Pipelines.IsAlive( handle.Index, handle.Generation ) ) return;

		ulong pipeline;
		LinkedParts parts = null;
		var started = System.Diagnostics.Stopwatch.GetTimestamp();
		using ( Zone( ZonePipeline ) )
		{
			if ( UsesLibraries( info ) )
			{
				parts = AcquireParts( info, layout );
				try
				{
					pipeline = Link( parts, layout, optimise: false );
					Interlocked.Increment( ref linksMade );
				}
				catch
				{
					ReleaseParts( parts );
					throw;
				}
			}
			else
			{
				pipeline = MakeGraphics( info, PipelineParts.All, library: false, layout, null, 0, 0 );
			}
		}

		CountPipeline( started );
		SetName( VkObjectType.Pipeline, pipeline, info.Name );

		bool alive;
		lock ( partLock )
		{
			alive = Pipelines.IsAlive( handle.Index, handle.Generation );
			if ( alive )
			{
				ref var record = ref Pipelines.Get( handle.Index, handle.Generation );
				record.Parts = parts;
				record.Pending = false;
				Volatile.Write( ref record.Pipeline, pipeline );
			}
			else
			{
				// Destroyed while it was made: nothing ever bound it
				Vk.vkDestroyPipeline( Vk.Device, pipeline, null );
				if ( parts is not null ) ReleaseParts( parts );
			}
		}

		var took = System.Diagnostics.Stopwatch.GetTimestamp() - requested;
		Interlocked.Add( ref deferredTicks, took );
		Interlocked.Add( ref deferredFrames, (long)(FrameNumber - frame) );
		long max;
		while ( took > (max = Interlocked.Read( ref deferredMaxTicks )) && Interlocked.CompareExchange( ref deferredMaxTicks, took, max ) != max )
		{
		}

		Interlocked.Increment( ref deferredMade );
		if ( alive && parts is not null ) QueueOptimise( handle, parts, layout );
	}

	/// <summary>Stop the builder threads, finishing what they're making; what's still queued isn't made.</summary>
	void StopBuilding()
	{
		Thread[] threads;
		lock ( buildQueue )
		{
			stopBuilding = true;
			threads = builders;
			Monitor.PulseAll( buildQueue );
		}

		foreach ( var thread in threads ?? [] ) thread.Join();
		lock ( buildQueue )
		{
			urgentQueue.Clear();
			buildQueue.Clear();
			prepareQueue.Clear();
			speculativeQueue.Clear();
			building = 0;
			buildingAhead = 0;
			Monitor.PulseAll( buildQueue );
		}
	}
}
