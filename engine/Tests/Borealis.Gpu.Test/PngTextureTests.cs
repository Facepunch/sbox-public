using Borealis.Gpu;
using Borealis.Source2;
using Borealis.Source2.Compression;
using Borealis.Source2.Materials;
using Borealis.Source2.Resources;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace BorealisGpuTests;

/// <summary>
/// Compiled textures of the <c>PNG_RGBA8888</c> format (an image file a game ships, compiled as is: a PNG in place of the
/// mips), read as 8-bit RGBA (<see cref="Png"/>).
/// </summary>
[TestClass]
public class PngTextureTests
{
	/// <summary>
	/// A core RGBA8888 texture made a PNG one, its pixels written as a PNG with every row filter: it reads as the same
	/// pixels, as RGBA8, from the whole file and from the prefix <see cref="TextureFile.PrefixNeeded"/> asks for.
	/// </summary>
	[TestMethod]
	public void PngTextureReadsAsItsPixels()
	{
		var content = ContentFiles.FindGame();
		var original = content.TryRead( "materials/dev/color_test_image.vtex" );
		if ( original is null ) Assert.Inconclusive( "No game content" );

		var texture = TextureFile.Read( original );
		Assert.AreEqual( Format.RGBA8Unorm, texture.Format, "the original is RGBA8888" );
		var pixels = texture.MipData[0];

		// The same header, its format PNG_RGBA8888 and one mip, then the PNG
		var resource = ResourceFile.Read( original );
		var (data, _) = resource.Find( "DATA" );
		var png = Encode( texture.Width, texture.Height, 6, 8, pixels, interlaced: false );
		var file = new byte[resource.HeaderSize + png.Length];
		original.AsSpan( 0, resource.HeaderSize ).CopyTo( file );
		file[data + 26] = 16;
		file[data + 27] = 1;
		png.CopyTo( file, resource.HeaderSize );

		var read = TextureFile.Read( file );
		Assert.AreEqual( Format.RGBA8Unorm, read.Format );
		Assert.AreEqual( System.Numerics.BitOperations.Log2( (uint)Math.Max( texture.Width, texture.Height ) ) + 1, read.Mips, "one listed mip: every mip, made from the image" );
		Assert.AreEqual( (texture.Width, texture.Height), (read.Width, read.Height) );
		CollectionAssert.AreEqual( pixels, read.MipData[0], "the PNG's pixels" );

		var needed = TextureFile.PrefixNeeded( file.AsSpan( 0, resource.HeaderSize + 64 ).ToArray(), TextureFile.Tail );
		Assert.IsTrue( needed > resource.HeaderSize + 64, "a prefix without the whole PNG asks for more" );
		Assert.AreEqual( file.Length, TextureFile.PrefixNeeded( file, TextureFile.Tail ), "the whole PNG is the whole file" );
	}

	/// <summary>
	/// A PNG texture whose header has more than one mip gets them made from its image, as Source 2 makes them on load: each
	/// texel of the next the average of 2x2 of the one before. It drew with its first mip alone, and read back nothing past it.
	/// </summary>
	[TestMethod]
	public void PngTextureMipsAreMadeFromItsImage()
	{
		var content = ContentFiles.FindGame();
		var original = content.TryRead( "materials/dev/color_test_image.vtex" );
		if ( original is null ) Assert.Inconclusive( "No game content" );

		var texture = TextureFile.Read( original );
		var pixels = texture.MipData[0];
		var resource = ResourceFile.Read( original );
		var (data, _) = resource.Find( "DATA" );
		var png = Encode( texture.Width, texture.Height, 6, 8, pixels, interlaced: false );
		var file = new byte[resource.HeaderSize + png.Length];
		original.AsSpan( 0, resource.HeaderSize ).CopyTo( file );
		file[data + 26] = 16;
		file[data + 27] = 3;
		png.CopyTo( file, resource.HeaderSize );

		var read = TextureFile.Read( file );
		Assert.AreEqual( 3, read.Mips );
		CollectionAssert.AreEqual( pixels, read.MipData[0], "the PNG's pixels" );
		for ( int mip = 1; mip < 3; mip++ )
		{
			int width = Math.Max( texture.Width >> mip, 1 ), height = Math.Max( texture.Height >> mip, 1 ), above = Math.Max( texture.Width >> (mip - 1), 1 );
			Assert.AreEqual( width * height * 4, read.MipData[mip].Length, $"mip {mip}'s size" );
			var source = read.MipData[mip - 1];
			for ( int channel = 0; channel < 4; channel++ )
			{
				var sum = source[channel] + source[4 + channel] + source[above * 4 + channel] + source[above * 4 + 4 + channel];
				Assert.AreEqual( (byte)((sum + 2) >> 2), read.MipData[mip][channel], $"mip {mip}'s first texel, channel {channel}" );
			}
		}
	}

	/// <summary>Palettes with transparency at 4 bits, 16-bit grey with alpha, and interlaced images decode as their pixels.</summary>
	[TestMethod]
	public void PngColourTypesDecode()
	{
		const int width = 13, height = 7;
		var random = new Random( 1 );

		// 16 colours, a palette index a pixel at 4 bits, the first four partly transparent
		var palette = new byte[16 * 3];
		random.NextBytes( palette );
		byte[] alphas = [0, 64, 128, 200];
		var indices = new byte[width * height];
		for ( int i = 0; i < indices.Length; i++ ) indices[i] = (byte)random.Next( 16 );
		var expected = new byte[width * height * 4];
		for ( int i = 0; i < indices.Length; i++ )
		{
			palette.AsSpan( indices[i] * 3, 3 ).CopyTo( expected.AsSpan( i * 4 ) );
			expected[i * 4 + 3] = indices[i] < alphas.Length ? alphas[indices[i]] : (byte)255;
		}

		var paletted = Encode( width, height, 3, 4, indices, interlaced: false, palette, alphas );
		CollectionAssert.AreEqual( expected, Png.Decode( paletted ).Rgba, "4-bit palette" );

		// 16-bit grey and alpha: the high bytes
		var grey = new byte[width * height * 4];
		random.NextBytes( grey );
		for ( int i = 0; i < width * height; i++ )
		{
			expected[i * 4] = expected[i * 4 + 1] = expected[i * 4 + 2] = grey[i * 4];
			expected[i * 4 + 3] = grey[i * 4 + 2];
		}

		CollectionAssert.AreEqual( expected, Png.Decode( Encode( width, height, 4, 16, grey, interlaced: false ) ).Rgba, "16-bit grey and alpha" );

		// RGBA, interlaced
		var rgba = new byte[width * height * 4];
		random.NextBytes( rgba );
		CollectionAssert.AreEqual( rgba, Png.Decode( Encode( width, height, 6, 8, rgba, interlaced: true ) ).Rgba, "interlaced" );
		CollectionAssert.AreEqual( rgba, Png.Decode( Encode( width, height, 6, 8, rgba, interlaced: false ) ).Rgba, "filtered" );
	}

	/// <summary>
	/// A PNG of <paramref name="samples"/> (rows packed at <paramref name="depth"/> bits a sample), each row (or each pass's
	/// row) filtered with the next of the five filters.
	/// </summary>
	static byte[] Encode( int width, int height, int colorType, int depth, byte[] samples, bool interlaced, byte[] palette = null, byte[] alphas = null )
	{
		var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
		var bits = channels * depth;
		var bpp = Math.Max( bits / 8, 1 );

		// A sample's bytes: indices given one a byte for sub-byte depths, 16-bit samples two bytes each
		int SampleBytes( int x, int y ) => depth >= 8 ? (y * width + x) * channels * (depth / 8) : y * width + x;

		using var raw = new MemoryStream();
		var filter = 0;
		void Rows( int x0, int y0, int dx, int dy )
		{
			var w = (width - x0 + dx - 1) / dx;
			var h = (height - y0 + dy - 1) / dy;
			if ( w <= 0 || h <= 0 ) return;
			var stride = (w * bits + 7) / 8;
			var previous = new byte[stride];
			for ( int r = 0; r < h; r++ )
			{
				var row = new byte[stride];
				for ( int c = 0; c < w; c++ )
				{
					var at = SampleBytes( x0 + c * dx, y0 + r * dy );
					if ( depth >= 8 ) samples.AsSpan( at, bits / 8 ).CopyTo( row.AsSpan( c * bits / 8 ) );
					else row[c * depth / 8] |= (byte)(samples[at] << (8 - depth - c * depth % 8));
				}

				var f = (byte)(filter++ % 5);
				raw.WriteByte( f );
				for ( int i = 0; i < stride; i++ )
				{
					int a = i >= bpp ? row[i - bpp] : 0, b = r > 0 ? previous[i] : 0, c = r > 0 && i >= bpp ? previous[i - bpp] : 0;
					int p = a + b - c, pa = Math.Abs( p - a ), pb = Math.Abs( p - b ), pc = Math.Abs( p - c );
					var predicted = f switch { 1 => a, 2 => b, 3 => (a + b) >> 1, 4 => pa <= pb && pa <= pc ? a : pb <= pc ? b : c, _ => 0 };
					raw.WriteByte( (byte)(row[i] - predicted) );
				}

				previous = row;
			}
		}

		if ( interlaced )
		{
			int[] sx = [0, 4, 0, 2, 0, 1, 0], sy = [0, 0, 4, 0, 2, 0, 1], dx = [8, 8, 4, 4, 2, 2, 1], dy = [8, 8, 8, 4, 4, 2, 2];
			for ( int pass = 0; pass < 7; pass++ ) Rows( sx[pass], sy[pass], dx[pass], dy[pass] );
		}
		else Rows( 0, 0, 1, 1 );

		using var compressed = new MemoryStream();
		using ( var zlib = new ZLibStream( compressed, CompressionLevel.Fastest, leaveOpen: true ) ) raw.WriteTo( zlib );

		using var png = new MemoryStream();
		png.Write( [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A] );
		var header = new byte[13];
		BinaryPrimitives.WriteInt32BigEndian( header, width );
		BinaryPrimitives.WriteInt32BigEndian( header.AsSpan( 4 ), height );
		header[8] = (byte)depth;
		header[9] = (byte)colorType;
		header[12] = (byte)(interlaced ? 1 : 0);
		Chunk( png, "IHDR", header );
		if ( palette is not null ) Chunk( png, "PLTE", palette );
		if ( alphas is not null ) Chunk( png, "tRNS", alphas );

		// Two data chunks, as encoders split them
		var data = compressed.ToArray();
		Chunk( png, "IDAT", data[..(data.Length / 2)] );
		Chunk( png, "IDAT", data[(data.Length / 2)..] );
		Chunk( png, "IEND", [] );
		return png.ToArray();
	}

	static void Chunk( Stream into, string type, byte[] body )
	{
		Span<byte> word = stackalloc byte[4];
		BinaryPrimitives.WriteInt32BigEndian( word, body.Length );
		into.Write( word );
		var typed = new byte[4 + body.Length];
		System.Text.Encoding.ASCII.GetBytes( type ).CopyTo( typed, 0 );
		body.CopyTo( typed, 4 );
		into.Write( typed );
		BinaryPrimitives.WriteUInt32BigEndian( word, Crc( typed ) );
		into.Write( word );
	}

	static uint Crc( byte[] data )
	{
		var crc = 0xFFFFFFFFu;
		foreach ( var b in data )
		{
			crc ^= b;
			for ( int k = 0; k < 8; k++ ) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
		}

		return ~crc;
	}
}
