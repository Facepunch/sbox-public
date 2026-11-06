namespace Editor.AnimGraph;

/// <summary>
/// Dock panel for editing graph variations - pick a variation from the tree, give it a model, and
/// choose which nodes override their data for it.
/// Ported from Esoterica's <c>AnimationGraphEditor::DrawVariationEditor</c>.
/// </summary>
public class VariationEditor : Widget
{
	private readonly Window _window;
	private readonly SectionHeader _treeHeader;
	private readonly SectionHeader _nodeHeader;
	private readonly Widget _treePanel;
	private readonly VariationTree _treeView;
	private readonly HashSet<string> _collapsed;
	private readonly IconButton _renameButton;
	private readonly IconButton _deleteButton;
	private readonly IconButton _overridesOnlyButton;
	private readonly Layout _pickerRow;
	private (Data Graph, string VariationID) _pickerBinding;
	private readonly LineEdit _filter;
	private Widget _rowsCanvas;
	private readonly Layout _rows;
	private readonly List<VariationNodeRow> _rowWidgets = new();
	private readonly Splitter _splitter;
	private bool _showOverridesOnly;
	private bool _hasVisibleNodes;
	private bool _hasNodes;

	private const string SplitterCookie = "AnimGraph.VariationSplitter";
	private const string CollapsedCookie = "AnimGraph.VariationsCollapsed";

	public VariationEditor( Window window ) : base( null )
	{
		_window = window;
		_collapsed = ProjectCookie.Get<List<string>>( CollapsedCookie, [] ).ToHashSet();

		Name = "Variations";
		WindowTitle = "Variations";
		SetWindowIcon( "call_split" );

		Layout = Layout.Column();
		Layout.Margin = 0;

		_splitter = new Splitter( this ) { IsVertical = true };
		Layout.Add( _splitter, 1 );

		var variationsSection = new Widget( _splitter ) { Layout = Layout.Column() };
		variationsSection.Layout.Spacing = 2;

		_treeHeader = AddSectionHeader( variationsSection, "Variations",
			"<b>Variations</b><p>Each variation animates its own model and can override the data of any node.</p><p>Click a variation to edit it, right-click for more options.</p>" );

		// Fills whatever the title and buttons don't need
		_pickerRow = _treeHeader.Layout.AddRow( 1 );

		_treeHeader.AddButton( "add", Theme.Green,
			"Create a new variation as a child of the selected one",
			() => _window.PromptCreateVariation( _window.SelectedVariationID ) );

		_renameButton = _treeHeader.AddButton( "edit", Theme.TextControl,
			"Rename the selected variation",
			() => _window.PromptRenameVariation( _window.SelectedVariationID ) );

		_deleteButton = _treeHeader.AddButton( "delete", Theme.Red,
			"Delete the selected variation and all of its children",
			() => _window.PromptDeleteVariation( _window.SelectedVariationID ) );

		_treePanel = AddPanel( variationsSection, () => null, 1 );

		// Expanding, indenting and scrolling all come from the tree view
		_treeView = _treePanel.Layout.Add( new VariationTree( _treePanel )
		{
			Margin = new Sandbox.UI.Margin( 2 ),
			IndentWidth = 12,
			ExpandWidth = 16,
		}, 1 );

		_treeView.BranchToggled = ( variationID, open ) =>
		{
			if ( open ) _collapsed.Remove( variationID );
			else _collapsed.Add( variationID );

			ProjectCookie.Set( CollapsedCookie, _collapsed.Order().ToList() );
		};

		_treeView.ItemSelected = item =>
		{
			if ( item is string variationID )
				_window.SetSelectedVariation( variationID );
		};

		_treeView.ItemActivated = item =>
		{
			if ( item is string variationID && variationID != Variation.DefaultVariationID )
				_window.PromptRenameVariation( variationID );
		};

		var nodesSection = new Widget( _splitter ) { Layout = Layout.Column() };
		nodesSection.Layout.Spacing = 2;

		_nodeHeader = AddSectionHeader( nodesSection, "Nodes",
			"<b>Nodes</b><p>Every node that can hold per variation data - clips, IK setups, referenced graphs.</p><p>Add an override to give the selected variation its own data, otherwise it inherits.</p>" );

		// Fills whatever the title and toggle don't need, like the parameter dock's filter
		_filter = _nodeHeader.Layout.Add( new LineEdit() { ClearButtonEnabled = true }, 1 );
		_filter.PlaceholderText = "⌕  Filter nodes..";
		_filter.FixedHeight = Theme.RowHeight;
		_filter.TextEdited += _ => FilterRows();

		_overridesOnlyButton = _nodeHeader.AddButton( "filter_alt", Theme.TextControl,
			"Only show nodes that override their data for this variation",
			null );
		_overridesOnlyButton.IsToggle = true;
		_overridesOnlyButton.OnToggled = active =>
		{
			_showOverridesOnly = active;
			FilterRows();
		};

		var (nodePanel, rowsCanvas, rows) = AddListPanel( nodesSection, () => _hasVisibleNodes ? null : EmptyHint, 1 );
		nodePanel.MinimumHeight = PillHeight * 2;
		_rowsCanvas = rowsCanvas;
		_rows = rows;

		_splitter.AddWidget( variationsSection );
		_splitter.AddWidget( nodesSection );
		_splitter.SetCollapsible( 0, false );
		_splitter.SetCollapsible( 1, false );
		_splitter.SetStretch( 0, 0 );
		_splitter.SetStretch( 1, 1 );

		if ( ProjectCookie.Get<string>( SplitterCookie, null ) is { Length: > 0 } state )
			_splitter.RestoreState( state );

		Rebuild();
	}

	public override void OnDestroyed()
	{
		ProjectCookie.Set( SplitterCookie, _splitter.SaveState() );
		base.OnDestroyed();
	}

	private string EmptyHint
	{
		get
		{
			if ( !_hasNodes )
				return "No nodes with per variation data";

			return _showOverridesOnly
				? "This variation overrides nothing yet"
				: "No nodes match the filter";
		}
	}

	public void Rebuild()
	{
		var graph = _window.Graph;
		var variationID = _window.SelectedVariationID;
		var isDefault = variationID == Variation.DefaultVariationID;

		_treeHeader.Title = "Variations";
		_treeHeader.Detail = $"{graph?.Variations?.All.Count ?? 0}";
		_renameButton.Enabled = !isDefault;
		_deleteButton.Enabled = !isDefault;
		_overridesOnlyButton.Visible = !isDefault;

		RebuildTree( graph );
		UpdateModelPicker( graph );
		RebuildRows( graph, variationID );
		FilterRows();
	}

	private void RebuildTree( Data graph )
	{
		_treeView.Clear();

		if ( graph?.Variations is null )
			return;

		var root = new VariationTreeNode( _window, Variation.DefaultVariationID );
		_treeView.AddItem( root );
		_treeView.Open( root, recursive: true );
		_treeView.SelectItem( _window.SelectedVariationID, skipEvents: true );

		// Last, so selecting doesn't expand a branch the user closed
		foreach ( var variationID in _collapsed )
			_treeView.Close( variationID );

		// The splitter owns the section's height now - just make sure a row is always visible
		_treePanel.MinimumHeight = PillHeight + 8;
	}

	/// <summary>
	/// The graph's target model and skeleton follow the selected variation, so these pickers edit
	/// whichever variation is active - Esoterica's per variation skeleton picker.
	/// </summary>
	private void UpdateModelPicker( Data graph )
	{
		var binding = (graph, _window.SelectedVariationID);
		if ( _pickerBinding == binding )
			return;

		_pickerBinding = binding;
		_pickerRow.Clear( true );

		if ( graph is null )
			return;

		var so = graph.GetSerialized();
		so.OnPropertyChanged += _ => _window.RebuildPreviewModel();
		so.OnPropertyFinishEdit += _ => _window.CommitChange( "Edit Variation Model" );

		AddPicker( so, nameof( Data.TargetModel ), $"The model the '{_window.SelectedVariationID}' variation animates" );
		AddPicker( so, nameof( Data.TargetSkeleton ), $"The skeleton the '{_window.SelectedVariationID}' variation's clips are compiled against" );
	}

	private void AddPicker( SerializedObject so, string propertyName, string toolTip )
	{
		var picker = ControlWidget.Create( so.GetProperty( propertyName ) );
		picker.MinimumWidth = Theme.RowHeight;
		picker.HorizontalSizeMode = SizeMode.Flexible;
		picker.FixedHeight = Theme.RowHeight;
		picker.ToolTip = toolTip;
		_pickerRow.Add( picker, 1 );
	}

	private void RebuildRows( Data graph, string variationID )
	{
		_hasVisibleNodes = false;

		var nodes = (graph?.AllNodes ?? [])
			.OfType<VariationDataNode>()
			.Select( node => (Node: node, Path: _window.GetNodePath( node )) )
			.ToList();

		_hasNodes = nodes.Count > 0;

		// Sorted by something that can't change while you edit - a clip node's display name is its
		// animation, so sorting by it would reshuffle the list every time you picked a new one
		var sorted = nodes
			.OrderBy( entry => entry.Path, StringComparer.OrdinalIgnoreCase )
			.ThenBy( entry => entry.Node.TypeDisplayInfo.Name, StringComparer.OrdinalIgnoreCase )
			.ThenBy( entry => entry.Node.Identifier, StringComparer.OrdinalIgnoreCase )
			.ToArray();

		if ( sorted.Select( entry => entry.Node.Identifier ).SequenceEqual( _rowWidgets.Select( row => row.Node.Identifier ) ) )
		{
			for ( var i = 0; i < sorted.Length; i++ )
				_rowWidgets[i].Bind( sorted[i].Node, sorted[i].Path, variationID );
			return;
		}

		using var _ = SuspendUpdates.For( _rowsCanvas );
		_rows.Clear( true );
		_rowWidgets.Clear();

		foreach ( var (node, path) in sorted )
		{
			var row = new VariationNodeRow( _window, this, node, path, variationID );
			_rowWidgets.Add( row );
			_rows.Add( row );
		}

		_rows.AddStretchCell();
	}

	private void FilterRows()
	{
		var filter = _filter.Text.Trim();
		var variationID = _window.SelectedVariationID;
		var overridesOnly = _showOverridesOnly && variationID != Variation.DefaultVariationID;
		var count = 0;

		foreach ( var row in _rowWidgets )
		{
			var visible = (!overridesOnly || row.Node.HasVariationOverride( variationID ))
				&& (string.IsNullOrEmpty( filter )
					|| row.Node.DisplayInfo.Name.Contains( filter, StringComparison.OrdinalIgnoreCase )
					|| row.Path.Contains( filter, StringComparison.OrdinalIgnoreCase ));

			row.Visible = visible;
			if ( visible )
				count++;
		}

		_hasVisibleNodes = count > 0;
		_nodeHeader.Detail = $"{count}";
		_rowsCanvas.Update();
	}

	internal void OnNodeEdited( Node node )
	{
		if ( node is not VariationDataNode )
			return;

		_rowWidgets.FirstOrDefault( row => row.Node == node )?.Refresh();

		if ( !string.IsNullOrWhiteSpace( _filter.Text ) )
			FilterRows();
	}

	internal bool IsSelected( VariationDataNode node ) => _window.IsSelected( node );

	/// <summary>
	/// Selecting a row targets the node in the inspector, where its data is edited.
	/// </summary>
	internal void SelectNode( VariationDataNode node )
	{
		_window.SelectVariationNode( node );
		UpdateSelection();
	}

	/// <summary>
	/// Repaint rows so selection highlights stay in sync with the inspector target.
	/// </summary>
	internal void UpdateSelection()
	{
		foreach ( var row in _rowWidgets )
			row.Update();
	}

	internal void OnGraphEdited() => Rebuild();

	private SectionHeader AddSectionHeader( Widget section, string title, string toolTip )
	{
		var header = new SectionHeader( section, title ) { ToolTip = toolTip };
		section.Layout.Add( header );
		return header;
	}

	/// <summary>
	/// Rows are a touch taller than a standard control row so the pills have some breathing room.
	/// </summary>
	internal static float PillHeight => Theme.RowHeight + 1;

	/// <summary>
	/// A panel with a centered hint drawn while it has nothing to show.
	/// </summary>
	private Widget AddPanel( Widget section, Func<string> emptyHint, int stretch )
	{
		var panel = new Widget( section ) { Layout = Layout.Column() };
		panel.Layout.Margin = new Sandbox.UI.Margin( 3, 2, 3, 2 );
		panel.OnPaintOverride = () =>
		{
			DockPanel.PaintBackground( panel.LocalRect );

			if ( emptyHint() is { } hint )
				DockPanel.PaintHint( panel.LocalRect, hint );

			return true;
		};

		section.Layout.Add( panel, stretch );
		return panel;
	}

	/// <summary>
	/// A scrollable list panel with a centered hint drawn while it has nothing to show.
	/// </summary>
	private (Widget Panel, Widget Canvas, Layout Rows) AddListPanel( Widget section, Func<string> emptyHint, int stretch )
	{
		var panel = AddPanel( section, emptyHint, stretch );

		var scroll = panel.Layout.Add( new ScrollArea( panel ), 1 );
		scroll.VerticalScrollbarMode = ScrollbarMode.Auto;
		scroll.HorizontalScrollbarMode = ScrollbarMode.Off;
		scroll.TranslucentBackground = true;
		scroll.NoSystemBackground = true;
		// Scope to the scroll area itself, otherwise the rules cascade into every
		// descendant - including the QToolTip popups shown for the rows inside it.
		scroll.Name = "AnimGraphVariationScroll";
		scroll.SetStyles( "#AnimGraphVariationScroll { border: none; background: transparent; }" );
		scroll.Canvas = new Widget( scroll )
		{
			Layout = Layout.Column(),
			HorizontalSizeMode = SizeMode.Flexible,
			VerticalSizeMode = SizeMode.CanGrow,
			TranslucentBackground = true,
			NoSystemBackground = true,
		};

		return (panel, scroll.Canvas, scroll.Canvas.Layout);
	}
}

/// <summary>
/// A tree view that reports when a branch is opened or closed, so the editor can remember it.
/// </summary>
internal sealed class VariationTree( Widget parent ) : TreeView( parent )
{
	/// <summary> Called with the variation's ID and whether it's now open. </summary>
	public Action<string, bool> BranchToggled { get; set; }

	protected override bool OnItemPressed( VirtualWidget item, MouseEvent e )
	{
		var wasOpen = item.IsOpen;

		// The base only swallows the press when it toggled the branch
		if ( base.OnItemPressed( item, e ) )
			return true;

		if ( item.Object is TreeNode<string> node )
			BranchToggled?.Invoke( node.Value, !wasOpen );

		return false;
	}
}

/// <summary>
/// One variation in the hierarchy tree - selecting it makes it the one being edited.
/// </summary>
internal sealed class VariationTreeNode : TreeNode<string>
{
	private readonly Window _window;
	private readonly List<string> _children;
	private readonly int _overrideCount;
	private readonly string _modelName;

	public VariationTreeNode( Window window, string variationID ) : base( variationID )
	{
		_window = window;
		_children = window.VariationHierarchy?.GetChildVariations( variationID ) ?? [];
		_overrideCount = window.CountVariationOverrides( variationID );
		_modelName = window.VariationHierarchy?.GetVariation( variationID )?.TargetModel?.Name;

		Height = VariationEditor.PillHeight;
	}

	public override bool HasChildren => _children.Count > 0;

	protected override void BuildChildren() => SetItems( _children.Select( id => new VariationTreeNode( _window, id ) ) );

	public override string GetTooltip()
	{
		var model = string.IsNullOrEmpty( _modelName ) ? "no model set" : _modelName;
		return $"<b>{Value}</b><p>Model: {model}</p><p>Overrides: {_overrideCount}</p><p>Click to edit this variation, right-click for more options.</p>";
	}

	public override bool OnContextMenu()
	{
		var menu = new Menu( TreeView );
		_window.BuildVariationContextMenu( menu, Value );
		menu.OpenAtCursor();
		return true;
	}

	public override void OnPaint( VirtualWidget item )
	{
		var selected = _window.SelectedVariationID == Value;

		// The pill starts after the expander, so the marker inside it can't sit under the arrow
		var pill = item.Rect.Shrink( 0, 1 );

		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( selected ? Theme.Primary.WithAlpha( 0.2f ) : item.Hovered ? Theme.ControlBackground.Lighten( 0.1f ) : Color.Transparent );
		Paint.DrawRect( pill, Theme.ControlRadius );

		if ( selected )
		{
			// A capsule tucked inside the pill, so it doesn't fight the rounded corners
			var marker = new Rect( pill.Left + 3, pill.Top + 3, 3, pill.Height - 6 );
			Paint.SetBrush( Theme.Primary );
			Paint.DrawRect( marker, marker.Width * 0.5f );
		}

		// Left inset clears the selection marker, right keeps the badge clear of the scrollbar
		var rect = item.Rect.Shrink( 12, 0, 14, 0 );

		Paint.SetPen( selected ? Theme.Primary : Theme.TextControl.WithAlpha( 0.6f ) );
		Paint.DrawIcon( rect, Parent is null ? "layers" : "subdirectory_arrow_right", 13, TextFlag.LeftCenter );

		rect = rect.Shrink( 18, 0, 0, 0 );

		Paint.SetPen( selected ? Theme.Text : Theme.TextControl );
		Paint.SetDefaultFont( 8, selected ? 600 : 400 );
		var nameRect = Paint.DrawText( rect, Value, TextFlag.LeftCenter );

		if ( _overrideCount > 0 )
		{
			var count = _overrideCount.ToString();
			Paint.SetDefaultFont( 7, 500 );
			var width = MathF.Max( 15, Paint.MeasureText( count ).x + 8 );
			var height = 14f;
			var badge = new Rect( rect.Right - width, rect.Center.y - height * 0.5f, width, height );

			Paint.ClearPen();
			Paint.SetBrush( Theme.TextControl.WithAlpha( 0.08f ) );
			Paint.DrawRect( badge, height * 0.5f );
			Paint.SetPen( Theme.TextControl.WithAlpha( 0.5f ) );
			Paint.DrawText( badge, count, TextFlag.Center );

			rect = rect.Shrink( 0, 0, width + 4, 0 );
		}

		if ( string.IsNullOrEmpty( _modelName ) )
			return;

		Paint.SetPen( Theme.TextControl.WithAlpha( 0.35f ) );
		Paint.SetDefaultFont( 8 );
		Paint.DrawText( rect.Shrink( nameRect.Width + 8, 0, 0, 0 ), System.IO.Path.GetFileName( _modelName ), TextFlag.LeftCenter );
	}
}

/// <summary>
/// One row of the node list - a node with per variation data, showing whether the selected
/// variation overrides it or inherits it, with a button to switch between the two.
/// </summary>
internal sealed class VariationNodeRow : Widget
{
	internal VariationDataNode Node => _node;
	internal string Path => _path;

	private readonly Window _window;
	private readonly VariationEditor _editor;
	private VariationDataNode _node;
	private string _path;
	private string _variationID;
	private string _source;
	private string _icon;
	private Color _color;
	private bool _hasOverride;
	private bool _isDefault;
	private IconButton _overrideButton;

	public VariationNodeRow( Window window, VariationEditor editor, VariationDataNode node, string path, string variationID ) : base( editor )
	{
		_window = window;
		_editor = editor;

		FixedHeight = VariationEditor.PillHeight;
		Cursor = CursorShape.Finger;

		Layout = Layout.Row();
		Layout.Margin = new Sandbox.UI.Margin( 6, 0, 12, 0 );
		Layout.Spacing = 4;
		Layout.AddStretchCell();

		Bind( node, path, variationID );
	}

	internal void Bind( VariationDataNode node, string path, string variationID )
	{
		_node = node;
		_path = path;
		_variationID = variationID;
		Refresh();
	}

	internal void Refresh()
	{
		_isDefault = _variationID == Variation.DefaultVariationID;
		_hasOverride = _node.HasVariationOverride( _variationID );
		_source = _isDefault ? null : _window.GetVariationDataSource( _node, _variationID );
		_icon = _node.DisplayInfo.Icon ?? _node.TypeDisplayInfo.Icon ?? "account_tree";
		_color = _node.PrimaryColor;

		ToolTip = _isDefault
			? $"{_path} / {_node.DisplayInfo.Name}\nClick to edit its data in the inspector, double-click to jump to the node"
			: _hasOverride
				? $"{_path} / {_node.DisplayInfo.Name}\nOverridden for '{_variationID}' - click to edit its data in the inspector"
				: $"{_path} / {_node.DisplayInfo.Name}\nInherits its data from '{_source}'";
		UpdateOverrideButton();
		Update();
	}

	private void UpdateOverrideButton()
	{
		if ( !_isDefault )
		{
			_overrideButton ??= Layout.Add( new IconButton( "edit_note" )
			{
				IconSize = 14,
				FixedSize = Theme.RowHeight - 2,
				Background = Color.Transparent,
				OnClick = () => _window.SetVariationOverride( _node, _variationID, !_hasOverride )
			} );
		}

		if ( _overrideButton is null )
			return;

		_overrideButton.Visible = !_isDefault;
		_overrideButton.Icon = _hasOverride ? "undo" : "edit_note";
		_overrideButton.ToolTip = _hasOverride
			? $"Remove the override so '{_variationID}' inherits this data again"
			: $"Give '{_variationID}' its own copy of this data";
		_overrideButton.Foreground = _hasOverride ? Theme.TextControl.WithAlpha( 0.6f ) : Theme.Green;
	}

	protected override void OnMouseEnter() => Update();
	protected override void OnMouseLeave() => Update();

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !e.LeftMouseButton )
			return;

		_editor.SelectNode( _node );
		e.Accepted = true;
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		_window.NavigateToNode( _node.Identifier );
		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		var selected = _editor.IsSelected( _node );

		// Nodes that inherit their data for this variation are dimmed, like upstream
		var inherited = !_isDefault && !_hasOverride;
		var unset = _node.GetResolvedVariationData( _window.Graph?.Variations, _variationID )?.GetVisualState()
			== VariationDataNode.VariationData.VisualState.HasUnsetData;

		// Inherited rows fade back so the overrides are what stands out
		var fade = inherited ? 0.45f : 1f;
		var pill = LocalRect.Shrink( 0, 1 );

		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( _color.WithAlpha( (selected ? 0.25f : IsUnderMouse ? 0.18f : 0.1f) * fade ) );
		Paint.DrawRect( pill, Theme.ControlRadius );

		if ( selected )
		{
			Paint.ClearBrush();
			Paint.SetPen( _color.WithAlpha( 0.8f ) );
			Paint.DrawRect( pill.Shrink( 0.5f ), Theme.ControlRadius );
		}

		Paint.SetPen( (unset ? Theme.Red : _color).WithAlpha( fade ) );
		Paint.DrawIcon( new Rect( pill.Left + 6, pill.Top, 16, pill.Height ), _icon, 15, TextFlag.Center );

		var textColor = unset ? Theme.Red : inherited ? Theme.TextControl.WithAlpha( 0.4f ) : Theme.TextControl;

		var rect = LocalRect.Shrink( 28, 0, _isDefault ? 12 : Theme.RowHeight + 12, 0 );

		// Where inherited data comes from is worth spelling out, an override is obvious from the row itself
		if ( inherited )
		{
			Paint.SetDefaultFont( 8 );
			var sourceWidth = Paint.MeasureText( _source ).x;

			Paint.SetPen( Theme.TextControl.WithAlpha( 0.3f ) );
			Paint.DrawIcon( new Rect( rect.Right - sourceWidth - 18, rect.Top, 14, rect.Height ), "link", 12, TextFlag.Center );

			Paint.SetPen( Theme.TextControl.WithAlpha( 0.35f ) );
			Paint.DrawText( rect, _source, TextFlag.RightCenter );

			rect = rect.Shrink( 0, 0, sourceWidth + 22, 0 );
		}

		Paint.SetPen( textColor );
		Paint.SetDefaultFont( 8, selected ? 600 : 400 );
		var nameRect = Paint.DrawText( rect, _node.DisplayInfo.Name, TextFlag.LeftCenter );

		if ( string.IsNullOrEmpty( _path ) )
			return;

		Paint.SetPen( textColor.WithAlpha( 0.45f ) );
		Paint.SetDefaultFont( 8 );
		Paint.DrawText( rect.Shrink( nameRect.Width + 8, 0, 0, 0 ), _path, TextFlag.LeftCenter );
	}
}
