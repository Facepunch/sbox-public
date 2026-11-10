using Sandbox.SceneRenderer;
using System;
using static SceneRendererTests.TestScenes;

namespace SceneRendererTests;

/// <summary>
/// The world's dense storage: bounds and matrices following transforms, and swap-removal.
/// </summary>
[TestClass]
public class WorldTests
{
	[TestMethod]
	public void WorldBoundsFollowTransform()
	{
		var world = new RenderWorld();
		var obj = new TestObject( UnitBox, new Transform( new Vector3( 10, 20, 30 ), Rotation.FromYaw( 45 ), 2 ) );
		world.Add( obj );

		var center = world.BoundsCenter[obj.Index];
		var extents = world.BoundsExtents[obj.Index];

		Assert.AreEqual( 10, center.x, 1e-4f );
		Assert.AreEqual( 20, center.y, 1e-4f );
		Assert.AreEqual( 30, center.z, 1e-4f );

		// A unit box scaled by 2 and turned 45 degrees about z is 2 * sqrt(2) wide in x and y
		Assert.AreEqual( 2 * MathF.Sqrt( 2 ), extents.x, 1e-4f );
		Assert.AreEqual( 2 * MathF.Sqrt( 2 ), extents.y, 1e-4f );
		Assert.AreEqual( 2, extents.z, 1e-4f );

		// Moving it updates the dense arrays
		obj.Transform = new Transform( new Vector3( -5, 0, 0 ) );
		Assert.AreEqual( -5, world.BoundsCenter[obj.Index].x, 1e-4f );
		Assert.AreEqual( 1, world.BoundsExtents[obj.Index].x, 1e-4f );
	}

	[TestMethod]
	public void WorldMatrixMatchesTransform()
	{
		var world = new RenderWorld();
		var transform = new Transform( new Vector3( 1, 2, 3 ), Rotation.From( 30, 60, 10 ), new Vector3( 1, 2, 3 ) );
		var obj = new TestObject( UnitBox, transform );
		world.Add( obj );

		var local = new Vector3( 0.3f, -0.7f, 0.2f );
		var expected = transform.PointToWorld( local );
		var actual = world.LocalToWorld[obj.Index].Transform( local );

		Assert.AreEqual( expected.x, actual.x, 1e-4f );
		Assert.AreEqual( expected.y, actual.y, 1e-4f );
		Assert.AreEqual( expected.z, actual.z, 1e-4f );
	}

	[TestMethod]
	public void RemoveMovesLastObjectIntoSlot()
	{
		var world = new RenderWorld();
		var a = new TestObject( UnitBox, new Transform( new Vector3( 1, 0, 0 ) ) );
		var b = new TestObject( UnitBox, new Transform( new Vector3( 2, 0, 0 ) ) );
		var c = new TestObject( UnitBox, new Transform( new Vector3( 3, 0, 0 ) ) );
		world.Add( a );
		world.Add( b );
		world.Add( c );

		world.Remove( a );

		Assert.AreEqual( 2, world.Count );
		Assert.IsNull( a.World );
		Assert.AreEqual( -1, a.Index );
		Assert.AreEqual( 0, c.Index );
		Assert.AreSame( c, world.Objects[0] );
		Assert.AreEqual( 3, world.BoundsCenter[0].x, 1e-4f );

		// Grows past the initial capacity
		for ( int i = 0; i < 200; i++ )
			world.Add( new TestObject( UnitBox, new Transform( new Vector3( i, 0, 0 ) ) ) );

		Assert.AreEqual( 202, world.Count );
		Assert.AreEqual( 199, world.BoundsCenter[201].x, 1e-4f );
	}
}
