using System.Numerics;
using System.Runtime.InteropServices;

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
/// one mask. Each entry is component &lt;&lt; 32 | side &lt;&lt; 16 | target component, sides as in Poly. A tile
/// gets a new object whenever a neighbour changes, so this never goes stale.
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
			ushort to = s.Get( link.Target ).Islands( forbidden ).Component[link.Target & s.PolyMask];
			all[count++] = (ulong)from << 32 | (ulong)link.Side << 16 | to;
		}

		Array.Sort( all, 0, count );
		Edges = Unique( all, count );
	}

	public static ulong[] Unique( ulong[] sorted, int count )
	{
		int unique = 0;
		for ( int i = 0; i < count; i++ )
		{
			if ( unique == 0 || sorted[i] != sorted[unique - 1] ) sorted[unique++] = sorted[i];
		}
		return unique == sorted.Length ? sorted : sorted.AsSpan( 0, unique ).ToArray();
	}
}

/// <summary>
/// The components of the tiles of one block of 8 x 8 tile coordinates under one area mask, joined over
/// the connections between those tiles. It depends on nothing but the tile objects in the block, so
/// maps share it until one of them is replaced.
/// </summary>
internal sealed class IslandBlock
{
	public const int Bits = 3, Size = 1 << Bits, Mask = Size - 1;

	// Block coordinates: tile coordinates shifted right by Bits.
	public readonly int X, Y;

	// Per tile place in the block, see Place: where its components start in Labels, -1 for no tile.
	public readonly int[] First;

	// Per tile component its label, counted from 0 within the block.
	public readonly int[] Labels;
	public readonly int Count;
	public readonly bool Empty;

	// Connections into the neighbour blocks: label << 32 | side << 24 | place there << 16 | component there.
	public readonly ulong[] Out;

	public static int Place( int x, int y ) => (y & Mask) << Bits | x & Mask;

	public IslandBlock( MeshState s, int x, int y, ulong forbidden )
	{
		X = x;
		Y = y;
		var tiles = new Tile?[Size * Size];
		var first = new int[Size * Size];
		int total = 0;
		for ( int i = 0; i < tiles.Length; i++ )
		{
			var tile = tiles[i] = s.Find( x << Bits | i & Mask, y << Bits | i >> Bits );
			first[i] = tile == null ? -1 : total;
			if ( tile != null ) total += tile.Islands( forbidden ).Count;
		}

		var labels = new int[total];
		for ( int i = 0; i < total; i++ ) labels[i] = i;
		int outside = 0;
		for ( int i = 0; i < tiles.Length; i++ )
		{
			if ( tiles[i] is not { } tile ) continue;
			foreach ( ulong edge in tile.Edges( s, forbidden ).Edges )
			{
				int place = Inside( i, (int)(edge >> 16 & 0xFFFF), out _ );
				if ( place < 0 ) outside++;
				else IslandMap.Union( labels, first[i] + (int)(edge >> 32), first[place] + (int)(edge & 0xFFFF) );
			}
		}
		int count = 0;
		// Union keeps every parent below its child, so one pass in order relabels.
		for ( int i = 0; i < total; i++ ) labels[i] = labels[i] == i ? count++ : labels[labels[i]];

		var edges = outside == 0 ? [] : new ulong[outside];
		outside = 0;
		for ( int i = 0; i < tiles.Length; i++ )
		{
			if ( tiles[i] is not { } tile ) continue;
			foreach ( ulong edge in tile.Edges( s, forbidden ).Edges )
			{
				int side = (int)(edge >> 16 & 0xFFFF);
				if ( Inside( i, side, out int there ) >= 0 ) continue;
				edges[outside++] = (ulong)labels[first[i] + (int)(edge >> 32)] << 32 | (ulong)side << 24 | (ulong)there << 16 | (edge & 0xFFFF);
			}
		}
		Array.Sort( edges );

		First = first;
		Labels = labels;
		Count = count;
		Empty = first.AsSpan().IndexOfAnyExcept( -1 ) < 0;
		Out = TileEdges.Unique( edges, outside );
	}

	// The place across side when it is in this block, else -1 with there its place in the neighbour block.
	static int Inside( int place, int side, out int there )
	{
		int x = (place & Mask) + (side == 0 ? 1 : side == 2 ? -1 : 0), y = (place >> Bits) + (side == 1 ? 1 : side == 3 ? -1 : 0);
		there = Place( x, y );
		return x == (x & Mask) && y == (y & Mask) ? there : -1;
	}

	public long Bytes => 64 + Tile.ArrayBytes( First.Length, 4 ) + Tile.ArrayBytes( Labels.Length, 4 ) + Tile.ArrayBytes( Out.Length, 8 );
}

internal readonly record struct IslandJoin( int Label, int Block, int Other );

/// <summary>
/// A link that joins two tile components under an area mask, kept with the block its start sits in: per
/// end the place in its block and the tile component, the block of the end by index.
/// </summary>
internal readonly record struct IslandLink( int Id, int EndBlock, ushort StartComponent, ushort EndComponent, byte StartPlace, byte EndPlace, byte Type );

/// <summary>
/// What the links of one block join, in block labels, with one bit per link type among them.
/// </summary>
internal readonly record struct LinkJoin( int Label, int Block, int Other, ulong Types );

/// <summary>
/// The labels of one tile's components in an island map.
/// </summary>
internal readonly struct TileLabels( int[] labels, int start, int[] local, int first )
{
	public int this[int component] => labels[start + local[first + component]];
}

/// <summary>
/// Polygons with the same label can reach each other under one area mask and one link type mask.
/// Three levels: the components inside each tile, kept with the tile build; blocks of 8 x 8 tiles,
/// shared from map to map while their tile objects stay; and here a union over the joins between
/// blocks and over the links, which each block keeps reduced to the label pairs they join. A map starts
/// from the last one built for its area mask, rebuilds only the blocks whose tile objects differ and
/// resolves only the links whose entries differ, so after a commit it costs per changed block and link
/// and per block, not per tile or link.
/// </summary>
internal sealed class IslandMap
{
	public readonly ulong Forbidden, ForbiddenLinks;
	public IslandMap? Next;
	readonly MeshState state;

	// A block keeps its index in the maps that start from this one, so the joins of a block stay valid.
	IslandBlock[] blocks = [];
	int blockCount, empty;

	// Open addressing over block coordinates: block index + 1, 0 for none.
	int[] lookup = new int[16];

	// Per block, its connections into the neighbour blocks, in their labels. Redone when either side is rebuilt.
	IslandJoin[]?[] joins = [];

	// Per block, sorted by id, the links whose start sits in it and that join two tile components under
	// the area mask, and what they join in labels. A block's link joins are redone when it is rebuilt,
	// when a block they name is rebuilt or when its links change. A map built whole lays out every
	// block's links in one array; an edit gives a block its own.
	ArraySegment<IslandLink>[] links = [];
	LinkJoin[]?[] linkJoins = [];

	// Per block, where its labels start in labels.
	int[] start = [];
	int[] labels = [];

	public IslandMap( MeshState s, ulong forbidden, ulong forbiddenLinks, IslandMap? from )
	{
		state = s;
		Forbidden = forbidden;
		ForbiddenLinks = forbiddenLinks;

		// Built whole, a map drops the blocks that lost their tiles.
		if ( from != null && from.empty * 2 > from.blockCount ) from = null;
		var rebuilt = new List<int>();
		if ( from == null )
		{
			foreach ( var page in s.Pages )
			{
				if ( page == null ) continue;
				foreach ( var tile in page )
				{
					if ( tile != null ) Rebuild( tile.X >> IslandBlock.Bits, tile.Y >> IslandBlock.Bits, null, rebuilt );
				}
			}
		}
		else
		{
			blocks = from.blocks;
			blockCount = from.blockCount;
			lookup = from.lookup;
			joins = from.joins;
			// The slots whose tile objects differ mark the blocks the old and the new object are in.
			Tile?[]?[] before = from.state.Pages, after = s.Pages;
			for ( int page = 0; page < Math.Max( before.Length, after.Length ); page++ )
			{
				var a = page < before.Length ? before[page] : null;
				var b = page < after.Length ? after[page] : null;
				if ( a == b ) continue;
				for ( int i = 0; i < MeshState.PageSize; i++ )
				{
					Tile? was = a?[i], now = b?[i];
					if ( was == now ) continue;
					if ( was != null ) Rebuild( was.X >> IslandBlock.Bits, was.Y >> IslandBlock.Bits, from, rebuilt );
					if ( now != null ) Rebuild( now.X >> IslandBlock.Bits, now.Y >> IslandBlock.Bits, from, rebuilt );
				}
			}
		}

		if ( rebuilt.Count > 0 )
		{
			var old = joins;
			joins = new IslandJoin[]?[blocks.Length];
			Array.Copy( old, joins, Math.Min( old.Length, blockCount ) );
			foreach ( int index in rebuilt )
			{
				var block = blocks[index];
				for ( int side = -1; side < 4; side++ )
				{
					int at = side < 0 ? index : Find( block.X + (side == 0 ? 1 : side == 2 ? -1 : 0), block.Y + (side == 1 ? 1 : side == 3 ? -1 : 0) );
					if ( at >= 0 && (joins[at] == null || (at < old.Length && joins[at] == old[at])) ) joins[at] = Join( at );
				}
			}
		}

		UpdateLinks( from, rebuilt );

		start = new int[blockCount];
		int total = 0;
		for ( int i = 0; i < blockCount; i++ )
		{
			start[i] = total;
			total += blocks[i].Count;
			if ( blocks[i].Empty ) empty++;
		}
		var parent = new int[total];
		for ( int i = 0; i < total; i++ ) parent[i] = i;
		for ( int i = 0; i < blockCount; i++ )
		{
			foreach ( var join in joins[i]! ) Union( parent, start[i] + join.Label, start[join.Block] + join.Other );
			if ( i >= linkJoins.Length || linkJoins[i] is not { } more ) continue;
			// A one-way link joins islands like any other: leaving it out would refuse the way forward
			// across a link that is the only connection.
			foreach ( ref readonly var join in more.AsSpan() )
			{
				if ( (join.Types & ~forbiddenLinks) != 0 ) Union( parent, start[i] + join.Label, start[join.Block] + join.Other );
			}
		}

		int count = 0;
		for ( int i = 0; i < total; i++ ) parent[i] = parent[i] == i ? count++ : parent[parent[i]];
		labels = parent;
	}

	// Builds the block at block coordinates x, y anew, unless this map already did.
	void Rebuild( int x, int y, IslandMap? from, List<int> rebuilt )
	{
		int index = Find( x, y );
		if ( index >= 0 && (from == null || index >= from.blockCount || blocks[index] != from.blocks[index]) ) return;

		if ( from != null && blocks == from.blocks ) blocks = (IslandBlock[])blocks.Clone();
		var block = new IslandBlock( state, x, y, Forbidden );
		if ( index >= 0 )
		{
			blocks[index] = block;
			rebuilt.Add( index );
			return;
		}

		if ( blockCount == blocks.Length ) Array.Resize( ref blocks, Math.Max( 16, blockCount * 2 ) );
		rebuilt.Add( blockCount );
		blocks[blockCount++] = block;
		if ( blockCount * 2 > lookup.Length || (from != null && lookup == from.lookup) )
		{
			lookup = new int[Math.Max( lookup.Length, (int)BitOperations.RoundUpToPowerOf2( (uint)blockCount * 2 ) )];
			for ( int i = 0; i < blockCount; i++ ) Insert( i );
		}
		else Insert( blockCount - 1 );
	}

	void Insert( int index )
	{
		int mask = lookup.Length - 1;
		int i = Hash( blocks[index].X, blocks[index].Y ) & mask;
		while ( lookup[i] != 0 ) i = (i + 1) & mask;
		lookup[i] = index + 1;
	}

	static int Hash( int x, int y ) => (int)((uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA77u) >>> 12;

	int Find( int x, int y )
	{
		int mask = lookup.Length - 1;
		for ( int i = Hash( x, y ) & mask; ; i = (i + 1) & mask )
		{
			int index = lookup[i] - 1;
			if ( index < 0 ) return -1;
			if ( blocks[index].X == x && blocks[index].Y == y ) return index;
		}
	}

	IslandJoin[] Join( int index )
	{
		var block = blocks[index];
		var edges = block.Out;
		if ( edges.Length == 0 ) return [];
		var result = new IslandJoin[edges.Length];
		int count = 0, side = -1, other = -1;
		foreach ( ulong edge in edges )
		{
			if ( (int)(edge >> 24 & 0xFF) != side )
			{
				side = (int)(edge >> 24 & 0xFF);
				other = Find( block.X + (side == 0 ? 1 : side == 2 ? -1 : 0), block.Y + (side == 1 ? 1 : side == 3 ? -1 : 0) );
			}
			var target = blocks[other];
			var join = new IslandJoin( (int)(edge >> 32), other, target.Labels[target.First[(int)(edge >> 16 & 0xFF)] + (int)(edge & 0xFFFF)] );
			if ( count == 0 || result[count - 1] != join ) result[count++] = join;
		}
		return count == result.Length ? result : result.AsSpan( 0, count ).ToArray();
	}

	const byte Rebuilt = 1, Edited = 2;

	// Moves the links whose entries differ from the ones of the map this one starts from between the
	// lists of the blocks, then redoes the link joins that may have changed.
	void UpdateLinks( IslandMap? from, List<int> rebuilt )
	{
		links = from?.links ?? [];
		linkJoins = from?.linkJoins ?? [];
		var marks = rebuilt.Count == 0 ? null : new byte[blockCount];
		foreach ( int index in rebuilt ) marks![index] = Rebuilt;

		// A commit gives a link a new entry, with a new serial, whenever a tile one of its ends sits in is
		// replaced, so an entry with the same serial still names the same tile builds at the same places.
		List<IslandLink>?[]? edits = null;
		LinkEntry[]?[] before = from?.state.LinkPages ?? [], after = state.LinkPages;
		if ( from == null ) AllLinks();
		for ( int page = 0; from != null && page < Math.Max( before.Length, after.Length ); page++ )
		{
			var a = page < before.Length ? before[page] : null;
			var b = page < after.Length ? after[page] : null;
			if ( a == b ) continue;
			for ( int i = 0; i < MeshState.LinkPageSize; i++ )
			{
				uint was = a == null ? 0 : a[i].Serial, now = b == null ? 0 : b[i].Serial;
				if ( was == now ) continue;
				int id = page << MeshState.LinkPageBits | i;
				if ( was != 0 && a![i].Connected )
				{
					var tile = from!.state.Get( a[i].StartPoly );
					Edit( ref edits, Find( tile.X >> IslandBlock.Bits, tile.Y >> IslandBlock.Bits ), new IslandLink { Id = id, EndBlock = -1 } );
				}
				if ( now != 0 && Resolve( b![i], id, out int block, out var link ) ) Edit( ref edits, block, link );
			}
		}

		if ( edits != null )
		{
			links = Writable( links, from?.links, blockCount );
			marks ??= new byte[blockCount];
			for ( int i = 0; i < blockCount; i++ )
			{
				if ( edits[i] is not { } list ) continue;
				links[i] = Merge( links[i], CollectionsMarshal.AsSpan( list ) );
				marks[i] |= Edited;
			}
		}
		if ( marks == null ) return;

		LinkJoin[]? scratch = null;
		for ( int i = 0; i < blockCount; i++ )
		{
			var known = i < linkJoins.Length ? linkJoins[i] : null;
			bool redo = marks[i] != 0;
			for ( int j = 0; !redo && known != null && j < known.Length; j++ ) redo = (marks[known[j].Block] & Rebuilt) != 0;
			var list = i < links.Length ? links[i] : default;
			if ( !redo || (list.Count == 0 && known is not { Length: > 0 }) ) continue;
			linkJoins = Writable( linkJoins, from?.linkJoins, blockCount );
			linkJoins[i] = Join( i, list, ref scratch );
		}
	}

	// The array itself once this map has its own copy of it.
	static T[] Writable<T>( T[] array, T[]? shared, int length )
	{
		if ( array != shared && array.Length >= length ) return array;
		var copy = new T[length];
		Array.Copy( array, copy, Math.Min( array.Length, length ) );
		return copy;
	}

	// Every link that joins, resolved once in id order, then laid out by block in the same array by a
	// stable counting sort applied in place.
	void AllLinks()
	{
		links = new ArraySegment<IslandLink>[blockCount];
		var pages = state.LinkPages;
		IslandLink[]? all = null;
		int[]? to = null;
		int count = 0;
		for ( int page = 0; page < pages.Length; page++ )
		{
			if ( pages[page] is not { } entries ) continue;
			for ( int i = 0; i < MeshState.LinkPageSize; i++ )
			{
				if ( entries[i].Serial == 0 || !Resolve( entries[i], page << MeshState.LinkPageBits | i, out int block, out var link ) ) continue;
				all ??= new IslandLink[pages.Length * MeshState.LinkPageSize];
				to ??= new int[all.Length];
				to[count] = block;
				all[count++] = link;
			}
		}
		if ( all == null || to == null ) return;

		// Where each block's links end, then per link where it goes.
		var ends = new int[blockCount];
		for ( int k = 0; k < count; k++ ) ends[to[k]]++;
		for ( int i = 1; i < blockCount; i++ ) ends[i] += ends[i - 1];
		for ( int k = count - 1; k >= 0; k-- ) to[k] = --ends[to[k]];
		for ( int k = 0; k < count; k++ )
		{
			while ( to[k] != k )
			{
				int there = to[k];
				(all[k], all[there]) = (all[there], all[k]);
				to[k] = to[there];
				to[there] = there;
			}
		}

		if ( count * 2 < all.Length ) Array.Resize( ref all, count );
		for ( int i = 0; i < blockCount; i++ )
		{
			int end = i + 1 < blockCount ? ends[i + 1] : count;
			if ( end > ends[i] ) links[i] = new ArraySegment<IslandLink>( all, ends[i], end - ends[i] );
		}
	}

	// Per block, in id order, the links to add and, as EndBlock -1, the ids to remove, a removal before
	// the addition of the same id.
	void Edit( ref List<IslandLink>?[]? edits, int block, in IslandLink link )
	{
		edits ??= new List<IslandLink>?[blockCount];
		(edits[block] ??= []).Add( link );
	}

	// A block's links, sorted by id, after its edits.
	static ArraySegment<IslandLink> Merge( ReadOnlySpan<IslandLink> old, ReadOnlySpan<IslandLink> edits )
	{
		var result = new IslandLink[old.Length + edits.Length];
		int count = 0, at = 0;
		foreach ( ref readonly var edit in edits )
		{
			while ( at < old.Length && old[at].Id < edit.Id ) result[count++] = old[at++];
			if ( edit.EndBlock >= 0 ) result[count++] = edit;
			else if ( at < old.Length && old[at].Id == edit.Id ) at++;
		}
		while ( at < old.Length ) result[count++] = old[at++];
		return count == result.Length ? result : result.AsSpan( 0, count ).ToArray();
	}

	// False for a link that joins nothing under the area mask: an end on no polygon or on a forbidden one,
	// a forbidden area, or both ends in one tile component.
	bool Resolve( in LinkEntry entry, int id, out int block, out IslandLink link )
	{
		link = default;
		block = -1;
		if ( !entry.Connected || (Forbidden >> entry.Def.Area & 1) != 0 ) return false;
		if ( !End( entry.StartPoly, out block, out byte startPlace, out ushort startComponent ) || !End( entry.EndPoly, out int endBlock, out byte endPlace, out ushort endComponent ) ) return false;
		if ( block == endBlock && startPlace == endPlace && startComponent == endComponent ) return false;
		link = new IslandLink( id, endBlock, startComponent, endComponent, startPlace, endPlace, entry.Def.Type );
		return true;
	}

	// False for a polygon the mask forbids.
	bool End( uint id, out int block, out byte place, out ushort component )
	{
		var tile = state.Get( id );
		component = tile.Islands( Forbidden ).Component[id & state.PolyMask];
		block = Find( tile.X >> IslandBlock.Bits, tile.Y >> IslandBlock.Bits );
		place = (byte)IslandBlock.Place( tile.X, tile.Y );
		return component != TileIslands.None;
	}

	// The label pairs the links of a block join, each pair once with the types of all its links, and
	// none for a link whose ends have one label. Worked out in scratch, which grows as needed.
	LinkJoin[] Join( int index, ReadOnlySpan<IslandLink> list, ref LinkJoin[]? scratch )
	{
		if ( list.Length == 0 ) return [];
		if ( scratch == null || scratch.Length < list.Length ) scratch = new LinkJoin[Math.Max( list.Length, 2 * (scratch?.Length ?? 0) )];
		var block = blocks[index];
		var result = scratch;
		int count = 0;
		foreach ( ref readonly var link in list )
		{
			int label = block.Labels[block.First[link.StartPlace] + link.StartComponent];
			var other = blocks[link.EndBlock];
			int there = other.Labels[other.First[link.EndPlace] + link.EndComponent];
			if ( link.EndBlock == index )
			{
				if ( label == there ) continue;
				if ( there < label ) (label, there) = (there, label);
			}
			result[count++] = new LinkJoin( label, link.EndBlock, there, 1ul << link.Type );
		}

		var found = result.AsSpan( 0, count );
		found.Sort( static ( x, y ) => x.Label != y.Label ? x.Label.CompareTo( y.Label ) : x.Block != y.Block ? x.Block.CompareTo( y.Block ) : x.Other.CompareTo( y.Other ) );
		int unique = 0;
		for ( int i = 0; i < count; i++ )
		{
			var join = result[i];
			if ( unique > 0 && result[unique - 1] is var last && last.Label == join.Label && last.Block == join.Block && last.Other == join.Other ) result[unique - 1] = last with { Types = last.Types | join.Types };
			else result[unique++] = join;
		}
		return result.AsSpan( 0, unique ).ToArray();
	}

	// The polygon's index in labels, -1 for a polygon the mask forbids.
	int Component( uint id )
	{
		var tile = state.Get( id );
		ushort component = tile.Islands( Forbidden ).Component[id & state.PolyMask];
		return component == TileIslands.None ? -1 : Start( tile, out var block, out int first ) + block.Labels[first + component];
	}

	int Start( Tile tile, out IslandBlock block, out int first )
	{
		int index = Find( tile.X >> IslandBlock.Bits, tile.Y >> IslandBlock.Bits );
		block = blocks[index];
		first = block.First[IslandBlock.Place( tile.X, tile.Y )];
		return start[index];
	}

	// -1 for a polygon the mask forbids.
	public int Of( uint id )
	{
		int component = Component( id );
		return component < 0 ? -1 : labels[component];
	}

	public TileLabels For( Tile tile )
	{
		int at = Start( tile, out var block, out int first );
		return new TileLabels( labels, at, block.Labels, first );
	}

	public IslandBlock? BlockOf( int tileX, int tileY )
	{
		int index = Find( tileX >> IslandBlock.Bits, tileY >> IslandBlock.Bits );
		return index < 0 ? null : blocks[index];
	}

	public ArraySegment<IslandLink> LinksOf( int tileX, int tileY )
	{
		int index = Find( tileX >> IslandBlock.Bits, tileY >> IslandBlock.Bits );
		return index < 0 || index >= links.Length ? default : links[index];
	}

	// What this map holds of its own, and with blocks set what its blocks and their links hold, which
	// maps of one area mask share.
	public long Bytes( bool withBlocks )
	{
		long bytes = 96 + Tile.ArrayBytes( blocks.Length, 8 ) + Tile.ArrayBytes( lookup.Length, 4 ) + Tile.ArrayBytes( joins.Length, 8 )
			+ Tile.ArrayBytes( start.Length, 4 ) + Tile.ArrayBytes( labels.Length, 4 );
		bytes += Tile.ArrayBytes( links.Length, 16 ) + Tile.ArrayBytes( linkJoins.Length, 8 );
		for ( int i = 0; i < blockCount; i++ )
		{
			bytes += Tile.ArrayBytes( joins[i]!.Length, 12 );
			if ( i < linkJoins.Length && linkJoins[i] is { } more ) bytes += Tile.ArrayBytes( more.Length, 24 );
			if ( !withBlocks ) continue;
			bytes += blocks[i].Bytes;
			if ( i < links.Length ) bytes += links[i].Count * 16L;
		}
		return bytes;
	}

	static int Root( int[] parent, int i )
	{
		while ( parent[i] != i ) i = parent[i] = parent[parent[i]];
		return i;
	}

	public static void Union( int[] parent, int a, int b )
	{
		a = Root( parent, a );
		b = Root( parent, b );
		if ( a != b ) parent[Math.Max( a, b )] = Math.Min( a, b );
	}
}
