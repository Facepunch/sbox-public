using Sandbox.Engine;
using System;
using System.Runtime.InteropServices;

namespace Editor;

internal sealed partial class PlayWidgetInput
{
	/// <summary>
	/// Qt as an X11 client. The mouse's motion comes from X11 raw events (<see cref="X11RelativePointer"/>) while the
	/// cursor is hidden, which is also what makes XWayland lock the Wayland pointer and pass on its relative motion.
	/// </summary>
	sealed class Linux : Platform
	{
		CursorShape cursorBeforeCapture;

		internal override bool BeginCapture( SceneRenderingWidget widget )
		{
			if ( !X11RelativePointer.Begin( widget._widget.winId() ) ) return false;
			cursorBeforeCapture = widget.Cursor;
			widget.Cursor = CursorShape.Blank;
			return true;
		}

		internal override void EndCapture( SceneRenderingWidget widget )
		{
			X11RelativePointer.End();
			if ( widget.IsValid() ) widget.Cursor = cursorBeforeCapture;
		}

		internal override Vector2 TakeMotion() => X11RelativePointer.TakeMotion();

		internal override void WarpCursor( Vector2 screenPosition ) => Application.CursorPosition = screenPosition;

		// Qt's xcb key codes are X11's: evdev's plus 8
		internal override int ScanCode( KeyEvent e ) => LinuxKeyCodes.ToScanCode( (int)e.NativeScanCode - 8 );

		// Qt's xcb plugin swaps the axes while Alt is held
		internal override Vector2 WheelAngle( WheelEvent e ) => e.HasAlt ? new Vector2( e.AngleDelta.y, e.AngleDelta.x ) : e.AngleDelta;

		internal override void GrabMouse( IntPtr qWidget ) => QWidgetGrabMouse( qWidget );

		internal override void ReleaseMouse( IntPtr qWidget ) => QWidgetReleaseMouse( qWidget );

		[DllImport( "libQt5Widgets.so.5", EntryPoint = "_ZN7QWidget9grabMouseEv" )]
		static extern void QWidgetGrabMouse( IntPtr widget );

		[DllImport( "libQt5Widgets.so.5", EntryPoint = "_ZN7QWidget12releaseMouseEv" )]
		static extern void QWidgetReleaseMouse( IntPtr widget );
	}
}
