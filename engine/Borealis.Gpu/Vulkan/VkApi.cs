using System.Runtime.InteropServices;
using System.Text;

namespace Borealis.Gpu.Vulkan;

/// <summary>
/// The Vulkan loader library and the entry points that need no instance: our own loader, not a binding. Loads
/// <c>vulkan-1.dll</c> on Windows and <c>libvulkan.so.1</c> elsewhere, and resolves everything else through
/// <c>vkGetInstanceProcAddr</c> and <c>vkGetDeviceProcAddr</c>.
/// </summary>
internal static unsafe class VkLoader
{
	static nint library;
	static delegate* unmanaged< nint, byte*, nint > getInstanceProcAddr;

	/// <summary>Whether a Vulkan loader was found.</summary>
	public static bool IsAvailable => TryLoad();

	static readonly object loadLock = new();

	public static bool TryLoad()
	{
		if ( library != 0 ) return true;

		lock ( loadLock )
		{
			if ( library != 0 ) return true;

			string[] names = OperatingSystem.IsWindows() ? ["vulkan-1.dll"]
				: OperatingSystem.IsMacOS() ? ["libvulkan.1.dylib", "libvulkan.dylib", "libMoltenVK.dylib"]
				: ["libvulkan.so.1", "libvulkan.so"];

			foreach ( var name in names )
			{
				if ( !NativeLibrary.TryLoad( name, out var handle ) ) continue;
				if ( !NativeLibrary.TryGetExport( handle, "vkGetInstanceProcAddr", out var proc ) ) continue;

				getInstanceProcAddr = (delegate* unmanaged< nint, byte*, nint >)proc;
				library = handle;
				return true;
			}

			return false;
		}
	}

	public static nint GetInstanceProcAddr( nint instance, string name )
	{
		Span<byte> utf8 = stackalloc byte[128];
		var length = Encoding.UTF8.GetBytes( name, utf8 );
		utf8[length] = 0;
		fixed ( byte* p = utf8 ) return getInstanceProcAddr( instance, p );
	}
}

internal static class VkCheck
{
	public static void Check( this VkResult result, string call )
	{
		if ( result < 0 ) throw new GpuException( call, result );
	}
}

/// <summary>
/// Entry points that come from the loader before there's an instance.
/// </summary>
internal static unsafe class VkGlobal
{
	public static delegate* unmanaged< VkInstanceCreateInfo*, void*, nint*, VkResult > vkCreateInstance;
	public static delegate* unmanaged< byte*, uint*, VkExtensionProperties*, VkResult > vkEnumerateInstanceExtensionProperties;
	public static delegate* unmanaged< uint*, VkLayerProperties*, VkResult > vkEnumerateInstanceLayerProperties;
	public static delegate* unmanaged< uint*, VkResult > vkEnumerateInstanceVersion;

	static bool loaded;

	public static void Load()
	{
		if ( loaded ) return;
		if ( !VkLoader.TryLoad() ) throw new GpuException( "No Vulkan loader (vulkan-1.dll or libvulkan.so.1) was found" );

		vkCreateInstance = (delegate* unmanaged< VkInstanceCreateInfo*, void*, nint*, VkResult >)VkLoader.GetInstanceProcAddr( 0, "vkCreateInstance" );
		vkEnumerateInstanceExtensionProperties = (delegate* unmanaged< byte*, uint*, VkExtensionProperties*, VkResult >)VkLoader.GetInstanceProcAddr( 0, "vkEnumerateInstanceExtensionProperties" );
		vkEnumerateInstanceLayerProperties = (delegate* unmanaged< uint*, VkLayerProperties*, VkResult >)VkLoader.GetInstanceProcAddr( 0, "vkEnumerateInstanceLayerProperties" );
		vkEnumerateInstanceVersion = (delegate* unmanaged< uint*, VkResult >)VkLoader.GetInstanceProcAddr( 0, "vkEnumerateInstanceVersion" );
		loaded = true;
	}
}

/// <summary>
/// Instance-level entry points. Optional ones (debug utils, surfaces of other platforms) are null when absent.
/// </summary>
internal sealed unsafe class VkInstanceApi
{
	public readonly nint Instance;

	public readonly delegate* unmanaged< nint, void*, void > vkDestroyInstance;
	public readonly delegate* unmanaged< nint, uint*, nint*, VkResult > vkEnumeratePhysicalDevices;
	public readonly delegate* unmanaged< nint, VkPhysicalDeviceProperties*, void > vkGetPhysicalDeviceProperties;
	public readonly delegate* unmanaged< nint, VkPhysicalDeviceProperties2*, void > vkGetPhysicalDeviceProperties2;
	public readonly delegate* unmanaged< nint, VkPhysicalDeviceFeatures2*, void > vkGetPhysicalDeviceFeatures2;
	public readonly delegate* unmanaged< nint, uint*, VkQueueFamilyProperties*, void > vkGetPhysicalDeviceQueueFamilyProperties;
	public readonly delegate* unmanaged< nint, VkPhysicalDeviceMemoryProperties*, void > vkGetPhysicalDeviceMemoryProperties;
	public readonly delegate* unmanaged< nint, VkPhysicalDeviceMemoryProperties2*, void > vkGetPhysicalDeviceMemoryProperties2;
	public readonly delegate* unmanaged< nint, VkFormat, VkFormatProperties*, void > vkGetPhysicalDeviceFormatProperties;
	public readonly delegate* unmanaged< nint, byte*, uint*, VkExtensionProperties*, VkResult > vkEnumerateDeviceExtensionProperties;
	public readonly delegate* unmanaged< nint, VkDeviceCreateInfo*, void*, nint*, VkResult > vkCreateDevice;
	public readonly delegate* unmanaged< nint, byte*, nint > vkGetDeviceProcAddr;

	public readonly delegate* unmanaged< nint, VkDebugUtilsMessengerCreateInfoEXT*, void*, ulong*, VkResult > vkCreateDebugUtilsMessengerEXT;
	public readonly delegate* unmanaged< nint, ulong, void*, void > vkDestroyDebugUtilsMessengerEXT;
	public readonly delegate* unmanaged< nint, VkDebugUtilsObjectNameInfoEXT*, VkResult > vkSetDebugUtilsObjectNameEXT;
	public readonly delegate* unmanaged< nint, VkDebugUtilsLabelEXT*, void > vkCmdBeginDebugUtilsLabelEXT;
	public readonly delegate* unmanaged< nint, void > vkCmdEndDebugUtilsLabelEXT;

	public readonly delegate* unmanaged< nint, ulong, void*, void > vkDestroySurfaceKHR;
	public readonly delegate* unmanaged< nint, uint, ulong, uint*, VkResult > vkGetPhysicalDeviceSurfaceSupportKHR;
	public readonly delegate* unmanaged< nint, ulong, VkSurfaceCapabilitiesKHR*, VkResult > vkGetPhysicalDeviceSurfaceCapabilitiesKHR;
	public readonly delegate* unmanaged< nint, ulong, uint*, VkSurfaceFormatKHR*, VkResult > vkGetPhysicalDeviceSurfaceFormatsKHR;
	public readonly delegate* unmanaged< nint, ulong, uint*, VkPresentMode*, VkResult > vkGetPhysicalDeviceSurfacePresentModesKHR;
	public readonly delegate* unmanaged< nint, VkWin32SurfaceCreateInfoKHR*, void*, ulong*, VkResult > vkCreateWin32SurfaceKHR;
	public readonly delegate* unmanaged< nint, VkXlibSurfaceCreateInfoKHR*, void*, ulong*, VkResult > vkCreateXlibSurfaceKHR;
	public readonly delegate* unmanaged< nint, VkWaylandSurfaceCreateInfoKHR*, void*, ulong*, VkResult > vkCreateWaylandSurfaceKHR;

	public VkInstanceApi( nint instance )
	{
		Instance = instance;

		nint Get( string name ) => VkLoader.GetInstanceProcAddr( instance, name );
		nint Need( string name )
		{
			var proc = Get( name );
			return proc != 0 ? proc : throw new GpuException( $"The Vulkan loader has no {name}" );
		}

		vkDestroyInstance = (delegate* unmanaged< nint, void*, void >)Need( "vkDestroyInstance" );
		vkEnumeratePhysicalDevices = (delegate* unmanaged< nint, uint*, nint*, VkResult >)Need( "vkEnumeratePhysicalDevices" );
		vkGetPhysicalDeviceProperties = (delegate* unmanaged< nint, VkPhysicalDeviceProperties*, void >)Need( "vkGetPhysicalDeviceProperties" );
		vkGetPhysicalDeviceProperties2 = (delegate* unmanaged< nint, VkPhysicalDeviceProperties2*, void >)Need( "vkGetPhysicalDeviceProperties2" );
		vkGetPhysicalDeviceFeatures2 = (delegate* unmanaged< nint, VkPhysicalDeviceFeatures2*, void >)Need( "vkGetPhysicalDeviceFeatures2" );
		vkGetPhysicalDeviceQueueFamilyProperties = (delegate* unmanaged< nint, uint*, VkQueueFamilyProperties*, void >)Need( "vkGetPhysicalDeviceQueueFamilyProperties" );
		vkGetPhysicalDeviceMemoryProperties = (delegate* unmanaged< nint, VkPhysicalDeviceMemoryProperties*, void >)Need( "vkGetPhysicalDeviceMemoryProperties" );
		vkGetPhysicalDeviceMemoryProperties2 = (delegate* unmanaged< nint, VkPhysicalDeviceMemoryProperties2*, void >)Need( "vkGetPhysicalDeviceMemoryProperties2" );
		vkGetPhysicalDeviceFormatProperties = (delegate* unmanaged< nint, VkFormat, VkFormatProperties*, void >)Need( "vkGetPhysicalDeviceFormatProperties" );
		vkEnumerateDeviceExtensionProperties = (delegate* unmanaged< nint, byte*, uint*, VkExtensionProperties*, VkResult >)Need( "vkEnumerateDeviceExtensionProperties" );
		vkCreateDevice = (delegate* unmanaged< nint, VkDeviceCreateInfo*, void*, nint*, VkResult >)Need( "vkCreateDevice" );
		vkGetDeviceProcAddr = (delegate* unmanaged< nint, byte*, nint >)Need( "vkGetDeviceProcAddr" );

		vkCreateDebugUtilsMessengerEXT = (delegate* unmanaged< nint, VkDebugUtilsMessengerCreateInfoEXT*, void*, ulong*, VkResult >)Get( "vkCreateDebugUtilsMessengerEXT" );
		vkDestroyDebugUtilsMessengerEXT = (delegate* unmanaged< nint, ulong, void*, void >)Get( "vkDestroyDebugUtilsMessengerEXT" );
		vkSetDebugUtilsObjectNameEXT = (delegate* unmanaged< nint, VkDebugUtilsObjectNameInfoEXT*, VkResult >)Get( "vkSetDebugUtilsObjectNameEXT" );
		vkCmdBeginDebugUtilsLabelEXT = (delegate* unmanaged< nint, VkDebugUtilsLabelEXT*, void >)Get( "vkCmdBeginDebugUtilsLabelEXT" );
		vkCmdEndDebugUtilsLabelEXT = (delegate* unmanaged< nint, void >)Get( "vkCmdEndDebugUtilsLabelEXT" );

		vkDestroySurfaceKHR = (delegate* unmanaged< nint, ulong, void*, void >)Get( "vkDestroySurfaceKHR" );
		vkGetPhysicalDeviceSurfaceSupportKHR = (delegate* unmanaged< nint, uint, ulong, uint*, VkResult >)Get( "vkGetPhysicalDeviceSurfaceSupportKHR" );
		vkGetPhysicalDeviceSurfaceCapabilitiesKHR = (delegate* unmanaged< nint, ulong, VkSurfaceCapabilitiesKHR*, VkResult >)Get( "vkGetPhysicalDeviceSurfaceCapabilitiesKHR" );
		vkGetPhysicalDeviceSurfaceFormatsKHR = (delegate* unmanaged< nint, ulong, uint*, VkSurfaceFormatKHR*, VkResult >)Get( "vkGetPhysicalDeviceSurfaceFormatsKHR" );
		vkGetPhysicalDeviceSurfacePresentModesKHR = (delegate* unmanaged< nint, ulong, uint*, VkPresentMode*, VkResult >)Get( "vkGetPhysicalDeviceSurfacePresentModesKHR" );
		vkCreateWin32SurfaceKHR = (delegate* unmanaged< nint, VkWin32SurfaceCreateInfoKHR*, void*, ulong*, VkResult >)Get( "vkCreateWin32SurfaceKHR" );
		vkCreateXlibSurfaceKHR = (delegate* unmanaged< nint, VkXlibSurfaceCreateInfoKHR*, void*, ulong*, VkResult >)Get( "vkCreateXlibSurfaceKHR" );
		vkCreateWaylandSurfaceKHR = (delegate* unmanaged< nint, VkWaylandSurfaceCreateInfoKHR*, void*, ulong*, VkResult >)Get( "vkCreateWaylandSurfaceKHR" );
	}

	/// <summary>
	/// Destroy a device before its own entry points are loaded (when loading them failed).
	/// </summary>
	public void DestroyDevice( nint device )
	{
		fixed ( byte* name = "vkDestroyDevice"u8 )
		{
			var destroy = (delegate* unmanaged< nint, void*, void >)vkGetDeviceProcAddr( device, name );
			if ( destroy != null ) destroy( device, null );
		}
	}
}

/// <summary>
/// Device-level entry points, resolved through <c>vkGetDeviceProcAddr</c> so calls skip the loader's dispatch.
/// </summary>
internal sealed unsafe class VkDeviceApi
{
	public readonly nint Device;

	public readonly delegate* unmanaged< nint, void*, void > vkDestroyDevice;
	public readonly delegate* unmanaged< nint, uint, uint, nint*, void > vkGetDeviceQueue;
	public readonly delegate* unmanaged< nint, VkResult > vkDeviceWaitIdle;
	public readonly delegate* unmanaged< nint, uint, VkSubmitInfo2*, ulong, VkResult > vkQueueSubmit2;
	public readonly delegate* unmanaged< nint, VkResult > vkQueueWaitIdle;

	public readonly delegate* unmanaged< nint, VkMemoryAllocateInfo*, void*, ulong*, VkResult > vkAllocateMemory;
	public readonly delegate* unmanaged< nint, ulong, void*, void > vkFreeMemory;
	public readonly delegate* unmanaged< nint, ulong, ulong, ulong, uint, void**, VkResult > vkMapMemory;
	public readonly delegate* unmanaged< nint, ulong, void > vkUnmapMemory;
	public readonly delegate* unmanaged< nint, uint, VkMappedMemoryRange*, VkResult > vkFlushMappedMemoryRanges;
	public readonly delegate* unmanaged< nint, uint, VkMappedMemoryRange*, VkResult > vkInvalidateMappedMemoryRanges;

	public readonly delegate* unmanaged< nint, VkBufferCreateInfo*, void*, ulong*, VkResult > vkCreateBuffer;
	public readonly delegate* unmanaged< nint, ulong, void*, void > vkDestroyBuffer;
	public readonly delegate* unmanaged< nint, ulong, VkMemoryRequirements*, void > vkGetBufferMemoryRequirements;
	public readonly delegate* unmanaged< nint, ulong, ulong, ulong, VkResult > vkBindBufferMemory;
	public readonly delegate* unmanaged< nint, VkImageCreateInfo*, void*, ulong*, VkResult > vkCreateImage;
	public readonly delegate* unmanaged< nint, ulong, void*, void > vkDestroyImage;
	public readonly delegate* unmanaged< nint, ulong, VkMemoryRequirements*, void > vkGetImageMemoryRequirements;
	public readonly delegate* unmanaged< nint, ulong, ulong, ulong, VkResult > vkBindImageMemory;
	public readonly delegate* unmanaged< nint, VkImageViewCreateInfo*, void*, ulong*, VkResult > vkCreateImageView;
	public readonly delegate* unmanaged< nint, ulong, void*, void > vkDestroyImageView;
	public readonly delegate* unmanaged< nint, VkSamplerCreateInfo*, void*, ulong*, VkResult > vkCreateSampler;
	public readonly delegate* unmanaged< nint, ulong, void*, void > vkDestroySampler;

	public readonly delegate* unmanaged< nint, VkShaderModuleCreateInfo*, void*, ulong*, VkResult > vkCreateShaderModule;
	public readonly delegate* unmanaged< nint, ulong, void*, void > vkDestroyShaderModule;
	public readonly delegate* unmanaged< nint, VkPipelineCacheCreateInfo*, void*, ulong*, VkResult > vkCreatePipelineCache;
	public readonly delegate* unmanaged< nint, ulong, void*, void > vkDestroyPipelineCache;
	public readonly delegate* unmanaged< nint, ulong, nuint*, void*, VkResult > vkGetPipelineCacheData;
	public readonly delegate* unmanaged< nint, ulong, uint, VkGraphicsPipelineCreateInfo*, void*, ulong*, VkResult > vkCreateGraphicsPipelines;
	public readonly delegate* unmanaged< nint, ulong, uint, VkComputePipelineCreateInfo*, void*, ulong*, VkResult > vkCreateComputePipelines;
	public readonly delegate* unmanaged< nint, ulong, void*, void > vkDestroyPipeline;
	public readonly delegate* unmanaged< nint, VkPipelineLayoutCreateInfo*, void*, ulong*, VkResult > vkCreatePipelineLayout;
	public readonly delegate* unmanaged< nint, ulong, void*, void > vkDestroyPipelineLayout;

	public readonly delegate* unmanaged< nint, VkDescriptorSetLayoutCreateInfo*, void*, ulong*, VkResult > vkCreateDescriptorSetLayout;
	public readonly delegate* unmanaged< nint, ulong, void*, void > vkDestroyDescriptorSetLayout;
	public readonly delegate* unmanaged< nint, VkDescriptorPoolCreateInfo*, void*, ulong*, VkResult > vkCreateDescriptorPool;
	public readonly delegate* unmanaged< nint, ulong, void*, void > vkDestroyDescriptorPool;
	public readonly delegate* unmanaged< nint, ulong, uint, VkResult > vkResetDescriptorPool;
	public readonly delegate* unmanaged< nint, VkDescriptorSetAllocateInfo*, ulong*, VkResult > vkAllocateDescriptorSets;
	public readonly delegate* unmanaged< nint, uint, VkWriteDescriptorSet*, uint, void*, void > vkUpdateDescriptorSets;

	public readonly delegate* unmanaged< nint, VkCommandPoolCreateInfo*, void*, ulong*, VkResult > vkCreateCommandPool;
	public readonly delegate* unmanaged< nint, ulong, void*, void > vkDestroyCommandPool;
	public readonly delegate* unmanaged< nint, ulong, uint, VkResult > vkResetCommandPool;
	public readonly delegate* unmanaged< nint, VkCommandBufferAllocateInfo*, nint*, VkResult > vkAllocateCommandBuffers;
	public readonly delegate* unmanaged< nint, VkCommandBufferBeginInfo*, VkResult > vkBeginCommandBuffer;
	public readonly delegate* unmanaged< nint, VkResult > vkEndCommandBuffer;

	public readonly delegate* unmanaged< nint, VkSemaphoreCreateInfo*, void*, ulong*, VkResult > vkCreateSemaphore;
	public readonly delegate* unmanaged< nint, ulong, void*, void > vkDestroySemaphore;
	public readonly delegate* unmanaged< nint, VkSemaphoreWaitInfo*, ulong, VkResult > vkWaitSemaphores;
	public readonly delegate* unmanaged< nint, ulong, ulong*, VkResult > vkGetSemaphoreCounterValue;

	public readonly delegate* unmanaged< nint, VkQueryPoolCreateInfo*, void*, ulong*, VkResult > vkCreateQueryPool;
	public readonly delegate* unmanaged< nint, ulong, void*, void > vkDestroyQueryPool;
	public readonly delegate* unmanaged< nint, ulong, uint, uint, nuint, void*, ulong, uint, VkResult > vkGetQueryPoolResults;
	public readonly delegate* unmanaged< nint, ulong, uint, uint, void > vkResetQueryPool;

	// Swap chain entry points are null on devices made without presentation
	public readonly delegate* unmanaged< nint, VkSwapchainCreateInfoKHR*, void*, ulong*, VkResult > vkCreateSwapchainKHR;
	public readonly delegate* unmanaged< nint, ulong, void*, void > vkDestroySwapchainKHR;
	public readonly delegate* unmanaged< nint, ulong, uint*, ulong*, VkResult > vkGetSwapchainImagesKHR;
	public readonly delegate* unmanaged< nint, ulong, ulong, ulong, ulong, uint*, VkResult > vkAcquireNextImageKHR;
	public readonly delegate* unmanaged< nint, VkPresentInfoKHR*, VkResult > vkQueuePresentKHR;

	public readonly delegate* unmanaged< nint, VkRenderingInfo*, void > vkCmdBeginRendering;
	public readonly delegate* unmanaged< nint, void > vkCmdEndRendering;
	public readonly delegate* unmanaged< nint, VkDependencyInfo*, void > vkCmdPipelineBarrier2;
	public readonly delegate* unmanaged< nint, int, ulong, void > vkCmdBindPipeline;
	public readonly delegate* unmanaged< nint, uint, uint, VkViewport*, void > vkCmdSetViewport;
	public readonly delegate* unmanaged< nint, uint, uint, VkRect2D*, void > vkCmdSetScissor;
	public readonly delegate* unmanaged< nint, float, float, float, void > vkCmdSetDepthBias;
	public readonly delegate* unmanaged< nint, uint, uint, void > vkCmdSetStencilReference;
	public readonly delegate* unmanaged< nint, uint, uint, ulong*, ulong*, void > vkCmdBindVertexBuffers;
	public readonly delegate* unmanaged< nint, ulong, ulong, int, void > vkCmdBindIndexBuffer;
	public readonly delegate* unmanaged< nint, int, ulong, uint, uint, ulong*, uint, uint*, void > vkCmdBindDescriptorSets;
	public readonly delegate* unmanaged< nint, ulong, uint, uint, uint, void*, void > vkCmdPushConstants;
	public readonly delegate* unmanaged< nint, uint, uint, uint, uint, void > vkCmdDraw;
	public readonly delegate* unmanaged< nint, uint, uint, uint, int, uint, void > vkCmdDrawIndexed;
	public readonly delegate* unmanaged< nint, ulong, ulong, uint, uint, void > vkCmdDrawIndirect;
	public readonly delegate* unmanaged< nint, ulong, ulong, uint, uint, void > vkCmdDrawIndexedIndirect;
	public readonly delegate* unmanaged< nint, ulong, ulong, ulong, ulong, uint, uint, void > vkCmdDrawIndexedIndirectCount;
	public readonly delegate* unmanaged< nint, uint, uint, uint, void > vkCmdDispatch;
	public readonly delegate* unmanaged< nint, ulong, ulong, void > vkCmdDispatchIndirect;
	public readonly delegate* unmanaged< nint, ulong, ulong, uint, VkBufferCopy*, void > vkCmdCopyBuffer;
	public readonly delegate* unmanaged< nint, ulong, ulong, VkImageLayout, uint, VkBufferImageCopy*, void > vkCmdCopyBufferToImage;
	public readonly delegate* unmanaged< nint, ulong, VkImageLayout, ulong, uint, VkBufferImageCopy*, void > vkCmdCopyImageToBuffer;
	public readonly delegate* unmanaged< nint, ulong, VkImageLayout, ulong, VkImageLayout, uint, VkImageCopy*, void > vkCmdCopyImage;
	public readonly delegate* unmanaged< nint, ulong, VkImageLayout, ulong, VkImageLayout, uint, VkImageBlit*, int, void > vkCmdBlitImage;
	public readonly delegate* unmanaged< nint, ulong, VkImageLayout, ulong, VkImageLayout, uint, VkImageResolve*, void > vkCmdResolveImage;
	public readonly delegate* unmanaged< nint, ulong, VkImageLayout, VkClearValue*, uint, VkImageSubresourceRange*, void > vkCmdClearColorImage;
	public readonly delegate* unmanaged< nint, ulong, VkImageLayout, VkClearValue*, uint, VkImageSubresourceRange*, void > vkCmdClearDepthStencilImage;
	public readonly delegate* unmanaged< nint, ulong, ulong, ulong, uint, void > vkCmdFillBuffer;
	public readonly delegate* unmanaged< nint, ulong, uint, uint, void > vkCmdResetQueryPool;
	public readonly delegate* unmanaged< nint, ulong, ulong, uint, void > vkCmdWriteTimestamp2;
	public readonly delegate* unmanaged< nint, ulong, uint, uint, void > vkCmdBeginQuery;
	public readonly delegate* unmanaged< nint, ulong, uint, void > vkCmdEndQuery;

	/// <summary>VK_AMD_buffer_marker, when the device was made with it; null otherwise.</summary>
	public delegate* unmanaged< nint, uint, ulong, ulong, uint, void > vkCmdWriteBufferMarkerAMD;

	/// <summary>VK_EXT_mesh_shader, when the device was made with it; null otherwise.</summary>
	public delegate* unmanaged< nint, ulong, ulong, uint, uint, void > vkCmdDrawMeshTasksIndirectEXT;
	public delegate* unmanaged< nint, ulong, ulong, ulong, ulong, uint, uint, void > vkCmdDrawMeshTasksIndirectCountEXT;

	/// <summary>VK_EXT_device_fault, when the device was made with it; null otherwise.</summary>
	public delegate* unmanaged< nint, VkDeviceFaultCountsEXT*, VkDeviceFaultInfoEXT*, VkResult > vkGetDeviceFaultInfoEXT;

	/// <summary>VK_KHR_pipeline_executable_properties, when the device was made with it; null otherwise.</summary>
	public delegate* unmanaged< nint, VkPipelineInfoKHR*, uint*, VkPipelineExecutablePropertiesKHR*, VkResult > vkGetPipelineExecutablePropertiesKHR;
	public delegate* unmanaged< nint, VkPipelineExecutableInfoKHR*, uint*, VkPipelineExecutableStatisticKHR*, VkResult > vkGetPipelineExecutableStatisticsKHR;

	public VkDeviceApi( VkInstanceApi instance, nint device )
	{
		Device = device;

		nint Get( string name )
		{
			Span<byte> utf8 = stackalloc byte[128];
			var length = Encoding.UTF8.GetBytes( name, utf8 );
			utf8[length] = 0;
			fixed ( byte* p = utf8 ) return instance.vkGetDeviceProcAddr( device, p );
		}

		nint Need( string name )
		{
			var proc = Get( name );
			return proc != 0 ? proc : throw new GpuException( $"The Vulkan device has no {name}" );
		}

		vkDestroyDevice = (delegate* unmanaged< nint, void*, void >)Need( "vkDestroyDevice" );
		vkGetDeviceQueue = (delegate* unmanaged< nint, uint, uint, nint*, void >)Need( "vkGetDeviceQueue" );
		vkDeviceWaitIdle = (delegate* unmanaged< nint, VkResult >)Need( "vkDeviceWaitIdle" );
		vkQueueSubmit2 = (delegate* unmanaged< nint, uint, VkSubmitInfo2*, ulong, VkResult >)Need( "vkQueueSubmit2" );
		vkQueueWaitIdle = (delegate* unmanaged< nint, VkResult >)Need( "vkQueueWaitIdle" );

		vkAllocateMemory = (delegate* unmanaged< nint, VkMemoryAllocateInfo*, void*, ulong*, VkResult >)Need( "vkAllocateMemory" );
		vkFreeMemory = (delegate* unmanaged< nint, ulong, void*, void >)Need( "vkFreeMemory" );
		vkMapMemory = (delegate* unmanaged< nint, ulong, ulong, ulong, uint, void**, VkResult >)Need( "vkMapMemory" );
		vkUnmapMemory = (delegate* unmanaged< nint, ulong, void >)Need( "vkUnmapMemory" );
		vkFlushMappedMemoryRanges = (delegate* unmanaged< nint, uint, VkMappedMemoryRange*, VkResult >)Need( "vkFlushMappedMemoryRanges" );
		vkInvalidateMappedMemoryRanges = (delegate* unmanaged< nint, uint, VkMappedMemoryRange*, VkResult >)Need( "vkInvalidateMappedMemoryRanges" );

		vkCreateBuffer = (delegate* unmanaged< nint, VkBufferCreateInfo*, void*, ulong*, VkResult >)Need( "vkCreateBuffer" );
		vkDestroyBuffer = (delegate* unmanaged< nint, ulong, void*, void >)Need( "vkDestroyBuffer" );
		vkGetBufferMemoryRequirements = (delegate* unmanaged< nint, ulong, VkMemoryRequirements*, void >)Need( "vkGetBufferMemoryRequirements" );
		vkBindBufferMemory = (delegate* unmanaged< nint, ulong, ulong, ulong, VkResult >)Need( "vkBindBufferMemory" );
		vkCreateImage = (delegate* unmanaged< nint, VkImageCreateInfo*, void*, ulong*, VkResult >)Need( "vkCreateImage" );
		vkDestroyImage = (delegate* unmanaged< nint, ulong, void*, void >)Need( "vkDestroyImage" );
		vkGetImageMemoryRequirements = (delegate* unmanaged< nint, ulong, VkMemoryRequirements*, void >)Need( "vkGetImageMemoryRequirements" );
		vkBindImageMemory = (delegate* unmanaged< nint, ulong, ulong, ulong, VkResult >)Need( "vkBindImageMemory" );
		vkCreateImageView = (delegate* unmanaged< nint, VkImageViewCreateInfo*, void*, ulong*, VkResult >)Need( "vkCreateImageView" );
		vkDestroyImageView = (delegate* unmanaged< nint, ulong, void*, void >)Need( "vkDestroyImageView" );
		vkCreateSampler = (delegate* unmanaged< nint, VkSamplerCreateInfo*, void*, ulong*, VkResult >)Need( "vkCreateSampler" );
		vkDestroySampler = (delegate* unmanaged< nint, ulong, void*, void >)Need( "vkDestroySampler" );

		vkCreateShaderModule = (delegate* unmanaged< nint, VkShaderModuleCreateInfo*, void*, ulong*, VkResult >)Need( "vkCreateShaderModule" );
		vkDestroyShaderModule = (delegate* unmanaged< nint, ulong, void*, void >)Need( "vkDestroyShaderModule" );
		vkCreatePipelineCache = (delegate* unmanaged< nint, VkPipelineCacheCreateInfo*, void*, ulong*, VkResult >)Need( "vkCreatePipelineCache" );
		vkDestroyPipelineCache = (delegate* unmanaged< nint, ulong, void*, void >)Need( "vkDestroyPipelineCache" );
		vkGetPipelineCacheData = (delegate* unmanaged< nint, ulong, nuint*, void*, VkResult >)Need( "vkGetPipelineCacheData" );
		vkCreateGraphicsPipelines = (delegate* unmanaged< nint, ulong, uint, VkGraphicsPipelineCreateInfo*, void*, ulong*, VkResult >)Need( "vkCreateGraphicsPipelines" );
		vkCreateComputePipelines = (delegate* unmanaged< nint, ulong, uint, VkComputePipelineCreateInfo*, void*, ulong*, VkResult >)Need( "vkCreateComputePipelines" );
		vkDestroyPipeline = (delegate* unmanaged< nint, ulong, void*, void >)Need( "vkDestroyPipeline" );
		vkCreatePipelineLayout = (delegate* unmanaged< nint, VkPipelineLayoutCreateInfo*, void*, ulong*, VkResult >)Need( "vkCreatePipelineLayout" );
		vkDestroyPipelineLayout = (delegate* unmanaged< nint, ulong, void*, void >)Need( "vkDestroyPipelineLayout" );

		vkCreateDescriptorSetLayout = (delegate* unmanaged< nint, VkDescriptorSetLayoutCreateInfo*, void*, ulong*, VkResult >)Need( "vkCreateDescriptorSetLayout" );
		vkDestroyDescriptorSetLayout = (delegate* unmanaged< nint, ulong, void*, void >)Need( "vkDestroyDescriptorSetLayout" );
		vkCreateDescriptorPool = (delegate* unmanaged< nint, VkDescriptorPoolCreateInfo*, void*, ulong*, VkResult >)Need( "vkCreateDescriptorPool" );
		vkDestroyDescriptorPool = (delegate* unmanaged< nint, ulong, void*, void >)Need( "vkDestroyDescriptorPool" );
		vkResetDescriptorPool = (delegate* unmanaged< nint, ulong, uint, VkResult >)Need( "vkResetDescriptorPool" );
		vkAllocateDescriptorSets = (delegate* unmanaged< nint, VkDescriptorSetAllocateInfo*, ulong*, VkResult >)Need( "vkAllocateDescriptorSets" );
		vkUpdateDescriptorSets = (delegate* unmanaged< nint, uint, VkWriteDescriptorSet*, uint, void*, void >)Need( "vkUpdateDescriptorSets" );

		vkCreateCommandPool = (delegate* unmanaged< nint, VkCommandPoolCreateInfo*, void*, ulong*, VkResult >)Need( "vkCreateCommandPool" );
		vkDestroyCommandPool = (delegate* unmanaged< nint, ulong, void*, void >)Need( "vkDestroyCommandPool" );
		vkResetCommandPool = (delegate* unmanaged< nint, ulong, uint, VkResult >)Need( "vkResetCommandPool" );
		vkAllocateCommandBuffers = (delegate* unmanaged< nint, VkCommandBufferAllocateInfo*, nint*, VkResult >)Need( "vkAllocateCommandBuffers" );
		vkBeginCommandBuffer = (delegate* unmanaged< nint, VkCommandBufferBeginInfo*, VkResult >)Need( "vkBeginCommandBuffer" );
		vkEndCommandBuffer = (delegate* unmanaged< nint, VkResult >)Need( "vkEndCommandBuffer" );

		vkCreateSemaphore = (delegate* unmanaged< nint, VkSemaphoreCreateInfo*, void*, ulong*, VkResult >)Need( "vkCreateSemaphore" );
		vkDestroySemaphore = (delegate* unmanaged< nint, ulong, void*, void >)Need( "vkDestroySemaphore" );
		vkWaitSemaphores = (delegate* unmanaged< nint, VkSemaphoreWaitInfo*, ulong, VkResult >)Need( "vkWaitSemaphores" );
		vkGetSemaphoreCounterValue = (delegate* unmanaged< nint, ulong, ulong*, VkResult >)Need( "vkGetSemaphoreCounterValue" );

		vkCreateQueryPool = (delegate* unmanaged< nint, VkQueryPoolCreateInfo*, void*, ulong*, VkResult >)Need( "vkCreateQueryPool" );
		vkDestroyQueryPool = (delegate* unmanaged< nint, ulong, void*, void >)Need( "vkDestroyQueryPool" );
		vkGetQueryPoolResults = (delegate* unmanaged< nint, ulong, uint, uint, nuint, void*, ulong, uint, VkResult >)Need( "vkGetQueryPoolResults" );
		vkResetQueryPool = (delegate* unmanaged< nint, ulong, uint, uint, void >)Need( "vkResetQueryPool" );

		vkCreateSwapchainKHR = (delegate* unmanaged< nint, VkSwapchainCreateInfoKHR*, void*, ulong*, VkResult >)Get( "vkCreateSwapchainKHR" );
		vkDestroySwapchainKHR = (delegate* unmanaged< nint, ulong, void*, void >)Get( "vkDestroySwapchainKHR" );
		vkGetSwapchainImagesKHR = (delegate* unmanaged< nint, ulong, uint*, ulong*, VkResult >)Get( "vkGetSwapchainImagesKHR" );
		vkAcquireNextImageKHR = (delegate* unmanaged< nint, ulong, ulong, ulong, ulong, uint*, VkResult >)Get( "vkAcquireNextImageKHR" );
		vkQueuePresentKHR = (delegate* unmanaged< nint, VkPresentInfoKHR*, VkResult >)Get( "vkQueuePresentKHR" );

		vkCmdBeginRendering = (delegate* unmanaged< nint, VkRenderingInfo*, void >)Need( "vkCmdBeginRendering" );
		vkCmdEndRendering = (delegate* unmanaged< nint, void >)Need( "vkCmdEndRendering" );
		vkCmdPipelineBarrier2 = (delegate* unmanaged< nint, VkDependencyInfo*, void >)Need( "vkCmdPipelineBarrier2" );
		vkCmdBindPipeline = (delegate* unmanaged< nint, int, ulong, void >)Need( "vkCmdBindPipeline" );
		vkCmdSetViewport = (delegate* unmanaged< nint, uint, uint, VkViewport*, void >)Need( "vkCmdSetViewport" );
		vkCmdSetScissor = (delegate* unmanaged< nint, uint, uint, VkRect2D*, void >)Need( "vkCmdSetScissor" );
		vkCmdSetDepthBias = (delegate* unmanaged< nint, float, float, float, void >)Need( "vkCmdSetDepthBias" );
		vkCmdSetStencilReference = (delegate* unmanaged< nint, uint, uint, void >)Need( "vkCmdSetStencilReference" );
		vkCmdBindVertexBuffers = (delegate* unmanaged< nint, uint, uint, ulong*, ulong*, void >)Need( "vkCmdBindVertexBuffers" );
		vkCmdBindIndexBuffer = (delegate* unmanaged< nint, ulong, ulong, int, void >)Need( "vkCmdBindIndexBuffer" );
		vkCmdBindDescriptorSets = (delegate* unmanaged< nint, int, ulong, uint, uint, ulong*, uint, uint*, void >)Need( "vkCmdBindDescriptorSets" );
		vkCmdPushConstants = (delegate* unmanaged< nint, ulong, uint, uint, uint, void*, void >)Need( "vkCmdPushConstants" );
		vkCmdDraw = (delegate* unmanaged< nint, uint, uint, uint, uint, void >)Need( "vkCmdDraw" );
		vkCmdDrawIndexed = (delegate* unmanaged< nint, uint, uint, uint, int, uint, void >)Need( "vkCmdDrawIndexed" );
		vkCmdDrawIndirect = (delegate* unmanaged< nint, ulong, ulong, uint, uint, void >)Need( "vkCmdDrawIndirect" );
		vkCmdDrawIndexedIndirect = (delegate* unmanaged< nint, ulong, ulong, uint, uint, void >)Need( "vkCmdDrawIndexedIndirect" );
		vkCmdDrawIndexedIndirectCount = (delegate* unmanaged< nint, ulong, ulong, ulong, ulong, uint, uint, void >)Need( "vkCmdDrawIndexedIndirectCount" );
		vkCmdDispatch = (delegate* unmanaged< nint, uint, uint, uint, void >)Need( "vkCmdDispatch" );
		vkCmdDispatchIndirect = (delegate* unmanaged< nint, ulong, ulong, void >)Need( "vkCmdDispatchIndirect" );
		vkCmdCopyBuffer = (delegate* unmanaged< nint, ulong, ulong, uint, VkBufferCopy*, void >)Need( "vkCmdCopyBuffer" );
		vkCmdCopyBufferToImage = (delegate* unmanaged< nint, ulong, ulong, VkImageLayout, uint, VkBufferImageCopy*, void >)Need( "vkCmdCopyBufferToImage" );
		vkCmdCopyImageToBuffer = (delegate* unmanaged< nint, ulong, VkImageLayout, ulong, uint, VkBufferImageCopy*, void >)Need( "vkCmdCopyImageToBuffer" );
		vkCmdCopyImage = (delegate* unmanaged< nint, ulong, VkImageLayout, ulong, VkImageLayout, uint, VkImageCopy*, void >)Need( "vkCmdCopyImage" );
		vkCmdBlitImage = (delegate* unmanaged< nint, ulong, VkImageLayout, ulong, VkImageLayout, uint, VkImageBlit*, int, void >)Need( "vkCmdBlitImage" );
		vkCmdResolveImage = (delegate* unmanaged< nint, ulong, VkImageLayout, ulong, VkImageLayout, uint, VkImageResolve*, void >)Need( "vkCmdResolveImage" );
		vkCmdClearColorImage = (delegate* unmanaged< nint, ulong, VkImageLayout, VkClearValue*, uint, VkImageSubresourceRange*, void >)Need( "vkCmdClearColorImage" );
		vkCmdClearDepthStencilImage = (delegate* unmanaged< nint, ulong, VkImageLayout, VkClearValue*, uint, VkImageSubresourceRange*, void >)Need( "vkCmdClearDepthStencilImage" );
		vkCmdFillBuffer = (delegate* unmanaged< nint, ulong, ulong, ulong, uint, void >)Need( "vkCmdFillBuffer" );
		vkCmdResetQueryPool = (delegate* unmanaged< nint, ulong, uint, uint, void >)Need( "vkCmdResetQueryPool" );
		vkCmdWriteTimestamp2 = (delegate* unmanaged< nint, ulong, ulong, uint, void >)Need( "vkCmdWriteTimestamp2" );
		vkCmdBeginQuery = (delegate* unmanaged< nint, ulong, uint, uint, void >)Need( "vkCmdBeginQuery" );
		vkCmdEndQuery = (delegate* unmanaged< nint, ulong, uint, void >)Need( "vkCmdEndQuery" );
		vkCmdWriteBufferMarkerAMD = (delegate* unmanaged< nint, uint, ulong, ulong, uint, void >)Get( "vkCmdWriteBufferMarkerAMD" );
		vkCmdDrawMeshTasksIndirectEXT = (delegate* unmanaged< nint, ulong, ulong, uint, uint, void >)Get( "vkCmdDrawMeshTasksIndirectEXT" );
		vkCmdDrawMeshTasksIndirectCountEXT = (delegate* unmanaged< nint, ulong, ulong, ulong, ulong, uint, uint, void >)Get( "vkCmdDrawMeshTasksIndirectCountEXT" );
		vkGetDeviceFaultInfoEXT = (delegate* unmanaged< nint, VkDeviceFaultCountsEXT*, VkDeviceFaultInfoEXT*, VkResult >)Get( "vkGetDeviceFaultInfoEXT" );
		vkGetPipelineExecutablePropertiesKHR = (delegate* unmanaged< nint, VkPipelineInfoKHR*, uint*, VkPipelineExecutablePropertiesKHR*, VkResult >)Get( "vkGetPipelineExecutablePropertiesKHR" );
		vkGetPipelineExecutableStatisticsKHR = (delegate* unmanaged< nint, VkPipelineExecutableInfoKHR*, uint*, VkPipelineExecutableStatisticKHR*, VkResult >)Get( "vkGetPipelineExecutableStatisticsKHR" );
	}
}
