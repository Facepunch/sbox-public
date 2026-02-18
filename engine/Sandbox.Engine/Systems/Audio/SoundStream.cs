
namespace Sandbox;

public sealed partial class SoundStream : IHandle, IDisposable
{
	internal CAudioStreamManaged native;

	#region IHandle
	void IHandle.HandleInit( IntPtr ptr )
	{
		native = ptr;
	}

	void IHandle.HandleDestroy()
	{
		native = IntPtr.Zero;
	}

	bool IHandle.HandleValid() => !native.IsNull;

	#endregion

	/// <summary>
	/// Number of samples per second, as set during its creation.
	/// </summary>
	public int SampleRate { get; internal set; }

	/// <summary>
	/// Number of audio channels, as set during its creation.
	/// </summary>
	public int Channels { get; internal set; }

	public int QueuedSampleCount => native.IsValid ? (int)native.QueuedSampleCount() : 0;
	public int MaxWriteSampleCount => native.IsValid ? (int)native.MaxWriteSampleCount() : 0;
	public int LatencySamplesCount => native.IsValid ? (int)native.LatencySamplesCount() : 0;

	internal SoundStream() { }
	internal SoundStream( HandleCreationData _ ) { }

	public SoundStream( int sampleRate = 44100, int channels = 1 ) : this()
	{
		if ( Application.IsHeadless || Application.IsUnitTest )
			return;

		if ( channels <= 0 || channels > 16 )
			throw new ArgumentException( "Invalid number of channels" );

		if ( sampleRate < 1024 )
			throw new ArgumentException( "Invalid sample rate" );

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
#pragma warning disable CA2000 // Dispose objects before losing scope
			// The "created" stream links to this so we dont want to dispose it
			var stream = CAudioStreamManaged.Create( channels, (uint)sampleRate );
#pragma warning restore CA2000 // Dispose objects before losing scope
			stream.Channels = channels;
			stream.SampleRate = sampleRate;
		}
	}

	~SoundStream()
	{
		Dispose();
	}

	public unsafe void WriteData( Span<short> data )
	{
		if ( !native.IsValid )
			throw new ArgumentException( "Invalid sound stream" );

		if ( data.Length <= 0 )
			return;

		fixed ( short* data_ptr = data )
		{
			native.WriteAudioData( (IntPtr)data_ptr, (uint)(data.Length / Channels), (uint)Channels );
		}
	}

	public void Dispose()
	{
		if ( native.IsValid )
		{
			native.Destroy();
			native = IntPtr.Zero;
		}
	}

	/// <summary>
	/// Play sound of the stream.
	/// </summary>
	public SoundHandle Play( float volume = 1.0f, float pitch = 1.0f )
	{
		if ( !native.IsValid )
			return default;

		CSfxTable table = native.GetSfxTable();
		if ( table.IsNull )
			return default;

		return Sound.PlayFile( table, volume, pitch, 0, 0.0f, "Sound Stream" );
	}

	/// <summary>
	/// Play sound of the stream.
	/// </summary>
	[Obsolete( "Decibels are obsolete" )]
	public SoundHandle Play( float volume, float pitch, float decibels ) => Play( volume, pitch );
}
