using Borealis.Gpu;

namespace BorealisGpuTests;

/// <summary>
/// A device that's lost, or a wait for something the GPU will never reach, doesn't hold the CPU: waits return or throw,
/// submissions say the device is lost, and the device still disposes.
/// </summary>
[TestClass]
[DoNotParallelize]
public class DeviceLostTests : GpuTest
{
	/// <summary>Waiting for a point nothing has submitted throws rather than waiting forever.</summary>
	[TestMethod]
	[Timeout( 30_000 )]
	public void WaitingForAPointNeverSubmittedThrows()
	{
		var list = Device.BeginCommandList( QueueType.Graphics, "First" );
		var point = Device.Submit( list );
		Device.Wait( point );

		Assert.ThrowsException<InvalidOperationException>( () => Device.Wait( new SyncPoint( QueueType.Graphics, point.Value + 5 ) ) );
	}

	/// <summary>
	/// Once the device is lost, it says why and what it was doing, waits and frames don't block, a submission throws a
	/// device-lost error, and it still disposes.
	/// </summary>
	[TestMethod]
	[Timeout( 30_000 )]
	public void LostDeviceStopsWaiting()
	{
		var list = Device.BeginCommandList( QueueType.Graphics, "Before the loss" );
		var point = Device.Submit( list );
		Device.Wait( point );

		Assert.IsFalse( Device.IsLost );
		Device.MarkLost( "Lost on purpose" );
		Assert.IsTrue( Device.IsLost );
		StringAssert.Contains( Device.LostReport, "Lost on purpose" );
		StringAssert.Contains( Device.LostReport, "Before the loss" );

		Device.Wait( point );
		Assert.IsTrue( Device.IsComplete( Device.LastSubmitted( QueueType.Graphics ) ) );
		Device.BeginFrame();
		Device.WaitIdle();

		var after = Device.BeginCommandList( QueueType.Graphics, "After the loss" );
		var thrown = Assert.ThrowsException<GpuException>( () => Device.Submit( after ) );
		Assert.IsTrue( thrown.IsDeviceLost, thrown.Message );

		// The loss is said once, as an error
		var errors = TakeValidationErrors();
		CollectionAssert.AreEqual( new[] { "Borealis.Gpu.DeviceLost" }, errors, string.Join( ", ", errors ) );
	}
}
