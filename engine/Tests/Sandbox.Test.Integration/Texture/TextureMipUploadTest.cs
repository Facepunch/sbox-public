using System;
using System.IO;

namespace TextureTests;

[TestClass]
public class TextureMipUploadTest
{
	[TestInitialize]
	public void RequireGraphics()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) != "1" )
			Assert.Inconclusive( "Set SBOX_TEST_GRAPHICS=1 to verify uploaded GPU pixels." );
	}

	[TestMethod]
	[DataRow( 1, 1, 1, 1 )]
	[DataRow( 1, 1, 5, 1 )]
	[DataRow( 8, 8, 1, 4 )]
	[DataRow( 8, 8, 3, 4 )]
	[DataRow( 16, 8, 5, 5 )]
	[DataRow( 3, 7, 3, 3 )]
	[DataRow( 64, 4, 17, 7 )]
	[DataRow( 128, 256, 3, 9 )]
	public void ArrayPreservesEveryPixelInEveryAuthoredMip( int width, int height, int layers, int mips )
	{
		foreach ( var format in new[] { ImageFormat.RGBA8888, ImageFormat.BGRA8888 } )
		{
			var data = BuildData( width, height, layers, mips, format );
			using var texture = Texture.CreateArray( width, height, layers, format )
				.WithMips( mips ).WithData( data ).WithStaticUsage().Finish();
			AssertDescriptor( texture, width, height, layers, mips );
			AssertPixels( texture, width, height, layers, mips );
		}
	}

	[TestMethod]
	[DataRow( 1, 1, 1 )]
	[DataRow( 8, 8, 4 )]
	[DataRow( 16, 8, 5 )]
	[DataRow( 3, 7, 3 )]
	[DataRow( 64, 4, 7 )]
	public void OrdinaryTexturePreservesAuthoredMips( int width, int height, int mips )
	{
		using var texture = Texture.Create( width, height )
			.WithMips( mips ).WithData( BuildData( width, height, 1, mips ) ).WithStaticUsage().Finish();
		AssertDescriptor( texture, width, height, 1, mips );
		AssertPixels( texture, width, height, 1, mips );
	}

	[TestMethod]
	[DataRow( 8, 1 )]
	[DataRow( 8, 4 )]
	[DataRow( 3, 2 )]
	public void CubemapPreservesEveryFaceAndMip( int size, int mips )
	{
		using var texture = Texture.CreateCube( size, size )
			.WithMips( mips ).WithData( BuildData( size, size, 6, mips ) ).WithStaticUsage().Finish();
		AssertDescriptor( texture, size, size, 6, mips );
		AssertPixels( texture, size, size, 6, mips );
	}

	[TestMethod]
	[DataRow( 4, 4, 3, 3 )]
	[DataRow( 8, 4, 5, 4 )]
	[DataRow( 3, 7, 3, 3 )]
	public void VolumePreservesPixelsWhileDepthShrinks( int width, int height, int depth, int mips )
	{
		using var texture = Texture.CreateVolume( width, height, depth )
			.WithMips( mips ).WithData( BuildData( width, height, depth, mips, volume: true ) ).WithStaticUsage().Finish();
		AssertDescriptor( texture, width, height, depth, mips );
		AssertPixels( texture, width, height, depth, mips, volume: true );
	}

	[TestMethod]
	[DataRow( 1 )]
	[DataRow( 3 )]
	[DataRow( 5 )]
	public void BaseOnlyArrayGeneratesMipsForEveryLayer( int layers )
	{
		const int size = 8;
		const int mips = 4;
		using var texture = Texture.CreateArray( size, size, layers )
			.WithMips( mips ).WithData( BuildData( size, size, layers, 1, solid: true ) ).Finish();
		AssertDescriptor( texture, size, size, layers, mips );
		AssertPixels( texture, size, size, layers, mips, solid: true );
	}

	[TestMethod]
	public void DeclaredDataLengthExcludesTrailingCapacity()
	{
		var payload = BuildData( 8, 8, 3, 4 );
		var buffer = new byte[payload.Length + 257];
		Array.Fill( buffer, (byte)0xcd );
		payload.CopyTo( buffer, 0 );
		using var texture = Texture.CreateArray( 8, 8, 3 )
			.WithMips( 4 ).WithData( buffer, payload.Length ).WithStaticUsage().Finish();
		AssertPixels( texture, 8, 8, 3, 4 );
	}

	[TestMethod]
	[DataRow( ImageFormat.DXT1, 1 )]
	[DataRow( ImageFormat.DXT1, 3 )]
	[DataRow( ImageFormat.DXT5, 1 )]
	[DataRow( ImageFormat.DXT5, 5 )]
	public void CompressedArrayPreservesEveryLayerAndMip( ImageFormat format, int layers )
	{
		const int size = 64;
		const int mips = 5;
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		for ( var mip = mips - 1; mip >= 0; mip-- )
		{
			var width = size >> mip;
			for ( var layer = 0; layer < layers; layer++ )
			{
				var color = CompressedColor( layer, mip );
				var rgb565 = (ushort)(((color.r >> 3) << 11) | ((color.g >> 2) << 5) | (color.b >> 3));
				for ( var block = 0; block < (width / 4) * (width / 4); block++ )
				{
					if ( format == ImageFormat.DXT5 )
						writer.Write( new byte[] { 255, 255, 0, 0, 0, 0, 0, 0 } );
					writer.Write( rgb565 );
					writer.Write( (ushort)0 );
					writer.Write( 0U );
				}
			}
		}
		using var texture = Texture.CreateArray( size, size, layers, format )
			.WithMips( mips ).WithData( stream.ToArray() ).WithStaticUsage().Finish();
		AssertDescriptor( texture, size, size, layers, mips );
		for ( var mip = 0; mip < mips; mip++ )
			for ( var layer = 0; layer < layers; layer++ )
			{
				var width = size >> mip;
				var expected = Enumerable.Repeat( CompressedColor( layer, mip ), width * width ).ToArray();
				var actual = new Color32[expected.Length];
				texture.GetPixels( (0, 0, width, width), layer, mip, actual.AsSpan(), ImageFormat.RGBA8888 );
				CollectionAssert.AreEqual( expected, actual, $"Compressed layer {layer}, mip {mip}." );
			}
	}

	[TestMethod]
	[DataRow( ImageFormat.R32F, 1, false )]
	[DataRow( ImageFormat.RGBA32323232F, 4, false )]
	[DataRow( ImageFormat.RGBA16161616F, 4, true )]
	public void FloatingPointArrayPreservesSampleBits( ImageFormat format, int channels, bool half )
	{
		const int size = 8;
		const int layers = 3;
		const int mips = 4;
		byte[] Level( int width, int layer, int mip )
		{
			using var stream = new MemoryStream();
			using var writer = new BinaryWriter( stream );
			for ( var pixel = 0; pixel < width * width; pixel++ )
				for ( var channel = 0; channel < channels; channel++ )
				{
					var value = (layer * 31 + mip * 7 + pixel + channel + 1) / 127.0f;
					if ( half ) writer.Write( BitConverter.HalfToUInt16Bits( (Half)value ) );
					else writer.Write( value );
				}
			return stream.ToArray();
		}
		using var payload = new MemoryStream();
		for ( var mip = mips - 1; mip >= 0; mip-- )
			for ( var layer = 0; layer < layers; layer++ )
				payload.Write( Level( size >> mip, layer, mip ) );
		using var texture = Texture.CreateArray( size, size, layers, format )
			.WithMips( mips ).WithData( payload.ToArray() ).WithStaticUsage().Finish();
		AssertDescriptor( texture, size, size, layers, mips );
		for ( var mip = 0; mip < mips; mip++ )
			for ( var layer = 0; layer < layers; layer++ )
			{
				var width = size >> mip;
				var expected = Level( width, layer, mip );
				var actual = new byte[expected.Length];
				texture.GetPixels( (0, 0, width, width), layer, mip, actual.AsSpan(), format );
				CollectionAssert.AreEqual( expected, actual, $"Float layer {layer}, mip {mip}." );
			}
	}

	private static Color32 Pixel( int layer, int mip, int x, int y, bool solid )
	{
		if ( solid ) { mip = 0; x = 0; y = 0; }
		return new Color32( (byte)(17 + layer * 31 + mip * 13 + x * 7), (byte)(31 + mip * 41 + y * 9),
			(byte)(197 - layer * 19 + x * 3 + y * 5), (byte)(223 - mip * 7 - layer * 3) );
	}

	private static Color32 CompressedColor( int layer, int mip )
	{
		var value = layer * 3 + mip;
		return new Color32( (byte)((value & 1) != 0 ? 255 : 0), (byte)((value & 2) != 0 ? 255 : 0), (byte)((value & 4) != 0 ? 255 : 0), 255 );
	}

	private static byte[] BuildData( int width, int height, int layers, int mips, ImageFormat format = ImageFormat.RGBA8888, bool volume = false, bool solid = false )
	{
		using var stream = new MemoryStream();
		for ( var mip = mips - 1; mip >= 0; mip-- )
			for ( var layer = 0; layer < (volume ? Math.Max( 1, layers >> mip ) : layers); layer++ )
				for ( var y = 0; y < Math.Max( 1, height >> mip ); y++ )
					for ( var x = 0; x < Math.Max( 1, width >> mip ); x++ )
					{
						var color = Pixel( layer, mip, x, y, solid );
						stream.Write( format == ImageFormat.BGRA8888
							? new byte[] { color.b, color.g, color.r, color.a }
							: new byte[] { color.r, color.g, color.b, color.a } );
					}
		return stream.ToArray();
	}

	private static void AssertDescriptor( Texture texture, int width, int height, int depth, int mips )
	{
		Assert.IsTrue( texture.IsValid && !texture.IsError );
		Assert.AreEqual( width, texture.Width );
		Assert.AreEqual( height, texture.Height );
		Assert.AreEqual( depth, texture.Depth );
		Assert.AreEqual( mips, texture.Mips );
	}

	private static void AssertPixels( Texture texture, int width, int height, int layers, int mips, bool volume = false, bool solid = false )
	{
		for ( var mip = 0; mip < mips; mip++ )
		{
			var w = Math.Max( 1, width >> mip );
			var h = Math.Max( 1, height >> mip );
			for ( var layer = 0; layer < (volume ? Math.Max( 1, layers >> mip ) : layers); layer++ )
			{
				var expected = new Color32[w * h];
				for ( var y = 0; y < h; y++ )
					for ( var x = 0; x < w; x++ )
						expected[y * w + x] = Pixel( layer, mip, x, y, solid );
				var actual = new Color32[expected.Length];
				if ( volume )
					texture.GetPixels3D( (0, 0, layer, w, h, 1), mip, actual.AsSpan(), ImageFormat.RGBA8888 );
				else
					texture.GetPixels( (0, 0, w, h), layer, mip, actual.AsSpan(), ImageFormat.RGBA8888 );
				CollectionAssert.AreEqual( expected, actual, $"Layer {layer}, mip {mip}." );
			}
		}
	}
}
