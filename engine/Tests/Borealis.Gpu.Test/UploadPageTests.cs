using Borealis.Gpu;

namespace BorealisGpuTests;

/// <summary>
/// Upload pages a burst of uploads took (a map's load) are freed once they've gone unused for a while, down to a few kept
/// for later lists, rather than held mapped for good.
/// </summary>
[TestClass]
[DoNotParallelize]
public class UploadPageTests : GpuTest
{
	[TestMethod]
	public void IdlePagesAreFreed()
	{
		var list = Device.BeginCommandList( QueueType.Graphics, "Burst" );
		for ( int i = 0; i < 24; i++ ) list.AllocateUpload( 3 << 20 );
		Device.Wait( Device.Submit( list ) );

		for ( int i = 0; i <= Device.FramesInFlight; i++ ) Device.BeginFrame();
		Assert.IsTrue( Device.FreePages.Count >= 24, $"the burst's pages are free for reuse ({Device.FreePages.Count})" );

		// Idle for a whole window of frames: up to two windows after the burst
		for ( ulong i = 0; i <= 2 * Device.UploadPageIdleFrames; i++ ) Device.BeginFrame();
		Assert.AreEqual( Device.KeptFreeUploadPages, Device.FreePages.Count );
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
}
