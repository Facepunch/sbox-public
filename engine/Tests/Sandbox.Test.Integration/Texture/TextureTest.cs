using System;
using Sandbox.UI;

namespace TextureTests;

[TestClass]
public class TextureTest
{
	[TestMethod]
	public void VideoPresentationTrackingOverridesNativeUse()
	{
		using var player = new VideoPlayer();
		var frame = Application.FrameCount;

		try
		{
			player.TrackPresentation( false );
			Application.FrameCount += 3;
			player.Texture.MarkUsed();
			Assert.IsTrue( player.LastPresented > 2 );

			player.TrackPresentation( true );
			Assert.AreEqual( 0, player.LastPresented );
		}
		finally
		{
			Application.FrameCount = frame;
		}
	}

	[TestMethod]
	public void BackgroundPlaybackStateControlsPresentation()
	{
		using var automaticVisible = new VideoPlayer();
		using var automaticOffscreen = new VideoPlayer();
		using var pausedVisible = new VideoPlayer();
		using var runningOffscreen = new VideoPlayer();
		var panel = new Panel();
		panel.Box.Rect = new Rect( 0, 0, 100, 100 );
		var scissor = PanelRenderer.GPUScissor.Single( new Rect( 0, 0, 200, 200 ), BorderRadii.Zero, Matrix.Identity );
		var frame = Application.FrameCount;

		try
		{
			Application.FrameCount += 3;
			bool? onScreen = null;
			PanelRenderer.MarkPresented( automaticVisible.Texture, panel, Matrix.Identity, scissor, ref onScreen );
			Assert.AreEqual( 0, automaticVisible.LastPresented );

			panel.Box.Rect = new Rect( 300, 0, 100, 100 );
			onScreen = null;
			PanelRenderer.MarkPresented( automaticOffscreen.Texture, panel, Matrix.Identity, scissor, ref onScreen );
			Assert.IsTrue( automaticOffscreen.LastPresented > 2 );

			panel.Box.Rect = new Rect( 0, 0, 100, 100 );
			onScreen = null;
			PanelRenderer.MarkPresented( pausedVisible.Texture, panel, Matrix.Identity, scissor, ref onScreen, playbackPaused: true );
			Assert.IsTrue( pausedVisible.LastPresented > 2 );

			panel.Box.Rect = new Rect( 300, 0, 100, 100 );
			onScreen = null;
			PanelRenderer.MarkPresented( runningOffscreen.Texture, panel, Matrix.Identity, scissor, ref onScreen, playbackPaused: false );
			Assert.AreEqual( 0, runningOffscreen.LastPresented );
		}
		finally
		{
			Application.FrameCount = frame;
			panel.Delete( true );
		}
	}

	[TestMethod]
	public void AnimatedImageDoesNotUseVideoVisibilityPolicy()
	{
		using var texture = Texture.Create( 1, 1 ).Finish();
		texture.IsAnimated = true;
		var panel = new Panel();
		panel.Box.Rect = new Rect( 300, 0, 100, 100 );
		var scissor = PanelRenderer.GPUScissor.Single( new Rect( 0, 0, 200, 200 ), BorderRadii.Zero, Matrix.Identity );
		bool? onScreen = null;

		try
		{
			PanelRenderer.MarkPresented( texture, panel, Matrix.Identity, scissor, ref onScreen );
			Assert.IsNull( onScreen );
		}
		finally
		{
			panel.Delete( true );
		}
	}

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

	[TestMethod]
	public void GetPixelsDestArrayBoundsCheck()
	{
		var texture = Texture.Create( 4, 4 ).Finish();

		// Exactly-sized buffer for the whole texture should not trigger the bounds check.
		// The native ReadTexturePixels fails in headless mode. However, we only want to
		// validate that the ArgumentExcpetion does not get thrown for getting the entire
		// texture.
		var exactBuffer = new Color32[4 * 4];
		try
		{
			texture.GetPixels( (0, 0, 4, 4), 0, 0, exactBuffer.AsSpan(), ImageFormat.RGBA8888, (0, 0, 4, 4), 4 );
		}
		catch ( ArgumentException )
		{
			Assert.Fail( "Should not reject a dest array that exactly fits the requested rect" );
		}
		catch ( Exception )
		{
		}

		// Undersized buffer should throw
		var tooSmallBuffer = new Color32[4 * 4 - 1];
		Assert.ThrowsException<ArgumentException>( () =>
		{
			texture.GetPixels( (0, 0, 4, 4), 0, 0, tooSmallBuffer.AsSpan(), ImageFormat.RGBA8888, (0, 0, 4, 4), 4 );
		} );
	}
}
