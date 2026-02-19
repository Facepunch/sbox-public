using Sandbox.UI;
using System;
namespace Editor;

public partial class ListView
{
	/// <summary>
	/// Rebuild the scrollbars and layout for the visible items
	/// </summary>
	protected override void Rebuild()
	{
		if ( !IsValid )
			return;

		LayoutScrollbar();
		LayoutItems();
		Update();
	}

	/// <summary>
	/// Work out how big the scrollbars need to be and layout the current PVS
	/// </summary>
	protected virtual void LayoutScrollbar()
	{
		var rect = CanvasRect;

		itemsPerRow = 1;
		if ( ItemSize.x > 0 ) itemsPerRow = ((rect.Width + ItemSpacing.x) / (ItemSize.x + ItemSpacing.x)).FloorToInt();
		itemsPerRow = Math.Max( 1, itemsPerRow );

		rowHeight = (ItemSize.y + ItemSpacing.y).CeilToInt();
		rowCount = ((float)_items.Count() / (float)itemsPerRow).CeilToInt();

		var canvasHeight = rowCount * ItemSize.y;

		if ( rowCount > 0 )
			canvasHeight += (rowCount - 1) * ItemSpacing.y;

		VerticalScrollbar.Minimum = 0;
		VerticalScrollbar.Maximum = Math.Max( VerticalScrollbar.Minimum, (canvasHeight - rect.Height).CeilToInt() );
		VerticalScrollbar.SingleStep = Math.Max( rowHeight, rect.Height * 0.33f ).CeilToInt();
		VerticalScrollbar.PageStep = rect.Height.FloorToInt();
	}

	protected virtual void LayoutItems()
	{
		//
		// Plenty of optimization here.
		// Like on scroll we probably don't need to lay the items out unless the scroll differs by rowheight amount
		// and this dictionary call here is probably pain.
		// but ultimately the repaint is going to take as long as this, and it's still pretty fast, so fuck it for now.
		//

		var old = ItemLayouts.ToList();
		ItemLayouts.Clear();

		if ( _items.Count == 0 )
		{
			if ( ItemScrollExit is not null )
			{
				foreach ( var item in old )
				{
					ItemScrollExit.Invoke( item.Object );
				}
			}

			return;
		}

		var rect = CanvasRect;

		float scrollOffset = VerticalScrollbar.Value - rect.Top;

		float visibleHeight = Height;
		float rowHeight = ItemSize.y + ItemSpacing.y;
		float offset = scrollOffset % rowHeight;
		int firstVisibleRow = (scrollOffset / rowHeight).FloorToInt();
		if ( firstVisibleRow < 0 ) firstVisibleRow = 0;
		int firstVisibleIndex = firstVisibleRow * itemsPerRow;

		int visibleRows = ((visibleHeight + offset) / ItemSize.y).CeilToInt();
		int visibleItems = visibleRows * itemsPerRow;

		var subset = _items.Skip( firstVisibleIndex ).Take( visibleItems );

		var itemRect = new Rect( 0, ItemSize );
		if ( ItemSize.x <= 0 ) itemRect.Width = rect.Width;

		var col = 0;
		var row = 0;

		float rowStart = 0;
		float rowSpacing = ItemSpacing.x;
		float spareSpace = (rect.Width + ItemSpacing.x) - (itemRect.Width + ItemSpacing.x) * itemsPerRow;
		spareSpace = Math.Max( 0, spareSpace );

		switch ( ItemAlign )
		{
			case Align.Stretch:
				{
					itemRect.Width += spareSpace / itemsPerRow;
					break;
				}

			case Align.Center:
				{
					rowStart = spareSpace * 0.5f;
					break;
				}

			case Align.SpaceAround:
				{
					rowSpacing = (spareSpace / (itemsPerRow));
					rowStart = rowSpacing / 2.0f;
					rowSpacing += ItemSpacing.x;
					break;
				}

			case Align.SpaceBetween:
				{
					if ( itemsPerRow > 1 )
					{
						var items = Math.Min( _items.Count, itemsPerRow );
						rowStart = 0;
						rowSpacing = (rect.Width - items * ItemSize.x) / Math.Max( 1, (items - 1) );
					}
					break;
				}

			default:
				break;
		}

		foreach ( var item in subset )
		{
			itemRect.Position = new Vector2( rect.Left + rowStart + col * (itemRect.Width + rowSpacing), row * (ItemSize.y + ItemSpacing.y) - offset );

			var lo = old.FirstOrDefault( x => x.Object.Equals( item ) );

			if ( lo == null )
			{
				lo = new VirtualWidget();
				lo.Selected = SelectedItems.Contains( item );

				ItemScrollEnter?.Invoke( item );
			}
			else
			{
				old.Remove( lo );
			}

			lo.Object = item;
			lo.Rect = itemRect;
			lo.Row = firstVisibleRow + row;
			lo.Column = col;
			ItemLayouts.Add( lo );

			col++;
			if ( col >= itemsPerRow )
			{
				row++;
				col = 0;
			}
		}

		if ( ItemScrollExit is not null )
		{
			// culling callback
			foreach ( var item in old )
			{
				ItemScrollExit.Invoke( item.Object );
			}
		}
	}
}
