using Microsoft.AspNetCore.Components;

namespace Sandbox.UI;

/// <summary>
/// Implemented by items that occupy a complete row in a <see cref="MixedVirtualGrid"/>.
/// </summary>
public interface IMixedVirtualGridFullRow
{
	bool IsFullRow { get; }
	float Height { get; }
}

/// <summary>
/// A virtual grid that mixes fixed-size tiles with full-width rows.
/// </summary>
public class MixedVirtualGrid : BaseVirtualPanel
{
	private readonly record struct Row( int FirstIndex, int Count, float Top, float Height, bool FullWidth );

	private readonly List<Row> _rows = new();
	private int[] _rowForItem = [];
	private Rect _innerRect;
	private float _viewportHeight;
	private float _totalHeight;
	private float _tileWidth;
	private float _tileHeight;
	private Vector2 _spacing;
	private int _columns = 1;
	private int _geometryHash;
	private int _updateHash;
	private int _dropIndex = -1;

	[Parameter] public Vector2 ItemSize { get; set; } = new( 100, 100 );
	[Parameter] public bool FillWidth { get; set; } = true;
	[Parameter] public Func<PanelEvent, bool> CanAcceptDrop { get; set; }
	[Parameter] public Action<int, PanelEvent> OnDropIndex { get; set; }
	private bool _dragHover;

	/// <summary>
	/// Builds the cells again, for when they show more than their item, like the tile size.
	/// </summary>
	public void RecreateCells()
	{
		foreach ( var panel in _created.Values )
			panel.Delete( true );

		_created.Clear();
		_cellData.Clear();
		NeedsRebuild = true;
	}

	protected override void UpdateLayoutSpacing( Vector2 spacing )
	{
		_spacing = spacing;
	}

	protected override bool UpdateLayout()
	{
		var geometryHash = HashCode.Combine( Box.RectInner, Box.Rect, ScaleFromScreen, ItemSize, FillWidth, _spacing, _items.Count );
		if ( NeedsRebuild || geometryHash != _geometryHash )
		{
			_geometryHash = geometryHash;
			RebuildRows();
		}

		var updateHash = HashCode.Combine( geometryHash, ScrollOffset.y );
		if ( updateHash == _updateHash ) return false;

		_updateHash = updateHash;
		return true;
	}

	private void RebuildRows()
	{
		_rows.Clear();
		_rowForItem = new int[_items.Count];
		_totalHeight = 0f;

		var inner = Box.RectInner;
		inner.Position -= Box.Rect.Position;
		_innerRect = inner * ScaleFromScreen;
		_viewportHeight = Box.Rect.Height * ScaleFromScreen;

		var nominalWidth = MathF.Max( 1f, ItemSize.x );
		var nominalHeight = MathF.Max( 1f, ItemSize.y );
		var stepX = nominalWidth + _spacing.x;
		var fit = (_innerRect.Width + _spacing.x) / stepX;
		_tileWidth = nominalWidth;
		_tileHeight = nominalHeight;

		if ( FillWidth )
		{
			_columns = Math.Max( 1, (int)MathF.Round( fit ) );
			_tileWidth = MathF.Max( 1f, (_innerRect.Width - (_columns - 1) * _spacing.x) / _columns );
			_tileHeight = MathF.Max( 1f, _tileWidth * nominalHeight / nominalWidth );
		}
		else
		{
			_columns = Math.Max( 1, fit.FloorToInt() );
		}

		var top = _innerRect.Top;
		var index = 0;
		while ( index < _items.Count )
		{
			var fullHeight = FullRowHeight( _items[index] );
			if ( fullHeight is not null )
			{
				AddRow( index, 1, top, fullHeight.Value, true );
				top += fullHeight.Value + _spacing.y;
				index++;
				continue;
			}

			var count = 1;
			while ( count < _columns && index + count < _items.Count && FullRowHeight( _items[index + count] ) is null )
				count++;

			AddRow( index, count, top, _tileHeight, false );
			top += _tileHeight + _spacing.y;
			index += count;
		}

		var bottomPadding = MathF.Max( 0f, _viewportHeight - _innerRect.Bottom );
		_totalHeight = MathF.Max( 0f, top + bottomPadding );
	}

	private static float? FullRowHeight( object item )
	{
		if ( item is not IMixedVirtualGridFullRow { IsFullRow: true } fullRow ) return null;
		if ( !float.IsFinite( fullRow.Height ) ) return 1f;
		return MathF.Max( 1f, fullRow.Height );
	}

	private void AddRow( int firstIndex, int count, float top, float height, bool fullWidth )
	{
		var rowIndex = _rows.Count;
		_rows.Add( new Row( firstIndex, count, top, height, fullWidth ) );
		for ( var i = 0; i < count; i++ )
			_rowForItem[firstIndex + i] = rowIndex;
	}

	protected override void GetVisibleRange( out int first, out int pastEnd )
	{
		if ( _rows.Count == 0 )
		{
			first = 0;
			pastEnd = 0;
			return;
		}

		var visibleTop = ScrollOffset.y * ScaleFromScreen;
		var visibleBottom = visibleTop + _viewportHeight;
		var firstRow = _rows.FindIndex( row => row.Top + row.Height > visibleTop );
		if ( firstRow < 0 ) firstRow = _rows.Count - 1;
		firstRow = Math.Max( 0, firstRow - 1 );

		var pastRow = _rows.FindIndex( firstRow, row => row.Top >= visibleBottom );
		if ( pastRow < 0 ) pastRow = _rows.Count;
		pastRow = Math.Min( _rows.Count, pastRow + 1 );

		first = _rows[firstRow].FirstIndex;
		var last = _rows[pastRow - 1];
		pastEnd = last.FirstIndex + last.Count;
	}

	protected override void PositionPanel( int index, Panel panel )
	{
		if ( index < 0 || index >= _rowForItem.Length ) return;

		var row = _rows[_rowForItem[index]];
		var column = index - row.FirstIndex;
		var rect = row.FullWidth
			? new Rect( _innerRect.Left, row.Top, _innerRect.Width, row.Height )
			: new Rect( _innerRect.Left + column * (_tileWidth + _spacing.x), row.Top, _tileWidth, _tileHeight );

		panel.Style.Left = rect.Left;
		panel.Style.Top = rect.Top;
		panel.Style.Width = rect.Width;
		panel.Style.Height = rect.Height;
		panel.SetClass( "full-row", row.FullWidth );
		panel.Style.Dirty();
	}

	protected override float GetTotalHeight( int itemCount ) => _totalHeight;

	public int GetDropIndex( Vector2 viewportPosition )
	{
		if ( _rows.Count == 0 ) return 0;

		var point = (viewportPosition + ScrollOffset) * ScaleFromScreen;
		if ( point.y < _rows[0].Top ) return 0;

		var rowIndex = _rows.FindLastIndex( row => row.Top <= point.y );
		if ( rowIndex < 0 ) return 0;

		var row = _rows[rowIndex];
		if ( point.y > row.Top + row.Height )
			return row.FirstIndex + row.Count;

		if ( row.FullWidth )
			return row.FirstIndex + (point.y >= row.Top + row.Height * 0.5f ? 1 : 0);

		var firstCenter = _innerRect.Left + _tileWidth * 0.5f;
		var slot = ((point.x - firstCenter) / (_tileWidth + _spacing.x)).FloorToInt() + 1;
		return row.FirstIndex + slot.Clamp( 0, row.Count );
	}

	protected override void OnDragEnter( PanelEvent e )
	{
		base.OnDragEnter( e );
		if ( OnDropIndex is null || CanAcceptDrop?.Invoke( e ) != true ) return;

		_dragHover = true;
		SetDropIndex( GetDropIndex( MousePosition ) );
		e.StopPropagation();
	}

	protected override void OnDragLeave( PanelEvent e )
	{
		base.OnDragLeave( e );
		_dragHover = false;
		ClearDropIndex();
	}

	protected override void OnDrop( PanelEvent e )
	{
		base.OnDrop( e );
		if ( OnDropIndex is null || CanAcceptDrop?.Invoke( e ) != true ) return;

		var index = GetDropIndex( MousePosition );
		_dragHover = false;
		ClearDropIndex();
		OnDropIndex( index, e );
		e.StopPropagation();
	}

	public override void Tick()
	{
		base.Tick();
		if ( !_dragHover ) return;
		if ( CanAcceptDrop?.Invoke( null ) != true )
		{
			_dragHover = false;
			ClearDropIndex();
			return;
		}

		var edge = 36f;
		if ( MousePosition.y < edge ) ScrollOffset -= new Vector2( 0, 12 );
		if ( MousePosition.y > Box.Rect.Height - edge ) ScrollOffset += new Vector2( 0, 12 );
		SetDropIndex( GetDropIndex( MousePosition ) );
	}

	private void SetDropIndex( int index )
	{
		if ( _dropIndex == index ) return;
		ClearDropIndex();
		_dropIndex = index;

		if ( index < _items.Count && _created.TryGetValue( index, out var before ) )
			before.AddClass( "drop-before" );
		else if ( index > 0 && _created.TryGetValue( index - 1, out var after ) )
			after.AddClass( "drop-after" );
	}

	private void ClearDropIndex()
	{
		if ( _dropIndex < 0 ) return;
		foreach ( var panel in _created.Values )
		{
			panel.RemoveClass( "drop-before" );
			panel.RemoveClass( "drop-after" );
		}
		_dropIndex = -1;
	}
}
