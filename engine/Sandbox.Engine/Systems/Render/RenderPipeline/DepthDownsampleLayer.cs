using NativeEngine;

namespace Sandbox.Rendering;

internal class DepthDownsampleLayer : ProceduralRenderLayer
{
	RenderViewport Viewport;
	RenderTarget DestDepth;
	bool MSAAInput;

	private static ComputeShader DepthResolveShader = new ComputeShader( "shaders/depthresolve_cs.shader" );

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

		DestDepth = GetTarget( (int)Viewport.Rect.Width, (int)Viewport.Rect.Height );

		view.GetRenderAttributesPtr().SetTextureValue( "DepthChainDownsample", DestDepth.DepthTarget.native, -1 );
		view.GetRenderAttributesPtr().SetTextureValue( "DepthChainDownsamplePrevFrame", DestDepth.DepthTarget.native, -1 );
	}

	/// <summary>
	/// The depth chain's target for a viewport this size: a temporary RG32F texture with every mip, never multisampled.
	/// </summary>
	internal static RenderTarget GetTarget( int width, int height )
	{
		var numMips = (int)Math.Log2( Math.Min( width, height ) ) + 1;
		return RenderTarget.GetTemporary( width, height, ImageFormat.None, ImageFormat.RG3232F, MultisampleAmount.MultisampleNone, numMips );
	}

	internal override void OnRender()
	{
		// Our SourceDepth comes from the scene render target system
		Render( Graphics.Context, null, DestDepth.DepthTarget, MSAAInput, (int)Viewport.Rect.Width, (int)Viewport.Rect.Height );
	}

	/// <summary>
	/// Build the depth chain into <paramref name="destDepth"/>: the depth resolved into its first mip, then min and max
	/// downsampled into the rest. <paramref name="sourceDepth"/> is the depth to read, or null for what the layer binds as
	/// <c>SourceDepth</c>. Also what the managed scene renderer runs, into its own frame.
	/// </summary>
	internal static void Render( IRenderContext context, Texture sourceDepth, Texture destDepth, bool msaaInput, int width, int height )
	{
		var attributes = RenderAttributes.Pool.Get();
		if ( sourceDepth is not null ) attributes.Set( "SourceDepth", sourceDepth );
		attributes.Set( "DestDepth", destDepth );
		attributes.SetCombo( "D_MSAA", msaaInput );
		RenderTools.Compute( context, attributes.Get(), DepthResolveShader.ComputeMaterial.native.GetMode(), width, height, 1 );

		RenderAttributes.Pool.Return( attributes );

		// Downsample using min max
		NativeEngine.CSceneSystem.DownsampleTexture( context, destDepth.native, 5 ); /* DOWNSAMPLE_METHOD_MINMAX ( None of it is enumed properly? ) */
	}
}
