using Borealis.Gpu;

namespace BorealisGpuTests;

/// <summary>
/// Query pools whose owners outlive the device (a window disposed after the renderer shut down) go with the device, and
/// disposing them afterwards does nothing.
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
}
