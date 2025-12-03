using System.IO;

namespace Editor;

public class DirectoryEntry : IAssetListEntry
{
	private readonly string Path;
	public DirectoryInfo DirectoryInfo => new DirectoryInfo( Path );

	public string Name { get; private set; }
	public string GetStatusText() => Path;

	static Dictionary<string, FolderMetadata> AllMetadata = null;
	FolderMetadata Metadata;

	public DirectoryEntry( string path )
	{
		Path = path;
		Name = System.IO.Path.GetFileName( Path );
		Metadata = GetMetadata( Path );
	}

	//
	// Events
	//
	public bool OnDoubleClicked( AssetList list )
	{
		if ( list.Browser is AssetBrowser ab )
			ab.NavigateTo( Path );
		return true;
	}

	//
	// List rendering
	//
	private string GetUniqueIcon() => GetUniqueIcon( Name.ToLowerInvariant() );

	private static string MetadataPath => "Directory.metadata";

	internal static string GetUniqueIcon( string name ) => name.ToLower() switch
	{
		"data" => "description",
		"maps" => "map",
		"materials" => "format_paint",
		"models" => "view_in_ar",
		"prefabs" => "ballot",
		"scenes" => "perm_media",
		"shaders" => "grain",
		"textures" => "texture",
		"fonts" => "text_fields",
		"audio" or "sounds" => "volume_up",
		"animgraphs" => "animation",
		"ui" => "palette",

		"assets" => "category",
		"code" => "code",
		"unittests" => "science",
		"editor" => "hardware",
		"exports" => "open_in_browser",
		"localization" => "language",
		"projectsettings" => "tune",

		_ => null,
	};

	public void DrawText( Rect rect )
	{
		Paint.SetDefaultFont( 7 );
		Paint.ClearPen();
		Paint.SetPen( Theme.Text.WithAlpha( 0.7f ) );

		rect.Position = rect.Position - new Vector2( 0, 4 );

		var strText = Paint.GetElidedText( Name, rect.Width, ElideMode.Right );
		Paint.DrawText( rect, strText, TextFlag.Center | TextFlag.WordWrap );
	}

	public void DrawIcon( Rect rect )
	{
		Paint.ClearBrush();
		Paint.SetPen( Metadata.Color );
		var folderRect = Paint.DrawIcon( rect, "folder", rect.Width );

		var icon = string.IsNullOrEmpty( Metadata.Icon ) ? GetUniqueIcon() : Metadata.Icon;
		if ( !string.IsNullOrEmpty( icon ) )
		{
			folderRect.Top += 5f;
			Paint.SetPen( Metadata.Color.Darken( 0.25f ) );
			Paint.DrawIcon( folderRect, icon, rect.Width / 3f, TextFlag.DontClip | TextFlag.Center );
		}
	}

	public void Delete()
	{
		DirectoryInfo.Delete( true );
	}

	public void Rename( string newName )
	{
		var parentPath = DirectoryInfo.Parent.FullName;
		var newPath = System.IO.Path.Combine( parentPath, newName );

		DirectoryInfo.MoveTo( newPath );
	}

	internal static FolderMetadata GetMetadata( string path )
	{
		var relativePath = GetRelativePath( path );

		// If we don't have a metadata dictionary, initialize it
		if ( AllMetadata is null )
		{
			var loadedMetadata = FileSystem.ProjectSettings.ReadJsonOrDefault<IEnumerable<KeyValuePair<string, FolderMetadata>>>( MetadataPath );
			if ( loadedMetadata is not null )
			{
				// Load metadata from file
				AllMetadata = loadedMetadata.ToDictionary( x => x.Key, x => x.Value );
			}
			else
			{
				// If the file doesn't exist, or the data is malformed, initialize with an empty dictionary
				AllMetadata = new Dictionary<string, FolderMetadata>();
			}
		}

		// Get the cached metadata if it exists, otherwise create a new entry
		if ( AllMetadata.TryGetValue( relativePath, out var metadata ) )
		{
			return metadata;
		}

		var newData = new FolderMetadata();
		AllMetadata[relativePath] = newData;
		return newData;
	}

	internal static void RenameMetadata( string path, string newPath )
	{
		var relativePath = GetRelativePath( path );
		var newRelativePath = GetRelativePath( newPath );
		if ( AllMetadata.TryGetValue( relativePath, out var metadata ) )
		{
			AllMetadata[newRelativePath] = metadata;
			AllMetadata.Remove( relativePath );
		}
		SaveMetadata();
	}

	static string GetRelativePath( string path )
	{
		var rootPath = Project.Current.GetRootPath();
		return System.IO.Path.GetRelativePath( rootPath, path );
	}

	public override bool Equals( object obj )
	{
		if ( obj is not DirectoryEntry de )
		{
			return false;
		}

		return DirectoryInfo.FullName.Equals( de.DirectoryInfo.FullName );
	}

	public override int GetHashCode()
	{
		return DirectoryInfo.FullName.GetHashCode();
	}

	internal static void SaveMetadata()
	{
		// Don't serialize if we don't have any metadata
		if ( AllMetadata is null )
			return;

		// Filter out any default entries
		var newDataHash = new FolderMetadata().GetHashCode();
		var savedMetadata = AllMetadata.Where( x =>
		{
			if ( x.Value.GetHashCode() == newDataHash )
				return false;
			return true;
		} );

		// Don't save an empty file if we don't have to
		if ( savedMetadata.Count() == 0 && !FileSystem.ProjectSettings.FileExists( MetadataPath ) )
			return;

		FileSystem.ProjectSettings.WriteJson( MetadataPath, savedMetadata );
	}

	internal class FolderMetadata
	{
		[DefaultValue( "#E6DB74" )]
		public Color Color { get; set; } = Theme.Yellow;

		[IconName]
		public string Icon { get; set; } = "";

		public override int GetHashCode()
		{
			return System.HashCode.Combine( Color, Icon );
		}
	}
}
