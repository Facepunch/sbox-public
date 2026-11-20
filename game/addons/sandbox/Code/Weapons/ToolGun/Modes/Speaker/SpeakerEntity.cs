using Sandbox.UI;

/// <summary>
/// How a <see cref="SpeakerEntity"/> reacts to its key or trigger signal.
/// </summary>
public enum SpeakerMode
{
	/// <summary>
	/// Each press plays the sound once, all the way through.
	/// </summary>
	[Title( "#entity.speakermode.oneshot" )]
	OneShot,

	/// <summary>
	/// The sound plays while the input is held (repeating if it ends, with <see cref="SpeakerEntity.Looping"/>), and
	/// stops when it's let go.
	/// </summary>
	[Title( "#entity.speakermode.hold" )]
	Hold,

	/// <summary>
	/// Press once to start the sound, press again to stop it. Repeats while on with <see cref="SpeakerEntity.Looping"/>.
	/// </summary>
	[Title( "#entity.speakermode.toggle" )]
	Toggle,
}

/// <summary>
/// Plays a sound when told to: by a key from a linked seat, or by a wire - a button, a seat's Entered/Exited/Occupied
/// (see <see cref="SeatSignals"/>), logic, anything with an output.
///
/// The host decides input state: <see cref="IsOn"/> for held and toggled sounds, broadcasts for one-shots.
/// Each client manages its audible voices and one-shot queue using the policy sent with each trigger.
/// </summary>
[Alias( "speaker" ), Title( "#entity.name.speaker" ), Icon( "volume_up" )]
public sealed class SpeakerEntity : Component, IPlayerControllable, Global.IControlEvents, IInspectHandle, ISignalDisplayName
{
	/// <summary>
	/// "Speaker: horn" in the wire tool, so several speakers on one contraption can be told apart.
	/// </summary>
	string ISignalDisplayName.SignalDisplayName
	{
		get
		{
			var title = Game.Language.GetPhrase( "entity.name.speaker" );
			var sound = SoundResolver.GetDisplayName( Sound );
			return sound is null ? title : $"{title}: {sound}";
		}
	}

	/// <summary>
	/// What a new speaker plays.
	/// </summary>
	public const string DefaultSound = "sounds/vehicles/buggy/horn.sound";

	/// <summary>
	/// The sound this speaker plays: any .sound in the game, or a workshop sound's package ident. Empty is silent.
	/// See <see cref="SoundResolver"/>.
	/// </summary>
	[Property, ClientEditable, ResourceSelect( Extension = "sound", AllowPackages = true, Popup = true ), Title( "#entity.property.speaker_sound" ), Description( "#entity.description.speaker_sound" ), Group( "#tool.setting.group.sound" ), Order( 0 )]
	public string Sound { get; set; } = DefaultSound;

	/// <summary>
	/// Hold and toggle: start the sound again whenever it ends, for as long as it's on. Off plays it once per
	/// press. Sounds that loop by themselves loop either way.
	/// </summary>
	[Property, ClientEditable, Title( "#entity.property.looping" ), Description( "#entity.description.speaker_looping" ), Group( "#tool.setting.group.sound" ), Order( 0 )]
	public bool Looping { get; set; } = false;

	/// <summary>
	/// One-shot, hold or toggle.
	/// </summary>
	[Property, ClientEditable, Title( "#entity.property.mode" ), Description( "#entity.description.speaker_mode" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public SpeakerMode Mode { get; set; } = SpeakerMode.OneShot;

	[Property, ClientEditable, ShowIf( nameof( Mode ), SpeakerMode.OneShot ), Title( "#entity.property.speaker_retrigger" ), Description( "#entity.description.speaker_retrigger" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public SpeakerRetrigger Retrigger { get; set; } = SpeakerRetrigger.Restart;

	public bool ShowOverlapLimit => Mode == SpeakerMode.OneShot && Retrigger == SpeakerRetrigger.Overlap;

	[Property, ClientEditable, ShowIf( nameof( ShowOverlapLimit ), true ), Range( 1, 16 ), Title( "#entity.property.speaker_max_voices" ), Description( "#entity.description.speaker_max_voices" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public int MaxVoices { get; set; } = 8;

	/// <summary>
	/// 0 to 1. Can be wired, e.g. to a throttle.
	/// </summary>
	[Property, Sync, ClientEditable, SignalInput, Range( 0, 1 ), Title( "#entity.property.volume" ), Description( "#entity.description.speaker_volume" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float Volume { get; set; } = 1f;

	/// <summary>
	/// Playback speed, 1 is normal. Can be wired, e.g. to make an engine whine rise with speed.
	/// </summary>
	[Property, Sync, ClientEditable, SignalInput, Range( 0.25f, 2f ), Title( "#entity.property.pitch" ), Description( "#entity.description.speaker_pitch" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float Pitch { get; set; } = 1f;

	[Property, Sync, ClientEditable, Range( SpeakerAttenuation.MinRange, SpeakerAttenuation.MaxRange ), Title( "#entity.property.speaker_range" ), Description( "#entity.description.speaker_range" ), Group( "#tool.setting.group.sound" ), Order( 1 )]
	public float SoundRange { get; set; } = SpeakerAttenuation.DefaultRange;

	[Property, Sync, ClientEditable, Range( SpeakerAttenuation.MinFalloff, SpeakerAttenuation.MaxFalloff ), Title( "#entity.property.speaker_falloff" ), Description( "#entity.description.speaker_falloff" ), Group( "#tool.setting.group.sound" ), Order( 1 )]
	public float Falloff { get; set; } = SpeakerAttenuation.DefaultFalloff;

	/// <summary>
	/// The key that plays the sound for whoever sits in a seat on this contraption.
	/// </summary>
	[Property, ClientEditable, Title( "#entity.property.speaker_key" ), Description( "#entity.description.speaker_key" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Key { get; set; }

	/// <summary>
	/// Whether the held/toggled sound is playing. Owned by the host.
	/// </summary>
	[Sync] public bool IsOn { get; private set; }

	/// <summary>
	/// How long held and toggled sounds take to fade out when they stop.
	/// </summary>
	private const float FadeTime = 0.1f;

	// host state - the key and the wires are kept apart so letting go of one doesn't cancel the other
	private bool _keyOn;
	private bool _wireOn;
	private bool _wireDown;
	private bool _keyDown;
	private bool _keyStopped;
	private bool _wireStopped;

	// client state
	private SoundHandle _loop;
	private SoundEvent _loopSound;
	private bool _playedThisTime;
	private readonly List<SoundHandle> _oneShots = new();
	private readonly Queue<SoundEvent> _queuedSounds = new();
	private const int MaxQueuedSounds = 32;
	private readonly SpeakerAttenuation _attenuation = new();

	protected override void OnEnabled()
	{
		InspectHandles.Register( this );
	}

	protected override void OnDisabled()
	{
		InspectHandles.Unregister( this );

		_keyOn = _wireOn = _wireDown = false;
		_keyDown = _keyStopped = _wireStopped = false;
		if ( !IsProxy ) IsOn = false;

		StopLoop( 0f );
		StopOneShots( 0f );
	}

	protected override void OnDestroy()
	{
		InspectHandles.Unregister( this );
		StopLoop( 0f );
		StopOneShots( 0f );
	}

	/// <summary>
	/// No model to click on, so the inspect menu shows an icon for it instead.
	/// </summary>
	bool IInspectHandle.ShowInspectHandle => !GetComponentInChildren<ModelRenderer>( true ).IsValid();

	#region Wire inputs

	/// <summary>
	/// Does whatever the key would: plays once, plays while held, or toggles, depending on the mode.
	/// Wire a button or a seat's Entered here and it behaves like the key.
	/// </summary>
	[SignalInput( Default = true, Name = "#entity.signal.speaker_trigger" )]
	public void Trigger( bool down )
	{
		var pressed = down && !_wireDown;
		_wireDown = down;
		if ( !down ) _wireStopped = false;
		if ( _wireStopped ) return;

		switch ( Mode )
		{
			case SpeakerMode.OneShot:
				if ( pressed ) PlayOnce( Sound, Retrigger, MaxVoices );
				break;
			case SpeakerMode.Toggle:
				if ( pressed ) SetWireOn( !_wireOn );
				break;
			case SpeakerMode.Hold:
				SetWireOn( down );
				break;
		}
	}

	/// <summary>
	/// Stops all playback and clears held/toggled state. Inputs already held must
	/// be released before they can start playback again.
	/// </summary>
	[SignalInput( Name = "#entity.signal.speaker_stop" )]
	public void StopSound()
	{
		if ( IsProxy ) return;

		_keyStopped = _keyDown;
		_wireStopped = _wireDown;
		_keyOn = false;
		_wireOn = false;
		UpdateOn();
		StopAll();
	}

	#endregion

	#region Seat key

	/// <summary>
	/// The seated player's key. Runs on the host every fixed update while someone sits in a linked seat.
	/// </summary>
	public void OnControl()
	{
		_keyDown = Key.Down();
		if ( !_keyDown ) _keyStopped = false;
		if ( _keyStopped ) return;

		switch ( Mode )
		{
			case SpeakerMode.OneShot:
				if ( Key.Pressed() ) PlayOnce( Sound, Retrigger, MaxVoices );
				break;
			case SpeakerMode.Toggle:
				if ( Key.Pressed() ) SetKeyOn( !_keyOn );
				break;
			case SpeakerMode.Hold:
				SetKeyOn( _keyDown );
				break;
		}
	}

	void Global.IControlEvents.OnControlStopped( ControlEvent e )
	{
		if ( !IsPartOf( e.Target ) ) return;
		_keyDown = _keyStopped = false;

		// OnControl stops being called once they're out, so nothing would ever let go of a held key
		if ( Mode != SpeakerMode.Hold || !_keyOn ) return;

		SetKeyOn( false );
	}

	/// <summary>
	/// Is this seat (or driven prop) on the same contraption as us?
	/// </summary>
	private bool IsPartOf( GameObject target )
	{
		if ( !Networking.IsHost || !target.IsValid() ) return false;

		var linked = new LinkedGameObjectBuilder();
		linked.AddConnected( GameObject );
		return linked.Objects.Contains( target.Root );
	}

	#endregion

	#region Host state

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;

		// switched to one-shot while something was held or toggled on
		if ( Mode == SpeakerMode.OneShot && (_keyOn || _wireOn) )
		{
			_keyOn = _wireOn = false;
			UpdateOn();
		}
	}

	private void SetKeyOn( bool on )
	{
		if ( _keyOn == on ) return;
		_keyOn = on;
		UpdateOn();
	}

	private void SetWireOn( bool on )
	{
		if ( _wireOn == on ) return;
		_wireOn = on;
		UpdateOn();
	}

	private void UpdateOn()
	{
		if ( IsProxy ) return;
		IsOn = _keyOn || _wireOn;
	}

	#endregion

	#region Playback

	/// <summary>
	/// Request a one-shot for everyone. Capture the sound and policy on the host so
	/// property replication timing cannot change which settings a trigger uses.
	/// </summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void PlayOnce( string soundPath, SpeakerRetrigger retrigger, int maxVoices )
	{
		if ( !Active ) return;
		RemoveFinishedOneShots();

		// a workshop sound that hasn't finished downloading yet is skipped - OnUpdate preloads it, so this is rare
		var sound = SoundResolver.Get( soundPath );
		if ( sound is null ) return;

		switch ( retrigger )
		{
			case SpeakerRetrigger.IgnoreWhilePlaying:
				if ( _oneShots.Count > 0 || _queuedSounds.Count > 0 ) return;
				break;
			case SpeakerRetrigger.Overlap:
				// Switching away from Queue must not leave delayed surprises behind.
				_queuedSounds.Clear();
				TrimOneShots( maxVoices.Clamp( 1, 16 ) - 1 );
				break;
			case SpeakerRetrigger.Queue:
				if ( _oneShots.Count > 0 || _queuedSounds.Count > 0 )
				{
					if ( _queuedSounds.Count < MaxQueuedSounds ) _queuedSounds.Enqueue( sound );
					return;
				}
				break;
			default:
				StopOneShots( 0f );
				break;
		}

		StartOneShot( sound );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void StopAll()
	{
		StopOneShots( 0f );
		StopLoop( 0f );
		_playedThisTime = false;
	}

	private SoundHandle PlayAttached( SoundEvent sound )
	{
		var handle = Sandbox.Sound.Play( sound, WorldPosition );
		if ( handle.IsValid() )
		{
			handle.Parent = GameObject;
			handle.FollowParent = true;
			// Speakers are world sources, even when the chosen event was authored
			// for UI/music mixers which bypass distance attenuation.
			handle.ListenLocal = false;
			handle.SpacialBlend = 1f;
			handle.TargetMixer = Sandbox.Audio.Mixer.FindMixerByName( "Game" );
			ApplyPlaybackSettings( handle );
		}

		return handle;
	}

	private void ApplyPlaybackSettings( SoundHandle handle )
	{
		handle.Volume = Volume;
		handle.Pitch = Pitch;
		handle.DistanceAttenuation = true;
		handle.Distance = SpeakerAttenuation.ClampRange( SoundRange );
		handle.Falloff = _attenuation.GetCurve( Falloff );
	}

	protected override void OnUpdate()
	{
		// also starts downloading a workshop pick, so it's ready before it's needed
		var sound = SoundResolver.Get( Sound );

		if ( IsOn && sound is not null )
		{
			// picked a different sound while it was playing - start the new one
			if ( _loopSound != sound )
			{
				StopLoop( 0f );
				_playedThisTime = false;
			}

			// starts once each time it's turned on; when looping, again every time it ends
			var ended = !_loop.IsValid() || _loop.IsStopped;
			if ( ended && (!_playedThisTime || Looping) )
			{
				_loop = PlayAttached( sound );
				_loopSound = sound;
				_playedThisTime = true;
			}
		}
		else
		{
			StopLoop( FadeTime );
			_playedThisTime = false;
		}

		if ( _loop.IsValid() )
		{
			ApplyPlaybackSettings( _loop );
		}

		if ( Mode != SpeakerMode.OneShot )
		{
			StopOneShots( 0f );
			return;
		}

		RemoveFinishedOneShots();
		if ( Retrigger == SpeakerRetrigger.Overlap ) TrimOneShots( MaxVoices.Clamp( 1, 16 ) );
		if ( _oneShots.Count == 0 && _queuedSounds.Count > 0 )
			StartOneShot( _queuedSounds.Dequeue() );

		foreach ( var handle in _oneShots ) ApplyPlaybackSettings( handle );
	}

	private void StopLoop( float fade )
	{
		if ( _loop.IsValid() )
			_loop.Stop( fade );

		_loop = null;
		_loopSound = null;
	}

	private void StartOneShot( SoundEvent sound )
	{
		var handle = PlayAttached( sound );
		if ( handle.IsValid() && !handle.IsStopped ) _oneShots.Add( handle );
	}

	private void RemoveFinishedOneShots()
	{
		_oneShots.RemoveAll( handle => !handle.IsValid() || handle.IsStopped );
	}

	private void TrimOneShots( int limit )
	{
		while ( _oneShots.Count > limit )
		{
			_oneShots[0]?.Stop( 0f );
			_oneShots.RemoveAt( 0 );
		}
	}

	private void StopOneShots( float fade )
	{
		foreach ( var handle in _oneShots )
		{
			if ( handle.IsValid() ) handle.Stop( fade );
		}
		_oneShots.Clear();
		_queuedSounds.Clear();
	}

	#endregion
}
