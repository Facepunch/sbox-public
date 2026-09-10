namespace Sandbox.UI;

/// <summary>
/// A row of menu headings - File, Edit, View. Click one to open it; once one is open, hovering
/// another switches to it.
/// </summary>
[Library( "menubar" )]
[StyleSheet.Inline( "menubar", Styles )]
public class MenuBar : Panel
{
	const string Styles = """
		.menubar
		{
			flex-direction: row;
			align-items: center;
			flex-shrink: 0;
		}

		.menubar > .menu
		{
			height: auto;
			padding: 4px 8px;
		}

		.menubar > .menu:active { background-color: rgba( 255, 255, 255, 0.16 ); }

		// Heading rows show text and maybe an icon - nothing else. Written to outweigh the menu
		// sheet's own rules for those parts.
		.menubar > .menu > .gutter,
		.menubar > .menu > .shortcut,
		.menubar > .menu.has-submenu > .chevron { display: none; }

		.menubar > .menu.has-icon > .gutter { display: flex; width: auto; margin-right: 4px; }
		.menubar > .menu.has-icon > .gutter > .icon { display: flex; }
		.menubar > .menu.has-icon > .gutter > .check { display: none; }
		""";

	/// <summary>
	/// The heading whose menu is showing, if any.
	/// </summary>
	public Menu OpenMenu { get; private set; }

	public MenuBar()
	{
		AddClass( "menubar" );
	}

	/// <summary>
	/// Add a heading. Its row becomes the heading and its options the dropdown.
	/// </summary>
	public Menu AddMenu( Menu menu )
	{
		menu.Parent = this;
		return menu;
	}

	/// <inheritdoc cref="AddMenu(Menu)"/>
	public Menu AddMenu( string text )
	{
		return AddMenu( new Menu( text ) );
	}

	/// <summary>
	/// The headings, in order.
	/// </summary>
	public IEnumerable<Menu> Menus => Children.OfType<Menu>();

	protected override void OnChildAdded( Panel child )
	{
		base.OnChildAdded( child );

		if ( child is Menu menu )
		{
			menu.Closed += OnMenuClosed;
		}
	}

	protected override void OnChildRemoved( Panel child )
	{
		base.OnChildRemoved( child );

		if ( child is Menu menu )
		{
			menu.Close();
			menu.Closed -= OnMenuClosed;
		}
	}

	/// <summary>
	/// Open this heading's menu, closing whichever was open.
	/// </summary>
	public void Show( Menu menu )
	{
		if ( OpenMenu == menu ) return;

		OpenMenu?.Close();
		menu.Open( menu, Popup.PositionMode.BelowLeft );

		// An empty heading has nothing to open
		OpenMenu = menu.IsOpen ? menu : null;
	}

	/// <summary>
	/// Close whatever is open.
	/// </summary>
	public void CloseAll()
	{
		OpenMenu?.Close();
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		if ( Heading( e.Target ) is not { Enabled: true } menu ) return;

		e.StopPropagation();

		if ( OpenMenu == menu ) menu.Close();
		else Show( menu );
	}

	/// <summary>
	/// Once a menu is open, sliding along the headings switches between them.
	/// </summary>
	protected override void OnMouseOver( MousePanelEvent e )
	{
		base.OnMouseOver( e );

		if ( OpenMenu is null ) return;
		if ( Heading( e.Target ) is not { Enabled: true } menu || menu == OpenMenu ) return;

		Show( menu );
	}

	/// <summary>
	/// Left and right that an open menu didn't use move between the headings.
	/// </summary>
	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( OpenMenu is not null && e.Button is "left" or "right" )
		{
			MoveOpen( e.Button == "right" ? 1 : -1 );
			e.StopPropagation = true;
			return;
		}

		base.OnButtonTyped( e );
	}

	/// <summary>
	/// The heading a panel is part of, if it's under one of ours.
	/// </summary>
	Menu Heading( Panel panel )
	{
		for ( var current = panel; current is not null && current != this; current = current.Parent )
		{
			if ( current is Menu menu && menu.Parent == this ) return menu;
		}

		return null;
	}

	/// <summary>
	/// Open the heading to the left or right of the open one, wrapping at the ends.
	/// </summary>
	void MoveOpen( int direction )
	{
		if ( OpenMenu is null ) return;

		var menus = Menus.ToList();
		if ( menus.Count < 2 ) return;

		var index = menus.IndexOf( OpenMenu );
		Show( menus[(index + direction + menus.Count) % menus.Count] );
	}

	void OnMenuClosed( Menu menu )
	{
		if ( OpenMenu == menu ) OpenMenu = null;
	}
}
