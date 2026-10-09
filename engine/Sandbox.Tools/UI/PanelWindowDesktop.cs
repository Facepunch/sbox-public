using NativeEngine;
using Sandbox.UI;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Editor;

/// <summary>
/// Main-thread desktop access for panel window docking drags.
/// </summary>
internal static class PanelWindowDesktop
{
	const uint NextWindow = 2; // GW_HWNDNEXT
	static readonly HashSet<string> _reportedFailures = new();

	/// <summary>
	/// Reads the desktop pointer and SDL button mask. The left button is bit 0 (mask 1).
	/// </summary>
	internal static uint GetPointer( out Vector2 position )
	{
		var buttons = Sdl.GetGlobalMouseState( out var x, out var y );
		position = new Vector2( x, y );
		return buttons;
	}

	/// <summary>
	/// Captures the mouse to SDL's focused window, or releases it. Unsupported capture returns false.
	/// </summary>
	internal static bool Capture( bool capture )
	{
		return Check( Sdl.CaptureMouse( capture ), nameof( Sdl.CaptureMouse ) );
	}

	/// <summary>Owned tool windows stay above their dock window and follow its visibility.</summary>
	internal static void SetOwner( PanelWindow window, PanelWindow owner )
	{
		if ( !Sdl.SetWindowParent( window.Handle, owner.Handle ) )
			throw new InvalidOperationException( $"Could not set dock window owner: {Sdl.GetError()}" );
	}

	/// <summary>
	/// Converts surface pixels to desktop coordinates through SDL's window coordinate space.
	/// </summary>
	internal static Vector2 ToDesktop( PanelWindow window, Vector2 surfacePoint )
	{
		var point = window.PixelsToWindow( surfacePoint );
		var x = (int)point.x;
		var y = (int)point.y;
		EngineGlobal.Plat_WindowToScreenCoords( window.Handle, ref x, ref y );
		return new Vector2( x, y );
	}

	/// <summary>
	/// Converts desktop coordinates to surface pixels, including the window's pixel density.
	/// </summary>
	internal static Vector2 ToSurface( PanelWindow window, Vector2 desktopPoint )
	{
		var x = (int)desktopPoint.x;
		var y = (int)desktopPoint.y;
		EngineGlobal.Plat_ScreenToWindowCoords( window.Handle, ref x, ref y );
		return ((IPanelWindow)window).ToSurface( new Vector2( x, y ) );
	}

	/// <summary>
	/// Finds the exposed managed client area, ignoring the drag window and input-transparent tooltips.
	/// On X11 and macOS, only the current SDL mouse focus is supported. Capture and unsupported backends return null.
	/// </summary>
	internal static PanelWindow WindowAt( Vector2 desktopPoint, PanelWindow excluded )
	{
		PanelWindow target = null;

		if ( OperatingSystem.IsWindows() )
		{
			// Windows can disappear or change z-order during traversal. Never revisit a handle.
			var visited = new HashSet<IntPtr>();
			for ( var handle = GetTopWindow( IntPtr.Zero ); handle != IntPtr.Zero && visited.Add( handle ); handle = GetWindow( handle, NextWindow ) )
			{
				if ( !IsWindowVisible( handle ) || IsIconic( handle ) ) continue;
				if ( !GetWindowRect( handle, out var bounds ) ) return null;
				if ( desktopPoint.x < bounds.Left || desktopPoint.x >= bounds.Right || desktopPoint.y < bounds.Top || desktopPoint.y >= bounds.Bottom ) continue;

				foreach ( var window in PanelWindow.All )
				{
					if ( !window.IsOpen || window.Handle == IntPtr.Zero ) continue;
					var properties = Sdl.GetWindowProperties( window.Handle );
					if ( !Check( properties != 0, nameof( Sdl.GetWindowProperties ) ) ) return null;
					var nativeHandle = Sdl.GetPointerProperty( properties, "SDL.window.win32.hwnd", IntPtr.Zero );
					if ( nativeHandle != handle ) continue;

					target = window;
					break;
				}

				// An unrelated application's window blocks everything beneath it.
				if ( target is null ) return null;
				if ( target != excluded && !target.IgnoresInput ) break;
				target = null;
			}
		}
		else
		{
			// Wayland and other backends need a platform hit-test API, not guessed desktop coordinates.
			var driver = Sdl.GetCurrentVideoDriver();
			if ( driver != "x11" && driver != "cocoa" ) return null;

			var focus = Sdl.GetMouseFocus();
			if ( focus == IntPtr.Zero || (Sdl.GetWindowFlags( focus ) & Sdl.WindowFlags.MouseCapture) != 0 ) return null;

			// SDL mouse focus isn't a desktop hit-test API. Capture reports the source, and
			// there is no reliable way here to find a window underneath the drag preview.
			GetPointer( out var pointer );
			if ( MathF.Abs( pointer.x - desktopPoint.x ) >= 1 || MathF.Abs( pointer.y - desktopPoint.y ) >= 1 ) return null;

			foreach ( var window in PanelWindow.All )
			{
				if ( window.Handle != focus ) continue;
				if ( !window.IsOpen || window == excluded || window.IgnoresInput ) return null;
				if ( !window.IsVisible || window.IsMinimized ) return null;
				target = window;
				break;
			}
		}

		if ( target is null ) return null;

		// A title bar or frame blocks lower windows, but isn't a docking surface.
		var point = ToSurface( target, desktopPoint );
		var size = target.PixelSize;
		return point.x >= 0 && point.y >= 0 && point.x < size.x && point.y < size.y ? target : null;
	}

	static bool Check( bool success, string operation )
	{
		if ( !success && _reportedFailures.Add( operation ) )
			Log.Warning( $"Panel window docking: {operation} failed: {Sdl.GetError()}" );

		return success;
	}

	[DllImport( "user32.dll" )]
	static extern IntPtr GetTopWindow( IntPtr window );

	[DllImport( "user32.dll" )]
	static extern IntPtr GetWindow( IntPtr window, uint command );

	[DllImport( "user32.dll" )]
	[return: MarshalAs( UnmanagedType.Bool )]
	static extern bool IsWindowVisible( IntPtr window );

	[DllImport( "user32.dll" )]
	[return: MarshalAs( UnmanagedType.Bool )]
	static extern bool IsIconic( IntPtr window );

	[DllImport( "user32.dll" )]
	[return: MarshalAs( UnmanagedType.Bool )]
	static extern bool GetWindowRect( IntPtr window, out NativeRect rect );

	[StructLayout( LayoutKind.Sequential )]
	struct NativeRect
	{
		internal int Left;
		internal int Top;
		internal int Right;
		internal int Bottom;
	}
}
