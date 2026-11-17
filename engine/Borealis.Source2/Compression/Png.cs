using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Borealis.Source2.Compression;

/// <summary>
/// A PNG decoder to 8-bit RGBA: what a compiled texture of the <c>PNG_RGBA8888</c> format holds (an image file a game
/// ships, compiled as is). Every colour type and bit depth, palettes with transparency, and interlacing; 16-bit samples
/// keep their high byte. Ancillary chunks (gamma, colour profiles) are ignored, as the engine ignores them.
/// </summary>
internal static class Png
{
	static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

	/// <summary>Whether <paramref name="data"/> starts with a PNG signature.</summary>
	public static bool Is( ReadOnlySpan<byte> data ) => data.StartsWith( Signature );

	/// <summary>
	/// Bytes of the PNG at the start of <paramref name="data"/> up to the end of its <c>IEND</c> chunk; when that chunk
	/// isn't in <paramref name="data"/> yet, more than it holds (<see cref="int.MaxValue"/>), so a reader reads the rest.
	/// </summary>
	public static long Length( ReadOnlySpan<byte> data )
	{
		long position = Signature.Length;
		while ( position + 8 <= data.Length )
		{
			var length = BinaryPrimitives.ReadUInt32BigEndian( data[(int)position..] );
			var type = BinaryPrimitives.ReadUInt32BigEndian( data[(int)(position + 4)..] );
			position += 12 + length;
			if ( type == Iend ) return position;
		}

		return int.MaxValue;
	}

	const uint Ihdr = 0x49484452, Plte = 0x504C5445, Trns = 0x74524E53, Idat = 0x49444154, Iend = 0x49454E44;

	/// <summary>Decode the PNG in <paramref name="data"/>: its size, and its pixels as RGBA8, rows top first.</summary>
	public static (int Width, int Height, byte[] Rgba) Decode( ReadOnlySpan<byte> data )
	{
		if ( !Is( data ) ) throw new InvalidDataException( "Not a PNG" );

		int width = 0, height = 0, depth = 0, colorType = 0, interlace = 0;
		byte[] palette = null, transparency = null;
		using var compressed = new MemoryStream();
		var position = Signature.Length;
		while ( true )
		{
			if ( position + 8 > data.Length ) throw new InvalidDataException( "PNG ends before IEND" );
			var length = (int)BinaryPrimitives.ReadUInt32BigEndian( data[position..] );
			var type = BinaryPrimitives.ReadUInt32BigEndian( data[(position + 4)..] );
			if ( length < 0 || position + 12L + length > data.Length ) throw new InvalidDataException( "PNG chunk runs past the end" );
			var body = data.Slice( position + 8, length );
			position += 12 + length;

			if ( type == Ihdr )
			{
				width = (int)BinaryPrimitives.ReadUInt32BigEndian( body );
				height = (int)BinaryPrimitives.ReadUInt32BigEndian( body[4..] );
				depth = body[8];
				colorType = body[9];
				interlace = body[12];
			}
			else if ( type == Plte ) palette = body.ToArray();
			else if ( type == Trns ) transparency = body.ToArray();
			else if ( type == Idat ) compressed.Write( body );
			else if ( type == Iend ) break;
		}

		var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new InvalidDataException( $"PNG colour type {colorType}" ) };
		if ( width <= 0 || height <= 0 || depth is not (1 or 2 or 4 or 8 or 16) ) throw new InvalidDataException( $"PNG {width}x{height} at {depth} bits" );
		if ( colorType == 3 && palette is null ) throw new InvalidDataException( "PNG palette image without a palette" );

		compressed.Position = 0;
		using var inflate = new ZLibStream( compressed, CompressionMode.Decompress );
		using var raw = new MemoryStream();
		inflate.CopyTo( raw );
		var filtered = raw.GetBuffer().AsSpan( 0, (int)raw.Length );

		var rgba = new byte[width * height * 4];
		var bitsPerPixel = channels * depth;
		var image = new Image( width, colorType, depth, palette, transparency, rgba );
		if ( interlace == 0 )
		{
			image.Pass( filtered, 0, 0, 1, 1, width, height, bitsPerPixel );
			return (width, height, rgba);
		}

		// Adam7: seven reduced images one after another, each filtered on its own
		ReadOnlySpan<int> startX = [0, 4, 0, 2, 0, 1, 0], startY = [0, 0, 4, 0, 2, 0, 1], stepX = [8, 8, 4, 4, 2, 2, 1], stepY = [8, 8, 8, 4, 4, 2, 2];
		var offset = 0;
		for ( int pass = 0; pass < 7; pass++ )
		{
			var w = (width - startX[pass] + stepX[pass] - 1) / stepX[pass];
			var h = (height - startY[pass] + stepY[pass] - 1) / stepY[pass];
			if ( w <= 0 || h <= 0 ) continue;
			var bytes = (1 + (w * bitsPerPixel + 7) / 8) * h;
			if ( offset + bytes > filtered.Length ) throw new InvalidDataException( "PNG data ends early" );
			image.Pass( filtered.Slice( offset, bytes ), startX[pass], startY[pass], stepX[pass], stepY[pass], w, h, bitsPerPixel );
			offset += bytes;
		}

		return (width, height, rgba);
	}

	readonly struct Image( int width, int colorType, int depth, byte[] palette, byte[] transparency, byte[] rgba )
	{
		/// <summary>Unfilter a (reduced) image's rows in place and write its pixels at their places in the whole.</summary>
		public void Pass( Span<byte> data, int x0, int y0, int dx, int dy, int w, int h, int bitsPerPixel )
		{
			var stride = (w * bitsPerPixel + 7) / 8;
			var bpp = Math.Max( bitsPerPixel / 8, 1 );
			if ( data.Length < (stride + 1) * h ) throw new InvalidDataException( "PNG data ends early" );

			Span<byte> previous = Span<byte>.Empty;
			for ( int y = 0; y < h; y++ )
			{
				var filter = data[y * (stride + 1)];
				var row = data.Slice( y * (stride + 1) + 1, stride );
				Unfilter( filter, row, previous, bpp );
				previous = row;

				var o = ((y0 + y * dy) * width + x0) * 4;
				for ( int x = 0; x < w; x++, o += dx * 4 ) Pixel( row, x, rgba.AsSpan( o, 4 ) );
			}
		}

		static void Unfilter( byte filter, Span<byte> row, ReadOnlySpan<byte> previous, int bpp )
		{
			var up = !previous.IsEmpty;
			switch ( filter )
			{
				case 0: break;
				case 1:
					for ( int i = bpp; i < row.Length; i++ ) row[i] += row[i - bpp];
					break;
				case 2:
					if ( up ) for ( int i = 0; i < row.Length; i++ ) row[i] += previous[i];
					break;
				case 3:
					for ( int i = 0; i < row.Length; i++ ) row[i] += (byte)(((i >= bpp ? row[i - bpp] : 0) + (up ? previous[i] : 0)) >> 1);
					break;
				case 4:
					for ( int i = 0; i < row.Length; i++ )
					{
						int a = i >= bpp ? row[i - bpp] : 0, b = up ? previous[i] : 0, c = up && i >= bpp ? previous[i - bpp] : 0;
						int p = a + b - c, pa = Math.Abs( p - a ), pb = Math.Abs( p - b ), pc = Math.Abs( p - c );
						row[i] += (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
					}

					break;
				default: throw new InvalidDataException( $"PNG filter {filter}" );
			}
		}

		/// <summary>Sample <paramref name="index"/> of a row (a channel of a pixel), as 8 bits; palette and grey sub-byte samples raw.</summary>
		int Sample( ReadOnlySpan<byte> row, int index )
		{
			if ( depth == 8 ) return row[index];
			if ( depth == 16 ) return row[index * 2];
			var bit = index * depth;
			return (row[bit >> 3] >> (8 - depth - (bit & 7))) & ((1 << depth) - 1);
		}

		// Sub-byte greys spread over 0..255
		int Grey( int value ) => depth >= 8 ? value : value * 255 / ((1 << depth) - 1);

		void Pixel( ReadOnlySpan<byte> row, int x, Span<byte> into )
		{
			switch ( colorType )
			{
				case 0:
					{
						var g = Sample( row, x );
						into[0] = into[1] = into[2] = (byte)Grey( g );
						into[3] = (byte)(transparency is { Length: >= 2 } && Raw( row, x, 1 ) == BinaryPrimitives.ReadUInt16BigEndian( transparency ) ? 0 : 255);
						break;
					}
				case 2:
					into[0] = (byte)Sample( row, x * 3 );
					into[1] = (byte)Sample( row, x * 3 + 1 );
					into[2] = (byte)Sample( row, x * 3 + 2 );
					into[3] = (byte)(transparency is { Length: >= 6 } && Raw( row, x, 3 ) == Key( transparency ) ? 0 : 255);
					break;
				case 3:
					{
						var i = Sample( row, x );
						if ( i * 3 + 2 < palette.Length )
						{
							into[0] = palette[i * 3];
							into[1] = palette[i * 3 + 1];
							into[2] = palette[i * 3 + 2];
						}

						into[3] = transparency is not null && i < transparency.Length ? transparency[i] : (byte)255;
						break;
					}
				case 4:
					into[0] = into[1] = into[2] = (byte)Sample( row, x * 2 );
					into[3] = (byte)Sample( row, x * 2 + 1 );
					break;
				case 6:
					into[0] = (byte)Sample( row, x * 4 );
					into[1] = (byte)Sample( row, x * 4 + 1 );
					into[2] = (byte)Sample( row, x * 4 + 2 );
					into[3] = (byte)Sample( row, x * 4 + 3 );
					break;
			}
		}

		// A pixel's samples at full precision, packed, to compare with a tRNS colour key
		long Raw( ReadOnlySpan<byte> row, int x, int channels )
		{
			long key = 0;
			for ( int c = 0; c < channels; c++ )
			{
				var index = x * channels + c;
				var value = depth == 16 ? BinaryPrimitives.ReadUInt16BigEndian( row[(index * 2)..] ) : depth == 8 ? row[index] : Sample( row, index );
				key = (key << 16) | (long)value;
			}

			return key;
		}

		static long Key( ReadOnlySpan<byte> transparency )
			=> ((long)BinaryPrimitives.ReadUInt16BigEndian( transparency ) << 32) | ((long)BinaryPrimitives.ReadUInt16BigEndian( transparency[2..] ) << 16) | BinaryPrimitives.ReadUInt16BigEndian( transparency[4..] );
	}
}
