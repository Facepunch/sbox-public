using Microsoft.AspNetCore.Components;

namespace Sandbox.UI;

/// <summary>
/// A virtualized tree. Holds the flat list of visible rows, a pool of <see cref="TreeRow"/> panels
/// that are rebound as it scrolls, and all the index-based input handling. Knows nothing about the
/// items themselves - <see cref="TreeView{T}"/> supplies those.
/// </summary>
public abstract class BaseTreeView : Panel
{
	/// <summary>
	/// Layout of one visible row, in layout units relative to the content's top left.
	/// </summary>
	protected struct RowLayout
	{
		public float Top;
		public float Height;
		public int Depth;
		public bool HasChildren;
		public bool IsOpen;
	}

	readonly List<RowLayout> _rows = new();
	readonly List<TreeRow> _active = new();
	readonly List<TreeRow> _free = new();

	int _activeFirst;
	int _bindVersion;
	int _placementHash;
	float _totalHeight;
	int _pendingRename = -1;
	int _pendingScroll = -1;
	int _highlightRow = -1;
	float _highlightUntil;
	int _highlightPhase;

	/// <summary>
	/// Height of a row when the tree has no per-item height.
	/// </summary>
	[Parameter]
	public float RowHeight { get; set; } = 24;

	/// <summary>
	/// Extra left padding per level of depth.
	/// </summary>
	[Parameter]
	public float IndentWidth { get; set; } = 16;

	/// <summary>
	/// Rows above and below the viewport that stay bound, so fast scrolls don't flash empty.
	/// </summary>
	[Parameter]
	public int OverscanRows { get; set; } = 1;

	/// <summary>
	/// How long <see cref="HighlightRow"/> holds before it fades, in seconds.
	/// </summary>
	[Parameter]
	public float HighlightTime { get; set; } = 1.0f;

	/// <summary>
	/// How long the highlight takes to fade out, in seconds. Styled by the <c>highlight-fade</c> class.
	/// </summary>
	[Parameter]
	public float HighlightFadeTime { get; set; } = 1.0f;

	/// <summary>
	/// True while one of our rows is being dragged. The tree carries the <c>dragging</c> class.
	/// </summary>
	public bool IsDragging { get; private set; }

	/// <summary>
	/// Rebuild the flat row list on the next tick.
	/// </summary>
	public bool NeedsRebuild { get; set; } = true;

	/// <summary>
	/// Number of rows currently reachable through open parents.
	/// </summary>
	public int RowCount => _rows.Count;

	/// <summary>
	/// Number of row panels currently bound to a row.
	/// </summary>
	public int ActiveRowCount => _active.Count;

	/// <summary>
	/// Number of row panels alive, bound or pooled.
	/// </summary>
	public int PooledRowCount => _active.Count + _free.Count;

	/// <summary>
	/// How many times a row panel has been bound. Goes up on scroll and rebuild, never per frame.
	/// </summary>
	public int BindCount { get; private set; }

	/// <summary>
	/// The row keyboard navigation moves from, or -1.
	/// </summary>
	public int CursorRow { get; private set; } = -1;

	/// <summary>
	/// Where a shift-selection ranges from, or -1.
	/// </summary>
	public int AnchorRow { get; private set; } = -1;

	protected BaseTreeView()
	{
		AddClass( "treeview" );
		Style.Position = PositionMode.Relative;
		Style.Overflow = OverflowMode.Scroll;
		AcceptsFocus = true;
	}

	/// <summary>
	/// Walk the data and call <see cref="AddRow"/> for every reachable row, in order.
	/// </summary>
	protected abstract void BuildRows();

	/// <summary>
	/// Fill a pooled row panel with the item at this row.
	/// </summary>
	protected abstract void BindRow( int row, TreeRow panel );

	/// <summary>
	/// Cheap per-tick check for data that changed underneath us. Set <see cref="NeedsRebuild"/> if so.
	/// </summary>
	protected virtual void CheckForChanges() { }

	protected abstract bool IsRowSelected( int row );
	protected abstract void SetRowSelected( int row, bool selected );
	protected abstract void ClearSelectionInternal();
	protected abstract void OnSelectionFinished();
	protected abstract void SetRowOpen( int row, bool open, bool recursive );
	protected abstract void OnRowActivated( int row );
	protected abstract void OnRowContextMenu( int row );
	protected abstract bool CanRenameRow( int row );
	protected abstract void OnRowRenamed( int row, string text );
	protected internal abstract bool CanDragRow( int row );
	protected abstract void OnRowDropped( int targetRow, int sourceRow );

	/// <summary>
	/// Append a row during <see cref="BuildRows"/>.
	/// </summary>
	protected void AddRow( float height, int depth, bool hasChildren, bool isOpen )
	{
		_rows.Add( new RowLayout
		{
			Top = _totalHeight,
			Height = height,
			Depth = depth,
			HasChildren = hasChildren,
			IsOpen = isOpen
		} );

		_totalHeight += height;
	}

	/// <summary>
	/// Layout of a row.
	/// </summary>
	protected RowLayout GetRow( int row ) => _rows[row];

	/// <summary>
	/// Depth of a row, roots being zero.
	/// </summary>
	public int GetRowDepth( int row ) => _rows[row].Depth;

	/// <summary>
	/// The nearest row above with one less depth, or -1 for a root.
	/// </summary>
	public int GetParentRow( int row )
	{
		if ( row < 0 || row >= _rows.Count ) return -1;

		var depth = _rows[row].Depth - 1;
		if ( depth < 0 ) return -1;

		for ( int i = row - 1; i >= 0; i-- )
		{
			if ( _rows[i].Depth == depth ) return i;
		}

		return -1;
	}

	/// <summary>
	/// One past the last row inside this row's subtree.
	/// </summary>
	public int GetSubtreeEnd( int row )
	{
		var depth = _rows[row].Depth;
		int i = row + 1;
		while ( i < _rows.Count && _rows[i].Depth > depth ) i++;
		return i;
	}

	/// <summary>
	/// Rebuild everything now instead of waiting for the next tick.
	/// </summary>
	public void Rebuild()
	{
		NeedsRebuild = false;

		_rows.Clear();
		_totalHeight = 0;
		BuildRows();

		if ( CursorRow >= _rows.Count ) CursorRow = _rows.Count - 1;
		if ( AnchorRow >= _rows.Count ) AnchorRow = -1;
		if ( _highlightRow >= _rows.Count ) _highlightRow = -1;

		_bindVersion++;
		SetNeedsFinalLayout();
	}

	/// <summary>
	/// Re-run <see cref="BindRow"/> on every bound row without rebuilding the row list.
	/// </summary>
	public void RebindRows()
	{
		_bindVersion++;
	}

	public override void Tick()
	{
		base.Tick();

		if ( ComputedStyle is null || !IsVisible ) return;

		CheckForChanges();

		if ( NeedsRebuild )
		{
			Rebuild();
		}

		UpdateWindow();
		UpdateHighlight();

		if ( _pendingScroll >= 0 )
		{
			var row = _pendingScroll;
			_pendingScroll = -1;
			ScrollToRow( row );
		}

		if ( _pendingRename >= 0 )
		{
			var row = _pendingRename;
			_pendingRename = -1;
			BeginRename( row );
		}
	}

	public override void OnHotloaded()
	{
		base.OnHotloaded();
		NeedsRebuild = true;
	}

	/// <summary>
	/// Layout-unit rect of the content area, relative to our own top left.
	/// </summary>
	void GetContentRect( out float left, out float top, out float width, out float height )
	{
		var scale = ScaleFromScreen;
		left = (Box.RectInner.Left - Box.Rect.Left) * scale;
		top = (Box.RectInner.Top - Box.Rect.Top) * scale;
		width = Box.RectInner.Width * scale;
		height = Box.Rect.Height * scale;
	}

	/// <summary>
	/// Index of the first row whose bottom edge is below <paramref name="y"/>.
	/// </summary>
	int FindRowAt( float y )
	{
		int lo = 0, hi = _rows.Count;
		while ( lo < hi )
		{
			int mid = (lo + hi) >> 1;
			if ( _rows[mid].Top + _rows[mid].Height > y ) hi = mid;
			else lo = mid + 1;
		}
		return lo;
	}

	/// <summary>
	/// Index of the first row that starts at or below <paramref name="y"/>.
	/// </summary>
	int FindRowStartingAt( float y )
	{
		int lo = 0, hi = _rows.Count;
		while ( lo < hi )
		{
			int mid = (lo + hi) >> 1;
			if ( _rows[mid].Top >= y ) hi = mid;
			else lo = mid + 1;
		}
		return lo;
	}

	/// <summary>
	/// Bind row panels to whatever's in the viewport, reusing panels that were already bound.
	/// </summary>
	void UpdateWindow()
	{
		GetContentRect( out var contentLeft, out var contentTop, out var contentWidth, out var viewportHeight );

		var scrollY = ScrollOffset.y * ScaleFromScreen - contentTop;

		int first = 0, pastEnd = 0;
		if ( _rows.Count > 0 )
		{
			first = FindRowAt( scrollY );
			pastEnd = FindRowStartingAt( scrollY + viewportHeight );

			first = Math.Max( 0, first - OverscanRows );
			pastEnd = Math.Min( _rows.Count, pastEnd + OverscanRows );
		}

		var placementHash = HashCode.Combine( contentLeft, contentTop, contentWidth, IndentWidth );
		var replace = placementHash != _placementHash;
		_placementHash = placementHash;

		// Anything bound to stale data goes back to the pool
		if ( _active.Count > 0 && (_active[0].BindVersion != _bindVersion || pastEnd <= _activeFirst || first >= _activeFirst + _active.Count) )
		{
			for ( int i = 0; i < _active.Count; i++ ) Release( _active[i] );
			_active.Clear();
		}

		if ( _active.Count == 0 )
		{
			_activeFirst = first;
		}

		// Trim the ends that scrolled away
		while ( _active.Count > 0 && _activeFirst < first )
		{
			Release( _active[0] );
			_active.RemoveAt( 0 );
			_activeFirst++;
		}

		while ( _active.Count > 0 && _activeFirst + _active.Count > pastEnd )
		{
			Release( _active[^1] );
			_active.RemoveAt( _active.Count - 1 );
		}

		// Grow into the rows that scrolled in
		while ( _activeFirst > first )
		{
			_activeFirst--;
			var panel = Acquire();
			_active.Insert( 0, panel );
			Bind( _activeFirst, panel );
		}

		while ( _activeFirst + _active.Count < pastEnd )
		{
			var panel = Acquire();
			_active.Add( panel );
			Bind( _activeFirst + _active.Count - 1, panel );
		}

		if ( replace )
		{
			for ( int i = 0; i < _active.Count; i++ ) Place( _activeFirst + i, _active[i] );
		}

		// Don't hoard panels after the viewport shrinks
		while ( _free.Count > _active.Count + 4 )
		{
			var panel = _free[^1];
			_free.RemoveAt( _free.Count - 1 );
			panel.Delete( true );
		}
	}

	TreeRow Acquire()
	{
		if ( _free.Count > 0 )
		{
			var panel = _free[^1];
			_free.RemoveAt( _free.Count - 1 );
			return panel;
		}

		var created = new TreeRow { Tree = this };
		AddChild( created );
		return created;
	}

	void Release( TreeRow panel )
	{
		panel.Unbind();
		_free.Add( panel );
	}

	void Bind( int row, TreeRow panel )
	{
		panel.RowIndex = row;
		panel.BindVersion = _bindVersion;
		BindCount++;

		ApplyState( row, panel );
		Place( row, panel );
		BindRow( row, panel );
	}

	void ApplyState( int row, TreeRow panel )
	{
		var layout = _rows[row];
		panel.SetState( layout.Depth, layout.HasChildren, layout.IsOpen, IsRowSelected( row ), IndentWidth );

		var highlight = row == _highlightRow ? _highlightPhase : 0;
		panel.SetClass( "highlight", highlight == 1 );
		panel.SetClass( "highlight-fade", highlight == 2 );
	}

	/// <summary>
	/// Flash a row so the eye finds it: the <c>highlight</c> class for <see cref="HighlightTime"/>,
	/// then <c>highlight-fade</c> for <see cref="HighlightFadeTime"/>.
	/// </summary>
	public void HighlightRow( int row )
	{
		if ( row < 0 || row >= _rows.Count ) return;

		_highlightRow = row;
		_highlightUntil = RealTime.Now + HighlightTime;
		_highlightPhase = 1;
		RefreshRowStates();
	}

	void UpdateHighlight()
	{
		if ( _highlightRow < 0 ) return;

		var phase = RealTime.Now < _highlightUntil ? 1 : RealTime.Now < _highlightUntil + HighlightFadeTime ? 2 : 0;
		if ( phase == _highlightPhase ) return;

		_highlightPhase = phase;
		if ( phase == 0 ) _highlightRow = -1;

		RefreshRowStates();
	}

	internal void SetDragging( bool dragging )
	{
		if ( IsDragging == dragging ) return;

		IsDragging = dragging;
		SetClass( "dragging", dragging );
	}

	void Place( int row, TreeRow panel )
	{
		GetContentRect( out var left, out var top, out var width, out _ );
		var layout = _rows[row];
		panel.SetRect( left, top + layout.Top, width, layout.Height );
	}

	/// <summary>
	/// Push selection and open state into every bound row.
	/// </summary>
	protected void RefreshRowStates()
	{
		for ( int i = 0; i < _active.Count; i++ )
		{
			ApplyState( _activeFirst + i, _active[i] );
		}
	}

	/// <summary>
	/// The bound panel for a row, or null if it isn't in the viewport.
	/// </summary>
	public TreeRow GetRowPanel( int row )
	{
		var i = row - _activeFirst;
		if ( i < 0 || i >= _active.Count ) return null;
		return _active[i];
	}

	protected override void FinalLayoutChildren( Vector2 offset )
	{
		for ( int i = 0; i < _active.Count; i++ )
		{
			_active[i].FinalLayout( offset );
		}

		// Parked rows still need their box moved out of the way
		for ( int i = 0; i < _free.Count; i++ )
		{
			_free[i].FinalLayout( offset );
		}

		var rect = Box.Rect;
		rect.Position -= ScrollOffset;

		var extent = Box.Padding.Top + _totalHeight * ScaleToScreen + Box.Padding.Bottom;
		rect.Height = MathF.Max( extent, rect.Height );

		ConstrainScrolling( rect.Size );

		ScrollbarY?.FinalLayout( offset );
		ScrollbarX?.FinalLayout( offset );
	}

	/// <summary>
	/// Scroll the least amount that brings this row fully into view.
	/// </summary>
	public void ScrollToRow( int row )
	{
		if ( row < 0 || row >= _rows.Count ) return;

		if ( ComputedStyle is null || Box.Rect.Height <= 0 )
		{
			_pendingScroll = row;
			return;
		}

		GetContentRect( out _, out var contentTop, out _, out var viewportHeight );

		var layout = _rows[row];
		var scrollY = ScrollOffset.y * ScaleFromScreen;
		var top = contentTop + layout.Top;
		var bottom = top + layout.Height;

		if ( top < scrollY ) scrollY = top;
		else if ( bottom > scrollY + viewportHeight ) scrollY = bottom - viewportHeight;
		else return;

		ScrollVelocity = 0;
		ScrollOffset = ScrollOffset.WithY( MathF.Max( 0, scrollY ) * ScaleToScreen );
		SetNeedsFinalLayout();
	}

	/// <summary>
	/// Open or close a row. Shift-click on the expander does it recursively.
	/// </summary>
	public void ToggleRow( int row, bool recursive = false )
	{
		if ( row < 0 || row >= _rows.Count ) return;
		SetRowOpen( row, !_rows[row].IsOpen, recursive );
	}

	/// <summary>
	/// Start renaming a row's label in place, scrolling it into view first if needed.
	/// </summary>
	public void BeginRename( int row )
	{
		if ( row < 0 || row >= _rows.Count ) return;
		if ( !CanRenameRow( row ) ) return;

		var panel = GetRowPanel( row );
		if ( panel is null )
		{
			ScrollToRow( row );
			_pendingRename = row;
			return;
		}

		panel.BeginRename();
	}

	internal void RowRenamed( int row, string text )
	{
		OnRowRenamed( row, text );
	}

	internal void RowClicked( int row, MousePanelEvent e )
	{
		SelectRow( row, e.HasCtrl, e.HasShift );
	}

	internal void RowDoubleClicked( int row )
	{
		OnRowActivated( row );
	}

	internal void RowRightClicked( int row )
	{
		if ( !IsRowSelected( row ) )
		{
			SelectRow( row, false, false );
		}

		OnRowContextMenu( row );
	}

	internal void RowDropped( int targetRow, int sourceRow )
	{
		if ( sourceRow < 0 || sourceRow >= _rows.Count ) return;
		if ( targetRow >= _rows.Count ) return;
		OnRowDropped( targetRow, sourceRow );
	}

	/// <summary>
	/// Select a row the way a click would: plain replaces, ctrl toggles, shift ranges from the anchor.
	/// </summary>
	public void SelectRow( int row, bool toggle = false, bool range = false )
	{
		if ( row < 0 || row >= _rows.Count ) return;

		if ( range )
		{
			if ( AnchorRow < 0 ) AnchorRow = CursorRow >= 0 ? CursorRow : row;

			if ( !toggle ) ClearSelectionInternal();

			var a = Math.Min( AnchorRow, row );
			var b = Math.Max( AnchorRow, row );
			for ( int i = a; i <= b; i++ ) SetRowSelected( i, true );
		}
		else if ( toggle )
		{
			SetRowSelected( row, !IsRowSelected( row ) );
			AnchorRow = row;
		}
		else
		{
			ClearSelectionInternal();
			SetRowSelected( row, true );
			AnchorRow = row;
		}

		CursorRow = row;
		RefreshRowStates();
		OnSelectionFinished();
	}

	/// <summary>
	/// Drop the selection, keeping the cursor.
	/// </summary>
	public void ClearSelection()
	{
		ClearSelectionInternal();
		AnchorRow = -1;
		RefreshRowStates();
		OnSelectionFinished();
	}

	/// <summary>
	/// Move the cursor by some rows, selecting (or range-selecting with shift) where it lands.
	/// </summary>
	public void MoveCursor( int delta, bool range = false )
	{
		if ( _rows.Count == 0 ) return;

		var row = CursorRow < 0 ? 0 : Math.Clamp( CursorRow + delta, 0, _rows.Count - 1 );
		SelectRow( row, false, range );
		ScrollToRow( row );
	}

	public override void OnButtonEvent( ButtonEvent e )
	{
		if ( !e.Pressed )
		{
			base.OnButtonEvent( e );
			return;
		}

		if ( HandleKey( e ) )
		{
			e.StopPropagation = true;
			return;
		}

		base.OnButtonEvent( e );
	}

	bool HandleKey( ButtonEvent e )
	{
		switch ( e.Button )
		{
			case "up":
				MoveCursor( -1, e.HasShift );
				return true;

			case "down":
				MoveCursor( 1, e.HasShift );
				return true;

			case "home":
				MoveCursor( -_rows.Count, e.HasShift );
				return true;

			case "end":
				MoveCursor( _rows.Count, e.HasShift );
				return true;

			case "pageup":
				MoveCursor( -PageRows(), e.HasShift );
				return true;

			case "pagedown":
				MoveCursor( PageRows(), e.HasShift );
				return true;

			case "left":
				CursorLeft();
				return true;

			case "right":
				CursorRight();
				return true;

			case "space":
				ToggleRow( CursorRow );
				return true;

			case "enter":
				if ( CursorRow >= 0 ) OnRowActivated( CursorRow );
				return true;

			case "f2":
				BeginRename( CursorRow );
				return true;
		}

		return false;
	}

	int PageRows()
	{
		GetContentRect( out _, out _, out _, out var viewportHeight );
		return Math.Max( 1, (int)(viewportHeight / RowHeight) - 1 );
	}

	/// <summary>
	/// Left closes an open row, otherwise climbs to its parent.
	/// </summary>
	void CursorLeft()
	{
		if ( CursorRow < 0 ) { MoveCursor( 0 ); return; }

		var layout = _rows[CursorRow];
		if ( layout.HasChildren && layout.IsOpen )
		{
			SetRowOpen( CursorRow, false, false );
			return;
		}

		var parent = GetParentRow( CursorRow );
		if ( parent >= 0 ) MoveCursor( parent - CursorRow );
	}

	/// <summary>
	/// Right opens a closed row, otherwise steps into its first child.
	/// </summary>
	void CursorRight()
	{
		if ( CursorRow < 0 ) { MoveCursor( 0 ); return; }

		var layout = _rows[CursorRow];
		if ( !layout.HasChildren ) return;

		if ( !layout.IsOpen )
		{
			SetRowOpen( CursorRow, true, false );
			return;
		}

		MoveCursor( 1 );
	}

	protected override void OnClick( MousePanelEvent e )
	{
		// A click on the empty space below the rows drops the selection
		if ( e.Target != this ) return;
		if ( e.HasCtrl || e.HasShift ) return;

		ClearSelection();
	}

	protected override void OnDrop( PanelEvent e )
	{
		if ( e is DropEvent ) return;
		if ( e.Target is not TreeRow source || source.Tree != this ) return;

		RowDropped( -1, source.RowIndex );
		e.StopPropagation();
	}
}
