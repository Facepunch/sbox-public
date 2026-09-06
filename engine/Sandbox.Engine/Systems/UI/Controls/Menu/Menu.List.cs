namespace Sandbox.UI;

//
// Opening and closing. The rows live nowhere while the menu is closed; opening creates a popup
// list and moves the rows in, closing moves them back out and drops the list. Where the list
// appears is the popup's business - floating in the root in a game, in an OS window in the editor.
//
public partial class Menu
{
	/// <summary>
	/// How far a submenu overlaps the row that opened it, in pixels.
	/// </summary>
	const float SubmenuOverlap = -4;

	MenuList _list;

	// When a click elsewhere took the list down, and whether Close() was what took it down
	float _dismissedAt = float.NegativeInfinity;
	bool _closing;

	/// <summary>
	/// For this long after a click elsewhere closes a menu, opening it is ignored - so the click
	/// that closed it can't be the click that opens it again. Clicking an open heading closes it.
	/// </summary>
	internal static float ReopenGuard = 0.3f;

	/// <summary>
	/// Whether this menu's options are showing.
	/// </summary>
	public bool IsOpen => _list.IsValid() && !_list.IsDeleting;

	/// <summary>
	/// The floating list showing this menu's options, while open.
	/// </summary>
	internal Popup ListPanel => IsOpen ? _list : null;

	/// <summary>
	/// Show this menu's options next to a panel. Use this for context menus and buttons.
	/// </summary>
	public void Open( Panel source, Popup.PositionMode position, float offset = 0 )
	{
		if ( IsOpen ) return;

		if ( RealTime.Now - _dismissedAt < ReopenGuard )
		{
			_dismissedAt = float.NegativeInfinity;
			return;
		}

		AboutToShow?.Invoke( this );

		// Nothing to show - an empty menu doesn't open
		if ( _rows.Count == 0 ) return;

		_list = new MenuList( this );
		_list.SetPositioning( source, position, offset );

		// Menus don't scale in like the game's popups do - they carry their own look
		_list.RemoveClass( "popup-panel" );

		foreach ( var row in _rows )
		{
			row.Parent = _list;
		}

		SetHighlighted( null );
		SetClass( "open", true );

		_list.Focus();
	}

	/// <summary>
	/// Show this menu's options beside its own row, the way a submenu opens.
	/// </summary>
	public void Open()
	{
		Open( this, Popup.PositionMode.RightTop, SubmenuOverlap );
	}

	/// <summary>
	/// Hide this menu's options, and any open submenus.
	/// </summary>
	public void Close()
	{
		_closing = true;
		_list?.Delete( true );
		_closing = false;
	}

	/// <summary>
	/// The list is going away - by us, by something closing every popup, or by its window
	/// closing. Rows come out before the list is deleted so they survive it.
	/// </summary>
	void OnListClosing( MenuList list )
	{
		if ( list != _list ) return;

		_list = null;
		if ( !_closing ) _dismissedAt = RealTime.Now;

		foreach ( var row in _rows )
		{
			(row as Menu)?.Close();
			row.Parent = null;

			// Nothing tells a detached row the mouse left it
			row.Switch( PseudoClass.Hover, false );
		}

		SetHighlighted( null );
		_hoverRow = null;
		SetClass( "open", false );

		Closed?.Invoke( this );
	}

	/// <summary>
	/// The floating list of rows. A popup, so a click anywhere else closes it.
	/// </summary>
	[StyleSheet.Inline( "menu", Styles )]
	sealed class MenuList : Popup
	{
		readonly Menu _owner;
		bool _closing;

		public MenuList( Menu owner )
		{
			_owner = owner;
			AcceptsFocus = true;
			AddClass( "menulist" );
		}

		protected override BasePopup ParentPopup => _owner.ParentMenu?._list;

		/// <summary>
		/// Styled as if it were inside the row that opened it, wherever it's actually showing. A
		/// root menu's row isn't in any tree, so its list styles under the panel it opened from.
		/// </summary>
		internal override Panel StyleParent => _owner.Parent is not null ? _owner : PopupSource;

		public override void Delete( bool immediate = false )
		{
			if ( _closing ) return;
			_closing = true;

			_owner.OnListClosing( this );

			// The window it was in may already be gone, so no outro
			base.Delete( true );

			// After the delete - deleting a focused panel clears the focus
			_owner.ParentMenu?.ListPanel?.Focus();
		}

		public override void Tick()
		{
			base.Tick();
			_owner.TickHover();
		}

		public override void OnButtonTyped( ButtonEvent e )
		{
			if ( _owner.OnKey( e ) )
			{
				e.StopPropagation = true;
				return;
			}

			base.OnButtonTyped( e );
		}

		protected override void OnEscape( PanelEvent e )
		{
			_owner.Close();
		}

		/// <summary>
		/// A submenu that would run off the right of the screen flips to the left of its row.
		/// </summary>
		public override void OnLayout( ref Rect layoutRect )
		{
			if ( Host is not null ) return;

			if ( Position == PositionMode.RightTop && layoutRect.Right > ScreenSurfaceSize.x )
			{
				var source = PopupSource.Box.Rect;
				var width = layoutRect.Width;

				layoutRect.Left = source.Left - width - PopupSourceOffset * PopupSource.ScaleToScreen;
				layoutRect.Right = layoutRect.Left + width;
			}

			base.OnLayout( ref layoutRect );
		}
	}
}
