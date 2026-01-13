using Sandbox.Utility;

namespace Sandbox;

public static partial class Input
{
	/// <summary>
	/// What's our current player index (for input scoping)?
	/// -1 is the default behavior, where it'll accept keyboard AND gamepad inputs.
	/// Anything above that, is targeting a specific controller.
	/// </summary>
	internal static int CurrentPlayerScope { get; private set; } = -1;

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
			// Fallback if we're not using any input scoping
			if ( CurrentPlayerScope == -1 ) return Controller.First;

			// Out of range?
			if ( CurrentPlayerScope >= Controller.All.Count() )
			{
				return null;
			}

			if ( Controller.All.ElementAt( CurrentPlayerScope ) is { } controller )
			{
				return controller;
			}

			return null;
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
	/// Processes controller inputs based on a player index (for input scoping). This can be called many times a frame.
	/// </summary>
	private static void ProcessControllerInput( int playerIndex = -1 )
	{
		CurrentPlayerScope = playerIndex;

		// Default input accepts gamepad and keyboard and mouse, so we don't want to reset analogs
		if ( CurrentPlayerScope != -1 )
		{
			// Reset analogs
			AnalogLook = default;
			AnalogMove = 0;
		}

		if ( Input.CurrentController is { } controller && UsingController )
		{
			// Use controller's input context
			// Doesn't need to be flipped, we do this once a frame for each controller.
			using var inputScope = controller.InputContext?.Push();

			var lookX = controller.GetAxis( NativeEngine.GameControllerAxis.RightX ) * Time.Delta * Preferences.ControllerLookYawSpeed;
			var lookY = controller.GetAxis( NativeEngine.GameControllerAxis.RightY ) * Time.Delta * Preferences.ControllerLookPitchSpeed;

			AnalogLook = new Angles( lookY, -lookX, 0 );

			var moveX = controller.GetAxis( NativeEngine.GameControllerAxis.LeftX );
			var moveY = controller.GetAxis( NativeEngine.GameControllerAxis.LeftY );

			AnalogMove = new Vector3( -moveY, -moveX, 0 );

			MotionData = new()
			{
				Gyroscope = controller.Gyroscope,
				Accelerometer = controller.Accelerometer
			};

			controller.UpdateHaptics();
		}
	}

	/// <summary>
	/// Push a specific player scope to be active
	/// </summary>
	public static IDisposable PlayerScope( int index )
	{
		index = index.Clamp( 0, int.MaxValue );

		var old = CurrentPlayerScope;

		// Process input for our new scope
		ProcessControllerInput( index );

		return DisposeAction.Create( () =>
		{
			if ( CurrentPlayerScope == index )
			{
				ProcessControllerInput( old );
			}
		} );
	}
}
