using NativeEngine;

namespace Sandbox.Rendering;

internal class RefractionStencilLayer : RenderLayer
{
	public RefractionStencilLayer()
	{
		Name = $"Refraction Stencil Layer";
		LayerType = SceneLayerType.Opaque;
		Flags |= LayerFlags.NeverRemove | LayerFlags.IsDepthRenderingPass | LayerFlags.ForceDepthFastPath;
		ShaderMode = "Depth";
		ClearFlags = ClearFlags.Depth | ClearFlags.Stencil;
		ObjectFlagsRequired = SceneObjectFlags.WantsFrameBufferCopyTexture | SceneObjectFlags.IsTranslucent;
		Attributes.SetCombo( "D_REFRACTION_TEST", 1 );
		Attributes.SetCombo( "D_RENDER_BACKFACES", 1 );
	}

	public void Setup( ISceneView view, RenderViewport vp )
	{
		var rt = RenderTarget.GetTemporary(
			(int)vp.Rect.Width,
			(int)vp.Rect.Height,
			colorFormat: ImageFormat.None,
			depthFormat: ImageFormat.D16 );

		// Color outputs to UV offsets
		DepthAttachment = rt.ToDepthHandle( view );

		view.GetRenderAttributesPtr().SetTextureValue( "RefractionDepthTexture", rt.DepthTarget.native, -1 );
	}
}
