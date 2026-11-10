
namespace Editor;

public partial class CloudAssetBrowser : Widget
{
	/// <summary>
	/// Wire up the ViewMode button's click handler. Called once after FilterBar is created.
	/// OrderMode's handler is wired per-result inside FetchPackages (it depends on server data).
	/// </summary>
	void ConfigureViewMode()
	{
		ViewMode.MouseLeftPress = () =>
		{
			var menu = new ContextMenu( this );

			menu.AddOption( "List View", "view_headline", () => ViewModeType = AssetListViewMode.List );
			menu.AddOption( "Small Icons", "apps", () => ViewModeType = AssetListViewMode.SmallIcons );
			menu.AddOption( "Medium Icons", "grid_on", () => ViewModeType = AssetListViewMode.MediumIcons );
			menu.AddOption( "Large Icons", "grid_view", () => ViewModeType = AssetListViewMode.LargeIcons );

			menu.OpenAt( ViewMode.ScreenRect.BottomLeft, false );
		};
	}
}
