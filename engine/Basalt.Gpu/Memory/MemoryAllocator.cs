using Basalt.Gpu.Vulkan;

namespace Basalt.Gpu.Memory;

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
/// <c>bufferImageGranularity</c> never matters, and dedicated allocations for anything big.
/// </summary>
internal sealed unsafe class MemoryAllocator : IDisposable
{
	readonly VkDeviceApi vk;
	readonly VkPhysicalDeviceMemoryProperties properties;
	readonly bool integrated;
	readonly ulong blockSize;
	readonly List<MemoryBlock> blocks = new();
	readonly object allocationLock = new();

	/// <summary>Bytes allocated from the driver.</summary>
	public ulong Reserved { get; private set; }

	/// <summary>Bytes handed out.</summary>
	public ulong Used { get; private set; }

	public MemoryAllocator( VkDeviceApi vk, in VkPhysicalDeviceMemoryProperties properties, bool integrated )
	{
		this.vk = vk;
		this.properties = properties;
		this.integrated = integrated;

		// Smaller blocks where the biggest device-local heap is small (integrated GPUs, the Deck's carve-out)
		ulong largest = 0;
		for ( int i = 0; i < properties.memoryHeapCount; i++ )
		{
			var heap = properties.Heap( i );
			if ( (heap.flags & Vk.HeapDeviceLocal) != 0 ) largest = Math.Max( largest, heap.size );
		}

		blockSize = largest >= 2ul << 30 ? 64ul << 20 : 16ul << 20;
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

	public Allocation Allocate( in VkMemoryRequirements requirements, MemoryLocation location, bool image )
	{
		var type = FindType( requirements.memoryTypeBits, location );
		var size = requirements.size;
		var alignment = Math.Max( requirements.alignment, 1 );

		lock ( allocationLock )
		{
			// Anything over a quarter of a block gets its own allocation
			if ( size > blockSize / 4 )
			{
				var dedicated = NewBlock( type, size, image );
				dedicated.Dedicated = true;
				dedicated.TryAllocate( size, alignment, out var dedicatedOffset );
				Used += size;
				return new Allocation( dedicated, dedicatedOffset, size );
			}

			foreach ( var block in blocks )
			{
				if ( block.Dedicated || block.TypeIndex != type || block.Images != image ) continue;
				if ( !block.TryAllocate( size, alignment, out var offset ) ) continue;

				Used += size;
				return new Allocation( block, offset, size );
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

			// Dedicated blocks go at once; shared ones once empty, keeping one spare per type
			if ( block.Dedicated || (block.Used == 0 && HasSpare( block )) )
			{
				blocks.Remove( block );
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
