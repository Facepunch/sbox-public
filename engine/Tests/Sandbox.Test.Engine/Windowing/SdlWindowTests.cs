using NativeEngine;
using Sandbox.Engine;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace EngineTests;

[TestClass, DoNotParallelize]
public unsafe class SdlWindowTests
{
	static Vector2 pixelSize;
	static bool modeAvailable, fullscreen;
	static int fullscreenExits;
	static IntPtr fullscreenMode;
	static readonly List<string> destroyed = new();

	[TestMethod]
	public void DisposeDestroysTheWindowOnlyOnce() => WithWindow( window =>
	{
		window.Dispose();
		window.Dispose();
		CollectionAssert.AreEqual( new[] { "window" }, destroyed );
		Assert.AreEqual( IntPtr.Zero, window.Handle );
	} );

	[TestMethod]
	public void DesktopFullscreenDoesNotSelectAnExclusiveMode() => WithWindow( window =>
	{
		modeAvailable = true;
		Assert.IsTrue( window.SetFullscreen( true ) );
		Assert.IsTrue( fullscreen );
		Assert.AreEqual( IntPtr.Zero, fullscreenMode );
	} );

	[TestMethod]
	public void FullscreenModeChangesStayFullscreenAndFallbackUsesDesktop() => WithWindow( window =>
	{
		fullscreen = true;
		modeAvailable = true;
		Assert.IsTrue( window.SetFullscreen( true, 1920, 1080 ) );
		Assert.AreNotEqual( IntPtr.Zero, fullscreenMode );

		modeAvailable = false;
		Assert.IsTrue( window.SetFullscreen( true, 3840, 2160 ) );
		Assert.AreEqual( IntPtr.Zero, fullscreenMode );
		Assert.AreEqual( 0, fullscreenExits );
	} );

	static void WithWindow( Action<SdlWindow> test )
	{
		var getSize = Sdl.__N.globalSdl_GetWindowSizeInPixels;
		var flags = Sdl.__N.globalSdl_GetWindowFlags;
		var destroyWindow = Sdl.__N.globalSdl_DestroyWindow;
		var sync = Sdl.__N.globalSdl_SyncWindow;
		var getDisplay = Sdl.__N.globalSdl_GetDisplayForWindow;
		var closest = Sdl.__N.globalSdl_GetClosestFullscreenDisplayMode;
		var setMode = Sdl.__N.globalSdl_SetWindowFullscreenMode;
		var setFullscreen = Sdl.__N.globalSdl_SetWindowFullscreen;
		var wasMainThread = ThreadSafe.IsMainThread;
		ThreadSafe.MarkMainThread();
		try
		{
			Sdl.__N.globalSdl_GetWindowSizeInPixels = (delegate* unmanaged< IntPtr, out int, out int, int >)(delegate* unmanaged< IntPtr, int*, int*, int >)&GetSize;
			Sdl.__N.globalSdl_GetWindowFlags = &GetFlags;
			Sdl.__N.globalSdl_DestroyWindow = &DestroyWindow;
			Sdl.__N.globalSdl_SyncWindow = &Sync;
			Sdl.__N.globalSdl_GetDisplayForWindow = &GetDisplay;
			Sdl.__N.globalSdl_GetClosestFullscreenDisplayMode = (delegate* unmanaged< uint, int, int, float, int, out Sdl.DisplayMode, int >)(delegate* unmanaged< uint, int, int, float, int, Sdl.DisplayMode*, int >)&GetClosest;
			Sdl.__N.globalSdl_SetWindowFullscreenMode = &SetMode;
			Sdl.__N.globalSdl_SetWindowFullscreen = &SetFullscreen;

			pixelSize = new Vector2( 800, 600 );
			fullscreen = modeAvailable = false;
			fullscreenExits = 0;
			fullscreenMode = IntPtr.Zero;
			destroyed.Clear();
			using var window = new SdlWindow( (IntPtr)1 );
			test( window );
		}
		finally
		{
			Sdl.__N.globalSdl_GetWindowSizeInPixels = getSize;
			Sdl.__N.globalSdl_GetWindowFlags = flags;
			Sdl.__N.globalSdl_DestroyWindow = destroyWindow;
			Sdl.__N.globalSdl_SyncWindow = sync;
			Sdl.__N.globalSdl_GetDisplayForWindow = getDisplay;
			Sdl.__N.globalSdl_GetClosestFullscreenDisplayMode = closest;
			Sdl.__N.globalSdl_SetWindowFullscreenMode = setMode;
			Sdl.__N.globalSdl_SetWindowFullscreen = setFullscreen;
			typeof( ThreadSafe ).GetField( "isMainThread", BindingFlags.NonPublic | BindingFlags.Static ).SetValue( null, wasMainThread );
		}
	}

	[UnmanagedCallersOnly]
	static int GetSize( IntPtr window, int* width, int* height ) { *width = (int)pixelSize.x; *height = (int)pixelSize.y; return 1; }
	[UnmanagedCallersOnly]
	static long GetFlags( IntPtr window ) => fullscreen ? (long)Sdl.WindowFlags.Fullscreen : 0;
	[UnmanagedCallersOnly]
	static void DestroyWindow( IntPtr window ) => destroyed.Add( "window" );
	[UnmanagedCallersOnly]
	static int Sync( IntPtr window ) => 1;
	[UnmanagedCallersOnly]
	static uint GetDisplay( IntPtr window ) => 1;
	[UnmanagedCallersOnly]
	static int GetClosest( uint display, int width, int height, float refresh, int highDensity, Sdl.DisplayMode* mode )
	{
		*mode = new Sdl.DisplayMode { Width = width, Height = height, RefreshRate = refresh };
		return modeAvailable ? 1 : 0;
	}
	[UnmanagedCallersOnly]
	static int SetMode( IntPtr window, IntPtr mode ) { fullscreenMode = mode; return 1; }
	[UnmanagedCallersOnly]
	static int SetFullscreen( IntPtr window, int value ) { fullscreen = value != 0; if ( !fullscreen ) fullscreenExits++; return 1; }
}
