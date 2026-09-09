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

using Sandbox;

namespace Sandbox.Navigation.Pathfinding;

internal sealed class PathSearch
{
	private readonly NavMeshGraph mesh;
	private readonly MeshQuery geometry;
	private readonly SearchNodePool nodes = new();
	private readonly SearchQueue open = new();
	private TraversalFilter filter;
	private long targetPolygon;
	private Vector3 target;
	private SearchNode closest;
	private float closestDistance;
	private Status status;

	internal PathSearch( NavMeshGraph mesh, MeshQuery geometry ) { this.mesh = mesh; this.geometry = geometry; }

	internal Status Begin( long startPolygon, long targetPolygon, Vector3 start, Vector3 target, TraversalFilter filter )
	{
		open.Clear();
		nodes.Clear();
		closest = null;
		status = Status.Failure | Status.InvalidInput;
		if ( !mesh.IsValidPolyRef( startPolygon ) || !mesh.IsValidPolyRef( targetPolygon )
			|| !Geometry.IsFinite( start ) || !Geometry.IsFinite( target ) ) return status;
		this.filter = filter;
		this.targetPolygon = targetPolygon;
		this.target = target;
		closest = nodes.GetNode( startPolygon );
		closest.pos = start;
		closest.flags = NodeFlags.NODE_OPEN;
		closestDistance = Vector3.DistanceBetween( start, target );
		closest.total = closestDistance * 0.999f;
		open.Push( closest );
		return status = Status.Running;
	}

	internal Status Advance( int iterations )
	{
		if ( !status.InProgress() ) return status;
		for ( int iteration = 0; iteration < iterations && !open.IsEmpty(); iteration++ )
		{
			var current = open.Pop();
			current.flags = NodeFlags.NODE_CLOSED;
			if ( current.id == targetPolygon ) { closest = current; return status = Status.Success; }
			if ( mesh.GetTileAndPolyByRef( current.id, out var tile, out var polygon ).Failed() ) return status = Status.Failure;
			long parent = nodes.GetNodeAtIdx( current.pidx )?.id ?? 0;
			for ( int linkIndex = polygon.firstLink; linkIndex != MeshConstants.NULL_LINK; linkIndex = tile.links[linkIndex].next )
			{
				var link = tile.links[linkIndex];
				long reference = link.refs;
				if ( reference == 0 || reference == parent ) continue;
				if ( mesh.GetTileAndPolyByRef( reference, out var nextTile, out var nextPolygon ).Failed() || !filter.Allows( nextPolygon.area ) ) continue;
				// Different entry sides into a tile need distinct search nodes.
				var next = nodes.GetNode( reference, link.side == 0xff ? 0 : link.side >> 1 );
				var position = next.pos;
				var edgeStatus = reference == targetPolygon
					? geometry.GetEdgeIntersectionPoint( current.pos, current.id, ref polygon, tile, target, reference, ref nextPolygon, nextTile, ref position )
					: geometry.GetEdgeMidPoint( current.id, ref polygon, tile, reference, ref nextPolygon, nextTile, ref position );
				if ( edgeStatus.Failed() ) continue;
				float cost = current.cost + filter.Cost( current.pos, position, polygon.area );
				float distance = Vector3.DistanceBetween( position, target );
				if ( reference == targetPolygon ) cost += filter.Cost( position, target, nextPolygon.area );
				float total = cost + (reference == targetPolygon ? 0 : distance * 0.999f);
				if ( next.flags != 0 && total >= next.total ) continue;
				next.pidx = nodes.GetNodeIdx( current );
				next.pos = position;
				next.cost = cost;
				next.total = total;
				if ( (next.flags & NodeFlags.NODE_OPEN) != 0 ) open.Modify( next );
				else open.Push( next );
				next.flags = NodeFlags.NODE_OPEN;
				if ( distance < closestDistance ) { closestDistance = distance; closest = next; }
			}
		}
		if ( open.IsEmpty() ) status = Status.Success | Status.Partial;
		return status;
	}

	internal Status Finish( List<long> path )
	{
		path.Clear();
		if ( status.Failed() || closest is null ) return Status.Failure;
		var node = closest;
		// Bound parent traversal even if a malformed graph introduced a cycle.
		for ( int remaining = nodes.GetNodeCount(); node is not null && remaining > 0; remaining-- )
		{
			path.Add( node.id );
			node = nodes.GetNodeAtIdx( node.pidx );
		}
		if ( node is not null ) { path.Clear(); return Status.Failure; }
		path.Reverse();
		return closest.id == targetPolygon ? Status.Success : Status.Success | Status.Partial;
	}
}

[SkipHotload]
internal class SearchNode
{
	public readonly int ptr;
	internal int QueueIndex = -1;

	public Vector3 pos; // Position of the node.
	public float cost; // Cost from previous node to current node.
	public float total; // Cost up to the node.
	public int pidx; // Index to parent node.
	public int state; // extra state information. A polyRef can have multiple nodes with different extra info. see MAX_STATES_PER_NODE
	public int flags; // Node flags. A combination of NodeFlags.
	public long id; // Polygon ref the node corresponds to.

	public SearchNode( int ptr )
	{
		this.ptr = ptr;
	}

	public static int ComparisonNodeTotal( SearchNode a, SearchNode b )
	{
		int compare = a.total.CompareTo( b.total );
		if ( 0 != compare )
			return compare;

		return a.ptr.CompareTo( b.ptr );
	}

	public override string ToString()
	{
		return $"Node [ptr={ptr} id={id} cost={cost} total={total}]";
	}
}

// Reuses search nodes and hash storage between queries.
internal class SearchNodePool
{
	private const int HashSize = 1024; // power of two

	private SearchNode[] _nodes;
	private int[] _next;
	private readonly int[] _first;
	private int _nodeCount;

	public SearchNodePool()
	{
		_nodes = new SearchNode[128];
		_next = new int[128];
		_first = new int[HashSize];
		Array.Fill( _first, -1 );
	}

	private static int HashRef( long id )
	{
		ulong h = (ulong)id * 0x9E3779B97F4A7C15UL;
		return (int)(h >> 32) & (HashSize - 1);
	}

	public void Clear()
	{
		// Local movement visits few polygons; clear only their buckets.
		if ( _nodeCount < 128 )
		{
			for ( int i = 0; i < _nodeCount; i++ ) _first[HashRef( _nodes[i].id )] = -1;
		}
		else Array.Fill( _first, -1 );
		_nodeCount = 0;
	}

	public int GetNodeCount()
	{
		return _nodeCount;
	}

	public SearchNode GetNode( long id, int state )
	{
		int bucket = HashRef( id );

		for ( int i = _first[bucket]; i != -1; i = _next[i] )
		{
			var node = _nodes[i];
			if ( node.id == id && node.state == state )
				return node;
		}

		return Create( id, state, bucket );
	}

	private SearchNode Create( long id, int state, int bucket )
	{
		if ( _nodeCount >= _nodes.Length )
		{
			Array.Resize( ref _nodes, _nodes.Length * 2 );
			Array.Resize( ref _next, _next.Length * 2 );
		}

		int i = _nodeCount++;
		var node = _nodes[i] ??= new SearchNode( i );
		node.pos = Vector3.Zero; // reset: nodes are reused across Clear()
		node.pidx = 0;
		node.cost = 0;
		node.total = 0;
		node.id = id;
		node.state = state;
		node.flags = 0;

		_next[i] = _first[bucket];
		_first[bucket] = i;

		return node;
	}

	public int GetNodeIdx( SearchNode node )
	{
		return node != null
			? node.ptr + 1
			: 0;
	}

	public SearchNode GetNodeAtIdx( int idx )
	{
		return idx != 0
			? _nodes[idx - 1]
			: null;
	}

	public SearchNode GetNode( long refs )
	{
		return GetNode( refs, 0 );
	}

}

internal class SearchQueue
{
	private readonly List<SearchNode> heap = new();

	public int Count() => heap.Count;
	public bool IsEmpty() => heap.Count == 0;
	public SearchNode Peek() => heap[0];

	public void Clear()
	{
		foreach ( var node in heap ) node.QueueIndex = -1;
		heap.Clear();
	}

	public void Push( SearchNode node )
	{
		node.QueueIndex = heap.Count;
		heap.Add( node );
		Up( node.QueueIndex );
	}

	public SearchNode Pop()
	{
		var result = heap[0];
		var last = heap[^1];
		heap.RemoveAt( heap.Count - 1 );
		if ( heap.Count > 0 )
		{
			heap[0] = last;
			last.QueueIndex = 0;
			Down( 0 );
		}
		result.QueueIndex = -1;
		return result;
	}

	public void Modify( SearchNode node )
	{
		// A* decreases priorities, but support increases too.
		int index = node.QueueIndex;
		if ( index < 0 ) { Push( node ); return; }
		Up( index );
		Down( node.QueueIndex );
	}

	private void Up( int index )
	{
		while ( index > 0 )
		{
			int parent = (index - 1) / 2;
			if ( SearchNode.ComparisonNodeTotal( heap[parent], heap[index] ) <= 0 ) break;
			Swap( index, parent );
			index = parent;
		}
	}

	private void Down( int index )
	{
		while ( index * 2 + 1 < heap.Count )
		{
			int child = index * 2 + 1;
			if ( child + 1 < heap.Count && SearchNode.ComparisonNodeTotal( heap[child + 1], heap[child] ) < 0 ) child++;
			if ( SearchNode.ComparisonNodeTotal( heap[index], heap[child] ) <= 0 ) break;
			Swap( index, child );
			index = child;
		}
	}

	private void Swap( int a, int b )
	{
		(heap[a], heap[b]) = (heap[b], heap[a]);
		heap[a].QueueIndex = a;
		heap[b].QueueIndex = b;
	}
}

internal static class NodeFlags
{
	public const int NODE_OPEN = 0x01;
	public const int NODE_CLOSED = 0x02;
	public const int NODE_PARENT_DETACHED = 0x04; // parent of the node is not adjacent. Found using raycast.
}
