namespace Editor;

/// <summary>
/// Lists Workshop maps, with a search box and sort order.
/// </summary>
public class WorkshopMapBrowser : Widget
{
	/// <summary>
	/// A map was clicked. Null when the selection is cleared.
	/// </summary>
	public Action<Storage.QueryItem> OnMapHighlight;

	/// <summary>
	/// A map was double clicked.
	/// </summary>
	public Action<Storage.QueryItem> OnMapSelected;

	/// <summary>
	/// The highlighted map. Null if there isn't one.
	/// </summary>
	public Storage.QueryItem SelectedMap => _list.SelectedItems.OfType<Storage.QueryItem>().FirstOrDefault();

	record SortMode( string Title, string Icon, Storage.SortOrder Order );

	static readonly SortMode[] SortModes =
	[
		new( "Popular", "local_fire_department", Storage.SortOrder.RankedByVote ),
		new( "Trending", "trending_up", Storage.SortOrder.RankedByTrend ),
		new( "Most Subscribed", "favorite", Storage.SortOrder.RankedByTotalUniqueSubscriptions ),
		new( "Newest", "new_releases", Storage.SortOrder.RankedByPublicationDate ),
		new( "Recently Updated", "update", Storage.SortOrder.RankedByLastUpdatedDate ),
	];

	readonly MapList _list;
	readonly SearchWidget _search;
	readonly ToolButton _sortButton;

	SortMode _sort = SortModes[0];
	Storage.QueryResult _page;
	int _searchId;
	bool _loading;

	public WorkshopMapBrowser( Widget parent ) : base( parent )
	{
		MinimumSize = new( 100, 100 );
		Layout = Layout.Column();

		var toolbar = Layout.AddRow();
		toolbar.Spacing = 6;
		toolbar.Margin = new Margin( 6, 4, 8, 4 );

		_search = toolbar.Add( new SearchWidget( this, false ), 1 );
		_search.FixedHeight = Theme.ControlHeight;
		_search.PlaceholderText = "⌕  Search Workshop Maps";
		_search.ValueChanged = () => Find( 300 );

		_sortButton = toolbar.Add( new ToolButton( "Sort Order", _sort.Icon, this ) );
		_sortButton.FixedSize = Theme.RowHeight;
		_sortButton.MouseLeftPress = OpenSortMenu;

		_list = Layout.Add( new MapList( this ), 1 );
		_list.ItemSize = new Vector2( 152, 190 );
		_list.ItemPaint = PaintMap;
		_list.MultiSelect = false;
		_list.Margin = new Margin( 4 );
		_list.OnScrolledToEnd = LoadMore;
		_list.OnSelectionChanged = ( items ) => OnMapHighlight?.Invoke( items.OfType<Storage.QueryItem>().FirstOrDefault() );
		_list.ItemActivated = ( item ) =>
		{
			if ( item is Storage.QueryItem map )
				OnMapSelected?.Invoke( map );
		};
		_list.OnPaintOverride = PaintStatus;

		Find();
	}

	void OpenSortMenu()
	{
		var menu = new ContextMenu( this );

		foreach ( var mode in SortModes )
		{
			var option = menu.AddOption( mode.Title, mode.Icon, () =>
			{
				_sort = mode;
				_sortButton.Icon = mode.Icon;
				Find();
			} );

			option.Checkable = true;
			option.Checked = mode == _sort;
		}

		menu.OpenAt( _sortButton.ScreenRect.BottomLeft, false );
	}

	/// <summary>
	/// Search from the first page. The delay gives typing a moment to settle.
	/// </summary>
	async void Find( int delay = 0 )
	{
		var searchId = ++_searchId;

		_page = null;
		_loading = true;
		_list.Clear();
		_list.Update();

		if ( delay > 0 ) await Task.Delay( delay );
		if ( !IsValid || searchId != _searchId ) return;

		var query = new Storage.Query
		{
			SearchText = string.IsNullOrWhiteSpace( _search.Value ) ? null : _search.Value.Trim(),
			SortOrder = _sort.Order
		};
		query.TagsRequired.Add( "map" );

		AddPage( searchId, await query.Run() );
	}

	async void LoadMore()
	{
		if ( _loading || _page is null || !_page.HasMoreResults() ) return;

		var searchId = _searchId;
		_loading = true;

		AddPage( searchId, await _page.GetNextResults() );
	}

	void AddPage( int searchId, Storage.QueryResult page )
	{
		if ( !IsValid || searchId != _searchId ) return;

		_page = page;
		_loading = false;
		_list.AddItems( page?.Items ?? [] );
		_list.Update();
	}

	bool PaintStatus()
	{
		if ( _list.Items.Any() ) return false;

		Paint.ClearBrush();
		Paint.SetPen( Theme.TextLight );
		Paint.DrawText( _list.LocalRect, _loading ? "Searching..." : "No maps found", TextFlag.Center );

		return false;
	}

	void PaintMap( VirtualWidget item )
	{
		if ( item.Object is not Storage.QueryItem map ) return;

		var rect = item.Rect.Shrink( 2 );

		if ( Paint.HasSelected || Paint.HasPressed )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Blue.Darken( 0.4f ) );
			Paint.DrawRect( rect, Theme.ControlRadius );
		}
		else if ( Paint.HasMouseOver )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.SurfaceLightBackground.WithAlpha( 0.4f ) );
			Paint.DrawRect( rect, 4 );
		}

		var iconRect = rect.Shrink( 4 );
		iconRect.Height = iconRect.Width;

		if ( string.IsNullOrEmpty( map.Preview ) )
		{
			Paint.SetPen( Theme.Text.WithAlpha( 0.2f ) );
			Paint.DrawIcon( iconRect, "map", 48 );
		}
		else
		{
			Paint.BilinearFiltering = true;
			Paint.Draw( iconRect, map.Preview, borderRadius: 2 );
			Paint.BilinearFiltering = false;
		}

		var textRect = rect.Shrink( 4, 0 );
		textRect.Top = iconRect.Bottom + 4;

		Paint.SetDefaultFont( 7 );
		Paint.SetPen( Theme.Text.WithAlpha( 0.7f ) );
		Paint.DrawText( textRect, Paint.GetElidedText( map.Title ?? "", textRect.Width ), TextFlag.LeftTop );

		textRect.Top += 12;

		Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
		Paint.DrawText( textRect, Paint.GetElidedText( map.Owner?.Name ?? "", textRect.Width ), TextFlag.LeftTop );
	}

	/// <summary>
	/// A list that says when it's scrolled to the bottom.
	/// </summary>
	class MapList : ListView
	{
		public Action OnScrolledToEnd;

		public MapList( Widget parent ) : base( parent )
		{
		}

		protected override void OnScrollChanged()
		{
			base.OnScrollChanged();

			if ( VerticalScrollbar.Value == VerticalScrollbar.Maximum )
				OnScrolledToEnd?.Invoke();
		}
	}
}
