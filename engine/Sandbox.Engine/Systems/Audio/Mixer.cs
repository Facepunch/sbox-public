using Sandbox.Utility;
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
	readonly Dictionary<Listener, MultiChannelBuffer> _outputBuffers = [];

	/// <summary>
	/// Tracks which listener buffers were used during the current frame.
	/// </summary>
	readonly HashSet<Listener> _usedListeners = [];

	/// <summary>
	/// We don't want to access sound listeners directly, because it might keep changing
	/// in the other thread. This is a local copy for us to use.
	/// </summary>
	IReadOnlyList<Listener> _listeners;

	/// <summary>
	/// Id of listeners that have been removed so we can dispose of their buffers.
	/// </summary>
	IReadOnlyList<Listener> _removedListeners;

	/// <summary>
	/// The current voices playing on this mixer
	/// </summary>
	int _voiceCount;

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
	/// Called at the start of the mixing frame
	/// </summary>
	internal void StartMixing( IReadOnlyList<Listener> listeners, IReadOnlyList<Listener> removedListeners )
	{
		_listeners = listeners;
		_removedListeners = removedListeners;
		_voiceCount = 0;
		_usedListeners?.Clear();

		_outputBuffer.Silence();

		if ( _outputBuffers is not null )
		{
			// Dispose buffers of removed listeners
			foreach ( var id in _removedListeners )
			{
				if ( _outputBuffers.Remove( id, out var buffer ) )
				{
					buffer.Dispose();
				}
			}
		}
	}

	/// <summary>
	/// Mix the child mixes
	/// </summary>
	internal void MixChildren( List<SoundHandle> voices )
	{
		lock ( Lock )
		{
			if ( Children is null || Children.Count == 0 )
				return;

			foreach ( var child in Children )
			{
				child.StartMixing( _listeners, _removedListeners );
				child.MixChildren( voices );

				if ( child.ShouldMixVoices() )
				{
					child.MixVoices( voices );
				}

				child.FinishMixing();

				// add into the main buffer
				_outputBuffer.MixFrom( child.Output, 1.0f );
			}
		}
	}

	bool ShouldPlay( SoundHandle voice )
	{
		if ( !voice.CanBeMixed() ) return false;
		if ( !voice.IsTargettingMixer( this ) ) return false;

		return true;
	}

	/// <summary>
	/// Mix the incoming voices into the mix
	/// </summary>
	internal void MixVoices( List<SoundHandle> voices )
	{
		// loop all playing sounds, mix them into buffer
		// Can't do this in a thread because stream audio hrtf can't handle that
		foreach ( var voice in voices.Where( ShouldPlay ).OrderByDescending( x => x._CreatedTime ).Take( _maxVoices ) )
		{
			lock ( voice )
			{
				// While yeah this is checked, we could have a race condition where sampler
				// has become null while we were ordering and taking!
				if ( !ShouldPlay( voice ) ) continue;

				MixVoice( voice );

				Interlocked.Add( ref _voiceCount, 1 );
			}
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
	/// Mixing is finished. Clean up and finalize
	/// </summary>
	internal void FinishMixing()
	{
		ApplyProcessors();

		var volume = Volume;

		//
		// Scale by convar. Todo allow mixers to define convars?
		//
		if ( !Application.IsEditor )
		{
			if ( string.Equals( Name, "music", StringComparison.OrdinalIgnoreCase ) )
			{
				volume *= Preferences.MusicVolume;
			}
			else if ( string.Equals( Name, "voip", StringComparison.OrdinalIgnoreCase ) )
			{
				volume *= Preferences.VoipVolume;
			}
		}

		_outputBuffer.Scale( volume );
		Meter.Add( _outputBuffer, _voiceCount );
	}


	static Superluminal _mixVoice = new( "Mix Voice", "#4d5e73" );
	MultiChannelBuffer mixBuffer = new( AudioEngine.ChannelCount );

	/// <summary>
	/// Mix a single voice
	/// </summary>
	void MixVoice( SoundHandle voice )
	{
		using var _ = _mixVoice.Start();
		var volume = voice.Volume;

		if ( voice.IsFadingOut )
		{
			volume *= voice.Fadeout.EvaluateDelta( (float)voice.TimeUntilFaded.Fraction );
		}

		if ( voice.IsFadingIn )
		{
			volume *= voice.Fadein.EvaluateDelta( (float)voice.TimeUntilFadedIn.Fraction );

			if ( voice.TimeUntilFadedIn )
			{
				voice.IsFadingIn = false;
			}
		}

		var samples = voice.sampler.GetLastReadSamples();
		var buffer = samples.Get( AudioChannel.Left );

		// Store the levels on the sound for use later
		voice.Amplitude = buffer.LevelMax;

		// Process lipsync if it's enabled
		if ( voice.LipSync is not null && voice.LipSync.Enabled )
		{
			voice.LipSync.ProcessLipSync( buffer );
		}

		// player voice loopback. Don't mix it if we're not in loopback mode
		if ( voice.Loopback && !AudioEngine.VoiceLoopback )
		{
			return;
		}

		// If voice is local, we don't need to play it at every listener, just once
		var listenLocal = voice.ListenLocal || voice.Scene is null;
		var listenerCount = listenLocal ? 1 : _listeners.Count;

		for ( int i = 0; i < listenerCount; i++ )
		{
			var listener = Listener.Local;
			if ( !listenLocal )
			{
				listener = _listeners[i];
				if ( listener.Scene != voice.Scene )
					continue;
			}

			var source = voice.GetSource( listener );
			if ( source is null )
				continue;

			if ( !_outputBuffers.TryGetValue( listener, out var targetBuffer ) )
			{
				// Allocate a new buffer for this listener.
				targetBuffer = _outputBuffers[listener] = new MultiChannelBuffer( AudioEngine.ChannelCount );
			}

			// Track which buffers have been used, these buffers will have processors applied to them.
			if ( _usedListeners.Add( listener ) )
			{
				// First use this mix, silence the buffer.
				targetBuffer.Silence();
			}

			//
			// Upmix because samples could be mono.
			//
			mixBuffer.CopyFromUpmix( samples );

			//
			// Apply any attenuation and occlusion
			//
			ApplyDirectMix( source, listener, mixBuffer, volume );

			//
			// Make it stereo, based on location
			//
			ConvertToBinaural( source, listener, voice, mixBuffer );

			//
			// Mix it into the target buffer
			//
			targetBuffer.MixFrom( mixBuffer, 1.0f );
		}
	}

	MultiChannelBuffer _input;

	void ApplyDirectMix( SteamAudioSource source, Listener listener, MultiChannelBuffer inputoutput, float volume )
	{
		if ( source is null )
			return;

		if ( _input is not null && _input.ChannelCount != inputoutput.ChannelCount )
		{
			_input.Dispose();
			_input = null;
		}

		_input ??= new MultiChannelBuffer( inputoutput.ChannelCount );
		_input.CopyFrom( inputoutput );

		source.ApplyDirectMix( listener, _input, inputoutput, Occlusion, volume );
	}

	/// <summary>
	/// This will spatialize the voice based on its location
	/// </summary>
	void ConvertToBinaural( SteamAudioSource source, Listener listener, SoundHandle voice, MultiChannelBuffer inputoutput )
	{
		if ( source is null )
			return;

		if ( _input is not null && _input.ChannelCount != inputoutput.ChannelCount )
		{
			_input.Dispose();
			_input = null;
		}

		_input ??= new MultiChannelBuffer( inputoutput.ChannelCount );
		_input.CopyFrom( inputoutput );

		bool is2d = voice.ListenLocal;

		if ( is2d )
		{
			// don't play the sound too close to the listener
			var pos = voice.Position;
			while ( pos.Length < 0.5f )
			{
				pos += new Vector3( 1, 0, 0 );
			}

			var spacial = 0.1f * Spacializing;

			// 2D sounds use very low spatialization, nearest neighbor is sufficient
			source.ApplyBinauralMix( pos, spacial, useNearestInterpolation: true, _input, inputoutput );
			return;
		}
		else
		{
			var soundDirectionLocal = listener.MixTransform.PointToLocal( voice.Position );
			var spacial = voice.SpacialBlend * Spacializing;

			// If sounds are really close, we need to fade them to be non spatial
			// If the sound is right on the listener, we need to offset it a bit because
			// steam audio will shit itself if it's right in the middle of the head
			{
				var soundDistance = soundDirectionLocal.Length;

				if ( soundDistance < 32.0f )
				{
					spacial *= soundDistance.Remap( 1.0f, 32.0f, 0, 1.0f );

					if ( soundDistance <= 0.1f )
					{
						soundDirectionLocal = new Vector3( 0.1f, 0, 0 );
					}
					else
					{
						soundDirectionLocal = soundDirectionLocal.Normal * soundDistance;
					}
				}
			}

			// Determine HRTF interpolation mode:
			// - Voice/speech sounds don't benefit from bilinear interpolation
			// - Player's own voice (loopback) uses nearest
			// - Low spatial blend means minimal HRTF effect, nearest is sufficient
			bool useNearest = voice.IsVoice || voice.Loopback || spacial < 0.5f;

			source.ApplyBinauralMix( soundDirectionLocal, spacial, useNearest, _input, inputoutput );
		}
	}

	/// <summary>
	/// Stop all sound handles using this mixer
	/// </summary>
	public void StopAll( float fade )
	{
		SoundHandle.StopAll( fade, this );
	}
}
