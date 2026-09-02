namespace Sandbox.PanelGallery;

/// <summary>
/// The control gallery - a list of controls down the left, a page of tests for the selected
/// one on the right. This is where UI rendering, styles and input get eyeballed.
/// </summary>
public class GalleryWindow : Panel
{
	readonly PanelWindow _window;
	readonly Dictionary<GalleryPageInfo, Panel> _navItems = new();

	Panel _sidebar;
	Panel _content;
	GalleryPageInfo _current;
	bool _lightMode;

	public GalleryWindow( PanelWindow window )
	{
		_window = window;

		AddClass( "editor-window gallery-window" );
		StyleSheet.Load( "/styles/editor.scss" );
		StyleSheet.Load( "/styles/controlgallery.scss" );

		BuildTitleBar();

		var body = Add.Panel( "gallery-body" );
		BuildSidebar( body );
		_content = body.Add.Panel( "gallery-content" );

		Open( StartPage() );
	}

	/// <summary>
	/// The page to open first. "-page Icons" jumps straight to a page, for iterating on it.
	/// </summary>
	static GalleryPageInfo StartPage()
	{
		if ( RequestedPage() is { } wanted )
		{
			var match = GalleryPageInfo.All.Concat( UiTestPages.Pages ).FirstOrDefault( x => x.Title.Contains( wanted, StringComparison.OrdinalIgnoreCase ) );
			if ( match is not null ) return match;
		}

		return GalleryPageInfo.All[0];
	}

	static string RequestedPage()
	{
		var args = Environment.GetCommandLineArgs();
		var index = Array.FindIndex( args, x => x.Equals( "-page", StringComparison.OrdinalIgnoreCase ) );

		return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
	}

	void BuildTitleBar()
	{
		var bar = Add.Panel( "titlebar window-drag" );

		bar.Add.Label( "Control Gallery", "gallery-title" );

		bar.Add.Panel( "grow" );

		WindowButton( bar, "contrast", null, ToggleTheme );
		WindowButton( bar, "remove", null, _window.Minimize );
		WindowButton( bar, "crop_square", null, _window.ToggleMaximized );
		WindowButton( bar, "close", "close", _window.Dispose );
	}

	static void WindowButton( Panel bar, string icon, string classname, Action onClick )
	{
		var button = bar.Add.Panel( "windowbutton window-nodrag" );
		if ( classname is not null ) button.AddClass( classname );

		button.Add.Icon( icon, "icon" );
		button.AddEventListener( "onclick", onClick );
	}

	void BuildSidebar( Panel body )
	{
		_sidebar = body.Add.Panel( "sidebar" );

		Section( "CONTROLS" );

		foreach ( var page in GalleryPageInfo.All )
			NavItem( page );

		if ( UiTestPages.Pages.Count > 0 )
		{
			Section( "RENDERING" );

			foreach ( var page in UiTestPages.Pages )
				NavItem( page );
		}
	}

	void Section( string title )
	{
		var label = _sidebar.Add.Panel( "sectionlabel" );
		label.Add.Label( title, "text" );
		label.Add.Panel( "rule" );
	}

	void NavItem( GalleryPageInfo page )
	{
		var current = page;

		var item = _sidebar.Add.Panel( "navitem" );
		item.Add.Icon( current.Icon, "icon" );
		item.Add.Label( current.Title, "label" );
		item.AddEventListener( "onclick", () => Open( current ) );
		item.SetClass( "active", current == _current );

		_navItems[current] = item;
	}

	void Open( GalleryPageInfo page )
	{
		_current = page;

		_content.DeleteChildren( true );
		_content.AddChild( page.Create() );

		foreach ( var (info, item) in _navItems )
			item.SetClass( "active", info == page );
	}

	void ToggleTheme()
	{
		_lightMode = !_lightMode;
		SetClass( "style-light", _lightMode );
	}
}
