using Basalt.Source2.Compression;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;

namespace Basalt.Source2.Resources;

/// <summary>
/// Binary KeyValues3 (version 2, <c>0x4B563302</c>), which compiled materials and most other resources keep in their
/// <c>DATA</c> block: a header, then LZ4 or Zstandard compressed sections of bytes, 32-bit and 64-bit values, strings
/// and one type byte per value. Values read as <see cref="Dictionary{TKey,TValue}"/> (objects), <c>object[]</c>
/// (arrays), <see cref="string"/>, <see cref="long"/>, <see cref="ulong"/>, <see cref="double"/>, <see cref="float"/>,
/// <see cref="int"/>, <see cref="uint"/>, <see cref="bool"/>, <c>byte[]</c> (binary blobs) and null.
/// </summary>
/// <remarks>
/// Binary blobs (an entity lump's key values, a material's dynamic expressions) come after the main section: their
/// lengths follow the types in it, then <c>0xFFEEDD00</c>, then (LZ4) each compressed chunk's length as a ushort. The
/// chunks are one LZ4 stream, each up to the header's frame size, matches reaching back into the chunks before.
/// </remarks>
public static class Kv3
{
	const uint Version2 = 0x4B563302;

	/// <summary>Read a KV3 block's root value.</summary>
	public static object Read( ReadOnlySpan<byte> block )
	{
		var magic = BinaryPrimitives.ReadUInt32LittleEndian( block );
		if ( magic != Version2 ) throw new InvalidDataException( $"KV3 format {magic:X8} isn't supported (only version 2)" );

		var header = new ByteReader( block[4..].ToArray() );
		header.Skip( 16 );
		var compression = header.UInt();
		header.UShort();
		var frameSize = (int)header.UShort();
		var byteCount = (int)header.UInt();
		var intCount = (int)header.UInt();
		var eightCount = (int)header.UInt();
		var stringsAndTypes = (int)header.UInt();
		header.UShort();
		header.UShort();
		var size = (int)header.UInt();
		var packed = (int)header.UInt();
		var blockCount = (int)header.UInt();
		var blockTotal = (int)header.UInt();

		var body = block[(4 + header.Position)..];
		byte[] data;
		byte[] blobSource;
		switch ( compression )
		{
			case 0:
				data = body[..size].ToArray();
				blobSource = body[size..].ToArray();
				break;
			case 1:
				data = Lz4.Decompress( body[..packed], size );
				blobSource = body[packed..].ToArray();
				break;
			case 2:
				// One frame for the main section and the blobs after it
				var all = Zstd.Decompress( body[..packed], size + blockTotal );
				data = all[..size];
				blobSource = all[size..];
				break;
			default:
				throw new InvalidDataException( $"KV3 compression {compression}" );
		}

		var parser = new Parser( data, byteCount, intCount, eightCount );
		if ( blockCount > 0 ) parser.Blobs = Blobs( data, parser.StringsStart + stringsAndTypes, blockCount, compression, frameSize, blobSource );
		return parser.Root();
	}

	/// <summary>The binary blobs, from their lengths after the types and the data after the main section.</summary>
	static byte[][] Blobs( byte[] data, int lengthsStart, int count, uint compression, int frameSize, byte[] source )
	{
		var reader = new ByteReader( data, lengthsStart );
		var lengths = new int[count];
		for ( int i = 0; i < count; i++ ) lengths[i] = (int)reader.UInt();
		if ( reader.UInt() != 0xFFEEDD00 ) throw new InvalidDataException( "KV3 blob lengths aren't followed by their trailer" );

		var blobs = new byte[count][];
		var at = 0;
		if ( compression != 1 )
		{
			for ( int i = 0; i < count; i++ )
			{
				blobs[i] = source.AsSpan( at, lengths[i] ).ToArray();
				at += lengths[i];
			}

			return blobs;
		}

		// LZ4: one stream, in chunks of up to a frame each
		var output = new byte[lengths.Sum()];
		var position = 0;
		for ( int i = 0; i < count; i++ )
		{
			var end = position + lengths[i];
			while ( position < end )
			{
				var chunk = reader.UShort();
				var written = Lz4.Decompress( source.AsSpan( at, chunk ), output.AsSpan( 0, Math.Min( end, position + Math.Max( frameSize, 1 ) ) ), position );
				at += chunk;
				if ( written <= position ) throw new InvalidDataException( "KV3 blob chunk decompressed to nothing" );
				position = written;
			}

			blobs[i] = output[(end - lengths[i])..end];
		}

		return blobs;
	}

	/// <summary>A member of an object, or null.</summary>
	public static object Get( this Dictionary<string, object> obj, string name ) => obj.TryGetValue( name, out var value ) ? value : null;

	sealed class Parser
	{
		readonly byte[] data;
		readonly int bytesStart, intsStart, eightsStart, typesStart;
		readonly string[] strings;
		int nextByte, nextInt, nextEight, nextType, nextBlob;

		/// <summary>Where the strings start (the types follow them).</summary>
		public readonly int StringsStart;

		/// <summary>The binary blobs, in the order their values come.</summary>
		public byte[][] Blobs = [];

		public Parser( byte[] data, int byteCount, int intCount, int eightCount )
		{
			this.data = data;
			bytesStart = 0;
			intsStart = (byteCount + 3) & ~3;
			eightsStart = (intsStart + intCount * 4 + 7) & ~7;
			var stringsStart = eightsStart + eightCount * 8;
			StringsStart = stringsStart;

			// The first integer is the string count
			nextInt = 0;
			strings = new string[Int()];
			var position = stringsStart;
			for ( int i = 0; i < strings.Length; i++ )
			{
				var end = Array.IndexOf( data, (byte)0, position );
				strings[i] = Encoding.UTF8.GetString( data, position, end - position );
				position = end + 1;
			}

			typesStart = position;
		}

		int Int() => BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( intsStart + 4 * nextInt++ ) );

		ReadOnlySpan<byte> Eight() => data.AsSpan( eightsStart + 8 * nextEight++, 8 );

		byte Type()
		{
			var type = data[typesStart + nextType++];
			if ( (type & 0x80) != 0 ) nextType++;
			return (byte)(type & 0x7F);
		}

		public object Root() => Value( Type() );

		object Value( byte type )
		{
			switch ( type )
			{
				case 1: return null;
				case 2: return data[bytesStart + nextByte++] != 0;
				case 3: return BinaryPrimitives.ReadInt64LittleEndian( Eight() );
				case 4: return BinaryPrimitives.ReadUInt64LittleEndian( Eight() );
				case 5: return BinaryPrimitives.ReadDoubleLittleEndian( Eight() );
				case 6:
					var index = Int();
					return index < 0 ? "" : strings[index];
				case 7:
					if ( nextBlob >= Blobs.Length ) throw new InvalidDataException( "KV3 blob value without a blob" );
					return Blobs[nextBlob++];
				case 8:
					var array = new object[Int()];
					for ( int i = 0; i < array.Length; i++ ) array[i] = Value( Type() );
					return array;
				case 9:
					var count = Int();
					var obj = new Dictionary<string, object>( count, StringComparer.Ordinal );
					for ( int i = 0; i < count; i++ )
					{
						var name = strings[Int()];
						obj[name] = Value( Type() );
					}

					return obj;
				case 10:
					var typed = new object[Int()];
					var elementType = Type();
					for ( int i = 0; i < typed.Length; i++ ) typed[i] = Value( elementType );
					return typed;
				case 11: return Int();
				case 12: return (uint)Int();
				case 13: return true;
				case 14: return false;
				case 15: return 0L;
				case 16: return 1L;
				case 17: return 0.0;
				case 18: return 1.0;
				case 19: return BitConverter.Int32BitsToSingle( Int() );
				default: throw new InvalidDataException( $"KV3 value type {type} isn't supported" );
			}
		}
	}
}
