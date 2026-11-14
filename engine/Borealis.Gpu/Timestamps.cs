using Borealis.Gpu.Vulkan;

namespace Borealis.Gpu;

/// <summary>
/// A pool of GPU timestamps (<see cref="Device.CreateTimestamps"/>): reset them in a command list
/// (<see cref="CommandList.ResetTimestamps"/>), write them as work passes a stage (<see cref="CommandList.WriteTimestamp"/>)
/// and read them back once that work has finished. Ticks are <see cref="DeviceCapabilities.TimestampPeriod"/> nanoseconds.
/// </summary>
public sealed unsafe class TimestampQueries : IDisposable
{
	readonly Device device;
	internal ulong Pool { get; private set; }

	/// <summary>How many timestamps it holds.</summary>
	public int Count { get; }

	internal TimestampQueries( Device device, int count )
	{
		this.device = device;
		Count = count;
		var info = new VkQueryPoolCreateInfo { sType = VkStructureType.QueryPoolCreateInfo, queryType = 2, queryCount = (uint)count };
		ulong pool;
		device.Vk.vkCreateQueryPool( device.Vk.Device, &info, null, &pool ).Check( "vkCreateQueryPool" );
		Pool = pool;
	}

	/// <summary>
	/// Copy timestamps <paramref name="first"/> onwards into <paramref name="ticks"/> if the GPU has written them all; false,
	/// without waiting, if it hasn't yet.
	/// </summary>
	public bool TryRead( int first, Span<ulong> ticks )
	{
		if ( first < 0 || first + ticks.Length > Count ) throw new ArgumentOutOfRangeException( nameof( first ) );
		fixed ( ulong* data = ticks )
		{
			// 64-bit results
			var result = device.Vk.vkGetQueryPoolResults( device.Vk.Device, Pool, (uint)first, (uint)ticks.Length, (nuint)(ticks.Length * 8), data, 8, 1 );
			if ( result == VkResult.NotReady ) return false;
			result.Check( "vkGetQueryPoolResults" );
			return true;
		}
	}

	/// <summary>Destroy the pool; the GPU must be done with it.</summary>
	public void Dispose()
	{
		if ( Pool == 0 ) return;
		device.Vk.vkDestroyQueryPool( device.Vk.Device, Pool, null );
		Pool = 0;
	}
}

public sealed partial class Device
{
	/// <summary>
	/// Make a pool of <paramref name="count"/> timestamps, where <see cref="DeviceCapabilities.Timestamps"/> says the
	/// graphics queue writes them.
	/// </summary>
	public TimestampQueries CreateTimestamps( int count ) => new( this, count );
}
