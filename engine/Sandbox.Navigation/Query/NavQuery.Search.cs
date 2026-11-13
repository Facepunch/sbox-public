using System.Numerics;
using System.Runtime.CompilerServices;

namespace Nav;

public sealed partial class NavQuery
{
	// One node per polygon. Its position is fixed by its first visit, and with it Rest: what its total
	// adds to Cost, the estimate to the target or, on the target polygon, the cost of the last leg.
	// Bits holds the open and closed flags and, above them, link id + 1 when the best way in is a link.
	struct Node
	{
		public Vector3 Pos;
		public float Cost, Rest;
		public uint Id;
		public int Parent;
		public uint Bits;
	}

	struct TableSlot
	{
		public uint Id, Stamp;
		public int Node;
	}

	const uint OpenFlag = 1, ClosedFlag = 2;
	const int StateShift = 2;

	Node[] nodes = new Node[64];
	int nodeCount;

	// Slots carry the stamp of the search that wrote them, so starting a search clears nothing.
	TableSlot[] table = new TableSlot[256];
	uint stamp;

	// A 4-ary heap of total << 32 | node: totals are never negative, so their bits order like the
	// floats and one compare orders by total, then node index. A node whose total improved is pushed
	// again and its old entry is skipped when it surfaces, so results do not depend on heap layout.
	ulong[] heap = new ulong[64];
	int heapCount;

	// One entry may wait in front of the heap, never larger than anything in it. A search mostly goes
	// on with the node it has just pushed, and that node then never enters the heap.
	const ulong NoFront = ulong.MaxValue;
	ulong front = NoFront;

	// The last search: where the corridor ends, what it aimed for, and how many nodes it expanded.
	int searchEnd, searchClosest;
	uint searchTarget;
	Vector3 searchTargetPoint;
	float searchClosestDistance;
	NavFilter searchFilter;
	internal int SearchExpansions;

	// A path search finds its nodes through the tile: every tile it touches gets a run of cells, one
	// per polygon, holding node index + 1. A neighbour in the same tile is then one load away, with no
	// hashing. The walks over a few polygons (moves, walls) use the hash table below instead, so that
	// they never clear a run for a large tile.
	struct TileRun
	{
		public uint Slot, Stamp;
		public int Offset;
	}

	const uint NoSlot = uint.MaxValue;
	TileRun[] runs = new TileRun[64];
	int[] cells = new int[1024];
	int runCount, cellCount, lastRunOffset;
	uint lastRunSlot = NoSlot;

	void BeginNodes()
	{
		nodeCount = 0;
		heapCount = 0;
		front = NoFront;
		runCount = 0;
		cellCount = 0;
		lastRunSlot = NoSlot;
		if ( ++stamp == 0 )
		{
			Array.Clear( table );
			Array.Clear( runs );
			stamp = 1;
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	int Run( MeshState s, uint slot ) => slot == lastRunSlot ? lastRunOffset : FindRun( s, slot );

	int FindRun( MeshState s, uint slot )
	{
		int mask = runs.Length - 1;
		int i = (int)(slot * 0x9E3779B1u >> 16) & mask;
		while ( runs[i].Stamp == stamp )
		{
			if ( runs[i].Slot == slot )
			{
				lastRunSlot = slot;
				return lastRunOffset = runs[i].Offset;
			}
			i = (i + 1) & mask;
		}

		int count = s.Pages[slot >> MeshState.PageBits]![slot & (MeshState.PageSize - 1)]!.Polys.Length;
		if ( cellCount + count > cells.Length ) Array.Resize( ref cells, Math.Max( cells.Length * 2, cellCount + count ) );
		Array.Clear( cells, cellCount, count );
		runs[i] = new TileRun { Slot = slot, Stamp = stamp, Offset = cellCount };
		lastRunSlot = slot;
		lastRunOffset = cellCount;
		cellCount += count;
		if ( ++runCount * 2 > runs.Length )
		{
			var old = runs;
			runs = new TileRun[old.Length * 2];
			foreach ( ref readonly var run in old.AsSpan() )
			{
				if ( run.Stamp != stamp ) continue;
				int at = (int)(run.Slot * 0x9E3779B1u >> 16) & (runs.Length - 1);
				while ( runs[at].Stamp == stamp ) at = (at + 1) & (runs.Length - 1);
				runs[at] = run;
			}
		}
		return lastRunOffset;
	}

	// The node of the polygon behind a cell, made on first use with an infinite cost, so that the
	// first route to it always improves it.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	int SearchNode( int cell, uint id, out bool created )
	{
		int found = cells[cell];
		created = found == 0;
		if ( !created ) return found - 1;
		if ( nodeCount == nodes.Length ) Array.Resize( ref nodes, nodeCount * 2 );
		nodes[nodeCount] = new Node { Id = id, Parent = -1, Cost = float.PositiveInfinity };
		cells[cell] = ++nodeCount;
		return nodeCount - 1;
	}

	static int Hash( uint id ) => (int)(id * 0x9E3779B1u >> 8);

	// The node of a polygon for the walks that do not search: found through a hash table.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	int GetNode( uint id, out bool created )
	{
		var slots = table;
		int mask = slots.Length - 1;
		int i = Hash( id ) & mask;
		while ( slots[i].Stamp == stamp )
		{
			if ( slots[i].Id == id )
			{
				created = false;
				return slots[i].Node;
			}
			i = (i + 1) & mask;
		}
		created = true;
		if ( nodeCount == nodes.Length ) Array.Resize( ref nodes, nodeCount * 2 );
		int index = nodeCount++;
		nodes[index] = new Node { Id = id, Parent = -1 };
		slots[i] = new TableSlot { Id = id, Stamp = stamp, Node = index };
		if ( nodeCount * 2 > slots.Length ) GrowTable();
		return index;
	}

	int FindNode( uint id )
	{
		var slots = table;
		int mask = slots.Length - 1;
		for ( int i = Hash( id ) & mask; slots[i].Stamp == stamp; i = (i + 1) & mask )
		{
			if ( slots[i].Id == id ) return slots[i].Node;
		}
		return -1;
	}

	void GrowTable()
	{
		var slots = new TableSlot[table.Length * 2];
		int mask = slots.Length - 1;
		for ( int n = 0; n < nodeCount; n++ )
		{
			int i = Hash( nodes[n].Id ) & mask;
			while ( slots[i].Stamp == stamp ) i = (i + 1) & mask;
			slots[i] = new TableSlot { Id = nodes[n].Id, Stamp = stamp, Node = n };
		}
		table = slots;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	void Push( float total, int node )
	{
		ulong item = (ulong)BitConverter.SingleToUInt32Bits( total ) << 32 | (uint)node;
		if ( front == NoFront )
		{
			if ( heapCount == 0 || item < heap[0] )
			{
				front = item;
				return;
			}
		}
		else if ( item < front ) (item, front) = (front, item);

		if ( heapCount == heap.Length ) Array.Resize( ref heap, heapCount * 2 );
		var items = heap;
		int i = heapCount++;
		while ( i > 0 )
		{
			int parent = (i - 1) >> 2;
			if ( item >= items[parent] ) break;
			items[i] = items[parent];
			i = parent;
		}
		items[i] = item;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	ulong Pop()
	{
		if ( front != NoFront )
		{
			ulong first = front;
			front = NoFront;
			return first;
		}

		var items = heap;
		ulong top = items[0], item = items[--heapCount];
		int count = heapCount, i = 0;
		while ( true )
		{
			int child = i * 4 + 1;
			if ( child >= count ) break;
			int last = Math.Min( child + 4, count );
			int least = child;
			for ( int c = child + 1; c < last; c++ )
			{
				if ( items[c] < items[least] ) least = c;
			}
			if ( items[least] >= item ) break;
			items[i] = items[least];
			i = least;
		}
		items[i] = item;
		return top;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	static int FirstExt( ExtLink[] ext, int poly, int edge )
	{
		int key = poly << 8 | edge, low = 0, high = ext.Length;
		while ( low < high )
		{
			int middle = (low + high) >> 1;
			if ( (ext[middle].Poly << 8 | ext[middle].Edge) < key ) low = middle + 1;
			else high = middle;
		}
		return low;
	}

	static int FirstAttach( LinkAttach[] attach, int poly )
	{
		int low = 0, high = attach.Length;
		while ( low < high )
		{
			int middle = (low + high) >> 1;
			if ( attach[middle].Poly < poly ) low = middle + 1;
			else high = middle;
		}
		return low;
	}

	// A search that has not reached its target after this many expansions asks the island map whether
	// it can. Shorter searches never need the map, so a local query after a commit builds nothing.
	const int IslandCheck = 256;

	/// <summary>
	/// A* over polygons: cost is distance times the area multiplier, the estimate is 0.999 of the
	/// straight distance, a hair under it so that rounding never lifts the estimate above the cost that
	/// is left. When the target turns out to be on another island it is replaced by the nearest
	/// polygon of the start island and the search starts over, so an unreachable goal does not flood
	/// the island. Searches of up to IslandCheck expansions never ask. Returns true when the target
	/// polygon was reached; searchEnd is then its node, else the node that came closest.
	/// </summary>
	bool Search( MeshState s, uint startId, uint targetId, Vector3 start, Vector3 target, in NavFilter filter, int maxExpansions )
	{
		Begin( s, startId, targetId, start, target, filter );
		bool reached = Expand( s, filter, Math.Min( maxExpansions, IslandCheck ) );
		if ( reached || maxExpansions <= IslandCheck ) return reached;

		var islands = GetIslands( s, filter );
		int island = islands.Of( startId );
		if ( island == islands.Of( targetId ) || !Nearest( s, target, new Vector3( float.PositiveInfinity ), filter, islands, island, out uint reachable, out var reachablePoint ) )
			return Expand( s, filter, maxExpansions );

		int wasted = SearchExpansions;
		Begin( s, startId, reachable, start, reachablePoint, filter );
		reached = Expand( s, filter, maxExpansions );
		SearchExpansions += wasted;
		return reached;
	}

	void Begin( MeshState s, uint startId, uint targetId, Vector3 start, Vector3 target, in NavFilter filter )
	{
		BeginNodes();
		searchFilter = filter;
		searchTarget = targetId;
		searchTargetPoint = target;
		searchClosest = SearchNode( Run( s, startId >> PolyRef.PolyBits ) + (int)(startId & PolyRef.PolyMask), startId, out _ );
		searchClosestDistance = Vector3.Distance( start, target );
		nodes[searchClosest].Pos = start;
		nodes[searchClosest].Cost = 0;
		nodes[searchClosest].Rest = searchClosestDistance * 0.999f;
		nodes[searchClosest].Bits |= OpenFlag;
		Push( nodes[searchClosest].Rest, searchClosest );
		searchEnd = searchClosest;
		SearchExpansions = 0;
	}

	// Runs the search until the target is reached, the open list is empty or limit nodes are expanded
	// in total. Can be called again to go on.
	bool Expand( MeshState s, in NavFilter filter, int limit )
	{
		uint targetId = searchTarget;
		bool reached = false;
		int expansions = SearchExpansions;
		while ( (heapCount > 0 || front != NoFront) && expansions < limit )
		{
			ulong top = Pop();
			int current = (int)(uint)top;
			ref var node = ref nodes[current];
			if ( (node.Bits & OpenFlag) == 0 || (uint)(top >> 32) != BitConverter.SingleToUInt32Bits( node.Cost + node.Rest ) ) continue;
			node.Bits = node.Bits & ~OpenFlag | ClosedFlag;
			expansions++;

			uint id = node.Id;
			if ( id == targetId )
			{
				searchClosest = current;
				reached = true;
				break;
			}

			// Relaxing may grow the node array, so nothing below holds on to node.
			Vector3 position = node.Pos;
			float cost = node.Cost;
			uint parent = node.Parent >= 0 ? nodes[node.Parent].Id : PolyRef.NoId;

			var tile = s.Get( id );
			int p = (int)(id & PolyRef.PolyMask);
			uint tileBase = id & ~PolyRef.PolyMask;
			int run = Run( s, id >> PolyRef.PolyBits );
			var polys = tile.Polys;
			var verts = tile.Verts;
			ref readonly var poly = ref polys[p];
			float areaCost = filter.Cost( poly.Area );
			int n = poly.VertCount;

			for ( int j = 0; j < n; j++ )
			{
				ushort nei = poly.Neis[j];
				if ( nei == Poly.Wall ) continue;

				if ( nei < Poly.Border )
				{
					uint next = tileBase | nei;
					if ( next == parent ) continue;
					byte area = polys[nei].Area;
					if ( !filter.Allows( area ) ) continue;
					int index = SearchNode( run + nei, next, out bool created );
					if ( created ) Place( index, position, verts[poly.Verts[j]], verts[poly.Verts[j + 1 < n ? j + 1 : 0]], area );
					Improve( index, current, cost, position, areaCost );
					continue;
				}

				var ext = tile.Ext;
				for ( int e = FirstExt( ext, p, j ); e < ext.Length && ext[e].Poly == p && ext[e].Edge == j; e++ )
				{
					ref readonly var link = ref ext[e];
					if ( link.Target == parent ) continue;
					byte area = link.Area;
					if ( !filter.Allows( area ) ) continue;
					int index = SearchNode( Run( s, link.Target >> PolyRef.PolyBits ) + (int)(link.Target & PolyRef.PolyMask), link.Target, out bool created );
					if ( created )
					{
						Vector3 a = verts[poly.Verts[j]], b = verts[poly.Verts[j + 1 < n ? j + 1 : 0]];
						if ( link.TMin == 0 && link.TMax == 255 ) Place( index, position, a, b, area );
						else Place( index, position, Vector3.Lerp( a, b, link.TMin * (1f / 255f) ), Vector3.Lerp( a, b, link.TMax * (1f / 255f) ), area );
					}
					Improve( index, current, cost, position, areaCost );
				}
			}

			var attach = tile.Attach;
			if ( attach.Length == 0 ) continue;
			for ( int e = FirstAttach( attach, p ); e < attach.Length && attach[e].Poly == p; e++ )
			{
				ref readonly var link = ref s.Links[attach[e].Link];
				bool forward = link.StartPoly == id;
				uint next = forward ? link.EndPoly : link.StartPoly;
				if ( next == parent || !filter.Allows( link.Def.Area ) ) continue;
				byte area = s.Get( next ).Polys[next & PolyRef.PolyMask].Area;
				if ( !filter.Allows( area ) ) continue;

				// Walk to the entry on this polygon, then the link itself at the cost of its area.
				Vector3 entry = forward ? link.StartPoint : link.EndPoint, exit = forward ? link.EndPoint : link.StartPoint;
				int index = SearchNode( Run( s, next >> PolyRef.PolyBits ) + (int)(next & PolyRef.PolyMask), next, out bool created );
				if ( created ) Place( index, entry, exit, exit, area );
				Improve( index, current, cost + Vector3.Distance( position, entry ) * areaCost, entry, filter.Cost( link.Def.Area ), attach[e].Link + 1u );
			}
		}

		SearchExpansions = expansions;
		searchEnd = searchClosest;
		return reached;
	}

	// First visit: fixes where the path enters the polygon, on the portal from left to right.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	void Place( int index, Vector3 from, Vector3 left, Vector3 right, byte area )
	{
		ref var node = ref nodes[index];
		Vector3 position;
		float distance;
		if ( node.Id != searchTarget )
		{
			position = (left + right) * 0.5f;
			distance = Vector3.Distance( position, searchTargetPoint );
			node.Rest = distance * 0.999f;
		}
		else
		{
			// Aim the last portal crossing at the target, away from the ends of the portal.
			float t = Geo.IntersectSegments( from, searchTargetPoint, left, right, 1e-6f * areaScale, out _, out float crossing ) ? Math.Clamp( crossing, 0.1f, 0.9f ) : 0.5f;
			position = Vector3.Lerp( left, right, t );
			distance = Vector3.Distance( position, searchTargetPoint );
			node.Rest = distance * searchFilter.Cost( area );
		}
		node.Pos = position;
		if ( distance < searchClosestDistance )
		{
			searchClosestDistance = distance;
			searchClosest = index;
		}
	}

	// Takes the route through current when it is cheaper than the one the node has.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	void Improve( int index, int current, float cost, Vector3 from, float fromCost, uint link = 0 )
	{
		ref var node = ref nodes[index];
		cost += Vector3.Distance( from, node.Pos ) * fromCost;
		float total = cost + node.Rest;
		// Written so that a total that is not a number never counts as cheaper.
		if ( !(total < node.Cost + node.Rest) ) return;
		node.Parent = current;
		node.Cost = cost;
		node.Bits = link << StateShift | OpenFlag;
		Push( total, index );
	}

	// Writes the polygons from the start to node end into buffer at offset, links in between, and
	// returns how many. 0 when the parent chain is broken.
	int Corridor( MeshState s, int end, ref PolyRef[] buffer, int offset )
	{
		int count = 0, steps = 0;
		for ( int i = end; i >= 0; i = nodes[i].Parent )
		{
			count += nodes[i].Bits >> StateShift != 0 ? 2 : 1;
			if ( ++steps > nodeCount ) return 0;
		}
		if ( buffer.Length < offset + count ) Array.Resize( ref buffer, Math.Max( offset + count, buffer.Length * 2 ) );

		int at = offset + count;
		for ( int i = end; i >= 0; i = nodes[i].Parent )
		{
			buffer[--at] = s.RefOf( nodes[i].Id );
			uint link = nodes[i].Bits >> StateShift;
			if ( link != 0 ) buffer[--at] = PolyRef.ForLink( s.Links[link - 1].Serial, (int)(link - 1) );
		}
		return count;
	}

	/// <summary>
	/// A bounded search from start that rejoins the corridor at the furthest polygon it reached,
	/// instead of planning the whole route again. False leaves the corridor as it was.
	/// </summary>
	internal bool Repair( MeshState s, PolyRef start, Vector3 position, Vector3 target, in NavFilter filter, ref PolyRef[] corridor, ref int count, int expansions )
	{
		if ( count == 0 || !s.TryGet( start, out _, out _ ) || !s.TryGet( corridor[count - 1], out _, out _ ) || !Geo.IsFinite( position ) || !Geo.IsFinite( target ) ) return false;
		Search( s, start.Id, corridor[count - 1].Id, position, target, filter, expansions );

		for ( int k = count - 1; k >= 0; k-- )
		{
			if ( !s.TryGet( corridor[k], out _, out _ ) ) continue;
			uint id = corridor[k].Id;
			int node = cells[Run( s, id >> PolyRef.PolyBits ) + (int)(id & PolyRef.PolyMask)] - 1;
			// Joining at the start node would leave the corridor unchanged.
			if ( node < 0 || nodes[node].Parent < 0 ) continue;

			int head = 0, steps = 0;
			for ( int i = node; i >= 0; i = nodes[i].Parent )
			{
				head += nodes[i].Bits >> StateShift != 0 ? 2 : 1;
				if ( ++steps > nodeCount ) return false;
			}
			int tail = count - (k + 1);
			if ( corridor.Length < head + tail ) Array.Resize( ref corridor, Math.Max( head + tail, corridor.Length * 2 ) );
			Array.Copy( corridor, k + 1, corridor, head, tail );
			Corridor( s, node, ref corridor, 0 );
			count = head + tail;
			return true;
		}
		return false;
	}

	/// <summary>
	/// The island map of a state for the area mask of the filter, built on first need and kept with the
	/// state. Finding a map that exists takes no lock. Threads that miss it at the same moment queue
	/// behind one builder, so a state gets each map once.
	/// </summary>
	internal static IslandMap GetIslands( MeshState s, in NavFilter filter )
	{
		ulong forbidden = filter.Forbidden;
		for ( var map = Volatile.Read( ref s.Islands ); map != null; map = map.Next )
		{
			if ( map.Forbidden == forbidden ) return map;
		}

		// Taken only on the miss path, never by a reader that finds the value
		lock ( s )
		{
			for ( var map = s.Islands; map != null; map = map.Next )
			{
				if ( map.Forbidden == forbidden ) return map;
			}
			var built = new IslandMap( s, forbidden ) { Next = s.Islands };
			Volatile.Write( ref s.Islands, built );
			return built;
		}
	}
}
