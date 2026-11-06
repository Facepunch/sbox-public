using Sandbox.VR;

namespace Sandbox;

/// <summary>
/// Which of a frame's cameras can be seen. <see cref="Scene"/> renders every enabled camera, by priority, and a camera
/// that draws over the whole screen and clears it hides everything drawn to the screen before it: a menu, garage or
/// overview camera a game leaves enabled under its player's camera still costs a whole frame (its scene, post processing
/// and UI) that is never seen. <c>r_skip_hidden_cameras</c> skips such cameras.
/// </summary>
internal static class SceneCameraCover
{
	[ConVar( "r_skip_hidden_cameras", Help = "Don't render a camera drawing to the screen when a later camera of the frame draws over all of it and clears it to an opaque color" )]
	internal static bool Enabled { get; set; } = true;

	/// <summary>
	/// Whether this camera draws to the screen: not to a texture, and not to an HMD's eyes, which a camera targeting
	/// them does only while VR is active (as <see cref="CameraComponent"/>'s and <see cref="SceneCamera"/>'s render lists
	/// decide).
	/// </summary>
	internal static bool DrawsToScreen( CameraComponent camera ) =>
		!(camera.RenderTarget is not null && camera.RenderTarget.native.IsValid) &&
		(camera.TargetEye == StereoTargetEye.None || !VRSystem.IsActive);

	/// <summary>
	/// Whether a camera with these settings hides everything drawn to the screen before it in the frame: it renders, to
	/// the screen at the screen's size, over the whole viewport, and clears its color there to an opaque background.
	/// </summary>
	internal static bool HidesScreen( bool renders, bool drawsToScreen, bool customSize, Vector4 viewport, ClearFlags clear,
		float backgroundAlpha ) =>
		renders && drawsToScreen && !customSize &&
		viewport.x <= 0 && viewport.y <= 0 && viewport.x + viewport.z >= 1 && viewport.y + viewport.w >= 1 &&
		(clear & ClearFlags.Color) == ClearFlags.Color && backgroundAlpha >= 1;

	static bool HidesScreen( CameraComponent camera ) =>
		HidesScreen( camera.Active && !camera.IsSceneEditorCamera, DrawsToScreen( camera ), camera.CustomSize.HasValue,
			camera.Viewport, camera.ClearFlags, camera.BackgroundColor.a );

	/// <summary>
	/// The index of the last of these, in render order, that hides the screen: the ones before it are hidden. -1 when
	/// nothing before it is (no such camera, or only the first).
	/// </summary>
	internal static int LastCover<T>( IReadOnlyList<T> cameras, Func<T, bool> hidesScreen )
	{
		for ( int i = cameras.Count - 1; i > 0; i-- )
		{
			if ( hidesScreen( cameras[i] ) ) return i;
		}
		return -1;
	}

	/// <summary>
	/// The index in the frame's sorted cameras of the last one that hides the screen: the screen cameras before it
	/// aren't rendered. -1 when every camera renders.
	/// </summary>
	internal static int ScreenCover( List<CameraComponent> cameras )
	{
		// The managed scene renderer (r_managed_scene) owns the whole frame its own way: leave it alone
		if ( !Enabled || Rendering.ManagedSceneRendering.Enabled ) return -1;
		return LastCover( cameras, HidesScreen );
	}
}
