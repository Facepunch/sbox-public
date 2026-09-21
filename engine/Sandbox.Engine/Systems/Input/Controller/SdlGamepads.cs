using NativeEngine;

namespace Sandbox.Engine;

/// <summary>
/// Owns SDL gamepad handles, closing them on disconnect or shutdown. Initialization follows the first window paint.
/// </summary>
[SkipHotload]
internal static class SdlGamepads
{
	static bool initialized, mappingsLoaded;

	internal static void Initialize()
	{
		if ( Application.IsHeadless || (initialized && mappingsLoaded) ) return;
		ThreadSafe.AssertIsMainThread();
		if ( !initialized )
		{
			if ( !Sdl.InitSubSystem( Sdl.InitFlags.Gamepad ) )
			{
				Log.Warning( $"Unable to initialize SDL gamepads: {Sdl.GetError()}" );
				return;
			}
			initialized = true;
		}
		if ( !mappingsLoaded )
		{
			mappingsLoaded = Sdl.AddGamepadMappingsFromFile( "core/cfg/gamecontrollerdb.txt" ) >= 0;
			if ( !mappingsLoaded ) Log.Warning( $"Couldn't load gamepad mappings: {Sdl.GetError()}" );
		}
		Sdl.SetJoystickEventsEnabled( true );
		Sdl.SetGamepadEventsEnabled( true );
	}

	internal static void Connect( uint id )
	{
		var gamepad = Sdl.OpenGamepad( id );
		if ( gamepad == IntPtr.Zero )
		{
			Log.Warning( $"Couldn't open gamepad {id}: {Sdl.GetError()}" );
			return;
		}

		try
		{
			// Unsupported sensors simply return false.
			Sdl.SetGamepadSensorEnabled( gamepad, Sdl.SensorType.Accelerometer, true );
			Sdl.SetGamepadSensorEnabled( gamepad, Sdl.SensorType.Gyroscope, true );
			InputRouter.OnGameControllerConnected( (int)id );
		}
		catch
		{
			Sdl.CloseGamepad( gamepad );
			throw;
		}
	}

	internal static void Disconnect( uint id )
	{
		var gamepad = Sdl.GetGamepadFromID( id );
		if ( gamepad == IntPtr.Zero ) return;
		try { InputRouter.OnGameControllerDisconnected( (int)id ); }
		finally { Sdl.CloseGamepad( gamepad ); }
	}

	internal static void Shutdown()
	{
		if ( !initialized ) return;
		foreach ( var controller in Controller.All )
		{
			var gamepad = Sdl.GetGamepadFromID( (uint)controller.DeviceId );
			if ( gamepad != IntPtr.Zero ) Sdl.CloseGamepad( gamepad );
		}
		Controller.All.Clear();
		Sdl.QuitSubSystem( Sdl.InitFlags.Gamepad );
		initialized = mappingsLoaded = false;
	}
}
