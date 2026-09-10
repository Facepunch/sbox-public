/*
Copyright (c) 2009-2010 Mikko Mononen memon@inside.org
recast4j copyright (c) 2015-2019 Piotr Piastucki piotr@jtilia.org
DotRecast Copyright (c) 2023-2024 Choi Ikpil ikpil@naver.com
Copyright (c) 2024 Facepunch Studios Ltd

This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.
Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:
1. The origin of this software must not be misrepresented; you must not
 claim that you wrote the original software. If you use this software
 in a product, an acknowledgment in the product documentation would be
 appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
 misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
*/

using static Sandbox.Navigation.Pathfinding.MeshConstants;
using System.Runtime.CompilerServices;
using System;

namespace Sandbox.Navigation.Pathfinding;

internal class MeshQuery
{
	private bool PassFilter( long reference, TraversalFilter filter ) => _nav.GetTileAndPolyByRef( reference, out _, out var polygon ).Succeeded() && filter.Allows( polygon.area );
	protected readonly NavMeshGraph _nav; //< Pointer to navmesh data.
	private PathSearch search;

	protected readonly SearchNodePool _nodePool; //< Pointer to node pool.
	protected readonly SearchQueue _openList; //< Pointer to open list queue.

	// Reused FIFO scratch for the breadth-first walks, pop = advance the head index
	private readonly List<SearchNode> _bfsStack = new();

	// Reused by FindNearestPoly
	private FindNearestPolyQuery _findNearestPolyQuery;

	// Reused by QueryPolygons
	private readonly MeshTile[] _tileScratch = new MeshTile[32];

	internal readonly record struct BoundarySegment( Vector3 Start, Vector3 End, float DistanceSquared );

	/// <summary>Collects the nearest solid edges on the locally connected surface, including closed parts of tile portals.</summary>
	internal int FindLocalWalls( long start, Vector3 position, float range, float height, TraversalFilter filter, Span<BoundarySegment> walls )
	{
		if ( !IsValidPolyRef( start, filter ) || walls.IsEmpty ) return 0;
		_nodePool.Clear();
		_bfsStack.Clear();
		var first = _nodePool.GetNode( start );
		first.flags = NodeFlags.NODE_CLOSED;
		_bfsStack.Add( first );
		int count = 0;
		float rangeSquared = range * range;
		for ( int head = 0; head < _bfsStack.Count; head++ )
		{
			_nav.GetTileAndPolyByRefUnsafe( _bfsStack[head].id, out var tile, out var poly );
			for ( int edge = 0; edge < poly.vertCount; edge++ )
			{
				var a = tile.data.verts[poly.verts[edge]];
				var b = tile.data.verts[poly.verts[(edge + 1) % poly.vertCount]];
				if ( Geometry.DistancePtSegSqr2D( position, a, b, out _ ) > rangeSquared ) continue;
				bool open = false;
				for ( int i = poly.firstLink; i != NULL_LINK; i = tile.links[i].next )
				{
					var link = tile.links[i];
					if ( link.edge != edge || !IsGroundPortal( link.refs, filter ) ) continue;
					var portalA = link.side == 0xff ? a : Vector3.Lerp( a, b, link.bmin / 255f );
					var portalB = link.side == 0xff ? b : Vector3.Lerp( a, b, link.bmax / 255f );
					open |= link.side == 0xff;
					if ( Geometry.DistancePtSegSqr2D( position, portalA, portalB, out _ ) > rangeSquared ) continue;
					var neighbour = _nodePool.GetNode( link.refs );
					if ( (neighbour.flags & NodeFlags.NODE_CLOSED) != 0 ) continue;
					neighbour.flags = NodeFlags.NODE_CLOSED;
					_bfsStack.Add( neighbour );
				}
				if ( open ) continue;
				// Sweep the union of traversable portal intervals. Gaps remain walls.
				int cursor = 0;
				while ( cursor < 255 )
				{
					int covered = cursor, next = 255;
					for ( int i = poly.firstLink; i != NULL_LINK; i = tile.links[i].next )
					{
						var link = tile.links[i];
						if ( link.edge != edge || !IsGroundPortal( link.refs, filter ) ) continue;
						if ( link.bmin <= cursor ) covered = Math.Max( covered, link.bmax );
						else next = Math.Min( next, link.bmin );
					}
					if ( covered > cursor ) { cursor = covered; continue; }
					AddLocalWall( Vector3.Lerp( a, b, cursor / 255f ), Vector3.Lerp( a, b, next / 255f ), position, rangeSquared, height, walls, ref count );
					cursor = next;
				}
			}
		}
		return count;
	}

	private bool IsGroundPortal( long reference, TraversalFilter filter )
		=> _nav.GetTileAndPolyByRef( reference, out _, out var poly ).Succeeded() && poly.type == PolyTypes.POLYTYPE_GROUND && filter.Allows( poly.area );

	private static void AddLocalWall( Vector3 a, Vector3 b, Vector3 position, float rangeSquared, float height, Span<BoundarySegment> walls, ref int count )
	{
		float distance = Geometry.DistancePtSegSqr2D( position, a, b, out float t );
		if ( distance > rangeSquared || MathF.Abs( Vector3.Lerp( a, b, t ).y - position.y ) > height ) return;
		int slot = count;
		while ( slot > 0 && distance < walls[slot - 1].DistanceSquared ) slot--;
		if ( slot >= walls.Length ) return;
		count = Math.Min( count + 1, walls.Length );
		for ( int i = count - 1; i > slot; i-- ) walls[i] = walls[i - 1];
		walls[slot] = new( a, b, distance );
	}

	internal Status BeginPathSearch( long start, long end, Vector3 startPosition, Vector3 endPosition, TraversalFilter filter )
		=> (search ??= new PathSearch( _nav, this )).Begin( start, end, startPosition, endPosition, filter );
	internal Status AdvancePathSearch( int iterations ) => search.Advance( iterations );
	internal Status FinishPathSearch( List<long> path ) => search.Finish( path );
	internal Status FindPath( long start, long end, Vector3 startPosition, Vector3 endPosition, TraversalFilter filter, List<long> path )
	{
		var status = BeginPathSearch( start, end, startPosition, endPosition, filter );
		while ( status.InProgress() ) status = AdvancePathSearch( 256 );
		if ( status.Failed() ) { path.Clear(); return status; }
		return FinishPathSearch( path );
	}

	public MeshQuery( NavMeshGraph nav )
	{
		_nav = nav;
		_nodePool = new SearchNodePool();
		_openList = new SearchQueue();

	}

	public Status FindRandomPoint( TraversalFilter filter, Random frand, out long randomRef, out Vector3 randomPt )
	{
		randomRef = 0;
		randomPt = Vector3.Zero;

		if ( null == frand )
		{
			return Status.Failure | Status.InvalidInput;
		}

		// Randomly pick one tile. Assume that all tiles cover roughly the same area.
		MeshTile tile = null;
		float tsum = 0.0f;
		for ( int i = 0; i < _nav.GetMaxTiles(); i++ )
		{
			MeshTile t = _nav.GetTile( i );
			if ( t == null || t.data == null || t.data.header == null )
			{
				continue;
			}

			// Choose random tile using reservoir sampling.
			float area = 1.0f; // Could be tile area too.
			tsum += area;
			float u = frand.NextSingle();
			if ( u * tsum <= area )
			{
				tile = t;
			}
		}

		if ( tile == null )
		{
			return Status.Failure;
		}

		// Randomly pick one polygon weighted by polygon area.
		long polyRef = 0;
		long @base = _nav.GetPolyRefBase( tile );

		float areaSum = 0.0f;
		for ( int i = 0; i < tile.data.header.polyCount; ++i )
		{
			ref Poly p = ref tile.data.polys[i];
			// Do not return off-mesh connection polygons.
			if ( p.type != PolyTypes.POLYTYPE_GROUND )
			{
				continue;
			}

			// Must pass filter
			long refs = @base | (long)i;
			if ( !filter.Allows( p.area ) )
			{
				continue;
			}

			// Calc area of the polygon.
			float polyArea = 0.0f;
			for ( int j = 2; j < p.vertCount; ++j )
			{
				int va = p.verts[0];
				int vb = p.verts[j - 1];
				int vc = p.verts[j];
				polyArea += Geometry.TriArea2D( tile.data.verts[va], tile.data.verts[vb], tile.data.verts[vc] );
			}

			// Choose random polygon weighted by area, using reservoir sampling.
			areaSum += polyArea;
			float u = frand.NextSingle();
			if ( u * areaSum <= polyArea )
			{
				polyRef = refs;
			}
		}

		if ( polyRef == 0 )
		{
			return Status.Failure;
		}

		_nav.GetTileAndPolyByRefUnsafe( polyRef, out _, out var poly );

		// Randomly pick point on polygon.
		Span<Vector3> verts = stackalloc Vector3[poly.vertCount];

		for ( int j = 0; j < poly.vertCount; ++j )
		{
			verts[j] = tile.data.verts[poly.verts[j]];
		}

		float s = frand.NextSingle();
		float t0 = frand.NextSingle();

		Geometry.RandomPointInConvexPoly( verts, s, t0, out var pt );
		ClosestPointOnPoly( polyRef, pt, out var closest, out var _ );

		randomRef = polyRef;
		randomPt = closest;

		return Status.Success;
	}

	public Status ClosestPointOnPoly( long refs, Vector3 pos, out Vector3 closest, out bool posOverPoly )
	{
		closest = pos;
		posOverPoly = false;

		if ( !_nav.IsValidPolyRef( refs ) || !Geometry.IsFinite( pos ) )
		{
			return Status.Failure | Status.InvalidInput;
		}

		_nav.ClosestPointOnPoly( refs, pos, out closest, out posOverPoly );
		return Status.Success;
	}

	public Status ClosestPointOnPolyBoundary( long refs, Vector3 pos, out Vector3 closest )
	{
		closest = pos;
		var status = _nav.GetTileAndPolyByRef( refs, out var tile, out var poly );
		if ( status.Failed() )
		{
			return Status.Failure | Status.InvalidInput;
		}

		if ( tile == null || !Geometry.IsFinite( pos ) )
		{
			return Status.Failure | Status.InvalidInput;
		}

		// Collect vertices.
		int nv = poly.vertCount;

		Span<Vector3> verts = stackalloc Vector3[nv];
		Span<float> edged = stackalloc float[nv];
		Span<float> edget = stackalloc float[nv];
		for ( int i = 0; i < nv; ++i )
		{
			verts[i] = tile.data.verts[poly.verts[i]];
		}

		if ( Geometry.DistancePtPolyEdgesSqr( pos, verts, edged, edget ) )
		{
			closest = pos;
		}
		else
		{
			// Point is outside the polygon, Clamp to nearest edge.
			float dmin = edged[0];
			int imin = 0;
			for ( int i = 1; i < nv; ++i )
			{
				if ( edged[i] < dmin )
				{
					dmin = edged[i];
					imin = i;
				}
			}

			int va = imin;
			int vb = ((imin + 1) % nv);
			closest = Vector3.Lerp( verts[va], verts[vb], edget[imin] );
		}

		return Status.Success;
	}

	public Status GetPolyHeight( long refs, Vector3 pos, out float height )
	{
		height = default;

		var status = _nav.GetTileAndPolyByRef( refs, out var tile, out var poly );
		if ( status.Failed() )
		{
			return Status.Failure | Status.InvalidInput;
		}

		if ( !Geometry.IsFinite( pos ) )
		{
			return Status.Failure | Status.InvalidInput;
		}

		// We used to return success for offmesh connections, but the
		// Graph height queries do not handle links, so special
		// case it here.
		if ( poly.type == PolyTypes.POLYTYPE_OFFMESH_CONNECTION )
		{
			int i = poly.verts[0];
			var v0 = tile.data.verts[i];
			i = poly.verts[1];
			var v1 = tile.data.verts[i];
			Geometry.DistancePtSegSqr2D( pos, v0, v1, out var t );
			height = v0.y + (v1.y - v0.y) * t;

			return Status.Success;
		}

		if ( !_nav.GetPolyHeight( tile, ref poly, pos, out var h ) )
		{
			return Status.Failure | Status.InvalidInput;
		}

		height = h;
		return Status.Success;
	}

	public Status FindNearestPoly( Vector3 center, Vector3 halfExtents, TraversalFilter filter,
		out long nearestRef, out Vector3 nearestPt, out bool isOverPoly )
	{
		nearestRef = 0;
		nearestPt = center;
		isOverPoly = false;

		// Get nearby polygons from proximity grid.
		var query = _findNearestPolyQuery ??= new FindNearestPolyQuery( this, center );
		query.Init( this, center );
		Status status = QueryPolygons( center, halfExtents, filter, query );
		if ( status.Failed() )
		{
			return status;
		}

		nearestRef = query.NearestRef();
		nearestPt = query.NearestPt();
		isOverPoly = query.OverPoly();

		return Status.Success;
	}

	protected void QueryPolygonsInTile( MeshTile tile, Vector3 qmin, Vector3 qmax, TraversalFilter filter, IPolyQuery query )
	{
		Span<long> batch = stackalloc long[32];
		int count = 0;
		long baseReference = _nav.GetPolyRefBase( tile );
		foreach ( int polygon in BoundingVolumeTree.Query( tile.data, qmin, qmax ) )
		{
			long reference = baseReference | (long)polygon;
			if ( !filter.Allows( tile.data.polys[polygon].area ) ) continue;
			batch[count++] = reference;
			if ( count == batch.Length ) { query.Process( tile, batch, count ); count = 0; }
		}
		if ( count > 0 ) query.Process( tile, batch, count );
	}

	public Status QueryPolygons( Vector3 center, Vector3 halfExtents,
		TraversalFilter filter,
		long[] polys, out int polyCount, int maxPolys )
	{
		polyCount = 0;
		if ( null == polys || maxPolys < 0 )
			return Status.Failure | Status.InvalidInput;

		CollectPolysQuery collector = new CollectPolysQuery( polys, maxPolys );
		Status status = QueryPolygons( center, halfExtents, filter, collector );
		if ( status.Failed() )
			return status;

		polyCount = collector.NumCollected();
		return collector.Overflowed()
			? Status.Success | Status.BufferTooSmall
			: Status.Success;
	}

	public Status QueryPolygons( Vector3 center, Vector3 halfExtents, TraversalFilter filter, IPolyQuery query )
	{
		if ( !Geometry.IsFinite( center ) || !Geometry.IsFinite( halfExtents ) )
		{
			return Status.InvalidInput;
		}

		// Find tiles the query touches.
		Vector3 bmin = center - halfExtents;
		Vector3 bmax = center + halfExtents;

		// Find tiles the query touches.
		_nav.CalcTileLoc( bmin, out var minx, out var miny );
		_nav.CalcTileLoc( bmax, out var maxx, out var maxy );

		MeshTile[] neis = _tileScratch;

		for ( int y = miny; y <= maxy; ++y )
		{
			for ( int x = minx; x <= maxx; ++x )
			{
				int nneis = _nav.GetTilesAt( x, y, neis, neis.Length );
				for ( int j = 0; j < nneis; ++j )
				{
					QueryPolygonsInTile( neis[j], bmin, bmax, filter, query );
				}
			}
		}

		return Status.Success;
	}

	protected Status AppendVertex( Vector3 pos, byte flags, long refs, Span<StraightPath> straightPath, ref int straightPathCount, int maxStraightPath )
	{
		if ( straightPathCount > 0 && straightPath[straightPathCount - 1].pos.AlmostEqual( pos ) )
		{
			// The vertices are equal, update flags and poly.
			straightPath[straightPathCount - 1] = new StraightPath( straightPath[straightPathCount - 1].pos, flags, refs );
		}
		else
		{
			// Append new vertex.
			straightPath[straightPathCount] = new StraightPath( pos, flags, refs );
			straightPathCount++;

			// If there is no space to append more vertices, return.
			if ( straightPathCount >= maxStraightPath )
			{
				return Status.Success | Status.BufferTooSmall;
			}

			// If reached end of path, return.
			if ( flags == StraightPathFlags.STRAIGHTPATH_END )
			{
				return Status.Success;
			}
		}

		return Status.Running;
	}

	protected Status AppendPortals( int startIdx, int endIdx, Vector3 endPos, List<long> path,
		Span<StraightPath> straightPath, ref int straightPathCount, int maxStraightPath, int options )
	{
		var startPos = straightPath[straightPathCount - 1].pos;
		// Append or update last vertex
		Status stat;
		for ( int i = startIdx; i < endIdx; i++ )
		{
			// Calculate portal
			long from = path[i];
			var status = _nav.GetTileAndPolyByRef( from, out var fromTile, out var fromPoly );
			if ( status.Failed() )
			{
				return Status.Failure;
			}

			long to = path[i + 1];
			status = _nav.GetTileAndPolyByRef( to, out var toTile, out var toPoly );
			if ( status.Failed() )
			{
				return Status.Failure;
			}

			var ppStatus = GetPortalPoints( from, ref fromPoly, fromTile, to, ref toPoly, toTile, out var left, out var right );
			if ( ppStatus.Failed() )
			{
				break;
			}

			if ( (options & StraightPathOptions.STRAIGHTPATH_AREA_CROSSINGS) != 0 )
			{
				// Skip intersection if only area crossings are requested.
				if ( fromPoly.area == toPoly.area )
				{
					continue;
				}
			}

			// Append intersection
			if ( Geometry.IntersectSegSeg2D( startPos, endPos, left, right, out var _, out var t ) )
			{
				var pt = Vector3.Lerp( left, right, t );
				stat = AppendVertex( pt, 0, path[i + 1], straightPath, ref straightPathCount, maxStraightPath );
				if ( !stat.InProgress() )
				{
					return stat;
				}
			}
		}

		return Status.Running;
	}

	public virtual Status FindStraightPath( Vector3 startPos, Vector3 endPos,
		List<long> path, int pathSize,
		Span<StraightPath> straightPath, out int straightPathCount, int maxStraightPath,
		int options )
	{
		straightPathCount = 0;

		if ( !Geometry.IsFinite( startPos ) || !Geometry.IsFinite( endPos ) ||
			straightPath.IsEmpty ||
			null == path || pathSize <= 0 || path[0] == 0
			|| maxStraightPath <= 0 )
		{
			return Status.Failure | Status.InvalidInput;
		}

		Status stat = Status.None;

		// TODO: Should this be callers responsibility?
		var closestStartPosRes = ClosestPointOnPolyBoundary( path[0], startPos, out var closestStartPos );
		if ( closestStartPosRes.Failed() )
		{
			return Status.Failure | Status.InvalidInput;
		}

		var closestEndPosRes = ClosestPointOnPolyBoundary( path[pathSize - 1], endPos, out var closestEndPos );
		if ( closestEndPosRes.Failed() )
		{
			return Status.Failure | Status.InvalidInput;
		}

		// Add start point.
		stat = AppendVertex( closestStartPos, StraightPathFlags.STRAIGHTPATH_START, path[0], straightPath, ref straightPathCount, maxStraightPath );
		if ( !stat.InProgress() )
		{
			return stat;
		}

		if ( pathSize > 1 )
		{
			Vector3 portalApex = closestStartPos;
			Vector3 portalLeft = portalApex;
			Vector3 portalRight = portalApex;
			int apexIndex = 0;
			int leftIndex = 0;
			int rightIndex = 0;

			int leftPolyType = 0;
			int rightPolyType = 0;

			long leftPolyRef = path[0];
			long rightPolyRef = path[0];

			for ( int i = 0; i < pathSize; ++i )
			{
				Vector3 left;
				Vector3 right;
				int toType;

				if ( i + 1 < pathSize )
				{
					int fromType; // // fromType is ignored.

					// Next portal.
					var ppStatus = GetPortalPoints( path[i], path[i + 1], out left, out right, out fromType, out toType );
					if ( ppStatus.Failed() )
					{
						// Failed to get portal points, in practice this means that path[i+1] is invalid polygon.
						// Clamp the end point to path[i], and return the path so far.
						var cpStatus = ClosestPointOnPolyBoundary( path[i], endPos, out closestEndPos );
						if ( cpStatus.Failed() )
						{
							return Status.Failure | Status.InvalidInput;
						}

						// Append portals along the current straight path segment.
						if ( (options & (StraightPathOptions.STRAIGHTPATH_AREA_CROSSINGS | StraightPathOptions.STRAIGHTPATH_ALL_CROSSINGS)) != 0 )
						{
							// Ignore status return value as we're just about to return anyway.
							AppendPortals( apexIndex, i, closestEndPos, path, straightPath, ref straightPathCount, maxStraightPath, options );
						}

						// Ignore status return value as we're just about to return anyway.
						AppendVertex( closestEndPos, 0, path[i], straightPath, ref straightPathCount, maxStraightPath );

						return Status.Failure | Status.InvalidInput | (straightPathCount >= maxStraightPath ? Status.BufferTooSmall : Status.None);
					}

					// If starting really close the portal, advance.
					if ( i == 0 )
					{
						var distSqr = Geometry.DistancePtSegSqr2D( portalApex, left, right, out var t );
						if ( distSqr < 0.001f * 0.001f )
						{
							continue;
						}
					}
				}
				else
				{
					// End of the path.
					left = closestEndPos;
					right = closestEndPos;
					toType = PolyTypes.POLYTYPE_GROUND;
				}

				// Right vertex.
				if ( Geometry.TriArea2D( portalApex, portalRight, right ) <= 0.0f )
				{
					if ( portalApex.AlmostEqual( portalRight ) || right.AlmostEqual( portalRight ) || Geometry.TriArea2D( portalApex, portalLeft, right ) >= 0.0f )
					{
						portalRight = right;
						rightPolyRef = (i + 1 < pathSize) ? path[i + 1] : 0;
						rightPolyType = toType;
						rightIndex = i;
					}
					else
					{
						// Append portals along the current straight path segment.
						if ( (options & (StraightPathOptions.STRAIGHTPATH_AREA_CROSSINGS | StraightPathOptions.STRAIGHTPATH_ALL_CROSSINGS)) != 0 )
						{
							stat = AppendPortals( apexIndex, leftIndex, portalLeft, path, straightPath, ref straightPathCount, maxStraightPath, options );
							if ( !stat.InProgress() )
							{
								return stat;
							}
						}

						portalApex = portalLeft;
						apexIndex = leftIndex;

						byte flags = 0;
						if ( leftPolyRef == 0 )
						{
							flags = StraightPathFlags.STRAIGHTPATH_END;
						}
						else if ( leftPolyType == PolyTypes.POLYTYPE_OFFMESH_CONNECTION )
						{
							flags = StraightPathFlags.STRAIGHTPATH_OFFMESH_CONNECTION;
						}

						long refs = leftPolyRef;

						// Append or update vertex
						stat = AppendVertex( portalApex, flags, refs, straightPath, ref straightPathCount, maxStraightPath );
						if ( !stat.InProgress() )
						{
							return stat;
						}

						portalLeft = portalApex;
						portalRight = portalApex;
						leftIndex = apexIndex;
						rightIndex = apexIndex;

						// Restart
						i = apexIndex;

						continue;
					}
				}

				// Left vertex.
				if ( Geometry.TriArea2D( portalApex, portalLeft, left ) >= 0.0f )
				{
					if ( portalApex.AlmostEqual( portalLeft ) || left.AlmostEqual( portalLeft ) || Geometry.TriArea2D( portalApex, portalRight, left ) <= 0.0f )
					{
						portalLeft = left;
						leftPolyRef = (i + 1 < pathSize) ? path[i + 1] : 0;
						leftPolyType = toType;
						leftIndex = i;
					}
					else
					{
						// Append portals along the current straight path segment.
						if ( (options & (StraightPathOptions.STRAIGHTPATH_AREA_CROSSINGS | StraightPathOptions.STRAIGHTPATH_ALL_CROSSINGS)) != 0 )
						{
							stat = AppendPortals( apexIndex, rightIndex, portalRight, path, straightPath, ref straightPathCount, maxStraightPath, options );
							if ( !stat.InProgress() )
							{
								return stat;
							}
						}

						portalApex = portalRight;
						apexIndex = rightIndex;

						byte flags = 0;
						if ( rightPolyRef == 0 )
						{
							flags = StraightPathFlags.STRAIGHTPATH_END;
						}
						else if ( rightPolyType == PolyTypes.POLYTYPE_OFFMESH_CONNECTION )
						{
							flags = StraightPathFlags.STRAIGHTPATH_OFFMESH_CONNECTION;
						}

						long refs = rightPolyRef;

						// Append or update vertex
						stat = AppendVertex( portalApex, flags, refs, straightPath, ref straightPathCount, maxStraightPath );
						if ( !stat.InProgress() )
						{
							return stat;
						}

						portalLeft = portalApex;
						portalRight = portalApex;
						leftIndex = apexIndex;
						rightIndex = apexIndex;

						// Restart
						i = apexIndex;

						continue;
					}
				}
			}

			// Append portals along the current straight path segment.
			if ( (options & (StraightPathOptions.STRAIGHTPATH_AREA_CROSSINGS | StraightPathOptions.STRAIGHTPATH_ALL_CROSSINGS)) != 0 )
			{
				stat = AppendPortals( apexIndex, pathSize - 1, closestEndPos, path, straightPath, ref straightPathCount, maxStraightPath, options );
				if ( !stat.InProgress() )
				{
					return stat;
				}
			}
		}

		// Ignore status return value as we're just about to return anyway.
		AppendVertex( closestEndPos, StraightPathFlags.STRAIGHTPATH_END, 0, straightPath, ref straightPathCount, maxStraightPath );
		return Status.Success | (straightPathCount >= maxStraightPath ? Status.BufferTooSmall : Status.None);
	}

	public Status MoveAlongSurface( long startRef, Vector3 startPos, Vector3 endPos,
		TraversalFilter filter,
		out Vector3 resultPos, Span<long> visited, out int visitedCount, int maxVisitedSize )
	{
		resultPos = Vector3.Zero;

		visitedCount = 0;

		// Validate input
		if ( !_nav.IsValidPolyRef( startRef ) || !Geometry.IsFinite( startPos )
											|| !Geometry.IsFinite( endPos ) )
		{
			return Status.Failure | Status.InvalidInput;
		}

		Status status = Status.Success;

		_nodePool.Clear();

		SearchNode startNode = _nodePool.GetNode( startRef );
		startNode.pidx = 0;
		startNode.cost = 0;
		startNode.total = 0;
		startNode.id = startRef;
		startNode.flags = NodeFlags.NODE_CLOSED;
		_bfsStack.Clear();
		int bfsHead = 0;
		_bfsStack.Add( startNode );

		Vector3 bestPos = new Vector3();
		float bestDist = float.MaxValue;
		SearchNode bestNode = null;
		bestPos = startPos;

		// Search constraints
		var searchPos = Vector3.Lerp( startPos, endPos, 0.5f );
		float searchRad = Vector3.DistanceBetween( startPos, endPos ) / 2.0f + 0.001f;
		float searchRadSqr = searchRad * searchRad;

		Span<Vector3> verts = stackalloc Vector3[_nav.GetMaxVertsPerPoly()];

		const int MAX_NEIS = 8;
		Span<long> neis = stackalloc long[MAX_NEIS];

		while ( bfsHead < _bfsStack.Count )
		{
			// Pop front.
			SearchNode curNode = _bfsStack[bfsHead++];

			// Get poly and tile.
			// The API input has been checked already, skip checking internal data.
			long curRef = curNode.id;
			_nav.GetTileAndPolyByRefUnsafe( curRef, out var curTile, out var curPoly );

			// Collect vertices.
			int nverts = curPoly.vertCount;
			for ( int i = 0; i < nverts; ++i )
			{
				verts[i] = curTile.data.verts[curPoly.verts[i]];
			}

			// If target is inside the poly, stop search.
			if ( Geometry.PointInPolygon( endPos, verts.Slice( 0, nverts ) ) )
			{
				bestNode = curNode;
				bestPos = endPos;
				break;
			}

			// Find wall edges and find nearest point inside the walls.
			for ( int i = 0, j = curPoly.vertCount - 1; i < curPoly.vertCount; j = i++ )
			{
				// Find links to neighbours.
				int nneis = 0;

				if ( (curPoly.neis[j] & EXT_LINK) != 0 )
				{
					// Tile border.
					for ( int k = curPoly.firstLink; k != NULL_LINK; k = curTile.links[k].next )
					{
						Link link = curTile.links[k];
						if ( link.edge == j )
						{
							if ( link.refs != 0 )
							{
								if ( PassFilter( link.refs, filter ) )
								{
									if ( nneis < MAX_NEIS )
									{
										neis[nneis++] = link.refs;
									}
								}
							}
						}
					}
				}
				else if ( curPoly.neis[j] != 0 )
				{
					int idx = curPoly.neis[j] - 1;
					long refs = _nav.GetPolyRefBase( curTile ) | (long)idx;
					if ( PassFilter( refs, filter ) )
					{
						// Internal edge, encode id.
						neis[nneis++] = refs;
					}
				}

				if ( nneis == 0 )
				{
					// Wall edge, calc distance.
					int vj = j;
					int vi = i;
					var distSqr = Geometry.DistancePtSegSqr2D( endPos, verts[vj], verts[vi], out var tseg );
					if ( distSqr < bestDist )
					{
						// Update nearest distance.
						bestPos = Vector3.Lerp( verts[vj], verts[vi], tseg );
						bestDist = distSqr;
						bestNode = curNode;
					}
				}
				else
				{
					for ( int k = 0; k < nneis; ++k )
					{
						SearchNode neighbourNode = _nodePool.GetNode( neis[k] );
						// Skip if already visited.
						if ( (neighbourNode.flags & NodeFlags.NODE_CLOSED) != 0 )
						{
							continue;
						}

						// Skip the link if it is too far from search constraint.
						// TODO: Maybe should use GetPortalPoints(), but this one is way faster.
						int vj = j;
						int vi = i;
						var distSqr = Geometry.DistancePtSegSqr2D( searchPos, verts[vj], verts[vi], out var _ );
						if ( distSqr > searchRadSqr )
						{
							continue;
						}

						// Mark as the node as visited and push to queue.
						neighbourNode.pidx = _nodePool.GetNodeIdx( curNode );
						neighbourNode.flags |= NodeFlags.NODE_CLOSED;
						_bfsStack.Add( neighbourNode );
					}
				}
			}
		}

		int n = 0;
		if ( bestNode != null )
		{
			// Reverse the path.
			SearchNode prev = null;
			SearchNode node = bestNode;
			do
			{
				SearchNode next = _nodePool.GetNodeAtIdx( node.pidx );
				node.pidx = _nodePool.GetNodeIdx( prev );
				prev = node;
				node = next;
			} while ( node != null );

			// Store result
			node = prev;
			do
			{
				visited[n++] = node.id;
				if ( n >= maxVisitedSize )
				{
					status |= Status.BufferTooSmall;
					;
					break;
				}

				node = _nodePool.GetNodeAtIdx( node.pidx );
			} while ( node != null );
		}

		resultPos = bestPos;
		visitedCount = n;

		return status;
	}

	protected Status GetPortalPoints( long from, long to, out Vector3 left, out Vector3 right, out int fromType, out int toType )
	{
		left = Vector3.Zero;
		right = Vector3.Zero;
		fromType = 0;
		toType = 0;

		var status = _nav.GetTileAndPolyByRef( from, out var fromTile, out var fromPoly );
		if ( status.Failed() )
		{
			return Status.Failure | Status.InvalidInput;
		}

		fromType = fromPoly.type;

		status = _nav.GetTileAndPolyByRef( to, out var toTile, out var toPoly );
		if ( status.Failed() )
		{
			return Status.Failure | Status.InvalidInput;
		}

		toType = toPoly.type;

		return GetPortalPoints( from, ref fromPoly, fromTile, to, ref toPoly, toTile, out left, out right );
	}

	// Returns portal points between two polygons.
	protected Status GetPortalPoints( long from, ref Poly fromPoly, MeshTile fromTile,
		long to, ref Poly toPoly, MeshTile toTile,
		out Vector3 left, out Vector3 right )
	{
		left = Vector3.Zero;
		right = Vector3.Zero;

		// Find the link that points to the 'to' polygon.
		int matchingLink = NULL_LINK;
		for ( int i = fromPoly.firstLink; i != NULL_LINK; i = fromTile.links[i].next )
		{
			if ( fromTile.links[i].refs == to )
			{
				matchingLink = i;
				break;
			}
		}

		if ( matchingLink == NULL_LINK )
		{
			return Status.Failure | Status.InvalidInput;
		}

		ref readonly var link = ref fromTile.links[matchingLink];

		// Handle off-mesh connections.
		if ( fromPoly.type == PolyTypes.POLYTYPE_OFFMESH_CONNECTION )
		{
			// Find link that points to first vertex.
			for ( int i = fromPoly.firstLink; i != NULL_LINK; i = fromTile.links[i].next )
			{
				if ( fromTile.links[i].refs == to )
				{
					int v = fromTile.links[i].edge;

					left = fromTile.data.verts[fromPoly.verts[v]];

					right = fromTile.data.verts[fromPoly.verts[v]];

					return Status.Success;
				}
			}

			return Status.Failure | Status.InvalidInput;
		}

		if ( toPoly.type == PolyTypes.POLYTYPE_OFFMESH_CONNECTION )
		{
			for ( int i = toPoly.firstLink; i != NULL_LINK; i = toTile.links[i].next )
			{
				if ( toTile.links[i].refs == from )
				{
					int v = toTile.links[i].edge;

					left = toTile.data.verts[toPoly.verts[v]];

					right = toTile.data.verts[toPoly.verts[v]];

					return Status.Success;
				}
			}

			return Status.Failure | Status.InvalidInput;
		}

		// Find portal vertices.
		int v0 = fromPoly.verts[link.edge];
		int v1 = fromPoly.verts[(link.edge + 1) % fromPoly.vertCount];

		left = fromTile.data.verts[v0];
		right = fromTile.data.verts[v1];

		// If the link is at tile boundary, Clamp the vertices to
		// the link width.
		if ( link.side != 0xff )
		{
			// Unpack portal limits.
			if ( link.bmin != 0 || link.bmax != 255 )
			{
				float s = 1.0f / 255.0f;
				float tmin = link.bmin * s;
				float tmax = link.bmax * s;

				left = Vector3.Lerp( fromTile.data.verts[v0], fromTile.data.verts[v1], tmin );
				right = Vector3.Lerp( fromTile.data.verts[v0], fromTile.data.verts[v1], tmax );
			}
		}

		return Status.Success;
	}

	internal Status GetEdgeMidPoint( long from, ref Poly fromPoly, MeshTile fromTile, long to,
		ref Poly toPoly, MeshTile toTile, ref Vector3 mid )
	{
		var ppStatus = GetPortalPoints( from, ref fromPoly, fromTile, to, ref toPoly, toTile, out var left, out var right );
		if ( ppStatus.Failed() )
		{
			return Status.Failure | Status.InvalidInput;
		}

		mid.x = (left.x + right.x) * 0.5f;
		mid.y = (left.y + right.y) * 0.5f;
		mid.z = (left.z + right.z) * 0.5f;

		return Status.Success;
	}

	internal Status GetEdgeIntersectionPoint( Vector3 fromPos, long from, ref Poly fromPoly, MeshTile fromTile,
		Vector3 toPos, long to, ref Poly toPoly, MeshTile toTile,
		ref Vector3 pt )
	{
		var ppStatus = GetPortalPoints( from, ref fromPoly, fromTile, to, ref toPoly, toTile, out var left, out var right );
		if ( ppStatus.Failed() )
		{
			return Status.Failure;
		}

		float t = 0.5f;
		if ( Geometry.IntersectSegSeg2D( fromPos, toPos, left, right, out var _, out var t2 ) )
		{
			t = Math.Clamp( t2, 0.1f, 0.9f );
		}

		pt = Vector3.Lerp( left, right, t );
		return Status.Success;
	}

	public virtual Status FindDistanceToWall( long startRef, Vector3 centerPos, float maxRadius,
		TraversalFilter filter,
		out float hitDist, out Vector3 hitPos, out Vector3 hitNormal )
	{
		hitDist = 0;
		hitPos = Vector3.Zero;
		hitNormal = Vector3.Zero;

		// Validate input
		if ( !_nav.IsValidPolyRef( startRef ) || !Geometry.IsFinite( centerPos ) || maxRadius < 0
			|| !float.IsFinite( maxRadius ) )
		{
			return Status.Failure | Status.InvalidInput;
		}

		_nodePool.Clear();
		_openList.Clear();

		SearchNode startNode = _nodePool.GetNode( startRef );
		startNode.pos = centerPos;
		startNode.pidx = 0;
		startNode.cost = 0;
		startNode.total = 0;
		startNode.id = startRef;
		startNode.flags = NodeFlags.NODE_OPEN;
		_openList.Push( startNode );

		float radiusSqr = maxRadius * maxRadius;

		var hasBestV = false;
		var bestvj = Vector3.Zero;
		var bestvi = Vector3.Zero;

		var status = Status.Success;
		while ( !_openList.IsEmpty() )
		{
			SearchNode bestNode = _openList.Pop();
			bestNode.flags &= ~NodeFlags.NODE_OPEN;
			bestNode.flags |= NodeFlags.NODE_CLOSED;

			// Get poly and tile.
			// The API input has been checked already, skip checking internal data.
			long bestRef = bestNode.id;
			_nav.GetTileAndPolyByRefUnsafe( bestRef, out var bestTile, out var bestPoly );

			// Get parent poly and tile.
			long parentRef = 0;
			if ( bestNode.pidx != 0 )
			{
				parentRef = _nodePool.GetNodeAtIdx( bestNode.pidx ).id;
			}

			// Hit test walls.
			for ( int i = 0, j = bestPoly.vertCount - 1; i < bestPoly.vertCount; j = i++ )
			{
				// Skip non-solid edges.
				if ( (bestPoly.neis[j] & EXT_LINK) != 0 )
				{
					// Tile border.
					bool solid = true;
					for ( int k = bestPoly.firstLink; k != NULL_LINK; k = bestTile.links[k].next )
					{
						Link link = bestTile.links[k];
						if ( link.edge == j )
						{
							if ( link.refs != 0 )
							{
								if ( PassFilter( link.refs, filter ) )
								{
									solid = false;
								}
							}

							break;
						}
					}

					if ( !solid )
					{
						continue;
					}
				}
				else if ( bestPoly.neis[j] != 0 )
				{
					// Internal edge
					int idx = (bestPoly.neis[j] - 1);
					long refs = _nav.GetPolyRefBase( bestTile ) | (long)idx;
					if ( PassFilter( refs, filter ) )
					{
						continue;
					}
				}

				// Calc distance to the edge.
				int vj = bestPoly.verts[j];
				int vi = bestPoly.verts[i];
				var distSqr = Geometry.DistancePtSegSqr2D( centerPos, bestTile.data.verts[vj], bestTile.data.verts[vi], out var tseg );

				// Edge is too far, skip.
				if ( distSqr > radiusSqr )
				{
					continue;
				}

				// Hit wall, update radius.
				radiusSqr = distSqr;
				// Calculate hit pos.
				hitPos = bestTile.data.verts[vj] + (bestTile.data.verts[vi] - bestTile.data.verts[vj]) * tseg;

				hasBestV = true;
				bestvj = bestTile.data.verts[vj];
				bestvi = bestTile.data.verts[vi];
			}

			for ( int i = bestPoly.firstLink; i != NULL_LINK; i = bestTile.links[i].next )
			{
				Link link = bestTile.links[i];
				long neighbourRef = link.refs;
				// Skip invalid neighbours and do not follow back to parent.
				if ( neighbourRef == 0 || neighbourRef == parentRef )
				{
					continue;
				}

				// Expand to neighbour.
				_nav.GetTileAndPolyByRefUnsafe( neighbourRef, out var neighbourTile, out var neighbourPoly );

				// Skip off-mesh connections.
				if ( neighbourPoly.type == PolyTypes.POLYTYPE_OFFMESH_CONNECTION )
				{
					continue;
				}

				// Calc distance to the edge.
				int va = bestPoly.verts[link.edge];
				int vb = bestPoly.verts[(link.edge + 1) % bestPoly.vertCount];
				var distSqr = Geometry.DistancePtSegSqr2D( centerPos, bestTile.data.verts[va], bestTile.data.verts[vb], out var tseg );
				// If the circle is not touching the next polygon, skip it.
				if ( distSqr > radiusSqr )
				{
					continue;
				}

				if ( !PassFilter( neighbourRef, filter ) )
				{
					continue;
				}

				SearchNode neighbourNode = _nodePool.GetNode( neighbourRef );
				if ( null == neighbourNode )
				{
					status |= Status.CapacityExceeded;
					continue;
				}

				if ( (neighbourNode.flags & NodeFlags.NODE_CLOSED) != 0 )
				{
					continue;
				}

				// Cost
				if ( neighbourNode.flags == 0 )
				{
					GetEdgeMidPoint( bestRef, ref bestPoly, bestTile,
						neighbourRef, ref neighbourPoly, neighbourTile,
						ref neighbourNode.pos );
				}

				float total = bestNode.total + Vector3.DistanceBetween( bestNode.pos, neighbourNode.pos );

				// The node is already in open list and the new result is worse, skip.
				if ( (neighbourNode.flags & NodeFlags.NODE_OPEN) != 0 && total >= neighbourNode.total )
				{
					continue;
				}

				neighbourNode.id = neighbourRef;
				neighbourNode.flags = (neighbourNode.flags & ~NodeFlags.NODE_CLOSED);
				neighbourNode.pidx = _nodePool.GetNodeIdx( bestNode );
				neighbourNode.total = total;

				if ( (neighbourNode.flags & NodeFlags.NODE_OPEN) != 0 )
				{
					_openList.Modify( neighbourNode );
				}
				else
				{
					neighbourNode.flags |= NodeFlags.NODE_OPEN;
					_openList.Push( neighbourNode );
				}
			}
		}

		// Calc hit normal.
		if ( hasBestV )
		{
			var tangent = bestvi - bestvj;
			hitNormal = new Vector3( tangent.z, 0, -tangent.x ).Normal;
		}

		hitDist = MathF.Sqrt( radiusSqr );

		return status;
	}

	public bool IsValidPolyRef( long refs, TraversalFilter filter )
	{
		var status = _nav.GetTileAndPolyByRef( refs, out var tile, out var poly );
		if ( status.Failed() )
		{
			return false;
		}

		// If cannot pass filter, assume flags has changed and boundary is invalid.
		if ( !PassFilter( refs, filter ) )
		{
			return false;
		}

		return true;
	}

	// Gets the path leading to the specified end node.

}

[Flags]
internal enum Status
{
	None = 0,
	Failure = 1,
	Success = 2,
	Running = 4,
	InvalidInput = 8,
	BufferTooSmall = 16,
	Partial = 32,
	CapacityExceeded = 64,
	Occupied = 128,
}

internal static class StatusExtensions
{
	internal static bool IsEmpty( this Status status ) => status == Status.None;
	internal static bool Succeeded( this Status status ) => (status & (Status.Success | Status.Partial)) != 0;
	internal static bool Failed( this Status status ) => (status & (Status.Failure | Status.InvalidInput)) != 0;
	internal static bool InProgress( this Status status ) => (status & Status.Running) != 0;
	internal static bool IsPartial( this Status status ) => (status & Status.Partial) != 0;
}

internal static class StraightPathFlags
{
	public const byte STRAIGHTPATH_START = 0x01; //< The vertex is the start position in the path.
	public const byte STRAIGHTPATH_END = 0x02; //< The vertex is the end position in the path.
	public const byte STRAIGHTPATH_OFFMESH_CONNECTION = 0x04; //< The vertex is the start of an off-mesh connection.
}

//TODO: (PP) Add comments
internal readonly struct StraightPath
{
	public readonly Vector3 pos;

	public readonly byte flags;

	public readonly long refs;

	public StraightPath( Vector3 pos, byte flags, long refs )
	{
		this.pos = pos;
		this.flags = flags;
		this.refs = refs;
	}
}

internal static class StraightPathOptions
{
	public const int STRAIGHTPATH_AREA_CROSSINGS = 0x01; //< Add a vertex at every polygon edge crossing where area changes.
	public const int STRAIGHTPATH_ALL_CROSSINGS = 0x02; //< Add a vertex at every polygon edge crossing.
}

internal class StraightPathOption
{
	public static readonly StraightPathOption None = new StraightPathOption( 0, "None" );
	public static readonly StraightPathOption AreaCrossings = new StraightPathOption( StraightPathOptions.STRAIGHTPATH_AREA_CROSSINGS, "Area" );
	public static readonly StraightPathOption AllCrossings = new StraightPathOption( StraightPathOptions.STRAIGHTPATH_ALL_CROSSINGS, "All" );

	public readonly int Value;
	public readonly string Label;

	private StraightPathOption( int value, string label )
	{
		Value = value;
		Label = label;
	}
}

internal interface IPolyQuery
{
	void Process( MeshTile tile, Span<long> refs, int count );
}

internal class CollectPolysQuery : IPolyQuery
{
	private long[] _polys;
	private int _maxPolys;
	private int _numCollected;
	private bool _overflow;

	public CollectPolysQuery( long[] polys, int maxPolys )
	{
		_polys = polys;
		_maxPolys = maxPolys;
	}

	public int NumCollected()
	{
		return _numCollected;
	}

	public bool Overflowed()
	{
		return _overflow;
	}

	public void Process( MeshTile tile, Span<long> refs, int count )
	{
		int numLeft = _maxPolys - _numCollected;
		int toCopy = count;
		if ( toCopy > numLeft )
		{
			_overflow = true;
			toCopy = numLeft;
		}

		refs.Slice( 0, toCopy ).CopyTo( _polys.AsSpan( _numCollected, toCopy ) );
		_numCollected += toCopy;
	}
}

internal class FindNearestPolyQuery : IPolyQuery
{
	private MeshQuery _query;
	private Vector3 _center;
	private float _nearestDistanceSqr;
	private long _nearestRef;
	private Vector3 _nearestPoint;
	private bool _overPoly;

	public FindNearestPolyQuery( MeshQuery query, Vector3 center )
	{
		Init( query, center );
	}

	public void Init( MeshQuery query, Vector3 center )
	{
		_query = query;
		_center = center;
		_nearestDistanceSqr = float.MaxValue;
		_nearestRef = 0;
		_nearestPoint = center;
		_overPoly = false;
	}

	public void Process( MeshTile tile, Span<long> refs, int count )
	{
		for ( int i = 0; i < count; ++i )
		{
			long polyRef = refs[i];
			float d;

			// Find nearest polygon amongst the nearby polygons.
			_query.ClosestPointOnPoly( polyRef, _center, out var closestPtPoly, out var posOverPoly );

			// If a point is directly over a polygon and closer than
			// climb height, favor that instead of straight line nearest point.
			Vector3 diff = _center - closestPtPoly;
			if ( posOverPoly )
			{
				d = MathF.Abs( diff.y ) - tile.data.header.walkableClimb;
				d = d > 0 ? d * d : 0;
			}
			else
			{
				d = diff.LengthSquared;
			}

			if ( d < _nearestDistanceSqr )
			{
				_nearestPoint = closestPtPoly;
				_nearestDistanceSqr = d;
				_nearestRef = polyRef;
				_overPoly = posOverPoly;
			}
		}
	}

	public long NearestRef()
	{
		return _nearestRef;
	}

	public Vector3 NearestPt()
	{
		return _nearestPoint;
	}

	public bool OverPoly()
	{
		return _overPoly;
	}
}
