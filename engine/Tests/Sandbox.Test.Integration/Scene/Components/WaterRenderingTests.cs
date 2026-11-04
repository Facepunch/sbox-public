using NativeEngine;
using Sandbox.Rendering;
using System;

namespace SceneTests.Components;

/// <summary>
/// Exercises the real water vertex/pixel programs and HDR readback rather than checking bound attributes.
/// </summary>
[TestClass]
public class WaterRenderingTests
{
	/// <summary>
	/// Waves visibly change the rendered surface from either side, remain finite in HDR,
	/// and stop moving when their speed is zero and normal-texture animation is absent.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void FrozenWavesRenderFromBothSides( bool below )
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		const int width = 128, height = 96;
		var scene = new Scene();
		using var scope = scene.Push();
		using var target = Texture.CreateRenderTarget().WithSize( width, height )
			.WithFormat( ImageFormat.RGBA16161616F ).Create( "water-wave-regression" );
		try
		{
			var water = scene.CreateObject().Components.Create<WaterBody>();
			water.Size = new Vector2( 768, 768 );
			water.Underwater = false;
			water.SetCaustics( false );
			water.Effects = false;
			water.SetReflectionsEnabled( false );
			water.DisableRefraction();
			water.ClearDetail();
			water.MaterialCopy().Set( "SurfaceColor", new Color( 0.03f, 0.25f, 0.5f ) );
			water.MaterialCopy().Set( "Opacity", (float)(1) );
			water.MaterialCopy().Set( "g_fRoughness", 0.1f );
			water.Waves = true;
			water.WaveHeight = 48;
			water.WaveLength = 256;
			water.WaveWidth = 192;
			water.WaveSteepness = 0.7f;
			water.WaveSpeed = 0;
			water.Waves = false;
			var material = Material.FromShader( "shaders/water.shader" );
			Assert.AreEqual( "shaders/water.shader", material.Shader.native.GetFilename(), "Water shader was replaced by a fallback." );

			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			camera.CustomSize = new Vector2( width, height );
			camera.WorldPosition = new Vector3( 0, 0, below ? -256 : 256 );
			camera.WorldRotation = Rotation.LookAt( below ? Vector3.Up : Vector3.Down, Vector3.Forward );
			camera.Orthographic = true;
			camera.OrthographicHeight = 384;
			camera.ZNear = 1;
			camera.ZFar = 2048;
			camera.BackgroundColor = Color.Black;
			camera.SceneCamera.Attributes.Set( "msaa", 0 );
			scene.CreateObject().Components.Create<AmbientLight>().Color = Color.White * 0.25f;
			foreach ( float z in new[] { -280f, 280f } )
			{
				var light = scene.CreateObject().Components.Create<PointLight>();
				light.WorldPosition = new Vector3( -150, 80, z );
				light.Radius = 1000;
				light.LightColor = Color.White * 3;
				light.Shadows = false;
			}

			RenderFrames( 20 );
			var flat = ReadPixels();
			AssertVisibleFinite( flat );
			water.Waves = true;
			RenderFrames( 8 );
			var waves = ReadPixels();
			AssertVisibleFinite( waves );
			Assert.IsTrue( Difference( flat, waves ) > 0.0005f, "Enabling waves did not visibly change the rendered water." );
			RenderFrames( 4 );
			var stopped = ReadPixels();
			AssertVisibleFinite( stopped );
			Assert.IsTrue( Difference( waves, stopped ) < 0.002f, "Zero-speed waves continued to animate." );

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

			float[] ReadPixels()
			{
				var data = new ushort[width * height * 4];
				target.GetPixels<ushort>( (0, 0, width, height), 0, 0, data, ImageFormat.RGBA16161616F );
				var result = new float[data.Length];
				for ( int i = 0; i < result.Length; i++ )
					result[i] = (float)BitConverter.UInt16BitsToHalf( data[i] );
				return result;
			}
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Looking across steep waves, the clipmap's levels meet without cracks: every pixel of water between
	/// the camera and the far distance is drawn, for an ocean and inside a large pool's walls.
	/// The camera sits off every level's snapping grid so the levels' centres differ.
	/// </summary>
	[TestMethod, DoNotParallelize]
	[DataRow( true )]
	[DataRow( false )]
	public void ClipmapLevelsMeetWithoutCracks( bool ocean )
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		const int width = 512, height = 256;
		int previous = WaterBody.Detail;
		var scene = new Scene();
		using var scope = scene.Push();
		using var target = Texture.CreateRenderTarget().WithSize( width, height )
			.WithFormat( ImageFormat.RGBA16161616F ).Create( "water-clipmap-cracks" );
		try
		{
			WaterBody.Detail = 2;
			var water = scene.CreateObject().Components.Create<WaterBody>();
			water.Ocean = ocean;
			water.Size = new Vector2( 4096 );
			water.SetCaustics( false );
			water.Underwater = false;
			water.Effects = false;
			water.SetReflectionsEnabled( false );
			water.DisableRefraction();
			water.ClearDetail();
			water.MaterialCopy().Set( "SurfaceColor", Color.White );
			water.MaterialCopy().Set( "UndersideColor", Color.White );
			water.MaterialCopy().Set( "Opacity", (float)(1) );
			water.MaterialCopy().Set( "g_fRoughness", 1 );
			water.Waves = true;
			water.WaveHeight = 40;
			water.WaveLength = 384;
			water.WaveWidth = 256;
			water.WaveSteepness = 0.8f;
			water.WaveSpeed = 0;

			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			camera.CustomSize = new Vector2( width, height );
			camera.WorldPosition = new Vector3( 37.3f, -91.7f, 72 );
			camera.WorldRotation = Rotation.LookAt( new Vector3( 1, 0.2f, -0.36f ) );
			camera.FieldOfView = 90;
			camera.ZNear = 1;
			camera.ZFar = 20000;
			camera.BackgroundColor = Color.Black;
			camera.SceneCamera.Attributes.Set( "msaa", 0 );
			scene.CreateObject().Components.Create<AmbientLight>().Color = Color.White;

			RenderFrames( 20 );
			var surface = water.GetComponentsInChildren<ModelRenderer>().Single();
			Assert.IsTrue( surface.Attributes.GetInt( "ClipmapLevels" ) >= 4, "The view must span several clipmap levels." );

			// Check below the furthest water: the ocean's far distance, or a point well inside the pool's far wall.
			var flat = new Vector3( 1, 0.2f, 0 ).Normal;
			var far = camera.WorldPosition.WithZ( 0 ) + flat * (ocean ? 18000 : 1500);
			int top = (int)MathF.Ceiling( camera.PointToScreenNormal( far ).y * height ) + 4;
			Assert.IsTrue( top > 0 && top < height - 32, $"The fixture must see water across most of the view, from row {top}." );

			var data = new ushort[width * height * 4];
			target.GetPixels<ushort>( (0, 0, width, height), 0, 0, data, ImageFormat.RGBA16161616F );
			int holes = 0;
			for ( int y = top; y < height; y++ )
			{
				for ( int x = 0; x < width; x++ )
				{
					int i = (y * width + x) * 4;
					float brightest = MathF.Max( (float)BitConverter.UInt16BitsToHalf( data[i] ),
						MathF.Max( (float)BitConverter.UInt16BitsToHalf( data[i + 1] ), (float)BitConverter.UInt16BitsToHalf( data[i + 2] ) ) );
					if ( !(brightest > 0.0001f) )
						holes++;
				}
			}
			Assert.AreEqual( 0, holes, $"Background showed through {holes} water pixels below row {top}." );

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
			WaterBody.Detail = previous;
			scene.Destroy();
		}
	}

	static void AssertVisibleFinite( float[] pixels )
	{
		int visible = 0;
		for ( int i = 0; i < pixels.Length; i += 4 )
		{
			Assert.IsTrue( float.IsFinite( pixels[i] ) && float.IsFinite( pixels[i + 1] )
				&& float.IsFinite( pixels[i + 2] ) && float.IsFinite( pixels[i + 3] ), "Water produced a non-finite HDR pixel." );
			if ( pixels[i + 1] > 0.01f && pixels[i + 2] > 0.01f
				&& pixels[i + 1] > pixels[i] * 1.5f && pixels[i + 2] > pixels[i] * 1.5f )
				visible++;
		}
		Assert.IsTrue( visible > 1000, "Expected visible blue water; empty or fallback draws do not exercise the shader." );
	}

	static float Difference( float[] a, float[] b )
	{
		float difference = 0;
		for ( int i = 0; i < a.Length; i += 4 )
			difference += MathF.Abs( a[i] - b[i] ) + MathF.Abs( a[i + 1] - b[i + 1] ) + MathF.Abs( a[i + 2] - b[i + 2] );
		return difference / (a.Length / 4 * 3);
	}
}
