using System.Numerics;
using System.Runtime.CompilerServices;

namespace Nav;

public sealed partial class NavQuery
{
	// One node per polygon, holding the best route into it found so far: where it crosses into the
	// polygon, what it costs up to there, and Rest: what its total adds to Cost, the estimate to the
	// target or, on the target polygon, the cost of the last leg.
	// A route is priced as it is walked, not from portal to portal: Apex is its last corner, ApexCost
	// what it costs up to that, and Left and Right bound what the corner sees through the portals
	// since. A side equal to Apex is open. Bits holds the open and closed flags, whether Left and Right
	// are ends of the portal the route came in by, and above them link id + 1 when the way in is a link.
	struct Node
	{
		public Vector3 Pos;
		public float Cost, Rest;
		public uint Id;
		public int Parent;
		public uint Bits;
	}

	struct Wedge
	{
		public Vector3 Apex, Left, Right;
		public float ApexCost, Extra;
	}

	// What a route continues from: the node and funnel of the polygon it leaves
	struct Route
	{
		public Vector3 Pos, Apex, Left, Right;
		public float Cost, ApexCost, Extra;
		public uint Bits;
	}

	struct TableSlot
	{
		public uint Id, Stamp;
		public int Node;
	}

	const uint OpenFlag = 1, ClosedFlag = 2, LeftOwn = 4, RightOwn = 8;
	const int StateShift = 4;

	Node[] nodes = new Node[64];
	Wedge[] wedges = new Wedge[64];
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
		if ( wedges.Length < nodes.Length ) Array.Resize( ref wedges, nodes.Length );
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
	/// A* over polygons: cost is distance times the area multiplier, for a link also times the
	/// multiplier of its type and plus the penalty of its type. Distance is measured the way the path
	/// is walked, straight from corner to corner through the portals, so a walk and a link are priced
	/// alike whatever the size of the polygons. Only a bend round a corner of an earlier polygon is
	/// taken through the point the route entered by instead. The estimate is 0.999 of the
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
		searchTarget = targetId;
		searchTargetPoint = target;
		searchClosest = SearchNode( Run( s, startId >> s.PolyBits ) + (int)(startId & s.PolyMask), startId, out _ );
		searchClosestDistance = Vector3.Distance( start, target );
		nodes[searchClosest].Pos = start;
		wedges[searchClosest] = new Wedge { Apex = start, Left = start, Right = start };
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
		int bits = s.PolyBits;
		uint mask = s.PolyMask;
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
			ref readonly var wedge = ref wedges[current];
			var from = new Route { Pos = node.Pos, Cost = node.Cost, Bits = node.Bits, Apex = wedge.Apex, Left = wedge.Left, Right = wedge.Right, ApexCost = wedge.ApexCost, Extra = wedge.Extra };
			uint parent = node.Parent >= 0 ? nodes[node.Parent].Id : PolyRef.NoId;

			var tile = s.Get( id );
			int p = (int)(id & mask);
			uint tileBase = id & ~mask;
			int run = Run( s, id >> bits );
			var polys = tile.Polys;
			var verts = tile.Verts;
			ref readonly var poly = ref polys[p];
			float areaCost = filter.AreaCosts[poly.Area];
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
					int index = SearchNode( run + nei, next, out _ );
					Enter( index, current, from, verts[poly.Verts[j]], verts[poly.Verts[j + 1 < n ? j + 1 : 0]], areaCost, filter.AreaCosts[area] );
					continue;
				}

				var ext = tile.Ext;
				for ( int e = FirstExt( ext, p, j ); e < ext.Length && ext[e].Poly == p && ext[e].Edge == j; e++ )
				{
					ref readonly var link = ref ext[e];
					if ( link.Target == parent ) continue;
					byte area = link.Area;
					if ( !filter.Allows( area ) ) continue;
					int index = SearchNode( Run( s, link.Target >> bits ) + (int)(link.Target & mask), link.Target, out _ );
					Vector3 a = verts[poly.Verts[j]], b = verts[poly.Verts[j + 1 < n ? j + 1 : 0]];
					if ( link.TMin == 0 && link.TMax == 255 ) Enter( index, current, from, a, b, areaCost, filter.AreaCosts[area] );
					else Enter( index, current, from, Vector3.Lerp( a, b, link.TMin * (1f / 255f) ), Vector3.Lerp( a, b, link.TMax * (1f / 255f) ), areaCost, filter.AreaCosts[area] );
				}
			}

			var attach = tile.Attach;
			if ( attach.Length == 0 ) continue;
			for ( int e = FirstAttach( attach, p ); e < attach.Length && attach[e].Poly == p; e++ )
			{
				ref readonly var link = ref s.Link( attach[e].Link );
				bool forward = link.StartPoly == id;
				uint next = forward ? link.EndPoly : link.StartPoly;
				int type = link.Def.Type;
				if ( next == parent || !filter.Allows( link.Def.Area ) || !filter.AllowsLink( type ) ) continue;
				byte area = s.Get( next ).Polys[next & mask].Area;
				if ( !filter.Allows( area ) ) continue;

				// Walk to the entry on this polygon, pay the penalty of the link's type, then the link
				// itself at the cost of its area and type.
				Vector3 entry = forward ? link.StartPoint : link.EndPoint, exit = forward ? link.EndPoint : link.StartPoint;
				int index = SearchNode( Run( s, next >> bits ) + (int)(next & mask), next, out _ );
				float crossing = filter.LinkPenalties[type] + Vector3.Distance( entry, exit ) * filter.AreaCosts[link.Def.Area] * filter.LinkCosts[type];
				EnterByLink( index, current, from, entry, exit, areaCost, crossing, filter.AreaCosts[area], attach[e].Link + 1u );
			}
		}

		SearchExpansions = expansions;
		searchEnd = searchClosest;
		return reached;
	}

	// The route of from, continued through the portal from left to right into the polygon of node
	// index: taken when it is cheaper than the route the node has. cost is the multiplier of the
	// polygon it leaves, nextCost of the one it enters. The funnel takes the portal the way Pull does.
	// The JIT inlines nothing with more than 32 locals: a local more and Expand calls this.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	void Enter( int index, int current, in Route from, Vector3 left, Vector3 right, float cost, float nextCost )
	{
		// A polygon the search has gone on from keeps its route, so no route can lead back into itself.
		if ( (nodes[index].Bits & ClosedFlag) != 0 ) return;

		Vector3 apex = from.Apex, sideLeft = from.Left, sideRight = from.Right;
		// extra is what the ground from the corner to anchor is longer than its length in plan
		float apexCost = from.ApexCost, extra = from.Extra;
		Vector3 anchor = from.Pos;
		uint own = 0;
		// 1 when the right end crossed over the left side, 2 the other way round
		int bend = 0;

		if ( Geo.TriArea2D( apex, sideRight, right ) <= 0 )
		{
			if ( apex == sideRight || right == sideRight || Geo.TriArea2D( apex, sideLeft, right ) >= 0 )
			{
				sideRight = right;
				own |= RightOwn;
			}
			else bend = 1;
		}
		if ( bend == 0 && Geo.TriArea2D( apex, sideLeft, left ) >= 0 )
		{
			if ( apex == sideLeft || left == sideLeft || Geo.TriArea2D( apex, sideRight, left ) <= 0 )
			{
				sideLeft = left;
				own |= LeftOwn;
			}
			else bend = 2;
		}

		if ( bend != 0 )
		{
			// The route bends at the side it crossed. A corner of the polygon it is leaving sees the
			// whole portal. One further back may not, and the route then goes on from where it came in.
			Vector3 corner = bend == 1 ? sideLeft : sideRight;
			if ( ((from.Bits | own) & (bend == 1 ? LeftOwn : RightOwn)) != 0 )
			{
				apexCost += (Flat( apex, corner ) + extra + Excess( anchor, corner )) * cost;
				apex = corner;
			}
			else
			{
				apex = from.Pos;
				apexCost = from.Cost;
			}
			extra = 0;
			anchor = apex;
			sideLeft = left;
			sideRight = right;
			own = LeftOwn | RightOwn;
		}

		// The part of the portal the corner sees, from 0 at left to 1 at right
		float low = 0, high = 1;
		if ( (own & LeftOwn) == 0 && Seen( apex, sideLeft, left, right, 1e-6f * areaScale, out float seen ) ) low = seen > 0 ? MathF.Min( seen, 1 ) : 0;
		if ( (own & RightOwn) == 0 && Seen( apex, sideRight, left, right, 1e-6f * areaScale, out seen ) ) high = seen < 1 ? MathF.Max( seen, 0 ) : 1;
		if ( low > high ) low = high = (low + high) * 0.5f;

		Vector3 target = searchTargetPoint;
		float aim = Aim( apex, target, left, right ), crossing = aim > low ? MathF.Min( aim, high ) : low;
		Vector3 position = Vector3.Lerp( left, right, crossing );

		ref var node = ref nodes[index];
		float last = node.Id == searchTarget ? nextCost : 0.999f, distance = Vector3.Distance( position, target );
		float over = extra + Excess( anchor, position );
		float entered = apexCost + (Flat( apex, position ) + over) * cost, total = entered + distance * last;

		// The shortest way lies beside what the corner sees. It bends at the side in the way when that
		// is a corner of the polygon it leaves, else it may be shorter from where the route came in.
		if ( (aim < low && low > 0) || (aim > high && high < 1) )
		{
			bool leftSide = aim < low;
			Vector3 pivot = from.Pos;
			float pivotCost = from.Cost;
			if ( (from.Bits & (leftSide ? LeftOwn : RightOwn)) != 0 )
			{
				pivot = leftSide ? sideLeft : sideRight;
				pivotCost = apexCost + (Flat( apex, pivot ) + extra + Excess( anchor, pivot )) * cost;
			}
			float turn = Aim( pivot, target, left, right );
			Vector3 turned = Vector3.Lerp( left, right, turn > 0 ? MathF.Min( turn, 1 ) : 0 );
			float turnedDistance = Vector3.Distance( turned, target ), turnedOver = Excess( pivot, turned ), turnedCost = pivotCost + (Flat( pivot, turned ) + turnedOver) * cost;
			if ( turnedCost + turnedDistance * last < total )
			{
				apex = pivot;
				apexCost = pivotCost;
				sideLeft = left;
				sideRight = right;
				own = LeftOwn | RightOwn;
				position = turned;
				over = turnedOver;
				distance = turnedDistance;
				entered = turnedCost;
				total = turnedCost + turnedDistance * last;
			}
		}

		float had = node.Cost + node.Rest;
		// Of two routes that cost the same the one with its corner further back goes on straighter.
		// Written so that a total that is not a number never counts as cheaper.
		if ( !(total < had) && !(total == had && apexCost < wedges[index].ApexCost) ) return;

		node.Parent = current;
		node.Pos = position;
		node.Cost = entered;
		node.Rest = distance * last;
		if ( cost == nextCost ) wedges[index] = new Wedge { Apex = apex, Left = sideLeft, Right = sideRight, ApexCost = apexCost, Extra = over };
		else
		{
			// A straight stretch is priced at one multiplier, so it ends where the multiplier changes.
			wedges[index] = new Wedge { Apex = position, Left = position, Right = position, ApexCost = entered };
			own = 0;
		}
		node.Bits = own | OpenFlag;
		Push( total, index );
		if ( distance < searchClosestDistance )
		{
			searchClosestDistance = distance;
			searchClosest = index;
		}
	}

	// A straight stretch is straight in plan only: over a ridge it is as long as the ground. It is
	// priced as its length in plan plus what the ground was longer than that from crossing to crossing.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	static float Flat( Vector3 from, Vector3 to )
	{
		float x = to.X - from.X, y = to.Y - from.Y;
		return MathF.Sqrt( x * x + y * y );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	static float Excess( Vector3 from, Vector3 to )
	{
		float z = to.Z - from.Z;
		if ( z == 0 ) return 0;
		float x = to.X - from.X, y = to.Y - from.Y, flat = x * x + y * y;
		return MathF.Sqrt( flat + z * z ) - MathF.Sqrt( flat );
	}

	// Where on the line of a portal the way from a point to the target over it is shortest, 0 at left
	// and 1 at right: it divides the way along the portal between the two in the ratio of how far
	// each is off its line.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	static float Aim( Vector3 from, Vector3 target, Vector3 left, Vector3 right )
	{
		float alongX = right.X - left.X, alongY = right.Y - left.Y, length = alongX * alongX + alongY * alongY;
		if ( !(length > 0) ) return 0.5f;
		float fromAlong = (from.X - left.X) * alongX + (from.Y - left.Y) * alongY, targetAlong = (target.X - left.X) * alongX + (target.Y - left.Y) * alongY;
		float fromOff = MathF.Abs( alongX * (from.Y - left.Y) - alongY * (from.X - left.X) ), targetOff = MathF.Abs( alongX * (target.Y - left.Y) - alongY * (target.X - left.X) );
		float off = fromOff + targetOff;
		return off > 0 ? (fromAlong * off + (targetAlong - fromAlong) * fromOff) / (off * length) : targetAlong / length;
	}

	// Where the line from a corner through a point meets the line of a portal, 0 at left and 1 at right.
	// False when they are parallel.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	static bool Seen( Vector3 corner, Vector3 through, Vector3 left, Vector3 right, float epsilon, out float along )
	{
		float ux = through.X - corner.X, uy = through.Y - corner.Y, vx = right.X - left.X, vy = right.Y - left.Y;
		float d = ux * vy - uy * vx;
		along = 0;
		if ( MathF.Abs( d ) < epsilon ) return false;
		along = (ux * (corner.Y - left.Y) - uy * (corner.X - left.X)) / d;
		return true;
	}

	// The route of from, walked on to the entry of a link and over it into the polygon of node index.
	// crossing is what the link itself costs.
	void EnterByLink( int index, int current, in Route from, Vector3 entry, Vector3 exit, float cost, float crossing, float nextCost, uint link )
	{
		if ( (nodes[index].Bits & ClosedFlag) != 0 ) return;

		// Straight from the corner when it sees the entry, else round the side in the way
		Vector3 apex = from.Apex;
		bool pastLeft = Geo.TriArea2D( apex, from.Left, entry ) < 0, pastRight = Geo.TriArea2D( apex, from.Right, entry ) > 0;
		float walked;
		if ( !pastLeft && !pastRight ) walked = from.ApexCost + (Flat( apex, entry ) + from.Extra + Excess( from.Pos, entry )) * cost;
		else if ( (from.Bits & (pastLeft ? LeftOwn : RightOwn)) != 0 )
		{
			Vector3 corner = pastLeft ? from.Left : from.Right;
			walked = from.ApexCost + (Flat( apex, corner ) + from.Extra + Excess( from.Pos, corner ) + Vector3.Distance( corner, entry )) * cost;
		}
		else walked = from.Cost + Vector3.Distance( from.Pos, entry ) * cost;

		ref var node = ref nodes[index];
		float distance = Vector3.Distance( exit, searchTargetPoint );
		float entered = walked + crossing, rest = distance * (node.Id == searchTarget ? nextCost : 0.999f), total = entered + rest;
		if ( !(total < node.Cost + node.Rest) ) return;

		node.Parent = current;
		node.Pos = exit;
		node.Cost = entered;
		wedges[index] = new Wedge { Apex = exit, Left = exit, Right = exit, ApexCost = entered };
		node.Rest = rest;
		node.Bits = link << StateShift | OpenFlag;
		Push( total, index );
		if ( distance < searchClosestDistance )
		{
			searchClosestDistance = distance;
			searchClosest = index;
		}
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
			if ( link != 0 ) buffer[--at] = PolyRef.ForLink( s.Link( (int)(link - 1) ).Serial, (int)(link - 1) );
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
			int node = cells[Run( s, id >> s.PolyBits ) + (int)(id & s.PolyMask)] - 1;
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
	/// The island map of a state for the area and link type masks of the filter, built on first need
	/// and kept with the state. Finding a map that exists takes no lock. Threads that miss it queue
	/// behind one builder per mesh, so a state gets each map once and a map starts from the last one
	/// built for its area mask on any state of the mesh.
	/// </summary>
	internal static IslandMap GetIslands( MeshState s, in NavFilter filter )
	{
		ulong forbidden = filter.ForbiddenAreas, forbiddenLinks = filter.ForbiddenLinks;
		for ( var map = Volatile.Read( ref s.Islands ); map != null; map = map.Next )
		{
			if ( map.Forbidden == forbidden && map.ForbiddenLinks == forbiddenLinks ) return map;
		}

		// Taken only on the miss path, never by a reader that finds the value
		var latest = s.LatestIslands;
		lock ( latest )
		{
			for ( var map = s.Islands; map != null; map = map.Next )
			{
				if ( map.Forbidden == forbidden && map.ForbiddenLinks == forbiddenLinks ) return map;
			}
			int at = 0;
			while ( at < latest.Count && latest[at].Forbidden != forbidden ) at++;
			var built = new IslandMap( s, forbidden, forbiddenLinks, at < latest.Count ? latest[at] : null ) { Next = s.Islands };
			if ( at < latest.Count ) latest[at] = built;
			else latest.Add( built );
			Volatile.Write( ref s.Islands, built );
			return built;
		}
	}
}
