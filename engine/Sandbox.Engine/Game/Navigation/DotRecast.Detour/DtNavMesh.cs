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
	using System.Runtime.InteropServices;
	using static DtDetour;

	/// A navigation mesh based on tiles of convex polygons.
	/// @ingroup detour
	internal class DtNavMesh
	{
		private DtNavMeshParams m_params; //< Current initialization params. TODO: do not store this info twice.
		private Vector3 m_orig; // < Origin of the tile (0,0)
		private float m_tileWidth; // < Dimensions of each tile.
		private float m_tileHeight; // < Dimensions of each tile.
		private int m_maxTiles; // < Max number of tiles.
		private int m_tileLutSize; //< Tile hash lookup size (must be pot).
		private int m_tileLutMask; // < Tile hash lookup mask.

		private DtMeshTile[] m_posLookup; //< Tile hash lookup.
		private DtMeshTile m_nextFree; //< Freelist of tiles.
		private DtMeshTile[] m_tiles; //< List of tiles.

		// Reused by AddTile/RemoveTile neighbour connection
		private readonly DtMeshTile[] m_neisScratch = new DtMeshTile[32];

		/** The maximum number of vertices per navigation polygon. */
		private int m_maxVertPerPoly;

		private int m_tileCount;

		public DtStatus Init( DtNavMeshParams param, int maxVertsPerPoly )
		{
			m_params = param;
			m_orig = param.orig;
			m_tileWidth = param.tileWidth;
			m_tileHeight = param.tileHeight;

			// Init tiles
			m_maxVertPerPoly = maxVertsPerPoly;
			m_maxTiles = param.maxTiles;
			m_tileLutSize = DtUtils.NextPow2( param.maxTiles );
			if ( 0 == m_tileLutSize )
				m_tileLutSize = 1;
			m_tileLutMask = m_tileLutSize - 1;

			m_tiles = new DtMeshTile[m_maxTiles];
			m_posLookup = new DtMeshTile[m_tileLutSize];
			m_nextFree = null;
			for ( int i = m_maxTiles - 1; i >= 0; --i )
			{
				m_tiles[i] = new DtMeshTile( i );
				m_tiles[i].salt = 1;
				m_tiles[i].next = m_nextFree;
				m_nextFree = m_tiles[i];
			}

			return DtStatus.DT_SUCCESS;
		}

		private static DtNavMeshParams GetNavMeshParams( DtMeshData data )
		{
			DtNavMeshParams option = new DtNavMeshParams();
			option.orig = data.header.bmin;
			option.tileWidth = data.header.bmax.x - data.header.bmin.x;
			option.tileHeight = data.header.bmax.z - data.header.bmin.z;
			option.maxTiles = 1;
			option.maxPolys = data.header.polyCount;
			return option;
		}


		/// The maximum number of tiles supported by the navigation mesh.
		/// @return The maximum number of tiles supported by the navigation mesh.
		public int GetMaxTiles()
		{
			return m_maxTiles;
		}

		/// Returns pointer to tile in the tile array.
		public DtMeshTile GetTile( int i )
		{
			return m_tiles[i];
		}

		/// Gets the polygon reference for the tile's base polygon.
		///  @param[in]	tile		The tile.
		/// @return The polygon reference for the base polygon in the specified tile.
		public long GetPolyRefBase( DtMeshTile tile )
		{
			if ( tile == null )
			{
				return 0;
			}

			int it = tile.index;
			return EncodePolyId( tile.salt, it, 0 );
		}

		private int AllocLink( DtMeshTile tile )
		{
			if ( tile.linksFreeList == DT_NULL_LINK )
				return DT_NULL_LINK;

			int linkIdx = tile.linksFreeList;
			tile.linksFreeList = tile.links[linkIdx].next;
			return linkIdx;
		}

		private void FreeLink( DtMeshTile tile, int link )
		{
			tile.links[link].next = tile.linksFreeList;
			tile.linksFreeList = link;
		}

		/// Calculates the tile grid location for the specified world position.
		///  @param[in]	pos  The world position for the query. [(x, y, z)]
		///  @param[out]	tx		The tile's x-location. (x, y)
		///  @param[out]	ty		The tile's y-location. (x, y)
		public void CalcTileLoc( Vector3 pos, out int tx, out int ty )
		{
			tx = (int)MathF.Floor( (pos.x - m_orig.x) / m_tileWidth );
			ty = (int)MathF.Floor( (pos.z - m_orig.z) / m_tileHeight );
		}

		/// Gets the tile and polygon for the specified polygon reference.
		///  @param[in]		ref		The reference for the a polygon.
		///  @param[out]	tile	The tile containing the polygon.
		///  @param[out]	poly	The polygon.
		/// @return The status flags for the operation.
		public DtStatus GetTileAndPolyByRef( long refs, out DtMeshTile tile, out DtPoly poly )
		{
			tile = null;

			if ( refs == 0 )
			{
				poly = new DtPoly( 0, 0 );
				return DtStatus.DT_FAILURE;
			}

			DecodePolyId( refs, out var salt, out var it, out var ip );
			if ( it >= m_maxTiles )
			{
				poly = new DtPoly( 0, 0 );
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			if ( m_tiles[it].salt != salt || m_tiles[it].data == null || m_tiles[it].data.header == null )
			{
				poly = new DtPoly( 0, 0 );
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			if ( ip >= m_tiles[it].data.header.polyCount )
			{
				poly = new DtPoly( 0, 0 );
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			tile = m_tiles[it];
			poly = m_tiles[it].data.polys[ip];

			return DtStatus.DT_SUCCESS;
		}

		/// @par
		///
		/// @warning Only use this function if it is known that the provided polygon
		/// reference is valid. This function is faster than #getTileAndPolyByRef,
		/// but
		/// it does not validate the reference.
		/// Returns the tile and polygon for the specified polygon reference.
		///  @param[in]		ref		A known valid reference for a polygon.
		///  @param[out]	tile	The tile containing the polygon.
		///  @param[out]	poly	The polygon.
		public void GetTileAndPolyByRefUnsafe( long refs, out DtMeshTile tile, out DtPoly poly )
		{
			DecodePolyId( refs, out var salt, out var it, out var ip );
			tile = m_tiles[it];
			poly = m_tiles[it].data.polys[ip];
		}

		/// Checks the validity of a polygon reference.
		///  @param[in]	ref		The polygon reference to check.
		/// @return True if polygon reference is valid for the navigation mesh.
		public bool IsValidPolyRef( long refs )
		{
			if ( refs == 0 )
			{
				return false;
			}

			DecodePolyId( refs, out var salt, out var it, out var ip );
			if ( it >= m_maxTiles )
			{
				return false;
			}

			if ( m_tiles[it].salt != salt || m_tiles[it].data == null )
			{
				return false;
			}

			if ( ip >= m_tiles[it].data.header.polyCount )
			{
				return false;
			}

			return true;
		}

		public ref readonly DtNavMeshParams GetParams()
		{
			return ref m_params;
		}


		// TODO: These methods are duplicates from dtNavMeshQuery, but are needed for off-mesh connection finding.

		/// Queries polygons within a tile.
		List<long> QueryPolygonsInTile( DtMeshTile tile, Vector3 qmin, Vector3 qmax )
		{
			List<long> polys = new List<long>();
			if ( tile.data.bvTree != null )
			{
				int nodeIndex = 0;
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
				long @base = GetPolyRefBase( tile );
				int end = tile.data.header.bvNodeCount;
				while ( nodeIndex < end )
				{
					DtBVNode node = tile.data.bvTree[nodeIndex];
					bool overlap = DtUtils.OverlapQuantBounds( ref bmin, ref bmax, ref node.bmin, ref node.bmax );
					bool isLeafNode = node.i >= 0;

					if ( isLeafNode && overlap )
					{
						polys.Add( @base | (long)node.i );
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

				return polys;
			}
			else
			{
				Vector3 bmin = new Vector3();
				Vector3 bmax = new Vector3();
				long @base = GetPolyRefBase( tile );
				for ( int i = 0; i < tile.data.header.polyCount; ++i )
				{
					ref DtPoly p = ref tile.data.polys[i];
					// Do not return off-mesh connection polygons.
					if ( p.type == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION )
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
						bmax = Vector3.Max( bmax, tile.data.verts[v] );
					}

					if ( DtUtils.OverlapBounds( qmin, qmax, bmin, bmax ) )
					{
						polys.Add( @base | (long)i );
					}
				}

				return polys;
			}
		}

		public DtStatus UpdateTile( DtMeshData data, int flags )
		{
			long refs = GetTileRefAt( data.header.x, data.header.y, data.header.layer );
			refs = RemoveTile( refs );
			return AddTile( data, flags, refs, out _ );
		}

		/// @par
		///
		/// The add operation will fail if the data is in the wrong format, the allocated tile
		/// space is full, or there is a tile already at the specified reference.
		///
		/// The lastRef parameter is used to restore a tile with the same tile
		/// reference it had previously used.  In this case the #dtPolyRef's for the
		/// tile will be restored to the same values they were before the tile was 
		/// removed.
		///
		/// The nav mesh assumes exclusive access to the data passed and will make
		/// changes to the dynamic portion of the data. For that reason the data
		/// should not be reused in other nav meshes until the tile has been successfully
		/// removed from this nav mesh.
		///
		/// @see dtCreateNavMeshData, #removeTile
		/// Adds a tile to the navigation mesh.
		///  @param[in]		data		Data for the new tile mesh. (See: #dtCreateNavMeshData)
		///  @param[in]		dataSize	Data size of the new tile mesh.
		///  @param[in]		flags		Tile flags. (See: #dtTileFlags)
		///  @param[in]		lastRef		The desired reference for the tile. (When reloading a tile.) [opt] [Default: 0]
		///  @param[out]	result		The tile reference. (If the tile was succesfully added.) [opt]
		/// @return The status flags for the operation. 
		public DtStatus AddTile( DtMeshData data, int flags, long lastRef, out long result )
		{
			result = 0;

			// Make sure the data is in right format.
			DtMeshHeader header = data.header;

			// Make sure the location is free.
			if ( GetTileAt( header.x, header.y, header.layer ) != null )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_ALREADY_OCCUPIED;
			}

			// Allocate a tile.
			DtMeshTile tile = null;
			if ( lastRef == 0 )
			{
				if ( null != m_nextFree )
				{
					tile = m_nextFree;
					m_nextFree = tile.next;
					tile.next = null;
					m_tileCount++;
				}
			}
			else
			{
				// Try to relocate the tile to specific index with same salt.
				int tileIndex = DecodePolyIdTile( lastRef );
				if ( tileIndex >= m_maxTiles )
				{
					return DtStatus.DT_FAILURE | DtStatus.DT_OUT_OF_MEMORY;
				}

				// Try to find the specific tile id from the free list.
				DtMeshTile target = m_tiles[tileIndex];
				DtMeshTile prev = null;
				tile = m_nextFree;

				while ( null != tile && tile != target )
				{
					prev = tile;
					tile = tile.next;
				}

				// Could not find the correct location.
				if ( tile != target )
					return DtStatus.DT_FAILURE | DtStatus.DT_OUT_OF_MEMORY;

				// Remove from freelist
				if ( null == prev )
					m_nextFree = tile.next;
				else
					prev.next = tile.next;

				// Restore salt.
				tile.salt = DecodePolyIdSalt( lastRef );
			}

			// Make sure we could allocate a tile.
			if ( null == tile )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_OUT_OF_MEMORY;
			}

			// Insert tile into the position lut.
			int h = ComputeTileHash( header.x, header.y, m_tileLutMask );
			tile.next = m_posLookup[h];
			m_posLookup[h] = tile;


			// Patch header pointers.
			tile.data = data;
			tile.links = new DtLink[data.header.maxLinkCount];
			for ( int i = 0; i < tile.links.Length; ++i )
			{
				tile.links[i] = new DtLink();
			}

			// If there are no items in the bvtree, reset the tree pointer.
			if ( tile.data.bvTree != null && tile.data.bvTree.Length == 0 )
			{
				tile.data.bvTree = null;
			}

			// Build links freelist
			tile.linksFreeList = 0;
			tile.links[data.header.maxLinkCount - 1].next = DT_NULL_LINK;
			for ( int i = 0; i < data.header.maxLinkCount - 1; ++i )
				tile.links[i].next = i + 1;

			// Init tile.
			tile.flags = flags;

			ConnectIntLinks( tile );

			// Base off-mesh connections to their starting polygons and connect connections inside the tile.
			BaseOffMeshLinks( tile );
			ConnectExtOffMeshLinks( tile, tile, -1 );

			// Create connections with neighbour tiles.
			DtMeshTile[] neis = m_neisScratch;
			int nneis;

			// Connect with layers in current tile.
			nneis = GetTilesAt( header.x, header.y, neis, neis.Length );
			for ( int j = 0; j < nneis; ++j )
			{
				if ( neis[j] == tile )
				{
					continue;
				}

				ConnectExtLinks( tile, neis[j], -1 );
				ConnectExtLinks( neis[j], tile, -1 );
				ConnectExtOffMeshLinks( tile, neis[j], -1 );
				ConnectExtOffMeshLinks( neis[j], tile, -1 );
			}

			// Connect with neighbour tiles.
			for ( int i = 0; i < 8; ++i )
			{
				nneis = GetNeighbourTilesAt( header.x, header.y, i, neis, neis.Length );
				for ( int j = 0; j < nneis; ++j )
				{
					ConnectExtLinks( tile, neis[j], i );
					ConnectExtLinks( neis[j], tile, DtUtils.OppositeTile( i ) );
					ConnectExtOffMeshLinks( tile, neis[j], i );
					ConnectExtOffMeshLinks( neis[j], tile, DtUtils.OppositeTile( i ) );
				}
			}

			result = GetTileRef( tile );
			return DtStatus.DT_SUCCESS;
		}

		/// Removes the specified tile from the navigation mesh.
		/// @param[in] ref The reference of the tile to remove.
		/// @param[out] data Data associated with deleted tile.
		/// @param[out] dataSize Size of the data associated with deleted tile.
		///
		/// This function returns the data for the tile so that, if desired,
		/// it can be added back to the navigation mesh at a later point.
		///
		/// @see #addTile
		public long RemoveTile( long refs )
		{
			if ( refs == 0 )
			{
				return 0;
			}

			int tileIndex = DecodePolyIdTile( refs );
			int tileSalt = DecodePolyIdSalt( refs );
			if ( tileIndex >= m_maxTiles )
			{
				throw new Exception( "Invalid tile index" );
			}

			DtMeshTile tile = m_tiles[tileIndex];
			if ( tile.salt != tileSalt )
			{
				throw new Exception( "Invalid tile salt" );
			}

			// Remove tile from hash lookup.
			int h = ComputeTileHash( tile.data.header.x, tile.data.header.y, m_tileLutMask );
			DtMeshTile prev = null;
			DtMeshTile cur = m_posLookup[h];
			while ( null != cur )
			{
				if ( cur == tile )
				{
					if ( null != prev )
						prev.next = cur.next;
					else
						m_posLookup[h] = cur.next;
					break;
				}

				prev = cur;
				cur = cur.next;
			}

			// Remove connections to neighbour tiles.
			DtMeshTile[] neis = m_neisScratch;
			int nneis = 0;

			// Disconnect from other layers in current tile.
			nneis = GetTilesAt( tile.data.header.x, tile.data.header.y, neis, neis.Length );
			for ( int j = 0; j < nneis; ++j )
			{
				if ( neis[j] == tile ) continue;
				UnconnectLinks( neis[j], tile );
			}

			// Disconnect from neighbour tiles.
			for ( int i = 0; i < 8; ++i )
			{
				nneis = GetNeighbourTilesAt( tile.data.header.x, tile.data.header.y, i, neis, neis.Length );
				for ( int j = 0; j < nneis; ++j )
				{
					UnconnectLinks( neis[j], tile );
				}
			}

			// Reset tile.
			tile.data = null;
			tile.flags = 0;
			tile.links = null;
			tile.linksFreeList = DT_NULL_LINK;

			// Update salt, salt should never be zero.
			tile.salt = (tile.salt + 1) & ((1 << DT_SALT_BITS) - 1);
			if ( tile.salt == 0 )
			{
				tile.salt++;
			}

			// Add to free list.
			tile.next = m_nextFree;
			m_nextFree = tile;
			m_tileCount--;
			return GetTileRef( tile );
		}

		/// Builds internal polygons links for a tile.
		void ConnectIntLinks( DtMeshTile tile )
		{
			if ( tile == null )
			{
				return;
			}

			long @base = GetPolyRefBase( tile );

			for ( int i = 0; i < tile.data.header.polyCount; ++i )
			{
				ref DtPoly poly = ref tile.data.polys[i];
				poly.firstLink = DT_NULL_LINK;

				if ( poly.type == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION )
				{
					continue;
				}

				// Build edge links backwards so that the links will be
				// in the linked list from lowest index to highest.
				for ( int j = poly.vertCount - 1; j >= 0; --j )
				{
					// Skip hard and non-internal edges.
					if ( poly.neis[j] == 0 || (poly.neis[j] & DT_EXT_LINK) != 0 )
					{
						continue;
					}

					int idx = AllocLink( tile );
					DtLink link = tile.links[idx];
					link.refs = @base | (long)(poly.neis[j] - 1);
					link.edge = (byte)j;
					link.side = 0xff;
					link.bmin = link.bmax = 0;
					// Add to linked list.
					link.next = poly.firstLink;
					poly.firstLink = idx;
				}
			}
		}

		/// Removes external links at specified side.
		void UnconnectLinks( DtMeshTile tile, DtMeshTile target )
		{
			if ( tile == null || target == null )
			{
				return;
			}

			int targetNum = DecodePolyIdTile( GetTileRef( target ) );

			for ( int i = 0; i < tile.data.header.polyCount; ++i )
			{
				ref DtPoly poly = ref tile.data.polys[i];
				int j = poly.firstLink;
				int pj = DT_NULL_LINK;
				while ( j != DT_NULL_LINK )
				{
					if ( DecodePolyIdTile( tile.links[j].refs ) == targetNum )
					{
						// Remove link.
						int nj = tile.links[j].next;
						if ( pj == DT_NULL_LINK )
						{
							poly.firstLink = nj;
						}
						else
						{
							tile.links[pj].next = nj;
						}

						FreeLink( tile, j );
						j = nj;
					}
					else
					{
						// Advance
						pj = j;
						j = tile.links[j].next;
					}
				}
			}
		}

		/// Builds external polygon links for a tile.
		void ConnectExtLinks( DtMeshTile tile, DtMeshTile target, int side )
		{
			if ( tile == null )
			{
				return;
			}

			var connectPolys = new List<DtConnectPoly>();

			// Connect border links.
			for ( int i = 0; i < tile.data.header.polyCount; ++i )
			{
				ref DtPoly poly = ref tile.data.polys[i];

				// Create new links.
				// short m = DT_EXT_LINK | (short)side;

				int nv = poly.vertCount;
				for ( int j = 0; j < nv; ++j )
				{
					// Skip non-portal edges.
					if ( (poly.neis[j] & DT_EXT_LINK) == 0 )
					{
						continue;
					}

					int dir = poly.neis[j] & 0xff;
					if ( side != -1 && dir != side )
					{
						continue;
					}

					// Create new links
					int va = poly.verts[j];
					int vb = poly.verts[(j + 1) % nv];
					int nnei = FindConnectingPolys( tile.data.verts[va], tile.data.verts[vb], target, DtUtils.OppositeTile( dir ), ref connectPolys );
					foreach ( var connectPoly in connectPolys )
					{
						int idx = AllocLink( tile );
						if ( idx != DT_NULL_LINK )
						{
							DtLink link = tile.links[idx];
							link.refs = connectPoly.refs;
							link.edge = (byte)j;
							link.side = (byte)dir;

							link.next = poly.firstLink;
							poly.firstLink = idx;

							// Compress portal limits to a byte value.
							if ( dir == 0 || dir == 4 )
							{
								float tmin = (connectPoly.tmin - tile.data.verts[va].z)
											 / (tile.data.verts[vb].z - tile.data.verts[va].z);
								float tmax = (connectPoly.tmax - tile.data.verts[va].z)
											 / (tile.data.verts[vb].z - tile.data.verts[va].z);
								if ( tmin > tmax )
								{
									float temp = tmin;
									tmin = tmax;
									tmax = temp;
								}

								link.bmin = (byte)MathF.Round( Math.Clamp( tmin, 0.0f, 1.0f ) * 255.0f );
								link.bmax = (byte)MathF.Round( Math.Clamp( tmax, 0.0f, 1.0f ) * 255.0f );
							}
							else if ( dir == 2 || dir == 6 )
							{
								float tmin = (connectPoly.tmin - tile.data.verts[va].x)
											 / (tile.data.verts[vb].x - tile.data.verts[va].x);
								float tmax = (connectPoly.tmax - tile.data.verts[va].x)
											 / (tile.data.verts[vb].x - tile.data.verts[va].x);
								if ( tmin > tmax )
								{
									float temp = tmin;
									tmin = tmax;
									tmax = temp;
								}

								link.bmin = (byte)MathF.Round( Math.Clamp( tmin, 0.0f, 1.0f ) * 255.0f );
								link.bmax = (byte)MathF.Round( Math.Clamp( tmax, 0.0f, 1.0f ) * 255.0f );
							}
						}
					}
				}
			}
		}

		/// Builds external polygon links for a tile.
		void ConnectExtOffMeshLinks( DtMeshTile tile, DtMeshTile target, int side )
		{
			if ( tile == null )
			{
				return;
			}

			// Connect off-mesh links.
			// We are interested on links which land from target tile to this tile.
			int oppositeSide = (side == -1) ? 0xff : DtUtils.OppositeTile( side );

			for ( int i = 0; i < target.data.header.offMeshConCount; ++i )
			{
				DtOffMeshConnection targetCon = target.data.offMeshCons[i];
				if ( targetCon.side != oppositeSide )
				{
					continue;
				}

				ref DtPoly targetPoly = ref target.data.polys[targetCon.poly];
				// Skip off-mesh connections which start location could not be
				// connected at all.
				if ( targetPoly.firstLink == DT_NULL_LINK )
				{
					continue;
				}

				var ext = new Vector3()
				{
					x = targetCon.rad,
					y = target.data.header.walkableClimb,
					z = targetCon.rad
				};

				// Find polygon to connect to.
				Vector3 p = targetCon.endPos;
				var refs = FindNearestPolyInTile( tile, p, ext, out var nearestPt );
				if ( refs == 0 )
				{
					continue;
				}

				// findNearestPoly may return too optimistic results, further check
				// to make sure.

				if ( (nearestPt.x - p.x) * (nearestPt.x - p.x) + (nearestPt.z - p.z) * (nearestPt.z - p.z) > targetCon.rad * targetCon.rad )
				{
					continue;
				}

				// Make sure the location is on current mesh.
				target.data.verts[targetPoly.verts[1]] = nearestPt;

				// Link off-mesh connection to target poly.
				int idx = AllocLink( target );
				DtLink link = target.links[idx];
				link.refs = refs;
				link.edge = 1;
				link.side = (byte)oppositeSide;
				link.bmin = link.bmax = 0;
				// Add to linked list.
				link.next = targetPoly.firstLink;
				targetPoly.firstLink = idx;

				// Link target poly to off-mesh connection.
				if ( targetCon.isBiDirectional )
				{
					int tidx = AllocLink( tile );
					int landPolyIdx = DecodePolyIdPoly( refs );
					ref DtPoly landPoly = ref tile.data.polys[landPolyIdx];
					link = tile.links[tidx];
					link.refs = GetPolyRefBase( target ) | (long)targetCon.poly;
					link.edge = 0xff;
					link.side = (byte)(side == -1 ? 0xff : side);
					link.bmin = link.bmax = 0;
					// Add to linked list.
					link.next = landPoly.firstLink;
					landPoly.firstLink = tidx;
				}
			}
		}

		/// Returns all polygons in neighbour tile based on portal defined by the segment.
		private int FindConnectingPolys( Vector3 va, Vector3 vb, DtMeshTile tile, int side, ref List<DtConnectPoly> cons )
		{
			if ( tile == null )
				return 0;

			cons.Clear();

			Vector2 amin = Vector2.Zero;
			Vector2 amax = Vector2.Zero;
			CalcSlabEndPoints( va, vb, ref amin, ref amax, side );
			float apos = GetSlabCoord( va, side );

			// Remove links pointing to 'side' and compact the links array.
			Vector2 bmin = Vector2.Zero;
			Vector2 bmax = Vector2.Zero;
			int m = DT_EXT_LINK | side;
			int n = 0;
			long @base = GetPolyRefBase( tile );

			for ( int i = 0; i < tile.data.header.polyCount; ++i )
			{
				ref DtPoly poly = ref tile.data.polys[i];
				int nv = poly.vertCount;
				for ( int j = 0; j < nv; ++j )
				{
					// Skip edges which do not point to the right side.
					if ( poly.neis[j] != m )
					{
						continue;
					}

					int vc = poly.verts[j];
					int vd = poly.verts[(j + 1) % nv];
					float bpos = GetSlabCoord( tile.data.verts[vc], side );
					// Segments are not close enough.
					if ( MathF.Abs( apos - bpos ) > 0.01f )
					{
						continue;
					}

					// Check if the segments touch.
					CalcSlabEndPoints( tile.data.verts[vc], tile.data.verts[vd], ref bmin, ref bmax, side );

					if ( !OverlapSlabs( amin, amax, bmin, bmax, 0.01f, tile.data.header.walkableClimb ) )
					{
						continue;
					}

					// Add return value.
					long refs = @base | (long)i;
					float tmin = Math.Max( amin.x, bmin.x );
					float tmax = Math.Min( amax.x, bmax.x );
					cons.Add( new DtConnectPoly( refs, tmin, tmax ) );
					n++;
					break;
				}
			}

			return n;
		}

		private bool OverlapSlabs( Vector2 amin, Vector2 amax, Vector2 bmin, Vector2 bmax, float px, float py )
		{
			// Check for horizontal overlap.
			// The segment is shrunken a little so that slabs which touch
			// at end points are not connected.
			float minx = Math.Max( amin.x + px, bmin.x + px );
			float maxx = Math.Min( amax.x - px, bmax.x - px );
			if ( minx > maxx )
			{
				return false;
			}

			// Check vertical overlap.
			float ad = (amax.y - amin.y) / (amax.x - amin.x);
			float ak = amin.y - ad * amin.x;
			float bd = (bmax.y - bmin.y) / (bmax.x - bmin.x);
			float bk = bmin.y - bd * bmin.x;
			float aminy = ad * minx + ak;
			float amaxy = ad * maxx + ak;
			float bminy = bd * minx + bk;
			float bmaxy = bd * maxx + bk;
			float dmin = bminy - aminy;
			float dmax = bmaxy - amaxy;

			// Crossing segments always overlap.
			if ( dmin * dmax < 0 )
			{
				return true;
			}

			// Check for overlap at endpoints.
			float thr = (py * 2) * (py * 2);
			if ( dmin * dmin <= thr || dmax * dmax <= thr )
			{
				return true;
			}

			return false;
		}

		/// Builds internal polygons links for a tile.
		void BaseOffMeshLinks( DtMeshTile tile )
		{
			if ( tile == null )
			{
				return;
			}


			long @base = GetPolyRefBase( tile );

			// Base off-mesh connection start points.
			for ( int i = 0; i < tile.data.header.offMeshConCount; ++i )
			{
				DtOffMeshConnection con = tile.data.offMeshCons[i];

				ref DtPoly poly = ref tile.data.polys[con.poly];

				var ext = new Vector3()
				{
					x = con.rad,
					y = con.rad,
					z = con.rad,
				};

				// Find polygon to connect to.
				var refs = FindNearestPolyInTile( tile, con.startPos, ext, out var nearestPt );
				if ( refs == 0 )
				{
					continue;
				}

				// First vertex
				// findNearestPoly may return too optimistic results, further check
				// to make sure.
				if ( (nearestPt.x - con.startPos.x) * (nearestPt.x - con.startPos.x) + (nearestPt.z - con.startPos.z) * (nearestPt.z - con.startPos.z) > con.rad * con.rad )
				{
					continue;
				}

				// Make sure the location is on current mesh.
				tile.data.verts[poly.verts[0]] = nearestPt;

				// Link off-mesh connection to target poly.
				int idx = AllocLink( tile );
				DtLink link = tile.links[idx];
				link.refs = refs;
				link.edge = 0;
				link.side = 0xff;
				link.bmin = link.bmax = 0;
				// Add to linked list.
				link.next = poly.firstLink;
				poly.firstLink = idx;

				// Start end-point is always connect back to off-mesh connection.
				int tidx = AllocLink( tile );
				int landPolyIdx = DecodePolyIdPoly( refs );
				ref DtPoly landPoly = ref tile.data.polys[landPolyIdx];
				link = tile.links[tidx];
				link.refs = @base | (long)con.poly;
				link.edge = 0xff;
				link.side = 0xff;
				link.bmin = link.bmax = 0;
				// Add to linked list.
				link.next = landPoly.firstLink;
				landPoly.firstLink = tidx;
			}
		}

		/**
	 * Returns closest point on polygon.
	 *
	 * @param ref
	 * @param pos
	 * @return
	 */
		Vector3 ClosestPointOnDetailEdges( DtMeshTile tile, ref DtPoly poly, Vector3 pos, bool onlyBoundary )
		{
			int ip = poly.index;
			float dmin = float.MaxValue;
			float tmin = 0;
			Vector3 pmin = new Vector3();
			Vector3 pmax = new Vector3();

			for ( int j = 0; j < poly.vertCount; ++j )
			{
				int k = (j + 1) % poly.vertCount;

				Vector3 v0 = tile.data.verts[poly.verts[j]];
				Vector3 v1 = tile.data.verts[poly.verts[k]];

				var d = DtUtils.DistancePtSegSqr2D( pos, v0, v1, out var t );
				if ( d < dmin )
				{
					dmin = d;
					tmin = t;
					pmin = v0;
					pmax = v1;
				}
			}

			return Vector3.Lerp( pmin, pmax, tmin );
		}

		public bool GetPolyHeight( DtMeshTile tile, ref DtPoly poly, Vector3 pos, out float height )
		{
			height = 0;

			// Off-mesh connections do not have detail polys and getting height
			// over them does not make sense.
			if ( poly.type == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION )
			{
				return false;
			}

			int ip = poly.index;

			Span<Vector3> verts = stackalloc Vector3[poly.vertCount];
			for ( int i = 0; i < poly.vertCount; ++i )
			{
				verts[i] = tile.data.verts[poly.verts[i]];
			}

			if ( !DtUtils.PointInPolygon( pos, verts ) )
			{
				return false;
			}

			// Find height at the location.
			Span<Vector3> tempV = stackalloc Vector3[3];

			Span<Vector3> v = tempV;
			v[0] = tile.data.verts[poly.verts[0]];
			for ( int j = 1; j < poly.vertCount - 1; ++j )
			{
				for ( int k = 0; k < 2; ++k )
				{
					v[k + 1] = tile.data.verts[poly.verts[j + k]];
				}

				if ( DtUtils.ClosestHeightPointTriangle( pos, v[0], v[1], v[2], out var h ) )
				{
					height = h;
					return true;
				}
			}

			// If all triangle checks failed above (can happen with degenerate triangles
			// or larger floating point values) the point is on an edge, so just select
			// closest. This should almost never happen so the extra iteration here is
			// ok.
			var closest = ClosestPointOnDetailEdges( tile, ref poly, pos, false );
			height = closest.y;
			return true;
		}

		public void ClosestPointOnPoly( long refs, Vector3 pos, out Vector3 closest, out bool posOverPoly )
		{
			GetTileAndPolyByRefUnsafe( refs, out var tile, out var poly );
			closest = pos;

			if ( GetPolyHeight( tile, ref poly, pos, out var h ) )
			{
				closest.y = h;
				posOverPoly = true;
				return;
			}

			posOverPoly = false;

			// Off-mesh connections don't have detail polygons.
			if ( poly.type == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION )
			{
				int i = poly.verts[0];
				var v0 = tile.data.verts[i];
				i = poly.verts[1];
				var v1 = tile.data.verts[i];
				DtUtils.DistancePtSegSqr2D( pos, v0, v1, out var t );
				closest = Vector3.Lerp( v0, v1, t );
				return;
			}

			// Outside poly that is not an offmesh connection.
			closest = ClosestPointOnDetailEdges( tile, ref poly, pos, true );
		}

		/// Find nearest polygon within a tile.
		private long FindNearestPolyInTile( DtMeshTile tile, Vector3 center, Vector3 halfExtents, out Vector3 nearestPt )
		{
			nearestPt = Vector3.Zero;

			bool overPoly = false;
			Vector3 bmin = center - halfExtents;
			Vector3 bmax = center + halfExtents;

			// Get nearby polygons from proximity grid.
			List<long> polys = QueryPolygonsInTile( tile, bmin, bmax );

			// Find nearest polygon amongst the nearby polygons.
			long nearest = 0;
			float nearestDistanceSqr = float.MaxValue;
			for ( int i = 0; i < polys.Count; ++i )
			{
				long refs = polys[i];
				float d;
				ClosestPointOnPoly( refs, center, out var closestPtPoly, out var posOverPoly );

				// If a point is directly over a polygon and closer than
				// climb height, favor that instead of straight line nearest point.
				Vector3 diff = center - closestPtPoly;
				if ( posOverPoly )
				{
					d = MathF.Abs( diff.y ) - tile.data.header.walkableClimb;
					d = d > 0 ? d * d : 0;
				}
				else
				{
					d = diff.LengthSquared;
				}

				if ( d < nearestDistanceSqr )
				{
					nearestPt = closestPtPoly;
					nearestDistanceSqr = d;
					nearest = refs;
					overPoly = posOverPoly;
				}
			}

			return nearest;
		}

		public DtMeshTile GetTileAt( int x, int y, int layer )
		{
			// Find tile based on hash.
			int h = ComputeTileHash( x, y, m_tileLutMask );
			DtMeshTile tile = m_posLookup[h];
			while ( null != tile )
			{
				if ( null != tile.data &&
					null != tile.data.header &&
					tile.data.header.x == x &&
					tile.data.header.y == y &&
					tile.data.header.layer == layer )
				{
					return tile;
				}

				tile = tile.next;
			}

			return null;
		}

		/// Returns neighbour tile based on side.
		int GetNeighbourTilesAt( int x, int y, int side, DtMeshTile[] tiles, int maxTiles )
		{
			int nx = x, ny = y;
			switch ( side )
			{
				case 0:
					nx++;
					break;
				case 1:
					nx++;
					ny++;
					break;
				case 2:
					ny++;
					break;
				case 3:
					nx--;
					ny++;
					break;
				case 4:
					nx--;
					break;
				case 5:
					nx--;
					ny--;
					break;
				case 6:
					ny--;
					break;
				case 7:
					nx++;
					ny--;
					break;
			}

			return GetTilesAt( nx, ny, tiles, maxTiles );
		}

		/// Returns neighbour tile based on side.
		public int GetTilesAt( int x, int y, DtMeshTile[] tiles, int maxTiles )
		{
			int n = 0;

			// Find tile based on hash.
			int h = ComputeTileHash( x, y, m_tileLutMask );
			DtMeshTile tile = m_posLookup[h];
			while ( null != tile )
			{
				if ( null != tile.data &&
					null != tile.data.header &&
					tile.data.header.x == x &&
					tile.data.header.y == y )
				{
					if ( n < maxTiles )
						tiles[n++] = tile;
				}

				tile = tile.next;
			}

			return n;
		}

		public long GetTileRefAt( int x, int y, int layer )
		{
			return GetTileRef( GetTileAt( x, y, layer ) );
		}

		public DtMeshTile GetTileByRef( long refs )
		{
			if ( refs == 0 )
			{
				return null;
			}

			int tileIndex = DecodePolyIdTile( refs );
			int tileSalt = DecodePolyIdSalt( refs );
			if ( tileIndex >= m_maxTiles )
			{
				return null;
			}

			DtMeshTile tile = m_tiles[tileIndex];
			if ( tile.salt != tileSalt )
			{
				return null;
			}

			return tile;
		}

		public long GetTileRef( DtMeshTile tile )
		{
			if ( tile == null )
			{
				return 0;
			}

			return EncodePolyId( tile.salt, tile.index, 0 );
		}

		/// Gets the endpoints for an off-mesh connection, ordered by "direction of travel".
		///  @param[in]		prevRef		The reference of the polygon before the connection.
		///  @param[in]		polyRef		The reference of the off-mesh connection polygon.
		///  @param[out]	startPos	The start position of the off-mesh connection. [(x, y, z)]
		///  @param[out]	endPos		The end position of the off-mesh connection. [(x, y, z)]
		/// @return The status flags for the operation.
		/// 
		/// @par
		///
		/// Off-mesh connections are stored in the navigation mesh as special 2-vertex 
		/// polygons with a single edge. At least one of the vertices is expected to be 
		/// inside a normal polygon. So an off-mesh connection is "entered" from a 
		/// normal polygon at one of its endpoints. This is the polygon identified by 
		/// the prevRef parameter.
		public DtStatus GetOffMeshConnectionPolyEndPoints( long prevRef, long polyRef, ref Vector3 startPos, ref Vector3 endPos )
		{
			if ( polyRef == 0 )
			{
				return DtStatus.DT_FAILURE;
			}

			// Get current polygon
			DecodePolyId( polyRef, out var salt, out var it, out var ip );
			if ( it >= m_maxTiles )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			if ( m_tiles[it].salt != salt || m_tiles[it].data.header == null )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			DtMeshTile tile = m_tiles[it];
			if ( ip >= tile.data.header.polyCount )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			ref DtPoly poly = ref tile.data.polys[ip];

			// Make sure that the current poly is indeed off-mesh link.
			if ( poly.type != DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION )
			{
				return DtStatus.DT_FAILURE;
			}

			// Figure out which way to hand out the vertices.
			int idx0 = 0, idx1 = 1;

			// Find link that points to first vertex.
			for ( int i = poly.firstLink; i != DT_NULL_LINK; i = tile.links[i].next )
			{
				if ( tile.links[i].edge == 0 )
				{
					if ( tile.links[i].refs != prevRef )
					{
						idx0 = 1;
						idx1 = 0;
					}

					break;
				}
			}

			startPos = tile.data.verts[poly.verts[idx0]];
			endPos = tile.data.verts[poly.verts[idx1]];

			return DtStatus.DT_SUCCESS;
		}

		public int GetMaxVertsPerPoly()
		{
			return m_maxVertPerPoly;
		}

		public int GetTileCount()
		{
			return m_tileCount;
		}

		public bool IsAvailableTileCount()
		{
			return null != m_nextFree;
		}

		/// Sets the user defined area for the specified polygon.
		///  @param[in]	ref		The polygon reference.
		///  @param[in]	area	The new area id for the polygon. [Limit: DT_MAX_AREAS]
		/// @return The status flags for the operation.
		public DtStatus SetPolyArea( long refs, char area )
		{
			if ( refs == 0 )
			{
				return DtStatus.DT_FAILURE;
			}

			DecodePolyId( refs, out var salt, out var it, out var ip );
			if ( it >= m_maxTiles )
			{
				return DtStatus.DT_FAILURE;
			}

			if ( m_tiles[it].salt != salt || m_tiles[it].data == null || m_tiles[it].data.header == null )
			{
				return DtStatus.DT_INVALID_PARAM;
			}

			ref DtMeshTile tile = ref m_tiles[it];
			if ( ip >= tile.data.header.polyCount )
			{
				return DtStatus.DT_INVALID_PARAM;
			}

			DtPoly poly = tile.data.polys[ip];

			poly.area = area;

			return DtStatus.DT_SUCCESS;
		}

		/// Gets the user defined area for the specified polygon.
		///  @param[in]		ref			The polygon reference.
		///  @param[out]	resultArea	The area id for the polygon.
		/// @return The status flags for the operation.
		public DtStatus GetPolyArea( long refs, out int resultArea )
		{
			resultArea = 0;

			if ( refs == 0 )
			{
				return DtStatus.DT_FAILURE;
			}

			DecodePolyId( refs, out var salt, out var it, out var ip );
			if ( it >= m_maxTiles )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			if ( m_tiles[it].salt != salt || m_tiles[it].data == null || m_tiles[it].data.header == null )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			ref DtMeshTile tile = ref m_tiles[it];
			if ( ip >= tile.data.header.polyCount )
			{
				return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
			}

			DtPoly poly = tile.data.polys[ip];
			resultArea = poly.area;

			return DtStatus.DT_SUCCESS;
		}

		public Vector3 GetPolyCenter( long refs )
		{
			Vector3 center = Vector3.Zero;

			var status = GetTileAndPolyByRef( refs, out var tile, out var poly );
			if ( status.Succeeded() )
			{
				for ( int i = 0; i < poly.vertCount; ++i )
				{
					int v = poly.verts[i];
					center += tile.data.verts[v];
				}

				float s = 1.0f / poly.vertCount;
				center *= s;
			}

			return center;
		}

		public void ComputeBounds( out Vector3 bmin, out Vector3 bmax )
		{
			bmin = new Vector3( float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity );
			bmax = new Vector3( float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity );
			for ( int t = 0; t < GetMaxTiles(); ++t )
			{
				DtMeshTile tile = GetTile( t );
				if ( tile != null && tile.data != null )
				{
					for ( int i = 0; i < tile.data.verts.Length; ++i )
					{
						bmin = Vector3.Min( bmin, tile.data.verts[i] );
						bmax = Vector3.Max( bmax, tile.data.verts[i] );
					}
				}
			}
		}
	}
}
