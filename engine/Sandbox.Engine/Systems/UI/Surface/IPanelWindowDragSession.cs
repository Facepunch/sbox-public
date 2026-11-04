using NativeEngine;

namespace Sandbox.UI;

/// <summary>
/// A single cross-window pointer drag. Input callbacks record intent; transfers happen in Frame.
/// </summary>
internal interface IPanelWindowDragSession
{
	/// <summary>Advance the drag at the boundary before windows draw.</summary>
	void Frame();

	/// <summary>Cancel the drag. Cleanup may be deferred to Frame.</summary>
	void Cancel();

	/// <summary>Record a mouse button event. True consumes it before ordinary routing.</summary>
	bool OnMouseButton( IntPtr window, ButtonCode button, bool down );

	/// <summary>Record a key event. True consumes it before popup and focus routing.</summary>
	bool OnKey( IntPtr window, ButtonCode button, bool down );

	/// <summary>Record a window focus change.</summary>
	void OnFocus( IntPtr window, bool focused );

	/// <summary>Record a window leaving the registry.</summary>
	void OnWindowClosing( IPanelWindow window );
}
