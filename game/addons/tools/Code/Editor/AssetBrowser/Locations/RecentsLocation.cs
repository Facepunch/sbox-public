using System.IO;

namespace Editor;

public record RecentsLocation : LocalAssetBrowser.Location
{
	public override bool IsAggregate => true;

	public RecentsLocation() : base( "Recents", "History" )
	{
		Path = "@recents";
	}

	public override bool CanGoUp() => false;
	public override IEnumerable<LocalAssetBrowser.Location> GetDirectories() => Enumerable.Empty<LocalAssetBrowser.Location>();

	public override IEnumerable<FileInfo> GetFiles()
	{
		var menuProject = EditorUtility.Projects.GetAll().FirstOrDefault( x => x.Config.Ident == "menu" );
		string menuPath = menuProject?.GetAssetsPath().NormalizeFilename( false );

		foreach ( var asset in AssetSystem.All.OrderByDescending( x => x.LastOpened ).Take( 50 ) )
		{
			if ( menuPath is not null && menuProject != Project.Current )
			{
				bool isMenu = asset.AbsolutePath.StartsWith( menuPath );
				if ( isMenu ) continue;
			}

			yield return new FileInfo( asset.AbsolutePath );
		}
	}
}
