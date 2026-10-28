using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Features;
using System.Linq;
using static SceneRendererTests.TestScenes;

namespace SceneRendererTests;

/// <summary>
/// Aggregates, a map's world geometry: each view culls their fragments on its own, the survivors are one batch, and every
/// fragment's transform is written once a frame where its draw's base instance finds it.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AggregateTests
{
	static AggregateObject.FragmentDraw Fragment( int index, Vector3 center, float size = 1, uint indices = 3 ) => new()
	{
		Transform = Matrix3x4.Identity,
		BoundsMin = center - size,
		BoundsMax = center + size,
		DrawTint = Vector3.One,
		Args = new GpuBuffer.IndirectDrawIndexedArguments { IndexCount = indices, InstanceCount = 1, FirstIndex = (uint)index * 3, FirstInstance = (uint)index },
	};

	/// <summary>
	/// In front, behind, off to the side, and too small to see from the camera looking down +X.
	/// </summary>
	internal static AggregateObject TestAggregate() => new( TestMesh( false, false, false, false ),
	[
		Fragment( 0, new Vector3( 20, 0, 0 ), indices: 6 ),
		Fragment( 1, new Vector3( -20, 0, 0 ) ),
		Fragment( 2, new Vector3( 20, 500, 0 ) ),
		Fragment( 3, new Vector3( 900, 0, 0 ), size: 0.01f ),
	] );

	[TestMethod]
	public void FragmentsAreCulledOnTheirOwn()
	{
		var world = new RenderWorld();
		var aggregate = TestAggregate();
		world.Add( aggregate );

		using var system = new RenderSystem();
		Sandbox.Rendering.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, LookingDownX() );

		var feature = system.GetFeature<AggregateRenderFeature>();
		var batch = feature.BatchesFor( system.MainPass ).Single();
		Assert.AreEqual( 1, batch.Count, "only the fragment in front is drawn" );
		Assert.AreEqual( 2, batch.Triangles );

		var args = feature.Args[batch.FirstArg];
		Assert.AreEqual( 0u, args.FirstInstance, "its base instance is its fragment's index" );
		Assert.AreEqual( 6u, args.IndexCount );
	}

	[TestMethod]
	public void EveryFragmentIsWrittenOnceAFrame()
	{
		var world = new RenderWorld();
		var aggregate = TestAggregate();
		aggregate.Tint = new Color( 0.5f, 1, 1 );
		world.Add( aggregate );

		using var system = new RenderSystem();
		Sandbox.Rendering.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, LookingDownX() );

		// The cascades draw it too, from the same entries
		var feature = system.GetFeature<AggregateRenderFeature>();
		Assert.IsTrue( system.ShadowMaps.Passes.ToArray().Any( p => feature.BatchesFor( p ).Count > 0 ), "a cascade drew it" );

		var transforms = system.Transforms;
		Assert.AreEqual( 4, transforms.Count, "an entry per fragment, culled or not" );
		Assert.AreEqual( 4, transforms.InstanceCount, "ids in fragment order, shared by every view" );
		for ( int i = 0; i < 4; i++ )
			Assert.AreEqual( i, transforms.InstanceSlot( aggregate.FirstInstance + i ) );

		// 127.5 rounds to even
		Assert.AreEqual( 0x80FFFFu, transforms[0].TintRgb888, "the object's tint times the draw's" );
	}

	[TestMethod]
	public void FragmentsThatDoNotCastShadowsAreLeftOutOfShadowMaps()
	{
		var world = new RenderWorld();
		var fragment = Fragment( 0, new Vector3( 20, 0, 0 ) );
		fragment.Flags = AggregateFragmentFlags.DoNotCastShadows;
		world.Add( new AggregateObject( TestMesh( false ), [fragment] ) );

		using var system = new RenderSystem();
		Sandbox.Rendering.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, LookingDownX() );

		var feature = system.GetFeature<AggregateRenderFeature>();
		Assert.AreEqual( 1, feature.BatchesFor( system.MainPass ).Count );
		Assert.IsTrue( system.ShadowMaps.Passes.ToArray().All( p => feature.BatchesFor( p ).Count == 0 ) );
	}

	[TestMethod]
	public void MainViewFragmentsAreOcclusionCulled()
	{
		var world = new RenderWorld();
		world.Add( TestAggregate() );

		using var system = new RenderSystem();
		Sandbox.Rendering.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, LookingDownX() );

		var batch = system.GetFeature<AggregateRenderFeature>().BatchesFor( system.MainPass ).Single();
		Assert.IsTrue( batch.CullArg >= 0, "the batch has a cull run" );
		Assert.AreEqual( 1, system.Occlusion.InstanceCount, "an instance per drawn fragment" );
	}
}
