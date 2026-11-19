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

	/// <summary>Whether big allocations share large blocks (on), or each over a quarter of a small block is its own. For comparisons.</summary>
	public static bool LargeBlocks { get; set; } = true;

	// Kinds (type, images, large) with a block being made ahead
	readonly HashSet<(int Type, bool Images, bool Large, bool Content, bool Fallback)> makingAhead = new();
	bool disposed;

	// The driver's allocations (vkAllocateMemory, which needs no external lock) are made outside the allocator's lock: a big
	// one takes milliseconds while the driver clears it, and every other thread's allocation waited behind it. A map's
	// first seconds made hundreds, about 2 ms each, nearly 0.5 s of them on the main thread
	Allocation Allocate( in VkMemoryRequirements requirements, bool image, bool target, int type, bool content, bool refusable, bool fallback )
	{
		var size = requirements.size;
		var alignment = Math.Max( requirements.alignment, 1 );

		// A buffer over a quarter of a small block is its own (a map's vertex and index pages, views' visibility buffers,
		// tables that grow): one still used after what was made beside it has gone doesn't hold a large block
		if ( !image && size > OwnBufferSize && (content || OwnBigBuffers) ) return OwnBuffer( type, size, alignment, content, refusable, fallback );

		// Anything over a quarter of a large block gets its own allocation, and so does a target too big for a small block
		if ( size > (LargeBlocks && !target ? LargeBlockSize : blockSize) / 4 )
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
		lock ( allocationLock )
		{
			Allocations++;
			if ( TryShared( type, image, large, content, fallback, size, alignment ) is { } shared ) return shared;
		}

		// A new block, made without the lock; another thread (or the one made ahead) may have added one meanwhile, which is
		// tried first, and then this one stays as a spare
		var fresh = NewBlock( type, large ? LargeBlockSize : blockSize, image, content, refusable );
		fresh.Large = large;
		fresh.Content = content;
		fresh.Fallback = fallback;
		lock ( allocationLock )
		{
			Add( fresh );
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
		if ( !MakeBlocksAhead || !BlocksAhead || disposed ) return;
		var kind = (taken.TypeIndex, taken.Images, taken.Large, taken.Content, taken.Fallback);
		if ( makingAhead.Contains( kind ) || KindOf( taken ).Spares > 0 ) return;

		makingAhead.Add( kind );
		var size = kind.Large ? LargeBlockSize : blockSize;
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

	// Own buffer sizes with a spare being made ahead, and the size last taken per memory type
	readonly HashSet<(int Type, ulong Size)> makingPagesAhead = new();
	readonly Dictionary<int, ulong> lastPageSize = new();

	/// <summary>
	/// A big buffer's own allocation: an empty one of its size kept as a spare (made ahead, or freed and not yet trimmed)
	/// when there is one, else the driver's. Once a size is taken twice in a row (pages, and each view's buffers, come at a
	/// few sizes), the next is made ahead on a worker, so a map loading doesn't wait for the driver on the thread that
	/// makes them.
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
				if ( !fallback ) PageTaken( type, size );
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
			if ( !fallback ) PageTaken( type, size );
		}

		return new Allocation( page, offset, size, node );
	}

	/// <summary>A page of <paramref name="size"/> was taken: make the next ahead when that size was the last taken too. Under the lock.</summary>
	void PageTaken( int type, ulong size )
	{
		var again = lastPageSize.TryGetValue( type, out var last ) && last == size;
		lastPageSize[type] = size;
		if ( !again || !MakeBlocksAhead || !BlocksAhead || disposed || HasSparePage( type, size, null ) || !makingPagesAhead.Add( (type, size) ) ) return;

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
				allocator.makingPagesAhead.Remove( (type, size) );
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
			if ( best is not null && block.Used <= best.Used ) continue;
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
		}
	}

	bool HasSpare( MemoryBlock empty ) => KindOf( empty ).Spares > (empty.Spare ? 1 : 0);

	/// <summary>A block from the driver, not yet in <see cref="blocks"/> (<see cref="Add"/>): made without the lock.</summary>
	MemoryBlock NewBlock( int type, ulong size, bool image, bool content, bool refusable )
	{
		CheckBudget( type, size, content, refusable );
		var info = new VkMemoryAllocateInfo
		{
			sType = VkStructureType.MemoryAllocateInfo,
			allocationSize = size,
			memoryTypeIndex = (uint)type,
		};

		ulong memory;
		using ( Owner is { } owner ? owner.AllocateMemoryZone() : default )
			vk.vkAllocateMemory( vk.Device, &info, null, &memory ).Check( "vkAllocateMemory" );

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
	}

	/// <summary>Take a block out of the lists before it's freed; under the lock.</summary>
	void Remove( MemoryBlock block )
	{
		blocks.Remove( block );
		if ( !block.Dedicated ) KindOf( block ).Blocks.Remove( block );
		Unspare( block );
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
