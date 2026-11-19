using System;
using System.Text;

namespace ResourceTests;

[TestClass]
public class ImageDecoderBoundsTests
{
	static byte[] Psd( int mode, int channels, int width, int height, byte[] colorMode, byte[] rle )
	{
		var data = new System.Collections.Generic.List<byte>();
		void U16( int v ) { data.Add( (byte)(v >> 8) ); data.Add( (byte)v ); }
		void U32( int v ) { U16( v >> 16 ); U16( v & 0xffff ); }

		data.AddRange( "8BPS"u8.ToArray() );
		U16( 1 );
		data.AddRange( new byte[6] );
		U16( channels );
		U32( height );
		U32( width );
		U16( 8 );
		U16( mode );
		U32( colorMode.Length );
		data.AddRange( colorMode );
		U32( 0 );
		U32( 0 );
		U16( 1 );
		data.AddRange( new byte[2 * height * channels] );
		data.AddRange( rle );
		return data.ToArray();
	}

	[TestMethod]
	public void ValidRlePsdDecodes()
	{
		using var bitmap = Bitmap.CreateFromPsdBytes( Psd( 3, 3, 2, 1, [], [0x01, 10, 20, 0xFF, 30, 0x01, 40, 50] ) );

		Assert.IsNotNull( bitmap );
		CollectionAssert.AreEqual( new byte[] { 10, 30, 40, 255, 20, 30, 50, 255 }, bitmap.GetBuffer().ToArray() );
	}

	[TestMethod]
	public void RunLongerThanRowIsRejected()
	{
		using var literal = Bitmap.CreateFromPsdBytes( Psd( 3, 3, 2, 1, [], [0x05, 1, 2, 3, 4, 5, 6, 0xFF, 30, 0xFF, 40] ) );
		using var repeat = Bitmap.CreateFromPsdBytes( Psd( 3, 3, 2, 1, [], [0x81, 10, 0xFF, 30, 0xFF, 40] ) );

		Assert.IsNull( literal );
		Assert.IsNull( repeat );
	}

	[TestMethod]
	public void TruncatedRlePsdIsRejected()
	{
		using var bitmap = Bitmap.CreateFromPsdBytes( Psd( 3, 3, 2, 1, [], [0x01, 10, 20, 0xFF, 30, 0x01, 40] ) );
		Assert.IsNull( bitmap );
	}

	[TestMethod]
	public void PalettizedPsdWithoutFullPaletteIsRejected()
	{
		using var bitmap = Bitmap.CreateFromPsdBytes( Psd( 2, 1, 2, 1, new byte[3], [0xFF, 200] ) );
		Assert.IsNull( bitmap );
	}

	[TestMethod]
	public void NegativePsdSizeIsRejected()
	{
		using var bitmap = Bitmap.CreateFromPsdBytes( Psd( 3, 3, -1, 1, [], [] ) );
		Assert.IsNull( bitmap );
	}

	[TestMethod]
	public void IesPixelsOutliveGarbageCollection()
	{
		var ies = Encoding.ASCII.GetBytes( "IESNA:LM-63-2002\nTILT=NONE\n1 1000 1 2 1 1 1 0 0 0 1 1 100\n0 90\n0\n100 50\n" );

		using var bitmap = Bitmap.CreateFromIesBytes( ies );
		Assert.IsNotNull( bitmap );

		for ( int i = 0; i < 4; i++ )
		{
			_ = new byte[512 * 512 * 8];
			GC.Collect( GC.MaxGeneration, GCCollectionMode.Forced, true, true );
		}

		Assert.IsTrue( bitmap.GetPixel( 256, 256 ).r > 0.9f );
		Assert.IsTrue( bitmap.GetPixel( 0, 0 ).r < 0.01f );
	}
}
