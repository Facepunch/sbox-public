/// <summary>
/// The engine and handling tuning players can change from the inspector, in plain terms. Each one reads and writes
/// the real tuning in <see cref="Engine"/>, <see cref="Steering"/> and the axles, so that stays the only thing saved
/// and synced - these aren't [Property] themselves. Some tuning is copied out when the car is set up (wheel grip,
/// drive split), so a change rebuilds it on the host's next tick.
/// </summary>
public sealed partial class FourWheelVehicle
{
	const string EngineGroup = "#entity.group.vehicle_engine";
	const string HandlingGroup = "#entity.group.vehicle_handling";

	[ClientEditable, Group( EngineGroup ), Range( 5, 150 ), Title( "#entity.property.vehicle_top_speed" ), Description( "#entity.description.vehicle_top_speed" )]
	public float TopSpeed
	{
		get => Engine.MaxSpeed;
		set => Tune( () => Engine.MaxSpeed = Sane( value, 5, 150 ) );
	}

	[ClientEditable, Group( EngineGroup ), Range( 1, 60 ), Title( "#entity.property.vehicle_reverse_speed" )]
	public float ReverseSpeed
	{
		get => Engine.MaxReverseSpeed;
		set => Tune( () => Engine.MaxReverseSpeed = Sane( value, 1, 60 ) );
	}

	[ClientEditable, Group( EngineGroup ), Range( 50, 2000 ), Step( 10 ), Title( "#entity.property.vehicle_power" ), Description( "#entity.description.vehicle_power" )]
	public float Power
	{
		get => Engine.Horsepower;
		set => Tune( () => Engine.Horsepower = Sane( value, 50, 2000 ) );
	}

	[ClientEditable, Group( EngineGroup ), Range( 1, 5 ), Step( 0.1f ), Title( "#entity.property.vehicle_boost_force" )]
	public float BoostForce
	{
		get => Engine.BoostForce;
		set => Tune( () => Engine.BoostForce = Sane( value, 1, 5 ) );
	}

	[ClientEditable, Group( EngineGroup ), Range( 5, 200 ), Title( "#entity.property.vehicle_boost_speed" )]
	public float BoostTopSpeed
	{
		get => Engine.BoostMaxSpeed;
		set => Tune( () => Engine.BoostMaxSpeed = Sane( value, 5, 200 ) );
	}

	[ClientEditable, Group( HandlingGroup ), Range( 0.2f, 4 ), Step( 0.05f ), Title( "#entity.property.vehicle_grip" ), Description( "#entity.description.vehicle_grip" )]
	public float Grip
	{
		get => RearAxle.Friction;
		set => Tune( () => FrontAxle.Friction = RearAxle.Friction = Sane( value, 0.2f, 4 ) );
	}

	[ClientEditable, Group( HandlingGroup ), Range( 10, 80 ), Title( "#entity.property.vehicle_steering" ), Description( "#entity.description.vehicle_steering" )]
	public float SteeringLock
	{
		get => Steering.DegreesSlow;
		set => Tune( () => Steering.DegreesSlow = Sane( value, 10, 80 ) );
	}

	[ClientEditable, Group( HandlingGroup ), Range( 5, 60 ), Title( "#entity.property.vehicle_fast_steering" ), Description( "#entity.description.vehicle_fast_steering" )]
	public float FastSteeringLock
	{
		get => Steering.DegreesFast;
		set => Tune( () => Steering.DegreesFast = Sane( value, 5, 60 ) );
	}

	[ClientEditable, Group( HandlingGroup ), Range( 0, 1 ), Step( 0.05f ), Title( "#entity.property.vehicle_drive_bias" ), Description( "#entity.description.vehicle_drive_bias" )]
	public float RearDriveBias
	{
		get => RearAxle.TorqueFactor + FrontAxle.TorqueFactor > 0 ? RearAxle.TorqueFactor / (RearAxle.TorqueFactor + FrontAxle.TorqueFactor) : 0.5f;
		set => Tune( () =>
		{
			var rear = Sane( value, 0, 1 );
			RearAxle.TorqueFactor = rear;
			FrontAxle.TorqueFactor = 1 - rear;
		} );
	}

	bool _tuningChanged;

	void Tune( Action change )
	{
		change();
		_tuningChanged = true;
	}

	/// <summary>
	/// Within the slider's range - these come from other clients.
	/// </summary>
	static float Sane( float value, float min, float max ) => float.IsFinite( value ) ? value.Clamp( min, max ) : min;

	/// <summary>
	/// Rebuild the parts of the car that copied the tuning when it was set up. Host only, like the simulation.
	/// </summary>
	void ApplyTuningChanges()
	{
		if ( !_tuningChanged ) return;
		_tuningChanged = false;

		if ( _carSystemReady ) InitCarSystem();
	}
}
