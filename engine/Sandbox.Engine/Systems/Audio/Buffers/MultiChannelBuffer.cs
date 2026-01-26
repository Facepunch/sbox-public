namespace Sandbox.Audio;

/// <summary>
/// Holds up to 8 mix buffers, which usually represent output speakers.
/// </summary>
public sealed class MultiChannelBuffer : IDisposable
{
	// 8 buffers, 512 floats each, about 16kb
	internal CAudioMixDeviceBuffers _native;
	internal PerChannel<MixBuffer> _buffers;

	/// <summary>
	/// How many channels do we have
	/// </summary>
	public int ChannelCount { get; }

	public MultiChannelBuffer( int channelCount = 8 )
	{
		Assert.True( channelCount > 0 );
		Assert.True( channelCount <= 8 );

		_native = CAudioMixDeviceBuffers.Create( channelCount );
		ChannelCount = channelCount;

		for ( int i = 0; i < ChannelCount; i++ )
		{
#pragma warning disable CA2000 // Dispose objects before losing scope
			// Dispose is handled in Dispose()
			_buffers.Set( new AudioChannel( i ), new MixBuffer( _native.GetBuffer( i ) ) );
#pragma warning restore CA2000 // Dispose objects before losing scope
		}
	}

	~MultiChannelBuffer()
	{
		Dispose();
	}

	/// <summary>
	/// Delete and release all resources. Cannot be used again.
	/// </summary>
	public void Dispose()
	{
		if ( _native.IsNull )
			return;

		GC.SuppressFinalize( this );

		for ( int i = 0; i < ChannelCount; i++ )
		{
			_buffers.Get( new AudioChannel( i ) ).ClearPointer();
		}

		_native.Destroy();
		_native = default;
	}

	/// <summary>
	/// Get MixBuffer number i
	/// </summary>
	public MixBuffer Get( AudioChannel i )
	{
		return _buffers.Get( i );
	}

	/// <summary>
	/// Get MixBuffer number i
	/// </summary>
	public MixBuffer Get( int i ) => Get( new AudioChannel( i ) );

	/// <summary>
	/// Silence all buffers
	/// </summary>
	public void Silence()
	{
		for ( int i = 0; i < ChannelCount; i++ )
		{
			_buffers.Get( new AudioChannel( i ) ).Silence();
		}
	}

	/// <summary>
	/// Set this buffer to this value 
	/// </summary>
	public void CopyFrom( MultiChannelBuffer other )
	{
		for ( int i = 0; i < ChannelCount && i < other.ChannelCount; i++ )
		{
			_buffers.Get( new AudioChannel( i ) ).CopyFrom( other._buffers.Get( new AudioChannel( i ) ) );
		}
	}

	/// <summary>
	/// Copies from one buffer to the other. If the other has less channels, we'll upmix
	/// </summary>
	public void CopyFromUpmix( MultiChannelBuffer other )
	{
		for ( int i = 0; i < ChannelCount; i++ )
		{
			var otherBuffer = other._buffers.Get( new AudioChannel( i % other.ChannelCount ) );

			_buffers.Get( new AudioChannel( i ) ).CopyFrom( otherBuffer );
		}
	}

	/// <summary>
	/// Mix the target buffer into this buffer
	/// </summary>
	public void MixFrom( MultiChannelBuffer samples, float mix )
	{
		for ( int i = 0; i < ChannelCount && i < samples.ChannelCount; i++ )
		{
			_buffers.Get( new AudioChannel( i ) ).MixFrom( samples._buffers.Get( new AudioChannel( i ) ), mix );
		}
	}

	/// <summary>
	/// Scale volume of this buffer
	/// </summary>
	public void Scale( float volume )
	{
		if ( volume <= 0 )
		{
			Silence();
			return;
		}

		if ( volume.AlmostEqual( 1 ) )
			return;

		for ( int i = 0; i < ChannelCount; i++ )
		{
			_buffers.Get( new AudioChannel( i ) ).Scale( volume );
		}
	}

	/// <summary>
	/// Send to device output
	/// </summary>
	internal void SendToOutput()
	{
		if ( !g_pAudioDevice.IsValid() )
			return;

		g_pAudioDevice.SendOutput( _native );

		// If we are recording a screencapture we also need to send the buffer to the movie recorder
		if ( ScreenRecorder.IsRecording() && g_pAudioDevice.BytesPerSample() == 4 && g_pAudioDevice.ChannelCount() == 2 )
		{
			ScreenRecorder.RecordAudioSample( _native );
		}
	}

	/// <summary>
	/// Mix each channel into the single buffer, using passed in volume
	/// </summary>
	internal void ToMono( MixBuffer monoTarget, float volume )
	{
		for ( int i = 0; i < ChannelCount; i++ )
		{
			monoTarget.MixFrom( _buffers.Get( new AudioChannel( i ) ), volume );
		}
	}
}
