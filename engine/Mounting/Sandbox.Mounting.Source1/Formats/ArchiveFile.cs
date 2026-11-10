using System;
using System.Buffers;
using System.IO;
using System.Threading;

namespace Sandbox.Mounting.Source1;

internal sealed class ArchiveFile( string path )
{
	public string Path { get; } = path;
	private readonly FileStream stream = new( path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 );
	private int references = 1;

	public long Length => stream.Length;
	private int Read( Span<byte> buffer, long offset ) => RandomAccess.Read( stream.SafeFileHandle, buffer, offset );
	private void Retain() => Interlocked.Increment( ref references );

	public void Release()
	{
		if ( Interlocked.Decrement( ref references ) == 0 )
			stream.Dispose();
	}

	public Stream OpenRead( long offset, long length, ReadOnlyMemory<byte> preload = default )
	{
		var archiveLength = Length;
		if ( offset < 0 || length < 0 || offset > archiveLength || length > archiveLength - offset )
			throw new InvalidDataException( $"Entry extends beyond archive '{Path}'." );
		return new ArchiveStream( this, offset, length, preload );
	}

	public static Stream OpenPreload( ReadOnlyMemory<byte> preload ) => new PreloadStream( preload );

	public static Stream OpenSlice( Stream stream, long offset, long length )
	{
		if ( !stream.CanRead || !stream.CanSeek )
			throw new ArgumentException( "Archive slices require a readable, seekable stream.", nameof( stream ) );
		if ( offset < 0 || length < 0 || offset > stream.Length || length > stream.Length - offset )
			throw new InvalidDataException( "Archive slice extends beyond its source stream." );
		return new SliceStream( stream, offset, length );
	}

	private abstract class EntryStream( long length ) : Stream
	{
		protected readonly long _length = length;
		protected long _position;
		protected bool _disposed;

		public abstract override int Read( Span<byte> buffer );
		public abstract override int ReadByte();

		public override int Read( byte[] buffer, int offset, int count )
		{
			ArgumentNullException.ThrowIfNull( buffer );
			return Read( buffer.AsSpan( offset, count ) );
		}

		public override long Seek( long offset, SeekOrigin origin )
		{
			ObjectDisposedException.ThrowIf( _disposed, this );
			var position = origin switch
			{
				SeekOrigin.Begin => offset,
				SeekOrigin.Current => checked(_position + offset),
				SeekOrigin.End => checked(_length + offset),
				_ => throw new ArgumentOutOfRangeException( nameof( origin ) )
			};
			if ( position < 0 )
				throw new IOException( "Cannot seek before the start of an archive entry." );
			return _position = position;
		}

		public override long Position
		{
			get { ObjectDisposedException.ThrowIf( _disposed, this ); return _position; }
			set
			{
				ArgumentOutOfRangeException.ThrowIfNegative( value );
				Seek( value, SeekOrigin.Begin );
			}
		}
		public override long Length { get { ObjectDisposedException.ThrowIf( _disposed, this ); return _length; } }
		public override bool CanRead => !_disposed;
		public override bool CanSeek => !_disposed;
		public override bool CanWrite => false;
		public override void Flush() => ObjectDisposedException.ThrowIf( _disposed, this );
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();

		protected override void Dispose( bool disposing )
		{
			if ( disposing && !_disposed )
				ReleaseResources();
			_disposed = true;
			base.Dispose( disposing );
		}

		protected virtual void ReleaseResources() { }
	}

	private sealed class PreloadStream( ReadOnlyMemory<byte> preload ) : EntryStream( preload.Length )
	{
		public override int Read( Span<byte> buffer )
		{
			ObjectDisposedException.ThrowIf( _disposed, this );
			if ( _position >= _length )
				return 0;
			var count = (int)Math.Min( buffer.Length, _length - _position );
			preload.Span.Slice( (int)_position, count ).CopyTo( buffer );
			_position += count;
			return count;
		}

		public override int ReadByte()
		{
			ObjectDisposedException.ThrowIf( _disposed, this );
			return _position >= _length ? -1 : preload.Span[(int)_position++];
		}
	}

	private sealed class SliceStream( Stream stream, long offset, long length ) : EntryStream( length )
	{
		public override int Read( Span<byte> buffer )
		{
			ObjectDisposedException.ThrowIf( _disposed, this );
			if ( _position >= _length || buffer.IsEmpty )
				return 0;
			stream.Position = offset + _position;
			var count = stream.Read( buffer[..(int)Math.Min( buffer.Length, _length - _position )] );
			_position += count;
			return count;
		}

		public override int ReadByte()
		{
			Span<byte> value = stackalloc byte[1];
			return Read( value ) == 0 ? -1 : value[0];
		}

		protected override void ReleaseResources() => stream.Dispose();
	}

	private sealed class ArchiveStream : EntryStream
	{
		private const int BufferSize = 4096;
		private readonly ArchiveFile _archive;
		private readonly ReadOnlyMemory<byte> _preload;
		private readonly long _offset;
		private byte[] _buffer;
		private long _bufferStart = -1;
		private int _bufferCount;

		public ArchiveStream( ArchiveFile archive, long offset, long length, ReadOnlyMemory<byte> preload )
			: base( checked(length + preload.Length) )
		{
			_archive = archive;
			_preload = preload;
			_offset = offset;
			// Retired packages release their ownership, but existing streams keep the archive alive.
			archive.Retain();
		}

		public override int Read( Span<byte> buffer )
		{
			ObjectDisposedException.ThrowIf( _disposed, this );
			if ( _position >= _length || buffer.IsEmpty )
				return 0;
			buffer = buffer[..(int)Math.Min( buffer.Length, _length - _position )];
			var preloadRead = 0;
			if ( _position < _preload.Length )
			{
				preloadRead = (int)Math.Min( buffer.Length, _preload.Length - _position );
				_preload.Span.Slice( (int)_position, preloadRead ).CopyTo( buffer );
				_position += preloadRead;
				buffer = buffer[preloadRead..];
			}
			if ( buffer.IsEmpty )
				return preloadRead;

			var target = _offset + _position - _preload.Length;
			var available = target >= _bufferStart && target < _bufferStart + _bufferCount
				? (int)(_bufferStart + _bufferCount - target) : 0;
			int read;
			if ( available > 0 )
			{
				read = Math.Min( available, buffer.Length );
				_buffer.AsSpan( (int)(target - _bufferStart), read ).CopyTo( buffer );
			}
			else if ( buffer.Length >= BufferSize )
			{
				read = _archive.Read( buffer, target );
			}
			else
			{
				_buffer ??= ArrayPool<byte>.Shared.Rent( BufferSize );
				_bufferStart = target;
				_bufferCount = _archive.Read(
					_buffer.AsSpan( 0, (int)Math.Min( BufferSize, _length - _position ) ), target );
				read = Math.Min( _bufferCount, buffer.Length );
				_buffer.AsSpan( 0, read ).CopyTo( buffer );
			}
			if ( read == 0 )
				throw new EndOfStreamException( "Archive was truncated while reading an entry." );
			_position += read;
			return preloadRead + read;
		}

		public override int ReadByte()
		{
			ObjectDisposedException.ThrowIf( _disposed, this );
			if ( _position >= _length )
				return -1;
			if ( _position < _preload.Length )
				return _preload.Span[(int)_position++];

			var target = _offset + _position - _preload.Length;
			if ( target >= _bufferStart && target < _bufferStart + _bufferCount )
			{
				_position++;
				return _buffer[(int)(target - _bufferStart)];
			}
			Span<byte> value = stackalloc byte[1];
			return Read( value ) == 0 ? -1 : value[0];
		}

		protected override void ReleaseResources()
		{
			_archive.Release();
			if ( _buffer is not null )
			{
				ArrayPool<byte>.Shared.Return( _buffer );
				_buffer = null;
			}
		}
	}
}
