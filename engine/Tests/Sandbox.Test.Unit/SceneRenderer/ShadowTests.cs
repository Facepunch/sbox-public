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
	/// The dominant sun's current color and direction are available with global or per-light shadows disabled.
	/// </summary>
	[TestMethod]
	public void UnshadowedSunDataStaysCurrent()
	{
		var csmEnabled = Sandbox.Rendering.ShadowMapper.CSMEnabled;
		try
		{
			var mapper = new Sandbox.Rendering.ShadowMapper();
			var light = new Sandbox.Rendering.ShadowLight
			{
				Color = new Vector3( 1, 0.5f, 0.25f ),
				Direction = Vector3.Down,
				ShadowsEnabled = false
			};
			Sandbox.Rendering.ShadowMapper.CSMEnabled = true;
			mapper.DoDirectionalLight( light );
			Assert.AreEqual( new Vector4( light.Color, 0 ), mapper.DirectionalLightData.Color );
			Assert.AreEqual( new Vector4( light.Direction, 0 ), mapper.DirectionalLightData.Direction );

			Sandbox.Rendering.ShadowMapper.CSMEnabled = false;
			light.Color = Vector3.One;
			light.Direction = Vector3.Forward;
			mapper.DoDirectionalLight( light );
			Assert.IsTrue( mapper.DirectionalLightData.Enabled );
			Assert.AreEqual( 0u, mapper.DirectionalLightData.CascadeCount );
			Assert.AreEqual( new Vector4( light.Color, 0 ), mapper.DirectionalLightData.Color );
			Assert.AreEqual( new Vector4( light.Direction, 0 ), mapper.DirectionalLightData.Direction );
		}
		finally
		{
			Sandbox.Rendering.ShadowMapper.CSMEnabled = csmEnabled;
		}
	}

	/// <summary>
	/// The sun gets four orthographic cascades, each bigger than the last, and a cascade leaves out
	/// casters the one before it already covers - native's exclusion frustum.
	/// </summary>
	[TestMethod]
	public void SunCascades()
	{
		// The sun asks for four; pin the cap so the test doesn't depend on the default quality profile
		var maxCascades = Sandbox.Rendering.ShadowMapper.MaxCascades;
		Sandbox.Rendering.ShadowMapper.MaxCascades = 4;

		try
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
		finally
		{
			Sandbox.Rendering.ShadowMapper.MaxCascades = maxCascades;
		}
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
	/// A cascade's size only depends on the camera's projection. Far from the origin, fitting it in world space let float error
	/// in the inverse view projection resize it every frame the camera moved, which slid the texel grid and made every shadow
	/// edge crawl, however well the origin was snapped.
	/// </summary>
	[TestMethod]
	public void SunCascadesKeepTheirSizeFarFromTheOrigin()
	{
		var maxCascades = Sandbox.Rendering.ShadowMapper.MaxCascades;
		Sandbox.Rendering.ShadowMapper.MaxCascades = 4;

		try
		{
			var world = new RenderWorld();
			using var system = new RenderSystem();

			var view = new RenderView
			{
				FieldOfView = 90,
				ZNear = 5,
				ZFar = 30000,
				Viewport = new Rect( 0, 0, 1920, 1080 ),
			};

			float[] CascadeSizes( Vector3 position, Rotation rotation )
			{
				view.Position = position;
				view.Rotation = rotation;
				view.Update();
				CollectAndPrepare( system, world, view );

				var passes = system.ShadowMaps.Passes;
				var sizes = new float[passes.Length];
				for ( int i = 0; i < passes.Length; i++ )
					sizes[i] = passes[i].View.OrthoSize.x;
				return sizes;
			}

			var expected = CascadeSizes( Vector3.Zero, Rotation.Identity );
			Assert.AreEqual( 4, expected.Length );

			// Walking and turning the camera across a big map
			for ( int frame = 0; frame < 120; frame++ )
			{
				var position = new Vector3( 20000 + frame * 1.7f, -15000 + frame * 0.9f, 300 );
				var rotation = Rotation.From( 10 + frame * 0.05f, frame * 0.2f, 0 );
				CollectionAssert.AreEqual( expected, CascadeSizes( position, rotation ), $"frame {frame}" );
			}
		}
		finally
		{
			Sandbox.Rendering.ShadowMapper.MaxCascades = maxCascades;
		}
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
