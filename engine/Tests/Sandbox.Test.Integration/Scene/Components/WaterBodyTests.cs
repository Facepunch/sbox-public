using Sandbox.Rendering;
using NativeEngine;
using System;

namespace SceneTests.Components;

/// <summary>
/// The water's material decides what it draws, its fog and its caustics.
/// </summary>
[TestClass]
public class WaterBodyTests
{
	/// <summary>
	/// Neutral caustics match the tuned baseline, and doubling either control survives GPU data packing.
	/// </summary>
	[TestMethod]
	public void CausticsControlsAreMultipliers()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var scene = Scene.CreateEditorScene();
		using var scope = scene.Push();
		try
		{
			var water = scene.CreateObject().Components.Create<WaterBody>();
			var material = water.MaterialCopy();
			material.Set( "CausticsIntensity", 1f );
			material.Set( "CausticsBrightness", 1f );
			scene.EditorTick( 1, 1f / 60 );

			var projector = water.GetComponentsInChildren<LightModulator>().Single();
			Assert.IsTrue( projector.BuildData( out var baseline ) );
			Assert.AreEqual( 0.78f, baseline.Strength );
			Assert.AreEqual( 5f, baseline.Brightness );

			material.Set( "CausticsIntensity", 2f );
			scene.EditorTick( 2, 1f / 60 );
			Assert.IsTrue( projector.BuildData( out var doubledIntensity ) );
			Assert.AreEqual( baseline.Strength * 2, doubledIntensity.Strength );
			Assert.AreEqual( baseline.Brightness, doubledIntensity.Brightness );

			material.Set( "CausticsBrightness", 2f );
			scene.EditorTick( 3, 1f / 60 );
			Assert.IsTrue( projector.BuildData( out var doubledBrightness ) );
			Assert.AreEqual( baseline.Brightness * 2, doubledBrightness.Brightness );
			Assert.AreEqual( doubledIntensity.Strength, doubledBrightness.Strength );

			material.Set( "CausticsIntensity", 0f );
			scene.EditorTick( 4, 1f / 60 );
			Assert.IsFalse( scene.GetAll<LightModulator>().Any( x => x.Active ) );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Caustics scale independently and remain below the full wave envelope as the water is edited.
	/// </summary>
	[TestMethod]
	public void CausticsFollowScaleAndLowestWaveHeight()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var scene = Scene.CreateEditorScene();
		using var scope = scene.Push();
		try
		{
			var water = scene.CreateObject().Components.Create<WaterBody>();
			water.WorldPosition = new Vector3( 10, 20, 80 );
			water.WorldRotation = Rotation.From( new Angles( 20, 30, 0 ) );
			water.Waves = true;
			water.WaveHeight = 32;
			var material = water.MaterialCopy();
			material.Set( "CausticsScale", 1f );
			scene.EditorTick( 1, 1f / 60 );

			var projector = water.GetComponentsInChildren<LightModulator>().Single();
			var tiling = projector.Tiling;
			var detailScale = material.GetVector4( "DetailScale" );
			Assert.IsTrue( projector.WorldPosition.Distance( water.FromSurface( Vector3.Down * 32 ) ) < 0.001f );

			material.Set( "CausticsScale", 2f );
			water.WaveHeight = 64;
			scene.EditorTick( 2, 1f / 60 );
			Assert.AreEqual( tiling * 0.5f, projector.Tiling );
			Assert.AreEqual( detailScale, material.GetVector4( "DetailScale" ) );
			Assert.IsTrue( projector.WorldPosition.Distance( water.FromSurface( Vector3.Down * 64 ) ) < 0.001f );

			water.Waves = false;
			scene.EditorTick( 3, 1f / 60 );
			Assert.IsTrue( projector.WorldPosition.Distance( water.FromSurface( Vector3.Zero ) ) < 0.001f );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Shipped presets load their textures and are accepted by WaterBody without falling back to another material.
	/// </summary>
	[TestMethod]
	[DataRow( "blue" )]
	[DataRow( "green" )]
	[DataRow( "clear" )]
	[DataRow( "dark" )]
	public void DefaultPresetsAreUsable( string name )
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var material = Material.Load( $"materials/water/{name}.water.vmat" );
		Assert.IsTrue( material.IsValid && !material.IsError );
		Assert.IsNotNull( material.CreateCopy(), "Preset names must support procedural material copies." );
		Assert.AreEqual( 0f, material.GetVector4( "g_fRoughness" ).x );
		Assert.IsTrue( material.GetVector4( "WaterFogDepthRange" ).y > 0, "Compiled depth fog settings were lost." );
		Assert.IsTrue( material.GetVector4( "CausticsIntensity" ).x > 0, "Compiled caustics settings were lost." );
		Assert.IsTrue( material.GetVector4( "DetailSpeed" ).x > 0, "Compiled animation settings were lost." );
		Assert.IsTrue( material.GetVector4( "CausticsScale" ).x > 0, "Compiled caustics scale was lost." );

		foreach ( var textureName in new[] { "g_tDetailNormal", "g_tCaustics" } )
		{
			var texture = material.GetTexture( textureName );
			Assert.IsTrue( texture is { IsValid: true, IsError: false }, $"{name} is missing {textureName}." );
		}

		var scene = Scene.CreateEditorScene();
		using var scope = scene.Push();
		try
		{
			var water = scene.CreateObject( false ).Components.Create<WaterBody>();
			water.Material = material;
			Assert.AreSame( material, water.ActiveMaterial );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Swapping between materials, to a material of another shader and back changes the drawn colour, the
	/// refraction capture, fog and caustics together.
	/// </summary>
	[TestMethod]
	public void MaterialSwapsFollowThrough()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var scene = Scene.CreateEditorScene();
		using var scope = scene.Push();
		using var clock = Time.Scope( 2, 1.0 / 60 );
		int savedDetail = WaterBody.Detail;
		try
		{
			WaterBody.Detail = 2;
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			camera.CustomSize = new Vector2( 64 );
			camera.SceneCamera.Attributes.Set( "msaa", 0 );
			camera.WorldPosition = Vector3.Up * 64;
			camera.WorldRotation = Rotation.LookAt( Vector3.Down, Vector3.Forward );
			camera.ZNear = 1;
			camera.ZFar = 2048;
			camera.BackgroundColor = Color.Black;
			scene.CreateObject().Components.Create<AmbientLight>().Color = Color.White;
			var water = scene.CreateObject().Components.Create<WaterBody>();
			water.Size = new Vector2( 4096, 4096 );
			water.Underwater = false;
			water.Waves = false;
			water.Effects = false;
			water.SetReflectionsEnabled( false );

			Material Make( Color color, bool caustics, float fogDepth )
			{
				var material = Material.Load( "materials/water/water.vmat" ).CreateCopy();
				material.Set( "SurfaceColor", color );
				material.Set( "Opacity", 1f );
				material.Set( "WaterFogDepthRange", new Vector2( 0, fogDepth ) );
				material.SetFeature( "F_CAUSTICS", caustics ? 1 : 0 );
				return material;
			}

			var red = Make( Color.Red, false, 64 );
			var green = Make( Color.Green, true, 0 );
			using var target = Texture.CreateRenderTarget().WithSize( 64, 64 )
				.WithFormat( ImageFormat.RGBA16161616F ).Create( "Water material swap test" );

			Vector3 Render( Material material )
			{
				water.Material = material;
				EngineGlobal.SourceEnginePanelAppFrame();
				scene.EditorTick( 2, 1f / 60 );
				scene.PreRender();
				Assert.IsTrue( camera.RenderToTexture( target,
					new ViewSetup { EnablePostprocessing = false, EnableUI = false, EnableDebugOverlays = false } ) );
				g_pRenderDevice.Present( IntPtr.Zero );
				g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
				Span<ushort> pixel = stackalloc ushort[4];
				target.GetPixels<ushort>( (32, 32, 1, 1), 0, 0, pixel, ImageFormat.RGBA16161616F );
				EngineLoop.DrainFrameEndDisposables();
				RenderTarget.EndOfFrame();
				EngineLoop.RunAsyncTasks();
				return new Vector3( (float)BitConverter.UInt16BitsToHalf( pixel[0] ),
					(float)BitConverter.UInt16BitsToHalf( pixel[1] ), (float)BitConverter.UInt16BitsToHalf( pixel[2] ) );
			}

			int Modulators() => scene.GetAll<LightModulator>().Count( x => x.Active );
			Material Drawn() => water.GetComponentsInChildren<ModelRenderer>().Single().Model.Materials.Single();

			var first = Render( red );
			Assert.IsTrue( first.x > first.y * 2 && first.x > first.z * 2, $"The red material was not drawn: {first}" );
			Assert.AreSame( red, Drawn() );
			Assert.AreEqual( 0, Modulators() );
			Assert.IsNotNull( water.RefractionFog );

			var second = Render( green );
			Assert.IsTrue( second.y > second.x * 2 && second.y > second.z * 2, $"The green material was not drawn: {second}" );
			Assert.AreSame( green, Drawn() );
			Assert.AreEqual( 1, Modulators() );
			Assert.IsNull( water.RefractionFog );

			var fallback = Render( Material.Load( "materials/dev/primary_white.vmat" ) );
			Assert.AreSame( Material.Load( "materials/water/blue.water.vmat" ), Drawn() );
			Assert.IsTrue( fallback.z > fallback.x, $"The fallback did not draw the default water: {fallback}" );
			Assert.AreEqual( 1, Modulators() );

			var back = Render( red );
			Assert.IsTrue( back.Distance( first ) < 0.002f, $"Swapping back did not restore the red material: {first} / {back}" );
			Assert.AreSame( red, Drawn() );
			Assert.AreEqual( 0, Modulators() );
		}
		finally
		{
			WaterBody.Detail = savedDetail;
			scene.Destroy();
		}
	}
}
