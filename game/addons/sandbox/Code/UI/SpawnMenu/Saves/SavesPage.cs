using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// The host's local saves and workshop saves for the current map.
/// </summary>
[Title( "#spawnmenu.tab.saves" ), Order( 3100 ), Icon( "save" )]
public class SavesPage : BaseSpawnMenu
{
	protected override void Rebuild()
	{
		AddOption( "folder", "#spawnmenu.saves.local", () => new SaveMenu() );

		AddHeader( "#spawnmenu.section.workshop", "public" );
		AddOption( "apps", "#spawnmenu.entity.all", () => new WorkshopBrowser() );
		var myUploads = AddOption( "cloud_upload", "#spawnmenu.saves.my_uploads", () => new WorkshopBrowser { MyUploads = true } );
		AddCategoryOptions<SaveCategory>( category => new WorkshopBrowser { MyUploads = true, Category = category }, myUploads );

		AddCategoryOptions<SaveCategory>( category => new WorkshopBrowser { Category = category } );
	}

	internal static async Task Load( Panel source, Func<SaveSystem, Task<bool>> load )
	{
		if ( !Networking.IsHost || !source.IsValid() || SaveSystem.Current is not { IsLoadingSave: false } system ) return;

		if ( await load( system ) || !source.IsValid() || !Networking.IsHost ) return;

		Notices.AddNotice( "error", Game.Language.GetPhrase( "spawnmenu.saves.load_failed" ) );
	}
}
