using System.Runtime.InteropServices;

namespace Sandbox.Rendering;

/// <summary>
/// A bounding volume hierarchy over triangles, traced in any shader by including <c>common/classes/Bvh.hlsl</c>
/// and calling <see cref="Bind"/>. It needs no hardware ray tracing.
/// </summary>
public sealed class Bvh : IDisposable
{
	/// <summary>
	/// Nodes are stored depth first: a node's first child follows it, and <see cref="Skip"/> is the node after its subtree.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	struct Node
	{
		public Vector3 Mins;

		/// <summary>
		/// The node to visit when this one is missed or finished, the node count at the end.
		/// </summary>
		public int Skip;

		public Vector3 Maxs;

		/// <summary>
		/// Triangle count in the low three bits and the first triangle above them, -1 for an inner node.
		/// </summary>
		public int Leaf;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct Triangle
	{
		public Vector3 A;

		/// <summary>
		/// Its place in the list it was built from, which a hit returns.
		/// </summary>
		public uint Id;

		public Vector3 AB;
		public uint Unused0;
		public Vector3 AC;
		public uint Unused1;
	}

	const int LeafSize = 4;

	readonly List<Node> nodes;
	readonly Triangle[] triangles;

	GpuBuffer<Node> nodeBuffer;
	GpuBuffer<Triangle> triangleBuffer;

	/// <summary>
	/// Build from a triangle list, three vertices each.
	/// </summary>
	public Bvh( ReadOnlySpan<Vector3> vertices )
	{
		if ( vertices.Length % 3 != 0 )
			throw new ArgumentException( "Expected three vertices per triangle", nameof( vertices ) );

		var source = new Triangle[vertices.Length / 3];
		var boxes = new (Vector3 Mins, Vector3 Maxs)[source.Length];
		var centroids = new Vector3[source.Length];

		for ( var i = 0; i < source.Length; i++ )
		{
			Vector3 a = vertices[i * 3], b = vertices[i * 3 + 1], c = vertices[i * 3 + 2];
			source[i] = new Triangle { A = a, AB = b - a, AC = c - a, Id = (uint)i };
			boxes[i] = (Vector3.Min( a, Vector3.Min( b, c ) ), Vector3.Max( a, Vector3.Max( b, c ) ));
			centroids[i] = (boxes[i].Mins + boxes[i].Maxs) * 0.5f;
		}

		var order = Enumerable.Range( 0, source.Length ).ToArray();
		nodes = Build( boxes, centroids, order, 0, source.Length );

		// Built as how many nodes each subtree is
		var built = CollectionsMarshal.AsSpan( nodes );
		for ( var i = 0; i < built.Length; i++ )
			built[i].Skip += i;

		triangles = order.Select( i => source[i] ).ToArray();
	}

	// A subtree's nodes, skips counted from its first
	static List<Node> Build( (Vector3 Mins, Vector3 Maxs)[] source, Vector3[] centroids, int[] order, int first, int count )
	{
		var mins = new Vector3( float.MaxValue );
		var maxs = new Vector3( float.MinValue );
		var centroidMins = mins;
		var centroidMaxs = maxs;

		for ( var i = first; i < first + count; i++ )
		{
			var (lo, hi) = source[order[i]];
			mins = Vector3.Min( mins, lo );
			maxs = Vector3.Max( maxs, hi );
			centroidMins = Vector3.Min( centroidMins, centroids[order[i]] );
			centroidMaxs = Vector3.Max( centroidMaxs, centroids[order[i]] );
		}

		if ( count <= LeafSize )
			return [new Node { Mins = mins, Maxs = maxs, Leaf = first << 3 | count, Skip = 1 }];

		var (axis, plane) = Split( source, centroids, order, first, count, centroidMins, centroidMaxs - centroidMins );
		var half = count / 2;

		if ( plane > 0 )
		{
			// Triangles in bins before the plane go first
			var scale = Bins / (centroidMaxs[axis] - centroidMins[axis]);
			half = 0;
			for ( var i = first; i < first + count; i++ )
			{
				if ( Bin( centroids[order[i]][axis], centroidMins[axis], scale ) >= plane ) continue;
				(order[i], order[first + half]) = (order[first + half], order[i]);
				half++;
			}
		}

		// The two sides build at once, until they're too small to be worth a thread
		List<Node> left = null, right = null;
		if ( count > 4096 )
			Parallel.Invoke( () => left = Build( source, centroids, order, first, half ), () => right = Build( source, centroids, order, first + half, count - half ) );
		else
			(left, right) = (Build( source, centroids, order, first, half ), Build( source, centroids, order, first + half, count - half ));

		var nodes = new List<Node>( 1 + left.Count + right.Count ) { new Node { Mins = mins, Maxs = maxs, Leaf = -1, Skip = 1 + left.Count + right.Count } };
		nodes.AddRange( left );
		nodes.AddRange( right );
		return nodes;
	}

	const int Bins = 16;

	static int Bin( float centroid, float mins, float scale ) => Math.Min( Bins - 1, (int)((centroid - mins) * scale) );

	static float Area( Vector3 mins, Vector3 maxs )
	{
		var size = Vector3.Max( maxs - mins, Vector3.Zero );
		return size.x * size.y + size.y * size.z + size.z * size.x;
	}

	/// <summary>
	/// The surface area heuristic over binned centroids: the axis and bin to split before where the two sides' areas times
	/// their triangles add up least. Plane 0 if every centroid is in one place.
	/// </summary>
	static (int Axis, int Plane) Split( (Vector3 Mins, Vector3 Maxs)[] source, Vector3[] centroids, int[] order, int first, int count, Vector3 mins, Vector3 extent )
	{
		var best = (Axis: 0, Plane: 0, Cost: float.MaxValue);
		Span<int> counts = stackalloc int[Bins];
		Span<Vector3> binMins = stackalloc Vector3[Bins];
		Span<Vector3> binMaxs = stackalloc Vector3[Bins];
		Span<float> rightCost = stackalloc float[Bins];

		for ( var axis = 0; axis < 3; axis++ )
		{
			if ( extent[axis] <= 0 ) continue;

			counts.Clear();
			binMins.Fill( new Vector3( float.MaxValue ) );
			binMaxs.Fill( new Vector3( float.MinValue ) );

			for ( var i = first; i < first + count; i++ )
			{
				var b = Bin( centroids[order[i]][axis], mins[axis], Bins / extent[axis] );
				var (lo, hi) = source[order[i]];
				counts[b]++;
				binMins[b] = Vector3.Min( binMins[b], lo );
				binMaxs[b] = Vector3.Max( binMaxs[b], hi );
			}

			var (rMins, rMaxs, rCount) = (new Vector3( float.MaxValue ), new Vector3( float.MinValue ), 0);
			for ( var p = Bins - 1; p > 0; p-- )
			{
				(rMins, rMaxs, rCount) = (Vector3.Min( rMins, binMins[p] ), Vector3.Max( rMaxs, binMaxs[p] ), rCount + counts[p]);
				rightCost[p] = Area( rMins, rMaxs ) * rCount;
			}

			var (lMins, lMaxs, lCount) = (new Vector3( float.MaxValue ), new Vector3( float.MinValue ), 0);
			for ( var p = 1; p < Bins; p++ )
			{
				(lMins, lMaxs, lCount) = (Vector3.Min( lMins, binMins[p - 1] ), Vector3.Max( lMaxs, binMaxs[p - 1] ), lCount + counts[p - 1]);
				var cost = Area( lMins, lMaxs ) * lCount + rightCost[p];
				if ( lCount > 0 && lCount < count && cost < best.Cost ) best = (axis, p, cost);
			}
		}

		return (best.Axis, best.Plane);
	}

	/// <summary>
	/// Set the buffers <c>common/classes/Bvh.hlsl</c> reads, uploading them on first use.
	/// </summary>
	public void Bind( RenderAttributes attributes )
	{
		if ( nodeBuffer is null )
		{
			nodeBuffer = new GpuBuffer<Node>( nodes.Count, debugName: "BvhNodes" );
			nodeBuffer.SetData( CollectionsMarshal.AsSpan( nodes ) );

			// A buffer can't be empty
			triangleBuffer = new GpuBuffer<Triangle>( Math.Max( 1, triangles.Length ), debugName: "BvhTriangles" );
			if ( triangles.Length > 0 )
				triangleBuffer.SetData( triangles.AsSpan() );
		}

		attributes.Set( "BvhNodes", nodeBuffer );
		attributes.Set( "BvhTriangles", triangleBuffer );
	}

	public void Dispose()
	{
		nodeBuffer?.Dispose();
		triangleBuffer?.Dispose();
		nodeBuffer = null;
		triangleBuffer = null;
	}
}
