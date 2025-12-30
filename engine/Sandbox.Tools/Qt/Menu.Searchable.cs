using System;

namespace Editor
{
	/// <summary>
	/// Identical to Menu except DeleteOnClose defaults to true.
	/// Can optionally be made searchable by setting <see cref="Searchable"/> to true before opening.
	/// </summary>
	/// <example>
	/// <code>
	/// var menu = new ContextMenu { Searchable = true };
	/// menu.AddOption( "Option 1", action: () => {} );
	/// menu.OpenAtCursor();
	/// </code>
	/// </example>
	public class ContextMenu : Menu
	{
		private record struct CachedItem( Option Option, string FullPath, int Index );

		private List<CachedItem> _cachedOptions = [];
		private List<(Menu Menu, int Index)> _cachedMenus = [];
		private int _originalOptionCount;
		private bool _isCached;

		private LineEdit _searchBox;

		/// <summary>
		/// Adds a search bar in the context menu. Useful for big menus
		/// </summary>
		public bool Searchable { get; set; }


		public ContextMenu( Widget parent = null ) : base( parent )
		{
			DeleteOnClose = true;
		}

		protected override void OnAboutToShow()
		{
			base.OnAboutToShow();

			if ( Searchable && _searchBox == null )
			{
				_searchBox = new LineEdit( this )
				{
					PlaceholderText = "⌕  Search...",
					MinimumWidth = 200
				};

				_searchBox.SetStyles( "font-size: 8pt; padding: 4px 16px; margin: 4px 2px 2px 4px;" );
				_searchBox.TextEdited += SearchMenu;

				InsertWidgetAt( _searchBox, 0 );

				_searchBox.Focus();
			}
		}

		private void CacheMenu()
		{
			if ( _isCached ) return;

			_originalOptionCount = Options.Count;
			_cachedOptions = [.. GetAllOptionsRecursive().Select( ( x, i ) => new CachedItem( x.Option, x.FullPath, i ) )];
			_cachedMenus = [.. Menus.Select( ( x, i ) => (x, i) )];
			_isCached = true;
		}

		private void SearchMenu( string searchText )
		{
			CacheMenu();
			ClearItems();

			if ( string.IsNullOrWhiteSpace( searchText ) )
			{
				// Restore original menu structure
				var directOptions = _cachedOptions.Take( _originalOptionCount );
				AddOptions( directOptions );
				AddMenus( _cachedMenus.OrderBy( x => x.Index ) );
				return;
			}

			var matches = _cachedOptions
				.Where( x => x.FullPath.Contains( searchText, StringComparison.OrdinalIgnoreCase ) );

			var matchingMenus = _cachedMenus
				.Where( x => x.Menu.Title?.Contains( searchText, StringComparison.OrdinalIgnoreCase ) ?? false );

			AddOptions( matches );
			AddMenus( matchingMenus );
		}

		// Add options and menus from cached menu
		private void AddOptions( IEnumerable<CachedItem> items )
		{
			foreach ( var item in items )
			{
				_menu.addAction( item.Option._action );
				Options.Add( item.Option );
			}
		}

		private void AddMenus( IEnumerable<(Menu Menu, int Index)> menus )
		{
			foreach ( var (menu, _) in menus )
			{
				var action = menu.GetParentAction();
				if ( action.IsValid )
				{
					_menu.addAction( action );
					Menus.Add( menu );
				}
			}
		}

		private void ClearItems()
		{
			var options = Options.ToArray();
			foreach ( var option in options )
			{
				RemoveOption( option );
			}

			var menus = Menus.ToArray();
			foreach ( var menu in menus )
			{
				menu.RemoveFromParent();
			}
		}
	}
}

