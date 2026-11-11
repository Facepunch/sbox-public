using Basalt.Gpu.Vulkan;

namespace Basalt.Gpu;

/// <summary>What one pipeline statistics query counted between its begin and end.</summary>
public readonly record struct PipelineCounts( ulong Primitives, ulong VertexInvocations, ulong ClippedPrimitives, ulong FragmentInvocations, ulong ComputeInvocations );

/// <summary>
/// A pool of pipeline statistics queries (<see cref="Device.CreatePipelineStatistics"/>), where
/// <see cref="DeviceCapabilities.PipelineStatistics"/>: reset them in a command list (<see cref="CommandList.ResetQueries(PipelineStatistics, int, int)"/>),
/// count what the work between <see cref="CommandList.BeginQuery(PipelineStatistics, int)"/> and
/// <see cref="CommandList.EndQuery(PipelineStatistics, int)"/> does, both outside rendering or both in one rendering scope,
/// and read the counts back once it has finished. For profiling: what a pass assembles, shades and dispatches.
/// </summary>
public sealed unsafe class PipelineStatistics : IDisposable
{
	// Input assembly primitives, vertex shader invocations, clipping primitives, fragment shader invocations, compute
	// shader invocations; results come in this order
	const uint Counted = 0x2 | 0x4 | 0x40 | 0x80 | 0x400;
	const int Values = 5;

	readonly Device device;
	internal ulong Pool { get; private set; }

	/// <summary>How many queries it holds.</summary>
	public int Count { get; }

	internal PipelineStatistics( Device device, int count )
	{
		if ( !device.Capabilities.PipelineStatistics ) throw new GpuException( $"{device.Adapter.Name} has no pipeline statistics queries" );
		this.device = device;
		Count = count;

		// VK_QUERY_TYPE_PIPELINE_STATISTICS
		var info = new VkQueryPoolCreateInfo { sType = VkStructureType.QueryPoolCreateInfo, queryType = 1, queryCount = (uint)count, pipelineStatistics = Counted };
		ulong pool;
		device.Vk.vkCreateQueryPool( device.Vk.Device, &info, null, &pool ).Check( "vkCreateQueryPool" );
		Pool = pool;
	}

	/// <summary>
	/// Copy queries <paramref name="first"/> onwards into <paramref name="counts"/> if the GPU has finished them all; false,
	/// without waiting, if it hasn't yet.
	/// </summary>
	public bool TryRead( int first, Span<PipelineCounts> counts )
	{
		if ( first < 0 || first + counts.Length > Count ) throw new ArgumentOutOfRangeException( nameof( first ) );
		if ( counts.Length == 0 ) return true;

		var values = new ulong[counts.Length * Values];
		fixed ( ulong* data = values )
		{
			// 64-bit results
			var result = device.Vk.vkGetQueryPoolResults( device.Vk.Device, Pool, (uint)first, (uint)counts.Length, (nuint)(values.Length * 8), data, Values * 8, 1 );
			if ( result == VkResult.NotReady ) return false;
			result.Check( "vkGetQueryPoolResults" );
		}

		for ( int i = 0; i < counts.Length; i++ )
		{
			var v = values.AsSpan( i * Values, Values );
			counts[i] = new PipelineCounts( v[0], v[1], v[2], v[3], v[4] );
		}

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
	/// <summary>Make a pool of <paramref name="count"/> pipeline statistics queries.</summary>
	public PipelineStatistics CreatePipelineStatistics( int count ) => new( this, count );
}
