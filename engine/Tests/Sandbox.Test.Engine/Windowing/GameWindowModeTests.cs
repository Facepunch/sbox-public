using NativeEngine;
using Sandbox.Engine;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EngineTests;

[TestClass, DoNotParallelize]
public unsafe class GameWindowModeTests
{
	static bool fullscreen, waiting, canComplete;
	static int exitRequests, boundsQueries;
	static Vector2 size, position;
	static IntPtr displayMode;

	[TestMethod]
	public void FullscreenExitTimeoutDefersSizingUntilTheDesktopIsRestored()
	{
		var sync = Sdl.__N.globalSdl_SyncWindow;
		var error = Sdl.__N.globalSdl_GetError;
		var flags = Sdl.__N.globalSdl_GetWindowFlags;
		var setFullscreen = Sdl.__N.globalSdl_SetWindowFullscreen;
		var display = Sdl.__N.globalSdl_GetDisplayForWindow;
		var bounds = Sdl.__N.globalSdl_GetDisplayUsableBounds;
		var fullscreenMode = Sdl.__N.globalSdl_SetWindowFullscreenMode;
		var bordered = Sdl.__N.globalSdl_SetWindowBordered;
		var resizable = Sdl.__N.globalSdl_SetWindowResizable;
		var setSize = Sdl.__N.globalSdl_SetWindowSize;
		var setPosition = Sdl.__N.globalSdl_SetWindowPosition;
		var destroy = Sdl.__N.globalSdl_DestroyWindow;
		var wasMainThread = ThreadSafe.IsMainThread;
		ThreadSafe.MarkMainThread();

		try
		{
			Sdl.__N.globalSdl_SyncWindow = &Sync;
			Sdl.__N.globalSdl_GetError = &GetError;
			Sdl.__N.globalSdl_GetWindowFlags = &GetFlags;
			Sdl.__N.globalSdl_SetWindowFullscreen = &SetFullscreen;
			Sdl.__N.globalSdl_GetDisplayForWindow = &GetDisplay;
			Sdl.__N.globalSdl_GetDisplayUsableBounds = (delegate* unmanaged< uint, out Sdl.Rect, int >)(delegate* unmanaged< uint, Sdl.Rect*, int >)&GetBounds;
			Sdl.__N.globalSdl_SetWindowFullscreenMode = &SetFullscreenMode;
			Sdl.__N.globalSdl_SetWindowBordered = &SetFlag;
			Sdl.__N.globalSdl_SetWindowResizable = &SetFlag;
			Sdl.__N.globalSdl_SetWindowSize = &SetSize;
			Sdl.__N.globalSdl_SetWindowPosition = &SetPosition;
			Sdl.__N.globalSdl_DestroyWindow = &Destroy;

			fullscreen = true;
			waiting = canComplete = false;
			exitRequests = boundsQueries = 0;
			size = position = default;
			// Exercise the real mode application without creating an OS window or GPU resources.
			var window = (GameWindow)RuntimeHelpers.GetUninitializedObject( typeof( GameWindow ) );
			using var sdlWindow = new SdlWindow( (IntPtr)1 );
			typeof( GameWindow ).GetField( "window", BindingFlags.NonPublic | BindingFlags.Instance ).SetValue( window, sdlWindow );
			var apply = typeof( GameWindow ).GetMethod( "ApplyWindowMode", BindingFlags.NonPublic | BindingFlags.Instance )
				.CreateDelegate<Func<GameWindow.WindowMode, bool>>( window );
			var mode = new GameWindow.WindowMode( false, false, 3840, 2160 );

			Assert.IsFalse( apply( mode ) );
			Assert.AreEqual( 1, exitRequests );
			Assert.AreEqual( 0, boundsQueries, "The old exclusive bounds must not be used after a timeout." );

			Assert.IsFalse( apply( mode ) );
			Assert.AreEqual( 1, exitRequests, "A pending transition must finish before another request is issued." );
			Assert.AreEqual( 0, boundsQueries );

			canComplete = true;
			Assert.IsTrue( apply( mode ) );
			Assert.AreEqual( new Vector2( 2560, 1440 ), size );
			Assert.AreEqual( new Vector2( 100, 50 ), position );
			Assert.AreEqual( 1, boundsQueries );
			Assert.IsTrue( apply( mode ) );
			Assert.AreEqual( 1, boundsQueries, "Only a completed mode should be cached." );
			window.InvalidateWindowMode();
			Assert.IsTrue( apply( mode ) );
			Assert.AreEqual( 2, boundsQueries, "A user resize must allow the same mode to be applied again." );

			if ( OperatingSystem.IsLinux() )
			{
				foreach ( var requested in new[]
				{
					new GameWindow.WindowMode( false, true, 1280, 720 ),
					new GameWindow.WindowMode( true, false, 1280, 720 ),
					new GameWindow.WindowMode( true, true, 1280, 720 )
				} )
				{
					var previousBoundsQueries = boundsQueries;
					Assert.IsTrue( apply( requested ) );
					Assert.IsTrue( fullscreen );
					Assert.AreEqual( IntPtr.Zero, displayMode, "Linux must use desktop fullscreen." );
					Assert.AreEqual( previousBoundsQueries, boundsQueries, "Desktop fullscreen must not resize an ordinary window." );
					Assert.IsTrue( apply( mode ), "Returning to a bordered window must still work." );
					Assert.IsFalse( fullscreen );
				}
			}
		}
		finally
		{
			Sdl.__N.globalSdl_SyncWindow = sync;
			Sdl.__N.globalSdl_GetError = error;
			Sdl.__N.globalSdl_GetWindowFlags = flags;
			Sdl.__N.globalSdl_SetWindowFullscreen = setFullscreen;
			Sdl.__N.globalSdl_GetDisplayForWindow = display;
			Sdl.__N.globalSdl_GetDisplayUsableBounds = bounds;
			Sdl.__N.globalSdl_SetWindowFullscreenMode = fullscreenMode;
			Sdl.__N.globalSdl_SetWindowBordered = bordered;
			Sdl.__N.globalSdl_SetWindowResizable = resizable;
			Sdl.__N.globalSdl_SetWindowSize = setSize;
			Sdl.__N.globalSdl_SetWindowPosition = setPosition;
			Sdl.__N.globalSdl_DestroyWindow = destroy;
			typeof( ThreadSafe ).GetField( "isMainThread", BindingFlags.NonPublic | BindingFlags.Static ).SetValue( null, wasMainThread );
		}
	}

	[UnmanagedCallersOnly]
	static int Sync( IntPtr window )
	{
		if ( !waiting ) return 1;
		if ( !canComplete ) return 0;
		waiting = fullscreen = false;
		return 1;
	}

	[UnmanagedCallersOnly]
	static void Destroy( IntPtr window ) { }
	[UnmanagedCallersOnly]
	static IntPtr GetError() => IntPtr.Zero;
	[UnmanagedCallersOnly]
	static long GetFlags( IntPtr window ) => fullscreen ? (long)Sdl.WindowFlags.Fullscreen : 0;
	[UnmanagedCallersOnly]
	static int SetFullscreen( IntPtr window, int value )
	{
		if ( value != 0 )
		{
			fullscreen = true;
			return 1;
		}
		exitRequests++;
		waiting = true;
		return 1;
	}
	[UnmanagedCallersOnly]
	static uint GetDisplay( IntPtr window ) => 1;
	[UnmanagedCallersOnly]
	static int GetBounds( uint display, Sdl.Rect* bounds )
	{
		boundsQueries++;
		*bounds = new Sdl.Rect { X = 100, Y = 50, Width = fullscreen ? 1280 : 2560, Height = fullscreen ? 720 : 1440 };
		return 1;
	}
	[UnmanagedCallersOnly]
	static int SetFullscreenMode( IntPtr window, IntPtr mode ) { displayMode = mode; return 1; }
	[UnmanagedCallersOnly]
	static int SetFlag( IntPtr window, int value ) => 1;
	[UnmanagedCallersOnly]
	static int SetSize( IntPtr window, int width, int height )
	{
		size = new Vector2( width, height );
		return 1;
	}
	[UnmanagedCallersOnly]
	static int SetPosition( IntPtr window, int x, int y )
	{
		position = new Vector2( x, y );
		return 1;
	}
}
