using System.IO;

namespace Editor;

public record EverythingLocation : LocalAssetBrowser.Location
{
	public override bool IsAggregate => true;

	public EverythingLocation() : base( "Everything", "Public" )
	{
		Path = "@everything";
	}

	public override bool CanGoUp() => false;
	public override IEnumerable<LocalAssetBrowser.Location> GetDirectories() => Enumerable.Empty<LocalAssetBrowser.Location>();

	public override IEnumerable<FileInfo> GetFiles()
	{
		string projectPath = Project.Current.GetAssetsPath().NormalizeFilename( false );

		var menuProject = EditorUtility.Projects.GetAll().FirstOrDefault( x => x.Config.Ident == "menu" );
		string menuPath = menuProject?.GetAssetsPath().NormalizeFilename( false );

		foreach ( var asset in AssetSystem.All.OrderBy( x => x.Name ) )
		{
			bool isCloud = asset.AbsolutePath.Contains( ".sbox/cloud/" );
			if ( isCloud ) continue;

			if ( menuPath is not null && menuProject != Project.Current )
			{
				bool isMenu = asset.AbsolutePath.StartsWith( menuPath );
				if ( isMenu ) continue;
			}

			yield return new FileInfo( asset.AbsolutePath );
		}
	}
}
