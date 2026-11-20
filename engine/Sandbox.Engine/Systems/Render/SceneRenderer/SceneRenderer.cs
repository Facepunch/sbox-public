using NativeEngine;

namespace Sandbox.Rendering;

/// <summary>
/// How a scene camera's view is rendered: Borealis (Sandbox.Rendering's bridge, registered at startup), or nothing in a
/// process without a Borealis.Gpu device. Every path that renders a scene ends in one of <see cref="SceneCamera"/>'s entry
/// points, which forward here, so the process's renderer takes them all. See docs/reference/borealis-render-paths.md.
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
	/// Render the camera into colour and optional depth textures, as a child of the frame being drawn when there is one,
	/// and copy its resolved projection and viewport for whatever samples it.
	/// </summary>
	bool RenderToTexture( SceneCamera camera, Texture target, Texture depthTarget, in ViewSetup setup, string name, out ViewData viewData );

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
	/// Capture the envmap probes <paramref name="scene"/> needs this frame (<see cref="Scene.RenderEnvmaps"/>): the
	/// first dirty one, every frame, seen or not.
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
/// <see cref="None"/> in any other (a server, a headless test, a content worker), which renders nothing.
/// </summary>
internal static class SceneRenderer
{
	/// <summary>
	/// What renders in a process without a Borealis.Gpu device: nothing.
	/// </summary>
	public static ISceneRenderer None { get; } = new NoSceneRenderer();

	/// <summary>
	/// Borealis, through the GameObject bridge, on Borealis.Gpu. Set by Sandbox.Rendering when the app system installs it.
	/// </summary>
	public static ISceneRenderer Borealis { get; set; }

	/// <summary>
	/// What renders this frame: Borealis on a Borealis.Gpu device when it's installed, <see cref="None"/> otherwise.
	/// </summary>
	public static ISceneRenderer Current { get; private set; } = None;

	/// <summary>
	/// Pick the renderer, at the top of the frame's output.
	/// </summary>
	internal static void BeginFrame()
	{
		Current = ManagedSceneRendering.Enabled && Borealis is not null ? Borealis : None;
		Current.BeginFrame();
	}

	/// <summary>
	/// Called when a camera is disposed.
	/// </summary>
	internal static void Forget( SceneCamera camera ) => Borealis?.Forget( camera );

	/// <summary>
	/// Called when a scene world is deleted.
	/// </summary>
	internal static void Forget( SceneWorld world ) => Borealis?.Forget( world );

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
		Current = None;
	}

	/// <summary>
	/// A process with no device to draw on: every render is skipped.
	/// </summary>
	sealed class NoSceneRenderer : ISceneRenderer
	{
		public void Render( SceneCamera camera, SwapChainHandle_t swapChain, Vector2? size, in ViewSetup setup ) { }
		public void RenderToTexture( SceneCamera camera, Texture target, Vector2? size, in ViewSetup setup ) { }

		public bool RenderToTexture( SceneCamera camera, Texture target, Texture depthTarget, in ViewSetup setup, string name, out ViewData viewData )
		{
			viewData = default;
			return false;
		}

		public void RenderToBitmap( SceneCamera camera, Bitmap bitmap, in ViewSetup setup ) { }
		public void RenderToCube( SceneCamera camera, Texture cube, in ViewSetup setup, CommandList afterFaces = null ) { }
		public void Forget( SceneCamera camera ) { }
		public void Forget( SceneWorld world ) { }
		public void Shutdown() { }
		public void WarmUp() { }
		public void RenderEnvmaps( Scene scene ) { }
	}
}
