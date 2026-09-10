namespace Sandbox.PanelGallery;

/// <summary>
/// What a PanelWindow can do as an OS window: stacking order, visibility, opacity, its taskbar
/// button, and whether it takes focus. Opens a second window to do it all to.
/// </summary>
public class WindowsPage : GalleryPage
{
	readonly Sandbox.UI.Label output;
	PanelWindow window;

	public WindowsPage() : base( "Windows", "A PanelWindow as the OS sees it. Open the test window, then push it around from here - the buttons report what they did." )
	{
		var open = Case( "Test window" );
		Button( open, "Open", OpenWindow );
		Button( open, "Close", () => { window?.Dispose(); window = null; Say( "closed" ); } );

		var order = Case( "Stacking" );
		Button( order, "Toggle keep on top", () => With( w => { w.KeepOnTop = !w.KeepOnTop; Say( $"keep on top {w.KeepOnTop}" ); } ) );
		Button( order, "Send to back", () => With( w => { w.SendToBack(); Say( "sent to back" ); } ) );
		Button( order, "Bring to front", () => With( w => { w.BringToFront(); Say( $"brought to front, focused {w.IsFocused}" ); } ) );
		Button( order, "Focus", () => With( w => { w.Focus(); Say( $"focused {w.IsFocused}" ); } ) );

		var visibility = Case( "Visibility" );
		Button( visibility, "Hide", () => With( w => { w.Hide(); Say( $"hidden, visible {w.IsVisible}" ); } ) );
		Button( visibility, "Show", () => With( w => { w.Show(); Say( $"shown, visible {w.IsVisible}" ); } ) );
		Button( visibility, "Maximize", () => With( w => { w.Maximize(); Say( "maximize asked" ); } ) );
		Button( visibility, "Minimize", () => With( w => { w.Minimize(); Say( $"minimized {w.IsMinimized}" ); } ) );
		Button( visibility, "Opacity 100%", () => With( w => { w.Opacity = 1; Say( "opacity 1" ); } ) );
		Button( visibility, "Opacity 50%", () => With( w => { w.Opacity = 0.5f; Say( "opacity 0.5" ); } ) );
		Button( visibility, "Opacity 20%", () => With( w => { w.Opacity = 0.2f; Say( "opacity 0.2" ); } ) );

		var geometry = Case( "Geometry" );
		Button( geometry, "Move to center", () => With( w => { w.MoveToCenter(); Say( $"centred at {w.Position}" ); } ) );
		Button( geometry, "Push half off screen", () => With( w => { var b = w.DisplayBounds; w.Position = new Vector2( b.Right - 200, b.Bottom - 120 ); Say( $"at {w.Position}, display {b}" ); } ) );
		Button( geometry, "Snap to display", () => With( w => { w.SnapToDisplay(); Say( $"snapped to {w.Position}, work area {w.DisplayWorkArea}" ); } ) );
		Button( geometry, "Toggle 16:9 lock", () => With( w => { w.AspectRatioLock = w.AspectRatioLock is null ? 16f / 9 : null; Say( $"aspect lock {w.AspectRatioLock?.ToString() ?? "off"}" ); } ) );
		Button( geometry, "Toggle resizable", () => With( w => { w.Resizable = !w.Resizable; Say( $"resizable {w.Resizable}" ); } ) );
		Button( geometry, "Toggle borderless fullscreen", () => With( w => { w.ExclusiveFullscreen = false; w.Fullscreen = !w.Fullscreen; Say( $"fullscreen {w.IsFullscreen}, borderless" ); } ) );
		Button( geometry, "Toggle exclusive fullscreen", () => With( w => { w.ExclusiveFullscreen = true; w.Fullscreen = !w.Fullscreen; Say( $"fullscreen {w.IsFullscreen}, exclusive" ); } ) );

		var attention = Case( "Attention" );
		Button( attention, "Flash briefly", () => With( w => { w.FlashTaskbar(); Say( "flashed" ); } ) );
		Button( attention, "Flash until focused", () => With( w => { w.FlashTaskbar( untilFocused: true ); Say( "flashing until it's focused" ); } ) );
		Button( attention, "Stop flashing", () => With( w => { w.StopFlashing(); Say( "stopped" ); } ) );

		var ownership = Case( "Ownership" );
		Button( ownership, "Toggle owned by this window", () => With( w =>
		{
			w.Owner = w.Owner is null ? PanelWindow.FromPanel( this ) : null;
			Say( w.Owner is null ? "top level again" : "owned by the gallery - it stays above it and minimizes with it" );
		} ) );

		Button( ownership, "Toggle modal", () => With( w =>
		{
			w.Modal = !w.Modal;
			Say( w.Modal ? (w.Owner is null ? "modal, but with no owner there's nothing to block" : "modal - this window is blocked until it closes") : "not modal" );
		} ) );

		Button( ownership, "Toggle can close", () => With( w => { w.CanClose = !w.CanClose; Say( $"can close {w.CanClose} - try the X and Alt+F4" ); } ) );
		Button( ownership, "Toggle hide on close", () => With( w => { w.HideOnClose = !w.HideOnClose; Say( $"hide on close {w.HideOnClose}" ); } ) );

		var shell = Case( "Shell" );
		Button( shell, "Toggle show in taskbar", () => With( w => { w.ShowInTaskbar = !w.ShowInTaskbar; Say( $"show in taskbar {w.ShowInTaskbar}" ); } ) );
		Button( shell, "Set overlay icon", () => With( w => { w.SetOverlayIcon( MakeIcon(), "3 things need attention" ); Say( "overlay set - look at the taskbar button" ); } ) );
		Button( shell, "Clear overlay icon", () => With( w => { w.SetOverlayIcon( null ); Say( "overlay cleared" ); } ) );
		Button( shell, "Set icon", () => With( w => { w.SetIcon( MakeIcon() ); Say( "icon set - look at the title bar and the taskbar" ); } ) );

		output = Output();
	}

	void OpenWindow()
	{
		if ( window is { IsOpen: true } )
		{
			window.Focus();
			Say( "already open" );
			return;
		}

		var parent = PanelWindow.FromPanel( this );
		var position = parent is null ? new Vector2( -1, -1 ) : parent.Position + new Vector2( 80, 80 );

		window = new PanelWindow( "Test window", new Vector2( 420, 260 ), position );
		window.OnCloseRequested = () => { Say( window.HideOnClose ? "close asked - hiding, Show brings it back" : "close asked - disposing" ); if ( !window.HideOnClose ) window = null; return true; };
		window.OnMoved = () => Say( $"moved to {window?.Position}" );
		window.OnResized = () => Say( $"resized to {window?.Size}" );
		window.OnMinimized = () => Say( "minimized" );
		window.OnMaximized = () => Say( "maximized" );
		window.OnRestored = () => Say( "restored" );
		window.OnActivated = () => Say( "activated" );
		window.OnDeactivated = () => Say( "deactivated" );
		window.OnDisplayChanged = () => Say( $"now on display {window?.DisplayBounds}" );

		var body = window.Root.Add.Panel( "test-window-body" );
		body.StyleSheet.Load( "/styles/editor.scss" );
		body.StyleSheet.Load( "/styles/controlgallery.scss" );
		body.Add.Label( "Test window", "page-title" );
		body.Add.Label( "Push me around from the gallery. Type here to see whether focus moved:", "page-blurb" );
		body.AddChild( new Sandbox.UI.TextEntry() );

		Say( "opened" );
	}

	/// <summary>
	/// Run something against the test window, or say there isn't one.
	/// </summary>
	void With( Action<PanelWindow> action )
	{
		if ( window is not { IsOpen: true } )
		{
			Say( "open the test window first" );
			return;
		}

		action( window );
	}

	/// <summary>
	/// A blue disc with a white dot, drawn a pixel at a time so it needs nothing from disk.
	/// </summary>
	static Bitmap MakeIcon()
	{
		const int size = 64;
		var bitmap = new Bitmap( size, size );
		bitmap.Clear( Color.Transparent );

		var centre = new Vector2( size / 2f, size / 2f );
		for ( int y = 0; y < size; y++ )
		{
			for ( int x = 0; x < size; x++ )
			{
				var d = (new Vector2( x + 0.5f, y + 0.5f ) - centre).Length;
				if ( d < size * 0.48f ) bitmap.SetPixel( x, y, d < size * 0.18f ? Color.White : Color.Parse( "#3273EB" ).Value );
			}
		}

		return bitmap;
	}

	void Button( Panel parent, string title, Action action )
	{
		parent.AddChild( new Sandbox.UI.Button( title, null, "flatbutton", action ) );
	}

	void Say( string text ) => output.Text = text;
}
