using System.IO;
using System.Text.Json.Nodes;

namespace Editor.AnimGraph;

internal sealed class ImportSkeletonDialog : Dialog
{
	private sealed class ImportSettings
	{
		[Property, Title( "Skeleton File" ), FilePath( Extension = "fbx,dmx,smd" )]
		public string SourceFilename { get; set; } = "";

		[Property, Title( "Import Scale" ), Editor( "importscale" )]
		public float Scale { get; set; } = 1f;

		[Property, Description( "Leave empty to import the whole skeleton, or name a bone to import only that hierarchy." )]
		public string RootBone { get; set; } = "";

		[Property, ResourceType( "vmdl" )]
		public string PreviewModel { get; set; } = "";
	}

	private readonly ImportSettings _settings;
	private readonly string _directory;
	private readonly string _assetsDirectory;
	private readonly Label _status;
	private readonly WarningBox _error;
	private readonly Button _import;
	private string _filePath;

	[Event( "asset.createoptions" )]
	private static void AddCreateOption( List<CreateAsset.Entry> entries )
	{
		entries.Add( new CreateAsset.Entry
		{
			Name = "Skeleton",
			Icon = "accessibility_new",
			Category = "Animation",
			Action = folder => new ImportSkeletonDialog( folder.FullName ).Show()
		} );
	}

	[Event( "asset.contextmenu", Priority = 50 )]
	private static void AddSourceOption( AssetContextMenu context )
	{
		if ( context.SelectedList.Count != 1 )
			return;

		var source = context.SelectedList[0].FileInfo.FullName;
		if ( Path.GetExtension( source ).ToLowerInvariant() is not (".fbx" or ".dmx" or ".smd") )
			return;

		context.Menu.AddOption( "Create Skeleton", "accessibility_new",
			() => new ImportSkeletonDialog( Path.GetDirectoryName( source ), source ).Show() );
	}

	public ImportSkeletonDialog( string directory, string source = "", Widget parent = null ) : base( parent ?? EditorWindow )
	{
		_assetsDirectory = Path.GetFullPath( Project.Current.GetAssetsPath() );
		_directory = IsProjectAsset( directory ) ? directory : _assetsDirectory;
		_settings = new ImportSettings { SourceFilename = source };

		Window.WindowTitle = "Import Skeleton";
		Window.Size = new Vector2( 600, 300 );
		Window.MinimumSize = new Vector2( 540, 280 );
		Window.SetWindowIcon( "accessibility_new" );

		Layout = Layout.Column();
		Layout.Margin = 12;
		Layout.Spacing = 8;
		Layout.Add( new Label( "Import a skeleton from an FBX, DMX or SMD file.", this ) { WordWrap = true } );

		var serialized = EditorTypeLibrary.GetSerializedObject( _settings );
		var sourceRow = Layout.AddRow();
		sourceRow.Spacing = 8;
		var sourceSheet = new ControlSheet();
		sourceSheet.AddRow( serialized.GetProperty( nameof( ImportSettings.SourceFilename ) ) );
		sourceRow.Add( sourceSheet, 1 );
		sourceRow.Add( new Button( "Browse...", "folder_open", this ) { Clicked = ChooseSource } );

		var sheet = new ControlSheet();
		sheet.AddRow( serialized.GetProperty( nameof( ImportSettings.Scale ) ) );
		sheet.AddRow( serialized.GetProperty( nameof( ImportSettings.RootBone ) ) );
		sheet.AddRow( serialized.GetProperty( nameof( ImportSettings.PreviewModel ) ) );
		Layout.Add( sheet );
		Layout.Add( new Label( "Files outside your project are copied into Assets so the skeleton stays with your project.", this )
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
		_import = buttons.Add( new Button.Primary( "Import...", "file_download", this ) { Clicked = Import } );

		serialized.OnPropertyChanged += _ =>
		{
			_error.Visible = false;
			UpdateValidation();
		};
		UpdateValidation();
	}

	private void ChooseSource()
	{
		var dialog = new FileDialog( Window ) { Title = "Choose Skeleton File", Directory = _directory };
		dialog.SetFindExistingFile();
		dialog.SetModeOpen();
		dialog.SetNameFilter( "Skeleton Source (*.fbx *.dmx *.smd)" );
		if ( dialog.Execute() && !string.IsNullOrWhiteSpace( dialog.SelectedFile ) )
		{
			_settings.SourceFilename = dialog.SelectedFile;
			_error.Visible = false;
			UpdateValidation();
		}
	}

	private string ValidateSettings( out string source )
	{
		source = null;
		if ( string.IsNullOrWhiteSpace( _settings.SourceFilename ) )
			return "Choose a skeleton file to import.";

		try
		{
			source = AssetSystem.FindByPath( _settings.SourceFilename )?.GetSourceFile( true )
				?? Path.GetFullPath( _settings.SourceFilename, _assetsDirectory );
		}
		catch ( Exception exception ) when ( exception is ArgumentException or NotSupportedException or PathTooLongException )
		{
			return "The skeleton file path is invalid. Choose an existing FBX, DMX or SMD file.";
		}

		if ( !File.Exists( source ) || Path.GetExtension( source ).ToLowerInvariant() is not (".fbx" or ".dmx" or ".smd") )
			return "Choose an existing FBX, DMX or SMD skeleton file.";

		if ( !float.IsFinite( _settings.Scale ) || _settings.Scale <= 0 )
			return "Import scale must be positive and finite.";

		return null;
	}

	private void UpdateValidation()
	{
		var message = ValidateSettings( out _ );
		_import.Enabled = message is null;
		_import.ToolTip = message ?? "Choose where to save the new skeleton";
		_status.Text = message ?? "Ready to import.";
	}

	private void Import()
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

			var dialog = new FileDialog( Window )
			{
				Title = "Create Skeleton",
				Directory = _filePath is null ? _directory : Path.GetDirectoryName( _filePath ),
				DefaultSuffix = ".skel"
			};
			dialog.SetFindFile();
			dialog.SetModeSave();
			dialog.SetNameFilter( "Skeleton (*.skel)" );
			dialog.SelectFile( _filePath is null ? $"{Path.GetFileNameWithoutExtension( source )}.skel" : Path.GetFileName( _filePath ) );
			if ( !dialog.Execute() || string.IsNullOrWhiteSpace( dialog.SelectedFile ) )
				return;

			var path = Path.GetFullPath( dialog.SelectedFile );
			if ( !IsProjectAsset( path ) || !Path.GetExtension( path ).Equals( ".skel", StringComparison.OrdinalIgnoreCase ) )
			{
				ShowError( "Save the .skel skeleton inside this project's Assets folder." );
				return;
			}

			var sameAsset = string.Equals( _filePath, path, StringComparison.OrdinalIgnoreCase );
			if ( File.Exists( path ) && !sameAsset )
			{
				ShowError( "An asset already exists at this path. Choose a different name for your new skeleton." );
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
			_settings.SourceFilename = Path.GetRelativePath( _assetsDirectory, source ).Replace( '\\', '/' );
			var document = new JsonObject
			{
				["_class"] = "CSkeletonDocument",
				["m_sourceFilename"] = _settings.SourceFilename,
				["m_flGlobalScale"] = _settings.Scale,
				["m_rootBoneName"] = _settings.RootBone,
				["m_previewModel"] = ResourceReference.Write( ResourceId.FromPath( _settings.PreviewModel ) )
			};
			Directory.CreateDirectory( Path.GetDirectoryName( path ) );
			if ( !Kv3Document.Write( path, document, overwrite: sameAsset ) )
			{
				ShowError( "Couldn't save the skeleton. See the console for details." );
				return;
			}
			_filePath = path;

			var asset = AssetSystem.RegisterFile( path );
			if ( asset is null || !asset.Compile( false ) )
			{
				ShowError( "The skeleton source was saved, but compilation failed. Check the console, correct the import settings and try again." );
				return;
			}

			if ( Skeleton.Load( asset.Path ) is not { IsValid: true, IsError: false, BoneCount: > 0 } )
			{
				ShowError( "The skeleton compiled but could not be loaded. Check the console for details." );
				return;
			}

			MainAssetBrowser.Instance?.Local.OnAssetCreated( asset, path );
			asset.OpenInEditor();
			Close();
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException )
		{
			Log.Warning( exception, "AnimGraph: could not import skeleton" );
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
