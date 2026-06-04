using NativeEngine;

namespace Sandbox;

public enum SoundFormat : byte
{
	PCM16 = 0,
	PCM8,
	MP3,
	ADPCM,
};

/// <summary>
/// A sound resource.
/// </summary>
public partial class SoundFile : Resource, IValid
{
	/// <summary>
	/// Options for creating a sound from raw PCM data via <see cref="FromPcm(string, Span{byte}, PcmOptions)"/>.
	/// </summary>
	public struct PcmOptions()
	{
		/// <summary>
		/// Number of channels (1 = mono, 2 = stereo).
		/// </summary>
		public int Channels { get; set; } = 1;

		/// <summary>
		/// Sample rate in Hz (e.g. 44100).
		/// </summary>
		public uint Rate { get; set; } = 44100;

		/// <summary>
		/// Bits per sample (8, 16, or 32).
		/// </summary>
		public int Bits { get; set; } = 16;

		/// <summary>
		/// Whether the sound should loop. Overridden by <see cref="LoopStart"/>/<see cref="LoopEnd"/> if set.
		/// </summary>
		public bool Loop { get; set; } = false;

		/// <summary>
		/// Start sample of the loop region. -1 means no loop.
		/// </summary>
		public int LoopStart { get; set; } = -1;

		/// <summary>
		/// End sample of the loop region. 0 means end of sound.
		/// </summary>
		public int LoopEnd { get; set; } = 0;
	}

	/// <summary>
	/// Options for creating a sound from WAV or MP3 data via <see cref="FromWav(string, Span{byte}, LoadOptions)"/> or <see cref="FromMp3(string, Span{byte}, LoadOptions)"/>.
	/// </summary>
	public struct LoadOptions()
	{
		/// <summary>
		/// Whether the sound should loop. Overridden by <see cref="LoopStart"/>/<see cref="LoopEnd"/> if set.
		/// </summary>
		public bool Loop { get; set; } = false;

		/// <summary>
		/// Start sample of the loop region. -1 means no loop.
		/// </summary>
		public int LoopStart { get; set; } = -1;

		/// <summary>
		/// End sample of the loop region. 0 means end of sound.
		/// </summary>
		public int LoopEnd { get; set; } = 0;
	}

	internal CSfxTable native;
	internal VSound_t sound;

	internal static Dictionary<string, SoundFile> Loaded = new();

	/// <summary>
	/// Ran when the file is reloaded/recompiled, etc.
	/// </summary>
	public Action OnSoundReloaded { get; set; }

	/// <summary>
	/// true if sound is loaded
	/// </summary>
	public bool IsLoaded => sound.IsValid;

	/// <summary>
	/// Format of the audio file.
	/// </summary>
	public SoundFormat Format => sound.format();

	/// <summary>
	/// Bits per each sample of this sound file.
	/// </summary>
	public int BitsPerSample => sound.BitsPerSample();

	/// <summary>
	/// Number of channels this audio file has.
	/// </summary>
	public int Channels => sound.channels();

	/// <summary>
	/// Bytes per each sample of this sound file.
	/// </summary>
	public int BytesPerSample => sound.BytesPerSample();

	/// <summary>
	/// Size of one sample, typically this would be "sample size * channel count", but can vary on audio format.
	/// </summary>
	public int SampleFrameSize => sound.m_sampleFrameSize();

	/// <summary>
	/// Sample rate of this sound file, per second.
	/// </summary>
	public int Rate => sound.m_rate();

	/// <summary>
	/// Duration of the sound this sound file contains, in seconds.
	/// </summary>
	public float Duration => sound.Duration();

	public override bool IsValid => native.IsValid;

	// Can be played
	public bool IsValidForPlayback => IsValid && native.IsValidForPlayback();

	private SoundFile( CSfxTable native )
	{
		if ( native.IsNull ) throw new Exception( "SoundFile pointer cannot be null!" );

		this.native = native;
	}

	~SoundFile()
	{
		Destroy();
	}

	internal static void Init()
	{
		Shutdown();

		Loaded = new Dictionary<string, SoundFile>();
	}

	internal static void Shutdown()
	{
		if ( Loaded != null )
		{
			foreach ( var file in Loaded.Values )
			{
				file.Destroy();
			}
		}
	}

	internal override void Destroy()
	{
		// Release the native strong handle for the sound resource.
		// Without this the native refcount is never decremented and
		// the SOUND resource leaks on shutdown.
		if ( !sound.IsNull )
		{
			var s = sound;
			sound = default;
			MainThread.Queue( () => s.DestroyStrongHandle() );
		}

		native = default;

		base.Destroy();
	}

	internal void OnReloadInternal()
	{
		SoundHandle.StopAll( native );

		sound = default;
	}

	internal void OnReloadedInternal()
	{
		if ( native.IsValid )
			sound = native.GetSound();

		OnSoundReloaded?.Invoke();
	}

	/// <summary>
	/// Load a new sound from disk. Includes automatic caching.
	/// </summary>
	/// <param name="filename">The file path to load the sound from.</param>
	/// <returns>The loaded sound file, or null if failed.</returns>
	public static SoundFile Load( string filename )
	{
		ThreadSafe.AssertIsMainThread( "SoundFile.Load" );

		if ( !filename.EndsWith( ".vsnd", StringComparison.OrdinalIgnoreCase ) )
			filename = System.IO.Path.ChangeExtension( filename, "vsnd" );

		if ( Loaded.TryGetValue( filename, out var soundFile ) )
			return soundFile;

		if ( Mounting.Directory.TryLoad( filename, Mounting.ResourceType.Sound, out object sound ) && sound is SoundFile s )
			return s;

		var soundFilePointer = g_pSoundSystem.PrecacheSound( filename );
		if ( soundFilePointer.IsNull )
			return null;

		soundFile = new SoundFile( soundFilePointer );

		Loaded[filename] = soundFile;

		soundFile.RegisterWeakResourceId( filename );

		return soundFile;
	}

	/// <summary>
	/// Load from PCM.
	/// </summary>
	internal static unsafe SoundFile Create( string filename, Span<byte> data, int channels, uint rate, int format, uint sampleCount, float duration, int loopStart, int loopEnd )
	{
		fixed ( byte* pData = data )
		{
			var sfx = g_pSoundSystem.CreateSound( filename, channels, (int)rate, format, (int)sampleCount, duration, loopStart, loopEnd, (IntPtr)pData, data.Length );
			if ( sfx.IsNull )
				return null;

			var soundFile = new SoundFile( sfx );
			Loaded[filename] = soundFile;

			soundFile.RegisterWeakResourceId( filename );

			g_pSoundSystem.PreloadSound( sfx );

			return soundFile;
		}
	}

	/// <summary>
	/// Create a sound from raw PCM data.
	/// </summary>
	/// <param name="filename">Sound name</param>
	/// <param name="data">Raw interleaved PCM data</param>
	/// <param name="options">PCM format and loop options</param>
	public static unsafe SoundFile FromPcm( string filename, Span<byte> data, PcmOptions options = default )
	{
		ThreadSafe.AssertIsMainThread( "SoundFile.FromPcm" );

		if ( !filename.EndsWith( ".vsnd", StringComparison.OrdinalIgnoreCase ) )
			filename = System.IO.Path.ChangeExtension( filename, "vsnd" );

		if ( Loaded.TryGetValue( filename, out var sf ) )
			return sf;

		if ( data.Length <= 0 )
			throw new ArgumentException( "Invalid data" );

		var loopStart = options.LoopStart >= 0 ? options.LoopStart : (options.Loop ? 0 : -1);
		var loopEnd = options.LoopEnd;
		var bits = options.Bits;
		var format = bits == 8 ? 1 : bits == 16 ? 0 : bits == 32 ? 3 : throw new ArgumentException( $"Unsupported bits: {bits}" );
		var samples = (uint)(data.Length / (options.Channels * (bits >> 3)));
		var duration = samples / (float)options.Rate;

		return Create( filename, data, options.Channels, options.Rate, format, samples, duration, loopStart, loopEnd );
	}

	/// <summary>
	/// Load from WAV.
	/// </summary>
	/// <param name="filename">Sound name</param>
	/// <param name="data">WAV file data</param>
	/// <param name="options">Loop options</param>
	public static unsafe SoundFile FromWav( string filename, Span<byte> data, LoadOptions options = default )
	{
		ThreadSafe.AssertIsMainThread( "SoundFile.FromWav" );

		if ( !filename.EndsWith( ".vsnd", StringComparison.OrdinalIgnoreCase ) )
			filename = System.IO.Path.ChangeExtension( filename, "vsnd" );

		if ( Loaded.TryGetValue( filename, out var soundFile ) )
			return soundFile;

		if ( data.Length <= 0 )
			throw new ArgumentException( "Invalid data" );

		var soundData = SoundData.FromWav( data );
		var pcmData = soundData.PCMData ?? throw new ArgumentException( "Invalid WAV" );

		var format = 0;
		if ( soundData.Format == 1 )
		{
			if ( soundData.BitsPerSample == 8 ) format = 1;
			else if ( soundData.BitsPerSample == 16 ) format = 0;
		}
		else if ( soundData.Format == 2 )
		{
			format = 3;
		}

		var loopStart = options.LoopStart >= 0 ? options.LoopStart : (options.Loop ? 0 : -1);
		var loopEnd = options.LoopEnd;

		return Create( filename, pcmData, soundData.Channels, soundData.SampleRate, format, soundData.SampleCount, soundData.Duration, loopStart, loopEnd );
	}

	/// <summary>
	/// Load from MP3.
	/// </summary>
	/// <param name="filename">Sound name</param>
	/// <param name="data">MP3 file data</param>
	/// <param name="options">Loop options</param>
	public static unsafe SoundFile FromMp3( string filename, Span<byte> data, LoadOptions options = default )
	{
		ThreadSafe.AssertIsMainThread( "SoundFile.FromMp3" );

		if ( !filename.EndsWith( ".vsnd", StringComparison.OrdinalIgnoreCase ) )
			filename = System.IO.Path.ChangeExtension( filename, "vsnd" );

		if ( Loaded.TryGetValue( filename, out var soundFile ) )
			return soundFile;

		if ( data.Length <= 0 )
			throw new ArgumentException( "Invalid data" );

		var soundData = SoundData.FromMP3( data );
		var pcmData = soundData.PCMData ?? throw new ArgumentException( "Invalid MP3" );

		var format = 0;
		if ( soundData.Format == 1 )
		{
			if ( soundData.BitsPerSample == 8 ) format = 1;
			else if ( soundData.BitsPerSample == 16 ) format = 0;
		}
		else if ( soundData.Format == 3 )
		{
			format = 3;
		}

		var loopStart = options.LoopStart >= 0 ? options.LoopStart : (options.Loop ? 0 : -1);
		var loopEnd = options.LoopEnd;

		return Create( filename, pcmData, soundData.Channels, soundData.SampleRate, format, soundData.SampleCount, soundData.Duration, loopStart, loopEnd );
	}

	/// <summary>
	/// Load sound from OGG vorbis.
	/// </summary>
	/// <param name="filename">Sound name</param>
	/// <param name="data">OGG file data</param>
	/// <param name="options">Loop options</param>
	public static unsafe SoundFile FromOgg( string filename, Span<byte> data, LoadOptions options = default )
	{
		ThreadSafe.AssertIsMainThread( "SoundFile.FromOgg" );

		if ( !filename.EndsWith( ".vsnd", StringComparison.OrdinalIgnoreCase ) )
			filename = System.IO.Path.ChangeExtension( filename, "vsnd" );

		if ( Loaded.TryGetValue( filename, out var soundFile ) )
			return soundFile;

		if ( data.Length <= 0 )
			throw new ArgumentException( "Invalid data" );

		var soundData = SoundData.FromOGG( data );
		var pcmData = soundData.PCMData ?? throw new ArgumentException( "Invalid OGG file" );

		var format = 0;
		if ( soundData.Format == 1 )
		{
			if ( soundData.BitsPerSample == 8 ) format = 1;
			else if ( soundData.BitsPerSample == 16 ) format = 0;
		}
		else if ( soundData.Format == 3 )
		{
			format = 3;
		}

		var loopStart = options.LoopStart >= 0 ? options.LoopStart : (options.Loop ? 0 : -1);
		var loopEnd = options.LoopEnd;

		return Create( filename, pcmData, soundData.Channels, soundData.SampleRate, format, soundData.SampleCount, soundData.Duration, loopStart, loopEnd );
	}

	[Obsolete( $"Use {nameof( FromPcm )}( filename, data, {nameof( PcmOptions )} )" )]
	public static SoundFile FromPcm( string filename, Span<byte> data, int channels, uint rate, int bits, bool loop )
		=> FromPcm( filename, data, new PcmOptions { Channels = channels, Rate = rate, Bits = bits, Loop = loop } );

	[Obsolete( $"Use {nameof( FromPcm )}( filename, data, {nameof( PcmOptions )} )" )]
	public static SoundFile FromPcm( string filename, Span<byte> data, int channels, uint rate, int bits, int loopStart, int loopEnd )
		=> FromPcm( filename, data, new PcmOptions { Channels = channels, Rate = rate, Bits = bits, LoopStart = loopStart, LoopEnd = loopEnd } );

	[Obsolete( $"Use {nameof( FromWav )}( filename, data, {nameof( LoadOptions )} )" )]
	public static SoundFile FromWav( string filename, Span<byte> data, bool loop )
		=> FromWav( filename, data, new LoadOptions { Loop = loop } );

	[Obsolete( $"Use {nameof( FromWav )}( filename, data, {nameof( LoadOptions )} )" )]
	public static SoundFile FromWav( string filename, Span<byte> data, int loopStart, int loopEnd )
		=> FromWav( filename, data, new LoadOptions { LoopStart = loopStart, LoopEnd = loopEnd } );

	[Obsolete( $"Use {nameof( FromMp3 )}( filename, data, {nameof( LoadOptions )} )" )]
	public static SoundFile FromMp3( string filename, Span<byte> data, bool loop )
		=> FromMp3( filename, data, new LoadOptions { Loop = loop } );

	[Obsolete( $"Use {nameof( FromMp3 )}( filename, data, {nameof( LoadOptions )} )" )]
	public static SoundFile FromMp3( string filename, Span<byte> data, int loopStart, int loopEnd )
		=> FromMp3( filename, data, new LoadOptions { LoopStart = loopStart, LoopEnd = loopEnd } );

	// this is a fucking mess

	// TODO: Document. What's the difference beetween preloading here and precaching in Load()? What does this do that Load() doesn't?
	public async Task<bool> LoadAsync()
	{
		if ( native.IsNull ) return false;
		if ( sound.IsValid ) return true;

		g_pSoundSystem.PreloadSound( native );

		RealTimeSince timeout = 0;

		// We need to wait until the sound has loaded
		while ( !native.IsValidForPlayback() )
		{

			await Task.Yield();
			if ( !native.IsValid ) return false;

			if ( timeout > 3 )
			{
				return false;
			}
		}

		sound = native.GetSound();

		return true;
	}

	public void Preload()
	{
		if ( native.IsNull ) return;
		if ( sound.IsValid ) return;

		g_pSoundSystem.PreloadSound( native );

		sound = native.GetSound();
	}

	internal enum CacheStatus
	{
		NotLoaded = 0,
		IsLoaded,
		ErrorLoading,
	};

	/// <summary>
	/// Request decompressed audio samples.
	/// </summary>
	public async Task<short[]> GetSamplesAsync()
	{
		if ( native.IsNull )
			return null;

		g_pSoundSystem.PreloadSound( native );

		RealTimeSince timeout = 0;

		// We need to wait until the sound has loaded before trying to load the source
		while ( !native.IsValidForPlayback() )
		{
			await Task.Yield();

			if ( timeout > 10 )
			{
				return null;
			}
		}

		timeout = 0;

		using ( var mixer = native.CreateMixer() )
		{
			// Failed to create mixer -> bail to prevent native crash
			if ( mixer.IsNull ) return null;

			while ( !mixer.IsReadyToMix() )
			{
				await Task.Yield();

				if ( timeout > 10 )
				{
					return null;
				}

				continue;
			}


			return GetSamples();
		}
	}

	unsafe short[] GetSamples()
	{
		int sampleCount = native.GetSampleCount();
		if ( sampleCount == 0 )
		{
			return null;
		}

		// TODO: do something better than allocating an array each time?
		var samples = new short[sampleCount];

		fixed ( short* memory = &samples[0] )
		{
			if ( !native.GetSamples( (IntPtr)memory, (uint)sampleCount ) )
				return null;
		}

		return samples;
	}
}
