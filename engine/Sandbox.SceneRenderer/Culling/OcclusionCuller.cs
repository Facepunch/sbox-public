using Sandbox.Rendering;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sandbox.SceneRenderer.Culling;

internal enum OcclusionPhase
{
	None,

	/// <summary>Depth prepass, culled against last frame's depth.</summary>
	Early,

	/// <summary>Late depth prepass, for what the early cull rejected but is visible now.</summary>
	Late,

	/// <summary>Forward pass, culled against this frame's depth.</summary>
	Final,
}

/// <summary>
/// GPU cull buffers, with separate draw arguments and instance ids for each phase.
/// </summary>
internal sealed class OcclusionCuller : IDisposable
{
	// Must match CullInstance in occlusion_cull_cs.shader. Negative Extents.x disables culling.
	[StructLayout( LayoutKind.Sequential )]
	internal struct CullInstance
	{
		public Vector3 Center;
		public uint Run;
		public Vector3 Extents;
		public uint Id;
	}

	// Must match CullRun in occlusion_cull_cs.shader; Phases contains the shader's PHASE_ bits.
	[StructLayout( LayoutKind.Sequential )]
	internal struct CullRun
	{
		public uint FirstInstance;
		public uint FirstArg;
		public uint DrawCount;
		public uint Phases;
	}

	const int PhaseCount = 3;
	const uint AllPhases = (1 << PhaseCount) - 1;

	// The shader's RUN_FRAGMENTS and RUN_COMPACT: a run of aggregate fragments, each an instance with a draw of its own
	const uint RunFragments = 8;
	const uint RunCompact = 16;

	public static readonly int ArgsStride = Marshal.SizeOf<GpuBuffer.IndirectDrawIndexedArguments>();

	CullInstance[] instances = new CullInstance[256];
	CullRun[] runs = new CullRun[64];
	GpuBuffer.IndirectDrawIndexedArguments[] args = new GpuBuffer.IndirectDrawIndexedArguments[64];
	int pendingStart;
	int runCount;
	int argCount;
	int idCount;
	int tested;
	int culled;

	readonly UploadRing<CullInstance> instanceRing = new( "SceneRenderer OcclusionInstances" );
	readonly UploadRing<CullRun> runRing = new( "SceneRenderer OcclusionRuns" );
	readonly UploadRing<GpuBuffer.IndirectDrawIndexedArguments> argsRing = new( "SceneRenderer OcclusionArgs", GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments );
	readonly UploadRing<uint> culledRing = new( "SceneRenderer OcclusionCulledIds", GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.Vertex );
	readonly UploadRing<uint> statsRing = new( "SceneRenderer OcclusionStats" );
	readonly AsyncGPUReadback.BufferReadDelegate onStatsRead;

	public OcclusionCuller()
	{
		onStatsRead = bytes =>
		{
			var counts = MemoryMarshal.Cast<byte, uint>( bytes );
			Volatile.Write( ref culled, (int)(counts[1] - counts[0]) );
		};
	}

	public int InstanceCount { get; private set; }

	/// <summary>Instances skipped by the forward pass, read back a few frames later.</summary>
	public int Culled => Volatile.Read( ref culled );

	/// <summary>
	/// What the early cull tests against this frame. Without a chain, the prepass draws everything.
	/// </summary>
	public OcclusionDepth Previous { get; set; }

	public bool CullsEarly => Previous.Chain is not null;

	/// <summary>
	/// <c>r_occlusion_early 2</c>, held for the frame so both culls agree.
	/// </summary>
	public bool RejectEarly { get; set; }

	public GpuBuffer<uint> CulledIds => culledRing.Current;
	public GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments> Args => argsRing.Current;

	public GpuBuffer<CullInstance> Instances => instanceRing.Current;
	public GpuBuffer<CullRun> Runs => runRing.Current;

	public GpuBuffer<uint> Stats => statsRing.Current;

	public Vector2Int PhaseStride => new( argCount, idCount );

	public int ArgOffset( OcclusionPhase phase ) => ((int)phase - 1) * argCount;
	public int IdOffset( OcclusionPhase phase ) => ((int)phase - 1) * idCount;

	/// <summary>
	/// Whether aggregate fragments are culled at all (<c>r_occlusion_aggregates</c> not 0). Held for the frame.
	/// </summary>
	public bool CullsFragments { get; private set; }

	/// <summary>
	/// Whether fragment runs pack their survivors to the front and draw them with a count from the GPU
	/// (<c>r_occlusion_aggregates 2</c>), rather than drawing the hidden ones with no instances. Decided at
	/// <see cref="Upload"/>, where the device can be asked.
	/// </summary>
	public bool CompactFragments { get; private set; }

	bool wantsCompactFragments;

	// Fragment runs, by index, to lay out for compacting or not once that's known
	int[] fragmentRuns = new int[16];
	int fragmentRunCount;

	public void Begin()
	{
		CullsFragments = RenderPipeline.OcclusionAggregates != RenderPipeline.AggregateOcclusion.Off;
		wantsCompactFragments = RenderPipeline.OcclusionAggregates == RenderPipeline.AggregateOcclusion.Compact;
		pendingStart = 0;
		InstanceCount = 0;
		runCount = 0;
		argCount = 0;
		fragmentRunCount = 0;
		tested = 0;
	}

	public void AddInstance( int id, Vector3 center, Vector3 extents )
	{
		if ( InstanceCount == instances.Length ) Array.Resize( ref instances, instances.Length * 2 );
		instances[InstanceCount++] = new CullInstance { Center = center, Extents = extents, Id = (uint)id };
	}

	/// <summary>Commits the pending instances and returns their first indirect-argument index.</summary>
	public int CommitRun( int firstInstance, int count, ReadOnlySpan<RenderMesh.Draw> lodDraws )
	{
		var firstArg = argCount;
		if ( runCount == runs.Length ) Array.Resize( ref runs, runs.Length * 2 );
		while ( argCount + lodDraws.Length > args.Length ) Array.Resize( ref args, args.Length * 2 );

		for ( int i = pendingStart; i < InstanceCount; i++ )
			instances[i].Run = (uint)runCount;

		runs[runCount++] = new CullRun { FirstInstance = (uint)firstInstance, FirstArg = (uint)firstArg, DrawCount = (uint)lodDraws.Length, Phases = AllPhases };

		foreach ( var draw in lodDraws )
			args[argCount++] = new GpuBuffer.IndirectDrawIndexedArguments { IndexCount = (uint)draw.IndexCount, FirstIndex = (uint)draw.StartIndex };

		pendingStart = InstanceCount;
		tested += count;
		return firstArg;
	}

	/// <summary>
	/// Add a run of aggregate fragments, each an instance culled on its own with a draw of its own, and return where its
	/// args start. <paramref name="draws"/>' base instances index <paramref name="centers"/> and <paramref name="extents"/>.
	/// The args are laid out for compacting, as <c>DrawOcclusionCulled</c> in aggregatedesc.cpp does: [ survivors ][ fragments ]
	/// [ survivor count ]. Without it, the run draws its fragments' own args, the hidden ones with no instances.
	/// </summary>
	public int CommitFragments( ReadOnlySpan<GpuBuffer.IndirectDrawIndexedArguments> draws, ReadOnlySpan<Vector3> centers, ReadOnlySpan<Vector3> extents )
	{
		var count = draws.Length;
		var firstArg = argCount;
		if ( runCount == runs.Length ) Array.Resize( ref runs, runs.Length * 2 );
		if ( fragmentRunCount == fragmentRuns.Length ) Array.Resize( ref fragmentRuns, fragmentRuns.Length * 2 );
		while ( argCount + count * 2 + 1 > args.Length ) Array.Resize( ref args, args.Length * 2 );
		while ( InstanceCount + count > instances.Length ) Array.Resize( ref instances, instances.Length * 2 );

		Array.Clear( args, firstArg, count );
		for ( int i = 0; i < count; i++ )
		{
			var fragment = (int)draws[i].FirstInstance;
			instances[InstanceCount++] = new CullInstance { Center = centers[fragment], Run = (uint)runCount, Extents = extents[fragment], Id = (uint)i };
			args[firstArg + count + i] = draws[i] with { InstanceCount = 0 };
		}

		args[firstArg + count * 2] = default;

		fragmentRuns[fragmentRunCount++] = runCount;
		runs[runCount++] = new CullRun { FirstArg = (uint)firstArg, DrawCount = (uint)count, Phases = AllPhases | RunFragments };
		argCount += count * 2 + 1;
		pendingStart = InstanceCount;
		return firstArg;
	}

	/// <summary>
	/// Where a fragment run's args for <paramref name="phase"/> start: its survivors when compacting, else its fragments' own.
	/// </summary>
	public int FragmentArgs( OcclusionPhase phase, int firstArg, int count ) => ArgOffset( phase ) + firstArg + (CompactFragments ? 0 : count);

	/// <summary>
	/// Where a compacted fragment run's survivor count is for <paramref name="phase"/>.
	/// </summary>
	public int FragmentCountArg( OcclusionPhase phase, int firstArg, int count ) => ArgOffset( phase ) + firstArg + count * 2;

	public void Upload( RenderContext context, int instanceIdCount )
	{
		// Compacting needs drawIndirectCount. Without it the fragment runs cull in their own args, after the survivors'
		CompactFragments = wantsCompactFragments && RenderContext.SupportsDrawIndirectCount;
		foreach ( var run in fragmentRuns.AsSpan( 0, fragmentRunCount ) )
		{
			if ( CompactFragments ) runs[run].Phases |= RunCompact;
			else runs[run].FirstArg += runs[run].DrawCount;
		}

		instanceRing.Upload( context, instances.AsSpan( 0, InstanceCount ) );
		runRing.Upload( context, runs.AsSpan( 0, runCount ) );

		while ( argCount * PhaseCount > args.Length ) Array.Resize( ref args, args.Length * 2 );
		for ( int phase = 1; phase < PhaseCount; phase++ )
			Array.Copy( args, 0, args, phase * argCount, argCount );

		argsRing.Upload( context, args.AsSpan( 0, argCount * PhaseCount ) );

		idCount = instanceIdCount;
		culledRing.Upload( context, ReadOnlySpan<uint>.Empty, idCount * PhaseCount );
		// The shader counts fragments after the instance counts (RenderPipeline.OcclusionStats)
		Span<uint> stats = stackalloc uint[RenderPipeline.OcclusionStatCount];
		stats.Clear();
		stats[1] = (uint)tested;
		statsRing.Upload( context, stats );
	}

	public void ReadStats( RenderContext context ) => context.Native.ReadBufferAsync( Stats, onStatsRead, 0, 2 * sizeof( uint ) );

	public void Dispose()
	{
		instanceRing.Dispose();
		runRing.Dispose();
		argsRing.Dispose();
		culledRing.Dispose();
		statsRing.Dispose();
	}
}
