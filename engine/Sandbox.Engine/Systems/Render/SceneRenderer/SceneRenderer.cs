using NativeEngine;

namespace Sandbox.Rendering;

/// <summary>
/// One way of rendering a scene camera's view: native's scene system (<see cref="NativeSceneRenderer"/>) or Basalt
/// (Sandbox.Rendering's bridge, registered at startup). Every path that renders a scene ends in one of
/// <see cref="SceneCamera"/>'s entry points, which forward here, so <c>r_managed_scene</c> switches them all at once.
/// See docs/managed/render-paths.md.
/// </summary>
internal interface ISceneRenderer
{
	/// <summary>
	/// Render the camera into a swap chain, or into the headset's eyes when it targets one while VR is active.
	/// </summary>
	void Render( SceneCamera camera, SwapChainHandle_t swapChain, Vector2? size, in ViewSetup setup );

	/// <summary>
	/// Render the camera into a texture, as a child of the view being rendered when there is one.
	/// </summary>
	void RenderToTexture( SceneCamera camera, Texture target, Vector2? size, in ViewSetup setup );

	/// <summary>
	/// Render the camera into colour and optional depth textures, as a child of <paramref name="parent"/> when it's valid,
	/// and copy its resolved projection and viewport for whatever samples it.
	/// </summary>
	bool RenderToTexture( SceneCamera camera, Texture target, Texture depthTarget, ISceneView parent, in ViewSetup setup, string name, out ViewData viewData );

	/// <summary>
	/// Render the camera into a bitmap, blocking until it's read back.
	/// </summary>
	void RenderToBitmap( SceneCamera camera, Bitmap bitmap, in ViewSetup setup );

	/// <summary>
	/// Render the camera's six directions into a cube texture.
	/// </summary>
	void RenderToCube( SceneCamera camera, Texture cube, in ViewSetup setup );

	/// <summary>
	/// Free what's held for a camera that's been disposed, now rather than whenever the GC notices.
	/// </summary>
	void Forget( SceneCamera camera );

	/// <summary>
	/// Free what's held for a world that's been deleted.
	/// </summary>
	void Forget( SceneWorld world );

	/// <summary>
	/// Free every GPU resource held: at engine shutdown, before native goes, and when the switch moves away from it.
	/// </summary>
	void Shutdown();
}

/// <summary>
/// Which <see cref="ISceneRenderer"/> renders scenes. <c>r_managed_scene</c> is read once a frame, at the top of the frame's
/// output (<see cref="BeginFrame"/>), so a frame never mixes renderers. Tools that compare the two ask for
/// <see cref="Native"/> by name.
/// </summary>
internal static class SceneRenderer
{
	/// <summary>
	/// Native's scene system: the Source 2 camera renderer and its pipeline.
	/// </summary>
	public static ISceneRenderer Native { get; } = new NativeSceneRenderer();

	/// <summary>
	/// Basalt, through the GameObject bridge. Set by Sandbox.Rendering when the app system installs it.
	/// </summary>
	public static ISceneRenderer Basalt { get; set; }

	/// <summary>
	/// What renders this frame: Basalt while <c>r_managed_scene</c> is on and it's installed, native otherwise.
	/// </summary>
	public static ISceneRenderer Current { get; private set; } = Native;

	/// <summary>
	/// Read the switch, at the top of the frame's output. Moving off Basalt frees what it holds.
	/// </summary>
	internal static void BeginFrame()
	{
		var next = ManagedSceneRendering.Enabled && Basalt is not null ? Basalt : Native;
		if ( Current != next && Current != Native ) Current.Shutdown();

		Current = next;
	}

	/// <summary>
	/// Called when a camera is disposed.
	/// </summary>
	internal static void Forget( SceneCamera camera )
	{
		Native.Forget( camera );
		Basalt?.Forget( camera );
	}

	/// <summary>
	/// Called when a scene world is deleted.
	/// </summary>
	internal static void Forget( SceneWorld world )
	{
		Native.Forget( world );
		Basalt?.Forget( world );
	}

	/// <summary>
	/// Called at engine shutdown, while native is still there.
	/// </summary>
	internal static void Shutdown()
	{
		Basalt?.Shutdown();
		Basalt = null;
		Current = Native;
	}
}
