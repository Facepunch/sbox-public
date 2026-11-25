using Borealis;
using Borealis.Features;
using System;
using static BorealisTests.TestScenes;

namespace BorealisTests;

/// <summary>
/// Shadow views, through the shared <c>ShadowMapper</c>: sun cascades, local light shadows.
/// </summary>
// Frames go through Borealis's ShadowMapper, whose cache and frame counter are static: one test's frame
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
		var csmEnabled = RenderHost.Current.Shadows.CascadesEnabled;
		try
		{
			var mapper = new Borealis.Shadows.ShadowMapper();
			var light = new Borealis.Shadows.ShadowLight
			{
				Color = new Vector3( 1, 0.5f, 0.25f ),
				Direction = Vector3.Down,
				ShadowsEnabled = false
			};
			RenderHost.Current.Shadows.CascadesEnabled = true;
			mapper.DoDirectionalLight( light );
			Assert.AreEqual( new Vector4( light.Color, 0 ), mapper.DirectionalLightData.Color );
			Assert.AreEqual( new Vector4( light.Direction, 0 ), mapper.DirectionalLightData.Direction );

			RenderHost.Current.Shadows.CascadesEnabled = false;
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
			RenderHost.Current.Shadows.CascadesEnabled = csmEnabled;
		}
	}

	/// <summary>
	/// The sun gets four orthographic cascades, each bigger than the last, and each keeps the casters the one before it
	/// covers: both are sampled where they blend.
	/// </summary>
	/// <summary>
	/// An oblique view's sun cascades are the plain view's: they fit a near plane square to the view, not its clip plane, which
	/// would point them along the plane and shrink them round the camera.
	/// </summary>
	[TestMethod]
	public void ObliqueViewCascadesFitTheSquareView()
	{
		var world = new RenderWorld();
		world.Add( new MeshObject( null, new Transform( new Vector3( 10, 0, 0 ) ) ) { LocalBounds = UnitBox } );

		var plain = LookingDownX();
		using var system = new RenderSystem();
		CollectAndPrepare( system, world, plain );
		var expected = system.ShadowMaps.Directional.CascadeSpheres.ToArray();
		Assert.IsTrue( expected[0].w > 0 );

		var oblique = LookingDownX();
		var projection = Matrix.CreateProjection( oblique.ZNear, oblique.ZFar, oblique.FieldOfView, 1 );
		oblique.Projection = Matrix.CreateObliqueProjection( new Transform( oblique.Position, oblique.Rotation ), new Plane( new Vector3( 0, 0, -10 ), Vector3.Down ), projection );
		oblique.Update();

		using var other = new RenderSystem();
		CollectAndPrepare( other, world, oblique );
		var spheres = other.ShadowMaps.Directional.CascadeSpheres;
		for ( int i = 0; i < expected.Length; i++ )
		{
			Assert.AreEqual( expected[i].x, spheres[i].x, 1e-2f, $"cascade {i}" );
			Assert.AreEqual( expected[i].y, spheres[i].y, 1e-2f, $"cascade {i}" );
			Assert.AreEqual( expected[i].z, spheres[i].z, 1e-2f, $"cascade {i}" );
			Assert.AreEqual( expected[i].w, spheres[i].w, expected[i].w * 1e-4f, $"cascade {i}" );
		}
	}

	[TestMethod]
	public void SunCascades()
	{
		// The sun asks for four; pin the cap so the test doesn't depend on the default quality profile. Every cascade fit to
		// the view, the last too (CascadeCacheTests has the cached one)
		var maxCascades = RenderHost.Current.Shadows.MaxCascades;
		RenderHost.Current.Shadows.MaxCascades = 4;
		var farCache = Borealis.Shadows.ShadowMapper.FarCascadeCache;
		Borealis.Shadows.ShadowMapper.FarCascadeCache = false;

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

			// Move the box to the middle of the first cascade
			var center = system.ShadowMaps.Directional.CascadeSpheres[0];
			box.Transform = new Transform( new Vector3( center.x, center.y, center.z ) );
			CollectAndPrepare( system, world, view );

			passes = system.ShadowMaps.Passes;
			CollectionAssert.Contains( passes[0].Visible( mesh ), 0 );
			CollectionAssert.Contains( passes[1].Visible( mesh ), 0, "both cascades are sampled where they blend" );

			// No sun shadows, no cascades - but the sun still lights
			world.Lighting.SunShadows = false;
			CollectAndPrepare( system, world, view );
			Assert.AreEqual( 0, system.ShadowMaps.Passes.Length );
			Assert.AreEqual( 0u, system.ShadowMaps.Directional.CascadeCount );
			Assert.IsTrue( system.ShadowMaps.Directional.Enabled );
		}
		finally
		{
			RenderHost.Current.Shadows.MaxCascades = maxCascades;
			Borealis.Shadows.ShadowMapper.FarCascadeCache = farCache;
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

		Borealis.Shadows.ShadowMapper.ResetCache();
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
		var maxCascades = RenderHost.Current.Shadows.MaxCascades;
		RenderHost.Current.Shadows.MaxCascades = 4;
		var farCache = Borealis.Shadows.ShadowMapper.FarCascadeCache;
		Borealis.Shadows.ShadowMapper.FarCascadeCache = false;

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
			RenderHost.Current.Shadows.MaxCascades = maxCascades;
			Borealis.Shadows.ShadowMapper.FarCascadeCache = farCache;
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
