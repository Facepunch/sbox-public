using Sandbox.Utility;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sandbox.Audio;

/// <summary>
/// Takes a bunch of sound, changes its volumes, mixes it together, outputs it
/// </summary>
[Expose]
public partial class Mixer
{
	/// <summary>
	/// Allows monitoring of the output of the mixer
	/// </summary>
	[Hide]
	public AudioMeter Meter { get; } = new AudioMeter();

	/// <summary>
	/// Unique identifier for this object, for lookup, deserialization etc
	/// </summary>
	[Hide]
	public Guid Id { get; private set; } = Guid.NewGuid();

	/// <summary>
	/// Final mixed output buffer containing audio from all listeners.
	/// </summary>
	MultiChannelBuffer _outputBuffer;

	/// <summary>
	/// Per-listener audio buffers mixed into the final output buffer.
	/// </summary>
	readonly Dictionary<Listener, MultiChannelBuffer> _outputBuffers = new( ReferenceEqualityComparer.Instance );

	/// <summary>
	/// Tracks which listener buffers were written to during this mix frame.
	/// </summary>
	readonly HashSet<Listener> _usedListeners = new( ReferenceEqualityComparer.Instance );

	/// <summary>
	/// Snapshot for the current mix frame, set by StartMixing, read by MixVoices/FinishMixing.
	/// </summary>
	VoiceFrameSnapshot _snapshot;

	/// <summary>
	/// The current voice count for this mixer this frame.
	/// </summary>
	int _voiceCount;

	/// <summary>
	/// Reused list for sorting voices by priority. Stores (createdTime, voiceIndex) pairs.
	/// </summary>
	readonly List<(float CreatedTime, int Index)> _sortedVoices = new();

	/// <summary>
	/// Final mixed output buffer containing audio from all listeners.
	/// </summary>
	internal MultiChannelBuffer Output => _outputBuffer;

	float _volume = 1.0f;
	int _maxVoices = 64;

	/// <summary>
	/// The display name for this mixer
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Scale the volume of our output by this amount
	/// </summary>
	[Range( 0, 1 )]
	public float Volume
	{
		get => Volatile.Read( ref _volume );
		set => Interlocked.Exchange( ref _volume, value.Clamp( 0, 1 ) );
	}

	/// <summary>
	/// The maximum amount of voices to play at one time on this mixer
	/// </summary>
	public int MaxVoices
	{
		get => Volatile.Read( ref _maxVoices );
		set => Interlocked.Exchange( ref _maxVoices, value );
	}

	/// <summary>
	/// If true then this mixer will use custom occlusion tags. If false we'll use what our parent uses.
	/// </summary>
	[Obsolete( "Use OverrideOcclusion + BlockingTags / IgnoredTags instead." )]
	[ToggleGroup( "OverrideOcclusion" ), Hide]
	public bool OverrideOcclusion { get; set; }

	/// <summary>
	/// The tags which occlude our physics.
	/// </summary>
	[Obsolete( "Use BlockingTags instead." )]
	[ToggleGroup( "OverrideOcclusion" ), Hide]
	public TagSet OcclusionTags { get; private set; } = new TagSet();

	/// <summary>
	/// Get an array of occlusion tags our sounds want to hit. May return null if there are none defined!
	/// </summary>
	[Obsolete( "Use GetBlockingTags() instead." )]
	public IReadOnlySet<uint> GetOcclusionTags()
	{
#pragma warning disable CS0618
		if ( !OverrideOcclusion )
		{
			return Parent?.GetOcclusionTags();
		}

		return OcclusionTags?.GetTokens();
#pragma warning restore CS0618
	}

	/// <summary>
	/// If non-empty, audio simulation traces (occlusion, transmission, reverb rays) only register hits on
	/// bodies with one of these tags. Empty = hit everything that isn't in <see cref="IgnoredTags"/>.
	/// </summary>
	[Group( "Simulation" )]
	public TagSet BlockingTags { get; private set; } = new TagSet();

	/// <summary>
	/// Audio simulation traces always skip bodies with these tags (e.g. triggers, sky, passaudio).
	/// Applied on top of <see cref="BlockingTags"/>.
	/// </summary>
	[Group( "Simulation" )]
	public TagSet IgnoredTags { get; private set; } = new TagSet();

	/// <summary>
	/// Walks the parent chain. If either local tag set is non-empty, returns this mixer's <see cref="BlockingTags"/>;
	/// otherwise inherits from parent. Resolved together with <see cref="GetIgnoredTags"/> to avoid split-brain.
	/// </summary>
	public TagSet GetBlockingTags()
	{
		if ( !BlockingTags.IsEmpty || !IgnoredTags.IsEmpty ) return BlockingTags;
		return Parent?.GetBlockingTags();
	}

	/// <summary>
	/// Walks the parent chain. See <see cref="GetBlockingTags"/> for inheritance rules.
	/// </summary>
	public TagSet GetIgnoredTags()
	{
		if ( !BlockingTags.IsEmpty || !IgnoredTags.IsEmpty ) return IgnoredTags;
		return Parent?.GetIgnoredTags();
	}


	float _spatializing = 1.0f;

	/// <summary>
	/// When 0 the sound will come out of all speakers, when 1 it will be fully spatialized
	/// </summary>
	[Range( 0, 1 ), Group( "Simulation" )]
	public float Spatializing
	{
		get => Volatile.Read( ref _spatializing );
		set => Interlocked.Exchange( ref _spatializing, value );
	}

	/// <summary>
	/// Legacy misspelled alias for <see cref="Spatializing"/>.
	/// </summary>
	[Obsolete( "Use Spatializing instead." ), Hide]
	public float Spacializing
	{
		get => Spatializing;
		set => Spatializing = value;
	}

	float _distanceAttenuation = 1.0f;

	/// <summary>
	/// Sounds get quieter as they go further away
	/// </summary>
	[Range( 0, 1 ), Group( "Simulation" )]
	public float DistanceAttenuation
	{
		get => Volatile.Read( ref _distanceAttenuation );
		set => Interlocked.Exchange( ref _distanceAttenuation, value );
	}


	float _occlusion = 1.0f;

	/// <summary>
	/// How much these sounds can get occluded (0 = no occlusion simulation, 1 = full).
	/// </summary>
	[Range( 0, 1 ), Group( "Simulation" )]
	public float Occlusion
	{
		get => Volatile.Read( ref _occlusion );
		set => Interlocked.Exchange( ref _occlusion, value );
	}

	float _reverb = 1.0f;

	/// <summary>
	/// How much reverb is applied to sounds routed through this mixer (0 = dry / disabled, 1 = full).
	/// </summary>
	[Range( 0, 1 ), Group( "Simulation" )]
	public float Reverb
	{
		get => Volatile.Read( ref _reverb );
		set => Interlocked.Exchange( ref _reverb, value );
	}

	float _airAborb = 1.0f;

	/// <summary>
	/// How much the air absorbs energy from the sound
	/// </summary>
	[Range( 0, 1 ), Group( "Simulation" )]
	public float AirAbsorption
	{
		get => Volatile.Read( ref _airAborb );
		set => Interlocked.Exchange( ref _airAborb, value );
	}

	/// <summary>
	/// Should this be the only mixer that is heard?
	/// </summary>
	public bool Solo { get; set; }

	/// <summary>
	/// Is this mixer muted?
	/// </summary>
	public bool Mute { get; set; }

	/// <summary>
	/// The default mixer gets all sounds that don't have a mixer specifically assigned
	/// </summary>
	[Hide]
	public bool IsMaster => Parent is null;


	[Hide]
	Mixer Parent { get; set; }

	internal Mixer( Mixer parent )
	{
		// TODO - recreate on speaker config change
		_outputBuffer = new( AudioEngine.ChannelCount );
		Parent = parent;
	}

	/// <summary>
	/// Called at the start of the mixing frame. Stores the snapshot and clears per-frame state.
	/// </summary>
	internal void StartMixing( VoiceFrameSnapshot snapshot )
	{
		_snapshot = snapshot;
		_voiceCount = 0;
		_usedListeners.Clear();
		_outputBuffer.Silence();

		// Dispose per-listener buffers for listeners that were removed this frame.
		foreach ( var removed in snapshot.RemovedListeners )
		{
			if ( _outputBuffers.Remove( removed, out var buf ) ) buf.Dispose();
		}
	}

	/// <summary>
	/// Recursively mix all child mixers.
	/// </summary>
	internal void MixChildren( VoiceFrameSnapshot snapshot )
	{
		lock ( Lock )
		{
			if ( Children is null || Children.Count == 0 )
				return;

			foreach ( var child in Children )
			{
				child.StartMixing( snapshot );
				child.MixChildren( snapshot );

				if ( child.ShouldMixVoices() )
					child.MixVoices( snapshot );

				child.FinishMixing();

				_outputBuffer.MixFrom( child.Output, 1.0f );
			}
		}
	}

	bool ShouldPlay( in VoiceState vs )
	{
		if ( vs.Sampler is null ) return false;
		if ( vs.SourceCount == 0 ) return false;

		// vs.TargetMixer == null means "use the default mixer"
		if ( vs.TargetMixer is null ) return Mixer.Default == this;
		if ( string.IsNullOrEmpty( Name ) ) return false;
		return vs.TargetMixer.Name == Name;
	}

	/// <summary>
	/// Mix snapshot voices that target this mixer.
	/// No locks needed, all data comes from the immutable snapshot.
	/// </summary>
	internal void MixVoices( VoiceFrameSnapshot snapshot )
	{
		_sortedVoices.Clear();
		var voices = CollectionsMarshal.AsSpan( snapshot.Voices );
		for ( var i = 0; i < voices.Length; i++ )
		{
			if ( ShouldPlay( in voices[i] ) ) _sortedVoices.Add( (voices[i].CreatedTime, i) );
		}

		_sortedVoices.Sort( static ( a, b ) => b.CreatedTime.CompareTo( a.CreatedTime ) );

		var limit = Math.Min( _sortedVoices.Count, _maxVoices );
		for ( var si = 0; si < limit; si++ )
		{
			MixVoice( snapshot, voices, _sortedVoices[si].Index );
			Interlocked.Add( ref _voiceCount, 1 );
		}
	}

	private bool ShouldMixVoices()
	{
		if ( IsMuted() ) return false;
		if ( AnySolo( Master ) ) return IsSolo();

		return true;
	}

	internal bool IsMuted()
	{
		if ( Mute ) return true;
		if ( Parent is null ) return false;

		return Parent.IsMuted();
	}

	internal bool IsSolo()
	{
		if ( Solo ) return true;
		if ( Parent is null ) return false;

		return Parent.IsSolo();
	}

	internal static bool AnySolo( Mixer mixer )
	{
		if ( mixer.Solo ) return true;

		lock ( mixer.Lock )
		{
			if ( mixer.Children is null ) return false;
			foreach ( var child in mixer.Children )
			{
				if ( AnySolo( child ) ) return true;
			}
			return false;
		}
	}

	/// <summary>
	/// Mixing is finished. Apply processors, scale by volume, update meter.
	/// </summary>
	internal void FinishMixing()
	{
		ApplyProcessors();

		var volume = Volume;

		if ( !Application.IsEditor )
		{
			if ( string.Equals( Name, "music", StringComparison.OrdinalIgnoreCase ) )
				volume *= Preferences.MusicVolume;
			else if ( string.Equals( Name, "voice", StringComparison.OrdinalIgnoreCase ) )
				volume *= Preferences.VoipVolume;
		}

		_outputBuffer.Scale( volume );
		Meter.Add( _outputBuffer, _voiceCount );
	}

	static Superluminal _mixVoice = new( "Mix Voice", "#4d5e73" );
	MultiChannelBuffer mixBuffer = new( AudioEngine.ChannelCount );
	readonly MultiChannelBuffer _reverbSendBuffer = new( AudioEngine.ChannelCount );
	readonly MultiChannelBuffer _reverbOutputBuffer = new( AudioEngine.ChannelCount );
	readonly MultiChannelBuffer _reverbDryBuffer = new( AudioEngine.ChannelCount );

	/// <summary>
	/// Mix one voice described by the snapshot. No locks, all inputs come from the snapshot,
	/// outputs go to snapshot.Voices[voiceIndex].Output* fields.
	/// </summary>
	void MixVoice( VoiceFrameSnapshot snapshot, Span<VoiceState> voices, int voiceIndex )
	{
		using var _ = _mixVoice.Start();

		if ( (uint)voiceIndex >= (uint)voices.Length ) return;
		ref readonly var vs = ref voices[voiceIndex];

		var volume = vs.Volume * vs.FadeVolume;

		if ( vs.IsFadingIn && vs.FadeInTimer ) voices[voiceIndex].OutputFadeInComplete = true;

		var samples = vs.Sampler.GetLastReadSamples();
		var buffer = samples.Get( AudioChannel.Left );

		voices[voiceIndex].OutputAmplitude = buffer.LevelMax;

		if ( vs.HasLipSync ) vs.LipSync.ProcessLipSync( buffer );
		if ( vs.Loopback && !AudioEngine.VoiceLoopback ) return;

		var allModels = CollectionsMarshal.AsSpan( snapshot.AllModels );
		var allBinaurals = CollectionsMarshal.AsSpan( snapshot.AllBinaurals );
		var allParams = CollectionsMarshal.AsSpan( snapshot.AllParams );
		var reverbApplied = false;

		for ( var i = 0; i < vs.SourceCount; i++ )
		{
			DirectSoundModel source;
			Listener listener;
			Transform mixTransform;

			if ( vs.ListenLocal )
			{
				source = allModels[vs.SourceOffset];
				listener = Listener.Local;
				mixTransform = default;
			}
			else
			{
				var listeners = CollectionsMarshal.AsSpan( snapshot.Listeners );
				ref readonly var ls = ref listeners[i];
				if ( ls.Scene != vs.Scene ) continue;
				source = allModels[vs.SourceOffset + i];
				listener = ls.Listener;
				mixTransform = ls.MixTransform;
			}

			if ( source is null ) continue;
			if ( !_outputBuffers.TryGetValue( listener, out var targetBuffer ) ) targetBuffer = _outputBuffers[listener] = new MultiChannelBuffer( AudioEngine.ChannelCount );
			if ( _usedListeners.Add( listener ) ) targetBuffer.Silence();

			mixBuffer.CopyFromUpmix( samples );

			// Evaluate reverb gate up front so we can skip the dry-buffer copy when the send won't fire.
			float mixerReverbScale = Reverb;
			bool willRunReverb = !reverbApplied && vs.Reverb is not null && vs.Reverb.IsValid
					&& vs.ReverbRoom.Mix > 0.09f
					&& mixerReverbScale > 0f;
			float reverbSendLevel = 0f;
			if ( willRunReverb )
			{
				var srcParams = allParams[vs.SourceOffset + (vs.ListenLocal ? 0 : i)];
				var distAtten = ComputeDistanceAtten( listener, in srcParams );
				reverbSendLevel = srcParams.ReverbAmount * mixerReverbScale * distAtten * volume * (srcParams.OcclusionEnabled ? srcParams.TransmissionBands.Mid : 1f);
				// Capture dry signal before DirectEffect so reverb send bypasses distance attenuation.
				if ( reverbSendLevel >= 0.175f ) _reverbDryBuffer.CopyFrom( mixBuffer );
			}

			var sourceParams = allParams[vs.SourceOffset + i];
			// Always run SA DirectEffect to keep its internal gain interpolator warm; skipping causes a pop on re-audibility.
			ApplyDirectMix( source, listener, mixBuffer, volume, in sourceParams );
			ConvertToBinaural( allBinaurals[vs.SourceOffset + i], mixTransform, in vs, mixBuffer );

			if ( willRunReverb )
			{
				reverbApplied = true;
				if ( reverbSendLevel >= 0.175f )
				{
					var room = vs.ReverbRoom;
					FillReverbSend( _reverbDryBuffer, reverbSendLevel );
					_reverbOutputBuffer.Silence();
					vs.Reverb.Apply( room.DecayTimeLow, room.DecayTimeMid, room.DecayTimeHigh, _reverbSendBuffer, _reverbOutputBuffer );
					targetBuffer.MixFrom( _reverbOutputBuffer, room.Mix );
				}
			}

			targetBuffer.MixFrom( mixBuffer, 1.0f );
		}
	}

	static float ComputeDistanceAtten( Listener listener, in DirectSoundParams p )
	{
		if ( !p.DistanceAttenuation ) return 1f;

		var dist = p.Position.Distance( listener.MixTransform.Position );
		var t = MathX.Clamp( dist / MathF.Max( p.Distance, 1f ), 0f, 1f );
		return p.Falloff.Evaluate( t );
	}

	void FillReverbSend( MultiChannelBuffer source, float scale )
	{
		var weight = scale / source.ChannelCount;
		for ( var outCh = 0; outCh < _reverbSendBuffer.ChannelCount; outCh++ )
		{
			_reverbSendBuffer.Get( outCh ).Silence();
			for ( var inCh = 0; inCh < source.ChannelCount; inCh++ )
			{
				_reverbSendBuffer.Get( outCh ).MixFrom( source.Get( inCh ), weight );
			}
		}
	}

	MultiChannelBuffer _input;
	MultiChannelBuffer _binauralInput;

	void EnsureInputBuffer( int channelCount )
	{
		if ( _input?.ChannelCount != channelCount )
		{
			_input?.Dispose();
			_input = new MultiChannelBuffer( channelCount );
		}
	}

	void EnsureBinauralInputBuffer( int channelCount )
	{
		if ( _binauralInput?.ChannelCount != channelCount )
		{
			_binauralInput?.Dispose();
			_binauralInput = new MultiChannelBuffer( channelCount );
		}
	}

	void ApplyDirectMix( DirectSoundModel source, Listener listener, MultiChannelBuffer inputoutput, float volume,
		in DirectSoundParams sourceParams )
	{
		if ( source is null )
			return;

		EnsureInputBuffer( inputoutput.ChannelCount );
		_input.CopyFrom( inputoutput );
		source.Apply( listener, _input, inputoutput, Occlusion, volume, in sourceParams );
	}

	/// <summary>
	/// Spatialize the voice based on its snapshotted position and spatialization parameters.
	/// </summary>
	void ConvertToBinaural( BinauralEffect binaural, Transform mixTransform, in VoiceState vs, MultiChannelBuffer inputoutput )
	{
		if ( binaural is null ) return;

		if ( vs.ListenLocal )
		{
			var localSpatial = 0.1f * Spatializing;
			// ListenLocal voices on mixers with Spatializing==0 (music/UI default) would just
			// run a near-identity HRIR convolution; inputoutput already holds the desired output.
			if ( localSpatial <= 0f ) return;

			var pos = vs.Position;
			while ( pos.Length < 0.5f ) pos += new Vector3( 1, 0, 0 );

			EnsureBinauralInputBuffer( 2 );
			_binauralInput.CopyFrom( inputoutput );

			binaural.Apply( pos, localSpatial, useNearestInterpolation: true, _binauralInput, inputoutput );
			return;
		}

		var soundDirectionLocal = mixTransform.PointToLocal( vs.Position );
		var spacial = vs.SpacialBlend * Spatializing;

		var soundDistance = soundDirectionLocal.Length;
		if ( soundDistance < 32.0f )
		{
			spacial *= soundDistance.Remap( 1.0f, 32.0f, 0, 1.0f );
			soundDirectionLocal = soundDistance <= 0.1f
				? new Vector3( 0.1f, 0, 0 )
				: soundDirectionLocal.Normal * soundDistance;
		}

		EnsureBinauralInputBuffer( 2 );
		_binauralInput.CopyFrom( inputoutput );

		bool useNearest = vs.IsVoice || vs.Loopback || spacial < 0.5f || soundDistance > 1500f;
		binaural.Apply( soundDirectionLocal, spacial, useNearest, _binauralInput, inputoutput );
	}

	/// <summary>
	/// Stop all sound handles using this mixer
	/// </summary>
	public void StopAll( float fade )
	{
		SoundHandle.StopAll( fade, this );
	}
}



