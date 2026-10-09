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
		var rt = GetTarget( (int)vp.Rect.Width, (int)vp.Rect.Height );

		// Color outputs to UV offsets
		DepthAttachment = rt.ToDepthHandle( view );

		view.GetRenderAttributesPtr().SetTextureValue( "RefractionDepthTexture", rt.DepthTarget.native, -1 );
	}

	/// <summary>
	/// The refraction stencil's target for a quarter viewport this size: a temporary D16 depth. Also what the managed scene
	/// renderer draws it into.
	/// </summary>
	internal static RenderTarget GetTarget( int width, int height )
	{
		return RenderTarget.GetTemporary(
			width,
			height,
			colorFormat: ImageFormat.None,
			depthFormat: ImageFormat.D16 );
	}
}
