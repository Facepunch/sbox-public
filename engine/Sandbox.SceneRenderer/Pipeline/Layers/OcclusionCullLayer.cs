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
	Texture chain;

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

		// The shared chain only keeps sample 0 of a multisampled depth, which isn't conservative
		var viewport = frame.View.Viewport;
		chain = SharedPasses.DepthChainTarget( ((int)viewport.Width + 3) / 4, ((int)viewport.Height + 3) / 4 ).DepthTarget;
	}

	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats )
	{
		var culler = frame.System.Occlusion;
		var history = frame.System.OcclusionHistory;
		var viewport = frame.View.Viewport;
		var width = (int)viewport.Width;
		var height = (int)viewport.Height;
		var attributes = rc.Attributes;

		if ( !early )
		{
			var target = frame.Output.Target;
			rc.BuildDepthChain( target.Depth, target.Samples > 1, chain, width, height, DepthResolveMode.ConservativeQuarter );
		}

		// The early cull never reads this, but something has to be bound. A mip 0 texel is 4 pixels wide, so w is 2
		var current = early ? history.Chain : chain;
		attributes.Set( "OcclusionDepthChain", current );
		attributes.Set( "OcclusionViewport", new Vector4( width, height, current.Mips, 2 ) );
		attributes.Set( "OcclusionDepthRange", new Vector2( frame.DepthMin, frame.DepthMax ) );
		attributes.Set( "OcclusionWorldToProjection", frame.View.WorldToProjection );

		// Last frame's chain is full resolution. Without one, everything counts as drawn early
		var previous = history.Valid ? history.Chain : current;
		attributes.Set( "OcclusionPreviousChain", previous );
		attributes.Set( "OcclusionPreviousViewport", history.Valid ? new Vector4( previous.Width, previous.Height, previous.Mips, 0 ) : Vector4.Zero );
		attributes.Set( "OcclusionPreviousDepthRange", history.DepthRange );
		attributes.Set( "OcclusionPreviousWorldToProjection", history.WorldToProjection );

		attributes.Set( "OcclusionInstances", culler.Instances );
		attributes.Set( "OcclusionRuns", culler.Runs );
		attributes.Set( "OcclusionArgs", culler.Args );
		attributes.Set( "OcclusionCulledIds", culler.CulledIds );
		attributes.Set( "OcclusionStats", culler.Stats );
		attributes.Set( "OcclusionInstanceCount", culler.InstanceCount );
		attributes.Set( "OcclusionPhaseStride", culler.PhaseStride );
		attributes.Set( "OcclusionPass", early ? 0 : 1 );

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

		rc.BufferBarrier( culler.Stats, Compute, Write, Transfer, RenderBarrierAccessFlags_t.TransferReadBit );
		culler.ReadStats( rc );
		chain = null;
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
