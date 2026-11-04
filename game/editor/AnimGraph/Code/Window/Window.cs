using Sandbox.Helpers;

namespace Editor.AnimGraph;

/// <summary>
/// Main editor window for AnimGraph (.ag) files.
/// </summary>
[EditorForAssetType( "ag" )]
[EditorApp( "Animgraph Editor", "account_tree", "For editing animation graphs" )]
public partial class Window : DockWindow, IAssetEditor
{
	private Session _session;
	private View _graphView;
	private Properties _properties;
	private ClipList _clipList;
	private ParameterList _parameterList;
	private VariationEditor _variationEditor;
	private Preview _preview;
	private CompileLog _compileLog;
	private AnimationClipInfo _clipInfo;
	private bool _saving;

	private Data _graph => _session.Graph;
	private Asset _asset => _session.Asset;

	internal StateMachineNode SelectedStateMachine => _selectedStateMachine;
	internal StateMachineCanvas StateMachineCanvas => (_pageCanvas as StateMachinePage)?.Canvas;

	public Data Graph => _graph;
	public Scene PreviewScene => _preview?.PreviewScene;
	internal SceneModel PreviewModel => _preview?.ModelRenderer?.SceneModel;
	internal bool IsPreviewPaused => _preview?.IsPaused ?? false;
	public bool CanOpenMultipleAssets => true;

	internal UndoSystem UndoSystem => _undoSystem;

	/// <summary> Warnings from the most recent compile, shown in the Compile Log dock. </summary>
	internal IReadOnlyList<Compiler.Warning> CompileWarnings { get; private set; } = [];

	/// <summary> Duration of the most recent compile in milliseconds, or null if nothing compiled yet. </summary>
	internal double? CompileTime { get; private set; }

	public Window()
	{
		DeleteOnClose = true;
		Size = new Vector2( 1400, 900 );

		SetWindowIcon( Pixmap.FromFile( "animgraph_editor/appicon.ico" ) );

		_session = Session.Create();
		_undoSystem = new UndoSystem();
		_undoSystem.Initialize();

		BuildUI();
		NavigateToRoot();
		UpdateTitle();
	}

	public void AssetOpen( Asset asset )
	{
		if ( asset is null || !asset.HasSourceFile )
			return;

		ConfirmUnsavedChanges( () =>
		{
			OpenAsset( asset );
			Show();
		} );
	}

	private void OpenAsset( Asset asset )
	{
		Session session;
		try
		{
			if ( !Session.TryLoad( asset, out session ) )
				Log.Warning( $"AnimGraph: {asset.Path} has no editor document, starting empty" );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"AnimGraph: failed to read {asset.Path}" );
			session = Session.Create( asset );
		}

		OpenSession( session );
	}

	private void OpenSession( Session session )
	{
		_session = session;
		EditorCookie.Set( LastGraphCookie, _asset.Path );
		AddRecentFile( _asset.Path );
		SetGraph( _session.Graph );

		_undoSystem.Initialize();
		_dirty = _session.HasRepairs;
		_previewCompileStale = _session.HasRepairs;
		UpdateTitle();
	}

	public override void Show()
	{
		// Don't clobber an unsaved untitled session with the remembered graph
		if ( _asset is null && !_dirty )
			RestoreLastGraph();

		base.Show();

		StateCookie = "AnimGraph";

		// Opening happens before first layout, so fit again now the view has its real size
		FitCurrentView();
	}

	public override void OnDestroyed()
	{
		_externalChangesDialog?.Destroy();
		InvalidatePreviewCompile();
		_preview?.ClearRecording();
		SetPreviewGraph( null );
		base.OnDestroyed();
	}

	internal void SetRecordingReviewMode( bool reviewing, bool paused ) => _preview?.SetRecordingReviewMode( reviewing, paused );
	internal bool ApplyRecordedUpdate( int updateIndex ) => _preview?.ApplyRecordedUpdate( updateIndex ) ?? false;

	public void SelectMember( string memberName ) { }

	private void SetGraph( Data graph )
	{
		_externalChangesDialog?.Destroy();
		_session.ReplaceGraph( graph );
		_parameterReferences = null;
		_committedState = graph.Serialize();
		_properties.Target = graph;
		_clipInfo = null;
		_clipList?.SetGraph( graph );
		_parameterList?.Rebuild();
		_variationEditor?.Rebuild();
		_preview?.ClearRecording();
		_preview?.RebuildModel();

		CompileWarnings = [];
		CompileTime = null;
		_compileLog?.Refresh();

		NavigateToRoot();
		_previewCompileStale = false;
		InvalidatePreviewCompile();
		UpdateSavedPreviewGraph();

		if ( _autoCompilePreviewEnabled )
			CompilePreview();
	}

	internal void ReflectChildParametersToParent( InternalReferencedGraphNode node )
	{
		if ( string.IsNullOrWhiteSpace( node.GraphPath ) )
		{
			Log.Warning( "AnimGraph: Cannot reflect parameters from an internal graph with no graph set" );
			return;
		}

		var asset = AssetSystem.FindByPath( node.GraphPath );
		if ( asset is null )
		{
			Log.Warning( $"AnimGraph: Cannot find internal graph '{node.GraphPath}'" );
			return;
		}

		try
		{
			if ( !Session.TryLoad( asset, out var childSession ) )
			{
				Log.Warning( $"AnimGraph: '{asset.Path}' has no editor document" );
				return;
			}

			var childGraph = childSession.Graph;
			var outputLog = new List<string>();
			PushGraphUndo( "Reflect Child Parameters" );
			var changed = _graph.ReflectParameters( childGraph, false, outputLog );
			PushGraphRedo();

			if ( changed )
				_parameterList.Rebuild();

			foreach ( var message in outputLog )
			{
				if ( message.Contains( "Incorrect", StringComparison.Ordinal ) )
					Log.Warning( $"AnimGraph: {message}" );
				else
					Log.Info( $"AnimGraph: {message}" );
			}
		}
		catch ( Exception exception )
		{
			Log.Warning( $"AnimGraph: Could not read parameters from '{asset.Path}': {exception.Message}" );
		}
	}

	[Shortcut( "editor.new", "CTRL+N" )]
	private void New() => ConfirmUnsavedChanges( () =>
	{
		_session = Session.Create();
		SetGraph( _session.Graph );

		_undoSystem.Initialize();
		_dirty = false;
		UpdateTitle();
	} );

	[Shortcut( "editor.open", "CTRL+O" )]
	private void Open()
	{
		var fd = new FileDialog( null )
		{
			Title = "Open AnimGraph",
			DefaultSuffix = ".ag",
			Directory = LastDialogDirectory( "ag" )
		};

		fd.SetNameFilter( "AnimGraph (*.ag)" );

		if ( !fd.Execute() )
			return;

		SetLastDialogDirectory( "ag", fd.SelectedFile );

		if ( AssetSystem.FindByPath( fd.SelectedFile ) is { } asset )
			AssetOpen( asset );
	}

	private static string LastDialogDirectory( string extension )
	{
		var dir = ProjectCookie.GetString( $"LastSaveLocation.{extension}", "" );
		return System.IO.Directory.Exists( dir ) ? dir : Project.Current.GetAssetsPath();
	}

	private static void SetLastDialogDirectory( string extension, string file )
	{
		if ( System.IO.Path.GetDirectoryName( file ) is { Length: > 0 } dir )
			ProjectCookie.SetString( $"LastSaveLocation.{extension}", dir );
	}

	private const string RecentFilesCookie = "animgraph.recentfiles";
	private const string LastGraphCookie = "animgraph.lastgraph";
	private const int MaxRecentFiles = 10;

	protected void RestoreLastGraph()
	{
		var path = EditorCookie.Get( LastGraphCookie, "" );
		if ( string.IsNullOrWhiteSpace( path ) )
			return;

		if ( AssetSystem.FindByPath( path ) is { HasSourceFile: true } asset )
		{
			OpenAsset( asset );
			return;
		}

		EditorCookie.Set( LastGraphCookie, "" );
	}

	private static void AddRecentFile( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return;

		var recent = EditorCookie.Get<List<string>>( RecentFilesCookie, [] );
		recent.RemoveAll( p => string.Equals( p, path, StringComparison.OrdinalIgnoreCase ) );
		recent.Insert( 0, path );

		if ( recent.Count > MaxRecentFiles )
			recent.RemoveRange( MaxRecentFiles, recent.Count - MaxRecentFiles );

		EditorCookie.Set( RecentFilesCookie, recent );
	}

	private void PopulateRecentFiles( Menu menu )
	{
		menu.Clear();

		// Entries whose asset no longer exists are dropped for good
		var recent = EditorCookie.Get<List<string>>( RecentFilesCookie, [] );
		var valid = recent.Where( p => AssetSystem.FindByPath( p ) is not null ).ToList();

		if ( valid.Count != recent.Count )
			EditorCookie.Set( RecentFilesCookie, valid );

		foreach ( var path in valid )
		{
			var open = path;
			menu.AddOption( open, "animation", () => AssetOpen( AssetSystem.FindByPath( open ) ) );
		}

		if ( valid.Count == 0 )
		{
			menu.AddOption( "No recent files", null ).Enabled = false;
			return;
		}

		menu.AddSeparator();
		menu.AddOption( "Clear Recent Files", "delete_sweep", () => EditorCookie.Set( RecentFilesCookie, new List<string>() ) );
	}

	[Shortcut( "editor.save", "CTRL+S" )]
	private void Save() => Save( _asset?.GetSourceFile( true ) );

	protected override bool OnClose()
	{
		if ( !_dirty )
			return true;

		ConfirmUnsavedChanges( () =>
		{
			_dirty = false;
			Close();
		} );

		return false;
	}

	/// <summary> Runs <paramref name="proceed"/> immediately, or after a save prompt when the session has unsaved changes. </summary>
	private void ConfirmUnsavedChanges( Action proceed )
	{
		if ( !_dirty )
		{
			proceed();
			return;
		}

		var name = _asset?.Name ?? "untitled";

		new PopupWindow( "Unsaved Changes", $"\"{name}\" has unsaved changes. Would you like to save now?", "Cancel",
			new Dictionary<string, Action>
			{
				{ "Don't Save", proceed },
				{ "Save", () => { if ( Save( _asset?.GetSourceFile( true ) ) ) proceed(); } }
			} ).Show();
	}

	private void SaveAs() => Save( null );

	/// <summary> Reveal the current graph's source file in the OS file browser. </summary>
	private void ShowInExplorer()
	{
		if ( _asset?.GetSourceFile( true ) is not { Length: > 0 } path )
			return;

		EditorUtility.OpenFileFolder( path );
	}

	private bool Save( string path )
	{
		if ( _saving )
			return false;

		if ( path is null )
		{
			var fd = new FileDialog( null )
			{
				Title = "Save AnimGraph",
				DefaultSuffix = ".ag"
			};

			fd.SetFindFile();
			fd.SetModeSave();
			fd.SetNameFilter( "AnimGraph (*.ag)" );

			// Re-saving an existing asset starts next to it, otherwise use the last dialog location
			if ( _asset?.GetSourceFile( true ) is { } sourceFile )
			{
				fd.SelectFile( sourceFile );
			}
			else
			{
				fd.Directory = LastDialogDirectory( "ag" );
				fd.SelectFile( "untitled.ag" );
			}

			if ( !fd.Execute() || string.IsNullOrWhiteSpace( fd.SelectedFile ) )
				return false;

			path = fd.SelectedFile;
			SetLastDialogDirectory( "ag", path );
		}

		var showPreviewCompile = _previewCompileStale;
		InvalidatePreviewCompile();
		_saving = true;

		if ( showPreviewCompile )
		{
			SetPreviewCompiling( true );
			Application.Spin();
		}

		Compiler.Result result = null;

		try
		{
			result = _session.Save( path );
		}
		finally
		{
			if ( showPreviewCompile )
				SetPreviewCompiling( false );

			_saving = false;

			if ( result is null && _previewCompileStale && _autoCompilePreviewEnabled )
				CompilePreview();
		}

		foreach ( var warning in result.Warnings )
		{
			if ( warning.IsError )
				Log.Error( $"AnimGraph: {warning}" );
			else
				Log.Warning( $"AnimGraph: {warning}" );
		}

		EditorCookie.Set( LastGraphCookie, _asset.Path );
		AddRecentFile( _asset.Path );
		MainAssetBrowser.Instance?.Local.UpdateAssetList();

		_dirty = false;
		_externalChangesDialog?.Destroy();
		UpdateTitle();

		if ( !_preview.IsValid() )
			return true;

		try
		{
			ApplyPreviewCompile( _session.LastCompile );
		}
		catch ( Exception e )
		{
			ApplyPreviewFailure( e, "AnimGraph: saved successfully but failed to update preview" );
		}

		return true;
	}

}
