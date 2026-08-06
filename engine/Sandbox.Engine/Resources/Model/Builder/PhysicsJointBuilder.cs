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

	protected PhysicsJointBuilder() { }
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

	/// <summary>
	/// Target angular velocity for the motor.
	/// </summary>
	public Vector3 TargetVelocity { get => Desc.AngularTargetVelocity; set => Desc.AngularTargetVelocity = value; }

	/// <summary>
	/// Maximum torque the motor may apply.
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
	public HingeJointBuilder WithTargetVelocity( Vector3 v ) { TargetVelocity = v; EnableMotor = true; return this; }

	/// <inheritdoc cref="MaxTorque"/>
	/// <param name="v">The maximum motor torque.</param>
	public HingeJointBuilder WithMaxTorque( float v ) { MaxTorque = v; return this; }

	internal HingeJointBuilder()
	{
		Desc.Type = PhysicsJointType.REVOLUTE_JOINT;
	}
}

/// <summary>
/// Builds a ball joint with optional swing and twist limits.
/// </summary>
public sealed class BallJointBuilder : PhysicsJointBuilder
{
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

	internal BallJointBuilder()
	{
		Desc.Type = PhysicsJointType.SPHERICAL_JOINT;
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

	internal SliderJointBuilder()
	{
		Desc.Type = PhysicsJointType.PRISMATIC_JOINT;
	}
}
