using System.IO;

namespace Basalt.Source2.Compression;

/// <summary>
/// An LZ4 block decoder (the raw block format, no frame), which KV3 resources and some textures use.
/// </summary>
public static class Lz4
{
	/// <summary>
	/// Decompress one block to exactly <paramref name="size"/> bytes.
	/// </summary>
	public static byte[] Decompress( ReadOnlySpan<byte> source, int size )
	{
		var output = new byte[size];
		var written = Decompress( source, output );
		if ( written != size ) throw new InvalidDataException( $"LZ4 block held {written} bytes, expected {size}" );
		return output;
	}

	/// <summary>
	/// Decompress one block into <paramref name="output"/>; returns the bytes written.
	/// </summary>
	public static int Decompress( ReadOnlySpan<byte> source, Span<byte> output )
	{
		var input = 0;
		var position = 0;
		while ( input < source.Length )
		{
			var token = source[input++];

			var literals = token >> 4;
			if ( literals == 15 )
			{
				byte b;
				do
				{
					b = source[input++];
					literals += b;
				}
				while ( b == 255 );
			}

			source.Slice( input, literals ).CopyTo( output[position..] );
			input += literals;
			position += literals;

			// The last sequence has literals only
			if ( input >= source.Length ) break;

			var offset = source[input] | source[input + 1] << 8;
			input += 2;
			if ( offset == 0 || offset > position ) throw new InvalidDataException( $"LZ4 match offset {offset} at {position}" );

			var length = token & 15;
			if ( length == 15 )
			{
				byte b;
				do
				{
					b = source[input++];
					length += b;
				}
				while ( b == 255 );
			}

			length += 4;
			var from = position - offset;
			if ( offset >= length )
			{
				output.Slice( from, length ).CopyTo( output[position..] );
				position += length;
			}
			else
			{
				for ( int i = 0; i < length; i++ ) output[position++] = output[from++];
			}
		}

		return position;
	}
}
