using Sandbox.UI;

namespace Editor;

//
// Popups. A window's panels open menus and dropdowns the way panels in a game do, but here each
// popup opens in an OS window of its own, so it can hang outside the window like the OS's menus.
// The engine's Popup decides when and what; this is where.
//
public partial class PanelWindow : IPopupHost
{
	void IPopupHost.ShowPopup( Sandbox.UI.Popup popup, Panel source, Sandbox.UI.Popup.PositionMode position, float offset )
	{
		// Lives until it's dismissed - see PopupWindow.OnClosing
#pragma warning disable CA2000
		var window = (PopupWindow)Popup( this, PopupPosition( source.Box.Rect, MousePosition, position, offset ) );
#pragma warning restore CA2000
		window.Root.AddClass( "os-menu" );

		// Styled by the sheets that style the popup's owner, wherever up the tree they were loaded
		var sheets = window.Root.StyleSheet;

		foreach ( var sheet in OwnerStyleSheets( source ) )
		{
			sheets.Add( sheet );
		}

		popup.Parent = window.Root;

		// The popup came styled to float over a root. Here the window floats, so it's laid out as
		// an ordinary child and the window shrinks to it.
		popup.Style.Position = PositionMode.Relative;
		popup.Style.Left = null;
		popup.Style.Top = null;
		popup.Style.Right = null;
		popup.Style.Bottom = null;

		// No taller than the window it came from - a long list scrolls instead
		popup.Style.MaxHeight = PixelSize.y;

		// The compositor rounds the window's corners. Rounding the popup's own as well shows the
		// window's background in between - whatever radius its stylesheet asked for.
		popup.Style.Set( "border-radius", "0" );

		window.HostedPopup = popup;
	}

	void IPopupHost.HidePopup( Sandbox.UI.Popup popup )
	{
		FromPanel( popup )?.Dispose();
	}

	/// <summary>
	/// Where a popup window goes for a source rect, in the parent's pixels. A window can't be
	/// placed by its own size before it exists, so the modes that would need it fall back to the
	/// nearest that don't.
	/// </summary>
	internal static Vector2 PopupPosition( Rect source, Vector2 mouse, Sandbox.UI.Popup.PositionMode position, float offset )
	{
		return position switch
		{
			Sandbox.UI.Popup.PositionMode.UnderMouse => mouse,
			Sandbox.UI.Popup.PositionMode.Right or Sandbox.UI.Popup.PositionMode.RightTop or Sandbox.UI.Popup.PositionMode.RightBottom => new Vector2( source.Right + offset, source.Top ),
			Sandbox.UI.Popup.PositionMode.Left or Sandbox.UI.Popup.PositionMode.LeftBottom => new Vector2( source.Left - offset, source.Top ),
			_ => new Vector2( source.Left, source.Bottom + offset ),
		};
	}
}
