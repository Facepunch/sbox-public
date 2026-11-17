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
/// <c>bufferImageGranularity</c> never matters, and dedicated allocations for anything big. An allocation goes into the
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

	public Allocation Allocate( in VkMemoryRequirements requirements, MemoryLocation location, bool image )
	{
		try
		{
			return Allocate( requirements, location, image, FindType( requirements.memoryTypeBits, location ) );
		}
		catch ( GpuException e ) when ( e.Result == (int)VkResult.ErrorOutOfDeviceMemory && location == MemoryLocation.Device && FallbackType( requirements.memoryTypeBits ) is { } fallback )
		{
			var allocation = Allocate( requirements, location, image, fallback );
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

	Allocation Allocate( in VkMemoryRequirements requirements, MemoryLocation location, bool image, int type )
	{
		var size = requirements.size;
		var alignment = Math.Max( requirements.alignment, 1 );

		lock ( allocationLock )
		{
			Allocations++;

			// Anything over a quarter of a block gets its own allocation
			if ( size > blockSize / 4 )
			{
				var dedicated = NewBlock( type, size, image );
				dedicated.Dedicated = true;
				dedicated.TryAllocate( size, alignment, out var dedicatedOffset );
				Used += size;
				return new Allocation( dedicated, dedicatedOffset, size );
			}

			// The fullest block with room, so the emptier ones drain
			MemoryBlock best = null;
			foreach ( var block in blocks )
			{
				if ( block.Dedicated || block.TypeIndex != type || block.Images != image ) continue;
				if ( best is not null && block.Used <= best.Used ) continue;
				if ( block.Fits( size, alignment ) ) best = block;
			}

			if ( best is not null && best.TryAllocate( size, alignment, out var offset ) )
			{
				Used += size;
				return new Allocation( best, offset, size );
			}

			var fresh = NewBlock( type, blockSize, image );
			if ( !fresh.TryAllocate( size, alignment, out var freshOffset ) ) throw new GpuException( "A new memory block couldn't fit an allocation" );

			Used += size;
			return new Allocation( fresh, freshOffset, size );
		}
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
			if ( block != empty && !block.Dedicated && block.TypeIndex == empty.TypeIndex && block.Images == empty.Images && block.Used == 0 ) return true;
		}

		return false;
	}

	MemoryBlock NewBlock( int type, ulong size, bool image )
	{
		var info = new VkMemoryAllocateInfo
		{
			sType = VkStructureType.MemoryAllocateInfo,
			allocationSize = size,
			memoryTypeIndex = (uint)type,
		};

		ulong memory;
		vk.vkAllocateMemory( vk.Device, &info, null, &memory ).Check( "vkAllocateMemory" );

		var block = new MemoryBlock { Memory = memory, Size = size, TypeIndex = type, Images = image };
		block.Free.Add( (0, size) );

		if ( (properties.Type( type ).propertyFlags & Vk.MemoryHostVisible) != 0 )
		{
			void* mapped;
			vk.vkMapMemory( vk.Device, memory, 0, Vk.WholeSize, 0, &mapped ).Check( "vkMapMemory" );
			block.Mapped = (nint)mapped;
		}

		blocks.Add( block );
		Reserved += size;
		return block;
	}

	void FreeBlock( MemoryBlock block )
	{
		// Freeing memory unmaps it
		vk.vkFreeMemory( vk.Device, block.Memory, null );
		Reserved -= block.Size;
	}

	public void Dispose()
	{
		lock ( allocationLock )
		{
			foreach ( var block in blocks ) FreeBlock( block );
			blocks.Clear();
		}
	}
}
