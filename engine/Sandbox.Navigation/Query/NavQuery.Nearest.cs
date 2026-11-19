using System.Numerics;
using System.Runtime.CompilerServices;

namespace Nav;

public sealed partial class NavQuery
{
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	static int Gather( Tile tile, in Poly poly, Span<Vector3> v )
	{
		var verts = tile.Verts;
		int n = poly.VertCount;
		for ( int i = 0; i < n; i++ ) v[i] = verts[poly.Verts[i]];
		return n;
	}

	// The plane height of a polygon under the point. False when the point is not over it.
	bool HeightOver( ReadOnlySpan<Vector3> v, Vector3 point, out float height )
	{
		height = 0;
		if ( !Geo.PointInPolygon( point, v ) ) return false;
		for ( int j = 1; j < v.Length - 1; j++ )
		{
			if ( Geo.HeightOnTriangle( point, v[0], v[j], v[j + 1], 1e-6f * areaScale, out height ) ) return true;
		}
		// On an edge of a degenerate fan triangle: take the height from the nearest edge.
		height = ClosestOnEdges( v, point ).Z;
		return true;
	}

	static Vector3 ClosestOnEdges( ReadOnlySpan<Vector3> v, Vector3 point )
	{
		float best = float.MaxValue, bestT = 0;
		int bestEdge = 0;
		for ( int j = 0; j < v.Length; j++ )
		{
			float d = Geo.DistanceToSegmentSquared( point, v[j], v[j + 1 < v.Length ? j + 1 : 0], out float t );
			if ( d < best )
			{
				best = d;
				bestT = t;
				bestEdge = j;
			}
		}
		return Vector3.Lerp( v[bestEdge], v[bestEdge + 1 < v.Length ? bestEdge + 1 : 0], bestT );
	}

	// On the polygon's plane when the point is over it, else on its nearest edge.
	Vector3 ClosestOnPoly( Tile tile, int poly, Vector3 point, out bool over )
	{
		Span<Vector3> v = stackalloc Vector3[Poly.MaxVerts];
		int n = Gather( tile, in tile.Polys[poly], v );
		over = HeightOver( v[..n], point, out float height );
		return over ? new Vector3( point.X, point.Y, height ) : ClosestOnEdges( v[..n], point );
	}

	// The point itself when it is inside the polygon's outline, else the nearest point on the outline.
	bool ClosestOnBoundary( MeshState s, PolyRef poly, Vector3 point, out Vector3 closest )
	{
		closest = point;
		if ( !s.TryGet( poly, out var tile, out int index ) ) return false;
		Span<Vector3> v = stackalloc Vector3[Poly.MaxVerts];
		int n = Gather( tile, in tile.Polys[index], v );
		if ( !Geo.PointInPolygon( point, v[..n] ) ) closest = ClosestOnEdges( v[..n], point );
		return true;
	}

	int TileCoord( float v, int low, int high ) => (int)Math.Clamp( MathF.Floor( v / tileWorld ), low, high );

	/// <summary>
	/// Nearest polygon among those whose bounds touch the box around point, restricted to one island
	/// when islands is given. Tiles are visited in rings around the point and pruned by distance.
	/// </summary>
	internal bool Nearest( MeshState s, Vector3 point, Vector3 half, in NavFilter filter, IslandMap? islands, int island, out uint id, out Vector3 nearest )
	{
		id = PolyRef.NoId;
		nearest = point;
		var index = s.Index;
		if ( index.MaxX < index.MinX || !Geo.IsFinite( point ) || float.IsNaN( half.X + half.Y + half.Z ) ) return false;

		Vector3 bmin = point - half, bmax = point + half;
		int minX = TileCoord( bmin.X, index.MinX, index.MaxX ), maxX = TileCoord( bmax.X, index.MinX, index.MaxX );
		int minY = TileCoord( bmin.Y, index.MinY, index.MaxY ), maxY = TileCoord( bmax.Y, index.MinY, index.MaxY );
		int cx = TileCoord( point.X, minX, maxX ), cy = TileCoord( point.Y, minY, maxY );
		int rings = Math.Max( Math.Max( cx - minX, maxX - cx ), Math.Max( cy - minY, maxY - cy ) );

		float best = float.MaxValue;

		// Far more tile places in the range than tiles: walk the tiles. Rings over a sparse mesh, or under
		// a filter that excludes everything, would cost by the square of the mesh's extent.
		if ( ((long)maxX - minX + 1) * ((long)maxY - minY + 1) > s.TileCount * 4L )
		{
			foreach ( var page in s.Pages )
			{
				if ( page == null ) continue;
				foreach ( var tile in page )
				{
					if ( tile != null && tile.X >= minX && tile.X <= maxX && tile.Y >= minY && tile.Y <= maxY )
						NearestInTile( tile, point, bmin, bmax, filter, islands, island, ref best, ref id, ref nearest );
				}
			}
			return id != PolyRef.NoId;
		}

		for ( int ring = 0; ring <= rings; ring++ )
		{
			if ( ring == 0 )
			{
				NearestInTile( s.Find( cx, cy ), point, bmin, bmax, filter, islands, island, ref best, ref id, ref nearest );
				continue;
			}

			// Everything from the second ring on is at least ring - 1 tiles away.
			float gap = (ring - 1) * tileWorld;
			if ( gap * gap >= best ) break;

			for ( int x = Math.Max( cx - ring, minX ); x <= Math.Min( cx + ring, maxX ); x++ )
			{
				if ( cy - ring >= minY ) NearestInTile( s.Find( x, cy - ring ), point, bmin, bmax, filter, islands, island, ref best, ref id, ref nearest );
				if ( cy + ring <= maxY ) NearestInTile( s.Find( x, cy + ring ), point, bmin, bmax, filter, islands, island, ref best, ref id, ref nearest );
			}
			for ( int y = Math.Max( cy - ring + 1, minY ); y <= Math.Min( cy + ring - 1, maxY ); y++ )
			{
				if ( cx - ring >= minX ) NearestInTile( s.Find( cx - ring, y ), point, bmin, bmax, filter, islands, island, ref best, ref id, ref nearest );
				if ( cx + ring <= maxX ) NearestInTile( s.Find( cx + ring, y ), point, bmin, bmax, filter, islands, island, ref best, ref id, ref nearest );
			}
		}
		return id != PolyRef.NoId;
	}

	// A lower bound of the nearest metric over everything inside a tree node, or false when the node
	// misses the query box.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	bool NodeBound( in BvNode node, Vector3 origin, Vector3 cell, Vector3 point, Vector3 bmin, Vector3 bmax, out float bound )
	{
		Vector3 min = origin + new Vector3( node.MinX, node.MinY, node.MinZ ) * cell;
		Vector3 max = origin + new Vector3( node.MaxX, node.MaxY, node.MaxZ ) * cell;
		var gap = Vector3.Max( Vector3.Max( min - point, point - max ), Vector3.Zero );
		float vertical = MathF.Max( gap.Z - climb, 0 );
		bound = gap.X * gap.X + gap.Y * gap.Y + vertical * vertical;
		return bmin.X <= max.X && bmax.X >= min.X && bmin.Y <= max.Y && bmax.Y >= min.Y && bmin.Z <= max.Z && bmax.Z >= min.Z;
	}

	void NearestInTile( Tile? tile, Vector3 point, Vector3 bmin, Vector3 bmax, in NavFilter filter, IslandMap? islands, int island, ref float best, ref uint id, ref Vector3 nearest )
	{
		if ( tile == null ) return;
		var tree = tile.Tree;
		Vector3 origin = tile.TreeOrigin, cell = tile.TreeCell;
		if ( !NodeBound( tree[0], origin, cell, point, bmin, bmax, out float rootBound ) || rootBound >= best ) return;

		var polys = tile.Polys;
		// Restricted to an island: skip the tile unless one of its components belongs to it.
		ushort[]? components = null;
		int[] labels = [];
		int firstLabel = 0;
		if ( islands != null )
		{
			var local = tile.Islands( islands.Forbidden );
			labels = islands.Labels;
			firstLabel = islands.First[tile.Slot];
			bool any = false;
			for ( int c = 0; c < local.Count; c++ ) any |= labels[firstLabel + c] == island;
			if ( !any ) return;
			components = local.Component;
		}
		Span<int> stackNode = stackalloc int[64];
		Span<float> stackBound = stackalloc float[64];
		Span<Vector3> v = stackalloc Vector3[Poly.MaxVerts];
		int depth = 1;
		stackNode[0] = 0;
		stackBound[0] = rootBound;

		while ( depth > 0 )
		{
			depth--;
			if ( stackBound[depth] >= best ) continue;
			int at = stackNode[depth];
			ref readonly var node = ref tree[at];

			if ( node.First == BvNode.Inner )
			{
				// Nearer child on top of the stack. The left child wins a tie.
				int left = at + 1;
				int right = left + (tree[left].First == BvNode.Inner ? tree[left].Count : 1);
				bool leftHit = NodeBound( tree[left], origin, cell, point, bmin, bmax, out float leftBound ) && leftBound < best;
				bool rightHit = NodeBound( tree[right], origin, cell, point, bmin, bmax, out float rightBound ) && rightBound < best;
				if ( leftHit && rightHit && rightBound < leftBound )
				{
					stackNode[depth] = left;
					stackBound[depth++] = leftBound;
					stackNode[depth] = right;
					stackBound[depth++] = rightBound;
					continue;
				}
				if ( rightHit )
				{
					stackNode[depth] = right;
					stackBound[depth++] = rightBound;
				}
				if ( leftHit )
				{
					stackNode[depth] = left;
					stackBound[depth++] = leftBound;
				}
				continue;
			}

			for ( int p = node.First, end = node.First + node.Count; p < end; p++ )
			{
				ref readonly var poly = ref polys[p];
				if ( !filter.Allows( poly.Area ) ) continue;
				if ( components != null && (components[p] == TileIslands.None || labels[firstLabel + components[p]] != island) ) continue;

				int n = Gather( tile, poly, v );
				Vector3 min = v[0], max = v[0];
				for ( int i = 1; i < n; i++ )
				{
					min = Vector3.Min( min, v[i] );
					max = Vector3.Max( max, v[i] );
				}
				if ( bmin.X > max.X || bmax.X < min.X || bmin.Y > max.Y || bmax.Y < min.Y || bmin.Z > max.Z || bmax.Z < min.Z ) continue;

				Vector3 closest;
				float distance;
				if ( HeightOver( v[..n], point, out float height ) )
				{
					closest = new Vector3( point.X, point.Y, height );
					distance = MathF.Abs( point.Z - height ) - climb;
					distance = distance > 0 ? distance * distance : 0;
				}
				else
				{
					closest = ClosestOnEdges( v[..n], point );
					distance = Vector3.DistanceSquared( point, closest );
				}

				if ( distance < best )
				{
					best = distance;
					nearest = closest;
					id = (uint)tile.Slot << PolyRef.PolyBits | (uint)p;
				}
			}
		}
	}
}
