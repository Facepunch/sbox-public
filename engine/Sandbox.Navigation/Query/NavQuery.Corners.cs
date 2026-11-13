using System.Numerics;
using System.Runtime.CompilerServices;

namespace Nav;

public sealed partial class NavQuery
{
	// The ends of a link in the direction that leads from polygon from to polygon to. Either may be
	// NoId when the corridor does not say.
	internal static bool LinkEnds( MeshState s, PolyRef link, uint from, uint to, out Vector3 entry, out Vector3 exit )
	{
		entry = exit = default;
		int id = link.Link;
		if ( (uint)id >= (uint)s.Links.Length ) return false;
		ref readonly var found = ref s.Links[id];
		if ( found.Serial != link.Serial || !found.Connected ) return false;

		if ( (from == PolyRef.NoId || from == found.StartPoly) && (to == PolyRef.NoId || to == found.EndPoly) )
		{
			entry = found.StartPoint;
			exit = found.EndPoint;
			return true;
		}
		if ( found.Def.Bidirectional && (from == PolyRef.NoId || from == found.EndPoly) && (to == PolyRef.NoId || to == found.StartPoly) )
		{
			entry = found.EndPoint;
			exit = found.StartPoint;
			return true;
		}
		return false;
	}

	// The opening between two neighbouring polygons: the shared edge, or its open part on a tile border.
	// False when either is stale or they do not touch.
	bool Portal( MeshState s, PolyRef from, PolyRef to, out Vector3 left, out Vector3 right )
	{
		left = right = default;
		if ( !s.TryGet( from, out var tile, out int p ) || !s.TryGet( to, out var toTile, out int toPoly ) ) return false;

		ref readonly var poly = ref tile.Polys[p];
		int n = poly.VertCount;
		if ( tile == toTile )
		{
			for ( int j = 0; j < n; j++ )
			{
				if ( poly.Neis[j] != toPoly ) continue;
				left = tile.Verts[poly.Verts[j]];
				right = tile.Verts[poly.Verts[j + 1 < n ? j + 1 : 0]];
				return true;
			}
			return false;
		}

		var ext = tile.Ext;
		for ( int e = FirstExt( ext, p, 0 ); e < ext.Length && ext[e].Poly == p; e++ )
		{
			ref readonly var link = ref ext[e];
			if ( link.Target != to.Id ) continue;
			Vector3 a = tile.Verts[poly.Verts[link.Edge]], b = tile.Verts[poly.Verts[link.Edge + 1 < n ? link.Edge + 1 : 0]];
			left = a;
			right = b;
			if ( link.TMin != 0 || link.TMax != 255 )
			{
				left = Vector3.Lerp( a, b, link.TMin * (1f / 255f) );
				right = Vector3.Lerp( a, b, link.TMax * (1f / 255f) );
			}
			return true;
		}
		return false;
	}

	// Adds a corner, or only updates what the last corner enters when the point repeats it. True when
	// the funnel has to stop: the buffer is full or the end was added.
	bool AddCorner( Vector3 point, PolyRef enters, Span<Vector3> corners, Span<PolyRef> entered, ref int count )
	{
		if ( count > 0 && Geo.AlmostEqual( corners[count - 1], point, epsilon * 0.001f ) )
		{
			if ( count <= entered.Length ) entered[count - 1] = enters;
			return false;
		}
		corners[count] = point;
		if ( count < entered.Length ) entered[count] = enters;
		count++;
		return count >= corners.Length || enters.IsNone;
	}

	/// <summary>
	/// String-pulls a corridor into corners. The corridor is pulled in stretches that end at a link's
	/// entry and start again at its exit: a link has no width, and a vertical one would vanish from a
	/// funnel on the ground plane. When part of the corridor has gone stale the corners end on the last
	/// good polygon. entered may be empty; otherwise it gets, per corner, the polygon or link the path
	/// goes into there, none at the end.
	/// </summary>
	internal int Funnel( MeshState s, ReadOnlySpan<PolyRef> path, Vector3 from, Vector3 to, Span<Vector3> corners, Span<PolyRef> entered, bool stopAtLink )
	{
		int count = 0, first = 0;
		while ( first < path.Length && count < corners.Length )
		{
			int link = first;
			while ( link < path.Length && !path[link].IsLink ) link++;
			if ( link == path.Length ) return count + Pull( s, path[first..], from, to, corners[count..], Tail( entered, count ) );

			uint before = link > 0 ? path[link - 1].Id : PolyRef.NoId, after = link + 1 < path.Length ? path[link + 1].Id : PolyRef.NoId;
			if ( !LinkEnds( s, path[link], before, after, out var entry, out var exit ) ) return count + Pull( s, path[first..link], from, to, corners[count..], Tail( entered, count ) );

			if ( link > first )
			{
				int pulled = Pull( s, path[first..link], from, entry, corners[count..], Tail( entered, count ) );
				if ( pulled == 0 ) return count;
				count += pulled;
				// The stretch stopped short of the link when it hit a stale polygon or filled the buffer.
				if ( !Geo.AlmostEqual( corners[count - 1], entry, epsilon * 0.001f ) ) return count;
				if ( count <= entered.Length ) entered[count - 1] = path[link];
			}
			if ( stopAtLink ) return count;

			from = exit;
			first = link + 1;
			if ( first == path.Length && count < corners.Length )
			{
				corners[count] = exit;
				if ( count < entered.Length ) entered[count] = default;
				count++;
			}
		}
		return count;
	}

	static Span<PolyRef> Tail( Span<PolyRef> entered, int count ) => count < entered.Length ? entered[count..] : default;

	// The funnel over a stretch of polygons.
	int Pull( MeshState s, ReadOnlySpan<PolyRef> path, Vector3 from, Vector3 to, Span<Vector3> corners, Span<PolyRef> entered )
	{
		int size = path.Length, count = 0;
		if ( size == 0 || corners.IsEmpty || !Geo.IsFinite( from ) || !Geo.IsFinite( to ) ) return 0;
		if ( !ClosestOnBoundary( s, path[0], from, out var start ) || !ClosestOnBoundary( s, path[size - 1], to, out var end ) ) return 0;

		if ( AddCorner( start, path[0], corners, entered, ref count ) ) return count;
		if ( size == 1 )
		{
			AddCorner( end, default, corners, entered, ref count );
			return count;
		}

		Vector3 apex = start, portalLeft = start, portalRight = start;
		int apexIndex = 0, leftIndex = 0, rightIndex = 0;
		PolyRef leftEnters = path[0], rightEnters = path[0];
		float equal = epsilon * 0.001f, skip = epsilon * 0.01f;
		// Every restart moves the apex on. The cap only ends a funnel that degenerate polygons keep in a loop.
		int restarts = size * 2 + 16;

		for ( int i = 0; i < size; i++ )
		{
			Vector3 left, right;
			if ( i + 1 < size )
			{
				if ( !Portal( s, path[i], path[i + 1], out left, out right ) )
				{
					if ( ClosestOnBoundary( s, path[i], to, out end ) ) AddCorner( end, default, corners, entered, ref count );
					return count;
				}
				// Starting on the first portal: skip it.
				if ( i == 0 && Geo.DistanceToSegmentSquared( apex, left, right, out _ ) < skip * skip ) continue;
			}
			else left = right = end;

			PolyRef enters = i + 1 < size ? path[i + 1] : default;

			if ( Geo.TriArea2D( apex, portalRight, right ) <= 0 )
			{
				if ( Geo.AlmostEqual( apex, portalRight, equal ) || Geo.AlmostEqual( right, portalRight, equal ) || Geo.TriArea2D( apex, portalLeft, right ) >= 0 )
				{
					portalRight = right;
					rightEnters = enters;
					rightIndex = i;
				}
				else
				{
					// The right side crossed over the left: the left point is a corner.
					apex = portalLeft;
					apexIndex = leftIndex;
					if ( AddCorner( apex, leftEnters, corners, entered, ref count ) ) return count;
					portalLeft = portalRight = apex;
					leftIndex = rightIndex = apexIndex;
					i = apexIndex;
					if ( --restarts < 0 ) break;
					continue;
				}
			}

			if ( Geo.TriArea2D( apex, portalLeft, left ) >= 0 )
			{
				if ( Geo.AlmostEqual( apex, portalLeft, equal ) || Geo.AlmostEqual( left, portalLeft, equal ) || Geo.TriArea2D( apex, portalRight, left ) <= 0 )
				{
					portalLeft = left;
					leftEnters = enters;
					leftIndex = i;
				}
				else
				{
					apex = portalRight;
					apexIndex = rightIndex;
					if ( AddCorner( apex, rightEnters, corners, entered, ref count ) ) return count;
					portalLeft = portalRight = apex;
					leftIndex = rightIndex = apexIndex;
					i = apexIndex;
					if ( --restarts < 0 ) break;
					continue;
				}
			}
		}

		AddCorner( end, default, corners, entered, ref count );
		return count;
	}
}
