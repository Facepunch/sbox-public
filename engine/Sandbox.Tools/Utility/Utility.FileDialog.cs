using System.IO;
namespace Editor;

public static partial class EditorUtility
{
	/// <summary>
	/// Open a file save dialog. Returns null on cancel, else the absolute path of the target file.
	/// An empty <paramref name="extension"/> shows every file.
	/// </summary>
	public static string SaveFileDialog( string title, string extension, string defaultPath )
	{
		var directory = defaultPath;
		if ( directory?.Contains( '.' ) == true ) directory = Path.GetDirectoryName( directory );

		var fd = CreateFileDialog( title, extension, directory, defaultPath );
		fd.SetModeSave();

		if ( !fd.Execute() )
			return null;

		return fd.SelectedFile;
	}

	/// <summary>
	/// Open a file open dialog. Returns null on cancel, else the absolute path of the target file.
	/// An empty <paramref name="extension"/> shows every file.
	/// </summary>
	public static string OpenFileDialog( string title, string extension, string defaultPath )
	{
		var fd = CreateFileDialog( title, extension, string.IsNullOrEmpty( defaultPath ) ? null : Path.GetDirectoryName( defaultPath ), defaultPath );
		fd.SetModeOpen();

		if ( !fd.Execute() )
			return null;

		return fd.SelectedFile;
	}

	static FileDialog CreateFileDialog( string title, string extension, string directory, string defaultPath )
	{
		extension = extension?.Trim().Trim( '.' );

		var fd = new FileDialog( null );
		fd.Title = title;
		if ( !string.IsNullOrEmpty( directory ) ) fd.Directory = directory;
		if ( !string.IsNullOrEmpty( defaultPath ) ) fd.SelectFile( Path.GetFileName( defaultPath ) );
		fd.SetFindFile();

		if ( !string.IsNullOrEmpty( extension ) )
		{
			fd.DefaultSuffix = $".{extension}";
			fd.SetNameFilter( $"{extension} (*.{extension})" );
		}

		return fd;
	}
}
