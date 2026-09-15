namespace Sandbox.PanelGallery;

/// <summary>
/// The panel UI system running as its own app - real OS windows, no editor, no Qt. This is where
/// controls get built and proven before they're trusted anywhere else.
/// </summary>
public class PanelGalleryAppSystem : PanelAppSystem
{
	readonly List<PanelWindow> _windows = new();
	int _captureFrame;
	readonly int _captureAtFrame = Math.Max( 1, IntArg( "-capture-frame", 30 ) );

	protected override void OnInitialized()
	{
		// Collect log output from startup, so the console has history when it's opened
		ConsolePanel.Hook();

		if ( Environment.GetCommandLineArgs().Any( x => x.Equals( "-simple", StringComparison.OrdinalIgnoreCase ) ) )
		{
			OpenToolWindow( "Entities", ["Player", "Camera", "Sun Light", "Ambient", "Terrain"], new Vector2( 200, 160 ) );
			OpenToolWindow( "Assets", ["citizen.vmdl", "wood.vmat", "gunshot.vsnd", "level.vmap"], new Vector2( 700, 240 ) );
			return;
		}

		// The old default - the mock editor straight up, no gallery around it
		if ( Environment.GetCommandLineArgs().Any( x => x.Equals( "-editor", StringComparison.OrdinalIgnoreCase ) ) )
		{
			_windows.Add( EditorWindow.Open() );
			return;
		}

		RegisterUiTests();

		// Borderless - the title bar in this one is panels, same as everything else. "-width 1600 -height 1400"
		// overrides the size, for screenshotting a whole page at once.
		var window = new PanelWindow( "Panel Gallery", new Vector2( IntArg( "-width", 1280 ), IntArg( "-height", 860 ) ), new Vector2( -1, -1 ), true );
		window.Root.AddChild( new GalleryWindow( window ) );
		if ( !Environment.GetCommandLineArgs().Any( x => x.Equals( "-width", StringComparison.OrdinalIgnoreCase ) || x.Equals( "-height", StringComparison.OrdinalIgnoreCase ) ) )
		{
			window.Maximize();
		}
		_windows.Add( window );
	}

	/// <summary>
	/// Captures the GPU with "-capture path.png" at frame 30, or the frame selected by "-capture-frame".
	/// </summary>
	protected override bool RunFrame()
	{
		var running = base.RunFrame();
		if ( !running || ++_captureFrame != _captureAtFrame ) return running;

		var args = Environment.GetCommandLineArgs();
		var index = Array.FindIndex( args, x => x.Equals( "-capture", StringComparison.OrdinalIgnoreCase ) );
		if ( index < 0 || index + 1 >= args.Length ) return running;

		var surface = _windows.FirstOrDefault( x => x.IsOpen )?.Surface;
		if ( surface is null ) return running;
		var world = new SceneWorld();
		try
		{
			using var camera = new SceneCamera( "Gallery Capture" )
			{
				World = world,
				BackgroundColor = Color.Black,
				ClearFlags = ClearFlags.All,
				EnablePostProcessing = false,
				OnRenderUI = surface.Render,
			};
			using var bitmap = new Bitmap( (int)surface.Size.x, (int)surface.Size.y );
			camera.RenderToBitmap( bitmap );
			System.IO.File.WriteAllBytes( args[index + 1], bitmap.ToPng() );
		}
		finally
		{
			world.Delete();
		}
		return running;
	}

	static int IntArg( string name, int fallback )
	{
		var args = Environment.GetCommandLineArgs();
		var index = Array.FindIndex( args, x => x.Equals( name, StringComparison.OrdinalIgnoreCase ) );
		return index >= 0 && index + 1 < args.Length && int.TryParse( args[index + 1], out var value ) ? value : fallback;
	}

	/// <summary>
	/// The renderer test pages compile into this assembly - the type library finds their
	/// stylesheet attributes, the mounted folder serves the scss the build copied there.
	/// </summary>
	void RegisterUiTests()
	{
		var path = System.IO.Path.Combine( Environment.CurrentDirectory, "addons", "editor", "assets", "uitests" );

		RegisterCompiledPanelCode( typeof( PanelGalleryAppSystem ).Assembly, path );
		UiTestPages.Register( typeof( PanelGalleryAppSystem ).Assembly );
	}

	void OpenToolWindow( string heading, string[] items, Vector2 position )
	{
		var window = new PanelWindow( $"Panel Gallery - {heading}", new Vector2( 760, 520 ), position )
		{
			BackgroundColor = Color.Black,
		};

		window.Root.AddChild( new ToolWindow( heading, items ) );
		_windows.Add( window );
	}
}
