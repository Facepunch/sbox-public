using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Borealis.Source2.Resources;

/// <summary>
/// Reads the little-endian, field-by-field layout <c>CUtlBuffer</c> writes.
/// </summary>
internal sealed class ByteReader( byte[] data, int position = 0, int end = -1 )
{
	readonly int end = end < 0 ? data.Length : end;

	public byte[] Data { get; } = data;
	public int Position { get; set; } = position;
	public int Remaining => end - Position;

	ReadOnlySpan<byte> Take( int count )
	{
		if ( count < 0 || Position + count > end ) throw new InvalidDataException( $"Read of {count} bytes at {Position} runs past {end}" );
		var span = Data.AsSpan( Position, count );
		Position += count;
		return span;
	}

	public byte Byte() => Take( 1 )[0];
	public sbyte SByte() => (sbyte)Take( 1 )[0];
	public bool Bool() => Take( 1 )[0] != 0;
	public short Short() => BinaryPrimitives.ReadInt16LittleEndian( Take( 2 ) );
	public ushort UShort() => BinaryPrimitives.ReadUInt16LittleEndian( Take( 2 ) );
	public int Int() => BinaryPrimitives.ReadInt32LittleEndian( Take( 4 ) );
	public uint UInt() => BinaryPrimitives.ReadUInt32LittleEndian( Take( 4 ) );
	public long Long() => BinaryPrimitives.ReadInt64LittleEndian( Take( 8 ) );
	public ulong ULong() => BinaryPrimitives.ReadUInt64LittleEndian( Take( 8 ) );
	public float Float() => BinaryPrimitives.ReadSingleLittleEndian( Take( 4 ) );
	public byte[] Bytes( int count ) => Take( count ).ToArray();
	public ReadOnlySpan<byte> Span( int count ) => Take( count );
	public void Skip( int count ) => Take( count );

	/// <summary>A count, with a sanity limit so a misread doesn't allocate the world.</summary>
	public int Count( int max )
	{
		var count = Int();
		if ( count < 0 || count > max ) throw new InvalidDataException( $"Count {count} at {Position - 4} out of range 0..{max}" );
		return count;
	}

	/// <summary>A fixed-size, null-padded string.</summary>
	public string Fixed( int size )
	{
		var span = Take( size );
		var length = span.IndexOf( (byte)0 );
		return Encoding.UTF8.GetString( length < 0 ? span : span[..length] );
	}

	/// <summary>A null-terminated string.</summary>
	public string Terminated()
	{
		var length = Data.AsSpan( Position, end - Position ).IndexOf( (byte)0 );
		if ( length < 0 ) throw new InvalidDataException( $"Unterminated string at {Position}" );
		var value = Encoding.UTF8.GetString( Data, Position, length );
		Position += length + 1;
		return value;
	}

	/// <summary><c>SerializeUInt32BackwardsCompatible</c>: a ushort, or 0xFFFF and a uint.</summary>
	public int CompactCount()
	{
		var value = UShort();
		return value == 0xFFFF ? (int)UInt() : value;
	}
}
