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

namespace Sandbox.Navigation.Pathfinding;

internal class MeshData
{
	public MeshHeader header; //< The tile header.
	public Poly[] polys; //< The tile polygons. [Size: MeshHeader::polyCount]
	public Vector3[] verts; //< The tile vertices. [(x, y, z) * MeshHeader::vertCount]

	public BoundingVolumeNode[] bvTree;

	public OffMeshConnection[] offMeshCons; //< The tile off-mesh connections. [Size: MeshHeader::offMeshConCount]
}

internal class MeshHeader
{


	public int x;

	public int y;

	public int layer;

	public int polyCount;

	public int vertCount;

	public int maxLinkCount;

	public int offMeshConCount;

	public int offMeshBase;

	public float walkableClimb;

	public Vector3 bmin = Vector3.Zero;

	public Vector3 bmax = Vector3.Zero;

	public float bvQuantFactor;
}

internal struct MeshParameters
{
	public Vector3 orig; //< The world space origin of the navigation mesh's tile space. [(x, y, z)]
	public float tileWidth; //< The width of each tile. (Along the x-axis.)
	public float tileHeight; //< The height of each tile. (Along the z-axis.)
	public int maxTiles; // Maximum loaded tile slots.
}

internal class MeshTile
{
	public readonly int index; // NavMeshGraph._tiles array index
	public int linksFreeList = NULL_LINK; //< Index to the next free link.
	public int salt; //< Counter describing modifications to the tile.
	public MeshData data; // The tile data.
	public Link[] links; // The tile links. [Size: MeshHeader::maxLinkCount]

	public int flags; //< Tile flags. (See: #TileFlags)

	public MeshTile( int index )
	{
		this.index = index;
	}
}

[System.Runtime.CompilerServices.InlineArray( 6 )]
internal struct VertexIndices { private int value; }

internal struct Poly
{
	public readonly int index;

	public int firstLink;

	public VertexIndices verts;

	public VertexIndices neis;

	public int vertCount;

	public int area;

	public byte type;

	public Poly( int index, int maxVertsPerPoly )
	{
		this.index = index;
		if ( maxVertsPerPoly > 6 ) throw new ArgumentOutOfRangeException( nameof( maxVertsPerPoly ) );
	}
}

internal static class PolyTypes
{
	public const int POLYTYPE_GROUND = 0; // The polygon is a standard convex polygon that is part of the surface of the mesh.
	public const int POLYTYPE_OFFMESH_CONNECTION = 1; // The polygon is an off-mesh connection consisting of two vertices.
}

internal struct Link
{
	public long refs; //< Neighbour reference. (The neighbor that is linked to.)
	public int next; //< Index of the next link.
	public byte edge; //< Index of the polygon edge that owns this link.
	public byte side; //< If a boundary link, defines on which side the link is.
	public byte bmin; //< If a boundary link, defines the minimum sub-edge area.
	public byte bmax; //< If a boundary link, defines the maximum sub-edge area.
}

internal class OffMeshConnection
{
	public Vector3 startPos;

	public Vector3 endPos;

	public float rad;

	public int poly;

	public bool isBiDirectional;

	public int side;

	public object userData;
}
