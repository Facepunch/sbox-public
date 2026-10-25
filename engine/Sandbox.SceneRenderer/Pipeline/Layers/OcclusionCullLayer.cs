using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Culls the main view against last frame's depth, then against this frame's prepass.
/// </summary>
internal sealed class OcclusionCullLayer : RenderLayer
{
	const RenderBarrierPipelineStageFlags_t Transfer = RenderBarrierPipelineStageFlags_t.TransferBit;
	const RenderBarrierPipelineStageFlags_t Compute = RenderBarrierPipelineStageFlags_t.ComputeShaderBit;
	const RenderBarrierPipelineStageFlags_t DrawIndirect = RenderBarrierPipelineStageFlags_t.DrawIndirectBit;
	const RenderBarrierPipelineStageFlags_t VertexInput = RenderBarrierPipelineStageFlags_t.VertexInputBit;
	const RenderBarrierAccessFlags_t Read = RenderBarrierAccessFlags_t.ShaderReadBit;
	const RenderBarrierAccessFlags_t Write = RenderBarrierAccessFlags_t.ShaderWriteBit;
	const RenderBarrierAccessFlags_t Uploaded = RenderBarrierAccessFlags_t.TransferWriteBit;

	readonly bool early;

	Material shader;

	// What the late cull tests against. Frozen, that's the frozen depth rather than a chain it builds
	OcclusionDepth current;
	bool frozen;

	public OcclusionCullLayer( bool early ) : base( early ? "Occlusion Cull (Early)" : "Occlusion Cull (Late)" )
	{
		this.early = early;
	}

	public override bool IsNeeded( RenderFrame frame ) => early ? frame.System.Occlusion.CullsEarly : frame.System.OcclusionActive;

	public override FrameResources Reads( RenderFrame frame ) => early ? FrameResources.None : FrameResources.Depth;
	public override FrameResources Writes => FrameResources.CulledInstances;

	// The prepasses and OpaqueLayer read our output through indirect draws, which the planner can't see
	public override bool HasSideEffects => true;

	public override void BeforeSegments( RenderFrame frame, RenderContext frameContext )
	{
		shader ??= Material.FromShader( "shaders/occlusion_cull_cs.shader" );
		if ( early ) return;

		var history = frame.System.OcclusionHistory;
		frozen = history.IsFrozen;
		current = history.LateDepth( frame.View.Viewport, new Vector2( frame.DepthMin, frame.DepthMax ), frame.View.WorldToProjection );
	}

	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats )
	{
		var culler = frame.System.Occlusion;
		var history = frame.System.OcclusionHistory;
		var debug = history.Debug;
		var attributes = rc.Attributes;

		// The first cull in the frame clears the debug grid
		if ( debug is not null && (early || !culler.CullsEarly) ) rc.ClearOcclusionDebug( debug );

		if ( !early && !frozen )
		{
			var viewport = frame.View.Viewport;
			var target = frame.Output.Target;
			rc.BuildDepthChain( target.Depth, target.Samples > 1, current.Chain, (int)viewport.Width, (int)viewport.Height, DepthResolveMode.ConservativeQuarter );
			history.KeepFrozen( current );
		}

		// The early cull never reads the current depth, but something has to be bound
		OcclusionDepth.Bind( attributes, early ? culler.Previous : current, culler.Previous );
		OcclusionDebugTarget.Bind( attributes, debug, frame.View.WorldToProjection );

		attributes.Set( "OcclusionInstances", culler.Instances );
		attributes.Set( "OcclusionRuns", culler.Runs );
		attributes.Set( "OcclusionArgs", culler.Args );
		attributes.Set( "OcclusionCulledIds", culler.CulledIds );
		attributes.Set( "OcclusionStats", culler.Stats );
		attributes.Set( "OcclusionInstanceCount", culler.InstanceCount );
		attributes.Set( "OcclusionPhaseStride", culler.PhaseStride );
		attributes.Set( "OcclusionPass", early ? 0 : 1 );
		attributes.Set( "OcclusionRejectEarly", culler.RejectEarly ? 1 : 0 );

		// Wait for the uploads, and in the late cull for the early cull and the prepass drawing from it
		rc.BufferBarrier( culler.Instances, Transfer, Uploaded, Compute, Read );
		rc.BufferBarrier( culler.Runs, Transfer, Uploaded, Compute, Read );
		rc.BufferBarrier( culler.Args, Transfer | Compute | DrawIndirect, Uploaded | Write, Compute, Read | Write );
		rc.BufferBarrier( culler.Stats, Transfer | Compute, Uploaded | Write, Compute, Read | Write );
		rc.BufferBarrier( culler.CulledIds, VertexInput | Compute, Write, Compute, Write );

		rc.Dispatch( shader, culler.InstanceCount, 1, 1 );

		rc.BufferBarrier( culler.Args, Compute, Write, DrawIndirect, RenderBarrierAccessFlags_t.IndirectCommandReadBit );
		rc.BufferBarrier( culler.CulledIds, Compute, Write, VertexInput, RenderBarrierAccessFlags_t.VertexAttributeReadBit );
		if ( early ) return;

		if ( debug is not null )
		{
			rc.ResolveOcclusionDebug( debug, current.Chain, frozen );
			debug.ReadCounts( rc.Native );
		}

		rc.BufferBarrier( culler.Stats, Compute, Write, Transfer, RenderBarrierAccessFlags_t.TransferReadBit );
		culler.ReadStats( rc );
		current = default;
	}

	public override void Dispose()
	{
		// Shared with other renderers, so not ours to destroy
		shader = null;
	}
}

/// <summary>
/// Keeps this frame's depth chain for the next frame's early cull. Reading it makes sure <see cref="DepthChainLayer"/>
/// builds it.
/// </summary>
internal sealed class OcclusionHistoryLayer : RenderLayer
{
	public OcclusionHistoryLayer() : base( "Occlusion History" ) { }

	public override bool IsNeeded( RenderFrame frame ) => frame.System.OcclusionActive;

	public override FrameResources Reads( RenderFrame frame ) => FrameResources.DepthChain;

	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats ) => frame.System.OcclusionHistory.Keep( frame.View.WorldToProjection, frame.DepthMin, frame.DepthMax );
}
