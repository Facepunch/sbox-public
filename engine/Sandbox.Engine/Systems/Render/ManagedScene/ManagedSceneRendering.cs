using NativeEngine;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sandbox.Rendering;

/// <summary>
/// What the managed scene renderer (Sandbox.SceneRenderer) offers the engine. It lives in its own assembly, which
/// references this one, so it registers itself here when it's loaded.
/// </summary>
internal interface IManagedSceneRenderer
{
	/// <summary>
	/// Render a camera's whole frame - its scene's world - into a swap chain, instead of the native pipeline.
	/// Returns false if it can't, and native should.
	/// </summary>
	bool Render( CameraComponent camera, SwapChainHandle_t swapChain, Vector2 size );

	/// <summary>
	/// Render a camera's whole frame into a texture, instead of the native pipeline. Returns false if it can't.
	/// </summary>
	bool RenderToTexture( CameraComponent camera, Texture target );

	/// <summary>
	/// Render a camera both ways - native to a bitmap with post processing off, managed to a texture - and compare
	/// them per pixel, saving both frames and where they differ into <paramref name="folder"/>. Returns the result
	/// as a line for the console.
	/// </summary>
	string Compare( CameraComponent camera, string folder );

	/// <summary>
	/// Free what it holds for a camera that's been destroyed - its renderer, targets and view - now, rather than whenever
	/// the GC notices, since they own native memory nothing else frees.
	/// </summary>
	void Forget( CameraComponent camera );

	/// <summary>
	/// Free its mirror of a world that's been deleted.
	/// </summary>
	void Forget( SceneWorld world );

	/// <summary>
	/// Free every GPU resource it holds, at engine shutdown - before native goes, so no finalizer frees them after.
	/// </summary>
	void Shutdown();
}

/// <summary>
/// Cameras render their scene's world through the managed scene renderer when <c>r_managed_scene</c> is on. The
/// managed renderer owns the camera's whole frame: native's post processing, UI and command lists don't run for
/// it, and engine overlays (the console, debug overlays) are drawn over it afterwards. See
/// docs/managed/scene-renderer.md, <i>The GameObject bridge</i>.
/// </summary>
internal static partial class ManagedSceneRendering
{
	/// <summary>
	/// Render cameras through Sandbox.SceneRenderer. Experimental: it draws models, skinned models, lights,
	/// environment probes, fog and ambient, and nothing else yet.
	/// </summary>
	[ConVar( "r_managed_scene", ConVarFlags.Protected, Help = "Render cameras through the managed scene renderer (experimental: no post processing, UI, particles, decals or map geometry yet)" )]
	public static bool Enabled { get; set; }

	/// <summary>
	/// Run the managed renderer's compute (the depth chain, contact shadows, and AO's command list) on the async compute queue
	/// beside the shadow maps, where the device has one. It saved 0.22 ms of a 6.3 ms frame at 4K, and 0.45 ms with AO
	/// (SceneLab's Heavy Scene, 2026-09-27).
	/// </summary>
	[ConVar( "r_managed_async_compute", Help = "Run the managed scene renderer's depth chain, contact shadows and AO on the async compute queue, beside shadow rendering" )]
	public static bool AsyncCompute { get; set; } = true;

	/// <summary>
	/// Set by Sandbox.SceneRenderer's module initializer when the assembly loads.
	/// </summary>
	public static IManagedSceneRenderer Renderer { get; set; }

	static bool loadTried;

	/// <summary>
	/// Called at engine shutdown: the managed renderer frees what it holds while native is still there.
	/// </summary>
	internal static void Shutdown()
	{
		Renderer?.Shutdown();
		Renderer = null;
	}

	/// <summary>
	/// Called when a camera is destroyed: the managed renderer frees what it made for it, if it's loaded.
	/// </summary>
	internal static void Forget( CameraComponent camera ) => Renderer?.Forget( camera );

	/// <summary>
	/// Called when a scene world is deleted: the managed renderer frees its mirror of it, if it's loaded.
	/// </summary>
	internal static void Forget( SceneWorld world ) => Renderer?.Forget( world );

	/// <summary>
	/// The managed renderer, loading its assembly the first time it's wanted. Null if it isn't there.
	/// </summary>
	static IManagedSceneRenderer Load()
	{
		if ( Renderer is not null || loadTried ) return Renderer;
		loadTried = true;

		try
		{
			var assembly = Assembly.Load( "Sandbox.SceneRenderer" );
			RuntimeHelpers.RunModuleConstructor( assembly.ManifestModule.ModuleHandle );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"r_managed_scene: couldn't load Sandbox.SceneRenderer - {e.Message}" );
		}

		if ( Renderer is null ) Log.Warning( "r_managed_scene: Sandbox.SceneRenderer didn't register a renderer" );
		return Renderer;
	}

	/// <summary>
	/// Render a camera through the managed renderer, if it's on and can. Called where the camera would add itself
	/// to native's render list.
	/// </summary>
	internal static bool TryRender( CameraComponent camera, SwapChainHandle_t swapChain, Vector2? size )
	{
		if ( pendingCompare is not null ) RunPendingCompare( camera );

		if ( !Enabled ) return false;

		// Stereo cameras stay native
		if ( VR.VRSystem.IsActive && camera.TargetEye != StereoTargetEye.None ) return false;

		var renderer = Load();
		if ( renderer is null ) return false;

		// Just before a compare, native renders the camera too, over the managed frame, so both renderers' effects that carry
		// frames forward (volumetric fog's history, auto exposure) have run when they're compared
		var warming = pendingCompare is not null && pendingFrames < CompareWarmFrames;

		if ( camera.RenderTarget is { } target && target.native.IsValid )
			return renderer.RenderToTexture( camera, target ) && !warming;

		var info = g_pRenderDevice.GetSwapChainInfo( swapChain );
		var targetSize = size ?? new Vector2( info.m_DisplayMode.m_nWidth, info.m_DisplayMode.m_nHeight );
		if ( !renderer.Render( camera, swapChain, targetSize ) ) return false;
		if ( warming ) return false;

		// The console, debug overlays and the like, which native adds to the main camera's view
		if ( camera.SceneCamera.EnableEngineOverlays )
			CCameraRenderer.RenderOverlay( swapChain );

		return true;
	}

	static string pendingCompare;
	static int pendingFrames;
	static bool quitAfterCompare;

	/// <summary>
	/// How many camera renders before a compare native renders the camera as well (<see cref="TryRender"/>): enough for
	/// volumetric fog's temporal history to settle.
	/// </summary>
	const int CompareWarmFrames = 120;

	/// <summary>
	/// Compare the managed renderer with native on the next camera that renders, a few frames from now so the
	/// scene has loaded: both frames and a diff go to <c>screenshots/managed_scene</c>, and the parity numbers to
	/// the console. <c>quit</c> exits afterwards, for scripts.
	/// </summary>
	[ConCmd( "r_managed_scene_compare", ConVarFlags.Protected, Help = "Render the next camera through native and the managed scene renderer and compare them: r_managed_scene_compare [frames to wait] [quit]" )]
	internal static void Compare( int frames = 30, string then = "" )
	{
		pendingCompare = System.IO.Path.Combine( Environment.CurrentDirectory, "screenshots", "managed_scene" );
		pendingFrames = Math.Max( 0, frames );
		quitAfterCompare = then.Equals( "quit", StringComparison.OrdinalIgnoreCase );
		Log.Info( $"r_managed_scene_compare: comparing the next camera in {pendingFrames} frames" );
	}

	static void RunPendingCompare( CameraComponent camera )
	{
		if ( pendingFrames-- > 0 ) return;

		var folder = pendingCompare;
		pendingCompare = null;

		// Not now: this is inside the frame's view rendering, where native's bitmap render only queues its view and
		// then waits for it forever (RenderViewScope renders at once only outside it). Next frame, before any views.
		compareCamera = camera;
		compareFolder = folder;
	}

	static CameraComponent compareCamera;
	static string compareFolder;

	/// <summary>
	/// Called at the start of a frame's output, before any views render - where a queued compare runs.
	/// </summary>
	internal static void BeforeRenderingViews()
	{
		if ( compareCamera is null ) return;

		var camera = compareCamera;
		var folder = compareFolder;
		compareCamera = null;
		compareFolder = null;

		RunCompare( camera, folder );
	}

	static void RunCompare( CameraComponent camera, string folder )
	{
		var renderer = Load();
		if ( renderer is null )
		{
			Log.Warning( "r_managed_scene_compare: no managed renderer" );
		}
		else if ( !camera.IsValid() )
		{
			Log.Warning( "r_managed_scene_compare: the camera went away" );
		}
		else
		{
			System.IO.Directory.CreateDirectory( folder );
			Log.Info( $"r_managed_scene_compare: {renderer.Compare( camera, folder )}" );
		}

		if ( quitAfterCompare ) ConVarSystem.Run( "quit" );
	}
}
