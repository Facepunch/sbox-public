using Sandbox.Engine;
using System;
using System.Runtime.InteropServices;

namespace Editor;

internal sealed partial class PlayWidgetInput
{
	/// <summary>
	/// Qt on Cocoa, where SDL can't adopt a view inside Qt's window. The cursor is frozen and hidden and the mouse's
	/// delta read (<see cref="CocoaRelativePointer"/>).
	/// </summary>
	sealed class MacOS : Platform
	{
		internal override bool BeginCapture( SceneRenderingWidget widget )
		{
			// The cursor stays where it is: put it in the widget, and off its edge, where a press would resize the window
			if ( !widget.LocalRect.IsInside( widget.FromScreen( Application.CursorPosition ) ) )
				CocoaRelativePointer.Warp( widget.ToScreen( widget.Size / 2 ) );

			return CocoaRelativePointer.Begin();
		}

		internal override void EndCapture( SceneRenderingWidget widget ) => CocoaRelativePointer.End();

		internal override Vector2 TakeMotion() => CocoaRelativePointer.TakeMotion();

		// Qt's own move posts a mouse event, which lands after the call returns
		internal override void WarpCursor( Vector2 screenPosition ) => CocoaRelativePointer.Warp( screenPosition );

		// Qt's native virtual key is the key's NSEvent.keyCode
		internal override int ScanCode( KeyEvent e ) => MacKeyCodes.ToScanCode( (int)e.NativeKeyCode );

		// Qt's Cocoa plugin never swaps the axes
		internal override Vector2 WheelAngle( WheelEvent e ) => e.AngleDelta;

		internal override void GrabMouse( IntPtr qWidget ) => QWidgetGrabMouse( qWidget );

		internal override void ReleaseMouse( IntPtr qWidget ) => QWidgetReleaseMouse( qWidget );

		// Resolved by DLLImportResolver to libQt5Widgets.dylib beside the engine's own
		[DllImport( "Qt5Widgets", EntryPoint = "_ZN7QWidget9grabMouseEv" )]
		static extern void QWidgetGrabMouse( IntPtr widget );

		[DllImport( "Qt5Widgets", EntryPoint = "_ZN7QWidget12releaseMouseEv" )]
		static extern void QWidgetReleaseMouse( IntPtr widget );
	}
}
