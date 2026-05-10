namespace Sandbox;

internal sealed partial class Controller
{
	/// <summary>
	/// A list of all of the controllers, active and inactive.
	/// </summary>
	internal static HashSet<Controller> All { get; private set; } = new();

	/// <summary>
	/// Get the first controller that's connected (shortcut)
	/// </summary>
	internal static Controller First => All.FirstOrDefault();

	/// <summary>
	/// The input context for this controller.
	/// </summary>
	internal Input.Context InputContext { get; set; }

	/// SDL reports values between this range
	internal static readonly Vector2 AXIS_RANGE = new( -32768, 32767 );

	List<InputAxis> ControllerAxes { get; set; } = new();

	/// <summary>
	/// Get an axis
	/// </summary>
	/// <param name="axis"></param>
	/// <param name="defaultValue"></param>
	/// <returns></returns>
	internal float GetAxis( NativeEngine.GameControllerAxis axis, float defaultValue = 0f )
	{
		var foundAxis = ControllerAxes.FirstOrDefault( x => x.Axis == axis );
		if ( foundAxis is null )
		{
			return defaultValue;
		}

		return foundAxis.Value;
	}

	internal void SetAxis( NativeEngine.GameControllerAxis axis, int inputValue )
	{
		float flValue = inputValue;
		float normalizedAxis = flValue.Remap( Controller.AXIS_RANGE.x, Controller.AXIS_RANGE.y, -1, 1 );

		// 12.5% deadzone
		// todo: make this modifiable 
		var deadzone = 0.125f;
		if ( MathF.Abs( normalizedAxis ) <= deadzone ) normalizedAxis = 0f;

		if ( normalizedAxis > 0f )
		{
			Sandbox.Input.UsingController = true;
		}

		var current = ControllerAxes.FirstOrDefault( x => x.Axis == axis );
		if ( current is null )
		{
			current = new InputAxis
			{
				Axis = axis,
			};
			ControllerAxes.Add( current );
		}

		current.Value = normalizedAxis;
	}

	/// <summary>
	/// An axis
	/// </summary>
	internal record class InputAxis
	{
		internal NativeEngine.GameControllerAxis Axis { get; set; }
		internal float Value { get; set; }
	}

}
