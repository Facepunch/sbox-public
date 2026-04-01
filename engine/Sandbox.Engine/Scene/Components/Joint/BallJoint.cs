namespace Sandbox;

/// <summary>
/// Fix two objects together but can rotate - like a shoulder.
/// </summary>
[Expose]
[Title( "Ball Joint" )]
[Category( "Physics" )]
[Icon( "waves" )]
[EditorHandle( "materials/gizmo/spring.png" )]
public sealed class BallJoint : Joint
{
	public enum MotorMode
	{
		/// <summary>
		/// The motor is disabled and only friction is applied.
		/// </summary>
		Disabled,

		/// <summary>
		/// The motor drives the joint towards a target rotation using frequency and damping.
		/// </summary>
		TargetRotation,

		/// <summary>
		/// The motor drives the joint with a target angular velocity and maximum torque.
		/// </summary>
		TargetVelocity
	}

	/// <summary>
	/// Motor mode
	/// </summary>
	[Property, Group( "Motor" )]
	public MotorMode Motor
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;

			if ( _joint.IsValid() )
			{
				ApplyMotor();
				_joint.WakeBodies();
			}
		}
	}

	/// <summary>
	/// Enables or disables the swing limit.
	/// </summary>
	[Property]
	public bool SwingLimitEnabled
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.SwingLimitEnabled = value;
				_joint.WakeBodies();
			}
		}
	} = false;

	/// <summary>
	/// The minimum and maximum swing angles allowed by the joint in degrees.
	/// </summary>
	[Property]
	public Vector2 SwingLimit
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.SwingLimit = value;
				_joint.WakeBodies();
			}
		}
	} = new( 0, 90 );

	/// <summary>
	/// Enables or disables the twist limit.
	/// </summary>
	[Property]
	public bool TwistLimitEnabled
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.TwistLimitEnabled = value;
				_joint.WakeBodies();
			}
		}
	} = false;

	/// <summary>
	/// The minimum and maximum twist angles allowed by the joint in degrees.
	/// </summary>
	[Property]
	public Vector2 TwistLimit
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.TwistLimit = value;
				_joint.WakeBodies();
			}
		}
	} = new( -15, 15 );

	/// <summary>
	/// Joint friction.
	/// </summary>
	[Property, Group( "Motor" ), ShowIf( nameof( Motor ), MotorMode.Disabled )]
	public float Friction
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;

			if ( _joint.IsValid() )
			{
				ApplyMotor();
				_joint.WakeBodies();
			}
		}
	} = 0.5f;

	/// <summary>
	/// Target angle of motor.
	/// </summary>
	[Property, Group( "Motor" ), ShowIf( nameof( Motor ), MotorMode.TargetRotation )]
	public Rotation TargetRotation
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;

			if ( _joint.IsValid() )
			{
				ApplyMotor();
				_joint.WakeBodies();
			}
		}
	}

	/// <summary>
	/// Frequency of motor.
	/// </summary>
	[Property, Group( "Motor" ), ShowIf( nameof( Motor ), MotorMode.TargetRotation )]
	public float Frequency
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;

			if ( _joint.IsValid() )
			{
				ApplyMotor();
				_joint.WakeBodies();
			}
		}
	} = 1.0f;

	/// <summary>
	/// Damping of motor.
	/// </summary>
	[Property, Group( "Motor" ), ShowIf( nameof( Motor ), MotorMode.TargetRotation )]
	public float DampingRatio
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;

			if ( _joint.IsValid() )
			{
				ApplyMotor();
				_joint.WakeBodies();
			}
		}
	} = 1.0f;

	/// <summary>
	/// Target angular velocity of the motor.
	/// </summary>
	[Property, Group( "Motor" ), ShowIf( nameof( Motor ), MotorMode.TargetVelocity )]
	public Vector3 TargetVelocity
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;

			if ( _joint.IsValid() )
			{
				ApplyMotor();
				_joint.WakeBodies();
			}
		}
	} = 0.0f;

	/// <summary>
	/// Maximum torque the motor can apply when in velocity mode.
	/// </summary>
	[Property, Group( "Motor" ), ShowIf( nameof( Motor ), MotorMode.TargetVelocity )]
	public float MaxTorque
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;

			if ( _joint.IsValid() )
			{
				ApplyMotor();
				_joint.WakeBodies();
			}
		}
	} = 0.0f;

	BallSocketJoint _joint;

	protected override PhysicsJoint CreateJoint( PhysicsPoint point1, PhysicsPoint point2 )
	{
		var localFrame1 = LocalFrame1;
		var localFrame2 = LocalFrame2;

		if ( Attachment == AttachmentMode.Auto )
		{
			localFrame1 = point1.LocalTransform;
			localFrame2 = point2.LocalTransform;
		}

		if ( !Scene.IsEditor )
		{
			LocalFrame1 = localFrame1;
			LocalFrame2 = localFrame2;

			Attachment = AttachmentMode.LocalFrames;
		}

		point1.LocalTransform = localFrame1;
		point2.LocalTransform = localFrame2;

		_joint = PhysicsJoint.CreateBallSocket( point1, point2 );

		_joint.SwingLimitEnabled = SwingLimitEnabled;
		_joint.SwingLimit = SwingLimit;
		_joint.TwistLimitEnabled = TwistLimitEnabled;
		_joint.TwistLimit = TwistLimit;

		ApplyMotor();

		_joint.WakeBodies();

		return _joint;
	}

	void ApplyMotor()
	{
		if ( !_joint.IsValid() )
			return;

		switch ( Motor )
		{
			case MotorMode.Disabled:
				_joint.Friction = Friction;
				break;

			case MotorMode.TargetRotation:
				_joint.native.SetTargetRotation( TargetRotation, Frequency, DampingRatio );
				break;

			case MotorMode.TargetVelocity:
				_joint.native.SetMotorVelocity( TargetVelocity, MaxTorque );
				break;
		}
	}
}
