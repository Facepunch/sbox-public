using System.IO;

namespace Editor;

public record DiskLocation : LocalAssetBrowser.Location
{
	public DiskLocation( string path ) : this( new DirectoryInfo( path ) )
	{
	}

	public DiskLocation( DirectoryInfo directory ) : base( null, null )
	{
		Path = directory.FullName;
		if ( Path.EndsWith( '\\' ) )
			Path = Path.TrimEnd( '\\' );

		Project = EditorUtility.FindProjectByDirectory( Path );

		RootPath = Project?.GetRootPath();
		RootTitle = Project?.Config.Title;
		if ( RootPath is null )
		{
			string v = Path.NormalizeFilename();

			if ( v.Contains( "sbox/game/core" ) )
			{
				RootPath = FileSystem.Root.GetFullPath( "/core/" );
				RootTitle = "Core";
				Icon = "Folder";
				Type = LocalAssetBrowser.LocationType.Assets;
			}
			else if ( v.Contains( "/addons/citizen/assets" ) )
			{
				RootPath = FileSystem.Root.GetFullPath( "/addons/citizen/assets/" );
				RootTitle = "Citizen";
				Icon = "face";
				Type = LocalAssetBrowser.LocationType.Assets;
			}
		}

		if ( string.Equals( RootPath, Path, StringComparison.OrdinalIgnoreCase ) )
		{
			Name = RootTitle;
			IsRoot = true;
			RelativePath = "";

			if ( Project != null )
			{
				Icon = Project.Config.Type switch
				{
					"map" => "public",
					"game" => "sports_esports",
					"content" => "perm_media",
					"tool" => "hardware",
					_ => "Folder_Special"
				};
			}
		}
		else
		{
			Name = directory.Name;

			if ( RootPath is null )
			{
				RelativePath = Path;
			}
			else
			{
				RelativePath = System.IO.Path.GetRelativePath( RootPath, Path ).NormalizeFilename( false, false );

				// determine where this folder is located, as it determines how we treat it and what we expect to be inside
				string initialFolder = RelativePath;
				int seperatorIdx = RelativePath.IndexOf( '/' );
				if ( seperatorIdx > 0 )
				{
					initialFolder = RelativePath.Substring( 0, seperatorIdx );
				}

				if ( Type == LocalAssetBrowser.LocationType.Generic )
				{
					Type = initialFolder.ToLower() switch
					{
						"assets" => LocalAssetBrowser.LocationType.Assets,
						"editor" or "code" or "unittests" => LocalAssetBrowser.LocationType.Code,
						"localization" => LocalAssetBrowser.LocationType.Localization,
						_ => LocalAssetBrowser.LocationType.Generic,
					};
				}

				Icon = "folder";

				if ( seperatorIdx == -1 && Project is not null )
				{
					ContentsIcon = DirectoryEntry.GetUniqueIcon( Name ) ?? null;
					Name = Name.ToTitleCase();
				}
			}
		}
	}

	public override bool IsValid()
	{
		return Directory.Exists( Path );
	}

	public override IEnumerable<LocalAssetBrowser.Location> GetDirectories()
	{
		if ( !Directory.Exists( Path ) )
			yield break;

		foreach ( var subDir in Directory.GetDirectories( Path ) )
		{
			var dir = new DirectoryInfo( subDir );

			if ( dir.Attributes.HasFlag( FileAttributes.Hidden ) )
				continue;

			string name = dir.Name.ToLower();
			if ( name.StartsWith( '.' ) ) continue;
			if ( name.StartsWith( '_' ) ) continue;

			if ( name.Equals( "config" ) ) continue;
			if ( name.Equals( "obj" ) ) continue;

			// Hide obj/ folder in code directories
			if ( Type is LocalAssetBrowser.LocationType.Code )
			{
				if ( name.Equals( "obj" ) ) continue;
				if ( name.Equals( "properties" ) ) continue;
			}

			if ( IsRoot && name.Equals( "libraries" ) )
				continue;

			yield return new DiskLocation( dir );
		}
	}

	public override IEnumerable<FileInfo> GetFiles()
	{
		foreach ( var filePath in Directory.GetFiles( Path ) )
		{
			var file = new FileInfo( filePath );

			if ( file.Attributes.HasFlag( FileAttributes.Hidden ) )
				continue;

			if ( file.Name.StartsWith( '.' ) ) continue;

			yield return file;
		}
	}
}
