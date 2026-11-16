using Borealis.Gpu.Vulkan;
using System.Collections.Concurrent;

namespace Borealis.Gpu;

/// <summary>
/// The driver's statistics for each pipeline's shaders (<see cref="DeviceOptions.ShaderStatistics"/>,
/// VK_KHR_pipeline_executable_properties): one entry per executable the driver made for a pipeline (a stage, or stages it
/// merged), with its wave size and every statistic it reports by name, such as RADV's VGPRs, SGPRs, spills and waves per
/// SIMD. Kept as each pipeline is made.
/// </summary>
public sealed unsafe partial class Device
{
	const uint CaptureStatistics = 0x40; // VK_PIPELINE_CREATE_CAPTURE_STATISTICS_BIT_KHR

	bool shaderStatistics;
	readonly ConcurrentQueue<ShaderStatistics> statistics = new();

	/// <summary>Every pipeline's executables made so far, with their statistics; empty without <see cref="DeviceOptions.ShaderStatistics"/>.</summary>
	public IReadOnlyCollection<ShaderStatistics> ShaderStatistics => statistics;

	void KeepStatistics( ulong pipeline, string name )
	{
		var info = new VkPipelineInfoKHR { sType = VkStructureType.PipelineInfoKhr, pipeline = pipeline };
		uint count = 0;
		if ( Vk.vkGetPipelineExecutablePropertiesKHR( Vk.Device, &info, &count, null ) != VkResult.Success || count == 0 ) return;

		var executables = stackalloc VkPipelineExecutablePropertiesKHR[(int)count];
		for ( int i = 0; i < count; i++ ) executables[i] = new VkPipelineExecutablePropertiesKHR { sType = VkStructureType.PipelineExecutablePropertiesKhr };
		if ( Vk.vkGetPipelineExecutablePropertiesKHR( Vk.Device, &info, &count, executables ) != VkResult.Success ) return;

		for ( uint e = 0; e < count; e++ )
		{
			var executable = new VkPipelineExecutableInfoKHR { sType = VkStructureType.PipelineExecutableInfoKhr, pipeline = pipeline, executableIndex = e };
			uint statCount = 0;
			Vk.vkGetPipelineExecutableStatisticsKHR( Vk.Device, &executable, &statCount, null );
			var stats = new VkPipelineExecutableStatisticKHR[statCount];
			for ( int i = 0; i < stats.Length; i++ ) stats[i].sType = VkStructureType.PipelineExecutableStatisticKhr;
			var values = new Dictionary<string, double>();
			fixed ( VkPipelineExecutableStatisticKHR* p = stats )
			{
				if ( statCount > 0 && Vk.vkGetPipelineExecutableStatisticsKHR( Vk.Device, &executable, &statCount, p ) == VkResult.Success )
				{
					for ( int i = 0; i < statCount; i++ )
					{
						var value = p[i].value;
						values[Text( p[i].name )] = p[i].format switch
						{
							0 => (uint)value != 0 ? 1 : 0,
							1 => (long)value,
							3 => BitConverter.UInt64BitsToDouble( value ),
							_ => value,
						};
					}
				}
			}

			statistics.Enqueue( new ShaderStatistics( name, Text( executables[e].name ), executables[e].stages, (int)executables[e].subgroupSize, values ) );
		}

		static string Text( byte* text ) => Utf8( text, 256 );
	}
}

/// <summary>
/// One executable of a pipeline, as the driver reports it: the pipeline's name, the executable's (the driver's, such as
/// "Fragment Shader"), the Vulkan stages it covers, its wave size, and its statistics by the driver's names.
/// </summary>
public sealed record ShaderStatistics( string Pipeline, string Executable, uint Stages, int WaveSize, IReadOnlyDictionary<string, double> Values );
