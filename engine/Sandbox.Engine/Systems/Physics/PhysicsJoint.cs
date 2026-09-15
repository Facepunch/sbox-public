namespace Sandbox.Physics;

/// <summary>
/// A physics constraint.
/// </summary>
public partial class PhysicsJoint : IValid
{
	internal PhysicsJointInternal _joint;

	public bool IsValid => _joint is not null && _joint.IsValid;

	internal PhysicsJoint() { }

	internal PhysicsJoint( PhysicsJointInternal joint )
	{
		_joint = joint;
		_joint.SetOwner( this );
	}

	internal PhysicsJointType JointType => _joint?.JointType ?? default;

	/// <summary>
	/// Removes this joint.
	/// </summary>
	public void Remove()
	{
		_joint?.Remove();
	}

	internal void InternalJointBroken()
	{
		_joint?.InternalJointBroken();
	}

	internal void WakeBodies()
	{
		_joint?.WakeBodies();
	}

	/// <summary>
	/// Called when the joint breaks.
	/// </summary>
	public event Action OnBreak
	{
		add => _joint.OnBreak += value;
		remove => _joint.OnBreak -= value;
	}

	/// <summary>
	/// The <see cref="PhysicsWorld"/> this joint belongs to.
	/// </summary>
	public PhysicsWorld World => _joint?.World?.Owner;

	/// <summary>
	/// The source physics body this joint is attached to.
	/// </summary>
	public PhysicsBody Body1 => _joint?.Body1;

	/// <summary>
	/// The target physics body this joint is constraining.
	/// </summary>
	public PhysicsBody Body2 => _joint?.Body2;

	/// <summary>
	/// A specific point this joint is attached at on <see cref="Body1"/>
	/// </summary>
	public PhysicsPoint Point1
	{
		get
		{
			if ( _joint is null ) return default;
			_joint.GetLocalFrameA( out var position, out var rotation );
			return new( _joint.Body1, position, rotation );
		}
		set => _joint?.SetLocalFrameA( value.LocalPosition, value.LocalRotation );
	}

	/// <summary>
	/// A specific point this joint is attached at on <see cref="Body2"/>
	/// </summary>
	public PhysicsPoint Point2
	{
		get
		{
			if ( _joint is null ) return default;
			_joint.GetLocalFrameB( out var position, out var rotation );
			return new( _joint.Body2, position, rotation );
		}
		set => _joint?.SetLocalFrameB( value.LocalPosition, value.LocalRotation );
	}

	[Obsolete]
	public bool IsActive
	{
		get => true;
		set { }
	}

	/// <summary>
	/// Enables or disables collisions between the 2 constrained physics bodies.
	/// </summary>
	public bool Collisions
	{
		get => _joint?.Collisions ?? false;
		set => _joint?.Collisions = value;
	}

	/// <summary>
	/// Strength of the linear constraint. If it takes any more energy than this, it'll break.
	/// </summary>
	public float Strength
	{
		get => _joint?.Strength ?? 0;
		set => _joint?.Strength = value;
	}

	/// <summary>
	/// Strength of the angular constraint. If it takes any more energy than this, it'll break.
	/// </summary>
	public float AngularStrength
	{
		get => _joint?.AngularStrength ?? 0;
		set => _joint?.AngularStrength = value;
	}

	internal float LinearImpulse => _joint?.LinearImpulse ?? 0;
	internal float AngularImpulse => _joint?.AngularImpulse ?? 0;

	static void ValidateCreate( PhysicsBody a, PhysicsBody b )
	{
		ArgumentNullException.ThrowIfNull( a, nameof( a ) );
		ArgumentNullException.ThrowIfNull( b, nameof( b ) );

		Assert.AreEqual( a.World, b.World );
		Assert.AreNotEqual( a, b );
	}

	/// <summary>
	/// Creates an almost solid constraint between two physics bodies.
	/// </summary>
	public static FixedJoint CreateFixed( PhysicsPoint a, PhysicsPoint b )
	{
		ValidateCreate( a.Body, b.Body );
		return a.Body.World.CreateWeldJoint( a.Body, b.Body, a.LocalTransform, b.LocalTransform );
	}

	/// <summary>
	/// Creates a constraint like a rope, where it has no minimum length but its max length is restrained.
	/// </summary>
	public static SpringJoint CreateLength( PhysicsPoint a, PhysicsPoint b, float maxLength )
	{
		ValidateCreate( a.Body, b.Body );

		var joint = a.Body.World.CreateSpringJoint( a.Body, b.Body, a.LocalTransform, b.LocalTransform );
		joint.MaxLength = maxLength;
		joint.MinLength = 0;

		return joint;
	}

	/// <summary>
	/// Creates a constraint that will try to stay the same length, like a spring, or a rod.
	/// </summary>
	public static SpringJoint CreateSpring( PhysicsPoint a, PhysicsPoint b, float minLength, float maxLength )
	{
		ValidateCreate( a.Body, b.Body );

		var joint = a.Body.World.CreateSpringJoint( a.Body, b.Body, a.LocalTransform, b.LocalTransform );
		joint.MaxLength = maxLength;
		joint.MinLength = minLength;

		return joint;
	}

	public static HingeJoint CreateHinge( PhysicsPoint a, PhysicsPoint b )
	{
		ValidateCreate( a.Body, b.Body );
		return a.Body.World.CreateRevoluteJoint( a.Body, b.Body, a.LocalTransform, b.LocalTransform );
	}

	public static HingeJoint CreateHinge( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 )
	{
		ValidateCreate( body1, body2 );

		if ( !body2.MotionEnabled && body1.MotionEnabled )
		{
			(body1, body2) = (body2, body1);
			(localFrame1, localFrame2) = (localFrame2, localFrame1);
		}

		return body1.World.CreateRevoluteJoint( body1, body2, localFrame1, localFrame2 );
	}

	/// <summary>
	/// Creates a slider constraint between two physics bodies via <see cref="PhysicsPoint"/>s.
	/// </summary>
	public static SliderJoint CreateSlider( PhysicsPoint a, PhysicsPoint b, float minLength, float maxLength )
	{
		ValidateCreate( a.Body, b.Body );

		var joint = a.Body.World.CreatePrismaticJoint( a.Body, b.Body, a.LocalTransform, b.LocalTransform );
		joint.MaxLength = maxLength;
		joint.MinLength = minLength;

		return joint;
	}

	/// <summary>
	/// Creates a ball socket constraint.
	/// </summary>
	public static BallSocketJoint CreateBallSocket( PhysicsBody body1, PhysicsBody body2, Vector3 origin )
	{
		ValidateCreate( body1, body2 );

		var anchor = new Transform( origin );
		var localFrame1 = anchor.ToLocal( body1.Transform );
		var localFrame2 = anchor.ToLocal( body2.Transform );

		return body1.World.CreateSphericalJoint( body1, body2, localFrame1, localFrame2 );
	}

	/// <summary>
	/// Creates a ball socket constraint.
	/// </summary>
	public static BallSocketJoint CreateBallSocket( PhysicsPoint a, PhysicsPoint b )
	{
		ValidateCreate( a.Body, b.Body );
		return a.Body.World.CreateSphericalJoint( a.Body, b.Body, a.LocalTransform, b.LocalTransform );
	}

	public static ControlJoint CreateControl( PhysicsPoint a, PhysicsPoint b )
	{
		ValidateCreate( a.Body, b.Body );
		return a.Body.World.CreateMotorJoint( a.Body, b.Body, a.LocalTransform, b.LocalTransform );
	}

	internal static WheelJoint CreateWheel( PhysicsPoint a, PhysicsPoint b )
	{
		ValidateCreate( a.Body, b.Body );
		return a.Body.World.CreateWheelJoint( a.Body, b.Body, a.LocalTransform, b.LocalTransform );
	}

	public static PhysicsJoint CreateFilter( PhysicsBody a, PhysicsBody b )
	{
		ValidateCreate( a, b );
		return a.World.CreateFilterJoint( a, b );
	}

	public static UprightJoint CreateUpright( PhysicsPoint a, PhysicsPoint b )
	{
		ValidateCreate( a.Body, b.Body );
		return a.Body.World.CreateParallelJoint( a.Body, b.Body, a.LocalTransform, b.LocalTransform );
	}

	[Obsolete]
	public static HingeJoint CreateHinge( PhysicsBody body1, PhysicsBody body2, Vector3 center, Vector3 axis )
	{
		throw new Exception( $"Unable to create joint" );
	}

	[Obsolete]
	public static SliderJoint CreateSlider( PhysicsBody body1, PhysicsBody body2, Vector3 origin1, Vector3 origin2, Vector3 axis, float minLength, float maxLength )
	{
		throw new Exception( $"Unable to create joint" );
	}

	[Obsolete]
	public static PulleyJoint CreatePulley( PhysicsBody body1, PhysicsBody body2, Vector3 anchor1, Vector3 ground1, Vector3 anchor2, Vector3 ground2 )
	{
		throw new Exception( $"We don't have pulley!" );
	}

	public sealed override int GetHashCode() => base.GetHashCode();
	public sealed override bool Equals( object obj ) => base.Equals( obj );
}
