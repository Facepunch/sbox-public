/// <summary>
/// A drivable four wheel vehicle. It goes on the vehicle body, a normal rigidbody that still collides, tumbles and
/// can be picked up.
/// <list type="bullet">
/// <item>This file - the seat, the engine starting and stopping, what's synced to everyone</item>
/// <item>FourWheelVehicle.Driver.cs - the driver's keys into throttle, brake, steering, boost and handbrake</item>
/// <item>FourWheelVehicle.Drivetrain.cs - engine, gearbox, brakes, steering, power slides and boost</item>
/// <item>FourWheelVehicle.Wheels.cs - the wheels: suspension, anti-roll bars, drive and grip</item>
/// <item>FourWheelVehicle.Rig.cs - posing the model: wheels, suspension and steering wheel</item>
/// </list>
/// The wheels aren't bodies: each finds the ground below it and pushes the body where they meet. Where they are and
/// what they look like comes from the model's bones.
/// </summary>
[Title( "Four Wheel Vehicle" ), Category( "Vehicles" ), Icon( "directions_car" )]
public sealed partial class FourWheelVehicle : Component, IPlayerControllable, IResizeConstraint
{
	public const int WheelCount = 4;
	public float MinimumResizeScale => 0.25f;

	/// <summary>
	/// The driver's seat. Whoever sits here drives.
	/// </summary>
	[Property] public BaseChair Seat { get; set; }

	/// <summary>
	/// Local axis of the steering column, pointing at the driver.
	/// </summary>
	[Property, Group( "Steering Wheel" )] public Vector3 SteeringWheelAxis { get; set; } = Vector3.Backward;

	/// <summary>
	/// How far the steering wheel turns at full lock, in degrees.
	/// </summary>
	[Property, Group( "Steering Wheel" )] public float SteeringWheelAngle { get; set; } = 90f;

	[Property, Group( "Tuning" ), InlineEditor] public VehicleBodyParams Body { get; set; } = new();
	[Property, Group( "Tuning" ), InlineEditor] public VehicleEngineParams Engine { get; set; } = new();
	[Property, Group( "Tuning" ), InlineEditor] public VehicleSteeringParams Steering { get; set; } = new();

	[Property, Group( "Tuning" ), InlineEditor]
	public VehicleAxleParams FrontAxle { get; set; } = new() { WheelRadius = 18f, TorqueFactor = 0.3f, BrakeFactor = 0.4f };

	[Property, Group( "Tuning" ), InlineEditor]
	public VehicleAxleParams RearAxle { get; set; } = new() { WheelRadius = 22f, TorqueFactor = 0.7f, BrakeFactor = 0.6f };

	/// <summary>
	/// The gravity the tuning is for, in inches per second squared. The vehicle falls at this rather than the scene's
	/// gravity. Zero uses the scene's gravity.
	/// </summary>
	[Property, Group( "Tuning" )] public float Gravity { get; set; } = 600f;

	/// <summary>
	/// Let tyres block against walls and other obstacles in addition to supporting the suspension.
	/// </summary>
	[Property, Group( "Tuning" )] public bool WheelObstacleCollisions { get; set; }

	[Property, Group( "Input" )] public ClientInput Forward { get; set; } = new() { Action = "Forward" };
	[Property, Group( "Input" )] public ClientInput Back { get; set; } = new() { Action = "Backward" };
	[Property, Group( "Input" )] public ClientInput Left { get; set; } = new() { Action = "Left" };
	[Property, Group( "Input" )] public ClientInput Right { get; set; } = new() { Action = "Right" };
	[Property, Group( "Input" )] public ClientInput Boost { get; set; } = new() { Action = "Run" };
	[Property, Group( "Input" )] public ClientInput Handbrake { get; set; } = new() { Action = "Jump" };

	[Property, Group( "Debug" )] public bool DrawDebug { get; set; }

	/// <summary>
	/// Front wheel steering angle in degrees, positive to the right.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public float SteerAngle { get; set; }

	/// <summary>
	/// The handbrake has the wheels locked.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public bool WheelsLocked { get; set; }

	[Sync( SyncFlags.FromHost )] public bool IsEngineOn { get; private set; }

	/// <summary>
	/// Engine speed for sound: the gearbox's, or the free revving engine's while revving in neutral.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public float SoundRpm { get; private set; }

	[Sync( SyncFlags.FromHost )] public int Gear { get; private set; }

	/// <summary>
	/// The driver is accelerating (not braking, not on the handbrake).
	/// </summary>
	[Sync( SyncFlags.FromHost )] public bool ThrottleDown { get; private set; }

	[Sync( SyncFlags.FromHost )] public bool Reversing { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool Boosting { get; private set; }

	/// <summary>
	/// How hard the driver's braking, 0 to 1 - for everyone else's view of the pedals and the driver's feet.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public float BrakeInput { get; private set; }

	/// <summary>
	/// Fastest a wheel is sliding over the ground, in/s.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public float SkidSpeed { get; private set; }

	/// <summary>
	/// Forward speed, in/s, negative backwards.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public float Speed { get; private set; }

	/// <summary>
	/// Speed in miles per hour, either way.
	/// </summary>
	public int SpeedMph => (int)MathF.Abs( InsToMph( Speed ) );

	[Sync( SyncFlags.FromHost )] public Vector4 SuspensionTravel { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool HasSuspensionState { get; private set; }

	private Rigidbody _body;
	private bool _carSystemReady;
	private Vector3 _simulationScale;

	// Vehicle tuning is authored at unit scale; traces use world-space dimensions.
	// Keep tiny visuals without reducing the handling and mass tuning indefinitely.
	public float FunctionalScale => MathF.Max( VehicleScale, 0.25f );
	private float VehicleMassScale => MathF.Max( MathF.Abs( WorldScale.x ), 0.25f )
		* MathF.Max( MathF.Abs( WorldScale.y ), 0.25f ) * MathF.Max( MathF.Abs( WorldScale.z ), 0.25f );
	private float VehicleScale => MathF.Max( MathF.Abs( WorldScale.z ), 0.001f );
	private bool _hasDriver;
	private bool _warnedInvalid;

	private float _steerPose;

	/// <summary>
	/// How fast the engine revs up and runs down in neutral, rpm a second.
	/// </summary>
	private const float FreeRevRiseRate = 5000f;
	private const float FreeRevFallRate = 2500f;

	private float _freeRevRpm;

	protected override void OnStart()
	{
		if ( Networking.IsHost )
			InitCarSystem();
	}

	/// <summary>
	/// Set up the body and the wheels from the tuning.
	/// </summary>
	private void InitCarSystem()
	{
		_carSystemReady = false;

		_body = GetComponent<Rigidbody>();
		var rig = GetRig();
		if ( !_body.IsValid() || rig is null )
		{
			if ( !_warnedInvalid )
				Log.Warning( $"{GameObject.Name}: a four wheel vehicle needs a Rigidbody, and a SkinnedModelRenderer whose model has the wheel bones ({string.Join( ", ", Corners.Select( WheelBone ) )} and their _spin bones)" );

			_warnedInvalid = true;
			return;
		}

		// the tuning's mass and centre of mass (zero mass is the model's own)
		_body.MassOverride = Math.Max( 0, Body.MassOverride ) * VehicleMassScale;
		_body.ResetInertiaTensor();

		if ( Body.MassCenterOverride != Vector3.Zero )
		{
			_body.OverrideMassCenter = true;
			_body.MassCenterOverride = Body.MassCenterOverride * WorldScale;
		}

		_bodyMass = _body.PhysicsBody.Mass;
		_body.GravityScale = GravityScale;

		for ( int i = 0; i < WheelCount; i++ )
		{
			_wheels[i] = CreateWheel( AxleOf( i ), rig.HardPoint( i ), VehicleScale, VehicleMassScale );
		}

		InitDrivetrain();
		AddWheelMass();
		_wheelsRig = rig;
		_simulationScale = WorldScale;
		_visualTravelReady = false;
		_body.PhysicsBody.Sleeping = false;
		_carSystemReady = true;

		// make sure the first steering update gets through
		_steeringAngle = -1f;
		SetSteeringAngle( 0f );
	}

	/// <summary>
	/// Driven from the seat by <see cref="ControlSystem"/>, before our fixed update, with the driver's input active.
	/// </summary>
	void IPlayerControllable.OnControl()
	{
		// no driving with the engine off
		if ( !_carSystemReady || !IsEngineOn ) return;

		// upside down, the wheels can't drive (including boost)
		var overturned = IsOverturned();

		// keys are all the way, a gamepad's triggers and stick anything between
		UpdateDriverControls( new DriverInput
		{
			Forward = overturned ? 0.0f : Forward.GetAnalog(),
			Back = overturned ? 0.0f : Back.GetAnalog(),
			Steering = Right.GetAnalog() - Left.GetAnalog(),
			Boost = Boost.Down() && !overturned,
			Handbrake = Handbrake.Down(),
		}, Time.Delta );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;

		// a tuning change from the inspector
		ApplyTuningChanges();

		// set up late (hotload), or rebuild if the model changed
		if ( !_carSystemReady || !_body.IsValid() || _wheelsRig != GetRig() || _simulationScale != WorldScale )
		{
			InitCarSystem();
			if ( !_carSystemReady ) return;
		}

		UpdateDriver();

		if ( !ParkedAsleep() )
		{
			UpdateDrivetrain( Time.Delta, _controls );
			Simulate( Time.Delta );
		}

		PublishState();
	}

	private TimeSince _timeSettled;

	/// <summary>
	/// A parked car that has settled goes to sleep, rather than being held up by its wheels every tick - that never
	/// quite comes to rest, it jitters on the spot. Anything that wakes the body (a hit, the physgun, a driver getting
	/// in) brings the wheels back.
	/// </summary>
	private bool ParkedAsleep()
	{
		var physics = _body.PhysicsBody;

		if ( _hasDriver )
		{
			physics.Sleeping = false;
			_timeSettled = 0;
			return false;
		}

		if ( physics.Sleeping )
			return true;

		// still (the velocity we read carries half a tick of gravity, see MeasuredVelocity), on all four wheels
		var velocity = _body.Velocity - BodyGravity * (Time.Delta * 0.5f);
		var still = velocity.Length < 3f && _body.AngularVelocity.Length < 15f.DegreeToRadian() && _wheels.All( w => w.InContact );
		if ( !still )
			_timeSettled = 0;

		if ( _timeSettled < 1.0f )
			return false;

		physics.Sleeping = true;
		return true;
	}

	/// <summary>
	/// Sync what everyone else needs: the steering and wheel visuals, and the engine state for sounds.
	/// </summary>
	private void PublishState()
	{
		SuspensionTravel = new Vector4(
			_wheels[0].GroundDistance - _wheels[0].Radius,
			_wheels[1].GroundDistance - _wheels[1].Radius,
			_wheels[2].GroundDistance - _wheels[2].Radius,
			_wheels[3].GroundDistance - _wheels[3].Radius );
		HasSuspensionState = true;
		SteerAngle = _state.SteeringAngle;
		WheelsLocked = _handbrakeOn;

		// Revving in neutral on the handbrake: the engine spins up quickly towards the limiter and runs down slower
		var revving = IsEngineOn && _freeRevving;
		_freeRevRpm = _freeRevRpm.Approach( revving ? Engine.MaxRpm * 0.98f : 0, (revving ? FreeRevRiseRate : FreeRevFallRate) * Time.Delta );

		SoundRpm = MathF.Max( _state.EngineRpm, _freeRevRpm );
		Gear = _state.Gear;
		ThrottleDown = IsEngineOn && (_throttleDown || revving);
		Reversing = _controls.Throttle < 0;
		Boosting = IsBoosting();
		BrakeInput = IsEngineOn ? _controls.Brake : 0;
		SkidSpeed = _state.SkidSpeed;
		Speed = _state.Speed;
	}

	/// <summary>
	/// The engine starts and stops as people get in and out of the seat. Getting out puts the handbrake on.
	/// </summary>
	private void UpdateDriver()
	{
		var occupied = Seat.IsValid() && Seat.IsOccupied;
		if ( occupied == _hasDriver ) return;

		_hasDriver = occupied;
		IsEngineOn = occupied;

		if ( !occupied )
		{
			ResetControls();
			OnDriverExit();
		}
	}

	public bool IsOverturned() => WorldRotation.Up.Dot( Vector3.Up ) < 0f;

	protected override void OnUpdate()
	{
		// the steering wheel follows the front wheels, a little behind
		_steerPose = _steerPose.LerpTo( SteerAngle / Steering.DegreesSlow, 1 - MathF.Pow( 0.8f, Time.Delta * 50 ) );

		PoseRig();
		UpdateDriverHands();

		if ( Networking.IsHost && _carSystemReady )
		{
			UpdateDebugHud( ShouldDrawDebug() );
		}
	}
}
