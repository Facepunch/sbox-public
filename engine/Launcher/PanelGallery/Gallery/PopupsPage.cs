namespace Sandbox.PanelGallery;

/// <summary>
/// OS popup windows - they hang over the parent window edge like real menus, and dismiss
/// when you click anywhere else.
/// </summary>
public class PopupsPage : GalleryPage
{
	readonly Sandbox.UI.Label _output;

	public PopupsPage() : base( "Popups", "PanelWindow.Popup - real OS windows. A menu should dismiss on a click anywhere outside it." )
	{
		var row = Case( "OS window menu" );

		Sandbox.UI.Button menuButton = null;
		menuButton = new Sandbox.UI.Button( "Open Menu", "menu", "flatbutton", () => OpenMenu( menuButton ) );
		row.AddChild( menuButton );

		row = Case( "In-surface popup, under the mouse" );

		Sandbox.UI.Button popupButton = null;
		popupButton = new Sandbox.UI.Button( "Popup Under Mouse", "ads_click", "flatbutton", () => OpenSurfacePopup( popupButton ) );
		row.AddChild( popupButton );

		_output = Output();
	}

	/// <summary>
	/// A popup panel inside this window's surface, positioned at the cursor - it should open
	/// exactly where you clicked.
	/// </summary>
	void OpenSurfacePopup( Panel source )
	{
		var popup = new Sandbox.UI.Popup( source, Sandbox.UI.Popup.PositionMode.UnderMouse, 0 );
		popup.AddClass( "dropdown" );
		popup.StyleSheet.Load( "/styles/editor.scss" );

		foreach ( var title in new[] { "First", "Second", "Third" } )
		{
			var current = title;
			popup.AddChild( new Sandbox.UI.Button( current, null, "row", () =>
			{
				_output.Text = current;
				popup.Delete();
			} ) );
		}
	}

	void OpenMenu( Panel anchor )
	{
		var window = PanelWindow.FromPanel( anchor );
		if ( window is null ) return;

		// Sized to fit the rows from the start - SizeToContents trims it, but a window born
		// far too small crushes the first layout instead
		var popup = PanelWindow.Popup( window, anchor.Box.Rect.BottomLeft + new Vector2( 0, 6 ), new Vector2( 220, 12 + 3 * 27 ) );

		var menu = popup.Root.Add.Panel( "dropdown" );
		menu.StyleSheet.Load( "/styles/editor.scss" );

		(string Icon, string Title)[] items = [("content_copy", "Copy"), ("content_paste", "Paste"), ("delete", "Delete")];

		foreach ( var item in items )
		{
			var current = item;
			menu.AddChild( new Sandbox.UI.Button( current.Title, current.Icon, "row", () =>
			{
				_output.Text = current.Title;
				popup.Dispose();
			} ) );
		}
	}
}
