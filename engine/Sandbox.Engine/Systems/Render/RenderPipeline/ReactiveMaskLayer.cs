using NativeEngine;

namespace Sandbox.Rendering;

/// <summary>
/// Renders objects matching the given flags in depth-only mode to a separate depth target.
/// Pixels where reactive geometry exists will have depth > 0 (reverse-Z clear = 0).
/// Multiple instances can share the same depth RT to accumulate different object types.
/// </summary>
internal class ReactiveDepthLayer : RenderLayer
{
	public ReactiveDepthLayer( string name, SceneObjectFlags requiredFlags, bool clearDepth = true )
	{
		Name = name;
		LayerType = SceneLayerType.DepthPrepass;
		ShaderMode = "Depth";

		Flags |= LayerFlags.NeverRemove | LayerFlags.IsDepthRenderingPass;

		ObjectFlagsRequired = requiredFlags;
		ObjectFlagsExcluded = SceneObjectFlags.IsLight;

		if ( clearDepth )
			ClearFlags = ClearFlags.Depth;
	}

	public void Setup( SceneViewRenderTargetHandle depthHandle )
	{
		DepthAttachment = depthHandle;
	}
}

/// <summary>
/// Converts the reactive depth target into an R16F reactive mask for FSR3.
/// Any pixel with depth > 0 (written by a reactive object) outputs 1.0.
/// </summary>
internal class ReactiveMaskLayer : ProceduralRenderLayer
{
	static Material MaskMaterial = Material.FromShader( "shaders/reactive_mask_generate.shader" );

	RenderTarget ReactiveMaskRT;
	RenderTarget ReactiveDepthRT;

	/// <summary>
	/// The reactive mask color RT handle, for additional layers to render into.
	/// </summary>
	public SceneViewRenderTargetHandle ReactiveMaskHandle { get; private set; }

	public ReactiveMaskLayer()
	{
		Name = "Reactive Mask";
		Flags |= LayerFlags.NeverRemove
			| LayerFlags.DoesntModifyDepthStencilBuffer;
	}

	/// <summary>
	/// Allocates the shared depth RT and the output mask RT.
	/// Returns the depth handle that ReactiveDepthLayer instances should render into.
	/// </summary>
	public SceneViewRenderTargetHandle Setup( RenderViewport viewport, ISceneView view )
	{
		ReactiveDepthRT = RenderTarget.GetTemporary(
			(int)viewport.Rect.Width,
			(int)viewport.Rect.Height,
			colorFormat: ImageFormat.None,
			depthFormat: ImageFormat.D32,
			targetName: "ReactiveDepth" );

		ReactiveMaskRT = RenderTarget.GetTemporary(
			(int)viewport.Rect.Width,
			(int)viewport.Rect.Height,
			colorFormat: ImageFormat.R16F,
			depthFormat: ImageFormat.None,
			targetName: "ReactiveMask" );

		var reactiveDepthHandle = ReactiveDepthRT.ToDepthHandle( view );

		ColorAttachment = ReactiveMaskRT.ToColorHandle( view );
		ReactiveMaskHandle = ColorAttachment;
		ClearFlags = ClearFlags.Color;

		RenderTargetAttributes["ReactiveDepth"] = reactiveDepthHandle;

		view.GetRenderAttributesPtr().SetTextureValue( "ReactiveMask", ReactiveMaskRT.ColorTarget.native, -1 );

		return reactiveDepthHandle;
	}

	internal override void OnRender()
	{
		Graphics.Blit( MaskMaterial );
	}
}

/// <summary>
/// Debug visualization: blits the reactive mask over the scene as a red overlay.
/// </summary>
internal class ReactiveMaskDebugLayer : ProceduralRenderLayer
{
	static Material DebugMaterial = Material.FromShader( "shaders/reactive_mask_debug.shader" );

	public ReactiveMaskDebugLayer()
	{
		Name = "Reactive Mask Debug";
		Flags |= LayerFlags.NeverRemove
			| LayerFlags.DoesntModifyDepthStencilBuffer;
	}

	internal override void OnRender()
	{
		Graphics.Blit( DebugMaterial );
	}
}
