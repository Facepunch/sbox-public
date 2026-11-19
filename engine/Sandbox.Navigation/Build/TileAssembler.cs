using System.Numerics;

namespace Nav;

/// <summary>
/// The last step of a tile build: writes the polygons in tree order, builds the tree over them and lists
/// the edges on the tile border. Holds scratch, so keep one per builder.
/// </summary>
internal sealed class TileAssembler
{
	Vector3[] mins = [], maxs = [];
	float[] keys = [];
	int[] order = [], remap = [];
	BvNode[] nodes = [];

	/// <summary>
	/// Per polygon six vertex indices, clockwise seen from above with the unused ones Poly.Wall, six
	/// neighbours as Poly.Neis holds them, and an area. Keeps verts as the tile's vertices. Returns null
	/// and the reason when a limit is exceeded.
	/// </summary>
	public Tile? Create( int x, int y, Vector3[] verts, ReadOnlySpan<ushort> polyVerts, ReadOnlySpan<ushort> neighbours, ReadOnlySpan<byte> areas, in NavSettings settings, out string? error )
	{
		int count = areas.Length;
		error = count == 0 ? "the tile has no polygons"
			: count > Poly.MaxPerTile ? $"the tile has {count} polygons, the limit is {Poly.MaxPerTile}"
			: verts.Length > Poly.MaxVertsPerTile ? $"the tile has {verts.Length} vertices, the limit is {Poly.MaxVertsPerTile}"
			: null;
		if ( error != null ) return null;

		if ( mins.Length < count )
		{
			int capacity = Math.Max( count, mins.Length * 2 );
			mins = new Vector3[capacity];
			maxs = new Vector3[capacity];
			keys = new float[capacity];
			order = new int[capacity];
			remap = new int[capacity];
			nodes = new BvNode[capacity];
		}

		float minZ = float.MaxValue, maxZ = float.MinValue;
		for ( int i = 0; i < count; i++ )
		{
			var indices = polyVerts.Slice( i * Poly.MaxVerts, Poly.MaxVerts );
			Vector3 min = verts[indices[0]], max = min;
			for ( int j = 1; j < Poly.MaxVerts && indices[j] != Poly.Wall; j++ )
			{
				min = Vector3.Min( min, verts[indices[j]] );
				max = Vector3.Max( max, verts[indices[j]] );
			}
			mins[i] = min;
			maxs[i] = max;
			order[i] = i;
			minZ = MathF.Min( minZ, min.Z );
			maxZ = MathF.Max( maxZ, max.Z );
		}

		Tile.TreeFrame( x, y, minZ, maxZ, settings, out var origin, out var cell );
		int nodeCount = 0;
		BuildNode( 0, count, ref nodeCount, origin, Vector3.One / cell );

		// The polygons in the order of the tree's leaves, neighbours renumbered to match
		for ( int i = 0; i < count; i++ ) remap[order[i]] = i;
		var polys = new Poly[count];
		int portalCount = 0;
		for ( int i = 0; i < count; i++ )
		{
			int source = order[i] * Poly.MaxVerts;
			ref var poly = ref polys[i];
			int vertCount = 0;
			for ( ; vertCount < Poly.MaxVerts && polyVerts[source + vertCount] != Poly.Wall; vertCount++ )
			{
				ushort neighbour = neighbours[source + vertCount];
				if ( neighbour < Poly.Border ) neighbour = (ushort)remap[neighbour];
				else if ( neighbour != Poly.Wall ) portalCount++;
				poly.Verts[vertCount] = polyVerts[source + vertCount];
				poly.Neis[vertCount] = neighbour;
			}
			poly.VertCount = (byte)vertCount;
			poly.Area = areas[order[i]];
		}

		var portals = portalCount == 0 ? [] : new PortalEdge[portalCount];
		portalCount = 0;
		for ( int i = 0; i < count && portalCount < portals.Length; i++ )
		{
			ref readonly var poly = ref polys[i];
			for ( int j = 0; j < poly.VertCount; j++ )
			{
				if ( poly.Neis[j] < Poly.Border || poly.Neis[j] == Poly.Wall ) continue;
				int side = poly.Neis[j] - Poly.Border;
				var a = verts[poly.Verts[j]];
				var b = verts[poly.Verts[j + 1 < poly.VertCount ? j + 1 : 0]];
				bool alongY = (side & 1) == 0;
				float alongA = ((alongY ? a.Y : a.X) - (alongY ? origin.Y : origin.X)) / cell.X;
				float alongB = ((alongY ? b.Y : b.X) - (alongY ? origin.Y : origin.X)) / cell.X;
				portals[portalCount++] = new PortalEdge
				{
					Poly = (ushort)i,
					Edge = (byte)j,
					Side = (byte)side,
					Lo = (ushort)Math.Clamp( MathF.Round( MathF.Min( alongA, alongB ) ), 0, 65535 ),
					Hi = (ushort)Math.Clamp( MathF.Round( MathF.Max( alongA, alongB ) ), 0, 65535 )
				};
			}
		}

		return new Tile( x, y, polys, verts, nodes.AsSpan( 0, nodeCount ).ToArray(), portals, settings );
	}

	void BuildNode( int lo, int hi, ref int next, Vector3 origin, Vector3 perCell )
	{
		int index = next++;
		Vector3 min = mins[order[lo]], max = maxs[order[lo]];
		for ( int i = lo + 1; i < hi; i++ )
		{
			min = Vector3.Min( min, mins[order[i]] );
			max = Vector3.Max( max, maxs[order[i]] );
		}

		var low = Vector3.Clamp( (min - origin) * perCell, Vector3.Zero, new Vector3( 65535 ) );
		var high = Vector3.Clamp( (max - origin) * perCell, Vector3.Zero, new Vector3( 65535 ) );
		ref var node = ref nodes[index];
		node.MinX = (ushort)MathF.Floor( low.X );
		node.MinY = (ushort)MathF.Floor( low.Y );
		node.MinZ = (ushort)MathF.Floor( low.Z );
		node.MaxX = (ushort)MathF.Ceiling( high.X );
		node.MaxY = (ushort)MathF.Ceiling( high.Y );
		node.MaxZ = (ushort)MathF.Ceiling( high.Z );

		if ( hi - lo <= BvNode.LeafSize )
		{
			node.First = (ushort)lo;
			node.Count = (ushort)(hi - lo);
			return;
		}

		var size = max - min;
		int axis = size.Y > size.X ? 1 : 0;
		if ( size.Z > size[axis] ) axis = 2;
		for ( int i = lo; i < hi; i++ ) keys[i] = mins[order[i]][axis];
		Array.Sort( keys, order, lo, hi - lo );

		// The left half takes a multiple of the leaf size, so every leaf but one is full.
		int middle = lo + (((hi - lo + 1) / 2 + BvNode.LeafSize - 1) & ~(BvNode.LeafSize - 1));
		BuildNode( lo, middle, ref next, origin, perCell );
		BuildNode( middle, hi, ref next, origin, perCell );
		nodes[index].First = BvNode.Inner;
		nodes[index].Count = (ushort)(next - index);
	}
}
