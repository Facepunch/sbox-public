using NativeEngine;
using Sandbox.Engine;
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace EngineTests;

[TestClass, DoNotParallelize]
public unsafe class SdlGamepadsTests
{
	static int initializes, quits, mappings;
	static bool failInit, failMappings;

	[TestMethod]
	public void InitializationRetriesWithoutAcquiringTheSubsystemTwice()
	{
		var init = Sdl.__N.globalSdl_InitSubSystem;
		var quit = Sdl.__N.globalSdl_QuitSubSystem;
		var load = Sdl.__N.globalSdl_AddGamepadMappingsFromFile;
		var joystick = Sdl.__N.globalSdl_SetJoystickEventsEnabled;
		var gamepad = Sdl.__N.globalSdl_SetGamepadEventsEnabled;
		var headless = Application.IsHeadless;
		var mainThread = ThreadSafe.IsMainThread;
		ThreadSafe.MarkMainThread();
		try
		{
			Sdl.__N.globalSdl_InitSubSystem = &Init;
			Sdl.__N.globalSdl_QuitSubSystem = &Quit;
			Sdl.__N.globalSdl_AddGamepadMappingsFromFile = &Mappings;
			Sdl.__N.globalSdl_SetJoystickEventsEnabled = &Events;
			Sdl.__N.globalSdl_SetGamepadEventsEnabled = &Events;
			initializes = quits = mappings = 0;
			SetHeadless( true );
			SdlGamepads.Initialize();
			Assert.AreEqual( 0, initializes );
			SetHeadless( false );
			failInit = true;
			SdlGamepads.Initialize();
			SdlGamepads.Shutdown();
			Assert.AreEqual( 0, quits );
			failInit = false;
			failMappings = true;
			SdlGamepads.Initialize();
			failMappings = false;
			SdlGamepads.Initialize();
			SdlGamepads.Initialize();
			Assert.AreEqual( 2, initializes );
			Assert.AreEqual( 2, mappings );
			SdlGamepads.Shutdown();
			SdlGamepads.Shutdown();
			Assert.AreEqual( 1, quits );
		}
		finally
		{
			SdlGamepads.Shutdown();
			Sdl.__N.globalSdl_InitSubSystem = init;
			Sdl.__N.globalSdl_QuitSubSystem = quit;
			Sdl.__N.globalSdl_AddGamepadMappingsFromFile = load;
			Sdl.__N.globalSdl_SetJoystickEventsEnabled = joystick;
			Sdl.__N.globalSdl_SetGamepadEventsEnabled = gamepad;
			SetHeadless( headless );
			typeof( ThreadSafe ).GetField( "isMainThread", BindingFlags.NonPublic | BindingFlags.Static ).SetValue( null, mainThread );
		}
	}

	static void SetHeadless( bool value ) => typeof( Application ).GetProperty( nameof( Application.IsHeadless ) ).SetValue( null, value );
	[UnmanagedCallersOnly] static int Init( long flags ) { initializes++; return failInit ? 0 : 1; }
	[UnmanagedCallersOnly] static void Quit( long flags ) => quits++;
	[UnmanagedCallersOnly] static int Mappings( IntPtr path ) { mappings++; return failMappings ? -1 : 0; }
	[UnmanagedCallersOnly] static void Events( int enabled ) { }
}
