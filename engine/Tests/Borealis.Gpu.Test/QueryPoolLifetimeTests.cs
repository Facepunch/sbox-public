using Borealis.Gpu;

namespace BorealisGpuTests;

/// <summary>
/// Query pools whose owners outlive the device (a window disposed after the renderer shut down) go with the device, and
/// disposing them afterwards does nothing; one disposed while work still uses it goes once that work is done.
/// </summary>
[TestClass]
public class QueryPoolLifetimeTests : GpuTest
{
	[TestMethod]
	public void PoolsLeftAliveGoWithTheDevice()
	{
		var timestamps = Device.CreateTimestamps( 4 );
		var occlusion = Device.CreateOcclusionQueries( 4 );

		// GpuTest's cleanup fails on validation errors, so a pool still alive when the device is destroyed would fail here
		DestroyDevice();
		timestamps.Dispose();
		occlusion.Dispose();
	}

	/// <summary>A pool disposed while submitted work still writes it is destroyed once that work is done, not at once.</summary>
	[TestMethod]
	public void PoolsInUseGoAfterTheirWork()
	{
		var timestamps = Device.CreateTimestamps( 2 );
		var list = Device.BeginCommandList( QueueType.Graphics, "Timestamps" );
		list.ResetTimestamps( timestamps, 0, 2 );
		list.WriteTimestamp( timestamps, 0 );
		list.WriteTimestamp( timestamps, 1 );
		Device.Submit( list );

		// Validation (synchronisation checks included) fails the test if the pool goes while the list may still run
		timestamps.Dispose();
		for ( int i = 0; i <= Device.FramesInFlight; i++ ) Device.BeginFrame();
		Device.WaitIdle();
	}
}
