namespace Sandbox;

public partial class SkinnedModelRenderer
{
	/// <summary>
	/// Drive viseme morphs from speaking voices. Turn off to check whether facial
	/// movement is coming from lipsync or something else.
	/// </summary>
	[ConVar( "snd_voice_lipsync" )]
	internal static bool VoiceLipSyncEnabled { get; set; } = true;

	SoundHandle _voice;
	SoundFile _voiceSoundFile;

	/// <summary>
	/// The sound this model is currently speaking, set by
	/// <see cref="SpeakSound(SoundEvent, GameObject)"/>. While this is playing we
	/// drive the model's viseme morphs from it, so the mouth moves along with the words.
	/// </summary>
	internal SoundHandle Voice
	{
		get => _voice;
		set
		{
			if ( _voice == value )
				return;

			_voice?.Stop();
			_voice?.Dispose();
			_voice = default;

			_voice = value;
			_voiceSoundFile = null;
		}
	}

	/// <summary>
	/// Play a sound as this model's voice. On top of regular sound playback, the
	/// model will lipsync to it - if the sound has a viseme track (authored in the
	/// sound editor) we play that back, otherwise the audio is analyzed as it
	/// plays. Either way the mouth moves, assuming the model has viseme morphs.
	/// </summary>
	/// <param name="sound">The sound to speak.</param>
	/// <param name="mouth">Optional object to play the sound from, like a mouth bone. Defaults to our GameObject.</param>
	public SoundHandle SpeakSound( SoundEvent sound, GameObject mouth = null )
	{
		var handle = Sound.Play( sound, out var soundFile );

		if ( handle.IsValid() )
		{
			var host = mouth ?? GameObject;
			handle.Position = host.WorldPosition;
			handle.Parent = host;
			handle.FollowParent = true;
		}

		Voice = handle;
		_voiceSoundFile = soundFile;

		return Voice;
	}

	/// <summary>
	/// Play a specific sound file as this model's voice. Useful when you've picked
	/// the file yourself - to keep it in sync across the network, or because you
	/// generated it. See <see cref="SpeakSound(SoundEvent, GameObject)"/>.
	/// </summary>
	/// <param name="sound">The sound file to speak.</param>
	/// <param name="volume">Playback volume.</param>
	/// <param name="pitch">Playback pitch.</param>
	/// <param name="mouth">Optional object to play the sound from, like a mouth bone. Defaults to our GameObject.</param>
	public SoundHandle SpeakSound( SoundFile sound, float volume = 1.0f, float pitch = 1.0f, GameObject mouth = null )
	{
		if ( !sound.IsValid() )
			return default;

#pragma warning disable CA2000 // Dispose objects before losing scope
		var handle = Sound.PlayFile( sound, volume, pitch );
#pragma warning restore CA2000
		if ( !handle.IsValid() )
			return default;

		var host = mouth ?? GameObject;
		handle.Position = host.WorldPosition;
		handle.Parent = host;
		handle.FollowParent = true;

		Voice = handle;
		_voiceSoundFile = sound;

		return Voice;
	}

	// How fast morphs chase their target weight. The LipSync component pipes its
	// smoothing knob through here.
	internal float VoiceMorphSmoothTime = 0.1f;

	// Realtime analysis produces weaker weights than authored tracks, scale them
	// up. The LipSync component pipes its scale knob through here.
	internal float VoiceRealtimeScale = 1.5f;

	// Realtime analysis is noisy - ignore viseme weights below this, so silence
	// and the noise floor don't twitch the face
	const float VoiceRealtimeDeadzone = 0.15f;

	// The model whose viseme morphs we're driving, so we can let go of them
	// when the voice stops or the model changes. The morph weights themselves
	// live on Model.Morphs, shared by everything that lipsyncs.
	Model _voiceMorphModel;

	/// <summary>
	/// True if there's a voice to lipsync, or morphs we're still driving that
	/// need letting go of. The animation system skips us entirely otherwise.
	/// </summary>
	internal bool VoiceActive => _voice is not null || _voiceMorphModel is not null;

	/// <summary>
	/// Drive viseme morphs from the current <see cref="Voice"/>. Called on the main
	/// thread each frame before animation updates, so the weights we set here get
	/// composited in the same frame's bone/morph update.
	/// </summary>
	internal void UpdateVoice()
	{
		// Voice stopped - ease the mouth back to the animation and let go
		if ( !_voice.IsValid() || _voice.Finished )
		{
			Voice = null;

			if ( _voiceMorphModel is not null )
			{
				var morphModel = SceneModel;
				if ( morphModel.IsValid() )
					ClearVoiceMorphs( morphModel );
				else
					_voiceMorphModel = null;
			}

			return;
		}

		var sceneModel = SceneModel;
		if ( !sceneModel.IsValid() )
			return;

		// Lipsync turned off - let go of the face but keep the voice, and stop
		// paying for realtime analysis on the mix thread
		if ( !VoiceLipSyncEnabled )
		{
			Voice.LipSync.Enabled = false;
			ClearVoiceMorphs( sceneModel );
			return;
		}

		var model = Model;
		if ( model is null || model.MorphCount == 0 )
			return;

		Span<float> weights = stackalloc float[Visemes.Count];
		var scale = 1.0f;

		var soundFile = _voiceSoundFile;
		var track = soundFile?.Visemes;

		if ( track is not null )
		{
			// The sound has an authored viseme track, sample it at the playback position
			track.Sample( Voice.Time, weights );
		}
		else if ( soundFile is null || soundFile.IsValidForPlayback )
		{
			// The sound is loaded and has no viseme track, analyze the audio as it plays
			Voice.LipSync.Enabled = true;

			var visemes = Voice.LipSync.VisemeWeights;
			for ( var i = 0; i < weights.Length && i < visemes.Length; i++ )
			{
				// Gate out analysis noise, so silence doesn't twitch the face
				var weight = visemes[i];
				weights[i] = weight > VoiceRealtimeDeadzone ? weight : 0.0f;
			}

			scale = VoiceRealtimeScale;
		}
		else
		{
			// The sound is still loading, so we don't yet know if it has a track -
			// and it isn't audible yet either. Hold off rather than kicking off
			// realtime analysis we'd abandon.
			return;
		}

		ApplyVoiceMorphs( sceneModel, weights, scale );
	}

	void ApplyVoiceMorphs( SceneModel sceneModel, ReadOnlySpan<float> visemeWeights, float scale )
	{
		_voiceMorphModel = Model;

		sceneModel.Morphs.ApplyVisemes( visemeWeights, scale, VoiceMorphSmoothTime );
	}

	// Fade the morphs we've been driving back to their animated values
	void ClearVoiceMorphs( SceneModel sceneModel )
	{
		if ( _voiceMorphModel is null )
			return;

		foreach ( var morphIndex in _voiceMorphModel.Morphs.VisemeMorphIndices )
		{
			sceneModel.Morphs.Reset( morphIndex, VoiceMorphSmoothTime );
		}

		_voiceMorphModel = null;
	}

	/// <summary>
	/// Let go of any voice morphs instantly. Called when the model is about to
	/// change - our morph indices are in the old model's morph space, and the
	/// scene object keeps its override slots across the swap, so anything we
	/// leave behind (even a fade) would drive random morphs on the new model.
	/// </summary>
	internal void ReleaseVoiceMorphs()
	{
		if ( _voiceMorphModel is null || _voiceMorphModel == Model )
			return;

		var sceneModel = SceneModel;
		if ( sceneModel.IsValid() )
		{
			foreach ( var morphIndex in _voiceMorphModel.Morphs.VisemeMorphIndices )
			{
				sceneModel.Morphs.Reset( morphIndex );
			}
		}

		_voiceMorphModel = null;
	}
}
