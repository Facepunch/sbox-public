using Borealis.Gpu.Vulkan;
using System.Diagnostics;
using System.Threading;

namespace Borealis.Gpu;

public sealed partial class Device
{
	int lost;

	// Whether the driver lost the device: a call returned VK_ERROR_DEVICE_LOST, or it signalled everything pending
	volatile bool driverLost;

	// Whether a call returned VK_ERROR_DEVICE_LOST: only then may the driver's fault report be read
	volatile bool lostReturned;

	/// <summary>
	/// Whether the device is lost: a queue call returned <c>VK_ERROR_DEVICE_LOST</c>, or the GPU stopped reaching what was
	/// waited for (<see cref="HangSeconds"/>). Nothing submitted finishes after it; waits return at once and submissions
	/// throw a <see cref="GpuException"/> whose <see cref="GpuException.IsDeviceLost"/> is set.
	/// </summary>
	public bool IsLost => Volatile.Read( ref lost ) != 0;

	/// <summary>
	/// What the device said when it was lost, taken at that moment: the call that saw it, the driver's fault report and the
	/// last lists submitted (<see cref="DescribeRecentSubmissions"/>). Null while it isn't lost.
	/// </summary>
	public string LostReport { get; private set; }

	/// <summary>
	/// How long a wait for the GPU may go without it reaching the value before the device is taken as lost. A Windows GPU
	/// timeout resets the device after about 2 s and Linux's after about 10 s; a wait still going well after that is waiting
	/// for something that will never be signalled.
	/// </summary>
	public double HangSeconds { get; set; } = 20;

	/// <summary>How long a wait goes before it's said once in the device's messages, with what was in flight.</summary>
	const double SlowWaitSeconds = 5;

	/// <summary>
	/// Take the device as lost (once): note what it was doing, tell the message callback, and from now on stop waiting for
	/// the GPU.
	/// </summary>
	internal void MarkLost( string reason, bool byDriver = false )
	{
		if ( byDriver ) driverLost = true;
		if ( Interlocked.Exchange( ref lost, 1 ) != 0 ) return;

		string report;
		try { report = DescribeRecentSubmissions(); }
		catch ( Exception e ) { report = $"(couldn't describe the recent submissions: {e.Message})"; }

		LostReport = $"{reason}\n{report}";
		message?.Invoke( new DeviceMessage( MessageSeverity.Error, "Borealis.Gpu.DeviceLost", $"Device lost: {reason}" ) );
	}

	/// <summary>Throw for a failed Vulkan call, taking the device as lost first when that's why it failed.</summary>
	internal void Check( VkResult result, string call )
	{
		if ( result == VkResult.ErrorDeviceLost )
		{
			lostReturned = true;
			MarkLost( $"{call} returned VK_ERROR_DEVICE_LOST", byDriver: true );
		}

		result.Check( call );
	}

	/// <summary>Throw if the device is lost, before work that would go to it.</summary>
	internal void ThrowIfLost( string call )
	{
		if ( IsLost ) throw new GpuException( call, VkResult.ErrorDeviceLost );
	}

	/// <summary>
	/// Wait on the CPU for a queue's timeline to reach <paramref name="value"/>, in slices: said once in the messages after
	/// <see cref="SlowWaitSeconds"/>, and the device taken as lost after <see cref="HangSeconds"/>. Returns at once when the
	/// device is lost.
	/// </summary>
	internal unsafe void WaitForTimeline( DeviceQueue queue, ulong value )
	{
		var semaphore = queue.Timeline;
		var info = new VkSemaphoreWaitInfo { sType = VkStructureType.SemaphoreWaitInfo, semaphoreCount = 1, pSemaphores = &semaphore, pValues = &value };
		var start = Stopwatch.GetTimestamp();
		var said = false;

		while ( !IsLost )
		{
			var result = Vk.vkWaitSemaphores( Vk.Device, &info, 500_000_000 );
			if ( result == VkResult.Success ) return;
			if ( result != VkResult.Timeout )
			{
				Check( result, "vkWaitSemaphores" );
				return;
			}

			var waited = Stopwatch.GetElapsedTime( start ).TotalSeconds;
			if ( waited >= HangSeconds )
			{
				MarkLost( $"The GPU didn't reach {queue.Type} {value} in {waited:F0} s (completed {queue.CompletedSeen})" );
				return;
			}

			if ( !said && waited >= SlowWaitSeconds )
			{
				said = true;
				message?.Invoke( new DeviceMessage( MessageSeverity.Warning, "Borealis.Gpu.SlowWait", $"Waited {waited:F0} s for {queue.Type} {value} (completed {queue.CompletedSeen}): {DescribeRecentSubmissions()}" ) );
			}
		}
	}
}
