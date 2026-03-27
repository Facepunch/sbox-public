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
	[Group( "Motor" )]
	[Property]
	public MotorMode Motor
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
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
			field = value;

			OnPropertyDirty();
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
			field = value;

			OnPropertyDirty();
		}
	} = new Vector2( 0, 90 );

	/// <summary>
	/// Enables or disables the twist limit.
	/// </summary>
	[Property]
	public bool TwistLimitEnabled
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
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
			field = value;

			OnPropertyDirty();
		}
	} = new Vector2( -15, 15 );

	/// <summary>
	/// Joint friction.
	/// </summary>
	[Group( "Motor" )]
	[Property, ShowIf( nameof( Motor ), MotorMode.Disabled )]
	public float Friction
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	} = 0.5f;

	/// <summary>
	/// Target angle of motor.
	/// </summary>
	[Group( "Motor" )]
	[Property, ShowIf( nameof( Motor ), MotorMode.TargetRotation )]
	public Rotation TargetRotation
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	}

	/// <summary>
	/// Frequency of motor.
	/// </summary>
	[Group( "Motor" )]
	[Property, ShowIf( nameof( Motor ), MotorMode.TargetRotation )]
	public float Frequency
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	} = 1.0f;

	/// <summary>
	/// Damping of motor.
	/// </summary>
	[Group( "Motor" )]
	[Property, ShowIf( nameof( Motor ), MotorMode.TargetRotation )]
	public float DampingRatio
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	} = 1.0f;

	/// <summary>
	/// Target angular velocity of the motor.
	/// </summary>
	[Group( "Motor" )]
	[Property, ShowIf( nameof( Motor ), MotorMode.TargetVelocity )]
	public Vector3 TargetVelocity
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
		}
	} = 0.0f;

	/// <summary>
	/// Maximum torque the motor can apply when in velocity mode.
	/// </summary>
	[Group( "Motor" )]
	[Property, ShowIf( nameof( Motor ), MotorMode.TargetVelocity )]
	public float MaxTorque
	{
		get;
		set
		{
			field = value;

			OnPropertyDirty();
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

		UpdateProperties();

		return _joint;
	}

	protected override void OnDirty()
	{
		base.OnDirty();

		UpdateProperties();
	}

	private void UpdateProperties()
	{
		if ( !_joint.IsValid() )
			return;

		_joint.SwingLimitEnabled = SwingLimitEnabled;
		_joint.SwingLimit = SwingLimit;
		_joint.TwistLimitEnabled = TwistLimitEnabled;
		_joint.TwistLimit = TwistLimit;

		if ( Motor == MotorMode.Disabled )
		{
			_joint.Friction = Friction;
		}
		else if ( Motor == MotorMode.TargetRotation )
		{
			_joint.native.SetTargetRotation( TargetRotation, Frequency, DampingRatio );
		}
		else if ( Motor == MotorMode.TargetVelocity )
		{
			_joint.native.SetMotorVelocity( TargetVelocity, MaxTorque );
		}

		_joint.WakeBodies();
	}
}
