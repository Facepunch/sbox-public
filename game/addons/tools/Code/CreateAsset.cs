using System.IO;

namespace Editor;

public static class CreateAsset
{
	public struct Entry
	{
		public string Name { get; init; }
		public string Icon { get; init; }
		public Pixmap IconImage { get; init; }
		public string Category { get; init; }

		// Should these show at the top outside of category views?
		public bool Pinned { get; init; }

		public Action<DirectoryInfo> Action { get; init; }
		public string Default { get; init; }


		public void Execute( DirectoryInfo folder )
		{
			if ( Action is not null )
			{
				Action( folder );
				return;
			}

			if ( !string.IsNullOrEmpty( Default ) )
			{
				CreateFromTemplate( Name, Default, folder );
			}
		}
	}

	static CreateAsset()
	{

	}

	internal static void AddOptions( Menu parent, LocalAssetBrowser.Location location )
	{
		var folder = new DirectoryInfo( location.Path );

		parent.AddOption( "Folder", "folder", () => Dialog.AskStringFolder( ( string foldername ) =>
		{
			folder.CreateSubdirectory( foldername );
			EditorEvent.Run( "assetsystem.newfolder" );
		}, "New folder name..", "Create Folder" ) );

		parent.AddSeparator();

		var gameResources = EditorTypeLibrary.GetAttributes<AssetTypeAttribute>().Select(
			x => new Entry()
			{
				Name = x.Name,
				Category = x.Category,
				IconImage = AssetType.FromType( x.TargetType )?.Icon64,
				Action = ( DirectoryInfo d ) => CreateGameResource( x, d ),
				Pinned = x.Extension == "sound" || x.Extension == "prefab" || x.Extension == "scene"
			}
		);

		var entries = new List<Entry>();
		if ( location.Type is LocalAssetBrowser.LocationType.Localization )
		{
			entries.Add( new Entry { Name = "Empty JSON File", Icon = "data_object", Default = "localization.json", Pinned = true } );
		}
		else if ( location.Type is LocalAssetBrowser.LocationType.Code )
		{
			entries.Add( new Entry { Name = "Empty C# File", Icon = "description", Default = "default.cs", Category = "Code", Pinned = true } );
			entries.Add( new Entry { Name = "Component", Icon = "sports_esports", Default = "component.cs", Category = "Code", Pinned = true } );
			entries.Add( new Entry { Name = "Panel Component", Icon = "desktop_windows", Default = "default.razor", Category = "Razor" } );
			entries.Add( new Entry { Name = "Style Sheet", Icon = "brush", Default = "default.scss", Category = "Razor" } );
		}
		else if ( location.Type is LocalAssetBrowser.LocationType.Assets )
		{
			entries.Add( new Entry { Name = "Material", IconImage = AssetType.FromType( typeof( Material ) )?.Icon64, Default = "default.vmat", Category = "Rendering", Pinned = true } );
			entries.Add( new Entry { Name = "Model", IconImage = AssetType.FromType( typeof( Model ) )?.Icon64, Default = "default.vmdl", Category = "Rendering", Pinned = true } );
			entries.Add( new Entry { Name = "Map", Icon = "hardware", Default = "default.vmap", Category = "World" } );

			entries.Add( new Entry { Name = "Standard Material Shader", Icon = "brush", Default = "material.shader", Category = "Shader" } );
			entries.Add( new Entry { Name = "Unlit Shader", Icon = "brush", Default = "unlit.shader", Category = "Shader" } );
			entries.Add( new Entry { Name = "Compute Shader", Icon = "brush", Default = "compute.shader", Category = "Shader" } );
			entries.Add( new Entry { Name = "Shader Graph", Icon = "account_tree", Default = "default.shdrgrph", Category = "Shader" } );
			entries.Add( new Entry { Name = "Shader Graph Function", Icon = "account_tree", Default = "subgraph.shdrfunc", Category = "Shader" } );

			entries.AddRange( gameResources );
		}

		foreach ( var entry in entries.Where( x => x.Pinned ).OrderBy( x => x.Name ) )
		{
			if ( entry.IconImage != null ) parent.AddOptionWithImage( entry.Name, entry.IconImage, () => entry.Execute( folder ) );
			else parent.AddOption( entry.Name, entry.Icon, () => entry.Execute( folder ) );
		}

		parent.AddSeparator();

		var grouped = entries.OrderBy( x => x.Name ).GroupBy( x => x.Category ).OrderBy( x => x.Key );
		foreach ( var group in grouped.Where( x => x.Key is not null ) )
		{
			var menu = parent.FindOrCreateMenu( group.Key );

			foreach ( var entry in group )
			{
				if ( entry.IconImage != null ) menu.AddOptionWithImage( entry.Name, entry.IconImage, () => entry.Execute( folder ) );
				else menu.AddOption( entry.Name, entry.Icon, () => entry.Execute( folder ) );
			}
		}
	}

	static string GetNewFilename( DirectoryInfo folder, string typeName, string extension )
	{
		typeName = typeName.ToLower();
		string destName = $"new {typeName}{extension}";

		int i = 1;
		while ( File.Exists( Path.Combine( folder.FullName, destName ) ) )
		{
			destName = $"new {typeName} {i++}{extension}";
		}

		return destName;
	}

	static void OpenCreateAssetFlyout( string resourceType, string defaultName, string extension, Action<string> onCreate )
	{
		AssetList.OpenCreateFlyout( resourceType, defaultName, extension,
			name =>
			{
				if ( string.IsNullOrWhiteSpace( name ) ) return;
				onCreate( name );
			} );
	}

	static void CreateFromTemplate( string name, string defaultFile, DirectoryInfo folder )
	{
		var extension = Path.GetExtension( defaultFile );
		var sourceFile = FileSystem.Root.GetFullPath( $"/templates/{defaultFile}" );
		if ( !File.Exists( sourceFile ) )
		{
			Log.Error( $"Can't create asset! Missing template: {defaultFile}" );
			return;
		}

		var defaultName = Path.GetFileNameWithoutExtension( GetNewFilename( folder, name, extension ) );

		OpenCreateAssetFlyout( name, defaultName, extension,
			destName =>
			{
				var destPath = Path.Combine( folder.FullName, destName );

				if ( File.Exists( destPath ) )
					return;

				File.Copy( sourceFile, destPath );
				var asset = AssetSystem.RegisterFile( destPath );
				MainAssetBrowser.Instance?.Local.OnAssetCreated( asset, destPath );
			} );
	}

	public static void CreateGameResource( AssetTypeAttribute gameResource, DirectoryInfo folder )
	{
		var slash = gameResource.Name.LastIndexOf( '/' );
		var name = slash == -1 ? gameResource.Name : gameResource.Name.Substring( slash + 1, gameResource.Name.Length - slash - 1 );
		var extension = $".{gameResource.Extension}";
		var defaultName = Path.GetFileNameWithoutExtension( GetNewFilename( folder, name, extension ) );

		OpenCreateAssetFlyout( name, defaultName, extension,
			destName =>
			{
				string destPath = Path.Combine( folder.FullName, destName );
				if ( File.Exists( destPath ) ) return;

				var asset = AssetSystem.CreateResource( gameResource.Extension, destPath );
				MainAssetBrowser.Instance?.Local.OnAssetCreated( asset, destPath );
			} );
	}
}
