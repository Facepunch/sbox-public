using Sandbox.Engine;
using System;
using System.Runtime.InteropServices;

namespace Editor;

/// <summary>
/// The play widget's input where Qt owns its window and no SDL window wraps it (Linux, where Qt is an X11 client).
/// Passes the widget's keys, buttons, wheel and motion on as SDL's would be, and captures the mouse by hiding the cursor
/// and taking its motion from X11 raw events (<see cref="X11RelativePointer"/>) once a frame. Hiding it is also what makes
/// XWayland lock the Wayland pointer and pass on its relative motion.
/// </summary>
internal sealed class PlayWidgetInput : IHostInputSurface
{
	static PlayWidgetInput current;

	readonly SceneRenderingWidget widget;
	bool captured;
	CursorShape cursorBeforeCapture;

	// Fractions of a wheel notch not yet reported as a whole one
	Vector2 wheelNotches;

	PlayWidgetInput( SceneRenderingWidget widget )
	{
		this.widget = widget;
	}

	/// <summary>
	/// The play widget's input when this widget is the play widget and Qt owns it, else null.
	/// </summary>
	internal static PlayWidgetInput For( SceneRenderingWidget widget ) => current is not null && current.widget == widget ? current : null;

	/// <summary>
	/// True while the play widget Qt owns has game focus: its keys belong to the game, not the editor's shortcuts.
	/// </summary>
	internal static bool HasGameFocus => current is not null && WindowInput.HasMouseFocus();

	/// <summary>
	/// A widget became the play widget with no SDL window over it: take its input where that's Qt's to give (Linux).
	/// Call before the widget gets focus.
	/// </summary>
	internal static void Attach( SceneRenderingWidget widget )
	{
		if ( !OperatingSystem.IsLinux() ) return;
		current = new PlayWidgetInput( widget );
		widget.MouseMove += current.OnMouseMove;
		WindowInput.SetHostSurface( current );
	}

	/// <summary>
	/// The play widget stopped being it. Call after it lost game focus, which let the mouse go.
	/// </summary>
	internal static void Detach()
	{
		if ( current is null ) return;
		current.widget.MouseMove -= current.OnMouseMove;
		WindowInput.SetHostSurface( null );
		current = null;
	}

	[EditorEvent.Frame]
	static void OnFrame() => current?.Frame();

	bool IHostInputSurface.SetMouseCapture( bool capture )
	{
		capture &= widget.IsValid();
		if ( captured == capture ) return captured;

		// The grab keeps the pointer's buttons and wheel coming here wherever it is; the raw events give its motion
		if ( capture )
		{
			if ( !X11RelativePointer.Begin( widget._widget.winId() ) ) return false;
			cursorBeforeCapture = widget.Cursor;
			widget.Cursor = CursorShape.Blank;
			QWidgetGrabMouse( widget._widget );
		}
		else
		{
			X11RelativePointer.End();
			if ( widget.IsValid() )
			{
				QWidgetReleaseMouse( widget._widget );
				widget.Cursor = cursorBeforeCapture;
			}
		}

		captured = capture;
		return captured;
	}

	void IHostInputSurface.WarpCursor( Vector2 position )
	{
		if ( widget.IsValid() ) Application.CursorPosition = widget.ToScreen( position );
	}

	/// <summary>
	/// Once a frame: the mouse's motion while captured.
	/// </summary>
	void Frame()
	{
		if ( !captured ) return;

		var motion = X11RelativePointer.TakeMotion();
		if ( !motion.IsNearZeroLength ) HostSurfaceInput.OnMouseMotion( motion );
	}

	// The cursor's position while the widget has focus; GameMode reports it while it hasn't. While captured the
	// motion comes from the raw events, and the pointer only moves back to the middle
	void OnMouseMove( Vector2 position )
	{
		if ( captured || !widget.IsFocused ) return;
		HostSurfaceInput.OnMousePosition( new Vector2( (int)position.x, (int)position.y ) );
	}

	internal void OnMouseButton( MouseButtons qtButton, Vector2 position, bool down )
	{
		// SDL's numbering, so it maps as SDL's buttons do
		byte sdlButton = qtButton switch
		{
			MouseButtons.Left => 1,
			MouseButtons.Middle => 2,
			MouseButtons.Right => 3,
			MouseButtons.Back => 4,
			MouseButtons.Forward => 5,
			_ => 0
		};

		HostSurfaceInput.OnMouseButton( SdlEvents.MouseButton( sdlButton ), down, new Vector2( (int)position.x, (int)position.y ) );
	}

	internal void OnMouseWheel( WheelEvent e )
	{
		// Qt swaps the axes while Alt is held, and counts scrolling left as positive where SDL counts right
		var angle = e.AngleDelta;
		if ( e.HasAlt ) angle = new Vector2( angle.y, angle.x );
		var notches = new Vector2( -angle.x, angle.y ) / 120f;

		wheelNotches += notches;
		var ticksX = (int)wheelNotches.x;
		var ticksY = (int)wheelNotches.y;
		wheelNotches -= new Vector2( ticksX, ticksY );

		HostSurfaceInput.OnMouseWheel( notches, ticksX, ticksY );
	}

	/// <summary>
	/// A key the play widget got: the game's while it has focus, as SDL takes them before Qt elsewhere. Returns whether
	/// it was, and the widget should do nothing more with it. Qt's xcb key codes are X11's: evdev's plus 8.
	/// </summary>
	internal bool OnKey( bool down, KeyEvent e )
	{
		if ( !WindowInput.HasMouseFocus() ) return false;
		e.Accepted = true;

		// SDL repeats presses alone; Qt also sends a release before each repeated press
		if ( !down && e.IsAutoRepeat ) return true;

		HostSurfaceInput.OnKey( LinuxKeyCodes.ToScanCode( (int)e.NativeScanCode - 8 ), down, down && e.IsAutoRepeat, e.Text );
		return true;
	}

	// No binding exposes these, and only Qt's own grab keeps the pointer's events coming to Qt
	[DllImport( "libQt5Widgets.so.5", EntryPoint = "_ZN7QWidget9grabMouseEv" )]
	static extern void QWidgetGrabMouse( IntPtr widget );

	[DllImport( "libQt5Widgets.so.5", EntryPoint = "_ZN7QWidget12releaseMouseEv" )]
	static extern void QWidgetReleaseMouse( IntPtr widget );
}
