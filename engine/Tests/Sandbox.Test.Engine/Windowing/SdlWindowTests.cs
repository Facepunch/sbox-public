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
	static RenderDeviceInfo_t chainMode;
	static Vector2 pixelSize;
	static bool failCreate, failUpdate, modeAvailable, fullscreen, renderTargetsReady;
	static int updates, fullscreenExits;
	static IntPtr fullscreenMode;
	static readonly List<string> destroyed = new();
	static readonly List<string> creation = new();

	[TestMethod]
	public void DisposeDestroysTheSwapChainBeforeTheWindowAndOnlyOnce() => WithWindow( window =>
	{
		window.CreateSwapChain( "Test", RenderMultisampleType.RENDER_MULTISAMPLE_NONE, false );
		window.Dispose();
		window.Dispose();
		CollectionAssert.AreEqual( new[] { "swapchain", "window" }, destroyed );
		Assert.AreEqual( IntPtr.Zero, window.Handle );
		Assert.AreEqual( default, window.SwapChain );
	} );

	[TestMethod]
	public void FailedSwapChainCreationStillReleasesTheWindow() => WithWindow( window =>
	{
		failCreate = true;
		Assert.ThrowsException<InvalidOperationException>( () => window.CreateSwapChain( "Test", RenderMultisampleType.RENDER_MULTISAMPLE_NONE, false ) );
		window.Dispose();
		CollectionAssert.AreEqual( new[] { "window" }, destroyed );
	} );

	[TestMethod]
	public void ResizePreservesRenderSettingsAndUsesTheActualSwapChainSize() => WithWindow( window =>
	{
		window.CreateSwapChain( "Test", RenderMultisampleType.RENDER_MULTISAMPLE_4X, true, mainWindow: true );
		Assert.IsTrue( window.UpdateSwapChain() );
		Assert.AreEqual( 0, updates );

		pixelSize = new Vector2( 1200, 700 );
		Assert.IsTrue( window.UpdateSwapChain() );
		Assert.AreEqual( 1, updates );
		Assert.AreEqual( RenderMultisampleType.RENDER_MULTISAMPLE_4X, chainMode.m_nMultisampleType );
		Assert.AreEqual( (byte)1, chainMode.m_bWaitForVSync );
		Assert.AreEqual( (byte)1, chainMode.m_bIsMainWindow );
		Assert.AreEqual( ImageFormat.RGBA8888, chainMode.m_DisplayMode.m_Format );

		chainMode.m_DisplayMode.m_nWidth = 1198;
		Assert.AreEqual( new Vector2( 1198, 700 ), window.SwapChainSize );
		pixelSize = default;
		Assert.IsFalse( window.UpdateSwapChain() );
		Assert.AreEqual( 1, updates, "A zero-sized window must not recreate its swap chain." );
	} );

	[TestMethod]
	public void CreationWaitsForQueuedPresentsBeforeCreatingTheSwapChain() => WithWindow( window =>
	{
		window.CreateSwapChain( "Test", RenderMultisampleType.RENDER_MULTISAMPLE_NONE, false );
		CollectionAssert.AreEqual( new[] { "flush", "create" }, creation );
	} );

	[TestMethod]
	public void FailedResizeIsReported() => WithWindow( window =>
	{
		window.CreateSwapChain( "Test", RenderMultisampleType.RENDER_MULTISAMPLE_NONE, false );
		pixelSize *= 2;
		failUpdate = true;
		Assert.ThrowsException<InvalidOperationException>( () => window.UpdateSwapChain() );
	} );

	[TestMethod]
	public void FailedResizeRetriesEvenWhenNativeAlreadyRecordedTheRequestedMode() => WithWindow( window =>
	{
		window.CreateSwapChain( "Test", RenderMultisampleType.RENDER_MULTISAMPLE_NONE, false );
		pixelSize *= 2;
		failUpdate = true;
		Assert.ThrowsException<InvalidOperationException>( () => window.UpdateSwapChain() );
		Assert.AreEqual( (int)pixelSize.x, chainMode.m_DisplayMode.m_nWidth );
		Assert.ThrowsException<InvalidOperationException>( () => window.UpdateSwapChain() );
		Assert.AreEqual( 2, updates );

		failUpdate = false;
		Assert.IsTrue( window.UpdateSwapChain() );
		Assert.AreEqual( 3, updates );
		Assert.IsTrue( window.UpdateSwapChain() );
		Assert.AreEqual( 3, updates, "A successful retry should stop recreating the swapchain." );
	} );

	[TestMethod]
	public void NativeInvalidationRecreatesAnUnchangedMode() => WithWindow( window =>
	{
		window.CreateSwapChain( "Test", RenderMultisampleType.RENDER_MULTISAMPLE_NONE, false );
		renderTargetsReady = false; // Recreation can fail inside native Present, outside this wrapper.
		Assert.IsTrue( window.UpdateSwapChain() );
		Assert.AreEqual( 1, updates );
		Assert.IsTrue( renderTargetsReady );
	} );

	[TestMethod]
	public void NativeRecoveryDoesNotCauseAnotherRecreation() => WithWindow( window =>
	{
		window.CreateSwapChain( "Test", RenderMultisampleType.RENDER_MULTISAMPLE_NONE, false );
		pixelSize *= 2;
		failUpdate = true;
		Assert.ThrowsException<InvalidOperationException>( () => window.UpdateSwapChain() );
		failUpdate = false;
		renderTargetsReady = true; // Native FrameUpdate recovered before the managed retry.
		Assert.IsTrue( window.UpdateSwapChain() );
		Assert.AreEqual( 1, updates );
	} );

	[TestMethod]
	public void DesktopFullscreenDoesNotSelectAnExclusiveMode() => WithWindow( window =>
	{
		modeAvailable = true;
		Assert.IsTrue( window.SetFullscreen( true ) );
		Assert.IsTrue( fullscreen );
		Assert.AreEqual( IntPtr.Zero, fullscreenMode );
		window.CreateSwapChain( "Test", RenderMultisampleType.RENDER_MULTISAMPLE_NONE, false );
		Assert.AreEqual( RenderDisplayModeUsage.CooperativeFullscreen, chainMode.m_nModeUsage );
	} );

	[TestMethod]
	public void FullscreenModeChangesStayFullscreenAndFallbackUsesDesktop() => WithWindow( window =>
	{
		fullscreen = true;
		modeAvailable = true;
		Assert.IsTrue( window.SetFullscreen( true, 1920, 1080 ) );
		Assert.AreNotEqual( IntPtr.Zero, fullscreenMode );
		window.CreateSwapChain( "Test", RenderMultisampleType.RENDER_MULTISAMPLE_NONE, false );
		Assert.AreEqual( RenderDisplayModeUsage.ExclusiveFullscreen, chainMode.m_nModeUsage );

		modeAvailable = false;
		Assert.IsTrue( window.SetFullscreen( true, 3840, 2160 ) );
		Assert.AreEqual( IntPtr.Zero, fullscreenMode );
		Assert.IsTrue( window.UpdateSwapChain() );
		Assert.AreEqual( RenderDisplayModeUsage.CooperativeFullscreen, chainMode.m_nModeUsage );
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
		var flush = g_pRenderDevice.__N.g_pRenderDevice_Flush;
		var create = g_pRenderDevice.__N.g_pRenderDevice_CreateSwapChain;
		var info = g_pRenderDevice.__N.g_pRenderDevice_GetSwapChainInfo;
		var update = g_pRenderDevice.__N.g_pRenderDevice_UpdateSwapChain;
		var canRender = g_pRenderDevice.__N.g_pRenderDevice_CanRenderToSwapChain;
		var destroyChain = g_pRenderDevice.__N.g_pRenderDevice_DestroySwapChain;
		var multisample = RenderDeviceManager.__N.Glue_RndrDvcMngr_GetBestMultisampleType;
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
			g_pRenderDevice.__N.g_pRenderDevice_Flush = &Flush;
			g_pRenderDevice.__N.g_pRenderDevice_CreateSwapChain = &Create;
			g_pRenderDevice.__N.g_pRenderDevice_GetSwapChainInfo = &GetInfo;
			g_pRenderDevice.__N.g_pRenderDevice_UpdateSwapChain = &Update;
			g_pRenderDevice.__N.g_pRenderDevice_CanRenderToSwapChain = &CanRender;
			g_pRenderDevice.__N.g_pRenderDevice_DestroySwapChain = &DestroyChain;
			RenderDeviceManager.__N.Glue_RndrDvcMngr_GetBestMultisampleType = &GetMultisample;

			chainMode = default;
			pixelSize = new Vector2( 800, 600 );
			failCreate = failUpdate = fullscreen = modeAvailable = renderTargetsReady = false;
			updates = fullscreenExits = 0;
			fullscreenMode = IntPtr.Zero;
			destroyed.Clear();
			creation.Clear();
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
			g_pRenderDevice.__N.g_pRenderDevice_Flush = flush;
			g_pRenderDevice.__N.g_pRenderDevice_CreateSwapChain = create;
			g_pRenderDevice.__N.g_pRenderDevice_GetSwapChainInfo = info;
			g_pRenderDevice.__N.g_pRenderDevice_UpdateSwapChain = update;
			g_pRenderDevice.__N.g_pRenderDevice_CanRenderToSwapChain = canRender;
			g_pRenderDevice.__N.g_pRenderDevice_DestroySwapChain = destroyChain;
			RenderDeviceManager.__N.Glue_RndrDvcMngr_GetBestMultisampleType = multisample;
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
	[UnmanagedCallersOnly]
	static void Flush() => creation.Add( "flush" );
	[UnmanagedCallersOnly]
	static IntPtr Create( IntPtr window, RenderDeviceInfo_t* mode, IntPtr name ) { creation.Add( "create" ); chainMode = *mode; renderTargetsReady = !failCreate; return failCreate ? IntPtr.Zero : (IntPtr)2; }
	[UnmanagedCallersOnly]
	static RenderDeviceInfo_t GetInfo( IntPtr chain ) => chainMode;
	[UnmanagedCallersOnly]
	static int Update( IntPtr chain, RenderDeviceInfo_t* mode ) { updates++; chainMode = *mode; renderTargetsReady = !failUpdate; return failUpdate ? 0 : 1; }
	[UnmanagedCallersOnly]
	static int CanRender( IntPtr chain ) => renderTargetsReady ? 1 : 0;
	[UnmanagedCallersOnly]
	static void DestroyChain( IntPtr chain ) => destroyed.Add( "swapchain" );
	[UnmanagedCallersOnly]
	static long GetMultisample( long value ) => value;
}
