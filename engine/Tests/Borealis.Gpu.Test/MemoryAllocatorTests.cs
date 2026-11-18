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
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { MakeBlocksAhead = false };
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

	/// <summary>
	/// Allocations over a quarter of a small block share large blocks, made only when one is needed, and only the biggest get
	/// their own; and once the last empty block of a kind is taken, another is made ahead on a worker, so the next allocation
	/// that needs one doesn't wait for the driver (a map's first seconds waited ~0.5 s on the main thread).
	/// </summary>
	[TestMethod]
	public void BigAllocationsShareLargeBlocksAndBlocksAreMadeAhead()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false );
		var small = new VkMemoryRequirements { size = allocator.BlockSize / 8, alignment = 256, memoryTypeBits = ~0u };
		var big = small with { size = allocator.BlockSize / 2 };
		var biggest = small with { size = MemoryAllocator.LargeBlockSize / 2 };

		void WaitFor( ulong reserved, string what )
		{
			var clock = System.Diagnostics.Stopwatch.StartNew();
			while ( allocator.Reserved != reserved && clock.Elapsed.TotalSeconds < 10 ) System.Threading.Thread.Sleep( 5 );
			Assert.AreEqual( reserved, allocator.Reserved, what );
		}

		// A small allocation's block, then an empty one made ahead
		var a = allocator.Allocate( small, MemoryLocation.Device, image: false );
		WaitFor( 2 * allocator.BlockSize, "a small block and one made ahead" );

		// Two big ones share a large block, and another large block is made ahead
		var b = allocator.Allocate( big, MemoryLocation.Device, image: false );
		var c = allocator.Allocate( big, MemoryLocation.Device, image: false );
		Assert.AreSame( b.Block, c.Block, "big allocations share a large block" );
		Assert.AreNotSame( a.Block, b.Block );
		WaitFor( 2 * allocator.BlockSize + 2 * MemoryAllocator.LargeBlockSize, "a large block and one made ahead" );

		// The biggest gets its own
		var d = allocator.Allocate( biggest, MemoryLocation.Device, image: false );
		Assert.AreEqual( 2 * allocator.BlockSize + 2 * MemoryAllocator.LargeBlockSize + biggest.size, allocator.Reserved, "the biggest allocation is its own" );

		foreach ( var allocation in (Allocation[])[a, b, c, d] ) allocator.Free( allocation );
	}

	/// <summary>
	/// A target too big for a small block (a view's colour or depth) gets its own allocation, which goes back to the driver
	/// as soon as it's freed: in large blocks, targets made and dropped with views and window sizes left them mostly empty
	/// (the editor held 512 MiB of large image blocks for 246 MiB of images). Small targets still share small blocks.
	/// </summary>
	[TestMethod]
	public void BigTargetsGetTheirOwnAllocation()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { MakeBlocksAhead = false };
		var big = new VkMemoryRequirements { size = allocator.BlockSize / 2, alignment = 256, memoryTypeBits = ~0u };
		var small = big with { size = allocator.BlockSize / 8 };

		var target = allocator.Allocate( big, MemoryLocation.Device, image: true, target: true );
		Assert.IsTrue( target.Block.Dedicated, "a big target is its own allocation" );
		Assert.AreEqual( big.size, allocator.Reserved );
		allocator.Free( target );
		Assert.AreEqual( 0ul, allocator.Reserved, "and goes back to the driver when freed" );

		var smallTarget = allocator.Allocate( small, MemoryLocation.Device, image: true, target: true );
		Assert.IsFalse( smallTarget.Block.Dedicated, "a small target shares a small block" );
		var texture = allocator.Allocate( big, MemoryLocation.Device, image: true );
		Assert.IsTrue( texture.Block.Large, "a big texture that isn't a target shares a large block" );

		allocator.Free( smallTarget );
		allocator.Free( texture );
	}
}
