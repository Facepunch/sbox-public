namespace Sandbox;

public static partial class Input
{
	/// <summary>
	/// How many controllers are active right now?
	/// </summary>
	public static int ControllerCount => Controller.All.Count();

	/// <summary>
	/// Whether or not the Virtual Cursor should show when using a controller. Disable this to control the cursor manually.
	/// </summary>
	public static bool EnableVirtualCursor { get; set; } = true;

	/// <summary>
	/// Tries to find the current controller to use.
	/// </summary>
	internal static Controller CurrentController
	{
		get
		{
			if ( CurrentPlayerScope == -1 ) return Controller.First;
			if ( CurrentPlayerScope == 0 ) return null;

			var controllerIndex = CurrentPlayerScope - 1;
			if ( controllerIndex >= Controller.All.Count() ) return null;

			return Controller.All.ElementAt( controllerIndex );
		}
	}

	/// <summary>
	/// An analog input, when fetched, is between -1 and 1 (0 being default)
	/// </summary>
	public static float GetAnalog( InputAnalog analog )
	{
		if ( Suppressed ) return default;

		if ( Input.CurrentController is { } controller && UsingController )
		{
			return controller.GetAxis( analog.ToAxis() );
		}

		return 0f;
	}

	/// <summary>
	/// Processes controller inputs for the current scope.
	/// </summary>
	private static void ProcessControllerInput()
	{
		if ( Input.CurrentController is not { } controller )
			return;

		// Use controller's input context
		using var inputScope = controller.InputContext?.Push();

		var lookX = controller.GetAxis( NativeEngine.GameControllerAxis.RightX ) * Time.Delta * Preferences.ControllerLookYawSpeed;
		var lookY = controller.GetAxis( NativeEngine.GameControllerAxis.RightY ) * Time.Delta * Preferences.ControllerLookPitchSpeed;

		AnalogLook += new Angles( lookY, -lookX, 0 );

		var moveX = controller.GetAxis( NativeEngine.GameControllerAxis.LeftX );
		var moveY = controller.GetAxis( NativeEngine.GameControllerAxis.LeftY );

		AnalogMove += new Vector3( -moveY, -moveX, 0 );

		MotionData = new()
		{
			Gyroscope = controller.Gyroscope,
			Accelerometer = controller.Accelerometer
		};

		controller.UpdateHaptics();
	}
}
