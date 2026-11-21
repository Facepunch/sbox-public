using System.Numerics;

namespace Borealis.Gpu.Memory;

/// <summary>
/// One <c>vkAllocateMemory</c> allocation, suballocated with a two-level segregated fit (TLSF): free ranges sit in lists
/// by size class, and two bitmaps find the smallest class with room in constant time, whatever the number of ranges.
/// Every range, free or used, is a node linked to its neighbours in address order, so a free merges with them at once.
/// Host-visible blocks stay mapped.
/// </summary>
internal sealed class MemoryBlock
{
	public ulong Memory;
	public nint Mapped;
	public int TypeIndex;
	public bool Dedicated;
	public bool Images;

	/// <summary>Whether it's a large block (<see cref="MemoryAllocator.LargeBlockSize"/>), for allocations too big for the small ones.</summary>
	public bool Large;

	/// <summary>Whether it holds content (a map's textures and geometry), kept apart from long-lived resources so it drains when the content goes.</summary>
	public bool Content;

	/// <summary>Whether it holds device allocations made in system memory because video memory was full or over budget.</summary>
	public bool Fallback;

	/// <summary>Whether it's a big buffer's own allocation, which can be kept empty as a spare for the next of its size.</summary>
	public bool Reusable;

	/// <summary>Bytes handed out.</summary>
	public ulong Used;

	/// <summary>The allocator's frame when it last became empty (<see cref="MemoryAllocator.Trim"/>).</summary>
	public ulong EmptySince;

	/// <summary>Whether it's empty and kept as a spare in the allocator's list of them.</summary>
	public bool Spare;

	/// <summary>
	/// Whether it's being emptied (<see cref="MemoryAllocator.Draining"/>): nothing new goes in, and what's in it that can be
	/// moved is made again elsewhere.
	/// </summary>
	public bool Draining;

	/// <summary>The allocator's frame it began draining in, or after which it may be drained again.</summary>
	public ulong DrainFrame;

	/// <summary>Never drained: it holds something that can't move (<see cref="MemoryAllocator.Pin"/>).</summary>
	public bool Pinned;

	// Size classes: sizes under SecondLevels have one class each; above, each power of two splits into SecondLevels
	const int SecondLevelBits = 5;
	const int SecondLevels = 1 << SecondLevelBits;

	struct Node
	{
		public ulong Offset;
		public ulong Size;
		public int PreviousPhysical, NextPhysical;

		// Free: the neighbours in its class's list. Recycled: NextFree links the spare nodes
		public int PreviousFree, NextFree;
		public bool Free;

		// Allocated: what it holds (MemoryAllocator.SetOwner), or 0
		public ulong Owner;
	}

	Node[] nodes = new Node[8];
	int nodeCount;
	int spareNodes = -1;
	ulong firstLevels;
	readonly uint[] secondLevels;
	readonly int[] heads;

	public MemoryBlock( ulong size )
	{
		ArgumentOutOfRangeException.ThrowIfZero( size );
		Size = size;
		var levels = Class( size ).First + 1;
		secondLevels = new uint[levels];
		heads = new int[levels * SecondLevels];
		Array.Fill( heads, -1 );

		var all = NewNode();
		nodes[all] = new Node { Offset = 0, Size = size, PreviousPhysical = -1, NextPhysical = -1 };
		AddFree( all );
	}

	public ulong Size { get; }

	/// <summary>Free ranges: never adjacent, since a free merges with its free neighbours.</summary>
	public int FreeRanges { get; private set; }

	/// <summary>The class a range of <paramref name="size"/> is listed in: rounded down, so its ranges are at least the class's smallest.</summary>
	static (int First, int Second) Class( ulong size )
	{
		if ( size < SecondLevels ) return (0, (int)size);
		var log = BitOperations.Log2( size );
		return (log - SecondLevelBits + 1, (int)(size >> (log - SecondLevelBits)) & (SecondLevels - 1));
	}

	/// <summary>The smallest class whose every range holds <paramref name="size"/>.</summary>
	static (int First, int Second) ClassHolding( ulong size )
	{
		if ( size >= SecondLevels ) size += (1ul << (BitOperations.Log2( size ) - SecondLevelBits)) - 1;
		return Class( size );
	}

	/// <summary>The first free range in the smallest non-empty class from (<paramref name="first"/>, <paramref name="second"/>) up, or -1.</summary>
	int FirstFrom( int first, int second )
	{
		if ( first >= secondLevels.Length ) return -1;
		var seconds = secondLevels[first] & (~0u << second);
		if ( seconds == 0 )
		{
			if ( first + 1 >= secondLevels.Length ) return -1;
			var firsts = firstLevels & (~0ul << (first + 1));
			if ( firsts == 0 ) return -1;
			first = BitOperations.TrailingZeroCount( firsts );
			seconds = secondLevels[first];
		}

		return heads[first * SecondLevels + BitOperations.TrailingZeroCount( seconds )];
	}

	// Vulkan's alignments are powers of two
	static ulong AlignUp( ulong offset, ulong alignment ) => (offset + alignment - 1) & ~(alignment - 1);

	bool Holds( int node, ulong size, ulong alignment ) => AlignUp( nodes[node].Offset, alignment ) + size <= nodes[node].Offset + nodes[node].Size;

	// Ranges of a class checked for one that fits an alignment, before moving on: a class's list can be long
	const int SearchLength = 8;

	/// <summary>A free range that holds <paramref name="size"/> at <paramref name="alignment"/>, or -1.</summary>
	int Find( ulong size, ulong alignment )
	{
		// Nothing free as large: its class is above every free range's
		if ( firstLevels == 0 || size > Size || Class( size ).First > 63 - BitOperations.LeadingZeroCount( firstLevels ) ) return -1;

		// The smallest class that holds the size: its ranges almost always fit as they're aligned too
		var (first, second) = ClassHolding( size );
		var tries = SearchLength;
		for ( var node = FirstFrom( first, second ); node >= 0 && tries-- > 0; node = nodes[node].NextFree )
		{
			if ( Holds( node, size, alignment ) ) return node;
		}

		// A range that holds the size at any offset
		if ( alignment > 1 )
		{
			(first, second) = ClassHolding( size + alignment - 1 );
			var any = FirstFrom( first, second );
			if ( any >= 0 ) return any;
		}

		// The class below, whose larger ranges may still hold it
		var (lowFirst, lowSecond) = Class( size );
		if ( (lowFirst, lowSecond) == ClassHolding( size ) ) return -1;
		tries = SearchLength;
		for ( var node = heads[lowFirst * SecondLevels + lowSecond]; node >= 0 && tries-- > 0; node = nodes[node].NextFree )
		{
			if ( Holds( node, size, alignment ) ) return node;
		}

		return -1;
	}

	/// <summary>Whether <see cref="TryAllocate"/> would find room, without taking it.</summary>
	public bool Fits( ulong size, ulong alignment ) => Find( size, Math.Max( alignment, 1 ) ) >= 0;

	/// <summary>
	/// Take <paramref name="size"/> bytes at <paramref name="alignment"/>: <paramref name="node"/> names them for
	/// <see cref="Release"/>.
	/// </summary>
	public bool TryAllocate( ulong size, ulong alignment, out ulong offset, out int node )
	{
		alignment = Math.Max( alignment, 1 );
		if ( !BitOperations.IsPow2( alignment ) ) throw new ArgumentException( $"Alignment {alignment} isn't a power of two", nameof( alignment ) );
		node = Find( size, alignment );
		if ( node < 0 )
		{
			offset = 0;
			return false;
		}

		RemoveFree( node );
		var start = nodes[node].Offset;
		var aligned = AlignUp( start, alignment );

		// What's before the aligned start and after the end stays free
		if ( aligned > start )
		{
			var before = NewNode();
			nodes[before] = new Node { Offset = start, Size = aligned - start, PreviousPhysical = nodes[node].PreviousPhysical, NextPhysical = node };
			if ( nodes[before].PreviousPhysical >= 0 ) nodes[nodes[before].PreviousPhysical].NextPhysical = before;
			nodes[node].PreviousPhysical = before;
			nodes[node].Offset = aligned;
			nodes[node].Size -= aligned - start;
			AddFree( before );
		}

		if ( nodes[node].Size > size )
		{
			var after = NewNode();
			nodes[after] = new Node { Offset = aligned + size, Size = nodes[node].Size - size, PreviousPhysical = node, NextPhysical = nodes[node].NextPhysical };
			if ( nodes[after].NextPhysical >= 0 ) nodes[nodes[after].NextPhysical].PreviousPhysical = after;
			nodes[node].NextPhysical = after;
			nodes[node].Size = size;
			AddFree( after );
		}

		offset = aligned;
		Used += size;
		return true;
	}

	/// <summary>Give back what <see cref="TryAllocate"/> handed out as <paramref name="node"/>, merging it with free neighbours.</summary>
	public void Release( int node )
	{
		if ( (uint)node >= (uint)nodeCount || nodes[node].Free ) throw new ArgumentException( $"Node {node} isn't allocated", nameof( node ) );
		Used -= nodes[node].Size;
		nodes[node].Owner = 0;

		var next = nodes[node].NextPhysical;
		if ( next >= 0 && nodes[next].Free )
		{
			RemoveFree( next );
			nodes[node].Size += nodes[next].Size;
			Unlink( next );
		}

		var previous = nodes[node].PreviousPhysical;
		if ( previous >= 0 && nodes[previous].Free )
		{
			RemoveFree( previous );
			nodes[previous].Size += nodes[node].Size;
			Unlink( node );
			node = previous;
		}

		AddFree( node );
	}

	/// <summary>Note what the range <paramref name="node"/> names holds: a handle, so a block being emptied can find what's in it.</summary>
	public void SetOwner( int node, ulong owner ) => nodes[node].Owner = owner;

	/// <summary>Add the owners of every allocated range that has one to <paramref name="owners"/>; allocates nothing past the list's room.</summary>
	public void CollectOwners( List<ulong> owners )
	{
		for ( int node = 0; node < nodeCount; node++ )
		{
			if ( !nodes[node].Free && nodes[node].Size > 0 && nodes[node].Owner != 0 ) owners.Add( nodes[node].Owner );
		}
	}

	/// <summary>Add the size of every allocated range to <paramref name="sizes"/>; allocates nothing past the list's room.</summary>
	public void CollectSizes( List<ulong> sizes )
	{
		for ( int node = 0; node < nodeCount; node++ )
		{
			if ( !nodes[node].Free && nodes[node].Size > 0 ) sizes.Add( nodes[node].Size );
		}
	}

	/// <summary>Add every free range (offset, size) to <paramref name="ranges"/>; allocates nothing past the list's room.</summary>
	public void CollectFree( List<(ulong Offset, ulong Size)> ranges )
	{
		for ( int node = 0; node < nodeCount; node++ )
		{
			if ( nodes[node].Free ) ranges.Add( (nodes[node].Offset, nodes[node].Size) );
		}
	}

	/// <summary>The free ranges in address order, for tests and diagnostics; allocates.</summary>
	public List<(ulong Offset, ulong Size)> FreeList()
	{
		var list = new List<(ulong, ulong)>();
		for ( int node = 0; node < nodeCount; node++ )
		{
			if ( nodes[node].Free ) list.Add( (nodes[node].Offset, nodes[node].Size) );
		}

		list.Sort();
		return list;
	}

	/// <summary>The largest free range.</summary>
	public ulong LargestFree
	{
		get
		{
			if ( firstLevels == 0 ) return 0;
			var first = 63 - BitOperations.LeadingZeroCount( firstLevels );
			var second = 31 - BitOperations.LeadingZeroCount( secondLevels[first] );
			ulong largest = 0;
			for ( var node = heads[first * SecondLevels + second]; node >= 0; node = nodes[node].NextFree ) largest = Math.Max( largest, nodes[node].Size );
			return largest;
		}
	}

	// A node merged into its neighbour: out of the address order and recycled
	void Unlink( int node )
	{
		ref var n = ref nodes[node];
		if ( n.PreviousPhysical >= 0 ) nodes[n.PreviousPhysical].NextPhysical = n.NextPhysical;
		if ( n.NextPhysical >= 0 ) nodes[n.NextPhysical].PreviousPhysical = n.PreviousPhysical;
		n = default;
		n.NextFree = spareNodes;
		spareNodes = node;
	}

	int NewNode()
	{
		if ( spareNodes >= 0 )
		{
			var spare = spareNodes;
			spareNodes = nodes[spare].NextFree;
			return spare;
		}

		if ( nodeCount == nodes.Length ) Array.Resize( ref nodes, nodes.Length * 2 );
		return nodeCount++;
	}

	void AddFree( int node )
	{
		var (first, second) = Class( nodes[node].Size );
		var list = first * SecondLevels + second;
		ref var n = ref nodes[node];
		n.Free = true;
		n.PreviousFree = -1;
		n.NextFree = heads[list];
		if ( heads[list] >= 0 ) nodes[heads[list]].PreviousFree = node;
		heads[list] = node;
		secondLevels[first] |= 1u << second;
		firstLevels |= 1ul << first;
		FreeRanges++;
	}

	void RemoveFree( int node )
	{
		ref var n = ref nodes[node];
		if ( n.PreviousFree >= 0 ) nodes[n.PreviousFree].NextFree = n.NextFree;
		else
		{
			var (first, second) = Class( n.Size );
			var list = first * SecondLevels + second;
			heads[list] = n.NextFree;
			if ( n.NextFree < 0 )
			{
				secondLevels[first] &= ~(1u << second);
				if ( secondLevels[first] == 0 ) firstLevels &= ~(1ul << first);
			}
		}

		if ( n.NextFree >= 0 ) nodes[n.NextFree].PreviousFree = n.PreviousFree;
		n.Free = false;
		n.PreviousFree = n.NextFree = -1;
		FreeRanges--;
	}
}
