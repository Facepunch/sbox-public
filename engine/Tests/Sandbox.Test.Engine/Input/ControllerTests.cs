using NativeEngine;
using System;
using System.Runtime.InteropServices;

namespace EngineTests;

[TestClass, DoNotParallelize]
public unsafe class ControllerTests
{
	static bool connected, sensorSupported, succeeds;
	static int calls, sensorReads;
	static long lastSensor;
	static (ushort, ushort, uint) rumble;

	[TestMethod]
	public void SensorFailureNeverReturnsPartialData() => WithController( controller =>
	{
		Assert.AreEqual( new Vector3( 1, 2, 3 ), controller.Accelerometer );
		Assert.AreEqual( (long)Sdl.SensorType.Accelerometer, lastSensor );
		Assert.AreEqual( new Angles( 1, 2, 3 ), controller.Gyroscope );
		Assert.AreEqual( (long)Sdl.SensorType.Gyroscope, lastSensor );
		succeeds = false;
		Assert.AreEqual( Vector3.Zero, controller.Accelerometer );
		sensorSupported = false;
		var reads = sensorReads;
		Assert.AreEqual( Vector3.Zero, controller.Accelerometer );
		Assert.AreEqual( reads, sensorReads );
	} );

	[TestMethod]
	public void DisconnectedControllerDoesNotUseAStaleHandle() => WithController( controller =>
	{
		connected = false;
		calls = 0;
		Assert.AreEqual( Vector3.Zero, controller.Accelerometer );
		Assert.AreEqual( default( Angles ), controller.Gyroscope );
		controller.LEDColor = Color.Red;
		controller.StopAllHaptics();
		Assert.AreEqual( 0, calls );
	} );

	[TestMethod]
	public void LedAndRumbleUseSdlSuccessAndArgumentConventions() => WithController( controller =>
	{
		controller.LEDColor = Color.Red;
		var color = controller.LEDColor;
		succeeds = false;
		controller.LEDColor = Color.Blue;
		Assert.AreEqual( color, controller.LEDColor );
		succeeds = true;
		controller.LEDColor = Color.Blue;
		Assert.AreEqual( (Color32)Color.Blue, controller.LEDColor );
		controller.Rumble( 123, 456, 789 );
		Assert.AreEqual( ((ushort)123, (ushort)456, 789u), rumble );
		controller.RumbleTriggers( 456, 123, 987 );
		Assert.AreEqual( ((ushort)456, (ushort)123, 987u), rumble );
		controller.StopAllHaptics();
		Assert.AreEqual( ((ushort)0, (ushort)0, 0u), rumble );
	} );

	[TestMethod]
	[DataRow( 2, "Xbox" )]
	[DataRow( 3, "Xbox" )]
	[DataRow( 4, "PlayStation" )]
	[DataRow( 5, "PlayStation" )]
	[DataRow( 6, "PlayStation" )]
	[DataRow( 7, "Switch" )]
	[DataRow( 8, "Switch" )]
	[DataRow( 9, "Switch" )]
	[DataRow( 10, "Switch" )]
	[DataRow( 0, "Unknown" )]
	[DataRow( 99, "Unknown" )]
	public void GlyphFamiliesMatchSdlTypes( int type, string family ) => Assert.AreEqual( family, Controller.GetGlyphSet( (Sdl.GamepadType)type ).ToString() );

	static void WithController( Action<Controller> test )
	{
		var get = Sdl.__N.globalSdl_GetGamepadFromID;
		var name = Sdl.__N.globalSdl_GetGamepadName;
		var type = Sdl.__N.globalSdl_GetRealGamepadType;
		var led = Sdl.__N.globalSdl_SetGamepadLED;
		var vibrate = Sdl.__N.globalSdl_RumbleGamepad;
		var triggers = Sdl.__N.globalSdl_RumbleGamepadTriggers;
		var hasSensor = Sdl.__N.globalSdl_GamepadHasSensor;
		var sensor = Sdl.__N.globalSdl_GetGamepadSensorData;
		try
		{
			Sdl.__N.globalSdl_GetGamepadFromID = &Get;
			Sdl.__N.globalSdl_GetGamepadName = &Name;
			Sdl.__N.globalSdl_GetRealGamepadType = &Type;
			Sdl.__N.globalSdl_SetGamepadLED = &Led;
			Sdl.__N.globalSdl_RumbleGamepad = &Rumble;
			Sdl.__N.globalSdl_RumbleGamepadTriggers = &Rumble;
			Sdl.__N.globalSdl_GamepadHasSensor = &HasSensor;
			Sdl.__N.globalSdl_GetGamepadSensorData = &Sensor;
			connected = sensorSupported = succeeds = true;
			calls = sensorReads = 0;
			test( new Controller( 123 ) );
		}
		finally
		{
			Sdl.__N.globalSdl_GetGamepadFromID = get;
			Sdl.__N.globalSdl_GetGamepadName = name;
			Sdl.__N.globalSdl_GetRealGamepadType = type;
			Sdl.__N.globalSdl_SetGamepadLED = led;
			Sdl.__N.globalSdl_RumbleGamepad = vibrate;
			Sdl.__N.globalSdl_RumbleGamepadTriggers = triggers;
			Sdl.__N.globalSdl_GamepadHasSensor = hasSensor;
			Sdl.__N.globalSdl_GetGamepadSensorData = sensor;
		}
	}

	[UnmanagedCallersOnly] static IntPtr Get( uint id ) => connected ? (IntPtr)123 : IntPtr.Zero;
	[UnmanagedCallersOnly] static IntPtr Name( IntPtr gamepad ) => IntPtr.Zero;
	[UnmanagedCallersOnly] static long Type( IntPtr gamepad ) => (long)Sdl.GamepadType.PS5;
	[UnmanagedCallersOnly] static int Led( IntPtr gamepad, byte r, byte g, byte b ) { calls++; return succeeds ? 1 : 0; }
	[UnmanagedCallersOnly] static int Rumble( IntPtr gamepad, ushort left, ushort right, uint duration ) { calls++; rumble = (left, right, duration); return succeeds ? 1 : 0; }
	[UnmanagedCallersOnly] static int HasSensor( IntPtr gamepad, long sensor ) { calls++; return sensorSupported ? 1 : 0; }
	[UnmanagedCallersOnly]
	static int Sensor( IntPtr gamepad, long sensor, IntPtr data, int count )
	{
		calls++;
		sensorReads++;
		lastSensor = sensor;
		for ( var i = 0; i < count; i++ ) ((float*)data)[i] = i + 1;
		return succeeds ? 1 : 0;
	}
}
