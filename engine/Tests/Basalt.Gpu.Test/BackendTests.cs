using Basalt.Gpu;
using BasaltGpuSample;

namespace BasaltGpuTests;

/// <summary>
/// Basalt's own renderer, unmodified, rendering through Basalt.Gpu (<c>Basalt.Backend</c>), offscreen.
/// </summary>
[TestClass]
[DoNotParallelize]
public class BackendTests : GpuTest
{
	/// <summary>
	/// The sample's world through a whole Basalt frame: the sky is the view's clear colour, the ground and objects are lit
	/// by the sun, something stands in shadow, and the depth prepass, shadow maps and forward pass all drew.
	/// </summary>
	[TestMethod]
	public void RenderSystemDrawsTheWorld()
	{
		const int width = 640, height = 400;
		using var scene = new BasaltScene( Device );

		Device.BeginFrame();
		var (_, _, color) = scene.Render( width, height, 1.0f );
		var stats = scene.Stats;
		Device.WaitIdle();

		var pixels = ToRgba8( ReadBack( Basalt.Backend.BackendDevice.Unwrap( color ).Texture, Basalt.Backend.BackendDevice.Unwrap( color ).Layout ), width, height );
		Save( pixels, width, height, "basalt-scene" );

		Assert.IsTrue( stats.Draws > 0, "forward drew" );
		Assert.IsTrue( stats.DepthDraws > 0, "the prepass drew" );
		Assert.IsTrue( stats.ShadowDraws > 0, "shadow maps drew" );

		// Sky at the top, ground below it
		var sky = Pixel( pixels, width, width / 2, 2 );
		var ground = Pixel( pixels, width, width / 2, height - 3 );
		Assert.IsTrue( sky.B > sky.R, $"sky is the clear colour: {sky}" );
		Assert.AreNotEqual( sky, ground, "the ground is drawn" );

		// Lit and shadowed ground: the brightest and darkest ground-coloured pixels differ a lot
		int brightest = 0, darkest = 255;
		for ( int y = height / 2; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				var p = Pixel( pixels, width, x, y );
				if ( Math.Abs( p.R - p.G ) > 12 || Math.Abs( p.G - p.B ) > 24 ) continue;
				brightest = Math.Max( brightest, p.G );
				darkest = Math.Min( darkest, p.G );
			}
		}

		Assert.IsTrue( brightest - darkest > 60, $"sunlit and shadowed ground: {darkest}..{brightest}" );
	}

	/// <summary>
	/// Frames keep coming without growing memory once warm: Basalt's upload rings, the backend's constants and the
	/// device's frame ring all recycle.
	/// </summary>
	[TestMethod]
	public void FramesSettle()
	{
		using var scene = new BasaltScene( Device );
		long reserved = 0;

		for ( int frame = 0; frame < 40; frame++ )
		{
			Device.BeginFrame();
			scene.Render( 320, 200, frame / 30f );
			if ( frame == 20 ) reserved = Device.MemoryUsage.Reserved;
		}

		Device.WaitIdle();
		Assert.AreEqual( reserved, Device.MemoryUsage.Reserved );
	}

	/// <summary>Half-float RGBA to 8-bit sRGB, for looking at.</summary>
	static byte[] ToRgba8( byte[] halfs, int width, int height )
	{
		var pixels = new byte[width * height * 4];
		for ( int i = 0; i < width * height * 4; i++ )
		{
			var linear = (float)BitConverter.ToHalf( halfs, i * 2 );
			var encoded = (i & 3) == 3 ? linear : linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow( linear, 1 / 2.4f ) - 0.055f;
			pixels[i] = (byte)Math.Clamp( encoded * 255 + 0.5f, 0, 255 );
		}

		return pixels;
	}
}
