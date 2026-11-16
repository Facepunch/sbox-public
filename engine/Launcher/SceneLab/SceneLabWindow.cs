using PanelWindow = Editor.PanelWindow;
using NativeEngine;
using Sandbox.Rendering.Bridge;
using Sandbox.UI;

namespace Sandbox.SceneLab;

/// <summary>
/// The Scene Lab window. A GameObject scene is ticked and its camera drawn into the swap chain by native's scene system in
/// <see cref="OnRenderBackground"/>, then the panels draw over it: a title bar with the menus, and a transparent viewport that
/// takes the orbit input.
/// </summary>
internal sealed partial class SceneLabWindow : PanelWindow
{
	readonly FrameTimer timer = new();

	Label stats;
	SceneViewport viewport;

	public float Yaw { get; set; } = 35;
	public float Pitch { get; set; } = 25;
	public float Distance { get; set; }

	/// <summary>
	/// Objects in the scene's world, lights included.
	/// </summary>
	public int ObjectCount => game?.SceneWorld.SceneObjects.Count ?? 0;

	/// <summary>
	/// The scene on screen.
	/// </summary>
	public SceneLabScene Scene { get; private set; }

	/// <summary>
	/// Set by a capture or benchmark from the command line - the app closes the window after this frame.
	/// </summary>
	public bool Finished { get; private set; }

	readonly string captureArgument = Args.String( "-capture" );
	readonly int captureFrame = Math.Max( 1, Args.Int( "-capture-frame", 30 ) );
	string captureRequested;
	Benchmark benchmark;
	ParityRun parity;
	int frame;

	/// <summary>
	/// Where this capture goes: <c>-capture</c>'s path, or the parity run's for the preset it's capturing.
	/// </summary>
	string CapturePath => parity?.CapturePath ?? captureArgument;

	// A result shown in place of the stats for a while
	string message;
	RealTimeUntil messageTime;
	RealTimeSince statsUpdated;

	/// <summary>
	/// <c>-swapchain-msaa N</c>: a multisampled window, whose sample count native's bitmap renders (the captures) take.
	/// 4x unless it says otherwise; <c>-swapchain-msaa 1</c> turns it off.
	/// </summary>
	internal override RenderMultisampleType SwapChainMultisample => Args.Int( "-swapchain-msaa", 4 ) switch
	{
		2 => RenderMultisampleType.RENDER_MULTISAMPLE_2X,
		4 => RenderMultisampleType.RENDER_MULTISAMPLE_4X,
		8 => RenderMultisampleType.RENDER_MULTISAMPLE_8X,
		_ => RenderMultisampleType.RENDER_MULTISAMPLE_NONE,
	};

	public SceneLabWindow( SceneLabScene scene ) : base( "Scene Lab", new Vector2( Args.Int( "-width", 1280 ), Args.Int( "-height", 760 ) ), null, true, vsync: !Args.Has( "-novsync" ) )
	{
		ClearsBackground = false;
		AlwaysFullFrameRate = true;

		// -fullscreen: the scene fills the display, borderless (or -fullscreen exclusive), with no title bar
		if ( Args.Has( "-fullscreen" ) )
		{
			ExclusiveFullscreen = Args.String( "-fullscreen" ) is "exclusive";
			Fullscreen = true;
		}

		// The render device only records GPU frame times for the main window's swap chain
		g_pRenderDevice.SetSwapChainIsMainWindow( Window.SwapChain, true );

		Root.AddChild( BuildUI() );
		Load( scene );

		if ( Args.Has( "-benchmark" ) )
		{
			VSync = false;
			benchmark = new Benchmark( Args.String( "-benchmark" ) );
		}

		if ( Args.Has( "-parity" ) )
		{
			VSync = false;
			parity = new ParityRun( Args.String( "-parity" ) );
			parity.Start( this );
		}
	}

	/// <summary>
	/// Load a preset for the parity run to capture, counting frames from zero.
	/// </summary>
	internal void StartCapture( SceneLabScene scene )
	{
		Load( scene );
		frame = 0;
	}

	/// <summary>
	/// Drop the loaded GameObject scene and draw nothing, while the parity run idles between presets.
	/// </summary>
	internal void Unload()
	{
		UnloadGame();
	}

	/// <summary>
	/// A capture is done: the parity run goes on to its next preset, anything else closes the window.
	/// </summary>
	void CaptureDone()
	{
		if ( parity is not null && parity.Advance( this ) ) return;
		if ( parity is not null ) FinishParity();
		else Finished = true;
	}

	/// <summary>
	/// The parity run has captured everything: its table, and the exit code.
	/// </summary>
	internal void FinishParity()
	{
		if ( parity.Summarise() > 0 ) Environment.ExitCode = 1;
		parity = null;
		Finished = true;
	}

	Panel BuildUI()
	{
		var root = new Panel();
		root.AddClass( "editor-window scenelab" );
		root.StyleSheet.Load( "/styles/editor.scss" );
		root.StyleSheet.Add( StyleSheet.FromString( Styles, "/styles/scenelab.scss" ) );

		var bar = root.Add.Panel( "titlebar window-drag" );
		if ( Args.Has( "-fullscreen" ) ) bar.Style.Display = DisplayMode.None;
		bar.Add.Label( "Scene Lab", "scenelab-title" );

		var menus = bar.AddChild( new MenuBar() );
		menus.AddClass( "window-nodrag" );

		var file = menus.AddMenu( "File" );
		file.AddOption( "Quit", "logout", RequestClose ).Shortcut = "Alt+F4";

		// A submenu per group, in the order the presets list them
		var scenes = menus.AddMenu( "Scene" );
		foreach ( var group in SceneLabScene.Presets.GroupBy( x => x.Group ) )
		{
			var submenu = scenes.AddMenu( group.Key );
			foreach ( var preset in group )
			{
				// A plain option rather than a toggle, so picking one closes the menu
				var option = submenu.AddOption( preset.Name, () => Load( preset ) );
				submenu.AboutToShow += m => option.Checked = Scene == preset;
			}
		}

		var viewMenu = menus.AddMenu( "View" );
		var occlusion = viewMenu.AddOption( "Occlusion Culling", on => { Sandbox.Rendering.RenderPipeline.OcclusionCulling = on; timer.Clear(); } );
		viewMenu.AboutToShow += m => occlusion.Checked = Sandbox.Rendering.RenderPipeline.OcclusionCulling;
		var vsync = viewMenu.AddOption( "VSync", on => VSync = on );
		viewMenu.AboutToShow += m => vsync.Checked = VSync;
		viewMenu.AddOption( "Reset Camera", "center_focus_strong", ResetCamera );
		viewMenu.AddSeparator();
		viewMenu.AddOption( "Capture", "photo_camera", () => captureRequested = OutputFolder() );

		bar.Add.Panel( "grow" );

		WindowButton( bar, "remove", null, Minimize );
		WindowButton( bar, "crop_square", null, ToggleMaximized );
		WindowButton( bar, "close", "close", RequestClose );

		viewport = root.AddChild( new SceneViewport( this ) );

		// Over the corner of the scene rather than in the title bar, where it didn't fit
		stats = viewport.Add.Label( "", "scenelab-stats" );

		return root;
	}

	static void WindowButton( Panel bar, string icon, string classname, Action onClick )
	{
		var button = bar.Add.Panel( "windowbutton window-nodrag" );
		if ( classname is not null ) button.AddClass( classname );

		button.Add.Icon( icon, "icon" );
		button.AddEventListener( "onclick", onClick );
	}

	const string Styles = """
		@import "_palette.scss";

		// The world shows through everywhere but the title bar
		.editor-window.scenelab { flex-direction: column; background-color: transparent; }
		.scenelab .titlebar { height: 40px; padding: 0 6px 0 16px; gap: 8px; justify-content: flex-start; background-color: $surface; border-bottom: 1px solid $line; }
		.scenelab .titlebar .grow { flex-grow: 1; }
		.scenelab .scenelab-title { font-weight: 600; color: $text-muted; }
		.scenelab .sceneviewport { flex-grow: 1; position: relative; pointer-events: all; }

		// Monospaced and fixed-width fields, so the numbers stay put as they change
		.scenelab .scenelab-stats { position: absolute; top: 10px; left: 10px; padding: 8px 10px; border-radius: 4px; background-color: rgba( 0, 0, 0, 0.55 ); font-family: Roboto Mono; font-size: 11px; color: #d8dce4; white-space: pre; pointer-events: none; }
		""";

	/// <summary>
	/// Replace the scene with a preset's.
	/// </summary>
	public void Load( SceneLabScene scene )
	{
		Scene = scene;
		LoadGame( scene );
	}

	public void ResetCamera() => (Yaw, Pitch, Distance) = gameCamera;

	// The swap chain last made the main one, and its size then
	SwapChainHandle_t mainSwapChain;
	Vector2 mainSwapChainSize;

	/// <summary>
	/// Render the scene into the swap chain, under the panels.
	/// </summary>
	private protected override void OnRenderBackground( SwapChainHandle_t swapChain, Vector2 size )
	{
		// A multisampled window is the main one, as a game's is: native's bitmap renders (the captures) take the main swap
		// chain's sample count. Set when the swap chain or its size changes: native asserts on every call from a tools-mode
		// app, and only the handle and its dimensions are kept
		if ( SwapChainMultisample != RenderMultisampleType.RENDER_MULTISAMPLE_NONE && ((IntPtr)swapChain != (IntPtr)mainSwapChain || size != mainSwapChainSize) )
		{
			CSceneSystem.SetMainSwapChain( swapChain );
			(mainSwapChain, mainSwapChainSize) = (swapChain, size);
		}

		parity?.Tick( this );
		if ( Finished || parity?.Idle == true ) return;

		if ( game is not null ) RenderGame( swapChain, size );
	}

	void ShowMessage( string text )
	{
		message = text;
		messageTime = 5;
	}

	/// <summary>
	/// Twice a second: the frame costs.
	/// </summary>
	void UpdateStats()
	{
		if ( stats is null || statsUpdated < 0.5f || timer.Count < 2 ) return;
		statsUpdated = 0;

		var summary = timer.Summarise();
		timer.Clear();

		var text = new System.Text.StringBuilder();
		if ( message is not null && !messageTime ) text.AppendLine( message ).AppendLine();

		text.AppendLine( $"Native{(VSync ? ", vsync" : "")}" );
		text.AppendLine( $"fps     {summary.Fps,8:0}" );
		text.AppendLine( $"frame   {summary.FrameMs,8:0.00} ms   p99 {summary.FrameP99Ms,6:0.00} ms" );
		text.AppendLine( $"render  {summary.RenderMs,8:0.000} ms   alloc {summary.AllocBytes,6:0} B" );
		text.Append( $"gpu     {(summary.GpuMs > 0 ? $"{summary.GpuMs,8:0.00} ms" : "     n/a")}" );

		// Native counts each material's draw of an object
		if ( Sandbox.Rendering.RenderPipeline.OcclusionCulling )
			text.AppendLine().AppendLine().Append( $"occluded{Sandbox.Rendering.RenderPipeline.GetOcclusionCulled(),8}   draws" );

		stats.Text = text.ToString();
	}

	/// <summary>
	/// Where captures and benchmark results go.
	/// </summary>
	public static string OutputFolder()
	{
		var folder = System.IO.Path.Combine( Environment.CurrentDirectory, "screenshots", "scenelab" );
		System.IO.Directory.CreateDirectory( folder );
		return folder;
	}

	private protected override void OnClosing()
	{
		// Let the GPU finish the frames in flight - they still read the buffers about to be freed
		Window?.Flush();

		UnloadGame();
	}
}
