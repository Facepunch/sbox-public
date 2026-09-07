namespace Sandbox.Physics;

/// <summary>
/// A ballsocket constraint.
/// </summary>
public partial class BallSocketJoint : PhysicsJoint
{
	internal BallSocketJoint( PhysicsJointInternal joint ) : base( joint ) { }

	Vector2 _swingLimit;
	Vector2 _twistLimit;

	/// <summary>
	/// Constraint friction.
	/// </summary>
	public float Friction
	{
		set => _joint?.Friction = value;
	}

	/// <summary>
	/// Maximum angle it should be allowed to swing to
	/// </summary>
	public Vector2 SwingLimit
	{
		get => _swingLimit;
		set
		{
			if ( _swingLimit == value ) return;
			_swingLimit = value;
			_joint?.SetLimit( "swing", _swingLimit );
		}
	}

	public bool SwingLimitEnabled
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			_joint?.SetLimitEnabled( "swing", field );
		}
	}

	public Vector2 TwistLimit
	{
		get => _twistLimit;
		set
		{
			if ( _twistLimit == value ) return;
			_twistLimit = value;
			_joint?.SetLimit( "twist", _twistLimit );
		}
	}

	public bool TwistLimitEnabled
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			_joint?.SetLimitEnabled( "twist", field );
		}
	}

	/// <summary>
	/// Set the target rotation motor (rotation, frequency, damping ratio).
	/// </summary>
	public void SetTargetRotation( Rotation rotation, float hertz, float damping ) => _joint?.SetTargetRotation( rotation, hertz, damping );

	/// <summary>
	/// Set the motor velocity and max torque.
	/// </summary>
	public void SetMotorVelocity( Vector3 velocity, float maxTorque ) => _joint?.SetMotorVelocity( velocity, maxTorque );
}
