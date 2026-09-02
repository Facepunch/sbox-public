using NativeEngine;

namespace Sandbox.UI;

/// <summary>
/// Where input for our own OS windows arrives. Native peels these events off before the engine's
/// input system sees them (see src/engine2/sbox/panelwindow.cpp), so nothing here can reach the
/// game, and nothing the game does can reach here.
/// <para>
/// This is also where the routing decisions live - the mouse goes to the window it's over, the
/// keyboard to the window that's focused.
/// </para>
/// </summary>
internal static class PanelWindowInput
{
	/// <summary>
	/// The cursor moved. SDL gives the position relative to the window it happened in, which is
	/// the only frame it means anything in.
	/// </summary>
	internal static void OnMouseMove( IntPtr window, float x, float y )
	{
		if ( PanelWindows.Find( window ) is not { } target ) return;

		PanelWindows.SetCursorPosition( target, new Vector2( x, y ) );
	}

	/// <summary>
	/// The cursor left a window. Without this a window the cursor has walked out of would carry
	/// on thinking it owns the mouse, because it stops getting move events.
	/// </summary>
	internal static void OnMouseLeave( IntPtr window )
	{
		if ( PanelWindows.Find( window ) is not { } target ) return;

		target.MouseInside = false;
	}

	internal static void OnMouseButton( IntPtr window, ButtonCode button, bool down, int clicks, int ikeymods )
	{
		// The window under the cursor, not the focused one - the mouse doesn't need focus
		if ( Target( window ) is not { } target ) return;

		// A button down anywhere that isn't a popup dismisses the popups, like OS menus. The
		// OS can't do this for us - a popup menu never takes real focus off its parent, so
		// there's no focus change to hear about.
		if ( down ) PanelWindows.DismissPopups( except: target );

		// The in-surface popups too, the way the game's input does - a click inside one
		// survives, anywhere else closes them
		if ( down ) BasePopup.CloseAll( target.Surface.Hovered );

		var modifiers = ToModifiers( ikeymods );

		target.Surface.SetMouseButton( button, down, modifiers );

		if ( down && clicks >= 2 && ToMouseButton( button ) is { } mouseButton )
		{
			// A third click arrives as its own event after the double, so a selection can grow
			// word then line the way it does everywhere else
			if ( clicks == 2 ) target.Surface.SetDoubleClick( mouseButton );
			else if ( clicks == 3 ) target.Surface.SetTripleClick( mouseButton );
		}
	}

	internal static void OnMouseWheel( IntPtr window, float x, float y, int ikeymods )
	{
		if ( Target( window ) is not { } target ) return;

		target.Surface.SetMouseWheel( new Vector2( x, y ), ToModifiers( ikeymods ) );
	}

	internal static void OnKey( IntPtr window, ButtonCode button, bool down, bool repeating, int ikeymods )
	{
		if ( PanelWindows.Find( window ) is not { } target ) return;

		target.Surface.SetKey( button, down, ToModifiers( ikeymods ) );
	}

	internal static void OnText( IntPtr window, string text )
	{
		if ( PanelWindows.Find( window ) is not { } target ) return;

		target.Surface.TypeText( text );
	}

	/// <summary>
	/// An OS drag came in over a window. The payload arrives here once; the hover queries
	/// that follow reuse it.
	/// </summary>
	internal static void OnDragEnter( IntPtr window, string files, string text )
	{
		PanelWindows.Find( window )?.Surface.DragEnter(
			files?.Split( '\n', StringSplitOptions.RemoveEmptyEntries ), text );
	}

	/// <summary>
	/// The drag moved - answer what dropping here would do, for the drag cursor.
	/// </summary>
	internal static int OnDragOver( IntPtr window, float x, float y )
	{
		return (int)(PanelWindows.Find( window )?.Surface.DragOver( new Vector2( x, y ) ) ?? DropAction.None);
	}

	/// <summary>
	/// The drag left the window without dropping.
	/// </summary>
	internal static void OnDragLeave( IntPtr window )
	{
		PanelWindows.Find( window )?.Surface.DragLeave();
	}

	/// <summary>
	/// The payload landed. Returns what was done with it.
	/// </summary>
	internal static int OnDragDrop( IntPtr window, float x, float y )
	{
		return (int)(PanelWindows.Find( window )?.Surface.Drop( new Vector2( x, y ) ) ?? DropAction.None);
	}

	/// <summary>
	/// An outgoing drag has the main thread parked in the OS drag loop - these pulses are
	/// the only frames that run until it lets go, same as a resize drag.
	/// </summary>
	internal static void OnDragFrame()
	{
		if ( PanelWindows.FrameAll() )
		{
			PanelWindows.FrameEnd();
		}
	}

	/// <summary>
	/// A file from the OS is being dropped on a window - they accumulate until the drop completes.
	/// </summary>
	internal static void OnDropFile( IntPtr window, string path )
	{
		PanelWindows.Find( window )?.Surface.DropFile( path );
	}

	/// <summary>
	/// Text from the OS is being dropped on a window.
	/// </summary>
	internal static void OnDropText( IntPtr window, string text )
	{
		PanelWindows.Find( window )?.Surface.DropText( text );
	}

	/// <summary>
	/// The drop landed - deliver it to the panel under the cursor.
	/// </summary>
	internal static void OnDropComplete( IntPtr window, float x, float y )
	{
		PanelWindows.Find( window )?.Surface.DropComplete( new Vector2( x, y ) );
	}

	// Windows with an IME composition in flight
	static readonly HashSet<IntPtr> _composing = new();

	/// <summary>
	/// The IME composition changed. A non-empty string is the pending composition; an empty one
	/// means it was committed or cancelled - committed text arrives through <see cref="OnText"/>
	/// either way, which is the filtered typing path.
	/// </summary>
	internal static void OnImeComposition( IntPtr window, string text )
	{
		if ( PanelWindows.Find( window ) is not { } target ) return;

		if ( ImeComposition.Update( target.Surface.Focus, _composing.Contains( window ), text ) )
			_composing.Add( window );
		else
			_composing.Remove( window );
	}

	internal static void OnFocus( IntPtr window, bool focused )
	{
		if ( PanelWindows.Find( window ) is not { } target ) return;

		if ( !focused )
		{
			target.MouseInside = false;

			// The window lost the keyboard - a text box shouldn't sit there looking focused
			// with a blinking caret it can't type into
			target.Surface?.System.ClearFocus();

			// The keyboard left the app - unless it went into one of our popups, which is a
			// menu opening, any open menu goes away with it
			if ( !target.IsPopup && !PanelWindows.AnyPopupFocused() )
			{
				PanelWindows.DismissPopups();
			}
		}
	}

	/// <summary>
	/// A window was resized or exposed. Redraw it right now - during a resize drag the OS holds
	/// the thread in a modal loop and this is the only chance we get. That makes this a whole
	/// frame, so it ends with the frame-end bookkeeping too.
	/// </summary>
	internal static void OnResized( IntPtr window )
	{
		if ( PanelWindows.Find( window )?.Frame( interactiveResize: true ) == true )
		{
			PanelWindows.FrameEnd();
		}
	}

	internal static void OnClose( IntPtr window )
	{
		_composing.Remove( window );
		PanelWindows.Find( window )?.RequestClose();
	}

	/// <summary>
	/// What the user picked in the OS dialog. Empty means cancelled.
	/// </summary>
	internal static void OnFileDialogResult( IntPtr window, string path )
	{
		PanelWindowDialogs.OnResult( string.IsNullOrEmpty( path ) ? null : path );
	}

	/// <summary>
	/// What's under the cursor, so the OS knows whether a click drags the window, resizes it, or
	/// belongs to the UI.
	/// </summary>
	internal static int OnHitTest( IntPtr window, int x, int y )
	{
		if ( PanelWindows.Find( window ) is not { } target ) return 0;

		return (int)target.HitTest( new Vector2( x, y ) );
	}

	/// <summary>
	/// Where a mouse event should go. The window the cursor is in, falling back to the window the
	/// event was reported against.
	/// </summary>
	static IPanelWindow Target( IntPtr window )
	{
		return PanelWindows.Hovering ?? PanelWindows.Find( window );
	}

	/// <summary>
	/// Engine modifier flags (IE_ShiftPressed and friends) to ours.
	/// </summary>
	static KeyboardModifiers ToModifiers( int engine )
	{
		var modifiers = KeyboardModifiers.None;

		if ( (engine & 1) != 0 ) modifiers |= KeyboardModifiers.Shift;
		if ( (engine & 2) != 0 ) modifiers |= KeyboardModifiers.Ctrl;
		if ( (engine & 4) != 0 ) modifiers |= KeyboardModifiers.Alt;

		return modifiers;
	}

	static MouseButtons? ToMouseButton( ButtonCode button ) => button switch
	{
		ButtonCode.MouseLeft => MouseButtons.Left,
		ButtonCode.MouseRight => MouseButtons.Right,
		ButtonCode.MouseMiddle => MouseButtons.Middle,
		_ => null,
	};
}
