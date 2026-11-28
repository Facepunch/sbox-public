namespace Sandbox.Npcs.Layers;

/// <summary>
/// Manages NPC speech state and plays sound files.
/// </summary>
public class SpeechLayer : BaseNpcLayer
{
	/// <summary>
	/// The subtitle text of the current line, if any. Synced to all clients.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public string CurrentSpeech { get; private set; }

	/// <summary>
	/// Whether the NPC is currently speaking. Voice lines usually have no subtitle,
	/// so this has to watch the sound itself, not just the subtitle text.
	/// </summary>
	internal bool IsSpeaking => CurrentSpeech is not null || (_soundHandle.IsValid() && !_soundHandle.IsStopped);

	/// <summary>
	/// Minimum seconds between speeches.
	/// </summary>
	public float Cooldown { get; set; } = 8f;

	/// <summary>
	/// A generic fallback sound (e.g. a grunt or mumble) played when we're talking without a specific sound.
	/// </summary>
	internal SoundEvent FallbackSound { get; set; }

	private SoundHandle _soundHandle;
	private TimeSince _lastSpoke;
	private TimeUntil _speechEnd;
	private GameObject _speechTarget;
	private bool _hasSpeechReservation;
	private readonly Dictionary<SoundEvent, List<SoundFile>> _remainingSounds = [];
	private readonly Dictionary<SoundEvent, SoundFile> _lastSounds = [];

	/// <summary>
	/// Whether the cooldown has elapsed and the NPC can speak again.
	/// </summary>
	public bool CanSpeak => _lastSpoke > Cooldown;

	/// <summary>
	/// Attempts to say a line by playing a sound event. False when the NPC cannot speak or the speech around it
	/// does not let the line through. Without <paramref name="options"/> it gets the defaults of
	/// <see cref="SpeechOptions"/>.
	/// </summary>
	public bool TrySay( SoundEvent sound, SpeechOptions? options = null )
	{
		var o = options ?? default;
		if ( sound is null || !Npc.IsValid() || Npc.Health < 1f ) return false;

		// Resolve the sound file host-side so every client plays the same one.
		var soundFile = PickSound( sound );
		if ( !soundFile.IsValid() ) return false;
		var speechDuration = GetSoundDuration( soundFile, o.Duration ?? 0f );

		if ( !BeginLine( o, speechDuration ) )
			return false;

		MarkSoundPlayed( sound, soundFile );

		PlaySound( soundFile, sound.Volume.GetValue(), sound.Pitch.GetValue() );

		if ( !string.IsNullOrEmpty( o.Subtitle ) )
		{
			CurrentSpeech = o.Subtitle;
		}

		return true;
	}

	// The speech around the NPC lets the line through: it takes over from whatever it was saying
	private bool BeginLine( in SpeechOptions o, float duration )
	{
		if ( !Npc.Scene.GetSystem<NpcSystem>().TryBeginSpeech( Npc, duration, o.Tags, o.Priority, o.Radius ) )
			return false;

		StopPlayback( releaseReservation: false );
		_hasSpeechReservation = true;
		_speechTarget = o.LookAt;
		_speechEnd = duration;
		_lastSpoke = 0;
		return true;
	}

	// AI runs host-side, so broadcast the sound to every client -- otherwise only the host hears it.
	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void PlaySound( SoundFile soundFile, float volume, float pitch )
	{
		if ( !soundFile.IsValid() ) return;
		if ( _soundHandle.IsValid() )
			_soundHandle.Stop();

		// Speak through the renderer so the NPC lipsyncs to the sound
		if ( Npc.IsValid() && Npc.Renderer.IsValid() )
		{
			_soundHandle = Npc.Renderer.SpeakSound( soundFile, volume, pitch, Npc.GameObject );
			return;
		}

		_soundHandle = Sound.PlayFile( soundFile, volume, pitch );

		if ( _soundHandle.IsValid() )
		{
			_soundHandle.Parent = Npc.GameObject;
		}
	}

	/// <summary>
	/// Attempts to say a line of text, shown as its own subtitle. False when the NPC cannot speak or the speech
	/// around it does not let the line through. Without <paramref name="options"/> it gets the defaults of
	/// <see cref="SpeechOptions"/>.
	/// </summary>
	public bool TrySay( string message, SpeechOptions? options = null )
	{
		var o = options ?? default;
		if ( string.IsNullOrEmpty( message ) || !Npc.IsValid() || Npc.Health < 1f ) return false;

		var duration = o.Duration ?? 3f;

		if ( FallbackSound is not null )
		{
			return TrySay( FallbackSound, o with { Subtitle = message, Duration = duration } );
		}

		if ( !BeginLine( o, duration ) )
			return false;

		CurrentSpeech = message;
		return true;
	}

	private SoundFile PickSound( SoundEvent sound )
	{
		if ( !_remainingSounds.TryGetValue( sound, out var remaining ) || remaining.Count == 0 )
		{
			remaining = sound.Sounds.Where( x => x.IsValid() ).Distinct().ToList();

			if ( remaining.Count > 1 && _lastSounds.TryGetValue( sound, out var last ) )
				remaining.Remove( last );

			_remainingSounds[sound] = remaining;
		}

		if ( remaining.Count == 0 )
			return null;

		return remaining[Game.Random.Int( 0, remaining.Count - 1 )];
	}

	private static float GetSoundDuration( SoundFile soundFile, float fallback )
	{
		if ( soundFile.IsLoaded )
			return soundFile.Duration;

		return fallback > 0f ? fallback : 3f;
	}

	private void MarkSoundPlayed( SoundEvent sound, SoundFile soundFile )
	{
		_remainingSounds[sound].Remove( soundFile );
		_lastSounds[sound] = soundFile;
	}

	/// <summary>
	/// Stop any current speech and sound.
	/// </summary>
	public void Stop()
	{
		StopPlayback();
	}

	private void StopPlayback( bool releaseReservation = true )
	{
		if ( releaseReservation && Networking.IsHost && _hasSpeechReservation && Npc.IsValid() )
			Npc.Scene.GetSystem<NpcSystem>().StopSpeech( Npc );

		if ( !Networking.IsHost )
			StopSoundLocal();
		else
			StopSound();

		CurrentSpeech = null;
		_speechTarget = null;
		_hasSpeechReservation = false;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void StopSound()
	{
		StopSoundLocal();
	}

	private void StopSoundLocal()
	{
		if ( _soundHandle.IsValid() )
		{
			_soundHandle.Stop();
		}
	}

	protected override void OnUpdate()
	{
		// The Npc can be gone while we're being torn down (death destroys the hierarchy).
		if ( !Npc.IsValid() )
			return;

		// The selected sound length is authoritative, including on dedicated servers.
		if ( Networking.IsHost && _hasSpeechReservation && _speechEnd )
		{
			StopPlayback( releaseReservation: false );
		}

		// Look whoever we're talking to in the eyes while we speak. Re-armed each
		// frame so the gaze lingers for a moment after the line ends, then hands
		// back to whatever the NPC was looking at before.
		if ( Networking.IsHost && IsSpeaking && _speechTarget.IsValid() )
		{
			Npc.Animation?.AddLookTarget( _speechTarget, 1f );
		}
	}
}
