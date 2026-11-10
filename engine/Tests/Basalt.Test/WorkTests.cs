using Basalt;
using Basalt.Features;
using BasaltTests.Headless;
using System.Linq;
using static BasaltTests.TestScenes;

namespace BasaltTests;

/// <summary>
/// Work a frame mustn't repeat, counted rather than timed, as timings drift from machine to machine: a batch key per
/// object, a sort of buckets already in order, a readback every frame.
/// </summary>
// Frames go through Basalt's ShadowMapper, whose cache and frame counter are static
[TestClass]
[DoNotParallelize]
public class WorkTests
{
	static RenderWorld Boxes( RenderMesh mesh, int count )
	{
		var world = new RenderWorld();
		for ( int i = 0; i < count; i++ )
			world.Add( new MeshObject( mesh, new Transform( new Vector3( 100 + i % 10 * 3, i / 10 % 10 * 3 - 15, i / 100 * 3 - 15 ) ) ) );
		return world;
	}

	/// <summary>
	/// Objects that share everything share a batch key, which each view makes once rather than once an object, and their
	/// buckets are already in order, so none is sorted. Hashing every object's references cost more than the rest of
	/// classifying it.
	/// </summary>
	[TestMethod]
	public void IdenticalObjectsShareABatchKeyAndSkipTheSort()
	{
		var world = Boxes( TestMesh( false ), 1000 );
		var view = LookingDownX();
		using var system = new RenderSystem();
		Basalt.Shadows.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, view );

		var feature = system.GetFeature<MeshRenderFeature>();
		var keys = feature.KeysMade;
		var sorted = feature.BucketsSorted;
		CollectAndPrepare( system, world, view );

		Assert.AreEqual( 1000, system.MainPass.Visible( feature ).Count );
		var views = 1 + system.ShadowMaps.Passes.Length;
		Assert.IsTrue( feature.KeysMade - keys <= views, $"{feature.KeysMade - keys} keys made for {views} views" );
		Assert.AreEqual( sorted, feature.BucketsSorted, "no bucket sorted" );
	}

	/// <summary>
	/// The occlusion cull's counts are read back every <see cref="Basalt.Culling.OcclusionCuller.StatsInterval"/> frames, not
	/// every frame: each read makes a staging buffer in native, a fraction of a millisecond.
	/// </summary>
	[TestMethod]
	public void OcclusionStatsAreReadNowAndThen()
	{
		var mesh = new RenderMesh( new HeadlessMesh( "box" ), UnitBox, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, null )]] );
		var world = Boxes( mesh, 100 );
		using var target = ViewTarget.Create( new Vector2Int( 64, 64 ) );
		using var system = new RenderSystem();
		var view = LookingDownX();
		Basalt.Shadows.ShadowMapper.ResetCache();

		const int frames = Basalt.Culling.OcclusionCuller.StatsInterval * 2;
		var reads = 0;
		for ( int i = 0; i < frames; i++ )
		{
			using var frame = HeadlessRenderHost.Headless.Capture();
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );
			Assert.IsTrue( system.OcclusionActive, "culling" );
			reads += frame.Commands.Count( c => c.Op == GpuOp.ReadBuffer );
		}

		Assert.AreEqual( 2, reads );
	}
}
