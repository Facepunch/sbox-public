
namespace Sandbox;

public static unsafe partial class Sound
{
	internal static void OnVoiceDeleted( int voiceIndex )
	{
	}

	public static SoundHandle Play( string eventName, float fadeInTime = 0.0f )
	{
		if ( Application.IsHeadless )
			return SoundHandle.Empty;

		var e = SoundEvent.Find( eventName );
		if ( e is not null ) return Play( e, fadeInTime );

		Log.Info( $"Couldn't find sound event {eventName}" );
		return default;
	}

	public static SoundHandle Play( SoundEvent soundEvent, float fadeInTime = 0.0f )
	{
		if ( Application.IsHeadless )
			return SoundHandle.Empty;

		if ( soundEvent is null )
			return default;

		var soundFile = soundEvent.GetNextSound();
		if ( !soundFile.IsValid() )
			return null;

		var handle = PlayFile( soundFile, soundEvent.Volume.GetValue(), soundEvent.Pitch.GetValue(), 0.0f, fadeInTime );
		if ( !handle.IsValid() )
		{
			handle?.Dispose();
			return null;
		}

		handle.Distance = soundEvent.Distance;
		handle.Falloff = soundEvent.Falloff;
		handle.OcclusionRadius = soundEvent.OcclusionRadius;
		handle.TargetMixer = soundEvent.DefaultMixer.Get();

		if ( soundEvent.UI )
		{
			handle.ListenLocal = true;
			handle.DistanceAttenuation = false;
			handle.AirAbsorption = false;
			handle.Transmission = false;
			handle.Occlusion = false;
		}
		else
		{
			handle.DistanceAttenuation = soundEvent.DistanceAttenuation;
			handle.AirAbsorption = soundEvent.AirAbsorption;
			handle.Transmission = soundEvent.Transmission;
			handle.Occlusion = soundEvent.Occlusion;
		}

		return handle;
	}

	/// <summary>
	/// Play a sound and set its position
	/// </summary>
	[ActionGraphNode( "sound.playat" ), Title( "Play Sound At" ), Group( "Audio" ), Icon( "volume_up" )]
	public static SoundHandle Play( SoundEvent soundEvent, Vector3 position, float fadeInTime = 0.0f )
	{
		var h = Play( soundEvent, fadeInTime );
		if ( h.IsValid() )
		{
			h.Position = position;
		}
		return h;
	}

	/// <summary>
	/// Play a sound and set its position
	/// </summary>
	public static SoundHandle Play( string eventName, Vector3 position, float fadeInTime = 0.0f )
	{
		var h = Play( eventName, fadeInTime );
		if ( h.IsValid() )
		{
			h.Position = position;
		}
		return h;
	}

	/// <summary>
	/// Play a sound and set its mixer
	/// </summary>
	public static SoundHandle Play( string eventName, Audio.Mixer mixer )
	{
		var h = Play( eventName );
		if ( h.IsValid() )
		{
			h.TargetMixer = mixer;
		}
		return h;
	}

	[ActionGraphNode( "sound.playfile" ), Title( "Play Sound File" ), Group( "Audio" ), Icon( "volume_up" )]
	[Obsolete( "Decibels are obsolete" )]
	public static SoundHandle PlayFile( SoundFile soundFile, float volume, float pitch, float decibels, float delay, float fadeInTime = 0.0f )
	{
		return PlayFile( soundFile.native.self, volume, pitch, delay, fadeInTime, soundFile.ResourceName );
	}

	[ActionGraphNode( "sound.playfile" ), Title( "Play Sound File" ), Group( "Audio" ), Icon( "volume_up" )]
	public static SoundHandle PlayFile( SoundFile soundFile, float volume = 1.0f, float pitch = 1.0f, float delay = 0.0f, float fadeInTime = 0.0f )
	{
		return PlayFile( soundFile.native.self, volume, pitch, delay, fadeInTime, soundFile.ResourceName );
	}

	internal static SoundHandle PlayFile( CSfxTable soundFile, float volume = 1.0f, float pitch = 1.0f, float delay = 0.0f, float fadeInTime = 0.0f, string debugName = "" )
	{
		if ( soundFile.IsNull )
			return default;

		// make sure it's loaded, or loading
		g_pSoundSystem.PreloadSound( soundFile );

		SoundHandle handle = new SoundHandle( soundFile );
		handle.Name = debugName;
		handle.Volume = volume;
		handle.Pitch = pitch;

		if ( fadeInTime > 0.0f )
		{
			handle.TimeUntilFadedIn = fadeInTime;
			handle.IsFadingIn = true;
		}

		if ( handle.sampler is not null )
		{
			handle.sampler.DelayOrSkipSamples( (int)(delay * handle.SampleRate) );
		}

		return handle;
	}

	[ActionGraphNode( "sound.stopall" ), Title( "Stop All Sounds" ), Group( "Audio" ), Icon( "volume_off" )]
	public static void StopAll( float fade )
	{
		SoundHandle.StopAll( fade );

	}
}
