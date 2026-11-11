using Basalt.Gpu.Vulkan;

namespace Basalt.Gpu;

/// <summary>
/// A pool of occlusion queries (<see cref="Device.CreateOcclusionQueries"/>): reset one in a command list
/// (<see cref="CommandList.ResetQueries"/>), count the samples that pass while drawing (<see cref="CommandList.BeginQuery"/>
/// and <see cref="CommandList.EndQuery"/>, inside one rendering scope) and read the count back once that work has finished.
/// Counts are exact where <see cref="DeviceCapabilities.PreciseOcclusion"/>.
/// </summary>
public sealed unsafe class OcclusionQueries : IDisposable
{
	readonly Device device;
	internal ulong Pool { get; private set; }

	/// <summary>How many queries it holds.</summary>
	public int Count { get; }

	internal OcclusionQueries( Device device, int count )
	{
		this.device = device;
		Count = count;

		// VK_QUERY_TYPE_OCCLUSION
		var info = new VkQueryPoolCreateInfo { sType = VkStructureType.QueryPoolCreateInfo, queryType = 0, queryCount = (uint)count };
		ulong pool;
		device.Vk.vkCreateQueryPool( device.Vk.Device, &info, null, &pool ).Check( "vkCreateQueryPool" );
		Pool = pool;
	}

	/// <summary>
	/// Copy query <paramref name="index"/>'s count into <paramref name="samples"/> if the GPU has finished it; false, without
	/// waiting, if it hasn't yet.
	/// </summary>
	public bool TryRead( int index, out ulong samples )
	{
		if ( index < 0 || index >= Count ) throw new ArgumentOutOfRangeException( nameof( index ) );
		ulong value;

		// 64-bit results
		var result = device.Vk.vkGetQueryPoolResults( device.Vk.Device, Pool, (uint)index, 1, 8, &value, 8, 1 );
		samples = value;
		if ( result == VkResult.NotReady ) return false;
		result.Check( "vkGetQueryPoolResults" );
		return true;
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
	/// <summary>Make a pool of <paramref name="count"/> occlusion queries.</summary>
	public OcclusionQueries CreateOcclusionQueries( int count ) => new( this, count );
}
