namespace Sandbox.Audio;

/// <summary>
/// Contains 512 samples of audio data, this is used when mixing a single channel
/// </summary>
public sealed unsafe class MixBuffer : IDisposable
{
	// 512 floats, about 2kb
	internal CAudioMixBuffer _native;
	float* _buffer;
	const int bufferLengthBytes = 512 * sizeof( float );

	/// <summary>
	/// If true, this buffer pointer is managed by something else
	/// </summary>
	bool _external;

	/// <summary>
	/// This is locked from being disposed
	/// </summary>
	bool _locked;

	public MixBuffer()
	{
		_native = CAudioMixBuffer.Create();
		_buffer = (float*)_native.GetDataPointer();
	}

	internal MixBuffer( CAudioMixBuffer buffer )
	{
		_native = buffer;
		_buffer = (float*)_native.GetDataPointer();
		_external = true;
	}

	internal void ClearPointer()
	{
		_native = default;
		_buffer = default;
		GC.SuppressFinalize( this );
	}

	~MixBuffer()
	{
		Dispose();
	}

	public void Dispose()
	{
		if ( _locked )
		{
			throw new MethodAccessException( "MixBuffer is locked, can't be disposed" );
		}

		GC.SuppressFinalize( this );

		if ( !_external )
		{
			MainThread.QueueDispose( _native );
		}

		_native = default;
		_buffer = default;
	}

	/// <summary>
	/// Silence this buffer
	/// </summary>
	public void Silence()
	{
		Buffer.Clear();
	}

	//
	// The start of the floats
	//
	internal MixBufferLock DataPointer( out IntPtr ptr )
	{
		// Already locked - cannot lock twice!

		if ( _locked )
			throw new ArgumentException();

		_locked = true;
		ptr = _native.GetDataPointer();

		return new MixBufferLock( this );
	}

	internal ref struct MixBufferLock
	{
		private MixBuffer mixBuffer;

		public MixBufferLock( MixBuffer mixBuffer )
		{
			this.mixBuffer = mixBuffer;
		}

		public void Dispose()
		{
			mixBuffer._locked = false;
		}
	}

	/// <summary>
	/// Get direct access to the memory
	/// </summary>
	internal unsafe Span<float> Buffer => new Span<float>( _buffer, 512 );

	/// <summary>
	/// Set this buffer to this value 
	/// </summary>
	public void CopyFrom( MixBuffer other )
	{
		NativeLowLevel.Copy( (IntPtr)_buffer, (IntPtr)other._buffer, bufferLengthBytes );
	}

	/// <summary>
	/// Mix this buffer with another
	/// </summary>
	public void MixFrom( MixBuffer other, float scale )
	{
		if ( scale.AlmostEqual( 1.0f, 0.001f ) )
		{
			Buffer.AsFloatSpan().Add( other.Buffer );
			return;
		}

		Buffer.AsFloatSpan().AddScaled( other.Buffer, scale );
	}

	/// <summary>
	/// Mix this buffer with another
	/// </summary>
	public void MixFrom( MultiChannelBuffer other, float scale )
	{
		scale = scale / ((float)other.ChannelCount);

		for ( int i = 0; i < other.ChannelCount; i++ )
		{
			MixFrom( other.Get( i ), scale );
		}
	}

	/// <summary>
	/// Scale the buffer by volume
	/// </summary>
	public void Scale( float volume )
	{
		Buffer.AsFloatSpan().Scale( volume );
	}

	/// <summary>
	/// Clamp each sample to [-1, 1] to prevent digital clipping from multiple voices accumulating.
	/// </summary>
	public void HardLimit()
	{
		var span = Buffer;
		for ( var i = 0; i < span.Length; i++ )
		{
			if ( span[i] > 1.0f ) span[i] = 1.0f;
			else if ( span[i] < -1.0f ) span[i] = -1.0f;
		}
	}

	public float LevelMax => Buffer.AsFloatSpan().Max();
	public float LevelAvg => Buffer.AsFloatSpan().Average();

	public void RandomFill()
	{
		var span = Buffer;
		var rand = new Random();

		for ( int i = 0; i < span.Length; i++ )
		{
			span[i] = (float)rand.NextDouble() * 100f; // Random float values
		}
	}
}
