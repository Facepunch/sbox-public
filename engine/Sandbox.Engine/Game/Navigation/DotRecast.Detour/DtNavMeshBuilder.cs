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

	internal static class DtNavMeshBuilder
	{
		const int MESH_NULL_IDX = 0xffff;


		private static void CalcExtends( ReadOnlySpan<BVItem> items, int imin, int imax, ref Vector3Int bmin, ref Vector3Int bmax )
		{
			bmin = items[imin].bmin;
			bmax = items[imin].bmax;

			for ( int i = imin + 1; i < imax; ++i )
			{
				BVItem it = items[i];
				if ( it.bmin.x < bmin.x )
					bmin.x = it.bmin.x;
				if ( it.bmin.y < bmin.y )
					bmin.y = it.bmin.y;
				if ( it.bmin.z < bmin.z )
					bmin.z = it.bmin.z;

				if ( it.bmax.x > bmax.x )
					bmax.x = it.bmax.x;
				if ( it.bmax.y > bmax.y )
					bmax.y = it.bmax.y;
				if ( it.bmax.z > bmax.z )
					bmax.z = it.bmax.z;
			}
		}

		private static int LongestAxis( int x, int y, int z )
		{
			int axis = 0;
			int maxVal = x;
			if ( y > maxVal )
			{
				axis = 1;
				maxVal = y;
			}

			if ( z > maxVal )
			{
				axis = 2;
				maxVal = z;
			}

			return axis;
		}

		public static int Subdivide( Span<BVItem> items, int imin, int imax, int curNode, DtBVNode[] nodes )
		{
			int inum = imax - imin;
			int icur = curNode;

			DtBVNode node = new DtBVNode();
			nodes[curNode++] = node;

			if ( inum == 1 )
			{
				// Leaf
				node.bmin = items[imin].bmin;
				node.bmax = items[imin].bmax;

				node.i = items[imin].i;
			}
			else
			{
				// Split
				CalcExtends( items, imin, imax, ref node.bmin, ref node.bmax );

				int axis = LongestAxis(
					node.bmax.x - node.bmin.x,
					node.bmax.y - node.bmin.y,
					node.bmax.z - node.bmin.z
				);

				if ( axis == 0 )
				{
					// Sort along x-axis
					items.Slice( imin, inum ).Sort( BVItemXComparer.Shared );
				}
				else if ( axis == 1 )
				{
					// Sort along y-axis
					items.Slice( imin, inum ).Sort( BVItemYComparer.Shared );
				}
				else
				{
					// Sort along z-axis
					items.Slice( imin, inum ).Sort( BVItemZComparer.Shared );
				}

				int isplit = imin + inum / 2;

				// Left
				curNode = Subdivide( items, imin, isplit, curNode, nodes );
				// Right
				curNode = Subdivide( items, isplit, imax, curNode, nodes );

				int iescape = curNode - icur;
				// Negative index means escape.
				node.i = -iescape;
			}

			return curNode;
		}

		private static int CreateBVTree( DtNavMeshCreateParams option, DtBVNode[] nodes )
		{
			// Build tree
			float quantFactor = 1 / option.cs;
			int polyCount = option.pmesh.PolyCount;

			// Pooled scratch, returned when this scope exits.
			using var itemsPooled = new PooledSpan<BVItem>( polyCount );
			Span<BVItem> items = itemsPooled.Span;

			for ( int i = 0; i < polyCount; i++ )
			{
				// BVItem is a struct, so mutate the span slot directly.
				ref BVItem it = ref items[i];
				it.i = i;

				int p = i * option.pmesh.MaxVertsPerPoly * 2;
				it.bmin.x = it.bmax.x = option.pmesh.Verts[option.pmesh.Polys[p] * 3 + 0];
				it.bmin.y = it.bmax.y = option.pmesh.Verts[option.pmesh.Polys[p] * 3 + 1];
				it.bmin.z = it.bmax.z = option.pmesh.Verts[option.pmesh.Polys[p] * 3 + 2];

				for ( int j = 1; j < option.pmesh.MaxVertsPerPoly; ++j )
				{
					if ( option.pmesh.Polys[p + j] == MESH_NULL_IDX )
						break;
					int x = option.pmesh.Verts[option.pmesh.Polys[p + j] * 3 + 0];
					int y = option.pmesh.Verts[option.pmesh.Polys[p + j] * 3 + 1];
					int z = option.pmesh.Verts[option.pmesh.Polys[p + j] * 3 + 2];

					if ( x < it.bmin.x )
						it.bmin.x = x;
					if ( y < it.bmin.y )
						it.bmin.y = y;
					if ( z < it.bmin.z )
						it.bmin.z = z;

					if ( x > it.bmax.x )
						it.bmax.x = x;
					if ( y > it.bmax.y )
						it.bmax.y = y;
					if ( z > it.bmax.z )
						it.bmax.z = z;
				}

				// Remap y
				it.bmin.y = (int)MathF.Floor( it.bmin.y * option.ch * quantFactor );
				it.bmax.y = (int)MathF.Ceiling( it.bmax.y * option.ch * quantFactor );
			}

			return Subdivide( items, 0, polyCount, 0, nodes );
		}

		const int XP = 1 << 0;
		const int ZP = 1 << 1;
		const int XM = 1 << 2;
		const int ZM = 1 << 3;

		public static int ClassifyOffMeshPoint( Vector3 pt, Vector3 bmin, Vector3 bmax )
		{
			int outcode = 0;
			outcode |= (pt.x >= bmax.x) ? XP : 0;
			outcode |= (pt.z >= bmax.z) ? ZP : 0;
			outcode |= (pt.x < bmin.x) ? XM : 0;
			outcode |= (pt.z < bmin.z) ? ZM : 0;

			switch ( outcode )
			{
				case XP:
					return 0;
				case XP | ZP:
					return 1;
				case ZP:
					return 2;
				case XM | ZP:
					return 3;
				case XM:
					return 4;
				case XM | ZM:
					return 5;
				case ZM:
					return 6;
				case XP | ZM:
					return 7;
			}

			return 0xff;
		}

		// TODO: Better error handling.

		/// @par
		/// 
		/// The output data array is allocated using the detour allocator (dtAlloc()).  The method
		/// used to free the memory will be determined by how the tile is added to the navigation
		/// mesh.
		///
		/// @see dtNavMesh, dtNavMesh::addTile()
		public static DtMeshData CreateNavMeshData( DtNavMeshCreateParams option )
		{
			if ( option.pmesh.VertCount >= 0xffff )
				return null;
			if ( option.pmesh.VertCount == 0 )
				return null;
			if ( option.pmesh.PolyCount == 0 )
				return null;

			int nvp = option.pmesh.MaxVertsPerPoly;

			// Classify off-mesh connection points. We store only the connections
			// whose start point is inside the tile. Pooled scratch, empty when there are none.
			using var offMeshConClassPooled = new PooledSpan<int>( option.offMeshConCount * 2 );
			Span<int> offMeshConClass = offMeshConClassPooled.Span;
			int storedOffMeshConCount = 0;
			int offMeshConLinkCount = 0;

			if ( option.offMeshConCount > 0 )
			{
				// Find tight heigh bounds, used for culling out off-mesh start
				// locations.
				float hmin = float.MaxValue;
				float hmax = -float.MaxValue;

				for ( int i = 0; i < option.pmesh.VertCount; ++i )
				{
					int iv = i * 3;
					float h = option.bmin.y + option.pmesh.Verts[iv + 1] * option.ch;
					hmin = Math.Min( hmin, h );
					hmax = Math.Max( hmax, h );
				}

				hmin -= option.walkableClimb;
				hmax += option.walkableClimb;
				Vector3 bmin = new Vector3();
				Vector3 bmax = new Vector3();
				bmin = option.bmin;
				bmax = option.bmax;
				bmin.y = hmin;
				bmax.y = hmax;

				for ( int i = 0; i < option.offMeshConCount; ++i )
				{
					Vector3 p0 = option.offMeshConVerts[i * 2];
					Vector3 p1 = option.offMeshConVerts[i * 2 + 1];

					offMeshConClass[i * 2 + 0] = ClassifyOffMeshPoint( p0, bmin, bmax );
					offMeshConClass[i * 2 + 1] = ClassifyOffMeshPoint( p1, bmin, bmax );

					// Zero out off-mesh start positions which are not even
					// potentially touching the mesh.
					if ( offMeshConClass[i * 2 + 0] == 0xff )
					{
						if ( p0.y < bmin.y || p0.y > bmax.y )
							offMeshConClass[i * 2 + 0] = 0;
					}

					// Count how many links should be allocated for off-mesh
					// connections.
					if ( offMeshConClass[i * 2 + 0] == 0xff )
						offMeshConLinkCount++;
					if ( offMeshConClass[i * 2 + 1] == 0xff )
						offMeshConLinkCount++;

					if ( offMeshConClass[i * 2 + 0] == 0xff )
						storedOffMeshConCount++;
				}
			}

			// Off-mesh connections are stored as polygons, adjust values.
			int totPolyCount = option.pmesh.PolyCount + storedOffMeshConCount;
			int totVertCount = option.pmesh.VertCount + storedOffMeshConCount * 2;

			// Find portal edges which are at tile borders.
			int edgeCount = 0;
			int portalCount = 0;
			for ( int i = 0; i < option.pmesh.PolyCount; ++i )
			{
				int p = i * 2 * nvp;
				for ( int j = 0; j < nvp; ++j )
				{
					if ( option.pmesh.Polys[p + j] == MESH_NULL_IDX )
						break;
					edgeCount++;

					if ( (option.pmesh.Polys[p + nvp + j] & 0x8000) != 0 )
					{
						int dir = option.pmesh.Polys[p + nvp + j] & 0xf;
						if ( dir != 0xf )
							portalCount++;
					}
				}
			}

			int maxLinkCount = edgeCount + portalCount * 2 + offMeshConLinkCount * 2;


			int bvTreeSize = option.buildBvTree ? option.pmesh.PolyCount * 2 : 0;
			DtMeshHeader header = new DtMeshHeader();
			Vector3[] navVerts = new Vector3[totVertCount];
			DtPoly[] navPolys = new DtPoly[totPolyCount];
			DtBVNode[] navBvtree = new DtBVNode[bvTreeSize];
			DtOffMeshConnection[] offMeshCons = new DtOffMeshConnection[storedOffMeshConCount];

			// Store header
			header.magic = DT_NAVMESH_MAGIC;
			header.version = DT_NAVMESH_VERSION;
			header.x = option.tileX;
			header.y = option.tileZ;
			header.layer = option.tileLayer;
			header.userId = option.userId;
			header.polyCount = totPolyCount;
			header.vertCount = totVertCount;
			header.maxLinkCount = maxLinkCount;
			header.bmin = option.bmin;
			header.bmax = option.bmax;
			header.bvQuantFactor = 1.0f / option.cs;
			header.offMeshBase = option.pmesh.PolyCount;
			header.walkableHeight = option.walkableHeight;
			header.walkableRadius = option.walkableRadius;
			header.walkableClimb = option.walkableClimb;
			header.offMeshConCount = storedOffMeshConCount;
			header.bvNodeCount = bvTreeSize;

			int offMeshVertsBase = option.pmesh.VertCount;
			int offMeshPolyBase = option.pmesh.PolyCount;

			// Store vertices
			// Mesh vertices
			for ( int i = 0; i < option.pmesh.VertCount; ++i )
			{
				int iv = i * 3;

				navVerts[i].x = option.bmin.x + option.pmesh.Verts[iv] * option.cs;
				navVerts[i].y = option.bmin.y + option.pmesh.Verts[iv + 1] * option.ch;
				navVerts[i].z = option.bmin.z + option.pmesh.Verts[iv + 2] * option.cs;
			}

			// Off-mesh link vertices.
			int n = 0;
			for ( int i = 0; i < option.offMeshConCount; ++i )
			{
				// Only store connections which start from this tile.
				if ( offMeshConClass[i * 2 + 0] == 0xff )
				{
					int linkv = i * 2;
					int v = (offMeshVertsBase + n * 2);

					navVerts[v] = option.offMeshConVerts[linkv];
					navVerts[v + 1] = option.offMeshConVerts[linkv + 1];

					n++;
				}
			}

			// Store polygons
			// Mesh polys
			int src = 0;
			for ( int i = 0; i < option.pmesh.PolyCount; ++i )
			{
				DtPoly p = new DtPoly( i, nvp );
				p.vertCount = 0;
				p.area = option.pmesh.Areas[i];
				p.type = DtPolyTypes.DT_POLYTYPE_GROUND;
				for ( int j = 0; j < nvp; ++j )
				{
					if ( option.pmesh.Polys[src + j] == MESH_NULL_IDX )
						break;
					p.verts[j] = option.pmesh.Polys[src + j];
					if ( (option.pmesh.Polys[src + nvp + j] & 0x8000) != 0 )
					{
						// Border or portal edge.
						int dir = option.pmesh.Polys[src + nvp + j] & 0xf;
						if ( dir == 0xf ) // Border
							p.neis[j] = 0;
						else if ( dir == 0 ) // Portal x-
							p.neis[j] = DT_EXT_LINK | 4;
						else if ( dir == 1 ) // Portal z+
							p.neis[j] = DT_EXT_LINK | 2;
						else if ( dir == 2 ) // Portal x+
							p.neis[j] = DT_EXT_LINK | 0;
						else if ( dir == 3 ) // Portal z-
							p.neis[j] = DT_EXT_LINK | 6;
					}
					else
					{
						// Normal connection
						p.neis[j] = option.pmesh.Polys[src + nvp + j] + 1;
					}

					p.vertCount++;
				}
				navPolys[i] = p;
				src += nvp * 2;
			}

			// Off-mesh connection vertices.
			n = 0;
			for ( int i = 0; i < option.offMeshConCount; ++i )
			{
				// Only store connections which start from this tile.
				if ( offMeshConClass[i * 2 + 0] == 0xff )
				{
					DtPoly p = new DtPoly( offMeshPolyBase + n, nvp );
					p.vertCount = 2;
					p.verts[0] = offMeshVertsBase + n * 2;
					p.verts[1] = offMeshVertsBase + n * 2 + 1;
					p.area = option.offMeshConAreas[i];
					p.type = DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION;
					navPolys[offMeshPolyBase + n] = p;
					n++;
				}
			}

			// Store and create BVtree.
			// TODO: take detail mesh into account! use byte per bbox extent?
			if ( option.buildBvTree )
			{
				// Do not set header.bvNodeCount set to make it work look exactly the same as in original Detour
				header.bvNodeCount = CreateBVTree( option, navBvtree );
			}

			// Store Off-Mesh connections.
			n = 0;
			for ( int i = 0; i < option.offMeshConCount; ++i )
			{
				// Only store connections which start from this tile.
				if ( offMeshConClass[i * 2 + 0] == 0xff )
				{
					DtOffMeshConnection con = new DtOffMeshConnection();
					offMeshCons[n] = con;
					con.poly = (offMeshPolyBase + n);
					// Copy connection end-points.
					con.startPos = option.offMeshConVerts[i * 2];
					con.endPos = option.offMeshConVerts[i * 2 + 1];

					con.rad = option.offMeshConRad[i];
					con.isBiDirectional = option.offMeshConBidirectional[i];
					con.side = offMeshConClass[i * 2 + 1];
					con.userData = option.offMeshConUserData[i];
					n++;
				}
			}

			DtMeshData nmd = new DtMeshData();
			nmd.header = header;
			nmd.verts = navVerts;
			nmd.polys = navPolys;
			nmd.bvTree = navBvtree;
			nmd.offMeshCons = offMeshCons;
			return nmd;
		}
	}
}
