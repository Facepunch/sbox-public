using System.Text;
using System.Text.RegularExpressions;

namespace Editor.ShaderGraph;

public class BlackboardView : Widget
{
	bool queryDirty = false;
	Layout Header;
	Layout SubHeader;
	LineEdit Search;
	ToolButton SearchClear;

	private TreeView _treeView;
	private BlackboardParameter _selectedParameter;
	private bool _hasSelection => _selectedParameter != null;

	private readonly MainWindow _window;
	private readonly UndoStack _undoStack;
	private readonly Dictionary<string, IBlackboardParameterType> AvailableParameters = new( StringComparer.OrdinalIgnoreCase );
	private readonly SelectionSystem Selection = new SelectionSystem();

	public Action OnDirty { get; set; }

	/// <summary>
	/// Called after a parameter bound node has been deleted.
	/// </summary>
	public Action OnParameterNodeDeleted { get; set; }

	private ShaderGraph _graph;
	public ShaderGraph Graph
	{
		get => _graph;
		set
		{
			if ( value == null ) return;
			if ( _graph == value ) return;

			_graph = value;
		}
	}

	public BlackboardView( Widget parent, MainWindow window ) : base( parent )
	{
		Layout = Layout.Column();

		_window = window;
		_undoStack = window.UndoStack;

		BuildUI();
	}

	public void BuildUI()
	{
		Layout.Clear( true );
		Header = Layout.AddColumn();

		SubHeader = Layout.AddRow();
		SubHeader.Spacing = 2;
		SubHeader.Margin = new Sandbox.UI.Margin( 0, 2 );
		SubHeader.Alignment = TextFlag.LeftCenter;

		var add = SubHeader.Add( new AddButton( "add" ) );
		add.MouseLeftPress = CreateParameterMenu;

		Search = SubHeader.Add( new LineEdit(), 1 );
		Search.PlaceholderText = "⌕  Search";
		Search.Layout = Layout.Row();
		Search.Layout.AddStretchCell( 1 );
		Search.TextChanged += x => queryDirty = true;
		Search.FixedHeight = Theme.RowHeight;

		SearchClear = Search.Layout.Add( new ToolButton( string.Empty, "clear", this ) );
		SearchClear.MouseLeftPress = () =>
		{
			Search.Text = string.Empty;
			Rebuild();

			// make sure we're open to the stuff we picked from search
			foreach ( var item in _treeView.Selection )
			{
				_treeView.ExpandPathTo( item );
			}
			_treeView.UpdateIfDirty();

			var scrollTarget = _treeView.Selection.FirstOrDefault();
			if ( scrollTarget is not null )
			{
				_treeView.ScrollTo( scrollTarget );
			}
		};
		SearchClear.Visible = false;

		_treeView = new TreeView();
		_treeView.MultiSelect = false;
		_treeView.Margin = 4;
		_treeView.ItemSpacing = 4;
		_treeView.BodyDropTarget = TreeView.DragDropTarget.None;
		_treeView.BodyContextMenu = OpenTreeViewContextMenu;
		_treeView.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( _treeView.LocalRect, Theme.ControlRadius );

			return false;
		};

		Selection.OnItemAdded += ( item ) =>
		{
			SetSelection( (BlackboardParameter)item );
			SelectionChanged();
		};

		Layout.Add( _treeView, 1 );

		CheckForChanges();
	}

	public void Rebuild()
	{
		if ( Graph == null )
			return;

		// Copy the current selection as we're about to kill it
		var selection = _treeView.Selection.Select( x => x as BlackboardParameter );

		// treeview will clear the selection, so give it a new one to clear
		_treeView.Selection = new SelectionSystem();
		_treeView.Clear();

		bool hasSearch = !string.IsNullOrEmpty( Search.Text );
		SearchClear.Visible = hasSearch;

		var parameters = Graph.Parameters;
		if ( hasSearch )
		{
			// flat search view

			var tokens = Regex.Matches( Search.Text, @"(\w+):(\S+)" )
			  .ToDictionary( m => m.Groups[1].Value, m => m.Groups[2].Value );

			var search = Regex.Replace( Search.Text, @"\b\w+:\S+\b", "" ).Trim();

			foreach ( var parameter in parameters )
			{
				if ( !parameter.Name.Contains( search, StringComparison.OrdinalIgnoreCase ) )
					continue;

				var treeNode = new BlackboardParameterSearchNode( parameter );
				treeNode.OnParameterDeleted += ( p ) =>
				{
					if ( _hasSelection )
					{
						ClearSelection();
						SelectionChanged();
						DeleteParameter( parameter );
					}
				};

				_treeView.AddItem( treeNode );
			}

			_treeView.Selection = Selection;
		}
		else
		{
			_treeView.Selection = Selection;

			foreach ( var parameter in parameters )
			{
				var treeNode = new BlackboardParameterNode( parameter );
				treeNode.OnParameterDeleted += ( p ) =>
				{
					if ( _hasSelection )
					{
						ClearSelection();
						SelectionChanged();
						DeleteParameter( parameter );
					}
				};

				_treeView.AddItem( treeNode );
				_treeView.Open( treeNode );
			}
		}
	}

	private void CreateParameterMenu()
	{
		var popup = new BlackboardPopupParameterTypeSelector( this, BlackboardParameter.GetRelevantParameters( AvailableParameters, Graph.IsSubgraph ) );
		popup.OnSelect += ( t ) =>
		{
			CreateNewParameter( t );
		};
		popup.OpenAtCursor();
	}

	[EditorEvent.Frame]
	public void CheckForChanges()
	{
		if ( !queryDirty )
			return;

		queryDirty = false;
		Rebuild();
	}

	private void OpenTreeViewContextMenu()
	{
		if ( !_treeView.SelectedItems.Any() )
			return;

		var rootItem = _treeView.Items.FirstOrDefault();
		if ( rootItem is null ) return;

		if ( rootItem is TreeNode node )
		{
			node.OnContextMenu();
		}
	}

	internal IDisposable UndoScope( string name )
	{
		PushUndo( name );
		return new Sandbox.Utility.DisposeAction( () => PushRedo() );
	}

	public void PushUndo( string name )
	{
		Log.Info( $"Push Undo ({name})" );
		_undoStack.PushUndo( name, Graph.UndoStackSerialize() );
		_window.OnUndoPushed();
	}

	public void PushRedo()
	{
		Log.Info( "Push Redo" );
		_undoStack.PushRedo( Graph.UndoStackSerialize() );
		_window.SetDirty();
	}

	public void AddParameterType<T>() where T : BlackboardParameter
	{
		AddParameterType( EditorTypeLibrary.GetType<T>() );
	}

	public void AddParameterType( TypeDescription type )
	{
		var parameterType = new ClassBlackboardParameterType( type );

		AvailableParameters.TryAdd( parameterType.Identifier, parameterType );
	}

	private void CreateNewParameter( IBlackboardParameterType type )
	{
		using var undoScope = UndoScope( "Add Parameter" );

		int id = Graph._parameters.Count;
		string name = $"Parameter{id}";

		var parameterInstance = (BlackboardParameter)type.CreateParameter( Graph, name );

		Graph.AddParameter( parameterInstance );

		OnDirty?.Invoke();

		SetSelection( parameterInstance );
		
		SelectionChanged();

		RebuildFromGraph( true );
	}

	private void DeleteParameter( BlackboardParameter parameter )
	{
		using var undoScope = UndoScope( "Delete Parameter" );

		_graph?.RemoveParameter( parameter );

		var identifier = parameter.Identifier;

		foreach ( var node in _graph.Nodes )
		{
			if ( node is IParameterNode parameterNode && parameterNode.ParameterIdentifier == identifier && parameterNode is BaseNode baseNode )
			{
				_graph.RemoveNode( baseNode );
				OnParameterNodeDeleted?.Invoke();
			}
		}

		_window.SetDirty();
		RebuildFromGraph( false );
	}

	private void BuildFromParameters( IEnumerable<BlackboardParameter> parameters, bool preserveCurrentSelection = false )
	{
		Rebuild();

		if ( _hasSelection )
		{
			var parameter = Graph.FindParameter( _selectedParameter.Identifier );
			SetSelection( parameter );
			SelectionChanged();
		}
	}

	public void RebuildFromGraph( bool preserveCurrentSelection = false )
	{
		Rebuild();

		if ( _graph is not null )
			BuildFromParameters( _graph.Parameters, preserveCurrentSelection );
	}

	public void SetSelection( BlackboardParameter parameter )
	{
		_selectedParameter = parameter;
		Selection.Set( parameter );
	}

	public void ClearSelection()
	{
		_selectedParameter = null;
		_treeView.Selection.Clear();
	}

	private void SelectionChanged()
	{
		if ( !_hasSelection )
		{
			_window.OnSelected( null );
			return;
		}

		_window.OnSelected( _selectedParameter );
	}
}

file class AddButton : Widget
{
	public string Icon;

	public AddButton( string icon ) : base( null )
	{
		Icon = icon;

		Cursor = CursorShape.Finger;
		FixedHeight = Theme.RowHeight;
	}

	protected override Vector2 SizeHint()
	{
		return new Vector2( Theme.RowHeight );
	}

	protected override void OnPaint()
	{
		Paint.ClearBrush();
		Paint.ClearPen();

		var color = Enabled ? Theme.ControlBackground : Theme.SurfaceBackground;

		if ( Enabled && Paint.HasMouseOver )
		{
			color = color.Lighten( 0.1f );
		}

		Paint.ClearPen();
		Paint.SetBrush( color );
		Paint.DrawRect( LocalRect, Theme.ControlRadius );

		Paint.ClearBrush();
		Paint.ClearPen();
		Paint.SetPen( Theme.Primary );

		Paint.DrawIcon( LocalRect, Icon, 14, TextFlag.Center );
	}
}

file class BlackboardPopupParameterTypeSelector : PopupWidget
{
	public Action<IBlackboardParameterType> OnSelect
	{
		get => Widget.OnSelect;
		set => Widget.OnSelect = value;
	}

	public TypeSelectorWidget Widget { get; set; }

	public BlackboardPopupParameterTypeSelector( Widget parent, IEnumerable<IBlackboardParameterType> availableTypes ) : base( parent )
	{
		Widget = new TypeSelectorWidget( this, availableTypes )
		{
			OnDestroy = Destroy
		};

		Layout = Layout.Column();
		Layout.Add( Widget );

		DeleteOnClose = true;
	}
}

file class TypeSelectorWidget : Widget
{
	public Action<IBlackboardParameterType> OnSelect { get; set; }
	public Action OnDestroy { get; set; }
	internal List<TypeSelection> Panels { get; set; } = new();
	internal int CurrentPanelId { get; set; } = 0;
	Widget Main { get; set; }

	string searchString;
	internal LineEdit Search { get; init; }

	IEnumerable<IBlackboardParameterType> AvailableTypes;

	public bool lineEditFocused = false;

	public TypeSelectorWidget( Widget parent, IEnumerable<IBlackboardParameterType> availableTypes ) : base( parent )
	{
		Layout = Layout.Column();
		AvailableTypes = availableTypes;

		var head = Layout.Row();
		head.Margin = 6;

		Layout.Add( head );

		Main = new Widget( this );
		Main.Layout = Layout.Row();
		Main.Layout.Enabled = false;
		Main.FixedSize = new( 300, 400 );
		Layout.Add( Main, 1 );

		DeleteOnClose = true;

		Search = new LineEdit( this );
		Search.Layout = Layout.Row();
		Search.Layout.AddStretchCell( 1 );
		Search.MinimumHeight = 22;
		Search.PlaceholderText = "Search...";
		Search.TextEdited += ( t ) =>
		{
			searchString = t;
			ResetSelection();
		};

		var clearButton = Search.Layout.Add( new ToolButton( string.Empty, "clear", this ) );
		clearButton.MouseLeftPress = () =>
		{
			Search.Text = searchString = string.Empty;
			ResetSelection();
		};

		head.Add( Search );

		var filterButton = new TypeFilterControlWidget( this, true );
		head.Add( filterButton );

		ResetSelection();

		Search.Focus();
	}

	/// <summary>
	/// Pushes a new selection to the selector
	/// </summary>
	/// <param name="selection"></param>
	void PushSelection( TypeSelection selection )
	{
		CurrentPanelId++;

		// Do we have something at our new index, if so, kill it
		if ( Panels.Count > CurrentPanelId && Panels.ElementAt( CurrentPanelId ) is var existingObj ) existingObj.Destroy();

		Panels.Insert( CurrentPanelId, selection );
		Main.Layout.Add( selection, 1 );

		if ( !selection.IsManual )
		{
			UpdateSelection( selection );
		}

		AnimateSelection( true, Panels[CurrentPanelId - 1], selection );

		selection.Focus();
	}

	/// <summary>
	/// Pops the current selection off
	/// </summary>
	internal void PopSelection()
	{
		// Don't pop while empty
		if ( CurrentPanelId == 0 ) return;

		var currentIdx = Panels[CurrentPanelId];
		CurrentPanelId--;

		AnimateSelection( false, currentIdx, Panels[CurrentPanelId] );

		Panels[CurrentPanelId].Focus();
	}

	/// <summary>
	/// Runs an animation on the last selection, and the current selection.
	/// I kinda hate this. A lot. But it's pretty.
	/// </summary>
	/// <param name="forward"></param>
	/// <param name="prev"></param>
	/// <param name="selection"></param>
	void AnimateSelection( bool forward, TypeSelection prev, TypeSelection selection )
	{
		const string easing = "ease-out";
		const float speed = 0.2f;

		var distance = Width;

		var prevFrom = prev.Position.x;
		var prevTo = forward ? prev.Position.x - distance : prev.Position.x + distance;

		var selectionFrom = forward ? selection.Position.x + distance : selection.Position.x;
		var selectionTo = forward ? selection.Position.x : selection.Position.x + distance;

		var func = ( TypeSelection a, float x ) =>
		{
			a.Position = a.Position.WithX( x );
			OnMoved();
		};

		Animate.Add( prev, speed, prevFrom, prevTo, x => func( prev, x ), easing );
		Animate.Add( selection, speed, selectionFrom, selectionTo, x => func( selection, x ), easing );
	}

	/// <summary>
	/// Resets the current selection, useful when setting up / searching
	/// </summary>
	protected void ResetSelection()
	{
		Main.Layout.Clear( true );
		Panels.Clear();

		var selection = new TypeSelection( Main, this );

		CurrentPanelId = 0;

		UpdateSelection( selection );

		Panels.Add( selection );
		Main.Layout.Add( selection );
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.SetPen( Theme.WidgetBackground.Darken( 0.4f ), 1 );
		Paint.SetBrush( Theme.WidgetBackground );
		Paint.DrawRect( LocalRect.Shrink( 1 ), 3 );
	}

	void OnTypeSelected( IBlackboardParameterType type )
	{
		OnSelect( type );
		OnDestroy?.Invoke();
	}

	protected override void OnKeyRelease( KeyEvent e )
	{
		if ( e.Key == KeyCode.Down )
		{
			var selection = Panels[CurrentPanelId];
			if ( selection.ItemList.FirstOrDefault().IsValid() )
			{
				selection.Focus();
				selection.PostKeyEvent( KeyCode.Down );
				e.Accepted = true;
			}
		}
	}

	int SearchScore( TypeDescription type, string[] parts )
	{
		var score = 0;

		var t = type.Title.Replace( " ", "" );
		var c = type.ClassName.Replace( " ", "" );

		foreach ( var w in parts )
		{
			if ( t.Contains( w, StringComparison.OrdinalIgnoreCase ) ) score += 10;
			if ( c.Contains( w, StringComparison.OrdinalIgnoreCase ) ) score += 5;
		}

		return score;
	}

	/// <summary>
	/// Updates any selection
	/// </summary>
	/// <param name="selection"></param>
	void UpdateSelection( TypeSelection selection )
	{
		selection.Clear();

		var types = AvailableTypes.Where( x => !x.Type.IsAbstract
		&& !x.Type.HasAttribute<HideAttribute>() ).OrderBy( x => x.Type.Order );

		if ( !string.IsNullOrWhiteSpace( searchString ) )
		{
			var searchWords = searchString.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
			var query = types.Select( x => new { x, score = SearchScore( x.Type, searchWords ) } )
								.ToArray()
								.Where( x => x.score > 0 );

			foreach ( var type in query.OrderByDescending( x => x.score ).Select( x => x.x ) )
			{
				selection.AddEntry( new TypeEntry( selection, type ) { MouseClick = () => OnTypeSelected( type ) } );
			}

			selection.AddStretchCell();

			return;
		}

		foreach ( var type in types )
		{
			selection.AddEntry( new TypeEntry( selection, type ) { MouseClick = () => OnTypeSelected( type ) } );
		}

		selection.AddStretchCell();
	}

}

file class TypeSelection : Widget
{
	internal string VariableTypeName { get; init; }
	ScrollArea Scroller { get; init; }
	TypeSelectorWidget Selector { get; set; }

	internal List<Widget> ItemList { get; private set; } = new();
	internal int CurrentItemId { get; private set; } = 0;
	internal Widget CurrentItem { get; private set; }

	internal bool IsManual { get; set; }

	internal TypeSelection( Widget parent, TypeSelectorWidget selector, string variableTypeName = null ) : base( parent )
	{
		VariableTypeName = variableTypeName;
		FixedSize = parent.ContentRect.Size;
		Layout = Layout.Column();

		Scroller = new ScrollArea( this );
		Scroller.Layout = Layout.Column();
		Scroller.FocusMode = FocusMode.None;
		Layout.Add( Scroller, 1 );

		Scroller.Canvas = new Widget( Scroller );
		Scroller.Canvas.Layout = Layout.Column();
	}

	protected bool SelectMoveRow( int delta )
	{
		var selection = Selector.Panels[Selector.CurrentPanelId];
		if ( delta == 1 && selection.ItemList.Count - 1 > selection.CurrentItemId )
		{
			selection.CurrentItem = selection.ItemList[++selection.CurrentItemId];
			selection.Update();

			if ( selection.CurrentItem.IsValid() )
			{
				Scroller.MakeVisible( selection.CurrentItem );
			}

			return true;
		}
		else if ( delta == -1 )
		{
			if ( selection.CurrentItemId > 0 )
			{
				selection.CurrentItem = selection.ItemList[--selection.CurrentItemId];
				selection.Update();

				if ( selection.CurrentItem.IsValid() )
				{
					Scroller.MakeVisible( selection.CurrentItem );
				}

				return true;
			}
			else
			{
				selection.Selector.Search.Focus();
				selection.CurrentItem = null;
				selection.Update();
				return true;
			}
		}

		return false;
	}

	protected bool Enter()
	{
		var selection = Selector.Panels[Selector.CurrentPanelId];
		if ( selection.ItemList[selection.CurrentItemId] is Widget entry )
		{
			entry.MouseClick?.Invoke();
			return true;
		}

		return false;
	}

	protected override void OnKeyRelease( KeyEvent e )
	{
		// Move down
		if ( e.Key == KeyCode.Down )
		{
			e.Accepted = true;
			SelectMoveRow( 1 );
			return;
		}

		// Move up 
		if ( e.Key == KeyCode.Up )
		{
			e.Accepted = true;
			SelectMoveRow( -1 );
			return;
		}

		// Back button while in any selection, goes to previous selction.
		if ( e.Key == KeyCode.Left && !Selector.lineEditFocused )
		{
			e.Accepted = true;
			Selector.PopSelection();
			return;
		}

		// Moving right, or hitting the enter key assumes you're trying to select something
		if ( (e.Key == KeyCode.Return || e.Key == KeyCode.Right) && Enter() )
		{
			e.Accepted = true;
			return;
		}
	}

	/// <summary>
	/// Adds a new entry to the current selection.
	/// </summary>
	/// <param name="entry"></param>
	internal Widget AddEntry( Widget entry )
	{
		var layoutWidget = Scroller.Canvas.Layout.Add( entry );
		ItemList.Add( entry );

		if ( entry is TypeEntry e ) e.Selector = this;

		return layoutWidget;
	}

	/// <summary>
	/// Adds a stretch cell to the bottom of the selection - good to call this when you know you're done adding entries.
	/// </summary>
	internal void AddStretchCell()
	{
		Scroller.Canvas.Layout.AddStretchCell( 1 );
		Update();
	}

	/// <summary>
	/// Adds a separator cell.
	/// </summary>
	internal void AddSeparator()
	{
		Scroller.Canvas.Layout.AddSeparator( true );
		Update();
	}

	/// <summary>
	/// Clears the current selection
	/// </summary>
	internal void Clear()
	{
		Scroller.Canvas.Layout.Clear( true );
		ItemList.Clear();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.SetPen( Theme.WidgetBackground.Darken( 0.8f ), 1 );
		Paint.SetBrush( Theme.WidgetBackground.Darken( 0.2f ) );
		Paint.DrawRect( LocalRect.Shrink( 0 ), 3 );
	}

}

file class TypeEntry : Widget
{
	public string Text { get; set; } = "Test";
	public string Icon { get; set; } = "note_add";

	internal TypeSelection Selector { get; set; }
	public IBlackboardParameterType Type { get; init; }

	internal TypeEntry( Widget parent, IBlackboardParameterType type = null ) : base( parent )
	{
		FixedHeight = 24;
		Type = type;

		if ( type is not null )
		{
			Text = type.Type.Title;
			Icon = type.Type.Icon;
			ToolTip = $"<b>{type.Type.Title}</b><br/>{type.Type.Description}";
		}
	}

	protected override void OnPaint()
	{
		var r = LocalRect.Shrink( 12, 2 );
		var hovered = IsUnderMouse || Selector.CurrentItem == this;
		var opacity = hovered ? 1.0f : 0.7f;
		var typeColor = Color.White;
		var textColor = Theme.TextControl.WithAlpha( hovered ? 1.0f : 0.5f );

		if ( ShaderGraphTheme.BlackboardConfigs.TryGetValue( Type.Type.TargetType, out var blackboardConfig ) )
		{
			typeColor = blackboardConfig.Color;
		}

		if ( hovered )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Primary.Lighten( 0.1f ).Desaturate( 0.3f ).WithAlpha( 0.4f * 0.6f ) );
			Paint.DrawRect( LocalRect );
		}

		Paint.SetPen( typeColor );
		Paint.DrawIcon( r.Shrink( 4f ), "circle", 12f, TextFlag.LeftCenter );

		r.Left += r.Height + 6;

		Paint.SetDefaultFont( 8 );
		Paint.SetPen( textColor );
		Paint.DrawText( r, Text, TextFlag.LeftCenter );
	}
}

file class TypeFilterControlWidget : Widget
{

	TypeSelectorWidget Target;

	Widget additionalOptions;

	public bool disabled { get; set; } = false;

	public TypeFilterControlWidget( TypeSelectorWidget targetObject, bool disable = false )
	{
		Target = targetObject;
		Cursor = CursorShape.Finger;
		MinimumWidth = Theme.RowHeight;
		HorizontalSizeMode = SizeMode.CanShrink;

		additionalOptions = null;

		disabled = disable;

		ToolTip = "Filter Settings";
	}

	protected override Vector2 SizeHint()
	{
		return new( Theme.RowHeight, Theme.RowHeight );
	}

	protected override Vector2 MinimumSizeHint()
	{
		return new( Theme.RowHeight, Theme.RowHeight );
	}

	protected override void OnDoubleClick( MouseEvent e ) { }

	protected override void OnMousePress( MouseEvent e )
	{
		if ( ReadOnly ) return;
		//OpenSettings();
		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		var rect = LocalRect.Shrink( 2 );
		var icon = "sort";

		if ( additionalOptions?.IsValid ?? false )
		{
			Paint.SetPen( Theme.Blue.WithAlpha( 0.3f ), 1 );
			Paint.SetBrush( Theme.Blue.WithAlpha( 0.2f ) );
			Paint.DrawRect( rect, 2 );

			Paint.SetPen( Theme.Blue );
			Paint.DrawIcon( rect, icon, 13 );
		}
		else
		{
			Paint.SetPen( Theme.Blue.WithAlpha( 0.3f ) );
			Paint.DrawIcon( rect, icon, 13 );
		}

		if ( IsUnderMouse )
		{
			if ( !disabled )
			{
				Paint.SetPen( Theme.Blue.WithAlpha( 0.5f ), 1 );
				Paint.ClearBrush();
				Paint.DrawRect( rect, 1 );
			}
		}
	}

	bool PaintMenuBackground()
	{
		Paint.SetBrushAndPen( Theme.ControlBackground );
		Paint.DrawRect( Paint.LocalRect, 0 );
		return true;
	}
}

file class BlackboardParameterNode : TreeNode<BlackboardParameter>
{
	public BlackboardParameterNode( BlackboardParameter p ) : base( p )
	{
		Height = Theme.RowHeight;
	}

	public override bool HasChildren => false;

	///<summary>
	///Called when a blackboard parameter is deleated.
	///</summary>
	public Action<BlackboardParameter> OnParameterDeleted { get; set; }

	public override string Name
	{
		get => Value.Name;
		set => Value.Name = value;
	}

	public override string GetTooltip()
	{
		var sb = new StringBuilder();

		sb.AppendLine( $"<h3>{Name}</h3>" );

		return sb.ToString();
	}

	public override bool CanEdit => true;

	public override int ValueHash
	{
		get
		{
			HashCode hc = new HashCode();
			hc.Add( Value.Name );

			return hc.ToHashCode();
		}
	}

	public override void OnPaint( VirtualWidget item )
	{
		var variable = Value;
		var isEven = item.Row % 2 == 0;
		var isHovered = item.Hovered;
		var fullSpanRect = item.Rect;
		fullSpanRect.Left = 4;
		fullSpanRect.Right = TreeView.Width - 4;
		var textColor = Theme.TextControl;
		var itemColor = Theme.ControlBackground;
		var typeColor = Color.White;

		if ( ShaderGraphTheme.BlackboardConfigs.TryGetValue( variable.GetType(), out var blackboardConfig ) )
		{
			typeColor = blackboardConfig.Color;
		}

		if ( item.Hovered )
		{
			textColor = Color.White;
			itemColor = Theme.Primary.Lighten( 0.1f ).Desaturate( 0.3f ).WithAlpha( 0.4f * 0.6f );

			Paint.ClearPen();
			Paint.SetBrush( itemColor );
			Paint.DrawRect( fullSpanRect );

			Paint.SetPen( Theme.TextControl );
		}
		if ( item.Selected )
		{
			textColor = Theme.TextControl;
			itemColor = Theme.Primary;

			Paint.ClearPen();
			Paint.SetBrush( itemColor );
			Paint.DrawRect( fullSpanRect );
		}
		else if ( isEven )
		{
			Paint.ClearPen();
			Paint.SetBrush( itemColor );
			Paint.DrawRect( fullSpanRect );
		}

		//Paint.ClearPen();
		//Paint.SetBrush( itemColor );
		//Paint.DrawRect( fullSpanRect, Theme.ControlRadius );

		var iconRect = fullSpanRect.Shrink( 4, 0, 0, 0 );
		Paint.SetPen( typeColor );
		Paint.DrawIcon( iconRect, "circle", 12f, TextFlag.LeftCenter );
		fullSpanRect.Left += 24f;

		Paint.SetPen( textColor.WithAlpha( 0.7f ) );
		Paint.SetBrush( textColor.WithAlpha( 0.7f ) );

		var textRect = Paint.DrawText( fullSpanRect.Shrink( 4, 0, 0, 0 ), $"{variable.Name}", TextFlag.LeftCenter );
		var typeRect = Paint.DrawText( fullSpanRect.Shrink( 0, 0, 4, 0 ), $"{DisplayInfo.ForType( variable.GetType() ).Name}", TextFlag.RightCenter );

		//Paint.SetPen( Color.Gray.WithAlpha( 0.25f ) );
		//Paint.SetBrush( Color.Gray.WithAlpha( 0.25f ) );
		//Paint.DrawRect( typeRect.Grow( 2 ), Theme.ControlRadius );
	}

	public override bool OnDragStart()
	{
		var drag = new Drag( TreeView );

		if ( TreeView.IsSelected( Value ) )
		{
			drag.Data.Object = Value;

			drag.Execute();

			return true;
		}

		return false;
	}

	public override bool OnContextMenu()
	{
		var m = new ContextMenu( TreeView ) { Searchable = false };

		m.AddOption( "Delete", "delete", () => { OnParameterDeleted?.Invoke( Value ); }, "editor.delete" );
		//m.AddOption( "Rename", "label", TreeView.BeginRename, "editor.rename" );

		m.OpenAtCursor( false );

		return true;
	}
}

file class BlackboardParameterSearchNode : BlackboardParameterNode
{
	public override bool HasChildren => false;
	public BlackboardParameterSearchNode( BlackboardParameter p ) : base( p )
	{
	}
}
