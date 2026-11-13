using NativeEngine;
using Sandbox.Rendering;
using System;
using System.IO;

namespace SceneTests.Components;

/// <summary>
/// Water material settings must affect the captured scene, not just the surface tint.
/// </summary>
[TestClass, DoNotParallelize]
public class WaterMaterialRenderingTests
{
	/// <summary>
	/// Looking through a transparent surface shows depth fog and projected caustics on a submerged floor.
	/// Doubling intensity doubles the added light in the highlights without producing negative light in troughs.
	/// </summary>
	[TestMethod]
	public void FogAndCausticsAreVisibleFromAbove()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var scene = new Scene();
		using var scope = scene.Push();
		using var target = Texture.CreateRenderTarget().WithSize( 256, 256 )
			.WithFormat( ImageFormat.RGBA16161616F ).Create( "Water fog and caustics" );
		try
		{
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			camera.WorldPosition = Vector3.Up * 256;
			camera.WorldRotation = Rotation.LookAt( Vector3.Down, Vector3.Forward );
			camera.ZNear = 1;
			camera.ZFar = 2048;
			camera.BackgroundColor = Color.Black;
			camera.EnablePostProcessing = false;
			camera.CustomSize = new Vector2( 256 );

			var sun = scene.CreateObject().Components.Create<DirectionalLight>();
			sun.WorldRotation = Rotation.LookAt( Vector3.Down );
			sun.LightColor = Color.White * 0.5f;
			scene.CreateObject().Components.Create<AmbientLight>().Color = Color.White * 0.03f;

			var floor = scene.CreateObject().Components.Create<ModelRenderer>();
			floor.Model = Model.Load( "models/dev/box.vmdl" );
			floor.MaterialOverride = Material.Load( "materials/dev/primary_white.vmat" );
			Assert.IsFalse( floor.Model.IsError || floor.MaterialOverride.IsError );
			floor.WorldPosition = Vector3.Down * 96;
			floor.WorldScale = new Vector3( 16, 16, 0.1f );

			var water = scene.CreateObject().Components.Create<WaterBody>();
			water.Size = new Vector2( 2048 );
			water.Underwater = false;
			water.Effects = false;
			water.SetReflectionsEnabled( false );
			var material = Material.Load( "materials/water/blue.water.vmat" ).CreateCopy();
			water.Material = material;
			material.Set( "Opacity", 0f );
			material.Set( "DetailStrength", 0f );
			material.Set( "CausticsIntensity", 1f );
			material.Set( "CausticsBrightness", 1f );

			var fogged = Capture( "fog-caustics" );
			material.Set( "WaterFogDepthRange", Vector2.Zero );
			var caustics = Capture( "caustics" );
			material.Set( "CausticsIntensity", 2f );
			var doubled = Capture( "caustics-double" );
			material.SetFeature( "F_CAUSTICS", 0 );
			var plain = Capture( "plain" );

			Assert.IsTrue( plain.Average( p => p.r ) > 0.1f, "The submerged floor was not visible." );
			Assert.IsTrue( fogged.Average( p => p.r ) < caustics.Average( p => p.r ) * 0.8f,
				"Depth fog did not attenuate the submerged floor." );
			Assert.IsTrue( fogged.Average( p => p.b ) > fogged.Average( p => p.r ) * 1.5f,
				"The material's fog colour was not visible." );
			float difference = caustics.Zip( plain, ( a, b ) => MathF.Abs( a.r - b.r ) ).Average();
			Assert.IsTrue( difference > 0.005f, $"Caustics did not modulate the light reaching the floor: {difference}." );

			var highlights = Enumerable.Range( 0, plain.Length ).Where( i => caustics[i].r > plain[i].r + 0.02f ).ToArray();
			Assert.IsTrue( highlights.Length > 10, "The caustics fixture did not contain visible highlights." );
			float doublingError = highlights.Average( i => MathF.Abs( (doubled[i].r - plain[i].r) - 2 * (caustics[i].r - plain[i].r) ) );
			Assert.IsTrue( doublingError < 0.005f, $"Doubling intensity did not double the caustic highlights: {doublingError}." );
			Assert.IsTrue( doubled.All( p => p.r >= 0 && p.g >= 0 && p.b >= 0 ), "Amplified caustics produced negative light." );

			Color[] Capture( string name )
			{
				for ( int frame = 0; frame < 8; frame++ )
				{
					// Allow asynchronous graphics pipelines to finish before the readback.
					System.Threading.Thread.Sleep( 50 );
					EngineGlobal.SourceEnginePanelAppFrame();
					scene.GameTick( 0 );
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

				Assert.AreEqual( name == "plain" ? 0 : 1, scene.GetAll<LightModulator>().Count( x => x.Active ) );

				var pixels = new ushort[256 * 256 * 4];
				target.GetPixels<ushort>( (0, 0, 256, 256), 0, 0, pixels, ImageFormat.RGBA16161616F );
				var colors = new Color[256 * 256];
				for ( int i = 0; i < colors.Length; i++ )
				{
					colors[i] = new Color( (float)BitConverter.UInt16BitsToHalf( pixels[i * 4] ),
						(float)BitConverter.UInt16BitsToHalf( pixels[i * 4 + 1] ),
						(float)BitConverter.UInt16BitsToHalf( pixels[i * 4 + 2] ) );
				}

				if ( Environment.GetEnvironmentVariable( "SBOX_WATER_CAPTURE" ) is { Length: > 0 } directory )
				{
					Directory.CreateDirectory( directory );
					using var bitmap = new Bitmap( 256, 256 );
					bitmap.SetPixels( colors );
					File.WriteAllBytes( Path.Combine( directory, $"{name}.png" ), bitmap.ToPng() );
				}

				return colors;
			}
		}
		finally
		{
			scene.Destroy();
		}
	}
}
