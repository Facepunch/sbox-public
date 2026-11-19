namespace Sandbox.Npcs.Layers;

/// <summary>
/// Manages NPC speech state and plays sound files.
/// </summary>
public class SpeechLayer : BaseNpcLayer
{
	/// <summary>
	/// The subtitle text of the current line, if any. Synced to all clients.
	/// </summary>
	[Sync] public string CurrentSpeech { get; set; }

	/// <summary>
	/// Whether the NPC is currently speaking. Voice lines usually have no subtitle,
	/// so this has to watch the sound itself, not just the subtitle text.
	/// </summary>
	public bool IsSpeaking => CurrentSpeech is not null || (_soundHandle.IsValid() && !_soundHandle.IsStopped);

	/// <summary>
	/// Minimum seconds between speeches.
	/// </summary>
	public float Cooldown { get; set; } = 8f;

	/// <summary>
	/// A generic fallback sound (e.g. a grunt or mumble) played when we're talking without a specific sound.
	/// </summary>
	public SoundEvent FallbackSound { get; set; }

	private SoundHandle _soundHandle;
	[Sync( SyncFlags.FromHost )]
	private TimeSince _lastSpoke { get; set; }
	[Sync( SyncFlags.FromHost )]
	private TimeUntil _speechEnd { get; set; }
	private GameObject _speechTarget;
	private bool _hasSpeechReservation;
	private readonly Dictionary<SoundEvent, List<SoundFile>> _remainingSounds = [];
	private readonly Dictionary<SoundEvent, SoundFile> _lastSounds = [];

	/// <summary>
	/// Whether the cooldown has elapsed and the NPC can speak again.
	/// </summary>
	public bool CanSpeak => _lastSpoke > Cooldown;

	/// <summary>
	/// Play a sound event, with its subtitle (if one exists) as the current speech.
	/// If <paramref name="lookAt"/> is given, the NPC looks them in the eyes
	/// while talking.
	/// </summary>
	public void Say( SoundEvent sound, float duration = 0f, GameObject lookAt = null )
	{
		TrySay( sound, duration, lookAt );
	}

	/// <summary>Attempts to play a tagged sound event.</summary>
	public bool TrySay( SoundEvent sound, float duration = 0f, GameObject lookAt = null,
		TagSet tags = null, int priority = 1000, float radius = 1500f )
	{
		return TrySay( sound, null, duration, lookAt, tags, priority, radius );
	}

	/// <summary>
	/// Play a sound event with an explicit subtitle override.
	/// </summary>
	public void Say( SoundEvent sound, string subtitle, float duration = 0f, GameObject lookAt = null )
	{
		TrySay( sound, subtitle, duration, lookAt );
	}

	/// <summary>Attempts to play a tagged sound event with a subtitle.</summary>
	public bool TrySay( SoundEvent sound, string subtitle, float duration = 0f, GameObject lookAt = null,
		TagSet tags = null, int priority = 1000, float radius = 1500f )
	{
		if ( sound is null || !Npc.IsValid() || Npc.Health < 1f ) return false;

		// Resolve the sound file host-side so every client plays the same one.
		var soundFile = PickSound( sound );
		if ( !soundFile.IsValid() ) return false;
		var speechDuration = GetSoundDuration( soundFile, duration );

		var system = Npc.Scene.GetSystem<NpcSystem>();
		if ( !system.TryBeginSpeech( Npc, speechDuration, tags, priority, radius ) )
			return false;

		StopPlayback( releaseReservation: false );
		_hasSpeechReservation = true;
		_speechTarget = lookAt;
		MarkSoundPlayed( sound, soundFile );

		PlaySound( soundFile, sound.Volume.GetValue(), sound.Pitch.GetValue() );

		if ( !string.IsNullOrEmpty( subtitle ) )
		{
			CurrentSpeech = subtitle;
		}

		_speechEnd = speechDuration;
		_lastSpoke = 0;
		return true;
	}

	// AI runs host-side, so broadcast the sound to every client -- otherwise only the host hears it.
	[Rpc.Broadcast]
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
	/// Say a string message using the fallback sound, with the string as the subtitle.
	/// </summary>
	public void Say( string message, float duration = 3f, GameObject lookAt = null )
	{
		TrySay( message, duration, lookAt );
	}

	/// <summary>Attempts to show a tagged message using the fallback sound.</summary>
	public bool TrySay( string message, float duration = 3f, GameObject lookAt = null,
		TagSet tags = null, int priority = 1000, float radius = 1500f )
	{
		if ( string.IsNullOrEmpty( message ) || !Npc.IsValid() || Npc.Health < 1f ) return false;

		if ( FallbackSound is not null )
		{
			return TrySay( FallbackSound, message, duration, lookAt, tags, priority, radius );
		}

		var system = Npc.Scene.GetSystem<NpcSystem>();
		if ( !system.TryBeginSpeech( Npc, duration, tags, priority, radius ) )
			return false;

		StopPlayback( releaseReservation: false );
		_hasSpeechReservation = true;
		_speechTarget = lookAt;
		CurrentSpeech = message;
		_speechEnd = duration;
		_lastSpoke = 0;
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
		if ( releaseReservation && !IsProxy && _hasSpeechReservation && Npc.IsValid() )
			Npc.Scene.GetSystem<NpcSystem>().StopSpeech( Npc );

		if ( IsProxy )
			StopSoundLocal();
		else
			StopSound();

		CurrentSpeech = null;
		_speechTarget = null;
		_hasSpeechReservation = false;
	}

	[Rpc.Broadcast]
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
		if ( !IsProxy && _hasSpeechReservation && _speechEnd )
		{
			StopPlayback( releaseReservation: false );
		}

		// Look whoever we're talking to in the eyes while we speak. Re-armed each
		// frame so the gaze lingers for a moment after the line ends, then hands
		// back to whatever the NPC was looking at before.
		if ( !IsProxy && IsSpeaking && _speechTarget.IsValid() )
		{
			Npc.Animation?.AddLookTarget( _speechTarget, 1f );
		}
	}

	public override void ResetLayer()
	{
		Stop();
	}
}
