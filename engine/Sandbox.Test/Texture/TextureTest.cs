using System;

namespace TestTexture;

[TestClass]
public class TextureTest
{
	[TestMethod]
	public void Copy()
	{
		var src = Texture.Create( 1, 1 ).Finish();
		var dst = Texture.Create( 1, 1 ).Finish();

		try
		{
			Graphics.CopyTexture( src, dst );
		}
		catch ( Exception ex )
		{
			Assert.Fail( $"Valid CopyTexture call threw an exception: {ex}" );
		}

		try
		{
			Graphics.CopyTexture( src, dst, srcMipSlice: 0, srcArraySlice: 0, dstMipSlice: 0, dstArraySlice: 0 );
		}
		catch ( Exception ex )
		{
			Assert.Fail( $"Valid CopyTexture call threw an exception: {ex}" );
		}

		// Out-of-range mip on src
		Assert.ThrowsException<ArgumentException>( () =>
		{
			Graphics.CopyTexture( src, dst, srcMipSlice: 1, srcArraySlice: 0, dstMipSlice: 0, dstArraySlice: 0 );
		} );

		// Out-of-range array slice on src
		Assert.ThrowsException<ArgumentException>( () =>
		{
			Graphics.CopyTexture( src, dst, srcMipSlice: 0, srcArraySlice: 1, dstMipSlice: 0, dstArraySlice: 0 );
		} );

		// Out-of-range mip on dst
		Assert.ThrowsException<ArgumentException>( () =>
		{
			Graphics.CopyTexture( src, dst, srcMipSlice: 0, srcArraySlice: 0, dstMipSlice: 1, dstArraySlice: 0 );
		} );

		// Out-of-range array slice on dst
		Assert.ThrowsException<ArgumentException>( () =>
		{
			Graphics.CopyTexture( src, dst, srcMipSlice: 0, srcArraySlice: 0, dstMipSlice: 0, dstArraySlice: 1 );
		} );
	}

	[TestMethod]
	public void GetPixelsNegativeDimensions()
	{
		var texture = Texture.Create( 128, 128 ).Finish();
		var buffer = new Color32[1];

		Assert.ThrowsException<ArgumentException>( () =>
		{
			texture.GetPixels( (0, 0, -1, 1), 0, 0, buffer.AsSpan(), ImageFormat.RGBA8888 );
		} );

		Assert.ThrowsException<ArgumentException>( () =>
		{
			texture.GetPixels( (0, 0, 1, -1), 0, 0, buffer.AsSpan(), ImageFormat.RGBA8888 );
		} );

		Assert.ThrowsException<ArgumentException>( () =>
		{
			texture.GetPixels( (0, 0, -1, -1), 0, 0, buffer.AsSpan(), ImageFormat.RGBA8888 );
		} );

		Assert.ThrowsException<ArgumentException>( () =>
		{
			texture.GetPixels( (0, 0, 0, 1), 0, 0, buffer.AsSpan(), ImageFormat.RGBA8888 );
		} );

		Assert.ThrowsException<ArgumentException>( () =>
		{
			texture.GetPixels( (0, 0, 1, 0), 0, 0, buffer.AsSpan(), ImageFormat.RGBA8888 );
		} );
	}

	[TestMethod]
	public void GetPixels3DNegativeDimensions()
	{
		var texture = Texture.CreateVolume( 128, 128, 4 ).Finish();
		var buffer = new Color32[1];

		Assert.ThrowsException<ArgumentException>( () =>
		{
			texture.GetPixels3D( (0, 0, 0, -1, 1, 1), 0, buffer.AsSpan(), ImageFormat.RGBA8888 );
		} );

		Assert.ThrowsException<ArgumentException>( () =>
		{
			texture.GetPixels3D( (0, 0, 0, 1, -1, 1), 0, buffer.AsSpan(), ImageFormat.RGBA8888 );
		} );

		Assert.ThrowsException<ArgumentException>( () =>
		{
			texture.GetPixels3D( (0, 0, 0, 1, 1, -1), 0, buffer.AsSpan(), ImageFormat.RGBA8888 );
		} );

		Assert.ThrowsException<ArgumentException>( () =>
		{
			texture.GetPixels3D( (0, 0, 0, -1, -1, -1), 0, buffer.AsSpan(), ImageFormat.RGBA8888 );
		} );
	}

	[TestMethod]
	public void GetPixelsAsyncNegativeDimensions()
	{
		var texture = Texture.Create( 128, 128 ).Finish();

		Assert.ThrowsException<ArgumentException>( () =>
		{
			texture.GetPixelsAsync<Color32>( _ => { }, ImageFormat.RGBA8888, (0, 0, -1, 1), 0, 0 );
		} );

		Assert.ThrowsException<ArgumentException>( () =>
		{
			texture.GetPixelsAsync<Color32>( _ => { }, ImageFormat.RGBA8888, (0, 0, 1, -1), 0, 0 );
		} );

		Assert.ThrowsException<ArgumentException>( () =>
		{
			texture.GetPixelsAsync<Color32>( _ => { }, ImageFormat.RGBA8888, (0, 0, -1, -1), 0, 0 );
		} );
	}

	[TestMethod]
	public void GetPixelsAsync3DNegativeDimensions()
	{
		var texture = Texture.CreateVolume( 128, 128, 4 ).Finish();

		Assert.ThrowsException<ArgumentException>( () =>
		{
			texture.GetPixelsAsync3D<Color32>( _ => { }, ImageFormat.RGBA8888, (0, 0, 0, -1, 1, 1), 0 );
		} );

		Assert.ThrowsException<ArgumentException>( () =>
		{
			texture.GetPixelsAsync3D<Color32>( _ => { }, ImageFormat.RGBA8888, (0, 0, 0, 1, -1, 1), 0 );
		} );

		Assert.ThrowsException<ArgumentException>( () =>
		{
			texture.GetPixelsAsync3D<Color32>( _ => { }, ImageFormat.RGBA8888, (0, 0, 0, -1, -1, 1), 0 );
		} );

		Assert.ThrowsException<ArgumentException>( () =>
		{
			texture.GetPixelsAsync3D<Color32>( _ => { }, ImageFormat.RGBA8888, (0, 0, 0, 1, 1, -1), 0 );
		} );
	}
}
