using NativeEngine;
using Sandbox.UI;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sandbox.Engine;

/// <summary>
/// Pumps bounded batches from the process-wide SDL queue on the main thread and routes events to their window.
/// </summary>
[SkipHotload]
internal static class SdlEvents
{
	static bool polling;
	static bool ignoreNextRelativeMouseMove;

	/// <summary>
	/// Pump events on the main thread, stopping after a 5 ms processing budget. Nested polls return immediately.
	/// </summary>
	internal static void Poll()
	{
		ThreadSafe.AssertIsMainThread();
		// Modal window callbacks can run a nested frame while SDL is pumping OS messages.
		if ( polling ) return;
		polling = true;
		try
		{
			var started = Stopwatch.GetTimestamp();
			while ( Sdl.PollEvent( out var e ) )
			{
				try { Dispatch( e ); }
				catch ( Exception exception ) { Log.Warning( exception, $"SDL event {e.Type} failed" ); }
				// Leave time for the frame even if events arrive faster than we can process them.
				if ( e.Type == Sdl.EventType.Quit || Stopwatch.GetElapsedTime( started ).TotalMilliseconds > 5 ) break;
			}
		}
		finally
		{
			polling = false;
			WindowInput.UpdateApplicationState();
		}
	}

	internal static unsafe void Dispatch( Sdl.Event e )
	{
		if ( e.Type is Sdl.EventType.WindowFocusGained or Sdl.EventType.WindowFocusLost )
			WindowInput.UpdateApplicationState();

		var window = Sdl.GetWindowFromEvent( (IntPtr)(&e) );
		if ( window != IntPtr.Zero && PanelWindowInput.IsPanelWindow( window ) )
		{
			PanelWindowInput.OnEvent( window, in e );
			return;
		}

		switch ( e.Type )
		{
			case Sdl.EventType.WindowResized:
			case Sdl.EventType.WindowPixelSizeChanged:
			case Sdl.EventType.WindowMaximized:
			case Sdl.EventType.WindowRestored:
				if ( GameWindow.Current is { } game && game.Handle == window ) game.InvalidateWindowMode();
				break;
			case Sdl.EventType.KeyDown:
			case Sdl.EventType.KeyUp:
				if ( !WindowInput.HasMouseFocus() ) break;
				var scanCode = Sandbox.Engine.KeyTranslation.ScanCodeToButtonCode( e.Key.Scancode );
				var repeat = e.Key.Repeat != 0 && scanCode is not (ButtonCode.KEY_LSHIFT or ButtonCode.KEY_RSHIFT);
				InputRouter.OnKey( scanCode, Sandbox.Engine.KeyTranslation.KeyCodeToButtonCode( e.Key.Key ), e.Type == Sdl.EventType.KeyDown, repeat );
				break;
			case Sdl.EventType.TextInput: InputRouter.OnText( Marshal.PtrToStringUTF8( e.Text.Text ) ); break;
			case Sdl.EventType.TextEditing: InputRouter.OnImeComposition( Marshal.PtrToStringUTF8( e.Edit.Text ) ); break;
			case Sdl.EventType.DropFile: InputRouter.OnDropFile( Marshal.PtrToStringUTF8( e.Drop.Data ) ); break;
			case Sdl.EventType.DropText: InputRouter.OnDropText( Marshal.PtrToStringUTF8( e.Drop.Data ) ); break;
			case Sdl.EventType.DropComplete: InputRouter.OnDropComplete( e.Drop.X, e.Drop.Y ); break;
			case Sdl.EventType.MouseMotion:
				if ( WindowInput.GetRelativeMouseMode() )
				{
					if ( !ignoreNextRelativeMouseMove ) InputRouter.OnMouseMotion( e.Motion.XRel, e.Motion.YRel );
					ignoreNextRelativeMouseMove = false;
				}
				else if ( WindowInput.HasMouseFocus() )
				{
					ignoreNextRelativeMouseMove = false;
					InputRouter.OnMousePositionChange( e.Motion.X, e.Motion.Y, e.Motion.XRel, e.Motion.YRel );
				}
				break;
			case Sdl.EventType.MouseButtonDown:
			case Sdl.EventType.MouseButtonUp:
				if ( window != IntPtr.Zero && window == WindowInput.GetEditorMainWindow() && !WindowInput.GetRelativeMouseMode() ) break;
				var button = MouseButton( e.Button.Button );
				if ( button != ButtonCode.BUTTON_CODE_INVALID ) InputRouter.OnMouseButton( button, e.Type == Sdl.EventType.MouseButtonDown );
				break;
			case Sdl.EventType.MouseWheel:
				var wheel = WheelDelta( e.Wheel );
				if ( wheel != Vector2.Zero ) InputRouter.OnMouseWheel( wheel.x, wheel.y );
				break;
			case Sdl.EventType.Quit: InputRouter.CloseApplication(); break;
			case Sdl.EventType.WindowCloseRequested:
				if ( GameWindow.Current is { } closingGame && closingGame.Handle == window ) InputRouter.CloseApplication();
				break;
			case Sdl.EventType.WindowFocusGained:
				// Avoid a jump from the first captured motion after switching windows.
				ignoreNextRelativeMouseMove = true;
				break;
			case Sdl.EventType.GamepadAdded: SdlGamepads.Connect( e.GamepadDevice.Which ); break;
			case Sdl.EventType.GamepadRemoved: SdlGamepads.Disconnect( e.GamepadDevice.Which ); break;
			case Sdl.EventType.GamepadButtonDown:
			case Sdl.EventType.GamepadButtonUp:
				InputRouter.OnGameControllerButton( (int)e.GamepadButton.Which, (GameControllerCode)e.GamepadButton.Button, e.Type == Sdl.EventType.GamepadButtonDown );
				break;
			case Sdl.EventType.GamepadAxisMotion:
				InputRouter.OnGameControllerAxis( (int)e.GamepadAxis.Which, (GameControllerAxis)e.GamepadAxis.Axis, e.GamepadAxis.Value );
				break;
		}
	}

	internal static ButtonCode MouseButton( byte button ) => button switch
	{
		1 => ButtonCode.MouseLeft,
		2 => ButtonCode.MouseMiddle,
		3 => ButtonCode.MouseRight,
		4 => ButtonCode.MouseBack,
		5 => ButtonCode.MouseForward,
		_ => ButtonCode.BUTTON_CODE_INVALID
	};

	internal static KeyboardModifiers Modifiers( uint mod ) =>
		((mod & 0x0003) != 0 ? KeyboardModifiers.Shift : KeyboardModifiers.None) |
		((mod & 0x00c0) != 0 ? KeyboardModifiers.Ctrl : KeyboardModifiers.None) |
		((mod & 0x0300) != 0 ? KeyboardModifiers.Alt : KeyboardModifiers.None);

	internal static Vector2 WheelDelta( Sdl.MouseWheelEvent wheel ) => new Vector2( wheel.X, wheel.Y ) * (wheel.Direction == 1 ? -1 : 1);
}
