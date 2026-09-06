namespace Sandbox.PanelGallery;

/// <summary>
/// The panel UI system running as its own app - real OS windows, no editor, no Qt. This is where
/// controls get built and proven before they're trusted anywhere else.
/// </summary>
public class PanelGalleryAppSystem : PanelAppSystem
{
	readonly List<PanelWindow> _windows = new();

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
