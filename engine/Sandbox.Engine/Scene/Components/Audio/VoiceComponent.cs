using Sandbox.Audio;
using Sandbox.Platform;

namespace Sandbox;

/// <summary>
/// Configures how voice chat is recorded and played back, and lets you lip-sync a <see cref="SkinnedModelRenderer"/>.
/// </summary>
[Expose]
[Category( "Audio" )]
[Title( "Voice Transmitter" )]
[Icon( "mic" )]
[Tint( EditorTint.Green )]
public class Voice : Component
{
	[Expose]
	public enum ActivateMode
	{
		[Icon( "hearing" )]
		[Description( "Always recording and transmitting voice" )]
		AlwaysOn,

		[Icon( "touch_app" )]
		[Description( "Hold a button down to talk" )]
		PushToTalk,

		[Icon( "science" )]
		[Description( "Toggle recording by switching IsListening to true or false" )]
		Manual
	}

	[Property] public float Volume { get; set; } = 1.0f;
	[Property] public ActivateMode Mode { get; set; }
	[Property, InputAction, ShowIf( nameof( Mode ), ActivateMode.PushToTalk )] public string PushToTalkInput { get; set; } = "voice";
	[Property] public bool WorldspacePlayback { get; set; } = true;

	[Description( "Play the sound of your own voice" )]
	[Property] public bool Loopback { get; set; } = false;

	/// <summary>
	/// Analyze the voice for viseme weights while it plays. Analysis only runs while a <see cref="Renderer"/>
	/// is set or <see cref="Visemes"/> / <see cref="LaughterScore"/> have been read recently.
	/// </summary>
	[Property, ToggleGroup( "LipSync", Label = "Lip Sync" )]
	public bool LipSync { get; set; } = true;

	/// <summary>
	/// Optional renderer whose viseme morphs are driven by the voice.
	/// </summary>
	[Property, Group( "LipSync" )]
	public SkinnedModelRenderer Renderer { get; set; }

	[Property, Group( "LipSync" ), Range( 0, 5 )]
	public float MorphScale { get; set; } = 3.0f;

	[Property, Group( "LipSync" ), Range( 0, 1 )]
	public float MorphSmoothTime { get; set; } = 0.1f;

	/// <summary>
	/// How long has it been since this sound played?
	/// </summary>
	public RealTimeSince LastPlayed { get; private set; }

	private RealTimeSince lipSyncRequested = float.MaxValue;

	/// <summary>
	/// Which mixer to target. Must be a descendant of the Voice mixer. Defaults to the Voice mixer if not set or invalid.
	/// </summary>
	[Property, ParentMixer( "Voice" )]
	public MixerHandle VoiceMixer { get; set; }

	public Mixer TargetMixer
	{
		get => VoiceMixer.Get( Mixer.Voice );
		set => VoiceMixer = value;
	}

	/// <inheritdoc cref="SoundHandle.Distance"/>
	[Property, AudioDistanceFloat]
	public float Distance { get; set; }

	/// <inheritdoc cref="SoundHandle.Falloff"/>
	[Property]
	public Curve Falloff { get; set; }

	/// <summary>
	/// Laughter score for the current audio frame, between 0 and 1
	/// </summary>
	public float LaughterScore
	{
		get
		{
			lipSyncRequested = 0;
			return SoundHandle.IsValid() ? SoundHandle.LipSync.LaughterScore : 0f;
		}
	}

	/// <summary>
	/// A list of 15 lipsync viseme weights. Requires <see cref="LipSync"/> to be enabled, doesn't need a <see cref="Renderer"/>.
	/// Reading this keeps analysis running, so the first read after a pause may be empty.
	/// </summary>
	public IReadOnlyList<float> Visemes
	{
		get
		{
			lipSyncRequested = 0;
			return SoundHandle.IsValid() ? SoundHandle.LipSync.Visemes : [];
		}
	}

	/// <summary>
	/// Measure of audio loudness.
	/// </summary>
	public float Amplitude => SoundHandle.IsValid() ? SoundHandle.Amplitude : 0f;

	/// <summary>
	/// True while the local player is talking through this.
	/// </summary>
	public bool IsRecording => Scene?.GetSystem<VoiceChatSystem>() is { IsListening: true } system && system.LocalVoice == this;

	/// <summary>
	/// Returns true if we want to listen to the mic.
	/// </summary>
	public bool IsListening
	{
		set;
		get
		{
			if ( IsProxy ) return false;
			if ( !Preferences.VoiceEnabled ) return false;

			// The player's Voice Mode wins: open microphone doesn't need a key, push to talk always does

			return Preferences.VoiceMode switch
			{
				VoiceMode.Disabled => false,
				VoiceMode.OpenMicrophone => Mode != ActivateMode.Manual || field,
				_ => Input.Down( PushToTalkInput )
			};
		}
	}

	public Voice()
	{
		Distance = VoiceChat.DefaultDistance;
		Falloff = VoiceChat.DefaultFalloff;
	}

	internal override void OnEnabledInternal()
	{
		// Ensure that the Mixer selected is a Voice mixer
		var currentMixer = VoiceMixer.GetOrDefault();
		if ( !currentMixer.IsDescendantOf( Mixer.Voice ) )
		{
			VoiceMixer = Mixer.Voice;
		}

		base.OnEnabledInternal();
	}

	internal override void OnDisabledInternal()
	{
		RemoveSpeaker();

		base.OnDisabledInternal();
	}

	internal override void OnDestroyInternal()
	{
		RemoveSpeaker();

		base.OnDestroyInternal();
	}

	protected sealed override void OnUpdate()
	{
		if ( Speaker is { IsIdle: true } )
		{
			RemoveSpeaker();
		}

		UpdateMorphs();
		UpdateSound();
	}

	internal Connection.Filter GetBroadcastFilter()
	{
		return new Connection.Filter( Connection.Filter.FilterType.Exclude, ExcludeFilter() );
	}

	/// <summary>
	/// Exclude these connection from hearing our voice.
	/// </summary>
	protected virtual IEnumerable<Connection> ExcludeFilter() => [];

	/// <summary>
	/// Who talks through this.
	/// </summary>
	internal Connection Owner => Network.Owner ?? Connection.Host;

	/// <summary>
	/// Whether we want to hear voice from a particular connection.
	/// </summary>
	protected virtual bool ShouldHearVoice( Connection connection )
	{
		return true;
	}

	/// <summary>
	/// Plays back what the owner says through this.
	/// </summary>
	internal VoiceChat.Speaker Speaker { get; private set; }

	private SoundHandle SoundHandle => Speaker?.SoundHandle;

	/// <summary>
	/// Play some of what the owner said. Returns the speaker, or null if it wasn't played.
	/// </summary>
	internal VoiceChat.Speaker Play( ReadOnlySpan<byte> packet )
	{
		if ( !ShouldHearVoice( Owner ) ) return null;

		Speaker ??= new VoiceChat.Speaker( isLoopback: !IsProxy );

		if ( !Speaker.WriteVoiceData( packet ) ) return null;

		LastPlayed = 0;

		// Apply our settings straight away, rather than playing the first bit with the defaults

		UpdateSound();

		return Speaker;
	}

	private void RemoveSpeaker()
	{
		Speaker?.Dispose();
		Speaker = null;
	}

	internal void UpdateSound()
	{
		if ( SoundHandle is not { IsValid: true } sound ) return;

		sound.TargetMixer = TargetMixer;
		sound.Distance = Distance;
		sound.Falloff = Falloff;
		sound.Volume = Volume;

		// If Loopback, pretend this isn't my own voice so we can always hear it
		sound.Loopback = !IsProxy && !Loopback;

		// Lipsync analysis is expensive, only run it when something consumes the result.
		// Applied every frame so toggling LipSync or late binding a Renderer affects the live voice.
		sound.LipSync.Enabled = LipSync && (Renderer.IsValid() || lipSyncRequested < 1.0f);

		if ( WorldspacePlayback )
		{
			sound.Transform = WorldTransform;
			sound.ListenLocal = false;
			sound.OcclusionEnabled = true;
		}
		else
		{
			sound.Transform = VoiceChat.DefaultTransform;
			sound.ListenLocal = true;
			sound.OcclusionEnabled = false;
		}
	}

	private void UpdateMorphs()
	{
		if ( !Renderer.IsValid() )
			return;

		// Nothing said for a while - the morphs have eased back to the animation
		if ( LastPlayed > 1.0f )
			return;

		var model = Renderer.Model;
		if ( model is null || model.MorphCount == 0 )
			return;

		var sceneModel = Renderer.SceneModel;
		if ( !sceneModel.IsValid() )
			return;

		// Drive the mouth from the live voice analysis. Voice packets are bursty,
		// so hold the current shape through short gaps, then ease back to zero.
		ReadOnlySpan<float> visemes = default;

		if ( LastPlayed < 0.2f && SoundHandle.IsValid() && SoundHandle.LipSync.VisemeWeights is { } live )
		{
			visemes = live;
		}

		sceneModel.Morphs.ApplyVisemes( visemes, MorphScale, MorphSmoothTime );
	}

	[AttributeUsage( AttributeTargets.Property )]
	public class ParentMixerAttribute : Attribute
	{
		public string MixerName { get; }
		public ParentMixerAttribute( string mixerName )
		{
			MixerName = mixerName;
		}
	}
}
