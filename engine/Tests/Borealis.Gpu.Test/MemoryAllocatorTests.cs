using Borealis.Gpu;
using Borealis.Gpu.Memory;
using Borealis.Gpu.Vulkan;

namespace BorealisGpuTests;

/// <summary>
/// The memory allocator puts each allocation in the fullest block it fits in, so lightly used blocks drain, and gives an
/// empty block kept as a spare back to the driver after <see cref="MemoryAllocator.SpareFrames"/> frames.
/// </summary>
[TestClass]
public class MemoryAllocatorTests : GpuTest
{
	[TestMethod]
	public void AllocationsFillTheFullestBlockAndEmptyBlocksGoBack()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false );
		var eighth = allocator.BlockSize / 8;
		var requirements = new VkMemoryRequirements { size = eighth, alignment = 256, memoryTypeBits = ~0u };

		// A full block and half of a second
		var first = new List<Allocation>();
		for ( int i = 0; i < 8; i++ ) first.Add( allocator.Allocate( requirements, MemoryLocation.Device, image: false ) );
		var second = new List<Allocation>();
		for ( int i = 0; i < 4; i++ ) second.Add( allocator.Allocate( requirements, MemoryLocation.Device, image: false ) );
		Assert.AreNotSame( first[0].Block, second[0].Block );
		Assert.AreEqual( 2 * allocator.BlockSize, allocator.Reserved );

		// The first block down to a quarter: a new allocation goes to the half-full second one, not the first's gap
		for ( int i = 0; i < 6; i++ ) allocator.Free( first[i] );
		var next = allocator.Allocate( requirements, MemoryLocation.Device, image: false );
		Assert.AreSame( second[0].Block, next.Block );

		// Emptied, the first block is kept as the spare until enough frames pass, then freed
		allocator.Free( first[6] );
		allocator.Free( first[7] );
		allocator.Trim( 1 );
		Assert.AreEqual( 2 * allocator.BlockSize, allocator.Reserved );
		allocator.Trim( 1 + MemoryAllocator.SpareFrames );
		Assert.AreEqual( allocator.BlockSize, allocator.Reserved );

		foreach ( var allocation in second ) allocator.Free( allocation );
		allocator.Free( next );
	}
}
