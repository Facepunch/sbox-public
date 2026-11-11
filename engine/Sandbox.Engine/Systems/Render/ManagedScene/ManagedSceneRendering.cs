namespace Sandbox.Rendering;

/// <summary>
/// The compare Basalt offers <c>r_managed_scene_compare</c>: a camera rendered by both renderers, compared per pixel.
/// </summary>
internal interface IManagedSceneCompare
{
	/// <summary>
	/// Render a camera both ways - native to a bitmap with post processing off, managed to a texture - and compare
	/// them per pixel, saving both frames and where they differ into <paramref name="folder"/>. Returns the result
	/// as a line for the console.
	/// </summary>
	string Compare( CameraComponent camera, string folder );
}

/// <summary>
/// Basalt's switches, its compare and its frame counters. With <c>r_managed_scene</c> on, every scene render goes through
/// Basalt (<see cref="SceneRenderer"/>), which owns the camera's whole frame: native's post processing, UI and command lists
/// don't run for it, and the platform overlay (the console, debug overlays) is drawn over it by whoever owns the swap chain.
/// See docs/managed/basalt.md, <i>The GameObject bridge</i>, and docs/managed/render-paths.md.
/// </summary>
internal static partial class ManagedSceneRendering
{
	/// <summary>
	/// Render every scene through Basalt instead of native. Read once a frame (<see cref="SceneRenderer.BeginFrame"/>).
	/// </summary>
	[ConVar( "r_managed_scene", ConVarFlags.Protected, Help = "Render cameras through the managed scene renderer (experimental)" )]
	public static bool Enabled { get; set; }

	/// <summary>
	/// Run the managed renderer's compute (the depth chain, contact shadows, and AO's command list) on the async compute queue
	/// beside the shadow maps, where the device has one. It saved 0.22 ms of a 6.3 ms frame at 4K, and 0.45 ms with AO
	/// (SceneLab's Heavy Scene, 2026-09-27).
	/// </summary>
	[ConVar( "r_managed_async_compute", Help = "Run the managed scene renderer's depth chain, contact shadows and AO on the async compute queue, beside SSR and shadow rendering" )]
	public static bool AsyncCompute { get; set; } = true;

	[ConVar( "r_managed_occlusion", Help = "Skip opaque meshes hidden by the depth prepass in the managed scene renderer" )]
	public static bool Occlusion { get; set; }

	/// <summary>
	/// Called as a camera component renders, before it does: counts down to a queued compare. True while the compare warms
	/// up, when native renders the camera too, over Basalt's frame, so both renderers' effects that carry frames forward
	/// (volumetric fog's history, auto exposure) have run when they're compared.
	/// </summary>
	internal static bool BeforeCameraRender( CameraComponent camera )
	{
		if ( pendingCompare is null ) return false;

		RunPendingCompare( camera );
		return pendingCompare is not null && pendingFrames < CompareWarmFrames;
	}

	static string pendingCompare;
	static int pendingFrames;
	static bool quitAfterCompare;

	/// <summary>
	/// How many camera renders before a compare native renders the camera as well (<see cref="BeforeCameraRender"/>): enough for
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
		var renderer = SceneRenderer.Basalt as IManagedSceneCompare;
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
