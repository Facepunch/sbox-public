namespace Sandbox.UI.Layout;

/// <summary>
/// Layout items in a vertical list
/// </summary>
internal class VerticalListLayout
{
	private float _itemHeight = 100f;

	private Rect _rect;        // inner/content (local)
	private Rect _outerRect;   // outer/viewport (local)
	private float _scrollOffset;

	private Vector2 _cellSize;
	private int _updateHash;

	/// <summary>
	/// Fixed item height (≥ 1).
	/// Values set below 1 are clamped to 1.
	/// </summary>
	public float ItemHeight
	{
		get => _itemHeight;
		set => _itemHeight = MathF.Max( 1f, value );
	}

	/// <summary>
	/// Gaps between items.
	/// X is ignored; Y is row gap.
	/// </summary>
	public Vector2 Spacing { get; set; } = 0;

	/// <summary>
	/// Update layout state from the given box, scale, and scroll.
	/// Returns true when internal state changed.
	/// </summary>
	public bool Update( Box box, float scaleFromScreen, float scrollOffset )
	{
		var hash = HashCode.Combine( box.RectInner, scaleFromScreen, scrollOffset, _itemHeight, Spacing );
		if ( hash == _updateHash ) return false;
		_updateHash = hash;

		var inner = box.RectInner;
		inner.Position = box.RectInner.Position - box.Rect.Position;

		_rect = inner * scaleFromScreen;
		_outerRect = box.Rect * scaleFromScreen;
		_scrollOffset = scrollOffset;

		_cellSize.x = MathF.Max( 1f, _rect.Width ); // fill width
		_cellSize.y = _itemHeight;

		return true;
	}

	/// <summary>
	/// Compute the visible index range [firstIndex, lastIndex).
	/// Compensates for inner top offset so top padding does not cull early items.
	/// </summary>
	public void GetVisibleRange( out int firstIndex, out int lastIndex )
	{
		float step = MathF.Max( 1f, _cellSize.y + Spacing.y );

		int top = ((_scrollOffset - _rect.Top) / step).FloorToInt();
		if ( top < 0 ) top = 0;

		int fit = (_outerRect.Height / step).CeilToInt() + 1;

		firstIndex = top;
		lastIndex = firstIndex + fit; // exclusive
	}

	/// <summary>
	/// Get the index of the item at the given local y position.
	/// </summary>
	public int GetIndexAt( float y )
	{
		float step = MathF.Max( 1f, _cellSize.y + Spacing.y );
		return ((y - _rect.Top) / step).FloorToInt();
	}

	/// <summary>
	/// Get the rectangle for the item at the given index.
	/// </summary>
	public Rect GetPosition( int index )
	{
		float step = _cellSize.y + Spacing.y;
		return new Rect( _rect.Left, _rect.Top + index * step, _cellSize.x, _cellSize.y );
	}

	/// <summary>
	/// Apply the item rectangle for <paramref name="index"/> to <paramref name="panel"/>.
	/// </summary>
	public void Position( int index, Panel panel )
	{
		var r = GetPosition( index );
		panel.Style.Left = r.Left;
		panel.Style.Top = r.Top;
		panel.Style.Width = r.Width;
		panel.Style.Height = r.Height;
		panel.Style.Dirty();
	}

	/// <summary>
	/// Calculate total content height for the given item count.
	/// </summary>
	public float GetHeight( int count )
	{
		float step = _cellSize.y + Spacing.y;
		if ( step <= 0f ) return MathF.Max( 0f, _outerRect.Height - _rect.Height );

		float paddingY = _outerRect.Height - _rect.Height;
		return count * step + MathF.Max( 0f, paddingY );
	}
}
