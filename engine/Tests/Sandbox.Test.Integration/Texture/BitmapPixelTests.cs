using System;
using System.Linq;
using SkiaSharp;

namespace TextureTests;

[TestClass]
public class BitmapPixelTests
{
	[TestMethod]
	public void AllocationFormatCannotBeOverriddenByAnInitializer()
	{
		var property = typeof( Bitmap ).GetProperty( nameof( Bitmap.IsFloatingPoint ) );
		Assert.IsNull( property.GetSetMethod( nonPublic: true ) );
		using var rgba = new Bitmap( 14, 1 );
		using var hdr = new Bitmap( 14, 1, true );
		Assert.IsFalse( rgba.IsFloatingPoint );
		Assert.AreEqual( 56, rgba.GetBuffer().Length );
		Assert.IsTrue( hdr.IsFloatingPoint );
		Assert.AreEqual( 112, hdr.GetBuffer().Length );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void PixelAccessAndOpacityUseTheAllocationFormat( bool floatingPoint )
	{
		using var bitmap = new Bitmap( 14, 2, floatingPoint );
		var color = new Color( floatingPoint ? 2 : 0.5f, 0.25f, 0.75f, 1 );
		bitmap.SetPixels( Enumerable.Repeat( color, 28 ).ToArray() );
		Assert.IsTrue( bitmap.IsOpaque() );
		Assert.AreEqual( color.r, bitmap.GetPixel( 13, 1 ).r, 0.005f );
		bitmap.SetPixel( 13, 1, color.WithAlpha( 0.25f ) );
		Assert.IsFalse( bitmap.IsOpaque() );
		Assert.AreEqual( 28, bitmap.GetPixels().Length );
		Assert.AreEqual( 28, bitmap.GetPixels16().Length );
		Assert.AreEqual( 28, bitmap.GetPixels32().Length );
		Assert.AreEqual( color.r, bitmap.GetPixels()[27].r, 0.005f );
		Assert.AreEqual( 0.25f, bitmap.GetPixels16()[27].ToColor().a, 0.005f );
		Assert.AreEqual( 1.0f, bitmap.GetPixels()[26].a, 0.005f );
		using var clone = bitmap.Clone();
		Assert.AreEqual( floatingPoint, clone.IsFloatingPoint );
		Assert.AreEqual( bitmap.GetPixel( 13, 1 ), clone.GetPixel( 13, 1 ) );
	}

	/// <summary>
	/// Padding keeps the same half-float pixels as drawing a copy into the smaller rectangle.
	/// </summary>
	[TestMethod]
	public void PaddingPreservesTranslucentHdrPixels()
	{
		using var bitmap = new Bitmap( 4, 4, true );
		var translucent = new Color( 0.1f, 0.2f, 2.94f, 0.47f );
		bitmap.SetPixels( Enumerable.Repeat( translucent, 16 ).ToArray() );

		using var copy = bitmap.Clone();
		using var expected = new Bitmap( 4, 4, true );
		expected.DrawBitmap( copy, new Rect( 1, 1, 2, 2 ) );
		bitmap.InsertPadding( new Sandbox.UI.Margin( 1 ) );

		CollectionAssert.AreEqual( expected.GetBuffer().ToArray(), bitmap.GetBuffer().ToArray() );
	}

	/// <summary>
	/// Bulk RGBA writes clear RGB when alpha rounds to zero and keep the straight color at nonzero alpha.
	/// </summary>
	[TestMethod]
	public void BulkWritesPreserveSkiaAlphaRounding()
	{
		using var bitmap = new Bitmap( 3, 1 );
		bitmap.SetPixels( [new Color( 1, 0, 1, 0 ), new Color( 1, 0, 1, 0.001f ), new Color32( 213, 31, 84, 1 )] );
		CollectionAssert.AreEqual( new Color32[] { default, default, new( 213, 31, 84, 1 ) }, bitmap.GetPixels32() );
	}

	[TestMethod]
	public void InvalidNativeStorageIsRejected()
	{
		Assert.ThrowsException<InvalidOperationException>( () =>
		{
			using var bitmap = new Bitmap( new SKBitmap( new SKImageInfo( 2, 2, SKColorType.Alpha8 ) ) );
		} );
		Assert.ThrowsException<InvalidOperationException>( () =>
		{
			using var bitmap = new Bitmap( new SKBitmap( new SKImageInfo( 2, 2, SKColorType.Rgba8888 ), 16 ) );
		} );
		using var unallocated = new SKBitmap();
		unallocated.InstallPixels( new SKImageInfo( 2, 2, SKColorType.RgbaF16 ), IntPtr.Zero );
		Assert.ThrowsException<InvalidOperationException>( () => new Bitmap( unallocated ) );
	}

	[TestMethod]
	public void PixelArgumentsCannotExceedTheBuffer()
	{
		using var bitmap = new Bitmap( 14, 1, true );
		Assert.ThrowsException<ArgumentException>( () => bitmap.SetPixels( new Color[15] ) );
		Assert.ThrowsException<ArgumentException>( () => bitmap.SetPixelData( new byte[111], true ) );
		Assert.ThrowsException<ArgumentException>( () => bitmap.SetPixelData( new byte[113], false ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => bitmap.SetPixel( int.MaxValue, 0, Color.Red ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => bitmap.GetPixel( 0, int.MaxValue ) );
		Assert.ThrowsException<OverflowException>( () => new Bitmap( 16384, 16384, true ) );
		bitmap.Dispose();
		Assert.ThrowsException<ObjectDisposedException>( () => bitmap.GetPixels() );
		Assert.ThrowsException<ObjectDisposedException>( () => bitmap.GetPixels16() );
		Assert.ThrowsException<ObjectDisposedException>( () => bitmap.GetPixels32() );
		Assert.ThrowsException<ObjectDisposedException>( () => bitmap.SetPixels( new Color[14] ) );
		Assert.ThrowsException<ObjectDisposedException>( () => bitmap.SetPixel( 0, 0, Color.Red ) );
		Assert.ThrowsException<ObjectDisposedException>( () => bitmap.GetBuffer() );
		Assert.ThrowsException<ObjectDisposedException>( () => bitmap.IsOpaque() );
	}
}
