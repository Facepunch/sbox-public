using static VehicleMath;

/// <summary>
/// The drivetrain: engine, gearbox, brakes, handbrake, steering, power slides and boost, turned into torque and
/// steering for the wheels (FourWheelVehicle.Wheels.cs), plus the pushes on the body - extra gravity and the power
/// slide. The maths is in meters and Newtons, converted where it touches the engine (inches).
/// </summary>
public sealed partial class FourWheelVehicle
{
	private const float MetersPerInch = 0.0254f;
	private const float InchesPerMeter = 1.0f / MetersPerInch;
	private const float MphToMetersPerSecond = 0.44707f;

	/// <summary>
	/// Rolling the wrong way faster than this (in/s) with the throttle down locks the wheels, so you can hold the car
	/// on a hill and pull away.
	/// </summary>
	private const float ThrottleOpposingForceEpsilon = 5.0f;

	private VehicleOperatingParams _state;

	/// <summary>
	/// The wheel torques are brakes, not drive.
	/// </summary>
	private bool _braking;
	private bool _handbrakeOn;
	private TireType _tireType = TireType.Normal;

	private float _bodyMass;
	private float _totalWheelMass;
	private float _wheelRadius;
	private float _torqueScale = 1f;
	private float _steeringAngle;

	private float _boostSecondsLeft;
	private float _boostSecondsUntilReady;

	private float _powerslideFrontAccel;
	private float _powerslideRearAccel;

	private enum TireType
	{
		Normal,
		Braking,
		Powerslide,
	}

	private static float MphToIns( float mph ) => mph * MphToMetersPerSecond * InchesPerMeter;
	private static float InsToMph( float ins ) => ins * MetersPerInch / MphToMetersPerSecond;

	// Driving speeds follow vehicle size; gravity remains independent of scale.
	private float HandlingSpeedScale => FunctionalScale;
	private float MaxSpeedIns => MphToIns( Engine.MaxSpeed ) * HandlingSpeedScale;
	private float MaxRevSpeedIns => MphToIns( Engine.MaxReverseSpeed ) * HandlingSpeedScale;
	private float BoostMaxSpeedIns => MphToIns( Engine.BoostMaxSpeed ) * HandlingSpeedScale;

	/// <summary>
	/// Gravity in meters per second squared - what the tuning's multiples of gravity are of.
	/// </summary>
	private float GravityLength => (Gravity > 0 ? Gravity : Scene.PhysicsWorld.Gravity.Length) * MetersPerInch;

	/// <summary>
	/// Scale on the scene's gravity that gives us <see cref="Gravity"/>.
	/// </summary>
	private float GravityScale => Gravity > 0 && Scene.PhysicsWorld.Gravity.Length > 0 ? Gravity / Scene.PhysicsWorld.Gravity.Length : 1f;

	private VehicleAxleParams AxleOf( int wheelIndex ) => wheelIndex < 2 ? FrontAxle : RearAxle;
	private int GearCount => Math.Max( 1, Engine.GearRatios?.Count ?? 0 );
	private float GearRatio( int gear ) => Engine.GearRatios is { Count: > 0 } ratios ? ratios[gear.Clamp( 0, ratios.Count - 1 )] : 1f;

	private bool IsBoostActive => _boostSecondsLeft > 0.0f;

	private void InitDrivetrain()
	{
		// the drive is shared between the axles by their torque factors
		var totalTorqueDistribution = FrontAxle.TorqueFactor + RearAxle.TorqueFactor;
		_torqueScale = totalTorqueDistribution > 0 ? 1f / totalTorqueDistribution : 1f;

		_totalWheelMass = 2 * (FrontAxle.WheelMass + RearAxle.WheelMass) * VehicleMassScale;
		_wheelRadius = MathF.Max( FrontAxle.WheelRadius, RearAxle.WheelRadius ) * FunctionalScale * MetersPerInch;
	}

	/// <summary>
	/// Run the drivetrain for a tick with the driver's controls.
	/// </summary>
	private void UpdateDrivetrain( float dt, VehicleControlParams controls )
	{
		_state.Speed = BodySpeed();
		var speed = InsToMph( _state.Speed ) / HandlingSpeedScale;
		var absSpeed = MathF.Abs( speed );

		var throttle = controls.Throttle;
		var handbrake = controls.Handbrake;
		var brake = controls.Brake;
		var powerslide = handbrake && (absSpeed > 18.0f);

		if ( handbrake )
		{
			throttle = 0.0f;
		}

		// boosting is flat out
		if ( IsBoostActive )
		{
			controls.Boost = 1;
			throttle = throttle < 0.0f ? -1.0f : 1.0f;
		}

		// coasting drags a little
		if ( throttle == 0.0f && brake == 0.0f && !handbrake )
		{
			brake = 0.1f;
		}

		UpdateSteering( controls.Steering, absSpeed );
		UpdatePowerslide( controls, powerslide, speed );
		UpdateEngine( controls, dt, throttle, brake );
		UpdateHandbrake( throttle, handbrake, powerslide );
		UpdateSkidding( handbrake );
		LimitSpin();
	}

	/// <summary>
	/// Speed along the body's forward axis, in/s, over the last tick (see <see cref="MeasuredVelocity"/>).
	/// </summary>
	private float BodySpeed() => (_body.Velocity - BodyGravity * (Time.Delta * 0.5f)).Dot( WorldRotation.Forward );

	private void UpdateSteering( float steering, float speed )
	{
		var degrees = SteeringCone( speed );

		// the input is integrated, so this keeps the time to full lock the same at any speed
		var steeringAngle = MathF.Pow( MathF.Abs( steering ), Steering.SteeringExponent ) * Sign( steering ) * degrees;

		SetSteeringAngle( steeringAngle.DegreeToRadian() );
		_state.SteeringAngle = steeringAngle;
	}

	/// <summary>
	/// The most the front wheels can turn at a speed (mph), in degrees: less the faster the car goes.
	/// </summary>
	private float SteeringCone( float speed )
	{
		var degrees = RemapValClamped( speed, Steering.SpeedSlow, Steering.SpeedFast, Steering.DegreesSlow, Steering.DegreesFast );
		var speedGame = MphToIns( speed ) * HandlingSpeedScale;
		if ( speedGame > MaxSpeedIns )
		{
			degrees = RemapValClamped( speedGame, MaxSpeedIns, BoostMaxSpeedIns, Steering.DegreesFast, Steering.DegreesBoost );
		}

		return MathF.Atan( MathF.Tan( degrees.DegreeToRadian() ) * (VehicleScale / FunctionalScale) ).RadianToDegree();
	}

	/// <summary>
	/// Point both front wheels at the angle. Positive is to the right, in radians.
	/// </summary>
	private void SetSteeringAngle( float angle )
	{
		if ( _steeringAngle == angle ) return;

		_steeringAngle = angle;
		_wheels[0].SteerAngle = angle;
		_wheels[1].SteerAngle = angle;
	}

	/// <summary>
	/// On the handbrake at speed the tyres swap for slippier ones, and steering into it pushes the nose and tail apart.
	/// </summary>
	private void UpdatePowerslide( VehicleControlParams controls, bool powerslide, float speed )
	{
		if ( !_hasDriver )
			return;

		var powerslideLeft = powerslide && controls.HandbrakeLeft;
		var powerslideRight = powerslide && controls.HandbrakeRight;

		var newTireType = powerslideLeft || powerslideRight ? TireType.Powerslide : powerslide ? TireType.Braking : TireType.Normal;
		if ( newTireType != _tireType )
		{
			_tireType = newTireType;
			for ( int i = 0; i < WheelCount; i++ )
			{
				SetWheelFriction( i, _tireType );
			}
		}

		// positive is a push to the left
		var frontAccel = 0.0f;
		if ( speed > 0 && (powerslideLeft != powerslideRight) )
		{
			var amount = RemapValClamped( speed, Steering.SpeedSlow, Steering.SpeedFast, 0, 1 );
			frontAccel = Steering.PowerSlideAccel * FunctionalScale * MetersPerInch * amount * (powerslideLeft ? 1 : -1);
		}

		_powerslideFrontAccel = frontAccel;
		_powerslideRearAccel = -frontAccel;
	}

	private void SetWheelFriction( int wheelIndex, TireType type )
	{
		var axle = AxleOf( wheelIndex );
		_wheels[wheelIndex].TyreFriction = type switch
		{
			TireType.Powerslide => axle.SkidFriction,
			TireType.Braking => axle.BrakeFriction,
			_ => axle.Friction,
		};
	}

	private void UpdateEngine( VehicleControlParams controls, float dt, float throttle, float brake )
	{
		var boosting = controls.Boost > 0;
		if ( boosting && _boostSecondsUntilReady <= 0.0f )
		{
			_boostSecondsLeft = Engine.BoostDuration;
			_boostSecondsUntilReady = Engine.BoostDuration + Engine.BoostDelay;
		}

		if ( _boostSecondsUntilReady > 0.0f )
			_boostSecondsUntilReady -= dt;

		if ( _boostSecondsLeft > 0.0f )
			_boostSecondsLeft -= dt;

		_state.BoostDelay = _boostSecondsUntilReady;
		_state.IsTorqueBoosting = boosting;

		CalcEngine( throttle, brake, controls.Steering, boosting );

		// the boost meter: full when it's ready, filling back up as it recharges
		var boostCycle = Engine.BoostDuration + Engine.BoostDelay;
		if ( boostCycle > 0 )
		{
			_state.BoostTimeLeft = _state.BoostDelay > 0 ? (int)(100 - 100 * (_state.BoostDelay / boostCycle)) : 100;
		}
	}

	private void UpdateHandbrake( float throttle, bool handbrake, bool powerslide )
	{
		// rolling the wrong way against the throttle: lock the wheels, so a hill start doesn't roll back
		if ( !powerslide &&
			((throttle < 0.0f && _state.Speed > ThrottleOpposingForceEpsilon * FunctionalScale) ||
			 (throttle > 0.0f && _state.Speed < -ThrottleOpposingForceEpsilon * FunctionalScale)) )
		{
			handbrake = true;
		}

		// in the air, locked wheels would only throw the car around
		if ( handbrake )
		{
			handbrake = _wheels.Any( w => w.InContact );
		}

		if ( handbrake != _handbrakeOn )
		{
			_handbrakeOn = handbrake;

			foreach ( var wheel in _wheels )
				wheel.Locked = handbrake;
		}
	}

	/// <summary>
	/// Wheels on the ground, and how fast the fastest sliding one goes - or the car's speed, sliding on the handbrake.
	/// </summary>
	private void UpdateSkidding( bool handbrake )
	{
		_state.WheelsInContact = _wheels.Count( w => w.InContact );
		_state.SkidSpeed = _wheels.Where( w => w.InContact ).Select( w => w.SlideSpeed ).DefaultIfEmpty( 0 ).Max();

		var absSpeed = MathF.Abs( _state.Speed );
		if ( handbrake && (absSpeed > 30) )
		{
			_state.SkidSpeed = absSpeed;
		}
	}

	private void LimitSpin()
	{
		var angVel = _body.AngularVelocity;
		var maxAngVel = Body.MaxAngularVelocity.DegreeToRadian();
		if ( Body.MaxAngularVelocity > 0 && angVel.Length > maxAngVel )
		{
			_body.AngularVelocity = angVel.Normal * maxAngVel;
		}
	}

	/// <summary>
	/// The automatic gearbox: the engine rpm from how fast the wheels turn, shifting up (only while accelerating)
	/// and down at the tuning's rpms.
	/// </summary>
	private void UpdateGearbox( float throttle )
	{
		// average wheel revolutions a second
		var revsPerSecond = _wheels.Sum( w => MathF.Abs( w.AngularVelocity ) ) * 0.5f / MathF.PI / WheelCount;
		float RpmInGear( int gear ) => revsPerSecond * (VehicleScale / FunctionalScale) * Engine.AxleRatio * GearRatio( gear ) * 60;

		if ( throttle > 0.0f )
		{
			while ( RpmInGear( _state.Gear ) > Engine.ShiftUpRpm && _state.Gear < GearCount - 1 )
				_state.Gear++;
		}

		while ( RpmInGear( _state.Gear ) < Engine.ShiftDownRpm && _state.Gear > 0 )
			_state.Gear--;

		_state.EngineRpm = RpmInGear( _state.Gear );
	}

	/// <summary>
	/// The wheels' torques, in Newton meters, from the throttle [-1, 1] or the brake [0, 1].
	/// </summary>
	private void CalcEngine( float throttle, float brake, float steering, bool boosting )
	{
		UpdateGearbox( throttle );

		var absSpeed = MathF.Abs( _state.Speed );

		// Speed governor: over the top speed the throttle fades, and further over, the brakes come on
		var maxSpeed = MathF.Max( 1, boosting ? BoostMaxSpeedIns : MaxSpeedIns );
		if ( (throttle > 0) && (absSpeed > maxSpeed) )
		{
			var frac = absSpeed / maxSpeed;
			if ( frac > Engine.AutobrakeSpeedGain )
			{
				throttle = 0;
				brake = _state.WheelsInContact == 0 ? 0 : (frac - 1.0f) * Engine.AutobrakeSpeedFactor;
			}
			throttle *= 0.1f;
		}

		// and in reverse
		if ( (throttle < 0) && !boosting && (absSpeed > MaxRevSpeedIns) )
		{
			throttle *= 0.1f;
		}

		_braking = false;

		if ( throttle != 0.0f )
		{
			// the force that propels the car, from the engine's power through the gears
			const float wattPerHp = 745.0f;
			const float secondsPerMinute = 60.0f;

			var wheelForceByThrottle = throttle *
				Engine.Horsepower * VehicleMassScale * FunctionalScale * FunctionalScale * (wattPerHp * secondsPerMinute) *
				GearRatio( _state.Gear ) * Engine.AxleRatio /
				(Engine.MaxRpm * _wheelRadius * (2 * MathF.PI));

			if ( _state.EngineRpm >= Engine.MaxRpm )
			{
				wheelForceByThrottle = 0;
			}

			for ( int i = 0; i < WheelCount; i++ )
			{
				var axle = AxleOf( i );

				// boost multiplies the torque - less while turning
				var boostFactor = 0.5f;
				if ( boosting && IsBoostActive )
				{
					var speedFactor = RemapValClamped( absSpeed, 0, MaxSpeedIns, Engine.BoostLowSpeedFactor, 1.0f );
					var turnFactor = 1.0f - (MathF.Abs( steering ) * 0.95f);
					boostFactor = MathF.Max( boostFactor, Engine.BoostForce * speedFactor * turnFactor );
				}

				_wheels[i].Torque = boostFactor * wheelForceByThrottle * axle.TorqueFactor * _torqueScale * _wheels[i].Radius * MetersPerInch;
			}
		}
		else if ( brake != 0 )
		{
			_braking = true;

			var wheelForceByBrake = brake * GravityLength * (_bodyMass + _totalWheelMass);
			var sign = _state.Speed >= 0.0f ? -1.0f : 1.0f;
			for ( int i = 0; i < WheelCount; i++ )
			{
				var axle = AxleOf( i );
				_wheels[i].Torque = 0.5f * sign * wheelForceByBrake * axle.BrakeFactor * _wheels[i].Radius * MetersPerInch;
			}
		}
		else
		{
			foreach ( var wheel in _wheels )
				wheel.Torque = 0;
		}
	}

	/// <summary>
	/// The driver got out: normal tyres back on, and the handbrake.
	/// </summary>
	private void OnDriverExit()
	{
		for ( int i = 0; i < WheelCount; i++ )
		{
			if ( _tireType != TireType.Normal )
			{
				SetWheelFriction( i, TireType.Normal );
			}

			_wheels[i].Locked = true;
		}

		_tireType = TireType.Normal;
		_state.SkidSpeed = 0.0f;
		_powerslideFrontAccel = 0;
		_powerslideRearAccel = 0;
	}

	/// <summary>
	/// Pushes on the body itself: extra gravity, and the power slide pushing the nose and tail sideways, two meters
	/// either side of the centre of mass. Both scale with the body's mass alone, without the wheels.
	/// </summary>
	private void SimulateBodyForces( float dt )
	{
		var body = _body.PhysicsBody;
		var center = body.MassCenter;

		var extraGravity = Body.AddGravity * GravityLength * _bodyMass;
		if ( extraGravity != 0 )
		{
			body.ApplyImpulseAt( center, Scene.PhysicsWorld.Gravity.Normal * (extraGravity * dt * InchesPerMeter) );
		}

		if ( _powerslideFrontAccel != 0 || _powerslideRearAccel != 0 )
		{
			var forward = WorldRotation.Forward * (2.0f * InchesPerMeter * VehicleScale);
			var left = WorldRotation.Left;
			body.ApplyImpulseAt( center + forward, left * (_bodyMass * _powerslideFrontAccel * dt * InchesPerMeter) );
			body.ApplyImpulseAt( center - forward, left * (_bodyMass * _powerslideRearAccel * dt * InchesPerMeter) );
		}
	}
}
