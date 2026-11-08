using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.SceneRenderer.Native;

/// <summary>
/// Native pipeline counterpart of <see cref="OcclusionCullLayer"/>.
/// </summary>
internal sealed class NativeOcclusionCull : IDisposable
{
	// Shared across phases. Grows to what the last view asked for; until then the overflow draws without culling.
	const int InitialCapacity = 1 << 14;

	// Views in a row using under a quarter of the capacity before it shrinks, so a pooled pipeline doesn't keep one heavy
	// frame's or one big camera's buffers for good. Long enough to ride out a camera passing through a quiet stretch
	const int ShrinkAfter = 300;

	static readonly ComputeShader Compute = new( "shaders/occlusion_cull_cs.shader" );

	int capacity = InitialCapacity;
	int requested;
	int underused;
	int underusedPeak;

	GpuBuffer<OcclusionCuller.CullInstance> instances;
	GpuBuffer<OcclusionCuller.CullRun> runs;
	GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments> args;
	GpuBuffer<uint> culledIds;

	// CSceneViewOcclusionCull::Stat_t, read back as RenderPipeline.OcclusionStats
	GpuBuffer<uint> stats;
	GpuBuffer<GpuBuffer.IndirectDispatchArguments> dispatchArgs;

	readonly Pass early;
	readonly Pass late;
	readonly Pass statsRead;

	RenderViewport viewport;
	bool msaaInput;
	Matrix worldToProjection;
	OcclusionHistory history;
	OcclusionDebugTarget debug;
	bool rejectEarly;

	// What the late and early culls test against. Frozen, the late cull tests against the frozen depth rather than building
	// its own
	OcclusionDepth current;
	OcclusionDepth previous;
	bool frozen;

	public NativeOcclusionCull()
	{
		early = new Pass( "Occlusion Cull (Early)", () => Cull( earlyPass: true ) );
		late = new Pass( "Occlusion Cull (Late)", () => Cull( earlyPass: false ) );
		statsRead = new Pass( "Occlusion Stats", ReadStats );
	}

	public bool CullsEarly => previous.Chain is not null;

	public Matrix WorldToProjection => worldToProjection;

	/// <summary>
	/// The camera's history, or null if there's no camera or another view already claimed it this frame.
	/// </summary>
	public static OcclusionHistory HistoryFor( SceneCamera camera )
	{
		if ( camera is null ) return null;

		var history = (OcclusionHistory)(camera.OcclusionHistory ??= new OcclusionHistory());
		return history.Claim( Application.FrameCount ) ? history : null;
	}

	public void Setup( ISceneView view, RenderViewport viewport, SceneViewRenderTargetHandle sourceDepth, bool msaaInput, OcclusionHistory history )
	{
		this.viewport = viewport;
		this.msaaInput = msaaInput;
		this.history = history;
		late.Read( "SourceDepth", sourceDepth );

		Resize();

		instances ??= new( capacity, GpuBuffer.UsageFlags.Structured, "NativeOcclusionInstances" );
		runs ??= new( capacity, GpuBuffer.UsageFlags.Structured, "NativeOcclusionRuns" );
		args ??= new( capacity, GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments, "NativeOcclusionArgs" );
		culledIds ??= new( capacity, GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.Vertex, "NativeOcclusionCulledIds" );
		stats ??= new( RenderPipeline.OcclusionStatCount, GpuBuffer.UsageFlags.Structured, "NativeOcclusionStats" );
		dispatchArgs ??= new( 1, GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments, "NativeOcclusionDispatchArgs" );

		// Transposed, since the shader does mul( M, pos )
		worldToProjection = view.GetFrustum().GetReverseZViewProjTranspose();

		history?.BeginFrame( true, viewport.Rect );
		previous = history?.EarlyDepth ?? default;
		rejectEarly = history is { RejectsEarly: true };
		debug = history?.Debug;
		frozen = history is { IsFrozen: true };

		var depthRange = new Vector2( viewport.MinZ, viewport.MaxZ );
		current = history?.LateDepth( viewport.Rect, depthRange, worldToProjection ) ?? OcclusionDepth.Quarter( null, viewport.Rect, depthRange, worldToProjection );

		view.SetOcclusionCull( instances.native, runs.native, args.native, culledIds.native, stats.native, dispatchArgs.native, capacity, RenderPipeline.AggregateMode );
	}

	/// <summary>
	/// The view's layers have recorded: note how much they asked for, so the next view this pipeline sets up can fit it.
	/// </summary>
	public void OnSubmitted( ISceneView view ) => requested = view.GetOcclusionCullRequested();

	// Grows straight to what the last view asked for, and shrinks to fit the most any view asked for while it was underused
	void Resize()
	{
		if ( requested > capacity )
		{
			SetCapacity( requested );
			return;
		}

		if ( capacity == InitialCapacity || requested * 4 > capacity )
		{
			underused = 0;
			underusedPeak = 0;
			return;
		}

		underusedPeak = Math.Max( underusedPeak, requested );
		if ( ++underused >= ShrinkAfter ) SetCapacity( underusedPeak );
	}

	void SetCapacity( int entries )
	{
		capacity = Math.Max( InitialCapacity, (int)System.Numerics.BitOperations.RoundUpToPowerOf2( (uint)Math.Max( entries, 1 ) ) );
		underused = 0;
		underusedPeak = 0;
		DisposeCullBuffers();
	}

	public ISceneLayer AddEarlyToView( ISceneView view, RenderViewport viewport ) => early.AddToView( view, viewport );
	public ISceneLayer AddLateToView( ISceneView view, RenderViewport viewport ) => late.AddToView( view, viewport );

	/// <summary>
	/// Reads back the cull stats. Reading a buffer submits everything recorded before it, so this goes last in the view:
	/// mid-frame, the GPU would sit idle until the render thread submitted the rest.
	/// </summary>
	public ISceneLayer AddStatsToView( ISceneView view, RenderViewport viewport ) => statsRead.AddToView( view, viewport );

	void Cull( bool earlyPass )
	{
		// The first cull in the frame clears the debug grid
		if ( debug is not null && (earlyPass || !CullsEarly) ) debug.Clear( Graphics.Context );

		if ( !earlyPass && !frozen )
		{
			// Our SourceDepth comes from the scene render target system
			SharedPasses.BuildDepthChain( Graphics.Context, null, current.Chain, msaaInput, (int)viewport.Rect.Width, (int)viewport.Rect.Height, DepthResolveMode.ConservativeQuarter );
			history?.KeepFrozen( current );
		}

		var attributes = RenderAttributes.Pool.Get();
		attributes.Set( "OcclusionInstances", instances );
		attributes.Set( "OcclusionRuns", runs );
		attributes.Set( "OcclusionArgs", args );
		attributes.Set( "OcclusionCulledIds", culledIds );
		attributes.Set( "OcclusionStats", stats );

		// The early pass never reads the current depth, but something has to be bound
		OcclusionDepth.Bind( attributes, earlyPass ? previous : current, previous );
		OcclusionDebugTarget.Bind( attributes, debug, worldToProjection );

		// Native runs each have a single phase and their own arguments, so there's no stride
		attributes.Set( "OcclusionPass", earlyPass ? 0 : 1 );
		attributes.Set( "OcclusionPhaseStride", Vector2Int.Zero );
		attributes.Set( "OcclusionRejectEarly", rejectEarly ? 1 : 0 );
		attributes.SetCombo( "D_GPU_COUNT", true );

		// Real access masks: the single argument ResourceBarrierTransition has none, so the indirect draws read stale args on AMD
		const RenderBarrierPipelineStageFlags_t Transfer = RenderBarrierPipelineStageFlags_t.TransferBit;
		const RenderBarrierPipelineStageFlags_t ComputeStage = RenderBarrierPipelineStageFlags_t.ComputeShaderBit;
		const RenderBarrierPipelineStageFlags_t DrawIndirect = RenderBarrierPipelineStageFlags_t.DrawIndirectBit;
		const RenderBarrierPipelineStageFlags_t VertexInput = RenderBarrierPipelineStageFlags_t.VertexInputBit;
		const RenderBarrierAccessFlags_t Read = RenderBarrierAccessFlags_t.ShaderReadBit;
		const RenderBarrierAccessFlags_t Write = RenderBarrierAccessFlags_t.ShaderWriteBit;
		const RenderBarrierAccessFlags_t Uploaded = RenderBarrierAccessFlags_t.TransferWriteBit;

		var context = Graphics.Context;
		context.BufferBarrierTransition( instances.native, Transfer, ComputeStage, Uploaded, Read );
		context.BufferBarrierTransition( runs.native, Transfer, ComputeStage, Uploaded, Read );
		context.BufferBarrierTransition( args.native, Transfer | ComputeStage | DrawIndirect, ComputeStage, Uploaded | Write, Read | Write );
		context.BufferBarrierTransition( culledIds.native, VertexInput | ComputeStage, ComputeStage, Write, Write );
		context.BufferBarrierTransition( stats.native, Transfer | ComputeStage, ComputeStage, Uploaded | Write, Read | Write );

		// The GPU writes the instance count in dispatchArgs, and the dispatch reads it
		context.BufferBarrierTransition( dispatchArgs.native, Transfer | ComputeStage, DrawIndirect, Uploaded | Write, RenderBarrierAccessFlags_t.IndirectCommandReadBit );

		Compute.DispatchIndirectWithAttributes( attributes, dispatchArgs );

		RenderAttributes.Pool.Return( attributes );

		context.BufferBarrierTransition( args.native, ComputeStage, DrawIndirect, Write, RenderBarrierAccessFlags_t.IndirectCommandReadBit );
		context.BufferBarrierTransition( culledIds.native, ComputeStage, VertexInput, Write, RenderBarrierAccessFlags_t.VertexAttributeReadBit );

		if ( !earlyPass ) debug?.Resolve( Graphics.Context, current.Chain, native: true, frozen );
	}

	void ReadStats()
	{
		Graphics.Context.BufferBarrierTransition( stats.native, RenderBarrierPipelineStageFlags_t.ComputeShaderBit, RenderBarrierPipelineStageFlags_t.TransferBit,
			RenderBarrierAccessFlags_t.ShaderWriteBit, RenderBarrierAccessFlags_t.TransferReadBit );
		RenderPipeline.ReadOcclusionStats( Graphics.Context, stats );
		debug?.ReadCounts( Graphics.Context );
	}

	public void Dispose()
	{
		DisposeCullBuffers();
		dispatchArgs?.Dispose();
		stats?.Dispose();
		stats = null;
		dispatchArgs = null;
	}

	// The capacity-sized buffers. The render device frees them once the GPU is done with them
	void DisposeCullBuffers()
	{
		instances?.Dispose();
		runs?.Dispose();
		args?.Dispose();
		culledIds?.Dispose();
		instances = null;
		runs = null;
		args = null;
		culledIds = null;
	}

	sealed class Pass( string name, Action render ) : NativeProceduralLayer( name )
	{
		public void Read( StringToken input, SceneViewRenderTargetHandle target ) => SetInput( input, target );

		protected override void OnRender() => render();
	}
}
