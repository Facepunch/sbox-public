using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#nullable enable

namespace Sandbox;

/// <summary>
/// Write and read bytes to a stream. This aims to be as allocation free as possible while also being as fast as possible.
/// </summary>
public unsafe ref struct ByteStream
{
	byte[]? writeData;
	ReadOnlySpan<byte> readSpan;

	int position;
	int usedSize;

	/// <summary>
	/// Is this stream writable?
	/// </summary>
	public readonly bool Writable => writeData is not null;

	/// <summary>
	/// The current read or write position. Values are clamped to valid range.
	/// </summary>
	public int Position
	{
		readonly get => position;

		set
		{
			// Clamp to valid range, prevent negative or out-of-bounds
			if ( value < 0 ) position = 0;
			else if ( value > usedSize ) position = usedSize;
			else position = value;
		}
	}

	internal readonly int BufferSize => writeData?.Length ?? usedSize;

	/// <summary>
	/// The total size of the data
	/// </summary>
	public readonly int Length => usedSize;

	internal ByteStream( int size )
	{
		if ( size <= 0 ) throw new ArgumentOutOfRangeException( nameof( size ), $"Size must be larger than 0" );

		writeData = ArrayPool<byte>.Shared.Rent( size );
		position = 0;
	}

	internal ByteStream( ReadOnlySpan<byte> data )
	{
		readSpan = data;
		usedSize = data.Length;
		position = 0;
	}

	internal ByteStream( void* data, int datasize )
		: this( datasize <= 0 ? throw new ArgumentOutOfRangeException( nameof( datasize ) ) : new ReadOnlySpan<byte>( data, datasize ) )
	{
	}

	/// <summary>
	/// Create a writable byte stream
	/// </summary>
	public static ByteStream Create( int size )
	{
		if ( size <= 0 ) throw new ArgumentOutOfRangeException( nameof( size ) );

		return new ByteStream( size );
	}

	/// <summary>
	/// Create a reader byte stream
	/// </summary>
	public static ByteStream CreateReader( ReadOnlySpan<byte> data )
	{
		return new ByteStream( data );
	}

	/// <summary>
	/// Create a reader byte stream
	/// </summary>
	internal static ByteStream CreateReader( void* data, int size )
	{
		return new ByteStream( new ReadOnlySpan<byte>( data, size ) );
	}

	public void Dispose()
	{
		if ( writeData is not null ) ArrayPool<byte>.Shared.Return( writeData );
		writeData = null;
		readSpan = default;
		position = default;
		usedSize = default;
	}

	/// <summary>
	/// Ensures buffer can accommodate write with overflow protection
	/// </summary>
	public void EnsureCanWrite( int size )
	{
		if ( writeData is null )
			throw new InvalidOperationException( "Cannot write to read-only stream" );

		if ( size < 0 ) throw new ArgumentOutOfRangeException( nameof( size ), "Invalid write size" );

		long required = (long)position + size;
		if ( required > int.MaxValue )
			throw new OutOfMemoryException( "Requested size exceeds maximum supported buffer size." );

		if ( required > writeData.Length )
		{
			long newSize = writeData.Length;
			// Grow geometrically 
			while ( newSize < required )
			{
				if ( newSize >= int.MaxValue / 2 )
				{
					newSize = required;
					break;
				}
				newSize *= 2;
			}

			var newBuffer = ArrayPool<byte>.Shared.Rent( (int)newSize );
			Array.Copy( writeData, newBuffer, usedSize );
			ArrayPool<byte>.Shared.Return( writeData );
			writeData = newBuffer;
		}
	}

	public int ReadRemaining
	{

		get
		{
			var remaining = usedSize - position;
			return remaining < 0 ? 0 : remaining;
		}
	}

	/// <summary>
	/// Validates read bounds with overflow protection
	/// </summary>
	public readonly void EnsureCanRead( int size )
	{
		if ( size < 0 )
			throw new ArgumentOutOfRangeException( nameof( size ), "Cannot read negative size" );

		// Uses checked arithmetic to prevent overflow
		checked
		{
			int required = position + size;
			if ( required > usedSize || required < 0 )
				throw new IndexOutOfRangeException( $"Read would exceed buffer bounds (pos:{position}, size:{size}, buffer:{BufferSize})" );
		}
	}

	/// <summary>
	/// Writes an array of unmanaged types
	/// </summary>
	public void WriteArray<T>( ReadOnlySpan<T> arr ) where T : unmanaged
	{
		if ( !SandboxedUnsafe.IsAcceptablePod<T>() )
			throw new InvalidOperationException( "Type must be unmanaged" );

		// Write length first
		Write( arr.Length );

		if ( arr.Length == 0 ) return;

		Write( arr );
	}

	/// <summary>
	/// Writes an array of unmanaged types
	/// </summary>
	public void WriteArray<T>( T[]? arr, bool includeCount = true ) where T : unmanaged
	{
		if ( arr == null )
		{
			if ( includeCount )
				Write( -1 );
			return;
		}

		if ( !SandboxedUnsafe.IsAcceptablePod<T>() )
			throw new InvalidOperationException( "Type must be unmanaged" );

		if ( includeCount )
			Write( arr.Length );

		if ( arr.Length == 0 ) return;

		Write( arr.AsSpan() );
	}

	public void Write( ByteStream stream )
	{
		// dont try to copy to self!
		if ( writeData is not null && stream.writeData == writeData )
			return;

		var len = stream.usedSize;
		if ( len == 0 ) return;

		Write( stream.ToSpan() );
	}

	internal void Write<T>( ReadOnlySpan<T> rawData ) where T : unmanaged
	{
		if ( !SandboxedUnsafe.IsAcceptablePod<T>() )
			throw new InvalidOperationException( "Type must be unmanaged" );

		checked
		{
			var bytesSize = rawData.Length * sizeof( T );
			if ( bytesSize == 0 ) return;

			EnsureCanWrite( bytesSize );

			// Copy via refs to avoid per-call pinning without incurring extra span conversions
			ref byte dstRef = ref MemoryMarshal.GetArrayDataReference( writeData! );
			ref byte dst = ref Unsafe.AddByteOffset( ref dstRef, (IntPtr)position );
			ref T srcRef = ref MemoryMarshal.GetReference( rawData );
			ref byte src = ref Unsafe.As<T, byte>( ref srcRef );
			Unsafe.CopyBlockUnaligned( ref dst, ref src, (uint)bytesSize );

			position += bytesSize;
			if ( position > usedSize ) usedSize = position;
		}
	}

	public void Write( byte[] rawData )
	{
		if ( rawData == null )
			throw new ArgumentNullException( nameof( rawData ) );

		Write( rawData.AsSpan() );
	}

	public void Write( byte[] rawData, int offset, int bytes )
	{
		if ( rawData == null ) throw new ArgumentNullException( nameof( rawData ) );
		if ( bytes < 0 ) throw new ArgumentOutOfRangeException( nameof( bytes ), "Cannot write negative bytes" );
		if ( offset < 0 ) throw new ArgumentOutOfRangeException( nameof( offset ), "Offset cannot be negative" );

		// Checks for overflow in offset + bytes
		checked
		{
			int endPos = offset + bytes;
			if ( endPos > rawData.Length ) throw new ArgumentOutOfRangeException( nameof( bytes ), "Offset + bytes exceeds array length" );
		}

		if ( bytes == 0 ) return;

		Write( rawData.AsSpan().Slice( offset, bytes ) );
	}

	public void Write( ByteStream stream, int offset, int maxSize )
	{
		// dont try to copy to self!
		if ( writeData is not null && stream.writeData == writeData )
			return;

		if ( offset < 0 ) throw new ArgumentOutOfRangeException( nameof( offset ), "Offset cannot be negative" );
		if ( maxSize < 0 ) throw new ArgumentOutOfRangeException( nameof( maxSize ), "Size cannot be negative" );
		if ( maxSize == 0 ) return;

		// Checks offset bounds and calculates safe copy length
		if ( offset > stream.usedSize ) throw new ArgumentOutOfRangeException( nameof( offset ), "Offset exceeds stream length" );
		int maxReadLeft = stream.usedSize - offset;
		int len = maxSize > maxReadLeft ? maxReadLeft : maxSize;
		if ( len <= 0 ) return;

		Write( stream.ToSpan().Slice( offset, len ) );
	}

	/// <summary>
	/// Writes a string
	/// </summary>
	public void Write( string? str )
	{
		if ( str == null )
		{
			Write( -1 );
			return;
		}

		if ( str.Length == 0 )
		{
			Write( 0 );
			return;
		}

		var dataLen = System.Text.Encoding.UTF8.GetByteCount( str );

		Write( dataLen );

		EnsureCanWrite( dataLen );

		var dst = writeData!.AsSpan( position, dataLen );
		System.Text.Encoding.UTF8.GetBytes( str, dst );

		position += dataLen;
		if ( position > usedSize ) usedSize = position;
	}

	/// <summary>
	/// Get the data as an array of bytes
	/// </summary>
	public readonly byte[] ToArray()
	{
		return ToSpan().ToArray();
	}

	/// <summary>
	/// Get the data as a span. Note this can't be kept around after disposing the ByteStream
	/// </summary>
	internal readonly ReadOnlySpan<byte> ToSpan()
	{
		return writeData is not null
			? writeData.AsSpan( 0, usedSize )
			: readSpan;
	}

	/// <summary>
	/// Writes an unmanaged type
	/// </summary>
	public void Write<T>( T value ) where T : unmanaged
	{
		if ( !SandboxedUnsafe.IsAcceptablePod<T>() )
			throw new InvalidOperationException( "Type must be unmanaged" );

		var size = sizeof( T );

		EnsureCanWrite( size );

		ref byte dstRef = ref MemoryMarshal.GetArrayDataReference( writeData! );
		ref byte target = ref Unsafe.AddByteOffset( ref dstRef, (IntPtr)position );
		Unsafe.WriteUnaligned( ref target, value );

		position += size;
		if ( position > usedSize ) usedSize = position;
	}

	/// <summary>
	/// Reads an unmanaged type
	/// </summary>
	public T Read<T>() where T : unmanaged
	{
		if ( !SandboxedUnsafe.IsAcceptablePod<T>() )
			throw new InvalidOperationException( "Type must be unmanaged" );

		var size = sizeof( T );

		// Uses checked arithmetic
		checked
		{
			int newPos = position + size;

			if ( newPos > usedSize || newPos < 0 )
				throw new IndexOutOfRangeException( $"Failed to read {typeof( T )} (pos:{position}, size:{size}, buffer:{BufferSize})" );

			T value;
			if ( writeData is not null )
			{
				ref byte src = ref MemoryMarshal.GetArrayDataReference( writeData );
				value = Unsafe.ReadUnaligned<T>( ref Unsafe.AddByteOffset( ref src, (IntPtr)position ) );
			}
			else
			{
				ref byte src = ref MemoryMarshal.GetReference( readSpan );
				value = Unsafe.ReadUnaligned<T>( ref Unsafe.AddByteOffset( ref src, (IntPtr)position ) );
			}

			position = newPos;
			return value;
		}
	}

	/// <summary>
	/// Try to read variable, return false if not enough data
	/// </summary>
	public bool TryRead<T>( out T v ) where T : unmanaged
	{
		if ( !SandboxedUnsafe.IsAcceptablePod<T>() )
			throw new InvalidOperationException( "Type must be unmanaged" );

		v = default;

		var size = sizeof( T );
		var remaining = usedSize - position;

		if ( remaining < size || remaining < 0 )
			return false;

		// Additional overflow check
		checked
		{
			int newPos = position + size;
			if ( newPos > usedSize || newPos < 0 )
				return false;

			if ( writeData is not null )
			{
				ref byte src = ref MemoryMarshal.GetArrayDataReference( writeData );
				v = Unsafe.ReadUnaligned<T>( ref Unsafe.AddByteOffset( ref src, (IntPtr)position ) );
			}
			else
			{
				ref byte src = ref MemoryMarshal.GetReference( readSpan );
				v = Unsafe.ReadUnaligned<T>( ref Unsafe.AddByteOffset( ref src, (IntPtr)position ) );
			}

			position = newPos;
			return true;
		}
	}

	/// <summary>
	/// Returns an array of unmanaged types
	/// </summary>
	public T[] ReadArray<T>( int maxElements = int.MaxValue / 2 ) where T : unmanaged
	{
		return ReadArraySpan<T>( maxElements ).ToArray();
	}

	/// <summary>
	/// Non allocating read of an array of unmanaged types
	/// </summary>
	internal ReadOnlySpan<T> ReadArraySpan<T>( int maxElements = int.MaxValue / 2 ) where T : unmanaged
	{
		if ( !SandboxedUnsafe.IsAcceptablePod<T>() )
			throw new ArgumentOutOfRangeException( nameof( T ), "Type not acceptable" );

		if ( maxElements <= 0 )
			throw new ArgumentOutOfRangeException( nameof( maxElements ), "Invalid max elements" );

		var len = Read<int>();

		// Validates array length
		if ( len < 0 )
			throw new InvalidOperationException( "Array length cannot be negative" );

		if ( len == 0 )
			return default;

		if ( len > maxElements )
			throw new IndexOutOfRangeException( $"Array length {len} exceeds maximum {maxElements}" );

		int elementSize = sizeof( T );
		if ( len > int.MaxValue / elementSize )
			throw new IndexOutOfRangeException( $"Array length {len} is too large for element size {elementSize}" );

		// Checks for multiplication overflow
		checked
		{
			int dataSize = elementSize * len;

			int newPos = position + dataSize;

			if ( newPos > usedSize || newPos < 0 )
				throw new IndexOutOfRangeException( $"Array read exceeds buffer (pos:{position}, size:{dataSize}, buffer:{BufferSize})" );

			ReadOnlySpan<byte> byteSpan;
			if ( writeData is not null )
			{
				byteSpan = writeData.AsSpan( position, dataSize );
			}
			else
			{
				byteSpan = readSpan.Slice( position, dataSize );
			}

			position = newPos;
			return MemoryMarshal.Cast<byte, T>( byteSpan );
		}
	}

	public string? Read<T>( string defaultValue = "" ) where T : IEquatable<string>
	{
		int datasize = Read<int>();

		// Validates size
		if ( datasize == -1 ) return null;
		if ( datasize == 0 ) return string.Empty;

		if ( datasize < 0 )
			throw new InvalidOperationException( "String size cannot be negative" );

		checked
		{
			int newPos = position + datasize;

			if ( newPos > usedSize || newPos < 0 )
				throw new IndexOutOfRangeException( $"String read exceeds buffer (pos:{position}, size:{datasize}, buffer:{BufferSize})" );

			ReadOnlySpan<byte> bytes;
			if ( writeData is not null )
			{
				bytes = writeData.AsSpan( position, datasize );
			}
			else
			{
				bytes = readSpan.Slice( position, datasize );
			}

			var str = System.Text.Encoding.UTF8.GetString( bytes );

			position = newPos;
			return str;
		}
	}

	public void Write<T>( T data, bool unused = false ) where T : IByteParsable
	{
		T.WriteObject( ref this, data );
	}

	public T Read<T>( T? defaultValue = default, bool unused = false ) where T : IByteParsable
	{
		return (T)T.ReadObject( ref this );
	}

	public object? ReadObject( Type objectType )
	{
		if ( objectType == typeof( byte ) ) return Read<byte>();
		if ( objectType == typeof( ushort ) ) return Read<ushort>();
		if ( objectType == typeof( short ) ) return Read<short>();
		if ( objectType == typeof( uint ) ) return Read<uint>();
		if ( objectType == typeof( int ) ) return Read<int>();
		if ( objectType == typeof( ulong ) ) return Read<ulong>();
		if ( objectType == typeof( long ) ) return Read<long>();
		if ( objectType == typeof( float ) ) return Read<float>();
		if ( objectType == typeof( double ) ) return Read<double>();
		if ( objectType == typeof( string ) ) return Read<string>();
		if ( objectType == typeof( Vector3 ) ) return Read<Vector3>();
		if ( objectType == typeof( Rotation ) ) return Read<Rotation>();
		if ( objectType == typeof( Angles ) ) return Read<Angles>();
		if ( objectType == typeof( Transform ) ) return Read<Transform>();

		throw new NotImplementedException( $"ReadObject doesn't support {objectType} - add support if it makes sense!" );
	}

	/// <summary>
	/// Read a block of data. Note - this can't be made public as it could lead to unsafe usage.
	/// </summary>
	internal ByteStream ReadByteStream( int size )
	{
		if ( size < 0 )
			throw new ArgumentOutOfRangeException( nameof( size ), "Size cannot be negative" );

		checked
		{
			int newPos = position + size;

			if ( newPos > usedSize || newPos < 0 )
				throw new IndexOutOfRangeException( $"Read exceeds buffer (pos:{position}, size:{size}, buffer:{BufferSize})" );

			ReadOnlySpan<byte> span;
			if ( writeData is not null )
			{
				span = writeData.AsSpan( position, size );
			}
			else
			{
				span = readSpan.Slice( position, size );
			}

			position = newPos;
			return CreateReader( span );
		}
	}

	/// <summary>
	/// Note never make public - as the span could point to disposed memory!
	/// </summary>
	internal ReadOnlySpan<byte> GetRemainingBytes()
	{
		var remaining = usedSize - position;
		if ( remaining <= 0 ) return default;

		if ( writeData is not null )
		{
			var span = writeData.AsSpan( position, remaining );
			position = usedSize;
			return span;
		}
		else
		{
			var span = readSpan.Slice( position, remaining );
			position = usedSize;
			return span;
		}
	}

	/// <summary>
	/// Write an Array, that we know is a Value array. We definitely know it's a value array.
	/// We're not exposing this to the public api because they don't know whether it's a value array.
	/// </summary>
	internal void WriteValueArray( Array array )
	{
		if ( array == null ) throw new ArgumentNullException( nameof( array ) );

		if ( array.Length == 0 ) return;

		var elementType = array.GetType().GetElementType()!;
		if ( !elementType.IsValueType )
			throw new InvalidOperationException( $"{elementType} isn't a value type!" );

		var elementSize = elementType.GetManagedSize();
		if ( elementSize <= 0 )
			throw new InvalidOperationException( $"Couldn't get size of {elementType}" );

		// Checks for multiplication overflow
		checked
		{
			int bytes = elementSize * array.Length;

			EnsureCanWrite( bytes );

			var handle = GCHandle.Alloc( array, GCHandleType.Pinned );

			try
			{
				var src = (void*)handle.AddrOfPinnedObject();
				var srcSpan = new ReadOnlySpan<byte>( src, bytes );
				srcSpan.CopyTo( writeData!.AsSpan( position, bytes ) );
			}
			finally
			{
				handle.Free();
			}

			position += bytes;
			if ( position > usedSize ) usedSize = position;
		}
	}

	/// <summary>
	/// Read an Array, that we know is a Value array. We definitely know it's a value array.
	/// We're not exposing this to the public api because they don't know whether it's a value array.
	/// </summary>
	internal void ReadValueArray( Array array )
	{
		if ( array == null ) throw new ArgumentNullException( nameof( array ) );

		if ( array.Length == 0 ) return;

		var elementType = array.GetType().GetElementType()!;
		if ( !elementType.IsValueType )
			throw new InvalidOperationException( $"{elementType} isn't a value type!" );

		var elementSize = elementType.GetManagedSize();
		if ( elementSize <= 0 )
			throw new InvalidOperationException( $"Couldn't get size of {elementType}" );

		// Checks for multiplication overflow
		checked
		{
			int bytes = elementSize * array.Length;

			int newPos = position + bytes;

			if ( newPos > usedSize || newPos < 0 )
				throw new IndexOutOfRangeException( $"Read exceeds buffer (pos:{position}, size:{bytes}, buffer:{BufferSize})" );

			var handle = GCHandle.Alloc( array, GCHandleType.Pinned );

			try
			{
				var dst = (void*)handle.AddrOfPinnedObject();

				var dstSpan = new Span<byte>( dst, bytes );
				if ( writeData is not null )
				{
					writeData.AsSpan( position, bytes ).CopyTo( dstSpan );
				}
				else
				{
					readSpan.Slice( position, bytes ).CopyTo( dstSpan );
				}
			}
			finally
			{
				handle.Free();
			}

			position = newPos;
		}
	}

	/// <inheritdoc cref="System.IO.Stream.Read(byte[],int,int)"/>
	public int Read( byte[] buffer, int offset, int count )
	{
		if ( buffer == null )
			throw new ArgumentNullException( nameof( buffer ) );

		return Read( buffer.AsSpan( offset, count ) );
	}

	/// <inheritdoc cref="System.IO.Stream.Read(Span{byte})"/>
	public int Read( Span<byte> buffer )
	{
		var remaining = usedSize - position;
		if ( remaining < 0 ) remaining = 0;

		var length = buffer.Length < remaining ? buffer.Length : remaining;

		if ( length <= 0 ) return 0;

		if ( writeData is not null )
		{
			var src = writeData.AsSpan( position, length );
			src.CopyTo( buffer );
		}
		else
		{
			readSpan.Slice( position, length ).CopyTo( buffer );
		}

		position += length;
		return length;
	}

	public ByteStream Compress( CompressionLevel compressionLevel = CompressionLevel.Optimal )
	{
		// Work directly with the span to avoid ToArray() allocation
		var data = ToSpan();

		// Pre-allocate with reasonable estimate (GZip typically 40-50% of original size + overhead)
		using var compressedStream = new MemoryStream( data.Length / 2 + 128 );
		using ( var gzipStream = new GZipStream( compressedStream, compressionLevel, leaveOpen: true ) )
		{
			gzipStream.Write( data );
		}

		return CreateReader( compressedStream.ToArray() );
	}

	public ByteStream Decompress()
	{
		using var compressedStream = new MemoryStream( ToArray() );
		using var decompressedStream = new MemoryStream();
		using var gzipStream = new GZipStream( compressedStream, CompressionMode.Decompress );

		gzipStream.CopyTo( decompressedStream );

		return CreateReader( decompressedStream.ToArray() );
	}
}
