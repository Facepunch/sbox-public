using System.Numerics;
using System.Runtime.CompilerServices;

namespace Nav;

public sealed partial class NavQuery
{
	float[] wallDistances = new float[16];

	// Breadth first over the polygons near the move. Nodes are made in visiting order, so the node
	// array is the queue.
	internal bool MoveOnSurface( MeshState s, ref PolyRef poly, ref Vector3 position, Vector3 delta, in NavFilter filter, Span<PolyRef> visited, out int visitedCount )
	{
		visitedCount = 0;
		if ( !s.TryGet( poly, out _, out _ ) || !Geo.IsFinite( position ) || !Geo.IsFinite( delta ) ) return false;

		Vector3 start = position, end = position + delta;
		BeginNodes();
		GetNode( poly.Id, out _ );

		Vector3 bestPoint = start;
		float bestDistance = float.MaxValue;
		int bestNode = -1;

		// Only edges near the straight move are crossed.
		var searchCentre = Vector3.Lerp( start, end, 0.5f );
		float searchRadius = Vector3.Distance( start, end ) / 2f + epsilon * 0.01f;
		searchRadius *= searchRadius;

		Span<Vector3> v = stackalloc Vector3[Poly.MaxVerts];
		for ( int head = 0; head < nodeCount; head++ )
		{
			uint id = nodes[head].Id;
			var tile = s.Get( id );
			int p = (int)(id & PolyRef.PolyMask);
			ref readonly var current = ref tile.Polys[p];
			int n = Gather( tile, current, v );

			if ( Geo.PointInPolygon( end, v[..n] ) )
			{
				bestNode = head;
				bestPoint = end;
				break;
			}

			for ( int i = 0, j = n - 1; i < n; j = i++ )
			{
				ushort nei = current.Neis[j];
				bool open = false;
				bool near = Geo.DistanceToSegmentSquared( searchCentre, v[j], v[i], out _ ) <= searchRadius;
				if ( nei < Poly.Border )
				{
					if ( filter.Allows( tile.Polys[nei].Area ) )
					{
						open = true;
						if ( near ) Enqueue( id & ~PolyRef.PolyMask | nei, head );
					}
				}
				else if ( nei != Poly.Wall )
				{
					var ext = tile.Ext;
					for ( int e = FirstExt( ext, p, j ); e < ext.Length && ext[e].Poly == p && ext[e].Edge == j; e++ )
					{
						uint target = ext[e].Target;
						if ( !filter.Allows( ext[e].Area ) ) continue;
						open = true;
						if ( near ) Enqueue( target, head );
					}
				}
				if ( open ) continue;

				float distance = Geo.DistanceToSegmentSquared( end, v[j], v[i], out float t );
				if ( distance < bestDistance )
				{
					bestPoint = Vector3.Lerp( v[j], v[i], t );
					bestDistance = distance;
					bestNode = head;
				}
			}
		}

		if ( bestNode < 0 ) return true;

		int length = 0;
		for ( int i = bestNode; i >= 0; i = nodes[i].Parent ) length++;
		int at = length;
		for ( int i = bestNode; i >= 0; i = nodes[i].Parent )
		{
			if ( --at < visited.Length ) visited[at] = s.RefOf( nodes[i].Id );
		}
		visitedCount = Math.Min( length, visited.Length );

		uint last = nodes[bestNode].Id;
		poly = s.RefOf( last );
		int count = Gather( s.Get( last ), in s.Get( last ).Polys[last & PolyRef.PolyMask], v );
		if ( HeightOver( v[..count], bestPoint, out float height ) ) bestPoint.Z = height;
		position = bestPoint;
		return true;
	}

	void Enqueue( uint id, int parent )
	{
		int node = GetNode( id, out bool created );
		if ( created ) nodes[node].Parent = parent;
	}

	// Walks the polygons whose edges are within range and keeps the nearest wall segments, sorted.
	// On a tile border the open stretches are portals and the gaps between them are walls.
	internal int Walls( MeshState s, uint startId, Vector3 position, float range, float height, in NavFilter filter, Span<Vector3> segments, bool nearestOnly )
	{
		int capacity = segments.Length / 2, count = 0;
		if ( capacity == 0 ) return 0;
		if ( wallDistances.Length < capacity ) wallDistances = new float[capacity];
		var distances = wallDistances.AsSpan( 0, capacity );

		BeginNodes();
		GetNode( startId, out _ );
		float rangeSquared = range * range;
		Span<Vector3> v = stackalloc Vector3[Poly.MaxVerts];

		for ( int head = 0; head < nodeCount; head++ )
		{
			uint id = nodes[head].Id;
			var tile = s.Get( id );
			int p = (int)(id & PolyRef.PolyMask);
			ref readonly var poly = ref tile.Polys[p];
			int n = Gather( tile, poly, v );

			for ( int edge = 0; edge < n; edge++ )
			{
				Vector3 a = v[edge], b = v[edge + 1 < n ? edge + 1 : 0];
				if ( Geo.DistanceToSegmentSquared( position, a, b, out _ ) > rangeSquared ) continue;

				ushort nei = poly.Neis[edge];
				if ( nei < Poly.Border )
				{
					if ( filter.Allows( tile.Polys[nei].Area ) ) Enqueue( id & ~PolyRef.PolyMask | nei, head );
					else AddWall( a, b, position, height, segments, distances, ref count, ref rangeSquared, nearestOnly );
					continue;
				}

				var ext = tile.Ext;
				int first = nei == Poly.Wall ? ext.Length : FirstExt( ext, p, edge ), last = first;
				while ( last < ext.Length && ext[last].Poly == p && ext[last].Edge == edge ) last++;

				for ( int e = first; e < last; e++ )
				{
					uint target = ext[e].Target;
					if ( !filter.Allows( ext[e].Area ) ) continue;
					Vector3 from = Vector3.Lerp( a, b, ext[e].TMin / 255f ), to = Vector3.Lerp( a, b, ext[e].TMax / 255f );
					if ( Geo.DistanceToSegmentSquared( position, from, to, out _ ) <= rangeSquared ) Enqueue( target, head );
				}

				// Sweep the union of the open stretches. What stays uncovered is wall.
				int cursor = 0;
				while ( cursor < 255 )
				{
					int covered = cursor, next = 255;
					for ( int e = first; e < last; e++ )
					{
						uint target = ext[e].Target;
						if ( !filter.Allows( ext[e].Area ) ) continue;
						if ( ext[e].TMin <= cursor ) covered = Math.Max( covered, ext[e].TMax );
						else next = Math.Min( next, ext[e].TMin );
					}
					if ( covered > cursor )
					{
						cursor = covered;
						continue;
					}
					AddWall( Vector3.Lerp( a, b, cursor / 255f ), Vector3.Lerp( a, b, next / 255f ), position, height, segments, distances, ref count, ref rangeSquared, nearestOnly );
					cursor = next;
				}
			}
		}
		return count;
	}

	static void AddWall( Vector3 a, Vector3 b, Vector3 position, float height, Span<Vector3> segments, Span<float> distances, ref int count, ref float rangeSquared, bool nearestOnly )
	{
		float distance = Geo.DistanceToSegmentSquared( position, a, b, out float t );
		if ( distance > rangeSquared || MathF.Abs( Vector3.Lerp( a, b, t ).Z - position.Z ) > height ) return;
		int slot = count;
		while ( slot > 0 && distance < distances[slot - 1] ) slot--;
		if ( slot >= distances.Length ) return;
		count = Math.Min( count + 1, distances.Length );
		for ( int i = count - 1; i > slot; i-- )
		{
			distances[i] = distances[i - 1];
			segments[i * 2] = segments[i * 2 - 2];
			segments[i * 2 + 1] = segments[i * 2 - 1];
		}
		distances[slot] = distance;
		segments[slot * 2] = a;
		segments[slot * 2 + 1] = b;
		// Looking for the nearest wall only: nothing farther than the current one matters any more.
		if ( nearestOnly && count == distances.Length ) rangeSquared = distances[count - 1];
	}

	/// <summary>
	/// Walks the surface in a straight line from a point on start towards to. t is how far it got, 1
	/// when nothing was in the way; last is the polygon it ended in. False when start is stale.
	/// </summary>
	internal bool Raycast( MeshState s, PolyRef start, Vector3 from, Vector3 to, in NavFilter filter, out float t, out PolyRef last )
	{
		t = 0;
		last = start;
		if ( !s.TryGet( start, out _, out _ ) || !Geo.IsFinite( from ) || !Geo.IsFinite( to ) ) return false;

		uint id = start.Id;
		Span<Vector3> v = stackalloc Vector3[Poly.MaxVerts];
		for ( int steps = 0; steps < 65536; steps++ )
		{
			var tile = s.Get( id );
			int p = (int)(id & PolyRef.PolyMask);
			ref readonly var poly = ref tile.Polys[p];
			int n = Gather( tile, poly, v );
			last = new PolyRef( tile.Serial, id );

			if ( !Geo.IntersectSegmentPolygon( from, to, v[..n], 1e-6f * areaScale, out _, out float exitT, out int exit ) ) return true;
			if ( exit < 0 )
			{
				t = 1;
				return true;
			}
			t = MathF.Max( t, exitT );

			uint next = PolyRef.NoId;
			ushort nei = poly.Neis[exit];
			if ( nei < Poly.Border )
			{
				if ( filter.Allows( tile.Polys[nei].Area ) ) next = id & ~PolyRef.PolyMask | nei;
			}
			else if ( nei != Poly.Wall )
			{
				// Where along the border edge the ray leaves decides which neighbour it enters.
				Geo.DistanceToSegmentSquared( from + (to - from) * exitT, v[exit], v[exit + 1 < n ? exit + 1 : 0], out float along );
				var ext = tile.Ext;
				for ( int e = FirstExt( ext, p, exit ); e < ext.Length && ext[e].Poly == p && ext[e].Edge == exit; e++ )
				{
					uint target = ext[e].Target;
					if ( along * 255f < ext[e].TMin || along * 255f > ext[e].TMax || !filter.Allows( ext[e].Area ) ) continue;
					next = target;
					break;
				}
			}
			if ( next == PolyRef.NoId ) return true;
			id = next;
		}
		return true;
	}

	static float NextRandom( ref uint seed )
	{
		if ( seed == 0 ) seed = 0x9E3779B9;
		seed ^= seed << 13;
		seed ^= seed >> 17;
		seed ^= seed << 5;
		return (seed >> 8) * (1f / 16777216f);
	}

	/// <summary>
	/// A point chosen uniformly by area: over the whole mesh for an infinite radius, else over the part
	/// of the mesh under the disc and within radius of the centre's height. All randomness comes from seed.
	/// Under a disc that is mostly off the mesh, 16 samples can all miss: the answer is then the point
	/// of the mesh nearest to the centre inside the disc, which is not a uniform choice.
	/// </summary>
	public bool FindRandomPoint( Vector3 center, float radius, in NavFilter filter, ref uint seed, out PolyRef poly, out Vector3 point )
	{
		poly = default;
		point = default;
		var s = mesh.State;
		Span<Vector3> v = stackalloc Vector3[Poly.MaxVerts];
		Tile? chosen = null;
		int chosenPoly = -1;

		if ( float.IsPositiveInfinity( radius ) )
		{
			// Reservoir sampling, first over tiles, then over the polygons of the tile picked.
			float sum = 0;
			foreach ( var page in s.Pages )
			{
				if ( page == null ) continue;
				foreach ( var tile in page )
				{
					if ( tile == null ) continue;
					float area = filter.Forbidden == 0 ? tile.Area : AllowedArea( tile, filter );
					if ( area <= 0 ) continue;
					sum += area;
					if ( NextRandom( ref seed ) * sum <= area ) chosen = tile;
				}
			}
			if ( chosen == null ) return false;

			sum = 0;
			for ( int p = 0; p < chosen.Polys.Length; p++ )
			{
				ref readonly var candidate = ref chosen.Polys[p];
				if ( !filter.Allows( candidate.Area ) ) continue;
				float area = PolyArea( chosen, candidate );
				sum += area;
				if ( NextRandom( ref seed ) * sum <= area ) chosenPoly = p;
			}
			if ( chosenPoly < 0 ) return false;

			int n = Gather( chosen, in chosen.Polys[chosenPoly], v );
			point = RandomInPolygon( v[..n], NextRandom( ref seed ), NextRandom( ref seed ) );
			if ( HeightOver( v[..n], point, out float height ) ) point.Z = height;
			poly = new PolyRef( chosen.Serial, (uint)chosen.Slot << PolyRef.PolyBits | (uint)chosenPoly );
			return true;
		}

		if ( !(radius >= 0) || !Geo.IsFinite( center ) ) return false;

		// A polygon by area among those that reach the disc, then a point in it: thrown away when it is
		// outside the disc, which leaves the points under the disc equally likely.
		Vector3 reach = new( radius ), bmin = center - reach, bmax = center + reach;
		float total = PickInBox( s, bmin, bmax, filter, float.PositiveInfinity, out _, out _ );
		for ( int attempt = 0; attempt < 16 && total > 0; attempt++ )
		{
			PickInBox( s, bmin, bmax, filter, NextRandom( ref seed ) * total, out chosen, out chosenPoly );
			if ( chosen == null ) break;
			int n = Gather( chosen, in chosen.Polys[chosenPoly], v );
			point = RandomInPolygon( v[..n], NextRandom( ref seed ), NextRandom( ref seed ) );
			if ( HeightOver( v[..n], point, out float height ) ) point.Z = height;
			if ( !InDisc( point, center, radius ) ) continue;
			poly = new PolyRef( chosen.Serial, (uint)chosen.Slot << PolyRef.PolyBits | (uint)chosenPoly );
			return true;
		}

		// Little of what reaches the disc is under it: the point of the mesh nearest to the centre, when
		// that is inside. So the call fails only when nothing is.
		if ( !Nearest( s, center, reach, filter, null, 0, out uint id, out point ) || !InDisc( point, center, radius ) ) return false;
		poly = s.RefOf( id );
		return true;
	}

	static bool InDisc( Vector3 point, Vector3 center, float radius )
	{
		float dx = point.X - center.X, dy = point.Y - center.Y;
		return dx * dx + dy * dy <= radius * radius && MathF.Abs( point.Z - center.Z ) <= radius;
	}

	// Adds up the area of the allowed polygons whose bounds touch the box, and names the polygon at
	// which the sum passes target. Returns the sum it got to.
	float PickInBox( MeshState s, Vector3 bmin, Vector3 bmax, in NavFilter filter, float target, out Tile? picked, out int pickedPoly )
	{
		picked = null;
		pickedPoly = -1;
		var index = s.Index;
		if ( index.MaxX < index.MinX ) return 0;
		int minX = TileCoord( bmin.X, index.MinX, index.MaxX ), maxX = TileCoord( bmax.X, index.MinX, index.MaxX );
		int minY = TileCoord( bmin.Y, index.MinY, index.MaxY ), maxY = TileCoord( bmax.Y, index.MinY, index.MaxY );
		Span<Vector3> v = stackalloc Vector3[Poly.MaxVerts];
		float sum = 0;
		for ( int y = minY; y <= maxY; y++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				var tile = s.Find( x, y );
				if ( tile == null ) continue;
				var tree = tile.Tree;
				for ( int i = 0; i < tree.Length; )
				{
					ref readonly var node = ref tree[i];
					if ( !NodeBound( node, tile.TreeOrigin, tile.TreeCell, bmin, bmin, bmax, out _ ) )
					{
						i += node.First == BvNode.Inner ? node.Count : 1;
						continue;
					}
					i++;
					if ( node.First == BvNode.Inner ) continue;
					for ( int p = node.First; p < node.First + node.Count; p++ )
					{
						ref readonly var poly = ref tile.Polys[p];
						if ( !filter.Allows( poly.Area ) ) continue;
						int n = Gather( tile, poly, v );
						Vector3 min = v[0], max = v[0];
						for ( int k = 1; k < n; k++ )
						{
							min = Vector3.Min( min, v[k] );
							max = Vector3.Max( max, v[k] );
						}
						if ( bmin.X > max.X || bmax.X < min.X || bmin.Y > max.Y || bmax.Y < min.Y || bmin.Z > max.Z || bmax.Z < min.Z ) continue;
						sum += PolyArea( tile, poly );
						if ( sum <= target ) continue;
						picked = tile;
						pickedPoly = p;
						return sum;
					}
				}
			}
		}
		return sum;
	}

	static float PolyArea( Tile tile, in Poly poly )
	{
		float area = 0;
		var first = tile.Verts[poly.Verts[0]];
		for ( int j = 2; j < poly.VertCount; j++ ) area += Geo.TriArea2D( first, tile.Verts[poly.Verts[j - 1]], tile.Verts[poly.Verts[j]] );
		return MathF.Abs( area ) * 0.5f;
	}

	static float AllowedArea( Tile tile, in NavFilter filter )
	{
		float area = 0;
		foreach ( ref readonly var poly in tile.Polys.AsSpan() )
		{
			if ( filter.Allows( poly.Area ) ) area += PolyArea( tile, poly );
		}
		return area;
	}

	// Picks a fan triangle by area with s, then a point in it with s and t.
	Vector3 RandomInPolygon( ReadOnlySpan<Vector3> v, float s, float t )
	{
		Span<float> areas = stackalloc float[Poly.MaxVerts];
		float sum = 0;
		for ( int i = 2; i < v.Length; i++ )
		{
			areas[i] = MathF.Max( 0.001f * areaScale, MathF.Abs( Geo.TriArea2D( v[0], v[i - 1], v[i] ) ) );
			sum += areas[i];
		}

		float threshold = s * sum, passed = 0, u = 1;
		int triangle = v.Length - 1;
		for ( int i = 2; i < v.Length; i++ )
		{
			if ( threshold < passed + areas[i] )
			{
				u = (threshold - passed) / areas[i];
				triangle = i;
				break;
			}
			passed += areas[i];
		}

		float w = MathF.Sqrt( t );
		return v[0] * (1 - w) + v[triangle - 1] * ((1 - u) * w) + v[triangle] * (u * w);
	}
}
