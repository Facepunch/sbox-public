using System.Diagnostics;

namespace Sandbox.PanelGallery;

/// <summary>
/// The control gallery - categories and pages in two columns down the left, a page of tests for the selected
/// one on the right. This is where UI rendering, styles and input get eyeballed.
/// </summary>
public class GalleryWindow : Panel
{
	readonly PanelWindow _window;
	readonly CookieContainer _cookies = new( "panelgallery", noexpire: true );
	readonly List<GalleryPageInfo> _pages = [];
	readonly Dictionary<string, Sandbox.UI.Button> _categoryButtons = [];
	readonly Dictionary<GalleryPageInfo, Sandbox.UI.Button> _pageButtons = [];

	Panel _pageList;
	string _category;
	Panel _content;
	GalleryPageInfo _current;
	bool _lightMode;
	Sandbox.UI.Label _frameRate;
	long _frameSampleStart;
	long _frameSampleCount;

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
	/// Updates this root's render rate and CPU statistics twice a second.
	/// </summary>
	public override void Tick()
	{
		base.Tick();

		var stats = _window.Root.Stats;
		var frames = stats.RenderCount;
		if ( frames == 0 ) return;
		var now = Stopwatch.GetTimestamp();
		if ( _frameSampleStart == 0 )
		{
			_frameSampleStart = now;
			_frameSampleCount = frames;
			return;
		}

		double seconds = Stopwatch.GetElapsedTime( _frameSampleStart, now ).TotalSeconds;
		if ( seconds < 0.5 ) return;
		long count = frames - _frameSampleCount;
		_frameRate.Text = $"{count / seconds:0} FPS · CPU {stats.CpuTime.TotalMilliseconds:0.00} ms · {stats.DrawCalls} draws · {stats.Instances} instances";
		_frameRate.Tooltip = "Root renders per second; CPU values are from the last render.\n" +
			$"Layout: {stats.LayoutTime.TotalMilliseconds:0.00} ms\n" +
			$"Command build: {stats.CommandBuildTime.TotalMilliseconds:0.00} ms\n" +
			$"Submission: {stats.SubmissionTime.TotalMilliseconds:0.00} ms\n" +
			$"Panels: {stats.PanelsRendered} rendered, {stats.PanelsCulled} culled subtrees\n" +
			$"Compositing: {stats.Layers} CSS layers, {stats.FrameGrabs} frame grabs";
		_frameSampleStart = now;
		_frameSampleCount = frames;
	}

	/// <summary>
	/// The page to open first. "-page Icons" jumps straight to a page, for iterating on it.
	/// </summary>
	GalleryPageInfo StartPage()
	{
		if ( (RequestedPage() ?? _cookies.GetString( "last-page", null )) is { } wanted )
		{
			var match = _pages.FirstOrDefault( x => $"{x.Folder}/{x.Title}".Equals( wanted, StringComparison.OrdinalIgnoreCase ) )
				?? _pages.FirstOrDefault( x => x.Title.Equals( wanted, StringComparison.OrdinalIgnoreCase ) )
				?? _pages.FirstOrDefault( x => x.Title.Contains( wanted, StringComparison.OrdinalIgnoreCase ) );
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

		bar.Add.Label( "Panel Gallery", "gallery-title" );

		bar.Add.Panel( "grow" );
		_frameRate = bar.Add.Label( "-- FPS", "gallery-frame-rate" );
		_frameRate.Tooltip = "Render rate and CPU work for this gallery root. Hover for timings, cache reuse and compositing counts.";

		WindowButton( bar, "contrast", null, ToggleTheme );
		WindowButton( bar, "remove", null, _window.Minimize );
		WindowButton( bar, "crop_square", null, _window.ToggleMaximized );
		if ( _window.CanClose )
			WindowButton( bar, "close", "close", _window.RequestClose );
	}

	static void WindowButton( Panel bar, string icon, string classname, Action onClick )
	{
		var button = bar.Add.Panel( "windowbutton window-nodrag" );
		if ( classname is not null ) button.AddClass( classname );

		button.Add.Icon( icon, "icon" );
		button.AddEventListener( "onclick", onClick );
	}


	static string Category( GalleryPageInfo page ) =>
		page.Folder.Split( '/', StringSplitOptions.RemoveEmptyEntries ).FirstOrDefault() ?? "Css Styles";

	static int CategoryOrder( string category ) => category switch
	{
		"Controls" => 0,
		"Css Styles" => 1,
		"Demos" => 2,
		"Painter" => 3,
		"Painter Shapes" => 4,
		"System" => 5,
		"WorldPanel" => 6,
		"Scene" => 7,
		_ => int.MaxValue
	};

	void BuildSidebar( Panel body )
	{
		_pages.AddRange( GalleryPageInfo.All
			.Concat( UiTestPages.Pages )
			.Concat( FilterPage.Entries() )
			.Concat( WorldPanelPage.Pages )
			.Concat( CameraPainterPage.Pages )
			.OrderBy( page => CategoryOrder( Category( page ) ) )
			.ThenBy( Category, StringComparer.OrdinalIgnoreCase )
			.ThenBy( page => page.Title, StringComparer.OrdinalIgnoreCase ) );

		var sidebar = body.Add.Panel( "sidebar gallery-categories" );
		var categories = sidebar.Add.Panel( "nav" );
		foreach ( var category in _pages.Select( Category ).Distinct() )
		{
			var button = new Sandbox.UI.Button( category, CategoryIcon( category ), "navitem",
				() => Open( _pages.First( page => Category( page ) == category ) ) );
			categories.AddChild( button );
			_categoryButtons.Add( category, button );
		}

		var pages = body.Add.Panel( "sidebar gallery-pages" );
		_pageList = pages.Add.Panel( "nav" );
	}

	static string CategoryIcon( string title ) => title switch
	{
		"Controls" => "widgets",
		"System" => "settings",
		"Demos" => "play_circle",
		"Painter" => "brush",
		"Painter Shapes" => "category",
		"Css Styles" => "science",
		"WorldPanel" => "view_in_ar",
		"Scene" => "public",
		_ => "folder"
	};

	void SelectCategory( string category )
	{
		if ( _category == category ) return;
		_category = category;
		foreach ( var (name, button) in _categoryButtons )
			button.Active = name == category;

		_pageList.DeleteChildren( true );
		_pageList.ScrollOffset = Vector2.Zero;
		_pageButtons.Clear();
		foreach ( var page in _pages.Where( page => Category( page ) == category ) )
		{
			var button = new Sandbox.UI.Button( page.Title, page.Icon, "navitem", () => Open( page ) );
			_pageList.AddChild( button );
			_pageButtons.Add( page, button );
		}
	}

	void Open( GalleryPageInfo page )
	{
		if ( _current == page ) return;
		_current = page;
		SelectCategory( Category( page ) );
		_cookies.SetString( "last-page", $"{page.Folder}/{page.Title}" );
		_cookies.Save();
		_content.DeleteChildren( true );
		var content = page.Create();
		_content.SetClass( "full-page", content.HasClass( "full-page" ) );
		_content.AddChild( content );

		foreach ( var (entry, button) in _pageButtons )
			button.Active = entry == page;
	}

	public override void OnDeleted()
	{
		_cookies.Dispose();
		base.OnDeleted();
	}

	void ToggleTheme()
	{
		_lightMode = !_lightMode;
		SetClass( "style-light", _lightMode );
	}
}
