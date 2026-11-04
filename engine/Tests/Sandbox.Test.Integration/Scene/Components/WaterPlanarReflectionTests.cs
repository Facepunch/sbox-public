using NativeEngine;
using Sandbox.Rendering;
using System;

namespace SceneTests.Components;

/// <summary>
/// Each view renders at most one planar reflection, shared by the planar water on its plane.
/// </summary>
[TestClass, DoNotParallelize]
public class WaterPlanarReflectionTests
{
	/// <summary>
	/// The engine renders one planar reflection per view: the plane whose planar water covers the most of the screen
	/// wins, and other water uses screen-space reflections. Water too small on screen gets none.
	/// </summary>
	[TestMethod]
	public void OnlyTheLargestPlanarWaterCaptures()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		int previousDownsample = WaterBody.PlanarDownsampleFactor;
		var scene = new Scene();
		using var scope = scene.Push();
		using var target = Texture.CreateRenderTarget().WithSize( 256, 128 )
			.WithFormat( ImageFormat.RGBA16161616F ).Create( "water-planar-budget" );
		try
		{
			WaterBody.PlanarDownsampleFactor = 1;
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			camera.CustomSize = new Vector2( 256, 128 );
			camera.WorldPosition = new Vector3( -900, 0, 300 );
			camera.WorldRotation = Rotation.LookAt( -camera.WorldPosition );
			camera.FieldOfView = 70;
			camera.ZFar = 20000;

			// A large pool on one plane, a smaller one on another.
			var large = Pool( scene, Vector3.Zero );
			large.Size = new Vector2( 1024 );
			var small = Pool( scene, new Vector3( 0, 500, 40 ) );
			small.Size = new Vector2( 512 );
			int captures = PlanarReflections.CaptureCount;
			RenderFrames( 4 );
			Assert.AreEqual( captures + 4, PlanarReflections.CaptureCount, "Only one planar reflection may render per view." );
			Assert.AreEqual( 0, PlanarReflections.LastPlane.Distance, 0.01f, "The plane with the most water on screen must win." );

			large.Reflections = WaterBody.ReflectionType.ScreenSpace;
			RenderFrames( 4 );
			Assert.AreEqual( captures + 8, PlanarReflections.CaptureCount );
			Assert.AreEqual( 40, PlanarReflections.LastPlane.Distance, 0.01f, "The next largest takes over when the winner goes away." );

			// From far away the small pool covers too little of the screen to be worth a reflection.
			camera.WorldPosition = new Vector3( -30000, 0, 3000 );
			camera.WorldRotation = Rotation.LookAt( small.WorldPosition - camera.WorldPosition );
			RenderFrames( 4 );
			Assert.AreEqual( captures + 8, PlanarReflections.CaptureCount, "Water this small on screen must not render a reflection." );

			void RenderFrames( int count )
			{
				for ( int frame = 0; frame < count; frame++ )
				{
					EngineGlobal.SourceEnginePanelAppFrame();
					scene.GameTick( 1.0 / 60 );
					scene.PreRender();
					Assert.IsTrue( camera.RenderToTexture( target, new ViewSetup
					{
						EnablePostprocessing = false,
						EnableUI = false,
						EnableDebugOverlays = false
					} ) );
					g_pRenderDevice.Present( IntPtr.Zero );
					g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
					EngineLoop.DrainFrameEndDisposables();
					RenderTarget.EndOfFrame();
					EngineLoop.RunAsyncTasks();
				}
			}
		}
		finally
		{
			WaterBody.PlanarDownsampleFactor = previousDownsample;
			scene.Destroy();
		}
	}

	static void RenderFrames( Scene scene, CameraComponent camera, Texture target, int count )
	{
		for ( int frame = 0; frame < count; frame++ )
		{
			EngineGlobal.SourceEnginePanelAppFrame();
			scene.GameTick( 1.0 / 60 );
			scene.PreRender();
			Assert.IsTrue( camera.RenderToTexture( target, new ViewSetup
			{
				EnablePostprocessing = false,
				EnableUI = false,
				EnableDebugOverlays = false
			} ) );
			g_pRenderDevice.Present( IntPtr.Zero );
			g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
			EngineLoop.DrainFrameEndDisposables();
			RenderTarget.EndOfFrame();
			EngineLoop.RunAsyncTasks();
		}
	}

	static WaterBody Pool( Scene scene, Vector3 position )
	{
		var water = scene.CreateObject().Components.Create<WaterBody>();
		water.WorldPosition = position;
		water.Size = new Vector2( 256 );
		water.SetCaustics( false );
		water.Underwater = false;
		water.Effects = false;
		water.DisableRefraction();
		water.ClearDetail();
		water.MaterialCopy().Set( "SurfaceColor", new Color( 0.02f, 0.02f, 0.03f ) );
		water.MaterialCopy().Set( "Opacity", (float)(1) );
		water.MaterialCopy().Set( "g_fRoughness", 0.02f );
		water.Reflections = WaterBody.ReflectionType.Planar;
		return water;
	}
}
