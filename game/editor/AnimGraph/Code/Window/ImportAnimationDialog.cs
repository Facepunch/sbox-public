using System.IO;

namespace Editor.AnimGraph;

internal sealed class ImportAnimationDialog : Dialog
{
	private readonly AnimationImportBatch _batch;
	private readonly Widget _settings;
	private readonly Widget _details;
	private readonly ScrollArea _queue;
	private readonly LineEdit _directory;
	private readonly Label _status;
	private readonly WarningBox _error;
	private readonly Button _import;
	private readonly Button _cancel;
	private readonly Dictionary<AnimationImportEntry, (Widget Row, Label Status, LineEdit Name, Button Source)> _rows = [];
	private AnimationImportEntry _selected;
	private Label _detailStatus;

	public Action<IReadOnlyList<Asset>> Imported { get; set; }

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

	[Event( "asset.contextmenu", Priority = 50 )]
	private static void AddSourceOption( AssetContextMenu context )
	{
		if ( context.SelectedList.Count == 0 || context.SelectedList.Any( entry => entry.FileInfo is not { } file
			|| Path.GetExtension( file.Name ).ToLowerInvariant() is not (".fbx" or ".dmx" or ".smd") ) )
			return;

		var sources = context.SelectedList.Select( entry => entry.FileInfo.FullName ).ToArray();
		context.Menu.AddOption( sources.Length == 1 ? "Create Animation Clip" : "Create Animation Clips", "movie", () =>
		{
			var assetsDirectory = Path.GetFullPath( Project.Current.GetAssetsPath() );
			var directory = Path.GetDirectoryName( sources[0] );
			var relative = Path.GetRelativePath( assetsDirectory, directory );
			if ( Path.IsPathRooted( relative ) || relative == ".." || relative.StartsWith( $"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal ) )
				directory = assetsDirectory;

			var dialog = new ImportAnimationDialog( directory );
			dialog.AddSources( sources );
			dialog.Show();
		} );
	}

	public ImportAnimationDialog( string directory, string skeletonName = "", Widget parent = null ) : base( parent ?? EditorWindow )
	{
		_batch = new AnimationImportBatch( Project.Current.GetAssetsPath(), directory, skeletonName );
		Window.Title = "Import Animations";
		Window.Size = new Vector2( 840, 720 );
		Window.MinimumSize = new Vector2( 720, 600 );
		Window.SetWindowIcon( "movie" );

		var layout = Layout = Layout.Column();
		layout.Margin = 12;
		layout.Spacing = 8;
		layout.Add( new Label( "Import FBX, DMX or SMD files. Each file becomes a clip using its full animation and first take unless changed below.", this ) { WordWrap = true } );

		_settings = layout.Add( new Widget( this ) { Layout = Layout.Column() } );
		_settings.Layout.Spacing = 8;
		var serialized = EditorTypeLibrary.GetSerializedObject( _batch.Settings );
		var sheet = new ControlSheet();
		var skeleton = sheet.AddRow( serialized.GetProperty( nameof( ClipDocument.SkeletonName ) ) );
		skeleton.Enabled = string.IsNullOrWhiteSpace( skeletonName );
		if ( !skeleton.Enabled )
			skeleton.ToolTip = "Uses the skeleton selected in your animation graph.";
		sheet.AddRow( serialized.GetProperty( nameof( ClipDocument.ImportScale ) ) );
		_settings.Layout.Add( sheet );
		serialized.OnPropertyChanged += _ => Refresh();

		var destination = _settings.Layout.AddRow();
		destination.Spacing = 8;
		destination.Add( new Label( "Output folder", this ) );
		_directory = destination.Add( new LineEdit( this ) { Text = directory }, 1 );
		_directory.TextEdited += value =>
		{
			_batch.OutputDirectory = value;
			Refresh();
		};
		destination.Add( new Button( "Browse...", "folder_open", this ) { Clicked = ChooseDirectory } );
		_settings.Layout.Add( new Button( "Add Files...", "add", this ) { Clicked = ChooseSources } );

		var header = layout.AddRow();
		header.Add( new Label( "Source / settings", this ), 1 );
		header.Add( new Label( "Output name (.anim)", this ), 1 );
		header.Add( new Label( "Status", this ) { FixedWidth = 140 } );
		_queue = layout.Add( new ScrollArea( this ) { MinimumHeight = 120, HorizontalScrollbarMode = ScrollbarMode.Off }, 1 );
		_queue.Canvas = new Widget( _queue ) { Layout = Layout.Column() };
		_queue.Canvas.Layout.Spacing = 4;

		_details = layout.Add( new Widget( this ) { Layout = Layout.Column(), Visible = false } );
		_details.Layout.Spacing = 4;
		layout.Add( new Label( "Uncheck clips to skip them. External files are copied into a source folder beside the clips. Existing assets are never overwritten.", this )
		{
			WordWrap = true,
			Color = Theme.TextLight
		} );
		_status = layout.Add( new Label( this ) { WordWrap = true, Color = Theme.TextLight } );
		_error = layout.Add( new WarningBox( this ) { BackgroundColor = Theme.Red, Icon = "error_outline", Visible = false } );
		var buttons = layout.AddRow();
		buttons.Spacing = 8;
		buttons.AddStretchCell();
		_cancel = buttons.Add( new Button( "Cancel", this ) { Clicked = CancelOrClose } );
		_import = buttons.Add( new Button.Primary( "Import", "file_download", this ) { Clicked = () => _ = ImportAsync() } );
		Refresh();
	}

	private void ChooseSources()
	{
		var dialog = new FileDialog( this ) { Title = "Choose Animation Files", Directory = _batch.OutputDirectory };
		dialog.SetFindExistingFiles();
		dialog.SetModeOpen();
		dialog.SetNameFilter( "Animation Source (*.fbx *.dmx *.smd)" );
		if ( !dialog.Execute() )
			return;
		AddSources( dialog.SelectedFiles );
	}

	private void AddSources( IEnumerable<string> sources )
	{
		_batch.AddSources( sources );
		_selected ??= _batch.Entries.FirstOrDefault();
		RebuildQueue();
	}

	private void ChooseDirectory()
	{
		var dialog = new FileDialog( this ) { Title = "Choose Clip Output Folder", Directory = _batch.OutputDirectory };
		dialog.SetFindDirectory();
		dialog.SetModeOpen();
		if ( !dialog.Execute() || string.IsNullOrWhiteSpace( dialog.SelectedFile ) )
			return;
		_directory.Text = _batch.OutputDirectory = dialog.SelectedFile;
		Refresh();
	}

	private void RebuildQueue()
	{
		_queue.Canvas.Layout.Clear( true );
		_rows.Clear();
		foreach ( var entry in _batch.Entries )
		{
			var row = _queue.Canvas.Layout.Add( new Widget( _queue.Canvas ) { Layout = Layout.Row() } );
			row.Layout.Spacing = 6;
			var included = row.Layout.Add( new Checkbox( row ) { Value = entry.Included, ToolTip = "Include this clip in the import" } );
			included.Toggled = () =>
			{
				entry.Included = included.Value;
				Refresh();
			};
			var source = row.Layout.Add( new Button( Path.GetFileName( entry.Source ), "tune", row )
			{
				ToolTip = $"Edit import settings for {entry.Source}",
				Clicked = () => ShowDetails( entry )
			}, 1 );
			var name = row.Layout.Add( new LineEdit( row ) { Text = entry.Name, ToolTip = "Clip name, without .anim" }, 1 );
			name.TextEdited += value =>
			{
				entry.Name = value;
				Refresh();
			};
			var status = row.Layout.Add( new Label( row ) { FixedWidth = 116 } );
			row.Layout.Add( new IconButton( "close" )
			{
				ToolTip = "Remove from queue. Files already saved are kept.",
				OnClick = () =>
				{
					_batch.Entries.Remove( entry );
					if ( _selected == entry )
						_selected = _batch.Entries.FirstOrDefault();
					RebuildQueue();
				}
			} );
			_rows.Add( entry, (row, status, name, source) );
		}
		_queue.Canvas.Layout.AddStretchCell();
		ShowDetails( _selected );
	}

	private void ShowDetails( AnimationImportEntry entry )
	{
		_selected = entry;
		_details.Layout.Clear( true );
		_details.Visible = entry is not null;
		_detailStatus = null;
		if ( entry is null )
		{
			Refresh();
			return;
		}
		_details.Layout.Add( new Label( $"Settings for {Path.GetFileName( entry.Source )}", _details ) { ToolTip = entry.Source } );
		_detailStatus = _details.Layout.Add( new Label( _details ) { WordWrap = true, Color = Theme.Red } );
		var serialized = EditorTypeLibrary.GetSerializedObject( entry.Document );
		var sheet = new ControlSheet();
		sheet.AddRow( serialized.GetProperty( nameof( ClipDocument.Take ) ) );
		sheet.AddGroup( "Trim / Duration",
		[
			serialized.GetProperty( nameof( ClipDocument.FrameRange ) ),
			serialized.GetProperty( nameof( ClipDocument.StartFrame ) ),
			serialized.GetProperty( nameof( ClipDocument.EndFrame ) ),
			serialized.GetProperty( nameof( ClipDocument.DurationOverride ) )
		] );
		_details.Layout.Add( sheet );
		_details.Layout.Add( new Button( "Add All Takes", "playlist_add", _details )
		{
			Enabled = entry.Document.GetAnimationTakes().Count > 1,
			ToolTip = "Queue one clip per take in this source file. Each new clip uses its full duration.",
			Clicked = () =>
			{
				try
				{
					_batch.AddAllTakes( entry );
					_error.Visible = false;
					RebuildQueue();
				}
				catch ( InvalidOperationException exception )
				{
					Log.Warning( exception, "AnimGraph: could not add animation takes" );
					ShowError( exception.Message );
				}
			}
		} );
		serialized.OnPropertyChanged += _ => Refresh();
		Refresh();
	}

	private void Refresh()
	{
		var settingsError = _batch.ValidateSettings();
		var errors = _batch.ValidateEntries();
		var pending = _batch.Entries.Where( entry => entry.Included && entry.Asset is null ).ToArray();
		var invalid = pending.Count( entry => errors[entry] is not null );
		var imported = _batch.Entries.Count( entry => entry.Asset is not null );
		var failed = pending.Count( entry => entry.Error is not null );
		foreach ( var (entry, row) in _rows )
		{
			var error = entry.Asset is null && entry.Included ? errors[entry] ?? entry.Error : null;
			row.Row.Enabled = !_batch.IsRunning && entry.Asset is null;
			row.Source.Tint = entry == _selected ? Theme.SelectedBackground : Theme.ButtonBackground;
			row.Name.Enabled = entry.Document.FilePath is null;
			row.Status.Text = entry.Asset is not null ? "Imported" : entry.IsImporting ? "Importing..." : !entry.Included ? "Skipped"
				: entry.Error is not null ? "Failed" : error is not null ? "Needs attention" : "Ready";
			row.Status.Color = error is not null ? Theme.Red : entry.Asset is not null ? Theme.Green : Theme.TextLight;
			row.Status.ToolTip = error ?? row.Status.Text;
			try
			{
				row.Name.ToolTip = _batch.GetOutputPath( entry );
			}
			catch ( Exception exception ) when ( exception is ArgumentException or NotSupportedException or PathTooLongException )
			{
				row.Name.ToolTip = "Invalid output path";
			}
		}
		_settings.Enabled = !_batch.IsRunning;
		_details.Enabled = !_batch.IsRunning && _selected is { Asset: null, Included: true };
		if ( _detailStatus is not null )
		{
			_detailStatus.Text = errors[_selected] ?? _selected.Error ?? "";
			_detailStatus.Visible = !string.IsNullOrEmpty( _detailStatus.Text );
		}
		_import.Enabled = !_batch.IsRunning && settingsError is null && invalid == 0 && pending.Length > 0;
		var action = failed > 0 && failed == pending.Length ? "Retry" : "Import";
		_import.Text = $"{action} {pending.Length} Clip{(pending.Length == 1 ? "" : "s")}";
		_import.ToolTip = settingsError ?? (invalid > 0 ? "Rename, correct or uncheck the clips marked Needs attention." : "Import checked clips. Completed clips are not imported again.");
		_cancel.Text = _batch.IsRunning ? _batch.CancelRequested ? "Stopping..." : "Stop After Current Clip" : imported > 0 ? "Close" : "Cancel";
		_cancel.Enabled = !_batch.IsRunning || !_batch.CancelRequested;
		_status.Text = _batch.IsRunning ? $"{_batch.ProcessedCount} / {_batch.TotalCount} processed. {_batch.Entries.FirstOrDefault( entry => entry.IsImporting )?.Name ?? "Preparing"}..."
			: _batch.Entries.Count == 0 ? "Add animation files to get started."
			: $"{imported} imported, {failed} failed, {pending.Length - failed} pending, {_batch.Entries.Count( entry => !entry.Included )} skipped."
				+ (settingsError is not null ? $" {settingsError}" : invalid > 0 ? $" {invalid} clip(s) need attention; hover their status for details." : "");
	}

	private async Task ImportAsync()
	{
		_error.Visible = false;
		try
		{
			var imported = await _batch.ImportAsync( _ =>
			{
				if ( this.IsValid() )
					Refresh();
			} );
			if ( MainAssetBrowser.Instance?.Local is { IsValid: true } browser )
			{
				foreach ( var asset in imported )
					browser.OnAssetCreated( asset, asset.GetSourceFile( true ) );
			}
			if ( imported.Count > 0 )
				Imported?.Invoke( imported );
			if ( !this.IsValid() )
				return;
			if ( imported.Count > 0 && _batch.Entries.Count == 1 )
			{
				if ( Imported is null )
					imported[0].OpenInEditor();
				Close();
				return;
			}
			Refresh();
		}
		catch ( InvalidOperationException exception )
		{
			Log.Warning( exception, "AnimGraph: could not start animation import" );
			if ( this.IsValid() )
			{
				ShowError( exception.Message );
				Refresh();
			}
		}
	}

	private void CancelOrClose()
	{
		if ( !_batch.IsRunning )
		{
			Close();
			return;
		}
		_batch.CancelRequested = true;
		Refresh();
	}

	public override void OnDestroyed()
	{
		_batch.CancelRequested = true;
		base.OnDestroyed();
	}

	private void ShowError( string message )
	{
		_error.Label.Text = message;
		_error.Visible = true;
	}
}
