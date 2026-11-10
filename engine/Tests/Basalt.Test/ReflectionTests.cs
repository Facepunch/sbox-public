using Basalt;
using System;
using System.Linq;
using static BasaltTests.TestScenes;

namespace BasaltTests;

/// <summary>
/// A reflection view (<see cref="RenderView.Reflection"/>) drawn as native's pipeline draws one: no shadows unless asked, then
/// cascades of its own, and no bloom objects.
/// </summary>
// Frames go through Basalt's ShadowMapper, whose cache and frame counter are static
[TestClass]
[DoNotParallelize]
public class ReflectionTests
{
	static RenderWorld World()
	{
		var world = new RenderWorld();
		world.Add( new MeshObject( null, new Transform( new Vector3( 10, 0, 0 ) ) ) { LocalBounds = UnitBox } );
		world.Add( new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 20, 0, 0 ) ) ) { Radius = 50 } );
		return world;
	}

	/// <summary>
	/// Unshadowed, the sun still lights a reflection but has no cascades, and lights get no maps: it waits on no shadow map.
	/// With <see cref="RenderView.ReflectionShadows"/> it draws them.
	/// </summary>
	[TestMethod]
	public void ReflectionsDrawShadowsOnlyWhenAsked()
	{
		var world = World();
		using var system = new RenderSystem();

		var view = LookingDownX();
		view.Reflection = true;
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 0, system.ShadowMaps.Passes.Length, "no shadow views" );
		Assert.AreEqual( 0u, system.ShadowMaps.Directional.CascadeCount, "no cascades" );
		Assert.IsTrue( system.ShadowMaps.Directional.Enabled, "the sun still lights it" );

		view.ReflectionShadows = true;
		CollectAndPrepare( system, world, view );
		Assert.IsTrue( system.ShadowMaps.Directional.CascadeCount > 0, "cascades" );
		Assert.IsTrue( system.ShadowMaps.Passes.ToArray().Any( p => !p.View.Orthographic ), "and the point light's faces" );
	}

	/// <summary>
	/// Nothing samples a reflection's bloom, so its bloom objects aren't drawn.
	/// </summary>
	[TestMethod]
	public void ReflectionsDrawNoBloomObjects()
	{
		var world = new RenderWorld();
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 20, 0, 0 ) ) ) { Bloom = true } );

		using var system = new RenderSystem();
		var view = LookingDownX();
		CollectAndPrepare( system, world, view );
		var bloom = system.Layers.OfType<BloomLayer>().Single();
		Assert.IsTrue( bloom.IsNeeded( system.Frame ), "a view draws them" );

		view.Reflection = true;
		CollectAndPrepare( system, world, view );
		Assert.IsFalse( bloom.IsNeeded( system.Frame ), "a reflection doesn't" );
	}
}
