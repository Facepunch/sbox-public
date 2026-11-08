using NativeEngine;
using Basalt;
using Basalt.Gpu;
using Basalt.Gpu.Source2;
using static BasaltTests.TestScenes;

namespace BasaltTests;

/// <summary>
/// Rendering into a target: sample counts and formats as native reads them, and the per-view constants a
/// viewport within a bigger, multisampled target gives the shaders.
/// </summary>
[TestClass]
public class TargetTests
{
	[TestMethod]
	public void SampleCountsMatchNative()
	{
		// RenderMultisampleTypeNumSamples
		Assert.AreEqual( 1, Source2Formats.Samples( RenderMultisampleType.RENDER_MULTISAMPLE_NONE ) );
		Assert.AreEqual( 1, Source2Formats.Samples( RenderMultisampleType.RENDER_MULTISAMPLE_INVALID ) );
		Assert.AreEqual( 2, Source2Formats.Samples( RenderMultisampleType.RENDER_MULTISAMPLE_2X ) );
		Assert.AreEqual( 4, Source2Formats.Samples( RenderMultisampleType.RENDER_MULTISAMPLE_4X ) );
		Assert.AreEqual( 6, Source2Formats.Samples( RenderMultisampleType.RENDER_MULTISAMPLE_6X ) );
		Assert.AreEqual( 8, Source2Formats.Samples( RenderMultisampleType.RENDER_MULTISAMPLE_8X ) );
		Assert.AreEqual( 16, Source2Formats.Samples( RenderMultisampleType.RENDER_MULTISAMPLE_16X ) );
	}

	[TestMethod]
	public void OnlyFixedPointTargetsWriteSrgb()
	{
		// Float targets hold the linear HDR the shaders write; 8-bit ones are encoded like a swap chain
		Assert.IsTrue( TextureFormat.RGBA16161616F.IsFloat() );
		Assert.IsTrue( TextureFormat.RGBA32323232F.IsFloat() );
		Assert.IsTrue( TextureFormat.R16F.IsFloat() );
		Assert.IsFalse( TextureFormat.RGBA8888.IsFloat() );
		Assert.IsFalse( TextureFormat.RGBA1010102.IsFloat() );
		Assert.IsFalse( TextureFormat.RGBA16161616.IsFloat() );
	}

	[TestMethod]
	public void EveryFormatHasAnEngineFormat()
	{
		// The backend maps by name: a renamed format would only fail when something made one
		foreach ( var format in System.Enum.GetValues<TextureFormat>() )
		{
			if ( format == TextureFormat.Unknown ) continue;
			Assert.AreEqual( format, Source2Formats.From( format.ToEngine() ), format.ToString() );
		}

		Assert.AreEqual( TextureFormat.Unknown, Source2Formats.From( ImageFormat.BC7 ) );
	}

	[TestMethod]
	public void ConstantsDescribeTheViewportWithinItsTarget()
	{
		var view = LookingDownX();
		view.Viewport = new Rect( 100, 50, 800, 600 );
		view.Update();

		var c = new ViewConstants();
		view.FillConstants( ref c, new Vector2( 1600, 1200 ), 0, samples: 4, minZ: RenderSystem.MainWorldMinZ );

		Assert.AreEqual( 4, c.MsaaSampleCount );
		Assert.AreEqual( 0.02f, c.ViewportMinZ );
		Assert.AreEqual( 1.0f, c.ViewportMaxZ );
		Assert.AreEqual( new Vector2( 100, 50 ), c.ViewportOffset );
		Assert.AreEqual( new Vector2( 800, 600 ), c.ViewportSize );
		Assert.AreEqual( new Vector2( 1600, 1200 ), c.RenderTargetSize );
		Assert.AreEqual( new Vector2( 0.5f, 0.5f ), c.ViewportToGBufferRatio );
		Assert.AreEqual( 1.0f / 800, c.InvViewportSize.x, 1e-9f );
		Assert.AreEqual( 1.0f / 1600, c.InvGBufferSize.x, 1e-9f );
	}

	[TestMethod]
	public void ShadowViewsKeepTheWholeDepthRange()
	{
		// Only the main view leaves room for the 3D skybox
		var c = new ViewConstants();
		LookingDownX().FillConstants( ref c, new Vector2( 1000, 1000 ), 0 );

		Assert.AreEqual( 1, c.MsaaSampleCount );
		Assert.AreEqual( 0.0f, c.ViewportMinZ );
	}
}
