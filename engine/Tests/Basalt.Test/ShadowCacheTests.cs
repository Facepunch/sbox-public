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
}
