using Facepunch.XR;

namespace Sandbox.VR;

/// <summary>
/// Represents a VR digital input action (e.g. X button)
/// </summary>
public struct DigitalInput
{
	internal readonly InputBooleanActionState _data;

	/// <summary>
	/// The current value of this input - true if pressed, false if not pressed.
	/// </summary>
	public readonly bool IsPressed => _data.state != 0;

	/// <summary>
	/// The previous value of this input - true if it was pressed, false if it was not pressed.
	/// </summary>
	public readonly bool WasPressed { get; private init; }

	/// <summary>
	/// How much <see cref="IsPressed"/> has changed since the last update.
	/// </summary>
	public readonly bool Delta { get; private init; }

	/// <summary>
	/// Whether or not this action is currently accessible (if false, then <see cref="IsPressed"/> will always be false and will never change).
	/// </summary>
	public readonly bool Active => _data.isActive;

	internal DigitalInput( DigitalInput? previous, VRSystem.BooleanAction action, InputSource inputSource )
	{
		_data = VRSystem.GetBooleanActionState( action, inputSource );

		if ( previous != null )
		{
			WasPressed = previous.Value.IsPressed;
			Delta = IsPressed != WasPressed;
		}
	}

	/// <summary>
	/// Implicitly returns <see cref="IsPressed"/> as a <see cref="bool"/>.
	/// </summary>
	public static implicit operator bool( DigitalInput o )
	{
		return o.IsPressed;
	}
}
