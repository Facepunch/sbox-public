using Microsoft.AspNetCore.Components;
using Sandbox;

namespace MenuProject;

/// <summary>
/// A virtualized, scrollable list whose items can each be a different height - section headers, cards
/// and rows all in the one list. Like <see cref="VirtualList"/>, only what's on screen gets a panel.
/// Each item's height comes from <see cref="ItemHeight"/>, so it has to match what the item draws.
/// </summary>
public sealed class MixedVirtualList : BaseVirtualPanel
{
	/// <summary>
	/// How tall an item is, in layout units.
	/// </summary>
	[Parameter]
	public Func<object, float> ItemHeight { get; set; }

	readonly List<float> _tops = new();    // where each item starts, down from the content's top
	readonly List<float> _heights = new();
	float _contentHeight;
	Vector2 _spacing;
	bool _offsetsDirty = true;

	Rect _rect;       // inner/content (local)
	Rect _outerRect;  // outer/viewport (local)
	float _scrollOffset;
	int _updateHash;

	/// <summary>
	/// Re-render the cells on screen. They're only built when they first appear or their item
	/// changes - call this when something else they show has changed.
	/// </summary>
	public void RefreshCells()
	{
		foreach ( var panel in _created.Values )
			panel.StateHasChanged();
	}

	protected override void UpdateLayoutSpacing( Vector2 spacing )
	{
		if ( _spacing == spacing ) return;

		_spacing = spacing;
		_offsetsDirty = true;
	}

	protected override bool UpdateLayout()
	{
		var offsetsChanged = _offsetsDirty || NeedsRebuild || _tops.Count != _items.Count;
		if ( offsetsChanged )
		{
			RebuildOffsets();
			MoveCells();
		}

		var hash = HashCode.Combine( Box.RectInner, ScaleFromScreen, ScrollOffset.y, _contentHeight );
		if ( hash == _updateHash && !offsetsChanged ) return false;
		_updateHash = hash;

		var inner = Box.RectInner;
		inner.Position = Box.RectInner.Position - Box.Rect.Position;

		_rect = inner * ScaleFromScreen;
		_outerRect = Box.Rect * ScaleFromScreen;
		_scrollOffset = ScrollOffset.y * ScaleFromScreen;

		return true;
	}

	void RebuildOffsets()
	{
		_offsetsDirty = false;
		_tops.Clear();
		_heights.Clear();

		var y = 0f;

		for ( int i = 0; i < _items.Count; i++ )
		{
			// Zero is allowed - an item can shrink away to nothing while it animates out
			var height = MathF.Max( 0f, ItemHeight?.Invoke( _items[i] ) ?? 32f );

			if ( i > 0 ) y += _spacing.y;

			_tops.Add( y );
			_heights.Add( height );
			y += height;
		}

		_contentHeight = y;
	}

	/// <summary>
	/// Cells are kept by index, and one whose index now holds a different item gets torn down and
	/// built again - so inserting or removing items rebuilds every cell after them, and a new cell is
	/// empty for a frame. Before that happens, move each cell to wherever its item went.
	/// </summary>
	void MoveCells()
	{
		// The cells whose index now holds something else
		_movedFrom.Clear();
		foreach ( var (index, data) in _cellData )
		{
			if ( index >= _items.Count || !EqualityComparer<object>.Default.Equals( _items[index], data ) )
				_movedFrom.Add( index );
		}

		if ( _movedFrom.Count == 0 ) return;

		// Lift them all out first, so one landing on another's old index can't collide with it
		_moving.Clear();
		foreach ( var index in _movedFrom )
		{
			var data = _cellData[index];
			_cellData.Remove( index );

			if ( !_created.Remove( index, out var panel ) ) continue;

			if ( data is null || !_moving.TryAdd( data, panel ) )
				panel.Delete( true );
		}

		_movedFrom.Clear();

		// Then put each down where its item is now
		for ( int i = 0; i < _items.Count && _moving.Count > 0; i++ )
		{
			var item = _items[i];
			if ( item is null || _created.ContainsKey( i ) ) continue;
			if ( !_moving.Remove( item, out var panel ) ) continue;

			_created[i] = panel;
			_cellData[i] = item;
		}

		// Whatever's left, its item is gone
		foreach ( var panel in _moving.Values )
			panel.Delete( true );

		_moving.Clear();
	}

	readonly Dictionary<object, Panel> _moving = new();
	readonly List<int> _movedFrom = new();

	protected override void GetVisibleRange( out int first, out int pastEnd )
	{
		var top = _scrollOffset - _rect.Top;
		var bottom = top + _outerRect.Height;

		// The first item that ends below the top of the view
		int lo = 0, hi = _tops.Count;
		while ( lo < hi )
		{
			var mid = (lo + hi) / 2;
			if ( _tops[mid] + _heights[mid] <= top ) lo = mid + 1;
			else hi = mid;
		}

		first = lo;
		pastEnd = first;

		while ( pastEnd < _tops.Count && _tops[pastEnd] < bottom )
			pastEnd++;
	}

	protected override void PositionPanel( int index, Panel panel )
	{
		if ( index >= _tops.Count ) return;

		// Each edge snapped to a whole screen pixel, from the same sum its neighbour uses - so one
		// cell's bottom is exactly the next one's top. Left to the layout, the two round separately at
		// fractional scales and a hairline of whatever's behind shows through between them.
		var top = Snap( _rect.Top + _tops[index] );
		var bottom = Snap( _rect.Top + _tops[index] + _heights[index] );

		panel.Style.Left = _rect.Left;
		panel.Style.Top = top;
		panel.Style.Width = MathF.Max( 1f, _rect.Width );
		panel.Style.Height = MathF.Max( 0f, bottom - top );

		// Shrunk away to nothing - hide it. Clipping can't be trusted to: a zero-size box doesn't clip
		// its overflow, so the item would draw in full over whatever's below.
		panel.Style.Display = bottom > top ? DisplayMode.Flex : DisplayMode.None;
		panel.Style.Dirty();
	}

	/// <summary>
	/// The nearest layout position that lands on a whole screen pixel.
	/// </summary>
	float Snap( float units )
	{
		var scale = ScaleToScreen;
		if ( scale <= 0 ) return units;

		return MathF.Round( units * scale ) / scale;
	}

	protected override float GetTotalHeight( int itemCount )
	{
		var padding = MathF.Max( 0f, _outerRect.Height - _rect.Height );
		return _contentHeight + padding;
	}
}
