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

	public void Begin()
	{
		pendingStart = 0;
		InstanceCount = 0;
		runCount = 0;
		argCount = 0;
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

	public void Upload( RenderContext context, int instanceIdCount )
	{
		instanceRing.Upload( context, instances.AsSpan( 0, InstanceCount ) );
		runRing.Upload( context, runs.AsSpan( 0, runCount ) );

		while ( argCount * PhaseCount > args.Length ) Array.Resize( ref args, args.Length * 2 );
		for ( int phase = 1; phase < PhaseCount; phase++ )
			Array.Copy( args, 0, args, phase * argCount, argCount );

		argsRing.Upload( context, args.AsSpan( 0, argCount * PhaseCount ) );

		idCount = instanceIdCount;
		culledRing.Upload( context, ReadOnlySpan<uint>.Empty, idCount * PhaseCount );
		statsRing.Upload( context, [0u, (uint)tested] );
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
