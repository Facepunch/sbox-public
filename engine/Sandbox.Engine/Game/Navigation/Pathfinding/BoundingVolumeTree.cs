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

namespace Sandbox.Navigation.Pathfinding;

/// <summary>Quantized polygon bounds in a flat, depth-first tree. Negative indices skip a subtree.</summary>
internal struct BoundingVolumeNode
{
	public Vector3Int Min, Max;
	public int Index;
}

internal static class BoundingVolumeTree
{
	private readonly record struct AxisComparer( int Axis ) : IComparer<BoundingVolumeNode>
	{
		public int Compare( BoundingVolumeNode a, BoundingVolumeNode b ) => a.Min[Axis].CompareTo( b.Min[Axis] );
	}

	internal static BoundingVolumeNode[] Build( MeshBuildParameters parameters )
	{
		var polygons = parameters.pmesh;
		if ( polygons.PolyCount == 0 ) return [];
		using var scratch = new PooledSpan<BoundingVolumeNode>( polygons.PolyCount );
		var items = scratch.Span;
		for ( int i = 0; i < items.Length; i++ )
		{
			ref var item = ref items[i];
			item.Index = i;
			int start = i * polygons.MaxVertsPerPoly * 2;
			int vertex = polygons.Polys[start] * 3;
			item.Min = item.Max = new Vector3Int( polygons.Verts[vertex], polygons.Verts[vertex + 1], polygons.Verts[vertex + 2] );
			for ( int j = 1; j < polygons.MaxVertsPerPoly && polygons.Polys[start + j] != 0xffff; j++ )
			{
				vertex = polygons.Polys[start + j] * 3;
				for ( int axis = 0; axis < 3; axis++ )
				{
					item.Min[axis] = Math.Min( item.Min[axis], polygons.Verts[vertex + axis] );
					item.Max[axis] = Math.Max( item.Max[axis], polygons.Verts[vertex + axis] );
				}
			}
			item.Min.y = (int)MathF.Floor( item.Min.y * parameters.ch / parameters.cs );
			item.Max.y = (int)MathF.Ceiling( item.Max.y * parameters.ch / parameters.cs );
		}
		var nodes = new BoundingVolumeNode[items.Length * 2 - 1];
		Build( items, nodes, 0 );
		return nodes;
	}

	private static int Build( Span<BoundingVolumeNode> items, BoundingVolumeNode[] nodes, int index )
	{
		ref var node = ref nodes[index];
		node = items[0];
		if ( items.Length == 1 ) return index + 1;
		foreach ( var item in items.Slice( 1 ) )
			for ( int axis = 0; axis < 3; axis++ )
			{
				node.Min[axis] = Math.Min( node.Min[axis], item.Min[axis] );
				node.Max[axis] = Math.Max( node.Max[axis], item.Max[axis] );
			}
		var size = node.Max - node.Min;
		int splitAxis = size.y > size.x ? 1 : 0;
		if ( size.z > size[splitAxis] ) splitAxis = 2;
		items.Sort( new AxisComparer( splitAxis ) );
		int middle = items.Length / 2;
		int next = Build( items.Slice( 0, middle ), nodes, index + 1 );
		next = Build( items.Slice( middle ), nodes, next );
		node.Index = index - next;
		return next;
	}

	internal static Overlaps Query( MeshData tile, Vector3 minimum, Vector3 maximum ) => new( tile, minimum, maximum );

	/// <summary>Allocation-free traversal, shared by placement queries and tile-link construction.</summary>
	internal struct Overlaps
	{
		private readonly MeshData tile;
		private readonly Vector3 minimum, maximum;
		private readonly Vector3Int quantizedMinimum, quantizedMaximum;
		private int index;
		public int Current { get; private set; }
		public Overlaps GetEnumerator() => this;

		internal Overlaps( MeshData tile, Vector3 minimum, Vector3 maximum )
		{
			this.tile = tile;
			this.minimum = minimum;
			this.maximum = maximum;
			index = 0;
			Current = -1;
			quantizedMinimum = quantizedMaximum = default;
			for ( int axis = 0; axis < 3; axis++ )
			{
				float low = Math.Clamp( minimum[axis], tile.header.bmin[axis], tile.header.bmax[axis] ) - tile.header.bmin[axis];
				float high = Math.Clamp( maximum[axis], tile.header.bmin[axis], tile.header.bmax[axis] ) - tile.header.bmin[axis];
				quantizedMinimum[axis] = (int)(low * tile.header.bvQuantFactor) & 0x7ffffffe;
				quantizedMaximum[axis] = (int)(high * tile.header.bvQuantFactor + 1) | 1;
			}
		}

		public bool MoveNext()
		{
			if ( tile.bvTree is { Length: > 0 } nodes )
			{
				while ( index < nodes.Length )
				{
					ref readonly var node = ref nodes[index];
					bool overlaps = quantizedMinimum.x <= node.Max.x && quantizedMaximum.x >= node.Min.x
						&& quantizedMinimum.y <= node.Max.y && quantizedMaximum.y >= node.Min.y
						&& quantizedMinimum.z <= node.Max.z && quantizedMaximum.z >= node.Min.z;
					index += overlaps || node.Index >= 0 ? 1 : -node.Index;
					if ( overlaps && node.Index >= 0 ) { Current = node.Index; return true; }
				}
			}
			else
			{
				while ( index < tile.polys.Length )
				{
					int candidate = index++;
					ref readonly var polygon = ref tile.polys[candidate];
					if ( polygon.type == PolyTypes.POLYTYPE_OFFMESH_CONNECTION ) continue;
					var low = tile.verts[polygon.verts[0]];
					var high = low;
					for ( int i = 1; i < polygon.vertCount; i++ )
					{
						low = Vector3.Min( low, tile.verts[polygon.verts[i]] );
						high = Vector3.Max( high, tile.verts[polygon.verts[i]] );
					}
					if ( Geometry.OverlapBounds( minimum, maximum, low, high ) ) { Current = candidate; return true; }
				}
			}
			return false;
		}
	}
}
