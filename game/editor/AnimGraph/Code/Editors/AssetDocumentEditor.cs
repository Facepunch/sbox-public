namespace Editor.AnimGraph;

public abstract class AssetDocumentEditor : DockWindow, IAssetEditor, AssetSystem.IEventListener
{
	private AnimationDocumentSession _session;
	private Asset _asset;
	private DockWidget _previewDock;
	private Label _error;
	private Option _save;
	private Option _undo;
	private Option _redo;
	private bool _allowClose;
	protected Asset Asset => _asset;
	private protected AnimationDocumentSession Session => _session;
	protected DockWidget PreviewDock => _previewDock;
	protected abstract string EditorName { get; }
	protected abstract string LayoutCookie { get; }
	private protected abstract IAnimationDocument LoadDocument( string path );
	protected abstract void CreateEditor( Widget center, ScrollArea settings );
	protected abstract void RefreshDocument();
	protected abstract void UpdatePreview();
	protected abstract void RefreshCompiledAsset();

	public bool CanOpenMultipleAssets => false;

	protected AssetDocumentEditor()
	{
		DeleteOnClose = true;
		Size = new Vector2( 1280, 850 );
		MinimumSize = new Vector2( 800, 500 );

		var file = MenuBar.FindOrCreateMenu( "File" );
		_save = file.AddOption( "Save", "common/save.png", SaveShortcut, "editor.save" );
		file.AddOption( "Revert Changes", "restore", Revert );
		file.AddSeparator();
		file.AddOption( "Close", "close", Close );
		var edit = MenuBar.FindOrCreateMenu( "Edit" );
		_undo = edit.AddOption( "Undo", "undo", Undo, "editor.undo" );
		_redo = edit.AddOption( "Redo", "redo", Redo, "editor.redo" );
		var view = MenuBar.FindOrCreateMenu( "View" );
		view.AboutToShow += () => CreateDynamicViewMenu( view );
		var toolbar = new ToolBar( this, "AnimationAssetToolbar" );
		toolbar.AddOption( _save );
		toolbar.AddSeparator();
		toolbar.AddOption( _undo );
		toolbar.AddOption( _redo );
		AddToolBar( toolbar );
		_save.Enabled = _undo.Enabled = _redo.Enabled = false;
	}

	public void AssetOpen( Asset asset )
	{
		_asset = asset;
		_session = AnimationDocumentSession.Acquire( asset, LoadDocument );
		_session.Changed += OnDocumentChanged;
		SetWindowIcon( asset.AssetType.Icon128 );

		var center = new Widget( this ) { Layout = Layout.Column(), MinimumSize = new Vector2( 240, 200 ) };
		_error = center.Layout.Add( new Label( center ) { Color = Theme.Red, WordWrap = true, Margin = 8, Visible = false } );
		var settings = new ScrollArea( this )
		{
			HorizontalScrollbarMode = ScrollbarMode.Off,
			TranslucentBackground = true,
			NoSystemBackground = true,
			MinimumWidth = 320
		};
		settings.SetStyles( "QScrollArea { border: 0px; }" );
		CreateEditor( center, settings );
		_previewDock = DockManager.SetCentralWidget( center );
		_previewDock.MinimumSizeFromContent = true;
		DockManager.AddDock( "Settings", "tune", settings, DockArea.Hidden ).MinimumSizeFromContent = true;
		CreateDocumentDocks();
		OnDocumentChanged( null );
		Show();
		StateCookie = LayoutCookie;
	}

	protected override void BuildDefaultLayout()
	{
		var settings = DockManager.OpenDock( "Settings", DockArea.Right, _previewDock );
		BuildDocumentLayout( settings );
		DockManager.SetSplitterProportions( settings, 0.68f, 0.32f );
	}

	protected void Commit( string name ) => _session.Commit( name, this );
	protected void OnPreviewModelChanged() => Commit( "Set Preview Model" );
	protected virtual void CreateDocumentDocks() { }
	protected virtual void BuildDocumentLayout( DockWidget settings ) { }
	protected virtual bool ValidateDocument() => true;
	protected virtual void OnDocumentClosing() { }

	protected void OnDocumentChanged( object source )
	{
		if ( source != this )
			RefreshDocument();
		_error.Visible = false;
		_save.Enabled = !_session.ReadOnly;
		_undo.Enabled = _session.CanUndo;
		_redo.Enabled = _session.CanRedo;
		WindowTitle = $"{_asset.Name}{(_asset.HasUnsavedChanges ? " *" : "")} - {EditorName} Editor{(_session.ReadOnly ? " (Read Only)" : "")}";
		UpdatePreview();
	}

	void AssetSystem.IEventListener.OnAssetChanged( Asset asset )
	{
		if ( asset != _asset )
			return;

		RefreshCompiledAsset();
	}

	protected virtual void FinishEditing()
	{
		if ( Application.FocusWidget is { } focus )
		{
			var roots = DockManager.DockTypes.Select( dock => DockManager.FindDockWidget( dock.Title )?.Widget ).Prepend( this );
			if ( roots.Any( root => root.IsValid() && (root == focus || root.IsAncestorOf( focus )) ) )
				focus.Blur();
		}
	}

	private bool Save()
	{
		FinishEditing();
		if ( _session.ReadOnly )
			return false;

		if ( !ValidateDocument() )
			return false;

		if ( !_session.Save( this ) )
			return ShowError( "Couldn't save this animation asset. See the console for details." );

		_asset.Compile( false );
		return true;
	}

	protected bool ShowError( string message )
	{
		_error.Text = message;
		_error.Visible = true;
		return false;
	}

	[Shortcut( "editor.save", "CTRL+S" )]
	protected void SaveShortcut() => Save();

	[Shortcut( "editor.undo", "CTRL+Z", ShortcutType.Window )]
	protected void Undo()
	{
		FinishEditing();
		_session?.Undo();
	}

	[Shortcut( "editor.redo", "CTRL+Y", ShortcutType.Window )]
	protected void Redo()
	{
		FinishEditing();
		_session?.Redo();
	}

	private void Revert()
	{
		FinishEditing();
		if ( _session.ReadOnly || !_asset.HasUnsavedChanges )
			return;

		new PopupWindow( "Revert Changes", $"Discard changes to '{_asset.Name}'?", "Cancel",
			new Dictionary<string, Action> { { "Revert", () => _session.Reset() } } ).Show();
	}

	protected override bool OnClose()
	{
		FinishEditing();
		if ( _allowClose || _session is null || !_asset.HasUnsavedChanges )
			return true;

		new PopupWindow( "Unsaved Changes", $"Save changes to '{_asset.Name}' before closing?", "Cancel",
			new Dictionary<string, Action>
			{
				{ "Discard", () => { _session.Reset(); _allowClose = true; Close(); } },
				{ "Save", () => { if ( Save() ) { _allowClose = true; Close(); } } }
			} ).Show();
		return false;
	}

	public void SelectMember( string memberName ) { }

	public override void OnDestroyed()
	{
		if ( _session is not null )
		{
			_session.Changed -= OnDocumentChanged;
			OnDocumentClosing();
			_session.Release();
		}
		base.OnDestroyed();
	}
}
