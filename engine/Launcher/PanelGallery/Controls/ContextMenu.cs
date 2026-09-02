namespace Sandbox.PanelGallery;

/// <summary>
/// A menu in its own OS window, so it can hang outside the window that opened it - the same thing
/// the menu bar uses, reachable from anywhere a right click happens.
/// </summary>
public static class ContextMenu
{
	/// <summary>
	/// One row. A null entry draws a separator.
	/// </summary>
	public record Item( string Title, Action Action, string Icon = null, string Shortcut = null );

	static PanelWindow open;

	/// <summary>
	/// Whatever is open, closed.
	/// </summary>
	public static void Close()
	{
		if ( open is null ) return;

		var window = open;
		open = null;
		window.Dispose();
	}

	/// <summary>
	/// Open a menu under the cursor, in its own window, so it can hang outside the window that
	/// opened it.
	/// </summary>
	public static void Open( Panel panel, IEnumerable<Item> items, bool lightMode )
	{
		Close();

		if ( WindowOf( panel ) is not { } parent ) return;

		// Straight from the surface - a mouse event's position is relative to whatever it hit
		var position = parent.MousePosition;

		var list = items.ToArray();

		var popup = PanelWindow.Popup( parent, position );

		var menu = popup.Root.Add.Panel( "dropdown" );
		menu.StyleSheet.Load( "/styles/gallery.scss" );
		menu.SetClass( EditorWindow.LightModeClass, lightMode );

		foreach ( var item in list )
		{
			if ( item is null )
			{
				menu.Add.Panel( "separator" );
				continue;
			}

			var row = menu.Clickable( "row", () =>
			{
				Close();
				item.Action?.Invoke();
			} );

			if ( item.Icon is not null ) row.Icon( item.Icon );

			row.Add.Label( item.Title );

			if ( item.Shortcut is not null ) row.Add.Label( item.Shortcut, "shortcut" );
		}

		open = popup;
	}

	/// <summary>
	/// The window a panel is in, found by matching its root against the open windows.
	/// </summary>
	public static PanelWindow WindowOf( Panel panel )
	{
		var root = panel?.FindRootPanel();
		if ( root is null ) return null;

		return PanelWindow.All.FirstOrDefault( x => x.Root == root );
	}
}
