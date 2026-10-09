using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Culling;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SceneRendererTests;

/// <summary>
/// The world's bounding volume tree: its invariants hold through any mix of adds, moves and removes, and
/// its queries find what a brute force scan finds - including when it drops small subtrees.
/// </summary>
[TestClass]
public class SpatialTreeTests
{
	static RenderView Camera()
	{
		var view = new RenderView
		{
			Position = new Vector3( -200, 0, 50 ),
			Rotation = Rotation.Identity,
			FieldOfView = 90,
			ZNear = 1,
			ZFar = 5000,
			Viewport = new Rect( 0, 0, 1000, 1000 ),
		};
		view.Update();
		return view;
	}

	static RenderWorld RandomWorld( Random random, int count )
	{
		var world = new RenderWorld();
		for ( int i = 0; i < count; i++ )
		{
			var size = 0.5f + random.NextSingle() * 20;
			var position = new Vector3( random.NextSingle() * 4000 - 2000, random.NextSingle() * 4000 - 2000, random.NextSingle() * 400 );
			world.Add( new MeshObject( null, new Transform( position ) ) { LocalBounds = new BBox( new Vector3( -size ), new Vector3( size ) ) } );
		}

		return world;
	}

	[TestMethod]
	public void MoveRetainsPaddingButShrinksOversizedBounds()
	{
		var tree = new SpatialTree();
		var leaf = tree.Add( new Vector3( -1000 ), new Vector3( 1000 ), 42 );
		Assert.IsFalse( tree.Move( leaf, new Vector3( -999 ), new Vector3( 1001 ) ) );

		Assert.IsTrue( tree.Move( leaf, -Vector3.One, Vector3.One ), "shrinking must discard the old oversized bounds" );
		Assert.IsFalse( tree.Move( leaf, new Vector3( -0.5f ), new Vector3( 1.5f ) ), "small moves should retain padding" );
		tree.Validate();

		var results = new List<int>();
		var query = new SpatialTree.SphereQuery { Center = new Vector3( 500, 0, 0 ), Radius = 10 };
		tree.Query( ref query, results );
		Assert.AreEqual( 0, results.Count, "the old bounds must no longer produce candidates" );

		query.Center = Vector3.Zero;
		tree.QueryClassified( ref query, results, out var rejected );
		CollectionAssert.AreEqual( new[] { 42 << 1 | 1 }, results );
		Assert.AreEqual( 0, rejected );
	}

	[TestMethod]
	public void SortedInsertsMovesAndRemovalsPreserveAllLeaves()
	{
		var tree = new SpatialTree();
		var leaves = new int[512];
		var expected = new HashSet<int>();
		var results = new List<int>();
		var query = new SpatialTree.SphereQuery { Radius = 100000 };

		for ( int cycle = 0; cycle < 2; cycle++ )
		{
			for ( int i = 0; i < leaves.Length; i++ )
			{
				var center = new Vector3( i * 10, 0, 0 );
				leaves[i] = tree.Add( center - Vector3.One, center + Vector3.One, i );
				expected.Add( i );
				tree.Validate();
			}

			for ( int i = 0; i < leaves.Length; i++ )
			{
				var center = new Vector3( -i * 20, 100, 0 );
				tree.Move( leaves[i], center - Vector3.One, center + Vector3.One );
				tree.Validate();
			}

			for ( int i = 0; i < leaves.Length; i++ )
			{
				results.Clear();
				tree.Query( ref query, results );
				Assert.AreEqual( expected.Count, results.Count );
				Assert.IsTrue( expected.SetEquals( results ) );

				tree.Remove( leaves[i] );
				expected.Remove( i );
				tree.Validate();
			}

			results.Clear();
			tree.QueryClassified( ref query, results, out var rejected );
			Assert.AreEqual( 0, results.Count );
			Assert.AreEqual( 0, rejected );
			Assert.AreEqual( 0, tree.NodeCount );
		}
	}

	[TestMethod]
	public void StaysValidThroughChanges()
	{
		var random = new Random( 1 );
		var world = RandomWorld( random, 2000 );
		world.Tree.Validate();

		var objects = new List<RenderObject>();
		for ( int i = 0; i < world.Count; i++ ) objects.Add( GetObject( world, i ) );

		for ( int step = 0; step < 3000; step++ )
		{
			var obj = objects[random.Next( objects.Count )];
			switch ( random.Next( 3 ) )
			{
				case 0:
					// Nudge - usually stays inside its padding
					obj.Transform = obj.Transform.WithPosition( obj.Transform.Position + new Vector3( random.NextSingle() - 0.5f ) );
					break;
				case 1:
					// Jump - always reinserts
					obj.Transform = obj.Transform.WithPosition( new Vector3( random.NextSingle() * 4000 - 2000, random.NextSingle() * 4000 - 2000, 0 ) );
					break;
				case 2:
					world.Remove( obj );
					world.Add( obj );
					break;
			}
		}

		world.Tree.Validate();
		Assert.AreEqual( 2 * world.Count - 1, world.Tree.NodeCount, "a leaf per object and a branch per pair" );
		Assert.IsTrue( world.Tree.Height < 40, $"height {world.Tree.Height} for {world.Count} objects" );
	}

	[TestMethod]
	public void QueriesMatchBruteForce()
	{
		var random = new Random( 2 );
		var world = RandomWorld( random, 3000 );
		var view = Camera();
		var frustum = view.Frustum;
		var centers = world.BoundsCenter.ToArray();
		var extents = world.BoundsExtents.ToArray();

		// Frustum: the tree's candidates, exactly tested, are what testing everything finds
		var candidates = new List<int>();
		var frustumQuery = new SpatialTree.FrustumQuery { Frustum = frustum };
		world.Tree.QueryClassified( ref frustumQuery, candidates, out var rejected );
		Assert.AreEqual( 0, rejected, "no size cull asked for" );

		var fromTree = candidates.Where( e => (e & 1) != 0 || frustum.Intersects( centers[e >> 1], extents[e >> 1] ) ).Select( e => e >> 1 ).ToHashSet();
		var brute = Enumerable.Range( 0, world.Count ).Where( i => frustum.Intersects( centers[i], extents[i] ) ).ToHashSet();
		Assert.IsTrue( brute.SetEquals( fromTree ), $"tree found {fromTree.Count}, brute force {brute.Count}" );
		Assert.IsTrue( candidates.Any( e => (e & 1) != 0 ), "some subtrees are wholly inside" );

		// Sphere: every object touching it is among the candidates
		var center = new Vector3( 300, -200, 100 );
		const float radius = 600;
		candidates.Clear();
		var sphereQuery = new SpatialTree.SphereQuery { Center = center, Radius = radius };
		world.Tree.Query( ref sphereQuery, candidates );

		var found = candidates.ToHashSet();
		for ( int i = 0; i < world.Count; i++ )
		{
			var nearest = center.Clamp( centers[i] - extents[i], centers[i] + extents[i] );
			if ( nearest.Distance( center ) <= radius )
				Assert.IsTrue( found.Contains( i ), $"object {i} touches the sphere but wasn't found" );
		}
	}

	/// <summary>
	/// Dropping a subtree as too small never drops an object the exact per-object test would keep, and it
	/// does drop most of what that test drops.
	/// </summary>
	[TestMethod]
	public void SubtreeSizeCullIsConservative()
	{
		var random = new Random( 3 );
		var world = RandomWorld( random, 5000 );
		var view = Camera();
		view.SizeCullThreshold = 0.02f;
		var frustum = view.Frustum;
		var size = new SizeCull( view );
		var centers = world.BoundsCenter;
		var extents = world.BoundsExtents;

		var candidates = new List<int>();
		var query = new SpatialTree.FrustumQuery { Frustum = frustum, Size = size };
		world.Tree.QueryClassified( ref query, candidates, out var rejected );

		var returned = candidates.Select( e => e >> 1 ).ToHashSet();
		var kept = 0;
		var culled = 0;
		for ( int i = 0; i < world.Count; i++ )
		{
			if ( !frustum.Intersects( centers[i], extents[i] ) ) continue;
			if ( size.Culls( centers[i], extents[i] ) )
			{
				culled++;
				continue;
			}

			kept++;
			Assert.IsTrue( returned.Contains( i ), $"object {i} is big enough to draw, but its subtree was dropped" );
		}

		Assert.IsTrue( culled > 0 && rejected > culled / 2, $"dropped {rejected} a subtree at a time of {culled} too small ({kept} kept)" );
	}

	static RenderObject GetObject( RenderWorld world, int index )
	{
		// Objects aren't exposed publicly by index
		return world.Objects[index];
	}
}
