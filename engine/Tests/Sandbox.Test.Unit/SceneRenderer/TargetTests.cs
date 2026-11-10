using NativeEngine;
using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Gpu;
using static SceneRendererTests.TestScenes;

namespace SceneRendererTests;

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
		Assert.AreEqual( 1, ViewTarget.SampleCount( RenderMultisampleType.RENDER_MULTISAMPLE_NONE ) );
		Assert.AreEqual( 1, ViewTarget.SampleCount( RenderMultisampleType.RENDER_MULTISAMPLE_INVALID ) );
		Assert.AreEqual( 2, ViewTarget.SampleCount( RenderMultisampleType.RENDER_MULTISAMPLE_2X ) );
		Assert.AreEqual( 4, ViewTarget.SampleCount( RenderMultisampleType.RENDER_MULTISAMPLE_4X ) );
		Assert.AreEqual( 6, ViewTarget.SampleCount( RenderMultisampleType.RENDER_MULTISAMPLE_6X ) );
		Assert.AreEqual( 8, ViewTarget.SampleCount( RenderMultisampleType.RENDER_MULTISAMPLE_8X ) );
		Assert.AreEqual( 16, ViewTarget.SampleCount( RenderMultisampleType.RENDER_MULTISAMPLE_16X ) );
	}

	[TestMethod]
	public void OnlyFixedPointTargetsWriteSrgb()
	{
		// Float targets hold the linear HDR the shaders write; 8-bit ones are encoded like a swap chain
		Assert.IsTrue( ViewTarget.IsFloatFormat( ImageFormat.RGBA16161616F ) );
		Assert.IsTrue( ViewTarget.IsFloatFormat( ImageFormat.RGBA32323232F ) );
		Assert.IsTrue( ViewTarget.IsFloatFormat( ImageFormat.R16F ) );
		Assert.IsFalse( ViewTarget.IsFloatFormat( ImageFormat.RGBA8888 ) );
		Assert.IsFalse( ViewTarget.IsFloatFormat( ImageFormat.RGBA1010102 ) );
		Assert.IsFalse( ViewTarget.IsFloatFormat( ImageFormat.RGBA16161616 ) );
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
