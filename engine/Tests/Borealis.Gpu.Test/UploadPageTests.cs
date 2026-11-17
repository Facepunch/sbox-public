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
}
