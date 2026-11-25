using System.IO;
namespace Editor;

internal static class MapMenu
{
	public static Option PlayMapOption { get; private set; }

	[Event( "asset.contextmenu", Priority = 50 )]
	public static void OnMapFileAssetContext( AssetContextMenu e )
	{
		if ( e.SelectedList.Count != 1 )
			return;

		var entry = e.SelectedList[0];

		if ( entry == null ) return;
		if ( entry.AssetType != AssetType.MapFile ) return;

		bool isCompiled = File.Exists( Path.ChangeExtension( entry.Asset.AbsolutePath, ".vpk" ) );

		PlayMapOption = e.Menu.AddOption( "Play Map", "play_arrow", action: () => EditorScene.PlayMap( entry.Asset ) );
		PlayMapOption.Text = isCompiled ? "Play Map" : "Map is not compiled";
		PlayMapOption.Icon = isCompiled ? "play_arrow" : "error";

		if ( !isCompiled )
		{
			PlayMapOption.Enabled = false;
		}

		e.Menu.AddOption( "Convert to Scene...", "account_tree", action: () => ConvertToScene( entry.Asset ) );
	}

	static async void ConvertToScene( Asset source )
	{
		var assets = Project.Current.GetAssetsPath();
		var folder = assets;
		if ( !string.IsNullOrEmpty( source.AbsolutePath ) && Path.IsPathFullyQualified( source.AbsolutePath ) )
		{
			var relative = Path.GetRelativePath( assets, source.AbsolutePath );
			if ( !Path.IsPathRooted( relative ) && !relative.StartsWith( ".." + Path.DirectorySeparatorChar ) )
				folder = Path.GetDirectoryName( source.AbsolutePath );
		}

		var dialog = new FileDialog( null ) { Title = "Convert VMAP to Scene", Directory = folder };
		dialog.SetFindFile();
		dialog.SetModeSave();
		dialog.SetNameFilter( "Scene (*.scene)" );
		dialog.SelectFile( Path.Combine( folder, Path.ChangeExtension( Path.GetFileName( source.AbsolutePath ?? source.RelativePath ), ".scene" ) ) );
		if ( !dialog.Execute() ) return;
		try
		{
			var scene = await EditorUtility.ImportVmapAsync( source, dialog.SelectedFile );
			EditorScene.OpenScene( scene );
		}
		catch ( System.Exception exception )
		{
			Log.Error( exception, $"Could not convert {source.Path} to a scene." );
		}
	}
}
