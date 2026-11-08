using NativeEngine;
using Sandbox.Rendering;
using System;
using System.Threading;
using System.Runtime.InteropServices;

namespace EngineTests;

[TestClass]
public class CubemapSubmissionTest
{
	SceneWorld world;
	INativeRenderPipeline previousPipeline;

	// Test native batching and camera hooks without the lighting lookup textures,
	// which Texture.Load deliberately leaves null during unit-test startup.
	sealed class HookPipeline : INativeRenderPipeline
	{
		[UnmanagedFunctionPointer( CallingConvention.StdCall )]
		delegate void OnRenderCallback( ManagedRenderSetup_t setup );

		static readonly DelegateFunctionPointer Callback = DelegateFunctionPointer.Get<OnRenderCallback>(
			setup =>
			{
				using ( new Graphics.Scope( in setup ) )
					Graphics.Context.Clear( Color.Black, true, false, false );
				Graphics.OnLayer( (int)Stage.AfterPostProcess, setup );
			} );

		public void AddLayersToView( CRenderPipelineSetup setup )
		{
			AddUIHook( "Test Camera Hook", Stage.AfterPostProcess, setup.m_pView, setup.m_viewport, setup.m_hFinalColor );
		}

		public ISceneLayer AddUIHook( string name, Stage stage, ISceneView view, RenderViewport viewport, SceneViewRenderTargetHandle color )
		{
			var layer = view.AddManagedProceduralLayer( name, viewport, Callback, default, true );
			layer.SetShaderMode( "Forward" );
			layer.SetOutput( color, -1, RenderColorSpace.RENDER_SRGB );
			layer.m_nLayerFlags |= LayerFlags.NeverRemove;
			return layer;
		}

		public void AddSkybox3DLayersToView( CRenderPipelineSetup setup ) { }
		public void OnSceneViewSubmitted( ISceneView view ) { }
		public void WarmUp() { }
		public void Shutdown() { }
	}

	[TestInitialize]
	public void Setup()
	{
		previousPipeline = RenderPipeline.Current;
		RenderPipeline.Current = new HookPipeline();
	}

	[TestCleanup]
	public void Cleanup()
	{
		try { world?.Delete(); }
		finally { RenderPipeline.Current = previousPipeline; }
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

		// The test pipeline clears every face to black. Filtering an unfinished face, or
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

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void FacesShareOneBatchAndKeepTheirArraySlices( bool existingBatch )
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) != "1" )
			Assert.Inconclusive( "Set SBOX_TEST_GRAPHICS=1 to test cubemap rendering on the GPU." );

		world = new SceneWorld();
		using var camera = new SceneCamera( "CubemapSubmissionTest" )
		{
			World = world,
			FieldOfView = 90,
			ZNear = 1,
			ZFar = 100,
			EnablePostProcessing = false
		};
		using var cube = Texture.CreateCube( 16, 16 ).AsRenderTarget().Finish();
		Color[] colors = [Color.Red, Color.Green, Color.Blue, Color.Yellow, Color.Magenta, Color.Cyan];
		int faces = 0;
		camera.OnRenderStageHook += ( stage, _ ) =>
		{
			if ( stage != Stage.AfterPostProcess ) return;
			int face = Graphics.SceneView.GetRenderTargetArrayIndex();
			Interlocked.Or( ref faces, 1 << face );
			Graphics.Context.Clear( colors[face], true, false, false );
		};

		// Seed a known view count so a standalone capture cannot accidentally pass
		// by restoring stats from an earlier six-view capture.
		CSceneSystem.BeginRenderingViews( true );
		CSceneSystem.FinishRenderingViews();
		CSceneSystem.WaitForRenderingToComplete();
		int previousViewCount = CSceneSystem.GetPerFrameStats().m_nNumViewsRendered;
		Assert.AreEqual( 0, previousViewCount );

		if ( existingBatch ) CSceneSystem.BeginRenderingViews( true );
		try
		{
			camera.RenderToCubeTexture( cube );
			Assert.AreEqual( existingBatch, CSceneSystem.IsRenderingViews(), "The camera must preserve the caller's bracket." );
		}
		finally
		{
			if ( existingBatch )
			{
				CSceneSystem.FinishRenderingViews();
				CSceneSystem.WaitForRenderingToComplete();
			}
		}

		Assert.AreEqual( 63, faces, "Every face must execute its hook." );
		Assert.AreEqual( existingBatch ? 6 : previousViewCount, CSceneSystem.GetPerFrameStats().m_nNumViewsRendered,
			existingBatch ? "The caller's batch must count all six views." : "A standalone capture must preserve the main frame's stats." );
		using var pixels = cube.GetBitmap();
		for ( int face = 0; face < 6; ++face )
			Assert.AreEqual( colors[face], pixels.GetPixel( 8, face * 16 + 8 ), $"Face {face}" );
	}
}
