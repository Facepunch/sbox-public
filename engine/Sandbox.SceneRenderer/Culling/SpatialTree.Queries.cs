namespace Sandbox.SceneRenderer.Culling;

/// <summary>
/// How much of a box a query region covers.
/// </summary>
internal enum Containment
{
	Outside,
	Intersects,
	Inside,
}

/// <summary>
/// Implemented by structs so traversal specialises without per-node delegate calls.
/// </summary>
internal interface ISpatialQuery
{
	Containment Test( in Vector3 min, in Vector3 max );

	/// <summary>
	/// Rejects whole subtrees, including those already inside the query region.
	/// </summary>
	bool Reject( in Vector3 min, in Vector3 max );
}

internal sealed partial class SpatialTree
{
	[ThreadStatic] static int[] s_queryStack;

	/// <summary>
	/// Appends candidates. Contained subtrees skip <see cref="ISpatialQuery.Test"/>, but still run <see cref="ISpatialQuery.Reject"/>.
	/// </summary>
	public void Query<T>( ref T query, List<int> results ) where T : struct, ISpatialQuery => Query( ref query, results, false, out _ );

	/// <summary>
	/// Appends (item &lt;&lt; 1) | inside, where inside guarantees exact bounds are also contained.
	/// <paramref name="rejected"/> counts leaves dropped by <see cref="ISpatialQuery.Reject"/>.
	/// </summary>
	public void QueryClassified<T>( ref T query, List<int> results, out int rejected ) where T : struct, ISpatialQuery => Query( ref query, results, true, out rejected );

	void Query<T>( ref T query, List<int> results, bool classified, out int rejected ) where T : struct, ISpatialQuery
	{
		rejected = 0;
		if ( root == Null ) return;

		// Stack capacity: one pending sibling per level plus the current node.
		var capacity = Height + 1;
		var stack = s_queryStack;
		if ( stack is null || stack.Length < capacity )
			s_queryStack = stack = new int[Math.Max( 64, capacity )];

		var count = 1;
		stack[0] = root << 1;

		while ( count > 0 )
		{
			var entry = stack[--count];
			var index = entry >> 1;
			ref var node = ref nodes[index];

			var inside = (entry & 1) != 0;
			if ( !inside )
			{
				var containment = query.Test( node.Min, node.Max );
				if ( containment == Containment.Outside ) continue;
				inside = containment == Containment.Inside;
			}

			if ( query.Reject( node.Min, node.Max ) )
			{
				rejected += node.LeafCount;
				continue;
			}

			if ( node.IsLeaf )
			{
				results.Add( classified ? node.Item << 1 | (inside ? 1 : 0) : node.Item );
				continue;
			}

			var flag = inside ? 1 : 0;
			stack[count++] = node.Child1 << 1 | flag;
			stack[count++] = node.Child2 << 1 | flag;
		}
	}

	/// <summary>
	/// Frustum query with conservative subtree size culling.
	/// </summary>
	internal struct FrustumQuery : ISpatialQuery
	{
		public ViewFrustum Frustum;
		public SizeCull Size;

		public readonly Containment Test( in Vector3 min, in Vector3 max )
		{
			var center = (min + max) * 0.5f;
			var extents = (max - min) * 0.5f;
			return Frustum.Classify( center, extents );
		}

		public readonly bool Reject( in Vector3 min, in Vector3 max ) => Size.CullsAll( min, max );
	}

	/// <summary>
	/// Sphere query with conservative subtree size culling.
	/// </summary>
	internal struct SphereQuery : ISpatialQuery
	{
		public Vector3 Center;
		public float Radius;
		public SizeCull Size;

		public readonly bool Reject( in Vector3 min, in Vector3 max ) => Size.CullsAll( min, max );

		public readonly Containment Test( in Vector3 min, in Vector3 max )
		{
			var nearest = Center.Clamp( min, max );
			if ( nearest.DistanceSquared( Center ) > Radius * Radius ) return Containment.Outside;

			var farthest = Vector3.Max( (Center - min).Abs(), (max - Center).Abs() );
			return farthest.LengthSquared <= Radius * Radius ? Containment.Inside : Containment.Intersects;
		}
	}
}
