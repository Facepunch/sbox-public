using NativeEngine;

namespace Sandbox.Engine;

/// <summary>
/// A game surface in a native window the host owns, with no SDL window over it to read input from or capture the mouse
/// with: the editor's play widget on Linux and macOS, where SDL doesn't adopt Qt's windows. The host passes the surface's input to
/// <see cref="HostSurfaceInput"/>, and <see cref="WindowInput"/> has it capture the mouse.
/// </summary>
internal interface IHostInputSurface
{
	/// <summary>
	/// Start or stop capturing the mouse: hide the cursor over the surface, keep the pointer on it and report its motion
	/// as deltas (<see cref="HostSurfaceInput.OnMouseMotion"/>). Returns whether the mouse is captured.
	/// </summary>
	bool SetMouseCapture( bool capture );

	/// <summary>
	/// Move the cursor to a position on the surface.
	/// </summary>
	void WarpCursor( Vector2 position );
}

/// <summary>
/// Input from a host's game surface (<see cref="IHostInputSurface"/>), gated and routed as <see cref="SdlEvents"/> does
/// SDL's, so the game can't tell the two apart.
/// </summary>
[SkipHotload]
internal static class HostSurfaceInput
{
	// Captured motion moves the engine's cursor position without moving the cursor, so the first position after it
	// has nothing to be a delta from
	static bool positionStale;

	/// <summary>
	/// A key, by its SDL scancode, with the text it typed if any.
	/// </summary>
	internal static void OnKey( int scancode, bool down, bool repeat, string text )
	{
		// As SdlEvents does a key: none without game focus, and Shift never repeats
		if ( !WindowInput.HasMouseFocus() ) return;
		var scanCode = Sandbox.Engine.KeyTranslation.ScanCodeToButtonCode( scancode );
		repeat &= scanCode is not (ButtonCode.KEY_LSHIFT or ButtonCode.KEY_RSHIFT);
		InputRouter.OnKey( scanCode, Sandbox.Engine.KeyTranslation.KeyCodeToButtonCode( Sdl.GetKeyFromScancode( scancode, 0, true ) ), down, repeat );

		// SDL sends text after the key that typed it, only while a panel takes text, and never control characters
		if ( down && InputRouter.KeyboardFocusPanel is not null && !string.IsNullOrEmpty( text ) && !text.Any( char.IsControl ) )
			InputRouter.OnText( text );
	}

	/// <summary>
	/// A mouse button at a position on the surface.
	/// </summary>
	internal static void OnMouseButton( ButtonCode button, bool down, Vector2 position )
	{
		// As SdlEvents does a button: the click that focuses the surface lands where the cursor is, not where it was last seen
		if ( !WindowInput.GetRelativeMouseMode() && InputRouter.MouseCursorPosition != position )
			InputRouter.OnMousePositionChange( position.x, position.y, 0, 0 );
		if ( button != ButtonCode.BUTTON_CODE_INVALID ) InputRouter.OnMouseButton( button, down );
	}

	/// <summary>
	/// The cursor moved to a position on the surface, while the mouse isn't captured.
	/// </summary>
	internal static void OnMousePosition( Vector2 position )
	{
		if ( WindowInput.GetRelativeMouseMode() ) return;
		var delta = positionStale ? Vector2.Zero : position - InputRouter.MouseCursorPosition;
		positionStale = false;
		InputRouter.OnMousePositionChange( position.x, position.y, delta.x, delta.y );
	}

	/// <summary>
	/// The mouse moved while captured.
	/// </summary>
	internal static void OnMouseMotion( Vector2 delta )
	{
		if ( !WindowInput.GetRelativeMouseMode() ) return;
		positionStale = true;
		InputRouter.OnMouseMotion( delta.x, delta.y );
	}

	/// <summary>
	/// The wheel turned, in notches, with the whole notches it has turned since the last.
	/// </summary>
	internal static void OnMouseWheel( Vector2 delta, int ticksX, int ticksY ) => InputRouter.OnMouseWheel( delta.x, delta.y, ticksX, ticksY );
}
