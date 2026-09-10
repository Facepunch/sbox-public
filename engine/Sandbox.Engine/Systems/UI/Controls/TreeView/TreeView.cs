using Microsoft.AspNetCore.Components;

namespace Sandbox.UI;

/// <summary>
/// A virtualized tree over your own objects. You give it roots and a way to get children; it
/// keeps a flat list of the rows that are reachable through open nodes and only ever creates
/// enough row panels to fill the viewport. Give <see cref="GetChildren"/> lists or arrays and
/// scrolling allocates nothing.
/// </summary>
public class TreeView<T> : BaseTreeView
{
	readonly List<T> _rowItems = new();
	readonly List<IList<T>> _rowChildLists = new();
	readonly List<int> _rowChildCounts = new();
	readonly List<T> _path = new();

	IEnumerable<T> _roots;
	IList<T> _rootList;
	int _rootCount;
	IEqualityComparer<T> _comparer = EqualityComparer<T>.Default;
	HashSet<T> _open = new();
	HashSet<T> _selection = new();

	/// <summary>
	/// The top level items. Pass a list or array and additions and removals are picked up automatically.
	/// </summary>
	[Parameter]
	public IEnumerable<T> Roots
	{
		get => _roots;
		set
		{
			if ( ReferenceEquals( _roots, value ) ) return;

			_roots = value;
			_rootList = value as IList<T>;
			NeedsRebuild = true;
		}
	}

	/// <summary>
	/// The children of an item. Return a list or array to avoid allocating enumerators.
	/// </summary>
	[Parameter]
	public Func<T, IEnumerable<T>> GetChildren { get; set; }

	/// <summary>
	/// The parent of an item, or default for a root. Optional, but without it opening the path to
	/// an item is a search down from the roots, which walks a lazy tree into existence.
	/// </summary>
	[Parameter]
	public Func<T, T> GetParent { get; set; }

	/// <summary>
	/// Whether an item can be expanded. Optional - without it <see cref="GetChildren"/> is asked.
	/// </summary>
	[Parameter]
	public Func<T, bool> CanExpand { get; set; }

	/// <summary>
	/// Height of a specific item's row. Optional - without it every row is <see cref="BaseTreeView.RowHeight"/>.
	/// </summary>
	[Parameter]
	public Func<T, float> GetHeight { get; set; }

	/// <summary>
	/// Hover text for an item.
	/// </summary>
	[Parameter]
	public Func<T, string> GetTooltip { get; set; }

	/// <summary>
	/// Whether F2 can rename an item. Optional - without it any item can when <see cref="OnRename"/> is set.
	/// </summary>
	[Parameter]
	public Func<T, bool> CanRename { get; set; }

	/// <summary>
	/// Fill a row panel from its item. Called when a row scrolls in or the tree refreshes, never
	/// per frame. Rows are reused, so create extra panels once and update them here.
	/// </summary>
	[Parameter]
	public Action<TreeRow, T> OnRow { get; set; }

	/// <summary>
	/// Razor template for a row's content. Re-rendered on every bind, which allocates - prefer
	/// <see cref="OnRow"/> where that matters.
	/// </summary>
	[Parameter]
	public RenderFragment<T> Row { get; set; }

	/// <summary>
	/// The cursor item after any selection change.
	/// </summary>
	[Parameter]
	public Action<T> OnSelect { get; set; }

	/// <summary>
	/// Any change to <see cref="Selection"/>.
	/// </summary>
	[Parameter]
	public Action OnSelectionChanged { get; set; }

	/// <summary>
	/// Double click or enter on an item.
	/// </summary>
	[Parameter]
	public Action<T> OnActivate { get; set; }

	/// <summary>
	/// Right click on an item. Build and show whatever menu you like.
	/// </summary>
	[Parameter]
	public Action<T> OnContextMenu { get; set; }

	/// <summary>
	/// The user finished renaming an item in place. Apply it to your data.
	/// </summary>
	[Parameter]
	public Action<T, string> OnRename { get; set; }

	/// <summary>
	/// Can this item be dragged? Setting this makes rows draggable.
	/// </summary>
	[Parameter]
	public Func<T, bool> CanDrag { get; set; }

	/// <summary>
	/// A dragged item was dropped on a target item, or on empty space when the target is default.
	/// </summary>
	[Parameter]
	public Action<T, T> OnItemDropped { get; set; }

	/// <summary>
	/// How items are compared for open and selected state. Set before adding anything.
	/// </summary>
	public IEqualityComparer<T> Comparer
	{
		get => _comparer;
		set
		{
			value ??= EqualityComparer<T>.Default;
			if ( ReferenceEquals( value, _comparer ) ) return;

			_comparer = value;
			_open = new HashSet<T>( _open, value );
			_selection = new HashSet<T>( _selection, value );
		}
	}

	/// <summary>
	/// The selected items.
	/// </summary>
	public IReadOnlyCollection<T> Selection => _selection;

	/// <summary>
	/// The items that are open.
	/// </summary>
	public IReadOnlyCollection<T> OpenItems => _open;

	/// <summary>
	/// The item under the keyboard cursor, or default.
	/// </summary>
	public T CursorItem => CursorRow >= 0 && CursorRow < _rowItems.Count ? _rowItems[CursorRow] : default;

	/// <summary>
	/// The item at a visible row.
	/// </summary>
	public T GetRowItem( int row ) => _rowItems[row];

	/// <summary>
	/// The visible row of an item, or -1 if it's not reachable through open parents.
	/// </summary>
	public int GetItemRow( T item )
	{
		for ( int i = 0; i < _rowItems.Count; i++ )
		{
			if ( _comparer.Equals( _rowItems[i], item ) ) return i;
		}

		return -1;
	}

	public bool IsOpen( T item ) => _open.Contains( item );
	public bool IsSelected( T item ) => _selection.Contains( item );

	/// <summary>
	/// Open an item. Recursive opens everything underneath it too.
	/// </summary>
	public void Open( T item, bool recursive = false )
	{
		if ( _open.Add( item ) ) NeedsRebuild = true;
		if ( !recursive ) return;

		var children = GetChildren?.Invoke( item );
		if ( children is null ) return;

		if ( children is IList<T> list )
		{
			for ( int i = 0; i < list.Count; i++ ) Open( list[i], true );
		}
		else
		{
			foreach ( var child in children ) Open( child, true );
		}
	}

	/// <summary>
	/// Close an item. Recursive closes everything underneath it too, only looking inside nodes
	/// that were open - a closed node's children can't be showing, so they're never fetched.
	/// </summary>
	public void Close( T item, bool recursive = false )
	{
		var wasOpen = _open.Remove( item );
		if ( wasOpen ) NeedsRebuild = true;
		if ( !recursive || !wasOpen ) return;

		var children = GetChildren?.Invoke( item );
		if ( children is null ) return;

		if ( children is IList<T> list )
		{
			for ( int i = 0; i < list.Count; i++ ) Close( list[i], true );
		}
		else
		{
			foreach ( var child in children ) Close( child, true );
		}
	}

	public void Toggle( T item, bool recursive = false )
	{
		if ( _open.Contains( item ) ) Close( item, recursive );
		else Open( item, recursive );
	}

	/// <summary>
	/// Open every ancestor of an item so it becomes visible. Returns false if it isn't in the tree.
	/// </summary>
	public bool ExpandPathTo( T item )
	{
		_path.Clear();

		if ( GetParent is not null )
		{
			for ( var p = GetParent( item ); p is not null; p = GetParent( p ) )
			{
				_path.Add( p );
			}
		}
		else if ( !FindPath( _roots, item ) )
		{
			return false;
		}

		for ( int i = 0; i < _path.Count; i++ ) Open( _path[i] );
		return true;
	}

	bool FindPath( IEnumerable<T> items, T target )
	{
		if ( items is null ) return false;

		if ( items is IList<T> list )
		{
			for ( int i = 0; i < list.Count; i++ )
			{
				if ( FindPathThrough( list[i], target ) ) return true;
			}

			return false;
		}

		foreach ( var item in items )
		{
			if ( FindPathThrough( item, target ) ) return true;
		}

		return false;
	}

	bool FindPathThrough( T item, T target )
	{
		if ( _comparer.Equals( item, target ) ) return true;
		if ( !ItemHasChildren( item, out var children ) ) return false;

		_path.Add( item );
		if ( FindPath( children ?? GetChildren( item ), target ) ) return true;
		_path.RemoveAt( _path.Count - 1 );

		return false;
	}

	/// <summary>
	/// Select an item, opening the path to it. Add keeps the current selection.
	/// </summary>
	public void Select( T item, bool add = false )
	{
		ExpandPathTo( item );
		if ( NeedsRebuild ) Rebuild();

		var row = GetItemRow( item );
		if ( row < 0 ) return;

		SelectRow( row, add, false );
		ScrollToRow( row );
	}

	/// <summary>
	/// Scroll an item into view, opening the path to it.
	/// </summary>
	public void ScrollTo( T item )
	{
		ExpandPathTo( item );
		if ( NeedsRebuild ) Rebuild();

		var row = GetItemRow( item );
		if ( row >= 0 ) ScrollToRow( row );
	}

	/// <summary>
	/// Scroll an item into view and flash it. Opens the path to it first.
	/// </summary>
	public void Highlight( T item )
	{
		ExpandPathTo( item );
		if ( NeedsRebuild ) Rebuild();

		var row = GetItemRow( item );
		if ( row < 0 ) return;

		ScrollToRow( row );
		HighlightRow( row );
	}

	/// <summary>
	/// The data changed in a way the tree can't see - a rename, a reorder. Rebuilds and rebinds.
	/// </summary>
	public void Refresh()
	{
		NeedsRebuild = true;
	}

	/// <summary>
	/// One item's data changed. Rebuilds the whole row list, which is cheap.
	/// </summary>
	public void Refresh( T item )
	{
		NeedsRebuild = true;
	}

	protected override void BuildRows()
	{
		_rowItems.Clear();
		_rowChildLists.Clear();
		_rowChildCounts.Clear();
		_rootCount = _rootList?.Count ?? 0;

		AddItems( _roots, 0 );
	}

	void AddItems( IEnumerable<T> items, int depth )
	{
		if ( items is null ) return;

		if ( items is IList<T> list )
		{
			for ( int i = 0; i < list.Count; i++ ) AddItem( list[i], depth );
			return;
		}

		foreach ( var item in items ) AddItem( item, depth );
	}

	void AddItem( T item, int depth )
	{
		var hasChildren = ItemHasChildren( item, out var children );
		var open = hasChildren && _open.Contains( item );

		IList<T> childList = null;
		int childCount = 0;

		if ( open )
		{
			children ??= GetChildren( item );
			childList = children as IList<T>;
			childCount = childList?.Count ?? 0;
		}

		var height = GetHeight?.Invoke( item ) ?? RowHeight;

		AddRow( height, depth, hasChildren, open );
		_rowItems.Add( item );
		_rowChildLists.Add( childList );
		_rowChildCounts.Add( childCount );

		if ( open ) AddItems( children, depth + 1 );
	}

	/// <summary>
	/// Whether an item expands. Hands back the children if it had to fetch them to find out.
	/// </summary>
	bool ItemHasChildren( T item, out IEnumerable<T> children )
	{
		children = null;

		if ( CanExpand is not null ) return CanExpand( item );
		if ( GetChildren is null ) return false;

		children = GetChildren( item );
		if ( children is null ) return false;
		if ( children is IList<T> list ) return list.Count > 0;

		using var e = children.GetEnumerator();
		return e.MoveNext();
	}

	protected override void CheckForChanges()
	{
		if ( _rootList is not null && _rootList.Count != _rootCount )
		{
			NeedsRebuild = true;
			return;
		}

		for ( int i = 0; i < _rowChildLists.Count; i++ )
		{
			var list = _rowChildLists[i];
			if ( list is null ) continue;

			if ( list.Count != _rowChildCounts[i] )
			{
				NeedsRebuild = true;
				return;
			}
		}
	}

	protected override void BindRow( int row, TreeRow panel )
	{
		var item = _rowItems[row];

		if ( OnRow is not null )
		{
			OnRow( panel, item );
		}
		else if ( Row is not null )
		{
			panel.Content.ChildContent = Row( item );
			panel.Content.StateHasChanged();
		}
		else
		{
			panel.Text = item?.ToString();
		}

		if ( GetTooltip is not null )
		{
			var tooltip = GetTooltip( item );
			if ( panel.Tooltip != tooltip ) panel.Tooltip = tooltip;
		}
	}

	protected override bool IsRowSelected( int row ) => _selection.Contains( _rowItems[row] );

	protected override void SetRowSelected( int row, bool selected )
	{
		if ( selected ) _selection.Add( _rowItems[row] );
		else _selection.Remove( _rowItems[row] );
	}

	protected override void ClearSelectionInternal() => _selection.Clear();

	protected override void OnSelectionFinished()
	{
		OnSelectionChanged?.Invoke();

		if ( OnSelect is not null && CursorRow >= 0 && CursorRow < _rowItems.Count )
		{
			OnSelect( _rowItems[CursorRow] );
		}
	}

	protected override void SetRowOpen( int row, bool open, bool recursive )
	{
		if ( open ) Open( _rowItems[row], recursive );
		else Close( _rowItems[row], recursive );
	}

	protected override void OnRowActivated( int row ) => OnActivate?.Invoke( _rowItems[row] );

	protected override void OnRowContextMenu( int row ) => OnContextMenu?.Invoke( _rowItems[row] );

	protected override bool CanRenameRow( int row )
	{
		if ( OnRename is null ) return false;
		return CanRename?.Invoke( _rowItems[row] ) ?? true;
	}

	protected override void OnRowRenamed( int row, string text )
	{
		OnRename?.Invoke( _rowItems[row], text );
		NeedsRebuild = true;
	}

	protected internal override bool CanDragRow( int row )
	{
		if ( CanDrag is null ) return false;
		return CanDrag( _rowItems[row] );
	}

	protected override void OnRowDropped( int targetRow, int sourceRow )
	{
		if ( OnItemDropped is null ) return;

		var target = targetRow >= 0 ? _rowItems[targetRow] : default;
		OnItemDropped( _rowItems[sourceRow], target );
		NeedsRebuild = true;
	}
}

/// <summary>
/// A <see cref="TreeView{T}"/> over plain objects, for Razor markup that doesn't want to name a type.
/// </summary>
public sealed class TreeView : TreeView<object>
{
}
