using NativeEngine;
using Sandbox.Engine;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox.UI;

internal static partial class PanelWindowInput
{
	// Keep handles until native destruction, even after the panel leaves PanelWindows.All.
	// Events queued while disposal waits for frame end must not fall through to game input.
	static readonly HashSet<IntPtr> nativeWindows = new();
	static Vector2 pinnedPosition;

	/// <summary>
	/// Install panel input hooks on the main thread. Pair with DetachWindow before destroying the SDL window.
	/// </summary>
	internal static unsafe void SetupWindow( IntPtr window, bool customChrome = false, bool dropTarget = false )
	{
		if ( nativeWindows.Add( window ) )
		{
			if ( nativeWindows.Count == 1 )
				Sdl.AddEventWatch( (IntPtr)(delegate* unmanaged[Cdecl]< IntPtr, Sdl.Event*, byte >)&WatchEvent, IntPtr.Zero );
		}

		Sdl.StartTextInput( window );
		Sdl.SetHint( "SDL_MOUSE_FOCUS_CLICKTHROUGH", "1" );
		if ( customChrome )
			Sdl.SetWindowHitTest( window, (IntPtr)(delegate* unmanaged[Cdecl]< IntPtr, Sdl.Point*, IntPtr, int >)&HitTest, IntPtr.Zero );

		if ( dropTarget )
		{
			var callbacks = new Sdl.ExternalDropCallbacks
			{
				OnEnter = (IntPtr)(delegate* unmanaged[Cdecl]< IntPtr, IntPtr, IntPtr, void >)&DragEnter,
				OnOver = (IntPtr)(delegate* unmanaged[Cdecl]< IntPtr, float, float, int >)&DragOver,
				OnLeave = (IntPtr)(delegate* unmanaged[Cdecl]< IntPtr, void >)&DragLeave,
				OnDrop = (IntPtr)(delegate* unmanaged[Cdecl]< IntPtr, float, float, int >)&DragDrop,
				OnDragFrame = (IntPtr)(delegate* unmanaged[Cdecl]< void >)&DragFrame
			};
			Sdl.SetWindowExternalDropTarget( window, (IntPtr)(&callbacks) );
		}
	}

	/// <summary>
	/// Remove panel input hooks on the main thread before destroying the SDL window.
	/// </summary>
	internal static unsafe void DetachWindow( IntPtr window )
	{
		_composing.Remove( window );
		Sdl.SetWindowExternalDropTarget( window, IntPtr.Zero );
		if ( nativeWindows.Remove( window ) )
		{
			if ( nativeWindows.Count == 0 )
				Sdl.RemoveEventWatch( (IntPtr)(delegate* unmanaged[Cdecl]< IntPtr, Sdl.Event*, byte >)&WatchEvent, IntPtr.Zero );
		}
	}

	/// <summary>
	/// Release capture before a closing window clears its handle, even if SDL rejects the request.
	/// </summary>
	internal static void OnWindowClosed( IPanelWindow window )
	{
		if ( PanelWindows.CaptureWindow != window ) return;
		SetRelativeMouse( window, false );
		PanelWindows.CaptureWindow = null;
		PanelWindows.CaptureDelta = 0;
		PanelWindows.SkipNextCaptureDelta = false;
	}

	/// <summary>
	/// Change panel capture and its pinned cursor together, publishing only the state accepted by SDL.
	/// </summary>
	internal static void SetRelativeMouse( IPanelWindow window, bool relative )
	{
		if ( relative && PanelWindows.CaptureWindow is { } previous && previous != window )
		{
			SetRelativeMouse( previous, false );
			if ( PanelWindows.CaptureWindow is not null ) return;
		}

		var captured = PanelWindows.CaptureWindow == window;
		var position = pinnedPosition;
		if ( relative && !captured )
		{
			Sdl.GetMouseState( out var x, out var y );
			position = new Vector2( x, y );
		}
		var actual = SdlWindow.SetRelativeMouseMode( window.Handle, relative );
		if ( actual == captured ) return;

		if ( actual )
		{
			pinnedPosition = position;
			PanelWindows.CaptureWindow = window;
		}
		else
		{
			Sdl.WarpMouseInWindow( window.Handle, pinnedPosition.x, pinnedPosition.y );
			PanelWindows.CaptureWindow = null;
		}
		PanelWindows.SkipNextCaptureDelta = actual;
		PanelWindows.CaptureDelta = 0;
	}

	// Only marked panel windows reach here. SDL owns any strings in the event until the next poll.
	internal static void OnEvent( IntPtr window, in Sdl.Event e )
	{
		switch ( e.Type )
		{
			case Sdl.EventType.MouseMotion:
				OnMouseMove( window, e.Motion.X, e.Motion.Y, e.Motion.XRel, e.Motion.YRel );
				if ( PanelWindows.CaptureWindow?.Handle == window ) Sdl.WarpMouseInWindow( window, pinnedPosition.x, pinnedPosition.y );
				break;
			case Sdl.EventType.MouseButtonDown:
			case Sdl.EventType.MouseButtonUp:
				var button = SdlEvents.MouseButton( e.Button.Button );
				if ( button != ButtonCode.BUTTON_CODE_INVALID ) OnMouseButton( window, button, e.Type == Sdl.EventType.MouseButtonDown, e.Button.Clicks, SdlEvents.Modifiers( Sdl.GetModState() ) );
				break;
			case Sdl.EventType.MouseWheel:
				var wheel = SdlEvents.WheelDelta( e.Wheel );
				if ( wheel != Vector2.Zero ) OnMouseWheel( window, wheel.x, wheel.y, SdlEvents.Modifiers( Sdl.GetModState() ) );
				break;
			case Sdl.EventType.KeyDown:
			case Sdl.EventType.KeyUp:
				OnKey( window, Sandbox.Engine.KeyTranslation.ScanCodeToButtonCode( e.Key.Scancode ), e.Type == Sdl.EventType.KeyDown, SdlEvents.Modifiers( e.Key.Mod ) );
				break;
			case Sdl.EventType.TextInput: OnText( window, Marshal.PtrToStringUTF8( e.Text.Text ) ); break;
			case Sdl.EventType.TextEditing: OnImeComposition( window, Marshal.PtrToStringUTF8( e.Edit.Text ) ); break;
			case Sdl.EventType.WindowMoved: OnMoved( window ); break;
			case Sdl.EventType.WindowDisplayChanged:
			case Sdl.EventType.WindowDisplayScaleChanged: OnDisplayChanged( window ); break;
			case Sdl.EventType.WindowMinimized: OnStateChanged( window, 1 ); break;
			case Sdl.EventType.WindowMaximized: OnStateChanged( window, 2 ); break;
			case Sdl.EventType.WindowRestored: OnStateChanged( window, 0 ); break;
			case Sdl.EventType.WindowCloseRequested: OnClose( window ); break;
			case Sdl.EventType.WindowFocusGained: OnFocus( window, true ); break;
			case Sdl.EventType.WindowFocusLost: OnFocus( window, false ); break;
			case Sdl.EventType.WindowMouseLeave: OnMouseLeave( window ); break;
			case Sdl.EventType.DropFile: OnDropFile( window, Marshal.PtrToStringUTF8( e.Drop.Data ) ); break;
			case Sdl.EventType.DropText: OnDropText( window, Marshal.PtrToStringUTF8( e.Drop.Data ) ); break;
			case Sdl.EventType.DropComplete: OnDropComplete( window, e.Drop.X, e.Drop.Y ); break;
				// Resize/expose events were already handled by the watch, inside SDL's modal loop.
		}
	}

	internal static bool IsPanelWindow( IntPtr window ) => nativeWindows.Contains( window );

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static unsafe byte WatchEvent( IntPtr userdata, Sdl.Event* e )
	{
		try
		{
			if ( e->Type is not (Sdl.EventType.WindowExposed or Sdl.EventType.WindowResized or Sdl.EventType.WindowPixelSizeChanged) ) return 1;
			// SDL watches can run on other threads. Rendering and the window registry belong to the main thread.
			var windowEvent = *e;
			MainThread.Queue( () =>
			{
				var copy = windowEvent;
				var window = Sdl.GetWindowFromEvent( (IntPtr)(&copy) );
				if ( window == IntPtr.Zero ) return;
				if ( PanelWindows.Find( window ) is null ) return;
				// Modal resize loops park the normal frame update, including scratch-target expiry.
				CSceneSystem.FrameUpdate();
				OnResized( window );
			} );
		}
		catch ( Exception e2 ) { Log.Warning( e2, "Panel window resize callback failed" ); }
		return 1;
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static unsafe int HitTest( IntPtr window, Sdl.Point* point, IntPtr userdata )
	{
		try { return OnHitTest( window, point->X, point->Y ); }
		catch ( Exception e ) { Log.Warning( e, "Panel window hit test failed" ); return 0; }
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static void DragEnter( IntPtr window, IntPtr files, IntPtr text )
	{
		try { OnDragEnter( window, Marshal.PtrToStringUTF8( files ), Marshal.PtrToStringUTF8( text ) ); }
		catch ( Exception e ) { Log.Warning( e, "Panel window drag enter failed" ); }
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static int DragOver( IntPtr window, float x, float y )
	{
		try { return OnDragOver( window, x, y ); }
		catch ( Exception e ) { Log.Warning( e, "Panel window drag over failed" ); return 0; }
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static void DragLeave( IntPtr window )
	{
		try { OnDragLeave( window ); }
		catch ( Exception e ) { Log.Warning( e, "Panel window drag leave failed" ); }
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static int DragDrop( IntPtr window, float x, float y )
	{
		try { return OnDragDrop( window, x, y ); }
		catch ( Exception e ) { Log.Warning( e, "Panel window drop failed" ); return 0; }
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static void DragFrame()
	{
		try { CSceneSystem.FrameUpdate(); OnDragFrame(); }
		catch ( Exception e ) { Log.Warning( e, "Panel window drag frame failed" ); }
	}
}
