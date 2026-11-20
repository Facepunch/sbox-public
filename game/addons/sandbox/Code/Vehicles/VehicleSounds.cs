/// <summary>
/// What the engine's doing, as far as its sound goes. Each state plays its own recording.
/// </summary>
public enum VehicleSoundState
{
	None,
	Shutdown,
	StartIdle,
	Idle,
	Gear0,
	Gear1,
	Gear2,
	Gear3,
	Gear4,
	Slowdown,
	SlowdownHighSpeed,
	Gear0Resume,
	Gear1Resume,
	Gear2Resume,
	Gear3Resume,
	Gear4Resume,
	Turbo,
	Reverse,
}

/// <summary>
/// A band of speed (as a fraction of top speed) one gear sound covers.
/// </summary>
public sealed class VehicleSoundGear
{
	[Property] public float MaxSpeed { get; set; }
	[Property] public float SpeedApproachFactor { get; set; } = 1;
}

/// <summary>
/// The recording for a sound state.
/// </summary>
public sealed class VehicleStateSound
{
	[Property] public VehicleSoundState State { get; set; }

	/// <summary>
	/// Recording for this state. Left empty, the state is silent.
	/// </summary>
	[Property] public SoundEvent Sound { get; set; }

	/// <summary>
	/// Seconds to stay in this state before moving on.
	/// </summary>
	[Property] public float MinTime { get; set; }
}

/// <summary>
/// An impact sound for the body hitting something. The hardest one a hit reaches plays.
/// </summary>
public sealed class VehicleCrashSound
{
	/// <summary>
	/// How hard the body has to hit, in/s along the contact normal.
	/// </summary>
	[Property] public float MinSpeed { get; set; }
	[Property] public SoundEvent Sound { get; set; }

	/// <summary>
	/// Knock the engine sound back to idle, as if the driver lifted off.
	/// </summary>
	[Property] public bool DropsToIdle { get; set; } = true;
}

/// <summary>
/// Engine, gear, skid and crash sounds for a <see cref="FourWheelVehicle"/>: one recording per sound state, chosen
/// from the throttle and how fast the car is going. And the exhaust backfires - a crackle when you lift off from high
/// revs, pops against the rev limiter and on hard upshifts - with a flame out of the pipe.
///
/// Runs on everyone from the engine state the host syncs on the vehicle.
/// </summary>
[Title( "Vehicle Sounds" ), Category( "Vehicles" ), Icon( "volume_up" )]
public sealed class VehicleSounds : Component, IPlayerControllable
{
	[Property] public FourWheelVehicle Vehicle { get; set; }

	/// <summary>
	/// Where the exhaust comes out, pointing out of the pipe. Backfire flames come from here.
	/// </summary>
	[Property, Group( "Exhaust" )] public GameObject Exhaust { get; set; }

	/// <summary>
	/// Spawned at the exhaust on each backfire.
	/// </summary>
	[Property, Group( "Exhaust" )] public GameObject BackfireEffect { get; set; }

	[Property, Group( "Exhaust" )] public SoundEvent BackfireSound { get; set; }

	/// <summary>
	/// Most backfires a second in the crackle after lifting off.
	/// </summary>
	[Property, Group( "Exhaust" )] public float CrackleRate { get; set; } = 6f;

	/// <summary>
	/// Revs the crackle starts from, as a fraction of max rpm.
	/// </summary>
	[Property, Group( "Exhaust" ), Range( 0, 1 )] public float CrackleRevs { get; set; } = 0.55f;

	/// <summary>
	/// Backfires a second against the rev limiter.
	/// </summary>
	[Property, Group( "Exhaust" )] public float LimiterRate { get; set; } = 3f;

	/// <summary>
	/// Pop on upshifts with the throttle down.
	/// </summary>
	[Property, Group( "Exhaust" )] public bool ShiftPops { get; set; } = true;

	/// <summary>
	/// As the vehicle speeds up it moves through these, each with its own gear sound.
	/// </summary>
	[Property, Group( "Engine" )]
	public List<VehicleSoundGear> Gears { get; set; } =
	[
		new() { MaxSpeed = 0.27f, SpeedApproachFactor = 1.0f },
		new() { MaxSpeed = 0.5f, SpeedApproachFactor = 0.05f },
		new() { MaxSpeed = 0.75f, SpeedApproachFactor = 0.052f },
		new() { MaxSpeed = 0.95f, SpeedApproachFactor = 0.034f },
		new() { MaxSpeed = 1.5f, SpeedApproachFactor = 0.033f },
		new() { MaxSpeed = 2.0f, SpeedApproachFactor = 0.03f },
	];

	[Property, Group( "Engine" )]
	public List<VehicleStateSound> States { get; set; } =
	[
		new() { State = VehicleSoundState.StartIdle, MinTime = 4.0f },
		new() { State = VehicleSoundState.Idle },
		new() { State = VehicleSoundState.Reverse, MinTime = 0.5f },
		new() { State = VehicleSoundState.Gear0, MinTime = 0.5f },
		new() { State = VehicleSoundState.Gear0Resume, MinTime = 0.75f },
		new() { State = VehicleSoundState.Gear1, MinTime = 0.5f },
		new() { State = VehicleSoundState.Gear1Resume, MinTime = 0.5f },
		new() { State = VehicleSoundState.Gear2, MinTime = 0.5f },
		new() { State = VehicleSoundState.Gear2Resume, MinTime = 0.5f },
		new() { State = VehicleSoundState.Gear3, MinTime = 0.5f },
		new() { State = VehicleSoundState.Gear3Resume, MinTime = 0.5f },
		new() { State = VehicleSoundState.Gear4, MinTime = 0.5f },
		new() { State = VehicleSoundState.Gear4Resume, MinTime = 0.5f },
		new() { State = VehicleSoundState.SlowdownHighSpeed, MinTime = 2.0f },
		new() { State = VehicleSoundState.Slowdown, MinTime = 2.0f },
		new() { State = VehicleSoundState.Turbo, MinTime = 2.5f },
		new() { State = VehicleSoundState.Shutdown },
	];

	[Property, Group( "Impacts" )]
	public List<VehicleCrashSound> CrashSounds { get; set; } =
	[
		new() { MinSpeed = 80, DropsToIdle = false },
		new() { MinSpeed = 250 },
		new() { MinSpeed = 500 },
	];

	/// <summary>
	/// Seconds after an impact sound before another one that isn't harder can play - several shapes and substeps
	/// report the same hit.
	/// </summary>
	[Property, Group( "Impacts" )] public float ImpactCooldown { get; set; } = 0.15f;

	/// <summary>
	/// Looped while skidding.
	/// </summary>
	[Property, Group( "Tyres" )] public SoundEvent SkidSound { get; set; }

	/// <summary>
	/// Played at a wheel when it's shoved up into the body - landing, bumps, kerbs.
	/// </summary>
	[Property, Group( "Suspension" )] public SoundEvent SuspensionSound { get; set; }

	/// <summary>
	/// How fast a wheel has to be pushed up (in/s, in the model's own scale) to make a sound, and how fast for full
	/// volume. Read from the posed wheels, which ease towards the host's suspension, so it peaks lower than the real
	/// spring - about 30x the jump in travel.
	/// </summary>
	[Property, Group( "Suspension" )] public float SuspensionMinSpeed { get; set; } = 50f;
	[Property, Group( "Suspension" )] public float SuspensionMaxSpeed { get; set; } = 200f;

	/// <summary>
	/// Seconds before the same wheel can clunk again.
	/// </summary>
	[Property, Group( "Suspension" )] public float SuspensionCooldown { get; set; } = 0.3f;

	/// <summary>
	/// Sounds while the driver holds <see cref="Horn"/>, until they let go or it runs out.
	/// </summary>
	[Property, Group( "Horn" )] public SoundEvent HornSound { get; set; }
	[Property, Group( "Horn" )] public ClientInput Horn { get; set; } = new() { Action = "Attack1" };

	/// <summary>
	/// How long the horn takes to fade out when let go early, seconds.
	/// </summary>
	[Property, Group( "Horn" )] public float HornFadeTime { get; set; } = 0.1f;

	/// <summary>
	/// The driver's holding the horn.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public bool HornDown { get; private set; }

	public VehicleSoundState State => _state;

	private const float SkidThreshold = 10.0f;

	private VehicleSoundState _state = VehicleSoundState.None;
	private float _stateStartTime;
	private int _soundGear;
	private float _speedPercentage;
	private bool _wasOn;

	private bool _crashed;
	private float _pendingImpactSpeed;
	private Vector3 _pendingImpactPosition;
	private TimeSince _sinceImpact;
	private float _lastImpactSpeed;

	private SoundHandle _hornSound;
	private bool _wasHornDown;

	private SoundHandle _stateSound;
	private SoundEvent _stateSoundEvent;
	private SoundHandle _skidSound;
	private bool _lastSkid;
	private int _lastGear;

	private float _overrunCharge;
	private readonly List<float> _pendingBackfires = new();

	private readonly TimeSince[] _sinceSuspension = new TimeSince[FourWheelVehicle.WheelCount];

	protected override void OnDestroy()
	{
		StopStateSound();
		_skidSound?.Stop();
		_hornSound?.Stop();
	}

	/// <summary>
	/// The horn key. Runs on the host, from the driver's seat.
	/// </summary>
	void IPlayerControllable.OnControl()
	{
		HornDown = Horn.Down();
	}

	protected override void OnUpdate()
	{
		if ( !Vehicle.IsValid() ) return;

		var on = Vehicle.IsEngineOn;
		if ( on && !_wasOn )
			SoundStart();
		_wasOn = on;

		if ( _state != VehicleSoundState.None )
		{
			SoundUpdate( !on );
			UpdateSkid();
		}

		UpdateBackfires();
		UpdateSuspension();
		UpdateHorn();

		// a crash only knocks the engine sound down the frame it lands
		_crashed = false;
	}

	private void SoundStart()
	{
		_state = VehicleSoundState.None;
		SoundUpdate( false );
	}

	/// <summary>
	/// Move through the sound gears with the speed and pick the state - the throttle, reverse, boost and speed come
	/// from the host.
	/// </summary>
	private void SoundUpdate( bool exitVehicle )
	{
		var currentSpeed = (Vehicle.SpeedMph / MathF.Max( 1, Vehicle.Engine.MaxSpeed )).Clamp( 0, 1 );

		// Figure out our speed for the purposes of sound playing.
		// We slow the transition down a little to make the gear changes slower.
		if ( Gears.Count > 0 && currentSpeed > _speedPercentage )
		{
			// don't accelerate when the throttle isn't down
			if ( !Vehicle.ThrottleDown )
				currentSpeed = _speedPercentage;

			currentSpeed = VehicleMath.Approach( currentSpeed, _speedPercentage, Time.Delta * Gears[_soundGear.Clamp( 0, Gears.Count - 1 )].SpeedApproachFactor );
		}
		_speedPercentage = currentSpeed.Clamp( 0, 1 );

		RecalculateSoundGear();

		var newState = ChooseState( exitVehicle );
		if ( newState != _state )
		{
			_state = newState;
			OnNewState();
		}
	}

	private void RecalculateSoundGear()
	{
		for ( int i = Gears.Count - 1; i >= 0; i-- )
		{
			var minSpeed = i == 0 ? 0 : Gears[i - 1].MaxSpeed;
			if ( _speedPercentage > minSpeed )
			{
				_soundGear = i;
				break;
			}
		}

		// If we're going in reverse, we want to stay in first gear
		if ( Vehicle.Reversing )
			_soundGear = 0;
	}

	private float MinTime( VehicleSoundState state ) => FindState( state )?.MinTime ?? 0;
	private SoundEvent StateSound( VehicleSoundState state ) => FindState( state )?.Sound;

	private VehicleStateSound FindState( VehicleSoundState state )
	{
		ArgumentNullException.ThrowIfNull( States, "source" );

		foreach ( var s in States )
		{
			if ( s.State == state )
				return s;
		}

		return null;
	}

	private VehicleSoundState MapGearToState( int gear ) => gear switch
	{
		0 => Vehicle.Reversing ? VehicleSoundState.Reverse : VehicleSoundState.Gear0,
		1 => VehicleSoundState.Gear1,
		2 => VehicleSoundState.Gear2,
		3 => VehicleSoundState.Gear3,
		_ => VehicleSoundState.Gear4,
	};

	private VehicleSoundState MapGearToMidState( int gear ) => gear switch
	{
		0 => Vehicle.Reversing ? VehicleSoundState.Reverse : VehicleSoundState.Gear0Resume,
		1 => VehicleSoundState.Gear1Resume,
		2 => VehicleSoundState.Gear2Resume,
		3 => VehicleSoundState.Gear3Resume,
		_ => VehicleSoundState.Gear4Resume,
	};

	private VehicleSoundState ChooseState( bool exitVehicle )
	{
		var inStateForMinTime = Time.Now - _stateStartTime > MinTime( _state );
		var throttleDown = Vehicle.ThrottleDown;
		var reverse = Vehicle.Reversing;
		var turbo = Vehicle.Boosting;

		// exit overrides everything else
		if ( exitVehicle )
		{
			return _state is VehicleSoundState.None ? _state : VehicleSoundState.Shutdown;
		}

		// check global state in states that don't mask them
		switch ( _state )
		{
			case VehicleSoundState.Turbo:
				if ( _crashed )
					return VehicleSoundState.Idle;
				break;

			case VehicleSoundState.Reverse:
			case VehicleSoundState.Gear0:
			case VehicleSoundState.Gear1:
			case VehicleSoundState.Gear2:
			case VehicleSoundState.Gear3:
			case VehicleSoundState.Gear4:
			case VehicleSoundState.Slowdown:
			case VehicleSoundState.SlowdownHighSpeed:
			case VehicleSoundState.Gear0Resume:
			case VehicleSoundState.Gear1Resume:
			case VehicleSoundState.Gear2Resume:
			case VehicleSoundState.Gear3Resume:
			case VehicleSoundState.Gear4Resume:
				if ( turbo )
					return VehicleSoundState.Turbo;
				if ( _crashed )
					return VehicleSoundState.Idle;
				break;
		}

		switch ( _state )
		{
			case VehicleSoundState.StartIdle:
				if ( inStateForMinTime || throttleDown )
					return VehicleSoundState.Idle;
				break;

			case VehicleSoundState.Idle:
				if ( inStateForMinTime && throttleDown )
				{
					if ( turbo )
						return VehicleSoundState.Turbo;
					return reverse ? VehicleSoundState.Reverse : VehicleSoundState.Gear0;
				}
				break;

			case VehicleSoundState.Gear0Resume:
			case VehicleSoundState.Gear0:
				if ( (inStateForMinTime && !throttleDown) || reverse )
					return VehicleSoundState.Idle;
				if ( _soundGear > 0 )
					return VehicleSoundState.Gear1;
				break;

			case VehicleSoundState.Gear1Resume:
			case VehicleSoundState.Gear1:
				if ( inStateForMinTime && !throttleDown )
					return VehicleSoundState.Slowdown;
				if ( _soundGear != 1 )
					return MapGearToState( _soundGear );
				break;

			case VehicleSoundState.Gear2Resume:
			case VehicleSoundState.Gear2:
				if ( inStateForMinTime )
				{
					if ( !throttleDown )
						return VehicleSoundState.Slowdown;
					if ( _soundGear != 2 )
						return MapGearToState( _soundGear );
				}
				break;

			case VehicleSoundState.Gear3Resume:
			case VehicleSoundState.Gear3:
				if ( inStateForMinTime )
				{
					if ( !throttleDown )
						return VehicleSoundState.Slowdown;
					if ( _soundGear != 3 )
						return MapGearToState( _soundGear );
				}
				break;

			case VehicleSoundState.Gear4Resume:
			case VehicleSoundState.Gear4:
				if ( inStateForMinTime && !throttleDown )
					return VehicleSoundState.Slowdown;
				if ( _soundGear != 4 )
					return MapGearToMidState( _soundGear );
				break;

			case VehicleSoundState.Reverse:
				if ( inStateForMinTime && !reverse )
					return VehicleSoundState.Slowdown;
				break;

			case VehicleSoundState.SlowdownHighSpeed:
			case VehicleSoundState.Slowdown:
				if ( throttleDown )
					return MapGearToMidState( _soundGear );
				if ( _soundGear == 0 )
					return VehicleSoundState.Idle;
				break;

			case VehicleSoundState.None:
				return VehicleSoundState.StartIdle;

			case VehicleSoundState.Turbo:
				if ( inStateForMinTime && !turbo )
					return MapGearToMidState( _soundGear );
				break;
		}

		return _state;
	}

	private void OnNewState()
	{
		switch ( _state )
		{
			case VehicleSoundState.Shutdown:
				StopStateSound();
				PlayOneShot( StateSound( _state ) );
				break;

			case VehicleSoundState.Idle:
				PlayLoopingSound( StateSound( _state ) );
				break;

			case VehicleSoundState.Slowdown:
			case VehicleSoundState.SlowdownHighSpeed:
				PlayLoopingSound( StateSound( _soundGear < 2 ? VehicleSoundState.Slowdown : VehicleSoundState.SlowdownHighSpeed ) );
				break;

			case VehicleSoundState.None:
				break;

			default:
				PlayLoopingSound( StateSound( _state ) );
				break;
		}

		_stateStartTime = Time.Now;
	}

	private FourWheelVehicle _impactSource;

	protected override void OnEnabled() => HookImpacts();
	protected override void OnDisabled() => UnhookImpacts();

	/// <summary>
	/// Listen for the vehicle's impacts (<see cref="FourWheelVehicle.Impacted"/>) - the body's collisions and the tyres
	/// blocking against walls - following <see cref="Vehicle"/> if it's swapped.
	/// </summary>
	private void HookImpacts()
	{
		if ( _impactSource == Vehicle ) return;

		UnhookImpacts();
		_impactSource = Vehicle;
		if ( _impactSource.IsValid() )
			_impactSource.Impacted += OnVehicleImpact;
	}

	private void UnhookImpacts()
	{
		if ( _impactSource is not null )
			_impactSource.Impacted -= OnVehicleImpact;
		_impactSource = null;
	}

	/// <summary>
	/// On the host: keep the hardest hit this tick, played from <see cref="OnFixedUpdate"/>.
	/// </summary>
	private void OnVehicleImpact( float speed, Vector3 position )
	{
		if ( speed <= _pendingImpactSpeed ) return;

		_pendingImpactSpeed = speed;
		_pendingImpactPosition = position;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;

		HookImpacts();

		// let go of the horn if the driver got out holding it
		if ( !(Vehicle?.Seat?.IsOccupied ?? false) )
			HornDown = false;

		if ( _pendingImpactSpeed <= 0 ) return;

		var speed = _pendingImpactSpeed;
		_pendingImpactSpeed = 0;

		var tier = ImpactTier( speed );
		if ( tier < 0 ) return;
		if ( _sinceImpact < ImpactCooldown && speed <= _lastImpactSpeed ) return;

		_sinceImpact = 0;
		_lastImpactSpeed = speed;
		BroadcastImpact( tier, _pendingImpactPosition );
	}

	/// <summary>
	/// The hardest of <see cref="CrashSounds"/> this speed reaches, whatever order they're listed in. -1 for none.
	/// </summary>
	private int ImpactTier( float speed )
	{
		var tier = -1;
		for ( int i = 0; i < CrashSounds.Count; i++ )
		{
			var crash = CrashSounds[i];
			if ( speed >= crash.MinSpeed && (tier < 0 || crash.MinSpeed > CrashSounds[tier].MinSpeed) )
				tier = i;
		}

		return tier;
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Unreliable )]
	private void BroadcastImpact( int tier, Vector3 position )
	{
		if ( tier < 0 || tier >= CrashSounds.Count ) return;

		var crash = CrashSounds[tier];
		if ( crash.Sound is not null )
			Sound.Play( crash.Sound, position );

		if ( crash.DropsToIdle )
			_crashed = true;
	}

	/// <summary>
	/// The skid sound. We have to slide at least 15% of our speed at higher speeds to make it, otherwise it plays
	/// too often.
	/// </summary>
	private void UpdateSkid()
	{
		const float skidFactor = 0.15f;
		const float minSpeed = SkidThreshold / skidFactor;

		var speed = Vehicle.Speed;
		var threshold = _lastSkid ? SkidThreshold : speed * skidFactor;
		if ( threshold < SkidThreshold )
		{
			// ramp in the skid threshold to avoid the sound at really low speeds unless really skidding
			threshold = VehicleMath.RemapValClamped( MathF.Abs( speed ), 0, minSpeed, SkidThreshold * 8, SkidThreshold );
		}

		var skidding = Vehicle.SkidSpeed > threshold && Vehicle.IsEngineOn;
		if ( skidding != _lastSkid )
		{
			_lastSkid = skidding;

			if ( SkidSound is not null )
			{
				_skidSound?.Stop( 0.1f );
				_skidSound = skidding ? Sound.Play( SkidSound, WorldPosition ) : null;
			}
		}

		if ( _skidSound is not null )
			_skidSound.Position = WorldPosition;
	}

	/// <summary>
	/// A clunk when a wheel is shoved up into the body hard enough, louder the harder. Engine or not - a car dropped
	/// off the physgun lands just the same. Only the hardest hit each frame plays, so four wheels landing together
	/// don't stack up into one loud one.
	/// </summary>
	private void UpdateSuspension()
	{
		if ( SuspensionSound is null ) return;

		var strongest = -1;
		var strongestSpeed = SuspensionMinSpeed;

		for ( int i = 0; i < FourWheelVehicle.WheelCount; i++ )
		{
			var speed = Vehicle.SuspensionSpeed( i );
			if ( speed <= SuspensionMinSpeed || _sinceSuspension[i] < SuspensionCooldown ) continue;

			// it hit either way - don't let it clunk again straight after for the same bump
			_sinceSuspension[i] = 0;

			if ( speed > strongestSpeed )
			{
				strongest = i;
				strongestSpeed = speed;
			}
		}

		if ( strongest < 0 ) return;

		var handle = Sound.Play( SuspensionSound, Vehicle.WheelPosition( strongest ) );
		if ( handle.IsValid() )
			handle.Volume = VehicleMath.RemapValClamped( strongestSpeed, SuspensionMinSpeed, SuspensionMaxSpeed, 0.3f, 1.0f );
	}

	private void UpdateHorn()
	{
		var down = HornDown;

		if ( down && !_wasHornDown && HornSound is not null )
		{
			_hornSound?.Stop( HornFadeTime );
			_hornSound = Sound.Play( HornSound, WorldPosition );
			if ( _hornSound.IsValid() )
			{
				_hornSound.Parent = GameObject;
				_hornSound.FollowParent = true;
			}
		}
		else if ( !down && _wasHornDown )
		{
			_hornSound?.Stop( HornFadeTime );
			_hornSound = null;
		}

		_wasHornDown = down;
	}

	/// <summary>
	/// Fade out the old state's sound and start the new one.
	/// </summary>
	private void PlayLoopingSound( SoundEvent sound )
	{
		if ( sound == _stateSoundEvent && _stateSound.IsValid() )
			return;

		StopStateSound();

		if ( sound is null ) return;

		_stateSoundEvent = sound;
		_stateSound = Sound.Play( sound, WorldPosition );
		if ( _stateSound.IsValid() )
		{
			_stateSound.Parent = GameObject;
			_stateSound.FollowParent = true;
		}
	}

	private void StopStateSound()
	{
		_stateSound?.Stop( 0.2f );
		_stateSound = null;
		_stateSoundEvent = null;
	}

	private void PlayOneShot( SoundEvent sound )
	{
		if ( sound is null ) return;
		Sound.Play( sound, WorldPosition );
	}

	/// <summary>
	/// The exhaust backfiring. Unburnt fuel lights in the hot pipe: for a moment after lifting off from high revs
	/// (it builds up under hard throttle and dies away over about a second), against the rev limiter, and on hard
	/// upshifts. Each one pops and shoots a flame out of the exhaust.
	/// </summary>
	private void UpdateBackfires()
	{
		var on = Vehicle.IsEngineOn;
		var load = on && Vehicle.ThrottleDown;
		var rev = Vehicle.SoundRpm / MathF.Max( 1, Vehicle.Engine.MaxRpm );
		var dt = Time.Delta;

		// heat and fuel through the pipe: builds over a few tenths of a second of hard throttle, fades once you lift
		_overrunCharge = load
			? _overrunCharge + (1 - _overrunCharge) * MathF.Min( 1, dt / 0.4f )
			: _overrunCharge * MathF.Max( 0, 1 - dt / 0.7f );

		if ( on )
		{
			var crackle = load ? 0 : CrackleRate * _overrunCharge * ((rev - CrackleRevs) / MathF.Max( 0.01f, 0.9f - CrackleRevs )).Clamp( 0, 1 );
			var limiter = load && rev > 0.93f ? LimiterRate : 0;
			if ( Random.Shared.Float() < (crackle + limiter) * dt )
				Backfire();

			// upshift with the throttle down: the ignition cut pops a few times
			var gear = Vehicle.Gear;
			if ( ShiftPops && gear > _lastGear && load )
			{
				var at = Time.Now;
				for ( int i = Random.Shared.Next( 1, 4 ); i > 0; i-- )
				{
					at += Random.Shared.Float( 0.03f, 0.09f );
					_pendingBackfires.Add( at );
				}
			}
			_lastGear = gear;
		}

		for ( int i = _pendingBackfires.Count - 1; i >= 0; i-- )
		{
			if ( _pendingBackfires[i] > Time.Now ) continue;

			_pendingBackfires.RemoveAt( i );
			Backfire();
		}
	}

	private void Backfire()
	{
		var origin = Exhaust.IsValid() ? Exhaust.WorldPosition : WorldPosition;
		if ( BackfireSound is not null )
			Sound.Play( BackfireSound, origin );

		if ( !BackfireEffect.IsValid() || !Exhaust.IsValid() ) return;

		// with a parent the clone's transform is local to it - right on the end of the pipe
		var fx = BackfireEffect.Clone( new CloneConfig { Transform = global::Transform.Zero, Parent = Exhaust, StartEnabled = false } );
		// Particle dimensions and launch velocity are world units, independent of the emitter transform.
		foreach ( var effect in fx.GetComponentsInChildren<ParticleEffect>( true ) )
		{
			effect.OnParticleCreated += particle =>
			{
				var scale = MathF.Max( MathF.Abs( Exhaust.WorldScale.z ), 0.001f );
				particle.StartScale *= scale;
				particle.Size *= scale;
				particle.Radius *= scale;
				particle.Velocity *= scale;
			};
		}
		fx.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		fx.Enabled = true;
	}
}
