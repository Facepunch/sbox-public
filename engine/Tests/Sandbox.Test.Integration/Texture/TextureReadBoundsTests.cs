using System;

namespace TextureTests;

// Each span is a slice of a larger backing array so an unchecked native write stays inside managed memory.
[TestClass]
public class TextureReadBoundsTests
{
	[TestMethod]
	public void GetPixelsRejectsNegativeDestinationX()
	{
		using var texture = Texture.Create( 4, 4 ).Finish();
		var backing = new Color32[64];

		Assert.ThrowsException<ArgumentException>( () =>
			texture.GetPixels( (0, 0, 4, 1), 0, 0, backing.AsSpan( 16, 16 ), ImageFormat.RGBA8888, (-2, 0, 4, 1), 4 ) );
	}

	[TestMethod]
	public void GetPixelsRejectsNegativeDestinationY()
	{
		using var texture = Texture.Create( 4, 4 ).Finish();
		var backing = new Color32[64];

		Assert.ThrowsException<ArgumentException>( () =>
			texture.GetPixels( (0, 0, 4, 2), 0, 0, backing.AsSpan( 16, 16 ), ImageFormat.RGBA8888, (0, -1, 4, 2), 4 ) );
	}

	[TestMethod]
	public void GetPixelsRejectsStrideNarrowerThanRow()
	{
		using var texture = Texture.Create( 4, 4 ).Finish();
		var backing = new Color32[64];

		Assert.ThrowsException<ArgumentException>( () =>
			texture.GetPixels( (0, 0, 4, 4), 0, 0, backing.AsSpan( 16, 4 ), ImageFormat.RGBA8888, (0, 0, 4, 4), 0 ) );
	}

	[TestMethod]
	public void GetPixelsRejectsDestinationRectLargerThanSource()
	{
		using var texture = Texture.Create( 4, 4 ).Finish();
		var buffer = new Color32[16];

		Assert.ThrowsException<ArgumentException>( () =>
			texture.GetPixels( (0, 0, 2, 2), 0, 0, buffer.AsSpan(), ImageFormat.RGBA8888, (0, 0, 4, 4), 4 ) );
	}

	[TestMethod]
	public void GetPixelsRejectsDestinationSizeLargerThanSource()
	{
		using var texture = Texture.Create( 4, 4 ).Finish();
		var buffer = new Color32[16];

		Assert.ThrowsException<ArgumentException>( () =>
			texture.GetPixels( (0, 0, 2, 2), 0, 0, buffer.AsSpan(), ImageFormat.RGBA8888, (4, 4) ) );
	}

	[TestMethod]
	public void GetPixelsRejectsSliceOutOfRange()
	{
		using var texture = Texture.Create( 4, 4 ).Finish();
		var buffer = new Color32[16];

		Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
			texture.GetPixels( (0, 0, 4, 4), 100000, 0, buffer.AsSpan(), ImageFormat.RGBA8888 ) );
	}

	[TestMethod]
	public void GetPixelsAcceptsValidSubRect()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) != "1" )
			Assert.Inconclusive( "Set SBOX_TEST_GRAPHICS=1 to read back GPU pixels." );

		using var texture = Texture.Create( 4, 4 ).Finish();
		var buffer = new Color32[16];

		texture.GetPixels( (0, 0, 2, 2), 0, 0, buffer.AsSpan(), ImageFormat.RGBA8888, (1, 1, 2, 2), 4 );
	}

	[TestMethod]
	public void UpdateRejectsNegativeRegion()
	{
		using var texture = Texture.Create( 4, 4 ).Finish();
		var data = new byte[64];

		Assert.ThrowsException<ArgumentException>( () => texture.Update( data, -1, 0, 1, 1 ) );
	}
}
