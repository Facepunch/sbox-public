using NativeEngine;
using Sandbox.UI;
using System;

namespace Editor;

/// <summary>
/// A borderless window that sits above the window that opened it and can hang outside it, the way
/// an OS menu does. Born hidden at its parent's size, it shrinks to whatever is put in it and only
/// then appears, so it ends up the size of its contents. Dismissed by a click anywhere else.
/// </summary>
internal sealed class PopupWindow : PanelWindow
{
	/// <summary>
	/// The window this one hangs off.
	/// </summary>
	internal PanelWindow Parent { get; }

	/// <summary>
	/// A popup that never takes the keyboard or the mouse - a tooltip, say. It can't be focused,
	/// and the mouse goes to whatever is under it.
	/// </summary>
	public override bool IgnoresInput { get; }

	/// <summary>
	/// The popup this window was opened to show, if a host put one here. It's deleted first when
	/// the window closes, so it can take its contents with it.
	/// </summary>
	internal Popup HostedPopup { get; set; }

	internal override bool TakesKeyboardFocus => !IgnoresInput && (HostedPopup?.TakesKeyboardFocus ?? true);

	public override bool IsPopup => true;
	internal override IPanelWindow ParentWindow => Parent;

	// Where the window goes, in SDL's window coordinates relative to the parent
	readonly Vector2 _position;

	// The size to be born at, in window coordinates
	readonly Vector2 _initialSize;

	internal PopupWindow( PanelWindow parent, Vector2 localPosition, bool ignoresInput )
	{
		ThreadSafe.AssertIsMainThread();

		Parent = parent;
		IgnoresInput = ignoresInput;
		SizeToContents = true;

		// Dressed like the OS's own menus
		RoundedCorners = true;
		DropShadow = true;

		_position = localPosition;

		// Start as big as the parent and let FitToContents take it down. Starting small would make
		// the contents lay out against a width they're about to lose, and it's that first layout
		// FitToContents measures.
		// A submenu can be wider than its parent menu. Measure against the original window
		// so a narrow suggestion list doesn't force its documentation to wrap prematurely.
		var sizingWindow = parent;
		while ( sizingWindow is PopupWindow popupParent ) sizingWindow = popupParent.Parent;
		_initialSize = parent.PixelsToWindow( sizingWindow.PixelSize );

		var surface = new UISurface { DpiScale = parent.Surface.DpiScale, Size = sizingWindow.PixelSize };

		// The OS draws this window's edge - styles keyed on this drop the border and shadow they
		// would draw floating in a root
		surface.Root.AddClass( "os-popup" );

		// The root starts as big as the parent, and a stretched child would report the whole of
		// that back as its size and never shrink. Set here rather than in a stylesheet because
		// the root is above whatever sheet the contents bring with them.
		surface.Root.Style.AlignItems = Align.FlexStart;

		Attach( surface );
	}

	/// <summary>
	/// The OS window is made at the first frame boundary, not on construction - building a swap
	/// chain while another window is mid-render leaves it in a state that never presents.
	/// </summary>
	private protected override bool CreateNativeWindow()
	{
		var x = (int)_position.x;
		var y = (int)_position.y;
		var width = (int)MathF.Ceiling( _initialSize.x );
		var height = (int)MathF.Ceiling( _initialSize.y );

		//
		// A real SDL popup window - positioned relative to its parent, kept above it, hidden and
		// destroyed along with it. Keyboard focus stays with the parent - SDL never moves it to a
		// popup menu - so keys reach us through PanelWindows.KeyboardTarget, and a click anywhere
		// else is what dismisses us.
		//
		// Hidden until the first frame has been drawn - a popup born visible flashes a blank
		// window at its starting size before the UI sizes and fills it.
		//
		var flags = Sdl.WindowFlags.PopupMenu | Sdl.WindowFlags.Vulkan | Sdl.WindowFlags.HighPixelDensity | Sdl.WindowFlags.Hidden;

		// A window that ignores input never takes keyboard focus either - so a tooltip appearing
		// doesn't pull the caret out of a text entry. Not SDL_WINDOW_TOOLTIP: a swap chain on one
		// of those never presents. A menu popup flagged not focusable is what SDL documents for
		// this anyway.
		if ( !TakesKeyboardFocus )
			flags |= Sdl.WindowFlags.NotFocusable;

		var window = Sdl.CreatePopupWindow( Parent.Handle, x, y, width, height, flags );

		Window = new Sandbox.Engine.SdlWindow( window );

		// The mouse falls straight through to the window underneath, which keeps its hover
		if ( IgnoresInput )
			Sdl.SetWindowMouseTransparent( window, true );

		PanelWindowInput.SetupWindow( window );

		// What was asked of the window before it existed
		if ( RoundedCorners ) Sdl.SetWindowRoundedCorners( window, true );
		if ( DropShadow ) Sdl.SetWindowHasShadow( window, true );

		// A popup can open on a display that scales differently to the window that spawned it
		Surface.DpiScale = DisplayScale;

		// Opaque - the compositor rounds the window's corners itself. Windows doesn't composite
		// swapchain alpha, so drawing our own round corners isn't an option - the corner pixels
		// come out as uninitialized garbage.
		CreateRenderer( "PanelWindow Popup", vsync: false );

		return true;
	}

	/// <summary>
	/// Popups take the scale of the display they open on and keep it - they're too short-lived to
	/// be dragged between displays.
	/// </summary>
	private protected override bool FollowsDisplayScale => false;

	/// <summary>
	/// It was born as big as its parent, so the OS may have shoved it back onto the screen to
	/// make it fit. It has shrunk to its contents since, so ask again for where it was meant to
	/// go - the OS still gets the last word if it doesn't fit there either.
	/// </summary>
	private protected override void OnFirstShow()
	{
		Sdl.SetWindowPosition( Handle, (int)_position.x, (int)_position.y );
		_anchorPosition = null;
		UpdateAnchor();
	}

	Vector2? _anchorPosition;

	/// <summary>
	/// Place caret popups using their measured size, within the monitor's work area.
	/// Coordinates passed to SDL are relative to the parent window.
	/// </summary>
	internal void UpdateAnchor()
	{
		if ( Handle == IntPtr.Zero || HostedPopup?.AnchorRect is not { } anchor ) return;
		var bounds = Parent.DisplayWorkArea;
		// SDL reports popup positions relative to their parent window.
		for ( var ancestor = Parent; ancestor is not null; ancestor = (ancestor as PopupWindow)?.Parent )
			bounds.Position -= ancestor.Position;
		var source = new Rect( Parent.PixelsToWindow( anchor.Position ), Parent.PixelsToWindow( anchor.Size ) );
		var position = Sandbox.UI.Popup.AnchorPosition( source, PixelsToWindow( PixelSize ), bounds,
			HostedPopup.Position, Parent.PixelsToWindow( new Vector2( HostedPopup.PopupSourceOffset * HostedPopup.PopupSource.ScaleToScreen ) ).x );
		position = new Vector2( (int)position.x, (int)position.y );
		if ( _anchorPosition == position ) return;
		_anchorPosition = position;
		Sdl.SetWindowPosition( Handle, (int)position.x, (int)position.y );
	}

	/// <summary>
	/// The popup this window was showing goes before the surface does, so anything that wants
	/// out of it - a menu's rows - gets out before the surface deletes everything in it.
	/// </summary>
	private protected override void OnClosing()
	{
		var popup = HostedPopup;
		HostedPopup = null;

		if ( popup is { IsDeleting: false } )
		{
			popup.Delete( true );
		}
	}
}
