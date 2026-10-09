using Microsoft.AspNetCore.Components;
using System.Collections;

namespace Sandbox.UI;

/// <summary>
/// Base class for virtualized, scrollable panels that only create item panels when visible.
/// </summary>
public abstract class BaseVirtualPanel : Panel
{
	protected readonly Dictionary<int, object> _cellData = new();   // index -> last data used to build the cell
	protected readonly Dictionary<int, Panel> _created = new();    // index -> created panel
	protected readonly List<int> _removals = new();    // temp list for removal without mutating during iteration
	protected readonly List<object> _items = new();    // backing store for Items

	/// <summary>
	/// When setting the items from a List we keep a reference to it here for change detection.
	/// </summary>
	private IList _sourceList;
	private int _sourceListCount;
	protected bool _lastCellCreated;

	/// <summary>
	/// When true, forces a layout rebuild on the next <see cref="Tick"/>.
	/// </summary>
	public bool NeedsRebuild { get; set; }

	/// <summary>
	/// Template used to render an item into a cell panel.
	/// </summary>
	[Parameter]
	public RenderFragment<object> Item { get; set; }

	/// <summary>
	/// Called when a cell is created. Allows you to fill the cell in
	/// </summary>
	[Parameter]
	public Action<Panel, object> OnCreateCell { get; set; }

	/// <summary>
	/// Called when the last cell has been viewed. This allows you to view more.
	/// </summary>
	[Parameter]
	public Action OnLastCell { get; set; }

	/// <summary>
	/// Initializes the base virtual panel with default styles.
	/// </summary>
	protected BaseVirtualPanel()
	{
		Style.Position = PositionMode.Relative;
		Style.Overflow = OverflowMode.Scroll;
	}

	/// <summary>
	/// Replaces the current items. Only triggers a rebuild if the sequence is actually different.
	/// When set to an IList (like List&lt;T&gt;), changes to the source list will be automatically detected.
	/// </summary>
	[Parameter]
	public IEnumerable<object> Items
	{
		set
		{
			if ( value is null )
			{
				if ( _items.Count == 0 ) return;
				Clear();
				return;
			}

			// Try to keep a reference to the original list for change detection
			_sourceList = value as IList;

			// Materialize the items - use _sourceList if available to avoid double enumeration
			IList<object> materializedList;
			if ( _sourceList != null )
			{
				// Cast from non-generic IList to List<object>
				materializedList = new List<object>( _sourceList.Count );
				foreach ( var item in _sourceList )
					materializedList.Add( item );
			}
			else
			{
				// Fallback for pure IEnumerable
				materializedList = value.ToList();
				_sourceList = null;
				_sourceListCount = 0;
			}

			_sourceListCount = materializedList.Count;

			// Fast length check, then content equality with the configured comparer.
			if ( _items.Count == materializedList.Count && _items.SequenceEqual( materializedList, EqualityComparer<object>.Default ) )
				return;

			_items.Clear();
			_items.AddRange( materializedList );

			NeedsRebuild = true;
			_lastCellCreated = false;

			StateHasChanged();
		}
	}

	/// <summary>
	/// Adds a single item and marks the panel for rebuild.
	/// </summary>
	/// <param name="item">The item to append.</param>
	public void AddItem( object item )
	{
		_items.Add( item );
		NeedsRebuild = true;
	}

	/// <summary>
	/// Adds multiple items and marks the panel for rebuild.
	/// </summary>
	/// <param name="items">Items to append.</param>
	public void AddItems( IEnumerable<object> items )
	{
		_items.AddRange( items as IList<object> ?? items.ToList() );
		NeedsRebuild = true;
	}

	/// <summary>
	/// Removes the first occurrence of a specific item and marks the panel for rebuild.
	/// </summary>
	/// <param name="item">The item to remove.</param>
	/// <returns>True if item was found and removed; otherwise false.</returns>
	public bool RemoveItem( object item )
	{
		var removed = _items.Remove( item );
		if ( removed )
			NeedsRebuild = true;
		return removed;
	}

	/// <summary>
	/// Removes the item at the specified index and marks the panel for rebuild.
	/// </summary>
	/// <param name="index">The zero-based index of the item to remove.</param>
	public void RemoveAt( int index )
	{
		_items.RemoveAt( index );
		NeedsRebuild = true;
	}

	/// <summary>
	/// Inserts an item at the specified index and marks the panel for rebuild.
	/// </summary>
	/// <param name="index">The zero-based index at which item should be inserted.</param>
	/// <param name="item">The item to insert.</param>
	public void InsertItem( int index, object item )
	{
		_items.Insert( index, item );
		NeedsRebuild = true;
	}

	/// <summary>
	/// Clears all items and destroys created panels.
	/// </summary>
	public void Clear()
	{
		_items.Clear();
		_sourceList = null;
		_sourceListCount = 0;
		NeedsRebuild = true;

		foreach ( var p in _created.Values )
			p.Delete( true );

		_created.Clear();
		_cellData.Clear();
	}

	/// <summary>
	/// Checks the source list for changes and updates the internal items if needed.
	/// </summary>
	private void CheckSourceForChanges()
	{
		if ( _sourceList is null ) return;
		if ( _sourceList.Count == _sourceListCount ) return;

		// Count has changed - update our cached items
		_items.Clear();
		_sourceListCount = _sourceList.Count;

		// Cast items from the non-generic IList to object
		foreach ( var item in _sourceList )
			_items.Add( item );

		NeedsRebuild = true;
		_lastCellCreated = false;

		StateHasChanged();
	}

	/// <summary>
	/// Per-frame update: adjusts spacing from CSS, updates layout, creates/destroys visible panels.
	/// </summary>
	public override void Tick()
	{
		base.Tick();

		if ( ComputedStyle is null || !IsVisible ) return;

		CheckSourceForChanges();

		// Pull CSS gaps into layout spacing.
		UpdateLayoutSpacing( new(
			ComputedStyle.ColumnGap?.Value ?? 0,
			ComputedStyle.RowGap?.Value ?? 0
		) );

		// Recompute layout if needed or if explicitly requested.
		if ( UpdateLayout() || NeedsRebuild )
		{
			NeedsRebuild = false;

			// Get visible index range [first, pastEnd)
			GetVisibleRange( out var first, out var pastEnd );

			// Remove anything outside the visible window or without data.
			DeleteNotVisible( first, pastEnd - 1 );

			// Ensure visible cells exist and are positioned.
			for ( int i = first; i < pastEnd; i++ )
				RefreshCreated( i );
		}
	}

	/// <summary>
	/// Final layout pass for child panels and scroll bounds.
	/// </summary>
	/// <param name="offset">Layout offset.</param>
	protected override void FinalLayoutChildren( Vector2 offset )
	{
		foreach ( var kv in _created )
			kv.Value.FinalLayout( offset );

		// Extend scrollable height to fit all rows.
		var rect = Box.Rect;
		rect.Position -= ScrollOffset;
		rect.Height = MathF.Max( GetTotalHeight( _items.Count ) * ScaleToScreen, rect.Height );

		ConstrainScrolling( rect.Size );
	}

	/// <summary>
	/// Returns true if <paramref name="i"/> is a valid item index.
	/// </summary>
	/// <param name="i">Item index.</param>
	/// <returns>True if within bounds; otherwise false.</returns>
	public bool HasData( int i ) => i >= 0 && i < _items.Count;

	/// <summary>
	/// Gets the number of items in the panel.
	/// </summary>
	public int ItemCount => _items.Count;

	/// <summary>
	/// Convenience helper that sets <see cref="Items"/>.
	/// </summary>
	/// <param name="enumerable">New items sequence.</param>
	public void SetItems( IEnumerable<object> enumerable ) => Items = enumerable;

	// Remove panels not in [minInclusive, maxInclusive] or with missing data.
	private void DeleteNotVisible( int minInclusive, int maxInclusive )
	{
		_removals.Clear();

		foreach ( var idx in _created.Keys )
		{
			if ( idx < minInclusive || idx > maxInclusive || !HasData( idx ) )
				_removals.Add( idx );
		}

		for ( int i = 0; i < _removals.Count; i++ )
		{
			var idx = _removals[i];
			if ( _created.Remove( idx, out var panel ) )
				panel.Delete( true );
			_cellData.Remove( idx );
		}

		_removals.Clear();
	}

	// Ensure a panel exists for index i, rebuilding if data changed, then position it.
	private void RefreshCreated( int i )
	{
		if ( !HasData( i ) ) return;

		var data = _items[i];
		var needsRebuild = !_cellData.TryGetValue( i, out var last ) || !EqualityComparer<object>.Default.Equals( last, data );

		if ( !_created.TryGetValue( i, out var panel ) || needsRebuild )
		{
			panel?.Delete( true );

			panel = Add.Panel( "cell" );
			panel.Style.Position = PositionMode.Absolute;
			panel.ChildContent = Item?.Invoke( data );

			_created[i] = panel;
			_cellData[i] = data;

			OnCreateCell?.Invoke( panel, data );

			if ( _items.Count - 1 == i )
			{
				OnCreatedLastCell();
			}
		}

		PositionPanel( i, panel );
	}

	private void OnCreatedLastCell()
	{
		if ( _lastCellCreated ) return;

		_lastCellCreated = true;
		OnLastCell?.InvokeWithWarning();
	}

	/// <summary>
	/// Updates the layout spacing based on CSS gaps.
	/// </summary>
	/// <param name="spacing">The spacing vector from CSS.</param>
	protected abstract void UpdateLayoutSpacing( Vector2 spacing );

	/// <summary>
	/// Updates the layout and returns true if the layout changed.
	/// </summary>
	/// <returns>True if layout was updated; otherwise false.</returns>
	protected abstract bool UpdateLayout();

	/// <summary>
	/// Gets the range of visible item indices.
	/// </summary>
	/// <param name="first">First visible index (inclusive).</param>
	/// <param name="pastEnd">Past-the-end index (exclusive).</param>
	protected abstract void GetVisibleRange( out int first, out int pastEnd );

	/// <summary>
	/// Positions a panel at the specified index.
	/// </summary>
	/// <param name="index">Item index.</param>
	/// <param name="panel">Panel to position.</param>
	protected abstract void PositionPanel( int index, Panel panel );

	/// <summary>
	/// Gets the total height needed to display the specified number of items.
	/// </summary>
	/// <param name="itemCount">Number of items.</param>
	/// <returns>Total height in layout units.</returns>
	protected abstract float GetTotalHeight( int itemCount );
}
