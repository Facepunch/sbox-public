using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Features;
using System;
using static SceneRendererTests.TestScenes;

namespace SceneRendererTests;

/// <summary>
/// Shadow views, through the shared <c>ShadowMapper</c>: sun cascades and their exclusion, local light shadows.
/// </summary>
// Frames go through the engine's ShadowMapper, whose cache and frame counter are static: one test's frame
// can't run in the middle of another's
[TestClass]
[DoNotParallelize]
public class ShadowTests
{
	/// <summary>
	/// The sun gets four orthographic cascades, each bigger than the last, and a cascade leaves out
	/// casters the one before it already covers - native's exclusion frustum.
	/// </summary>
	[TestMethod]
	public void SunCascades()
	{
		var world = new RenderWorld();
		var box = new MeshObject( null, new Transform( new Vector3( 10, 0, 0 ) ) ) { LocalBounds = UnitBox };
		world.Add( box );

		var view = LookingDownX();
		using var system = new RenderSystem();
		var mesh = system.GetFeature<MeshRenderFeature>();

		CollectAndPrepare( system, world, view );

		var passes = system.ShadowMaps.Passes;
		Assert.AreEqual( 4, passes.Length );
		Assert.AreEqual( 4u, system.ShadowMaps.Directional.CascadeCount );

		for ( int i = 0; i < passes.Length; i++ )
		{
			Assert.IsTrue( passes[i].IsShadow );
			Assert.IsTrue( passes[i].View.Orthographic );
			Assert.AreSame( view, passes[i].Root, "LOD and size culling come from the main view" );
			if ( i > 0 ) Assert.IsTrue( passes[i].View.OrthoSize.x > passes[i - 1].View.OrthoSize.x, "cascades grow" );
		}

		// Move the box to the middle of the first cascade: then it's inside the second's exclusion box
		var center = system.ShadowMaps.Directional.CascadeSpheres[0];
		box.Transform = new Transform( new Vector3( center.x, center.y, center.z ) );
		CollectAndPrepare( system, world, view );

		passes = system.ShadowMaps.Passes;
		CollectionAssert.Contains( passes[0].Visible( mesh ), 0 );
		CollectionAssert.DoesNotContain( passes[1].Visible( mesh ), 0, "the first cascade already has it" );

		// No sun shadows, no cascades - but the sun still lights
		world.Lighting.SunShadows = false;
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 0, system.ShadowMaps.Passes.Length );
		Assert.AreEqual( 0u, system.ShadowMaps.Directional.CascadeCount );
		Assert.IsTrue( system.ShadowMaps.Directional.Enabled );
	}

	/// <summary>
	/// A baked sun's shadows are in the lightmaps and probes, so its cascades leave static casters out and only what moves casts
	/// in real time - as <c>ShadowMapper.FindOrCreateDirectionalShadowMaps</c> excludes <c>StaticObject</c> for a baked light.
	/// </summary>
	[TestMethod]
	public void BakedSunLeavesStaticCastersOut()
	{
		var world = new RenderWorld();
		var fixedBox = new MeshObject( null, new Transform( new Vector3( 10, 0, 0 ) ) ) { LocalBounds = UnitBox, IsStatic = true };
		var movingBox = new MeshObject( null, new Transform( new Vector3( 10, 3, 0 ) ) ) { LocalBounds = UnitBox };
		world.Add( fixedBox );
		world.Add( movingBox );
		world.Lighting.SunShadowsBaked = true;

		var view = LookingDownX();
		using var system = new RenderSystem();
		var mesh = system.GetFeature<MeshRenderFeature>();

		Sandbox.Rendering.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, view );
		var visible = system.ShadowMaps.Passes[0].Visible( mesh );
		CollectionAssert.Contains( visible, movingBox.Index );
		CollectionAssert.DoesNotContain( visible, fixedBox.Index, "its shadow is baked" );

		world.Lighting.SunShadowsBaked = false;
		CollectAndPrepare( system, world, view );
		visible = system.ShadowMaps.Passes[0].Visible( mesh );
		CollectionAssert.Contains( visible, movingBox.Index );
		CollectionAssert.Contains( visible, fixedBox.Index );
	}

	/// <summary>
	/// A point light casts into six cube faces and a spot into one view, each pointing its binned
	/// light at its shadow. Lights and casters that opt out are left out, and a spot too small on
	/// screen gets none, as native's <c>r.shadows.size_cull_threshold</c> has it.
	/// </summary>
	[TestMethod]
	public void LocalLightShadows()
	{
		var world = new RenderWorld();
		world.Lighting.SunColor = Color.Black;

		var caster = new MeshObject( null, new Transform( new Vector3( 100, 0, -20 ) ) ) { LocalBounds = UnitBox };
		var nonCaster = new MeshObject( null, new Transform( new Vector3( 100, 0, 20 ) ) ) { LocalBounds = UnitBox, CastShadows = false };
		world.Add( caster );
		world.Add( nonCaster );

		var point = new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 100, 0, 0 ) ) ) { Radius = 50 };
		var spot = new LightObject( LightObject.LightKind.Spot, new Transform( new Vector3( 100, 30, 0 ), Rotation.LookAt( Vector3.Down ) ) ) { Radius = 50 };
		var unshadowed = new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 100, -30, 0 ) ) ) { Radius = 50, CastShadows = false };
		var tinySpot = new LightObject( LightObject.LightKind.Spot, new Transform( new Vector3( 900, 0, 0 ) ) ) { Radius = 1 };
		world.Add( point );
		world.Add( spot );
		world.Add( unshadowed );
		world.Add( tinySpot );

		var view = LookingDownX();
		using var system = new RenderSystem();
		var mesh = system.GetFeature<MeshRenderFeature>();
		var binner = system.GetFeature<LightBinnerFeature>();

		CollectAndPrepare( system, world, view );

		var passes = system.ShadowMaps.Passes;
		Assert.AreEqual( 7, passes.Length, "six cube faces and a spot" );

		var gpuLights = binner.PackedGpuLights;
		var packed = binner.PackedLights;
		for ( int i = 0; i < packed.Length; i++ )
		{
			var expected = packed[i] == point || packed[i] == spot ? 0u : 0xFFFFFFFF;
			Assert.AreEqual( expected, gpuLights[i].ShadowMapIndex, $"light {i}" );
		}

		var sawCaster = false;
		foreach ( var pass in passes )
		{
			var visible = pass.Visible( mesh );
			sawCaster |= visible.Contains( 0 );
			CollectionAssert.DoesNotContain( visible, 1, "doesn't cast shadows" );
			Assert.AreEqual( 0, pass.Visible( binner ).Count, "lights aren't drawn into shadow maps" );
		}

		Assert.IsTrue( sawCaster, "the caster under the point light is in one of its faces" );
	}
}
