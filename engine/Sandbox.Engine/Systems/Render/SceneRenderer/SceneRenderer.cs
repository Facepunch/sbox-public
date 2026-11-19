using NativeEngine;

namespace Sandbox.Rendering;

/// <summary>
/// One way of rendering a scene camera's view: native's scene system (<see cref="NativeSceneRenderer"/>) or Borealis
/// (Sandbox.Rendering's bridge, registered at startup). Every path that renders a scene ends in one of
/// <see cref="SceneCamera"/>'s entry points, which forward here, so the process's renderer takes them all.
/// See docs/reference/borealis-render-paths.md.
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
	/// Render the camera's six directions into a cube texture, then <paramref name="afterFaces"/> (the GGX filter), on the GPU
	/// after the sixth face.
	/// </summary>
	void RenderToCube( SceneCamera camera, Texture cube, in ViewSetup setup, CommandList afterFaces = null );

	/// <summary>
	/// Free what's held for a camera that's been disposed, now rather than whenever the GC notices.
	/// </summary>
	void Forget( SceneCamera camera );

	/// <summary>
	/// Free what's held for a world that's been deleted.
	/// </summary>
	void Forget( SceneWorld world );

	/// <summary>
	/// Free every GPU resource held: at engine shutdown, before native goes.
	/// </summary>
	void Shutdown();

	/// <summary>
	/// Make what it renders with now, on the main thread at startup, so the first frames render as the rest do.
	/// </summary>
	void WarmUp();

	/// <summary>
	/// The frame's output starts (<see cref="SceneRenderer.BeginFrame"/>), whether or not anything renders a scene in it.
	/// </summary>
	void BeginFrame()
	{
	}

	/// <summary>
	/// Whether envmap probes wait to capture this frame (<see cref="Scene.RenderEnvmaps"/>): the renderer is still making a
	/// just-loaded scene's models, so a capture would be slow and miss them.
	/// </summary>
	bool HoldsCaptures => false;

	/// <summary>
	/// Capture the envmap probes <paramref name="scene"/> needs this frame (<see cref="Scene.RenderEnvmaps"/>). Native's:
	/// the first dirty one, every frame, seen or not.
	/// </summary>
	void RenderEnvmaps( Scene scene )
	{
		// One a frame: several in one frame balloon transform buffers and descriptor sets in complex scenes
		foreach ( var envmap in scene.EnvmapProbes() )
		{
			if ( !envmap.Dirty ) continue;

			envmap.RenderCubemap();
			break;
		}
	}
}

/// <summary>
/// Which <see cref="ISceneRenderer"/> renders scenes: Borealis in a process drawing on Borealis.Gpu (<c>-borealisgpu</c>),
/// native's scene system in any other. Tools that need native's ask for <see cref="Native"/> by name.
/// </summary>
internal static class SceneRenderer
{
	/// <summary>
	/// Native's scene system: the Source 2 camera renderer and its pipeline.
	/// </summary>
	public static ISceneRenderer Native { get; } = new NativeSceneRenderer();

	/// <summary>
	/// Borealis, through the GameObject bridge, on Borealis.Gpu. Set by Sandbox.Rendering when the app system installs it.
	/// </summary>
	public static ISceneRenderer Borealis { get; set; }

	/// <summary>
	/// What renders this frame: Borealis on a Borealis.Gpu device when it's installed, native otherwise.
	/// </summary>
	public static ISceneRenderer Current { get; private set; } = Native;

	/// <summary>
	/// Pick the renderer, at the top of the frame's output. A process drawing on Borealis.Gpu renders through Borealis: Source 2's
	/// renderer has no device to draw on there.
	/// </summary>
	internal static void BeginFrame()
	{
		Current = ManagedSceneRendering.Enabled && Borealis is not null ? Borealis : Native;
		Current.BeginFrame();
	}

	/// <summary>
	/// Called when a camera is disposed.
	/// </summary>
	internal static void Forget( SceneCamera camera )
	{
		Native.Forget( camera );
		Borealis?.Forget( camera );
	}

	/// <summary>
	/// Called when a scene world is deleted.
	/// </summary>
	internal static void Forget( SceneWorld world )
	{
		Native.Forget( world );
		Borealis?.Forget( world );
	}

	/// <summary>
	/// Called at startup, before anything renders. Picks the renderer for what renders before the first frame's output
	/// (startup, and tests that drive frames themselves).
	/// </summary>
	internal static void WarmUp()
	{
		BeginFrame();
		if ( ManagedSceneRendering.Enabled ) Borealis?.WarmUp();
	}

	/// <summary>
	/// Called at engine shutdown, while native is still there.
	/// </summary>
	internal static void Shutdown()
	{
		Borealis?.Shutdown();
		Borealis = null;
		Current = Native;
	}
}
