using Basalt.Gpu.Memory;
using Basalt.Gpu.Vulkan;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Basalt.Gpu;

/// <summary>
/// A Vulkan device: the queues work is submitted to, and owner of every resource made on it. Resources are named by
/// handles and destroyed through <see cref="Destroy(BufferHandle)"/> and friends, which wait for the GPU to finish with
/// them. See docs/managed/basalt-gpu.md.
/// </summary>
public sealed unsafe partial class Device : IDisposable
{
	internal readonly VkInstanceApi Instance;
	internal readonly VkDeviceApi Vk;
	internal readonly nint PhysicalDevice;
	internal readonly MemoryAllocator Allocator;
	internal readonly VkPhysicalDeviceLimits Limits;

	readonly ulong messenger;
	readonly GCHandle self;
	readonly Action<DeviceMessage> message;
	readonly HashSet<string> deviceExtensions;
	int validationErrors;
	bool disposed;

	/// <summary>The GPU it runs on.</summary>
	public AdapterInfo Adapter { get; }

	/// <summary>What it can do beyond the baseline.</summary>
	public DeviceCapabilities Capabilities { get; }

	/// <summary>Frames the CPU may record ahead of the GPU.</summary>
	public int FramesInFlight { get; }

	/// <summary>Errors the validation layer has reported. Tests check it stays 0.</summary>
	public int ValidationErrors => validationErrors;

	/// <summary>Whether the validation layer is on.</summary>
	public bool ValidationEnabled { get; }

	/// <summary>Whether a Vulkan loader is installed, without creating anything.</summary>
	public static bool IsSupported => VkLoader.IsAvailable;

	Device( DeviceOptions options )
	{
		VkGlobal.Load();
		message = options.Message;
		FramesInFlight = Math.Clamp( options.FramesInFlight, 1, 4 );
		self = GCHandle.Alloc( this, GCHandleType.Weak );

		uint loaderVersion = Vulkan.Vk.ApiVersion13;
		if ( VkGlobal.vkEnumerateInstanceVersion != null ) VkGlobal.vkEnumerateInstanceVersion( &loaderVersion );
		if ( loaderVersion < Vulkan.Vk.ApiVersion13 ) throw new GpuException( $"The Vulkan loader is {Vulkan.Vk.ApiMajor( loaderVersion )}.{Vulkan.Vk.ApiMinor( loaderVersion )}; Basalt.Gpu needs 1.3" );

		// Instance
		var available = InstanceExtensions();
		var extensions = new List<string>();
		var debugUtils = available.Contains( "VK_EXT_debug_utils" );
		if ( debugUtils ) extensions.Add( "VK_EXT_debug_utils" );
		if ( options.Presentation )
		{
			foreach ( var name in SurfaceSource.InstanceExtensions )
			{
				if ( available.Contains( name ) ) extensions.Add( name );
			}
		}

		var layers = new List<string>();
		if ( options.Validation && InstanceLayers().Contains( "VK_LAYER_KHRONOS_validation" ) )
		{
			layers.Add( "VK_LAYER_KHRONOS_validation" );
			ValidationEnabled = true;
		}

		using var strings = new NativeStrings();
		var appName = strings.Add( options.ApplicationName ?? "Basalt" );
		var engineName = strings.Add( options.EngineName ?? "Basalt" );
		var app = new VkApplicationInfo
		{
			sType = VkStructureType.ApplicationInfo,
			pApplicationName = appName,
			pEngineName = engineName,
			engineVersion = 1,
			apiVersion = Vulkan.Vk.ApiVersion13,
		};

		var messengerInfo = new VkDebugUtilsMessengerCreateInfoEXT
		{
			sType = VkStructureType.DebugUtilsMessengerCreateInfoExt,
			messageSeverity = Vulkan.Vk.SeverityWarning | Vulkan.Vk.SeverityError,
			messageType = Vulkan.Vk.MessageGeneral | Vulkan.Vk.MessageValidation | Vulkan.Vk.MessagePerformance,
			pfnUserCallback = &DebugCallback,
			pUserData = (void*)GCHandle.ToIntPtr( self ),
		};

		var syncValidation = 4; // VK_VALIDATION_FEATURE_ENABLE_SYNCHRONIZATION_VALIDATION_EXT
		var validationFeatures = new VkValidationFeaturesEXT
		{
			sType = VkStructureType.ValidationFeaturesExt,
			enabledValidationFeatureCount = 1,
			pEnabledValidationFeatures = &syncValidation,
		};

		void* instanceNext = null;
		if ( debugUtils )
		{
			messengerInfo.pNext = instanceNext;
			instanceNext = &messengerInfo;
		}

		if ( ValidationEnabled && options.SynchronizationValidation )
		{
			validationFeatures.pNext = instanceNext;
			instanceNext = &validationFeatures;
		}

		var instanceInfo = new VkInstanceCreateInfo
		{
			sType = VkStructureType.InstanceCreateInfo,
			pNext = instanceNext,
			pApplicationInfo = &app,
			enabledLayerCount = (uint)layers.Count,
			ppEnabledLayerNames = strings.Array( layers ),
			enabledExtensionCount = (uint)extensions.Count,
			ppEnabledExtensionNames = strings.Array( extensions ),
		};

		nint instance;
		VkGlobal.vkCreateInstance( &instanceInfo, null, &instance ).Check( "vkCreateInstance" );
		Instance = new VkInstanceApi( instance );

		if ( debugUtils && Instance.vkCreateDebugUtilsMessengerEXT != null )
		{
			messengerInfo.pNext = null;
			ulong handle;
			Instance.vkCreateDebugUtilsMessengerEXT( instance, &messengerInfo, null, &handle ).Check( "vkCreateDebugUtilsMessengerEXT" );
			messenger = handle;
		}

		try
		{
			PhysicalDevice = ChooseAdapter( options.PreferredAdapter, out var properties );
			Limits = properties.limits;
			Adapter = new AdapterInfo
			{
				Name = Utf8( properties.deviceName, 256 ),
				VendorId = properties.vendorID,
				DeviceId = properties.deviceID,
				Discrete = properties.deviceType == 2,
				Integrated = properties.deviceType == 1,
				Software = properties.deviceType == 4,
				ApiVersion = $"{Vulkan.Vk.ApiMajor( properties.apiVersion )}.{Vulkan.Vk.ApiMinor( properties.apiVersion )}.{Vulkan.Vk.ApiPatch( properties.apiVersion )}",
			};

			if ( Limits.maxBoundDescriptorSets < ShaderAbi.SetCount )
				throw new GpuException( $"{Adapter.Name} binds {Limits.maxBoundDescriptorSets} descriptor sets; s&box's shaders use {ShaderAbi.SetCount}" );

			deviceExtensions = DeviceExtensions( PhysicalDevice );
			Vk = CreateLogicalDevice( options, out var features, out var computeFamily );
			graphicsQueue = new DeviceQueue( this, Vk, queueFamilies[0], QueueType.Graphics );
			computeQueue = computeFamily ? new DeviceQueue( this, Vk, queueFamilies[1], QueueType.Compute ) : graphicsQueue;

			var indexing = new VkPhysicalDeviceDescriptorIndexingProperties { sType = VkStructureType.PhysicalDeviceDescriptorIndexingProperties };
			var properties2 = new VkPhysicalDeviceProperties2 { sType = VkStructureType.PhysicalDeviceProperties2, pNext = &indexing };
			Instance.vkGetPhysicalDeviceProperties2( PhysicalDevice, &properties2 );

			VkPhysicalDeviceMemoryProperties memory;
			Instance.vkGetPhysicalDeviceMemoryProperties( PhysicalDevice, &memory );
			Allocator = new MemoryAllocator( Vk, memory, Adapter.Integrated );

			var bindlessTextures = (int)Math.Min( 65536u, Math.Min( indexing.maxDescriptorSetUpdateAfterBindSampledImages, indexing.maxPerStageDescriptorUpdateAfterBindSampledImages ) / 2 * 2 );
			var bindlessSamplers = (int)Math.Min( 2048u, Math.Min( indexing.maxDescriptorSetUpdateAfterBindSamplers, indexing.maxPerStageDescriptorUpdateAfterBindSamplers ) );
			var bindlessStorage = (int)Math.Min( 4096u, Math.Min( indexing.maxDescriptorSetUpdateAfterBindStorageImages, indexing.maxPerStageDescriptorUpdateAfterBindStorageImages ) );

			Capabilities = new DeviceCapabilities
			{
				AsyncCompute = computeFamily,
				DrawIndirectCount = features.drawIndirectCount,
				TextureCompressionBC = features.bc,
				MaxAnisotropy = features.anisotropy ? Limits.maxSamplerAnisotropy : 1,
				Timestamps = Limits.timestampComputeAndGraphics != 0,
				PreciseOcclusion = features.preciseOcclusion,
				PipelineStatistics = features.pipelineStatistics,
				TimestampPeriod = Limits.timestampPeriod,
				MaxTextureSize = (int)Limits.maxImageDimension2D,
				ColorSampleCounts = (int)Limits.framebufferColorSampleCounts,
				UniformAlignment = (int)Limits.minUniformBufferOffsetAlignment,
				MaxDynamicUniformBuffers = (int)Limits.maxDescriptorSetUniformBuffersDynamic,
				StorageAlignment = (int)Limits.minStorageBufferOffsetAlignment,
				BindlessTextures = bindlessTextures,
				BindlessSamplers = bindlessSamplers,
				BindlessStorageTextures = bindlessStorage,
			};

			Bindless = new BindlessTable( this, bindlessTextures, bindlessSamplers, bindlessStorage, features.storageBufferUpdateAfterBind );
			CreatePipelineCache( options.PipelineCacheData );
			CreateFrames();
		}
		catch
		{
			if ( Vk is not null )
			{
				Vk.vkDeviceWaitIdle( Vk.Device );
				graphicsQueue?.Dispose();
				if ( computeQueue != graphicsQueue ) computeQueue?.Dispose();
				Vk.vkDestroyDevice( Vk.Device, null );
			}

			DestroyInstance();
			throw;
		}
	}

	/// <summary>
	/// Create a device. Throws <see cref="GpuException"/> if there's no Vulkan 1.3 GPU.
	/// </summary>
	public static Device Create( DeviceOptions options = null ) => new( options ?? new DeviceOptions() );

	internal readonly struct Features
	{
		public bool drawIndirectCount { get; init; }
		public bool bc { get; init; }
		public bool anisotropy { get; init; }
		public bool preciseOcclusion { get; init; }
		public bool pipelineStatistics { get; init; }
		public bool storageBufferUpdateAfterBind { get; init; }
	}

	HashSet<string> InstanceExtensions()
	{
		uint count;
		VkGlobal.vkEnumerateInstanceExtensionProperties( null, &count, null ).Check( "vkEnumerateInstanceExtensionProperties" );
		var props = new VkExtensionProperties[count];
		fixed ( VkExtensionProperties* p = props ) VkGlobal.vkEnumerateInstanceExtensionProperties( null, &count, p ).Check( "vkEnumerateInstanceExtensionProperties" );

		var names = new HashSet<string>();
		for ( int i = 0; i < count; i++ )
		{
			fixed ( byte* name = props[i].extensionName ) names.Add( Utf8( name, 256 ) );
		}

		return names;
	}

	static HashSet<string> InstanceLayers()
	{
		uint count;
		VkGlobal.vkEnumerateInstanceLayerProperties( &count, null ).Check( "vkEnumerateInstanceLayerProperties" );
		var props = new VkLayerProperties[count];
		fixed ( VkLayerProperties* p = props ) VkGlobal.vkEnumerateInstanceLayerProperties( &count, p ).Check( "vkEnumerateInstanceLayerProperties" );

		var names = new HashSet<string>();
		for ( int i = 0; i < count; i++ )
		{
			fixed ( byte* name = props[i].layerName ) names.Add( Utf8( name, 256 ) );
		}

		return names;
	}

	HashSet<string> DeviceExtensions( nint physicalDevice )
	{
		uint count;
		Instance.vkEnumerateDeviceExtensionProperties( physicalDevice, null, &count, null ).Check( "vkEnumerateDeviceExtensionProperties" );
		var props = new VkExtensionProperties[count];
		fixed ( VkExtensionProperties* p = props ) Instance.vkEnumerateDeviceExtensionProperties( physicalDevice, null, &count, p ).Check( "vkEnumerateDeviceExtensionProperties" );

		var names = new HashSet<string>();
		for ( int i = 0; i < count; i++ )
		{
			fixed ( byte* name = props[i].extensionName ) names.Add( Utf8( name, 256 ) );
		}

		return names;
	}

	nint ChooseAdapter( string preferred, out VkPhysicalDeviceProperties chosen )
	{
		uint count;
		Instance.vkEnumeratePhysicalDevices( Instance.Instance, &count, null ).Check( "vkEnumeratePhysicalDevices" );
		var devices = new nint[count];
		fixed ( nint* p = devices ) Instance.vkEnumeratePhysicalDevices( Instance.Instance, &count, p ).Check( "vkEnumeratePhysicalDevices" );

		nint best = 0;
		var bestScore = int.MinValue;
		chosen = default;
		var rejected = new StringBuilder();

		foreach ( var device in devices )
		{
			VkPhysicalDeviceProperties properties;
			Instance.vkGetPhysicalDeviceProperties( device, &properties );
			var name = Utf8( properties.deviceName, 256 );

			if ( properties.apiVersion < Vulkan.Vk.ApiVersion13 || !HasRequiredFeatures( device ) )
			{
				rejected.Append( $" {name}: no Vulkan 1.3 with dynamic rendering, synchronization2, timeline semaphores and descriptor indexing." );
				continue;
			}

			var score = properties.deviceType switch { 2 => 300, 1 => 200, 3 => 100, _ => 0 };
			if ( preferred is not null && name.Contains( preferred, StringComparison.OrdinalIgnoreCase ) ) score += 1000;
			if ( score > bestScore )
			{
				bestScore = score;
				best = device;
				chosen = properties;
			}
		}

		if ( best == 0 ) throw new GpuException( $"No GPU supports what Basalt.Gpu needs.{rejected}" );
		return best;
	}

	bool HasRequiredFeatures( nint device )
	{
		var v13 = new VkPhysicalDeviceVulkan13Features { sType = VkStructureType.PhysicalDeviceVulkan13Features };
		var v12 = new VkPhysicalDeviceVulkan12Features { sType = VkStructureType.PhysicalDeviceVulkan12Features, pNext = &v13 };
		var features = new VkPhysicalDeviceFeatures2 { sType = VkStructureType.PhysicalDeviceFeatures2, pNext = &v12 };
		Instance.vkGetPhysicalDeviceFeatures2( device, &features );

		return v13.values[VkPhysicalDeviceVulkan13Features.DynamicRendering] != 0
			&& v13.values[VkPhysicalDeviceVulkan13Features.Synchronization2] != 0
			&& v12.values[VkPhysicalDeviceVulkan12Features.TimelineSemaphore] != 0
			&& v12.values[VkPhysicalDeviceVulkan12Features.RuntimeDescriptorArray] != 0
			&& v12.values[VkPhysicalDeviceVulkan12Features.DescriptorBindingPartiallyBound] != 0
			&& v12.values[VkPhysicalDeviceVulkan12Features.DescriptorBindingSampledImageUpdateAfterBind] != 0
			&& v12.values[VkPhysicalDeviceVulkan12Features.DescriptorBindingStorageImageUpdateAfterBind] != 0
			&& v12.values[VkPhysicalDeviceVulkan12Features.DescriptorBindingUpdateUnusedWhilePending] != 0;
	}

	VkDeviceApi CreateLogicalDevice( DeviceOptions options, out Features enabled, out bool asyncCompute )
	{
		// Queues: a graphics family that also computes, and a compute-only family for async compute if there is one
		uint familyCount;
		Instance.vkGetPhysicalDeviceQueueFamilyProperties( PhysicalDevice, &familyCount, null );
		var families = new VkQueueFamilyProperties[familyCount];
		fixed ( VkQueueFamilyProperties* p = families ) Instance.vkGetPhysicalDeviceQueueFamilyProperties( PhysicalDevice, &familyCount, p );

		int graphics = -1, compute = -1;
		for ( int i = 0; i < familyCount; i++ )
		{
			var flags = families[i].queueFlags;
			var both = Vulkan.Vk.QueueGraphics | Vulkan.Vk.QueueCompute;
			if ( graphics < 0 && (flags & both) == both ) graphics = i;
			if ( compute < 0 && (flags & Vulkan.Vk.QueueCompute) != 0 && (flags & Vulkan.Vk.QueueGraphics) == 0 ) compute = i;
		}

		if ( graphics < 0 ) throw new GpuException( $"{Adapter.Name} has no graphics queue" );
		asyncCompute = compute >= 0;

		var priority = 1.0f;
		var queueInfos = stackalloc VkDeviceQueueCreateInfo[2];
		queueInfos[0] = new VkDeviceQueueCreateInfo { sType = VkStructureType.DeviceQueueCreateInfo, queueFamilyIndex = (uint)graphics, queueCount = 1, pQueuePriorities = &priority };
		queueInfos[1] = new VkDeviceQueueCreateInfo { sType = VkStructureType.DeviceQueueCreateInfo, queueFamilyIndex = (uint)compute, queueCount = 1, pQueuePriorities = &priority };

		// Features: what's required, and what's wanted where it's there
		var s13 = new VkPhysicalDeviceVulkan13Features { sType = VkStructureType.PhysicalDeviceVulkan13Features };
		var s12 = new VkPhysicalDeviceVulkan12Features { sType = VkStructureType.PhysicalDeviceVulkan12Features, pNext = &s13 };
		var s11 = new VkPhysicalDeviceVulkan11Features { sType = VkStructureType.PhysicalDeviceVulkan11Features, pNext = &s12 };
		var supported = new VkPhysicalDeviceFeatures2 { sType = VkStructureType.PhysicalDeviceFeatures2, pNext = &s11 };
		Instance.vkGetPhysicalDeviceFeatures2( PhysicalDevice, &supported );

		var e13 = new VkPhysicalDeviceVulkan13Features { sType = VkStructureType.PhysicalDeviceVulkan13Features };
		var e12 = new VkPhysicalDeviceVulkan12Features { sType = VkStructureType.PhysicalDeviceVulkan12Features, pNext = &e13 };
		var e11 = new VkPhysicalDeviceVulkan11Features { sType = VkStructureType.PhysicalDeviceVulkan11Features, pNext = &e12 };
		var features = new VkPhysicalDeviceFeatures2 { sType = VkStructureType.PhysicalDeviceFeatures2, pNext = &e11 };

		ReadOnlySpan<VkFeature> core =
		[
			VkFeature.FullDrawIndexUint32, VkFeature.ImageCubeArray, VkFeature.IndependentBlend, VkFeature.SampleRateShading,
			VkFeature.MultiDrawIndirect, VkFeature.DrawIndirectFirstInstance, VkFeature.DepthClamp, VkFeature.DepthBiasClamp,
			VkFeature.FillModeNonSolid, VkFeature.SamplerAnisotropy, VkFeature.TextureCompressionBc, VkFeature.OcclusionQueryPrecise, VkFeature.PipelineStatisticsQuery, VkFeature.FragmentStoresAndAtomics,
			VkFeature.ShaderImageGatherExtended, VkFeature.ShaderStorageImageExtendedFormats, VkFeature.ShaderStorageImageReadWithoutFormat,
			VkFeature.ShaderStorageImageWriteWithoutFormat, VkFeature.ShaderUniformBufferArrayDynamicIndexing, VkFeature.ShaderSampledImageArrayDynamicIndexing,
			VkFeature.ShaderStorageBufferArrayDynamicIndexing, VkFeature.ShaderStorageImageArrayDynamicIndexing, VkFeature.ShaderInt64, VkFeature.ShaderInt16,
		];

		foreach ( var f in core ) features.features.values[(int)f] = supported.features.values[(int)f];

		e11.values[VkPhysicalDeviceVulkan11Features.ShaderDrawParameters] = s11.values[VkPhysicalDeviceVulkan11Features.ShaderDrawParameters];

		ReadOnlySpan<int> v12 =
		[
			VkPhysicalDeviceVulkan12Features.DrawIndirectCount, VkPhysicalDeviceVulkan12Features.ShaderFloat16, VkPhysicalDeviceVulkan12Features.ShaderInt8,
			VkPhysicalDeviceVulkan12Features.DescriptorIndexing, VkPhysicalDeviceVulkan12Features.ShaderSampledImageArrayNonUniformIndexing,
			VkPhysicalDeviceVulkan12Features.ShaderStorageBufferArrayNonUniformIndexing, VkPhysicalDeviceVulkan12Features.DescriptorBindingSampledImageUpdateAfterBind,
			VkPhysicalDeviceVulkan12Features.DescriptorBindingStorageImageUpdateAfterBind, VkPhysicalDeviceVulkan12Features.DescriptorBindingStorageBufferUpdateAfterBind,
			VkPhysicalDeviceVulkan12Features.DescriptorBindingUpdateUnusedWhilePending, VkPhysicalDeviceVulkan12Features.DescriptorBindingPartiallyBound,
			VkPhysicalDeviceVulkan12Features.RuntimeDescriptorArray, VkPhysicalDeviceVulkan12Features.ScalarBlockLayout,
			VkPhysicalDeviceVulkan12Features.UniformBufferStandardLayout, VkPhysicalDeviceVulkan12Features.SeparateDepthStencilLayouts,
			VkPhysicalDeviceVulkan12Features.HostQueryReset, VkPhysicalDeviceVulkan12Features.TimelineSemaphore, VkPhysicalDeviceVulkan12Features.VulkanMemoryModel,
			VkPhysicalDeviceVulkan12Features.VulkanMemoryModelDeviceScope,
		];

		foreach ( var f in v12 ) e12.values[f] = s12.values[f];

		ReadOnlySpan<int> v13 =
		[
			VkPhysicalDeviceVulkan13Features.ShaderDemoteToHelperInvocation, VkPhysicalDeviceVulkan13Features.SubgroupSizeControl,
			VkPhysicalDeviceVulkan13Features.Synchronization2, VkPhysicalDeviceVulkan13Features.DynamicRendering, VkPhysicalDeviceVulkan13Features.Maintenance4,
		];

		foreach ( var f in v13 ) e13.values[f] = s13.values[f];

		enabled = new Features
		{
			drawIndirectCount = e12.values[VkPhysicalDeviceVulkan12Features.DrawIndirectCount] != 0,
			bc = features.features.values[(int)VkFeature.TextureCompressionBc] != 0,
			anisotropy = features.features.values[(int)VkFeature.SamplerAnisotropy] != 0,
			preciseOcclusion = features.features.values[(int)VkFeature.OcclusionQueryPrecise] != 0,
			pipelineStatistics = features.features.values[(int)VkFeature.PipelineStatisticsQuery] != 0,
			storageBufferUpdateAfterBind = e12.values[VkPhysicalDeviceVulkan12Features.DescriptorBindingStorageBufferUpdateAfterBind] != 0,
		};

		var extensions = new List<string>();
		if ( options.Presentation && deviceExtensions.Contains( "VK_KHR_swapchain" ) ) extensions.Add( "VK_KHR_swapchain" );

		using var strings = new NativeStrings();
		var info = new VkDeviceCreateInfo
		{
			sType = VkStructureType.DeviceCreateInfo,
			pNext = &features,
			queueCreateInfoCount = asyncCompute ? 2u : 1u,
			pQueueCreateInfos = queueInfos,
			enabledExtensionCount = (uint)extensions.Count,
			ppEnabledExtensionNames = strings.Array( extensions ),
		};

		nint device;
		Instance.vkCreateDevice( PhysicalDevice, &info, null, &device ).Check( "vkCreateDevice" );

		VkDeviceApi vk;
		try
		{
			vk = new VkDeviceApi( Instance, device );
		}
		catch
		{
			Instance.DestroyDevice( device );

			throw;
		}

		Presentation = extensions.Count > 0;
		queueFamilies = asyncCompute ? [(uint)graphics, (uint)compute] : [(uint)graphics];
		return vk;
	}

	/// <summary>Whether swap chains can be made.</summary>
	internal bool Presentation { get; private set; }

	/// <summary>The families resources are shared between: one, or graphics and compute.</summary>
	internal uint[] queueFamilies;

	[UnmanagedCallersOnly]
	static uint DebugCallback( uint severity, uint types, VkDebugUtilsMessengerCallbackDataEXT* data, void* user )
	{
		try
		{
			if ( GCHandle.FromIntPtr( (nint)user ).Target is not Device device ) return 0;

			var level = severity >= Vulkan.Vk.SeverityError ? MessageSeverity.Error
				: severity >= Vulkan.Vk.SeverityWarning ? MessageSeverity.Warning
				: severity >= Vulkan.Vk.SeverityInfo ? MessageSeverity.Info
				: MessageSeverity.Verbose;

			var id = data->pMessageIdName != null ? Marshal.PtrToStringUTF8( (nint)data->pMessageIdName ) : "";
			var text = data->pMessage != null ? Marshal.PtrToStringUTF8( (nint)data->pMessage ) : "";

			if ( level == MessageSeverity.Error && (types & Vulkan.Vk.MessageValidation) != 0 ) Interlocked.Increment( ref device.validationErrors );
			device.message?.Invoke( new DeviceMessage( level, id, text ) );
		}
		catch
		{
			// Never throw back into the driver
		}

		return 0;
	}

	/// <summary>
	/// Give a Vulkan object a name debuggers and validation messages show.
	/// </summary>
	internal void SetName( VkObjectType type, ulong handle, string name )
	{
		if ( name is null || Vk is null || Instance.vkSetDebugUtilsObjectNameEXT == null || handle == 0 ) return;

		Span<byte> utf8 = stackalloc byte[256];
		var length = Encoding.UTF8.GetBytes( name.AsSpan( 0, Math.Min( name.Length, 80 ) ), utf8 );
		utf8[length] = 0;

		fixed ( byte* p = utf8 )
		{
			var info = new VkDebugUtilsObjectNameInfoEXT { sType = VkStructureType.DebugUtilsObjectNameInfoExt, objectType = type, objectHandle = handle, pObjectName = p };
			Instance.vkSetDebugUtilsObjectNameEXT( Vk.Device, &info );
		}
	}

	internal static string Utf8( byte* text, int max )
	{
		var length = 0;
		while ( length < max && text[length] != 0 ) length++;
		return Encoding.UTF8.GetString( text, length );
	}

	/// <summary>
	/// Wait until the GPU has finished everything submitted.
	/// </summary>
	public void WaitIdle()
	{
		Flush();
		Vk.vkDeviceWaitIdle( Vk.Device ).Check( "vkDeviceWaitIdle" );
	}

	/// <summary>
	/// Wait for the GPU, then free everything: resources still alive, swap chains, the device and instance.
	/// </summary>
	public void Dispose()
	{
		if ( disposed ) return;
		disposed = true;

		Vk.vkDeviceWaitIdle( Vk.Device );
		foreach ( var swapChain in swapChains.ToArray() ) swapChain.Dispose();

		DestroyFrames();
		CollectGarbage( force: true );
		DestroyAllResources();
		Bindless.Dispose();
		DestroyPipelineCache();
		graphicsQueue.Dispose();
		if ( computeQueue != graphicsQueue ) computeQueue.Dispose();
		Allocator.Dispose();
		Vk.vkDestroyDevice( Vk.Device, null );
		DestroyInstance();
	}

	void DestroyInstance()
	{
		if ( messenger != 0 ) Instance.vkDestroyDebugUtilsMessengerEXT( Instance.Instance, messenger, null );
		Instance.vkDestroyInstance( Instance.Instance, null );
		if ( self.IsAllocated ) self.Free();
	}
}

/// <summary>
/// UTF-8 strings and string arrays in native memory for create infos, freed together.
/// </summary>
internal sealed unsafe class NativeStrings : IDisposable
{
	readonly List<nint> strings = new();
	readonly List<nint> arrays = new();

	public byte* Add( string text )
	{
		var p = (byte*)Marshal.StringToCoTaskMemUTF8( text );
		strings.Add( (nint)p );
		return p;
	}

	public byte** Array( IReadOnlyList<string> texts )
	{
		if ( texts.Count == 0 ) return null;

		var array = (byte**)NativeMemory.Alloc( (nuint)(texts.Count * sizeof( nint )) );
		arrays.Add( (nint)array );
		for ( int i = 0; i < texts.Count; i++ ) array[i] = Add( texts[i] );
		return array;
	}

	/// <summary>Native memory freed with the strings.</summary>
	public void* Allocate( int bytes )
	{
		var p = NativeMemory.AllocZeroed( (nuint)Math.Max( bytes, 1 ) );
		arrays.Add( (nint)p );
		return p;
	}

	public void Dispose()
	{
		foreach ( var p in strings ) Marshal.FreeCoTaskMem( p );
		foreach ( var p in arrays ) NativeMemory.Free( (void*)p );
		strings.Clear();
		arrays.Clear();
	}
}
