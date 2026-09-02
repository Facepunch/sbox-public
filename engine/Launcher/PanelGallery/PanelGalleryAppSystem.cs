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
			var editor = new PanelWindow( "Panel Gallery", new Vector2( 1500, 940 ), new Vector2( -1, -1 ), true );
			editor.Root.AddChild( new EditorWindow( editor ) );
			_windows.Add( editor );
			return;
		}

		RegisterUiTests();

		// Borderless - the title bar in this one is panels, same as everything else
		var window = new PanelWindow( "Control Gallery", new Vector2( 1280, 860 ), new Vector2( -1, -1 ), true );
		window.Root.AddChild( new GalleryWindow( window ) );
		_windows.Add( window );
	}

	/// <summary>
	/// The renderer test pages compile into this assembly - the type library finds their
	/// stylesheet attributes, the mounted folder serves the scss.
	/// </summary>
	void RegisterUiTests()
	{
		var path = System.IO.Path.GetFullPath( System.IO.Path.Combine( Environment.CurrentDirectory, "..", "engine", "Launcher", "PanelGallery", "UiTests" ) );

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
