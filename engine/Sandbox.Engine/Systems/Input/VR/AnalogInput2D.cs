using Facepunch.XR;

namespace Sandbox.VR;

/// <summary>
/// Represents a two-dimensional VR analog input action (e.g. joysticks)
/// </summary>
public struct AnalogInput2D
{
	internal readonly InputVector2ActionState _data;

	/// <summary>
	/// The current value of this input, with both axes ranging from 0 to 1.
	/// </summary>
	public readonly Vector2 Value => new Vector2( _data.x, _data.y );

	/// <summary>
	/// How much <see cref="Value"/> has changed since the last update, with both axes ranging from 0 to 1.
	/// </summary>
	public readonly Vector2 Delta { get; private init; }

	/// <summary>
	/// Whether or not this action is currently accessible (if false, then <see cref="Value"/> will always be 0 and will never change).
	/// </summary>
	public readonly bool Active => _data.isActive;

	internal AnalogInput2D( AnalogInput2D? previous, VRSystem.Vector2Action action, InputSource inputSource )
	{
		_data = VRSystem.GetVector2ActionState( action, inputSource );

		if ( previous != null )
			Delta = Value - previous.Value.Value;
	}

	/// <summary>
	/// Implicitly returns <see cref="Value"/> as a <see cref="Vector2"/>.
	/// </summary>
	public static implicit operator Vector2( AnalogInput2D o )
	{
		return o.Value;
	}
}
