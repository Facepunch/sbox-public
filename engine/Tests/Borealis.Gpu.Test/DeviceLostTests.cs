using Borealis.Gpu;

namespace BorealisGpuTests;

/// <summary>
/// A device that's lost, or a wait for something the GPU will never reach, doesn't hold the CPU: waits return or throw,
/// submissions say the device is lost, and the device still disposes. A binding that would put an empty view in front of
/// the GPU throws instead.
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
	/// Binding a texture as storage when it has no storage view throws, rather than writing an empty descriptor the GPU would
	/// read or write through (undefined without the null descriptor feature; a write can fault and lose the device).
	/// </summary>
	[TestMethod]
	public void StorageBindingOfASampledOnlyTextureThrows()
	{
		var texture = Device.CreateTexture( TextureInfo.Texture2D( 4, 4, Format.RGBA8Unorm, name: "Sampled only" ) );
		var layout = Device.CreateBindingLayout( [new BindingSlot( 0, BindingType.StorageTexture, ShaderStages.Compute )] );
		var list = Device.BeginCommandList( QueueType.Graphics, "Storage binding" );

		var thrown = Assert.ThrowsException<ArgumentException>( () => list.SetBindings( 0, layout, [Binding.StorageTexture( 0, texture )] ) );
		StringAssert.Contains( thrown.Message, "Sampled only" );
		Device.Wait( Device.Submit( list ) );
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

	/// <summary>
	/// A watching wait (<see cref="Device.TryWait"/>) that the driver says the device is lost to returns as done, with the
	/// device taken as lost, rather than throwing on the watcher's own thread and ending the process before the frame's loss
	/// handling runs.
	/// </summary>
	[TestMethod]
	[Timeout( 30_000 )]
	public void AWatchingWaitTakesALossAsDone()
	{
		Assert.IsFalse( Device.TryWaitDone( Borealis.Gpu.Vulkan.VkResult.Timeout ) );
		Assert.IsFalse( Device.IsLost );

		Assert.IsTrue( Device.TryWaitDone( Borealis.Gpu.Vulkan.VkResult.ErrorDeviceLost ) );
		Assert.IsTrue( Device.IsLost );
		StringAssert.Contains( Device.LostReport, "vkWaitSemaphores returned VK_ERROR_DEVICE_LOST" );

		var errors = TakeValidationErrors();
		CollectionAssert.AreEqual( new[] { "Borealis.Gpu.DeviceLost" }, errors, string.Join( ", ", errors ) );
	}

	/// <summary>
	/// A timeline past everything submitted (what a driver signals once the device is lost, before any call says so) takes the
	/// device as lost, so nothing is submitted after it: a signal below the timeline's value is invalid.
	/// </summary>
	[TestMethod]
	[Timeout( 30_000 )]
	public unsafe void TimelinePastSubmittedMeansLost()
	{
		Device.Wait( Device.Submit( Device.BeginCommandList( QueueType.Graphics, "First" ) ) );

		// A held list takes the next value without reaching the GPU, so the timeline is read again
		var held = Device.SubmitLater( Device.BeginCommandList( QueueType.Graphics, "Held" ) );
		var queue = Device.QueueFor( QueueType.Graphics );
		var info = new Borealis.Gpu.Vulkan.VkSemaphoreSignalInfo { sType = Borealis.Gpu.Vulkan.VkStructureType.SemaphoreSignalInfo, semaphore = queue.Timeline, value = queue.Submitted + 1000 };
		Assert.AreEqual( 0, (int)Device.Vk.vkSignalSemaphore( Device.Vk.Device, &info ) );

		Assert.IsTrue( Device.IsComplete( held ) );
		Assert.IsTrue( Device.IsLost );
		StringAssert.Contains( Device.LostReport, "jumped" );
		Assert.ThrowsException<GpuException>( () => Device.Submit( Device.BeginCommandList( QueueType.Graphics, "After" ) ) );

		// The fault report is read as after a real loss, which validation reports here, where the device isn't
		CollectionAssert.Contains( TakeValidationErrors(), "Borealis.Gpu.DeviceLost" );
	}
}
