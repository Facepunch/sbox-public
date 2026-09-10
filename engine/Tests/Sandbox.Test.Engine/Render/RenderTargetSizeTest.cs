namespace RenderTests;

/// <summary>
/// Downsampled render targets divide the viewport by a factor. A tall enough factor against
/// a short viewport used to round an axis to zero and throw out of the bloom command list.
/// </summary>
[TestClass]
public class RenderTargetSizeTest
{
	[DataTestMethod]
	[DataRow( 1, 1 )]
	[DataRow( 2, 2 )]
	[DataRow( 4, 3 )]
	[DataRow( 1024, 11 )]
	public void CalculatesMaxMipCountIncludingBaseLevel( int size, int expectedMipCount )
	{
		Assert.AreEqual( expectedMipCount, RenderTarget.CalculateMaxMipCount( size, size ) );
	}

	[DataTestMethod]
	[DataRow( 1920f, 1080f, 1, 1920, 1080 )]
	[DataRow( 1920f, 1080f, 2, 960, 540 )]
	[DataRow( 1920f, 1080f, 4, 480, 270 )]
	public void DividesTheViewport( float width, float height, int sizeFactor, int expectedWidth, int expectedHeight )
	{
		var size = RenderTarget.ScaleDownSize( new Vector2( width, height ), sizeFactor );

		Assert.AreEqual( expectedWidth, size.Width );
		Assert.AreEqual( expectedHeight, size.Height );
	}

	/// <summary>The case that threw - a factor bigger than the viewport is tall.</summary>
	[DataTestMethod]
	[DataRow( 50f, 10f, 16 )]
	[DataRow( 8f, 8f, 64 )]
	[DataRow( 1920f, 1080f, 4096 )]
	public void NeverSmallerThanAPixel( float width, float height, int sizeFactor )
	{
		var size = RenderTarget.ScaleDownSize( new Vector2( width, height ), sizeFactor );

		Assert.IsTrue( size.Width >= 1, $"width was {size.Width}" );
		Assert.IsTrue( size.Height >= 1, $"height was {size.Height}" );
	}
}
