namespace Basalt;

/// <summary>
/// Builds resolved min/max depth mips for <c>Depth::Get</c> using <see cref="SharedPasses.BuildDepthChain"/>, as native's <c>DepthDownsampleLayer</c> does.
/// Runs before <c>AfterDepthPrepass</c>, after a prepass, when a later layer reads it: stages, glass, volumetric fog,
/// contact shadows (<see cref="FrameResources.DepthChain"/>). On the async compute queue where there is one, beside the shadow maps.
/// </summary>
internal sealed class DepthChainLayer : RenderLayer
{
	static readonly StringToken DepthChainName = new( "DepthChainDownsample" );
	static readonly StringToken DepthChainPrevFrameName = new( "DepthChainDownsamplePrevFrame" );

	GpuTexture chain;

	public DepthChainLayer() : base( "Hi-Z Depth Downsample" ) { }

	public override bool IsNeeded( RenderFrame frame ) => frame.System.DepthPrepass;

	public override FrameResources Writes => FrameResources.DepthChain;
	public override FrameResources Reads( RenderFrame frame ) => FrameResources.Depth;
	public override bool SupportsAsyncCompute => true;

	/// <summary>
	/// The prepass's depth made readable by compute, on the graphics queue: an attachment's transition on the compute queue
	/// may need a decompression only graphics can do.
	/// </summary>
	public override void HandOff( RenderFrame frame, GpuContext rc ) => rc.BarrierToComputeRead( frame.Output.Target.Depth );

	/// <summary>
	/// Bind the chain before recording. Previous-frame depth aliases the current chain, matching native.
	/// </summary>
	public override void BeforeSegments( RenderFrame frame, GpuContext frameContext )
	{
		SharedPasses.WarmUp();
		var viewport = frame.View.Viewport;
		var width = (int)viewport.Width;
		var height = (int)viewport.Height;
		chain = frame.System.OcclusionActive ? frame.System.OcclusionHistory.Target( width, height ) : frame.System.Transients.Get( SharedPasses.DepthChainDesc( width, height ), "Depth Chain" );
		frameContext.Attributes.Set( DepthChainName, chain );
		frameContext.Attributes.Set( DepthChainPrevFrameName, chain );
	}

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
	{
		var viewport = frame.View.Viewport;
		var target = frame.Output.Target;
		SharedPasses.BuildDepthChain( rc, target.Depth, chain, target.Samples > 1, (int)viewport.Width, (int)viewport.Height );

		// Readable by the draws and passes after it: a device that tracks layouts by hand binds only what's readable
		if ( !rc.AsyncCompute ) rc.BarrierComputeToPixelShaderRead( chain );
		chain = null;
	}
}
