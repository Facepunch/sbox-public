using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

[CustomEditor( typeof( string ), WithAllAttributes = [typeof( FileExtensionsAttribute )] )]
public sealed class FilePathControlWidget : ControlWidget
{
	public FilePathControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		Layout.Add( new StringControlWidget( property ), 1 );
		Layout.Add( new IconButton( "file_open", ShowFileSelector ) { ToolTip = "Select File" }, 0 );
	}

	private void ShowFileSelector()
	{
		var defaultPath = SerializedProperty.GetValue<string>() ?? "";
		var directory = Path.GetDirectoryName( defaultPath );

		if ( string.IsNullOrEmpty( directory ) )
		{
			directory = Project.Current.GetRootPath();
		}

		var extensions = SerializedProperty.GetAttributes<FileExtensionsAttribute>()
			.SelectMany( x => x.Extensions.Split( ',' ) )
			.Select( x => x.Trim().Trim( '.' ).ToLowerInvariant() )
			.Where( x => !string.IsNullOrEmpty( x ) )
			.ToArray();

		if ( extensions.Length == 0 )
		{
			Log.Error( "Expected at least one file extension." );
			extensions = ["txt"];
		}

		var fd = new FileDialog( null )
		{
			Title = SerializedProperty.DisplayName,
			Directory = directory,
			DefaultSuffix = $".{extensions[0]}"
		};

		fd.SelectFile( Path.GetFileName( defaultPath ) );
		fd.SetFindFile();
		fd.SetModeSave();
		fd.SetNameFilter( string.Join( " ", extensions.Select( x => $"*.{x}" ) ) );

		if ( !fd.Execute() ) return;

		SerializedProperty.SetValue( fd.SelectedFile );
	}

	protected override void OnPaint()
	{
		// No background
	}
}
