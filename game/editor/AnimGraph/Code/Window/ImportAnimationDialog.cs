using System.IO;

namespace Editor.AnimGraph;

internal sealed class ImportAnimationDialog : Dialog
{
	private readonly ClipDocument _document;
	private readonly string _directory;
	private readonly string _assetsDirectory;
	private readonly Label _status;
	private readonly WarningBox _error;
	private readonly Button _import;

	public Action<Asset> Imported { get; set; }

	[Event( "asset.createoptions" )]
	private static void AddCreateOption( List<CreateAsset.Entry> entries )
	{
		entries.Add( new CreateAsset.Entry
		{
			Name = "Animation Clip",
			Icon = "movie",
			Category = "Animation",
			Action = folder => new ImportAnimationDialog( folder.FullName ).Show()
		} );
	}

	public ImportAnimationDialog( string directory, string skeletonName = "", Widget parent = null ) : base( parent ?? EditorWindow )
	{
		_directory = directory;
		_assetsDirectory = Path.GetFullPath( Project.Current.GetAssetsPath() );
		_document = ClipDocument.Create( skeletonName );

		Window.WindowTitle = "Import Animation";
		Window.Size = new Vector2( 600, 300 );
		Window.MinimumSize = new Vector2( 540, 280 );
		Window.SetWindowIcon( "movie" );

		Layout = Layout.Column();
		Layout.Margin = 12;
		Layout.Spacing = 8;
		Layout.Add( new Label( "Import an FBX, DMX or SMD animation. The full animation is imported unless you change the import settings.", this ) { WordWrap = true } );

		var serialized = EditorTypeLibrary.GetSerializedObject( _document );
		var source = Layout.AddRow();
		source.Spacing = 8;
		var sourceSheet = new ControlSheet();
		sourceSheet.AddRow( serialized.GetProperty( nameof( ClipDocument.SourceFilename ) ) );
		source.Add( sourceSheet, 1 );
		source.Add( new Button( "Browse...", "folder_open", this )
		{
			Clicked = ChooseSource,
			ToolTip = "Choose an animation file from your computer"
		} );

		var sheet = new ControlSheet();
		var skeleton = sheet.AddRow( serialized.GetProperty( nameof( ClipDocument.SkeletonName ) ) );
		skeleton.Enabled = string.IsNullOrWhiteSpace( skeletonName );
		if ( !skeleton.Enabled )
			skeleton.ToolTip = "Uses the skeleton selected in your animation graph.";
		sheet.AddGroup( "Import Settings",
		[
			serialized.GetProperty( nameof( ClipDocument.Take ) ),
			serialized.GetProperty( nameof( ClipDocument.StartFrame ) ),
			serialized.GetProperty( nameof( ClipDocument.EndFrame ) ),
			serialized.GetProperty( nameof( ClipDocument.ImportScale ) ),
			serialized.GetProperty( nameof( ClipDocument.DurationOverride ) )
		] );
		Layout.Add( sheet );
		Layout.Add( new Label( "Files outside your project are copied into Assets so the animation stays with your project.", this )
		{
			WordWrap = true,
			Color = Theme.TextLight
		} );
		Layout.AddStretchCell();
		_status = Layout.Add( new Label( this ) { WordWrap = true, Color = Theme.TextLight } );
		_error = Layout.Add( new WarningBox( this ) { BackgroundColor = Theme.Red, Icon = "error_outline", Visible = false } );

		var buttons = Layout.AddRow();
		buttons.Spacing = 8;
		buttons.AddStretchCell();
		buttons.Add( new Button( "Cancel", this ) { Clicked = Close } );
		_import = buttons.Add( new Button.Primary( "Import...", "file_download", this ) { Clicked = () => Import() } );

		serialized.OnPropertyChanged += _ =>
		{
			_error.Visible = false;
			UpdateValidation();
		};
		UpdateValidation();
	}

	private void ChooseSource()
	{
		var dialog = new FileDialog( Window ) { Title = "Choose Animation File", Directory = _directory };
		dialog.SetFindExistingFile();
		dialog.SetModeOpen();
		dialog.SetNameFilter( "Animation Source (*.fbx *.dmx *.smd)" );
		if ( dialog.Execute() && !string.IsNullOrWhiteSpace( dialog.SelectedFile ) )
		{
			_document.SourceFilename = dialog.SelectedFile;
			_error.Visible = false;
			UpdateValidation();
		}
	}

	private string SourcePath => string.IsNullOrWhiteSpace( _document.SourceFilename ) ? null
		: AssetSystem.FindByPath( _document.SourceFilename )?.GetSourceFile( true )
			?? Path.GetFullPath( _document.SourceFilename, _assetsDirectory );

	private string ValidateSettings( out string source )
	{
		source = null;
		if ( string.IsNullOrWhiteSpace( _document.SourceFilename ) )
			return "Choose an animation file to import.";

		try
		{
			source = SourcePath;
		}
		catch ( Exception exception ) when ( exception is ArgumentException or NotSupportedException or PathTooLongException )
		{
			return "The animation file path is invalid. Choose an existing FBX, DMX or SMD file.";
		}

		if ( !File.Exists( source ) || Path.GetExtension( source ).ToLowerInvariant() is not (".fbx" or ".dmx" or ".smd") )
			return "Choose an existing FBX, DMX or SMD animation file.";

		if ( string.IsNullOrWhiteSpace( _document.SkeletonName ) )
			return "Choose the skeleton this animation will play on.";

		if ( Skeleton.Load( _document.SkeletonName ) is not { IsValid: true, IsError: false, BoneCount: > 0 } )
			return "The selected skeleton could not be loaded. Choose another skeleton or check its compilation errors.";

		if ( !float.IsFinite( _document.ImportScale ) || _document.ImportScale <= 0 )
			return "Import scale must be positive and finite.";

		if ( _document.StartFrame is { } start && _document.EndFrame is { } end && start > end )
			return "End frame must not be before start frame.";

		return null;
	}

	private void UpdateValidation()
	{
		var message = ValidateSettings( out _ );
		_import.Enabled = message is null;
		_import.ToolTip = message ?? "Choose where to save the new animation clip";
		_status.Text = message ?? "Ready to import.";
	}

	private void Import( string path = null )
	{
		try
		{
			var validation = ValidateSettings( out var source );
			if ( validation is not null )
			{
				ShowError( validation );
				return;
			}
			_error.Visible = false;

			if ( path is null )
			{
				var dialog = new FileDialog( Window )
				{
					Title = "Create Animation Clip",
					Directory = _document.FilePath is null ? _directory : Path.GetDirectoryName( _document.FilePath ),
					DefaultSuffix = ".anim"
				};
				dialog.SetFindFile();
				dialog.SetModeSave();
				dialog.SetNameFilter( "Animation Clip (*.anim)" );
				var name = Path.GetFileNameWithoutExtension( source );
				dialog.SelectFile( _document.FilePath is null ? $"{name}.anim" : Path.GetFileName( _document.FilePath ) );
				if ( !dialog.Execute() || string.IsNullOrWhiteSpace( dialog.SelectedFile ) )
					return;
				path = dialog.SelectedFile;
			}

			path = Path.GetFullPath( path );
			if ( !IsProjectAsset( path ) || !Path.GetExtension( path ).Equals( ".anim", StringComparison.OrdinalIgnoreCase ) )
			{
				ShowError( "Save the .anim clip inside this project's Assets folder." );
				return;
			}

			var sameAsset = string.Equals( _document.FilePath, path, StringComparison.OrdinalIgnoreCase );
			if ( File.Exists( path ) && !sameAsset )
			{
				ShowError( "An asset already exists at this path. Choose a different name for your new clip." );
				return;
			}

			if ( !IsProjectAsset( source ) )
			{
				var directory = Path.Combine( Path.GetDirectoryName( path ), "source" );
				Directory.CreateDirectory( directory );
				var name = Path.GetFileNameWithoutExtension( path );
				var extension = Path.GetExtension( source );
				var destination = Path.Combine( directory, name + extension );
				for ( var index = 2; File.Exists( destination ); ++index )
					destination = Path.Combine( directory, $"{name}_{index}{extension}" );
				File.Copy( source, destination, overwrite: false );
				source = destination;
			}
			_document.SourceFilename = Path.GetRelativePath( _assetsDirectory, source ).Replace( '\\', '/' );

			if ( !(sameAsset ? _document.Save() : _document.SaveAs( path )) )
			{
				ShowError( "Couldn't save the animation clip. See the console for details." );
				return;
			}

			var asset = AssetSystem.RegisterFile( path );
			if ( asset is null || !asset.Compile( false ) )
			{
				ShowError( "The clip source was saved, but compilation failed. Check the console, correct the import settings and try again." );
				return;
			}

			var clip = Clip.Load( asset.Path );
			if ( clip is not { IsValid: true, IsError: false, FrameCount: > 0 }
				|| !string.Equals( clip.SkeletonName, _document.SkeletonName, StringComparison.OrdinalIgnoreCase ) )
			{
				ShowError( "The clip compiled but could not be loaded against the selected skeleton. Check the console for details." );
				return;
			}

			MainAssetBrowser.Instance?.Local.OnAssetCreated( asset, path );
			if ( Imported is { } imported )
				imported( asset );
			else
				asset.OpenInEditor();
			Close();
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException )
		{
			Log.Warning( exception, "AnimGraph: could not import animation" );
			ShowError( exception.Message );
		}
	}

	private bool IsProjectAsset( string path )
	{
		var relative = Path.GetRelativePath( _assetsDirectory, path );
		return !Path.IsPathRooted( relative ) && relative != ".." && !relative.StartsWith( $"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal );
	}

	private void ShowError( string message )
	{
		_error.Label.Text = message;
		_error.Visible = true;
	}
}
