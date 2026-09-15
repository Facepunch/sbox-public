namespace Sandbox.Physics;

/// <summary>
/// A hinge-like constraint.
/// </summary>
public partial class HingeJoint : PhysicsJoint
{
	internal HingeJoint( PhysicsJointInternal joint ) : base( joint ) { }

	/// <summary>
	/// Maximum angle it should be allowed to go
	/// </summary>
	public float MaxAngle
	{
		get => _joint?.MaxLength ?? 0;
		set => _joint?.MaxLength = value.DegreeToRadian();
	}

	/// <summary>
	/// Minimum angle it should be allowed to go
	/// </summary>
	public float MinAngle
	{
		get => _joint?.MinLength ?? 0;
		set => _joint?.MinLength = value.DegreeToRadian();
	}

	public float Angle => (_joint?.Angle ?? 0).RadianToDegree();

	public Vector3 Axis
	{
		get
		{
			if ( _joint is null ) return default;
			_joint.GetLocalFrameA( out _, out var rotation );
			return rotation * Vector3.Up;
		}
	}

	public float Speed => Axis.Dot( Body2.AngularVelocity - Body1.AngularVelocity );

	/// <summary>
	/// Hinge friction.
	/// </summary>
	public float Friction
	{
		set => _joint?.Friction = value;
	}

	/// <summary>
	/// Set the angular spring parameters (target angle in radians, frequency, damping ratio).
	/// </summary>
	public void SetAngularSpring( Vector3 parameters ) => _joint?.SetAngularSpring( parameters );

	/// <summary>
	/// Set the angular motor (target velocity in radians/sec, max torque).
	/// </summary>
	public void SetAngularMotor( float targetVelocity, float maxTorque ) => _joint?.SetAngularMotor( targetVelocity, maxTorque );
}
