using System;
using System.Runtime.InteropServices;

namespace ContentBuild;

/// <summary>
/// A generated texture compiles its mips on the CPU as a texture made from its image gets them on the GPU: each level
/// half the one before (at least 1), each texel filtered bilinearly from under its center, on straight alpha. An 8-bit
/// texture is sRGB (VK_FORMAT_R8G8B8A8_SRGB), so the GPU filters its colour in linear light and its alpha as it is.
/// </summary>
[TestClass]
public class GeneratedTextureMipTests
{
	static Bitmap Make( int width, int height, params byte[] rgba )
	{
		var bitmap = new Bitmap( width, height );
		rgba.CopyTo( bitmap.GetBuffer() );
		return bitmap;
	}

	[TestMethod]
	public void ColoursAreAveragedWithoutPremultiplyingByAlpha()
	{
		// A transparent red, an opaque green, a half blue and a faint white: premultiplying would darken the result. Each
		// colour channel is half on in linear light, which sRGB encodes as 187.5; alpha is the plain mean, 111.75
		using var bitmap = Make( 2, 2, 255, 0, 0, 0, 0, 255, 0, 255, 0, 0, 255, 128, 255, 255, 255, 64 );
		using var mip = bitmap.NextMip();

		Assert.AreEqual( (1, 1), (mip.Width, mip.Height) );
		CollectionAssert.AreEqual( new byte[] { 188, 188, 188, 112 }, mip.GetBuffer().ToArray() );
	}

	[TestMethod]
	public void ColourIsFilteredInLinearLight()
	{
		// An svg's antialiased edge: a quarter of white in linear light encodes as 137, not the 64 of averaging the
		// bytes; the GPU made 137,137,137,1 from these texels in a corpus image
		using var bitmap = Make( 2, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 255, 255, 255, 5 );
		using var mip = bitmap.NextMip();

		CollectionAssert.AreEqual( new byte[] { 137, 137, 137, 1 }, mip.GetBuffer().ToArray() );
	}

	[TestMethod]
	public void OddSizesFilterFromUnderEachTexelsCenter()
	{
		// 5 wide to 2: centers fall at 0.75 and 3.25 of the source row, so colour is 3/4 of the way from 0 to 40 and 1/4
		// from 120 to 160 in linear light (33.9 and 131.5 encoded), alpha 30 and 130
		var row = new byte[5 * 4];
		for ( var x = 0; x < 5; x++ ) row.AsSpan( x * 4, 4 ).Fill( (byte)(x * 40) );
		using var five = Make( 5, 1, row );
		using var two = five.NextMip();
		Assert.AreEqual( (2, 1), (two.Width, two.Height) );
		CollectionAssert.AreEqual( new byte[] { 34, 34, 34, 30, 131, 131, 131, 130 }, two.GetBuffer().ToArray() );

		// 3 to 1 takes the middle texel, and 1 stays 1
		using var three = Make( 3, 1, 10, 10, 10, 10, 90, 90, 90, 90, 200, 200, 200, 200 );
		using var one = three.NextMip();
		CollectionAssert.AreEqual( new byte[] { 90, 90, 90, 90 }, one.GetBuffer().ToArray() );
		using var still = one.NextMip();
		Assert.AreEqual( (1, 1), (still.Width, still.Height) );
	}

	[TestMethod]
	public void HdrValuesStayAboveOne()
	{
		using var bitmap = new Bitmap( 2, 2, true );
		var texels = MemoryMarshal.Cast<byte, Half>( bitmap.GetBuffer() );
		float[] values = [4, 0, 2, 1, 0, 0, 2, 1, 2, 0, 2, 1, 0, 0, 2, 1];
		for ( var i = 0; i < values.Length; i++ ) texels[i] = (Half)values[i];

		using var mip = bitmap.NextMip();

		Assert.IsTrue( mip.IsFloatingPoint );
		var averaged = MemoryMarshal.Cast<byte, Half>( mip.GetBuffer() );
		CollectionAssert.AreEqual( new[] { 1.5f, 0f, 2f, 1f }, new[] { (float)averaged[0], (float)averaged[1], (float)averaged[2], (float)averaged[3] } );
	}
}
