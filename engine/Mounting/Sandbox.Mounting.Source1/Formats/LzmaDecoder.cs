using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed class LzmaDecoder
{
	public static byte[] Decode( byte[] data, int offset, int size, ReadOnlySpan<byte> properties )
	{
		if ( offset < 0 || offset > data.Length )
			throw new InvalidDataException( "Invalid LZMA input offset." );
		using var stream = new MemoryStream( data, offset, data.Length - offset, false );
		return Decode( stream, stream.Length, size, properties );
	}

	internal static byte[] Decode( Stream stream, long length, int size, ReadOnlySpan<byte> properties, int? prefixLength = null )
	{
		if ( properties.Length != 5 || properties[0] >= 9 * 5 * 5 )
			throw new InvalidDataException( "Invalid LZMA properties." );
		if ( length < 5 || length > stream.Length - stream.Position || size < 0 || size > Array.MaxLength || prefixLength < 0 )
			throw new InvalidDataException( "Invalid LZMA input or output size." );
		int literalContextBits = properties[0] % 9;
		int literalPositionBits = properties[0] / 9 % 5;
		int positionBits = properties[0] / (9 * 5);
		uint dictionarySize = Math.Max( BitConverter.ToUInt32( properties[1..] ), 4096u );
		return new LzmaDecoder( stream, length ).Decode( size, literalContextBits, literalPositionBits, positionBits, dictionarySize, prefixLength );
	}

	readonly Stream input;
	readonly long inputEnd;
	uint range = uint.MaxValue;
	uint code;
	private LzmaDecoder( Stream input, long length )
	{
		this.input = input;
		inputEnd = input.Position + length;
		if ( ReadByte() != 0 )
			throw new InvalidDataException( "Invalid LZMA range header." );
		for ( int i = 0; i < 4; i++ )
			code = (code << 8) | ReadByte();
		if ( code == uint.MaxValue )
			throw new InvalidDataException( "Invalid LZMA range code." );
	}

	byte ReadByte()
	{
		if ( input.Position == inputEnd )
			throw new InvalidDataException( "Truncated LZMA payload." );
		var value = input.ReadByte();
		if ( value < 0 )
			throw new InvalidDataException( "Truncated LZMA payload." );
		return (byte)value;
	}

	void Normalize()
	{
		if ( range < 1u << 24 )
		{
			range <<= 8;
			code = (code << 8) | ReadByte();
		}
	}

	int Bit( ushort[] probabilities, int index )
	{
		uint probability = probabilities[index];
		uint bound = (range >> 11) * probability;
		int bit;
		if ( code < bound )
		{
			range = bound;
			probabilities[index] = (ushort)(probability + ((2048 - probability) >> 5));
			bit = 0;
		}
		else
		{
			range -= bound;
			code -= bound;
			probabilities[index] = (ushort)(probability - (probability >> 5));
			bit = 1;
		}

		Normalize();
		return bit;
	}

	static ushort[] Probabilities( int count )
	{
		var result = new ushort[count];
		Array.Fill( result, (ushort)1024 );
		return result;
	}

	int Tree( ushort[] probabilities, int offset, int bits )
	{
		int symbol = 1;
		for ( int i = 0; i < bits; i++ )
			symbol = (symbol << 1) | Bit( probabilities, offset + symbol );
		return symbol - (1 << bits);
	}

	uint ReverseTree( ushort[] probabilities, int offset, int bits )
	{
		int symbol = 1;
		uint value = 0;
		for ( int i = 0; i < bits; i++ )
		{
			int bit = Bit( probabilities, offset + symbol );
			symbol = (symbol << 1) | bit;
			value |= (uint)bit << i;
		}

		return value;
	}

	uint DirectBits( int bits )
	{
		uint value = 0;
		for ( int i = 0; i < bits; i++ )
		{
			range >>= 1;
			uint bit = code >= range ? 1u : 0u;
			if ( bit != 0 )
				code -= range;
			value = (value << 1) | bit;
			Normalize();
		}

		return value;
	}

	int Length( ushort[] probabilities, int positionState )
	{
		if ( Bit( probabilities, 0 ) == 0 )
			return Tree( probabilities, 2 + positionState * 8, 3 ) + 2;
		if ( Bit( probabilities, 1 ) == 0 )
			return Tree( probabilities, 2 + 16 * 8 + positionState * 8, 3 ) + 10;
		return Tree( probabilities, 2 + 2 * 16 * 8, 8 ) + 18;
	}

	private byte[] Decode( int size, int lc, int lp, int pb, uint dictionarySize, int? prefixLength )
	{
		var output = new byte[Math.Min( size, prefixLength ?? size )];
		var literals = Probabilities( 0x300 << (lc + lp) );
		var isMatch = Probabilities( 12 * 16 );
		var isRep = Probabilities( 12 );
		var isRepG0 = Probabilities( 12 );
		var isRepG1 = Probabilities( 12 );
		var isRepG2 = Probabilities( 12 );
		var isRep0Long = Probabilities( 12 * 16 );
		var lengths = Probabilities( 2 + 2 * 16 * 8 + 256 );
		var repLengths = Probabilities( 2 + 2 * 16 * 8 + 256 );
		var distanceSlots = Probabilities( 4 * 64 );
		var distances = Probabilities( 114 );
		var align = Probabilities( 16 );
		int state = 0, position = 0;
		uint rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;
		while ( true )
		{
			if ( position == output.Length && output.Length < size )
				return output;
			if ( position == size && code == 0 && input.Position == inputEnd )
				return output;
			int positionState = position & ((1 << pb) - 1);
			int statePosition = state * 16 + positionState;
			if ( Bit( isMatch, statePosition ) == 0 )
			{
				if ( position == size )
					throw new InvalidDataException( "LZMA output exceeds its declared size." );
				byte previous = position == 0 ? (byte)0 : output[position - 1];
				int context = ((position & ((1 << lp) - 1)) << lc) + (previous >> (8 - lc));
				int literalOffset = context * 0x300;
				int symbol = 1;
				if ( state >= 7 )
				{
					ValidateDistance( rep0, position, dictionarySize );
					int match = output[position - (int)rep0 - 1];
					while ( symbol < 0x100 )
					{
						int matchBit = (match >> 7) & 1;
						match <<= 1;
						int bit = Bit( literals, literalOffset + ((1 + matchBit) << 8) + symbol );
						symbol = (symbol << 1) | bit;
						if ( matchBit != bit )
							break;
					}
				}

				while ( symbol < 0x100 )
					symbol = (symbol << 1) | Bit( literals, literalOffset + symbol );
				output[position++] = (byte)symbol;
				state = state < 4 ? 0 : state < 10 ? state - 3 : state - 6;
				continue;
			}

			int length;
			if ( Bit( isRep, state ) != 0 )
			{
				if ( Bit( isRepG0, state ) == 0 )
				{
					if ( Bit( isRep0Long, statePosition ) == 0 )
					{
						state = state < 7 ? 9 : 11;
						Copy( output, ref position, rep0, 1, dictionarySize, size );
						continue;
					}
				}
				else
				{
					uint distance;
					if ( Bit( isRepG1, state ) == 0 )
						distance = rep1;
					else
					{
						if ( Bit( isRepG2, state ) == 0 )
							distance = rep2;
						else
						{
							distance = rep3;
							rep3 = rep2;
						}

						rep2 = rep1;
					}

					rep1 = rep0;
					rep0 = distance;
				}

				length = Length( repLengths, positionState );
				state = state < 7 ? 8 : 11;
			}
			else
			{
				rep3 = rep2;
				rep2 = rep1;
				rep1 = rep0;
				length = Length( lengths, positionState );
				state = state < 7 ? 7 : 10;
				int slot = Tree( distanceSlots, Math.Min( length - 2, 3 ) * 64, 6 );
				rep0 = (uint)slot;
				if ( slot >= 4 )
				{
					int bits = (slot >> 1) - 1;
					rep0 = (uint)(2 | (slot & 1)) << bits;
					if ( slot < 14 )
						rep0 += ReverseTree( distances, (int)rep0 - slot - 1, bits );
					else
					{
						rep0 += DirectBits( bits - 4 ) << 4;
						rep0 += ReverseTree( align, 0, 4 );
					}

					if ( rep0 == uint.MaxValue )
					{
						if ( position != size || length != 2 || code != 0 || input.Position != inputEnd )
							throw new InvalidDataException( "Invalid LZMA end marker or output size." );
						return output;
					}
				}
			}

			Copy( output, ref position, rep0, length, dictionarySize, size );
		}
	}

	static void ValidateDistance( uint distance, int position, uint dictionarySize )
	{
		if ( distance >= position || distance >= dictionarySize )
			throw new InvalidDataException( "LZMA match is outside its dictionary." );
	}

	static void Copy( byte[] output, ref int position, uint distance, int length, uint dictionarySize, int size )
	{
		ValidateDistance( distance, position, dictionarySize );
		if ( length > size - position )
			throw new InvalidDataException( "LZMA output exceeds its declared size." );
		length = Math.Min( length, output.Length - position );
		int source = position - (int)distance - 1;
		for ( int i = 0; i < length; i++ )
			output[position++] = output[source++];
	}
}
