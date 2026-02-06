using Sandbox.UI;

namespace Editor;

public partial class TreeView : BaseItemWidget
{
	readonly HashSet<object> openItems = [];

	internal IEnumerable<object> OpenItems => openItems;

	/// <summary>
	/// Additional horizontal indent for each subtree level.
	/// </summary>
	public float IndentWidth { get => field; set { field = value; OnLayoutChanged(); } }

	/// <summary>
	/// Vertical spacing between each item.
	/// </summary>
	public float ItemSpacing { get => field; set { field = value; OnLayoutChanged(); } }

	/// <summary>
	/// Width of the expand/collapse button.
	/// </summary>
	public float ExpandWidth { get => field; set { field = value; OnLayoutChanged(); } }

	/// <summary>
	/// If true, when an object is selected via SelectItem or dynamically via SelectionOverride, the treeview will
	/// open all the items leading to that item and scroll to it.
	/// </summary>
	public bool ExpandForSelection { get; set; }

	public TreeView( Widget parent = null ) : base( parent )
	{
		Margin = new Margin( 8, 8, 16, 8 );
		IndentWidth = 20;
		ExpandWidth = Theme.RowHeight;
		ItemSpacing = 0;
		ExpandForSelection = true;
	}

	bool IsOpen( object obj )
	{
		obj = ResolveObject( obj );
		return openItems.Contains( obj );
	}

	IEnumerable<object> GetChildren( object obj )
	{
		if ( obj is TreeNode tn )
		{
			tn.TreeView = this;
			return tn.Children;
		}

		if ( obj is Asset a )
			return a.GetDependants( false );

		return Enumerable.Empty<object>();
	}

	bool GetItemIsEnabled( object obj )
	{
		if ( obj is TreeNode tn )
			return tn.Enabled;

		return true;
	}

	bool GetItemHasChildren( object obj )
	{
		if ( obj is TreeNode tn )
		{
			tn.TreeView = this;
			tn.UpdateHash();
			return tn.HasChildren;
		}

		if ( obj is Asset a )
			return a.GetDependants( false ).Count > 0;

		return false;
	}

	float GetItemHeight( object obj )
	{
		if ( obj is TreeNode tn )
			return tn.Height;

		return 24;
	}

	public void RefreshChildren()
	{
		foreach ( var item in _items )
		{
			if ( item is TreeNode node )
			{
				node.Dirty();
			}
		}
	}

	protected override void Rebuild()
	{
		Frame();
		LayoutScrollbar();
		Update();

		base.Rebuild();
	}

	/// <summary>
	/// Work out how big the scrollbars need to be and layout the current PVS
	/// </summary>
	protected virtual void LayoutScrollbar()
	{
		var rect = CanvasRect;

		var visibleRect = LocalRect;
		visibleRect.Position += new Vector2( 0, VerticalScrollbar.Value );
		visibleRect.Left += Margin.Left;
		visibleRect.Right -= Margin.Right;

		ItemLayouts.Clear();

		LayoutConfig config = new LayoutConfig();
		config.Rect = visibleRect;
		config.Layouts = ItemLayouts;

		BuildLayout( ref config );

		VerticalScrollbar.Minimum = 0;
		VerticalScrollbar.Maximum = Math.Max( VerticalScrollbar.Minimum, (config.Top - rect.Height).CeilToInt() );
		VerticalScrollbar.SingleStep = Math.Max( 32, rect.Height * 0.1f ).CeilToInt();
		VerticalScrollbar.PageStep = rect.Height.FloorToInt();
	}

	/// <summary>
	/// This struct exists so we can use BuildLayout/Recursive layout in different modes.
	/// 1. Build the layout - use Rect to determine is an object is visible, if it is then create a VirtualWidget and add it to Layouts.
	/// 2. Build a complete list of objects, in the order they appear, for keyboard navigation
	/// 3. Retrieve the Position of a specific object
	/// </summary>
	struct LayoutConfig
	{
		public List<object> IndexedObjects;
		public HashSet<VirtualWidget> Layouts;
		public Rect Rect;
		public int Row;
		public int Column;
		public float Top;
		public object TargetObject;
	}

	void BuildLayout( ref LayoutConfig config )
	{
		config.Top = Margin.Top;

		foreach ( var item in _items )
		{
			if ( !RecursiveLayout( item, ref config, out Rect r ) )
				break;
		}
	}

	public Func<object, bool> ShouldDisplayChild;

	bool RecursiveLayout( object item, ref LayoutConfig config, out Rect outRect )
	{
		outRect = default;
		config.IndexedObjects?.Add( ResolveObject( item ) );

		bool isEnabled = GetItemIsEnabled( item );

		if ( !isEnabled )
		{
			return true;
		}

		bool children = GetItemHasChildren( item );
		bool open = children && IsOpen( item );
		var height = GetItemHeight( item );
		var bottom = config.Top + height;

		VirtualWidget lo = default;

		if ( ShouldDisplayChild == null || (ShouldDisplayChild?.Invoke( item ) ?? false) )
		{
			if ( config.Layouts != null && bottom > config.Rect.Top && config.Top < config.Rect.Bottom )
			{
				lo = new VirtualWidget();
				lo.Selected = IsSelected( item );
				lo.Object = item;

				lo.Rect = config.Rect;
				lo.Rect = new Rect( lo.Rect.Left, config.Top - lo.Rect.Top, lo.Rect.Width, height );
				lo.Row = config.Row;
				lo.Column = config.Column;
				lo.HasChildren = children;
				lo.IsOpen = children && IsOpen( item );

				outRect = lo.Rect;

				config.Layouts?.Add( lo );
			}

			//
			// Getting the target object's rect
			//
			var value = ResolveObject( item );
			if ( config.TargetObject == value )
			{
				config.Rect = new Rect( 0, config.Top - Margin.Top, 1, height );
				return false;
			}

			config.Top += height + ItemSpacing;
			config.Row++;
		}

		if ( open )
		{
			int childCount = 0;
			Rect cr = default;

			config.Column++;
			foreach ( var child in GetChildren( item ) )
			{
				if ( !RecursiveLayout( child, ref config, out var childRect ) )
					return false;

				if ( childCount == 0 )
				{
					cr = childRect;
				}
				else
				{
					cr.Add( childRect );
				}

				childCount++;
			}
			config.Column--;

			lo?.ChildrenRect = cr;
		}

		return true;

	}

	protected override void PaintItem( VirtualWidget item )
	{
		var node = item.Object as TreeNode;

		float indent = (IndentWidth * item.Column) + ExpandWidth;

		item.Indent = indent;
		item.Rect.Left += indent;
		item.ChildrenRect.Left += indent + IndentWidth;
		if ( node != null )
		{
			node.OnPaint( item );
		}
		else
		{
			base.PaintItem( item );
		}
		item.ChildrenRect.Left -= indent + IndentWidth;
		item.Rect.Left -= indent;
		item.Indent = 0;


		if ( item.HasChildren )
		{
			if ( node?.ExpanderHidden ?? false )
				return;

			var left = item.Rect;
			left.Left += indent - ExpandWidth;
			left.Width = ExpandWidth;

			if ( item.IsOpen )
			{
				Paint.SetPen( Theme.Text );
				Paint.DrawIcon( left, "arrow_drop_down", 26, TextFlag.Center );
			}
			else
			{
				Paint.SetPen( Theme.Text.WithAlpha( 0.6f ) );
				Paint.DrawIcon( left, "arrow_right", 26, TextFlag.Center );
			}
		}
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		if ( DebugModeEnabled )
		{
			Paint.SetDefaultFont();

			var firstRow = ItemLayouts.Count > 0 ? ItemLayouts.Min( x => x.Row ) : 0;

			var debugText = $"Items:	{Items.Count():n0}\nVisible:	{ItemLayouts.Count:n0}\nFirst Row: {firstRow:n0}\nPaint Time: {TimeMsPaint:0.00}ms\nLayout Time: {timeMsRebuild:0.00}ms";

			var mt = Paint.MeasureText( LocalRect.Shrink( 10 ), debugText, TextFlag.LeftTop );

			mt.Position += new Vector2( Width - mt.Width - 32, 0 );

			Paint.ClearPen();
			Paint.SetBrush( Color.Black.WithAlpha( 0.5f ) );
			Paint.DrawRect( mt.Grow( 5 ), 6 );

			Paint.SetPen( Theme.Text );
			Paint.DrawText( mt, debugText, TextFlag.LeftTop );
		}
	}

	protected override void PaintItemDebug( VirtualWidget item )
	{
		base.PaintItemDebug( item );

		if ( item.Object is TreeNode node )
		{
			Paint.SetDefaultFont( 7 );
			Paint.DrawText( item.Rect.Shrink( 4, 2 ), $"{node.GetType().Name}", TextFlag.RightBottom );
		}
	}

	protected override void OnItemActivated( object item )
	{
		ItemActivated?.Invoke( item );

		if ( item is TreeNode node )
		{
			node.OnActivated();
		}
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		// Avoid calling OnItemActivated if we double click the expand button
		var item = GetItemAt( e.LocalPosition );
		if ( e.LeftMouseButton && item is not null && item.HasChildren )
		{
			var expandRect = item.Rect;
			expandRect.Left += IndentWidth * item.Column;
			expandRect.Width = ExpandWidth;

			if ( expandRect.IsInside( e.LocalPosition ) )
			{
				e.Accepted = true;
				return;
			}
		}

		base.OnDoubleClick( e );
	}

	/// <summary>
	/// Simulate pressing F2 to rename an item
	/// </summary>
	public void BeginRename()
	{
		Rebuild();
		OnBeginRename();
	}

	/// <summary>
	/// Called when F2 is pressed, to rename an item. TreeNode should have CanEdit set to true, and implement Name
	/// </summary>
	[Shortcut( "editor.rename", "F2" )]
	protected void OnBeginRename()
	{
		var items = Selection.Select( x => ResolveNode( x, false ) ).Where( x => x is not null && x.CanEdit ).ToList();
		var first = items.FirstOrDefault();
		var item = ItemLayouts.FirstOrDefault( x => x.Object == first );

		if ( item is null )
		{
			return;
		}

		//
		// Create popup for renaming this item
		//

		var indent = item.Column * IndentWidth + ExpandWidth + 20;
		var popup = new PopupWidget( this );
		popup.Layout = Layout.Column();
		popup.Position = ToScreen( item.Rect.TopLeft + new Vector2( indent, 0 ) );
		popup.Width = item.Rect.Width - ExpandWidth - 20;
		popup.Height = item.Rect.Height;

		var lineEdit = popup.Layout.Add( new LineEdit() );
		lineEdit.Text = first?.Name ?? "";

		var onComplete = () =>
		{
			if ( !popup.Visible ) return;

			first?.OnRename( item, lineEdit.Text, items );

			popup.Close();
		};

		popup.OnLostFocus += onComplete;
		lineEdit.ReturnPressed += onComplete;
		lineEdit.SelectAll();
		lineEdit.Focus();

		popup.Show();

	}

	protected override bool OnItemPressed( VirtualWidget pressedItem, MouseEvent e )
	{
		var node = pressedItem.Object as TreeNode;

		bool recursive = Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift );
		if ( node?.ExpanderFills ?? false )
		{
			Toggle( pressedItem.Object, recursive );
			return false;
		}

		if ( node?.HasChildren ?? false )
		{
			var expandRect = pressedItem.Rect;
			expandRect.Left += IndentWidth * pressedItem.Column;
			expandRect.Width = ExpandWidth;

			if ( expandRect.IsInside( e.LocalPosition ) )
			{
				Toggle( pressedItem.Object, recursive );
				return false;
			}
		}

		return true;
	}

	protected override void OnItemContextMenu( VirtualWidget pressedItem, MouseEvent e )
	{
		if ( pressedItem.Object is TreeNode node )
		{
			if ( node.OnContextMenu() )
				return;
		}

		base.OnItemContextMenu( pressedItem, e );
	}

	/// <summary>
	/// Set the selected object state. If state is true and ExpandForSelection is true, we'll
	/// try to expand the tree path to the selected object.
	/// </summary>
	protected override void SetSelected( object obj, bool state, bool skipEvents = false )
	{
		obj = ResolveObject( obj );

		var targetObject = obj;
		var node = ResolveNode( obj, true );
		if ( node is TreeNode tn )
		{
			if ( tn.Value is not null )
			{
				targetObject = tn.Value;
			}

			base.SetSelected( targetObject, state, skipEvents );

			if ( state && tn is not null )
			{
				tn.OnSelectionChanged( state );

				if ( ExpandForSelection )
				{
					ExpandPathTo( tn );
				}
			}

			return;
		}

		base.SetSelected( targetObject, state, skipEvents );
	}

	/// <summary>
	/// Expand the path all the way to this object
	/// </summary>
	public void ExpandPathTo( object obj )
	{
		if ( obj is not TreeNode tn )
		{
			tn = ResolveNode( obj, true );
			if ( tn == null ) return;
		}

		foreach ( var rootNode in _items.OfType<TreeNode>() )
		{
			foreach ( var node in rootNode.EnumeratePathTo( tn ) )
			{
				Open( node );
			}
		}
	}

	/// <summary>
	/// Toggle this node open or closed
	/// </summary>
	public void Toggle( object target, bool recursive = false )
	{
		target = ResolveObject( target );

		if ( openItems.Contains( target ) ) Close( target, recursive );
		else Open( target, recursive );
	}

	/// <summary>
	/// Open this node
	/// </summary>
	public void Open( object target, bool recursive = false )
	{
		target = ResolveObject( target );

		if ( openItems.Add( target ) )
		{
			Dirty( target );
		}

		if ( recursive )
		{
			var node = ResolveNode( target, true );
			if ( node is not null && node.HasChildren )
			{
				// ensure all the children exist before going thru and opening them
				node.InternalBuildChildren();

				foreach ( var child in GetChildren( node ) )
				{
					Open( child, recursive );
				}
			}
		}
	}

	/// <summary>
	/// Close this node
	/// </summary>
	public void Close( object target, bool recursive = false )
	{
		target = ResolveObject( target );

		if ( openItems.Remove( target ) )
		{
			Dirty( target );
		}

		if ( recursive )
		{
			var node = ResolveNode( target, true );
			if ( node is not null && node.HasChildren )
			{
				foreach ( var child in node.Children )
				{
					Close( child, recursive );
				}
			}
		}
	}

	/// <summary>
	/// Convert from an object to a TreeNode
	/// </summary>
	protected override object ResolveObject( object obj )
	{
		if ( obj is TreeNode tn && tn.Value is not null )
		{
			return tn.Value;
		}

		return obj;
	}

	/// <summary>
	/// Convert from an object to a TreeNode
	/// </summary>
	protected TreeNode ResolveNode( object obj, bool createPath )
	{
		if ( obj is null )
			return null;

		if ( obj is TreeNode tn )
			return tn;

		foreach ( var node in Items.OfType<TreeNode>() )
		{
			var found = node.ResolveNode( obj, createPath );
			if ( found is not null ) return found;
		}

		return null;
	}

	public override bool IsSelected( object obj )
	{
		if ( Selection.Contains( ResolveObject( obj ) ) )
			return true;

		if ( Selection.OfType<TreeNode>().Any( x => x.Value == obj ) )
			return true;

		return false;
	}

	protected override string GetTooltip( object obj )
	{
		if ( obj is TreeNode node )
		{
			return node.GetTooltip();
		}

		return base.GetTooltip( obj );
	}

	public override bool SelectMoveColumn( int positions )
	{
		var selected = ResolveObject( SelectedItems.FirstOrDefault() );
		if ( selected == null ) return false;

		var open = IsOpen( selected );

		// left means close, if it's open
		if ( positions < 0 && open )
		{
			Close( selected );
			return true;
		}

		// right means open, if it's closed
		if ( positions > 0 && !open )
		{
			Open( selected );
			return true;
		}

		// else jump to the next row
		return SelectMoveRow( positions );
	}

	List<object> BuildFullOrderedIndex()
	{
		LayoutConfig config = new LayoutConfig();
		config.IndexedObjects = new List<object>();

		BuildLayout( ref config );

		return config.IndexedObjects;
	}

	public override bool SelectMoveRow( int positions )
	{
		var selected = ResolveObject( SelectedItems.FirstOrDefault() );
		if ( selected == null ) return false;

		var indexes = BuildFullOrderedIndex();
		if ( ShouldDisplayChild != null )
		{
			indexes.RemoveAll( x => !ShouldDisplayChild( ResolveNode( x, false ) ) );
		}

		var idx = indexes.IndexOf( selected );
		if ( idx < 0 ) return false;

		idx += positions;
		idx = Math.Clamp( idx, 0, indexes.Count - 1 );

		var targetObj = indexes[idx];
		if ( targetObj != null )
		{
			SelectItem( targetObj );
			ScrollTo( targetObj );
			Update();
			return true;
		}

		return false;
	}

	/// <summary>
	/// Try to calculate position and size of a specific item in the tree view.
	/// </summary>
	/// <param name="item">Item to compute position/size for.</param>
	/// <param name="rect">The computed position/size of the item, if any.</param>
	/// <returns>Whether the item was found and has a valid position.</returns>
	public bool TryGetItemRect( object item, out Rect rect )
	{
		rect = default;

		LayoutConfig config = new LayoutConfig();
		config.TargetObject = item;
		config.Layouts = new HashSet<VirtualWidget>();
		BuildLayout( ref config );

		if ( config.Rect.Bottom == 0 )
			return false;

		rect = config.Rect;
		return true;
	}

	public override void ScrollTo( object target )
	{
		if ( !TryGetItemRect( target, out Rect rect ) )
		{
			Log.Trace( $"ScrollTo: Couldn't find item rect {target}" );
			return;
		}

		ScrollTo( rect.Top, rect.Height );
	}

	protected override bool OnDragItem( VirtualWidget item )
	{
		if ( item.Object is TreeNode node )
		{
			return node.OnDragStart();
		}

		return base.OnDragItem( item );

	}

	protected override DropAction OnItemDrag( ItemDragEvent e )
	{
		if ( e.Item.Object is TreeNode node )
		{
			return node.OnDragDrop( e );
		}

		return base.OnItemDrag( e );
	}

	protected override void OnKeyPressOnItem( KeyEvent e, object item )
	{
		if ( item is TreeNode node )
		{
			node.OnKeyPress( e );
			return;
		}
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		if ( !e.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) && !e.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) )
		{
			if ( GetItemAt( e.LocalPosition ) == null )
				UnselectAll();
		}
	}

	protected override void OnDragHoverItem( DragEvent ev, VirtualWidget item )
	{
		if ( item.Object is TreeNode node )
		{
			node.OnDragHover( ev );
			return;
		}
		base.OnDragHoverItem( ev, item );
	}

	protected override void OnDropOnItem( DragEvent ev, VirtualWidget item )
	{
		if ( item.Object is TreeNode node )
		{
			node.OnDrop( ev );
			return;
		}
		base.OnDropOnItem( ev, item );
	}

	protected override void OnSelectionAdded( object item )
	{
		if ( SelectedItems.Count() > 1 )
		{
			return;
		}

		// try to convert this to a treenode
		item = ResolveNode( item, true ) ?? item;

		if ( item is not null )
		{
			ExpandPathTo( item );
			ScrollTo( item );
		}
	}


	RealTimeSince timeSinceUpdate;

	[EditorEvent.Frame]
	void Frame()
	{
		if ( timeSinceUpdate < 0.1f ) return;
		timeSinceUpdate = 0;

		foreach ( var item in ItemLayouts )
		{
			if ( item.Object is TreeNode node )
			{
				node.ThinkInternal();
			}
		}
	}

	protected override void SelectTo( object item, bool skipEvents = false )
	{
		var indexed = BuildFullOrderedIndex();
		var currentObj = Selection.FirstOrDefault() ?? _items.FirstOrDefault();

		if ( currentObj is null ) return;

		UnselectAll( true );

		var indexA = indexed.IndexOf( ResolveObject( currentObj ) );
		var indexB = indexed.IndexOf( ResolveObject( item ) );

		if ( indexA == -1 || indexB == -1 ) return;

		if ( !skipEvents ) OnBeforeSelection?.Invoke( Selection.ToArray() );

		// Whenever we shift around we always want to maintain whatever item we started with
		// So lets explicitly add to this list backwards / forwards to always keep the start the same
		if ( indexA < indexB )
		{
			for ( int i = indexA; i <= indexB; i++ )
			{
				if ( ShouldDisplayChild != null )
				{
					var node = ResolveNode( indexed[i], true );

					if ( !ShouldDisplayChild( node ) )
						continue;
				}

				SetSelected( indexed[i], true, skipEvents );
			}
		}
		else if ( indexA > indexB )
		{
			for ( int i = indexA; i >= indexB; i-- )
			{
				if ( ShouldDisplayChild != null )
				{
					var node = ResolveNode( indexed[i], true );

					if ( !ShouldDisplayChild( node ) )
						continue;
				}

				SetSelected( indexed[i], true, skipEvents );
			}
		}

		if ( !skipEvents ) OnSelectionChanged?.Invoke( Selection.ToArray() );

		SmoothScrollTarget = 0f;
	}
}
