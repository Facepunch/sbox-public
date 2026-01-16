using NativeEngine;

namespace Sandbox.Rendering;

internal class DepthDownsampleLayer : ProceduralRenderLayer
{
	RenderViewport Viewport;
	RenderTarget DestDepth;
	bool MSAAInput;

	private static readonly ComputeShader DepthResolveShader = new ComputeShader( "shaders/depthresolve_cs.shader" );

	public DepthDownsampleLayer()
	{
		Name = "Hi-Z Depth Downsample";
		Flags |= LayerFlags.NeverRemove;
	}

	public void Setup( RenderViewport viewport, SceneViewRenderTargetHandle rtDepth, bool msaaInput, ISceneView view )
	{
		Viewport = viewport;
		MSAAInput = msaaInput;
		RenderTargetAttributes["SourceDepth"] = rtDepth;

		// Create our downsampled RT with mips, always MSAA off
		var numMips = (int)Math.Log2( Math.Min( Viewport.Rect.Width, Viewport.Rect.Height ) ) + 1;
		DestDepth = RenderTarget.GetTemporary( (int)Viewport.Rect.Width, (int)Viewport.Rect.Height, ImageFormat.None, ImageFormat.RG3232F, MultisampleAmount.MultisampleNone, numMips );

		view.GetRenderAttributesPtr().SetTextureValue( "DepthChainDownsample", DestDepth.DepthTarget.native, -1 );
		view.GetRenderAttributesPtr().SetTextureValue( "DepthChainDownsamplePrevFrame", DestDepth.DepthTarget.native, -1 );
	}

	internal override void OnRender()
	{
		// Resolve depth, our SourceDepth comes from the scene render target system
		var attributes = RenderAttributes.Pool.Get();
		attributes.Set( "DestDepth", DestDepth.DepthTarget );
		attributes.SetCombo( "D_MSAA", MSAAInput );
		DepthResolveShader.DispatchWithAttributes( attributes, (int)Viewport.Rect.Width, (int)Viewport.Rect.Height, 1 );

		RenderAttributes.Pool.Return( attributes );

		// Downsample using min max
		NativeEngine.CSceneSystem.DownsampleTexture( Graphics.Context, DestDepth.DepthTarget.native, 5 ); /* DOWNSAMPLE_METHOD_MINMAX ( None of it is enumed properly? ) */
	}
}
