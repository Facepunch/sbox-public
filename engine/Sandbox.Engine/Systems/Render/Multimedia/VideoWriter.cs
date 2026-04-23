using NativeEngine;
namespace Sandbox;

/// <summary>
/// Allows the creation of video content by encoding a sequence of frames.
/// </summary>
public sealed class VideoWriter : IDisposable
{
	public struct Config
	{
		// note: keeping file save path out of this
		// because we'll probably only want to expose that
		// carefully, and allow other write options

		public int Width;
		public int Height;
		public int FrameRate;
		public int Bitrate;
		public Codec Codec;
		public Container Container;
		public EncodingPreset Preset;
		public AudioCodec AudioCodec;
		public bool Transparency;

		/// <summary>
		/// Can this container support the codec.
		/// </summary>
		public bool IsCodecSupported()
		{
			// Validate codec support based on container
#pragma warning disable CS0618 // Intentional: H264/H265 are obsolete but still accepted for backward compat
			return Container switch
			{
				Container.MP4 => Codec is Codec.AV1 or Codec.VP9 or Codec.H264 or Codec.H265,
				Container.WebM => Codec is Codec.AV1 or Codec.VP9 or Codec.VP8,
				Container.WebP => Codec == Codec.WebP,
				_ => false,
			};
#pragma warning restore CS0618
		}

#pragma warning disable CS0618 // Intentional: obsolete codecs still map for backward compat
		internal string CodecName => Codec switch
		{
			Codec.VP8 or Codec.VP9 or Codec.H264 or Codec.H265 => "vp9",
			Codec.AV1 => "av1",
			Codec.WebP => "webp",
			_ => null,
		};
#pragma warning restore CS0618

		internal string AudioCodecName => AudioCodec switch
		{
			AudioCodec.Opus => "opus",
			_ => "opus",
		};

		internal string ContainerName => Container.ToString().ToLower();
	}

	[Expose]
	public enum Codec
	{
		/// <summary>
		/// Obsolete: H.264 is no longer supported, if used will map to VP9 instead.
		/// </summary>
		[Obsolete( "H.264 is no longer supported, use VP9 instead" )]
		H264,

		/// <summary>
		/// Obsolete: H.265 is no longer supported, if used will map to VP9 instead.
		/// </summary>
		[Obsolete( "H.265 is no longer supported, use VP9 instead" )]
		H265,

		/// <summary>
		/// Obsolete: VP8 is no longer supported, if used will map to VP9 instead.
		/// </summary>
		[Obsolete( "VP8 is no longer supported, use VP9 instead" )]
		VP8,

		/// <summary>
		/// VP9 codec (supports transparency)
		/// </summary>
		VP9,

		/// <summary>
		/// WebP codec (supports transparency)
		/// </summary>
		WebP,

		/// <summary>
		/// AV1 codec — excellent compression, slower encoding.
		/// </summary>
		AV1,
	}

	/// <summary>
	/// Controls the speed/quality tradeoff of video encoding.
	/// </summary>
	[Expose]
	public enum EncodingPreset
	{
		/// <summary>
		/// Optimized for speed. Suitable for real-time recording.
		/// </summary>
		Fast,

		/// <summary>
		/// Balanced speed and quality.
		/// </summary>
		Balanced,

		/// <summary>
		/// Optimized for quality. Suitable for offline export.
		/// </summary>
		Quality,
	}

	/// <summary>
	/// Audio codec to use for encoding.
	/// </summary>
	[Expose]
	public enum AudioCodec
	{
		/// <summary>
		/// Opus — high quality, open codec.
		/// </summary>
		Opus,

	}

	[Expose]
	public enum Container
	{
		/// <summary>
		/// MP4 container (does not support transparency)
		/// </summary>
		MP4,

		/// <summary>
		/// WebM container (supports transparency)
		/// </summary>
		WebM,

		/// <summary>
		/// WebP container (supports transparency)
		/// </summary>
		WebP,
	}

	private CVideoRecorder native;

	private readonly string path;
	private readonly int width;
	private readonly int height;
	private readonly int frameRate;
	private readonly int bitrate;

	public int Width => width;
	public int Height => height;

	internal VideoWriter( string path, Config config )
	{
		if ( !config.IsCodecSupported() )
			throw new ArgumentException( $"{config.Container} container does not support {config.Codec} codec" );

		this.path = path;

		width = config.Width;
		height = config.Height;
		frameRate = config.FrameRate > 0 ? config.FrameRate : 60;
		bitrate = config.Bitrate > 0 ? config.Bitrate : 8;

		var audioSampleRate = (int)Audio.AudioEngine.SamplingRate;
		var audioChannels = 2;

		native = CVideoRecorder.Create();
		native.Initialize( path, width, height, frameRate, bitrate, audioSampleRate, audioChannels, config.CodecName, (int)config.Preset, config.AudioCodecName, config.Transparency );
	}

	~VideoWriter()
	{
		MainThread.QueueDispose( this );
	}

	/// <summary>
	/// Dispose this recorder, the encoder will be flushed and video finalized.
	/// </summary>
	public void Dispose()
	{
		if ( native.IsValid )
		{
			native.Destroy();
			native = IntPtr.Zero;
		}

		GC.SuppressFinalize( this );
	}


	/// <summary>
	/// Finish creating this video. The encoder will be flushed and video finalized.
	/// </summary>
	public async Task FinishAsync()
	{
		if ( !native.IsValid )
			return;

		GC.SuppressFinalize( this );

		var n = native;
		native = IntPtr.Zero;

		await Task.Run( () => n.Destroy() );
	}


	/// <summary>
	/// Add a frame of data to be encoded. Timestamp is in microseconds. 
	/// If a timestamp is not specified, it will use an incremented 
	/// frame count as the timestamp.
	/// </summary>
	/// <param name="data">The frame data to be encoded.</param>
	/// <param name="timestamp">The timestamp for the frame in microseconds. If not specified, an incremented frame count will be used.</param>
	public unsafe bool AddFrame( ReadOnlySpan<byte> data, TimeSpan? timestamp = default )
	{
		if ( !native.IsValid )
			return false;

		long mcs = (long)(timestamp?.TotalMicroseconds ?? -1);

		if ( data.Length != (width * height * 4) )
			throw new ArgumentException( $"Invalid frame data" );

		fixed ( byte* dataPtr = data )
		{
			native.AddVideoFrame( (IntPtr)dataPtr, mcs );
		}

		return true;
	}

	/// <summary>
	/// Add a frame of data to be encoded. Timestamp is in microseconds. 
	/// If a timestamp is not specified, it will use an incremented 
	/// frame count as the timestamp.
	/// </summary>
	/// <param name="bitmap">The frame data to be encoded.</param>
	/// <param name="timestamp">The timestamp for the frame in microseconds. If not specified, an incremented frame count will be used.</param>
	public unsafe bool AddFrame( Bitmap bitmap, TimeSpan? timestamp = default )
	{
		return AddFrame( bitmap.GetBuffer(), timestamp );
	}

	/// <summary>
	/// Internal for now as I have no idea, how to expose audio recording in a good way yet.
	/// </summary>
	internal void AddAudioSamples( CAudioMixDeviceBuffers buffers )
	{
		native.AddAudioSamples( buffers );
	}
}
