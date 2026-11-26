using Borealis.Gpu;

namespace BorealisGpuTests;

/// <summary>
/// Upload pages a burst of uploads took (a map's load) are freed once they've gone unused for a while, down to a spare
/// kept for later lists, rather than held mapped for good.
/// </summary>
[TestClass]
public class UploadPageTests : GpuTest
{
	[TestMethod]
	public void IdlePagesAreFreed()
	{
		for ( ulong i = 0; i <= 2 * Device.UploadPageIdleFrames; i++ ) Device.BeginFrame();
		var reserved = Device.MemoryUsage.Reserved;
		var list = Device.BeginCommandList( QueueType.Graphics, "Burst" );
		for ( int i = 0; i < 24; i++ ) list.AllocateUpload( 3 << 20 );
		Device.Wait( Device.Submit( list ) );

		for ( int i = 0; i <= Device.FramesInFlight; i++ ) Device.BeginFrame();

		// Pages the burst's misses had made ahead are among the free ones once made
		var clock = System.Diagnostics.Stopwatch.StartNew();
		while ( Device.MakingUploadPagesAhead && clock.Elapsed.TotalSeconds < 10 ) System.Threading.Thread.Sleep( 5 );
		Assert.IsTrue( Device.FreePages[2].Count >= 24, $"the burst's pages are free for reuse ({Device.FreePages[2].Count})" );
		Assert.IsTrue( Device.FreePages[2].TryPeek( out var page ) && Device.Get( page.Buffer ).Memory.Block.Dedicated, "each page is its own allocation" );

		// Idle for a whole window of frames: up to two windows after the burst
		for ( ulong i = 0; i <= 2 * Device.UploadPageIdleFrames; i++ ) Device.BeginFrame();
		Assert.AreEqual( Device.KeptFreeUploadPages + Device.ExtraFreeUploadPages[2], Device.FreePages[2].Count );

		// The pages freed were those in the emptiest blocks, so the burst's blocks went back (once the allocator stops keeping
		// an empty one) but for the spare's
		for ( ulong i = 0; i <= Borealis.Gpu.Memory.MemoryAllocator.SpareFrames; i++ ) Device.BeginFrame();
		Assert.IsTrue( Device.MemoryUsage.Reserved - reserved <= (long)Device.Allocator.BlockSize, $"{(Device.MemoryUsage.Reserved - reserved) >> 20} MiB more reserved than before the burst" );
	}

	/// <summary>
	/// Pages each frame takes aren't trimmed: the free count at a frame's start overstated the spare by what the frame then
	/// took, so every window freed small pages the next frame made again (Construct's steady frames made a few each time).
	/// </summary>
	[TestMethod]
	public void PagesFramesTakeAreKept()
	{
		for ( ulong i = 0; i <= 2 * Device.UploadPageIdleFrames; i++ ) Device.BeginFrame();
		var made = Device.UploadPagesMadeBySize[0];

		// Six lists a frame, a small page each
		var frames = 3 * Device.UploadPageIdleFrames;
		for ( ulong i = 0; i <= frames; i++ )
		{
			for ( int l = 0; l < 6; l++ )
			{
				var list = Device.BeginCommandList( QueueType.Graphics, "Small" );
				list.AllocateUpload( 1024 );
				Device.Submit( list );
			}

			Device.BeginFrame();
		}

		var madeSince = Device.UploadPagesMadeBySize[0] - made;
		Assert.IsTrue( madeSince <= 6 * (Device.FramesInFlight + 2), $"{madeSince} small pages made over {frames} frames that take six each" );
	}

	/// <summary>
	/// A size whose trimmed pages had to be made again keeps one more free from then on, up to
	/// <see cref="Device.MaxExtraFreeUploadPages"/>: a frame that needs more every few seconds doesn't make them after every trim.
	/// </summary>
	[TestMethod]
	public void SizesMadeAgainAfterATrimKeepMore()
	{
		for ( ulong i = 0; i <= 2 * Device.UploadPageIdleFrames; i++ ) Device.BeginFrame();
		var extra = Device.ExtraFreeUploadPages[2];

		for ( int round = 0; round < 2; round++ )
		{
			// A burst, then idle until it's trimmed, then the burst again
			var list = Device.BeginCommandList( QueueType.Graphics, "Burst" );
			for ( int i = 0; i < 4; i++ ) list.AllocateUpload( 3 << 20 );
			Device.Wait( Device.Submit( list ) );

			// The next frame makes pages ahead for the burst's misses on a worker: wait for them, so they're free before the
			// window the trim looks at rather than landing after it (a starved thread pool under parallel suites made them late)
			Device.BeginFrame();
			var clock = System.Diagnostics.Stopwatch.StartNew();
			while ( Device.MakingUploadPagesAhead && clock.Elapsed.TotalSeconds < 30 ) System.Threading.Thread.Sleep( 5 );
			Assert.IsFalse( Device.MakingUploadPagesAhead, "the pages made ahead were made" );
			for ( ulong i = 1; i <= 2 * Device.UploadPageIdleFrames; i++ ) Device.BeginFrame();
		}

		Assert.IsTrue( Device.ExtraFreeUploadPages[2] > extra || Device.ExtraFreeUploadPages[2] == Device.MaxExtraFreeUploadPages, $"kept {Device.ExtraFreeUploadPages[2]} more (was {extra})" );
		Assert.AreEqual( Device.KeptFreeUploadPages + Device.ExtraFreeUploadPages[2], Device.FreePages[2].Count );
	}

	/// <summary>
	/// With <see cref="CommandList.SharedPages"/>, a burst's biggest pages share large blocks rather than each being a driver
	/// allocation of its own, and still go back once idle.
	/// </summary>
	[TestMethod]
	public void SharedPagesTakeFewDriverAllocations()
	{
		var shared = CommandList.SharedPages;
		CommandList.SharedPages = true;
		try
		{
			for ( ulong i = 0; i <= 2 * Device.UploadPageIdleFrames; i++ ) Device.BeginFrame();
			var reserved = Device.MemoryUsage.Reserved;
			var allocations = Device.Allocator.DriverAllocations;
			var list = Device.BeginCommandList( QueueType.Graphics, "Burst" );
			for ( int i = 0; i < 24; i++ ) list.AllocateUpload( 3 << 20 );
			Device.Wait( Device.Submit( list ) );
			for ( int i = 0; i <= Device.FramesInFlight; i++ ) Device.BeginFrame();
			var clock = System.Diagnostics.Stopwatch.StartNew();
			while ( Device.MakingUploadPagesAhead && clock.Elapsed.TotalSeconds < 10 ) System.Threading.Thread.Sleep( 5 );

			var made = Device.Allocator.DriverAllocations - allocations;
			Assert.IsTrue( Device.FreePages[2].Count >= 24, $"the burst's pages are free for reuse ({Device.FreePages[2].Count})" );
			Assert.IsTrue( made <= 4, $"{made} driver allocations for {Device.FreePages[2].Count} pages" );
			Assert.IsTrue( Device.FreePages[2].TryPeek( out var page ) && !Device.Get( page.Buffer ).Memory.Block.Dedicated, "pages share blocks" );

			// Idle: the pages go but for the spare, and their blocks with them
			for ( ulong i = 0; i <= 2 * Device.UploadPageIdleFrames; i++ ) Device.BeginFrame();
			Assert.AreEqual( Device.KeptFreeUploadPages + Device.ExtraFreeUploadPages[2], Device.FreePages[2].Count );
			for ( ulong i = 0; i <= Borealis.Gpu.Memory.MemoryAllocator.SpareFrames; i++ ) Device.BeginFrame();
			Assert.IsTrue( Device.MemoryUsage.Reserved - reserved <= (long)Borealis.Gpu.Memory.MemoryAllocator.LargeBlockSize, $"{(Device.MemoryUsage.Reserved - reserved) >> 20} MiB more reserved than before the burst" );
		}
		finally
		{
			CommandList.SharedPages = shared;
		}
	}

	/// <summary>
	/// A frame whose lists had to make pages of the biggest size (none free) has twice as many made on a worker when the next
	/// frame starts, so a burst's next frames find them free instead of each list waiting for the driver.
	/// </summary>
	[TestMethod]
	public void MissedPagesAreMadeAheadForTheNextFrame()
	{
		// Two windows first, so free pages other tests left are trimmed to the spare
		for ( ulong i = 0; i <= 2 * Device.UploadPageIdleFrames; i++ ) Device.BeginFrame();
		var ahead = Device.UploadPagesAhead;
		var list = Device.BeginCommandList( QueueType.Graphics, "Burst" );
		for ( int i = 0; i < 6; i++ ) list.AllocateUpload( 3 << 20 );
		Device.Wait( Device.Submit( list ) );

		// The frame that saw the misses starts: the pages are made ahead, then waited for here
		Device.BeginFrame();
		var clock = System.Diagnostics.Stopwatch.StartNew();
		while ( Device.UploadPagesAhead - ahead < 10 && clock.Elapsed.TotalSeconds < 10 ) System.Threading.Thread.Sleep( 5 );
		Assert.IsTrue( Device.UploadPagesAhead - ahead >= 10, $"twice the five missed pages made ahead ({Device.UploadPagesAhead - ahead})" );

		// The same burst again takes free pages: no list makes one
		Device.UploadPageMisses = 0;
		var again = Device.BeginCommandList( QueueType.Graphics, "Burst again" );
		for ( int i = 0; i < 6; i++ ) again.AllocateUpload( 3 << 20 );
		Device.Wait( Device.Submit( again ) );
		Assert.AreEqual( 0, Device.UploadPageMisses, "the burst's pages were free" );
		for ( int i = 0; i <= Device.FramesInFlight; i++ ) Device.BeginFrame();
	}

	/// <summary>
	/// Command lists a busy stretch took (a map's frames) are freed once they've gone unused for a while, down to a few kept,
	/// rather than kept with the command memory their pools grew to.
	/// </summary>
	[TestMethod]
	public void IdleCommandListsAreFreed()
	{
		var freedBefore = Device.CommandListsFreed;
		var releasedBefore = Device.CommandMemoryReleases;
		var lists = new CommandList[48];
		for ( int i = 0; i < lists.Length; i++ ) lists[i] = Device.BeginCommandList( QueueType.Graphics, "Burst" );
		SyncPoint done = default;
		foreach ( var list in lists ) done = Device.Submit( list );
		Device.Wait( done );

		for ( int i = 0; i <= Device.FramesInFlight; i++ ) Device.BeginFrame();
		Assert.IsTrue( Device.FreeCommandLists( QueueType.Graphics ) >= lists.Length, $"the burst's lists are spare ({Device.FreeCommandLists( QueueType.Graphics )})" );

		for ( ulong i = 0; i <= 2 * Device.UploadPageIdleFrames; i++ ) Device.BeginFrame();
		Assert.AreEqual( Device.KeptFreeCommandLists, Device.FreeCommandLists( QueueType.Graphics ) );
		Assert.IsTrue( Device.CommandListsFreed - freedBefore >= lists.Length - Device.KeptFreeCommandLists );
		Assert.AreEqual( Device.KeptFreeCommandLists, Device.CommandMemoryReleases - releasedBefore, "the lists kept gave back what the burst grew them to" );

		// The lists kept still record
		var again = Device.BeginCommandList( QueueType.Graphics, "Again" );
		Device.Wait( Device.Submit( again ) );
	}

	/// <summary>
	/// Spare lists a window frees are destroyed a few a frame, not all in the frame the window ends: a load's sixty-odd took
	/// 33 ms of one frame.
	/// </summary>
	[TestMethod]
	public void SpareListsAreFreedAFewAFrame()
	{
		for ( ulong i = 0; i <= 2 * Device.UploadPageIdleFrames; i++ ) Device.BeginFrame();
		var lists = new CommandList[48];
		for ( int i = 0; i < lists.Length; i++ ) lists[i] = Device.BeginCommandList( QueueType.Graphics, "Burst" );
		SyncPoint done = default;
		foreach ( var list in lists ) done = Device.Submit( list );
		Device.Wait( done );

		// Up to the frame whose window frees the burst's lists
		var freedBefore = Device.CommandListsFreed;
		for ( ulong i = 0; i <= 3 * Device.UploadPageIdleFrames && Device.CommandListsFreed == freedBefore; i++ ) Device.BeginFrame();
		var freed = Device.CommandListsFreed - freedBefore;
		Assert.IsTrue( freed > Device.ListsFreedPerFrame, $"{freed} lists freed" );
		Assert.AreEqual( freed, Device.ListsWaitingToBeFreed, "none destroyed in the frame the window ended" );

		Device.BeginFrame();
		Assert.AreEqual( freed - Device.ListsFreedPerFrame, Device.ListsWaitingToBeFreed );
		for ( int i = 0; i < freed; i++ ) Device.BeginFrame();
		Assert.AreEqual( 0, Device.ListsWaitingToBeFreed );
	}

	/// <summary>Frames that each take many lists keep them: none are freed and made again every window.</summary>
	[TestMethod]
	public void BusyFramesKeepTheirCommandLists()
	{
		var lists = new CommandList[40];
		void Frame()
		{
			for ( int i = 0; i < lists.Length; i++ ) lists[i] = Device.BeginCommandList( QueueType.Graphics, "Busy" );
			foreach ( var list in lists ) Device.Submit( list );
			Device.BeginFrame();
		}

		// Two windows first, so spares other tests left are trimmed already
		for ( ulong i = 0; i <= 2 * Device.UploadPageIdleFrames; i++ ) Frame();
		var freedBefore = Device.CommandListsFreed;
		for ( ulong i = 0; i <= 2 * Device.UploadPageIdleFrames; i++ ) Frame();
		Assert.AreEqual( freedBefore, Device.CommandListsFreed, "lists the frames use aren't freed and made again" );
	}

	/// <summary>
	/// Going by time, spare command lists wait <see cref="Device.CommandListSpareSeconds"/> before they're freed, longer than
	/// upload pages: a view that comes back every few seconds finds its lists still there.
	/// </summary>
	[TestMethod]
	public void SpareListsWaitTheirOwnSeconds()
	{
		var spareSeconds = Device.Allocator.SpareSeconds;
		var listSeconds = Device.CommandListSpareSeconds;
		try
		{
			// Two windows by frames first, so spares other tests left are trimmed already
			for ( ulong i = 0; i <= 2 * Device.UploadPageIdleFrames; i++ ) Device.BeginFrame();
			Device.Allocator.SpareSeconds = 0.01;
			Device.CommandListSpareSeconds = 600;

			var lists = new CommandList[48];
			for ( int i = 0; i < lists.Length; i++ ) lists[i] = Device.BeginCommandList( QueueType.Graphics, "Burst" );
			SyncPoint done = default;
			foreach ( var list in lists ) done = Device.Submit( list );
			Device.Wait( done );

			// Three windows of frames and of the allocator's seconds (two whole ones after the burst), in well under the lists'
			// seconds: kept
			for ( int window = 0; window < 3; window++ )
			{
				System.Threading.Thread.Sleep( 20 );
				for ( ulong i = 0; i <= Device.UploadPageIdleFrames; i++ ) Device.BeginFrame();
			}

			Assert.IsTrue( Device.FreeCommandLists( QueueType.Graphics ) >= lists.Length, $"the burst's lists are kept ({Device.FreeCommandLists( QueueType.Graphics )})" );

			// Past the lists' seconds, for three windows: freed down to those kept
			Device.CommandListSpareSeconds = 0.01;
			for ( int window = 0; window < 3; window++ )
			{
				System.Threading.Thread.Sleep( 20 );
				for ( ulong i = 0; i <= Device.UploadPageIdleFrames; i++ ) Device.BeginFrame();
			}

			Assert.AreEqual( Device.KeptFreeCommandLists, Device.FreeCommandLists( QueueType.Graphics ) );
		}
		finally
		{
			Device.Allocator.SpareSeconds = spareSeconds;
			Device.CommandListSpareSeconds = listSeconds;
		}
	}

	/// <summary>
	/// Frames that take a few lists fewer than before (a child view that comes and goes) free the lists they no longer use, but
	/// the spares kept keep their command memory: the next frames record into them, and each would grow its pool again.
	/// </summary>
	[TestMethod]
	public void SmallDropKeepsTheSparesCommandMemory()
	{
		var lists = new CommandList[40];
		void Frame( int count )
		{
			for ( int i = 0; i < count; i++ ) lists[i] = Device.BeginCommandList( QueueType.Graphics, "Steady" );
			for ( int i = 0; i < count; i++ ) Device.Submit( lists[i] );
			Device.BeginFrame();
		}

		// Two windows first, so spares other tests left are trimmed already
		for ( ulong i = 0; i <= 2 * Device.UploadPageIdleFrames; i++ ) Frame( 40 );
		var freedBefore = Device.CommandListsFreed;
		var releasedBefore = Device.CommandMemoryReleases;

		// Ten fewer a frame: the frames in flight leave 10 each spare, over the kept ones but by fewer than are kept
		for ( ulong i = 0; i <= 2 * Device.UploadPageIdleFrames; i++ ) Frame( 30 );
		var freed = Device.CommandListsFreed - freedBefore;
		Assert.IsTrue( freed > 0 && freed < Device.KeptFreeCommandLists, $"{freed} lists freed" );
		Assert.AreEqual( releasedBefore, Device.CommandMemoryReleases, "the spares kept keep their command memory" );
	}

	/// <summary>
	/// A list starts with a small upload page and takes the next size up each time it fills one, so lists that upload a
	/// little don't each hold 4 MiB.
	/// </summary>
	[TestMethod]
	public void PagesGrowWithWhatAListUploads()
	{
		var bytes = Device.UploadPageBytes;
		var list = Device.BeginCommandList( QueueType.Graphics, "Little" );
		var first = list.AllocateUpload( 1024 );
		Assert.AreEqual( CommandList.PageSizes[0], Device.SizeOf( first.Buffer ), "a small page first" );

		// Past the first page: the next size, then the biggest
		var second = list.AllocateUpload( (int)CommandList.PageSizes[0] );
		Assert.AreEqual( CommandList.PageSizes[1], Device.SizeOf( second.Buffer ) );
		var third = list.AllocateUpload( (int)CommandList.PageSizes[1] );
		Assert.AreEqual( CommandList.PageSizes[2], Device.SizeOf( third.Buffer ) );
		var fourth = list.AllocateUpload( (int)CommandList.PageSizes[2] );
		Assert.AreEqual( CommandList.PageSizes[2], Device.SizeOf( fourth.Buffer ), "and stays there" );
		Device.Wait( Device.Submit( list ) );
		Assert.IsTrue( Device.UploadPageBytes >= bytes );
	}
}
