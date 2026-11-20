using Borealis.Gpu.Vulkan;

namespace Borealis.Gpu.Memory;

/// <summary>
/// Part of a <see cref="MemoryBlock"/> a buffer or image is bound to.
/// </summary>
internal readonly record struct Allocation( MemoryBlock Block, ulong Offset, ulong Size, int Node )
{
	public ulong Memory => Block.Memory;

	/// <summary>Where it's mapped on the CPU, or 0 for device-only memory.</summary>
	public nint Mapped => Block.Mapped == 0 ? 0 : Block.Mapped + (nint)Offset;
}

/// <summary>
/// Suballocates device memory: blocks per memory type, separately for buffers and images so
/// <c>bufferImageGranularity</c> never matters, large blocks for big allocations (<see cref="LargeBlockSize"/>), dedicated
/// allocations for the biggest, and a spare block of a kind made ahead on a worker once the last is taken. An allocation goes into the
/// fullest block it fits in, so lightly used blocks drain and go back to the driver once empty: at once while another
/// empty block of their kind is kept, the last one after <see cref="SpareFrames"/> frames (<see cref="Trim"/>).
/// </summary>
internal sealed unsafe class MemoryAllocator : IDisposable
{
	readonly VkDeviceApi vk;
	readonly VkPhysicalDeviceMemoryProperties properties;
	readonly bool integrated;
	readonly ulong blockSize;
	readonly List<MemoryBlock> blocks = new();

	// Shared blocks by kind, so a lookup goes over its own kind's only: going over every block, each a cache miss, took
	// most of an allocation's time once a map had a few hundred
	readonly Dictionary<(int Type, bool Images, bool Large, bool Content, bool Fallback), Kind> kinds = new();

	sealed class Kind
	{
		public readonly List<MemoryBlock> Blocks = new();

		/// <summary>Its empty blocks, each a spare.</summary>
		public int Spares;

		/// <summary>The block being emptied, or null.</summary>
		public MemoryBlock Draining;

		/// <summary>Whether moves out of it may make one <see cref="DrainBlockSize"/> block, and whether they have.</summary>
		public bool DrainMayMake, DrainMade;

		/// <summary>The frame a shared block of this kind was last made in.</summary>
		public ulong LastNewBlock;

		/// <summary>When (<see cref="System.Diagnostics.Stopwatch"/> ticks) a shared block of this kind was last made.</summary>
		public long LastNewBlockTicks;
	}

	// Empty blocks kept as spares (shared ones, and big buffers' own allocations), which Trim frees after a while
	readonly List<MemoryBlock> spares = new();
	readonly object allocationLock = new();
	ulong frame;

	/// <summary>The device, for profiler zones around driver allocations; null for none.</summary>
	internal Device Owner { get; init; }

	/// <summary>Frames an empty block is kept for in case its kind is needed again, before it's freed.</summary>
	public const ulong SpareFrames = 300;

	/// <summary>Size of the shared blocks; allocations over a quarter of it get their own.</summary>
	public ulong BlockSize => blockSize;

	/// <summary>The device's memory types and heaps.</summary>
	public VkPhysicalDeviceMemoryProperties MemoryProperties => properties;

	/// <summary>Bytes allocated from the driver.</summary>
	public ulong Reserved { get; private set; }

	/// <summary>Bytes handed out.</summary>
	public ulong Used { get; private set; }

	/// <summary>Allocations made so far, for telling whether any happen per frame.</summary>
	public long Allocations { get; private set; }

	public MemoryAllocator( VkDeviceApi vk, in VkPhysicalDeviceMemoryProperties properties, bool integrated )
	{
		this.vk = vk;
		this.properties = properties;
		this.integrated = integrated;

		var heapCount = (int)properties.memoryHeapCount;
		heapReserved = new long[heapCount];
		heapUsed = new long[heapCount];
		driverUsage = new long[heapCount];
		driverBudget = new long[heapCount];
		reservedAtFetch = new long[heapCount];
		for ( int i = 0; i < heapCount; i++ ) driverBudget[i] = (long)(properties.Heap( i ).size / 10 * 8);

		// Small blocks: a part-used block holds back at most this much, and one emptied by a level or play mode goes back
		// to the driver
		blockSize = 16ul << 20;
	}

	/// <summary>
	/// The most a device-local heap the host sees through holds and is still the small window onto video memory (without
	/// resizable BAR), which uploads keep out of.
	/// </summary>
	public const ulong SmallWindowBytes = 256ul << 20;

	/// <summary>
	/// Choose the memory type for <paramref name="location"/> among <paramref name="typeBits"/>.
	/// </summary>
	public int FindType( uint typeBits, MemoryLocation location )
	{
		var best = -1;
		var bestScore = int.MinValue;

		for ( int i = 0; i < properties.memoryTypeCount; i++ )
		{
			if ( (typeBits & (1u << i)) == 0 ) continue;

			var flags = properties.Type( i ).propertyFlags;
			var deviceLocal = (flags & Vk.MemoryDeviceLocal) != 0;
			var hostVisible = (flags & Vk.MemoryHostVisible) != 0;
			var coherent = (flags & Vk.MemoryHostCoherent) != 0;
			var cached = (flags & Vk.MemoryHostCached) != 0;

			int score;
			switch ( location )
			{
				case MemoryLocation.Device:
					score = (deviceLocal ? 100 : 0) - (hostVisible ? 10 : 0);
					break;

				case MemoryLocation.Upload:
					if ( !hostVisible || !coherent ) continue;
					// Write-combined system memory on discrete GPUs keeps the small device-local window free. An integrated
					// GPU's device-local memory is system memory, unless its driver shows it to the host only through such a
					// window too (a Ryzen's, 256 MiB): uploads filled that and ran out
					var window = deviceLocal && properties.Heap( (int)properties.Type( i ).heapIndex ).size <= SmallWindowBytes;
					score = integrated && !window ? (deviceLocal ? 100 : 50) : (deviceLocal ? 10 : 100);
					score -= cached ? 5 : 0;
					break;

				default:
					if ( !hostVisible || !coherent ) continue;
					score = (cached ? 100 : 10) - (deviceLocal && !integrated ? 5 : 0);
					break;
			}

			if ( (flags & Vk.MemoryLazilyAllocated) != 0 ) score -= 1000;
			if ( score > bestScore )
			{
				bestScore = score;
				best = i;
			}
		}

		if ( best < 0 ) throw new GpuException( $"No memory type suits {location} memory (type bits {typeBits:X})" );
		return best;
	}

	/// <summary>
	/// Bytes of device allocations in system memory because video memory ran out (<c>VK_ERROR_OUT_OF_DEVICE_MEMORY</c>) or
	/// was over its budget: slower to read on the GPU, but the load goes on rather than failing. They're kept in blocks of
	/// their own, which drain as what's in them is freed.
	/// </summary>
	public long SystemFallbackBytes { get; private set; }

	// Per heap: bytes of blocks and bytes handed out; the driver's usage and budget when last read, and our reserved then
	readonly long[] heapReserved, heapUsed, driverUsage, driverBudget, reservedAtFetch;

	/// <summary>
	/// An artificial budget in bytes for each device-local heap, or 0 for the driver's alone: device-local allocations that
	/// would take the heap's usage past it fail as the driver's would when video memory is full, so out-of-memory handling
	/// can be tried on any machine (<see cref="DeviceOptions.MemoryBudgetBytes"/>).
	/// </summary>
	public long BudgetLimit { get; set; }

	/// <summary>Whether a device allocation that doesn't fit video memory goes to system memory (on), or fails. Off for tests.</summary>
	public bool SystemFallback { get; set; } = true;

	/// <summary>For tests: device allocations fail as if video memory were full (going to system memory with <see cref="SystemFallback"/>).</summary>
	internal bool RefuseDeviceLocal { get; set; }

	/// <summary>
	/// Take the driver's usage and budget per heap (VK_EXT_memory_budget), indexed by heap; between calls, usage is estimated
	/// from what this allocator reserves and frees.
	/// </summary>
	public void UpdateBudget( ReadOnlySpan<ulong> usage, ReadOnlySpan<ulong> budget )
	{
		lock ( allocationLock )
		{
			for ( int i = 0; i < heapReserved.Length && i < usage.Length && i < budget.Length; i++ )
			{
				driverUsage[i] = (long)usage[i];
				driverBudget[i] = (long)Math.Min( budget[i], properties.Heap( i ).size );
				reservedAtFetch[i] = heapReserved[i];
			}

			if ( overBudget && !AnyOverBudget() ) overBudget = false;
		}
	}

	/// <summary>Without VK_EXT_memory_budget: usage is what this allocator reserved, the budget 80% of each heap.</summary>
	public void UpdateBudgetWithoutDriver()
	{
		lock ( allocationLock )
		{
			for ( int i = 0; i < heapReserved.Length; i++ )
			{
				driverUsage[i] = heapReserved[i];
				reservedAtFetch[i] = heapReserved[i];
			}

			if ( overBudget && !AnyOverBudget() ) overBudget = false;
		}
	}

	/// <summary>Each heap's budget and use, into <paramref name="heaps"/> as far as it reaches; the number of heaps. Allocates nothing.</summary>
	public int GetHeaps( Span<MemoryHeapBudget> heaps )
	{
		lock ( allocationLock )
		{
			for ( int i = 0; i < heapReserved.Length && i < heaps.Length; i++ )
			{
				heaps[i] = new MemoryHeapBudget
				{
					Index = i,
					DeviceLocal = DeviceLocalHeap( i ),
					Size = (long)properties.Heap( i ).size,
					Budget = HeapBudget( i ),
					Usage = HeapUsage( i ),
					Reserved = heapReserved[i],
					Used = heapUsed[i],
				};
			}

			return heapReserved.Length;
		}
	}

	/// <summary>Device allocations refused for going over a heap's budget so far (made in system memory instead, or failed).</summary>
	public long OverBudgetAllocations { get; private set; }

	/// <summary>Where the warning goes, said once each time video memory goes over its budget until it's back under.</summary>
	public Action<string> Warn { get; set; }

	bool overBudget;

	bool DeviceLocalHeap( int heap ) => (properties.Heap( heap ).flags & Vk.HeapDeviceLocal) != 0;

	// Under the lock
	long HeapUsage( int heap ) => Math.Max( driverUsage[heap] + heapReserved[heap] - reservedAtFetch[heap], 0 );

	long HeapBudget( int heap ) => BudgetLimit > 0 && DeviceLocalHeap( heap ) ? Math.Min( BudgetLimit, driverBudget[heap] ) : driverBudget[heap];

	bool AnyOverBudget()
	{
		for ( int i = 0; i < heapReserved.Length; i++ )
		{
			if ( DeviceLocalHeap( i ) && HeapUsage( i ) > HeapBudget( i ) ) return true;
		}

		return false;
	}

	/// <summary>
	/// Before asking the driver for <paramref name="size"/> bytes of <paramref name="type"/>: fail as the driver would when
	/// video memory is full if it would take the heap past the artificial budget (<see cref="BudgetLimit"/>), or past the
	/// driver's for <paramref name="content"/>, which can live in system memory while streaming drops detail. Only when
	/// the allocation can go to system memory instead (<paramref name="refusable"/>); others (and other allocations past
	/// the driver's budget: targets, tables) are left for the driver to place.
	/// </summary>
	void CheckBudget( int type, ulong size, bool content, bool refusable )
	{
		var heap = (int)properties.Type( type ).heapIndex;
		if ( !DeviceLocalHeap( heap ) ) return;

		string warning = null;
		lock ( allocationLock )
		{
			var over = HeapUsage( heap ) + (long)size > HeapBudget( heap );
			if ( !over || !refusable || !(content || BudgetLimit > 0) ) return;

			OverBudgetAllocations++;
			if ( !overBudget )
			{
				overBudget = true;
				warning = $"video memory is over its budget ({HeapUsage( heap ) >> 20} MiB used, {HeapBudget( heap ) >> 20} MiB budget{(BudgetLimit > 0 ? ", capped" : "")}): textures and geometry go to system memory and streamed textures drop detail";
			}
		}

		if ( warning is not null ) Warn?.Invoke( warning );
		throw new GpuException( "vkAllocateMemory (over the memory budget)", VkResult.ErrorOutOfDeviceMemory );
	}

	/// <summary>Bytes handed out from a block of <paramref name="type"/> change by <paramref name="bytes"/>; under the lock.</summary>
	void CountUsed( int type, long bytes )
	{
		Used = (ulong)((long)Used + bytes);
		heapUsed[properties.Type( type ).heapIndex] += bytes;
	}

	/// <summary>
	/// Memory for a buffer or an image. A <paramref name="target"/> (an image the GPU draws or writes into: a view's colour,
	/// depth and storage targets) over a quarter of a small block gets its own allocation rather than room in a large block:
	/// targets come and go with views and their sizes, and the holes they left kept large blocks reserved but mostly empty.
	/// <paramref name="content"/> allocations (what a map loads and frees as a whole: its textures and geometry) share blocks
	/// only with each other: mixed with long-lived resources made meanwhile (a game's targets, tables that grew), those kept
	/// most blocks of an unloaded map's memory from going back.
	/// </summary>
	public Allocation Allocate( in VkMemoryRequirements requirements, MemoryLocation location, bool image, bool target = false, bool content = false )
	{
		try
		{
			if ( RefuseDeviceLocal && location == MemoryLocation.Device ) throw new GpuException( "vkAllocateMemory (refused for a test)", VkResult.ErrorOutOfDeviceMemory );
			var refusable = location == MemoryLocation.Device && SystemFallback && FallbackType( requirements.memoryTypeBits ) is not null;
			return Allocate( requirements, image, target, FindType( requirements.memoryTypeBits, location ), content, refusable, fallback: false );
		}
		catch ( GpuException e ) when ( e.Result == (int)VkResult.ErrorOutOfDeviceMemory && location == MemoryLocation.Device && SystemFallback && FallbackType( requirements.memoryTypeBits ) is { } fallback )
		{
			var allocation = Allocate( requirements, image, target, fallback, content, refusable: false, fallback: true );
			lock ( allocationLock ) SystemFallbackBytes += (long)allocation.Size;
			return allocation;
		}
	}

	/// <summary>A memory type among <paramref name="typeBits"/> that isn't video memory, for when video memory is full.</summary>
	int? FallbackType( uint typeBits )
	{
		for ( int i = 0; i < properties.memoryTypeCount; i++ )
		{
			if ( (typeBits & (1u << i)) == 0 ) continue;
			var flags = properties.Type( i ).propertyFlags;
			if ( (flags & Vk.MemoryDeviceLocal) == 0 && (flags & Vk.MemoryLazilyAllocated) == 0 ) return i;
		}

		return null;
	}

	/// <summary>
	/// Size of the large blocks, made only once something needs one: allocations over a quarter of a small block and up to
	/// a quarter of this (a streamed texture's mips, a page of vertices) share them instead of each being the driver's.
	/// </summary>
	public const ulong LargeBlockSize = 128ul << 20;

	/// <summary>
	/// Whether an empty block of a kind is made ahead on a worker once the last one is taken (on), so the next allocation
	/// that needs a block finds one rather than waiting for the driver. Off makes blocks where they're needed.
	/// </summary>
	public bool MakeBlocksAhead { get; init; } = true;

	/// <summary><see cref="MakeBlocksAhead"/> for every allocator, for comparisons.</summary>
	public static bool BlocksAhead { get; set; } = true;

	/// <summary>
	/// Whether images over a quarter of a small block share large blocks (<c>BOREALIS_LARGE_BLOCKS=1</c>), or each is its own
	/// allocation (the default). Large blocks of a map's big textures stranded 76-114 MiB on Construct, holes between
	/// 4-32 MiB images that only a new block could pack; their own allocations strand nothing for a driver call per big
	/// texture made, which the texture streamer makes on its reading workers.
	/// </summary>
	public static bool LargeBlocks { get; set; } = Environment.GetEnvironmentVariable( "BOREALIS_LARGE_BLOCKS" ) == "1";

	/// <summary><see cref="LargeBlocks"/> for this allocator.</summary>
	public bool ShareLargeBlocks { get; init; } = LargeBlocks;

	/// <summary>Blocks asked of the driver so far (<c>vkAllocateMemory</c>), shared and dedicated.</summary>
	public long DriverAllocations => System.Threading.Interlocked.Read( ref driverAllocations );

	long driverAllocations;

	/// <summary><see cref="System.Diagnostics.Stopwatch"/> ticks the driver's allocations took, in all and the longest.</summary>
	public long DriverAllocationTicks => System.Threading.Interlocked.Read( ref driverAllocationTicks );

	/// <inheritdoc cref="DriverAllocationTicks"/>
	public long LongestDriverAllocation => System.Threading.Interlocked.Read( ref longestDriverAllocation );

	long driverAllocationTicks, longestDriverAllocation;

	// Kinds (type, images, large) with a block being made ahead
	readonly HashSet<(int Type, bool Images, bool Large, bool Content, bool Fallback)> makingAhead = new();
	bool disposed;

	// The driver's allocations (vkAllocateMemory, which needs no external lock) are made outside the allocator's lock: a big
	// one takes milliseconds while the driver clears it, and every other thread's allocation waited behind it. A map's
	// first seconds made hundreds, about 2 ms each, nearly 0.5 s of them on the main thread
	/// <summary>
	/// While set on a thread, its allocations only take room in shared blocks that exist: one that would need a new block,
	/// or an allocation of its own, fails with <c>VK_ERROR_FRAGMENTED_POOL</c>. For moving out of a block being drained
	/// (<see cref="Device.Relocate"/>), where a new block would only swap one part-empty block for another.
	/// </summary>
	[ThreadStatic] internal static bool ExistingBlocksOnly;

	// The one block moves out of a large block being drained made (ChooseDraining), which they may fill though it starts empty
	MemoryBlock movesInto;

	/// <summary>
	/// While set on a thread, its buffers are allocations of their own (<see cref="OwnBuffer"/>) whatever their size: upload
	/// pages, whose number rises and falls with how many lists a frame records, left 4 MiB holes through shared blocks.
	/// </summary>
	[ThreadStatic] internal static bool OwnAllocation;

	static GpuException NoRoom() => new( "vkAllocateMemory (no room in the blocks there are)", VkResult.ErrorFragmentedPool );

	Allocation Allocate( in VkMemoryRequirements requirements, bool image, bool target, int type, bool content, bool refusable, bool fallback )
	{
		var size = requirements.size;
		var alignment = Math.Max( requirements.alignment, 1 );
		var ownAbove = (ShareLargeBlocks && !target ? LargeBlockSize : blockSize) / 4;
		if ( ExistingBlocksOnly && (fallback || (!image && size > OwnBufferSize && (content || OwnBigBuffers)) || size > ownAbove) ) throw NoRoom();

		// A buffer over a quarter of a small block is its own (a map's vertex and index pages, views' visibility buffers,
		// tables that grow): one still used after what was made beside it has gone doesn't hold a large block
		if ( !image && (OwnAllocation || (size > OwnBufferSize && (content || OwnBigBuffers))) ) return OwnBuffer( type, size, alignment, content, refusable, fallback );

		// Anything over a quarter of a large block gets its own allocation, and so does a target too big for a small block
		if ( size > ownAbove )
		{
			var dedicated = NewBlock( type, size, image, content, refusable );
			dedicated.Dedicated = true;
			dedicated.Fallback = fallback;
			dedicated.TryAllocate( size, alignment, out var dedicatedOffset, out var dedicatedNode );
			lock ( allocationLock )
			{
				Allocations++;
				Add( dedicated );
				CountUsed( type, (long)size );
			}

			return new Allocation( dedicated, dedicatedOffset, size, dedicatedNode );
		}

		var large = size > blockSize / 4;
		ulong freshSize;
		lock ( allocationLock )
		{
			Allocations++;
			if ( TryShared( type, image, large, content, fallback, size, alignment ) is { } shared ) return shared;
			freshSize = large ? LargeBlockSize : NextBlockSize( type, image, content, fallback, size + alignment );
		}

		if ( ExistingBlocksOnly )
		{
			// A move out of a large block being drained may make one smaller block, when that was what made draining it worth it
			lock ( allocationLock )
			{
				if ( !kinds.TryGetValue( (type, image, large, content, fallback), out var draining ) || draining.Draining is null || !draining.DrainMayMake || draining.DrainMade || size > DrainBlockSize ) throw NoRoom();
				draining.DrainMade = true;
				freshSize = DrainBlockSize;
			}
		}

		// A new block, made without the lock; another thread (or the one made ahead) may have added one meanwhile, which is
		// tried first, and then this one stays as a spare
		var fresh = NewBlock( type, freshSize, image, content, refusable );
		fresh.Large = large;
		fresh.Content = content;
		fresh.Fallback = fallback;
		lock ( allocationLock )
		{
			Add( fresh );
			if ( ExistingBlocksOnly ) movesInto = fresh;
			if ( TryShared( type, image, large, content, fallback, size, alignment ) is { } placed ) return placed;
			throw new GpuException( "A new memory block couldn't fit an allocation" );
		}
	}

	/// <summary>
	/// After taking room in <paramref name="taken"/>: when no empty block of its kind is left, make one on a worker (one at a
	/// time per kind). Under the lock.
	/// </summary>
	void MakeAhead( MemoryBlock taken )
	{
		if ( !MakeBlocksAhead || !BlocksAhead || disposed || ExistingBlocksOnly ) return;
		var kind = (taken.TypeIndex, taken.Images, taken.Large, taken.Content, taken.Fallback);
		if ( makingAhead.Contains( kind ) || KindOf( taken ).Spares > 0 ) return;

		makingAhead.Add( kind );
		var size = kind.Large ? LargeBlockSize : NextBlockSize( kind.TypeIndex, kind.Images, kind.Content, kind.Fallback, 0 );
		System.Threading.ThreadPool.UnsafeQueueUserWorkItem( static state =>
		{
			var (allocator, kind, size) = state;
			MemoryBlock block = null;
			try
			{
				block = allocator.NewBlock( kind.TypeIndex, size, kind.Images, kind.Content, refusable: true );
				block.Large = kind.Large;
				block.Content = kind.Content;
				block.Fallback = kind.Fallback;
			}
			catch ( GpuException )
			{
				// Out of memory ahead: the allocation that needs it reports it
			}

			lock ( allocator.allocationLock )
			{
				allocator.makingAhead.Remove( kind );
				if ( block is null ) return;
				if ( allocator.disposed )
				{
					allocator.FreeBlock( block, counted: false );
					return;
				}

				allocator.Add( block );
			}
		}, (this, kind, size), preferLocal: false );
	}

	/// <summary>
	/// Whether a kind's small blocks start small and grow (on): its first is <see cref="FirstBlockSize"/>
	/// (<see cref="FirstReadbackBlockSize"/> for readback memory) and each new one twice its largest, up to
	/// <see cref="BlockSize"/>, so a kind with a little in it (readback buffers, a menu's content) doesn't hold a whole
	/// block. Off makes every small block <see cref="BlockSize"/>, for tests of block placement.
	/// </summary>
	public bool GrowBlocks { get; init; } = true;

	/// <summary>The first small block of a kind (<see cref="GrowBlocks"/>).</summary>
	public const ulong FirstBlockSize = 2ul << 20, FirstReadbackBlockSize = 1ul << 20;

	/// <summary>
	/// The size of a kind's next small block: twice its largest, from the first size, up to <see cref="BlockSize"/>, and at
	/// least <paramref name="fits"/>. Under the lock; allocates nothing.
	/// </summary>
	ulong NextBlockSize( int type, bool image, bool content, bool fallback, ulong fits )
	{
		if ( !GrowBlocks ) return blockSize;
		ulong largest = 0;
		if ( kinds.TryGetValue( (type, image, false, content, fallback), out var kind ) )
		{
			foreach ( var block in kind.Blocks ) largest = Math.Max( largest, block.Size );
		}

		var flags = properties.Type( type ).propertyFlags;
		var readback = (flags & Vk.MemoryHostCached) != 0 && (flags & Vk.MemoryDeviceLocal) == 0;
		var next = largest == 0 ? (readback ? FirstReadbackBlockSize : FirstBlockSize) : Math.Min( largest * 2, blockSize );
		while ( next < fits && next < blockSize ) next *= 2;
		return Math.Min( next, blockSize );
	}

	/// <summary>
	/// Buffers bigger than this (a quarter of a small block) are allocations of their own (<see cref="OwnBuffer"/>). After a
	/// map was unloaded, large blocks were kept by one long-lived buffer each (a growing table, a view's buffer) that had
	/// been placed among the map's.
	/// </summary>
	public ulong OwnBufferSize => blockSize / 4;

	/// <summary>
	/// Whether every buffer over <see cref="OwnBufferSize"/> is its own (on), or only content's (geometry pages) while the rest
	/// share large blocks. For comparisons: <c>BOREALIS_SHARED_BIG_BUFFERS=1</c> turns it off.
	/// </summary>
	public static bool OwnBigBuffers { get; set; } = Environment.GetEnvironmentVariable( "BOREALIS_SHARED_BIG_BUFFERS" ) != "1";

	// Own buffer sizes with spares being made ahead (how many), and how often each size has been taken
	readonly Dictionary<(int Type, ulong Size), int> makingPagesAhead = new();
	readonly Dictionary<(int Type, ulong Size), int> pageTakes = new();

	/// <summary>
	/// Spares of an own buffer size made ahead after a take found none ready (<see cref="OwnBuffer"/>): a map's loads take
	/// upload and geometry pages in bursts, and one made ahead at a time left most of them to the driver on the thread that
	/// asked (about 350 in the seconds after a load, 0.7-3 ms each). They're spares like any other, freed once unused.
	/// </summary>
	public const int PagesAheadInBurst = 3;

	/// <summary>
	/// A big buffer's own allocation: an empty one of its size kept as a spare (made ahead, or freed and not yet trimmed)
	/// when there is one, else the driver's. Once a size has been taken before (pages, and each view's buffers, come at a
	/// few sizes), spares of it are made ahead on a worker: one while takes find one ready, <see cref="PagesAheadInBurst"/>
	/// once one didn't, so a map loading doesn't wait for the driver on the thread that makes them.
	/// </summary>
	Allocation OwnBuffer( int type, ulong size, ulong alignment, bool content, bool refusable, bool fallback )
	{
		lock ( allocationLock )
		{
			Allocations++;
			foreach ( var block in spares )
			{
				if ( !block.Dedicated || !block.Reusable || block.Used != 0 || block.Size != size || block.TypeIndex != type || block.Images || block.Fallback != fallback ) continue;
				Unspare( block );
				block.TryAllocate( size, alignment, out var spareOffset, out var spareNode );
				CountUsed( type, (long)size );
				if ( !fallback ) PageTaken( type, size, ready: true );
				return new Allocation( block, spareOffset, size, spareNode );
			}
		}

		var page = NewBlock( type, size, image: false, content, refusable );
		page.Dedicated = true;
		page.Reusable = true;
		page.Fallback = fallback;
		page.TryAllocate( size, alignment, out var offset, out var node );
		lock ( allocationLock )
		{
			Add( page );
			CountUsed( type, (long)size );
			if ( !fallback ) PageTaken( type, size, ready: false );
		}

		return new Allocation( page, offset, size, node );
	}

	/// <summary>
	/// A page of <paramref name="size"/> was taken (a spare <paramref name="ready"/>, or the driver's): once the size has been
	/// taken before, make spares of it ahead, one or <see cref="PagesAheadInBurst"/> with those already kept or being made.
	/// Under the lock.
	/// </summary>
	void PageTaken( int type, ulong size, bool ready )
	{
		var key = (type, size);
		var takes = pageTakes.GetValueOrDefault( key ) + 1;
		pageTakes[key] = takes;
		if ( takes < 2 || !MakeBlocksAhead || !BlocksAhead || disposed ) return;

		var wanted = ready ? 1 : PagesAheadInBurst;
		for ( var have = SparePages( type, size ) + makingPagesAhead.GetValueOrDefault( key ); have < wanted; have++ )
		{
			makingPagesAhead[key] = makingPagesAhead.GetValueOrDefault( key ) + 1;
			MakePageAhead( type, size );
		}
	}

	/// <summary>Make an empty own buffer allocation of <paramref name="size"/> on a worker and keep it as a spare.</summary>
	void MakePageAhead( int type, ulong size )
	{
		System.Threading.ThreadPool.UnsafeQueueUserWorkItem( static state =>
		{
			var (allocator, type, size) = state;
			MemoryBlock block = null;
			try
			{
				block = allocator.NewBlock( type, size, image: false, content: false, refusable: true );
				block.Dedicated = true;
				block.Reusable = true;
			}
			catch ( GpuException )
			{
				// Out of memory ahead: the allocation that needs it reports it
			}

			lock ( allocator.allocationLock )
			{
				var making = allocator.makingPagesAhead[(type, size)] - 1;
				if ( making > 0 ) allocator.makingPagesAhead[(type, size)] = making;
				else allocator.makingPagesAhead.Remove( (type, size) );
				if ( block is null ) return;
				if ( allocator.disposed )
				{
					allocator.FreeBlock( block, counted: false );
					return;
				}

				allocator.Add( block );
			}
		}, (this, type, size), preferLocal: false );
	}

	/// <summary>Empty own buffer allocations of <paramref name="size"/> kept; under the lock.</summary>
	int SparePages( int type, ulong size )
	{
		var count = 0;
		foreach ( var block in spares )
		{
			if ( block.Dedicated && block.Reusable && block.Used == 0 && block.Size == size && block.TypeIndex == type && !block.Images ) count++;
		}

		return count;
	}

	/// <summary>Whether an empty own buffer allocation of <paramref name="size"/> other than <paramref name="except"/> is kept; under the lock.</summary>
	bool HasSparePage( int type, ulong size, MemoryBlock except )
	{
		foreach ( var block in spares )
		{
			if ( block != except && block.Dedicated && block.Reusable && block.Used == 0 && block.Size == size && block.TypeIndex == type && !block.Images ) return true;
		}

		return false;
	}

	/// <summary>Room in the fullest shared block of its kind with room, so the emptier ones drain; under the lock.</summary>
	Allocation? TryShared( int type, bool image, bool large, bool content, bool fallback, ulong size, ulong alignment )
	{
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		try
		{
			return FindShared( type, image, large, content, fallback, size, alignment );
		}
		finally
		{
			SharedLookups++;
			SharedLookupTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
		}
	}

	/// <summary>Shared-block lookups (<see cref="TryShared"/>) so far, and the <see cref="System.Diagnostics.Stopwatch"/> ticks they took under the lock.</summary>
	public long SharedLookups { get; private set; }

	/// <inheritdoc cref="SharedLookups"/>
	public long SharedLookupTicks { get; private set; }

	/// <summary>Frees so far, and the <see cref="System.Diagnostics.Stopwatch"/> ticks their blocks took to give the range back.</summary>
	public long Releases { get; private set; }

	/// <inheritdoc cref="Releases"/>
	public long ReleaseTicks { get; private set; }

	Allocation? FindShared( int type, bool image, bool large, bool content, bool fallback, ulong size, ulong alignment )
	{
		if ( !kinds.TryGetValue( (type, image, large, content, fallback), out var kind ) ) return null;
		MemoryBlock best = null;
		foreach ( var block in kind.Blocks )
		{
			// A move out of a draining block never fills an empty one: that block would only be the next to drain
			if ( block.Draining || (best is not null && block.Used <= best.Used) || (ExistingBlocksOnly && block.Used == 0 && block != movesInto) ) continue;
			if ( block.Fits( size, alignment ) ) best = block;
		}

		if ( best is not null && best.Used == 0 ) Unspare( best );
		if ( best is null || !best.TryAllocate( size, alignment, out var offset, out var node ) ) return null;
		CountUsed( type, (long)size );
		MakeAhead( best );
		return new Allocation( best, offset, size, node );
	}

	public void Free( in Allocation allocation )
	{
		if ( allocation.Block is null ) return;

		lock ( allocationLock )
		{
			var block = allocation.Block;
			var start = System.Diagnostics.Stopwatch.GetTimestamp();
			block.Release( allocation.Node );
			ReleaseTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
			Releases++;
			CountUsed( block.TypeIndex, -(long)allocation.Size );
			if ( block.Fallback ) SystemFallbackBytes -= (long)allocation.Size;

			// Dedicated blocks go at once, but for one big buffer's of each size kept as a spare for a while (Trim); shared ones
			// once empty, keeping one spare per kind for a while
			if ( block.Dedicated && block.Reusable && !HasSparePage( block.TypeIndex, block.Size, block ) ) KeepSpare( block );
			else if ( block.Dedicated || (block.Used == 0 && HasSpare( block )) )
			{
				Remove( block );
				FreeBlock( block );
			}
			else if ( block.Used == 0 ) KeepSpare( block );
		}
	}

	/// <summary>
	/// Move on to <paramref name="newFrame"/>: free the empty blocks kept as spares for <see cref="SpareFrames"/> frames.
	/// Once per frame; allocates nothing.
	/// </summary>
	public void Trim( ulong newFrame )
	{
		lock ( allocationLock )
		{
			frame = newFrame;
			for ( int i = spares.Count - 1; i >= 0; i-- )
			{
				var block = spares[i];
				if ( frame - block.EmptySince < SpareFrames ) continue;
				Remove( block );
				FreeBlock( block );
			}

			if ( Defragment && frame % DrainCheckFrames == 0 ) ChooseDraining();
		}
	}

	/// <summary>
	/// Whether part-empty blocks of content images are drained (on), once no block has been made for <see cref="QuietFrames"/>:
	/// one block of a kind at a time, the emptiest under
	/// <see cref="DrainBelow"/> full whose contents fit in the free room of the kind's other blocks, takes nothing new while
	/// what's in it that can be made again elsewhere is (the texture streamer moves its textures,
	/// <see cref="Device.InDrainingBlock"/>), so it empties and goes back to the driver.
	/// </summary>
	public bool Defragment { get; set; } = Environment.GetEnvironmentVariable( "BOREALIS_DEFRAGMENT" ) != "0";

	/// <summary>How full a content block may be to be drained.</summary>
	public const double DrainBelow = 0.5;

	/// <summary>
	/// How full a block that isn't content may be to be drained: any, as long as the kind's other blocks have room for what
	/// it holds and a block's worth is free in all. A move costs a copy and one wait for the GPU between frames, not a
	/// texture's rebuild, and what these blocks hold (cube captures, targets, tables) leaves them part-used, not near empty.
	/// </summary>
	public const double MoveBelow = 1.0;

	/// <summary>The size of the one block moves out of a large block being drained may make (<see cref="ChooseDraining"/>).</summary>
	public const ulong DrainBlockSize = 32ul << 20;

	/// <summary>Frames a block is drained for before it's left as it is (what's left in it can't move), and not tried again for <see cref="DrainRetryFrames"/>.</summary>
	public const ulong DrainFrames = 900, DrainRetryFrames = 3600;

	const ulong DrainCheckFrames = 30;

	/// <summary>Blocks drained so far: begun, emptied, and given up on.</summary>
	public long DrainsBegun { get; private set; }

	/// <inheritdoc cref="DrainsBegun"/>
	public long DrainsEmptied { get; private set; }

	/// <inheritdoc cref="DrainsBegun"/>
	public long DrainsAbandoned { get; private set; }

	/// <summary>Frames without a new block before blocks are drained: while a map loads, blocks fill and empty by themselves.</summary>
	public const ulong QuietFrames = 120;

	/// <summary>Seconds without a new block of a kind before its blocks are drained, as well as <see cref="QuietFrames"/>.</summary>
	public double QuietSeconds { get; set; } = 2;


	// Under the lock; allocates nothing
	void ChooseDraining()
	{
		foreach ( var (key, kind) in kinds )
		{
			if ( kind.Draining is { } draining )
			{
				if ( frame - draining.DrainFrame < DrainFrames ) continue;

				// What's left can't move (textures that don't stream): leave it, and try it again much later
				StopDrain( draining );
				draining.DrainFrame = frame + DrainRetryFrames;
				DrainsAbandoned++;
				continue;
			}

			// Per kind: upload memory makes and frees blocks all the time, which says nothing about images or device buffers
			// In frames and in time: the menu runs at a thousand frames a second, where 120 frames is a blink of a load
			var quiet = frame - kind.LastNewBlock >= QuietFrames && System.Diagnostics.Stopwatch.GetElapsedTime( kind.LastNewBlockTicks ).TotalSeconds >= QuietSeconds;
			if ( !quiet || !Movable( key.Type, key.Images, key.Content ) || key.Fallback || kind.Blocks.Count < 2 ) continue;

			MemoryBlock emptiest = null;
			ulong free = 0;
			foreach ( var block in kind.Blocks )
			{
				if ( block.Used != 0 ) free += block.Size - block.Used;
				if ( block.Used == 0 || block.Pinned || frame < block.DrainFrame || block.Used > block.Size * (key.Content ? DrainBelow : MoveBelow) ) continue;
				if ( emptiest is not null && block.Used >= emptiest.Used ) continue;

				// What the device moves is made again at once, all of it or none: only a block whose contents would pack into the
				// others' room (making the copies to find out costs the frame a driver call per resource)
				if ( !key.Content && !PacksElsewhere( kind, block ) ) continue;
				emptiest = block;
			}

			// Only when the rest of the kind has room for what it holds (moves never make a block), and the kind has a block's
			// worth free in all, so emptying one leaves fewer. A large block whose contents fit in the others' room and one
			// smaller block (with a quarter to spare for what doesn't pack) is worth it too: 128 MiB back for 32
			if ( emptiest is null ) continue;
			var elsewhere = free - (emptiest.Size - emptiest.Used);
			var mayMake = false;
			if ( elsewhere < emptiest.Used || free < emptiest.Size )
			{
				mayMake = key.Large && emptiest.Size == LargeBlockSize && (elsewhere + DrainBlockSize) * 4 >= emptiest.Used * 5;
				if ( !mayMake ) continue;
			}

			kind.DrainMayMake = mayMake;
			kind.DrainMade = false;
			StartDrain( kind, emptiest );
		}
	}

	readonly List<ulong> packSizes = new();
	readonly List<(ulong Offset, ulong Size)> packRanges = new();

	/// <summary>
	/// Whether what <paramref name="candidate"/> holds would fit in the free room of its kind's other part-used blocks,
	/// largest first, each at the first range that holds it at a cautious alignment (64 KiB for images of that size or
	/// more, 4 KiB for smaller ones, 256 bytes for buffers). A guess the copies then prove; under the lock, allocates nothing
	/// once its scratch has room.
	/// </summary>
	bool PacksElsewhere( Kind kind, MemoryBlock candidate )
	{
		packSizes.Clear();
		packRanges.Clear();
		candidate.CollectSizes( packSizes );
		foreach ( var block in kind.Blocks )
		{
			if ( block != candidate && !block.Draining && block.Used != 0 ) block.CollectFree( packRanges );
		}

		packSizes.Sort();
		for ( int i = packSizes.Count - 1; i >= 0; i-- )
		{
			var size = packSizes[i];
			var alignment = !candidate.Images ? 256ul : size >= 65536 ? 65536ul : 4096ul;
			var placed = false;
			for ( int r = 0; r < packRanges.Count && !placed; r++ )
			{
				var (offset, length) = packRanges[r];
				var start = (offset + alignment - 1) & ~(alignment - 1);
				if ( start + size > offset + length ) continue;
				packRanges[r] = (start + size, offset + length - start - size);
				placed = true;
			}

			if ( !placed ) return false;
		}

		return true;
	}

	/// <summary>
	/// Whether blocks of a kind can be drained: content images (the texture streamer moves them) and, with
	/// <see cref="Relocation"/>, device buffers and images in video memory that aren't content (the device moves them in
	/// place between frames, <see cref="Device"/>'s relocation).
	/// </summary>
	bool Movable( int type, bool images, bool content )
	{
		if ( content ) return images;
		if ( !Relocation || (properties.Type( type ).propertyFlags & Vk.MemoryDeviceLocal) == 0 ) return false;
		return images ? RelocateImages : RelocateBuffers;
	}

	/// <summary>
	/// Whether blocks of device buffers and images that aren't content are drained too, by moving what's in them in place
	/// (on). <c>BOREALIS_RELOCATE=0</c> turns it off, for comparisons.
	/// </summary>
	public static bool Relocation { get; set; } = Environment.GetEnvironmentVariable( "BOREALIS_RELOCATE" ) != "0";

	/// <summary>Which of <see cref="Relocation"/>'s kinds are drained: device buffers, and images.</summary>
	public bool RelocateBuffers { get; set; } = true;

	/// <inheritdoc cref="RelocateBuffers"/>
	public bool RelocateImages { get; set; } = true;

	// Under the lock
	void StartDrain( Kind kind, MemoryBlock block )
	{
		block.Draining = true;
		block.DrainFrame = frame;
		kind.Draining = block;
		drainingCount++;
		if ( !block.Content ) drainingToMove++;
		DrainsBegun++;
	}

	// Under the lock: a block stops draining, emptied or not
	void StopDrain( MemoryBlock block )
	{
		block.Draining = false;
		drainingCount--;
		if ( !block.Content ) drainingToMove--;
		var kind = KindOf( block );
		if ( kind.Draining == block ) kind.Draining = null;
	}

	/// <summary>Whether a block that isn't content (which the device moves, not the streamer) is being drained.</summary>
	public bool AnyDrainingToMove => System.Threading.Volatile.Read( ref drainingToMove ) > 0;

	int drainingToMove;

	/// <summary>The blocks that aren't content being drained, into <paramref name="into"/>; allocates nothing past its room.</summary>
	internal void DrainingToMove( List<MemoryBlock> into )
	{
		lock ( allocationLock )
		{
			foreach ( var kind in kinds.Values )
			{
				if ( kind.Draining is { Content: false } block ) into.Add( block );
			}
		}
	}

	/// <summary>What the allocations in <paramref name="block"/> hold (<see cref="SetOwner"/>), into <paramref name="owners"/>.</summary>
	internal void CollectOwners( MemoryBlock block, List<ulong> owners )
	{
		lock ( allocationLock ) block.CollectOwners( owners );
	}

	/// <summary>Never drain the block <paramref name="allocation"/> is in: it holds something that can't move.</summary>
	public void Pin( in Allocation allocation )
	{
		if ( allocation.Block is null ) return;
		lock ( allocationLock ) allocation.Block.Pinned = true;
	}

	/// <summary>Note what <paramref name="allocation"/> holds, so moving out of a block being drained can find it.</summary>
	public void SetOwner( in Allocation allocation, ulong owner )
	{
		if ( allocation.Block is null ) return;
		lock ( allocationLock ) allocation.Block.SetOwner( allocation.Node, owner );
	}

	/// <summary>Stop draining <paramref name="block"/>: something in it can't move. Not tried again for <see cref="DrainRetryFrames"/>.</summary>
	internal void Abandon( MemoryBlock block )
	{
		lock ( allocationLock )
		{
			if ( !block.Draining ) return;
			StopDrain( block );
			block.DrainFrame = frame + DrainRetryFrames;
			DrainsAbandoned++;
		}
	}

	/// <summary>For tests: drain <paramref name="block"/> now, as <see cref="Trim"/> would pick it.</summary>
	internal void Drain( MemoryBlock block )
	{
		lock ( allocationLock )
		{
			var kind = KindOf( block );
			if ( block.Dedicated || block.Draining || kind.Draining is not null ) return;
			StartDrain( kind, block );
		}
	}

	/// <summary>For tests: stop draining <paramref name="block"/>.</summary>
	internal void Undrain( MemoryBlock block )
	{
		lock ( allocationLock ) EndDrain( block );
	}

	/// <summary>
	/// Stop draining the block <paramref name="allocation"/> is in, as <see cref="Trim"/> does when it hasn't emptied in
	/// <see cref="DrainFrames"/>: something in it has no room elsewhere.
	/// </summary>
	public void Abandon( in Allocation allocation )
	{
		lock ( allocationLock )
		{
			var block = allocation.Block;
			if ( block is not { Draining: true } ) return;
			StopDrain( block );
			block.DrainFrame = frame + DrainRetryFrames;
			DrainsAbandoned++;
		}
	}

	/// <summary>Whether any block is being drained.</summary>
	public bool AnyDraining => System.Threading.Volatile.Read( ref drainingCount ) > 0;

	/// <summary>Whether a content block (which the texture streamer moves textures out of) is being drained.</summary>
	public bool AnyDrainingContent => System.Threading.Volatile.Read( ref drainingCount ) > System.Threading.Volatile.Read( ref drainingToMove );

	int drainingCount;

	/// <summary>Whether <paramref name="allocation"/> is in a block being drained, so it should be made again elsewhere.</summary>
	public bool Draining( in Allocation allocation ) => allocation.Block is { Draining: true };

	bool HasSpare( MemoryBlock empty ) => KindOf( empty ).Spares > (empty.Spare ? 1 : 0);

	/// <summary>A block from the driver, not yet in <see cref="blocks"/> (<see cref="Add"/>): made without the lock.</summary>
	MemoryBlock NewBlock( int type, ulong size, bool image, bool content, bool refusable )
	{
		CheckBudget( type, size, content, refusable );
		System.Threading.Interlocked.Increment( ref driverAllocations );
		var info = new VkMemoryAllocateInfo
		{
			sType = VkStructureType.MemoryAllocateInfo,
			allocationSize = size,
			memoryTypeIndex = (uint)type,
		};

		ulong memory;
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		using ( Owner is { } owner ? owner.AllocateMemoryZone() : default )
			vk.vkAllocateMemory( vk.Device, &info, null, &memory ).Check( "vkAllocateMemory" );
		var ticks = System.Diagnostics.Stopwatch.GetTimestamp() - start;
		System.Threading.Interlocked.Add( ref driverAllocationTicks, ticks );
		long longest;
		while ( ticks > (longest = System.Threading.Interlocked.Read( ref longestDriverAllocation )) && System.Threading.Interlocked.CompareExchange( ref longestDriverAllocation, ticks, longest ) != longest ) { }

		var block = new MemoryBlock( size ) { Memory = memory, TypeIndex = type, Images = image };

		if ( (properties.Type( type ).propertyFlags & Vk.MemoryHostVisible) != 0 )
		{
			void* mapped;
			vk.vkMapMemory( vk.Device, memory, 0, Vk.WholeSize, 0, &mapped ).Check( "vkMapMemory" );
			block.Mapped = (nint)mapped;
		}

		return block;
	}

	/// <summary>Keep a block made by <see cref="NewBlock"/>; under the lock.</summary>
	void Add( MemoryBlock block )
	{
		blocks.Add( block );
		if ( !block.Dedicated ) KindOf( block ).Blocks.Add( block );
		if ( block.Used == 0 ) KeepSpare( block );
		Reserved += block.Size;
		heapReserved[properties.Type( block.TypeIndex ).heapIndex] += (long)block.Size;

		// Only shared blocks: allocations of their own (big streamed textures, upload pages) come and go all the time and
		// leave no holes, and counting them kept blocks from ever draining while textures streamed
		if ( !block.Dedicated )
		{
			var kind = KindOf( block );
			kind.LastNewBlock = frame;
			kind.LastNewBlockTicks = System.Diagnostics.Stopwatch.GetTimestamp();
		}
	}

	/// <summary>Take a block out of the lists before it's freed; under the lock.</summary>
	void Remove( MemoryBlock block )
	{
		blocks.Remove( block );
		if ( !block.Dedicated ) KindOf( block ).Blocks.Remove( block );
		Unspare( block );
		EndDrain( block );
		if ( movesInto == block ) movesInto = null;
	}

	// A drained block emptied (or went): it takes allocations again
	void EndDrain( MemoryBlock block )
	{
		if ( !block.Draining ) return;
		StopDrain( block );
		DrainsEmptied++;
	}

	/// <summary>The shared blocks of a block's kind; under the lock.</summary>
	Kind KindOf( MemoryBlock block )
	{
		var key = (block.TypeIndex, block.Images, block.Large, block.Content, block.Fallback);
		if ( !kinds.TryGetValue( key, out var kind ) ) kinds[key] = kind = new Kind();
		return kind;
	}

	/// <summary>An empty block kept as a spare from now, until <see cref="Trim"/> frees it or something takes it; under the lock.</summary>
	void KeepSpare( MemoryBlock block )
	{
		EndDrain( block );
		block.EmptySince = frame;
		if ( block.Spare ) return;
		block.Spare = true;
		spares.Add( block );
		if ( !block.Dedicated ) KindOf( block ).Spares++;
	}

	void Unspare( MemoryBlock block )
	{
		if ( !block.Spare ) return;
		block.Spare = false;
		spares.Remove( block );
		if ( !block.Dedicated ) KindOf( block ).Spares--;
	}

	void FreeBlock( MemoryBlock block, bool counted = true )
	{
		// Freeing memory unmaps it
		vk.vkFreeMemory( vk.Device, block.Memory, null );
		if ( !counted ) return;
		Reserved -= block.Size;
		heapReserved[properties.Type( block.TypeIndex ).heapIndex] -= (long)block.Size;
	}

	public void Dispose()
	{
		// Blocks being made ahead finish first: the device must outlive their allocations
		lock ( allocationLock ) disposed = true;
		while ( true )
		{
			lock ( allocationLock )
			{
				if ( makingAhead.Count == 0 && makingPagesAhead.Count == 0 ) break;
			}

			System.Threading.Thread.Sleep( 1 );
		}

		lock ( allocationLock )
		{
			foreach ( var block in blocks ) FreeBlock( block );
			blocks.Clear();
		}
	}
}
