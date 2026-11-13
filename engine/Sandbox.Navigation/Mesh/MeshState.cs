using System.Numerics;

namespace Nav;

/// <summary>
/// A resolved link. Poly ids are PolyRef.NoId for an end that found no polygon. Serial 0 marks a free id.
/// </summary>
internal struct LinkEntry
{
	public NavLink Def;
	public Vector3 StartPoint, EndPoint;
	public uint StartPoly, EndPoly;
	public uint Serial;

	public readonly bool Connected => StartPoly != PolyRef.NoId && EndPoly != PolyRef.NoId;
}

/// <summary>
/// Everything a query reads. Immutable once published, except the lazily added island maps. Tiles sit
/// in pages by slot so a commit copies the root and the pages it touches, not one entry per loaded tile.
/// </summary>
internal sealed class MeshState
{
	public const int PageBits = 8, PageSize = 1 << PageBits;

	public readonly Tile?[]?[] Pages;
	public readonly TileIndex Index;
	public readonly LinkEntry[] Links;
	public readonly uint Version;
	public readonly int TileCount;

	public IslandMap? Islands;

	public MeshState( Tile?[]?[] pages, TileIndex index, LinkEntry[] links, uint version, int tileCount )
	{
		Pages = pages;
		Index = index;
		Links = links;
		Version = version;
		TileCount = tileCount;
	}

	// For slots that come from outside, such as a PolyRef.
	public Tile? TileAt( uint slot )
	{
		var pages = Pages;
		uint page = slot >> PageBits;
		return page < (uint)pages.Length ? pages[page]?[slot & (PageSize - 1)] : null;
	}

	// For ids this state produced itself.
	public Tile Get( uint id )
	{
		uint slot = id >> PolyRef.PolyBits;
		return Pages[slot >> PageBits]![slot & (PageSize - 1)]!;
	}

	public Tile? Find( int x, int y )
	{
		int slot = Index.Find( x, y );
		return slot < 0 ? null : Pages[slot >> PageBits]![slot & (PageSize - 1)];
	}

	public bool TryGet( PolyRef reference, out Tile tile, out int poly )
	{
		uint id = reference.Id;
		poly = (int)(id & PolyRef.PolyMask);
		tile = TileAt( id >> PolyRef.PolyBits )!;
		return tile != null && tile.Serial == reference.Serial && poly < tile.Polys.Length;
	}

	public PolyRef RefOf( uint id ) => new( Get( id ).Serial, id );

	// Whether a corridor entry, polygon or link, still names what it named and the filter lets it pass.
	public bool IsValid( PolyRef entry, in NavFilter filter )
	{
		if ( !entry.IsLink ) return TryGet( entry, out var tile, out int poly ) && filter.Allows( tile.Polys[poly].Area );
		int id = entry.Link;
		return (uint)id < (uint)Links.Length && Links[id].Serial == entry.Serial && Links[id].Connected && filter.Allows( Links[id].Def.Area );
	}
}

/// <summary>
/// Tile coordinates to slot. Blocks of 16 x 16 tiles in an open-addressing table, so a commit that
/// changes the set of tiles copies the table of blocks and the blocks it touches.
/// </summary>
internal sealed class TileIndex
{
	const int BlockBits = 4, BlockMask = (1 << BlockBits) - 1, BlockTiles = 1 << (BlockBits * 2);

	// Slots holds slot + 1 per tile, 0 for none, and in its last element the serial of the commit that
	// made the array: that commit may still write to it, any later one copies it first.
	struct Block
	{
		public int X, Y;
		public int[]? Slots;
	}

	Block[] blocks;
	int used;
	bool hasEmpty;

	// Inclusive tile bounds, to the block. Empty when MaxX < MinX.
	public int MinX, MinY, MaxX = -1, MaxY = -1;

	public TileIndex()
	{
		blocks = new Block[16];
	}

	TileIndex( TileIndex other )
	{
		blocks = (Block[])other.blocks.Clone();
		used = other.used;
		MinX = other.MinX;
		MinY = other.MinY;
		MaxX = other.MaxX;
		MaxY = other.MaxY;
	}

	public TileIndex Clone() => new( this );

	static int Hash( int x, int y ) => (int)((uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA77u) >>> 12;

	public int Find( int x, int y )
	{
		int bx = x >> BlockBits, by = y >> BlockBits;
		var table = blocks;
		int mask = table.Length - 1;
		for ( int i = Hash( bx, by ) & mask; ; i = (i + 1) & mask )
		{
			ref readonly var block = ref table[i];
			if ( block.Slots == null ) return -1;
			if ( block.X == bx && block.Y == by ) return block.Slots[(y & BlockMask) << BlockBits | (x & BlockMask)] - 1;
		}
	}

	// Owner thread, on a clone that is not published yet. slot -1 removes. stamp is the commit's serial.
	public void Set( int x, int y, int slot, uint stamp )
	{
		int bx = x >> BlockBits, by = y >> BlockBits;
		int mask = blocks.Length - 1;
		int i = Hash( bx, by ) & mask;
		while ( blocks[i].Slots != null && (blocks[i].X != bx || blocks[i].Y != by) ) i = (i + 1) & mask;

		ref var block = ref blocks[i];
		if ( block.Slots == null )
		{
			if ( slot < 0 ) return;
			if ( (used + 1) * 2 > blocks.Length )
			{
				Rehash( blocks.Length * 2 );
				Set( x, y, slot, stamp );
				return;
			}
			block = new Block { X = bx, Y = by, Slots = new int[BlockTiles + 1] };
			used++;
		}
		else if ( block.Slots[BlockTiles] != (int)stamp ) block.Slots = (int[])block.Slots.Clone();

		block.Slots![BlockTiles] = (int)stamp;
		block.Slots[(y & BlockMask) << BlockBits | (x & BlockMask)] = slot + 1;
		if ( slot < 0 && IsEmpty( block.Slots ) ) hasEmpty = true;
	}

	// Owner thread. Every block is copied again by the next commit that writes to it.
	public void ForgetStamps()
	{
		foreach ( ref readonly var block in blocks.AsSpan() )
		{
			if ( block.Slots != null ) block.Slots[BlockTiles] = 0;
		}
	}

	static bool IsEmpty( int[] slots ) => slots.AsSpan( 0, BlockTiles ).IndexOfAnyExcept( 0 ) < 0;

	void Rehash( int capacity )
	{
		var old = blocks;
		blocks = new Block[capacity];
		used = 0;
		foreach ( ref readonly var block in old.AsSpan() )
		{
			if ( block.Slots == null || (hasEmpty && IsEmpty( block.Slots )) ) continue;
			int mask = capacity - 1;
			int i = Hash( block.X, block.Y ) & mask;
			while ( blocks[i].Slots != null ) i = (i + 1) & mask;
			blocks[i] = block;
			used++;
		}
		hasEmpty = false;
	}

	// Drops emptied blocks and refreshes the bounds. Call once before publishing.
	public void Seal()
	{
		if ( hasEmpty ) Rehash( blocks.Length );
		MinX = MinY = int.MaxValue;
		MaxX = MaxY = int.MinValue;
		foreach ( ref readonly var block in blocks.AsSpan() )
		{
			if ( block.Slots == null ) continue;
			MinX = Math.Min( MinX, block.X << BlockBits );
			MinY = Math.Min( MinY, block.Y << BlockBits );
			MaxX = Math.Max( MaxX, (block.X << BlockBits) + BlockMask );
			MaxY = Math.Max( MaxY, (block.Y << BlockBits) + BlockMask );
		}
		if ( used == 0 )
		{
			MinX = MinY = 0;
			MaxX = MaxY = -1;
		}
	}

	public long Bytes => 16 + 40 + Tile.ArrayBytes( blocks.Length, 16 ) + used * Tile.ArrayBytes( BlockTiles + 1, 4 );
}
