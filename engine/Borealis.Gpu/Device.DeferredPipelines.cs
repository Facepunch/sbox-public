using Borealis.Gpu.Vulkan;
using System.Threading;

namespace Borealis.Gpu;

/// <summary>Pipelines made on workers while their draws were skipped (<see cref="Device.DeferredPipelines"/>).</summary>
public readonly record struct DeferredPipelineStats( int Made, int Pending, int Failed, double AverageMs, double MaxMs, double AverageFrames, long SkippedDraws, long FramesWithSkips );

/// <summary>
/// Pipelines made on the builder threads while the draws that use them are skipped
/// (<see cref="GraphicsPipelineInfo.SkipUntilReady"/>), and parts made ahead (<see cref="Device.QueuePipelineParts"/>).
/// </summary>
public sealed unsafe partial class Device
{
	// What a draw is waiting for goes before parts made ahead; both are made by the builder threads
	readonly Queue<(PipelineHandle Handle, GraphicsPipelineInfo Info, ulong Layout, ulong Frame, long Requested)> buildQueue = new();
	readonly Queue<(GraphicsPipelineInfo Info, PipelineParts Parts)> prepareQueue = new();
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

	/// <summary>Whether the four parts a pipeline links are all made, so linking it is quick.</summary>
	bool PartsReady( in GraphicsPipelineInfo info, ulong layout )
	{
		var preRasterization = PreRasterizationKey( info, layout );
		var fragment = FragmentKey( info, layout );
		var vertexInput = VertexInputKey( info );
		var output = OutputKey( info );
		lock ( partLock )
		{
			return Made( preRasterizationParts, preRasterization ) && Made( fragmentParts, fragment ) && Made( vertexInputParts, vertexInput ) && Made( outputParts, output );
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
			buildQueue.Enqueue( (handle, info, layout, FrameNumber, System.Diagnostics.Stopwatch.GetTimestamp()) );
			StartBuilders();
			Monitor.Pulse( buildQueue );
		}

		return handle;
	}

	/// <summary>
	/// Make a pipeline's parts on the builder threads, after any pipeline a draw is waiting for
	/// (<see cref="PreparePipelineParts"/> makes them on the calling thread). Does nothing without pipeline libraries.
	/// </summary>
	public void QueuePipelineParts( in GraphicsPipelineInfo info, PipelineParts parts = PipelineParts.All )
	{
		if ( !UsesLibraries( info ) ) return;
		lock ( buildQueue )
		{
			if ( stopBuilding ) return;
			prepareQueue.Enqueue( (info, parts) );
			StartBuilders();
			Monitor.Pulse( buildQueue );
		}
	}

	/// <summary>Wait until nothing is queued or being made on the builder threads, or the timeout passes. For tests and measuring.</summary>
	public bool WaitForPendingPipelines( TimeSpan timeout )
	{
		var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
		lock ( buildQueue )
		{
			while ( buildQueue.Count + prepareQueue.Count + building > 0 )
			{
				var left = deadline - Environment.TickCount64;
				if ( left <= 0 ) return false;
				Monitor.Wait( buildQueue, (int)Math.Min( left, int.MaxValue ) );
			}
		}

		return true;
	}

	// Under the build queue's lock
	void StartBuilders()
	{
		if ( builders is not null ) return;
		builders = new Thread[Math.Clamp( Environment.ProcessorCount / 2, 2, 6 )];
		for ( int i = 0; i < builders.Length; i++ )
		{
			builders[i] = new Thread( Build ) { IsBackground = true, Name = $"Borealis.Gpu pipeline builder {i}" };
			builders[i].Start();
		}
	}

	void Build()
	{
		while ( true )
		{
			(PipelineHandle Handle, GraphicsPipelineInfo Info, ulong Layout, ulong Frame, long Requested) job = default;
			(GraphicsPipelineInfo Info, PipelineParts Parts) prepare = default;
			bool isJob;
			lock ( buildQueue )
			{
				while ( buildQueue.Count == 0 && prepareQueue.Count == 0 && !stopBuilding ) Monitor.Wait( buildQueue );
				if ( stopBuilding ) return;
				isJob = buildQueue.TryDequeue( out job );
				if ( !isJob ) prepare = prepareQueue.Dequeue();
				building++;
			}

			try
			{
				if ( isJob ) MakeDeferred( job.Handle, job.Info, job.Layout, job.Frame, job.Requested );
				else PreparePipelineParts( prepare.Info, prepare.Parts );
			}
			catch ( Exception e ) when ( e is GpuException or InvalidOperationException or ArgumentException )
			{
				// A part made ahead that fails is made again, and reported, by the pipeline that needs it; a pipeline that fails
				// stays unready and its draws skipped
				if ( isJob ) Interlocked.Increment( ref deferredFailed );
			}
			finally
			{
				lock ( buildQueue )
				{
					building--;
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
			buildQueue.Clear();
			prepareQueue.Clear();
			building = 0;
			Monitor.PulseAll( buildQueue );
		}
	}
}
