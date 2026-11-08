using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;

namespace Sandbox.Network;

/// <summary>
/// An encoded message on its way to one or more connections. Its buffer is pooled: whoever has it releases it, and a
/// transport that sends it later (on a worker thread) takes its own reference first and releases it once sent. A
/// packet that's never released is simply collected, releasing too early would hand its buffer to the next message.
/// </summary>
internal sealed class NetPacket
{
	static readonly ConcurrentQueue<NetPacket> Pool = new();

	byte[] _buffer;
	int _length;
	int _references;
	bool _pooledBuffer;

	public byte[] Buffer => _buffer;
	public int Length => _length;
	public ReadOnlySpan<byte> Span => _buffer.AsSpan( 0, _length );
	public ReadOnlyMemory<byte> Memory => _buffer.AsMemory( 0, _length );

	static NetPacket Get()
	{
		return Pool.TryDequeue( out var packet ) ? packet : new NetPacket();
	}

	/// <summary>
	/// A packet with a pooled buffer of at least <paramref name="capacity"/> bytes, see <see cref="SetLength"/>.
	/// </summary>
	public static NetPacket Rent( int capacity )
	{
		var packet = Get();
		packet._buffer = ArrayPool<byte>.Shared.Rent( Math.Max( 1, capacity ) );
		packet._length = capacity;
		packet._pooledBuffer = true;
		packet._references = 1;
		return packet;
	}

	/// <summary>
	/// A packet around an existing array, which isn't pooled.
	/// </summary>
	public static NetPacket Wrap( byte[] data )
	{
		var packet = Get();
		packet._buffer = data;
		packet._length = data.Length;
		packet._pooledBuffer = false;
		packet._references = 1;
		return packet;
	}

	public void SetLength( int length )
	{
		if ( length > _buffer.Length )
			throw new ArgumentOutOfRangeException( nameof( length ) );

		_length = length;
	}

	public void AddReference()
	{
		Interlocked.Increment( ref _references );
	}

	public void Release()
	{
		var references = Interlocked.Decrement( ref _references );

		if ( references > 0 )
			return;

		if ( references < 0 )
			throw new InvalidOperationException( "NetPacket released more often than it was referenced" );

		if ( _pooledBuffer )
			ArrayPool<byte>.Shared.Return( _buffer );

		_buffer = null;
		_length = 0;
		Pool.Enqueue( this );
	}
}
