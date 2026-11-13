namespace Nav;

/// <summary>
/// Which polygons of one tile hang together under one area mask, by the edges inside the tile.
/// Computed once per tile build and mask, on first need, and shared by the tile objects of the build.
/// </summary>
internal sealed class TileIslands
{
	public const ushort None = 0xFFFF;

	public readonly ulong Forbidden;

	// Per polygon its component, counted from 0, or None for a polygon the mask forbids.
	public readonly ushort[] Component;
	public readonly int Count;
	public TileIslands? Next;

	public TileIslands( Tile tile, ulong forbidden )
	{
		var polys = tile.Polys;
		var component = new ushort[polys.Length];
		var queue = new ushort[polys.Length];
		Array.Fill( component, None );
		int count = 0;
		for ( int first = 0; first < polys.Length; first++ )
		{
			if ( component[first] != None || (forbidden >> polys[first].Area & 1) != 0 ) continue;
			component[first] = (ushort)count;
			queue[0] = (ushort)first;
			for ( int head = 0, tail = 1; head < tail; head++ )
			{
				ref readonly var poly = ref polys[queue[head]];
				for ( int j = 0; j < poly.VertCount; j++ )
				{
					int nei = poly.Neis[j];
					if ( nei >= Poly.Border || component[nei] != None || (forbidden >> polys[nei].Area & 1) != 0 ) continue;
					component[nei] = (ushort)count;
					queue[tail++] = (ushort)nei;
				}
			}
			count++;
		}
		Forbidden = forbidden;
		Component = component;
		Count = count;
	}
}

/// <summary>
/// The connections of one tile object's components into the components of its neighbour tiles, under
/// one mask. Each entry is component &lt;&lt; 48 | target slot &lt;&lt; 16 | target component. A tile gets a new
/// object whenever a neighbour changes, so this never goes stale.
/// </summary>
internal sealed class TileEdges
{
	public readonly ulong Forbidden;
	public readonly ulong[] Edges;
	public TileEdges? Next;

	public TileEdges( Tile tile, MeshState s, ulong forbidden )
	{
		Forbidden = forbidden;
		var ext = tile.Ext;
		var mine = tile.Islands( forbidden ).Component;
		var all = ext.Length == 0 ? [] : new ulong[ext.Length];
		int count = 0;
		foreach ( ref readonly var link in ext.AsSpan() )
		{
			ushort from = mine[link.Poly];
			if ( from == TileIslands.None || (forbidden >> link.Area & 1) != 0 ) continue;
			ushort to = s.Get( link.Target ).Islands( forbidden ).Component[link.Target & PolyRef.PolyMask];
			all[count++] = (ulong)from << 48 | (ulong)(link.Target >> PolyRef.PolyBits) << 16 | to;
		}

		Array.Sort( all, 0, count );
		int unique = 0;
		for ( int i = 0; i < count; i++ )
		{
			if ( unique == 0 || all[i] != all[unique - 1] ) all[unique++] = all[i];
		}
		Edges = unique == all.Length ? all : all.AsSpan( 0, unique ).ToArray();
	}
}

/// <summary>
/// Polygons with the same label can reach each other under one area mask. Two levels: the components
/// inside each tile, kept with the tile, and here a label per component, from a union over the
/// connections between tiles and over links. Building it costs per tile, not per polygon.
/// </summary>
internal sealed class IslandMap
{
	public readonly ulong Forbidden;

	// Labels of a tile's components start at First[slot].
	public readonly int[] Labels;
	public readonly int[] First;
	public IslandMap? Next;
	readonly MeshState state;

	public IslandMap( MeshState s, ulong forbidden )
	{
		state = s;
		Forbidden = forbidden;
		var pages = s.Pages;
		var first = new int[pages.Length * MeshState.PageSize];
		int total = 0;
		for ( int page = 0; page < pages.Length; page++ )
		{
			var tiles = pages[page];
			if ( tiles == null ) continue;
			for ( int i = 0; i < tiles.Length; i++ )
			{
				first[page * MeshState.PageSize + i] = total;
				if ( tiles[i] is { } tile ) total += tile.Islands( forbidden ).Count;
			}
		}

		var parent = new int[total];
		for ( int i = 0; i < parent.Length; i++ ) parent[i] = i;

		foreach ( var tiles in pages )
		{
			if ( tiles == null ) continue;
			foreach ( var tile in tiles )
			{
				if ( tile == null ) continue;
				int from = first[tile.Slot];
				foreach ( ulong edge in tile.Edges( s, forbidden ).Edges )
					Union( parent, from + (int)(edge >> 48), first[(int)(edge >> 16 & 0xFFFFFFFF)] + (int)(edge & 0xFFFF) );
			}
		}

		// A one-way link joins islands like any other: leaving it out would refuse the way forward
		// across a link that is the only connection.
		foreach ( ref readonly var link in s.Links.AsSpan() )
		{
			if ( link.Serial == 0 || !link.Connected || (forbidden >> link.Def.Area & 1) != 0 ) continue;
			int a = Component( link.StartPoly, first ), b = Component( link.EndPoly, first );
			if ( a >= 0 && b >= 0 ) Union( parent, a, b );
		}

		for ( int i = 0; i < parent.Length; i++ ) parent[i] = Root( parent, i );
		Labels = parent;
		First = first;
	}

	int Component( uint id, int[] first )
	{
		ushort component = state.Get( id ).Islands( Forbidden ).Component[id & PolyRef.PolyMask];
		return component == TileIslands.None ? -1 : first[id >> PolyRef.PolyBits] + component;
	}

	// -1 for a polygon the mask forbids.
	public int Of( uint id )
	{
		int component = Component( id, First );
		return component < 0 ? -1 : Labels[component];
	}

	static int Root( int[] parent, int i )
	{
		while ( parent[i] != i ) i = parent[i] = parent[parent[i]];
		return i;
	}

	static void Union( int[] parent, int a, int b )
	{
		a = Root( parent, a );
		b = Root( parent, b );
		if ( a != b ) parent[Math.Max( a, b )] = Math.Min( a, b );
	}
}
