namespace Sandbox.PanelGallery;

/// <summary>
/// Popups in a panel window. A popup panel goes wherever the surface puts it - in a window of its
/// own here, in the root in a game - and a bare popup window holds whatever you like. Both should
/// hang over the window edge and dismiss on a click anywhere else.
/// </summary>
public class PopupsPage : GalleryPage
{
	readonly Sandbox.UI.Label _output;

	public PopupsPage() : base( "Popups", "Sandbox.UI.Popup and PanelWindow.Popup. Menus have their own page - these are the pieces under them." )
	{
		var row = Case( "Popup panel, under the mouse - an OS window here, in the root in a game" );

		Sandbox.UI.Button popupButton = null;
		popupButton = new Sandbox.UI.Button( "Popup Under Mouse", "ads_click", "flatbutton", () => OpenPopupPanel( popupButton ) );
		row.AddChild( popupButton );

		row = Case( "Bare popup window with whatever you put in it - not a menu" );

		Sandbox.UI.Button windowButton = null;
		windowButton = new Sandbox.UI.Button( "Open Window", "open_in_new", "flatbutton", () => OpenPopupWindow( windowButton ) );
		row.AddChild( windowButton );

		row = Case( "Open and close by themselves - window creation and teardown under load", true );
		row.AddChild( new PopupStress() );

		_output = Output();
	}

	/// <summary>
	/// The engine's popup panel, positioned at the cursor. The surface decides where it lives.
	/// </summary>
	void OpenPopupPanel( Panel source )
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

	/// <summary>
	/// A popup window made directly, with plain panels in it - what a colour picker or a preview
	/// would use, rather than a menu.
	/// </summary>
	void OpenPopupWindow( Panel anchor )
	{
		var window = PanelWindow.FromPanel( anchor );
		if ( window is null ) return;

		var popup = PanelWindow.Popup( window, anchor.Box.Rect.BottomLeft + new Vector2( 0, 6 ) );

		var contents = popup.Root.Add.Panel( "dropdown" );
		contents.StyleSheet.Load( "/styles/editor.scss" );
		contents.Add.Label( "A window of panels", "row" );

		foreach ( var item in new[] { ("content_copy", "Copy"), ("content_paste", "Paste"), ("delete", "Delete") } )
		{
			var current = item;
			contents.AddChild( new Sandbox.UI.Button( current.Item2, current.Item1, "row", () =>
			{
				_output.Text = current.Item2;
				popup.Dispose();
			} ) );
		}
	}
}
