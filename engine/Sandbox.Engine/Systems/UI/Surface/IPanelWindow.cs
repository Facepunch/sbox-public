using NativeEngine;

namespace Sandbox.UI;

/// <summary>
/// Engine routing and rendering contract for a <see cref="UISurface"/> window implemented
/// by Sandbox.Tools, including editor windows and standalone panel applications.
/// </summary>
internal interface IPanelWindow
{
	/// <summary>
	/// The SDL_Window pointer used to route SDL events, not a platform window handle.
	/// </summary>
	IntPtr Handle { get; }

	/// <summary>
	/// The UI running in this window.
	/// </summary>
	UISurface Surface { get; }

	/// <summary>
	/// Whether the window is still open, including popups awaiting native window creation.
	/// </summary>
	bool IsOpen { get; }

	/// <summary>
	/// Is the cursor over this window? Only one window can think so at a time.
	/// </summary>
	bool MouseInside { get; set; }

	/// <summary>
	/// Where the cursor is, in this window's pixels.
	/// </summary>
	void SetCursorPosition( Vector2 position );

	/// <summary>
	/// A position SDL reported against this window, in surface pixels. SDL talks in window
	/// coordinates and those aren't pixels on every platform, so every position arriving from the
	/// OS goes through here and nothing downstream has to know the difference.
	/// </summary>
	Vector2 ToSurface( Vector2 windowPosition );

	/// <summary>
	/// Simulate and draw. Called every frame, and again from inside a resize drag - the OS holds
	/// the thread in a modal loop there and this is the only chance we get. Returns whether
	/// anything was presented, so a loop paced by vsync knows when to back off instead.
	/// </summary>
	bool Frame();

	/// <summary>
	/// What's under the cursor, so the OS knows whether a click drags the window, resizes it, or
	/// belongs to the UI. The position is in surface pixels, like every other position down here.
	/// </summary>
	Sdl.HitTestResult HitTest( Vector2 position );

	/// <summary>
	/// The user clicked the window's close button.
	/// </summary>
	void RequestClose();

	/// <summary>
	/// The window was moved, by the user or by code.
	/// </summary>
	void Moved();

	/// <summary>
	/// The OS reported a size change. Also arrives for exposes, so check the size actually moved.
	/// </summary>
	void Resized();

	/// <summary>
	/// Minimized (1), maximized (2) or restored from either (0).
	/// </summary>
	void StateChanged( int state );

	/// <summary>
	/// The window took or lost the OS keyboard focus.
	/// </summary>
	void FocusChanged( bool focused );

	/// <summary>
	/// The window is now on a different display.
	/// </summary>
	void DisplayChanged();

	/// <summary>
	/// A popup - a transient window like a menu, dismissed by a click anywhere else. While one
	/// that takes input is up it has the keyboard, so a window meant to stay up alongside its
	/// parent should set <see cref="IgnoresInput"/> instead.
	/// </summary>
	bool IsPopup { get; }

	/// <summary>
	/// The window a popup hangs off. Null for a top-level window.
	/// </summary>
	IPanelWindow Parent { get; }

	/// <summary>
	/// A popup that never takes the mouse or the keyboard - a tooltip, say. Where the OS doesn't
	/// pass the mouse through it for us, the routing here has to.
	/// </summary>
	bool IgnoresInput { get; }

	/// <summary>
	/// Whether keyboard input is redirected to this popup. Mouse input is independent.
	/// </summary>
	bool TakesKeyboardFocus => !IgnoresInput;

	/// <summary>
	/// Let frames run inside a frame that's already running. An outgoing drag blocks in the
	/// middle of one, and the frames the OS drag loop pulses are the only ones there are.
	/// </summary>
	bool AllowNestedFrame { get; set; }

	/// <summary>
	/// Does this window have the OS keyboard focus?
	/// </summary>
	bool IsFocused { get; }

	/// <summary>
	/// Keep drawing at the display's frame rate even when nobody is looking at this window.
	/// Idle windows are paced right down - set this for one with something moving in it that
	/// has to keep moving, like a video or a live preview.
	/// </summary>
	bool AlwaysFullFrameRate { get; set; }
}
