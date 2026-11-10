using Basalt;
using Basalt.Features;
using System;
using static BasaltTests.TestScenes;

namespace BasaltTests;

/// <summary>
/// When a light's cached shadow map, and its static cache, are kept and when they're drawn again.
/// </summary>
// Frames go through Basalt's ShadowMapper, whose cache and frame counter are static
[TestClass]
[DoNotParallelize]
public class ShadowCacheTests
{
	[TestInitialize]
	public void ResetCache() => Basalt.Shadows.ShadowMapper.ResetCache();

	/// <summary>
	/// The views drawing a static cache this frame.
	/// </summary>
	static int StaticCacheViews( RenderSystem system ) => system.ShadowMaps.Passes.ToArray().Count( p => p.StaticFilter == StaticFilter.OnlyStatic );

	static MeshObject StaticBox( RenderWorld world, Vector3 position )
	{
		var box = new MeshObject( null, new Transform( position ) ) { LocalBounds = UnitBox, IsStatic = true };
		world.Add( box );
		return box;
	}

	/// <summary>
	/// A static light draws its static casters once, into a cache. Adding, removing, moving, hiding or changing a static caster
	/// within its reach draws the cache again; a change out of its reach doesn't.
	/// </summary>
	[TestMethod]
	public void StaticCacheRedrawsWhenAStaticCasterInReachChanges()
	{
		var world = new RenderWorld();
		world.Lighting.SunColor = Color.Black;
		var near = StaticBox( world, new Vector3( 100, 0, -20 ) );
		var far = StaticBox( world, new Vector3( 400, 0, -20 ) );
		world.Add( new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 100, 0, 0 ) ) ) { Radius = 50, IsStatic = true } );

		var view = LookingDownX();
		using var system = new RenderSystem();
		var mesh = system.GetFeature<MeshRenderFeature>();

		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 6, StaticCacheViews( system ), "drawn once" );
		Assert.IsTrue( system.ShadowMaps.Passes.ToArray().Any( p => p.StaticFilter == StaticFilter.OnlyStatic && p.Visible( mesh ).Contains( near.Index ) ) );

		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 0, StaticCacheViews( system ), "then kept" );

		void Redraws( string change, Action act, bool expected = true )
		{
			act();
			CollectAndPrepare( system, world, view );
			Assert.AreEqual( expected ? 6 : 0, StaticCacheViews( system ), change );
			CollectAndPrepare( system, world, view );
			Assert.AreEqual( 0, StaticCacheViews( system ), $"kept after {change}" );
		}

		Redraws( "moved out of reach", () => far.Transform = new Transform( new Vector3( 420, 0, -20 ) ), expected: false );
		Redraws( "moved", () => near.Transform = new Transform( new Vector3( 100, 5, -20 ) ) );
		Redraws( "stopped casting", () => near.CastShadows = false );
		Redraws( "cast again", () => near.CastShadows = true );
		Redraws( "faded", () => near.Tint = Color.White.WithAlpha( 0.5f ) );
		Redraws( "hidden", () => near.Mesh = null );
		Redraws( "made dynamic", () => near.IsStatic = false );
		Redraws( "made static", () => near.IsStatic = true );
		Redraws( "removed", () => world.Remove( near ) );
		Redraws( "added", () => StaticBox( world, new Vector3( 100, 0, 20 ) ) );
		Redraws( "added out of reach", () => StaticBox( world, new Vector3( 400, 0, 20 ) ), expected: false );
	}

	/// <summary>
	/// Run <paramref name="test"/> with only new, moved or changed lights' shadows drawn: none waits its turn for an update.
	/// </summary>
	static void WithoutUpdates( Action test )
	{
		var updates = RenderHost.Current.Shadows.MaxUpdatesPerFrame;
		RenderHost.Current.Shadows.MaxUpdatesPerFrame = 0;
		try
		{
			test();
		}
		finally
		{
			RenderHost.Current.Shadows.MaxUpdatesPerFrame = updates;
		}
	}

	/// <summary>
	/// A light's radius, cone and kind make its projection, so changing one draws its shadow again at once, as moving it does,
	/// rather than keeping the old projection's map until its next update.
	/// </summary>
	[TestMethod]
	public void ChangingALightsShapeRedrawsItsShadow() => WithoutUpdates( () =>
	{
		var world = new RenderWorld();
		world.Lighting.SunColor = Color.Black;
		world.Add( new MeshObject( null, new Transform( new Vector3( 300, 0, -20 ) ) ) { LocalBounds = UnitBox } );
		var light = new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 300, 0, 0 ), Rotation.LookAt( Vector3.Down ) ) ) { Radius = 50 };
		world.Add( light );

		var view = LookingDownX();
		using var system = new RenderSystem();

		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 6, system.ShadowMaps.Passes.Length );
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 0, system.ShadowMaps.Passes.Length, "the point light's map is kept" );

		light.Radius = 80;
		CollectAndPrepare( system, world, view );
		var passes = system.ShadowMaps.Passes.ToArray();
		Assert.AreEqual( 6, passes.Length, "a new radius" );
		Assert.IsTrue( passes.All( p => p.View.ZFar == 80 ) );

		light.Kind = LightObject.LightKind.Spot;
		CollectAndPrepare( system, world, view );
		passes = system.ShadowMaps.Passes.ToArray();
		Assert.AreEqual( 1, passes.Length, "a spot now" );
		Assert.IsFalse( passes[0].TargetMap.IsCube );

		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 0, system.ShadowMaps.Passes.Length, "the spot's map is kept" );

		light.ConeOuter = 30;
		CollectAndPrepare( system, world, view );
		passes = system.ShadowMaps.Passes.ToArray();
		Assert.AreEqual( 1, passes.Length, "a new cone" );
		Assert.AreEqual( 60, passes[0].View.FieldOfView );

		light.Color = Color.Red;
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 0, system.ShadowMaps.Passes.Length, "colour doesn't change the shadow" );
	} );
}
