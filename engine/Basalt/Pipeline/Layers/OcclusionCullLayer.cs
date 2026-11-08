using NativeEngine;
using Sandbox.Rendering;

namespace Basalt;

/// <summary>
/// Culls the main view against last frame's depth, then against this frame's prepass.
/// </summary>
internal sealed class OcclusionCullLayer : RenderLayer
{
	const GpuStage Transfer = GpuStage.Transfer;
	const GpuStage Compute = GpuStage.ComputeShader;
	const GpuStage DrawIndirect = GpuStage.DrawIndirect;
	const GpuStage VertexInput = GpuStage.VertexInput;
	const GpuAccess Read = GpuAccess.ShaderRead;
	const GpuAccess Write = GpuAccess.ShaderWrite;
	const GpuAccess Uploaded = GpuAccess.TransferWrite;

	readonly bool early;

	GpuMaterial shader;

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

	public override void BeforeSegments( RenderFrame frame, GpuContext frameContext )
	{
		shader ??= RenderHost.Current.Device.MaterialFromShader( "shaders/occlusion_cull_cs.shader" );
		if ( early ) return;

		var history = frame.System.OcclusionHistory;
		frozen = history.IsFrozen;
		var scratch = frame.System.Transients.Get( OcclusionDepth.QuarterChain( frame.View.Viewport ), "Occlusion Chain" );
		current = history.LateDepth( frame.View.Viewport, new Vector2( frame.DepthMin, frame.DepthMax ), frame.View.WorldToProjection, scratch );
	}

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
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
		OcclusionDebugTarget.Bind( attributes.Engine(), debug, frame.View.WorldToProjection );

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

		rc.BufferBarrier( culler.Args, Compute, Write, DrawIndirect, GpuAccess.IndirectCommandRead );
		rc.BufferBarrier( culler.CulledIds, Compute, Write, VertexInput, GpuAccess.VertexAttributeRead );
		if ( early ) return;

		if ( debug is not null )
		{
			rc.ResolveOcclusionDebug( debug, current.Chain, frozen );
			debug.ReadCounts( rc.NativeContext() );
		}

		rc.BufferBarrier( culler.Stats, Compute, Write, Transfer, GpuAccess.TransferRead );
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

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats ) => frame.System.OcclusionHistory.Keep( frame.View.WorldToProjection, frame.DepthMin, frame.DepthMax );
}
