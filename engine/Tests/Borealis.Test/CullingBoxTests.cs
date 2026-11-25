using System;
using Borealis;
using Borealis.Culling;
using Borealis.Features;
using static BorealisTests.TestScenes;

namespace BorealisTests;

/// <summary>
/// A world's culling boxes (<see cref="RenderWorld.CullingBoxes"/>): one that culls inside hides what's fully inside it, and
/// with any that cull outside, what touches none of those is hidden. Lights aren't culled by them.
/// </summary>
[TestClass]
public class CullingBoxTests
{
	[TestMethod]
	public void ABoxHidesWhatIsFullyInsideIt()
	{
		var box = new CullingBox( new Transform( new Vector3( 20, 0, 0 ) ), new Vector3( 4 ), cullsInside: true );
		ReadOnlySpan<CullingBox> boxes = [box];

		Assert.IsTrue( CullingBox.Culls( boxes, new Vector3( 20, 0, 0 ), new Vector3( 1 ) ), "inside" );
		Assert.IsTrue( CullingBox.Culls( boxes, new Vector3( 21, 0, 0 ), new Vector3( 1 ) ), "inside, touching a face" );
		Assert.IsFalse( CullingBox.Culls( boxes, new Vector3( 21.5f, 0, 0 ), new Vector3( 1 ) ), "partly outside" );
		Assert.IsFalse( CullingBox.Culls( boxes, new Vector3( 40, 0, 0 ), new Vector3( 1 ) ), "outside" );
	}

	[TestMethod]
	public void OutsideBoxesHideWhatTouchesNoneOfThem()
	{
		ReadOnlySpan<CullingBox> boxes =
		[
			new CullingBox( new Transform( new Vector3( 20, 0, 0 ) ), new Vector3( 4 ), cullsInside: false ),
			new CullingBox( new Transform( new Vector3( -20, 0, 0 ) ), new Vector3( 4 ), cullsInside: false ),
		];

		Assert.IsFalse( CullingBox.Culls( boxes, new Vector3( 20, 0, 0 ), new Vector3( 1 ) ), "inside the first" );
		Assert.IsFalse( CullingBox.Culls( boxes, new Vector3( -22.5f, 0, 0 ), new Vector3( 1 ) ), "overlapping the second" );
		Assert.IsTrue( CullingBox.Culls( boxes, new Vector3( 0, 0, 0 ), new Vector3( 1 ) ), "between them" );
		Assert.IsFalse( CullingBox.Culls( [], new Vector3( 0, 0, 0 ), new Vector3( 1 ) ), "no boxes" );
	}

	[TestMethod]
	public void BoxesTurnAndScaleWithTheirTransform()
	{
		// 4 x 40 x 4 turned a quarter about z: 40 long along x. Scale 2 makes it 8 x 80 x 8
		var turned = new CullingBox( new Transform( Vector3.Zero, Rotation.FromYaw( 90 ) ), new Vector3( 4, 40, 4 ), cullsInside: true );
		Assert.IsTrue( turned.Contains( new Vector3( 15, 0, 0 ), new Vector3( 1 ) ) );
		Assert.IsFalse( turned.Contains( new Vector3( 0, 15, 0 ), new Vector3( 1 ) ) );

		var scaled = new CullingBox( new Transform( Vector3.Zero, Rotation.Identity, 2 ), new Vector3( 4 ), cullsInside: true );
		Assert.IsTrue( scaled.Contains( new Vector3( 2.5f, 0, 0 ), new Vector3( 1 ) ) );
		Assert.IsFalse( scaled.Contains( new Vector3( 3.5f, 0, 0 ), new Vector3( 1 ) ) );
	}

	[TestMethod]
	public void TheCullLeavesOutWhatBoxesHide()
	{
		var world = new RenderWorld();
		var mesh = TestMesh( false );
		var hidden = new MeshObject( mesh, new Transform( new Vector3( 20, 0, 0 ) ) );
		var kept = new MeshObject( mesh, new Transform( new Vector3( 20, 10, 0 ) ) );
		var light = new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 20, 0, 0 ) ) ) { Radius = 1, CastShadows = false };
		world.Add( hidden );
		world.Add( kept );
		world.Add( light );

		using var system = new RenderSystem();
		var meshes = system.GetFeature<MeshRenderFeature>();

		var stats = Collect( system, world );
		CollectionAssert.AreEquivalent( new[] { hidden.Index, kept.Index }, system.MainPass.Visible( meshes ).ToArray(), "no boxes" );

		world.CullingBoxes.Add( new CullingBox( new Transform( new Vector3( 20, 0, 0 ) ), new Vector3( 4 ), cullsInside: true ) );
		stats = Collect( system, world );
		CollectionAssert.AreEquivalent( new[] { kept.Index }, system.MainPass.Visible( meshes ).ToArray(), "inside the box" );
		Assert.AreEqual( 1, stats.ObjectsBoxCulled );
		Assert.AreEqual( 1, system.MainPass.Visible( system.GetFeature<LightBinnerFeature>() ).Count, "a light isn't culled by boxes" );

		// It's tested every frame: one moved out of the box draws again
		hidden.Transform = new Transform( new Vector3( 20, -10, 0 ) );
		Collect( system, world );
		CollectionAssert.AreEquivalent( new[] { hidden.Index, kept.Index }, system.MainPass.Visible( meshes ).ToArray(), "moved out" );

		world.CullingBoxes.Clear();
		world.CullingBoxes.Add( new CullingBox( new Transform( new Vector3( 20, 10, 0 ) ), new Vector3( 4 ), cullsInside: false ) );
		Collect( system, world );
		CollectionAssert.AreEquivalent( new[] { kept.Index }, system.MainPass.Visible( meshes ).ToArray(), "only what touches the outside box" );
	}

	static RenderStats Collect( RenderSystem system, RenderWorld world )
	{
		var stats = new RenderStats();
		system.Collect( world, LookingDownX(), ref stats );
		return stats;
	}
}
