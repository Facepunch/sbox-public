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
using Sandbox.Navigation.Generation;

namespace Sandbox.Navigation.Pathfinding;

internal static class MeshBuilder
{
	const int MESH_NULL_IDX = 0xffff;

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

	public static MeshData CreateNavMeshData( MeshBuildParameters option )
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

		MeshHeader header = new MeshHeader();
		Vector3[] navVerts = new Vector3[totVertCount];
		Poly[] navPolys = new Poly[totPolyCount];
		var navBvtree = option.buildBvTree ? BoundingVolumeTree.Build( option ) : [];
		OffMeshConnection[] offMeshCons = new OffMeshConnection[storedOffMeshConCount];

		// Store header
		header.x = option.tileX;
		header.y = option.tileZ;
		header.layer = option.tileLayer;
		header.polyCount = totPolyCount;
		header.vertCount = totVertCount;
		header.maxLinkCount = maxLinkCount;
		header.bmin = option.bmin;
		header.bmax = option.bmax;
		header.bvQuantFactor = 1.0f / option.cs;
		header.offMeshBase = option.pmesh.PolyCount;
		header.walkableClimb = option.walkableClimb;
		header.offMeshConCount = storedOffMeshConCount;

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
			Poly p = new Poly( i, nvp );
			p.vertCount = 0;
			p.area = option.pmesh.Areas[i];
			p.type = PolyTypes.POLYTYPE_GROUND;
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
						p.neis[j] = EXT_LINK | 4;
					else if ( dir == 1 ) // Portal z+
						p.neis[j] = EXT_LINK | 2;
					else if ( dir == 2 ) // Portal x+
						p.neis[j] = EXT_LINK | 0;
					else if ( dir == 3 ) // Portal z-
						p.neis[j] = EXT_LINK | 6;
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
				Poly p = new Poly( offMeshPolyBase + n, nvp );
				p.vertCount = 2;
				p.verts[0] = offMeshVertsBase + n * 2;
				p.verts[1] = offMeshVertsBase + n * 2 + 1;
				p.area = option.offMeshConAreas[i];
				p.type = PolyTypes.POLYTYPE_OFFMESH_CONNECTION;
				navPolys[offMeshPolyBase + n] = p;
				n++;
			}
		}

		// Store and create BVtree.
		// TODO: take detail mesh into account! use byte per bbox extent?

		// Store Off-Mesh connections.
		n = 0;
		for ( int i = 0; i < option.offMeshConCount; ++i )
		{
			// Only store connections which start from this tile.
			if ( offMeshConClass[i * 2 + 0] == 0xff )
			{
				OffMeshConnection con = new OffMeshConnection();
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

		MeshData nmd = new MeshData();
		nmd.header = header;
		nmd.verts = navVerts;
		nmd.polys = navPolys;
		nmd.bvTree = navBvtree;
		nmd.offMeshCons = offMeshCons;
		return nmd;
	}
}

ref struct MeshBuildParameters
{
	public PolyMesh pmesh;

	public Span<Vector3> offMeshConVerts;

	public Span<float> offMeshConRad;

	public Span<int> offMeshConAreas;

	public Span<bool> offMeshConBidirectional;

	public Span<object> offMeshConUserData;

	public int offMeshConCount;


	public int tileX; // < The tile's x-grid location within the multi-tile destination mesh. (Along the x-axis.)
	public int tileZ; // < The tile's y-grid location within the multi-tile destination mesh. (Along the z-axis.)
	public int tileLayer; // < The tile's layer within the layered destination mesh. [Limit: >= 0] (Along the y-axis.)
	public Vector3 bmin; // < The minimum bounds of the tile. [(x, y, z)] [Unit: wu]
	public Vector3 bmax; // < The maximum bounds of the tile. [(x, y, z)] [Unit: wu]

	public float walkableHeight; // < The agent height. [Unit: wu]
	public float walkableRadius; // < The agent radius. [Unit: wu]
	public float walkableClimb; // < The agent maximum traversable ledge. (Up/Down) [Unit: wu]
	public float cs; // < The xz-plane cell size of the polygon mesh. [Limit: > 0] [Unit: wu]
	public float ch; // < The y-axis cell height of the polygon mesh. [Limit: > 0] [Unit: wu]

	public bool buildBvTree;
}
