namespace Sandbox.Physics;

/// <summary>
/// The wheel joint can be used to simulate wheels on vehicles.
/// The wheel joint restricts body B to move along a local axis in body A. Body B is free to rotate.
/// Supports a linear spring, linear limits, and a rotational motor.
/// </summary>
internal sealed class WheelJoint : PhysicsJoint
{
	const float TorqueScale = 40.0f;

	internal WheelJoint( PhysicsJointInternal joint ) : base( joint ) { }

	/// <summary>
	/// Enable or disable the wheel joint spring.
	/// </summary>
	public bool EnableSuspension
	{
		get => _joint?.Wheel_EnableSuspension ?? false;
		set => _joint?.Wheel_EnableSuspension = value;
	}

	/// <summary>
	/// Gets or sets the wheel joint suspension stiffness in Hertz.
	/// </summary>
	public float SuspensionHertz
	{
		get => _joint?.Wheel_SuspensionHertz ?? 0;
		set => _joint?.Wheel_SuspensionHertz = value;
	}

	/// <summary>
	/// Gets or sets the wheel joint suspension damping ratio (non-dimensional).
	/// </summary>
	public float SuspensionDampingRatio
	{
		get => _joint?.Wheel_SuspensionDampingRatio ?? 0;
		set => _joint?.Wheel_SuspensionDampingRatio = value;
	}

	/// <summary>
	/// Enable or disable the wheel joint suspension limit.
	/// </summary>
	public bool EnableSuspensionLimit
	{
		get => _joint?.Wheel_EnableSuspensionLimit ?? false;
		set => _joint?.Wheel_EnableSuspensionLimit = value;
	}

	/// <summary>
	/// Gets or sets the wheel joint suspension limits.
	/// </summary>
	public Vector2 SuspensionLimits
	{
		get => _joint is not null ? new( _joint.Wheel_LowerSuspensionLimit, _joint.Wheel_UpperSuspensionLimit ) : default;
		set => _joint?.Wheel_SetSuspensionLimits( value.x, value.y );
	}

	/// <summary>
	/// Enable or disable the wheel joint spin motor.
	/// </summary>
	public bool EnableSpinMotor
	{
		get => _joint?.Wheel_EnableSpinMotor ?? false;
		set => _joint?.Wheel_EnableSpinMotor = value;
	}

	/// <summary>
	/// Gets or sets the wheel joint spin motor speed in degrees per second.
	/// </summary>
	public float SpinMotorSpeed
	{
		get => (_joint?.Wheel_SpinMotorSpeed ?? 0).RadianToDegree();
		set => _joint?.Wheel_SpinMotorSpeed = value.DegreeToRadian();
	}

	/// <summary>
	/// Gets or sets the wheel joint maximum spin motor torque, usually in newton-meters.
	/// </summary>
	public float MaxSpinTorque
	{
		get => (_joint?.Wheel_MaxSpinTorque ?? 0) / TorqueScale;
		set => _joint?.Wheel_MaxSpinTorque = value * TorqueScale;
	}

	/// <summary>
	/// Enable or disable wheel steering.
	/// </summary>
	public bool EnableSteering
	{
		get => _joint?.Wheel_EnableSteering ?? false;
		set => _joint?.Wheel_EnableSteering = value;
	}

	/// <summary>
	/// Gets or sets the wheel joint steering stiffness in Hertz.
	/// </summary>
	public float SteeringHertz
	{
		get => _joint?.Wheel_SteeringHertz ?? 0;
		set => _joint?.Wheel_SteeringHertz = value;
	}

	/// <summary>
	/// Gets or sets the wheel joint steering damping ratio (non-dimensional).
	/// </summary>
	public float SteeringDampingRatio
	{
		get => _joint?.Wheel_SteeringDampingRatio ?? 0;
		set => _joint?.Wheel_SteeringDampingRatio = value;
	}

	/// <summary>
	/// Gets or sets the wheel joint maximum steering torque in N·m.
	/// </summary>
	public float MaxSteeringTorque
	{
		get => (_joint?.Wheel_MaxSteeringTorque ?? 0) / TorqueScale;
		set => _joint?.Wheel_MaxSteeringTorque = value * TorqueScale;
	}

	/// <summary>
	/// Enable or disable the wheel joint steering limit.
	/// </summary>
	public bool EnableSteeringLimit
	{
		get => _joint?.Wheel_EnableSteeringLimit ?? false;
		set => _joint?.Wheel_EnableSteeringLimit = value;
	}

	/// <summary>
	/// Gets or sets the wheel joint steering limits in degrees.
	/// </summary>
	public Vector2 SteeringLimits
	{
		get => (_joint?.Wheel_LowerSteeringLimit ?? 0).RadianToDegree();
		set => _joint?.Wheel_SetSteeringLimits( value.x.DegreeToRadian(), value.y.DegreeToRadian() );
	}

	/// <summary>
	/// Gets or sets the wheel joint target steering angle in degrees.
	/// </summary>
	public float TargetSteeringAngle
	{
		get => (_joint?.Wheel_TargetSteeringAngle ?? 0).RadianToDegree();
		set => _joint?.Wheel_TargetSteeringAngle = value.DegreeToRadian();
	}

	/// <summary>
	/// Gets the current wheel spin speed in degrees per second.
	/// </summary>
	public float SpinSpeed => (_joint?.Wheel_SpinSpeed ?? 0).RadianToDegree();

	/// <summary>
	/// Gets the current wheel spin torque in newton-meters.
	/// </summary>
	public float SpinTorque => _joint?.Wheel_SpinTorque ?? 0;

	/// <summary>
	/// Gets the current wheel steering angle in degrees.
	/// </summary>
	public float SteeringAngle => (_joint?.Wheel_SteeringAngle ?? 0).RadianToDegree();

	/// <summary>
	/// Gets the current wheel steering torque in newton-meters.
	/// </summary>
	public float SteeringTorque => _joint?.Wheel_SteeringTorque ?? 0;
}
