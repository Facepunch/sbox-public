using NativeEngine;

namespace Sandbox.Rendering;

internal class BloomLayer : RenderLayer
{
	public BloomLayer()
	{
		Name = $"Bloom Layer";
		LayerType = SceneLayerType.Opaque;
		Flags |= LayerFlags.NeverRemove;
		ShaderMode = "Forward";

		ClearFlags = ClearFlags.Color;
		Flags |= LayerFlags.NeedsPerViewLightingConstants;

		ObjectFlagsRequired = SceneObjectFlags.EffectsBloomLayer;
		ObjectFlagsExcluded = SceneObjectFlags.IsLight;
	}

	public void Setup( ISceneView view, RenderTarget renderTarget )
	{
		ColorAttachment = renderTarget.ToColorHandle( view );
		DepthAttachment = renderTarget.ToDepthHandle( view );
	}

	/// <summary>
	/// The bloom layer's target for a quarter viewport this size: a temporary RGBA1010102 colour with a mip chain for the blur,
	/// and D32 depth. Also what the managed scene renderer draws its bloom objects into.
	/// </summary>
	internal static RenderTarget GetTarget( int width, int height )
	{
		return RenderTarget.GetTemporary(
			width,
			height,
			colorFormat: ImageFormat.RGBA1010102,
			depthFormat: ImageFormat.D32,
			numMips: (int)Math.Log2( Math.Max( width, height ) ) );
	}
}

internal class BloomDownsampleLayer : ProceduralRenderLayer
{
	public RenderTarget RT { get; set; }
	public BloomDownsampleLayer()
	{
		Name = "Bloom Layer Gaussian Blur";
		Flags |= LayerFlags.NeverRemove;
	}

	// Bit wasteful if we are not rendering anything before, in the future these two layers would only be called if we are rendering something
	internal override void OnRender()
	{
		// Fucked?
		// Graphics.GenerateMipMaps( rt.ColorTarget, Graphics.DownsampleMethod.GaussianBlur );
		Render( Graphics.Context, RT.ColorTarget );
	}

	/// <summary>
	/// Blur <paramref name="color"/> down its mips. Also what the managed scene renderer runs, into its own frame.
	/// </summary>
	internal static void Render( IRenderContext context, Texture color )
	{
		NativeEngine.CSceneSystem.DownsampleTexture( context, color.native, (int)Graphics.DownsampleMethod.GaussianBlur );
	}
}
internal class QuarterDepthDownsampleLayer : ProceduralRenderLayer
{
	private static Material DepthResolve = Material.FromShader( "shaders/depthresolve.shader" );

	private bool MSAAInput;

	public QuarterDepthDownsampleLayer()
	{
		Name = "Quarter Depth Downsample";
		Flags |= LayerFlags.NeverRemove | LayerFlags.DoesntModifyColorBuffers;
		ClearFlags = ClearFlags.Depth | ClearFlags.Stencil;
		LayerType = SceneLayerType.Opaque;
	}

	public void Setup( ISceneView view, RenderViewport viewport, SceneViewRenderTargetHandle rtDepth, bool msaaInput, RenderTarget rtOutDepth )
	{
		RenderTargetAttributes["SourceDepth"] = rtDepth;
		MSAAInput = msaaInput;

		ColorAttachment = rtOutDepth.ToColorHandle( view );
		DepthAttachment = rtOutDepth.ToDepthHandle( view );
	}

	internal override void OnRender()
	{
		Render( Graphics.Attributes, MSAAInput );
	}

	/// <summary>
	/// Write the depth at a quarter of the resolution into the bound depth target, from <c>SourceDepth</c> in
	/// <paramref name="attributes"/> (<paramref name="msaaInput"/> or not) - inside a render block. Also what the managed
	/// scene renderer runs, into its own frame.
	/// </summary>
	internal static void Render( RenderAttributes attributes, bool msaaInput )
	{
		attributes.SetCombo( "D_MSAA", msaaInput );
		attributes.Set( "DownsampleFactor", 4 );
		Graphics.Blit( DepthResolve, attributes );
	}
}
