using NativeEngine;

namespace Sandbox;

internal sealed partial class Controller
{
	private Color[] ControllerColors = new[]
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
			Log.Info( $"\t> {controller.Name}, GlyphSet: {controller.GlyphSet}, Handle: {controller.SDLHandle}, Device ID: {controller.DeviceId}" );
			Log.Info( $"\t\t> Color: {controller.LEDColor}, GlyphVendor: {controller.GlyphVendor}" );
			Log.Info( $"\t\t> Accel: {controller.Accelerometer}, Gyro: {controller.Gyroscope}" );
		}
		Log.Info( "---------------------------------------------------------------------------" );
	}

	public int SDLHandle { get; init; }
	public int DeviceId { get; init; }

	/// <summary>
	/// The glyph set for this controller, used for icon/prompt selection.
	/// </summary>
	public GameControllerGlyphSet GlyphSet { get; init; }

	internal Controller( int joystickHandle, int deviceHandle )
	{
		SDLHandle = joystickHandle;
		DeviceId = deviceHandle;
		InputContext = Input.Context.Create( $"GameController:{SDLHandle}" );
		Name = NativeEngine.SDLGameController.GetControllerName( SDLHandle );
		GlyphSet = NativeEngine.SDLGameController.GetControllerGlyphSet( SDLHandle );

		var id = joystickHandle % 4;
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
			var vec = NativeEngine.SDLGameController.GetGyroscope( SDLHandle );
			return new Angles( vec.x, vec.y, vec.z );
		}
	}

	/// <summary>
	/// Gets a sensor reading from the device's accelerometer (if it has one)
	/// </summary>
	public Vector3 Accelerometer => NativeEngine.SDLGameController.GetAccelerometer( SDLHandle );

	private Color32 ledColor = Color.White;
	/// <summary>
	/// Sets the color of the gamepad if supported
	/// </summary>
	public Color32 LEDColor
	{
		get => ledColor;
		set
		{
			if ( NativeEngine.SDLGameController.SetLEDColor( SDLHandle, value.r, value.g, value.b ) )
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
		// Log.Trace( $"Trying to rumble {leftMotor}, {rightMotor}" );
		NativeEngine.SDLGameController.Rumble( SDLHandle, leftMotor, rightMotor, duration );
	}

	/// <summary>
	/// Rumbles the controller's triggers (if supported)
	/// </summary>
	/// <param name="leftTrigger">The speed of the left trigger motor, between 0 and 0xFFFF</param>
	/// <param name="rightTrigger">The speed of the right trigger motor, between 0 and 0xFFFF</param>
	/// <param name="duration">The duration of the vibration in ms</param>
	public void RumbleTriggers( int leftTrigger, int rightTrigger, int duration )
	{
		// Log.Trace( $"Trying to rumble triggers {leftTrigger}, {rightTrigger}" );
		NativeEngine.SDLGameController.RumbleTriggers( SDLHandle, leftTrigger, rightTrigger, duration );
	}

	/// <summary>
	/// Stops all rumble and haptic events on this controller.
	/// </summary>
	public void StopAllHaptics()
	{
		// Calling with 0 intensity stops any rumbling
		NativeEngine.SDLGameController.Rumble( SDLHandle, 0, 0, 0 );
		NativeEngine.SDLGameController.RumbleTriggers( SDLHandle, 0, 0, 0 );

		ActiveHapticEffect = null;
	}
}
