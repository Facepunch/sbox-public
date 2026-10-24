using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// A page of the dev tools.
/// </summary>
internal abstract class DevToolsTab : Panel
{
	/// <summary>
	/// The name <c>devtools &lt;tab&gt;</c> and links use.
	/// </summary>
	public abstract string TabId { get; }

	public abstract string Title { get; }

	/// <summary>
	/// Developer detail rather than something a player needs. Hidden unless <see cref="DevTools.ShowAdvanced"/>.
	/// </summary>
	public virtual bool Advanced => true;

	/// <summary>
	/// Whether this tab can be shown right now.
	/// </summary>
	public virtual bool Available => !Advanced || DevTools.ShowAdvanced;

	/// <summary>
	/// Takes the page's whole height and scrolls itself - a log or a list - instead of scrolling with the page.
	/// </summary>
	public virtual bool FillsPage => false;

	protected DevToolsTab()
	{
		AddClass( "page" );
		SetClass( "fills", FillsPage );
	}

	/// <summary>
	/// The analysis moved on. Called a few times a second while this tab is showing.
	/// </summary>
	public abstract void Refresh( DevTools.Summary summary );
}

/// <summary>
/// The dev tools' shell: a strip with the tabs and the buttons, then the page. The dev tools' window is borderless, so
/// there the strip is its title bar and drags it.
/// </summary>
internal sealed class DevToolsWindow : Panel
{
	readonly Panel _strip;
	readonly Label _title;
	readonly Label _maximize;
	readonly Label _visualization;
	readonly Label _pause;
	readonly Panel _tabBar;
	readonly Panel _badges;
	readonly Label _errors, _warnings;
	readonly Panel _pageScroll;
	readonly List<(DevToolsTab Tab, Label Button)> _tabs = new();
	readonly Dictionary<DevToolsTab, float> _scroll = new();
	DevToolsTab _shown;
	DevTools.Summary _refreshedWith;
	DevTools.Summary _badgedWith;

	/// <param name="host">What it's drawn in. Only a window of its own gets window buttons and a strip that drags it.</param>
	public DevToolsWindow( DevTools.Host host )
	{
		AddClass( "devtools" );

		var window = host == DevTools.Host.Window;

		_strip = AddChild( new Panel( null, window ? "strip window-drag" : "strip" ) );
		_title = _strip.AddChild( new Label( "Dev Tools", "title" ) );
		_tabBar = _strip.AddChild( new Panel( null, "tabs" ) );

		// Counts of what's out of line, the way a browser's dev tools count errors and warnings
		_badges = _strip.AddChild( new Panel( null, "badges window-nodrag" ) );
		_errors = Badge( "error", "bad" );
		_warnings = Badge( "warning", "warning" );
		_badges.AddEventListener( "onclick", () => DevTools.ActiveTab = DevTools.OverviewTabId );

		var controls = _strip.AddChild( new Panel( null, "host-controls" ) );

		// Picking a frame pauses; this is the way back
		_pause = Button( controls, "pause", null, () => DevTools.Paused = !DevTools.Paused );

		// Menus open upward from the game dock, over the game, where there's room for them
		var menuPosition = host == DevTools.Host.GameDock ? Popup.PositionMode.AboveRight : Popup.PositionMode.BelowRight;
		_visualization = Button( controls, "visibility", null, null );
		_visualization.AddEventListener( "onclick", () => DevToolsMenus.OpenVisualization( _visualization, menuPosition ) );
		var settings = Button( controls, "settings", "Settings", null );
		settings.AddEventListener( "onclick", () => DevToolsMenus.OpenSettings( settings, menuPosition ) );

		if ( host == DevTools.Host.GameDock )
		{
			controls.AddChild( new Panel( null, "divider" ) );
			Button( controls, "open_in_new", "Pop out into a window", () => DevTools.SetPoppedOut( true ) );
			Button( controls, "close", "Close (F11)", DevTools.Close );
		}
		else if ( window )
		{
			controls.AddChild( new Panel( null, "divider" ) );
			Button( controls, "call_to_action", "Dock in the game window", () => DevTools.SetPoppedOut( false ) );
			Button( controls, "remove", "Minimize", () => Window?.Minimize() );
			_maximize = Button( controls, "crop_square", "Maximize", () => Window?.ToggleMaximized() );
			Button( controls, "close", "Close (F11)", DevTools.Close ).AddClass( "close" );
		}

		var body = AddChild( new Panel( null, "body" ) );
		var workspace = body.AddChild( new Panel( null, "workspace" ) );
		_pageScroll = workspace.AddChild( new Panel( null, "page-scroll" ) );

		AddTab( new DevToolsConsoleTab() );
		AddTab( new DevToolsGlanceTab() );
		AddTab( new DevToolsCpuTab() );
		AddTab( new DevToolsGpuTab() );
		AddTab( new DevToolsMemoryTab() );
		AddTab( new DevToolsRenderingTab() );
		AddTab( new DevToolsNetworkTab() );
		AddTab( new DevToolsUiTab() );
		AddTab( new DevToolsAudioTab() );
	}

	/// <summary>
	/// The OS window the dev tools is in.
	/// </summary>
	SurfaceWindow Window => SurfaceWindow.FromPanel( this );

	static Label Button( Panel parent, string icon, string tooltip, Action onClick )
	{
		var button = parent.AddChild( new Label( icon, "icon-button window-nodrag" ) );
		button.Tooltip = tooltip;
		if ( onClick is not null ) button.AddEventListener( "onclick", onClick );
		return button;
	}

	Label Badge( string icon, string severity )
	{
		var badge = _badges.AddChild( new Panel( null, $"badge {severity}" ) );
		badge.AddChild( new Label( icon, "badge-icon" ) );
		return badge.AddChild( new Label( "0", "badge-count" ) );
	}

	void AddTab( DevToolsTab tab )
	{
		var button = _tabBar.AddChild( new Label( tab.Title, "tab window-nodrag" ) );
		button.AddEventListener( "onclick", () => DevTools.ActiveTab = tab.TabId );

		tab.AddClass( "hidden" );
		_pageScroll.AddChild( tab );
		_tabs.Add( (tab, button) );
	}

	public override void Tick()
	{
		base.Tick();

		var advanced = DevTools.ShowAdvanced;

		// Width classes: grids rearrange themselves to fit
		var width = Box.Rect.Width / ScaleToScreen;
		SetClass( "narrow", width > 0 && width < 600 );
		SetClass( "wide", width >= 1000 );
		SetClass( "player", !advanced );

		if ( _maximize is not null ) _maximize.Text = Window?.IsMaximized == true ? "filter_none" : "crop_square";

		var paused = DevTools.Paused;
		_pause.Text = paused ? "play_arrow" : "pause";
		_pause.SetClass( "active", paused );
		_pause.Tooltip = paused ? $"Paused at {DevTools.PausedAt:HH:mm:ss}. Resume" : "Hold everything the dev tools shows. Collection carries on underneath.";

		var mode = DevToolsMenus.VisualizationMode;
		_visualization.SetClass( "active", mode != SceneCameraDebugMode.Normal );
		_visualization.Tooltip = $"Tools visualization: {DevToolsMenus.VisualizationTitle( mode )}";

		UpdateBadges();
		UpdateTabs( advanced );
	}

	void UpdateBadges()
	{
		var s = DevTools.Current;
		if ( s == _badgedWith ) return;
		_badgedWith = s;

		var errors = s.CountIssues( DevTools.Severity.Bad );
		var warnings = s.CountIssues( DevTools.Severity.Warning );
		_errors.Text = $"{errors}";
		_warnings.Text = $"{warnings}";
		_errors.Parent.SetClass( "hidden", errors == 0 );
		_warnings.Parent.SetClass( "hidden", warnings == 0 );
		_badges.Tooltip = string.Join( "\n", s.Issues.Select( x => $"{x.Title}: {x.Detail}" ) );
	}

	internal static string LimitTooltip( DevTools.Summary s ) => s.Limiter switch
	{
		DevTools.Limiter.Capped => $"Waiting on {s.TargetSource}, {s.CapWaitAvg:0.00} ms a frame spare",
		DevTools.Limiter.VSync => "Frames land on the display's refresh",
		DevTools.Limiter.Gpu => "The GPU takes about as long as the whole frame",
		DevTools.Limiter.Cpu => "The GPU has slack, so the frame's CPU work is what takes the time",
		_ => null,
	};

	void UpdateTabs( bool advanced )
	{
		DevToolsTab active = null;

		foreach ( var (tab, button) in _tabs )
		{
			var available = tab.Available;
			button.SetClass( "hidden", !available );

			if ( available && string.Equals( tab.TabId, DevTools.ActiveTab, StringComparison.OrdinalIgnoreCase ) )
				active = tab;
		}

		// An unknown or unavailable tab falls back to the overview
		if ( active is null )
		{
			active = _tabs.First( x => x.Tab.TabId == DevTools.OverviewTabId || !x.Tab.Advanced ).Tab;
			DevTools.ActiveTab = active.TabId;
		}

		// Players get the overview alone - a title rather than a one-item tab bar
		_tabBar.SetClass( "hidden", !advanced );
		_title.SetClass( "hidden", advanced );

		if ( active != _shown )
		{
			// Each tab keeps its own scroll position
			if ( _shown is not null )
			{
				_scroll[_shown] = _pageScroll.ScrollOffset.y;
				_shown.AddClass( "hidden" );
			}

			active.RemoveClass( "hidden" );
			_pageScroll.SetClass( "fill", active.FillsPage );
			_pageScroll.ScrollOffset = new Vector2( 0, _scroll.GetValueOrDefault( active ) );
			_shown = active;
			_refreshedWith = null;
		}

		foreach ( var (tab, button) in _tabs )
			button.SetClass( "active", tab == active );

		var summary = DevTools.Current;
		if ( summary != _refreshedWith )
		{
			_refreshedWith = summary;
			active.Refresh( summary );
		}
	}
}
