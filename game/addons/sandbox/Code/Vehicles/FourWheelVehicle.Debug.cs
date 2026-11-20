/// <summary>
/// The tuning readout: everything the vehicle is doing, painted into the HUD by <see cref="VehicleDebugSection"/>.
/// Turn it on for the vehicle you're driving with <c>vehicle_debug 1</c>, or for one vehicle with its Draw Debug
/// property. Host only, since only the host simulates.
/// </summary>
public sealed partial class FourWheelVehicle
{
	[ConVar( "sandbox.vehicle.debug", Help = "Show the vehicle tuning overlay for the vehicle you're in (or the nearest one)." )]
	public static bool DebugOverlayEnabled { get; set; } = false;

	private static readonly string[] DebugWheelNames = ["FL", "FR", "RL", "RR"];

	private bool ShouldDrawDebug()
	{
		if ( DrawDebug ) return true;
		if ( !DebugOverlayEnabled ) return false;

		// the vehicle the local player is driving, otherwise the nearest one to the camera
		var vehicles = Scene.GetAllComponents<FourWheelVehicle>().ToArray();
		var driven = vehicles.FirstOrDefault( v => v.IsDrivenLocally() );
		if ( driven is not null ) return driven == this;

		var camera = Scene.Camera;
		if ( camera is null ) return false;

		return vehicles.OrderBy( v => v.WorldPosition.DistanceSquared( camera.WorldPosition ) ).First() == this;
	}

	private bool IsDrivenLocally()
	{
		if ( !Seat.IsValid() || !Seat.IsOccupied ) return false;

		var player = Seat.GetOccupant()?.GetComponent<Player>();
		return player.IsValid() && !player.IsProxy;
	}

	private VehicleDebugSection _debugSection;

	/// <summary>
	/// Fill the HUD's vehicle readout, or hide it if this vehicle was showing it.
	/// </summary>
	private void UpdateDebugHud( bool show )
	{
		if ( !show )
		{
			if ( _debugSection is not null && _debugSection.Data.Title == GameObject.Root.Name )
				_debugSection.Data = default;
			return;
		}

		_debugSection = HudLayer.FindSection( this, _debugSection );
		if ( _debugSection is null ) return;

		var sounds = GetComponent<VehicleSounds>();
		var mph = InsToMph( _state.Speed );

		_debugSection.Data = new VehicleDebugSection.Readout
		{
			Visible = true,
			Title = GameObject.Root.Name,
			EngineOn = IsEngineOn,
			Mph = MathF.Abs( mph ),
			GovernorMph = Engine.MaxSpeed,
			Gear = _state.Gear + 1,
			GearCount = GearCount,
			Rpm = _state.EngineRpm,
			SoundRpm = SoundRpm,
			MaxRpm = Engine.MaxRpm,
			ShiftUpRpm = Engine.ShiftUpRpm,
			ShiftDownRpm = Engine.ShiftDownRpm,
			Throttle = _controls.Throttle,
			Brake = _controls.Brake,
			Steering = _controls.Steering,
			SteeringAngle = _state.SteeringAngle,
			SteeringCone = SteeringCone( MathF.Abs( mph ) ),
			Handbrake = _controls.Handbrake,
			WheelsLocked = _handbrakeOn,
			Boosting = IsBoosting(),
			BoostPercent = _state.BoostTimeLeft,
			FreeRevving = _freeRevving,
			TireMode = _tireType.ToString(),
			SoundState = sounds?.State.ToString() ?? "-",
			WheelsInContact = _state.WheelsInContact,
			SkidSpeed = _state.SkidSpeed,
			Pitch = WorldRotation.Pitch(),
			Roll = WorldRotation.Roll(),
			TickRate = (int)ProjectSettings.Physics.FixedUpdateFrequency,
			Wheels = Enumerable.Range( 0, WheelCount ).Select( i =>
			{
				var wheel = _wheels[i];
				var info = WheelDebug( wheel );
				return new VehicleDebugSection.WheelReadout
				{
					Name = DebugWheelNames[i],
					Height = info.Height,
					TravelDown = wheel.SpringLength,
					TravelUp = wheel.Radius,
					Torque = wheel.Torque,
					Spin = wheel.AngularVelocity,
					RollSpin = info.RollSpin,
					SlipRatio = info.SlipRatio,
					SlipAngle = info.SlipAngle,
					Load = wheel.Load,
					Contact = wheel.InContact,
					Locked = wheel.Locked,
					Sliding = wheel.InContact && wheel.AtGripLimit,
					Steers = i < 2,
					Steer = wheel.SteerAngle.RadianToDegree(),
				};
			} ).ToArray(),
		};
	}

	private struct WheelDebugInfo
	{
		public float Height;
		public float RollSpin;
		public float SlipRatio;
		public float SlipAngle;
	}

	/// <summary>
	/// Slip the way tyre models think of it: how much faster the tread turns than the ground goes past under it
	/// (ratio) and how far the contact patch's travel is from where the wheel points (angle). The wheels
	/// always turn at the body's speed, so the ratio only shows locked wheels and the car turning about itself.
	/// </summary>
	private WheelDebugInfo WheelDebug( Wheel wheel )
	{
		var velocity = MeasuredVelocity( wheel.Contact );
		var along = velocity.Dot( wheel.ProjectedForward );
		var across = velocity.Dot( wheel.ProjectedAxle );
		var surface = wheel.AngularVelocity * wheel.Radius;

		return new WheelDebugInfo
		{
			Height = wheel.Radius - wheel.GroundDistance,
			RollSpin = along / wheel.Radius,
			SlipRatio = (surface - along) / MathF.Max( MathF.Abs( along ), 20 ),
			SlipAngle = MathF.Abs( along ) < 5 ? 0 : MathF.Atan2( across, MathF.Abs( along ) ).RadianToDegree(),
		};
	}
}
