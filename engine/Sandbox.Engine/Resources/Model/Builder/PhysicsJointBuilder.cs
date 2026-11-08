namespace Sandbox;

/// <summary>
/// Configures a joint between two model physics bodies.
/// </summary>
public abstract class PhysicsJointBuilder
{
	internal struct JointDesc
	{
		public PhysicsJointType Type;
		public int Body1, Body2;
		public ushort Flags;
		public bool EnableCollision, EnableLinearLimit, EnableLinearMotor;
		public Vector3 LinearTargetVelocity;
		public float MaxForce;
		public bool EnableSwingLimit, EnableTwistLimit, EnableAngularMotor;
		public Vector3 AngularTargetVelocity;
		public float MaxTorque, LinearFrequency, LinearDamping, AngularFrequency, AngularDamping;
		public float LinearStrength, AngularStrength;
		public Transform Frame1, Frame2;
		public Vector2 LinearLimit, SwingLimit, TwistLimit;
		public bool LinearMotorIsSpring, AngularMotorIsSpring, OverrideFriction;
		public float LinearTargetPosition, AngularTargetAngle, Friction;
		public Rotation AngularTargetRotation;
	}

	internal JointDesc Desc;

	/// <summary>
	/// The index of the first connected body, using the order bodies were added to the model.
	/// </summary>
	public int Body1 { get => Desc.Body1; set => Desc.Body1 = value; }

	/// <summary>
	/// The index of the second connected body, using the order bodies were added to the model.
	/// </summary>
	public int Body2 { get => Desc.Body2; set => Desc.Body2 = value; }

	/// <summary>
	/// The joint frame in the local space of <see cref="Body1"/>.
	/// </summary>
	public Transform Frame1 { get => Desc.Frame1; set => Desc.Frame1 = value; }

	/// <summary>
	/// The joint frame in the local space of <see cref="Body2"/>.
	/// </summary>
	public Transform Frame2 { get => Desc.Frame2; set => Desc.Frame2 = value; }

	/// <summary>
	/// Whether the connected bodies can collide with each other.
	/// </summary>
	public bool EnableCollision { get => Desc.EnableCollision; set => Desc.EnableCollision = value; }

	/// <summary>
	/// The maximum linear force the joint can withstand before breaking.
	/// </summary>
	public float LinearStrength { get => Desc.LinearStrength; set => Desc.LinearStrength = value; }

	/// <summary>
	/// The maximum torque the joint can withstand before breaking.
	/// </summary>
	public float AngularStrength { get => Desc.AngularStrength; set => Desc.AngularStrength = value; }

	protected PhysicsJointBuilder()
	{
		Desc.AngularTargetRotation = Rotation.Identity;
	}

	internal void Validate( int bodyCount )
	{
		if ( Body1 < 0 || Body1 >= bodyCount || Body1 > ushort.MaxValue )
			throw new ArgumentOutOfRangeException( nameof( Body1 ) );
		if ( Body2 < 0 || Body2 >= bodyCount || Body2 > ushort.MaxValue )
			throw new ArgumentOutOfRangeException( nameof( Body2 ) );
		if ( Body1 == Body2 )
			throw new ArgumentException( "A joint must connect two different bodies." );

		ValidateFrame( Frame1, nameof( Frame1 ) );
		ValidateFrame( Frame2, nameof( Frame2 ) );
		ValidateNonnegative( LinearStrength, nameof( LinearStrength ) );
		ValidateNonnegative( AngularStrength, nameof( AngularStrength ) );
		ValidateNonnegative( Desc.LinearFrequency, nameof( Desc.LinearFrequency ) );
		ValidateNonnegative( Desc.LinearDamping, nameof( Desc.LinearDamping ) );
		ValidateNonnegative( Desc.AngularFrequency, nameof( Desc.AngularFrequency ) );
		ValidateNonnegative( Desc.AngularDamping, nameof( Desc.AngularDamping ) );

		if ( Desc.EnableLinearLimit ) ValidateRange( Desc.LinearLimit, nameof( Desc.LinearLimit ) );
		if ( Desc.EnableSwingLimit ) ValidateRange( Desc.SwingLimit, nameof( Desc.SwingLimit ) );
		if ( Desc.EnableTwistLimit ) ValidateRange( Desc.TwistLimit, nameof( Desc.TwistLimit ) );
		if ( Desc.OverrideFriction ) ValidateNonnegative( Desc.Friction, nameof( Desc.Friction ) );

		if ( Desc.EnableLinearMotor )
		{
			ValidateNonnegative( Desc.MaxForce, nameof( Desc.MaxForce ) );
			if ( Desc.LinearMotorIsSpring )
			{
				if ( !float.IsFinite( Desc.LinearTargetPosition ) )
					throw new ArgumentOutOfRangeException( nameof( Desc.LinearTargetPosition ) );
			}
			else if ( !Desc.LinearTargetVelocity.IsFinite )
			{
				throw new ArgumentOutOfRangeException( nameof( Desc.LinearTargetVelocity ) );
			}
		}

		if ( Desc.EnableAngularMotor )
		{
			ValidateNonnegative( Desc.MaxTorque, nameof( Desc.MaxTorque ) );
			if ( Desc.AngularMotorIsSpring )
			{
				if ( !float.IsFinite( Desc.AngularTargetAngle ) )
					throw new ArgumentOutOfRangeException( nameof( Desc.AngularTargetAngle ) );
				ValidateRotation( Desc.AngularTargetRotation, nameof( Desc.AngularTargetRotation ) );
			}
			else if ( !Desc.AngularTargetVelocity.IsFinite )
			{
				throw new ArgumentOutOfRangeException( nameof( Desc.AngularTargetVelocity ) );
			}
		}
	}

	static void ValidateNonnegative( float value, string name )
	{
		if ( !float.IsFinite( value ) || value < 0 )
			throw new ArgumentOutOfRangeException( name, "Joint settings must be finite and nonnegative." );
	}

	static void ValidateRange( Vector2 range, string name )
	{
		if ( !float.IsFinite( range.x ) || !float.IsFinite( range.y ) || range.x > range.y )
			throw new ArgumentOutOfRangeException( name, "Joint limits must be finite and ordered." );
	}

	static void ValidateFrame( Transform frame, string name )
	{
		if ( !frame.Position.IsFinite )
			throw new ArgumentOutOfRangeException( name, "Joint positions must be finite." );
		ValidateRotation( frame.Rotation, name );
	}

	static void ValidateRotation( Rotation rotation, string name )
	{
		var length = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
		if ( !float.IsFinite( length ) || MathF.Abs( length - 1 ) > 0.001f )
			throw new ArgumentOutOfRangeException( name, "Joint rotations must be finite unit quaternions." );
	}
}

/// <summary>
/// Fluent configuration methods shared by all physics joint builders.
/// </summary>
public static class PhysicsJointBuilderExtensions
{
	/// <summary>Sets the first connected body.</summary>
	/// <param name="b">The joint builder.</param>
	/// <param name="v">The body index.</param>
	/// <returns>The joint builder.</returns>
	public static T WithBody1<T>( this T b, int v ) where T : PhysicsJointBuilder { b.Body1 = v; return b; }

	/// <summary>Sets the second connected body.</summary>
	/// <param name="b">The joint builder.</param>
	/// <param name="v">The body index.</param>
	/// <returns>The joint builder.</returns>
	public static T WithBody2<T>( this T b, int v ) where T : PhysicsJointBuilder { b.Body2 = v; return b; }

	/// <summary>Sets the joint frame in the first body's local space.</summary>
	/// <param name="b">The joint builder.</param>
	/// <param name="v">The local joint frame.</param>
	/// <returns>The joint builder.</returns>
	public static T WithFrame1<T>( this T b, Transform v ) where T : PhysicsJointBuilder { b.Frame1 = v; return b; }

	/// <summary>Sets the joint frame in the second body's local space.</summary>
	/// <param name="b">The joint builder.</param>
	/// <param name="v">The local joint frame.</param>
	/// <returns>The joint builder.</returns>
	public static T WithFrame2<T>( this T b, Transform v ) where T : PhysicsJointBuilder { b.Frame2 = v; return b; }

	/// <summary>Sets whether the connected bodies can collide.</summary>
	/// <param name="b">The joint builder.</param>
	/// <param name="v">Whether collision is enabled.</param>
	/// <returns>The joint builder.</returns>
	public static T WithCollision<T>( this T b, bool v ) where T : PhysicsJointBuilder { b.EnableCollision = v; return b; }

	/// <summary>Sets the joint's breaking force.</summary>
	/// <param name="b">The joint builder.</param>
	/// <param name="v">The maximum linear force.</param>
	/// <returns>The joint builder.</returns>
	public static T WithLinearStrength<T>( this T b, float v ) where T : PhysicsJointBuilder { b.LinearStrength = v; return b; }

	/// <summary>Sets the joint's breaking torque.</summary>
	/// <param name="b">The joint builder.</param>
	/// <param name="v">The maximum torque.</param>
	/// <returns>The joint builder.</returns>
	public static T WithAngularStrength<T>( this T b, float v ) where T : PhysicsJointBuilder { b.AngularStrength = v; return b; }
}

/// <summary>
/// Builds a hinge joint that rotates around one axis.
/// </summary>
public sealed class HingeJointBuilder : PhysicsJointBuilder
{
	/// <summary>
	/// Whether the hinge enforces a twist angle limit.
	/// </summary>
	public bool EnableTwistLimit { get => Desc.EnableTwistLimit; set => Desc.EnableTwistLimit = value; }

	/// <summary>
	/// The minimum and maximum allowed twist angles (degrees).
	/// </summary>
	public Vector2 TwistLimit { get => Desc.TwistLimit; set => Desc.TwistLimit = value; }

	/// <summary>
	/// Whether the hinge's angular motor is enabled.
	/// </summary>
	public bool EnableMotor { get => Desc.EnableAngularMotor; set => Desc.EnableAngularMotor = value; }

	/// <summary>The motor mode, matching the hinge component.</summary>
	public HingeJoint.MotorMode Motor
	{
		get => !EnableMotor ? HingeJoint.MotorMode.Disabled : Desc.AngularMotorIsSpring ? HingeJoint.MotorMode.TargetAngle : HingeJoint.MotorMode.TargetVelocity;
		set
		{
			if ( !Enum.IsDefined( value ) ) throw new ArgumentOutOfRangeException( nameof( value ) );
			EnableMotor = value != HingeJoint.MotorMode.Disabled;
			Desc.AngularMotorIsSpring = value == HingeJoint.MotorMode.TargetAngle;
		}
	}

	/// <summary>Spring motor target angle in degrees.</summary>
	public float TargetAngle { get => Desc.AngularTargetAngle; set => Desc.AngularTargetAngle = value; }

	/// <summary>Spring motor frequency in hertz.</summary>
	public float Frequency { get => Desc.AngularFrequency; set => Desc.AngularFrequency = value; }

	/// <summary>Spring motor damping ratio.</summary>
	public float DampingRatio { get => Desc.AngularDamping; set => Desc.AngularDamping = value; }

	/// <summary>Component-style friction, used when the motor is disabled.</summary>
	public float Friction { get => Desc.Friction; set { Desc.Friction = value; Desc.OverrideFriction = true; } }

	/// <summary>
	/// World angular velocity for the motor, in radians per second.
	/// </summary>
	public Vector3 TargetVelocity { get => Desc.AngularTargetVelocity; set => Desc.AngularTargetVelocity = value; }

	/// <summary>
	/// Maximum torque the velocity motor may apply.
	/// </summary>
	public float MaxTorque { get => Desc.MaxTorque; set => Desc.MaxTorque = value; }

	/// <summary>
	/// Sets and enables the twist angle limits.
	/// </summary>
	/// <param name="min">The minimum twist angle in degrees.</param>
	/// <param name="max">The maximum twist angle in degrees.</param>
	public HingeJointBuilder WithTwistLimit( float min, float max ) { TwistLimit = new Vector2( min, max ); EnableTwistLimit = true; return this; }

	/// <summary>
	/// Sets the target angular velocity and enables the motor.
	/// </summary>
	/// <param name="v">The target angular velocity.</param>
	public HingeJointBuilder WithTargetVelocity( Vector3 v ) { TargetVelocity = v; Motor = HingeJoint.MotorMode.TargetVelocity; return this; }

	/// <summary>Sets and enables the target-angle spring motor.</summary>
	public HingeJointBuilder WithTargetAngle( float angle )
	{
		TargetAngle = angle;
		Motor = HingeJoint.MotorMode.TargetAngle;
		return this;
	}

	/// <inheritdoc cref="Frequency"/>
	public HingeJointBuilder WithFrequency( float v ) { Frequency = v; return this; }

	/// <inheritdoc cref="DampingRatio"/>
	public HingeJointBuilder WithDampingRatio( float v ) { DampingRatio = v; return this; }

	/// <summary>Sets friction and disables the motor.</summary>
	public HingeJointBuilder WithFriction( float v ) { Friction = v; Motor = HingeJoint.MotorMode.Disabled; return this; }

	/// <inheritdoc cref="MaxTorque"/>
	/// <param name="v">The maximum motor torque.</param>
	public HingeJointBuilder WithMaxTorque( float v ) { MaxTorque = v; return this; }

	internal HingeJointBuilder()
	{
		Desc.Type = PhysicsJointType.REVOLUTE_JOINT;
		Frequency = 1;
		DampingRatio = 1;
	}
}

/// <summary>
/// Builds a ball joint with optional swing and twist limits.
/// </summary>
public sealed class BallJointBuilder : PhysicsJointBuilder
{
	/// <summary>Whether the joint's angular motor is enabled.</summary>
	public bool EnableMotor { get => Desc.EnableAngularMotor; set => Desc.EnableAngularMotor = value; }

	/// <summary>The motor mode, matching the ball component.</summary>
	public BallJoint.MotorMode Motor
	{
		get => !EnableMotor ? BallJoint.MotorMode.Disabled : Desc.AngularMotorIsSpring ? BallJoint.MotorMode.TargetRotation : BallJoint.MotorMode.TargetVelocity;
		set
		{
			if ( !Enum.IsDefined( value ) ) throw new ArgumentOutOfRangeException( nameof( value ) );
			EnableMotor = value != BallJoint.MotorMode.Disabled;
			Desc.AngularMotorIsSpring = value == BallJoint.MotorMode.TargetRotation;
		}
	}

	/// <summary>Spring motor target rotation relative to the joint frames.</summary>
	public Rotation TargetRotation { get => Desc.AngularTargetRotation; set => Desc.AngularTargetRotation = value; }

	/// <summary>Spring motor frequency in hertz.</summary>
	public float Frequency { get => Desc.AngularFrequency; set => Desc.AngularFrequency = value; }

	/// <summary>Spring motor damping ratio.</summary>
	public float DampingRatio { get => Desc.AngularDamping; set => Desc.AngularDamping = value; }

	/// <summary>Component-style friction, used when the motor is disabled.</summary>
	public float Friction { get => Desc.Friction; set { Desc.Friction = value; Desc.OverrideFriction = true; } }

	/// <summary>World angular velocity for the motor, in radians per second.</summary>
	public Vector3 TargetVelocity { get => Desc.AngularTargetVelocity; set => Desc.AngularTargetVelocity = value; }

	/// <summary>Maximum torque the velocity motor may apply.</summary>
	public float MaxTorque { get => Desc.MaxTorque; set => Desc.MaxTorque = value; }

	/// <summary>
	/// Whether the joint enforces a swing angle limit.
	/// </summary>
	public bool EnableSwingLimit { get => Desc.EnableSwingLimit; set => Desc.EnableSwingLimit = value; }

	/// <summary>
	/// Whether the joint enforces a twist angle limit.
	/// </summary>
	public bool EnableTwistLimit { get => Desc.EnableTwistLimit; set => Desc.EnableTwistLimit = value; }

	/// <summary>
	/// Maximum allowed swing angle in degrees.
	/// </summary>
	public float SwingLimit { get => Desc.SwingLimit.y; set => Desc.SwingLimit = new Vector2( 0, value ); }

	/// <summary>
	/// Minimum and maximum allowed twist angles in degrees.
	/// </summary>
	public Vector2 TwistLimit { get => Desc.TwistLimit; set => Desc.TwistLimit = value; }

	/// <summary>
	/// Sets and enables the swing angle limit.
	/// </summary>
	/// <param name="v">The maximum swing angle in degrees.</param>
	public BallJointBuilder WithSwingLimit( float v ) { SwingLimit = v; EnableSwingLimit = true; return this; }

	/// <summary>
	/// Sets and enables the twist angle limits.
	/// </summary>
	/// <param name="min">The minimum twist angle in degrees.</param>
	/// <param name="max">The maximum twist angle in degrees.</param>
	public BallJointBuilder WithTwistLimit( float min, float max ) { TwistLimit = new Vector2( min, max ); EnableTwistLimit = true; return this; }

	/// <summary>Sets the target angular velocity and enables the motor.</summary>
	public BallJointBuilder WithTargetVelocity( Vector3 v ) { TargetVelocity = v; Motor = BallJoint.MotorMode.TargetVelocity; return this; }

	/// <summary>Sets and enables the target-rotation spring motor.</summary>
	public BallJointBuilder WithTargetRotation( Rotation rotation )
	{
		TargetRotation = rotation;
		Motor = BallJoint.MotorMode.TargetRotation;
		return this;
	}

	/// <inheritdoc cref="Frequency"/>
	public BallJointBuilder WithFrequency( float v ) { Frequency = v; return this; }

	/// <inheritdoc cref="DampingRatio"/>
	public BallJointBuilder WithDampingRatio( float v ) { DampingRatio = v; return this; }

	/// <summary>Sets friction and disables the motor.</summary>
	public BallJointBuilder WithFriction( float v ) { Friction = v; Motor = BallJoint.MotorMode.Disabled; return this; }

	/// <inheritdoc cref="MaxTorque"/>
	public BallJointBuilder WithMaxTorque( float v ) { MaxTorque = v; return this; }

	internal BallJointBuilder()
	{
		Desc.Type = PhysicsJointType.SPHERICAL_JOINT;
		Desc.Friction = 0.5f;
		Frequency = 1;
		DampingRatio = 1;
	}
}

/// <summary>
/// Builds a fixed joint that locks the relative position and rotation of two bodies.
/// </summary>
public sealed class FixedJointBuilder : PhysicsJointBuilder
{
	/// <summary>
	/// The frequency of the joint's linear spring in hertz.
	/// Higher values make the joint stiffer in translation.
	/// </summary>
	public float LinearFrequency { get => Desc.LinearFrequency; set => Desc.LinearFrequency = value; }

	/// <summary>
	/// The damping ratio for the joint's linear spring.
	/// Higher values reduce oscillation in translation.
	/// </summary>
	public float LinearDamping { get => Desc.LinearDamping; set => Desc.LinearDamping = value; }

	/// <summary>
	/// The frequency of the joint's angular spring in hertz.
	/// Higher values make the joint stiffer in rotation.
	/// </summary>
	public float AngularFrequency { get => Desc.AngularFrequency; set => Desc.AngularFrequency = value; }

	/// <summary>
	/// The damping ratio for the joint's angular spring.
	/// Higher values reduce oscillation in rotation.
	/// </summary>
	public float AngularDamping { get => Desc.AngularDamping; set => Desc.AngularDamping = value; }

	/// <inheritdoc cref="LinearFrequency"/>
	/// <param name="v">The linear spring frequency.</param>
	public FixedJointBuilder WithLinearFrequency( float v ) { LinearFrequency = v; return this; }

	/// <inheritdoc cref="LinearDamping"/>
	/// <param name="v">The linear spring damping ratio.</param>
	public FixedJointBuilder WithLinearDamping( float v ) { LinearDamping = v; return this; }

	/// <inheritdoc cref="AngularFrequency"/>
	/// <param name="v">The angular spring frequency.</param>
	public FixedJointBuilder WithAngularFrequency( float v ) { AngularFrequency = v; return this; }

	/// <inheritdoc cref="AngularDamping"/>
	/// <param name="v">The angular spring damping ratio.</param>
	public FixedJointBuilder WithAngularDamping( float v ) { AngularDamping = v; return this; }

	internal FixedJointBuilder()
	{
		Desc.Type = PhysicsJointType.WELD_JOINT;
	}
}

/// <summary>
/// Builds a slider joint that moves along one axis.
/// </summary>
public sealed class SliderJointBuilder : PhysicsJointBuilder
{
	/// <summary>Whether a velocity or position motor is enabled.</summary>
	public bool EnableMotor { get => Desc.EnableLinearMotor; set => Desc.EnableLinearMotor = value; }

	/// <summary>The motor mode, matching the slider component.</summary>
	public SliderJoint.MotorMode Motor
	{
		get => !EnableMotor ? SliderJoint.MotorMode.Disabled : Desc.LinearMotorIsSpring ? SliderJoint.MotorMode.TargetPosition : SliderJoint.MotorMode.TargetVelocity;
		set
		{
			if ( !Enum.IsDefined( value ) ) throw new ArgumentOutOfRangeException( nameof( value ) );
			EnableMotor = value != SliderJoint.MotorMode.Disabled;
			Desc.LinearMotorIsSpring = value == SliderJoint.MotorMode.TargetPosition;
		}
	}

	/// <summary>World linear velocity for the motor, in units per second.</summary>
	public Vector3 TargetVelocity { get => Desc.LinearTargetVelocity; set => Desc.LinearTargetVelocity = value; }

	/// <summary>Spring motor target offset along the slider axis.</summary>
	public float TargetPosition { get => Desc.LinearTargetPosition; set => Desc.LinearTargetPosition = value; }

	/// <summary>Maximum force the velocity motor may apply.</summary>
	public float MaxForce { get => Desc.MaxForce; set => Desc.MaxForce = value; }

	/// <summary>Spring motor frequency in hertz.</summary>
	public float Frequency { get => Desc.LinearFrequency; set => Desc.LinearFrequency = value; }

	/// <summary>Spring motor damping ratio.</summary>
	public float DampingRatio { get => Desc.LinearDamping; set => Desc.LinearDamping = value; }

	/// <summary>Component-style friction, used when the motor is disabled.</summary>
	public float Friction { get => Desc.Friction; set { Desc.Friction = value; Desc.OverrideFriction = true; } }

	/// <summary>
	/// Whether the joint enforces a translation limit along its axis.
	/// </summary>
	public bool EnableLimit { get => Desc.EnableLinearLimit; set => Desc.EnableLinearLimit = value; }

	/// <summary>
	/// The minimum and maximum allowed translation along the joint axis.
	/// </summary>
	public Vector2 Limit { get => Desc.LinearLimit; set => Desc.LinearLimit = value; }

	/// <summary>
	/// Sets and enables the translation limits.
	/// </summary>
	/// <param name="min">The minimum translation along the joint axis.</param>
	/// <param name="max">The maximum translation along the joint axis.</param>
	public SliderJointBuilder WithLimit( float min, float max ) { Limit = new Vector2( min, max ); EnableLimit = true; return this; }

	/// <summary>Sets and enables the velocity motor.</summary>
	public SliderJointBuilder WithTargetVelocity( Vector3 v ) { TargetVelocity = v; Motor = SliderJoint.MotorMode.TargetVelocity; return this; }

	/// <summary>Sets and enables the target-position spring motor.</summary>
	public SliderJointBuilder WithTargetPosition( float position )
	{
		TargetPosition = position;
		Motor = SliderJoint.MotorMode.TargetPosition;
		return this;
	}

	/// <inheritdoc cref="MaxForce"/>
	public SliderJointBuilder WithMaxForce( float v ) { MaxForce = v; return this; }

	/// <inheritdoc cref="Frequency"/>
	public SliderJointBuilder WithFrequency( float v ) { Frequency = v; return this; }

	/// <inheritdoc cref="DampingRatio"/>
	public SliderJointBuilder WithDampingRatio( float v ) { DampingRatio = v; return this; }

	/// <summary>Sets friction and disables the motor.</summary>
	public SliderJointBuilder WithFriction( float v ) { Friction = v; Motor = SliderJoint.MotorMode.Disabled; return this; }

	internal SliderJointBuilder()
	{
		Desc.Type = PhysicsJointType.PRISMATIC_JOINT;
		Frequency = 1;
		DampingRatio = 1;
	}
}
