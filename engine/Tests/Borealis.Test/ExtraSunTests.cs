using Borealis;
using Borealis.Features;
using static BorealisTests.TestScenes;

namespace BorealisTests;

/// <summary>
/// Directional lights besides the sun (<see cref="SceneLighting.ExtraSuns"/>): packed after the binned lights for every pixel's
/// short list, a baked one also reached by its bake index, each with one orthographic shadow map.
/// </summary>
// Frames go through Borealis's ShadowMapper, whose cache and frame counter are static
[TestClass]
[DoNotParallelize]
public class ExtraSunTests
{
	static LightObject Light( RenderWorld world, float x, bool baked = false, int bakeIndex = -1 )
	{
		var light = new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( x, 0, 0 ) ) ) { Radius = 50, Baked = baked, BakeIndex = bakeIndex };
		world.Add( light );
		return light;
	}

	[TestMethod]
	public void OneSunPacksAsBefore()
	{
		var world = new RenderWorld();
		Light( world, 100 );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );
		var binner = system.GetFeature<LightBinnerFeature>();

		Assert.AreEqual( 0, binner.ExtraSunCount );
		Assert.AreEqual( 2, binner.UploadedLights.Length, "the light and the dummy" );
	}

	[TestMethod]
	public void ExtraSunsFollowTheBinnedLights()
	{
		var world = new RenderWorld();
		Light( world, 100 );
		Light( world, 120, baked: true, bakeIndex: 3 );

		var lighting = world.Lighting;
		lighting.SunShadows = false;
		var bakedSun = new LightBinnerFeature.GpuLight { Type = 2, Color = new Vector3( 0, 0, 2 ), Radius = float.MaxValue };
		lighting.ExtraSuns.Add( new ExtraSun { Direction = new Vector3( 0, 0, -1 ), Color = Color.Red } );
		lighting.ExtraSuns.Add( new ExtraSun { Direction = new Vector3( 1, 0, -1 ), Color = Color.Black } );
		lighting.ExtraSuns.Add( new ExtraSun { Direction = new Vector3( 0, 1, -1 ), Color = Color.Blue, BakeIndex = 5, Baked = bakedSun } );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );
		var binner = system.GetFeature<LightBinnerFeature>();

		// The binned lights (NumLights.x + NumLights.y), the two enabled suns, the baked one's entry, then the dummy
		Assert.AreEqual( 2, binner.Count );
		Assert.AreEqual( 2, binner.ExtraSunCount, "the black one lights nothing" );
		var uploaded = binner.UploadedLights;
		Assert.AreEqual( 6, uploaded.Length );

		var red = uploaded[2];
		Assert.AreEqual( 2u, red.Type );
		Assert.AreEqual( new Vector3( 1, 0, 0 ), red.Color );
		Assert.AreNotEqual( 0u, red.Flags & LightBinnerFeature.ExtraSunFlag );
		Assert.AreEqual( 0xFFFFFFFFu, red.ShadowMapIndex, "the sun's shadows are off, and so are the extra suns' by default" );

		// Forward, the shader's GetDirection, points towards the light; the position is far along it
		var forward = new Vector3( red.Row0.x, red.Row1.x, red.Row2.x );
		Assert.IsTrue( forward.Distance( new Vector3( 0, 0, 1 ) ) < 1e-5f, $"forward {forward}" );
		Assert.IsTrue( red.Row2.w > 1e6f, "placed far towards the light" );
		Assert.AreEqual( 0, binner.ExtraSunSource( 0 ), "the red sun is first in the list" );
		Assert.AreEqual( 2, binner.ExtraSunSource( 1 ) );

		// Baked surfaces reach the baked sun by its index, the other in the list
		Assert.AreEqual( 0u, red.Flags & 0x100 );
		Assert.AreNotEqual( 0u, uploaded[3].Flags & 0x100 );

		var blueForward = new Vector3( uploaded[3].Row0.x, uploaded[3].Row1.x, uploaded[3].Row2.x );
		Assert.IsTrue( blueForward.Distance( new Vector3( 0, -1, 1 ).Normal ) < 1e-5f, $"forward {blueForward}" );

		// The baked sun's index reaches its own packing, flagged so its shadow isn't the sun's
		Assert.AreEqual( 4, binner.BakedIndexMapping[5] );
		Assert.AreEqual( bakedSun.Color, uploaded[4].Color );
		Assert.AreNotEqual( 0u, uploaded[4].Flags & LightBinnerFeature.ExtraSunFlag );
		Assert.AreEqual( 1, binner.BakedIndexMapping[3], "the baked light keeps its slot" );
		Assert.AreEqual( 5, binner.BakedIndexMapping[0], "everything else goes to the dummy" );
		Assert.AreEqual( default, uploaded[5] );
	}

	[TestMethod]
	public void AShadowedExtraSunDrawsOneOrthographicMap()
	{
		var world = new RenderWorld();
		var box = new MeshObject( null, new Transform( new Vector3( 100, 0, -20 ) ) ) { LocalBounds = UnitBox };
		world.Add( box );
		world.Lighting.SunShadows = false;
		world.Lighting.ExtraSuns.Add( new ExtraSun { Direction = new Vector3( 0.3f, 0.2f, -1 ).Normal, Color = Color.Red, Shadows = true } );
		world.Lighting.ExtraSuns.Add( new ExtraSun { Direction = new Vector3( -0.3f, 0.2f, -1 ).Normal, Color = Color.Green } );

		using var system = new RenderSystem();
		Borealis.Shadows.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, LookingDownX() );
		var binner = system.GetFeature<LightBinnerFeature>();
		var mesh = system.GetFeature<MeshRenderFeature>();

		var passes = system.ShadowMaps.Passes;
		Assert.AreEqual( 1, passes.Length, "the shadowed extra sun's map only" );
		Assert.IsTrue( passes[0].View.Orthographic );
		CollectionAssert.Contains( passes[0].Visible( mesh ).ToList(), box.Index );

		var uploaded = binner.UploadedLights;
		Assert.AreEqual( 0u, uploaded[binner.Count].ShadowMapIndex, "the first projected shadow" );
		Assert.AreEqual( 0xFFFFFFFFu, uploaded[binner.Count + 1].ShadowMapIndex );
	}
}
