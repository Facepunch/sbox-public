namespace Sandbox.UI;

/// <summary>
/// A window hosting a <see cref="UISurface"/>. The window itself lives in Sandbox.Tools - only the
/// editor makes OS windows - but SDL delivers its events down here, so the engine needs this much
/// of it to route them and to draw it each frame.
/// </summary>
internal interface IPanelWindow
{
	/// <summary>
	/// The OS window handle, which is how an SDL event is traced back to us.
	/// </summary>
	IntPtr Handle { get; }

	/// <summary>
	/// The UI running in this window.
	/// </summary>
	UISurface Surface { get; }

	/// <summary>
	/// Whether the window still has its OS window and swap chain.
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
	/// Simulate and draw. Called every frame, and again from inside a resize drag - the OS holds
	/// the thread in a modal loop there and this is the only chance we get. Returns whether
	/// anything was presented, so a loop paced by vsync knows when to back off instead.
	/// <para>
	/// <paramref name="interactiveResize"/> is true for the frames a resize drag drives. A
	/// vsync'd window presents immediately for those - waiting for the display during a drag
	/// shows as the contents running a frame behind the window edge.
	/// </para>
	/// </summary>
	bool Frame( bool interactiveResize );

	/// <summary>
	/// What's under the cursor, so the OS knows whether a click drags the window, resizes it, or
	/// belongs to the UI.
	/// </summary>
	WindowHitTest HitTest( Vector2 position );

	/// <summary>
	/// The user clicked the window's close button.
	/// </summary>
	void RequestClose();

	/// <summary>
	/// A popup - a transient window like a menu, dismissed by a click anywhere else.
	/// </summary>
	bool IsPopup { get; }

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

	/// <summary>
	/// What's under the cursor in a window, for the OS. Values match SDL_HitTestResult - the
	/// cast to int happens at the native boundary and nowhere else.
	/// </summary>
	internal enum WindowHitTest
	{
		Normal = 0,
		Draggable = 1,
		ResizeTopLeft = 2,
		ResizeTop = 3,
		ResizeTopRight = 4,
		ResizeRight = 5,
		ResizeBottomRight = 6,
		ResizeBottom = 7,
		ResizeBottomLeft = 8,
		ResizeLeft = 9,
	}
}
