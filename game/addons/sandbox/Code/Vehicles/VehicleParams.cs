/// <summary>
/// How the body handles. Values are in the units they're tuned in (mph, inches, kg, multiples of body mass) and
/// converted where the physics uses them.
/// </summary>
public sealed class VehicleBodyParams
{
	/// <summary>
	/// Local centre of mass. Zero uses the model's.
	/// </summary>
	[Property] public Vector3 MassCenterOverride { get; set; }

	/// <summary>
	/// Body mass in kg, without the wheels. Zero uses the model's.
	/// </summary>
	[Property] public float MassOverride { get; set; } = 1000f;

	/// <summary>
	/// Extra gravity pushed on the body, as a fraction of normal gravity.
	/// </summary>
	[Property] public float AddGravity { get; set; } = 0.5f;

	/// <summary>
	/// Degrees per second. The body's angular velocity is clamped to this.
	/// </summary>
	[Property] public float MaxAngularVelocity { get; set; } = 720f;
}

public sealed class VehicleEngineParams
{
	[Property] public float Horsepower { get; set; } = 350f;
	[Property] public float MaxRpm { get; set; } = 4200f;

	/// <summary>
	/// Miles per hour.
	/// </summary>
	[Property] public float MaxSpeed { get; set; } = 35f;

	/// <summary>
	/// Miles per hour.
	/// </summary>
	[Property] public float MaxReverseSpeed { get; set; } = 14f;

	/// <summary>
	/// Speed gain allowed over <see cref="MaxSpeed"/> while coasting before the brakes go on.
	/// </summary>
	[Property] public float AutobrakeSpeedGain { get; set; } = 1.1f;

	/// <summary>
	/// The auto brake is this times the speed gain.
	/// </summary>
	[Property] public float AutobrakeSpeedFactor { get; set; } = 3f;

	[Property] public float AxleRatio { get; set; } = 4.56f;
	[Property] public List<float> GearRatios { get; set; } = [3.2f, 2.4f, 1.5f, 1.0f, 0.84f];
	[Property] public float ShiftUpRpm { get; set; } = 3800f;
	[Property] public float ShiftDownRpm { get; set; } = 1600f;

	/// <summary>
	/// How much boost multiplies the engine's torque.
	/// </summary>
	[Property, Group( "Boost" )] public float BoostForce { get; set; } = 1.5f;

	[Property, Group( "Boost" )] public float BoostDuration { get; set; } = 3f;

	/// <summary>
	/// Seconds after the boost ends before it can be used again.
	/// </summary>
	[Property, Group( "Boost" )] public float BoostDelay { get; set; } = 3f;

	/// <summary>
	/// Miles per hour.
	/// </summary>
	[Property, Group( "Boost" )] public float BoostMaxSpeed { get; set; } = 50f;

	/// <summary>
	/// How much of the boost you get from a standstill, rising to all of it at <see cref="MaxSpeed"/>.
	/// </summary>
	[Property, Group( "Boost" ), Range( 0, 1 )] public float BoostLowSpeedFactor { get; set; } = 1f;
}

public sealed class VehicleSteeringParams
{
	/// <summary>
	/// Steering cone at zero to slow speed.
	/// </summary>
	[Property] public float DegreesSlow { get; set; } = 50f;

	/// <summary>
	/// Steering cone at fast speed to max speed.
	/// </summary>
	[Property] public float DegreesFast { get; set; } = 18f;

	/// <summary>
	/// Steering cone at max boost speed (blends toward this after max speed).
	/// </summary>
	[Property] public float DegreesBoost { get; set; } = 11f;

	/// <summary>
	/// Steering is linear, then raised to this power to be slower at the beginning of the curve.
	/// </summary>
	[Property] public float SteeringExponent { get; set; } = 1.4f;

	/// <summary>
	/// Miles per hour.
	/// </summary>
	[Property] public float SpeedSlow { get; set; } = 14f;

	/// <summary>
	/// Miles per hour.
	/// </summary>
	[Property] public float SpeedFast { get; set; } = 20f;

	[Property] public float SteeringRateSlow { get; set; } = 4f;
	[Property] public float SteeringRateFast { get; set; } = 2f;
	[Property] public float SteeringRestRateSlow { get; set; } = 4f;
	[Property] public float SteeringRestRateFast { get; set; } = 2f;
	[Property] public float TurnThrottleReduceSlow { get; set; } = 0.01f;
	[Property] public float TurnThrottleReduceFast { get; set; } = 2f;
	[Property] public float BrakeSteeringRateFactor { get; set; } = 6f;
	[Property] public float ThrottleSteeringRestRateFactor { get; set; } = 2f;
	[Property] public float BoostSteeringRestRateFactor { get; set; } = 1.7f;
	[Property] public float BoostSteeringRateFactor { get; set; } = 1.7f;

	/// <summary>
	/// Inches per second squared, pushing the nose and tail apart while power sliding.
	/// </summary>
	[Property] public float PowerSlideAccel { get; set; } = 250f;
}

public sealed class VehicleAxleParams
{
	/// <summary>
	/// Inches.
	/// </summary>
	[Property, Group( "Wheel" )] public float WheelRadius { get; set; } = 18f;

	/// <summary>
	/// Inches. The width of the cylinder swept for the ground.
	/// </summary>
	[Property, Group( "Wheel" )] public float TyreWidth { get; set; } = 10f;

	/// <summary>
	/// Kilograms. Carried by the car, but standing on the ground rather than on the springs.
	/// </summary>
	[Property, Group( "Wheel" )] public float WheelMass { get; set; } = 100f;

	/// <summary>
	/// Tyre friction, times the ground's.
	/// </summary>
	[Property, Group( "Wheel" )] public float Friction { get; set; } = 1.337f;

	/// <summary>
	/// Tyre friction while handbrake sliding.
	/// </summary>
	[Property, Group( "Wheel" )] public float BrakeFriction { get; set; } = 0.6f;

	/// <summary>
	/// Tyre friction while power sliding.
	/// </summary>
	[Property, Group( "Wheel" )] public float SkidFriction { get; set; } = 0.2f;

	/// <summary>
	/// Drag on a rolling wheel, as a fraction of the load on it.
	/// </summary>
	[Property, Group( "Wheel" )] public float RollingResistance { get; set; } = 0.02f;

	/// <summary>
	/// How far below its rest position the wheel hangs with the spring relaxed, in inches.
	/// </summary>
	[Property, Group( "Wheel" )] public float SpringAdditionalLength { get; set; } = 8f;

	/// <summary>Maximum wheel travel above rest, in inches.</summary>
	[Property, Group( "Suspension" )] public float MaximumCompression { get; set; } = 4f;

	/// <summary>
	/// Multiple of body mass, Newtons per meter.
	/// </summary>
	[Property, Group( "Suspension" )] public float SpringConstant { get; set; } = 40f;

	/// <summary>
	/// Multiple of body mass, damping while the spring relaxes.
	/// </summary>
	[Property, Group( "Suspension" )] public float SpringDamping { get; set; } = 0.7f;

	/// <summary>
	/// Multiple of body mass, anti-roll bar stiffness between this axle's wheels.
	/// </summary>
	[Property, Group( "Suspension" )] public float StabilizerConstant { get; set; } = 10f;

	/// <summary>
	/// Multiple of body mass, damping while the spring compresses.
	/// </summary>
	[Property, Group( "Suspension" )] public float SpringDampingCompression { get; set; } = 9f;

	[Property] public float TorqueFactor { get; set; } = 0.5f;
	[Property] public float BrakeFactor { get; set; } = 0.5f;
}

/// <summary>
/// What the driver's asking for.
/// </summary>
public struct VehicleControlParams
{
	public float Throttle;
	public float Steering;
	public float Brake;
	public float Boost;
	public bool Handbrake;
	public bool HandbrakeLeft;
	public bool HandbrakeRight;
}

/// <summary>
/// What the vehicle's doing.
/// </summary>
public struct VehicleOperatingParams
{
	/// <summary>
	/// Inches per second along the body's forward axis.
	/// </summary>
	public float Speed;
	public float EngineRpm;
	public int Gear;
	public float BoostDelay;
	public int BoostTimeLeft;
	public float SkidSpeed;

	/// <summary>
	/// Degrees.
	/// </summary>
	public float SteeringAngle;
	public int WheelsInContact;
	public bool IsTorqueBoosting;
}
