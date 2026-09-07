namespace Sandbox.Physics;

/// <summary>
/// 3D physics joint backed by a native Box3D/Source 2 IPhysicsJoint handle.
/// </summary>
internal sealed class PhysicsJoint3d : PhysicsJointInternal, IHandle
{
	internal NativeEngine.IPhysicsJoint native;

	void IHandle.HandleInit( IntPtr ptr )
	{
		native = ptr;
		World = native.GetWorld();
	}

	void IHandle.HandleDestroy()
	{
		native = IntPtr.Zero;
		World = null;
	}

	bool IHandle.HandleValid() => !native.IsNull;

	internal PhysicsJoint3d() { }
	internal PhysicsJoint3d( HandleCreationData _ ) { }

	public override bool IsValid => !native.IsNull;

	internal override PhysicsJointType JointType => native.GetType_Native();


	public override PhysicsBody Body1 => native.IsValid ? native.GetBody1()?.Owner : null;
	public override PhysicsBody Body2 => native.IsValid ? native.GetBody2()?.Owner : null;

	public override void GetLocalFrameA( out Vector3 position, out Rotation rotation )
		=> native.GetLocalFrameA( out position, out rotation );

	public override void GetLocalFrameB( out Vector3 position, out Rotation rotation )
		=> native.GetLocalFrameB( out position, out rotation );

	public override void SetLocalFrameA( Vector3 position, Rotation rotation )
		=> native.SetLocalFrameA( position, rotation );

	public override void SetLocalFrameB( Vector3 position, Rotation rotation )
		=> native.SetLocalFrameB( position, rotation );

	public override void Remove()
	{
		if ( native.IsNull ) return;
		if ( World == null ) return;

		(World as PhysicsWorld3d).native.RemoveJoint( this );
	}

	public override bool Collisions
	{
		get => native.IsCollisionEnabled();
		set => native.SetEnableCollision( value );
	}

	public override float Strength
	{
		get => native.GetMaxLinearImpulse();
		set => native.SetMaxLinearImpulse( value );
	}

	public override float AngularStrength
	{
		get => native.GetMaxAngularImpulse();
		set => native.SetMaxAngularImpulse( value );
	}

	internal override float LinearImpulse => native.GetLinearImpulse();
	internal override float AngularImpulse => native.GetAngularImpulse();

	internal override void WakeBodies()
	{
		if ( Body1.IsValid() )
			(Body1._body as PhysicsBody3d).native.Wake();

		if ( Body2.IsValid() )
			(Body2._body as PhysicsBody3d).native.Wake();
	}


	public override PhysicsSpring SpringLinear
	{
		get => native.GetLinearSpring();
		set => native.SetLinearSpring( value );
	}

	public override PhysicsSpring SpringAngular
	{
		get => native.GetAngularSpring();
		set => native.SetAngularSpring( value );
	}


	public override float MinLength
	{
		get => native.GetMinLength();
		set => native.SetMinLength( value );
	}

	public override float MaxLength
	{
		get => native.GetMaxLength();
		set => native.SetMaxLength( value );
	}

	public override float MinForce
	{
		get => native.GetMinForce();
		set => native.SetMinForce( value );
	}

	public override float MaxForce
	{
		get => native.GetMaxForce();
		set => native.SetMaxForce( value );
	}

	public override float Friction
	{
		set => native.SetFriction( value );
	}

	public override float Angle => native.GetAngle();

	public override void SetLimit( string name, Vector2 limit ) => native.SetLimit( name, limit );
	public override void SetLimitEnabled( string name, bool state ) => native.SetLimitEnabled( name, state );

	public override void SetAngularSpring( Vector3 parameters ) => native.SetAngularSpring( parameters );
	public override void SetAngularMotor( float targetVelocity, float maxTorque ) => native.SetAngularMotor( targetVelocity, maxTorque );
	public override void SetTargetRotation( Rotation rotation, float hertz, float damping ) => native.SetTargetRotation( rotation, hertz, damping );
	public override void SetMotorVelocity( Vector3 velocity, float maxTorque ) => native.SetMotorVelocity( velocity, maxTorque );


	public override Vector3 Motor_LinearVelocity
	{
		get => native.IsNull ? default : native.Motor_GetLinearVelocity();
		set { if ( !native.IsNull ) native.Motor_SetLinearVelocity( value ); }
	}

	public override Vector3 Motor_AngularVelocity
	{
		get => native.IsNull ? default : native.Motor_GetAngularVelocity();
		set { if ( !native.IsNull ) native.Motor_SetAngularVelocity( value ); }
	}

	public override float Motor_MaxVelocityForce
	{
		get => native.IsNull ? 0 : native.Motor_GetMaxVelocityForce();
		set { if ( !native.IsNull ) native.Motor_SetMaxVelocityForce( value ); }
	}

	public override float Motor_MaxVelocityTorque
	{
		get => native.IsNull ? 0 : native.Motor_GetMaxVelocityTorque();
		set { if ( !native.IsNull ) native.Motor_SetMaxVelocityTorque( value ); }
	}

	public override float Motor_LinearHertz
	{
		get => native.IsNull ? 0 : native.Motor_GetLinearHertz();
		set { if ( !native.IsNull ) native.Motor_SetLinearHertz( value ); }
	}

	public override float Motor_LinearDampingRatio
	{
		get => native.IsNull ? 0 : native.Motor_GetLinearDampingRatio();
		set { if ( !native.IsNull ) native.Motor_SetLinearDampingRatio( value ); }
	}

	public override float Motor_AngularHertz
	{
		get => native.IsNull ? 0 : native.Motor_GetAngularHertz();
		set { if ( !native.IsNull ) native.Motor_SetAngularHertz( value ); }
	}

	public override float Motor_AngularDampingRatio
	{
		get => native.IsNull ? 0 : native.Motor_GetAngularDampingRatio();
		set { if ( !native.IsNull ) native.Motor_SetAngularDampingRatio( value ); }
	}

	public override float Motor_MaxSpringForce
	{
		get => native.IsNull ? 0 : native.Motor_GetMaxSpringForce();
		set { if ( !native.IsNull ) native.Motor_SetMaxSpringForce( value ); }
	}

	public override float Motor_MaxSpringTorque
	{
		get => native.IsNull ? 0 : native.Motor_GetMaxSpringTorque();
		set { if ( !native.IsNull ) native.Motor_SetMaxSpringTorque( value ); }
	}


	public override bool Wheel_EnableSuspension
	{
		get => !native.IsNull && native.Wheel_IsSuspensionEnabled();
		set { if ( !native.IsNull ) native.Wheel_EnableSuspension( value ); }
	}

	public override float Wheel_SuspensionHertz
	{
		get => native.IsNull ? 0 : native.Wheel_GetSuspensionHertz();
		set { if ( !native.IsNull ) native.Wheel_SetSuspensionHertz( value ); }
	}

	public override float Wheel_SuspensionDampingRatio
	{
		get => native.IsNull ? 0 : native.Wheel_GetSuspensionDampingRatio();
		set { if ( !native.IsNull ) native.Wheel_SetSuspensionDampingRatio( value ); }
	}

	public override bool Wheel_EnableSuspensionLimit
	{
		get => !native.IsNull && native.Wheel_IsSuspensionLimitEnabled();
		set { if ( !native.IsNull ) native.Wheel_EnableSuspensionLimit( value ); }
	}

	public override float Wheel_LowerSuspensionLimit => native.IsNull ? 0 : native.Wheel_GetLowerSuspensionLimit();
	public override float Wheel_UpperSuspensionLimit => native.IsNull ? 0 : native.Wheel_GetUpperSuspensionLimit();

	public override void Wheel_SetSuspensionLimits( float lower, float upper )
	{
		if ( !native.IsNull ) native.Wheel_SetSuspensionLimits( lower, upper );
	}

	public override bool Wheel_EnableSpinMotor
	{
		get => !native.IsNull && native.Wheel_IsSpinMotorEnabled();
		set { if ( !native.IsNull ) native.Wheel_EnableSpinMotor( value ); }
	}

	public override float Wheel_SpinMotorSpeed
	{
		get => native.IsNull ? 0 : native.Wheel_GetSpinMotorSpeed();
		set { if ( !native.IsNull ) native.Wheel_SetSpinMotorSpeed( value ); }
	}

	public override float Wheel_MaxSpinTorque
	{
		get => native.IsNull ? 0 : native.Wheel_GetMaxSpinTorque();
		set { if ( !native.IsNull ) native.Wheel_SetMaxSpinTorque( value ); }
	}

	public override bool Wheel_EnableSteering
	{
		get => !native.IsNull && native.Wheel_IsSteeringEnabled();
		set { if ( !native.IsNull ) native.Wheel_EnableSteering( value ); }
	}

	public override float Wheel_SteeringHertz
	{
		get => native.IsNull ? 0 : native.Wheel_GetSteeringHertz();
		set { if ( !native.IsNull ) native.Wheel_SetSteeringHertz( value ); }
	}

	public override float Wheel_SteeringDampingRatio
	{
		get => native.IsNull ? 0 : native.Wheel_GetSteeringDampingRatio();
		set { if ( !native.IsNull ) native.Wheel_SetSteeringDampingRatio( value ); }
	}

	public override float Wheel_MaxSteeringTorque
	{
		get => native.IsNull ? 0 : native.Wheel_GetMaxSteeringTorque();
		set { if ( !native.IsNull ) native.Wheel_SetMaxSteeringTorque( value ); }
	}

	public override bool Wheel_EnableSteeringLimit
	{
		get => !native.IsNull && native.Wheel_IsSteeringLimitEnabled();
		set { if ( !native.IsNull ) native.Wheel_EnableSteeringLimit( value ); }
	}

	public override float Wheel_LowerSteeringLimit => native.IsNull ? 0 : native.Wheel_GetLowerSteeringLimit();
	public override float Wheel_UpperSteeringLimit => native.IsNull ? 0 : native.Wheel_GetUpperSteeringLimit();

	public override void Wheel_SetSteeringLimits( float lower, float upper )
	{
		if ( !native.IsNull ) native.Wheel_SetSteeringLimits( lower, upper );
	}

	public override float Wheel_TargetSteeringAngle
	{
		get => native.IsNull ? 0 : native.Wheel_GetTargetSteeringAngle();
		set { if ( !native.IsNull ) native.Wheel_SetTargetSteeringAngle( value ); }
	}

	public override float Wheel_SpinSpeed => native.IsNull ? 0 : native.Wheel_GetSpinSpeed();
	public override float Wheel_SpinTorque => native.IsNull ? 0 : native.Wheel_GetSpinTorque();
	public override float Wheel_SteeringAngle => native.IsNull ? 0 : native.Wheel_GetSteeringAngle();
	public override float Wheel_SteeringTorque => native.IsNull ? 0 : native.Wheel_GetSteeringTorque();


	public override float Parallel_Hertz
	{
		get => native.Parallel_GetHertz();
		set => native.Parallel_SetHertz( value );
	}

	public override float Parallel_DampingRatio
	{
		get => native.Parallel_GetDampingRatio();
		set => native.Parallel_SetDampingRatio( value );
	}

	public override float Parallel_MaxTorque
	{
		get => native.Parallel_GetMaxTorque();
		set => native.Parallel_SetMaxTorque( value );
	}
}
