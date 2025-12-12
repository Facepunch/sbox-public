namespace Sandbox.Physics;

/// <summary>
/// The wheel joint can be used to simulate wheels on vehicles.
/// The wheel joint restricts body B to move along a local axis in body A. Body B is free to rotate.
/// Supports a linear spring, linear limits, and a rotational motor.
/// </summary>
internal sealed class WheelJoint : PhysicsJoint
{
	const float TorqueScale = 40.0f;

	internal WheelJoint( HandleCreationData _ ) { }

	/// <summary>
	/// Enable or disable the wheel joint spring.
	/// </summary>
	public bool EnableSuspension
	{
		get => !native.IsNull && native.Wheel_IsSuspensionEnabled();
		set
		{
			if ( native.IsNull ) return;
			native.Wheel_EnableSuspension( value );
		}
	}

	/// <summary>
	/// Gets or sets the wheel joint suspension stiffness in Hertz.
	/// </summary>
	public float SuspensionHertz
	{
		get => native.IsNull ? 0.0f : native.Wheel_GetSuspensionHertz();
		set
		{
			if ( native.IsNull ) return;
			native.Wheel_SetSuspensionHertz( value );
		}
	}

	/// <summary>
	/// Gets or sets the wheel joint suspension damping ratio (non-dimensional).
	/// </summary>
	public float SuspensionDampingRatio
	{
		get => native.IsNull ? 0.0f : native.Wheel_GetSuspensionDampingRatio();
		set
		{
			if ( native.IsNull ) return;
			native.Wheel_SetSuspensionDampingRatio( value );
		}
	}

	/// <summary>
	/// Enable or disable the wheel joint suspension limit.
	/// </summary>
	public bool EnableSuspensionLimit
	{
		get => !native.IsNull && native.Wheel_IsSuspensionLimitEnabled();
		set
		{
			if ( native.IsNull ) return;
			native.Wheel_EnableSuspensionLimit( value );
		}
	}

	/// <summary>
	/// Gets or sets the wheel joint suspension limits.
	/// </summary>
	public Vector2 SuspensionLimits
	{
		get => native.IsNull ? default : new( native.Wheel_GetLowerSuspensionLimit(), native.Wheel_GetUpperSuspensionLimit() );
		set
		{
			if ( native.IsNull ) return;
			native.Wheel_SetSuspensionLimits( value.x, value.y );
		}
	}

	/// <summary>
	/// Enable or disable the wheel joint spin motor.
	/// </summary>
	public bool EnableSpinMotor
	{
		get => !native.IsNull && native.Wheel_IsSpinMotorEnabled();
		set
		{
			if ( native.IsNull ) return;
			native.Wheel_EnableSpinMotor( value );
		}
	}

	/// <summary>
	/// Gets or sets the wheel joint spin motor speed in degrees per second.
	/// </summary>
	public float SpinMotorSpeed
	{
		get => native.IsNull ? 0.0f : native.Wheel_GetSpinMotorSpeed().RadianToDegree();
		set
		{
			if ( native.IsNull ) return;
			native.Wheel_SetSpinMotorSpeed( value.DegreeToRadian() );
		}
	}

	/// <summary>
	/// Gets or sets the wheel joint maximum spin motor torque, usually in newton-meters.
	/// </summary>
	public float MaxSpinTorque
	{
		get => native.IsNull ? 0.0f : native.Wheel_GetMaxSpinTorque() / TorqueScale;
		set
		{
			if ( native.IsNull ) return;
			native.Wheel_SetMaxSpinTorque( value * TorqueScale );
		}
	}

	/// <summary>
	/// Enable or disable wheel steering. Steering allows the wheel to rotate about the suspension axis.
	/// </summary>
	public bool EnableSteering
	{
		get => !native.IsNull && native.Wheel_IsSteeringEnabled();
		set
		{
			if ( native.IsNull ) return;
			native.Wheel_EnableSteering( value );
		}
	}

	/// <summary>
	/// Gets or sets the wheel joint steering stiffness in Hertz.
	/// </summary>
	public float SteeringHertz
	{
		get => native.IsNull ? 0.0f : native.Wheel_GetSteeringHertz();
		set
		{
			if ( native.IsNull ) return;
			native.Wheel_SetSteeringHertz( value );
		}
	}

	/// <summary>
	/// Gets or sets the wheel joint steering damping ratio (non-dimensional).
	/// </summary>
	public float SteeringDampingRatio
	{
		get => native.IsNull ? 0.0f : native.Wheel_GetSteeringDampingRatio();
		set
		{
			if ( native.IsNull ) return;
			native.Wheel_SetSteeringDampingRatio( value );
		}
	}

	/// <summary>
	/// Gets or sets the wheel joint maximum steering torque in N·m.
	/// </summary>
	public float MaxSteeringTorque
	{
		get => native.IsNull ? 0.0f : native.Wheel_GetMaxSteeringTorque() / TorqueScale;
		set
		{
			if ( native.IsNull ) return;
			native.Wheel_SetMaxSteeringTorque( value * TorqueScale );
		}
	}

	/// <summary>
	/// Enable or disable the wheel joint steering limit.
	/// </summary>
	public bool EnableSteeringLimit
	{
		get => !native.IsNull && native.Wheel_IsSteeringLimitEnabled();
		set
		{
			if ( native.IsNull ) return;
			native.Wheel_EnableSteeringLimit( value );
		}
	}

	/// <summary>
	/// Gets or sets the wheel joint steering limits in degrees.
	/// </summary>
	public Vector2 SteeringLimits
	{
		get => native.IsNull ? 0.0f : native.Wheel_GetLowerSteeringLimit().RadianToDegree();
		set
		{
			if ( native.IsNull ) return;
			native.Wheel_SetSteeringLimits( value.x.DegreeToRadian(), value.y.DegreeToRadian() );
		}
	}

	/// <summary>
	/// Gets or sets the wheel joint target steering angle in degrees.
	/// </summary>
	public float TargetSteeringAngle
	{
		get => native.IsNull ? 0.0f : native.Wheel_GetTargetSteeringAngle().RadianToDegree();
		set
		{
			if ( native.IsNull ) return;
			native.Wheel_SetTargetSteeringAngle( value.DegreeToRadian() );
		}
	}

	/// <summary>
	/// Gets the current wheel spin speed in degrees per second.
	/// </summary>
	public float SpinSpeed => native.IsNull ? 0.0f : native.Wheel_GetSpinSpeed().RadianToDegree();

	/// <summary>
	/// Gets the current wheel spin torque in newton-meters.
	/// </summary>
	public float SpinTorque => native.IsNull ? 0.0f : native.Wheel_GetSpinTorque();

	/// <summary>
	/// Gets the current wheel steering angle in degrees.
	/// </summary>
	public float SteeringAngle => native.IsNull ? 0.0f : native.Wheel_GetSteeringAngle().RadianToDegree();

	/// <summary>
	/// Gets the current wheel steering torque in newton-meters.
	/// </summary>
	public float SteeringTorque => native.IsNull ? 0.0f : native.Wheel_GetSteeringTorque();
}
