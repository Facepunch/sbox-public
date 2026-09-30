namespace Sandbox.Physics;

/// <summary>
/// A slider constraint, basically allows movement only on the arbitrary axis between the 2 constrained objects on creation.
/// </summary>
public partial class SliderJoint : PhysicsJoint
{
	internal SliderJoint( PhysicsJointInternal joint ) : base( joint ) { }

	/// <summary>
	/// Maximum length it should be allowed to go
	/// </summary>
	public float MaxLength
	{
		get => _joint?.MaxLength ?? 0;
		set => _joint?.MaxLength = value;
	}

	/// <summary>
	/// Minimum length it should be allowed to go
	/// </summary>
	public float MinLength
	{
		get => _joint?.MinLength ?? 0;
		set => _joint?.MinLength = value;
	}

	/// <summary>
	/// Slider friction.
	/// </summary>
	public float Friction
	{
		set => _joint?.Friction = value;
	}
}
