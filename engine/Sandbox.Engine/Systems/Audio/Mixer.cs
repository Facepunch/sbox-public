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
	[ToggleGroup( "OverrideOcclusion" )]
	public bool OverrideOcclusion { get; set; }

	/// <summary>
	/// The tags which occlude our physics
	/// </summary>
	[ToggleGroup( "OverrideOcclusion" )]
	public TagSet OcclusionTags { get; private set; } = new TagSet();

	/// <summary>
	/// Get an array of occlusion tags our sounds want to hit. May return null if there are none defined!
	/// </summary>
	public IReadOnlySet<uint> GetOcclusionTags()
	{
		if ( !OverrideOcclusion )
		{
			return Parent?.GetOcclusionTags();
		}

		return OcclusionTags?.GetTokens();
	}


	float _spacializing = 1.0f;

	/// <summary>
	/// When 0 the sound will come out of all speakers, when 1 it will be fully spacialized
	/// </summary>
	[Range( 0, 1 ), Group( "Voice Handling" )]
	public float Spacializing
	{
		get => Volatile.Read( ref _spacializing );
		set => Interlocked.Exchange( ref _spacializing, value );
	}

	float _distanceAttenuation = 1.0f;

	/// <summary>
	/// Sounds get quieter as they go further away
	/// </summary>
	[Range( 0, 1 ), Group( "Voice Handling" )]
	public float DistanceAttenuation
	{
		get => Volatile.Read( ref _distanceAttenuation );
		set => Interlocked.Exchange( ref _distanceAttenuation, value );
	}


	float _occlusion = 1.0f;

	/// <summary>
	/// How much these sounds can get occluded
	/// </summary>
	[Range( 0, 1 ), Group( "Voice Handling" )]
	public float Occlusion
	{
		get => Volatile.Read( ref _occlusion );
		set => Interlocked.Exchange( ref _occlusion, value );
	}

	float _airAborb = 1.0f;

	/// <summary>
	/// How much the air absorbs energy from the sound
	/// </summary>
	[Range( 0, 1 ), Group( "Voice Handling" )]
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
			return mixer.Children.Any( AnySolo );
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

		var AllModels = CollectionsMarshal.AsSpan( snapshot.AllModels );
		var allBinaurals = CollectionsMarshal.AsSpan( snapshot.AllBinaurals );
		var allParams = CollectionsMarshal.AsSpan( snapshot.AllParams );

		for ( var i = 0; i < vs.SourceCount; i++ )
		{
			AcousticModel source;
			Listener listener;
			Transform mixTransform;

			if ( vs.ListenLocal )
			{
				source = AllModels[vs.SourceOffset];
				listener = Listener.Local;
				mixTransform = default;
			}
			else
			{
				var listeners = CollectionsMarshal.AsSpan( snapshot.Listeners );
				ref readonly var ls = ref listeners[i];
				if ( ls.Scene != vs.Scene ) continue;
				source = AllModels[vs.SourceOffset + i];
				listener = ls.Listener;
				mixTransform = ls.MixTransform;
			}

			if ( source is null ) continue;

			if ( !_outputBuffers.TryGetValue( listener, out var targetBuffer ) )
				targetBuffer = _outputBuffers[listener] = new MultiChannelBuffer( AudioEngine.ChannelCount );

			if ( _usedListeners.Add( listener ) ) targetBuffer.Silence();

			mixBuffer.CopyFromUpmix( samples );
			var sourceParams = allParams[vs.SourceOffset + i];
			ApplyDirectMix( source, listener, mixBuffer, volume, in sourceParams );
			ConvertToBinaural( allBinaurals[vs.SourceOffset + i], mixTransform, in vs, mixBuffer );
			targetBuffer.MixFrom( mixBuffer, 1.0f );
		}
	}

	MultiChannelBuffer _input;

	void EnsureInputBuffer( int channelCount )
	{
		if ( _input?.ChannelCount != channelCount )
		{
			_input?.Dispose();
			_input = new MultiChannelBuffer( channelCount );
		}
	}

	void ApplyDirectMix( AcousticModel source, Listener listener, MultiChannelBuffer inputoutput, float volume,
		in AcousticModelParams sourceParams )
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
		if ( binaural is null )
			return;

		EnsureInputBuffer( inputoutput.ChannelCount );
		_input.CopyFrom( inputoutput );

		if ( vs.ListenLocal )
		{
			var pos = vs.Position;
			while ( pos.Length < 0.5f ) pos += new Vector3( 1, 0, 0 );

			binaural.Apply( pos, 0.1f * Spacializing, useNearestInterpolation: true, _input, inputoutput );
			return;
		}

		var soundDirectionLocal = mixTransform.PointToLocal( vs.Position );
		var spacial = vs.SpacialBlend * Spacializing;

		var soundDistance = soundDirectionLocal.Length;
		if ( soundDistance < 32.0f )
		{
			spacial *= soundDistance.Remap( 1.0f, 32.0f, 0, 1.0f );
			soundDirectionLocal = soundDistance <= 0.1f
				? new Vector3( 0.1f, 0, 0 )
				: soundDirectionLocal.Normal * soundDistance;
		}

		bool useNearest = vs.IsVoice || vs.Loopback || spacial < 0.5f;
		binaural.Apply( soundDirectionLocal, spacial, useNearest, _input, inputoutput );
	}

	/// <summary>
	/// Stop all sound handles using this mixer
	/// </summary>
	public void StopAll( float fade )
	{
		SoundHandle.StopAll( fade, this );
	}
}



