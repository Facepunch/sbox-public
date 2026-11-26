using NativeEngine;

namespace Sandbox;

public static partial class Graphics
{
	/// <summary>
	/// The frame a render block draws into: a frame Borealis draws on Borealis.Gpu (<c>BorealisGpuView</c>).
	/// <see cref="Graphics"/>, command lists, post processing and UI ask it what the camera's view is. A standalone block
	/// (<see cref="Scope.Create"/>) has none.
	/// </summary>
	internal interface IFrameView
	{
		/// <summary>
		/// The camera, as the view's frustum.
		/// </summary>
		CFrustum Frustum { get; }

		/// <summary>
		/// Whether the view has post processing on (<c>ISceneView.GetPostProcessEnabled</c>).
		/// </summary>
		bool PostProcessEnabled { get; }

		/// <summary>
		/// The view's tools visualisation mode (<c>ISceneView.GetToolsVisMode</c>).
		/// </summary>
		int ToolsVisMode { get; }

		/// <summary>
		/// Which face of a cube capture the view draws (its render target array index), or -1.
		/// </summary>
		int CubeFace { get; }

		/// <summary>
		/// Whether this stage belongs to a capture rather than a camera's own image.
		/// </summary>
		bool IsCapture { get; }

		Texture ColorTexture { get; }
		Texture DepthTexture { get; }

		/// <summary>
		/// Put an object's own lighting - its light probe volume - into attributes, as
		/// <c>CSceneSystem::SetupPerObjectLighting</c> does from a layer's view.
		/// </summary>
		void SetupLighting( SceneObject obj, RenderAttributes attributes );

		/// <summary>
		/// Draw a scene object with a transform, colour, material override and attributes into what's bound
		/// (<see cref="Graphics.Render(SceneObject, Transform?, Color?, Material)"/>).
		/// </summary>
		void RenderSceneObject( SceneObject obj, Transform transform, Color color, Material material, RenderAttributes attributes );

		/// <summary>
		/// Bind a render target's colour and depth in place of the frame's (<see cref="Graphics.RenderTarget"/>).
		/// </summary>
		void BindTarget( RenderTarget target );

		/// <summary>
		/// Go back to the frame's own targets and viewport, with the depth range the frame had before a render target was
		/// bound.
		/// </summary>
		void RestoreTargets( float minZ, float maxZ );
	}

	/// <summary>
	/// The frame the current render block draws into, or null in a standalone one.
	/// </summary>
	internal static IFrameView CurrentView => _state.view;

	/// <summary>
	/// The frame, for what only a view can answer - which a standalone render block has none of.
	/// </summary>
	static IFrameView View => _state.view ?? throw new InvalidOperationException( "A standalone render block has no view." );

	/// <summary>
	/// The current view's frustum.
	/// </summary>
	internal static CFrustum ViewFrustum => View.Frustum;

	/// <summary>
	/// Whether the current view has post processing on.
	/// </summary>
	internal static bool PostProcessEnabled => View.PostProcessEnabled;

	/// <summary>
	/// The current view's tools visualisation mode.
	/// </summary>
	internal static int ToolsVisMode => View.ToolsVisMode;

	/// <summary>
	/// Which face of a cube capture the current view draws (<c>SceneCamera.RenderToCubeTexture</c>), or -1.
	/// </summary>
	internal static int CubeFace => View.CubeFace;

	/// <summary>
	/// Whether this stage belongs to a capture rather than a camera's own image.
	/// </summary>
	internal static bool IsCapture => View.IsCapture;

	internal static Texture BoundColorTexture => _state.renderTarget?.ColorTarget ?? _state.view?.ColorTexture;
	internal static Texture BoundDepthTexture => _state.renderTarget is { } target ? target.DepthTarget : _state.view?.DepthTexture;

	/// <summary>The frame's own depth texture, regardless of an override bound by the current draw.</summary>
	internal static Texture DepthTexture => View.DepthTexture;
}
