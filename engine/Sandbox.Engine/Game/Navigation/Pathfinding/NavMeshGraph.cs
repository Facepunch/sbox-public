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

using System.Runtime.InteropServices;
using static Sandbox.Navigation.Pathfinding.MeshConstants;

namespace Sandbox.Navigation.Pathfinding;

internal class NavMeshGraph
{
	private readonly record struct ConnectPoly( long refs, float tmin, float tmax );

	private Vector3 _orig; // < Origin of the tile (0,0)
	private float _tileWidth; // < Dimensions of each tile.
	private float _tileHeight; // < Dimensions of each tile.
	private int _maxTiles; // < Max number of tiles.

	// Reused by AddTile/RemoveTile neighbour connection
	private readonly MeshTile[] _neisScratch = new MeshTile[32];
	private readonly List<ConnectPoly> connectPolys = new();

	private int _maxVertPerPoly;

	private readonly List<MeshTile> _tiles = new();
	private readonly SortedSet<int> freeTiles = new();
	private readonly Dictionary<(int X, int Y), List<MeshTile>> tileLocations = new();

	public Status Init( MeshParameters param, int maxVertsPerPoly )
	{
		_orig = param.orig;
		_tileWidth = param.tileWidth;
		_tileHeight = param.tileHeight;

		// Init tiles
		_maxVertPerPoly = maxVertsPerPoly;
		_maxTiles = param.maxTiles;
		_tiles.Clear();
		freeTiles.Clear();
		tileLocations.Clear();

		return Status.Success;
	}

	public int GetMaxTiles()
	{ return _tiles.Count; }

	public MeshTile GetTile( int i )
	{
		return _tiles[i];
	}

	public long GetPolyRefBase( MeshTile tile )
	{
		if ( tile == null )
		{
			return 0;
		}

		int it = tile.index;
		return EncodePolyId( tile.salt, it, 0 );
	}

	private int AllocLink( MeshTile tile )
	{
		if ( tile.linksFreeList == NULL_LINK )
			return NULL_LINK;

		int linkIdx = tile.linksFreeList;
		tile.linksFreeList = tile.links[linkIdx].next;
		return linkIdx;
	}

	private void FreeLink( MeshTile tile, int link )
	{
		tile.links[link].next = tile.linksFreeList;
		tile.linksFreeList = link;
	}

	public void CalcTileLoc( Vector3 pos, out int tx, out int ty )
	{
		tx = (int)MathF.Floor( (pos.x - _orig.x) / _tileWidth );
		ty = (int)MathF.Floor( (pos.z - _orig.z) / _tileHeight );
	}

	public Status GetTileAndPolyByRef( long refs, out MeshTile tile, out Poly poly )
	{
		tile = null;

		if ( refs == 0 )
		{
			poly = new Poly( 0, 0 );
			return Status.Failure;
		}

		DecodePolyId( refs, out var salt, out var it, out var ip );
		if ( it >= _tiles.Count )
		{
			poly = new Poly( 0, 0 );
			return Status.Failure | Status.InvalidInput;
		}

		if ( _tiles[it].salt != salt || _tiles[it].data == null || _tiles[it].data.header == null )
		{
			poly = new Poly( 0, 0 );
			return Status.Failure | Status.InvalidInput;
		}

		if ( ip >= _tiles[it].data.header.polyCount )
		{
			poly = new Poly( 0, 0 );
			return Status.Failure | Status.InvalidInput;
		}

		tile = _tiles[it];
		poly = _tiles[it].data.polys[ip];

		return Status.Success;
	}

	public void GetTileAndPolyByRefUnsafe( long refs, out MeshTile tile, out Poly poly )
	{
		DecodePolyId( refs, out var salt, out var it, out var ip );
		tile = _tiles[it];
		poly = _tiles[it].data.polys[ip];
	}

	public bool IsValidPolyRef( long refs )
	{
		if ( refs == 0 )
		{
			return false;
		}

		DecodePolyId( refs, out var salt, out var it, out var ip );
		if ( it >= _tiles.Count )
		{
			return false;
		}

		if ( _tiles[it].salt != salt || _tiles[it].data == null )
		{
			return false;
		}

		if ( ip >= _tiles[it].data.header.polyCount )
		{
			return false;
		}

		return true;
	}

	public Status UpdateTile( MeshData data, int flags )
	{
		long refs = GetTileRefAt( data.header.x, data.header.y, data.header.layer );
		refs = RemoveTile( refs );
		return AddTile( data, flags, refs, out _ );
	}

	public Status AddTile( MeshData data, int flags, long lastRef, out long result )
	{
		result = 0;

		// Make sure the data is in right format.
		MeshHeader header = data.header;

		// Make sure the location is free.
		if ( GetTileAt( header.x, header.y, header.layer ) != null )
		{
			return Status.Failure | Status.Occupied;
		}

		MeshTile tile;
		if ( lastRef != 0 )
		{
			int index = DecodePolyIdTile( lastRef );
			if ( !freeTiles.Remove( index ) ) return Status.Failure | Status.InvalidInput;
			tile = _tiles[index];
			tile.salt = DecodePolyIdSalt( lastRef );
		}
		else if ( freeTiles.Count > 0 )
		{
			int index = freeTiles.Min;
			freeTiles.Remove( index );
			tile = _tiles[index];
		}
		else
		{
			if ( _tiles.Count >= _maxTiles ) return Status.Failure | Status.CapacityExceeded;
			tile = new MeshTile( _tiles.Count ) { salt = 1 };
			_tiles.Add( tile );
		}
		var location = (header.x, header.y);
		if ( !tileLocations.TryGetValue( location, out var layers ) ) tileLocations.Add( location, layers = new() );
		layers.Add( tile );

		// Patch header pointers.
		tile.data = data;
		tile.links = new Link[data.header.maxLinkCount];

		// If there are no items in the bvtree, reset the tree pointer.
		if ( tile.data.bvTree != null && tile.data.bvTree.Length == 0 )
		{
			tile.data.bvTree = null;
		}

		// Build links freelist
		tile.linksFreeList = 0;
		tile.links[data.header.maxLinkCount - 1].next = NULL_LINK;
		for ( int i = 0; i < data.header.maxLinkCount - 1; ++i )
			tile.links[i].next = i + 1;

		// Init tile.
		tile.flags = flags;

		ConnectIntLinks( tile );

		// Base off-mesh connections to their starting polygons and connect connections inside the tile.
		BaseOffMeshLinks( tile );
		ConnectExtOffMeshLinks( tile, tile, -1 );

		// Create connections with neighbour tiles.
		MeshTile[] neis = _neisScratch;
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
				ConnectExtLinks( neis[j], tile, Geometry.OppositeTile( i ) );
				ConnectExtOffMeshLinks( tile, neis[j], i );
				ConnectExtOffMeshLinks( neis[j], tile, Geometry.OppositeTile( i ) );
			}
		}

		result = GetTileRef( tile );
		return Status.Success;
	}

	public long RemoveTile( long refs )
	{
		if ( refs == 0 )
		{
			return 0;
		}

		int tileIndex = DecodePolyIdTile( refs );
		int tileSalt = DecodePolyIdSalt( refs );
		if ( tileIndex >= _tiles.Count )
		{
			throw new Exception( "Invalid tile index" );
		}

		MeshTile tile = _tiles[tileIndex];
		if ( tile.salt != tileSalt )
		{
			throw new Exception( "Invalid tile salt" );
		}

		var location = (tile.data.header.x, tile.data.header.y);
		var layers = tileLocations[location];
		layers.Remove( tile );
		if ( layers.Count == 0 ) tileLocations.Remove( location );

		// Remove connections to neighbour tiles.
		MeshTile[] neis = _neisScratch;
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
		tile.linksFreeList = NULL_LINK;

		// Update salt, salt should never be zero.
		tile.salt = (tile.salt + 1) & ((1 << SALT_BITS) - 1);
		if ( tile.salt == 0 )
		{
			tile.salt++;
		}

		// Add to free list.
		freeTiles.Add( tile.index );
		return GetTileRef( tile );
	}

	void ConnectIntLinks( MeshTile tile )
	{
		if ( tile == null )
		{
			return;
		}

		long @base = GetPolyRefBase( tile );

		for ( int i = 0; i < tile.data.header.polyCount; ++i )
		{
			ref Poly poly = ref tile.data.polys[i];
			poly.firstLink = NULL_LINK;

			if ( poly.type == PolyTypes.POLYTYPE_OFFMESH_CONNECTION )
			{
				continue;
			}

			// Build edge links backwards so that the links will be
			// in the linked list from lowest index to highest.
			for ( int j = poly.vertCount - 1; j >= 0; --j )
			{
				// Skip hard and non-internal edges.
				if ( poly.neis[j] == 0 || (poly.neis[j] & EXT_LINK) != 0 )
				{
					continue;
				}

				int idx = AllocLink( tile );
				ref Link link = ref tile.links[idx];
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

	void UnconnectLinks( MeshTile tile, MeshTile target )
	{
		if ( tile == null || target == null )
		{
			return;
		}

		int targetNum = DecodePolyIdTile( GetTileRef( target ) );

		for ( int i = 0; i < tile.data.header.polyCount; ++i )
		{
			ref Poly poly = ref tile.data.polys[i];
			int j = poly.firstLink;
			int pj = NULL_LINK;
			while ( j != NULL_LINK )
			{
				if ( DecodePolyIdTile( tile.links[j].refs ) == targetNum )
				{
					// Remove link.
					int nj = tile.links[j].next;
					if ( pj == NULL_LINK )
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

	void ConnectExtLinks( MeshTile tile, MeshTile target, int side )
	{
		if ( tile == null )
		{
			return;
		}

		// Connect border links.
		for ( int i = 0; i < tile.data.header.polyCount; ++i )
		{
			ref Poly poly = ref tile.data.polys[i];

			// Create new links.
			// short m = EXT_LINK | (short)side;

			int nv = poly.vertCount;
			for ( int j = 0; j < nv; ++j )
			{
				// Skip non-portal edges.
				if ( (poly.neis[j] & EXT_LINK) == 0 )
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
				FindConnectingPolys( tile.data.verts[va], tile.data.verts[vb], target, Geometry.OppositeTile( dir ), connectPolys );
				foreach ( var connectPoly in connectPolys )
				{
					int idx = AllocLink( tile );
					if ( idx != NULL_LINK )
					{
						ref Link link = ref tile.links[idx];
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

	void ConnectExtOffMeshLinks( MeshTile tile, MeshTile target, int side )
	{
		if ( tile == null )
		{
			return;
		}

		// Connect off-mesh links.
		// We are interested on links which land from target tile to this tile.
		int oppositeSide = (side == -1) ? 0xff : Geometry.OppositeTile( side );

		for ( int i = 0; i < target.data.header.offMeshConCount; ++i )
		{
			OffMeshConnection targetCon = target.data.offMeshCons[i];
			if ( targetCon.side != oppositeSide )
			{
				continue;
			}

			ref Poly targetPoly = ref target.data.polys[targetCon.poly];
			// Skip off-mesh connections which start location could not be
			// connected at all.
			if ( targetPoly.firstLink == NULL_LINK )
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
			ref Link link = ref target.links[idx];
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
				ref Poly landPoly = ref tile.data.polys[landPolyIdx];
				link = ref tile.links[tidx];
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

	private void FindConnectingPolys( Vector3 va, Vector3 vb, MeshTile tile, int side, List<ConnectPoly> cons )
	{
		cons.Clear();
		if ( tile == null ) return;

		Vector2 amin = Vector2.Zero;
		Vector2 amax = Vector2.Zero;
		CalcSlabEndPoints( va, vb, ref amin, ref amax, side );
		float apos = GetSlabCoord( va, side );

		// Remove links pointing to 'side' and compact the links array.
		Vector2 bmin = Vector2.Zero;
		Vector2 bmax = Vector2.Zero;
		int m = EXT_LINK | side;
		long @base = GetPolyRefBase( tile );

		for ( int i = 0; i < tile.data.header.polyCount; ++i )
		{
			ref Poly poly = ref tile.data.polys[i];
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
				cons.Add( new ConnectPoly( refs, tmin, tmax ) );
				break;
			}
		}

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

	void BaseOffMeshLinks( MeshTile tile )
	{
		if ( tile == null )
		{
			return;
		}

		long @base = GetPolyRefBase( tile );

		// Base off-mesh connection start points.
		for ( int i = 0; i < tile.data.header.offMeshConCount; ++i )
		{
			OffMeshConnection con = tile.data.offMeshCons[i];

			ref Poly poly = ref tile.data.polys[con.poly];

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
			ref Link link = ref tile.links[idx];
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
			ref Poly landPoly = ref tile.data.polys[landPolyIdx];
			link = ref tile.links[tidx];
			link.refs = @base | (long)con.poly;
			link.edge = 0xff;
			link.side = 0xff;
			link.bmin = link.bmax = 0;
			// Add to linked list.
			link.next = landPoly.firstLink;
			landPoly.firstLink = tidx;
		}
	}

	Vector3 ClosestPointOnDetailEdges( MeshTile tile, ref Poly poly, Vector3 pos, bool onlyBoundary )
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

			var d = Geometry.DistancePtSegSqr2D( pos, v0, v1, out var t );
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

	public bool GetPolyHeight( MeshTile tile, ref Poly poly, Vector3 pos, out float height )
	{
		height = 0;

		// Off-mesh connections do not have detail polys and getting height
		// over them does not make sense.
		if ( poly.type == PolyTypes.POLYTYPE_OFFMESH_CONNECTION )
		{
			return false;
		}

		int ip = poly.index;

		Span<Vector3> verts = stackalloc Vector3[poly.vertCount];
		for ( int i = 0; i < poly.vertCount; ++i )
		{
			verts[i] = tile.data.verts[poly.verts[i]];
		}

		if ( !Geometry.PointInPolygon( pos, verts ) )
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

			if ( Geometry.ClosestHeightPointTriangle( pos, v[0], v[1], v[2], out var h ) )
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
		if ( poly.type == PolyTypes.POLYTYPE_OFFMESH_CONNECTION )
		{
			int i = poly.verts[0];
			var v0 = tile.data.verts[i];
			i = poly.verts[1];
			var v1 = tile.data.verts[i];
			Geometry.DistancePtSegSqr2D( pos, v0, v1, out var t );
			closest = Vector3.Lerp( v0, v1, t );
			return;
		}

		// Outside poly that is not an offmesh connection.
		closest = ClosestPointOnDetailEdges( tile, ref poly, pos, true );
	}

	private long FindNearestPolyInTile( MeshTile tile, Vector3 center, Vector3 halfExtents, out Vector3 nearestPt )
	{
		nearestPt = Vector3.Zero;

		bool overPoly = false;
		Vector3 bmin = center - halfExtents;
		Vector3 bmax = center + halfExtents;

		long baseReference = GetPolyRefBase( tile );

		// Find nearest polygon amongst the nearby polygons.
		long nearest = 0;
		float nearestDistanceSqr = float.MaxValue;
		foreach ( int polygon in BoundingVolumeTree.Query( tile.data, bmin, bmax ) )
		{
			long refs = baseReference | (long)polygon;
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

	public MeshTile GetTileAt( int x, int y, int layer )
	{
		if ( tileLocations.TryGetValue( (x, y), out var layers ) )
			foreach ( var tile in layers ) if ( tile.data.header.layer == layer ) return tile;
		return null;
	}

	int GetNeighbourTilesAt( int x, int y, int side, MeshTile[] tiles, int maxTiles )
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

	public int GetTilesAt( int x, int y, MeshTile[] tiles, int maxTiles )
	{
		if ( !tileLocations.TryGetValue( (x, y), out var layers ) ) return 0;
		int count = Math.Min( layers.Count, maxTiles );
		for ( int i = 0; i < count; i++ ) tiles[i] = layers[i];
		return count;
	}

	public long GetTileRefAt( int x, int y, int layer )
	{
		return GetTileRef( GetTileAt( x, y, layer ) );
	}

	public MeshTile GetTileByRef( long refs )
	{
		if ( refs == 0 )
		{
			return null;
		}

		int tileIndex = DecodePolyIdTile( refs );
		int tileSalt = DecodePolyIdSalt( refs );
		if ( tileIndex >= _tiles.Count )
		{
			return null;
		}

		MeshTile tile = _tiles[tileIndex];
		if ( tile.salt != tileSalt )
		{
			return null;
		}

		return tile;
	}

	public long GetTileRef( MeshTile tile )
	{
		if ( tile == null )
		{
			return 0;
		}

		return EncodePolyId( tile.salt, tile.index, 0 );
	}

	public Status GetOffMeshConnectionPolyEndPoints( long prevRef, long polyRef, ref Vector3 startPos, ref Vector3 endPos )
	{
		if ( polyRef == 0 )
		{
			return Status.Failure;
		}

		// Get current polygon
		DecodePolyId( polyRef, out var salt, out var it, out var ip );
		if ( it >= _tiles.Count )
		{
			return Status.Failure | Status.InvalidInput;
		}

		if ( _tiles[it].salt != salt || _tiles[it].data.header == null )
		{
			return Status.Failure | Status.InvalidInput;
		}

		MeshTile tile = _tiles[it];
		if ( ip >= tile.data.header.polyCount )
		{
			return Status.Failure | Status.InvalidInput;
		}

		ref Poly poly = ref tile.data.polys[ip];

		// Make sure that the current poly is indeed off-mesh link.
		if ( poly.type != PolyTypes.POLYTYPE_OFFMESH_CONNECTION )
		{
			return Status.Failure;
		}

		// Figure out which way to hand out the vertices.
		int idx0 = 0, idx1 = 1;

		// Find link that points to first vertex.
		for ( int i = poly.firstLink; i != NULL_LINK; i = tile.links[i].next )
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

		return Status.Success;
	}

	public int GetMaxVertsPerPoly()
	{
		return _maxVertPerPoly;
	}

}

internal static class MeshConstants
{
	public const int VERTS_PER_POLYGON = 6;



	public const int SALT_BITS = 16;
	public const int TILE_BITS = 28;
	public const int POLY_BITS = 20;

	public const int EXT_LINK = 0x8000;

	public const int NULL_LINK = unchecked((int)0xffffffff);

	public const int NODE_STATE_BITS = 2;
	public const int MAX_STATES_PER_NODE = 1 << NODE_STATE_BITS; // number of extra states per node. See Node::state

	public static long EncodePolyId( int salt, int it, int ip )
	{
		return (((long)salt) << (POLY_BITS + TILE_BITS)) | ((long)it << POLY_BITS) | (long)ip;
	}

	public static void DecodePolyId( long refs, out int salt, out int it, out int ip )
	{
		long saltMask = (1L << SALT_BITS) - 1;
		long tileMask = (1L << TILE_BITS) - 1;
		long polyMask = (1L << POLY_BITS) - 1;
		salt = (int)((refs >> (POLY_BITS + TILE_BITS)) & saltMask);
		it = (int)((refs >> POLY_BITS) & tileMask);
		ip = (int)(refs & polyMask);
	}

	public static int DecodePolyIdSalt( long refs )
	{
		long saltMask = (1L << SALT_BITS) - 1;
		return (int)((refs >> (POLY_BITS + TILE_BITS)) & saltMask);
	}

	public static int DecodePolyIdTile( long refs )
	{
		long tileMask = (1L << TILE_BITS) - 1;
		return (int)((refs >> POLY_BITS) & tileMask);
	}

	public static int DecodePolyIdPoly( long refs )
	{
		long polyMask = (1L << POLY_BITS) - 1;
		return (int)(refs & polyMask);
	}

	public static float GetSlabCoord( Vector3 vert, int side )
	{
		if ( side == 0 || side == 4 )
		{
			return vert.x;
		}
		else if ( side == 2 || side == 6 )
		{
			return vert.z;
		}

		return 0;
	}

	public static void CalcSlabEndPoints( Vector3 va, Vector3 vb, ref Vector2 bmin, ref Vector2 bmax, int side )
	{
		if ( side == 0 || side == 4 )
		{
			if ( va.z < vb.z )
			{
				bmin.x = va.z;
				bmin.y = va.y;
				bmax.x = vb.z;
				bmax.y = vb.y;
			}
			else
			{
				bmin.x = vb.z;
				bmin.y = vb.y;
				bmax.x = va.z;
				bmax.y = va.y;
			}
		}
		else if ( side == 2 || side == 6 )
		{
			if ( va.x < vb.x )
			{
				bmin.x = va.x;
				bmin.y = va.y;
				bmax.x = vb.x;
				bmax.y = vb.y;
			}
			else
			{
				bmin.x = vb.x;
				bmin.y = vb.y;
				bmax.x = va.x;
				bmax.y = va.y;
			}
		}
	}

}
