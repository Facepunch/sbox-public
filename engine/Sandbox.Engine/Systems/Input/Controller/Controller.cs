using NativeEngine;

namespace Sandbox;

internal sealed partial class Controller
{
	static readonly Color[] ControllerColors = new[]
	{
		Color.Red,
		Color.Green,
		Color.Blue,
		Color.White
	};


	[ConCmd( "controller_debug", ConVarFlags.Protected )]
	public static void ControllerDebug()
	{
		Log.Info( "---------------------------------------------------------------------------" );
		Log.Info( $"Detected {Controller.All.Count()} controllers" );
		foreach ( var controller in Controller.All )
		{
			Log.Info( $"\t> {controller.Name}, GlyphSet: {controller.GlyphSet}, Device ID: {controller.DeviceId}" );
			Log.Info( $"\t\t> Color: {controller.LEDColor}, GlyphVendor: {controller.GlyphVendor}" );
			Log.Info( $"\t\t> Accel: {controller.Accelerometer}, Gyro: {controller.Gyroscope}" );
		}
		Log.Info( "---------------------------------------------------------------------------" );
	}

	public int DeviceId { get; init; }

	// Resolve by ID so retained controllers remain safe after disconnection.
	IntPtr Gamepad => Sdl.GetGamepadFromID( (uint)DeviceId );

	/// <summary>
	/// The glyph set for this controller, used for icon/prompt selection.
	/// </summary>
	public GameControllerGlyphSet GlyphSet { get; init; }

	internal Controller( int deviceId )
	{
		DeviceId = deviceId;
		InputContext = Input.Context.Create( $"GameController:{DeviceId}" );
		var gamepad = Gamepad;
		Name = gamepad == IntPtr.Zero ? "Unknown" : Sdl.GetGamepadName( gamepad ) ?? "Unknown";
		GlyphSet = gamepad == IntPtr.Zero ? GameControllerGlyphSet.Unknown : GetGlyphSet( Sdl.GetRealGamepadType( gamepad ) );

		var id = deviceId % ControllerColors.Length;
		LEDColor = ControllerColors[id];
	}

	public override string ToString()
	{
		return $"{Name}";
	}

	/// <summary>
	/// Gets a sensor reading from the device's gyroscope (if it has one)
	/// </summary>
	public Angles Gyroscope
	{
		get
		{
			var vec = GetSensorData( Sdl.SensorType.Gyroscope );
			return new Angles( vec.x, vec.y, vec.z );
		}
	}

	/// <summary>
	/// Gets a sensor reading from the device's accelerometer (if it has one)
	/// </summary>
	public Vector3 Accelerometer => GetSensorData( Sdl.SensorType.Accelerometer );

	unsafe Vector3 GetSensorData( Sdl.SensorType sensor )
	{
		var gamepad = Gamepad;
		if ( gamepad == IntPtr.Zero || !Sdl.GamepadHasSensor( gamepad, sensor ) ) return default;

		var data = stackalloc float[3];
		return Sdl.GetGamepadSensorData( gamepad, sensor, (IntPtr)data, 3 )
			? new Vector3( data[0], data[1], data[2] ) : default;
	}

	internal static GameControllerGlyphSet GetGlyphSet( Sdl.GamepadType type ) => type switch
	{
		Sdl.GamepadType.Xbox360 or Sdl.GamepadType.XboxOne => GameControllerGlyphSet.Xbox,
		Sdl.GamepadType.PS3 or Sdl.GamepadType.PS4 or Sdl.GamepadType.PS5 => GameControllerGlyphSet.PlayStation,
		Sdl.GamepadType.SwitchPro or Sdl.GamepadType.JoyConLeft or Sdl.GamepadType.JoyConRight or Sdl.GamepadType.JoyConPair => GameControllerGlyphSet.Switch,
		_ => GameControllerGlyphSet.Unknown
	};

	private Color32 ledColor = Color.White;
	/// <summary>
	/// Sets the color of the gamepad if supported
	/// </summary>
	public Color32 LEDColor
	{
		get => ledColor;
		set
		{
			var gamepad = Gamepad;
			if ( gamepad != IntPtr.Zero && Sdl.SetGamepadLED( gamepad, value.r, value.g, value.b ) )
			{
				ledColor = value;
			}
		}
	}

	/// <summary>
	/// The name of this controller (e.g. "Xbox Wireless Controller", "Steam Controller")
	/// </summary>
	public string Name { get; init; }

	/// <summary>
	/// Which glyph folder to use for this controller.
	/// Derived from the controller's glyph set.
	/// </summary>
	public string GlyphVendor => GlyphSet switch
	{
		GameControllerGlyphSet.Xbox => "xbox",
		GameControllerGlyphSet.PlayStation => "playstation",
		GameControllerGlyphSet.Switch => "switch",
		GameControllerGlyphSet.Steam => "steam",
		_ => "xbox"
	};

	/// <summary>
	/// Rumbles the controller.
	/// </summary>
	/// <param name="leftMotor">The speed of the left motor, between 0 and 0xFFFF</param>
	/// <param name="rightMotor">The speed of the right motor, between 0 and 0xFFFF</param>
	/// <param name="duration">The duration of the vibration in ms</param>
	public void Rumble( int leftMotor, int rightMotor, int duration )
	{
		var gamepad = Gamepad;
		if ( gamepad != IntPtr.Zero ) Sdl.RumbleGamepad( gamepad, (ushort)leftMotor, (ushort)rightMotor, (uint)duration );
	}

	/// <summary>
	/// Rumbles the controller's triggers (if supported)
	/// </summary>
	/// <param name="leftTrigger">The speed of the left trigger motor, between 0 and 0xFFFF</param>
	/// <param name="rightTrigger">The speed of the right trigger motor, between 0 and 0xFFFF</param>
	/// <param name="duration">The duration of the vibration in ms</param>
	public void RumbleTriggers( int leftTrigger, int rightTrigger, int duration )
	{
		var gamepad = Gamepad;
		if ( gamepad != IntPtr.Zero ) Sdl.RumbleGamepadTriggers( gamepad, (ushort)leftTrigger, (ushort)rightTrigger, (uint)duration );
	}

	/// <summary>
	/// Stops all rumble and haptic events on this controller.
	/// </summary>
	public void StopAllHaptics()
	{
		// Calling with 0 intensity stops any rumbling
		Rumble( 0, 0, 0 );
		RumbleTriggers( 0, 0, 0 );

		ActiveHapticEffect = null;
	}
}
