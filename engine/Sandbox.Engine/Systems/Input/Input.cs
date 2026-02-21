using Sandbox.Engine;
using Sandbox.VR;

namespace Sandbox;

/// <summary>
/// Allows querying of player button presses and other inputs.
/// </summary>
public static partial class Input
{
	/// <summary>
	/// Virtual Reality specific input data.
	/// </summary>
	public static VRInput VR => VRInput.Current;

	/// <summary>
	/// Movement delta from the mouse.
	/// </summary>
	public static Vector2 MouseDelta
	{
		get => Suppressed ? default : CurrentContext.MouseDelta;
		set => CurrentContext.MouseDelta = value;
	}

	/// <summary>
	/// The state of the mouse wheel.
	/// </summary>
	public static Vector2 MouseWheel
	{
		get => Suppressed ? default : CurrentContext.MouseWheel;
		set => CurrentContext.MouseWheel = value;
	}

	/// <summary>
	/// True if the mouse cursor is visible (using UI etc)
	/// </summary>
	public static bool MouseCursorVisible
	{
		get => CurrentContext.MouseCursorVisible;
	}

	static Angles _analogLook;

	/// <summary>
	/// Analog look value from the default input device. This is scaled by Preferences.Sensitivity - so you don't need to scale it afterwards.
	/// </summary>
	[ActionGraphNode( "input.analog.look" ), Category( "Input" ), Icon( "gamepad" )]
	public static Angles AnalogLook
	{
		get => Suppressed ? default : _analogLook;
		set => _analogLook = value;
	}

	static Vector3 _analogMove;

	/// <summary>
	/// Analog move value from the default input device.
	/// </summary>
	[ActionGraphNode( "input.analog.move" ), Category( "Input" ), Icon( "gamepad" )]
	public static Vector3 AnalogMove
	{
		get => Suppressed ? default : _analogMove;
		set => _analogMove = value;
	}


	internal static void AddMouseMovement( Vector2 delta )
	{
		foreach ( var e in Contexts )
		{
			e.AccumMouseDelta += delta;
		}
	}

	internal static void AddMouseWheel( Vector2 delta )
	{
		foreach ( var e in Contexts )
		{
			e.AccumMouseWheel += delta;
		}
	}

	/// <summary>
	/// Called multiple times between ticks.
	/// </summary>
	internal static void Process()
	{
		// Reset suppression flag
		Suppressed = false;

		// Flip all controller input contexts once a frame
		foreach ( var controller in Controller.All )
		{
			controller.InputContext?.Flip();
		}

		//
		// Setup
		//
		CurrentContext.MouseCursorVisible = InputRouter.MouseCursorVisible;

		// this is all kind of how it did it in CInputService::HandleAnalogValueChange
		var mouseSensitivity = Preferences.Sensitivity * 10.0f;
		var halfDim = MathF.Max( Screen.Width, Screen.Height ) * 0.5f;
		if ( halfDim < 1.0f ) halfDim = 1.0f;

		AnalogLook = new( (MouseDelta.y / halfDim) * mouseSensitivity, (-MouseDelta.x / halfDim) * mouseSensitivity, 0 );

		if ( MouseCursorVisible )
		{
			AnalogLook = default;
		}

		Actions = CurrentContext.ActionsCurrent;
		LastActions = CurrentContext.ActionsPrevious;

		if ( Preferences.InvertMousePitch )
			AnalogLook = AnalogLook.WithPitch( -AnalogLook.pitch );

		if ( Preferences.InvertMouseYaw )
			AnalogLook = AnalogLook.WithYaw( -AnalogLook.yaw );

		// garry: do we need to smooth these or something?
		// They were smoothed in the old input code, but I think
		// we leave them as raw as possible now and let games decide
		AnalogMove = 0;
		if ( Down( "forward", false ) ) AnalogMove += Vector3.Forward;
		if ( Down( "backward", false ) ) AnalogMove += Vector3.Backward;
		if ( Down( "left", false ) ) AnalogMove += Vector3.Left;
		if ( Down( "right", false ) ) AnalogMove += Vector3.Right;

		ProcessControllerInput();
	}

	/// <summary>
	/// Current state of the current input device's motion sensor(s) if supported.
	/// This is only supported on: Dualshock 4+, Switch Controllers, Steam Controller, Steam Deck.
	/// </summary>
	public static InputMotionData MotionData { get; internal set; }

	/// <inheritdoc cref="GetButtonOrigin( string, bool )"/>
	/// <remarks>
	/// This will return <see langword="null"/> if no button was set in the action.
	/// </remarks>
	internal static string GetButtonOrigin( InputAction action, bool ignoreController = false )
	{
		if ( UsingController )
		{
			return action.GamepadCode.ToString();
		}

		string loadedGame = Application.GameIdent;

		if ( string.IsNullOrEmpty( loadedGame ) ) loadedGame = "common";
		var collection = InputBinds.FindCollection( loadedGame );

		var bind = collection.Get( action.Name, 0 );
		if ( string.IsNullOrEmpty( bind ) ) return action.KeyboardCode;

		return bind;
	}

	/// <summary>
	/// Returns the name of a key bound to this InputAction
	/// <example>For example:
	/// <code>Input.GetButtonOrigin( "Undo" )</code>
	/// could return <c>SPACE</c> if using keyboard or <c>A Button</c> when using a controller.
	/// </example>
	/// </summary>
	public static string GetButtonOrigin( string name, bool ignoreController = false )
	{
		var action = InputActions?
			.FirstOrDefault( x => string.Equals( x.Name, name, StringComparison.OrdinalIgnoreCase ) );

		if ( action is null )
		{
			Log.Warning( $"Couldn't find Input Action called \"{name}\"" );
			return null;
		}

		return GetButtonOrigin( action, ignoreController );
	}
}
