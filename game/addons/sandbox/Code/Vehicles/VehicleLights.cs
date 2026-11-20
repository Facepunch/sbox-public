/// <summary>
/// A vehicle's headlights (the low beams) and its light bar (the high beams), which comes on as well as them.
///
/// The driver taps the flashlight key to switch the headlights on and off, and holds it to switch between low and
/// high beam. While they're driving the key is ours: we cancel the player's own flashlight through
/// <see cref="Global.IPlayerEvents.OnPlayerFlashlight"/>.
/// </summary>
[Title( "Vehicle Lights" ), Category( "Vehicles" ), Icon( "highlight" )]
public sealed class VehicleLights : Component, IPlayerControllable, Global.IPlayerEvents
{
	public enum HeadlightMode
	{
		Off,
		Low,
		High,
	}

	[Property] public FourWheelVehicle Vehicle { get; set; }

	[Property, Group( "Input" )] public ClientInput Switch { get; set; } = new() { Action = "Flashlight" };

	/// <summary>
	/// How long the key has to be held to switch beams, seconds. A shorter press switches the lights on or off.
	/// </summary>
	[Property, Group( "Input" )] public float HoldTime { get; set; } = 0.35f;

	[Property] public Color LightColor { get; set; } = new( 1f, 0.94f, 0.84f );

	/// <summary>
	/// How long the bulbs take to come up to full brightness or go out, seconds.
	/// </summary>
	[Property] public float WarmUpTime { get; set; } = 0.12f;

	[Property, Group( "Low Beam" )] public List<SpotLight> LowBeams { get; set; } = [];
	[Property, Group( "Low Beam" )] public Texture LowBeamCookie { get; set; }
	[Property, Group( "Low Beam" )] public float LowBeamBrightness { get; set; } = 14f;
	[Property, Group( "Low Beam" )] public float LowBeamRange { get; set; } = 1800f;
	[Property, Group( "Low Beam" )] public float LowBeamCone { get; set; } = 62f;

	/// <summary>
	/// The light bar. It comes on as well as the low beams.
	/// </summary>
	[Property, Group( "High Beam" )] public List<SpotLight> HighBeams { get; set; } = [];
	[Property, Group( "High Beam" )] public Texture HighBeamCookie { get; set; }
	[Property, Group( "High Beam" )] public float HighBeamBrightness { get; set; } = 28f;
	[Property, Group( "High Beam" )] public float HighBeamRange { get; set; } = 4000f;
	[Property, Group( "High Beam" )] public float HighBeamCone { get; set; } = 44f;

	[Property, Group( "Sound" )] public SoundEvent SwitchOnSound { get; set; }
	[Property, Group( "Sound" )] public SoundEvent SwitchOffSound { get; set; }

	[Sync( SyncFlags.FromHost )] public HeadlightMode Mode { get; set; }

	private bool _wasDown;
	private bool _holdUsed;
	private TimeSince _pressed;
	private float _lowLevel;
	private float _highLevel;

	/// <summary>
	/// Tap to switch the lights, hold to switch beams. Runs on the host, from the driver's seat.
	/// </summary>
	void IPlayerControllable.OnControl()
	{
		var down = Switch.Down();

		if ( down && !_wasDown )
		{
			_pressed = 0;
			_holdUsed = false;
		}

		if ( down && !_holdUsed && _pressed >= HoldTime )
		{
			_holdUsed = true;
			SetMode( Mode == HeadlightMode.High ? HeadlightMode.Low : HeadlightMode.High );
		}

		if ( !down && _wasDown && !_holdUsed )
		{
			SetMode( Mode == HeadlightMode.Off ? HeadlightMode.Low : HeadlightMode.Off );
		}

		_wasDown = down;
	}

	/// <summary>
	/// The driver's flashlight key works the headlights instead.
	/// </summary>
	void Global.IPlayerEvents.OnPlayerFlashlight( PlayerFlashlightEvent e )
	{
		if ( IsDriver( e.Player ) )
			e.Cancelled = true;
	}

	private bool IsDriver( Player player )
	{
		var seat = Vehicle?.Seat;
		if ( !player.IsValid() || !seat.IsValid() || !seat.IsOccupied ) return false;

		return seat.GetOccupant()?.GetComponent<Player>() == player;
	}

	private void SetMode( HeadlightMode mode )
	{
		if ( mode == Mode ) return;

		Mode = mode;
		PlaySwitchSound( mode != HeadlightMode.Off );
	}

	[Rpc.Broadcast]
	private void PlaySwitchSound( bool on )
	{
		var sound = on ? SwitchOnSound : SwitchOffSound;
		if ( sound.IsValid() )
			Sound.Play( sound, WorldPosition );
	}

	protected override void OnFixedUpdate()
	{
		// let go of the key if the driver got out holding it
		if ( Networking.IsHost && !(Vehicle?.Seat?.IsOccupied ?? false) )
			_wasDown = false;
	}

	protected override void OnUpdate()
	{
		var rate = Time.Delta / MathF.Max( WarmUpTime, 0.001f );
		_lowLevel = _lowLevel.Approach( Mode != HeadlightMode.Off ? 1 : 0, rate );
		_highLevel = _highLevel.Approach( Mode == HeadlightMode.High ? 1 : 0, rate );

		UpdateLights( LowBeams, _lowLevel, LowBeamCookie, LowBeamBrightness, LowBeamRange, LowBeamCone );
		UpdateLights( HighBeams, _highLevel, HighBeamCookie, HighBeamBrightness, HighBeamRange, HighBeamCone );
	}

	private void UpdateLights( List<SpotLight> lights, float level, Texture cookie, float brightness, float range, float cone )
	{
		foreach ( var light in lights )
		{
			if ( !light.IsValid() ) continue;

			light.Enabled = level > 0;
			light.Cookie = cookie;
			// Light ranges are world units; the parent transform only scales their positions.
			var scale = MathF.Max( MathF.Abs( Vehicle.IsValid() ? Vehicle.WorldScale.z : WorldScale.z ), 0.001f );
			light.Radius = range * scale;
			light.ConeOuter = cone;
			light.ConeInner = cone * 0.8f;
			light.LightColor = (LightColor * (brightness * level)).WithAlpha( 1 );
		}
	}
}
