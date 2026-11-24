using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// The tabbed panel shared by the spawn and context menus.
/// </summary>
public sealed class ToolsMenu : TabPanel
{
	readonly Button _searchToggle;
	readonly Button _themeToggle;
	readonly Button _pinToggle;
	readonly Tab _savesTab;
	ToolMenuSearch _search;
	InterfaceTheme _theme;

	/// <summary>
	/// Builds the registered tool pages and their tab bar controls.
	/// </summary>
	public ToolsMenu()
	{
		AddClass( "spawnmenuright" );
		BuildTabs();
		_savesTab = TabBar.Tabs.FirstOrDefault( tab => tab.UserData is SaveMenu );
		UpdateSavesVisibility();

		_searchToggle = TabBar.AddChild( new Button( null, ToggleSearch ) );
		_searchToggle.AddClass( "search-toggle" );
		_searchToggle.Icon = "search";
		_searchToggle.Tooltip = "#spawnmenu.common.search";
		TabBar.SelectionChanged += OnTabSelected;
		OnTabSelected( TabBar.SelectedTab );

		_themeToggle = TabBar.AddChild( new Button( null, ToggleTheme ) );
		_themeToggle.AddClass( "theme-toggle" );
		UpdateThemeButton();

		_pinToggle = TabBar.AddChild( new Button( null, "push_pin", () => Scene?.Get<SpawnMenuHost>()?.TogglePin() ) );
		_pinToggle.AddClass( "pin-toggle" );
	}

	internal void SetPinned( bool pinned )
	{
		_pinToggle.SetClass( "selected", pinned );
		_pinToggle.Tooltip = pinned ? "#spawnmenu.common.unpin" : "#spawnmenu.common.pin";
	}

	/// <summary>
	/// Adds the tool and utility tabs registered by the game and addons.
	/// </summary>
	public void BuildTabs() => this.PopulateTabs<IUtilityTab>( "spawnmenu.utilitytab" );

	/// <summary>
	/// Keeps the theme button and host-only Saves tab up to date.
	/// </summary>
	public override void Tick()
	{
		base.Tick();
		UpdateSavesVisibility();

		if ( _theme != GamePreferences.Theme )
			UpdateThemeButton();
	}

	void UpdateThemeButton()
	{
		_theme = GamePreferences.Theme;
		var isDark = _theme == InterfaceTheme.Dark;
		_themeToggle.Icon = isDark ? "light_mode" : "dark_mode";
		_themeToggle.Tooltip = isDark ? "#spawnmenu.utility.theme.switch_light" : "#spawnmenu.utility.theme.switch_dark";
	}

	void UpdateSavesVisibility()
	{
		if ( !_savesTab.IsValid() ) return;

		var display = Networking.IsHost ? DisplayMode.Flex : DisplayMode.None;
		if ( _savesTab.Style.Display != display )
			_savesTab.Style.Display = display;

		if ( display == DisplayMode.None && TabBar.SelectedTab == _savesTab )
			TabBar.SelectTab( TabBar.Tabs.First( tab => tab != _savesTab ) );
	}

	static void ToggleTheme() => GamePreferences.SetTheme( GamePreferences.Theme == InterfaceTheme.Dark ? InterfaceTheme.Light : InterfaceTheme.Dark );

	void OnTabSelected( Tab tab )
	{
		CloseSearch();
		SetClass( "hide-search", tab?.UserData is EffectsHost or SaveMenu );
	}

	void ToggleSearch()
	{
		if ( _search.IsValid() )
		{
			CloseSearch();
			return;
		}

		if ( TabBar.SelectedTab?.UserData is not Panel page ) return;
		var sidebar = page.Children.OfType<UIGroupedList>().FirstOrDefault();
		if ( sidebar is null ) return;

		_search = AddChild( new ToolMenuSearch { Sidebar = sidebar } );
		SetChildIndex( _search, Body.SiblingIndex );
		_searchToggle.AddClass( "selected" );
	}

	void CloseSearch()
	{
		if ( !_search.IsValid() ) return;

		_search.Sidebar.Filter = null;
		_search.Delete( true );
		_search = null;
		_searchToggle.RemoveClass( "selected" );
	}
}
