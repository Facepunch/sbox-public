using NativeEngine;
using Sandbox.UI;
using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Reflection;

namespace UITests.Controls;

[TestClass, DoNotParallelize]
public unsafe class PanelWindowSdlTests
{
	FakePanelWindow window;
	delegate* unmanaged< IntPtr, float, float, void > savedWarp;
	static IntPtr dialogCallback, dialogContext;

	[TestMethod]
	public void OverlappingDialogsDoNotShareASelection()
	{
		var saved = Sdl.__N.globalSdl_ShowOpenFolderDialogAt;
		var wasMainThread = ThreadSafe.IsMainThread;
		ThreadSafe.MarkMainThread();
		try
		{
			Sdl.__N.globalSdl_ShowOpenFolderDialogAt = &OpenFolder;
			var first = PanelWindowDialogs.PickFolder( IntPtr.Zero, null );
			var second = PanelWindowDialogs.PickSaveFile( IntPtr.Zero, null, null );
			Assert.IsTrue( second.IsCompletedSuccessfully );
			Assert.IsNull( second.Result );
			Assert.IsFalse( first.IsCompleted, "The original dialog must remain pending." );
		}
		finally
		{
			if ( dialogCallback != IntPtr.Zero )
			{
				((delegate* unmanaged[Cdecl]< IntPtr, IntPtr, int, void >)dialogCallback)( dialogContext, IntPtr.Zero, -1 );
				MainThread.RunQueues();
				dialogCallback = IntPtr.Zero;
			}
			Sdl.__N.globalSdl_ShowOpenFolderDialogAt = saved;
			typeof( ThreadSafe ).GetField( "isMainThread", BindingFlags.NonPublic | BindingFlags.Static ).SetValue( null, wasMainThread );
		}
	}

	[UnmanagedCallersOnly]
	static void OpenFolder( IntPtr callback, IntPtr context, IntPtr window, IntPtr path )
	{
		dialogCallback = callback;
		dialogContext = context;
	}

	[TestInitialize]
	public void Setup()
	{
		savedWarp = Sdl.__N.globalSdl_WarpMouseInWindow;
		Sdl.__N.globalSdl_WarpMouseInWindow = &WarpMouse;
		window = new FakePanelWindow { Handle = 1234 };
		PanelWindows.Register( window );
	}

	[TestCleanup]
	public void Cleanup()
	{
		Sdl.__N.globalSdl_WarpMouseInWindow = savedWarp;
		PanelWindows.CaptureWindow = null;
		PanelWindows.CaptureDelta = default;
		PanelWindows.SkipNextCaptureDelta = false;
		PanelWindows.Unregister( window );
	}

	[UnmanagedCallersOnly]
	static void WarpMouse( IntPtr window, float x, float y ) { }

	// Write SDL's wire layout independently of the managed structs, including their padding.
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public unsafe void MouseMotionPreservesPositionAndRelativeDelta( bool captured )
	{
		Span<byte> data = stackalloc byte[128];
		data.Clear();
		BinaryPrimitives.WriteUInt32LittleEndian( data, 0x400 ); // SDL_EVENT_MOUSE_MOTION
		BinaryPrimitives.WriteSingleLittleEndian( data[28..], 120.5f );
		BinaryPrimitives.WriteSingleLittleEndian( data[32..], 75.25f );
		BinaryPrimitives.WriteSingleLittleEndian( data[36..], -3.5f );
		BinaryPrimitives.WriteSingleLittleEndian( data[40..], 2.25f );
		if ( captured ) PanelWindows.CaptureWindow = window;

		fixed ( byte* e = data ) PanelWindowInput.OnEvent( window.Handle, in *(Sdl.Event*)e );

		Assert.AreEqual( captured ? new Vector2( -3.5f, 2.25f ) : Vector2.Zero, PanelWindows.CaptureDelta );
		Assert.AreEqual( captured ? Vector2.Zero : new Vector2( 120.5f, 75.25f ), window.CursorPosition );
	}

	[TestMethod]
	[DataRow( 0x209u, 1 )] // SDL_EVENT_WINDOW_MINIMIZED
	[DataRow( 0x20au, 2 )] // SDL_EVENT_WINDOW_MAXIMIZED
	[DataRow( 0x20bu, 0 )] // SDL_EVENT_WINDOW_RESTORED
	public unsafe void WindowStateEventsReachTheirWindow( uint type, int expected )
	{
		window.StateChanged( -1 );
		var e = new Sdl.Event { Type = (Sdl.EventType)type };
		PanelWindowInput.OnEvent( window.Handle, in e );
		Assert.AreEqual( expected, window.WindowState );
	}

	[TestMethod]
	public unsafe void CloseRequestOnlyClosesTheAddressedWindow()
	{
		var e = new Sdl.Event { Type = (Sdl.EventType)0x210 }; // SDL_EVENT_WINDOW_CLOSE_REQUESTED
		PanelWindowInput.OnEvent( 5678, in e );
		Assert.IsFalse( window.CloseRequested );
		PanelWindowInput.OnEvent( window.Handle, in e );
		Assert.IsTrue( window.CloseRequested );
	}
}
