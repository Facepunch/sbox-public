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
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false };
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
	/// A device's allocator keeps an empty block for <see cref="MemoryAllocator.SpareSeconds"/> as well as
	/// <see cref="MemoryAllocator.SpareFrames"/> frames: a menu's frames pass 300 in a fraction of a second.
	/// </summary>
	[TestMethod]
	public void SparesAreKeptForSecondsAsWellAsFrames()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false, SpareSeconds = 0.5 };
		var requirements = new VkMemoryRequirements { size = allocator.BlockSize / 8, alignment = 256, memoryTypeBits = ~0u };
		var full = new List<Allocation>();
		for ( int i = 0; i < 8; i++ ) full.Add( allocator.Allocate( requirements, MemoryLocation.Device, image: false ) );
		var other = allocator.Allocate( requirements, MemoryLocation.Device, image: false );
		Assert.AreEqual( 2 * allocator.BlockSize, allocator.Reserved );

		// The second block emptied: kept past the frames while the seconds haven't passed, then freed
		allocator.Free( other );
		allocator.Trim( 1 );
		allocator.Trim( 2 + MemoryAllocator.SpareFrames );
		Assert.AreEqual( 2 * allocator.BlockSize, allocator.Reserved, "kept for its seconds" );
		System.Threading.Thread.Sleep( 600 );
		allocator.Trim( 3 + MemoryAllocator.SpareFrames );
		Assert.AreEqual( allocator.BlockSize, allocator.Reserved, "freed once both passed" );

		foreach ( var allocation in full ) allocator.Free( allocation );
	}

	/// <summary>
	/// Threads that find no room in a kind's blocks at the same moment make one new block between them, not one each: sixteen
	/// recording threads once made sixteen 16 MiB blocks at once after a map loaded, 70-170 ms each in the driver.
	/// </summary>
	[TestMethod]
	public void ThreadsShortOfRoomMakeOneBlockBetweenThem()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false };
		var requirements = new VkMemoryRequirements { size = allocator.BlockSize / 8, alignment = 256, memoryTypeBits = ~0u };
		const int threads = 16;
		var allocations = new Allocation[threads];
		using var start = new System.Threading.Barrier( threads );
		var workers = new System.Threading.Thread[threads];
		for ( int i = 0; i < threads; i++ )
		{
			var index = i;
			workers[i] = new System.Threading.Thread( () =>
			{
				start.SignalAndWait();
				allocations[index] = allocator.Allocate( requirements, MemoryLocation.Upload, image: false );
			} );
			workers[i].Start();
		}

		foreach ( var worker in workers ) worker.Join();
		Assert.AreEqual( 2, allocator.DriverAllocations, "sixteen eighths of a block take two blocks" );
		Assert.AreEqual( 2 * allocator.BlockSize, allocator.Reserved );
		foreach ( var allocation in allocations ) allocator.Free( allocation );
	}

	/// <summary>
	/// Images over a quarter of a small block share large blocks, made only when one is needed, and only the biggest get
	/// their own; and once the last empty block of a kind is taken, another is made ahead on a worker, so the next allocation
	/// that needs one doesn't wait for the driver (a map's first seconds waited ~0.5 s on the main thread).
	/// </summary>
	[TestMethod]
	public void BigAllocationsShareLargeBlocksAndBlocksAreMadeAhead()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, ShareLargeBlocks = true };
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
	/// A block is made ahead only when an allocation takes an empty one: once the spare has been trimmed, allocations that
	/// fit the blocks there are make none (each made one again, so a loaded map kept an empty block of every kind).
	/// </summary>
	[TestMethod]
	public void BlocksAreMadeAheadOnlyWhenAnEmptyOneIsTaken()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, SpareSeconds = 0 };
		var small = new VkMemoryRequirements { size = allocator.BlockSize / 8, alignment = 256, memoryTypeBits = ~0u };

		void WaitFor( ulong reserved, string what )
		{
			var clock = System.Diagnostics.Stopwatch.StartNew();
			while ( allocator.Reserved != reserved && clock.Elapsed.TotalSeconds < 10 ) System.Threading.Thread.Sleep( 5 );
			Assert.AreEqual( reserved, allocator.Reserved, what );
		}

		var a = allocator.Allocate( small, MemoryLocation.Device, image: false );
		WaitFor( 2 * allocator.BlockSize, "a block and one made ahead" );
		allocator.Trim( MemoryAllocator.SpareFrames + 1 );
		Assert.AreEqual( allocator.BlockSize, allocator.Reserved, "the spare goes once unused" );

		var b = allocator.Allocate( small, MemoryLocation.Device, image: false );
		Assert.AreSame( a.Block, b.Block );
		System.Threading.Thread.Sleep( 200 );
		Assert.AreEqual( allocator.BlockSize, allocator.Reserved, "room in a part-used block makes nothing ahead" );

		allocator.Free( a );
		allocator.Free( b );
	}

	/// <summary>
	/// A buffer of a quarter of a small block or more that isn't content (a view's visibility buffers, a growing table) is its
	/// own allocation too, and none of its size is made ahead: shared, a 4 MiB table held a 128 MiB block at the menu.
	/// </summary>
	[TestMethod]
	public void BigBuffersThatArentContentAreTheirOwnWithNoneMadeAhead()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false };
		var table = new VkMemoryRequirements { size = allocator.OwnBufferSize + 64, alignment = 256, memoryTypeBits = ~0u };
		var first = allocator.Allocate( table, MemoryLocation.Device, image: false );
		var second = allocator.Allocate( table, MemoryLocation.Device, image: false );
		Assert.IsTrue( first.Block.Dedicated && second.Block.Dedicated, "each is its own allocation" );
		System.Threading.Thread.Sleep( 200 );
		Assert.AreEqual( 2 * table.size, allocator.Reserved, "no spares of its size are made ahead" );

		// Exactly a quarter (the GPU-driven frame's upload buffers) is its own too; content of that size shares a block
		var quarter = table with { size = allocator.OwnBufferSize };
		var own = allocator.Allocate( quarter, MemoryLocation.Device, image: false );
		Assert.IsTrue( own.Block.Dedicated, "a quarter of a block is its own" );
		var content = allocator.Allocate( quarter, MemoryLocation.Device, image: false, content: true );
		Assert.IsFalse( content.Block.Dedicated, "content of that size shares" );

		allocator.Free( first );
		allocator.Free( second );
		allocator.Free( own );
		allocator.Free( content );
	}

	/// <summary>
	/// A buffer over a quarter of a small block (a map's vertex and index pages, a view's visibility buffers, a growing table)
	/// is its own allocation, so one still used after its map has gone doesn't hold a large block (three 128 MiB blocks
	/// held 26 MiB after three maps); once a size has been taken before, spares of it are made ahead on a worker: one
	/// while takes find one ready, <see cref="MemoryAllocator.PagesAheadInBurst"/> after one had to wait for the driver (a
	/// load takes them in bursts). One freed of each size is kept as a spare, and spares go after
	/// <see cref="MemoryAllocator.SpareFrames"/> frames.
	/// </summary>
	[TestMethod]
	public void BigBuffersAreTheirOwnAndMadeAhead()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false };
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

		var burst = (ulong)MemoryAllocator.PagesAheadInBurst;
		var second = allocator.Allocate( page, MemoryLocation.Device, image: false, content: true );
		WaitFor( (2 + burst) * page.size, "the second of a size, the driver's, makes a burst of spares ahead" );
		var third = allocator.Allocate( page, MemoryLocation.Device, image: false, content: true );
		Assert.AreNotSame( second.Block, third.Block );
		WaitFor( (2 + burst) * page.size, "the third takes a spare, and the ones left are enough" );

		// Freed: the spares left stay, the freed pages go (one of a size is kept only without another), then all are trimmed
		foreach ( var allocation in (Allocation[])[first, second, third] ) allocator.Free( allocation );
		Assert.AreEqual( (burst - 1) * page.size, allocator.Reserved, "the spares made ahead are kept" );
		allocator.Trim( MemoryAllocator.SpareFrames + 1 );
		Assert.AreEqual( 0ul, allocator.Reserved, "and freed after a while" );
	}

	/// <summary>
	/// A freed block's memory goes back to the driver on a worker, not on the thread that let it go (the frame thread's frame
	/// start, where a map's unload freed 85 blocks in one frame): it stops counting as reserved at once, and the worker frees it.
	/// </summary>
	[TestMethod]
	public void BlocksGoBackToTheDriverOnAWorker()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false, ShareLargeBlocks = true };
		var big = new VkMemoryRequirements { size = allocator.BlockSize / 2, alignment = 256, memoryTypeBits = ~0u };

		var frees = allocator.DriverFrees;
		for ( int i = 0; i < 4; i++ ) allocator.Free( allocator.Allocate( big, MemoryLocation.Device, image: true, target: true ) );
		Assert.AreEqual( 0ul, allocator.Reserved, "a freed block isn't reserved any more" );
		Assert.AreEqual( frees + 4, allocator.DriverFrees, "and counts as given back" );

		allocator.FinishReleases();
		var clock = System.Diagnostics.Stopwatch.StartNew();
		while ( allocator.DriverFreesOnWorker == 0 && clock.Elapsed.TotalSeconds < 10 )
		{
			allocator.Free( allocator.Allocate( big, MemoryLocation.Device, image: true, target: true ) );
			System.Threading.Thread.Sleep( 5 );
		}

		allocator.FinishReleases();
		Assert.IsTrue( allocator.DriverFreesOnWorker > 0, "the driver's frees are made on a worker" );
	}

	/// <summary>
	/// A target too big for a small block (a view's colour or depth) gets its own allocation, which goes back to the driver
	/// as soon as it's freed: in large blocks, targets made and dropped with views and window sizes left them mostly empty
	/// (the editor held 512 MiB of large image blocks for 246 MiB of images). Small targets still share small blocks.
	/// </summary>
	[TestMethod]
	public void BigTargetsGetTheirOwnAllocation()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false, ShareLargeBlocks = true };
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

	/// <summary>Without large blocks (the default), an image over a quarter of a small block is its own allocation, target or not.</summary>
	[TestMethod]
	public void BigImagesAreTheirOwnAllocation()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false, ShareLargeBlocks = false };
		var big = new VkMemoryRequirements { size = allocator.BlockSize / 2, alignment = 256, memoryTypeBits = ~0u };
		var texture = allocator.Allocate( big, MemoryLocation.Device, image: true, content: true );
		Assert.IsTrue( texture.Block.Dedicated );
		Assert.AreEqual( big.size, allocator.Reserved, "nothing stranded beside it" );
		allocator.Free( texture );
		Assert.AreEqual( 0ul, allocator.Reserved );
	}

	/// <summary>
	/// Each heap counts the blocks its memory types hold and what's handed out of them, with no budget read yet: usage is
	/// what the allocator reserved.
	/// </summary>
	[TestMethod]
	public void HeapsCountReservedAndUsed()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false };
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
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false, Warn = warnings.Add };
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
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false };
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
	/// Content blocks three quarters full, each with two holes too small for what they hold, are compacted once their kind has
	/// a block and a quarter free: each drain moves into the room there is and one block of its own size (an emptied one it
	/// finds kept as a spare), until the textures are packed tight and a block has gone back.
	/// </summary>
	[TestMethod]
	public void FullContentBlocksWithSmallHolesCompact()
	{
		// Without the drain into a block the size of what's left (TailDrain), which would then take the last block's quarter
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false, TailDrain = false };
		var quarter = new VkMemoryRequirements { size = allocator.BlockSize / 4, alignment = 256, memoryTypeBits = ~0u };
		var eighth = quarter with { size = allocator.BlockSize / 8 };
		var live = new List<Allocation>();
		var holes = new List<Allocation>();
		for ( int i = 0; i < 5; i++ )
		{
			live.Add( allocator.Allocate( quarter, MemoryLocation.Device, image: true, content: true ) );
			holes.Add( allocator.Allocate( eighth, MemoryLocation.Device, image: true, content: true ) );
			live.Add( allocator.Allocate( quarter, MemoryLocation.Device, image: true, content: true ) );
			holes.Add( allocator.Allocate( eighth, MemoryLocation.Device, image: true, content: true ) );
			live.Add( allocator.Allocate( quarter, MemoryLocation.Device, image: true, content: true ) );
		}

		foreach ( var hole in holes ) allocator.Free( hole );
		Assert.AreEqual( (long)(5 * allocator.BlockSize), (long)allocator.Reserved );

		// As the texture streamer does: what's in the draining block is made again with ExistingBlocksOnly, then let go
		ulong frame = MemoryAllocator.QuietFrames + 30;
		var drains = 0;
		for ( ; drains < 20; drains++, frame += 30 )
		{
			allocator.Trim( frame );
			if ( !allocator.AnyDraining ) break;
			for ( int i = 0; i < live.Count; i++ )
			{
				if ( !allocator.Draining( live[i] ) ) continue;
				MemoryAllocator.ExistingBlocksOnly = true;
				Allocation moved;
				try
				{
					moved = allocator.Allocate( quarter, MemoryLocation.Device, image: true, content: true );
				}
				finally
				{
					MemoryAllocator.ExistingBlocksOnly = false;
				}

				allocator.Free( live[i] );
				live[i] = moved;
			}

			Assert.IsFalse( allocator.AnyDraining, "each drain empties its block" );
		}

		allocator.Trim( frame + 2 * MemoryAllocator.SpareFrames );
		Assert.IsTrue( drains is > 0 and < 20, $"{drains} drains, and then it stops" );
		Assert.AreEqual( (long)(4 * allocator.BlockSize), (long)allocator.Reserved, "fifteen quarters in four blocks" );
		Assert.IsTrue( allocator.DriverAllocations <= 5 + drains );

		// Off, nothing over half full drains
		using var off = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false, CompactContent = false, TailDrain = false };
		var kept = new List<Allocation>();
		for ( int i = 0; i < 40; i++ ) kept.Add( off.Allocate( eighth, MemoryLocation.Device, image: true, content: true ) );
		for ( int i = 0; i < 40; i += 4 ) off.Free( kept[i] );
		off.Trim( MemoryAllocator.QuietFrames + 30 );
		Assert.IsFalse( off.AnyDraining );

		foreach ( var allocation in live ) allocator.Free( allocation );
		for ( int i = 0; i < 40; i++ ) if ( i % 4 != 0 ) off.Free( kept[i] );
	}

	/// <summary>
	/// A block of device buffers that aren't content drains, however full, only when what it holds would pack into the other
	/// blocks' free ranges: the device makes every copy at once, so a block whose biggest buffer finds no hole is passed over
	/// for one that packs.
	/// </summary>
	[TestMethod]
	public void DeviceBufferBlocksDrainOnlyWhenTheirContentsPack()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false };
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
	/// A volume relocates too, every slice: a map's light probe volumes, which nothing could move, kept the blocks they were
	/// in from draining.
	/// </summary>
	[TestMethod]
	public void RelocatedVolumesKeepEverySlice()
	{
		var texture = Device.CreateTexture( new TextureInfo { Width = 8, Height = 8, Depth = 4, Layers = 1, Mips = 1, Dimension = TextureDimension.Texture3D, Format = Format.RGBA8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopySource | TextureUsage.CopyDestination, Name = "Relocated volume", Content = true } );
		var texels = new byte[8 * 8 * 4 * 4];
		for ( int i = 0; i < texels.Length; i++ ) texels[i] = (byte)(i * 13 + 1);
		Device.WriteTexture( texture, texels );

		var copy = Device.Relocate( texture );
		Assert.IsTrue( copy.IsValid, "a volume can be relocated" );
		Device.Destroy( texture );

		var list = Device.BeginCommandList( QueueType.Graphics, "Volume readback" );
		var buffer = Device.CreateBuffer( new BufferInfo { Size = texels.Length, Memory = MemoryLocation.Readback, Name = "Readback" } );
		list.Barrier( TextureBarrier.Transition( copy, ImageLayout.ShaderRead, ImageLayout.CopySource, PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferRead ) );
		list.CopyTextureToBuffer( copy, 0, 0, buffer );
		list.Barrier( new BufferBarrier { Buffer = buffer, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		Device.Wait( Device.Submit( list ) );
		CollectionAssert.AreEqual( texels, Device.Map( buffer ).ToArray(), "every slice was copied" );
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
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false };
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
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false, ShareLargeBlocks = true };
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
	/// When a 32 MiB block and the other large blocks' room wouldn't hold what a large content block under half full holds,
	/// but a 64 MiB one would, moves out of it make that: the last block a map's load filled was left a third full.
	/// </summary>
	[TestMethod]
	public void LargeBlocksDrainIntoA64MiBBlockWhen32IsntEnough()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false, ShareLargeBlocks = true };
		var page = new VkMemoryRequirements { size = MemoryAllocator.LargeBlockSize / 16, alignment = 256, memoryTypeBits = ~0u };
		var first = new List<Allocation>();
		for ( int i = 0; i < 16; i++ ) first.Add( allocator.Allocate( page, MemoryLocation.Device, image: true, content: true ) );
		var second = new List<Allocation>();
		for ( int i = 0; i < 16; i++ ) second.Add( allocator.Allocate( page, MemoryLocation.Device, image: true, content: true ) );

		// The first holds seven (56 MiB), the second has room for one
		for ( int i = 0; i < 9; i++ ) allocator.Free( first[i] );
		allocator.Free( second[0] );
		allocator.Trim( MemoryAllocator.QuietFrames + 30 );
		Assert.IsTrue( allocator.Draining( first[9] ) );

		var moved = new List<Allocation>();
		MemoryAllocator.ExistingBlocksOnly = true;
		try
		{
			for ( int i = 0; i < 7; i++ ) moved.Add( allocator.Allocate( page, MemoryLocation.Device, image: true, content: true ) );
		}
		finally
		{
			MemoryAllocator.ExistingBlocksOnly = false;
		}

		Assert.AreEqual( 1, moved.Count( m => m.Block == second[1].Block ), "the others' room first" );
		Assert.AreEqual( 6, moved.Count( m => m.Block.Size == 2 * MemoryAllocator.DrainBlockSize ), "then one 64 MiB block" );

		for ( int i = 9; i < 16; i++ ) allocator.Free( first[i] );
		allocator.Trim( 2 * MemoryAllocator.SpareFrames );
		Assert.AreEqual( MemoryAllocator.LargeBlockSize + 2 * MemoryAllocator.DrainBlockSize, allocator.Reserved, "128 MiB back for 64" );

		foreach ( var allocation in second.Skip( 1 ).Concat( moved ) ) allocator.Free( allocation );
	}

	/// <summary>
	/// With <see cref="MemoryAllocator.OwnBigContentImages"/> (a device's), a content image over a quarter of a small block
	/// (a map's 2048 textures) is an allocation of its own, which goes back to the driver as it's freed; an image that isn't
	/// content still shares a large block. A device's allocator has it on.
	/// </summary>
	[TestMethod]
	public void BigContentImagesAreTheirOwn()
	{
		Assert.IsTrue( Device.Allocator.OwnBigContentImages, "a device's allocator" );
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false, ShareLargeBlocks = true, OwnBigContentImages = true };
		var texture = new VkMemoryRequirements { size = 5504 << 10, alignment = 65536, memoryTypeBits = ~0u };
		var content = allocator.Allocate( texture, MemoryLocation.Device, image: true, content: true );
		Assert.IsTrue( content.Block.Dedicated, "a content image of its own" );
		Assert.AreEqual( texture.size, content.Block.Size );
		var other = allocator.Allocate( texture, MemoryLocation.Device, image: true );
		Assert.IsTrue( other.Block.Large, "one that isn't content shares a large block" );
		var small = allocator.Allocate( texture with { size = 2 << 20 }, MemoryLocation.Device, image: true, content: true );
		Assert.IsFalse( small.Block.Dedicated, "and a small content image a small block" );

		var reserved = allocator.Reserved;
		allocator.Free( content );
		Assert.AreEqual( reserved - texture.size, allocator.Reserved, "freed at once" );
		allocator.Free( other );
		allocator.Free( small );
	}

	/// <summary>
	/// A small block left holding a little, with no room for it elsewhere in its kind (the only one: a load grew a menu's
	/// textures' block to 16 MiB and left them 3 of it), drains into one block of the size they need, for content (the
	/// streamer moves it) and what the device moves alike. One a little over half its smaller size isn't drained.
	/// </summary>
	[TestMethod]
	public void LoneSmallBlocksDrainIntoASmallerOne()
	{
		foreach ( var content in new[] { true, false } )
		{
			using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false };
			var mebibyte = new VkMemoryRequirements { size = 1 << 20, alignment = 256, memoryTypeBits = ~0u };
			var placed = new List<Allocation>();
			for ( int i = 0; i < 16; i++ ) placed.Add( allocator.Allocate( mebibyte, MemoryLocation.Device, image: true, content: content ) );
			Assert.AreEqual( allocator.BlockSize, allocator.Reserved, "one block" );

			// Three left: they move into one 4 MiB block, and the 16 MiB one goes
			for ( int i = 0; i < 13; i++ ) allocator.Free( placed[i] );
			allocator.Trim( MemoryAllocator.QuietFrames + 30 );
			Assert.IsTrue( allocator.Draining( placed[13] ), $"drained ({(content ? "content" : "moved by the device")})" );

			var moved = new List<Allocation>();
			MemoryAllocator.ExistingBlocksOnly = true;
			try
			{
				for ( int i = 0; i < 3; i++ ) moved.Add( allocator.Allocate( mebibyte, MemoryLocation.Device, image: true, content: content ) );
			}
			finally
			{
				MemoryAllocator.ExistingBlocksOnly = false;
			}

			Assert.IsTrue( moved.All( m => m.Block.Size == 4 << 20 ), "into one 4 MiB block" );
			for ( int i = 13; i < 16; i++ ) allocator.Free( placed[i] );
			allocator.Trim( 2 * MemoryAllocator.SpareFrames );
			Assert.AreEqual( 4ul << 20, allocator.Reserved, "16 MiB back for 4" );

			// Three of 4 MiB: no smaller block holds them with room to spare
			allocator.Trim( 3 * MemoryAllocator.SpareFrames );
			Assert.IsFalse( allocator.Draining( moved[0] ), "the smaller block isn't drained again" );
			foreach ( var allocation in moved ) allocator.Free( allocation );
		}
	}

	/// <summary>
	/// A kind's last block left part-used, with less than a block free in all and no room for what it holds elsewhere (what
	/// each kind of a loaded map ends with), drains into one block the size of what it holds, for content (the streamer
	/// moves it) and what the device moves alike, and that block isn't drained again. One that wouldn't give back an eighth
	/// isn't drained at all; with <see cref="MemoryAllocator.TailDrain"/> off, neither is.
	/// </summary>
	[TestMethod]
	public void PartUsedLastBlocksDrainIntoOneTheirSize()
	{
		var mebibyte = new VkMemoryRequirements { size = 1 << 20, alignment = 256, memoryTypeBits = ~0u };
		foreach ( var content in new[] { true, false } )
		{
			using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false };
			var full = new List<Allocation>();
			for ( int i = 0; i < 16; i++ ) full.Add( allocator.Allocate( mebibyte, MemoryLocation.Device, image: true, content: content ) );
			var last = new List<Allocation>();
			for ( int i = 0; i < 10; i++ ) last.Add( allocator.Allocate( mebibyte, MemoryLocation.Device, image: true, content: content ) );
			Assert.AreEqual( 2 * allocator.BlockSize, allocator.Reserved );

			allocator.Trim( MemoryAllocator.QuietFrames + 30 );
			Assert.IsTrue( allocator.Draining( last[0] ), $"the last block drains ({(content ? "content" : "moved by the device")})" );
			Assert.IsFalse( allocator.Draining( full[0] ) );

			var moved = new List<Allocation>();
			MemoryAllocator.ExistingBlocksOnly = true;
			try
			{
				for ( int i = 0; i < 10; i++ ) moved.Add( allocator.Allocate( mebibyte, MemoryLocation.Device, image: true, content: content ) );
			}
			finally
			{
				MemoryAllocator.ExistingBlocksOnly = false;
			}

			Assert.IsTrue( moved.All( m => m.Block.Size == 11 << 20 ), "into one block of 10 MiB and a sixteenth, rounded up to 11" );
			foreach ( var allocation in last ) allocator.Free( allocation );
			allocator.Trim( 2 * MemoryAllocator.SpareFrames );
			Assert.AreEqual( allocator.BlockSize + (11ul << 20), allocator.Reserved, "16 MiB back for 11" );
			Assert.AreEqual( 1, allocator.TailDrains );

			allocator.Trim( 3 * MemoryAllocator.SpareFrames );
			Assert.IsFalse( allocator.AnyDraining, "the block made isn't drained again" );
			foreach ( var allocation in full.Concat( moved ) ) allocator.Free( allocation );
		}

		// Fourteen of sixteen: a block its size would give back only one
		using var nearlyFull = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false };
		var fourteen = new List<Allocation>();
		for ( int i = 0; i < 14; i++ ) fourteen.Add( nearlyFull.Allocate( mebibyte, MemoryLocation.Device, image: true, content: true ) );
		nearlyFull.Trim( MemoryAllocator.QuietFrames + 30 );
		Assert.IsFalse( nearlyFull.AnyDraining, "under an eighth to give back" );
		foreach ( var allocation in fourteen ) nearlyFull.Free( allocation );

		// Off
		using var off = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false, TailDrain = false };
		var ten = new List<Allocation>();
		for ( int i = 0; i < 10; i++ ) ten.Add( off.Allocate( mebibyte, MemoryLocation.Device, image: true, content: true ) );
		off.Trim( MemoryAllocator.QuietFrames + 30 );
		Assert.IsFalse( off.AnyDraining );
		foreach ( var allocation in ten ) off.Free( allocation );
	}

	/// <summary>
	/// A kind's small blocks start small and grow (<see cref="MemoryAllocator.GrowBlocks"/>): its first is 2 MiB (1 MiB of
	/// readback memory), each new one twice its largest up to <see cref="MemoryAllocator.BlockSize"/>, and one is made big
	/// enough for what it's made for.
	/// </summary>
	[TestMethod]
	public void SmallBlocksStartSmallAndGrow()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, MakeBlocksAhead = false };
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

		// A 3 MiB image into a kind with no block: its first is made big enough, and as it isn't content, holds a whole number
		// of them (MemoryAllocator.WholeUnits)
		var three = allocator.Allocate( mebibyte with { size = 3 << 20 }, MemoryLocation.Device, image: true );
		Assert.AreEqual( 3ul << 20, three.Block.Size );
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

	/// <summary>
	/// A new block of images that aren't content holds a whole number of the image that took the kind's last empty block
	/// (<see cref="MemoryAllocator.WholeUnits"/>): a map's 128 px cube captures, 1056 KiB each, fill their blocks with nothing
	/// left over, where fifteen to a 16 MiB block left 0.53 MiB of each unused. Content's blocks aren't cut.
	/// </summary>
	[TestMethod]
	public void ImageBlocksHoldWholeCubeCaptures()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, MakeBlocksAhead = false };
		var cube = new VkMemoryRequirements { size = 1056 << 10, alignment = 1024, memoryTypeBits = ~0u };
		var placed = new List<Allocation>();
		var sizes = new List<ulong>();
		for ( int i = 0; i < 45; i++ )
		{
			var allocation = allocator.Allocate( cube, MemoryLocation.Device, image: true, target: true );
			if ( placed.All( p => p.Block != allocation.Block ) ) sizes.Add( allocation.Block.Size );
			placed.Add( allocation );
		}

		// 1, 2, 4, 8 and then 15 to a block, each block full
		CollectionAssert.AreEqual( new ulong[] { 1, 2, 4, 8, 15, 15 }.Select( n => n * cube.size ).ToArray(), sizes );
		Assert.AreEqual( allocator.Used, allocator.Reserved );

		// Content isn't cut: 16 MiB, as before
		var content = new List<Allocation>();
		for ( int i = 0; i < 31; i++ ) content.Add( allocator.Allocate( cube, MemoryLocation.Device, image: true, content: true ) );
		Assert.AreEqual( 16ul << 20, content[^1].Block.Size );

		foreach ( var allocation in placed.Concat( content ) ) allocator.Free( allocation );
	}

	/// <summary>
	/// Spare upload pages leave a block (<see cref="MemoryAllocator.PagesMayMove"/>) when it holds nothing but pages, is at
	/// most a quarter full, and the kind's other blocks have room for them.
	/// </summary>
	[TestMethod]
	public void SparePagesLeaveOnlyABlockOfPagesWithRoomBeside()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false };
		var eighth = new VkMemoryRequirements { size = allocator.BlockSize / 8, alignment = 256, memoryTypeBits = ~0u };
		var page = eighth.size;

		// A full first block, then two pages in a second
		var first = new List<Allocation>();
		for ( int i = 0; i < 8; i++ ) first.Add( allocator.Allocate( eighth, MemoryLocation.Upload, image: false ) );
		var second = new List<Allocation> { allocator.Allocate( eighth, MemoryLocation.Upload, image: false ), allocator.Allocate( eighth, MemoryLocation.Upload, image: false ) };
		Assert.AreNotSame( first[0].Block, second[0].Block );
		Assert.IsNull( allocator.PagesMayMove( second[0].Block, 2 * page, page, page ), "no room beside it" );

		allocator.Free( first[6] );
		allocator.Free( first[7] );
		Assert.AreEqual( 2 * page, allocator.PagesMayMove( second[0].Block, 2 * page, page, page ), "pages alone, a quarter full, room beside" );
		Assert.IsNull( allocator.PagesMayMove( second[0].Block, page, page, page ), "something else is in the block too" );
		Assert.IsNull( allocator.PagesMayMove( first[0].Block, 6 * page, page, page ), "over a quarter full" );

		foreach ( var allocation in first.Take( 6 ).Concat( second ) ) allocator.Free( allocation );
	}

	/// <summary>
	/// A drain is given up as soon as something in the block has no room elsewhere; the next look drains it into a block of
	/// its own size instead (<see cref="MemoryAllocator.TailDrain"/>), and one of those given up isn't tried again for a while.
	/// </summary>
	[TestMethod]
	public void DrainsWithNoRoomAreAbandoned()
	{
		using var allocator = new MemoryAllocator( Device.Vk, Device.Allocator.MemoryProperties, false ) { QuietSeconds = 0, GrowBlocks = false, MakeBlocksAhead = false };
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
		var tails = allocator.TailDrains;
		allocator.Trim( MemoryAllocator.QuietFrames + 60 );
		Assert.IsTrue( allocator.Draining( first[6] ), "drained into a block of its own at the next look" );
		Assert.AreEqual( tails + 1, allocator.TailDrains );

		allocator.Abandon( first[6] );
		Assert.AreEqual( 2, allocator.DrainsAbandoned );
		allocator.Trim( MemoryAllocator.QuietFrames + 90 );
		Assert.IsFalse( allocator.Draining( first[6] ), "not tried again at once" );

		foreach ( var allocation in first.Skip( 6 ).Where( ( _, i ) => i is < 2 or > 3 ) ) allocator.Free( allocation );
	}
}
