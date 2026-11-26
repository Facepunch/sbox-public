using Sandbox.UI;

namespace Sandbox;

internal static class SpawnMenuTabs
{
	internal static void PopulateTabs<T>( this TabPanel tabs, string cookieKey )
	{
		if ( !tabs.IsValid() || tabs.Body.ChildrenCount > 0 ) return;

		var bar = tabs.TabBar;
		bar.AutoHideText = false;

		var pageTypes = Game.TypeLibrary.GetTypes<T>()
			.Where( type => !type.IsAbstract && type.TargetType != typeof( BaseSpawnMenu ) )
			.OrderBy( type => type.Order )
			.ThenBy( type => LocalizedText.Resolve( type.Title ), StringComparer.CurrentCultureIgnoreCase );

		foreach ( var type in pageTypes )
		{
			var page = type.Create<Panel>();
			var tab = tabs.AddTab( type.Title, page, type.Icon );
			tab.UserData = page;

			if ( page is UtilityTab )
			{
				tab.Text = null;
				tab.Tooltip = type.Title;
				tab.Style.ColumnGap = 0;
			}
			else
			{
				tab.Tooltip = null;
			}

			if ( page is BaseSpawnMenu spawnPage )
				spawnPage.MenuTab = tab;
		}

		var savedPage = Game.Cookies.Get<string>( cookieKey, null );
		var selected = bar.Tabs.FirstOrDefault( tab => tab.UserData.GetType().Name == savedPage ) ?? bar.SelectedTab;
		if ( selected is null ) return;

		void SaveSelection( Tab tab ) => Game.Cookies.Set( cookieKey, tab?.UserData?.GetType().Name );

		bar.SelectTab( selected );
		SaveSelection( selected );
		bar.SelectionChanged += SaveSelection;
	}
}
