namespace Editor.AnimGraph;

public partial class Window
{
	private readonly HashSet<string> _searchActiveTypes = new();
	private readonly HashSet<string> _searchExcludedTypes = new();
	private DockWidget _centralDock;
	private ToolBar _toolBar;
	private IconButton _findGraphButton;
	private bool _viewMenuWired;
	private Option _compilePreviewOption;
	private Option _variationOption;
	private Option _showInExplorerOption;
	private OptionPair _undo, _redo, _cut, _copy, _paste, _save;

	[EditorEvent.Hotload]
	public void OnHotload()
	{
		_graphView.PrepareForRebuild();
		_workspace.SecondaryView.PrepareForRebuild();
		(_pageCanvas as INodePage)?.EmbeddedGraphView?.PrepareForRebuild();
		CommitChange( _pendingUndoName, _pendingChangeScope );

		Data.InvalidateSerializationCache();
		InvalidatePreviewCompile();
		_debugSampler.Clear();
		_preview.ClearRecording();
		_previewVariationResults.Clear();
		_previewCompileStale = true;

		foreach ( var node in _graph.AllNodes )
			node.RefreshMetadata();

		using var _ = SuspendUpdates.For( DockManager );
		_pageCanvas = null;
		_shownPage = null;
		_breadcrumbShown = null;
		_clipInfo = null;
		BuildUI();

		ShowPage();
		_preview.SetAnimationGraph( _previewGraph );
		ApplyPreviewParameters();

		if ( _autoCompilePreviewEnabled )
			CompilePreview();
	}

	private Widget BuildGraphCanvas()
	{
		var graphCanvas = new Widget( this );
		graphCanvas.Layout = Layout.Column();

		_navButtons.Clear();
		_breadcrumb = new Widget( graphCanvas );
		_breadcrumb.FixedHeight = Theme.RowHeight + 2;
		_breadcrumb.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.SurfaceBackground );
			Paint.DrawRect( _breadcrumb.LocalRect );
			return true;
		};

		var navRow = _breadcrumb.Layout = Layout.Row();
		navRow.Margin = new Sandbox.UI.Margin( 0, 0, 0, 2 );
		navRow.Spacing = 2;

		NavButton AddNav( string icon, string tooltip, Action onClick, Func<bool> enabled )
		{
			var button = navRow.Add( new NavButton( icon, tooltip, onClick, enabled ) );
			_navButtons.Add( button );
			return button;
		}

		AddNav( "arrow_back", "Back to previous location", GoBack, () => _navBack.Count > 0 );
		AddNav( "arrow_forward", "Forward to next location", GoForward, () => _navForward.Count > 0 );
		AddNav( "arrow_upward", "Go up one level", GoUp, () => _nav.Count > 1 );
		AddNav( "home", "Go to graph root", GoHome, () => _nav.Count > 1 );

		var sep = new Widget( _breadcrumb ) { FixedWidth = 5 };
		sep.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.TextLight.WithAlpha( 0.2f ) );
			Paint.DrawRect( new Rect( 2, 6, 1, sep.Height - 12 ) );
			return true;
		};
		navRow.Add( sep );

		_crumbs = navRow.Add( new BreadcrumbBar( _breadcrumb ), 1 );
		_findGraphButton = navRow.Add( new IconButton( "search", OpenSearch, _breadcrumb )
		{
			Background = Color.Transparent,
			Foreground = Theme.TextControl,
			IconSize = 14,
			FixedWidth = 24
		} );
		UpdateNavigationTooltips();
		navRow.Add( new IconButton( "fit_screen", FitCurrentView, _breadcrumb )
		{
			Background = Color.Transparent,
			Foreground = Theme.TextControl,
			IconSize = 14,
			ToolTip = "Fit all nodes in view",
			FixedWidth = 24
		} );
		graphCanvas.Layout.Add( _breadcrumb );

		_workspace = new Workspace( graphCanvas, this );
		_pageHost = new Widget( _workspace );
		_pageHost.Layout = Layout.Column();
		_workspace.SetPrimary( _pageHost );
		graphCanvas.Layout.Add( _workspace, 1 );

		_graphView = new View( this, _pageHost );
		_graphView.Graph = _graph;
		_pageHost.Layout.Add( _graphView, 1 );

		return graphCanvas;
	}

	[Event( "keybinds.update" )]
	private void UpdateNavigationTooltips()
	{
		if ( !_findGraphButton.IsValid() )
			return;

		var keys = EditorShortcuts.GetDisplayKeys( "animgraph.find" );
		_findGraphButton.ToolTip = string.IsNullOrEmpty( keys ) ? "Find in Graph" : $"Find in Graph [{keys}]";
	}

	private void BuildUI()
	{
		BuildMenuBar();
		BuildToolbar();

		var graphCanvas = BuildGraphCanvas();

		_properties = new Properties( this );
		_properties.Target = _graph;
		_properties.PropertyUpdated += OnPropertyUpdated;
		_properties.TargetChanged += () =>
		{
			_parameterList?.UpdateSelection();
			_variationEditor?.UpdateSelection();
		};

		_clipList = new ClipList( _graph );
		_clipList.OnClipSelected = OnClipSelected;
		_clipList.Rebuild();

		_parameterList = new ParameterList( this );
		_parameterList.Rebuild();

		_variationEditor = new VariationEditor( this );

		_preview = new Preview( this );

		var history = new History( this );
		_compileLog = new CompileLog( this );

		if ( _centralDock is null )
		{
			_centralDock = DockManager.SetCentralWidget( graphCanvas );
		}
		else
		{
			var previous = _centralDock.Widget;
			_centralDock.Widget = graphCanvas;
			previous.Destroy();
		}

		void AddDock( string title, string icon, Widget widget )
		{
			DockManager.RegisterDockType( new() { Title = title, Icon = icon, Area = DockArea.Hidden, CreateAction = () => widget } );
			DockManager.AddDock( title, icon, widget, DockArea.Hidden ).MinimumSizeFromContent = true;
		}

		AddDock( "Preview", "visibility", _preview );
		AddDock( "Parameters", "tune", _parameterList );
		AddDock( "Properties", "edit", _properties );
		AddDock( "Animation Clips", "movie", _clipList );
		AddDock( "Variations", "call_split", _variationEditor );
		AddDock( "History", "history", history );
		AddDock( "Compile Log", "rule", _compileLog );
	}

	protected override void BuildDefaultLayout()
	{
		var preview = DockManager.OpenDock( "Preview", DockArea.Left );
		DockManager.SetSplitterProportions( preview, 0.2f, 0.8f );

		var properties = DockManager.OpenDock( "Properties", DockArea.Right );
		var compileLog = DockManager.OpenDock( "Compile Log", DockArea.Bottom, _centralDock );
		var parameters = DockManager.OpenDock( "Parameters", DockArea.Bottom, preview );
		var clips = DockManager.OpenDock( "Animation Clips", DockArea.Bottom, properties );
		DockManager.OpenDock( "Variations", DockArea.Center, clips );
		DockManager.OpenDock( "History", DockArea.Center, clips );

		DockManager.SetSplitterProportions( parameters, 0.68f, 0.32f );
		DockManager.SetSplitterProportions( clips, 0.62f, 0.38f );
		DockManager.SetSplitterProportions( compileLog, 0.75f, 0.25f );

		DockManager.RaiseDock( "Animation Clips" );
		DockManager.RaiseDock( "Compile Log" );
	}

	private void BuildMenuBar()
	{
		var file = MenuBar.FindOrCreateMenu( "File" );
		file.Clear();
		file.AddOption( "New", "common/new.png", New, "editor.new" );
		file.AddOption( "New from Model...", "movie", () => new CreateGraphDialog( LastDialogDirectory( "ag" ) ).Show() );
		file.AddOption( "Open", "common/open.png", Open, "editor.open" );

		var recent = file.AddMenu( "Recent Files", "history" );
		recent.AboutToShow += () => PopulateRecentFiles( recent );

		file.AddSeparator();
		_save.Menu = file.AddOption( "Save", "common/save.png", Save, "editor.save" );
		file.AddOption( "Save As...", "save_as", SaveAs );

		file.AddSeparator();
		_showInExplorerOption = file.AddOption( "Show in Explorer", "folder_open", ShowInExplorer );

		var edit = MenuBar.FindOrCreateMenu( "Edit" );
		edit.Clear();
		_undo.Menu = edit.AddOption( "Undo", "undo", () => Undo(), "editor.undo" );
		_redo.Menu = edit.AddOption( "Redo", "redo", () => Redo(), "editor.redo" );
		edit.AddSeparator();
		_cut.Menu = edit.AddOption( "Cut", "common/cut.png", CutSelection, "editor.cut" );
		_copy.Menu = edit.AddOption( "Copy", "common/copy.png", CopySelection, "editor.copy" );
		_paste.Menu = edit.AddOption( "Paste", "common/paste.png", PasteSelection, "editor.paste" );
		edit.AddOption( "Select All", "select_all", SelectAll, "editor.select-all" );
		edit.AddSeparator();
		edit.AddOption( "Find in Graph", "search", OpenSearch, "animgraph.find" );

		var view = MenuBar.FindOrCreateMenu( "View" );

		if ( !_viewMenuWired )
		{
			_viewMenuWired = true;
			view.AboutToShow += () => OnViewMenu( view );
		}
	}

	[EditorEvent.Frame]
	private void UpdateOptionStates()
	{
		if ( !Visible )
			return;

		var graph = FocusedGraphView;
		var hasGraphSelection = graph?.SelectedItems.OfType<NodeEditor.NodeUI>().Any() ?? false;
		var page = FocusedNodePage;

		_undo.Enabled = _undoSystem.Back.Count > 0;
		_redo.Enabled = _undoSystem.Forward.Count > 0;
		_cut.Enabled = page?.CanCutSelection == true || hasGraphSelection;
		_copy.Enabled = page?.CanCopySelection == true || hasGraphSelection;
		_paste.Enabled = page?.CanPasteSelection == true || graph?.CanPasteSelection() == true;
		_save.Enabled = _dirty;

		if ( _showInExplorerOption is not null )
			_showInExplorerOption.Enabled = _asset?.HasSourceFile == true;
	}

	private void OnViewMenu( Menu view )
	{
		view.Clear();
		view.AddOption( "Restore To Default", "settings_backup_restore", ResetLayout );
		view.AddSeparator();

		foreach ( var dock in DockManager.DockTypes )
		{
			var option = view.AddOption( dock.Title, dock.Icon );
			option.Checkable = true;
			option.Checked = DockManager.IsDockOpen( dock.Title );
			option.Toggled += open => DockManager.SetDockState( dock.Title, open );
		}

		view.AddSeparator();

		var style = view.AddOption( "Grid-Aligned Wires", "turn_sharp_right" );
		style.Checkable = true;
		style.Checked = View.EnableGridAlignedWires;
		style.Toggled += enabled => View.EnableGridAlignedWires = enabled;

		view.AddOption( "Compile Preview", "refresh", CompilePreview );

		var autoCompile = view.AddOption( "Auto Compile Preview", "sync" );
		autoCompile.Checkable = true;
		autoCompile.Checked = _autoCompilePreviewEnabled;
		autoCompile.FetchCheckedState = () => _autoCompilePreviewEnabled;
		autoCompile.Toggled += SetAutoCompilePreviewEnabled;
	}

	private void BuildToolbar()
	{
		if ( _toolBar is not null )
		{
			RemoveToolBar( _toolBar );
			_toolBar.Destroy();
		}

		_toolBar = new ToolBar( this, "AnimGraphToolbar" );
		_toolBar.MinimumSize = 24;
		_toolBar.SetIconSize( 24 );
		AddToolBar( _toolBar, ToolbarPosition.Top );

		_toolBar.AddOption( new Option( "New", "common/new.png", New ) { ToolTip = "New", ShortcutName = "editor.new" } );
		_toolBar.AddOption( new Option( "Open", "common/open.png", Open ) { ToolTip = "Open", ShortcutName = "editor.open" } );
		_save.Toolbar = _toolBar.AddOption( new Option( "Save", "common/save.png", Save ) { ToolTip = "Save", ShortcutName = "editor.save" } );

		_toolBar.AddSeparator();

		_undo.Toolbar = _toolBar.AddOption( new Option( "Undo", "undo", () => Undo() ) { ToolTip = "Undo", ShortcutName = "editor.undo" } );
		_redo.Toolbar = _toolBar.AddOption( new Option( "Redo", "redo", () => Redo() ) { ToolTip = "Redo", ShortcutName = "editor.redo" } );

		_toolBar.AddSeparator();

		_cut.Toolbar = _toolBar.AddOption( new Option( "Cut", "common/cut.png", CutSelection ) { ToolTip = "Cut", ShortcutName = "editor.cut" } );
		_copy.Toolbar = _toolBar.AddOption( new Option( "Copy", "common/copy.png", CopySelection ) { ToolTip = "Copy", ShortcutName = "editor.copy" } );
		_paste.Toolbar = _toolBar.AddOption( new Option( "Paste", "common/paste.png", PasteSelection ) { ToolTip = "Paste", ShortcutName = "editor.paste" } );

		_toolBar.AddSeparator();
		_compilePreviewOption = new Option( "Compile Preview", _previewCompiling ? "schedule" : "refresh", CompilePreview )
		{
			ToolTip = _previewCompiling ? "Compiling preview..." : "Compile Preview",
			Enabled = !_previewCompiling,
		};
		_toolBar.AddOption( _compilePreviewOption );

		_toolBar.AddSeparator();

		_variationOption = new Option( $"Variation: {SelectedVariationID}", "call_split", OpenVariationMenu )
		{
			ToolTip = "<b>Variation</b><p>The variation being edited - it decides the preview model and which node data you see.</p><p>Manage variations in the Variations dock.</p>",
		};
		_toolBar.AddOption( _variationOption );
	}

	private void OpenVariationMenu()
	{
		var menu = new Menu( this );
		BuildVariationMenu( menu );
		menu.OpenAtCursor();
	}

	/// <summary>
	/// Keep the toolbar's variation label in sync with the selection.
	/// </summary>
	internal void UpdateVariationOption()
	{
		if ( _variationOption is not null )
			_variationOption.Text = $"Variation: {SelectedVariationID}";
	}

	[Shortcut( "animgraph.find", "CTRL+F", ShortcutType.Window )]
	private void OpenSearch()
	{
		var popup = new SearchPopup( this, _searchActiveTypes, _searchExcludedTypes );
		var position = _breadcrumb.ToScreen( new Vector2( _breadcrumb.Width - popup.Width, _breadcrumb.Height ) );
		popup.OpenAt( position, false );
	}

	private struct OptionPair
	{
		public Option Menu;
		public Option Toolbar;

		public bool Enabled
		{
			set
			{
				if ( Menu is not null ) Menu.Enabled = value;
				if ( Toolbar is not null ) Toolbar.Enabled = value;
			}
		}
	}
}

file class NavButton : Widget
{
	private readonly string _icon;
	private readonly Action _onClick;
	private readonly Func<bool> _enabled;

	public NavButton( string icon, string tooltip, Action onClick, Func<bool> enabled ) : base( null )
	{
		_icon = icon;
		_onClick = onClick;
		_enabled = enabled;

		ToolTip = tooltip;
		FixedWidth = 24;
	}

	protected override void OnMouseEnter() => Update();
	protected override void OnMouseLeave() => Update();

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !e.LeftMouseButton || !_enabled() )
			return;

		_onClick();
		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		var enabled = _enabled();
		var hovered = enabled && IsUnderMouse;

		Cursor = enabled ? CursorShape.Finger : CursorShape.Arrow;

		Paint.ClearPen();
		Paint.SetBrush( hovered ? Theme.ControlBackground.Lighten( 0.1f ) : Theme.ControlBackground );
		Paint.DrawRect( LocalRect, Theme.ControlRadius );

		Paint.ClearBrush();
		Paint.SetPen( enabled ? Theme.Primary : Theme.TextControl.WithAlpha( 0.25f ) );
		Paint.DrawIcon( LocalRect, _icon, 14, TextFlag.Center );
	}
}

internal sealed class BreadcrumbButton : Widget
{
	private string _title;
	private string _icon;
	private Color? _tint;
	private readonly Action _onClick;
	private readonly bool _isCurrent;

	public float ContentWidth { get; private set; }

	public BreadcrumbButton( string title, string icon, Color? tint, Action onClick, bool? isCurrent = null ) : base( null )
	{
		_onClick = onClick;
		_isCurrent = isCurrent ?? onClick is null;

		Cursor = onClick is not null ? CursorShape.Finger : CursorShape.Arrow;
		SetContent( title, icon, tint );
	}

	public void SetContent( string title, string icon, Color? tint = null )
	{
		_title = title;
		_icon = icon;
		_tint = tint;
		ToolTip = title;

		Paint.SetDefaultFont( 8, _isCurrent ? 600 : 400 );
		ContentWidth = Paint.MeasureText( _title ).x + (string.IsNullOrEmpty( _icon ) ? 4 : 26);
		FixedWidth = ContentWidth;
		Update();
	}

	protected override void OnMouseEnter() => Update();
	protected override void OnMouseLeave() => Update();

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LeftMouseButton )
		{
			_onClick?.Invoke();
			e.Accepted = true;
		}
	}

	protected override void OnPaint()
	{
		var color = _isCurrent ? Theme.Text
			: _onClick is not null && IsUnderMouse ? Theme.Blue
			: Theme.TextControl;

		Paint.SetPen( _tint ?? color );
		var textOffset = 0f;
		if ( !string.IsNullOrEmpty( _icon ) )
		{
			Paint.DrawIcon( new Rect( 0, 0, 18, Height ), _icon, 14, TextFlag.Center );
			textOffset = 22;
		}

		Paint.SetDefaultFont( 8, _isCurrent ? 600 : 400 );
		var textWidth = Math.Max( 0, Width - textOffset );
		var title = Paint.GetElidedText( _title, textWidth, ElideMode.Right );
		Paint.DrawText( new Rect( textOffset, 0, textWidth, Height ), title, TextFlag.LeftCenter );
	}
}
