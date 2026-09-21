using NativeEngine;
using Sandbox.Engine;
using Sandbox.UI;
using System;
using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using UITests.Controls;

namespace EngineTests;

[TestClass, DoNotParallelize]
public unsafe class SdlEventsTests
{
	static int polls;

	[TestMethod]
	public void PanelCloseDoesNotQuitTheApplication() => WithEvents( () =>
	{
		var window = new FakePanelWindow { Handle = 1234 };
		PanelWindowInput.SetupWindow( window.Handle );
		PanelWindows.Register( window );
		try
		{
			SdlEvents.Dispatch( new Sdl.Event { Type = Sdl.EventType.WindowCloseRequested } );
			Assert.IsTrue( window.CloseRequested );
			Assert.IsFalse( Application.WantsExit );
			PanelWindows.Unregister( window );
			Assert.IsTrue( PanelWindowInput.IsPanelWindow( window.Handle ), "Deferred native disposal must still consume panel events." );
			SdlEvents.Dispatch( new Sdl.Event { Type = Sdl.EventType.WindowCloseRequested } );
			Assert.IsFalse( Application.WantsExit );
		}
		finally
		{
			PanelWindows.Unregister( window );
			PanelWindowInput.DetachWindow( window.Handle );
		}
		Assert.IsFalse( PanelWindowInput.IsPanelWindow( window.Handle ) );
		SdlEvents.Dispatch( new Sdl.Event { Type = Sdl.EventType.WindowCloseRequested } );
		Assert.IsFalse( Application.WantsExit, "Closing a Qt window must not quit the game." );
	} );

	[TestMethod]
	public void QuitStopsPollingAndNestedPollDoesNotDrainTheQueue() => WithEvents( () =>
	{
		SdlEvents.Poll();
		Assert.IsTrue( Application.WantsExit );
		Assert.AreEqual( 1, polls );
	} );

	[TestMethod]
	[DataRow( 1, (int)ButtonCode.MouseLeft )]
	[DataRow( 2, (int)ButtonCode.MouseMiddle )]
	[DataRow( 3, (int)ButtonCode.MouseRight )]
	[DataRow( 4, (int)ButtonCode.MouseBack )]
	[DataRow( 5, (int)ButtonCode.MouseForward )]
	[DataRow( 6, (int)ButtonCode.BUTTON_CODE_INVALID )]
	public void MouseButtonsMatchSdl( int button, int expected ) => Assert.AreEqual( (ButtonCode)expected, SdlEvents.MouseButton( (byte)button ) );

	[TestMethod]
	public void WheelDirectionAndModifierSidesAreNormalized()
	{
		Assert.AreEqual( new Vector2( -2, 3 ), SdlEvents.WheelDelta( new Sdl.MouseWheelEvent { X = 2, Y = -3, Direction = 1 } ) );
		Assert.AreEqual( new Vector2( 2, -3 ), SdlEvents.WheelDelta( new Sdl.MouseWheelEvent { X = 2, Y = -3 } ) );
		Assert.AreEqual( KeyboardModifiers.Shift | KeyboardModifiers.Ctrl | KeyboardModifiers.Alt, SdlEvents.Modifiers( 0x0541 ) );
		Assert.AreEqual( KeyboardModifiers.Shift | KeyboardModifiers.Ctrl | KeyboardModifiers.Alt, SdlEvents.Modifiers( 0x0a82 ) );
		Assert.AreEqual( KeyboardModifiers.Shift, SdlEvents.Modifiers( 0x0003 ) );
		Assert.AreEqual( KeyboardModifiers.Ctrl, SdlEvents.Modifiers( 0x00c0 ) );
		Assert.AreEqual( KeyboardModifiers.Alt, SdlEvents.Modifiers( 0x0300 ) );
		Assert.AreEqual( KeyboardModifiers.None, SdlEvents.Modifiers( 0x3c00 ) ); // Num/caps lock and GUI have no managed modifier.
	}

	[TestMethod]
	public void GamepadEventsReadSdlWireLayout()
	{
		Span<byte> data = stackalloc byte[128];
		data.Clear();
		BinaryPrimitives.WriteUInt32LittleEndian( data, 0x650 );
		BinaryPrimitives.WriteUInt32LittleEndian( data[16..], 123 );
		data[20] = 4;
		BinaryPrimitives.WriteInt16LittleEndian( data[24..], -32768 );
		fixed ( byte* bytes = data )
		{
			var e = *(Sdl.Event*)bytes;
			Assert.AreEqual( Sdl.EventType.GamepadAxisMotion, e.Type );
			Assert.AreEqual( 123u, e.GamepadAxis.Which );
			Assert.AreEqual( (byte)4, e.GamepadAxis.Axis );
			Assert.AreEqual( (short)-32768, e.GamepadAxis.Value );
			Assert.AreEqual( 123u, e.GamepadDevice.Which );
			Assert.AreEqual( 123u, e.GamepadButton.Which );
			Assert.AreEqual( (byte)4, e.GamepadButton.Button );
		}
	}

	static void WithEvents( Action test )
	{
		var poll = Sdl.__N.globalSdl_PollEvent;
		var window = Sdl.__N.globalSdl_GetWindowFromEvent;
		var addWatch = Sdl.__N.globalSdl_AddEventWatch;
		var removeWatch = Sdl.__N.globalSdl_RemoveEventWatch;
		var startText = Sdl.__N.globalSdl_StartTextInput;
		var hint = Sdl.__N.globalSdl_SetHint;
		var dropTarget = Sdl.__N.globalSdl_SetWindowExternalDropTarget;
		var wasMainThread = ThreadSafe.IsMainThread;
		var wantsExit = Application.WantsExit;
		ThreadSafe.MarkMainThread();
		try
		{
			Sdl.__N.globalSdl_PollEvent = (delegate* unmanaged< out Sdl.Event, int >)(delegate* unmanaged< Sdl.Event*, int >)&Poll;
			Sdl.__N.globalSdl_GetWindowFromEvent = &GetWindow;
			Sdl.__N.globalSdl_AddEventWatch = &Accept;
			Sdl.__N.globalSdl_RemoveEventWatch = &RemoveWatch;
			Sdl.__N.globalSdl_StartTextInput = &StartText;
			Sdl.__N.globalSdl_SetHint = &Accept;
			Sdl.__N.globalSdl_SetWindowExternalDropTarget = &Accept;
			Application.WantsExit = false;
			polls = 0;
			test();
		}
		finally
		{
			Sdl.__N.globalSdl_PollEvent = poll;
			Sdl.__N.globalSdl_GetWindowFromEvent = window;
			Sdl.__N.globalSdl_AddEventWatch = addWatch;
			Sdl.__N.globalSdl_RemoveEventWatch = removeWatch;
			Sdl.__N.globalSdl_StartTextInput = startText;
			Sdl.__N.globalSdl_SetHint = hint;
			Sdl.__N.globalSdl_SetWindowExternalDropTarget = dropTarget;
			Application.WantsExit = wantsExit;
			typeof( ThreadSafe ).GetField( "isMainThread", BindingFlags.NonPublic | BindingFlags.Static ).SetValue( null, wasMainThread );
		}
	}

	[UnmanagedCallersOnly]
	static int Poll( Sdl.Event* e )
	{
		polls++;
		if ( polls > 1 ) return 0;
		SdlEvents.Poll();
		*e = new Sdl.Event { Type = Sdl.EventType.Quit };
		return 1;
	}

	[UnmanagedCallersOnly] static IntPtr GetWindow( IntPtr e ) => (IntPtr)1234;
	[UnmanagedCallersOnly] static int Accept( IntPtr first, IntPtr second ) => 1;
	[UnmanagedCallersOnly] static void RemoveWatch( IntPtr filter, IntPtr userdata ) { }
	[UnmanagedCallersOnly] static int StartText( IntPtr window ) => 1;
}
