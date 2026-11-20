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
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { GrowBlocks = false, MakeBlocksAhead = false };
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
	/// Images over a quarter of a small block share large blocks, made only when one is needed, and only the biggest get
	/// their own; and once the last empty block of a kind is taken, another is made ahead on a worker, so the next allocation
	/// that needs one doesn't wait for the driver (a map's first seconds waited ~0.5 s on the main thread).
	/// </summary>
	[TestMethod]
	public void BigAllocationsShareLargeBlocksAndBlocksAreMadeAhead()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { GrowBlocks = false };
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

		// Two big images share a large block, and another large block is made ahead
		var b = allocator.Allocate( big, MemoryLocation.Device, image: true );
		var c = allocator.Allocate( big, MemoryLocation.Device, image: true );
		Assert.AreSame( b.Block, c.Block, "big allocations share a large block" );
		Assert.AreNotSame( a.Block, b.Block );
		WaitFor( 2 * allocator.BlockSize + 2 * MemoryAllocator.LargeBlockSize, "a large block and one made ahead" );

		// The biggest gets its own
		var d = allocator.Allocate( biggest, MemoryLocation.Device, image: true );
		Assert.AreEqual( 2 * allocator.BlockSize + 2 * MemoryAllocator.LargeBlockSize + biggest.size, allocator.Reserved, "the biggest allocation is its own" );

		foreach ( var allocation in (Allocation[])[a, b, c, d] ) allocator.Free( allocation );
	}

	/// <summary>
	/// A buffer over a quarter of a small block (a map's vertex and index pages, a view's visibility buffers, a growing table)
	/// is its own allocation, so one still used after its map has gone doesn't hold a large block (three 128 MiB blocks
	/// held 26 MiB after three maps); once a size is taken twice the next is made ahead on a worker, and one freed of each
	/// size is kept as a spare until <see cref="MemoryAllocator.SpareFrames"/> frames pass.
	/// </summary>
	[TestMethod]
	public void BigBuffersAreTheirOwnAndMadeAhead()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { GrowBlocks = false };
		var page = new VkMemoryRequirements { size = 2 * allocator.OwnBufferSize, alignment = 256, memoryTypeBits = ~0u };

		void WaitFor( ulong reserved, string what )
		{
			var clock = System.Diagnostics.Stopwatch.StartNew();
			while ( allocator.Reserved != reserved && clock.Elapsed.TotalSeconds < 10 ) System.Threading.Thread.Sleep( 5 );
			Assert.AreEqual( reserved, allocator.Reserved, what );
		}

		var first = allocator.Allocate( page, MemoryLocation.Device, image: false, content: true );
		Assert.IsTrue( first.Block.Dedicated, "a big buffer is its own allocation" );
		Assert.AreEqual( page.size, allocator.Reserved, "nothing is made ahead for a size taken once" );

		var second = allocator.Allocate( page, MemoryLocation.Device, image: false, content: true );
		WaitFor( 3 * page.size, "the second of a size makes the next ahead" );
		var third = allocator.Allocate( page, MemoryLocation.Device, image: false, content: true );
		Assert.AreNotSame( second.Block, third.Block );
		WaitFor( 4 * page.size, "the third takes the spare, and another is made" );

		// Freed: one spare page is kept, then trimmed
		foreach ( var allocation in (Allocation[])[first, second, third] ) allocator.Free( allocation );
		Assert.AreEqual( page.size, allocator.Reserved, "one spare is kept" );
		allocator.Trim( MemoryAllocator.SpareFrames + 1 );
		Assert.AreEqual( 0ul, allocator.Reserved, "and freed after a while" );
	}

	/// <summary>
	/// A target too big for a small block (a view's colour or depth) gets its own allocation, which goes back to the driver
	/// as soon as it's freed: in large blocks, targets made and dropped with views and window sizes left them mostly empty
	/// (the editor held 512 MiB of large image blocks for 246 MiB of images). Small targets still share small blocks.
	/// </summary>
	[TestMethod]
	public void BigTargetsGetTheirOwnAllocation()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { GrowBlocks = false, MakeBlocksAhead = false };
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

	/// <summary>
	/// Each heap counts the blocks its memory types hold and what's handed out of them, with no budget read yet: usage is
	/// what the allocator reserved.
	/// </summary>
	[TestMethod]
	public void HeapsCountReservedAndUsed()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { GrowBlocks = false, MakeBlocksAhead = false };
		allocator.UpdateBudgetWithoutDriver();
		var requirements = new VkMemoryRequirements { size = allocator.BlockSize / 4, alignment = 256, memoryTypeBits = ~0u };
		var allocation = allocator.Allocate( requirements, MemoryLocation.Device, image: true );

		Span<MemoryHeapBudget> heaps = stackalloc MemoryHeapBudget[16];
		var count = allocator.GetHeaps( heaps );
		var heap = heaps[(int)Device.Allocator.MemoryProperties.Type( allocation.Block.TypeIndex ).heapIndex];
		Assert.IsTrue( count > 0 && heap.DeviceLocal );
		Assert.AreEqual( (long)allocator.BlockSize, heap.Reserved );
		Assert.AreEqual( (long)requirements.size, heap.Used );
		Assert.AreEqual( (long)allocator.BlockSize, heap.Usage, "usage moves with what's reserved between reads" );
		Assert.AreEqual( (long)(allocator.BlockSize - requirements.size), heap.Stranded );

		allocator.Free( allocation );
		allocator.GetHeaps( heaps );
		Assert.AreEqual( 0, heaps[heap.Index].Used );
	}

	/// <summary>
	/// Past an artificial budget (<see cref="MemoryAllocator.BudgetLimit"/>), a device allocation that can live in system
	/// memory goes there, in blocks of its own, as when the driver runs out of video memory, and a warning is said once; without a system memory
	/// type to fall back to, it fails as the driver's would.
	/// </summary>
	[TestMethod]
	public void OverTheBudgetAllocationsGoToSystemMemory()
	{
		var warnings = new List<string>();
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { GrowBlocks = false, MakeBlocksAhead = false, Warn = warnings.Add };
		var heapCount = (int)Device.Allocator.MemoryProperties.memoryHeapCount;
		var nothing = new ulong[heapCount];
		var everything = new ulong[heapCount];
		Array.Fill( everything, ulong.MaxValue );
		allocator.UpdateBudget( nothing, everything );
		allocator.BudgetLimit = (long)(allocator.BlockSize * 3 / 2);

		var eighth = new VkMemoryRequirements { size = allocator.BlockSize / 8, alignment = 256, memoryTypeBits = ~0u };
		var placed = new List<Allocation>();
		for ( int i = 0; i < 12; i++ ) placed.Add( allocator.Allocate( eighth, MemoryLocation.Device, image: true, content: true ) );

		bool DeviceLocal( Allocation a ) => (Device.Allocator.MemoryProperties.Type( a.Block.TypeIndex ).propertyFlags & Vk.MemoryDeviceLocal) != 0;
		Assert.IsTrue( placed.Take( 8 ).All( DeviceLocal ), "the first block fits the budget" );
		Assert.IsFalse( placed.Skip( 8 ).Any( DeviceLocal ), "past it, system memory" );
		Assert.AreEqual( (long)(4 * eighth.size), allocator.SystemFallbackBytes );
		Assert.AreEqual( 1, warnings.Count, "said once" );
		Assert.IsTrue( allocator.OverBudgetAllocations >= 1 );

		// Nowhere to fall back to: it fails rather than taking the heap past the budget
		allocator.SystemFallback = false;
		allocator.RefuseDeviceLocal = true;
		var e = Assert.ThrowsException<GpuException>( () => allocator.Allocate( eighth with { size = allocator.BlockSize / 2 }, MemoryLocation.Device, image: true, content: true ) );
		Assert.AreEqual( (int)VkResult.ErrorOutOfDeviceMemory, e.Result );

		foreach ( var allocation in placed ) allocator.Free( allocation );
		Assert.AreEqual( 0, allocator.SystemFallbackBytes, "and none once they're freed" );
	}

	/// <summary>
	/// A content image block under half full, whose contents fit in the room its kind's other blocks have, is drained: it
	/// takes nothing new, and once what's in it has gone elsewhere it's empty and stops draining.
	/// </summary>
	[TestMethod]
	public void SparseContentBlocksDrain()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { GrowBlocks = false, MakeBlocksAhead = false };
		var eighth = new VkMemoryRequirements { size = allocator.BlockSize / 8, alignment = 256, memoryTypeBits = ~0u };
		var first = new List<Allocation>();
		for ( int i = 0; i < 8; i++ ) first.Add( allocator.Allocate( eighth, MemoryLocation.Device, image: true, content: true ) );
		var second = new List<Allocation>();
		for ( int i = 0; i < 8; i++ ) second.Add( allocator.Allocate( eighth, MemoryLocation.Device, image: true, content: true ) );

		// The first down to a quarter, the second to three quarters: room for the first's two eighths
		for ( int i = 0; i < 6; i++ ) allocator.Free( first[i] );
		for ( int i = 0; i < 2; i++ ) allocator.Free( second[i] );
		allocator.Trim( 30 );
		Assert.IsFalse( allocator.AnyDraining, "not while blocks are still being made" );
		allocator.Trim( MemoryAllocator.QuietFrames + 30 );
		Assert.IsTrue( allocator.Draining( first[6] ), "the sparse block drains" );
		Assert.IsFalse( allocator.Draining( second[2] ) );
		Assert.IsTrue( allocator.AnyDraining );

		// Nothing new goes in it, though it's the emptiest: the moved allocations land in the second
		var moved = allocator.Allocate( eighth, MemoryLocation.Device, image: true, content: true );
		Assert.AreSame( second[2].Block, moved.Block );

		allocator.Free( first[6] );
		allocator.Free( first[7] );
		Assert.IsFalse( allocator.AnyDraining, "emptied, it stops draining" );
		Assert.AreEqual( 1, allocator.DrainsEmptied );

		foreach ( var allocation in second.Skip( 2 ) ) allocator.Free( allocation );
		allocator.Free( moved );
	}

	/// <summary>
	/// A block of device buffers that aren't content drains, however full, only when what it holds would pack into the other
	/// blocks' free ranges: the device makes every copy at once, so a block whose biggest buffer finds no hole is passed over
	/// for one that packs.
	/// </summary>
	[TestMethod]
	public void DeviceBufferBlocksDrainOnlyWhenTheirContentsPack()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { GrowBlocks = false, MakeBlocksAhead = false };
		var sixteenth = new VkMemoryRequirements { size = allocator.BlockSize / 16, alignment = 256, memoryTypeBits = ~0u };
		var first = new List<Allocation>();
		for ( int i = 0; i < 16; i++ ) first.Add( allocator.Allocate( sixteenth, MemoryLocation.Device, image: false ) );
		var big = allocator.Allocate( sixteenth with { size = sixteenth.size * 3 }, MemoryLocation.Device, image: false );
		var second = new List<Allocation>();
		for ( int i = 0; i < 13; i++ ) second.Add( allocator.Allocate( sixteenth, MemoryLocation.Device, image: false ) );
		Assert.AreNotSame( first[0].Block, big.Block );

		// Holes of one sixteenth apart in the first; the second down to its big buffer, three sixteenths
		foreach ( var i in (int[])[1, 3, 5, 7] ) allocator.Free( first[i] );
		foreach ( var allocation in second ) allocator.Free( allocation );
		allocator.Trim( MemoryAllocator.QuietFrames + 30 );
		Assert.IsFalse( allocator.Draining( big ), "the emptiest block's big buffer has no hole to go to" );
		Assert.IsTrue( allocator.Draining( first[0] ), "the fuller block's buffers fit the other's room: it drains instead" );
		allocator.Undrain( first[0].Block );

		allocator.Free( big );
		foreach ( var i in (int[])[0, 2, 4, 6, 8, 9, 10, 11, 12, 13, 14, 15] ) allocator.Free( first[i] );
	}

	/// <summary>A relocated texture is a copy of every mip and layer in new memory, left in the layout it was in.</summary>
	[TestMethod]
	public void RelocatedTexturesKeepTheirTexels()
	{
		var texture = Device.CreateTexture( new TextureInfo { Width = 16, Height = 16, Mips = 2, Layers = 2, Dimension = TextureDimension.Texture2DArray, Format = Format.RGBA8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopySource | TextureUsage.CopyDestination, Name = "Relocated", Content = true } );
		var texels = new byte[16 * 16 * 4];
		for ( int i = 0; i < texels.Length; i++ ) texels[i] = (byte)(i * 7);
		for ( int layer = 0; layer < 2; layer++ )
		{
			Device.WriteTexture( texture, texels, mip: 0, layer: layer );
			Device.WriteTexture( texture, texels.AsSpan( 0, 8 * 8 * 4 ), mip: 1, layer: layer );
		}

		var copy = Device.Relocate( texture );
		Assert.IsTrue( copy.IsValid );
		Assert.AreNotEqual( Device.Get( texture ).Memory, Device.Get( copy ).Memory );
		Device.Destroy( texture );

		CollectionAssert.AreEqual( texels, ReadBack( copy, ImageLayout.ShaderRead ) );
		var list = Device.BeginCommandList( QueueType.Graphics, "Mip 1 layer 1" );
		var buffer = Device.CreateBuffer( new BufferInfo { Size = 8 * 8 * 4, Memory = MemoryLocation.Readback, Name = "Readback" } );
		list.CopyTextureToBuffer( copy, 1, 1, buffer );
		list.Barrier( new BufferBarrier { Buffer = buffer, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		Device.Wait( Device.Submit( list ) );
		CollectionAssert.AreEqual( texels.AsSpan( 0, 8 * 8 * 4 ).ToArray(), Device.Map( buffer ).ToArray() );
		Device.Destroy( buffer );
		Device.Destroy( copy );
	}

	/// <summary>
	/// A move out of a draining block only takes room in blocks there are (<see cref="MemoryAllocator.ExistingBlocksOnly"/>):
	/// with none of its kind, or for an allocation that would be its own, it fails rather than making one.
	/// </summary>
	[TestMethod]
	public void MovesOnlyTakeRoomInBlocksThereAre()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { GrowBlocks = false, MakeBlocksAhead = false };
		var eighth = new VkMemoryRequirements { size = allocator.BlockSize / 8, alignment = 256, memoryTypeBits = ~0u };
		MemoryAllocator.ExistingBlocksOnly = true;
		try
		{
			var e = Assert.ThrowsException<GpuException>( () => allocator.Allocate( eighth, MemoryLocation.Device, image: true, content: true ) );
			Assert.AreEqual( (int)VkResult.ErrorFragmentedPool, e.Result );
		}
		finally
		{
			MemoryAllocator.ExistingBlocksOnly = false;
		}

		var first = allocator.Allocate( eighth, MemoryLocation.Device, image: true, content: true );
		MemoryAllocator.ExistingBlocksOnly = true;
		try
		{
			var moved = allocator.Allocate( eighth, MemoryLocation.Device, image: true, content: true );
			Assert.AreSame( first.Block, moved.Block, "room in a block there is" );
			Assert.AreEqual( allocator.BlockSize, allocator.Reserved );
			allocator.Free( moved );
			Assert.ThrowsException<GpuException>( () => allocator.Allocate( eighth with { size = MemoryAllocator.LargeBlockSize / 2 }, MemoryLocation.Device, image: true, content: true ) );
		}
		finally
		{
			MemoryAllocator.ExistingBlocksOnly = false;
		}

		// An empty block (a spare) isn't room for a move: filling it would make the next block to drain
		allocator.Free( first );
		Assert.AreEqual( allocator.BlockSize, allocator.Reserved, "kept as a spare" );
		MemoryAllocator.ExistingBlocksOnly = true;
		try
		{
			Assert.ThrowsException<GpuException>( () => allocator.Allocate( eighth, MemoryLocation.Device, image: true, content: true ) );
		}
		finally
		{
			MemoryAllocator.ExistingBlocksOnly = false;
		}
	}

	/// <summary>
	/// A large content block under half full can be drained even when the other large blocks lack the room, if their room and
	/// one <see cref="MemoryAllocator.DrainBlockSize"/> block would hold it: moves fill the others, then make that one block,
	/// and no more; emptied, the large block goes back.
	/// </summary>
	[TestMethod]
	public void LargeBlocksDrainIntoOneSmallerBlock()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { GrowBlocks = false, MakeBlocksAhead = false };
		var page = new VkMemoryRequirements { size = MemoryAllocator.LargeBlockSize / 16, alignment = 256, memoryTypeBits = ~0u };
		var first = new List<Allocation>();
		for ( int i = 0; i < 16; i++ ) first.Add( allocator.Allocate( page, MemoryLocation.Device, image: true, content: true ) );
		var second = new List<Allocation>();
		for ( int i = 0; i < 16; i++ ) second.Add( allocator.Allocate( page, MemoryLocation.Device, image: true, content: true ) );
		Assert.IsTrue( first[0].Block.Large );

		// The first holds five, the second has room for three
		for ( int i = 0; i < 11; i++ ) allocator.Free( first[i] );
		for ( int i = 0; i < 3; i++ ) allocator.Free( second[i] );
		allocator.Trim( MemoryAllocator.QuietFrames + 30 );
		Assert.IsTrue( allocator.Draining( first[11] ) );

		var moved = new List<Allocation>();
		MemoryAllocator.ExistingBlocksOnly = true;
		try
		{
			for ( int i = 0; i < 7; i++ ) moved.Add( allocator.Allocate( page, MemoryLocation.Device, image: true, content: true ) );
			Assert.ThrowsException<GpuException>( () => allocator.Allocate( page, MemoryLocation.Device, image: true, content: true ), "one block, no more" );
		}
		finally
		{
			MemoryAllocator.ExistingBlocksOnly = false;
		}

		Assert.AreEqual( 3, moved.Count( m => m.Block == second[3].Block ), "the others' room first" );
		Assert.AreEqual( 4, moved.Count( m => m.Block.Size == MemoryAllocator.DrainBlockSize ), "then one smaller block, full" );

		for ( int i = 11; i < 16; i++ ) allocator.Free( first[i] );
		allocator.Trim( 2 * MemoryAllocator.SpareFrames );
		Assert.AreEqual( MemoryAllocator.LargeBlockSize + MemoryAllocator.DrainBlockSize, allocator.Reserved, "128 MiB back for 32" );

		foreach ( var allocation in second.Skip( 3 ).Concat( moved ) ) allocator.Free( allocation );
	}

	/// <summary>
	/// A kind's small blocks start small and grow (<see cref="MemoryAllocator.GrowBlocks"/>): its first is 2 MiB (1 MiB of
	/// readback memory), each new one twice its largest up to <see cref="MemoryAllocator.BlockSize"/>, and one is made big
	/// enough for what it's made for.
	/// </summary>
	[TestMethod]
	public void SmallBlocksStartSmallAndGrow()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { MakeBlocksAhead = false };
		var mebibyte = new VkMemoryRequirements { size = 1 << 20, alignment = 256, memoryTypeBits = ~0u };
		var placed = new List<Allocation>();
		var sizes = new List<ulong>();
		for ( int i = 0; i < 40; i++ )
		{
			var allocation = allocator.Allocate( mebibyte, MemoryLocation.Device, image: true, content: true );
			if ( placed.All( p => p.Block != allocation.Block ) ) sizes.Add( allocation.Block.Size );
			placed.Add( allocation );
		}

		CollectionAssert.AreEqual( new ulong[] { 2 << 20, 4 << 20, 8 << 20, 16 << 20, 16 << 20 }, sizes );

		// A 3 MiB image into a kind with no block: its first is made big enough
		var three = allocator.Allocate( mebibyte with { size = 3 << 20 }, MemoryLocation.Device, image: true );
		Assert.AreEqual( 4ul << 20, three.Block.Size );
		placed.Add( three );

		var type = allocator.FindType( mebibyte.memoryTypeBits, MemoryLocation.Readback );
		var flags = Device.Allocator.MemoryProperties.Type( type ).propertyFlags;
		if ( (flags & Vk.MemoryHostCached) != 0 && (flags & Vk.MemoryDeviceLocal) == 0 )
		{
			var readback = allocator.Allocate( mebibyte with { size = 64 << 10 }, MemoryLocation.Readback, image: false );
			Assert.AreEqual( MemoryAllocator.FirstReadbackBlockSize, readback.Block.Size );
			placed.Add( readback );
		}

		foreach ( var allocation in placed ) allocator.Free( allocation );
	}

	/// <summary>A drain is given up as soon as something in the block has no room elsewhere, and not tried again for a while.</summary>
	[TestMethod]
	public void DrainsWithNoRoomAreAbandoned()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { GrowBlocks = false, MakeBlocksAhead = false };
		var eighth = new VkMemoryRequirements { size = allocator.BlockSize / 8, alignment = 256, memoryTypeBits = ~0u };
		var first = new List<Allocation>();
		for ( int i = 0; i < 16; i++ ) first.Add( allocator.Allocate( eighth, MemoryLocation.Device, image: true, content: true ) );
		for ( int i = 0; i < 6; i++ ) allocator.Free( first[i] );
		for ( int i = 8; i < 10; i++ ) allocator.Free( first[i] );
		allocator.Trim( MemoryAllocator.QuietFrames + 30 );
		Assert.IsTrue( allocator.Draining( first[6] ) );

		allocator.Abandon( first[6] );
		Assert.IsFalse( allocator.AnyDraining );
		Assert.AreEqual( 1, allocator.DrainsAbandoned );
		allocator.Trim( MemoryAllocator.QuietFrames + 60 );
		Assert.IsFalse( allocator.Draining( first[6] ), "not tried again at once" );

		foreach ( var allocation in first.Skip( 6 ).Where( ( _, i ) => i is < 2 or > 3 ) ) allocator.Free( allocation );
	}
}
