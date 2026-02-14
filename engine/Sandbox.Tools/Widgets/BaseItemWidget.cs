using Sandbox.UI;
using System;

namespace Editor;

public partial class BaseItemWidget : BaseScrollWidget
{
	/// <summary>
	/// Called when an item is clicked.
	/// </summary>
	public Action<object> ItemClicked { get; set; }

	/// <summary>
	/// Called when an item is selected.
	/// </summary>
	public Action<object> ItemSelected { get; set; }

	/// <summary>
	/// Called when an item is no longer selected.
	/// </summary>
	public Action<object> ItemDeselected { get; set; }

	/// <summary>
	/// Called when an item is hovered by the user's cursor.
	/// </summary>
	public Action<object> ItemHoverEnter { get; set; }

	/// <summary>
	/// Called when an item is no longer hovered by the user's cursor.
	/// </summary>
	public Action<object> ItemHoverLeave { get; set; }

	/// <summary>
	/// Called when an item is right clicked.
	/// </summary>
	public Action<object> ItemContextMenu { get; set; }

	/// <summary>
	/// Called when an item is double left clicked.
	/// </summary>
	public Action<object> ItemActivated { get; set; }

	/// <summary>
	/// Used to overwrite an item's style
	/// </summary>
	public Action<VirtualWidget> ItemPaint { get; set; }

	/// <summary>
	/// Called to see whether or not we can drag a specific item.
	/// </summary>
	public Func<object, bool> ItemDrag { get; set; }

	/// <summary>
	/// Can override an item's selection here.
	/// </summary>
	public Func<object> SelectionOverride { get; set; }

	/// <summary>
	/// Called when right clicking on the item's parent.
	/// </summary>
	public Action BodyContextMenu { get; set; }

	/// <summary>
	/// Called before selection is changed on selection. When multiple items are affected this will only be called once.
	/// </summary>
	public Action<object[]> OnBeforeSelection { get; set; }

	/// <summary>
	/// Called before selection is changed on deselection. When multiple items are affected this will only be called once.
	/// </summary>
	public Action<object[]> OnBeforeDeselection { get; set; }

	/// <summary>
	/// Multiple items have been selected
	/// </summary>
	[Obsolete( "Use OnSelectionChanged or ItemSelected instead" )]
	public Action<object[]> ItemsSelected
	{
		get => OnSelectionChanged;
		set => OnSelectionChanged = value;
	}

	/// <summary>
	/// Multiple items have been deselected
	/// </summary>
	[Obsolete( "Use OnSelectionChanged or ItemSelected instead" )]
	public Action<object[]> ItemsDeselected
	{
		get => OnSelectionChanged;
		set => OnSelectionChanged = value;
	}

	/// <summary>
	/// Called when selection has changed. When multiple items are affected this will only be called once.
	/// </summary>
	public Action<object[]> OnSelectionChanged { get; set; }

	/// <summary>
	/// If set, selecting an item will not deselect all already selected items, clicking a selected item will deselect it.
	/// </summary>
	public bool ToggleSelect { get; set; }

	public enum DragDropTarget
	{
		None,
		LastRoot,
		Closest
	}

	/// <summary>
	/// What shall we do if they drag something in and it's not over an item?
	/// </summary>
	public DragDropTarget BodyDropTarget { get; set; } = DragDropTarget.None;

	/// <summary>
	/// Gets or sets the maximum distance, in pixels, at which a target is considered close enough for drag-and-drop when in BodyDropTarget.Closest mode.
	/// operations.
	/// </summary>
	public float DragDropTargetClosestThreshold { get; set; } = 32.0f;

	public override bool ProvidesDebugMode => true;

	Margin _margin;
	public Margin Margin { get => _margin; set { _margin = value; OnLayoutChanged(); } }

	/// <summary>
	/// The inner of LocalRect with Margin
	/// </summary>
	public Rect CanvasRect => LocalRect.Shrink( Margin );

	protected List<object> _items = new();
	public IEnumerable<object> Items
	{
		get
		{
			lock ( _items )
			{
				return _items.ToList();
			}
		}
	}

	readonly protected HashSet<VirtualWidget> ItemLayouts = [];

	public BaseItemWidget( Widget parent = null ) : base( parent )
	{
		AcceptDrops = true;
	}

	/// <summary>
	/// Set the items in the list.
	/// </summary>
	public void SetItems( IEnumerable<object> items )
	{

		_items.Clear();

		if ( items != null )
		{
			_items.AddRange( items );
		}

		OnLayoutChanged();
	}

	/// <summary>
	/// Add multiple items.
	/// </summary>
	public void AddItems( IEnumerable<object> items )
	{
		if ( items != null )
		{
			_items.AddRange( items );
		}

		OnLayoutChanged();
	}

	/// <summary>
	/// Add given item to this widget.
	/// </summary>
	public T AddItem<T>( T item )
	{
		_items.Add( item );

		OnLayoutChanged();
		return item;
	}

	/// <summary>
	/// Remove given item from this widget.
	/// </summary>
	public void RemoveItem( object item )
	{

		item = ResolveObject( item );

		if ( _items.Remove( item ) )
		{
			Dirty( item );
		}
	}

	/// <summary>
	/// Remove all items.
	/// </summary>
	public virtual void Clear()
	{
		_items.Clear();

		ItemLayouts.Clear();
		Selection.Clear();

		Dirty();
	}

	bool dataDirty;

	public virtual void Dirty( object dirtyObject = null )
	{
		dataDirty = true;
	}

	protected virtual void OnLayoutChanged()
	{
		dataDirty = true;
	}

	protected override void OnScrollChanged()
	{
		base.OnScrollChanged();

		dataDirty = true;
	}

	protected override void OnResize()
	{
		base.OnResize();
		Rebuild();
	}

	System.Diagnostics.Stopwatch diagTimePaint = new System.Diagnostics.Stopwatch();
	protected float TimeMsPaint => (float)diagTimePaint.Elapsed.TotalMilliseconds;

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		diagTimePaint.Restart();
		foreach ( var item in ItemLayouts.ToArray() )
		{
			item.Selected = IsSelected( item.Object );

			Paint.SetFlags( item.Selected, item.Hovered, item.Pressed, false, true );

			try
			{
				PaintItem( item );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e );
			}

			if ( DebugModeEnabled )
			{
				PaintItemDebug( item );
			}

		}
		diagTimePaint.Stop();
	}

	protected virtual void PaintItemDebug( VirtualWidget item )
	{
		Paint.ClearBrush();
		Paint.SetPen( Color.Red, 1 );
		Paint.DrawRect( item.Rect );
	}

	protected virtual void PaintItem( VirtualWidget item )
	{
		if ( ItemPaint != null )
		{
			ItemPaint.Invoke( item );
			return;
		}

		var col = Theme.Text.WithAlpha( 0.7f );

		if ( Paint.HasPressed ) col = Theme.Yellow;
		else if ( Paint.HasSelected ) col = Theme.Blue;
		else if ( Paint.HasMouseOver ) col = Theme.Green;

		Paint.ClearPen();
		Paint.SetBrush( col.WithAlpha( ((item.Row + item.Column) % 2) == 0 ? 0.2f : 0.1f ) );
		Paint.DrawRect( item.Rect.Shrink( 1 ), 6 );

		Paint.SetPen( col );
		Paint.DrawText( item.Rect.Shrink( 8 ), $"{item.Object}", TextFlag.WrapAnywhere | TextFlag.Center );
	}

	VirtualWidget hovered;

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );
		UpdateHovered();
	}

	/// <summary>
	/// Get the virtual item at this local position.
	/// </summary>
	public VirtualWidget GetItemAt( Vector2 localPosition )
	{
		foreach ( var item in ItemLayouts )
		{
			if ( item.Rect.IsInside( localPosition ) )
				return item;
		}

		return default;
	}

	void UpdateHovered()
	{
		var localMousePos = this.FromScreen( Application.CursorPosition );

		// still hovered the same one, ignore
		if ( hovered != null && hovered.Rect.IsInside( localMousePos ) && ItemLayouts.Contains( hovered ) )
		{
			return;
		}

		var item = GetItemAt( localMousePos );
		if ( item != null )
		{
			InternalHoverChange( item );
			return;
		}

		InternalHoverChange( default );
	}

	private void InternalHoverChange( VirtualWidget item )
	{
		if ( item == hovered ) return;

		var oldHover = hovered?.Object ?? null;
		if ( hovered != null )
		{
			ItemHoverLeave?.Invoke( hovered.Object );
			hovered.Hovered = false;
		}

		hovered = item;

		if ( hovered != null )
		{
			ItemHoverEnter?.Invoke( hovered.Object );
			hovered.Hovered = true;
		}

		OnHoverChanged( oldHover, hovered?.Object ?? null );

		Update(); // redraw
	}

	/// <summary>
	/// Hover has changed, neither of these objects are guaranteed to be non null.
	/// </summary>
	protected virtual void OnHoverChanged( object oldHover, object newHover )
	{
		if ( newHover == null )
		{
			ToolTip = null;
		}
		else
		{
			ToolTip = GetTooltip( newHover );
		}
	}

	/// <summary>
	/// Called to retrieve a tooltip for given item.
	/// </summary>
	protected virtual string GetTooltip( object obj )
	{
		return null;
	}

	VirtualWidget pressedItem;

	protected override void OnMousePress( MouseEvent e )
	{
		UpdateHovered();

		if ( e.LeftMouseButton )
		{
			if ( pressedItem != null )
			{
				pressedItem.Pressed = false;
			}

			if ( hovered == null || OnItemPressed( hovered, e ) )
			{
				pressedItem = hovered;

				if ( pressedItem != null )
				{
					pressedItem.Pressed = true;
					e.Accepted = true;
					IsDraggable = true;
				}
			}

			Update();
		}

		if ( e.RightMouseButton )
		{
			if ( hovered != null )
			{
				//
				// Make sure the object we're clicking on is in the selection
				//
				if ( !Selection.Contains( ResolveObject( hovered.Object ) ) )
				{
					SelectItem( hovered.Object, false, true );
				}

				OnItemContextMenu( hovered, e );
			}
			else
			{
				BodyContextMenu?.Invoke();
			}
			// item context
		}

		base.OnMousePress( e );
	}

	/// <summary>
	/// Allows over-riding mouse press on an item, without click or selection.
	/// Return true to allow default behavior.
	/// </summary>
	protected virtual bool OnItemPressed( VirtualWidget pressedItem, MouseEvent e )
	{
		return true;
	}

	/// <summary>
	/// The item has been right clicked
	/// </summary>
	protected virtual void OnItemContextMenu( VirtualWidget pressedItem, MouseEvent e )
	{
		ItemContextMenu?.Invoke( hovered.Object );
	}

	public override void OnDragLeave()
	{
		SetDropTarget( null );

		base.OnDragLeave();
	}

	public ItemDragEvent CurrentItemDragEvent { get; private set; }

	/// <summary>
	/// Get the virtual item to use as a drop target for a given drag event
	/// </summary>
	protected virtual VirtualWidget GetDragItem( DragEvent ev )
	{
		var item = GetItemAt( ev.LocalPosition );
		if ( item is not null )
			return item;

		object fallbackItem = default;

		if ( BodyDropTarget == DragDropTarget.LastRoot && ItemLayouts.Count > 0 )
		{
			fallbackItem = _items.LastOrDefault();
		}

		if ( BodyDropTarget == DragDropTarget.Closest )
		{
			var closestDist = float.MaxValue;
			var closestItem = default( VirtualWidget );

			foreach ( var widget in ItemLayouts )
			{
				var dist = widget.Rect.ClosestPoint( ev.LocalPosition ).Distance( ev.LocalPosition );
				if ( dist > closestDist ) continue;

				closestDist = dist;
				closestItem = widget;
			}

			if ( closestDist > DragDropTargetClosestThreshold )
				return null;

			return closestItem;
		}

		if ( fallbackItem is null )
			return default;

		return FindVirtualWidget( fallbackItem );

	}

	public override void OnDragHover( DragEvent ev )
	{
		var item = GetDragItem( ev );
		if ( item is not null )
		{
			CurrentItemDragEvent = ItemDragEvent.From( ev, item );

			var a = OnItemDrag( CurrentItemDragEvent );
			SetDropTarget( a != DropAction.Ignore ? item : null );
			ev.Action = a;
			return;
		}
		else
		{
			CurrentItemDragEvent = ItemDragEvent.From( ev );
			var a = OnBodyDragDrop( CurrentItemDragEvent );
			SetDropTarget( a != DropAction.Ignore ? item : null );
			ev.Action = a;
			return;
		}
	}

	/// <summary>
	/// Called when a drag drop is being dropped onto the canvas
	/// </summary>
	protected virtual DropAction OnBodyDragDrop( ItemDragEvent ev )
	{
		return DropAction.Ignore;
	}

	public override void OnDragDrop( DragEvent ev )
	{
		SetDropTarget( null );

		var item = GetDragItem( ev );
		if ( item != null )
		{
			var dragEvent = ItemDragEvent.From( ev, item );
			dragEvent.IsDrop = true;
			OnItemDrag( dragEvent );
		}
		else
		{
			var dragEvent = ItemDragEvent.From( ev );
			dragEvent.IsDrop = true;
			OnBodyDragDrop( dragEvent );
		}

		Dirty();
	}

	private void SetDropTarget( VirtualWidget item )
	{
		foreach ( var i in ItemLayouts )
		{
			i.Dropping = item == i;
		}

		Update();
	}

	/// <summary>
	/// Given an object, try to find the virtual widget. This can of course return null if the item isn't visible
	/// </summary>
	protected VirtualWidget FindVirtualWidget( object obj )
	{
		foreach ( var item in ItemLayouts )
		{
			if ( item.Object == obj )
				return item;
		}

		return null;
	}

	/// <summary>
	/// Called when a dragged item is being hovered over this widget.
	/// This is the place to make drag and drop previews.
	/// </summary>
	protected virtual DropAction OnItemDrag( ItemDragEvent e ) // TODO rename OnItemDragDrop
	{
		// this is all just backwards compatibility
		// we should delete OnDragHoverItem and OnDropOnItem
		// and everything should be overriding OnItemDrag
		if ( !e.IsDrop )
		{
			e.rootEvent.LocalPosition -= e.Item.Rect.Position;
			OnDragHoverItem( e.rootEvent, e.Item );
			return e.rootEvent.Action;
		}
		else
		{
			e.rootEvent.LocalPosition -= e.Item.Rect.Position;
			OnDropOnItem( e.rootEvent, e.Item );
		}

		return DropAction.Ignore;
	}

	/// <summary>
	/// Called when a dragged item is being hovered over this widget.
	/// This is the place to make drag and drop previews.
	/// </summary>
	protected virtual void OnDragHoverItem( DragEvent ev, VirtualWidget item )
	{

	}

	/// <summary>
	/// Called when an item is drag and dropped onto this widget.
	/// </summary>
	protected virtual void OnDropOnItem( DragEvent ev, VirtualWidget item )
	{

	}

	protected override void OnDragStart()
	{
		base.OnDragStart();

		if ( pressedItem == null )
			return;

		try
		{
			pressedItem.Dragging = true;

			Update();

			if ( OnDragItem( pressedItem ) )
				return;

			if ( ItemDrag == null )
				return;

			if ( !ItemDrag( pressedItem.Object ) )
				return;
		}
		finally
		{
			pressedItem.Dragging = false;
			pressedItem.Pressed = false;
			pressedItem = null;
		}
	}

	/// <summary>
	/// Called when we start to drag an item.
	/// </summary>
	protected virtual bool OnDragItem( VirtualWidget item )
	{
		return false;
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );

		UpdateHovered();

		if ( e.LeftMouseButton )
		{
			if ( pressedItem == hovered )
			{
				if ( pressedItem is null )
				{
					// Why? Causes issues when dragging an item outside of the panel.
					/*if ( !e.HasShift )
					{
						UnselectAll();
					}*/
				}
				else
				{
					ItemClicked?.Invoke( pressedItem.Object );

					if ( e.HasShift )
					{
						SelectTo( hovered.Object );
					}
					else
					{
						// Deselect selected when holding Ctrl or when toggle selection is on
						if ( (ToggleSelect || e.HasCtrl) && hovered.Selected )
							UnselectItem( hovered.Object );
						else
							SelectItem( hovered.Object, e.HasCtrl );
					}
				}
			}

			if ( pressedItem != null )
			{
				pressedItem.Pressed = false;
				pressedItem = null;
			}
		}

		IsDraggable = false;
		Update();
	}

	protected virtual void OnItemActivated( object item )
	{
		ItemActivated?.Invoke( item );
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		if ( e.LeftMouseButton && hovered is not null )
		{
			OnItemActivated( hovered.Object );
			e.Accepted = true;
			return;
		}

		e.Accepted = true;
	}

	public virtual bool SelectMoveColumn( int positions )
	{
		return SelectMove( positions );
	}

	public virtual bool SelectMoveRow( int positions )
	{
		return SelectMove( positions );
	}


	protected override void OnKeyPress( KeyEvent e )
	{
		if ( e.Key == KeyCode.Left && SelectMoveColumn( -1 ) )
		{
			e.Accepted = true;
			return;
		}

		if ( e.Key == KeyCode.Right && SelectMoveColumn( 1 ) )
		{
			e.Accepted = true;
			return;
		}

		if ( e.Key == KeyCode.Down && SelectMoveRow( 1 ) )
		{
			e.Accepted = true;
			return;
		}

		if ( e.Key == KeyCode.Up && SelectMoveRow( -1 ) )
		{
			e.Accepted = true;
			return;
		}

		bool isEnter = e.Key == KeyCode.Enter || e.Key == KeyCode.Return;

		if ( isEnter && Selection.Any() && ItemActivated != null )
		{
			foreach ( var item in Selection.ToArray() )
			{
				OnItemActivated( item );
			}

			e.Accepted = true;
			return;
		}

		foreach ( var item in Selection.ToArray() )
		{
			OnKeyPressOnItem( e, item );
		}

		TryItemJump( e );

		base.OnKeyPress( e );
	}

	/// <summary>
	/// A key has been pressed on this selected item.
	/// </summary>
	protected virtual void OnKeyPressOnItem( KeyEvent e, object item )
	{

	}

	/// <summary>
	/// Returns the index of given item.
	/// </summary>
	protected int ItemIndex( object item )
	{
		return _items.IndexOf( item );
	}

	/// <summary>
	/// Returns the item at given index, or null.
	/// </summary>
	protected object GetAtIndex( int i )
	{
		if ( i < 0 ) return null;
		return _items.Skip( i ).FirstOrDefault();
	}

	/// <summary>
	/// Ensure that given item is in view, scrolling to it if necessary.
	/// </summary>
	public virtual void ScrollTo( object target )
	{

	}

	/// <summary>
	/// Ensure that given position is in view, scrolling to it if necessary.
	/// </summary>
	/// <param name="targetPosition">Target vertical position to make sure is in view.</param>
	/// <param name="height">Height of a potential item/element we want to make sure is in view.</param>
	public virtual void ScrollTo( float targetPosition, float height )
	{
		if ( !VerticalScrollbar.IsValid() )
			return;

		float edgeBuffer = 20.0f;
		var top = VerticalScrollbar.Value;
		var btm = top + CanvasRect.Height;

		var pTop = targetPosition;
		var pBot = pTop + height;

		if ( pTop < top )
		{
			SmoothScrollTarget = pTop - top - edgeBuffer;
			return;
		}

		if ( pBot > btm )
		{
			SmoothScrollTarget = pBot - btm + edgeBuffer;
			return;
		}
	}

	System.Diagnostics.Stopwatch diagTimeRebuild = new System.Diagnostics.Stopwatch();
	protected float timeMsRebuild => (float)diagTimeRebuild.Elapsed.TotalMilliseconds;

	int _selectionHash;

	[EditorEvent.Frame]
	public void UpdateIfDirty()
	{
		if ( !Visible ) return;

		UpdateDynamicSelection();

		//
		// If the selection has changed, we better update
		//
		if ( _selectionHash != Selection.GetHashCode() )
		{
			Update();
			_selectionHash = Selection.GetHashCode();
		}

		if ( !dataDirty ) return;

		// don't rebuild while clicking
		if ( pressedItem != null )
		{
			return;
		}

		diagTimeRebuild.Restart();
		Rebuild();
		diagTimeRebuild.Stop();

		dataDirty = false;
	}

	int dynamicSelectionHash = -1;

	void UpdateDynamicSelection()
	{
		if ( SelectionOverride == null ) return;

		var newSelect = SelectionOverride();

		//
		// We only ever want to update this when the value returned by SelectionOverride() has changed.
		//
		var newSelectHash = HashCode.Combine( newSelect );
		if ( newSelectHash == dynamicSelectionHash ) return;
		dynamicSelectionHash = newSelectHash;

		if ( newSelect == null )
		{
			if ( SelectedItems.Any() )
			{
				UnselectAll( true );
			}
			return;
		}

		if ( Selection.Count == 1 && IsSelected( newSelect ) )
			return;

		SelectItem( newSelect );
		ScrollTo( newSelect );
		Update();
	}

	/// <summary>
	/// Rebuild the panel layout.
	/// </summary>
	protected virtual void Rebuild()
	{
		UpdateHovered();
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		if ( _selection is not null )
			_selection.OnItemAdded -= OnSelectionAdded;
	}

	/// <summary>
	/// Whether to allow selecting multiple items at once.
	/// </summary>
	public bool MultiSelect { get; set; }


	SelectionSystem _selection = new SelectionSystem();

	public SelectionSystem Selection
	{
		get => _selection;
		set
		{
			if ( _selection is not null )
				_selection.OnItemAdded -= OnSelectionAdded;

			_selection = value;

			if ( _selection is not null )
				_selection.OnItemAdded += OnSelectionAdded;
		}
	}

	protected virtual void OnSelectionAdded( object item )
	{

	}

	/// <summary>
	/// Selected items.
	/// </summary>
	public IEnumerable<object> SelectedItems => Selection;

	/// <summary>
	/// For derived classes where the object is wrapped in another class (i.e. TreeView)
	/// </summary>
	protected virtual object ResolveObject( object obj )
	{
		return obj;
	}


	/// <summary>
	/// Return true if this item is selected.
	/// </summary>
	public virtual bool IsSelected( object obj )
	{
		return Selection.Contains( obj ) || Selection.Contains( ResolveObject( obj ) );
	}

	/// <summary>
	/// Select given item.
	/// </summary>
	/// <param name="obj">Item to select.</param>
	/// <param name="add">Whether to add the item to selection, or replace current selection.</param>
	/// <param name="skipEvents">Do not invoke events.</param>
	public void SelectItem( object obj, bool add = false, bool skipEvents = false )
	{
		var selected = Selection.ToArray();
		if ( !skipEvents ) OnBeforeSelection?.Invoke( selected );

		if ( !MultiSelect || (!ToggleSelect && !add) )
		{
			foreach ( var i in selected )
			{
				SetSelected( i, false, skipEvents );
			}
		}

		SetSelected( obj, true, skipEvents );

		if ( !skipEvents ) OnSelectionChanged?.Invoke( Selection.ToArray() );
	}

	/// <summary>
	/// Select multiple items.
	/// </summary>
	/// <param name="items">Items to select.</param>
	/// <param name="add">Whether to add the items to the selection, or replace current selection.</param>
	/// <param name="skipEvents">Do not invoke events.</param>
	public void SelectItems( IEnumerable<object> items, bool add = false, bool skipEvents = false )
	{
		var selected = Selection.ToArray();
		if ( !skipEvents ) OnBeforeSelection?.Invoke( selected );
		if ( !MultiSelect && items.Count() > 1 )
		{
			items = items.Take( 1 );
		}

		if ( !MultiSelect || (!ToggleSelect && !add) )
		{
			foreach ( var i in selected )
			{
				SetSelected( i, false, skipEvents );
			}
		}

		foreach ( var obj in items )
		{
			SetSelected( obj, true, skipEvents );
		}

		if ( !skipEvents ) OnSelectionChanged?.Invoke( Selection.ToArray() );
	}

	/// <summary>
	/// Unselect given item.
	/// </summary>
	/// <param name="obj">Item to deselect.</param>
	/// <param name="skipEvents">Do not invoke events.</param>
	public void UnselectItem( object obj, bool skipEvents = false )
	{
		var selected = Selection.ToArray();
		if ( !skipEvents ) OnBeforeDeselection?.Invoke( selected );

		SetSelected( obj, false, skipEvents );

		if ( !skipEvents ) OnSelectionChanged?.Invoke( Selection.ToArray() );
	}

	/// <summary>
	/// Unselects all items that are currently selected (if any)
	/// </summary>
	/// <param name="skipEvents">Do not invoke events.</param>
	public void UnselectAll( bool skipEvents = false )
	{
		var selected = Selection.ToArray();
		if ( selected.Length < 1 )
			return;

		if ( !skipEvents ) OnBeforeDeselection?.Invoke( selected );

		foreach ( var i in selected )
		{
			SetSelected( i, false, skipEvents );
		}

		if ( !skipEvents ) OnSelectionChanged?.Invoke( Selection.ToArray() );
	}

	/// <summary>
	/// Set the selection state of an item.
	/// </summary>
	/// <param name="obj">Item to set selection state of.</param>
	/// <param name="state">Whether the item should be selected or not.</param>
	/// <param name="skipEvents">Do not invoke <see cref="ItemSelected"/> and <see cref="ItemDeselected"/>.</param>
	protected virtual void SetSelected( object obj, bool state, bool skipEvents = false )
	{
		obj = ResolveObject( obj );

		if ( state )
		{
			if ( !Selection.Add( obj ) )
				return; // already selected
		}
		else
		{
			if ( !Selection.Remove( obj ) )
				return; // already not selected
		}

		foreach ( var item in ItemLayouts.Where( x => x.Object == obj ) )
		{
			item.Selected = state;
		}

		if ( !skipEvents )
		{
			if ( state ) ItemSelected?.Invoke( obj );
			else ItemDeselected?.Invoke( obj );
		}
	}

	/// <summary>
	/// Move the selection pointer by this many positions.
	/// </summary>
	public bool SelectMove( int i )
	{
		var obj = Selection.FirstOrDefault();
		if ( obj == null )
		{
			// select first one?
			return false;
		}

		var currentIndex = ItemIndex( obj );
		var targetIndex = Math.Clamp( currentIndex + i, 0, _items.Count() - 1 );

		if ( targetIndex == currentIndex )
			return false;

		var targetObj = GetAtIndex( targetIndex );
		if ( targetObj != null )
		{
			SelectItem( targetObj );
			ScrollTo( targetObj );
			Update();
		}
		return targetObj != null;
	}

	/// <summary>
	/// Select everything between the current selection pointer and this one.
	/// </summary>
	protected virtual void SelectTo( object item, bool skipEvents = false )
	{
		var currentObj = Selection.FirstOrDefault() ?? _items.FirstOrDefault();
		if ( currentObj == null )
		{
			return;
		}

		if ( currentObj == item )
		{
			SelectItem( item );
			return;
		}

		var selected = Selection.ToArray();
		if ( !skipEvents ) OnBeforeSelection?.Invoke( selected );

		foreach ( var i in selected )
		{
			SetSelected( i, false, skipEvents );
		}

		var fromIndex = ItemIndex( currentObj );
		var toIndex = ItemIndex( item );

		// Whenever we shift around we always want to maintain whatever item we started with
		// So lets explicitly add to this list backwards / forwards to always keep the start the same
		if ( fromIndex < toIndex )
		{
			for ( int i = fromIndex; i <= toIndex; i++ )
			{
				var obj = GetAtIndex( i );
				SetSelected( obj, true, skipEvents );
			}
		}
		else if ( fromIndex > toIndex )
		{
			for ( int i = fromIndex; i >= toIndex; i-- )
			{
				var obj = GetAtIndex( i );
				SetSelected( obj, true, skipEvents );
			}
		}

		if ( !skipEvents ) OnSelectionChanged?.Invoke( Selection.ToArray() );

		Update();
	}

	protected void SelectAll( bool skipEvents = false )
	{
		if ( !skipEvents ) OnBeforeSelection?.Invoke( Selection.ToArray() );

		for ( int i = 0; i < _items.Count(); i++ )
		{
			var obj = GetAtIndex( i );
			SetSelected( obj, true, skipEvents );
		}

		if ( !skipEvents ) OnSelectionChanged?.Invoke( Selection.ToArray() );

		Update();
	}

	[Flags]
	public enum ItemEdge
	{
		None = 0,
		Top = 1,
		Left = 2,
		Bottom = 4,
		Right = 8
	}

	public struct ItemDragEvent
	{
		public Vector2 LocalPosition;
		public VirtualWidget Item;
		public KeyboardModifiers KeyboardModifiers => rootEvent.KeyboardModifiers;
		public ItemEdge DropEdge;

		/// <summary>
		/// If true, this is a drop - not just a hover
		/// </summary>
		public bool IsDrop;
		public bool HasShift => rootEvent.HasShift;
		public bool HasCtrl => rootEvent.HasCtrl;
		public bool HasAlt => rootEvent.HasAlt;
		public DragData Data => rootEvent.Data;

		internal DragEvent rootEvent;

		internal static ItemDragEvent From( DragEvent ev, VirtualWidget item )
		{
			var e = new ItemDragEvent
			{
				rootEvent = ev,
				LocalPosition = ev.LocalPosition - item.Rect.Position,
				Item = item,
			};

			if ( e.LocalPosition.y < 5 ) e.DropEdge |= ItemEdge.Top;
			if ( e.LocalPosition.y > item.Rect.Height - 5 ) e.DropEdge |= ItemEdge.Bottom;
			// left and right

			return e;
		}

		internal static ItemDragEvent From( DragEvent ev )
		{
			return new ItemDragEvent
			{
				rootEvent = ev,
				LocalPosition = ev.LocalPosition,
			};
		}
	}


	RealTimeSince timeSinceTyped;
	string typedText;

	private void TryItemJump( KeyEvent e )
	{
		if ( timeSinceTyped > 1.0f )
			typedText = "";

		timeSinceTyped = 0;

		if ( string.IsNullOrEmpty( e.Text ) )
			return;

		typedText += e.Text;
		SelectItemStartingWith( typedText );
	}


	public virtual void SelectItemStartingWith( string text )
	{
		var item = FindItemsThatStartWith( text ).FirstOrDefault();
		if ( item is null ) return;

		SelectItem( item );
		ScrollTo( item );
	}

	protected virtual IEnumerable<object> FindItemsThatStartWith( string text )
	{
		return Items.Where( x => x.ToString().StartsWith( text, StringComparison.OrdinalIgnoreCase ) );
	}

	protected override void OnShortcutPressed( KeyEvent e )
	{
		if ( e.HasCtrl )
		{
			// todo - accept if select all?
			return;
		}

		if ( e.HasShift ) return;
		if ( e.HasAlt ) return;

		// accept single key press shortcuts, because we want to enable jump to item by name
		e.Accepted = true;
	}
}
