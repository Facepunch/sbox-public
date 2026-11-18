using Borealis.Gpu.Vulkan;

namespace Borealis.Gpu.Memory;

/// <summary>
/// Part of a <see cref="MemoryBlock"/> a buffer or image is bound to.
/// </summary>
internal readonly record struct Allocation( MemoryBlock Block, ulong Offset, ulong Size )
{
	public ulong Memory => Block.Memory;

	/// <summary>Where it's mapped on the CPU, or 0 for device-only memory.</summary>
	public nint Mapped => Block.Mapped == 0 ? 0 : Block.Mapped + (nint)Offset;
}

/// <summary>
/// One <c>vkAllocateMemory</c> allocation, suballocated first-fit. Host-visible blocks stay mapped.
/// </summary>
internal sealed class MemoryBlock
{
	public ulong Memory;
	public ulong Size;
	public nint Mapped;
	public int TypeIndex;
	public bool Dedicated;
	public bool Images;

	/// <summary>Whether it's a large block (<see cref="MemoryAllocator.LargeBlockSize"/>), for allocations too big for the small ones.</summary>
	public bool Large;

	/// <summary>Whether it holds content (a map's textures and geometry), kept apart from long-lived resources so it drains when the content goes.</summary>
	public bool Content;

	/// <summary>Free ranges, sorted by offset, never adjacent.</summary>
	public readonly List<(ulong Offset, ulong Size)> Free = new();

	public ulong Used;

	/// <summary>The allocator's frame when it last became empty (<see cref="MemoryAllocator.Trim"/>).</summary>
	public ulong EmptySince;

	/// <summary>Whether <see cref="TryAllocate"/> would find room, without taking it.</summary>
	public bool Fits( ulong size, ulong alignment )
	{
		for ( int i = 0; i < Free.Count; i++ )
		{
			var (start, length) = Free[i];
			var aligned = (start + alignment - 1) / alignment * alignment;
			if ( length >= aligned - start + size ) return true;
		}

		return false;
	}

	public bool TryAllocate( ulong size, ulong alignment, out ulong offset )
	{
		for ( int i = 0; i < Free.Count; i++ )
		{
			var (start, length) = Free[i];
			var aligned = (start + alignment - 1) / alignment * alignment;
			var padding = aligned - start;
			if ( length < padding + size ) continue;

			// Leave what's before and after the allocation free
			var after = length - padding - size;
			Free.RemoveAt( i );
			if ( after > 0 ) Free.Insert( i, (aligned + size, after) );
			if ( padding > 0 ) Free.Insert( i, (start, padding) );

			offset = aligned;
			Used += size;
			return true;
		}

		offset = 0;
		return false;
	}

	public void Release( ulong offset, ulong size )
	{
		Used -= size;

		var i = 0;
		while ( i < Free.Count && Free[i].Offset < offset ) i++;
		Free.Insert( i, (offset, size) );

		// Merge with the next range, then the previous
		if ( i + 1 < Free.Count && Free[i].Offset + Free[i].Size == Free[i + 1].Offset )
		{
			Free[i] = (Free[i].Offset, Free[i].Size + Free[i + 1].Size);
			Free.RemoveAt( i + 1 );
		}

		if ( i > 0 && Free[i - 1].Offset + Free[i - 1].Size == Free[i].Offset )
		{
			Free[i - 1] = (Free[i - 1].Offset, Free[i - 1].Size + Free[i].Size);
			Free.RemoveAt( i );
		}
	}
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

		// Small blocks: a part-used block holds back at most this much, and one emptied by a level or play mode goes back
		// to the driver
		blockSize = 16ul << 20;
	}

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
					// Write-combined system memory on discrete GPUs keeps the small device-local window free
					score = integrated ? (deviceLocal ? 100 : 50) : (deviceLocal ? 10 : 100);
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
	/// Device allocations made in system memory because video memory ran out (<c>VK_ERROR_OUT_OF_DEVICE_MEMORY</c>): slower
	/// to read on the GPU, but the load goes on rather than failing.
	/// </summary>
	public long SystemFallbackBytes { get; private set; }

	/// <remarks>
	/// <paramref name="content"/> allocations (what a map loads and frees as a whole: its textures and geometry) share blocks
	/// only with each other: mixed with long-lived resources made meanwhile (a game's targets, tables that grew), those kept
	/// most blocks of an unloaded map's memory from going back.
	/// </remarks>
	public Allocation Allocate( in VkMemoryRequirements requirements, MemoryLocation location, bool image, bool content = false )
	{
		try
		{
			return Allocate( requirements, location, image, FindType( requirements.memoryTypeBits, location ), content );
		}
		catch ( GpuException e ) when ( e.Result == (int)VkResult.ErrorOutOfDeviceMemory && location == MemoryLocation.Device && FallbackType( requirements.memoryTypeBits ) is { } fallback )
		{
			var allocation = Allocate( requirements, location, image, fallback, content );
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
	readonly HashSet<(int Type, bool Images, bool Large, bool Content)> makingAhead = new();
	bool disposed;

	// The driver's allocations (vkAllocateMemory, which needs no external lock) are made outside the allocator's lock: a big
	// one takes milliseconds while the driver clears it, and every other thread's allocation waited behind it. A map's
	// first seconds made hundreds, about 2 ms each, nearly 0.5 s of them on the main thread
	Allocation Allocate( in VkMemoryRequirements requirements, MemoryLocation location, bool image, int type, bool content )
	{
		var size = requirements.size;
		var alignment = Math.Max( requirements.alignment, 1 );

		// Anything over a quarter of a large block gets its own allocation
		if ( size > (LargeBlocks ? LargeBlockSize : blockSize) / 4 )
		{
			var dedicated = NewBlock( type, size, image );
			dedicated.Dedicated = true;
			dedicated.TryAllocate( size, alignment, out var dedicatedOffset );
			lock ( allocationLock )
			{
				Allocations++;
				Add( dedicated );
				Used += size;
			}

			return new Allocation( dedicated, dedicatedOffset, size );
		}

		var large = size > blockSize / 4;
		lock ( allocationLock )
		{
			Allocations++;
			if ( TryShared( type, image, large, content, size, alignment ) is { } shared ) return shared;
		}

		// A new block, made without the lock; another thread (or the one made ahead) may have added one meanwhile, which is
		// tried first, and then this one stays as a spare
		var fresh = NewBlock( type, large ? LargeBlockSize : blockSize, image );
		fresh.Large = large;
		fresh.Content = content;
		lock ( allocationLock )
		{
			Add( fresh );
			fresh.EmptySince = frame;
			if ( TryShared( type, image, large, content, size, alignment ) is { } placed ) return placed;
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
		var kind = (taken.TypeIndex, taken.Images, taken.Large, taken.Content);
		if ( makingAhead.Contains( kind ) ) return;
		foreach ( var block in blocks )
		{
			if ( !block.Dedicated && block.Used == 0 && block.TypeIndex == kind.TypeIndex && block.Images == kind.Images && block.Large == kind.Large && block.Content == kind.Content ) return;
		}

		makingAhead.Add( kind );
		var size = kind.Large ? LargeBlockSize : blockSize;
		System.Threading.ThreadPool.UnsafeQueueUserWorkItem( static state =>
		{
			var (allocator, kind, size) = state;
			MemoryBlock block = null;
			try
			{
				block = allocator.NewBlock( kind.TypeIndex, size, kind.Images );
				block.Large = kind.Large;
				block.Content = kind.Content;
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
				block.EmptySince = allocator.frame;
			}
		}, (this, kind, size), preferLocal: false );
	}

	/// <summary>Room in the fullest shared block of its kind with room, so the emptier ones drain; under the lock.</summary>
	Allocation? TryShared( int type, bool image, bool large, bool content, ulong size, ulong alignment )
	{
		MemoryBlock best = null;
		foreach ( var block in blocks )
		{
			if ( block.Dedicated || block.TypeIndex != type || block.Images != image || block.Large != large || block.Content != content ) continue;
			if ( best is not null && block.Used <= best.Used ) continue;
			if ( block.Fits( size, alignment ) ) best = block;
		}

		if ( best is null || !best.TryAllocate( size, alignment, out var offset ) ) return null;
		Used += size;
		MakeAhead( best );
		return new Allocation( best, offset, size );
	}

	public void Free( in Allocation allocation )
	{
		if ( allocation.Block is null ) return;

		lock ( allocationLock )
		{
			var block = allocation.Block;
			block.Release( allocation.Offset, allocation.Size );
			Used -= allocation.Size;

			// Dedicated blocks go at once; shared ones once empty, keeping one spare per type for a while (Trim)
			if ( block.Dedicated || (block.Used == 0 && HasSpare( block )) )
			{
				blocks.Remove( block );
				FreeBlock( block );
			}
			else if ( block.Used == 0 ) block.EmptySince = frame;
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
			for ( int i = blocks.Count - 1; i >= 0; i-- )
			{
				var block = blocks[i];
				if ( block.Dedicated || block.Used != 0 || frame - block.EmptySince < SpareFrames ) continue;
				blocks.RemoveAt( i );
				FreeBlock( block );
			}
		}
	}

	bool HasSpare( MemoryBlock empty )
	{
		foreach ( var block in blocks )
		{
			if ( block != empty && !block.Dedicated && block.TypeIndex == empty.TypeIndex && block.Images == empty.Images && block.Large == empty.Large && block.Content == empty.Content && block.Used == 0 ) return true;
		}

		return false;
	}

	/// <summary>A block from the driver, not yet in <see cref="blocks"/> (<see cref="Add"/>): made without the lock.</summary>
	MemoryBlock NewBlock( int type, ulong size, bool image )
	{
		var info = new VkMemoryAllocateInfo
		{
			sType = VkStructureType.MemoryAllocateInfo,
			allocationSize = size,
			memoryTypeIndex = (uint)type,
		};

		ulong memory;
		using ( Owner is { } owner ? owner.AllocateMemoryZone() : default )
			vk.vkAllocateMemory( vk.Device, &info, null, &memory ).Check( "vkAllocateMemory" );

		var block = new MemoryBlock { Memory = memory, Size = size, TypeIndex = type, Images = image };
		block.Free.Add( (0, size) );

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
		Reserved += block.Size;
	}

	void FreeBlock( MemoryBlock block, bool counted = true )
	{
		// Freeing memory unmaps it
		vk.vkFreeMemory( vk.Device, block.Memory, null );
		if ( counted ) Reserved -= block.Size;
	}

	public void Dispose()
	{
		// Blocks being made ahead finish first: the device must outlive their allocations
		lock ( allocationLock ) disposed = true;
		while ( true )
		{
			lock ( allocationLock )
			{
				if ( makingAhead.Count == 0 ) break;
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
