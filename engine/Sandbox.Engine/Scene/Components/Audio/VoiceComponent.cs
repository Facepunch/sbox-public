using Sandbox.Audio;

namespace Sandbox;

/// <summary>
/// Records and transmits voice/microphone input to other players.
/// </summary>
[Expose]
[Category( "Audio" )]
[Title( "Voice Transmitter" )]
[Icon( "mic" )]
[Tint( EditorTint.Green )]
public class Voice : Component
{
	static Voice singleRecorder;

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

	[Property, Group( "Viseme" )]
	public SkinnedModelRenderer Renderer { get; set; }

	[Property, Group( "Viseme" ), Range( 0, 5 )]
	public float MorphScale { get; set; } = 3.0f;

	[Property, Group( "Viseme" ), Range( 0, 1 )]
	public float MorphSmoothTime { get; set; } = 0.1f;

	/// <summary>
	/// How long has it been since this sound played?
	/// </summary>
	public RealTimeSince LastPlayed { get; private set; }

	/// <summary>
	/// Laughter score for the current audio frame, between 0 and 1
	/// </summary>
	public float LaughterScore => sound.IsValid() ? sound.LipSync.LaughterScore : 0;

	private bool recording = false;
	private SoundStream soundStream;
	private SoundHandle sound;
	private float[] morphs;
	private float[] morphVelocity;

	private static readonly string[] VisemeNames = new string[]
	{
		"viseme_sil",
		"viseme_PP",
		"viseme_FF",
		"viseme_TH",
		"viseme_DD",
		"viseme_KK",
		"viseme_CH",
		"viseme_SS",
		"viseme_NN",
		"viseme_RR",
		"viseme_AA",
		"viseme_E",
		"viseme_I",
		"viseme_O",
		"viseme_U",
	};

	private MixerHandle targetMixer;

	/// <inheritdoc cref="SoundHandle.TargetMixer"/>
	[Property]
	public MixerHandle VoiceMixer
	{
		get => targetMixer;
		set
		{
			if ( value == targetMixer )
				return;

			targetMixer = value;
			if ( sound.IsValid() )
				sound.TargetMixer = targetMixer.GetOrDefault();
		}
	}

	public Mixer TargetMixer
	{
		get => targetMixer.GetOrDefault();
		set => VoiceMixer = value;
	}

	private float distance = 15_000f;

	/// <inheritdoc cref="SoundHandle.Distance"/>
	[Property, AudioDistanceFloat]
	public float Distance
	{
		get => distance;
		set
		{
			if ( value == distance )
				return;

			distance = value;
			if ( sound.IsValid() )
				sound.Distance = distance;
		}
	}

	private Curve falloff = new( new( 0, 1, 0, -1.8f ), new( 0.05f, 0.22f, 3.5f, -3.5f ), new( 0.2f, 0.04f, 0.16f, -0.16f ), new( 1, 0 ) );

	/// <inheritdoc cref="SoundHandle.Falloff"/>
	[Property]
	public Curve Falloff
	{
		get => falloff;
		set
		{
			falloff = value;
			if ( sound.IsValid() )
				sound.Falloff = falloff;
		}
	}

	/// <summary>
	/// A list of 15 lipsync viseme weights. Requires <see cref="LipSync"/> to be enabled.
	/// </summary>
	public IReadOnlyList<float> Visemes => sound.IsValid() ? sound.LipSync.Visemes : Array.Empty<float>();

	internal override void OnEnabledInternal()
	{
		VoiceManager.OnCompressedVoiceData += OnVoice;

		soundStream = new SoundStream( VoiceManager.SampleRate );

		if ( Renderer.IsValid() && Renderer.Model.MorphCount > 0 )
		{
			morphs = new float[Renderer.Model.MorphCount];
			morphVelocity = new float[Renderer.Model.MorphCount];
		}

		base.OnEnabledInternal();
	}

	internal override void OnDisabledInternal()
	{
		base.OnDisabledInternal();

		VoiceManager.OnCompressedVoiceData -= OnVoice;

		if ( recording )
		{
			VoiceManager.StopRecording();
			recording = false;
		}

		sound?.Dispose();
		sound = null;
		soundStream?.Dispose();
		soundStream = null;
	}

	public bool IsRecording
	{
		get => recording;
	}

	private bool _isListening;

	/// <summary>
	/// Returns true if the mic is listening. Even if it's listening, it might
	/// not be playing - because it will only record and transmit if it can hear sound.
	/// </summary>
	public bool IsListening
	{
		set => _isListening = value;
		get
		{
			if ( IsProxy ) return false;
			if ( Mode == ActivateMode.AlwaysOn ) return true;
			if ( Mode == ActivateMode.PushToTalk )
			{
				return Input.Down( PushToTalkInput );
			}

			if ( Mode == ActivateMode.Manual )
				return _isListening;

			return false;
		}
	}

	/// <summary>
	/// Measure of audio loudness.
	/// </summary>
	public float Amplitude => sound.IsValid() ? sound.Amplitude : 0;

	private void UpdateSound()
	{
		if ( !sound.IsValid() ) return;

		sound.Volume = Volume;
		sound.Loopback = !IsProxy && !Loopback;

		if ( WorldspacePlayback )
		{
			sound.Position = WorldPosition;
			sound.Occlusion = true;
			sound.OcclusionRadius = 64;
		}
		else
		{
			sound.ListenLocal = true;

			// in ListenLocal mode we could let them place the sound in screen space,
			// so it plays back to the left on one team, to the right on others etc
			sound.Position = Vector3.Forward * 10.0f;
		}
	}

	protected sealed override void OnUpdate()
	{
		ApplyVisemes();
		FadeMorphs();
		UpdateSound();

		if ( !VoiceManager.IsValid )
			return;

		if ( IsListening )
		{
			if ( !recording )
			{
				VoiceManager.StartRecording();
				recording = true;
				singleRecorder = this;
			}
		}
		else if ( recording )
		{
			if ( singleRecorder == this )
			{
				VoiceManager.StopRecording();
			}

			recording = false;
		}
	}

	/// <summary>
	/// Exclude these connection from hearing our voice.
	/// </summary>
	protected virtual IEnumerable<Connection> ExcludeFilter()
	{
		return Enumerable.Empty<Connection>();
	}

	/// <summary>
	/// Whether we want to hear voice from a particular connection.
	/// </summary>
	protected virtual bool ShouldHearVoice( Connection connection )
	{
		return true;
	}

	private void OnVoice( Memory<byte> compressed )
	{
		if ( IsProxy ) return;
		if ( singleRecorder != this ) return;

		if ( Networking.System is not null )
		{
			using ( Rpc.FilterExclude( ExcludeFilter() ) )
			{
				Msg_Voice( compressed.ToArray() );
			}
		}
		else
		{
			Msg_Voice( compressed.ToArray() );
		}
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly | NetFlags.UnreliableNoDelay )]
	private void Msg_Voice( byte[] buffer )
	{
		if ( buffer == null || buffer.Length == 0 )
			return;

		if ( Application.IsHeadless )
			return;

		if ( !ShouldHearVoice( Rpc.Caller ) )
			return;

		OnVoice( buffer );
	}

	private void FadeMorphs()
	{
		if ( !Renderer.IsValid() )
			return;

		if ( morphs == null )
			return;

		var model = Renderer.Model;
		if ( model == null )
			return;

		var morphCount = model.MorphCount;
		if ( morphCount == 0 )
			return;

		if ( morphCount != morphs.Length )
		{
			morphs = new float[morphCount];
			morphVelocity = new float[morphCount];
		}

		var sceneModel = Renderer.SceneModel;
		if ( !sceneModel.IsValid() )
			return;

		if ( LastPlayed > 1.0f )
			return;

		for ( int i = 0; i < morphCount; i++ )
		{
			var weight = sceneModel.Morphs.Get( i );
			float target = LastPlayed < 0.2f ? morphs[i] : 0.0f;

			weight = MathX.SmoothDamp( weight, target, ref morphVelocity[i], MorphSmoothTime, Time.Delta );
			sceneModel.Morphs.Set( i, Math.Max( 0, weight ) );
		}
	}

	private void ApplyVisemes()
	{
		if ( !sound.IsValid() )
			return;

		if ( !Renderer.IsValid() )
			return;

		if ( morphs == null )
			return;

		var model = Renderer.Model;
		if ( model == null )
			return;

		var morphCount = model.MorphCount;
		if ( morphCount == 0 )
			return;

		if ( morphCount != morphs.Length )
			return;

		var visemes = sound.LipSync.Visemes;
		if ( visemes is null )
			return;

		for ( int i = 0; i < morphCount; i++ )
		{
			float totalWeight = 0;
			for ( int visemeIndex = 0; visemeIndex < visemes.Count; visemeIndex++ )
			{
				float weight = model.GetVisemeMorph( VisemeNames[visemeIndex], i );
				totalWeight += weight * visemes[visemeIndex];
			}

			morphs[i] = (totalWeight * MorphScale).Clamp( 0, 1 );
		}
	}

	private unsafe void OnVoice( byte[] buffer )
	{
		if ( buffer.Length < 2 )
			return;
		if ( soundStream is null )
			return;

		VoiceManager.Uncompress( buffer, samples =>
		{
			if ( !sound.IsValid() )
			{
				sound = soundStream.Play();
				sound.TargetMixer = TargetMixer;
				sound.Distance = Distance;
				sound.Falloff = Falloff;
				sound.LipSync.Enabled = true;
			}

			soundStream.WriteData( samples.Span );

			LastPlayed = 0;
			UpdateSound();
		} );
	}
}
