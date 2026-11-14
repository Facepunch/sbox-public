using Borealis;
using Borealis.Gpu;
using static BorealisTests.TestScenes;

namespace BorealisTests;

/// <summary>
/// Rendering into a target: which formats write sRGB, and the per-view constants a viewport within a bigger,
/// multisampled target gives the shaders.
/// </summary>
[TestClass]
public class TargetTests
{
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
