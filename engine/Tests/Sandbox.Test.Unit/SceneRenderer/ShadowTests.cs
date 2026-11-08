using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Features;
using System;
using static SceneRendererTests.TestScenes;

namespace SceneRendererTests;

/// <summary>
/// Shadow views, through the shared <c>ShadowMapper</c>: sun cascades, local light shadows.
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
	/// The sun gets four orthographic cascades, each bigger than the last, and each keeps the casters the one before it
	/// covers: both are sampled where they blend.
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
			Sandbox.Rendering.ShadowMapper.MaxCascades = maxCascades;
		}
	}

	/// <summary>
	/// The caster volume is the corner hull swept toward the light: points the light reaches the hull from are in,
	/// points beside or downstream of it are out.
	/// </summary>
	[TestMethod]
	public void CasterVolumeSweepsTowardLight()
	{
		// Unit cube in frustum corner order: ring 0-3 at z=1, ring 4-7 at z=0, same xy order
		Vector3[] c = [new( 0, 0, 1 ), new( 0, 1, 1 ), new( 1, 1, 1 ), new( 1, 0, 1 ), new( 0, 0, 0 ), new( 0, 1, 0 ), new( 1, 1, 0 ), new( 1, 0, 0 )];
		var planes = new Vector4[12];

		bool Inside( int count, Vector3 p )
		{
			for ( int i = 0; i < count; i++ )
				if ( Vector3.Dot( new Vector3( planes[i].x, planes[i].y, planes[i].z ), p ) + planes[i].w < -1e-4f ) return false;
			return true;
		}

		// Straight down: bottom and four sides kept, the four top edges are the silhouette
		int count = Sandbox.Rendering.ShadowMapper.BuildCasterVolume( c, new Vector3( 0, 0, -1 ), planes );
		Assert.AreEqual( 9, count );
		Assert.IsTrue( Inside( count, new Vector3( 0.5f, 0.5f, 0.5f ) ) );
		Assert.IsTrue( Inside( count, new Vector3( 0.5f, 0.5f, 50 ) ) );
		Assert.IsFalse( Inside( count, new Vector3( 0.5f, 0.5f, -5 ) ) );
		Assert.IsFalse( Inside( count, new Vector3( 5, 0.5f, 0.5f ) ) );
		Assert.IsFalse( Inside( count, new Vector3( 5, 0.5f, 50 ) ) );

		// Slanted: in only along the light's path into the cube
		count = Sandbox.Rendering.ShadowMapper.BuildCasterVolume( c, new Vector3( 1, 0, -1 ).Normal, planes );
		Assert.IsTrue( Inside( count, new Vector3( -2.5f, 0.5f, 3.5f ) ) );
		Assert.IsFalse( Inside( count, new Vector3( 2.5f, 0.5f, 3.5f ) ) );
		Assert.IsFalse( Inside( count, new Vector3( 3.5f, 0.5f, -2.5f ) ) );
	}

	/// <summary>
	/// Each cascade view can find the caster volume fit with it, by where it renders from - that's how clutter's GPU
	/// cull gets the planes long after the cascades were worked out. The volume keeps what stands between the sun and
	/// the view, and drops what stands beside it.
	/// </summary>
	[TestMethod]
	public void CascadeViewsFindTheirCasterVolume()
	{
		var world = new RenderWorld();
		world.Lighting.SunDirection = Vector3.Down;
		world.Add( new MeshObject( null, new Transform( new Vector3( 100, 0, 0 ) ) ) { LocalBounds = UnitBox } );

		var casterCull = Sandbox.Rendering.ShadowMapper.CasterCull;
		try
		{
			Sandbox.Rendering.ShadowMapper.CasterCull = true;
			Sandbox.Rendering.ShadowMapper.ResetCache();
			using var system = new RenderSystem();
			CollectAndPrepare( system, world, LookingDownX() );

			var passes = system.ShadowMaps.Passes;
			Assert.IsTrue( passes.Length > 0 );

			foreach ( var pass in passes )
			{
				var volume = Sandbox.Rendering.ShadowMapper.FindCasterVolume( pass.View.Position );
				Assert.IsNotNull( volume, $"{pass.View.Name} has no volume to cull against" );

				bool Inside( Vector3 p )
				{
					for ( int i = 0; i < volume.PlaneCount; i++ )
					{
						var plane = volume.Planes[i];
						if ( Vector3.Dot( new Vector3( plane.x, plane.y, plane.z ), p ) + plane.w < 0 ) return false;
					}
					return true;
				}

				// The camera looks down +X from the origin with a 90° field of view, and the sun is straight overhead
				Assert.IsTrue( Inside( new Vector3( 100, 0, 500 ) ), "above what the camera sees" );
				Assert.IsFalse( Inside( new Vector3( 100, 400, 500 ) ), "beside it, its shadow falls where nobody looks" );
			}
		}
		finally
		{
			Sandbox.Rendering.ShadowMapper.CasterCull = casterCull;
		}
	}

	/// <summary>
	/// A local light's caster volume closes at the light instead of running on: what stands between it and the hull
	/// is in, what's past the light or beside the cone is out.
	/// </summary>
	[TestMethod]
	public void CasterVolumeClosesAtALocalLight()
	{
		Vector3[] c = [new( 0, 0, 1 ), new( 0, 1, 1 ), new( 1, 1, 1 ), new( 1, 0, 1 ), new( 0, 0, 0 ), new( 0, 1, 0 ), new( 1, 1, 0 ), new( 1, 0, 0 )];
		var planes = new Vector4[12];

		bool Inside( int count, Vector3 p )
		{
			for ( int i = 0; i < count; i++ )
				if ( Vector3.Dot( new Vector3( planes[i].x, planes[i].y, planes[i].z ), p ) + planes[i].w < -1e-4f ) return false;
			return true;
		}

		// Straight above: bottom and four sides kept, the four top edges close up at the light
		int count = Sandbox.Rendering.ShadowMapper.BuildCasterVolumeFrom( c, new Vector3( 0.5f, 0.5f, 5 ), planes );
		Assert.AreEqual( 9, count );
		Assert.IsTrue( Inside( count, new Vector3( 0.5f, 0.5f, 0.5f ) ), "in the hull" );
		Assert.IsTrue( Inside( count, new Vector3( 0.5f, 0.5f, 2 ) ), "between the light and the hull" );
		Assert.IsFalse( Inside( count, new Vector3( 0.5f, 0.5f, 50 ) ), "past the light, it can't shadow the hull" );
		Assert.IsFalse( Inside( count, new Vector3( 0.5f, 0.5f, -5 ) ), "behind the hull" );
		Assert.IsFalse( Inside( count, new Vector3( 5, 0.5f, 2 ) ), "beside the cone" );

		// A light inside the hull reaches all of it, so nothing is swept and the hull bounds itself
		count = Sandbox.Rendering.ShadowMapper.BuildCasterVolumeFrom( c, new Vector3( 0.5f, 0.5f, 0.5f ), planes );
		Assert.AreEqual( 6, count );
		Assert.IsTrue( Inside( count, new Vector3( 0.5f, 0.5f, 0.9f ) ) );
		Assert.IsFalse( Inside( count, new Vector3( 0.5f, 0.5f, 1.5f ) ) );
	}

	/// <summary>
	/// A point light behind the camera only renders the one cube face whose cone reaches into the view. The other five
	/// cover directions no visible receiver sits in, so nothing would ever sample them.
	/// </summary>
	[TestMethod]
	public void CubeFacesThatMissTheCameraDontRender()
	{
		var world = new RenderWorld();
		world.Lighting.SunColor = Color.Black;
		world.Add( new MeshObject( null, new Transform( new Vector3( 50, 0, 0 ) ) ) { LocalBounds = UnitBox } );

		// Behind the camera, but its radius reaches well into the view, so the binner keeps it
		world.Add( new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( -50, 0, 0 ) ) ) { Radius = 200 } );

		var casterCull = Sandbox.Rendering.ShadowMapper.CasterCull;
		try
		{
			using var system = new RenderSystem();

			Sandbox.Rendering.ShadowMapper.CasterCull = false;
			Sandbox.Rendering.ShadowMapper.ResetCache();
			CollectAndPrepare( system, world, LookingDownX() );
			Assert.AreEqual( 6, system.ShadowMaps.Passes.Length, "every face, culled or not" );

			Sandbox.Rendering.ShadowMapper.CasterCull = true;
			Sandbox.Rendering.ShadowMapper.ResetCache();
			CollectAndPrepare( system, world, LookingDownX() );
			Assert.AreEqual( 1, system.ShadowMaps.Passes.Length, "only the face looking back at the camera" );
		}
		finally
		{
			Sandbox.Rendering.ShadowMapper.CasterCull = casterCull;
		}
	}

	/// <summary>
	/// A VR eye's projection is built from FOV tangents and has no far plane, so its corners at clip depth 0 come back
	/// with w = 0 and land at infinity. The camera hull every caster cull tests against has to cap them instead of
	/// carrying infinities into the plane maths - and cap them long, since a hull that's too short would cull shadow
	/// views an eye that sees forever still needs.
	/// </summary>
	[TestMethod]
	public void CameraHullCapsAProjectionWithNoFarPlane()
	{
		const float near = 5f, tangent = 1f, aspect = 1.2f;

		// Reverse-Z infinite perspective, inverted: clip (X,Y,Z,W) -> view (X*aspect*tangent, Y*tangent, W, Z/near).
		// The far ring is at clip Z = 0, so it comes out with w = 0.
		var projectionToView = new System.Numerics.Matrix4x4(
			aspect * tangent, 0, 0, 0,
			0, tangent, 0, 0,
			0, 0, 0, 1f / near,
			0, 0, 1, 0 );

		var view = new Sandbox.Rendering.ShadowViewInfo
		{
			ProjectionToView = projectionToView,
			ViewToWorldRotation = System.Numerics.Matrix4x4.Identity,
			CameraPosition = Vector3.Zero,
			ViewportSize = new Vector2( 1000, 1000 ),
		};

		var corners = new Vector3[8];
		Sandbox.Rendering.ShadowMapper.BuildCameraHull( view, corners );

		foreach ( var corner in corners )
			Assert.IsTrue( corner.IsNaN == false && float.IsFinite( corner.Length ), $"corner {corner} isn't finite" );

		// The near ring unprojects where the projection puts it, and the far ring runs on out along the same lines
		for ( int i = 0; i < 4; i++ )
		{
			Assert.AreEqual( near, corners[i].z, 1e-3f, $"near corner {i}" );
			Assert.AreEqual( 262144f, corners[i + 4].z, 1f, $"far corner {i}" );
			Assert.AreEqual( corners[i].x / near, corners[i + 4].x / 262144f, 1e-3f, $"far corner {i} left the line" );
			Assert.AreEqual( corners[i].y / near, corners[i + 4].y / 262144f, 1e-3f, $"far corner {i} left the line" );
		}
	}

	/// <summary>
	/// A face left out before renders as soon as the camera turns to where it can see what that face casts onto -
	/// the map would hand back depth that was never drawn otherwise.
	/// </summary>
	[TestMethod]
	public void ACulledFaceRendersOnceTheCameraNeedsIt()
	{
		var world = new RenderWorld();
		world.Lighting.SunColor = Color.Black;
		world.Add( new MeshObject( null, new Transform( new Vector3( 50, 0, 0 ) ) ) { LocalBounds = UnitBox } );
		world.Add( new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( -50, 0, 0 ) ) ) { Radius = 200 } );

		var casterCull = Sandbox.Rendering.ShadowMapper.CasterCull;
		try
		{
			Sandbox.Rendering.ShadowMapper.CasterCull = true;
			Sandbox.Rendering.ShadowMapper.ResetCache();
			using var system = new RenderSystem();

			// The light is behind the camera: only the face looking back along +X (CubeRotations[1]) reaches the view
			CollectAndPrepare( system, world, LookingDownX() );
			var slices = system.ShadowMaps.Passes.ToArray().Select( x => x.TargetSlice ).ToArray();
			CollectionAssert.AreEqual( new[] { 1 }, slices );

			// Turn around and the light is in front, so the face pointing away from the camera covers what it sees
			var turned = LookingDownX();
			turned.Rotation = Rotation.FromYaw( 180 );
			turned.Update();
			CollectAndPrepare( system, world, turned );

			slices = system.ShadowMaps.Passes.ToArray().Select( x => x.TargetSlice ).ToArray();
			CollectionAssert.Contains( slices, 0, "the face the camera turned toward" );
		}
		finally
		{
			Sandbox.Rendering.ShadowMapper.CasterCull = casterCull;
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
