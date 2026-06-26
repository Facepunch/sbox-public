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

namespace DotRecast.Detour
{
	using static DtDetour;

	/// Provides the ability to perform pathfinding related queries against
	/// a navigation mesh.
	/// @ingroup detour
	internal class DtNavMeshQuery
	{
		protected readonly DtNavMesh m_nav; //< Pointer to navmesh data.
		protected DtQueryData m_query; //< Sliced query state.

		protected readonly DtNodePool m_tinyNodePool; //< Pointer to small node pool.
		protected readonly DtNodePool m_nodePool; //< Pointer to node pool.
		protected readonly DtNodeQueue m_openList; //< Pointer to open list queue.
		protected readonly HashSet<int> m_visitedNodesCache;

		// Reused FIFO scratch for the breadth-first walks, pop = advance the head index
		private readonly List<DtNode> m_bfsStack = new();

		// Reused by FindNearestPoly
		private DtFindNearestPolyQuery m_findNearestPolyQuery;

		// Reused by QueryPolygons
		private readonly DtMeshTile[] m_tileScratch = new DtMeshTile[32];

		//////////////////////////////////////////////////////////////////////////////////////////

		/// @class dtNavMeshQuery
		///
		/// For methods that support undersized buffers, if the buffer is too small 
		/// to hold the entire result set the return status of the method will include 
		/// the #DT_BUFFER_TOO_SMALL flag.
		///
		/// Constant member functions can be used by multiple clients without side
		/// effects. (E.g. No change to the closed list. No impact on an in-progress
		/// sliced path query. Etc.)
		/// 
		/// Walls and portals: A @e wall is a polygon segment that is 
		/// considered impassable. A @e portal is a passable segment between polygons.
		/// A portal may be treated as a wall based on the dtQueryFilter used for a query.
		///
		/// @see dtNavMesh, dtQueryFilter, #dtAllocNavMeshQuery(), #dtAllocNavMeshQuery()
		public DtNavMeshQuery( DtNavMesh nav )
		{
			m_nav = nav;
			m_nodePool = new DtNodePool();
			m_openList = new DtNodeQueue();
			m_tinyNodePool = new DtNodePool();
			m_visitedNodesCache = new HashSet<int>();

		}

		/// Returns random location on navmesh.
		/// Polygons are chosen weighted by area. The search runs in linear related to number of polygon.
		///  @param[in]		filter			The polygon filter to apply to the query.
		///  @param[in]		frand			Function returning a random number [0..1).
		///  @param[out]	randomRef		The reference id of the random location.
		///  @param[out]	randomPt		The random location. 
		/// @returns The status flags for the query.
		public DtStatus FindRandomPoint( IDtQueryFilter filter, Random frand, out long randomRef, out Vector3 randomPt )
		{
			randomRef = 0;
			randomPt = Vector3.Zero;

			if ( null == filter || null == frand )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			// Randomly pick one tile. Assume that all tiles cover roughly the same area.
			DtMeshTile tile = null;
			float tsum = 0.0f;
			for ( int i = 0; i < m_nav.GetMaxTiles(); i++ )
			{
				DtMeshTile t = m_nav.GetTile( i );
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
				return DtStatus.DT_FAILURE;
			}

			// Randomly pick one polygon weighted by polygon area.
			long polyRef = 0;
			long @base = m_nav.GetPolyRefBase( tile );

			float areaSum = 0.0f;
			for ( int i = 0; i < tile.data.header.polyCount; ++i )
			{
				ref DtPoly p = ref tile.data.polys[i];
				// Do not return off-mesh connection polygons.
				if ( p.type != DtPolyTypes.DT_POLYTYPE_GROUND )
				{
					continue;
				}

				// Must pass filter
				long refs = @base | (long)i;
				if ( !filter.PassFilter( refs ) )
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
					polyArea += DtUtils.TriArea2D( tile.data.verts[va], tile.data.verts[vb], tile.data.verts[vc] );
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
				return DtStatus.DT_FAILURE;
			}

			m_nav.GetTileAndPolyByRefUnsafe( polyRef, out _, out var poly );

			// Randomly pick point on polygon.
			Span<Vector3> verts = stackalloc Vector3[poly.vertCount];

			for ( int j = 0; j < poly.vertCount; ++j )
			{
				verts[j] = tile.data.verts[poly.verts[j]];
			}

			float s = frand.NextSingle();
			float t0 = frand.NextSingle();

			DtUtils.RandomPointInConvexPoly( verts, s, t0, out var pt );
			ClosestPointOnPoly( polyRef, pt, out var closest, out var _ );

			randomRef = polyRef;
			randomPt = closest;

			return DtStatus.DT_SUCCESS;
		}

		/// Returns random location on navmesh within the reach of specified location.
		/// Polygons are chosen weighted by area. The search runs in linear related to number of polygon.
		/// The location is not exactly constrained by the circle, but it limits the visited polygons.
		///  @param[in]		startRef		The reference id of the polygon where the search starts.
		///  @param[in]		centerPos		The center of the search circle. [(x, y, z)]
		///  @param[in]		maxRadius		The radius of the search circle. [Units: wu]
		///  @param[in]		filter			The polygon filter to apply to the query.
		///  @param[in]		frand			Function returning a random number [0..1).
		///  @param[out]	randomRef		The reference id of the random location.
		///  @param[out]	randomPt		The random location. [(x, y, z)]
		/// @returns The status flags for the query.
		public DtStatus FindRandomPointAroundCircle( long startRef, Vector3 centerPos, float maxRadius,
			IDtQueryFilter filter, Random frand, out long randomRef, out Vector3 randomPt )
		{
			return FindRandomPointAroundCircle( startRef, centerPos, maxRadius, filter, frand, DtNoOpDtPolygonByCircleConstraint.Shared, out randomRef, out randomPt );
		}

		/**
	 * Returns random location on navmesh within the reach of specified location. Polygons are chosen weighted by area.
	 * The search runs in linear related to number of polygon. The location is strictly constrained by the circle.
	 *
	 * @param startRef
	 *            The reference id of the polygon where the search starts.
	 * @param centerPos
	 *            The center of the search circle. [(x, y, z)]
	 * @param maxRadius
	 * @param filter
	 *            The polygon filter to apply to the query.
	 * @param frand
	 *            Function returning a random number [0..1).
	 * @return Random location
	 */
		public DtStatus FindRandomPointWithinCircle( long startRef, Vector3 centerPos, float maxRadius,
			IDtQueryFilter filter, Random frand, out long randomRef, out Vector3 randomPt )
		{
			return FindRandomPointAroundCircle( startRef, centerPos, maxRadius, filter, frand, DtStrictDtPolygonByCircleConstraint.Shared, out randomRef, out randomPt );
		}

		/// Returns random location on navmesh within the reach of specified location.
		/// Polygons are chosen weighted by area. The search runs in linear related to number of polygon.
		/// The location is not exactly constrained by the circle, but it limits the visited polygons.
		///  @param[in]		startRef		The reference id of the polygon where the search starts.
		///  @param[in]		centerPos		The center of the search circle. [(x, y, z)]
		///  @param[in]		maxRadius		The radius of the search circle. [Units: wu]
		///  @param[in]		filter			The polygon filter to apply to the query.
		///  @param[in]		frand			Function returning a random number [0..1).
		///  @param[in]		constraint      
		///  @param[out]	randomRef		The reference id of the random location.
		///  @param[out]	randomPt		The random location. [(x, y, z)]
		/// @returns The status flags for the query.
		public DtStatus FindRandomPointAroundCircle( long startRef, Vector3 centerPos, float maxRadius,
			IDtQueryFilter filter, Random frand, IDtPolygonByCircleConstraint constraint,
			out long randomRef, out Vector3 randomPt )
		{
			randomRef = startRef;
			randomPt = centerPos;

			// Validate input
			if ( !m_nav.IsValidPolyRef( startRef ) || !DtUtils.IsFinite( centerPos ) || maxRadius < 0
				|| !float.IsFinite( maxRadius ) || null == filter || null == frand )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			if ( !filter.PassFilter( startRef ) )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			m_nodePool.Clear();
			m_openList.Clear();

			DtNode startNode = m_nodePool.GetNode( startRef );
			startNode.pos = centerPos;
			startNode.pidx = 0;
			startNode.cost = 0;
			startNode.total = 0;
			startNode.id = startRef;
			startNode.flags = DtNodeFlags.DT_NODE_OPEN;
			m_openList.Push( startNode );

			DtStatus status = DtStatus.DT_SUCCESS;

			float radiusSqr = maxRadius * maxRadius;
			float areaSum = 0.0f;

			long randomPolyRef = 0;
			Span<Vector3> randomPolyVerts = null;

			while ( !m_openList.IsEmpty() )
			{
				DtNode bestNode = m_openList.Pop();
				bestNode.flags &= ~DtNodeFlags.DT_NODE_OPEN;
				bestNode.flags |= DtNodeFlags.DT_NODE_CLOSED;

				// Get poly and tile.
				// The API input has been checked already, skip checking internal data.
				long bestRef = bestNode.id;
				m_nav.GetTileAndPolyByRefUnsafe( bestRef, out var bestTile, out var bestPoly );

				// Place random locations on on ground.
				if ( bestPoly.type == DtPolyTypes.DT_POLYTYPE_GROUND )
				{
					// Calc area of the polygon.
					float polyArea = 0.0f;
					Span<Vector3> polyVerts = new Vector3[bestPoly.vertCount];
					for ( int j = 0; j < bestPoly.vertCount; ++j )
					{
						polyVerts[j] = bestTile.data.verts[bestPoly.verts[j]];
					}

					var ncverts = constraint.Apply( polyVerts, centerPos, maxRadius, out var constrainedVerts );
					if ( !constrainedVerts.IsEmpty )
					{
						int vertCount = constrainedVerts.Length;
						for ( int j = 2; j < vertCount; ++j )
						{
							int va = 0;
							int vb = (j - 1);
							int vc = j;
							polyArea += DtUtils.TriArea2D( constrainedVerts[va], constrainedVerts[vb], constrainedVerts[vc] );
						}

						// Choose random polygon weighted by area, using reservoir sampling.
						areaSum += polyArea;
						float u = frand.NextSingle();
						if ( u * areaSum <= polyArea )
						{
							randomPolyRef = bestRef;
							randomPolyVerts = constrainedVerts;
						}
					}
				}

				// Get parent poly and tile.
				long parentRef = 0;
				if ( bestNode.pidx != 0 )
				{
					parentRef = m_nodePool.GetNodeAtIdx( bestNode.pidx ).id;
				}

				for ( int i = bestPoly.firstLink; i != DT_NULL_LINK; i = bestTile.links[i].next )
				{
					DtLink link = bestTile.links[i];
					long neighbourRef = link.refs;
					// Skip invalid neighbours and do not follow back to parent.
					if ( neighbourRef == 0 || neighbourRef == parentRef )
					{
						continue;
					}

					// Expand to neighbour
					m_nav.GetTileAndPolyByRefUnsafe( neighbourRef, out var neighbourTile, out var neighbourPoly );

					// Do not advance if the polygon is excluded by the filter.
					if ( !filter.PassFilter( neighbourRef ) )
					{
						continue;
					}

					// Find edge and calc distance to the edge.
					var ppStatus = GetPortalPoints( bestRef, ref bestPoly, bestTile, neighbourRef,
						ref neighbourPoly, neighbourTile, out var va, out var vb );
					if ( ppStatus.Failed() )
					{
						continue;
					}

					// If the circle is not touching the next polygon, skip it.
					var distSqr = DtUtils.DistancePtSegSqr2D( centerPos, va, vb, out var tesg );
					if ( distSqr > radiusSqr )
					{
						continue;
					}

					DtNode neighbourNode = m_nodePool.GetNode( neighbourRef );
					if ( null == neighbourNode )
					{
						status |= DtStatus.DT_OUT_OF_NODES;
						continue;
					}

					if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_CLOSED) != 0 )
					{
						continue;
					}

					// Cost
					if ( neighbourNode.flags == 0 )
					{
						neighbourNode.pos = Vector3.Lerp( va, vb, 0.5f );
					}

					float total = bestNode.total + Vector3.DistanceBetween( bestNode.pos, neighbourNode.pos );

					// The node is already in open list and the new result is worse, skip.
					if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_OPEN) != 0 && total >= neighbourNode.total )
					{
						continue;
					}

					neighbourNode.id = neighbourRef;
					neighbourNode.flags = (neighbourNode.flags & ~DtNodeFlags.DT_NODE_CLOSED);
					neighbourNode.pidx = m_nodePool.GetNodeIdx( bestNode );
					neighbourNode.total = total;

					if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_OPEN) != 0 )
					{
						m_openList.Modify( neighbourNode );
					}
					else
					{
						neighbourNode.flags = DtNodeFlags.DT_NODE_OPEN;
						m_openList.Push( neighbourNode );
					}
				}
			}

			if ( randomPolyRef == 0 )
			{
				return DtStatus.DT_FAILURE;
			}

			// Randomly pick point on polygon.
			float s = frand.NextSingle();
			float t = frand.NextSingle();

			DtUtils.RandomPointInConvexPoly( randomPolyVerts, s, t, out var pt );
			ClosestPointOnPoly( randomPolyRef, pt, out var closest, out var _ );

			randomRef = randomPolyRef;
			randomPt = closest;

			return status;
		}

		//////////////////////////////////////////////////////////////////////////////////////////
		/// @par
		///
		/// Uses the detail polygons to find the surface height. (Most accurate.)
		///
		/// @p pos does not have to be within the bounds of the polygon or navigation mesh.
		///
		/// See closestPointOnPolyBoundary() for a limited but faster option.
		///
		/// Finds the closest point on the specified polygon.
		///  @param[in]		ref			The reference id of the polygon.
		///  @param[in]		pos			The position to check. [(x, y, z)]
		///  @param[out]	closest		The closest point on the polygon. [(x, y, z)]
		///  @param[out]	posOverPoly	True of the position is over the polygon.
		/// @returns The status flags for the query.
		public DtStatus ClosestPointOnPoly( long refs, Vector3 pos, out Vector3 closest, out bool posOverPoly )
		{
			closest = pos;
			posOverPoly = false;

			if ( !m_nav.IsValidPolyRef( refs ) || !DtUtils.IsFinite( pos ) )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			m_nav.ClosestPointOnPoly( refs, pos, out closest, out posOverPoly );
			return DtStatus.DT_SUCCESS;
		}

		/// @par
		///
		/// Much faster than ClosestPointOnPoly().
		///
		/// If the provided position lies within the polygon's xz-bounds (above or below), 
		/// then @p pos and @p closest will be equal.
		///
		/// The height of @p closest will be the polygon boundary.  The height detail is not used.
		/// 
		/// @p pos does not have to be within the bounds of the polybon or the navigation mesh.
		/// 
		/// Returns a point on the boundary closest to the source point if the source point is outside the 
		/// polygon's xz-bounds.
		///  @param[in]		ref			The reference id to the polygon.
		///  @param[in]		pos			The position to check. [(x, y, z)]
		///  @param[out]	closest		The closest point. [(x, y, z)]
		/// @returns The status flags for the query.
		public DtStatus ClosestPointOnPolyBoundary( long refs, Vector3 pos, out Vector3 closest )
		{
			closest = pos;
			var status = m_nav.GetTileAndPolyByRef( refs, out var tile, out var poly );
			if ( status.Failed() )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			if ( tile == null || !DtUtils.IsFinite( pos ) )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
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

			if ( DtUtils.DistancePtPolyEdgesSqr( pos, verts, edged, edget ) )
			{
				closest = pos;
			}
			else
			{
				// Point is outside the polygon, dtClamp to nearest edge.
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

			return DtStatus.DT_SUCCESS;
		}

		/// @par
		///
		/// Will return #DT_FAILURE if the provided position is outside the xz-bounds
		/// of the polygon.
		///
		/// Gets the height of the polygon at the provided position using the height detail. (Most accurate.)
		/// @param[in] ref The reference id of the polygon.
		/// @param[in] pos A position within the xz-bounds of the polygon. [(x, y, z)]
		/// @param[out] height The height at the surface of the polygon.
		/// @returns The status flags for the query.
		public DtStatus GetPolyHeight( long refs, Vector3 pos, out float height )
		{
			height = default;

			var status = m_nav.GetTileAndPolyByRef( refs, out var tile, out var poly );
			if ( status.Failed() )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			if ( !DtUtils.IsFinite( pos ) )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			// We used to return success for offmesh connections, but the
			// getPolyHeight in DetourNavMesh does not do this, so special
			// case it here.
			if ( poly.type == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION )
			{
				int i = poly.verts[0];
				var v0 = tile.data.verts[i];
				i = poly.verts[1];
				var v1 = tile.data.verts[i];
				DtUtils.DistancePtSegSqr2D( pos, v0, v1, out var t );
				height = v0.y + (v1.y - v0.y) * t;

				return DtStatus.DT_SUCCESS;
			}

			if ( !m_nav.GetPolyHeight( tile, ref poly, pos, out var h ) )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			height = h;
			return DtStatus.DT_SUCCESS;
		}

		/// Finds the polygon nearest to the specified center point.
		/// [opt] means the specified parameter can be a null pointer, in that case the output parameter will not be set.
		/// 
		///  @param[in]		center		The center of the search box. [(x, y, z)]
		///  @param[in]		halfExtents	The search distance along each axis. [(x, y, z)]
		///  @param[in]		filter		The polygon filter to apply to the query.
		///  @param[out]	nearestRef	The reference id of the nearest polygon. Will be set to 0 if no polygon is found.
		///  @param[out]	nearestPt	The nearest point on the polygon. Unchanged if no polygon is found. [opt] [(x, y, z)]
		///  @param[out]	isOverPoly 	Set to true if the point's X/Z coordinate lies inside the polygon, false otherwise. Unchanged if no polygon is found. [opt]
		/// @returns The status flags for the query.
		public DtStatus FindNearestPoly( Vector3 center, Vector3 halfExtents, IDtQueryFilter filter,
			out long nearestRef, out Vector3 nearestPt, out bool isOverPoly )
		{
			nearestRef = 0;
			nearestPt = center;
			isOverPoly = false;

			// Get nearby polygons from proximity grid.
			var query = m_findNearestPolyQuery ??= new DtFindNearestPolyQuery( this, center );
			query.Init( this, center );
			DtStatus status = QueryPolygons( center, halfExtents, filter, query );
			if ( status.Failed() )
			{
				return status;
			}

			nearestRef = query.NearestRef();
			nearestPt = query.NearestPt();
			isOverPoly = query.OverPoly();

			return DtStatus.DT_SUCCESS;
		}

		/// Queries polygons within a tile.
		protected void QueryPolygonsInTile( DtMeshTile tile, Vector3 qmin, Vector3 qmax, IDtQueryFilter filter, IDtPolyQuery query )
		{
			const int batchSize = 32;
			Span<long> polyRefs = stackalloc long[batchSize];
			int n = 0;

			if ( tile.data.bvTree != null )
			{
				int nodeIndex = 0;
				int end = tile.data.header.bvNodeCount;
				var tbmin = tile.data.header.bmin;
				var tbmax = tile.data.header.bmax;
				float qfac = tile.data.header.bvQuantFactor;

				// Calculate quantized box
				Vector3Int bmin;
				Vector3Int bmax;
				// dtClamp query box to world box.
				float minx = Math.Clamp( qmin.x, tbmin.x, tbmax.x ) - tbmin.x;
				float miny = Math.Clamp( qmin.y, tbmin.y, tbmax.y ) - tbmin.y;
				float minz = Math.Clamp( qmin.z, tbmin.z, tbmax.z ) - tbmin.z;
				float maxx = Math.Clamp( qmax.x, tbmin.x, tbmax.x ) - tbmin.x;
				float maxy = Math.Clamp( qmax.y, tbmin.y, tbmax.y ) - tbmin.y;
				float maxz = Math.Clamp( qmax.z, tbmin.z, tbmax.z ) - tbmin.z;
				// Quantize
				bmin.x = (int)(qfac * minx) & 0x7ffffffe;
				bmin.y = (int)(qfac * miny) & 0x7ffffffe;
				bmin.z = (int)(qfac * minz) & 0x7ffffffe;
				bmax.x = (int)(qfac * maxx + 1) | 1;
				bmax.y = (int)(qfac * maxy + 1) | 1;
				bmax.z = (int)(qfac * maxz + 1) | 1;

				// Traverse tree
				long @base = m_nav.GetPolyRefBase( tile );
				while ( nodeIndex < end )
				{
					DtBVNode node = tile.data.bvTree[nodeIndex];
					bool overlap = DtUtils.OverlapQuantBounds( ref bmin, ref bmax, ref node.bmin, ref node.bmax );
					bool isLeafNode = node.i >= 0;

					if ( isLeafNode && overlap )
					{
						long refs = @base | (long)node.i;
						if ( filter.PassFilter( refs ) )
						{
							polyRefs[n] = refs;

							if ( n == batchSize - 1 )
							{
								query.Process( tile, polyRefs, batchSize );
								n = 0;
							}
							else
							{
								n++;
							}
						}
					}

					if ( overlap || isLeafNode )
					{
						nodeIndex++;
					}
					else
					{
						int escapeIndex = -node.i;
						nodeIndex += escapeIndex;
					}
				}
			}
			else
			{
				Vector3 bmin = new Vector3();
				Vector3 bmax = new Vector3();
				long @base = m_nav.GetPolyRefBase( tile );
				for ( int i = 0; i < tile.data.header.polyCount; ++i )
				{
					ref DtPoly p = ref tile.data.polys[i];
					// Do not return off-mesh connection polygons.
					if ( p.type == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION )
					{
						continue;
					}

					// Must pass filter
					long refs = @base | (long)i;
					if ( !filter.PassFilter( refs ) )
					{
						continue;
					}

					// Calc polygon bounds.
					int v = p.verts[0];
					bmin = tile.data.verts[v];
					bmax = tile.data.verts[v];

					for ( int j = 1; j < p.vertCount; ++j )
					{
						v = p.verts[j];
						bmin = Vector3.Min( bmin, tile.data.verts[v] );
						bmax = Vector3.Min( bmax, tile.data.verts[v] );
					}

					if ( DtUtils.OverlapBounds( qmin, qmax, bmin, bmax ) )
					{
						polyRefs[n] = refs;

						if ( n == batchSize - 1 )
						{
							query.Process( tile, polyRefs, batchSize );
							n = 0;
						}
						else
						{
							n++;
						}
					}
				}
			}

			// Process the last polygons that didn't make a full batch.
			if ( n > 0 )
			{
				query.Process( tile, polyRefs, n );
			}
		}

		/// @par 
		///
		/// If no polygons are found, the function will return #DT_SUCCESS with a
		/// @p polyCount of zero.
		///
		/// If @p polys is too small to hold the entire result set, then the array will 
		/// be filled to capacity. The method of choosing which polygons from the 
		/// full set are included in the partial result set is undefined.
		///
		/// Finds polygons that overlap the search box.
		///  @param[in]		center		The center of the search box. [(x, y, z)]
		///  @param[in]		halfExtents		The search distance along each axis. [(x, y, z)]
		///  @param[in]		filter		The polygon filter to apply to the query.
		///  @param[out]	polys		The reference ids of the polygons that overlap the query box.
		///  @param[out]	polyCount	The number of polygons in the search result.
		///  @param[in]		maxPolys	The maximum number of polygons the search result can hold.
		/// @returns The status flags for the query.
		public DtStatus QueryPolygons( Vector3 center, Vector3 halfExtents,
			IDtQueryFilter filter,
			long[] polys, out int polyCount, int maxPolys )
		{
			polyCount = 0;
			if ( null == polys || maxPolys < 0 )
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;

			DtCollectPolysQuery collector = new DtCollectPolysQuery( polys, maxPolys );
			DtStatus status = QueryPolygons( center, halfExtents, filter, collector );
			if ( status.Failed() )
				return status;

			polyCount = collector.NumCollected();
			return collector.Overflowed()
				? DtStatus.DT_SUCCESS | DtStatus.DT_BUFFER_TOO_SMALL
				: DtStatus.DT_SUCCESS;
		}

		/// @par 
		///
		/// The query will be invoked with batches of polygons. Polygons passed
		/// to the query have bounding boxes that overlap with the center and halfExtents
		/// passed to this function. The dtPolyQuery::process function is invoked multiple
		/// times until all overlapping polygons have been processed.
		///
		/// Finds polygons that overlap the search box.
		///  @param[in]		center		The center of the search box. [(x, y, z)]
		///  @param[in]		halfExtents		The search distance along each axis. [(x, y, z)]
		///  @param[in]		filter		The polygon filter to apply to the query.
		///  @param[in]		query		The query. Polygons found will be batched together and passed to this query.
		public DtStatus QueryPolygons( Vector3 center, Vector3 halfExtents, IDtQueryFilter filter, IDtPolyQuery query )
		{
			if ( !DtUtils.IsFinite( center ) || !DtUtils.IsFinite( halfExtents ) || null == filter )
			{
				return DtStatus.DT_INVALID_PARAM;
			}

			// Find tiles the query touches.
			Vector3 bmin = center - halfExtents;
			Vector3 bmax = center + halfExtents;

			// Find tiles the query touches.
			m_nav.CalcTileLoc( bmin, out var minx, out var miny );
			m_nav.CalcTileLoc( bmax, out var maxx, out var maxy );

			DtMeshTile[] neis = m_tileScratch;

			for ( int y = miny; y <= maxy; ++y )
			{
				for ( int x = minx; x <= maxx; ++x )
				{
					int nneis = m_nav.GetTilesAt( x, y, neis, neis.Length );
					for ( int j = 0; j < nneis; ++j )
					{
						QueryPolygonsInTile( neis[j], bmin, bmax, filter, query );
					}
				}
			}

			return DtStatus.DT_SUCCESS;
		}

		/// @par
		///
		/// If the end polygon cannot be reached through the navigation graph,
		/// the last polygon in the path will be the nearest the end polygon.
		///
		/// If the path array is to small to hold the full result, it will be filled as 
		/// far as possible from the start polygon toward the end polygon.
		///
		/// The start and end positions are used to calculate traversal costs. 
		/// (The y-values impact the result.)
		///
		/// @name Standard Pathfinding Functions
		/// @{
		/// Finds a path from the start polygon to the end polygon.
		///  @param[in]		startRef	The reference id of the start polygon.
		///  @param[in]		endRef		The reference id of the end polygon.
		///  @param[in]		startPos	A position within the start polygon. [(x, y, z)]
		///  @param[in]		endPos		A position within the end polygon. [(x, y, z)]
		///  @param[in]		filter		The polygon filter to apply to the query.
		///  @param[out]	path		An ordered list of polygon references representing the path. (Start to end.) 
		///  							[(polyRef) * @p pathCount]
		///  @param[out]	pathCount	The number of polygons returned in the @p path array.
		///  @param[in]		maxPath		The maximum number of polygons the @p path array can hold. [Limit: >= 1]
		public DtStatus FindPath( long startRef, long endRef, Vector3 startPos, Vector3 endPos, IDtQueryFilter filter, ref List<long> path, DtFindPathOption fpo )
		{
			if ( null == path )
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;

			path.Clear();

			// Validate input
			if ( !m_nav.IsValidPolyRef( startRef ) || !m_nav.IsValidPolyRef( endRef ) || !DtUtils.IsFinite( startPos ) || !DtUtils.IsFinite( endPos ) || null == filter )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			var heuristic = fpo.heuristic;
			var raycastLimit = fpo.raycastLimit;
			var options = fpo.options;

			float raycastLimitSqr = raycastLimit * raycastLimit;

			// trade quality with performance?
			if ( (options & DtFindPathOptions.DT_FINDPATH_ANY_ANGLE) != 0 && raycastLimit < 0f )
			{
				// limiting to several times the character radius yields nice results. It is not sensitive
				// so it is enough to compute it from the first tile.
				DtMeshTile tile = m_nav.GetTileByRef( startRef );
				float agentRadius = tile.data.header.walkableRadius;
				raycastLimitSqr = (agentRadius * DT_RAY_CAST_LIMIT_PROPORTIONS) * (agentRadius * DT_RAY_CAST_LIMIT_PROPORTIONS);
			}

			if ( startRef == endRef )
			{
				path.Add( startRef );
				return DtStatus.DT_SUCCESS;
			}

			m_nodePool.Clear();
			m_openList.Clear();

			DtNode startNode = m_nodePool.GetNode( startRef );
			startNode.pos = startPos;
			startNode.pidx = 0;
			startNode.cost = 0;
			startNode.total = heuristic.GetCost( startPos, endPos );
			startNode.id = startRef;
			startNode.flags = DtNodeFlags.DT_NODE_OPEN;
			m_openList.Push( startNode );

			DtNode lastBestNode = startNode;
			float lastBestNodeCost = startNode.total;

			DtRaycastHit rayHit = new DtRaycastHit();
			rayHit.path = new List<long>();
			while ( !m_openList.IsEmpty() )
			{
				// Remove node from open list and put it in closed list.
				DtNode bestNode = m_openList.Pop();
				bestNode.flags &= ~DtNodeFlags.DT_NODE_OPEN;
				bestNode.flags |= DtNodeFlags.DT_NODE_CLOSED;

				// Reached the goal, stop searching.
				if ( bestNode.id == endRef )
				{
					lastBestNode = bestNode;
					break;
				}

				// Get current poly and tile.
				// The API input has been checked already, skip checking internal data.
				long bestRef = bestNode.id;
				m_nav.GetTileAndPolyByRefUnsafe( bestRef, out var bestTile, out var bestPoly );

				// Get parent poly and tile.
				long parentRef = 0, grandpaRef = 0;
				DtMeshTile parentTile = null;
				DtPoly parentPoly;
				DtNode parentNode = null;
				if ( bestNode.pidx != 0 )
				{
					parentNode = m_nodePool.GetNodeAtIdx( bestNode.pidx );
					parentRef = parentNode.id;
					if ( parentNode.pidx != 0 )
					{
						grandpaRef = m_nodePool.GetNodeAtIdx( parentNode.pidx ).id;
					}
				}

				if ( parentRef != 0 )
				{
					m_nav.GetTileAndPolyByRefUnsafe( parentRef, out parentTile, out parentPoly );
				}

				// decide whether to test raycast to previous nodes
				bool tryLOS = false;
				if ( (options & DtFindPathOptions.DT_FINDPATH_ANY_ANGLE) != 0 )
				{
					if ( (parentRef != 0) &&
						(raycastLimitSqr >= float.MaxValue || Vector3.DistanceBetweenSquared( parentNode.pos, bestNode.pos ) < raycastLimitSqr) )
					{
						tryLOS = true;
					}
				}

				for ( int i = bestPoly.firstLink; i != DT_NULL_LINK; i = bestTile.links[i].next )
				{
					long neighbourRef = bestTile.links[i].refs;

					// Skip invalid ids and do not expand back to where we came from.
					if ( neighbourRef == 0 || neighbourRef == parentRef )
					{
						continue;
					}

					if ( !filter.PassFilter( neighbourRef ) )
					{
						continue;
					}

					// get the node
					DtNode neighbourNode = m_nodePool.GetNode( neighbourRef, 0 );

					// do not expand to nodes that were already visited from the
					// same parent
					if ( neighbourNode.pidx != 0 && neighbourNode.pidx == bestNode.pidx )
					{
						continue;
					}

					// Get neighbour poly and tile.
					// The API input has been checked already, skip checking internal data.
					m_nav.GetTileAndPolyByRefUnsafe( neighbourRef, out var neighbourTile, out var neighbourPoly );

					// If the node is visited the first time, calculate node position.
					var neighbourPos = neighbourNode.pos;
					var empStatus = neighbourRef == endRef
						? GetEdgeIntersectionPoint( bestNode.pos, bestRef, ref bestPoly, bestTile,
							endPos, neighbourRef, ref neighbourPoly, neighbourTile,
							ref neighbourPos )
						: GetEdgeMidPoint( bestRef, ref bestPoly, bestTile,
							neighbourRef, ref neighbourPoly, neighbourTile,
							ref neighbourPos );

					// Calculate cost and heuristic.
					float cost = 0;
					float heuristicCost = 0;

					// raycast parent
					bool foundShortCut = false;
					List<long> shortcut = null;
					if ( tryLOS )
					{
						var rayStatus = Raycast( parentRef, parentNode.pos, neighbourPos, filter,
							DtRaycastOptions.DT_RAYCAST_USE_COSTS, ref rayHit, grandpaRef );
						if ( rayStatus.Succeeded() )
						{
							foundShortCut = rayHit.t >= 1.0f && rayHit.path[^1] == neighbourRef;
							if ( foundShortCut )
							{
								shortcut = new List<long>( rayHit.path );
								// shortcut found using raycast. Using shorter cost
								// instead
								cost = parentNode.cost + rayHit.pathCost;
							}
						}
					}

					// update move cost
					if ( !foundShortCut )
					{
						float curCost = filter.GetCost( bestNode.pos, neighbourPos, parentRef, bestRef, neighbourRef );
						cost = bestNode.cost + curCost;
					}

					// Special case for last node.
					if ( neighbourRef == endRef )
					{
						// Cost
						float endCost = filter.GetCost( neighbourPos, endPos, bestRef, neighbourRef, 0L );
						cost = cost + endCost;
					}
					else
					{
						// Cost
						heuristicCost = heuristic.GetCost( neighbourPos, endPos );
					}

					float total = cost + heuristicCost;

					// The node is already in open list and the new result is worse, skip.
					if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_OPEN) != 0 && total >= neighbourNode.total )
					{
						continue;
					}

					// The node is already visited and process, and the new result is worse, skip.
					if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_CLOSED) != 0 && total >= neighbourNode.total )
					{
						continue;
					}

					// Add or update the node.
					neighbourNode.pidx = foundShortCut ? bestNode.pidx : m_nodePool.GetNodeIdx( bestNode );
					neighbourNode.id = neighbourRef;
					neighbourNode.flags = (neighbourNode.flags & ~DtNodeFlags.DT_NODE_CLOSED);
					neighbourNode.cost = cost;
					neighbourNode.total = total;
					neighbourNode.pos = neighbourPos;
					neighbourNode.shortcut = shortcut;

					if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_OPEN) != 0 )
					{
						// Already in open, update node location.
						m_openList.Modify( neighbourNode );
					}
					else
					{
						// Put the node in open list.
						neighbourNode.flags |= DtNodeFlags.DT_NODE_OPEN;
						m_openList.Push( neighbourNode );
					}

					// Update nearest node to target so far.
					if ( heuristicCost < lastBestNodeCost )
					{
						lastBestNodeCost = heuristicCost;
						lastBestNode = neighbourNode;
					}
				}
			}

			var status = GetPathToNode( lastBestNode, ref path );
			if ( lastBestNode.id != endRef )
			{
				status |= DtStatus.DT_PARTIAL_RESULT;
			}

			return status;
		}

		///@}
		/// @name Sliced Pathfinding Functions
		/// Common use case:
		///	-# Call initSlicedFindPath() to initialize the sliced path query.
		///	-# Call updateSlicedFindPath() until it returns complete.
		///	-# Call finalizeSlicedFindPath() to get the path.
		///@{ 
		/// Initializes a sliced path query.
		///  @param[in]		startRef	The reference id of the start polygon.
		///  @param[in]		endRef		The reference id of the end polygon.
		///  @param[in]		startPos	A position within the start polygon. [(x, y, z)]
		///  @param[in]		endPos		A position within the end polygon. [(x, y, z)]
		///  @param[in]		filter		The polygon filter to apply to the query.
		///  @param[in]		options		query options (see: #dtFindPathOptions)
		/// @returns The status flags for the query.
		public DtStatus InitSlicedFindPath( long startRef, long endRef, Vector3 startPos, Vector3 endPos, IDtQueryFilter filter, int options )
		{
			return InitSlicedFindPath( startRef, endRef, startPos, endPos, filter, options, DtDefaultQueryHeuristic.Default, -1.0f );
		}

		public DtStatus InitSlicedFindPath( long startRef, long endRef, Vector3 startPos, Vector3 endPos, IDtQueryFilter filter, int options, float raycastLimit )
		{
			return InitSlicedFindPath( startRef, endRef, startPos, endPos, filter, options, DtDefaultQueryHeuristic.Default, raycastLimit );
		}

		public DtStatus InitSlicedFindPath( long startRef, long endRef, Vector3 startPos, Vector3 endPos, IDtQueryFilter filter, int options, IDtQueryHeuristic heuristic, float raycastLimit )
		{
			// Init path state.
			m_query = new DtQueryData();
			m_query.status = DtStatus.DT_FAILURE;
			m_query.startRef = startRef;
			m_query.endRef = endRef;
			m_query.startPos = startPos;
			m_query.endPos = endPos;
			m_query.filter = filter;
			m_query.options = options;
			m_query.heuristic = heuristic;
			m_query.raycastLimitSqr = raycastLimit * raycastLimit;

			// Validate input
			if ( !m_nav.IsValidPolyRef( startRef ) || !m_nav.IsValidPolyRef( endRef ) || !DtUtils.IsFinite( startPos ) || !DtUtils.IsFinite( endPos ) || null == filter )
			{
				return DtStatus.DT_INVALID_PARAM;
			}

			// trade quality with performance?
			if ( (options & DtFindPathOptions.DT_FINDPATH_ANY_ANGLE) != 0 && raycastLimit < 0f )
			{
				// limiting to several times the character radius yields nice results. It is not sensitive
				// so it is enough to compute it from the first tile.
				DtMeshTile tile = m_nav.GetTileByRef( startRef );
				float agentRadius = tile.data.header.walkableRadius;
				m_query.raycastLimitSqr = (agentRadius * DT_RAY_CAST_LIMIT_PROPORTIONS) * (agentRadius * DT_RAY_CAST_LIMIT_PROPORTIONS);
			}

			if ( startRef == endRef )
			{
				m_query.status = DtStatus.DT_SUCCESS;
				return DtStatus.DT_SUCCESS;
			}

			m_nodePool.Clear();
			m_openList.Clear();

			DtNode startNode = m_nodePool.GetNode( startRef );
			startNode.pos = startPos;
			startNode.pidx = 0;
			startNode.cost = 0;
			startNode.total = heuristic.GetCost( startPos, endPos );
			startNode.id = startRef;
			startNode.flags = DtNodeFlags.DT_NODE_OPEN;
			m_openList.Push( startNode );

			m_query.status = DtStatus.DT_IN_PROGRESS;
			m_query.lastBestNode = startNode;
			m_query.lastBestNodeCost = startNode.total;

			return m_query.status;
		}

		/// Updates an in-progress sliced path query.
		///  @param[in]		maxIter		The maximum number of iterations to perform.
		///  @param[out]	doneIters	The actual number of iterations completed. [opt]
		/// @returns The status flags for the query.
		public virtual DtStatus UpdateSlicedFindPath( int maxIter, out int doneIters )
		{
			doneIters = 0;
			if ( !m_query.status.InProgress() )
			{
				return m_query.status;
			}

			// Make sure the request is still valid.
			if ( !m_nav.IsValidPolyRef( m_query.startRef ) || !m_nav.IsValidPolyRef( m_query.endRef ) )
			{
				m_query.status = DtStatus.DT_FAILURE;
				return DtStatus.DT_FAILURE;
			}

			var rayHit = new DtRaycastHit();
			rayHit.path = new List<long>();

			int iter = 0;
			while ( iter < maxIter && !m_openList.IsEmpty() )
			{
				iter++;

				// Remove node from open list and put it in closed list.
				DtNode bestNode = m_openList.Pop();
				bestNode.flags &= ~DtNodeFlags.DT_NODE_OPEN;
				bestNode.flags |= DtNodeFlags.DT_NODE_CLOSED;

				// Reached the goal, stop searching.
				if ( bestNode.id == m_query.endRef )
				{
					m_query.lastBestNode = bestNode;
					var details = m_query.status & DtStatus.DT_STATUS_DETAIL_MASK;
					m_query.status = DtStatus.DT_SUCCESS | details;
					doneIters = iter;
					return m_query.status;
				}

				// Get current poly and tile.
				// The API input has been checked already, skip checking internal
				// data.
				long bestRef = bestNode.id;
				var status = m_nav.GetTileAndPolyByRef( bestRef, out var bestTile, out var bestPoly );
				if ( status.Failed() )
				{
					// The polygon has disappeared during the sliced query, fail.
					m_query.status = DtStatus.DT_FAILURE;
					doneIters = iter;
					return m_query.status;
				}

				// Get parent and grand parent poly and tile.
				long parentRef = 0, grandpaRef = 0;
				DtMeshTile parentTile = null;
				DtPoly parentPoly;
				DtNode parentNode = null;
				if ( bestNode.pidx != 0 )
				{
					parentNode = m_nodePool.GetNodeAtIdx( bestNode.pidx );
					parentRef = parentNode.id;
					if ( parentNode.pidx != 0 )
					{
						grandpaRef = m_nodePool.GetNodeAtIdx( parentNode.pidx ).id;
					}
				}

				if ( parentRef != 0 )
				{
					bool invalidParent = false;
					status = m_nav.GetTileAndPolyByRef( parentRef, out parentTile, out parentPoly );
					invalidParent = status.Failed();
					if ( invalidParent || (grandpaRef != 0 && !m_nav.IsValidPolyRef( grandpaRef )) )
					{
						// The polygon has disappeared during the sliced query fail.
						m_query.status = DtStatus.DT_FAILURE;
						doneIters = iter;
						return m_query.status;
					}
				}

				// decide whether to test raycast to previous nodes
				bool tryLOS = false;
				if ( (m_query.options & DtFindPathOptions.DT_FINDPATH_ANY_ANGLE) != 0 )
				{
					if ( (parentRef != 0) &&
						(m_query.raycastLimitSqr >= float.MaxValue || Vector3.DistanceBetweenSquared( parentNode.pos, bestNode.pos ) < m_query.raycastLimitSqr) )
					{
						tryLOS = true;
					}
				}

				for ( int i = bestPoly.firstLink; i != DT_NULL_LINK; i = bestTile.links[i].next )
				{
					long neighbourRef = bestTile.links[i].refs;

					// Skip invalid ids and do not expand back to where we came
					// from.
					if ( neighbourRef == 0 || neighbourRef == parentRef )
					{
						continue;
					}

					// Get neighbour poly and tile.
					// The API input has been checked already, skip checking internal
					// data.
					m_nav.GetTileAndPolyByRefUnsafe( neighbourRef, out var neighbourTile, out var neighbourPoly );

					if ( !m_query.filter.PassFilter( neighbourRef ) )
					{
						continue;
					}

					int crossSide = 0; // See https://github.com/recastnavigation/recastnavigation/issues/438

					if ( bestTile.links[i].side != 0xff )
						crossSide = bestTile.links[i].side >> 1;

					// get the neighbor node
					DtNode neighbourNode = m_nodePool.GetNode( neighbourRef, crossSide );

					// do not expand to nodes that were already visited from the
					// same parent
					if ( neighbourNode.pidx != 0 && neighbourNode.pidx == bestNode.pidx )
					{
						continue;
					}

					// If the node is visited the first time, calculate node
					// position.
					var neighbourPos = neighbourNode.pos;
					var empStatus = neighbourRef == m_query.endRef
						? GetEdgeIntersectionPoint( bestNode.pos, bestRef, ref bestPoly, bestTile,
							m_query.endPos, neighbourRef, ref neighbourPoly, neighbourTile,
							ref neighbourPos )
						: GetEdgeMidPoint( bestRef, ref bestPoly, bestTile,
							neighbourRef, ref neighbourPoly, neighbourTile,
							ref neighbourPos );

					// Calculate cost and heuristic.
					float cost = 0;
					float heuristic = 0;

					// raycast parent
					bool foundShortCut = false;
					List<long> shortcut = null;
					if ( tryLOS )
					{
						status = Raycast( parentRef, parentNode.pos, neighbourPos, m_query.filter,
							DtRaycastOptions.DT_RAYCAST_USE_COSTS, ref rayHit, grandpaRef );
						if ( status.Succeeded() )
						{
							foundShortCut = rayHit.t >= 1.0f && rayHit.path[^1] == neighbourRef;
							if ( foundShortCut )
							{
								shortcut = new List<long>( rayHit.path );
								// shortcut found using raycast. Using shorter cost
								// instead
								cost = parentNode.cost + rayHit.pathCost;
							}
						}
					}


					// update move cost
					if ( !foundShortCut )
					{
						// No shortcut found.
						float curCost = m_query.filter.GetCost( bestNode.pos, neighbourPos, parentRef, bestRef, neighbourRef );
						cost = bestNode.cost + curCost;
					}

					// Special case for last node.
					if ( neighbourRef == m_query.endRef )
					{
						float endCost = m_query.filter.GetCost( neighbourPos, m_query.endPos, bestRef, neighbourRef, 0 );

						cost = cost + endCost;
						heuristic = 0;
					}
					else
					{
						heuristic = m_query.heuristic.GetCost( neighbourPos, m_query.endPos );
					}

					float total = cost + heuristic;

					// The node is already in open list and the new result is worse,
					// skip.
					if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_OPEN) != 0 && total >= neighbourNode.total )
					{
						continue;
					}

					// The node is already visited and process, and the new result
					// is worse, skip.
					if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_CLOSED) != 0 && total >= neighbourNode.total )
					{
						continue;
					}

					// Add or update the node.
					neighbourNode.pidx = foundShortCut ? bestNode.pidx : m_nodePool.GetNodeIdx( bestNode );
					neighbourNode.id = neighbourRef;
					neighbourNode.flags = (neighbourNode.flags & ~DtNodeFlags.DT_NODE_CLOSED);
					neighbourNode.cost = cost;
					neighbourNode.total = total;
					neighbourNode.pos = neighbourPos;
					neighbourNode.shortcut = shortcut;

					if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_OPEN) != 0 )
					{
						// Already in open, update node location.
						m_openList.Modify( neighbourNode );
					}
					else
					{
						// Put the node in open list.
						neighbourNode.flags |= DtNodeFlags.DT_NODE_OPEN;
						m_openList.Push( neighbourNode );
					}

					// Update nearest node to target so far.
					if ( heuristic < m_query.lastBestNodeCost )
					{
						m_query.lastBestNodeCost = heuristic;
						m_query.lastBestNode = neighbourNode;
					}
				}
			}

			// Exhausted all nodes, but could not find path.
			if ( m_openList.IsEmpty() )
			{
				var details = m_query.status & DtStatus.DT_STATUS_DETAIL_MASK;
				m_query.status = DtStatus.DT_SUCCESS | details;
			}

			doneIters = iter;
			return m_query.status;
		}

		/// Finalizes and returns the results of a sliced path query.
		///  @param[out]	path		An ordered list of polygon references representing the path. (Start to end.) 
		///  							[(polyRef) * @p pathCount]
		///  @param[out]	pathCount	The number of polygons returned in the @p path array.
		///  @param[in]		maxPath		The max number of polygons the path array can hold. [Limit: >= 1]
		/// @returns The status flags for the query.
		public virtual DtStatus FinalizeSlicedFindPath( ref List<long> path )
		{
			if ( null == path )
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;

			path.Clear();

			if ( m_query.status.Failed() )
			{
				// Reset query.
				m_query = new DtQueryData();
				return DtStatus.DT_FAILURE;
			}

			if ( m_query.startRef == m_query.endRef )
			{
				// Special case: the search starts and ends at same poly.
				path.Add( m_query.startRef );
			}
			else
			{
				// Reverse the path.
				if ( m_query.lastBestNode.id != m_query.endRef )
				{
					m_query.status |= DtStatus.DT_PARTIAL_RESULT;
				}

				GetPathToNode( m_query.lastBestNode, ref path );
			}

			var details = m_query.status & DtStatus.DT_STATUS_DETAIL_MASK;

			// Reset query.
			m_query = new DtQueryData();

			return DtStatus.DT_SUCCESS | details;
		}

		/// Finalizes and returns the results of an incomplete sliced path query, returning the path to the furthest
		/// polygon on the existing path that was visited during the search.
		///  @param[in]		existing		An array of polygon references for the existing path.
		///  @param[in]		existingSize	The number of polygon in the @p existing array.
		///  @param[out]	path			An ordered list of polygon references representing the path. (Start to end.) 
		///  								[(polyRef) * @p pathCount]
		///  @param[out]	pathCount		The number of polygons returned in the @p path array.
		///  @param[in]		maxPath			The max number of polygons the @p path array can hold. [Limit: >= 1]
		/// @returns The status flags for the query.
		public virtual DtStatus FinalizeSlicedFindPathPartial( List<long> existing, int existingSize, ref List<long> path )
		{
			if ( null == path )
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;

			path.Clear();

			if ( null == existing || existingSize <= 0 )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			if ( m_query.status.Failed() )
			{
				// Reset query.
				m_query = new DtQueryData();
				return DtStatus.DT_FAILURE;
			}

			if ( m_query.startRef == m_query.endRef )
			{
				// Special case: the search starts and ends at same poly.
				path.Add( m_query.startRef );
			}
			else
			{
				// Find furthest existing node that was visited.
				DtNode node = null;
				for ( int i = existingSize - 1; i >= 0; --i )
				{
					node = m_nodePool.FindNode( existing[i] );
					if ( node != null )
					{
						break;
					}
				}

				if ( node == null )
				{
					m_query.status |= DtStatus.DT_PARTIAL_RESULT;
					node = m_query.lastBestNode;
				}

				GetPathToNode( node, ref path );
			}

			var details = m_query.status & DtStatus.DT_STATUS_DETAIL_MASK;

			// Reset query.
			m_query = new DtQueryData();

			return DtStatus.DT_SUCCESS | details;
		}

		protected DtStatus AppendVertex( Vector3 pos, byte flags, long refs, Span<DtStraightPath> straightPath, ref int straightPathCount, int maxStraightPath )
		{
			if ( straightPathCount > 0 && straightPath[straightPathCount - 1].pos.AlmostEqual( pos ) )
			{
				// The vertices are equal, update flags and poly.
				straightPath[straightPathCount - 1] = new DtStraightPath( straightPath[straightPathCount - 1].pos, flags, refs );
			}
			else
			{
				// Append new vertex.
				straightPath[straightPathCount] = new DtStraightPath( pos, flags, refs );
				straightPathCount++;

				// If there is no space to append more vertices, return.
				if ( straightPathCount >= maxStraightPath )
				{
					return DtStatus.DT_SUCCESS | DtStatus.DT_BUFFER_TOO_SMALL;
				}

				// If reached end of path, return.
				if ( flags == DtStraightPathFlags.DT_STRAIGHTPATH_END )
				{
					return DtStatus.DT_SUCCESS;
				}
			}

			return DtStatus.DT_IN_PROGRESS;
		}

		protected DtStatus AppendPortals( int startIdx, int endIdx, Vector3 endPos, List<long> path,
			Span<DtStraightPath> straightPath, ref int straightPathCount, int maxStraightPath, int options )
		{
			var startPos = straightPath[straightPathCount - 1].pos;
			// Append or update last vertex
			DtStatus stat;
			for ( int i = startIdx; i < endIdx; i++ )
			{
				// Calculate portal
				long from = path[i];
				var status = m_nav.GetTileAndPolyByRef( from, out var fromTile, out var fromPoly );
				if ( status.Failed() )
				{
					return DtStatus.DT_FAILURE;
				}

				long to = path[i + 1];
				status = m_nav.GetTileAndPolyByRef( to, out var toTile, out var toPoly );
				if ( status.Failed() )
				{
					return DtStatus.DT_FAILURE;
				}

				var ppStatus = GetPortalPoints( from, ref fromPoly, fromTile, to, ref toPoly, toTile, out var left, out var right );
				if ( ppStatus.Failed() )
				{
					break;
				}

				if ( (options & DtStraightPathOptions.DT_STRAIGHTPATH_AREA_CROSSINGS) != 0 )
				{
					// Skip intersection if only area crossings are requested.
					if ( fromPoly.area == toPoly.area )
					{
						continue;
					}
				}

				// Append intersection
				if ( DtUtils.IntersectSegSeg2D( startPos, endPos, left, right, out var _, out var t ) )
				{
					var pt = Vector3.Lerp( left, right, t );
					stat = AppendVertex( pt, 0, path[i + 1], straightPath, ref straightPathCount, maxStraightPath );
					if ( !stat.InProgress() )
					{
						return stat;
					}
				}
			}

			return DtStatus.DT_IN_PROGRESS;
		}

		/// @par
		/// 
		/// This method peforms what is often called 'string pulling'.
		///
		/// The start position is clamped to the first polygon in the path, and the 
		/// end position is clamped to the last. So the start and end positions should 
		/// normally be within or very near the first and last polygons respectively.
		///
		/// The returned polygon references represent the reference id of the polygon 
		/// that is entered at the associated path position. The reference id associated 
		/// with the end point will always be zero.  This allows, for example, matching 
		/// off-mesh link points to their representative polygons.
		///
		/// If the provided result buffers are too small for the entire result set, 
		/// they will be filled as far as possible from the start toward the end 
		/// position.
		///
		/// Finds the straight path from the start to the end position within the polygon corridor.
		///  @param[in]		startPos			Path start position. [(x, y, z)]
		///  @param[in]		endPos				Path end position. [(x, y, z)]
		///  @param[in]		path				An array of polygon references that represent the path corridor.
		///  @param[in]		pathSize			The number of polygons in the @p path array.
		///  @param[out]	straightPath		Points describing the straight path. [(x, y, z) * @p straightPathCount].
		///  @param[in]		maxStraightPath		The maximum number of points the straight path arrays can hold.  [Limit: > 0]
		///  @param[in]		options				Query options. (see: #dtStraightPathOptions)
		/// @returns The status flags for the query.
		public virtual DtStatus FindStraightPath( Vector3 startPos, Vector3 endPos,
			List<long> path, int pathSize,
			Span<DtStraightPath> straightPath, out int straightPathCount, int maxStraightPath,
			int options )
		{
			straightPathCount = 0;

			if ( !DtUtils.IsFinite( startPos ) || !DtUtils.IsFinite( endPos ) ||
				straightPath.IsEmpty ||
				null == path || pathSize <= 0 || path[0] == 0
				|| maxStraightPath <= 0 )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			DtStatus stat = DtStatus.DT_STATUS_NOTHING;

			// TODO: Should this be callers responsibility?
			var closestStartPosRes = ClosestPointOnPolyBoundary( path[0], startPos, out var closestStartPos );
			if ( closestStartPosRes.Failed() )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			var closestEndPosRes = ClosestPointOnPolyBoundary( path[pathSize - 1], endPos, out var closestEndPos );
			if ( closestEndPosRes.Failed() )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			// Add start point.
			stat = AppendVertex( closestStartPos, DtStraightPathFlags.DT_STRAIGHTPATH_START, path[0], straightPath, ref straightPathCount, maxStraightPath );
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
								return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
							}

							// Append portals along the current straight path segment.
							if ( (options & (DtStraightPathOptions.DT_STRAIGHTPATH_AREA_CROSSINGS | DtStraightPathOptions.DT_STRAIGHTPATH_ALL_CROSSINGS)) != 0 )
							{
								// Ignore status return value as we're just about to return anyway.
								AppendPortals( apexIndex, i, closestEndPos, path, straightPath, ref straightPathCount, maxStraightPath, options );
							}

							// Ignore status return value as we're just about to return anyway.
							AppendVertex( closestEndPos, 0, path[i], straightPath, ref straightPathCount, maxStraightPath );

							return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM | (straightPathCount >= maxStraightPath ? DtStatus.DT_BUFFER_TOO_SMALL : DtStatus.DT_STATUS_NOTHING);
						}

						// If starting really close the portal, advance.
						if ( i == 0 )
						{
							var distSqr = DtUtils.DistancePtSegSqr2D( portalApex, left, right, out var t );
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
						toType = DtPolyTypes.DT_POLYTYPE_GROUND;
					}

					// Right vertex.
					if ( DtUtils.TriArea2D( portalApex, portalRight, right ) <= 0.0f )
					{
						if ( portalApex.AlmostEqual( portalRight ) || right.AlmostEqual( portalRight ) || DtUtils.TriArea2D( portalApex, portalLeft, right ) >= 0.0f )
						{
							portalRight = right;
							rightPolyRef = (i + 1 < pathSize) ? path[i + 1] : 0;
							rightPolyType = toType;
							rightIndex = i;
						}
						else
						{
							// Append portals along the current straight path segment.
							if ( (options & (DtStraightPathOptions.DT_STRAIGHTPATH_AREA_CROSSINGS | DtStraightPathOptions.DT_STRAIGHTPATH_ALL_CROSSINGS)) != 0 )
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
								flags = DtStraightPathFlags.DT_STRAIGHTPATH_END;
							}
							else if ( leftPolyType == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION )
							{
								flags = DtStraightPathFlags.DT_STRAIGHTPATH_OFFMESH_CONNECTION;
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
					if ( DtUtils.TriArea2D( portalApex, portalLeft, left ) >= 0.0f )
					{
						if ( portalApex.AlmostEqual( portalLeft ) || left.AlmostEqual( portalLeft ) || DtUtils.TriArea2D( portalApex, portalRight, left ) <= 0.0f )
						{
							portalLeft = left;
							leftPolyRef = (i + 1 < pathSize) ? path[i + 1] : 0;
							leftPolyType = toType;
							leftIndex = i;
						}
						else
						{
							// Append portals along the current straight path segment.
							if ( (options & (DtStraightPathOptions.DT_STRAIGHTPATH_AREA_CROSSINGS | DtStraightPathOptions.DT_STRAIGHTPATH_ALL_CROSSINGS)) != 0 )
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
								flags = DtStraightPathFlags.DT_STRAIGHTPATH_END;
							}
							else if ( rightPolyType == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION )
							{
								flags = DtStraightPathFlags.DT_STRAIGHTPATH_OFFMESH_CONNECTION;
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
				if ( (options & (DtStraightPathOptions.DT_STRAIGHTPATH_AREA_CROSSINGS | DtStraightPathOptions.DT_STRAIGHTPATH_ALL_CROSSINGS)) != 0 )
				{
					stat = AppendPortals( apexIndex, pathSize - 1, closestEndPos, path, straightPath, ref straightPathCount, maxStraightPath, options );
					if ( !stat.InProgress() )
					{
						return stat;
					}
				}
			}

			// Ignore status return value as we're just about to return anyway.
			AppendVertex( closestEndPos, DtStraightPathFlags.DT_STRAIGHTPATH_END, 0, straightPath, ref straightPathCount, maxStraightPath );
			return DtStatus.DT_SUCCESS | (straightPathCount >= maxStraightPath ? DtStatus.DT_BUFFER_TOO_SMALL : DtStatus.DT_STATUS_NOTHING);
		}

		/// @par
		///
		/// This method is optimized for small delta movement and a small number of 
		/// polygons. If used for too great a distance, the result set will form an 
		/// incomplete path.
		///
		/// @p resultPos will equal the @p endPos if the end is reached. 
		/// Otherwise the closest reachable position will be returned.
		/// 
		/// @p resultPos is not projected onto the surface of the navigation 
		/// mesh. Use #getPolyHeight if this is needed.
		///
		/// This method treats the end position in the same manner as 
		/// the #raycast method. (As a 2D point.) See that method's documentation 
		/// for details.
		/// 
		/// If the @p visited array is too small to hold the entire result set, it will 
		/// be filled as far as possible from the start position toward the end 
		/// position.
		///
		/// Moves from the start to the end position constrained to the navigation mesh.
		///  @param[in]		startRef		The reference id of the start polygon.
		///  @param[in]		startPos		A position of the mover within the start polygon. [(x, y, x)]
		///  @param[in]		endPos			The desired end position of the mover. [(x, y, z)]
		///  @param[in]		filter			The polygon filter to apply to the query.
		///  @param[out]	resultPos		The result position of the mover. [(x, y, z)]
		///  @param[out]	visited			The reference ids of the polygons visited during the move.
		///  @param[out]	visitedCount	The number of polygons visited during the move.
		///  @param[in]		maxVisitedSize	The maximum number of polygons the @p visited array can hold.
		/// @returns The status flags for the query.
		public DtStatus MoveAlongSurface( long startRef, Vector3 startPos, Vector3 endPos,
			IDtQueryFilter filter,
			out Vector3 resultPos, Span<long> visited, out int visitedCount, int maxVisitedSize )
		{
			resultPos = Vector3.Zero;

			visitedCount = 0;

			// Validate input
			if ( !m_nav.IsValidPolyRef( startRef ) || !DtUtils.IsFinite( startPos )
												|| !DtUtils.IsFinite( endPos ) || null == filter )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			DtStatus status = DtStatus.DT_SUCCESS;

			m_tinyNodePool.Clear();

			DtNode startNode = m_tinyNodePool.GetNode( startRef );
			startNode.pidx = 0;
			startNode.cost = 0;
			startNode.total = 0;
			startNode.id = startRef;
			startNode.flags = DtNodeFlags.DT_NODE_CLOSED;
			m_bfsStack.Clear();
			int bfsHead = 0;
			m_bfsStack.Add( startNode );

			Vector3 bestPos = new Vector3();
			float bestDist = float.MaxValue;
			DtNode bestNode = null;
			bestPos = startPos;

			// Search constraints
			var searchPos = Vector3.Lerp( startPos, endPos, 0.5f );
			float searchRad = Vector3.DistanceBetween( startPos, endPos ) / 2.0f + 0.001f;
			float searchRadSqr = searchRad * searchRad;

			Span<Vector3> verts = stackalloc Vector3[m_nav.GetMaxVertsPerPoly()];

			const int MAX_NEIS = 8;
			Span<long> neis = stackalloc long[MAX_NEIS];

			while ( bfsHead < m_bfsStack.Count )
			{
				// Pop front.
				DtNode curNode = m_bfsStack[bfsHead++];

				// Get poly and tile.
				// The API input has been checked already, skip checking internal data.
				long curRef = curNode.id;
				m_nav.GetTileAndPolyByRefUnsafe( curRef, out var curTile, out var curPoly );

				// Collect vertices.
				int nverts = curPoly.vertCount;
				for ( int i = 0; i < nverts; ++i )
				{
					verts[i] = curTile.data.verts[curPoly.verts[i]];
				}

				// If target is inside the poly, stop search.
				if ( DtUtils.PointInPolygon( endPos, verts.Slice( 0, nverts ) ) )
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

					if ( (curPoly.neis[j] & DT_EXT_LINK) != 0 )
					{
						// Tile border.
						for ( int k = curPoly.firstLink; k != DT_NULL_LINK; k = curTile.links[k].next )
						{
							DtLink link = curTile.links[k];
							if ( link.edge == j )
							{
								if ( link.refs != 0 )
								{
									if ( filter.PassFilter( link.refs ) )
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
						long refs = m_nav.GetPolyRefBase( curTile ) | (long)idx;
						if ( filter.PassFilter( refs ) )
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
						var distSqr = DtUtils.DistancePtSegSqr2D( endPos, verts[vj], verts[vi], out var tseg );
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
							DtNode neighbourNode = m_tinyNodePool.GetNode( neis[k] );
							// Skip if already visited.
							if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_CLOSED) != 0 )
							{
								continue;
							}

							// Skip the link if it is too far from search constraint.
							// TODO: Maybe should use GetPortalPoints(), but this one is way faster.
							int vj = j;
							int vi = i;
							var distSqr = DtUtils.DistancePtSegSqr2D( searchPos, verts[vj], verts[vi], out var _ );
							if ( distSqr > searchRadSqr )
							{
								continue;
							}

							// Mark as the node as visited and push to queue.
							neighbourNode.pidx = m_tinyNodePool.GetNodeIdx( curNode );
							neighbourNode.flags |= DtNodeFlags.DT_NODE_CLOSED;
							m_bfsStack.Add( neighbourNode );
						}
					}
				}
			}

			int n = 0;
			if ( bestNode != null )
			{
				// Reverse the path.
				DtNode prev = null;
				DtNode node = bestNode;
				do
				{
					DtNode next = m_tinyNodePool.GetNodeAtIdx( node.pidx );
					node.pidx = m_tinyNodePool.GetNodeIdx( prev );
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
						status |= DtStatus.DT_BUFFER_TOO_SMALL;
						;
						break;
					}

					node = m_tinyNodePool.GetNodeAtIdx( node.pidx );
				} while ( node != null );
			}

			resultPos = bestPos;
			visitedCount = n;

			return status;
		}

		protected DtStatus GetPortalPoints( long from, long to, out Vector3 left, out Vector3 right, out int fromType, out int toType )
		{
			left = Vector3.Zero;
			right = Vector3.Zero;
			fromType = 0;
			toType = 0;

			var status = m_nav.GetTileAndPolyByRef( from, out var fromTile, out var fromPoly );
			if ( status.Failed() )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			fromType = fromPoly.type;

			status = m_nav.GetTileAndPolyByRef( to, out var toTile, out var toPoly );
			if ( status.Failed() )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			toType = toPoly.type;

			return GetPortalPoints( from, ref fromPoly, fromTile, to, ref toPoly, toTile, out left, out right );
		}

		// Returns portal points between two polygons.
		protected DtStatus GetPortalPoints( long from, ref DtPoly fromPoly, DtMeshTile fromTile,
			long to, ref DtPoly toPoly, DtMeshTile toTile,
			out Vector3 left, out Vector3 right )
		{
			left = Vector3.Zero;
			right = Vector3.Zero;

			// Find the link that points to the 'to' polygon.
			DtLink link = null;
			for ( int i = fromPoly.firstLink; i != DT_NULL_LINK; i = fromTile.links[i].next )
			{
				if ( fromTile.links[i].refs == to )
				{
					link = fromTile.links[i];
					break;
				}
			}

			if ( link == null )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			// Handle off-mesh connections.
			if ( fromPoly.type == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION )
			{
				// Find link that points to first vertex.
				for ( int i = fromPoly.firstLink; i != DT_NULL_LINK; i = fromTile.links[i].next )
				{
					if ( fromTile.links[i].refs == to )
					{
						int v = fromTile.links[i].edge;

						left = fromTile.data.verts[fromPoly.verts[v]];

						right = fromTile.data.verts[fromPoly.verts[v]];

						return DtStatus.DT_SUCCESS;
					}
				}

				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			if ( toPoly.type == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION )
			{
				for ( int i = toPoly.firstLink; i != DT_NULL_LINK; i = toTile.links[i].next )
				{
					if ( toTile.links[i].refs == from )
					{
						int v = toTile.links[i].edge;

						left = toTile.data.verts[toPoly.verts[v]];

						right = toTile.data.verts[toPoly.verts[v]];

						return DtStatus.DT_SUCCESS;
					}
				}

				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			// Find portal vertices.
			int v0 = fromPoly.verts[link.edge];
			int v1 = fromPoly.verts[(link.edge + 1) % fromPoly.vertCount];

			left = fromTile.data.verts[v0];
			right = fromTile.data.verts[v1];

			// If the link is at tile boundary, dtClamp the vertices to
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

			return DtStatus.DT_SUCCESS;
		}

		protected DtStatus GetEdgeMidPoint( long from, ref DtPoly fromPoly, DtMeshTile fromTile, long to,
			ref DtPoly toPoly, DtMeshTile toTile, ref Vector3 mid )
		{
			var ppStatus = GetPortalPoints( from, ref fromPoly, fromTile, to, ref toPoly, toTile, out var left, out var right );
			if ( ppStatus.Failed() )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			mid.x = (left.x + right.x) * 0.5f;
			mid.y = (left.y + right.y) * 0.5f;
			mid.z = (left.z + right.z) * 0.5f;

			return DtStatus.DT_SUCCESS;
		}

		protected DtStatus GetEdgeIntersectionPoint( Vector3 fromPos, long from, ref DtPoly fromPoly, DtMeshTile fromTile,
			Vector3 toPos, long to, ref DtPoly toPoly, DtMeshTile toTile,
			ref Vector3 pt )
		{
			var ppStatus = GetPortalPoints( from, ref fromPoly, fromTile, to, ref toPoly, toTile, out var left, out var right );
			if ( ppStatus.Failed() )
			{
				return DtStatus.DT_FAILURE;
			}

			float t = 0.5f;
			if ( DtUtils.IntersectSegSeg2D( fromPos, toPos, left, right, out var _, out var t2 ) )
			{
				t = Math.Clamp( t2, 0.1f, 0.9f );
			}

			pt = Vector3.Lerp( left, right, t );
			return DtStatus.DT_SUCCESS;
		}

		/// @par
		///
		/// This method is meant to be used for quick, short distance checks.
		///
		/// If the path array is too small to hold the result, it will be filled as 
		/// far as possible from the start postion toward the end position.
		///
		/// <b>Using the Hit Parameter t of RaycastHit</b>
		/// 
		/// If the hit parameter is a very high value (FLT_MAX), then the ray has hit 
		/// the end position. In this case the path represents a valid corridor to the 
		/// end position and the value of @p hitNormal is undefined.
		///
		/// If the hit parameter is zero, then the start position is on the wall that 
		/// was hit and the value of @p hitNormal is undefined.
		///
		/// If 0 &lt; t &lt; 1.0 then the following applies:
		///
		/// @code
		/// distanceToHitBorder = distanceToEndPosition * t
		/// hitPoint = startPos + (endPos - startPos) * t
		/// @endcode
		///
		/// <b>Use Case Restriction</b>
		///
		/// The raycast ignores the y-value of the end position. (2D check.) This 
		/// places significant limits on how it can be used. For example:
		///
		/// Consider a scene where there is a main floor with a second floor balcony 
		/// that hangs over the main floor. So the first floor mesh extends below the 
		/// balcony mesh. The start position is somewhere on the first floor. The end 
		/// position is on the balcony.
		///
		/// The raycast will search toward the end position along the first floor mesh. 
		/// If it reaches the end position's xz-coordinates it will indicate FLT_MAX
		/// (no wall hit), meaning it reached the end position. This is one example of why
		/// this method is meant for short distance checks.
		///
		/// Casts a 'walkability' ray along the surface of the navigation mesh from 
		/// the start position toward the end position.
		///  @param[in]		startRef	The reference id of the start polygon.
		///  @param[in]		startPos	A position within the start polygon representing 
		///  							the start of the ray. [(x, y, z)]
		///  @param[in]		endPos		The position to cast the ray toward. [(x, y, z)]
		///  @param[in]		filter		The polygon filter to apply to the query.
		///  @param[in]		options		govern how the raycast behaves. See dtRaycastOptions
		///  @param[out]	hit			Pointer to a raycast hit structure which will be filled by the results.
		///  @param[in]		prevRef		parent of start ref. Used during for cost calculation [opt]
		/// @returns The status flags for the query.
		public DtStatus Raycast( long startRef, Vector3 startPos, Vector3 endPos,
			IDtQueryFilter filter, int options,
			ref DtRaycastHit hit, long prevRef )
		{
			// Validate input
			if ( !m_nav.IsValidPolyRef( startRef ) || !DtUtils.IsFinite( startPos ) || !DtUtils.IsFinite( endPos )
				|| null == filter || (prevRef != 0 && !m_nav.IsValidPolyRef( prevRef )) )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			hit.t = 0;
			hit.path.Clear();
			hit.pathCost = 0;

			Span<Vector3> verts = stackalloc Vector3[m_nav.GetMaxVertsPerPoly() + 1];

			Vector3 curPos = Vector3.Zero;
			Vector3 lastPos = Vector3.Zero;

			curPos = startPos;
			Vector3 dir = endPos - startPos;
			hit.hitNormal = Vector3.Zero;

			DtMeshTile prevTile, tile, nextTile;
			DtPoly prevPoly, poly, nextPoly;

			// The API input has been checked already, skip checking internal data.
			long curRef = startRef;
			m_nav.GetTileAndPolyByRefUnsafe( curRef, out tile, out poly );
			nextTile = prevTile = tile;
			nextPoly = prevPoly = poly;
			if ( prevRef != 0 )
			{
				m_nav.GetTileAndPolyByRefUnsafe( prevRef, out prevTile, out prevPoly );
			}

			while ( curRef != 0 )
			{
				// Cast ray against current polygon.

				// Collect vertices.
				for ( int i = 0; i < poly.vertCount; ++i )
				{
					verts[i] = tile.data.verts[poly.verts[i]];
				}

				bool intersects = DtUtils.IntersectSegmentPoly2D( startPos, endPos, verts.Slice( 0, poly.vertCount ), out var tmin, out var tmax, out var segMin, out var segMax );
				if ( !intersects )
				{
					// Could not hit the polygon, keep the old t and report hit.
					return DtStatus.DT_SUCCESS;
				}

				hit.hitEdgeIndex = segMax;

				// Keep track of furthest t so far.
				if ( tmax > hit.t )
				{
					hit.t = tmax;
				}

				// Store visited polygons.
				hit.path.Add( curRef );

				// Ray end is completely inside the polygon.
				if ( segMax == -1 )
				{
					hit.t = float.MaxValue;

					// add the cost
					if ( (options & DtRaycastOptions.DT_RAYCAST_USE_COSTS) != 0 )
					{
						hit.pathCost += filter.GetCost( curPos, endPos, prevRef, curRef, curRef );
					}

					return DtStatus.DT_SUCCESS;
				}

				// Follow neighbours.
				long nextRef = 0;

				for ( int i = poly.firstLink; i != DT_NULL_LINK; i = tile.links[i].next )
				{
					DtLink link = tile.links[i];

					// Find link which contains this edge.
					if ( link.edge != segMax )
					{
						continue;
					}

					m_nav.GetTileAndPolyByRefUnsafe( link.refs, out nextTile, out nextPoly );

					// Skip off-mesh connections.
					if ( nextPoly.type == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION )
					{
						continue;
					}

					// Skip links based on filter.
					if ( !filter.PassFilter( link.refs ) )
					{
						continue;
					}

					// If the link is internal, just return the ref.
					if ( link.side == 0xff )
					{
						nextRef = link.refs;
						break;
					}

					// If the link is at tile boundary,

					// Check if the link spans the whole edge, and accept.
					if ( link.bmin == 0 && link.bmax == 255 )
					{
						nextRef = link.refs;
						break;
					}

					// Check for partial edge links.
					int v0 = poly.verts[link.edge];
					int v1 = poly.verts[(link.edge + 1) % poly.vertCount];
					int left = v0;
					int right = v1;

					// Check that the intersection lies inside the link portal.
					if ( link.side == 0 || link.side == 4 )
					{
						// Calculate link size.
						const float s = 1.0f / 255.0f;
						float lmin = tile.data.verts[left].z
									 + (tile.data.verts[right].z - tile.data.verts[left].z) * (link.bmin * s);
						float lmax = tile.data.verts[left].z
									 + (tile.data.verts[right].z - tile.data.verts[left].z) * (link.bmax * s);
						if ( lmin > lmax )
						{
							(lmin, lmax) = (lmax, lmin);
						}

						// Find Z intersection.
						float z = startPos.z + (endPos.z - startPos.z) * tmax;
						if ( z >= lmin && z <= lmax )
						{
							nextRef = link.refs;
							break;
						}
					}
					else if ( link.side == 2 || link.side == 6 )
					{
						// Calculate link size.
						const float s = 1.0f / 255.0f;
						float lmin = tile.data.verts[left].x
									 + (tile.data.verts[right].x - tile.data.verts[left].x) * (link.bmin * s);
						float lmax = tile.data.verts[left].x
									 + (tile.data.verts[right].x - tile.data.verts[left].x) * (link.bmax * s);
						if ( lmin > lmax )
						{
							(lmin, lmax) = (lmax, lmin);
						}

						// Find X intersection.
						float x = startPos.x + (endPos.x - startPos.x) * tmax;
						if ( x >= lmin && x <= lmax )
						{
							nextRef = link.refs;
							break;
						}
					}
				}

				// add the cost
				if ( (options & DtRaycastOptions.DT_RAYCAST_USE_COSTS) != 0 )
				{
					// compute the intersection point at the furthest end of the polygon
					// and correct the height (since the raycast moves in 2d)
					lastPos = curPos;
					curPos = startPos + dir * hit.t;
					var e1 = verts[segMax];
					var e2 = verts[(segMax + 1) % poly.vertCount];
					var eDir = e2 - e1;
					var diff = curPos - e1;
					float s = eDir.x * eDir.x > eDir.z * eDir.z ? diff.x / eDir.x : diff.z / eDir.z;
					curPos.y = e1.y + eDir.y * s;

					hit.pathCost += filter.GetCost( lastPos, curPos, prevRef, curRef, nextRef );
				}

				if ( nextRef == 0 )
				{
					// No neighbour, we hit a wall.

					// Calculate hit normal.
					int a = segMax;
					int b = segMax + 1 < poly.vertCount ? segMax + 1 : 0;

					float dx = verts[b].x - verts[a].x;
					float dz = verts[b].z - verts[a].z;
					hit.hitNormal = new Vector3( dz, 0, -dx ).Normal;
					return DtStatus.DT_SUCCESS;
				}

				// No hit, advance to neighbour polygon.
				prevRef = curRef;
				curRef = nextRef;
				prevTile = tile;
				tile = nextTile;
				prevPoly = poly;
				poly = nextPoly;
			}

			return DtStatus.DT_SUCCESS;
		}

		/// @par
		///
		/// At least one result array must be provided.
		///
		/// The order of the result set is from least to highest cost to reach the polygon.
		///
		/// A common use case for this method is to perform Dijkstra searches.
		/// Candidate polygons are found by searching the graph beginning at the start polygon.
		///
		/// If a polygon is not found via the graph search, even if it intersects the
		/// search circle, it will not be included in the result set. For example:
		///
		/// polyA is the start polygon.
		/// polyB shares an edge with polyA. (Is adjacent.)
		/// polyC shares an edge with polyB, but not with polyA
		/// Even if the search circle overlaps polyC, it will not be included in the
		/// result set unless polyB is also in the set.
		///
		/// The value of the center point is used as the start position for cost
		/// calculations. It is not projected onto the surface of the mesh, so its
		/// y-value will effect the costs.
		///
		/// Intersection tests occur in 2D. All polygons and the search circle are
		/// projected onto the xz-plane. So the y-value of the center point does not
		/// effect intersection tests.
		///
		/// If the result arrays are to small to hold the entire result set, they will be
		/// filled to capacity.
		///
		///@}
		/// @name Dijkstra Search Functions
		/// @{ 
		/// Finds the polygons along the navigation graph that touch the specified circle.
		///  @param[in]		startRef		The reference id of the polygon where the search starts.
		///  @param[in]		centerPos		The center of the search circle. [(x, y, z)]
		///  @param[in]		radius			The radius of the search circle.
		///  @param[in]		filter			The polygon filter to apply to the query.
		///  @param[out]	resultRef		The reference ids of the polygons touched by the circle. [opt]
		///  @param[out]	resultParent	The reference ids of the parent polygons for each result. 
		///  								Zero if a result polygon has no parent. [opt]
		///  @param[out]	resultCost		The search cost from @p centerPos to the polygon. [opt]
		///  @param[out]	resultCount		The number of polygons found. [opt]
		///  @param[in]		maxResult		The maximum number of polygons the result arrays can hold.
		/// @returns The status flags for the query.
		public DtStatus FindPolysAroundCircle( long startRef, Vector3 centerPos, float radius, IDtQueryFilter filter,
			ref List<long> resultRef, ref List<long> resultParent, ref List<float> resultCost )
		{
			if ( null != resultRef )
			{
				resultRef.Clear();
				resultParent.Clear();
				resultCost.Clear();
			}

			// Validate input
			if ( !m_nav.IsValidPolyRef( startRef ) || !DtUtils.IsFinite( centerPos ) || radius < 0
				|| !float.IsFinite( radius ) || null == filter )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			m_nodePool.Clear();
			m_openList.Clear();

			DtNode startNode = m_nodePool.GetNode( startRef );
			startNode.pos = centerPos;
			startNode.pidx = 0;
			startNode.cost = 0;
			startNode.total = 0;
			startNode.id = startRef;
			startNode.flags = DtNodeFlags.DT_NODE_OPEN;
			m_openList.Push( startNode );

			float radiusSqr = radius * radius;

			while ( !m_openList.IsEmpty() )
			{
				DtNode bestNode = m_openList.Pop();
				bestNode.flags &= ~DtNodeFlags.DT_NODE_OPEN;
				bestNode.flags |= DtNodeFlags.DT_NODE_CLOSED;

				// Get poly and tile.
				// The API input has been checked already, skip checking internal data.
				long bestRef = bestNode.id;
				m_nav.GetTileAndPolyByRefUnsafe( bestRef, out var bestTile, out var bestPoly );

				// Get parent poly and tile.
				long parentRef = 0;
				DtMeshTile parentTile = null;
				DtPoly parentPoly;
				if ( bestNode.pidx != 0 )
				{
					parentRef = m_nodePool.GetNodeAtIdx( bestNode.pidx ).id;
				}

				if ( parentRef != 0 )
				{
					m_nav.GetTileAndPolyByRefUnsafe( parentRef, out parentTile, out parentPoly );
				}

				resultRef.Add( bestRef );
				resultParent.Add( parentRef );
				resultCost.Add( bestNode.total );

				for ( int i = bestPoly.firstLink; i != DT_NULL_LINK; i = bestTile.links[i].next )
				{
					DtLink link = bestTile.links[i];
					long neighbourRef = link.refs;
					// Skip invalid neighbours and do not follow back to parent.
					if ( neighbourRef == 0 || neighbourRef == parentRef )
					{
						continue;
					}


					// Do not advance if the polygon is excluded by the filter.
					if ( !filter.PassFilter( neighbourRef ) )
					{
						continue;
					}

					// Expand to neighbour
					m_nav.GetTileAndPolyByRefUnsafe( neighbourRef, out var neighbourTile, out var neighbourPoly );

					// Find edge and calc distance to the edge.
					var ppStatus = GetPortalPoints( bestRef, ref bestPoly, bestTile, neighbourRef, ref neighbourPoly,
						neighbourTile, out var va, out var vb );
					if ( ppStatus.Failed() )
					{
						continue;
					}

					// If the circle is not touching the next polygon, skip it.
					var distSqr = DtUtils.DistancePtSegSqr2D( centerPos, va, vb, out var _ );
					if ( distSqr > radiusSqr )
					{
						continue;
					}

					DtNode neighbourNode = m_nodePool.GetNode( neighbourRef );

					if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_CLOSED) != 0 )
					{
						continue;
					}

					// Cost
					if ( neighbourNode.flags == 0 )
					{
						neighbourNode.pos = Vector3.Lerp( va, vb, 0.5f );
					}

					float cost = filter.GetCost( bestNode.pos, neighbourNode.pos, parentRef, bestRef, neighbourRef );

					float total = bestNode.total + cost;
					// The node is already in open list and the new result is worse, skip.
					if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_OPEN) != 0 && total >= neighbourNode.total )
					{
						continue;
					}

					neighbourNode.id = neighbourRef;
					neighbourNode.pidx = m_nodePool.GetNodeIdx( bestNode );
					neighbourNode.total = total;

					if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_OPEN) != 0 )
					{
						m_openList.Modify( neighbourNode );
					}
					else
					{
						neighbourNode.flags = DtNodeFlags.DT_NODE_OPEN;
						m_openList.Push( neighbourNode );
					}
				}
			}

			return DtStatus.DT_SUCCESS;
		}

		/// @par
		///
		/// The order of the result set is from least to highest cost.
		/// 
		/// At least one result array must be provided.
		///
		/// A common use case for this method is to perform Dijkstra searches. 
		/// Candidate polygons are found by searching the graph beginning at the start 
		/// polygon.
		/// 
		/// The same intersection test restrictions that apply to findPolysAroundCircle()
		/// method apply to this method.
		/// 
		/// The 3D centroid of the search polygon is used as the start position for cost 
		/// calculations.
		/// 
		/// Intersection tests occur in 2D. All polygons are projected onto the 
		/// xz-plane. So the y-values of the vertices do not effect intersection tests.
		/// 
		/// If the result arrays are is too small to hold the entire result set, they will 
		/// be filled to capacity.
		///
		/// Finds the polygons along the naviation graph that touch the specified convex polygon.
		///  @param[in]		startRef		The reference id of the polygon where the search starts.
		///  @param[in]		verts			The vertices describing the convex polygon. (CCW) 
		///  								[(x, y, z) * @p nverts]
		///  @param[in]		nverts			The number of vertices in the polygon.
		///  @param[in]		filter			The polygon filter to apply to the query.
		///  @param[out]	resultRef		The reference ids of the polygons touched by the search polygon. [opt]
		///  @param[out]	resultParent	The reference ids of the parent polygons for each result. Zero if a 
		///  								result polygon has no parent. [opt]
		///  @param[out]	resultCost		The search cost from the centroid point to the polygon. [opt]
		///  @param[out]	resultCount		The number of polygons found.
		///  @param[in]		maxResult		The maximum number of polygons the result arrays can hold.
		/// @returns The status flags for the query.
		public DtStatus FindPolysAroundShape( long startRef, Vector3[] verts, IDtQueryFilter filter,
			ref List<long> resultRef, ref List<long> resultParent, ref List<float> resultCost )
		{
			resultRef.Clear();
			resultParent.Clear();
			resultCost.Clear();

			// Validate input
			int nverts = verts.Length;
			if ( !m_nav.IsValidPolyRef( startRef ) || null == verts || nverts < 3 || null == filter )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			m_nodePool.Clear();
			m_openList.Clear();

			Vector3 centerPos = Vector3.Zero;
			for ( int i = 0; i < nverts; ++i )
			{
				centerPos += verts[i];
			}

			float scale = 1.0f / nverts;
			centerPos.x *= scale;
			centerPos.y *= scale;
			centerPos.z *= scale;

			DtNode startNode = m_nodePool.GetNode( startRef );
			startNode.pos = centerPos;
			startNode.pidx = 0;
			startNode.cost = 0;
			startNode.total = 0;
			startNode.id = startRef;
			startNode.flags = DtNodeFlags.DT_NODE_OPEN;
			m_openList.Push( startNode );

			while ( !m_openList.IsEmpty() )
			{
				DtNode bestNode = m_openList.Pop();
				bestNode.flags &= ~DtNodeFlags.DT_NODE_OPEN;
				bestNode.flags |= DtNodeFlags.DT_NODE_CLOSED;

				// Get poly and tile.
				// The API input has been checked already, skip checking internal data.
				long bestRef = bestNode.id;
				m_nav.GetTileAndPolyByRefUnsafe( bestRef, out var bestTile, out var bestPoly );

				// Get parent poly and tile.
				long parentRef = 0;
				DtMeshTile parentTile = null;
				DtPoly parentPoly;
				if ( bestNode.pidx != 0 )
				{
					parentRef = m_nodePool.GetNodeAtIdx( bestNode.pidx ).id;
				}

				if ( parentRef != 0 )
				{
					m_nav.GetTileAndPolyByRefUnsafe( parentRef, out parentTile, out parentPoly );
				}

				resultRef.Add( bestRef );
				resultParent.Add( parentRef );
				resultCost.Add( bestNode.total );

				for ( int i = bestPoly.firstLink; i != DT_NULL_LINK; i = bestTile.links[i].next )
				{
					DtLink link = bestTile.links[i];
					long neighbourRef = link.refs;
					// Skip invalid neighbours and do not follow back to parent.
					if ( neighbourRef == 0 || neighbourRef == parentRef )
					{
						continue;
					}

					// Do not advance if the polygon is excluded by the filter.
					if ( !filter.PassFilter( neighbourRef ) )
					{
						continue;
					}

					// Expand to neighbour
					m_nav.GetTileAndPolyByRefUnsafe( neighbourRef, out var neighbourTile, out var neighbourPoly );

					// Find edge and calc distance to the edge.
					var ppStatus = GetPortalPoints( bestRef, ref bestPoly, bestTile, neighbourRef, ref neighbourPoly,
						neighbourTile, out var va, out var vb );
					if ( ppStatus.Failed() )
					{
						continue;
					}

					// If the poly is not touching the edge to the next polygon, skip the connection it.
					bool intersects = DtUtils.IntersectSegmentPoly2D( va, vb, verts, out var tmin, out var tmax, out var segMin, out var segMax );
					if ( !intersects )
					{
						continue;
					}

					if ( tmin > 1.0f || tmax < 0.0f )
					{
						continue;
					}

					DtNode neighbourNode = m_nodePool.GetNode( neighbourRef );

					if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_CLOSED) != 0 )
					{
						continue;
					}

					// Cost
					if ( neighbourNode.flags == 0 )
					{
						neighbourNode.pos = Vector3.Lerp( va, vb, 0.5f );
					}

					float cost = filter.GetCost( bestNode.pos, neighbourNode.pos, parentRef, bestRef, neighbourRef );

					float total = bestNode.total + cost;

					// The node is already in open list and the new result is worse, skip.
					if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_OPEN) != 0 && total >= neighbourNode.total )
					{
						continue;
					}

					neighbourNode.id = neighbourRef;
					neighbourNode.pidx = m_nodePool.GetNodeIdx( bestNode );
					neighbourNode.total = total;

					if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_OPEN) != 0 )
					{
						m_openList.Modify( neighbourNode );
					}
					else
					{
						neighbourNode.flags = DtNodeFlags.DT_NODE_OPEN;
						m_openList.Push( neighbourNode );
					}
				}
			}

			return DtStatus.DT_SUCCESS;
		}

		/// @par
		///
		/// This method is optimized for a small search radius and small number of result 
		/// polygons.
		///
		/// Candidate polygons are found by searching the navigation graph beginning at 
		/// the start polygon.
		///
		/// The same intersection test restrictions that apply to the findPolysAroundCircle 
		/// mehtod applies to this method.
		///
		/// The value of the center point is used as the start point for cost calculations. 
		/// It is not projected onto the surface of the mesh, so its y-value will effect 
		/// the costs.
		/// 
		/// Intersection tests occur in 2D. All polygons and the search circle are 
		/// projected onto the xz-plane. So the y-value of the center point does not 
		/// effect intersection tests.
		/// 
		/// If the result arrays are is too small to hold the entire result set, they will 
		/// be filled to capacity.
		/// 
		/// Finds the non-overlapping navigation polygons in the local neighbourhood around the center position.
		///  @param[in]		startRef		The reference id of the polygon where the search starts.
		///  @param[in]		centerPos		The center of the query circle. [(x, y, z)]
		///  @param[in]		radius			The radius of the query circle.
		///  @param[in]		filter			The polygon filter to apply to the query.
		///  @param[out]	resultRef		The reference ids of the polygons touched by the circle.
		///  @param[out]	resultParent	The reference ids of the parent polygons for each result. 
		/// @returns The status flags for the query.
		public DtStatus FindLocalNeighbourhood( long startRef, Vector3 centerPos, float radius,
			IDtQueryFilter filter,
			ref List<long> resultRef, ref List<long> resultParent )
		{
			// Validate input
			if ( !m_nav.IsValidPolyRef( startRef ) || !DtUtils.IsFinite( centerPos ) || radius < 0
				|| !float.IsFinite( radius ) || null == filter
				|| null == resultRef || null == resultParent )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			resultRef.Clear();
			resultParent.Clear();

			m_tinyNodePool.Clear();

			DtNode startNode = m_tinyNodePool.GetNode( startRef );
			startNode.pidx = 0;
			startNode.id = startRef;
			startNode.flags = DtNodeFlags.DT_NODE_CLOSED;
			m_bfsStack.Clear();
			int bfsHead = 0;
			m_bfsStack.Add( startNode );

			resultRef.Add( startNode.id );
			resultParent.Add( 0L );

			float radiusSqr = radius * radius;

			Span<Vector3> pa = stackalloc Vector3[m_nav.GetMaxVertsPerPoly()];
			Span<Vector3> pb = stackalloc Vector3[m_nav.GetMaxVertsPerPoly()];

			while ( bfsHead < m_bfsStack.Count )
			{
				// Pop front.
				DtNode curNode = m_bfsStack[bfsHead++];

				// Get poly and tile.
				// The API input has been checked already, skip checking internal data.
				long curRef = curNode.id;
				m_nav.GetTileAndPolyByRefUnsafe( curRef, out var curTile, out var curPoly );

				for ( int i = curPoly.firstLink; i != DT_NULL_LINK; i = curTile.links[i].next )
				{
					DtLink link = curTile.links[i];
					long neighbourRef = link.refs;
					// Skip invalid neighbours.
					if ( neighbourRef == 0 )
					{
						continue;
					}

					DtNode neighbourNode = m_tinyNodePool.GetNode( neighbourRef );
					// Skip visited.
					if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_CLOSED) != 0 )
					{
						continue;
					}

					// Expand to neighbour
					m_nav.GetTileAndPolyByRefUnsafe( neighbourRef, out var neighbourTile, out var neighbourPoly );

					// Skip off-mesh connections.
					if ( neighbourPoly.type == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION )
					{
						continue;
					}

					// Do not advance if the polygon is excluded by the filter.
					if ( !filter.PassFilter( neighbourRef ) )
					{
						continue;
					}

					// Find edge and calc distance to the edge.
					var ppStatus = GetPortalPoints( curRef, ref curPoly, curTile, neighbourRef, ref neighbourPoly,
						neighbourTile, out var va, out var vb );
					if ( ppStatus.Failed() )
					{
						continue;
					}

					// If the circle is not touching the next polygon, skip it.
					var distSqr = DtUtils.DistancePtSegSqr2D( centerPos, va, vb, out var _ );
					if ( distSqr > radiusSqr )
					{
						continue;
					}

					// Mark node visited, this is done before the overlap test so that
					// we will not visit the poly again if the test fails.
					neighbourNode.flags |= DtNodeFlags.DT_NODE_CLOSED;
					neighbourNode.pidx = m_tinyNodePool.GetNodeIdx( curNode );

					// Check that the polygon does not collide with existing polygons.

					// Collect vertices of the neighbour poly.
					int npa = neighbourPoly.vertCount;
					for ( int k = 0; k < npa; ++k )
					{
						pa[k] = neighbourTile.data.verts[neighbourPoly.verts[k]];
					}

					bool overlap = false;
					for ( int j = 0; j < resultRef.Count; ++j )
					{
						long pastRef = resultRef[j];

						// Connected polys do not overlap.
						bool connected = false;
						for ( int k = curPoly.firstLink; k != DT_NULL_LINK; k = curTile.links[k].next )
						{
							if ( curTile.links[k].refs == pastRef )
							{
								connected = true;
								break;
							}
						}

						if ( connected )
						{
							continue;
						}

						// Potentially overlapping.
						m_nav.GetTileAndPolyByRefUnsafe( pastRef, out var pastTile, out var pastPoly );

						// Get vertices and test overlap
						int npb = pastPoly.vertCount;
						for ( int k = 0; k < npb; ++k )
						{
							pb[k] = pastTile.data.verts[pastPoly.verts[k]];
						}

						if ( DtUtils.OverlapPolyPoly2D( pa.Slice( 0, npa ), pb.Slice( 0, npb ) ) )
						{
							overlap = true;
							break;
						}
					}

					if ( overlap )
					{
						continue;
					}

					resultRef.Add( neighbourRef );
					resultParent.Add( curRef );
					m_bfsStack.Add( neighbourNode );
				}
			}

			return DtStatus.DT_SUCCESS;
		}


		protected void InsertInterval( Span<DtSegInterval> ints, ref int nints, int maxInts, int tmin, int tmax, long refs )
		{
			if ( nints + 1 > maxInts )
				return;

			// Find insertion point.
			int idx = 0;
			while ( idx < nints )
			{
				if ( tmax <= ints[idx].tmin )
				{
					break;
				}

				idx++;
			}

			// Move current results.
			if ( 0 != nints - idx )
			{
				ints.Slice( idx, nints - idx ).CopyTo( ints.Slice( idx + 1, nints - idx ) );
			}

			// Store
			ints[idx] = new DtSegInterval( refs, tmin, tmax );
			nints++;
		}

		/// @par
		///
		/// If the @p segmentRefs parameter is provided, then all polygon segments will be returned. 
		/// Otherwise only the wall segments are returned.
		/// 
		/// A segment that is normally a portal will be included in the result set as a 
		/// wall if the @p filter results in the neighbor polygon becoomming impassable.
		/// 
		/// The @p segmentVerts and @p segmentRefs buffers should normally be sized for the 
		/// maximum segments per polygon of the source navigation mesh.
		/// 
		/// Returns the segments for the specified polygon, optionally including portals.
		///  @param[in]		ref				The reference id of the polygon.
		///  @param[in]		filter			The polygon filter to apply to the query.
		///  @param[out]	segmentVerts	The segments. [(ax, ay, az, bx, by, bz) * segmentCount]
		///  @param[out]	segmentRefs		The reference ids of each segment's neighbor polygon. 
		///  								Or zero if the segment is a wall. [opt] [(parentRef) * @p segmentCount] 
		///  @param[out]	segmentCount	The number of segments returned.
		///  @param[in]		maxSegments		The maximum number of segments the result arrays can hold.
		/// @returns The status flags for the query.
		public DtStatus GetPolyWallSegments( long refs, IDtQueryFilter filter,
			Span<RcSegmentVert> segmentVerts, Span<long> segmentRefs, ref int segmentCount,
			int maxSegments )
		{
			segmentCount = 0;

			DtStatus status = m_nav.GetTileAndPolyByRef( refs, out var tile, out var poly );
			if ( status.Failed() )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			if ( null == filter )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			int n = 0;
			const int MAX_INTERVAL = 16;
			Span<DtSegInterval> ints = stackalloc DtSegInterval[MAX_INTERVAL];
			int nints;

			bool storePortals = !segmentRefs.IsEmpty;

			status = DtStatus.DT_SUCCESS;

			for ( int i = 0, j = poly.vertCount - 1; i < poly.vertCount; j = i++ )
			{
				// Skip non-solid edges.
				nints = 0;
				if ( (poly.neis[j] & DT_EXT_LINK) != 0 )
				{
					// Tile border.
					for ( int k = poly.firstLink; k != DT_NULL_LINK; k = tile.links[k].next )
					{
						DtLink link = tile.links[k];
						if ( link.edge == j )
						{
							if ( link.refs != 0 )
							{
								if ( filter.PassFilter( link.refs ) )
								{
									InsertInterval( ints, ref nints, MAX_INTERVAL, link.bmin, link.bmax, link.refs );
								}
							}
						}
					}
				}
				else
				{
					// Internal edge
					long neiRef = 0;
					if ( poly.neis[j] != 0 )
					{
						int idx = (poly.neis[j] - 1);
						neiRef = m_nav.GetPolyRefBase( tile ) | (long)idx;
						if ( !filter.PassFilter( neiRef ) )
						{
							neiRef = 0;
						}
					}

					// If the edge leads to another polygon and portals are not stored, skip.
					if ( neiRef != 0 && !storePortals )
					{
						continue;
					}

					if ( n < maxSegments )
					{
						int ivj = poly.verts[j];
						int ivi = poly.verts[i];
						var seg = new RcSegmentVert();
						seg.vmin = tile.data.verts[ivj];
						seg.vmax = tile.data.verts[ivi];

						segmentVerts[n] = seg;
						if ( !segmentRefs.IsEmpty )
						{
							segmentRefs[n] = neiRef;
						}

						n++;
					}
					else
					{
						status |= DtStatus.DT_BUFFER_TOO_SMALL;
					}

					continue;
				}

				// Add sentinels
				InsertInterval( ints, ref nints, MAX_INTERVAL, -1, 0, 0 );
				InsertInterval( ints, ref nints, MAX_INTERVAL, 255, 256, 0 );

				// Store segments.
				int vj = poly.verts[j];
				int vi = poly.verts[i];
				for ( int k = 1; k < nints; ++k )
				{
					// Portal segment.
					if ( storePortals && ints[k].refs != 0 )
					{
						float tmin = ints[k].tmin / 255.0f;
						float tmax = ints[k].tmax / 255.0f;

						if ( n < maxSegments )
						{
							var seg = new RcSegmentVert();
							seg.vmin = Vector3.Lerp( tile.data.verts[vj], tile.data.verts[vi], tmin );
							seg.vmax = Vector3.Lerp( tile.data.verts[vj], tile.data.verts[vi], tmax );
							segmentVerts[n] = seg;

							if ( !segmentRefs.IsEmpty )
							{
								segmentRefs[n] = ints[k].refs;
							}

							n++;
						}
						else
						{
							status |= DtStatus.DT_BUFFER_TOO_SMALL;
						}
					}

					// Wall segment.
					int imin = ints[k - 1].tmax;
					int imax = ints[k].tmin;
					if ( imin != imax )
					{
						float tmin = imin / 255.0f;
						float tmax = imax / 255.0f;

						if ( n < maxSegments )
						{
							var seg = new RcSegmentVert();
							seg.vmin = Vector3.Lerp( tile.data.verts[vj], tile.data.verts[vi], tmin );
							seg.vmax = Vector3.Lerp( tile.data.verts[vj], tile.data.verts[vi], tmax );
							segmentVerts[n] = seg;
							if ( !segmentRefs.IsEmpty )
							{
								segmentRefs[n] = 0;
							}

							n++;
						}
						else
						{
							status |= DtStatus.DT_BUFFER_TOO_SMALL;
						}
					}
				}
			}

			segmentCount = n;

			return status;
		}

		/// @par
		///
		/// @p hitPos is not adjusted using the height detail data.
		///
		/// @p hitDist will equal the search radius if there is no wall within the
		/// radius. In this case the values of @p hitPos and @p hitNormal are
		/// undefined.
		///
		/// The normal will become unpredicable if @p hitDist is a very small number.
		///
		/// Finds the distance from the specified position to the nearest polygon wall.
		///  @param[in]		startRef		The reference id of the polygon containing @p centerPos.
		///  @param[in]		centerPos		The center of the search circle. [(x, y, z)]
		///  @param[in]		maxRadius		The radius of the search circle.
		///  @param[in]		filter			The polygon filter to apply to the query.
		///  @param[out]	hitDist			The distance to the nearest wall from @p centerPos.
		///  @param[out]	hitPos			The nearest position on the wall that was hit. [(x, y, z)]
		///  @param[out]	hitNormal		The normalized ray formed from the wall point to the 
		///  								source point. [(x, y, z)]
		/// @returns The status flags for the query.
		public virtual DtStatus FindDistanceToWall( long startRef, Vector3 centerPos, float maxRadius,
			IDtQueryFilter filter,
			out float hitDist, out Vector3 hitPos, out Vector3 hitNormal )
		{
			hitDist = 0;
			hitPos = Vector3.Zero;
			hitNormal = Vector3.Zero;

			// Validate input
			if ( !m_nav.IsValidPolyRef( startRef ) || !DtUtils.IsFinite( centerPos ) || maxRadius < 0
				|| !float.IsFinite( maxRadius ) || null == filter )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			m_nodePool.Clear();
			m_openList.Clear();

			DtNode startNode = m_nodePool.GetNode( startRef );
			startNode.pos = centerPos;
			startNode.pidx = 0;
			startNode.cost = 0;
			startNode.total = 0;
			startNode.id = startRef;
			startNode.flags = DtNodeFlags.DT_NODE_OPEN;
			m_openList.Push( startNode );

			float radiusSqr = maxRadius * maxRadius;

			var hasBestV = false;
			var bestvj = Vector3.Zero;
			var bestvi = Vector3.Zero;

			var status = DtStatus.DT_SUCCESS;
			while ( !m_openList.IsEmpty() )
			{
				DtNode bestNode = m_openList.Pop();
				bestNode.flags &= ~DtNodeFlags.DT_NODE_OPEN;
				bestNode.flags |= DtNodeFlags.DT_NODE_CLOSED;

				// Get poly and tile.
				// The API input has been checked already, skip checking internal data.
				long bestRef = bestNode.id;
				m_nav.GetTileAndPolyByRefUnsafe( bestRef, out var bestTile, out var bestPoly );

				// Get parent poly and tile.
				long parentRef = 0;
				if ( bestNode.pidx != 0 )
				{
					parentRef = m_nodePool.GetNodeAtIdx( bestNode.pidx ).id;
				}

				// Hit test walls.
				for ( int i = 0, j = bestPoly.vertCount - 1; i < bestPoly.vertCount; j = i++ )
				{
					// Skip non-solid edges.
					if ( (bestPoly.neis[j] & DT_EXT_LINK) != 0 )
					{
						// Tile border.
						bool solid = true;
						for ( int k = bestPoly.firstLink; k != DT_NULL_LINK; k = bestTile.links[k].next )
						{
							DtLink link = bestTile.links[k];
							if ( link.edge == j )
							{
								if ( link.refs != 0 )
								{
									if ( filter.PassFilter( link.refs ) )
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
						long refs = m_nav.GetPolyRefBase( bestTile ) | (long)idx;
						if ( filter.PassFilter( refs ) )
						{
							continue;
						}
					}

					// Calc distance to the edge.
					int vj = bestPoly.verts[j];
					int vi = bestPoly.verts[i];
					var distSqr = DtUtils.DistancePtSegSqr2D( centerPos, bestTile.data.verts[vj], bestTile.data.verts[vi], out var tseg );

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

				for ( int i = bestPoly.firstLink; i != DT_NULL_LINK; i = bestTile.links[i].next )
				{
					DtLink link = bestTile.links[i];
					long neighbourRef = link.refs;
					// Skip invalid neighbours and do not follow back to parent.
					if ( neighbourRef == 0 || neighbourRef == parentRef )
					{
						continue;
					}

					// Expand to neighbour.
					m_nav.GetTileAndPolyByRefUnsafe( neighbourRef, out var neighbourTile, out var neighbourPoly );

					// Skip off-mesh connections.
					if ( neighbourPoly.type == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION )
					{
						continue;
					}

					// Calc distance to the edge.
					int va = bestPoly.verts[link.edge];
					int vb = bestPoly.verts[(link.edge + 1) % bestPoly.vertCount];
					var distSqr = DtUtils.DistancePtSegSqr2D( centerPos, bestTile.data.verts[va], bestTile.data.verts[vb], out var tseg );
					// If the circle is not touching the next polygon, skip it.
					if ( distSqr > radiusSqr )
					{
						continue;
					}

					if ( !filter.PassFilter( neighbourRef ) )
					{
						continue;
					}

					DtNode neighbourNode = m_nodePool.GetNode( neighbourRef );
					if ( null == neighbourNode )
					{
						status |= DtStatus.DT_OUT_OF_NODES;
						continue;
					}

					if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_CLOSED) != 0 )
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
					if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_OPEN) != 0 && total >= neighbourNode.total )
					{
						continue;
					}

					neighbourNode.id = neighbourRef;
					neighbourNode.flags = (neighbourNode.flags & ~DtNodeFlags.DT_NODE_CLOSED);
					neighbourNode.pidx = m_nodePool.GetNodeIdx( bestNode );
					neighbourNode.total = total;

					if ( (neighbourNode.flags & DtNodeFlags.DT_NODE_OPEN) != 0 )
					{
						m_openList.Modify( neighbourNode );
					}
					else
					{
						neighbourNode.flags |= DtNodeFlags.DT_NODE_OPEN;
						m_openList.Push( neighbourNode );
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

		/// Returns true if the polygon reference is valid and passes the filter restrictions.
		/// @param[in] ref The polygon reference to check.
		/// @param[in] filter The filter to apply.
		public bool IsValidPolyRef( long refs, IDtQueryFilter filter )
		{
			var status = m_nav.GetTileAndPolyByRef( refs, out var tile, out var poly );
			if ( status.Failed() )
			{
				return false;
			}

			// If cannot pass filter, assume flags has changed and boundary is invalid.
			if ( !filter.PassFilter( refs ) )
			{
				return false;
			}

			return true;
		}

		/// Gets the navigation mesh the query object is using.
		/// @return The navigation mesh the query object is using.
		public DtNavMesh GetAttachedNavMesh()
		{
			return m_nav;
		}

		/// Gets a path from the explored nodes in the previous search.
		///  @param[in]		endRef		The reference id of the end polygon.
		///  @param[out]	path		An ordered list of polygon references representing the path. (Start to end.)
		///  							[(polyRef) * @p pathCount]
		///  @param[out]	pathCount	The number of polygons returned in the @p path array.
		///  @param[in]		maxPath		The maximum number of polygons the @p path array can hold. [Limit: >= 0]
		///  @returns		The status flags. Returns DT_FAILURE | DT_INVALID_PARAM if any parameter is wrong, or if
		///  				@p endRef was not explored in the previous search. Returns DT_SUCCESS | DT_BUFFER_TOO_SMALL
		///  				if @p path cannot contain the entire path. In this case it is filled to capacity with a partial path.
		///  				Otherwise returns DT_SUCCESS.
		///  @remarks		The result of this function depends on the state of the query object. For that reason it should only
		///  				be used immediately after one of the two Dijkstra searches, findPolysAroundCircle or findPolysAroundShape.
		public DtStatus GetPathFromDijkstraSearch( long endRef, ref List<long> path )
		{
			if ( !m_nav.IsValidPolyRef( endRef ) || null == path )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			path.Clear();

			if ( m_nodePool.FindNodes( endRef, out var endNodes ) != 1
				|| (endNodes[0].flags & DtNodeFlags.DT_NODE_CLOSED) == 0 )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			DtNode endNode = endNodes[0];

			return GetPathToNode( endNode, ref path );
		}

		// Gets the path leading to the specified end node.
		protected DtStatus GetPathToNode( DtNode endNode, ref List<long> path )
		{
			// Reverse the path.
			DtNode curNode = endNode;

			m_visitedNodesCache.Clear();

			do
			{
				// TODO Ideally we woulnd't need this, cycles only seem to happen when the navmesh is still generating
				if ( !m_visitedNodesCache.Add( curNode.ptr ) )
				{
					// Detected a cycle in the node chain.
					return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
				}

				path.Add( curNode.id );
				DtNode nextNode = m_nodePool.GetNodeAtIdx( curNode.pidx );
				if ( curNode.shortcut != null )
				{
					// remove potential duplicates from shortcut path
					for ( int i = curNode.shortcut.Count - 1; i >= 0; i-- )
					{
						long id = curNode.shortcut[i];
						if ( id != curNode.id && id != nextNode.id )
						{
							path.Add( id );
						}
					}
				}

				curNode = nextNode;
			} while ( curNode != null );

			path.Reverse();
			return DtStatus.DT_SUCCESS;
		}

		/// @par
		///
		/// The closed list is the list of polygons that were fully evaluated during 
		/// the last navigation graph search. (A* or Dijkstra)
		/// 
		public bool IsInClosedList( long refs )
		{
			if ( m_nodePool == null )
			{
				return false;
			}

			int n = m_nodePool.FindNodes( refs, out var nodes );
			for ( int i = 0; i < n; ++i )
			{
				if ( (nodes[i].flags & DtNodeFlags.DT_NODE_CLOSED) != 0 )
				{
					return true;
				}
			}

			return false;
		}

		public DtNodePool GetNodePool()
		{
			return m_nodePool;
		}
	}
}
