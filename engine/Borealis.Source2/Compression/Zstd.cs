using System.Buffers.Binary;
using System.IO;
using System.Numerics;

namespace Borealis.Source2.Compression;

/// <summary>
/// A Zstandard decoder (RFC 8878): frames of raw, RLE and compressed blocks, Huffman-coded literals, FSE-coded sequences
/// and dictionaries with their entropy tables. It's what s&amp;box's compiled shaders are compressed with
/// (<c>VFX_COMPRESSION_ZSTD_DICT</c>), and needs no native library.
/// </summary>
public static class Zstd
{
	const uint FrameMagic = 0xFD2FB528;
	const uint SkippableMagicMask = 0xFFFFFFF0;
	const uint SkippableMagic = 0x184D2A50;

	/// <summary>
	/// Decompress every frame in <paramref name="source"/> into <paramref name="destination"/>, which must be large
	/// enough. Returns the bytes written.
	/// </summary>
	public static int Decompress( ReadOnlySpan<byte> source, Span<byte> destination, ZstdDictionary dictionary = null )
	{
		var written = 0;
		while ( source.Length > 0 )
		{
			var magic = BinaryPrimitives.ReadUInt32LittleEndian( source );
			if ( (magic & SkippableMagicMask) == SkippableMagic )
			{
				var size = (int)BinaryPrimitives.ReadUInt32LittleEndian( source[4..] );
				source = source[(8 + size)..];
				continue;
			}

			if ( magic != FrameMagic ) throw new InvalidDataException( $"Not a Zstandard frame (magic {magic:X8})" );
			var frame = new FrameDecoder( dictionary, workspace ??= new Workspace() );
			var used = frame.Decode( source, destination[written..], out var produced );
			written += produced;
			source = source[used..];
		}

		return written;
	}

	/// <summary>
	/// Decompress to a new array of <paramref name="size"/> bytes, which must be exactly what the frames hold.
	/// </summary>
	public static byte[] Decompress( ReadOnlySpan<byte> source, int size, ZstdDictionary dictionary = null )
	{
		var output = new byte[size];
		var written = Decompress( source, output, dictionary );
		if ( written != size ) throw new InvalidDataException( $"Zstandard data held {written} bytes, expected {size}" );
		return output;
	}

	/// <summary>
	/// The decompressed size a frame declares, or -1 when it doesn't.
	/// </summary>
	public static long FrameContentSize( ReadOnlySpan<byte> source )
	{
		if ( source.Length < 5 || BinaryPrimitives.ReadUInt32LittleEndian( source ) != FrameMagic ) return -1;
		var descriptor = source[4];
		var sizeFlag = descriptor >> 6;
		var singleSegment = (descriptor & 0x20) != 0;
		var position = 5 + (singleSegment ? 0 : 1) + DictionaryIdBytes( descriptor & 3 );
		return ContentSize( source[position..], sizeFlag, singleSegment );
	}

	/// <summary>
	/// A thread's decoding scratch, kept between frames: the literals buffer and the tables a frame reads, filled in place
	/// rather than made for every frame (a texture's every mip is one).
	/// </summary>
	sealed class Workspace
	{
		public readonly byte[] Literals = new byte[MaxBlockBytes];
		public readonly FseTable LiteralLengths = FseTable.Empty( 9 ), Offsets = FseTable.Empty( 8 ), MatchLengths = FseTable.Empty( 9 );
		public readonly HuffmanTable Huffman = HuffmanTable.Empty();
		public readonly FseTable HuffmanWeights = FseTable.Empty( 6 );
	}

	/// <summary>The most a block decodes to, and so the most literals it holds.</summary>
	const int MaxBlockBytes = 1 << 17;

	[ThreadStatic] static Workspace workspace;

	static int DictionaryIdBytes( int flag ) => flag switch { 0 => 0, 1 => 1, 2 => 2, _ => 4 };

	static long ContentSize( ReadOnlySpan<byte> data, int flag, bool singleSegment ) => flag switch
	{
		0 => singleSegment ? data[0] : -1,
		1 => BinaryPrimitives.ReadUInt16LittleEndian( data ) + 256,
		2 => BinaryPrimitives.ReadUInt32LittleEndian( data ),
		_ => (long)BinaryPrimitives.ReadUInt64LittleEndian( data ),
	};

	/// <summary>
	/// Decodes one frame, keeping the entropy tables and repeat offsets blocks inherit from each other.
	/// </summary>
	ref struct FrameDecoder
	{
		readonly ZstdDictionary dictionary;
		readonly Workspace workspace;
		HuffmanTable literalsTable;
		FseTable literalLengths, offsets, matchLengths;
		uint rep1, rep2, rep3;

		public FrameDecoder( ZstdDictionary dictionary, Workspace workspace )
		{
			this.dictionary = dictionary;
			this.workspace = workspace;
			rep1 = 1;
			rep2 = 4;
			rep3 = 8;
			if ( dictionary is not null )
			{
				literalsTable = dictionary.Literals;
				literalLengths = dictionary.LiteralLengths;
				offsets = dictionary.Offsets;
				matchLengths = dictionary.MatchLengths;
				rep1 = dictionary.Repeat1;
				rep2 = dictionary.Repeat2;
				rep3 = dictionary.Repeat3;
			}
		}

		ReadOnlySpan<byte> History => dictionary is null ? default : dictionary.Content;

		public int Decode( ReadOnlySpan<byte> source, Span<byte> destination, out int produced )
		{
			var descriptor = source[4];
			var sizeFlag = descriptor >> 6;
			var singleSegment = (descriptor & 0x20) != 0;
			var checksum = (descriptor & 0x04) != 0;
			if ( (descriptor & 0x08) != 0 ) throw new InvalidDataException( "Zstandard frame uses a reserved bit" );

			var position = 5 + (singleSegment ? 0 : 1);
			var idBytes = DictionaryIdBytes( descriptor & 3 );
			uint dictionaryId = idBytes switch
			{
				0 => 0,
				1 => source[position],
				2 => BinaryPrimitives.ReadUInt16LittleEndian( source[position..] ),
				_ => BinaryPrimitives.ReadUInt32LittleEndian( source[position..] ),
			};

			position += idBytes;
			if ( dictionaryId != 0 && (dictionary is null || dictionary.Id != dictionaryId) )
				throw new InvalidDataException( $"Zstandard frame needs dictionary {dictionaryId}" );

			var contentSize = ContentSize( source[position..], sizeFlag, singleSegment );
			position += sizeFlag switch { 0 => singleSegment ? 1 : 0, 1 => 2, 2 => 4, _ => 8 };
			if ( contentSize > destination.Length ) throw new InvalidDataException( $"Zstandard frame holds {contentSize} bytes, more than the {destination.Length} given" );

			var output = 0;
			while ( true )
			{
				var header = source[position] | source[position + 1] << 8 | source[position + 2] << 16;
				position += 3;
				var last = (header & 1) != 0;
				var type = (header >> 1) & 3;
				var size = header >> 3;

				switch ( type )
				{
					case 0:
						source.Slice( position, size ).CopyTo( destination[output..] );
						output += size;
						position += size;
						break;

					case 1:
						destination.Slice( output, size ).Fill( source[position] );
						output += size;
						position += 1;
						break;

					case 2:
						output = DecodeBlock( source.Slice( position, size ), destination, output );
						position += size;
						break;

					default:
						throw new InvalidDataException( "Zstandard block of reserved type" );
				}

				if ( last ) break;
			}

			if ( checksum ) position += 4;
			if ( contentSize >= 0 && output != contentSize ) throw new InvalidDataException( $"Zstandard frame decoded {output} bytes, its header says {contentSize}" );

			produced = output;
			return position;
		}

		//
		// Compressed blocks: literals, then sequences that interleave them with matches
		//

		int DecodeBlock( ReadOnlySpan<byte> block, Span<byte> output, int position )
		{
			var used = DecodeLiterals( block, out var literals );
			var sequences = block[used..];

			int count;
			var p = 0;
			var first = sequences[0];
			if ( first == 0 ) { count = 0; p = 1; }
			else if ( first < 128 ) { count = first; p = 1; }
			else if ( first < 255 ) { count = ((first - 128) << 8) + sequences[1]; p = 2; }
			else { count = sequences[1] + (sequences[2] << 8) + 0x7F00; p = 3; }

			if ( count == 0 )
			{
				literals.CopyTo( output[position..] );
				return position + literals.Length;
			}

			var modes = sequences[p++];
			literalLengths = ReadTable( modes >> 6, sequences, ref p, literalLengths, FseTable.LiteralLengthDefault, workspace.LiteralLengths, 35, 9 );
			offsets = ReadTable( (modes >> 4) & 3, sequences, ref p, offsets, FseTable.OffsetDefault, workspace.Offsets, 31, 8 );
			matchLengths = ReadTable( (modes >> 2) & 3, sequences, ref p, matchLengths, FseTable.MatchLengthDefault, workspace.MatchLengths, 52, 9 );

			var bits = new BackwardBits( sequences[p..] );
			var llState = bits.Read( literalLengths.Log );
			var ofState = bits.Read( offsets.Log );
			var mlState = bits.Read( matchLengths.Log );

			var history = History;
			var literal = 0;
			for ( int i = 0; i < count; i++ )
			{
				var ofCode = offsets.Symbols[ofState];
				var llCode = literalLengths.Symbols[llState];
				var mlCode = matchLengths.Symbols[mlState];

				var offsetValue = (1u << ofCode) + bits.Read( ofCode );
				var matchLength = (int)(MatchLengthBase[mlCode] + bits.Read( MatchLengthBits[mlCode] ));
				var literalLength = (int)(LiteralLengthBase[llCode] + bits.Read( LiteralLengthBits[llCode] ));

				if ( i + 1 < count )
				{
					llState = literalLengths.Next[llState] + bits.Read( literalLengths.Bits[llState] );
					mlState = matchLengths.Next[mlState] + bits.Read( matchLengths.Bits[mlState] );
					ofState = offsets.Next[ofState] + bits.Read( offsets.Bits[ofState] );
				}

				uint offset;
				if ( offsetValue > 3 )
				{
					offset = offsetValue - 3;
					rep3 = rep2;
					rep2 = rep1;
					rep1 = offset;
				}
				else
				{
					// With no literals before it, a repeat code skips the most recent offset
					var code = literalLength == 0 ? offsetValue + 1 : offsetValue;
					switch ( code )
					{
						case 1:
							offset = rep1;
							break;
						case 2:
							offset = rep2;
							rep2 = rep1;
							rep1 = offset;
							break;
						case 3:
							offset = rep3;
							rep3 = rep2;
							rep2 = rep1;
							rep1 = offset;
							break;
						default:
							offset = rep1 - 1;
							rep3 = rep2;
							rep2 = rep1;
							rep1 = offset;
							break;
					}
				}

				literals.Slice( literal, literalLength ).CopyTo( output[position..] );
				literal += literalLength;
				position += literalLength;

				if ( offset == 0 || offset > position + history.Length ) throw new InvalidDataException( $"Zstandard match offset {offset} reaches before the start" );

				var from = position - (int)offset;
				if ( from >= 0 && offset >= matchLength )
				{
					output.Slice( from, matchLength ).CopyTo( output[position..] );
					position += matchLength;
					continue;
				}

				for ( int j = 0; j < matchLength; j++, from++ )
				{
					output[position++] = from < 0 ? history[history.Length + from] : output[from];
				}
			}

			literals[literal..].CopyTo( output[position..] );
			return position + literals.Length - literal;
		}

		// A new table goes into the workspace's own for its kind (into), replacing the one it held: a frame's tables are only
		// ever the last one read, the predefined one or the dictionary's
		static FseTable ReadTable( int mode, ReadOnlySpan<byte> data, ref int position, FseTable previous, FseTable predefined, FseTable into, int maxSymbol, int maxLog )
		{
			switch ( mode )
			{
				case 0:
					return predefined;
				case 1:
					return into.SetRle( data[position++] );
				case 2:
					var table = FseTable.Read( data[position..], maxSymbol, maxLog, out var used, into );
					position += used;
					return table;
				default:
					return previous ?? throw new InvalidDataException( "Zstandard block repeats a table it never had" );
			}
		}

		int DecodeLiterals( ReadOnlySpan<byte> block, out ReadOnlySpan<byte> literals )
		{
			var first = block[0];
			var type = first & 3;
			var sizeFormat = (first >> 2) & 3;

			if ( type < 2 )
			{
				int size, header;
				switch ( sizeFormat )
				{
					case 1:
						size = (first >> 4) + (block[1] << 4);
						header = 2;
						break;
					case 3:
						size = (first >> 4) + (block[1] << 4) + (block[2] << 12);
						header = 3;
						break;
					default:
						size = first >> 3;
						header = 1;
						break;
				}

				if ( type == 0 )
				{
					literals = block.Slice( header, size );
					return header + size;
				}

				var buffer = Literals( size );
				buffer.Fill( block[header] );
				literals = buffer;
				return header + 1;
			}

			var streams = sizeFormat == 0 ? 1 : 4;
			var headerSize = sizeFormat switch { 0 or 1 => 3, 2 => 4, _ => 5 };
			var sizeBits = sizeFormat switch { 0 or 1 => 10, 2 => 14, _ => 18 };
			ulong packed = 0;
			for ( int i = 0; i < headerSize; i++ ) packed |= (ulong)block[i] << (8 * i);
			var mask = (1UL << sizeBits) - 1;
			var regenerated = (int)((packed >> 4) & mask);
			var compressed = (int)((packed >> (4 + sizeBits)) & mask);

			var data = block.Slice( headerSize, compressed );
			if ( type == 2 )
			{
				literalsTable = HuffmanTable.Read( data, out var used, workspace.Huffman, workspace.HuffmanWeights );
				data = data[used..];
			}
			else if ( literalsTable is null )
			{
				throw new InvalidDataException( "Zstandard literals reuse a Huffman table there isn't" );
			}

			var output = Literals( regenerated );
			if ( streams == 1 )
			{
				literalsTable.Decode( data, output );
			}
			else
			{
				var size1 = BinaryPrimitives.ReadUInt16LittleEndian( data );
				var size2 = BinaryPrimitives.ReadUInt16LittleEndian( data[2..] );
				var size3 = BinaryPrimitives.ReadUInt16LittleEndian( data[4..] );
				var streamData = data[6..];
				var segment = (regenerated + 3) / 4;
				literalsTable.Decode( streamData[..size1], output[..segment] );
				literalsTable.Decode( streamData.Slice( size1, size2 ), output.Slice( segment, segment ) );
				literalsTable.Decode( streamData.Slice( size1 + size2, size3 ), output.Slice( 2 * segment, segment ) );
				literalsTable.Decode( streamData[(size1 + size2 + size3)..], output[(3 * segment)..] );
			}

			literals = output;
			return headerSize + compressed;
		}

		readonly Span<byte> Literals( int size )
		{
			if ( size > MaxBlockBytes ) throw new InvalidDataException( $"Zstandard block of {size} literals, more than a block holds" );
			return workspace.Literals.AsSpan( 0, size );
		}
	}

	static readonly uint[] LiteralLengthBase =
	[
		0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24, 28, 32, 40, 48, 64, 128, 256, 512, 1024,
		2048, 4096, 8192, 16384, 32768, 65536,
	];

	static readonly byte[] LiteralLengthBits =
	[
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 3, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
	];

	static readonly uint[] MatchLengthBase =
	[
		3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33,
		34, 35, 37, 39, 41, 43, 47, 51, 59, 67, 83, 99, 131, 259, 515, 1027, 2051, 4099, 8195, 16387, 32771, 65539,
	];

	static readonly byte[] MatchLengthBits =
	[
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 3,
		3, 4, 4, 5, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
	];
}

/// <summary>
/// A bit stream read from its end towards its start, as Zstandard's Huffman and FSE streams are. The last byte's highest
/// set bit marks where it starts. Reading past the start gives zeros, which is how a decoder notices it's done.
/// </summary>
internal ref struct BackwardBits
{
	readonly ReadOnlySpan<byte> data;
	int position;

	public BackwardBits( ReadOnlySpan<byte> data )
	{
		this.data = data;
		if ( data.Length == 0 ) throw new InvalidDataException( "Empty Zstandard bit stream" );
		var last = data[^1];
		if ( last == 0 ) throw new InvalidDataException( "Zstandard bit stream without its end marker" );
		position = (data.Length - 1) * 8 + BitOperations.Log2( last );
	}

	/// <summary>Bits left before the start; negative once read past it.</summary>
	public readonly int Remaining => position;

	public uint Read( int count )
	{
		if ( count == 0 ) return 0;
		position -= count;
		return (uint)Bits.Extract( data, position, count );
	}

	public readonly uint Peek( int count ) => (uint)Bits.Extract( data, position - count, count );

	public void Skip( int count ) => position -= count;
}

internal static class Bits
{
	/// <summary>
	/// <paramref name="count"/> (up to 32) bits starting at bit <paramref name="start"/>, little-endian bit order; bits
	/// before the start of the data read as zeros.
	/// </summary>
	public static ulong Extract( ReadOnlySpan<byte> data, int start, int count )
	{
		if ( start < 0 )
		{
			if ( start + count <= 0 ) return 0;
			return Extract( data, 0, start + count ) << -start;
		}

		var index = start >> 3;
		ulong value;
		if ( index + 8 <= data.Length )
		{
			value = BinaryPrimitives.ReadUInt64LittleEndian( data[index..] );
		}
		else
		{
			value = 0;
			for ( int i = 0; index + i < data.Length && i < 8; i++ ) value |= (ulong)data[index + i] << (8 * i);
		}

		return (value >> (start & 7)) & ((1UL << count) - 1);
	}
}

/// <summary>
/// A decoding table for one FSE-coded symbol type: symbol, bits to read and the base of the next state, per state.
/// </summary>
internal sealed class FseTable
{
	public int Log;
	public byte[] Symbols;
	public byte[] Bits;
	public uint[] Next;

	public static readonly FseTable LiteralLengthDefault = Build(
		[4, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 1, 1, 1, 1, 1, -1, -1, -1, -1], 6 );

	public static readonly FseTable MatchLengthDefault = Build(
		[1, 4, 3, 2, 2, 2, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, -1, -1, -1, -1, -1, -1, -1], 6 );

	public static readonly FseTable OffsetDefault = Build(
		[1, 1, 1, 1, 1, 1, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, -1, -1, -1, -1, -1], 5 );

	/// <summary>A table that always decodes <paramref name="symbol"/> and reads nothing.</summary>
	public static FseTable Rle( byte symbol ) => new() { Log = 0, Symbols = [symbol], Bits = [0], Next = [0] };

	/// <summary>A table with room for up to <paramref name="maxLog"/> bits of state, to fill in place (<see cref="Read"/>, <see cref="SetRle"/>).</summary>
	public static FseTable Empty( int maxLog ) => new() { Symbols = new byte[1 << maxLog], Bits = new byte[1 << maxLog], Next = new uint[1 << maxLog] };

	/// <summary>Make this one always decode <paramref name="symbol"/> and read nothing.</summary>
	public FseTable SetRle( byte symbol )
	{
		Log = 0;
		Symbols[0] = symbol;
		Bits[0] = 0;
		Next[0] = 0;
		return this;
	}

	/// <summary>
	/// Read a table description (normalized counts) from the start of <paramref name="data"/>, into <paramref name="into"/>
	/// (from <see cref="Empty"/>, with room for <paramref name="maxLog"/>) or a new table.
	/// </summary>
	public static FseTable Read( ReadOnlySpan<byte> data, int maxSymbol, int maxLog, out int used, FseTable into = null )
	{
		var position = 0;
		var log = (int)Borealis.Source2.Compression.Bits.Extract( data, 0, 4 ) + 5;
		position += 4;
		if ( log > maxLog ) throw new InvalidDataException( $"Zstandard FSE table accuracy {log} over {maxLog}" );

		Span<short> counts = stackalloc short[maxSymbol + 1];
		counts.Clear();
		var remaining = (1 << log) + 1;
		var threshold = 1 << log;
		var bitCount = log + 1;
		var symbol = 0;
		var previousZero = false;

		while ( remaining > 1 && symbol <= maxSymbol )
		{
			if ( previousZero )
			{
				var zeros = symbol;
				while ( true )
				{
					var repeat = (int)Borealis.Source2.Compression.Bits.Extract( data, position, 2 );
					position += 2;
					zeros += repeat;
					if ( repeat != 3 ) break;
				}

				if ( zeros > maxSymbol + 1 ) throw new InvalidDataException( "Zstandard FSE zero run past the last symbol" );
				symbol = zeros;
				if ( symbol > maxSymbol ) break;
			}

			var max = (2 * threshold - 1) - remaining;
			var value = (int)Borealis.Source2.Compression.Bits.Extract( data, position, bitCount );
			int count;
			if ( (value & (threshold - 1)) < max )
			{
				count = value & (threshold - 1);
				position += bitCount - 1;
			}
			else
			{
				count = value & (2 * threshold - 1);
				if ( count >= threshold ) count -= max;
				position += bitCount;
			}

			count--;
			remaining -= Math.Abs( count );
			counts[symbol++] = (short)count;
			previousZero = count == 0;

			while ( remaining < threshold )
			{
				bitCount--;
				threshold >>= 1;
			}
		}

		if ( remaining != 1 ) throw new InvalidDataException( "Zstandard FSE counts don't add up" );
		used = (position + 7) / 8;
		return Build( counts, log, into );
	}

	public static FseTable Build( ReadOnlySpan<short> counts, int log, FseTable into = null )
	{
		var size = 1 << log;
		var table = into ?? new FseTable { Symbols = new byte[size], Bits = new byte[size], Next = new uint[size] };
		if ( table.Symbols.Length < size ) throw new InvalidDataException( $"Zstandard FSE table of {size} states, more than {table.Symbols.Length}" );
		table.Log = log;
		Span<uint> next = stackalloc uint[counts.Length];
		var high = size - 1;

		for ( int s = 0; s < counts.Length; s++ )
		{
			if ( counts[s] == -1 )
			{
				table.Symbols[high--] = (byte)s;
				next[s] = 1;
			}
			else
			{
				next[s] = (uint)Math.Max( (int)counts[s], 0 );
			}
		}

		var step = (size >> 1) + (size >> 3) + 3;
		var mask = size - 1;
		var position = 0;
		for ( int s = 0; s < counts.Length; s++ )
		{
			for ( int i = 0; i < counts[s]; i++ )
			{
				table.Symbols[position] = (byte)s;
				do position = (position + step) & mask;
				while ( position > high );
			}
		}

		if ( position != 0 ) throw new InvalidDataException( "Zstandard FSE table didn't fill" );

		for ( int u = 0; u < size; u++ )
		{
			var s = table.Symbols[u];
			var state = next[s]++;
			var bits = log - BitOperations.Log2( state );
			table.Bits[u] = (byte)bits;
			table.Next[u] = (state << bits) - (uint)size;
		}

		return table;
	}
}

/// <summary>
/// A Huffman decoding table for literals: the symbol and code length for every <see cref="Log"/>-bit prefix.
/// </summary>
internal sealed class HuffmanTable
{
	public int Log;
	public byte[] Symbols;
	public byte[] Lengths;

	/// <summary>The longest code Zstandard allows.</summary>
	const int MaxLog = 11;

	/// <summary>A table with room for the longest codes, to fill in place (<see cref="Read"/>).</summary>
	public static HuffmanTable Empty() => new() { Symbols = new byte[1 << MaxLog], Lengths = new byte[1 << MaxLog] };

	/// <summary>
	/// Read a tree description: weights, FSE-compressed or four bits each, the last one implied; into
	/// <paramref name="into"/> (from <see cref="Empty"/>) or a new table, the weights' FSE table into
	/// <paramref name="weightsTable"/> (<see cref="FseTable.Empty"/> of 6) or a new one.
	/// </summary>
	public static HuffmanTable Read( ReadOnlySpan<byte> data, out int used, HuffmanTable into = null, FseTable weightsTable = null )
	{
		var header = data[0];
		Span<byte> weights = stackalloc byte[256];
		int count;

		if ( header < 128 )
		{
			var compressed = data.Slice( 1, header );
			var table = FseTable.Read( compressed, 255, 6, out var tableBytes, weightsTable );
			var bits = new BackwardBits( compressed[tableBytes..] );
			var state1 = bits.Read( table.Log );
			var state2 = bits.Read( table.Log );
			count = 0;

			while ( true )
			{
				weights[count++] = table.Symbols[state1];
				state1 = table.Next[state1] + bits.Read( table.Bits[state1] );
				if ( bits.Remaining < 0 )
				{
					weights[count++] = table.Symbols[state2];
					break;
				}

				weights[count++] = table.Symbols[state2];
				state2 = table.Next[state2] + bits.Read( table.Bits[state2] );
				if ( bits.Remaining < 0 )
				{
					weights[count++] = table.Symbols[state1];
					break;
				}

				if ( count > 254 ) throw new InvalidDataException( "Zstandard Huffman weights overrun" );
			}

			used = 1 + header;
		}
		else
		{
			count = header - 127;
			for ( int i = 0; i < count; i++ )
			{
				var b = data[1 + i / 2];
				weights[i] = (byte)((i & 1) == 0 ? b >> 4 : b & 15);
			}

			used = 1 + (count + 1) / 2;
		}

		var sum = 0;
		for ( int i = 0; i < count; i++ )
		{
			if ( weights[i] > 0 ) sum += 1 << (weights[i] - 1);
		}

		if ( sum == 0 ) throw new InvalidDataException( "Zstandard Huffman tree with no weights" );
		var log = BitOperations.Log2( (uint)sum ) + 1;
		if ( log > MaxLog ) throw new InvalidDataException( $"Zstandard Huffman codes of {log} bits, over {MaxLog}" );
		var left = (1 << log) - sum;
		if ( !BitOperations.IsPow2( left ) ) throw new InvalidDataException( "Zstandard Huffman weights don't complete a tree" );
		weights[count++] = (byte)(BitOperations.Log2( (uint)left ) + 1);

		Span<int> rankStart = stackalloc int[log + 2];
		Span<int> rankCount = stackalloc int[log + 2];
		for ( int i = 0; i < count; i++ ) rankCount[weights[i]]++;

		var next = 0;
		for ( int w = 1; w <= log; w++ )
		{
			rankStart[w] = next;
			next += rankCount[w] << (w - 1);
		}

		var result = into ?? new HuffmanTable { Symbols = new byte[1 << log], Lengths = new byte[1 << log] };
		result.Log = log;
		for ( int s = 0; s < count; s++ )
		{
			var w = weights[s];
			if ( w == 0 ) continue;
			var length = 1 << (w - 1);
			var start = rankStart[w];
			result.Symbols.AsSpan( start, length ).Fill( (byte)s );
			result.Lengths.AsSpan( start, length ).Fill( (byte)(log + 1 - w) );
			rankStart[w] += length;
		}

		return result;
	}

	/// <summary>
	/// Decode one stream, which must fill <paramref name="output"/> exactly.
	/// </summary>
	public void Decode( ReadOnlySpan<byte> stream, Span<byte> output )
	{
		var bits = new BackwardBits( stream );
		for ( int i = 0; i < output.Length; i++ )
		{
			var index = bits.Peek( Log );
			output[i] = Symbols[index];
			bits.Skip( Lengths[index] );
		}

		if ( bits.Remaining != 0 ) throw new InvalidDataException( "Zstandard Huffman stream didn't end where its literals did" );
	}
}

/// <summary>
/// A Zstandard dictionary: an id, entropy tables and repeat offsets frames start from, and content matches can reach
/// back into.
/// </summary>
public sealed class ZstdDictionary
{
	const uint Magic = 0xEC30A437;

	/// <summary>Its id, which frames compressed with it name.</summary>
	public uint Id { get; private init; }

	/// <summary>The history frames start with.</summary>
	public byte[] Content { get; private init; }

	internal HuffmanTable Literals { get; private init; }
	internal FseTable Offsets { get; private init; }
	internal FseTable MatchLengths { get; private init; }
	internal FseTable LiteralLengths { get; private init; }
	internal uint Repeat1 { get; private init; }
	internal uint Repeat2 { get; private init; }
	internal uint Repeat3 { get; private init; }

	/// <summary>
	/// Read a dictionary made by <c>zstd --train</c>, or treat data without the dictionary magic as raw content.
	/// </summary>
	public static ZstdDictionary Read( ReadOnlySpan<byte> data )
	{
		if ( data.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian( data ) != Magic )
			return new ZstdDictionary { Content = data.ToArray(), Repeat1 = 1, Repeat2 = 4, Repeat3 = 8 };

		var id = BinaryPrimitives.ReadUInt32LittleEndian( data[4..] );
		var position = 8;
		var literals = HuffmanTable.Read( data[position..], out var used );
		position += used;
		var offsets = FseTable.Read( data[position..], 31, 8, out used );
		position += used;
		var matchLengths = FseTable.Read( data[position..], 52, 9, out used );
		position += used;
		var literalLengths = FseTable.Read( data[position..], 35, 9, out used );
		position += used;

		return new ZstdDictionary
		{
			Id = id,
			Literals = literals,
			Offsets = offsets,
			MatchLengths = matchLengths,
			LiteralLengths = literalLengths,
			Repeat1 = BinaryPrimitives.ReadUInt32LittleEndian( data[position..] ),
			Repeat2 = BinaryPrimitives.ReadUInt32LittleEndian( data[(position + 4)..] ),
			Repeat3 = BinaryPrimitives.ReadUInt32LittleEndian( data[(position + 8)..] ),
			Content = data[(position + 12)..].ToArray(),
		};
	}

	static ZstdDictionary vfx;

	/// <summary>
	/// The dictionary vfx compresses compiled shaders with (<c>g_VfxZStdDictionary</c>).
	/// </summary>
	public static ZstdDictionary Vfx
	{
		get
		{
			if ( vfx is not null ) return vfx;
			using var stream = typeof( ZstdDictionary ).Assembly.GetManifestResourceStream( "Borealis.Source2.VfxDictionary.zdict" )
				?? throw new FileNotFoundException( "The vfx Zstandard dictionary isn't embedded" );
			var bytes = new byte[stream.Length];
			stream.ReadExactly( bytes );
			return vfx = Read( bytes );
		}
	}
}
