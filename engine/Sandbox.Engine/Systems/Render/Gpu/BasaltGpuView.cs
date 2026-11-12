using NativeEngine;

namespace Sandbox;

/// <summary>
/// A camera's frame on a Basalt.Gpu device, as the drawing in its stages asks about it (<see cref="Graphics.IFrameView"/>):
/// the targets it draws into, the camera's frustum and whether post processing is on. A block made with
/// <see cref="Graphics.Scope.CreateOnView"/> draws into <see cref="Target"/> at
/// <see cref="Viewport"/>, and setting <see cref="Graphics.RenderTarget"/> back to null comes back to them. It has no native
/// targets or layer: what asks for those (native readbacks of the frame, per-object lighting, drawing a scene object
/// again) gets nothing.
/// </summary>
internal sealed class BasaltGpuView : Graphics.IFrameView, IDisposable
{
	readonly RenderBackend backend;

	public BasaltGpuView( RenderBackend backend ) => this.backend = backend;

	/// <summary>The frame's own targets, which a block starts on.</summary>
	public RenderTarget Target;

	/// <summary>The camera's part of <see cref="Target"/>, in pixels.</summary>
	public Rect Viewport;

	/// <summary>What <c>ISceneView.GetPostProcessEnabled</c> says for a native view.</summary>
	public bool PostProcessEnabled = true;

	/// <summary>
	/// Whether <see cref="Target"/>'s colour is written through its sRGB view, so what's drawn is encoded by the hardware, as
	/// Source 2 writes an 8-bit texture a camera renders into.
	/// </summary>
	public bool SrgbWrite;

	/// <summary>The kind of layer the stage is, which drawing code may check.</summary>
	public SceneLayerType LayerType = SceneLayerType.Translucent;

	/// <summary>The camera, as the view's frustum: set it up with <see cref="SetCamera"/>.</summary>
	public CFrustum Frustum { get; } = CFrustum.Create();

	/// <summary>Point the frustum where a camera is, as <c>CCameraRenderer</c> sets up its view's.</summary>
	public void SetCamera( SceneCamera camera, Vector2 size )
	{
		var aspect = size.y > 0 ? size.x / size.y : 1;
		if ( camera.Ortho ) Frustum.InitOrthoCamera( camera.Position, camera.Rotation.Angles(), camera.ZNear, camera.ZFar, camera.OrthoHeight * aspect, camera.OrthoHeight );
		else Frustum.InitCamera( camera.Position, camera.Rotation.Angles(), camera.ZNear, camera.ZFar, camera.FieldOfView, aspect );
	}

	/// <summary>Bind the frame's targets and its viewport, as a stage starts.</summary>
	public void Bind()
	{
		BindOwn();
		Graphics.Viewport = Viewport;
	}

	void BindOwn()
	{
		if ( backend is BasaltGpuRenderBackend gpu ) gpu.BindRenderTarget( Target, SrgbWrite );
		else backend.BindRenderTarget( Target );
	}

	bool Graphics.IFrameView.PostProcessEnabled => PostProcessEnabled;
	int Graphics.IFrameView.ToolsVisMode => 0;
	bool Graphics.IFrameView.IsChildView => false;
	int Graphics.IFrameView.CubeFace => -1;
	bool Graphics.IFrameView.IsCapture => false;

	// Its targets are Basalt.Gpu textures, with no native handle
	ITexture Graphics.IFrameView.GetColorTarget( out bool owned )
	{
		owned = false;
		return default;
	}

	ITexture Graphics.IFrameView.GetDepthTarget( out bool owned )
	{
		owned = false;
		return default;
	}

	IMaterialMode Graphics.IFrameView.ModeFor( Material material ) => material.native.GetMode();

	StringToken Graphics.IFrameView.DrawShaderMode => default;

	void Graphics.IFrameView.SetupLighting( SceneObject obj, RenderAttributes attributes ) { }

	void Graphics.IFrameView.RenderSceneObject( IRenderContext context, SceneObject obj, Transform transform, Color color, Material material, RenderAttributes attributes ) { }

	// A target of only depth draws into the frame's colour, as native's layer binding assumes
	void Graphics.IFrameView.BindTarget( IRenderContext context, RenderTarget target )
	{
		var bound = target.ColorTarget is null ? RenderTarget.From( Target.ColorTarget, target.DepthTarget ) : target;
		if ( backend is BasaltGpuRenderBackend gpu ) gpu.BindRenderTarget( bound, SrgbWrite && bound.ColorTarget == Target.ColorTarget );
		else backend.BindRenderTarget( bound );
	}

	void Graphics.IFrameView.RestoreTargets( IRenderContext context, float minZ, float maxZ )
	{
		BindOwn();
		backend.SetViewport( Viewport );
	}

	public void Dispose() => Frustum.Delete();
}
