using Sandbox.Engine;
using System;

namespace Editor;

/// <summary>
/// The play widget's input where Qt owns its window and no SDL window wraps it (Linux, where Qt is an X11 client, and
/// macOS, where SDL can't adopt a view inside Qt's window). Passes the widget's keys, buttons, wheel and motion on as
/// SDL's would be, and captures the mouse once a frame's motion at a time. What the window system needs for that is
/// <see cref="Platform"/>, one per OS in its own file.
/// </summary>
internal sealed partial class PlayWidgetInput : IHostInputSurface
{
	/// <summary>
	/// What the play widget's input needs from the window system Qt runs on. Each platform's is in its own file.
	/// </summary>
	abstract class Platform
	{
		/// <summary>This platform's, or null where SDL wraps the play widget's window and takes its input itself (Windows).</summary>
		internal static Platform Create()
		{
			if ( OperatingSystem.IsLinux() ) return new Linux();
			if ( OperatingSystem.IsMacOS() ) return new MacOS();
			return null;
		}

		/// <summary>Hide the cursor, hold it on the widget and start reporting the mouse's motion. False if it can't.</summary>
		internal abstract bool BeginCapture( SceneRenderingWidget widget );
		/// <summary>Let the cursor go and show it. The widget may no longer be valid.</summary>
		internal abstract void EndCapture( SceneRenderingWidget widget );
		/// <summary>The mouse's motion since the last call while captured, as SDL's relative mode reports it.</summary>
		internal abstract Vector2 TakeMotion();
		/// <summary>Move the cursor to a position on screen, in Qt's global coordinates.</summary>
		internal abstract void WarpCursor( Vector2 screenPosition );
		/// <summary>The SDL scancode of the key an event is for, or zero.</summary>
		internal abstract int ScanCode( KeyEvent e );
		/// <summary>A wheel event's angle delta, x for the wheel's horizontal axis whatever keys are held.</summary>
		internal abstract Vector2 WheelAngle( WheelEvent e );
		/// <summary><c>QWidget::grabMouse</c>: no binding exposes it, and only Qt's own grab keeps the pointer's events coming to Qt.</summary>
		internal abstract void GrabMouse( IntPtr qWidget );
		/// <summary><c>QWidget::releaseMouse</c>.</summary>
		internal abstract void ReleaseMouse( IntPtr qWidget );
	}

	static PlayWidgetInput current;

	readonly SceneRenderingWidget widget;
	readonly Platform platform;
	bool captured;

	// Fractions of a wheel notch not yet reported as a whole one
	Vector2 wheelNotches;

	PlayWidgetInput( SceneRenderingWidget widget, Platform platform )
	{
		this.widget = widget;
		this.platform = platform;
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
	/// A widget became the play widget with no SDL window over it: take its input where that's Qt's to give (Linux, macOS).
	/// Call before the widget gets focus.
	/// </summary>
	internal static void Attach( SceneRenderingWidget widget )
	{
		if ( Platform.Create() is not { } platform ) return;
		current = new PlayWidgetInput( widget, platform );
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

		// The grab keeps the pointer's buttons and wheel coming here wherever it is; the pointer capture gives its motion
		if ( capture )
		{
			if ( !platform.BeginCapture( widget ) ) return false;
			platform.GrabMouse( widget._widget );
		}
		else
		{
			platform.EndCapture( widget );
			if ( widget.IsValid() ) platform.ReleaseMouse( widget._widget );
		}

		captured = capture;
		return captured;
	}

	void IHostInputSurface.WarpCursor( Vector2 position )
	{
		if ( widget.IsValid() ) platform.WarpCursor( widget.ToScreen( position / widget.DpiScale ) );
	}

	/// <summary>
	/// Once a frame: the mouse's motion while captured.
	/// </summary>
	void Frame()
	{
		if ( !captured ) return;

		var motion = platform.TakeMotion();
		if ( !motion.IsNearZeroLength ) HostSurfaceInput.OnMouseMotion( motion );
	}

	// The game's surface is the widget's pixels; Qt's positions are in its own units, two pixels each on a Retina display
	Vector2 ToSurface( Vector2 widgetPosition )
	{
		var pixels = widgetPosition * widget.DpiScale;
		return new Vector2( (int)pixels.x, (int)pixels.y );
	}

	// The cursor's position while the widget has focus; GameMode reports it while it hasn't. While captured the
	// motion comes from the pointer capture, and the pointer is held in place
	void OnMouseMove( Vector2 position )
	{
		if ( captured || !widget.IsFocused ) return;
		HostSurfaceInput.OnMousePosition( ToSurface( position ) );
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

		HostSurfaceInput.OnMouseButton( SdlEvents.MouseButton( sdlButton ), down, ToSurface( position ) );
	}

	internal void OnMouseWheel( WheelEvent e )
	{
		// Qt counts scrolling left as positive where SDL counts right
		var angle = platform.WheelAngle( e );
		var notches = new Vector2( -angle.x, angle.y ) / 120f;

		// Qt's Cocoa plugin also sends a scroll's begin and end, which turn nothing
		if ( notches == Vector2.Zero ) return;

		wheelNotches += notches;
		var ticksX = (int)wheelNotches.x;
		var ticksY = (int)wheelNotches.y;
		wheelNotches -= new Vector2( ticksX, ticksY );

		HostSurfaceInput.OnMouseWheel( notches, ticksX, ticksY );
	}

	/// <summary>
	/// A key the play widget got: the game's while it has focus, as SDL takes them before Qt elsewhere. Returns whether
	/// it was, and the widget should do nothing more with it.
	/// </summary>
	internal bool OnKey( bool down, KeyEvent e )
	{
		if ( !WindowInput.HasMouseFocus() ) return false;
		e.Accepted = true;

		// SDL repeats presses alone; Qt also sends a release before each repeated press
		if ( !down && e.IsAutoRepeat ) return true;

		HostSurfaceInput.OnKey( platform.ScanCode( e ), down, down && e.IsAutoRepeat, e.Text );
		return true;
	}
}
