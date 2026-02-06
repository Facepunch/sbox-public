using Facepunch.XR;

namespace Sandbox.VR;

/// <summary>
/// Represents a VR analog input action (e.g. trigger)
/// </summary>
public struct AnalogInput
{
	internal InputFloatActionState _data;

	/// <summary>
	/// The current value of this input, from 0 to 1.
	/// </summary>
	public readonly float Value => _data.value;

	/// <summary>
	/// How much <see cref="Value"/> has changed since the last update, from 0 to 1.
	/// </summary>
	public readonly float Delta { get; private init; }

	/// <summary>
	/// Whether or not this action is currently accessible (if false, then <see cref="Value"/> will always be 0 and will never change).
	/// </summary>
	public readonly bool Active => _data.isActive;

	internal AnalogInput( AnalogInput? previous, VRSystem.FloatAction action, InputSource inputSource )
	{
		_data = VRSystem.GetFloatActionState( action, inputSource );

		if ( previous != null )
			Delta = Value - previous.Value.Value;
	}

	/// <summary>
	/// Implicitly returns <see cref="Value"/> as a <see cref="float"/>.
	/// </summary>
	public static implicit operator float( AnalogInput o )
	{
		return o.Value;
	}
}
