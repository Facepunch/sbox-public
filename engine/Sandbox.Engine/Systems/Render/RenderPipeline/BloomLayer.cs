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
		NativeEngine.CSceneSystem.DownsampleTexture( Graphics.Context, RT.ColorTarget.native, (int)Graphics.DownsampleMethod.GaussianBlur );
	}
}
internal class QuarterDepthDownsampleLayer : ProceduralRenderLayer
{
	private static Material DepthResolve = Material.Create( "depthresolve", "shaders/depthresolve.shader" );
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
		Graphics.Attributes.SetCombo( "D_MSAA", MSAAInput );
		Graphics.Attributes.Set( "DownsampleFactor", 4 );
		Graphics.Blit( DepthResolve );
	}
}
