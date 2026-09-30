using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Features;
using static SceneRendererTests.TestScenes;

namespace SceneRendererTests;

/// <summary>
/// Baked lights, binned as native's <c>CLightBinnerStandard</c> bins them: after the dynamic ones, reached through the
/// bake index mapping, with shadow maps only when their shadows are mixed.
/// </summary>
// Frames go through the engine's ShadowMapper, whose cache and frame counter are static
[TestClass]
[DoNotParallelize]
public class BakedLightTests
{
	static LightObject Light( RenderWorld world, float x, bool baked, int bakeIndex = -1, bool mixed = false )
	{
		var light = new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( x, 0, 0 ) ) ) { Radius = 50, Baked = baked, BakeIndex = bakeIndex, MixedShadows = mixed };
		world.Add( light );
		return light;
	}

	[TestMethod]
	public void BakedLightsFollowDynamicOnes()
	{
		var world = new RenderWorld();
		world.Lighting.SunColor = Color.Black;

		// A baked light nearer than both dynamic ones, which would sort first by importance
		var baked = Light( world, 60, baked: true, bakeIndex: 7 );
		var near = Light( world, 100, baked: false );
		var far = Light( world, 300, baked: false, bakeIndex: 2 );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );
		var binner = system.GetFeature<LightBinnerFeature>();

		Assert.AreEqual( 3, binner.Count );
		Assert.AreEqual( 2, binner.DynamicCount );
		Assert.AreEqual( 1, binner.BakedCount );
		CollectionAssert.AreEqual( new[] { near, far, baked }, binner.PackedLights.ToArray() );

		// Each bake index to where its light was packed - a dynamic light's too - and every other index to the dummy
		// light after them, which is black
		var mapping = binner.BakedIndexMapping;
		Assert.AreEqual( 256, mapping.Length );
		Assert.AreEqual( 2, mapping[7] );
		Assert.AreEqual( 1, mapping[2] );

		var uploaded = binner.UploadedLights;
		Assert.AreEqual( 4, uploaded.Length, "the lights and the dummy" );
		Assert.AreEqual( 3, mapping[0] );
		Assert.AreEqual( 3, mapping[255] );
		Assert.AreEqual( default, uploaded[3] );
	}

	[TestMethod]
	public void SunWithABakeIndexIsPackedAfterTheLights()
	{
		var world = new RenderWorld();
		Light( world, 100, baked: true, bakeIndex: 1 );

		var sun = new LightBinnerFeature.GpuLight { Type = 2, Radius = float.MaxValue };
		world.Lighting.SunBakeIndex = 0;
		world.Lighting.SunBaked = sun;

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );
		var binner = system.GetFeature<LightBinnerFeature>();

		// The light, the sun, then the dummy
		var uploaded = binner.UploadedLights;
		Assert.AreEqual( 3, uploaded.Length );
		Assert.AreEqual( 0, binner.BakedIndexMapping[1] );
		Assert.AreEqual( 1, binner.BakedIndexMapping[0] );
		Assert.AreEqual( sun, uploaded[1] );
		Assert.AreEqual( 2, binner.BakedIndexMapping[2] );
	}

	/// <summary>
	/// A baked light's shadows are in its lightmaps unless they're mixed - native's <c>LIGHTTYPE_FLAGS_MIXED_SHADOWS</c>,
	/// Hammer's stationary lights - so only a dynamic or mixed light renders a shadow map.
	/// </summary>
	[TestMethod]
	public void OnlyMixedBakedLightsRenderShadowMaps()
	{
		var world = new RenderWorld();
		world.Lighting.SunColor = Color.Black;
		world.Add( new MeshObject( null, new Transform( new Vector3( 100, 0, -20 ) ) ) { LocalBounds = UnitBox } );

		Light( world, 100, baked: true );
		var mixed = Light( world, 100.5f, baked: true, mixed: true );

		using var system = new RenderSystem();
		Sandbox.Rendering.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, LookingDownX() );

		Assert.AreEqual( 6, system.ShadowMaps.Passes.Length, "the mixed light's cube faces only" );

		var binner = system.GetFeature<LightBinnerFeature>();
		var packed = binner.PackedLights;
		for ( int i = 0; i < packed.Length; i++ )
		{
			var expected = packed[i] == mixed ? 0u : 0xFFFFFFFF;
			Assert.AreEqual( expected, binner.PackedGpuLights[i].ShadowMapIndex, $"light {i}" );
		}
	}

	static LightProbeVolume Volume( Vector3 position, float size, int priority = 0, uint group = 0 ) => new()
	{
		Transform = new Transform( position ),
		BoxMins = new Vector3( -size ),
		BoxMaxs = new Vector3( size ),
		RenderPriority = priority,
		LightGroups = [group],
		Attributes = new RenderAttributes( default( NativeEngine.CRenderAttributes ) ),
	};

	/// <summary>
	/// An object is lit from one light probe volume, as <c>CLightBinner2::ChooseLightProbeVolume</c> picks it: one containing its
	/// origin over one that doesn't, then the higher priority, then the nearest; one that doesn't contain it must touch its bounds.
	/// </summary>
	[TestMethod]
	public void LightProbeVolumeIsChosenAsNativeChoosesIt()
	{
		var outdoor = Volume( Vector3.Zero, 100 );
		var indoor = Volume( new Vector3( 50, 0, 0 ), 20, priority: 1 );
		var away = Volume( new Vector3( 300, 0, 0 ), 50 );
		var volumes = new System.Collections.Generic.List<LightProbeVolume> { outdoor, indoor, away };

		static Vector3 At( float x ) => new( x, 0, 0 );
		var small = new Vector3( 1 );

		Assert.AreEqual( 0, LightProbeVolume.Choose( volumes, At( 0 ), At( 0 ), small, 0 ), "inside only the outdoor one" );
		Assert.AreEqual( 1, LightProbeVolume.Choose( volumes, At( 50 ), At( 50 ), small, 0 ), "inside both: the higher priority" );
		Assert.AreEqual( 2, LightProbeVolume.Choose( volumes, At( 245 ), At( 245 ), new Vector3( 10 ), 0 ), "inside none: the nearest it touches" );
		Assert.AreEqual( -1, LightProbeVolume.Choose( volumes, At( 200 ), At( 200 ), small, 0 ), "inside none, touching none" );
		Assert.AreEqual( -1, LightProbeVolume.Choose( volumes, At( 0 ), At( 0 ), small, 7 ), "none lights its light group" );

		// A volume that isn't rendering, or has no constants, lights nothing
		outdoor.Enabled = false;
		Assert.AreEqual( -1, LightProbeVolume.Choose( volumes, At( 0 ), At( 0 ), small, 0 ) );
		outdoor.Enabled = true;
		outdoor.Attributes = null;
		Assert.AreEqual( -1, LightProbeVolume.Choose( volumes, At( 0 ), At( 0 ), small, 0 ) );
	}

	/// <summary>
	/// Only objects that need a probe get one, in the main view, and objects in different volumes draw in different runs.
	/// </summary>
	[TestMethod]
	public void ObjectsThatNeedAProbeDrawWithTheirVolume()
	{
		var world = new RenderWorld();
		world.Lighting.SunColor = Color.Black;
		var near = Volume( new Vector3( 20, 0, 0 ), 5 );
		var far = Volume( new Vector3( 40, 0, 0 ), 5 );
		world.LightProbeVolumes.Add( near );
		world.LightProbeVolumes.Add( far );

		var mesh = TestMesh( false );
		var inNear = new MeshObject( mesh, new Transform( new Vector3( 20, 0, 0 ) ) ) { NeedsLightProbe = true };
		var inFar = new MeshObject( mesh, new Transform( new Vector3( 40, 0, 0 ) ) ) { NeedsLightProbe = true };
		var unlit = new MeshObject( mesh, new Transform( new Vector3( 20, 2, 0 ) ) );
		world.Add( inNear );
		world.Add( inFar );
		world.Add( unlit );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );
		var feature = system.GetFeature<MeshRenderFeature>();

		Assert.AreSame( near.Attributes, feature.ProbeFor( inNear ) );
		Assert.AreSame( far.Attributes, feature.ProbeFor( inFar ) );
		Assert.IsNull( feature.ProbeFor( unlit ) );

		var runs = feature.RunsFor( system.MainPass ).Opaque;
		Assert.AreEqual( 3, runs.Count, "one run per volume, and one without" );
		Assert.AreEqual( 1, runs.Count( r => r.Probe == near.Attributes ) );
		Assert.AreEqual( 1, runs.Count( r => r.Probe == far.Attributes ) );

		// The choice is kept between frames, and made again when an object moves or a volume changes
		inNear.Transform = new Transform( new Vector3( 40, 1, 0 ) );
		CollectAndPrepare( system, world, LookingDownX() );
		Assert.AreSame( far.Attributes, feature.ProbeFor( inNear ) );

		far.Enabled = false;
		CollectAndPrepare( system, world, LookingDownX() );
		Assert.IsNull( feature.ProbeFor( inNear ) );
		Assert.IsNull( feature.ProbeFor( inFar ) );
	}
}
