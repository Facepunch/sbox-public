namespace Sandbox.Physics;

/// <summary>
/// Internal base class for physics joints. Contains all joint functions and properties.
/// </summary>
internal abstract class PhysicsJointInternal : IValid
{
	public abstract bool IsValid { get; }

	PhysicsJoint _owner;

	/// <summary>
	/// The public wrapper for this joint. Created on demand for joints made by native code.
	/// </summary>
	internal PhysicsJoint Owner => _owner ??= new PhysicsJoint( this );

	internal void SetOwner( PhysicsJoint owner ) => _owner ??= owner;

	/// <summary>
	/// The physics world this joint belongs to.
	/// </summary>
	internal PhysicsWorldInternal World { get; set; }

	internal abstract PhysicsJointType JointType { get; }


	public abstract PhysicsBody Body1 { get; }
	public abstract PhysicsBody Body2 { get; }

	public abstract void GetLocalFrameA( out Vector3 position, out Rotation rotation );
	public abstract void GetLocalFrameB( out Vector3 position, out Rotation rotation );
	public abstract void SetLocalFrameA( Vector3 position, Rotation rotation );
	public abstract void SetLocalFrameB( Vector3 position, Rotation rotation );

	public abstract void Remove();

	public abstract bool Collisions { get; set; }

	public abstract float Strength { get; set; }
	public abstract float AngularStrength { get; set; }

	internal abstract float LinearImpulse { get; }
	internal abstract float AngularImpulse { get; }

	internal abstract void WakeBodies();


	public event Action OnBreak;

	internal void InternalJointBroken()
	{
		OnBreak?.Invoke();
	}


	public virtual PhysicsSpring SpringLinear { get; set; }
	public virtual PhysicsSpring SpringAngular { get; set; }


	public virtual float MinLength { get; set; }
	public virtual float MaxLength { get; set; }
	public virtual float MinForce { get; set; }
	public virtual float MaxForce { get; set; }
	public virtual float Friction { set { } }
	public virtual float Angle => 0;

	public virtual void SetLimit( string name, Vector2 limit ) { }
	public virtual void SetLimitEnabled( string name, bool state ) { }


	public virtual void SetAngularSpring( Vector3 parameters ) { }
	public virtual void SetAngularMotor( float targetVelocity, float maxTorque ) { }
	public virtual void SetTargetRotation( Rotation rotation, float hertz, float damping ) { }
	public virtual void SetMotorVelocity( Vector3 velocity, float maxTorque ) { }


	public virtual Vector3 Motor_LinearVelocity { get; set; }
	public virtual Vector3 Motor_AngularVelocity { get; set; }
	public virtual float Motor_MaxVelocityForce { get; set; }
	public virtual float Motor_MaxVelocityTorque { get; set; }
	public virtual float Motor_LinearHertz { get; set; }
	public virtual float Motor_LinearDampingRatio { get; set; }
	public virtual float Motor_AngularHertz { get; set; }
	public virtual float Motor_AngularDampingRatio { get; set; }
	public virtual float Motor_MaxSpringForce { get; set; }
	public virtual float Motor_MaxSpringTorque { get; set; }


	public virtual bool Wheel_EnableSuspension { get; set; }
	public virtual float Wheel_SuspensionHertz { get; set; }
	public virtual float Wheel_SuspensionDampingRatio { get; set; }
	public virtual bool Wheel_EnableSuspensionLimit { get; set; }
	public virtual float Wheel_LowerSuspensionLimit => 0;
	public virtual float Wheel_UpperSuspensionLimit => 0;
	public virtual void Wheel_SetSuspensionLimits( float lower, float upper ) { }
	public virtual bool Wheel_EnableSpinMotor { get; set; }
	public virtual float Wheel_SpinMotorSpeed { get; set; }
	public virtual float Wheel_MaxSpinTorque { get; set; }
	public virtual bool Wheel_EnableSteering { get; set; }
	public virtual float Wheel_SteeringHertz { get; set; }
	public virtual float Wheel_SteeringDampingRatio { get; set; }
	public virtual float Wheel_MaxSteeringTorque { get; set; }
	public virtual bool Wheel_EnableSteeringLimit { get; set; }
	public virtual float Wheel_LowerSteeringLimit => 0;
	public virtual float Wheel_UpperSteeringLimit => 0;
	public virtual void Wheel_SetSteeringLimits( float lower, float upper ) { }
	public virtual float Wheel_TargetSteeringAngle { get; set; }
	public virtual float Wheel_SpinSpeed => 0;
	public virtual float Wheel_SpinTorque => 0;
	public virtual float Wheel_SteeringAngle => 0;
	public virtual float Wheel_SteeringTorque => 0;


	public virtual float Parallel_Hertz { get; set; }
	public virtual float Parallel_DampingRatio { get; set; }
	public virtual float Parallel_MaxTorque { get; set; }
}
