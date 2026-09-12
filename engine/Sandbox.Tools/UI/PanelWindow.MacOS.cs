using System;
using System.Runtime.InteropServices;

namespace Editor;

public partial class PanelWindow
{
	/// <summary>
	/// AppKit zooms a window in the first time it's ordered on screen, and setting that animation
	/// up is the most expensive thing about showing one: it creates a display link, which on first
	/// use asks the window server for the whole display mode list - 150 to 250 ms on the main thread
	/// before the window is even visible. We draw our own frames, we don't want the zoom, and a
	/// window that appears at once beats one that arrives late with a flourish.
	/// </summary>
	static class MacOS
	{
		const nint NSWindowAnimationBehaviorNone = 2;

		[DllImport( "libSDL3.0" )] static extern uint SDL_GetWindowProperties( IntPtr window );
		[DllImport( "libSDL3.0" )] static extern IntPtr SDL_GetPointerProperty( uint props, string name, IntPtr defaultValue );
		[DllImport( "/usr/lib/libobjc.A.dylib" )] static extern IntPtr sel_registerName( string name );
		[DllImport( "/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend" )] static extern void objc_msgSend_nint( IntPtr self, IntPtr selector, nint arg );

		static bool warned;

		/// <summary>
		/// Turn off the show animation on the NSWindow behind an SDL window.
		/// </summary>
		public static void DisableShowAnimation( IntPtr sdlWindow )
		{
			try
			{
				var props = SDL_GetWindowProperties( sdlWindow );
				var nsWindow = props == 0 ? IntPtr.Zero : SDL_GetPointerProperty( props, "SDL.window.cocoa.window", IntPtr.Zero );

				if ( nsWindow == IntPtr.Zero )
				{
					if ( !warned ) Log.Warning( "PanelWindow: couldn't find the NSWindow behind the SDL window - keeping the show animation" );
					warned = true;
					return;
				}

				objc_msgSend_nint( nsWindow, sel_registerName( "setAnimationBehavior:" ), NSWindowAnimationBehaviorNone );
			}
			catch ( Exception e )
			{
				if ( !warned ) Log.Warning( e, "PanelWindow: couldn't turn off the show animation" );
				warned = true;
			}
		}
	}
}
