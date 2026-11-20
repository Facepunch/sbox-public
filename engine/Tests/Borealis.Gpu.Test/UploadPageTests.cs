using Borealis.Gpu;

namespace BorealisGpuTests;

/// <summary>
/// Upload pages a burst of uploads took (a map's load) are freed once they've gone unused for a while, down to a spare
/// kept for later lists, rather than held mapped for good.
/// </summary>
[TestClass]
[DoNotParallelize]
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
		Assert.AreEqual( Device.KeptFreeUploadPages, Device.FreePages[2].Count );

		// The pages freed were those in the emptiest blocks, so the burst's blocks went back (once the allocator stops keeping
		// an empty one) but for the spare's
		for ( ulong i = 0; i <= Borealis.Gpu.Memory.MemoryAllocator.SpareFrames; i++ ) Device.BeginFrame();
		Assert.IsTrue( Device.MemoryUsage.Reserved - reserved <= (long)Device.Allocator.BlockSize, $"{(Device.MemoryUsage.Reserved - reserved) >> 20} MiB more reserved than before the burst" );
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

		// The lists kept still record
		var again = Device.BeginCommandList( QueueType.Graphics, "Again" );
		Device.Wait( Device.Submit( again ) );
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
