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
			RebuildOffsets();

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
			var height = MathF.Max( 1f, ItemHeight?.Invoke( _items[i] ) ?? 32f );

			if ( i > 0 ) y += _spacing.y;

			_tops.Add( y );
			_heights.Add( height );
			y += height;
		}

		_contentHeight = y;
	}

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
		panel.Style.Height = MathF.Max( 1f, bottom - top );
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
