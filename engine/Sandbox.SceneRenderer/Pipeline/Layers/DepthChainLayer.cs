using Sandbox.Rendering;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Builds resolved min/max depth mips for <c>Depth::Get</c> using <c>DepthDownsampleLayer.Render</c>.
/// Runs before <c>AfterDepthPrepass</c>, after a prepass, when a later layer reads it: stages, glass, volumetric fog,
/// contact shadows (<see cref="FrameResources.DepthChain"/>). On the async compute queue where there is one, beside the shadow maps.
/// </summary>
internal sealed class DepthChainLayer : RenderLayer
{
	static readonly StringToken DepthChainName = new( "DepthChainDownsample" );
	static readonly StringToken DepthChainPrevFrameName = new( "DepthChainDownsamplePrevFrame" );

	// Pooled depth chain for this frame.
	RenderTarget chain;

	public DepthChainLayer() : base( "Hi-Z Depth Downsample" ) { }

	public override bool IsNeeded( RenderFrame frame ) => frame.System.DepthPrepass;

	public override FrameResources Writes => FrameResources.DepthChain;

	// The resolve and min/max downsample are compute dispatches
	public override bool AsyncCompute => true;

	/// <summary>
	/// The prepass's depth made readable by compute, on the graphics queue: an attachment's transition on the compute queue
	/// may need a decompression only graphics can do.
	/// </summary>
	public override void HandOff( RenderFrame frame, RenderContext rc ) => rc.BarrierToComputeRead( frame.Output.Target.Depth );

	/// <summary>
	/// Bind the pooled chain before recording (<c>DepthDownsampleLayer.Setup</c>).
	/// Previous-frame depth aliases the current chain, matching native.
	/// </summary>
	public override void BeforeSegments( RenderFrame frame, RenderContext frameContext )
	{
		var viewport = frame.View.Viewport;
		chain = DepthDownsampleLayer.GetTarget( (int)viewport.Width, (int)viewport.Height );
		frameContext.Attributes.Set( DepthChainName, chain.DepthTarget );
		frameContext.Attributes.Set( DepthChainPrevFrameName, chain.DepthTarget );
	}

	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats )
	{
		var viewport = frame.View.Viewport;
		var target = frame.Output.Target;
		rc.BuildDepthChain( target.Depth, target.Samples > 1, chain.DepthTarget, (int)viewport.Width, (int)viewport.Height );
		chain = null;
	}
}
