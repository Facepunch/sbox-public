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

	/// <summary>Sets and enables a linear velocity motor, in units per second.</summary>
	public void SetLinearMotor( float targetVelocity, float maxForce ) => _joint?.SetLinearMotor( targetVelocity, maxForce );

	/// <summary>Sets and enables a position spring motor.</summary>
	public void SetTargetPosition( float position, float frequency, float dampingRatio )
	{
		_joint?.SetLinearSpring( new Vector3( frequency, dampingRatio, position ) );
	}

	internal void ConfigureLimits( Vector2 limits, bool? enabled )
	{
		_joint?.SetLinearLimits( limits.x, limits.y );
		if ( enabled is bool state )
			_joint?.SetLinearLimitEnabled( state );
		else
			MinLength = limits.x;
	}
}
