using NativeEngine;
using System;

namespace EngineTests;

[TestClass]
public class CubemapSubmissionTest
{
	SceneWorld world;

	[TestCleanup]
	public void Cleanup()
	{
		world?.Delete();
	}

	[TestMethod]
	public void EmptyNativeBracketKeepsTheActiveDescriptorSet()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) != "1" )
			Assert.Inconclusive( "Set SBOX_TEST_GRAPHICS=1 to test descriptor submission." );

		var before = CRenderAttributes.Create();
		var after = CRenderAttributes.Create();
		try
		{
			before.SetGlobalBindlessDescriptorSet();
			CSceneSystem.BeginRenderingViews( true );
			CSceneSystem.FinishRenderingViews();
			CSceneSystem.WaitForRenderingToComplete();
			after.SetGlobalBindlessDescriptorSet();
			Assert.IsTrue( CRenderAttributes.AreEqual( before, after, false ), "An empty scene batch must not consume a descriptor-set copy." );
		}
		finally
		{
			before.DeleteThis();
			after.DeleteThis();
		}
	}

	[TestMethod]
	public void FilteringRunsAfterAllFaces()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) != "1" )
			Assert.Inconclusive( "Set SBOX_TEST_GRAPHICS=1 to test cubemap filtering on the GPU." );

		world = new SceneWorld();
		using var cube = Texture.CreateCube( 64, 64, ImageFormat.RGBA16161616F ).AsRenderTarget().WithUAVBinding().WithMips( 7 ).Finish();
		cube.Clear( Color.Red );
		CubemapRendering.Render( world, cube, Transform.Zero, 1, 100, CubemapRendering.GGXFilterType.Fast );

		// The world is empty, so every face renders black. Filtering an unfinished face, or
		// skipping the filter, leaves the initial red in at least one lower mip.
		for ( int mip = 0; mip < cube.Mips; ++mip )
		{
			using var pixels = cube.GetBitmap( mip );
			int size = 64 >> mip;
			for ( int face = 0; face < 6; ++face )
			{
				var color = pixels.GetPixel( size / 2, face * size + size / 2 );
				Assert.IsTrue( color.r < 0.02f && color.g < 0.02f && color.b < 0.02f, $"Face {face}, mip {mip}: {color}" );
			}
		}
	}
}
