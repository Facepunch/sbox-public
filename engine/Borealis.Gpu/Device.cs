using Borealis.Gpu.Memory;
using Borealis.Gpu.Vulkan;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Borealis.Gpu;

/// <summary>
/// A Vulkan device: the queues work is submitted to, and owner of every resource made on it. Resources are named by
/// handles and destroyed through <see cref="Destroy(BufferHandle)"/> and friends, which wait for the GPU to finish with
/// them. See docs/reference/borealis-gpu.md.
/// </summary>
public sealed unsafe partial class Device : IDisposable
{
	internal readonly VkInstanceApi Instance;
	internal readonly VkDeviceApi Vk;
	internal readonly nint PhysicalDevice;
	internal readonly MemoryAllocator Allocator;
	internal readonly VkPhysicalDeviceLimits Limits;

	// The most work groups a dispatch may ask for in each dimension
	readonly (uint X, uint Y, uint Z) maxGroups;

	/// <summary>
	/// Throw for a dispatch of a negative count (which Vulkan reads as billions of groups) or more groups than the device
	/// allows: either keeps the GPU busy until the device is lost, taking every other process on the GPU with it.
	/// </summary>
	internal void CheckGroupCounts( int x, int y, int z )
	{
		if ( x < 0 || y < 0 || z < 0 || (uint)x > maxGroups.X || (uint)y > maxGroups.Y || (uint)z > maxGroups.Z )
			throw new ArgumentOutOfRangeException( nameof( x ), $"A dispatch of {x} x {y} x {z} groups is outside the device's range ({maxGroups.X} x {maxGroups.Y} x {maxGroups.Z})" );
	}

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

	/// <summary>
	/// Every shader stage Borealis uses (<c>VkShaderStageFlags</c>): vertex, fragment and compute, and task and mesh with
	/// <see cref="DeviceCapabilities.MeshShaders"/>. Not tessellation or geometry, which it has none of: push constants
	/// visible to them as well read wrong on AMD's driver without mesh shaders.
	/// </summary>
	internal uint StageAll { get; }

	/// <summary>
	/// The stages compute layouts' push constants are visible to: <see cref="StageAll"/> on a device with graphics pipeline
	/// libraries, compute alone on one without. AMD's driver (26.11.2) keeps compute pipelines in its shader cache, on
	/// disk too, by their layout but not by whether the device has pipeline libraries, which moves push constants: a
	/// pipeline made first on one kind of device read zeros on the other. Different layouts keep the two apart.
	/// </summary>
	internal uint ComputePushStages { get; }

	/// <summary>The stages push constants are visible to in the layouts of pipelines bound at <paramref name="bindPoint"/>.</summary>
	internal uint PushStages( int bindPoint ) => bindPoint == Vulkan.Vk.BindPointCompute ? ComputePushStages : StageAll;

	/// <summary>Frames the CPU may record ahead of the GPU.</summary>
	public int FramesInFlight { get; }

	/// <summary>Errors the validation layer has reported. Tests check it stays 0.</summary>
	public int ValidationErrors => validationErrors;

	/// <summary>Whether the validation layer is on.</summary>
	public bool ValidationEnabled { get; }

	/// <summary>
	/// Whether <see cref="CommandList.SetBindings"/> binds the slot's last set again when the bindings are the same, rather
	/// than writing another.
	/// </summary>
	public bool ReusesTransientSets { get; set; } = true;

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
		if ( loaderVersion < Vulkan.Vk.ApiVersion13 ) throw new GpuException( $"The Vulkan loader is {Vulkan.Vk.ApiMajor( loaderVersion )}.{Vulkan.Vk.ApiMinor( loaderVersion )}; Borealis.Gpu needs 1.3" );

		// Hardware counters: GPUPerfAPI initialises before the instance, and needs its extensions
		var counterSelection = options.GpuCounters;
		if ( counterSelection is not null && GpuCounters.Prepare() is { } noCounters )
		{
			options.Message?.Invoke( new DeviceMessage( MessageSeverity.Warning, "Borealis.Gpu.Counters", $"GPU counters are off: {noCounters}" ) );
			counterSelection = null;
		}

		countersPrepared = counterSelection is not null;

		// Instance
		var available = InstanceExtensions();
		var extensions = new List<string>();
		if ( countersPrepared && available.Contains( Gpa.InstanceExtension ) ) extensions.Add( Gpa.InstanceExtension );
		var debugUtils = available.Contains( "VK_EXT_debug_utils" );
		if ( debugUtils ) extensions.Add( "VK_EXT_debug_utils" );
		if ( options.Presentation )
		{
			foreach ( var name in SurfaceSource.InstanceExtensions )
			{
				if ( available.Contains( name ) ) extensions.Add( name );
			}
		}

		foreach ( var name in options.InstanceExtensions ?? [] )
		{
			if ( !available.Contains( name ) ) throw new GpuException( $"The Vulkan instance has no {name}, which the window system needs" );
			if ( !extensions.Contains( name ) ) extensions.Add( name );
		}

		foreach ( var name in options.OptionalInstanceExtensions ?? [] )
		{
			if ( available.Contains( name ) && !extensions.Contains( name ) ) extensions.Add( name );
		}

		var layers = new List<string>();
		if ( options.Validation && InstanceLayers().Contains( "VK_LAYER_KHRONOS_validation" ) )
		{
			layers.Add( "VK_LAYER_KHRONOS_validation" );
			ValidationEnabled = true;
		}

		using var strings = new NativeStrings();
		var appName = strings.Add( options.ApplicationName ?? "Borealis" );
		var engineName = strings.Add( options.EngineName ?? "Borealis" );
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

		// VK_VALIDATION_FEATURE_ENABLE_SYNCHRONIZATION_VALIDATION_EXT, VK_VALIDATION_FEATURE_ENABLE_GPU_ASSISTED_EXT,
		// VK_VALIDATION_FEATURE_ENABLE_BEST_PRACTICES_EXT
		var enabledValidation = stackalloc int[3];
		var enabledValidationCount = 0u;
		if ( options.SynchronizationValidation ) enabledValidation[enabledValidationCount++] = 4;
		if ( options.GpuAssistedValidation ) enabledValidation[enabledValidationCount++] = 0;
		if ( options.BestPracticesValidation ) enabledValidation[enabledValidationCount++] = 2;
		var validationFeatures = new VkValidationFeaturesEXT
		{
			sType = VkStructureType.ValidationFeaturesExt,
			enabledValidationFeatureCount = enabledValidationCount,
			pEnabledValidationFeatures = enabledValidation,
		};

		void* instanceNext = null;
		if ( debugUtils )
		{
			messengerInfo.pNext = instanceNext;
			instanceNext = &messengerInfo;
		}

		if ( ValidationEnabled && enabledValidationCount > 0 )
		{
			validationFeatures.pNext = instanceNext;
			instanceNext = &validationFeatures;
		}

		// GPU-assisted validation without its in-shader descriptor checks (DeviceOptions.GpuAssistedDescriptorChecks)
		uint off = 0;
		var setting = new VkLayerSettingEXT { pLayerName = strings.Add( "VK_LAYER_KHRONOS_validation" ), pSettingName = strings.Add( "gpuav_descriptor_checks" ), type = 0, valueCount = 1, pValues = &off };
		var layerSettings = new VkLayerSettingsCreateInfoEXT { sType = VkStructureType.LayerSettingsCreateInfoExt, settingCount = 1, pSettings = &setting };
		if ( ValidationEnabled && options.GpuAssistedValidation && !options.GpuAssistedDescriptorChecks )
		{
			if ( InstanceExtensions( "VK_LAYER_KHRONOS_validation" ).Contains( "VK_EXT_layer_settings" ) && !extensions.Contains( "VK_EXT_layer_settings" ) ) extensions.Add( "VK_EXT_layer_settings" );
			layerSettings.pNext = instanceNext;
			instanceNext = &layerSettings;
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
			maxGroups = (properties.limits.maxComputeWorkGroupCount[0], properties.limits.maxComputeWorkGroupCount[1], properties.limits.maxComputeWorkGroupCount[2]);
			VkPhysicalDeviceMemoryProperties memory;
			Instance.vkGetPhysicalDeviceMemoryProperties( PhysicalDevice, &memory );
			ulong deviceLocal = 0;
			for ( int i = 0; i < memory.memoryHeapCount; i++ )
				if ( (memory.Heap( i ).flags & Vulkan.Vk.HeapDeviceLocal) != 0 ) deviceLocal += memory.Heap( i ).size;
			Adapter = new AdapterInfo
			{
				Name = Utf8( properties.deviceName, 256 ),
				VendorId = properties.vendorID,
				DeviceId = properties.deviceID,
				Discrete = properties.deviceType == 2,
				Integrated = properties.deviceType == 1,
				Software = properties.deviceType == 4,
				ApiVersion = $"{Vulkan.Vk.ApiMajor( properties.apiVersion )}.{Vulkan.Vk.ApiMinor( properties.apiVersion )}.{Vulkan.Vk.ApiPatch( properties.apiVersion )}",
				DriverVersion = AdapterInfo.DecodeDriverVersion( properties.vendorID, properties.driverVersion ),
				DeviceLocalBytes = deviceLocal,
			};

			if ( Limits.maxBoundDescriptorSets < ShaderAbi.SetCount )
				throw new GpuException( $"{Adapter.Name} binds {Limits.maxBoundDescriptorSets} descriptor sets; s&box's shaders use {ShaderAbi.SetCount}" );

			deviceExtensions = DeviceExtensions( PhysicalDevice );
			Vk = CreateLogicalDevice( options, out var features, out var computeFamily );
			StageAll = Vulkan.Vk.StageVertex | Vulkan.Vk.StageFragment | Vulkan.Vk.StageCompute | (features.meshShaders ? Vulkan.Vk.StageTask | Vulkan.Vk.StageMesh : 0);
			ComputePushStages = pipelineLibraries ? StageAll : Vulkan.Vk.StageCompute;
			graphicsQueue = new DeviceQueue( this, Vk, queueFamilies[0], QueueType.Graphics );
			computeQueue = computeFamily ? new DeviceQueue( this, Vk, queueFamilies[1], QueueType.Compute ) : graphicsQueue;
			if ( AddressReports ) ListenForAddressBindings();

			var driver = new VkPhysicalDeviceDriverProperties { sType = VkStructureType.PhysicalDeviceDriverProperties };
			var subgroupSizes = new VkPhysicalDeviceSubgroupSizeControlProperties { sType = VkStructureType.PhysicalDeviceSubgroupSizeControlProperties, pNext = &driver };
			var subgroups = new VkPhysicalDeviceSubgroupProperties { sType = VkStructureType.PhysicalDeviceSubgroupProperties, pNext = &subgroupSizes };
			var indexing = new VkPhysicalDeviceDescriptorIndexingProperties { sType = VkStructureType.PhysicalDeviceDescriptorIndexingProperties, pNext = &subgroups };
			var properties2 = new VkPhysicalDeviceProperties2 { sType = VkStructureType.PhysicalDeviceProperties2, pNext = &indexing };
			Instance.vkGetPhysicalDeviceProperties2( PhysicalDevice, &properties2 );

			// VK_DRIVER_ID_AMD_PROPRIETARY and _AMD_OPEN_SOURCE: fast links draw what a pixel shader discards
			LinksDiscardsOptimised = pipelineLibraries && driver.driverID is 1 or 2 && Environment.GetEnvironmentVariable( "BOREALIS_DISCARD_FAST_LINKS" ) != "1";

			Allocator = new MemoryAllocator( Vk, memory, Adapter.Integrated ) { Owner = this, BudgetLimit = options.MemoryBudgetBytes, SpareSeconds = MemoryAllocator.DeviceSpareSeconds, DeviceAddresses = BufferAddresses, OwnBigContentImages = MemoryAllocator.BigContentImagesOwn };
			Allocator.Warn = text => message?.Invoke( new DeviceMessage( MessageSeverity.Warning, "Borealis.Gpu.MemoryBudget", text ) );
			RefreshMemoryBudget();
			if ( options.MemoryBudgetBytes > 0 ) message?.Invoke( new DeviceMessage( MessageSeverity.Warning, "Borealis.Gpu.MemoryBudget", $"video memory budget capped at {options.MemoryBudgetBytes >> 20} MiB" ) );

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
				HostQueryReset = features.hostQueryReset,
				TimestampPeriod = Limits.timestampPeriod,
				MaxTextureSize = (int)Limits.maxImageDimension2D,
				ColorSampleCounts = (int)Limits.framebufferColorSampleCounts,
				UniformAlignment = (int)Limits.minUniformBufferOffsetAlignment,
				MaxDynamicUniformBuffers = (int)Limits.maxDescriptorSetUniformBuffersDynamic,
				StorageAlignment = (int)Limits.minStorageBufferOffsetAlignment,
				BindlessTextures = bindlessTextures,
				BindlessSamplers = bindlessSamplers,
				BindlessStorageTextures = bindlessStorage,
				MeshShaders = features.meshShaders,
				PipelineLibraries = pipelineLibraries,
				// VK_SHADER_STAGE_VERTEX_BIT, and the vote (2) and ballot (8) operations wave-uniform vertex code uses
				VertexWaveOps = (subgroups.supportedStages & 1) != 0 && (subgroups.supportedOperations & 0xA) == 0xA,
				FragmentWave32 = features.subgroupSizeControl && (subgroupSizes.requiredSubgroupSizeStages & Vulkan.Vk.StageFragment) != 0
					&& subgroupSizes.minSubgroupSize <= 32 && subgroupSizes.maxSubgroupSize >= 32,
			};

			Bindless = new BindlessTable( this, bindlessTextures, bindlessSamplers, bindlessStorage, features.storageBufferUpdateAfterBind );
			if ( options.PipelineCacheFolder is { } folder ) pipelineCachePath = PipelineCacheFile.PathFor( folder, properties.vendorID, properties.deviceID, properties.driverVersion, new ReadOnlySpan<byte>( properties.pipelineCacheUUID, 16 ) );
			var cacheData = pipelineCachePath is null ? null : PipelineCacheFile.Load( pipelineCachePath );
			CreatePipelineCache( cacheData ?? options.PipelineCacheData );
			CreateFrames();
			CreateMarkers();
			CreateDefaultViewSet();
			CreateBindlessDefaults();
			CreateIndirectCounts( driver.driverID, features.drawIndirectCount );
			LoadPartManifest( options );
			if ( options.HangWatch ) StartWatch();
			if ( countersPrepared ) OpenCounters( counterSelection );
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
		public bool hostQueryReset { get; init; }
		public bool storageBufferUpdateAfterBind { get; init; }
		public bool subgroupSizeControl { get; init; }
		public bool meshShaders { get; init; }
	}

	/// <summary>The instance extensions the loader and drivers have, or those <paramref name="layer"/> has.</summary>
	static HashSet<string> InstanceExtensions( string layer = null )
	{
		using var strings = new NativeStrings();
		var layerName = layer is null ? null : strings.Add( layer );
		// The count can change between the two calls (another thread's instance loading layers, as parallel tests do): ask
		// again while the array came back too small, and read no further than it
		uint count;
		VkExtensionProperties[] props;
		VkResult result;
		do
		{
			VkGlobal.vkEnumerateInstanceExtensionProperties( layerName, &count, null ).Check( "vkEnumerateInstanceExtensionProperties" );
			props = new VkExtensionProperties[count];
			fixed ( VkExtensionProperties* p = props ) result = VkGlobal.vkEnumerateInstanceExtensionProperties( layerName, &count, p );
			result.Check( "vkEnumerateInstanceExtensionProperties" );
		}
		while ( result == VkResult.Incomplete );

		var names = new HashSet<string>();
		for ( int i = 0; i < Math.Min( count, (uint)props.Length ); i++ )
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

		if ( best == 0 ) throw new GpuException( $"No GPU supports what Borealis.Gpu needs.{rejected}" );
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

		// GeometryShader isn't for geometry stages (Borealis has none): SPIR-V's Geometry capability covers pixel shaders that
		// read SV_PrimitiveID, as sbox/quad_overdraw.slangh does in sky and tools_shading_complexity
		ReadOnlySpan<VkFeature> core =
		[
			VkFeature.FullDrawIndexUint32, VkFeature.ImageCubeArray, VkFeature.IndependentBlend, VkFeature.GeometryShader, VkFeature.SampleRateShading,
			VkFeature.MultiDrawIndirect, VkFeature.DrawIndirectFirstInstance, VkFeature.DepthClamp, VkFeature.DepthBiasClamp,
			VkFeature.FillModeNonSolid, VkFeature.SamplerAnisotropy, VkFeature.TextureCompressionBc, VkFeature.OcclusionQueryPrecise, VkFeature.PipelineStatisticsQuery, VkFeature.FragmentStoresAndAtomics,
			VkFeature.ShaderImageGatherExtended, VkFeature.ShaderStorageImageExtendedFormats, VkFeature.ShaderStorageImageReadWithoutFormat,
			VkFeature.ShaderStorageImageWriteWithoutFormat, VkFeature.ShaderUniformBufferArrayDynamicIndexing, VkFeature.ShaderSampledImageArrayDynamicIndexing,
			VkFeature.ShaderStorageBufferArrayDynamicIndexing, VkFeature.ShaderStorageImageArrayDynamicIndexing, VkFeature.ShaderClipDistance, VkFeature.ShaderInt64, VkFeature.ShaderInt16,
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
		if ( options.BufferAddresses ) e12.values[VkPhysicalDeviceVulkan12Features.BufferDeviceAddress] = s12.values[VkPhysicalDeviceVulkan12Features.BufferDeviceAddress];

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
			hostQueryReset = e12.values[VkPhysicalDeviceVulkan12Features.HostQueryReset] != 0,
			storageBufferUpdateAfterBind = e12.values[VkPhysicalDeviceVulkan12Features.DescriptorBindingStorageBufferUpdateAfterBind] != 0,
			subgroupSizeControl = e13.values[VkPhysicalDeviceVulkan13Features.SubgroupSizeControl] != 0,
		};

		var extensions = new List<string>();
		if ( options.Presentation && deviceExtensions.Contains( "VK_KHR_swapchain" ) ) extensions.Add( "VK_KHR_swapchain" );
		if ( countersPrepared ) AddCounterExtensions( extensions );

		// What a library working on the device asks for (a vendor upscaler), where the device has it
		foreach ( var name in options.OptionalDeviceExtensions?.Invoke( Instance.Instance, PhysicalDevice ) ?? [] )
		{
			if ( deviceExtensions.Contains( name ) && !extensions.Contains( name ) ) extensions.Add( name );
		}

		// Device fault reports, for what the GPU was doing when the device is lost
		var fault = new VkPhysicalDeviceFaultFeaturesEXT { sType = VkStructureType.PhysicalDeviceFaultFeaturesExt };
		if ( deviceExtensions.Contains( "VK_EXT_device_fault" ) )
		{
			var supportedFault = new VkPhysicalDeviceFaultFeaturesEXT { sType = VkStructureType.PhysicalDeviceFaultFeaturesExt };
			var query = new VkPhysicalDeviceFeatures2 { sType = VkStructureType.PhysicalDeviceFeatures2, pNext = &supportedFault };
			Instance.vkGetPhysicalDeviceFeatures2( PhysicalDevice, &query );
			if ( supportedFault.deviceFault != 0 )
			{
				extensions.Add( "VK_EXT_device_fault" );
				fault.deviceFault = 1;
				e13.pNext = &fault;
			}
		}

		// The GPU address ranges the driver binds and takes back, for which resource a fault address lies in when the device is lost
		var addressBinding = new VkPhysicalDeviceAddressBindingReportFeaturesEXT { sType = VkStructureType.PhysicalDeviceAddressBindingReportFeaturesExt };
		if ( options.ProgressMarkers && messenger != 0 && deviceExtensions.Contains( "VK_EXT_device_address_binding_report" ) )
		{
			var supportedBinding = new VkPhysicalDeviceAddressBindingReportFeaturesEXT { sType = VkStructureType.PhysicalDeviceAddressBindingReportFeaturesExt };
			var query = new VkPhysicalDeviceFeatures2 { sType = VkStructureType.PhysicalDeviceFeatures2, pNext = &supportedBinding };
			Instance.vkGetPhysicalDeviceFeatures2( PhysicalDevice, &query );
			if ( supportedBinding.reportAddressBinding != 0 )
			{
				extensions.Add( "VK_EXT_device_address_binding_report" );
				addressBinding.reportAddressBinding = 1;
				addressBinding.pNext = e13.pNext;
				e13.pNext = &addressBinding;
			}
		}

		// Progress markers, for how far the GPU got when the device is lost
		if ( options.ProgressMarkers && deviceExtensions.Contains( "VK_AMD_buffer_marker" ) ) extensions.Add( "VK_AMD_buffer_marker" );

		// Presenting the newest image ready at each vertical blank, dropping older ones (PresentMode.FifoLatestReady)
		var latestReady = new VkPhysicalDevicePresentModeFifoLatestReadyFeaturesEXT { sType = VkStructureType.PhysicalDevicePresentModeFifoLatestReadyFeaturesExt };
		if ( extensions.Contains( "VK_KHR_swapchain" ) && deviceExtensions.Contains( "VK_EXT_present_mode_fifo_latest_ready" ) )
		{
			var supportedLatest = new VkPhysicalDevicePresentModeFifoLatestReadyFeaturesEXT { sType = VkStructureType.PhysicalDevicePresentModeFifoLatestReadyFeaturesExt };
			var query = new VkPhysicalDeviceFeatures2 { sType = VkStructureType.PhysicalDeviceFeatures2, pNext = &supportedLatest };
			Instance.vkGetPhysicalDeviceFeatures2( PhysicalDevice, &query );
			if ( supportedLatest.presentModeFifoLatestReady != 0 )
			{
				extensions.Add( "VK_EXT_present_mode_fifo_latest_ready" );
				latestReady.presentModeFifoLatestReady = 1;
				latestReady.pNext = e13.pNext;
				e13.pNext = &latestReady;
				FifoLatestReady = true;
			}
		}

		// Shader statistics, for measuring register use
		var executables = new VkPhysicalDevicePipelineExecutablePropertiesFeaturesKHR { sType = VkStructureType.PhysicalDevicePipelineExecutablePropertiesFeaturesKhr };
		if ( options.ShaderStatistics && deviceExtensions.Contains( "VK_KHR_pipeline_executable_properties" ) )
		{
			extensions.Add( "VK_KHR_pipeline_executable_properties" );
			executables.pipelineExecutableInfo = 1;
			executables.pNext = e13.pNext;
			e13.pNext = &executables;
		}

		// Memory budgets, for how much of each heap the process is using
		if ( deviceExtensions.Contains( "VK_EXT_memory_budget" ) ) extensions.Add( "VK_EXT_memory_budget" );

		// Task and mesh shaders, where the device has both
		var mesh = new VkPhysicalDeviceMeshShaderFeaturesEXT { sType = VkStructureType.PhysicalDeviceMeshShaderFeaturesExt };
		if ( options.MeshShaders && deviceExtensions.Contains( "VK_EXT_mesh_shader" ) )
		{
			var supportedMesh = new VkPhysicalDeviceMeshShaderFeaturesEXT { sType = VkStructureType.PhysicalDeviceMeshShaderFeaturesExt };
			var query = new VkPhysicalDeviceFeatures2 { sType = VkStructureType.PhysicalDeviceFeatures2, pNext = &supportedMesh };
			Instance.vkGetPhysicalDeviceFeatures2( PhysicalDevice, &query );
			if ( supportedMesh.taskShader != 0 && supportedMesh.meshShader != 0 )
			{
				extensions.Add( "VK_EXT_mesh_shader" );
				mesh.taskShader = 1;
				mesh.meshShader = 1;
				mesh.pNext = e13.pNext;
				e13.pNext = &mesh;
			}
		}

		// Graphics pipeline libraries that link fast: pipelines are linked from parts made ahead (Device.GraphicsPipelines)
		var library = new VkPhysicalDeviceGraphicsPipelineLibraryFeaturesEXT { sType = VkStructureType.PhysicalDeviceGraphicsPipelineLibraryFeaturesExt };
		if ( options.PipelineLibraries && deviceExtensions.Contains( "VK_EXT_graphics_pipeline_library" ) && deviceExtensions.Contains( "VK_KHR_pipeline_library" ) )
		{
			var supportedLibrary = new VkPhysicalDeviceGraphicsPipelineLibraryFeaturesEXT { sType = VkStructureType.PhysicalDeviceGraphicsPipelineLibraryFeaturesExt };
			var query = new VkPhysicalDeviceFeatures2 { sType = VkStructureType.PhysicalDeviceFeatures2, pNext = &supportedLibrary };
			Instance.vkGetPhysicalDeviceFeatures2( PhysicalDevice, &query );
			var libraryProperties = new VkPhysicalDeviceGraphicsPipelineLibraryPropertiesEXT { sType = VkStructureType.PhysicalDeviceGraphicsPipelineLibraryPropertiesExt };
			var properties = new VkPhysicalDeviceProperties2 { sType = VkStructureType.PhysicalDeviceProperties2, pNext = &libraryProperties };
			Instance.vkGetPhysicalDeviceProperties2( PhysicalDevice, &properties );
			// NVIDIA's drivers draw nothing from some of the pipelines linked from parts (players' reports, 2026-11-21: the menu
			// stays black), so pipelines are made whole there until that's found; BOREALIS_PIPELINE_LIBRARY=1 links them anyway
			var nvidia = properties.properties.vendorID == 0x10DE && Environment.GetEnvironmentVariable( "BOREALIS_PIPELINE_LIBRARY" ) != "1";
			if ( supportedLibrary.graphicsPipelineLibrary != 0 && libraryProperties.graphicsPipelineLibraryFastLinking != 0 && !nvidia )
			{
				extensions.Add( "VK_KHR_pipeline_library" );
				extensions.Add( "VK_EXT_graphics_pipeline_library" );
				library.graphicsPipelineLibrary = 1;
				library.pNext = e13.pNext;
				e13.pNext = &library;
			}
		}

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

		Presentation = extensions.Contains( "VK_KHR_swapchain" );
		faultReports = extensions.Contains( "VK_EXT_device_fault" );
		AddressReports = extensions.Contains( "VK_EXT_device_address_binding_report" );
		BufferAddresses = e12.values[VkPhysicalDeviceVulkan12Features.BufferDeviceAddress] != 0 && vk.vkGetBufferDeviceAddress != null;
		memoryBudget = extensions.Contains( "VK_EXT_memory_budget" );
		bufferMarkers = extensions.Contains( "VK_AMD_buffer_marker" );
		passMarkers = options.PassProgressMarkers;
		shaderStatistics = extensions.Contains( "VK_KHR_pipeline_executable_properties" );
		pipelineLibraries = extensions.Contains( "VK_EXT_graphics_pipeline_library" );
		enabled = enabled with { meshShaders = extensions.Contains( "VK_EXT_mesh_shader" ) && vk.vkCmdDrawMeshTasksIndirectEXT != null && vk.vkCmdDrawMeshTasksIndirectCountEXT != null };
		queueFamilies = asyncCompute ? [(uint)graphics, (uint)compute] : [(uint)graphics];
		return vk;
	}

	/// <summary>Whether swap chains can be made.</summary>
	internal bool Presentation { get; private set; }

	/// <summary>Whether swap chains can present with <see cref="PresentMode.FifoLatestReady"/> (VK_EXT_present_mode_fifo_latest_ready).</summary>
	internal bool FifoLatestReady { get; private set; }

	/// <summary>Whether the device was made with VK_EXT_device_fault.</summary>
	bool faultReports;

	/// <summary>Whether the device was made with VK_EXT_memory_budget.</summary>
	bool memoryBudget;

	/// <summary>
	/// Each memory heap's size and, with VK_EXT_memory_budget, how much of it this process uses (every device it has,
	/// Source 2's too) and the budget the OS gives it, which shrinks as other processes use the heap: "device local: process
	/// 8236 MiB, budget 23746 of 24560 MiB (814 MiB elsewhere)".
	/// </summary>
	public string DescribeMemory()
	{
		RefreshMemoryBudget();
		Span<MemoryHeapBudget> budgets = stackalloc MemoryHeapBudget[16];
		var count = Allocator.GetHeaps( budgets );

		var heaps = new List<string>();
		foreach ( var heap in budgets[..count] )
		{
			var kind = heap.DeviceLocal ? "device local" : "host";
			var ours = $"; Borealis.Gpu {heap.Reserved >> 20} MiB reserved, {heap.Used >> 20} used, {heap.Stranded >> 20} stranded";
			heaps.Add( memoryBudget
				? $"{kind}: process {heap.Usage >> 20} MiB, budget {heap.Budget >> 20} of {heap.Size >> 20} MiB ({(heap.Size - heap.Budget) >> 20} MiB elsewhere{(heap.DeviceLocal && Allocator.BudgetLimit > 0 ? " or capped" : "")}){ours}"
				: $"{kind}: heap {heap.Size >> 20} MiB{ours}" );
		}

		if ( Allocator.OverBudgetAllocations > 0 ) heaps.Add( $"{Allocator.OverBudgetAllocations} allocations refused over budget" );

		// Video memory ran out and device allocations went to system memory (MemoryAllocator.SystemFallbackBytes)
		if ( Allocator.SystemFallbackBytes > 0 ) heaps.Add( $"{Allocator.SystemFallbackBytes >> 20} MiB of device allocations in system memory" );

		// Blocks drained and what moved out of them (Device.Relocation): the last move's frame, for a loss soon after one
		if ( Allocator.DrainsBegun > 0 ) heaps.Add( $"blocks drained: {Allocator.DrainsBegun} begun, {Allocator.DrainsEmptied} emptied, {Allocator.DrainsAbandoned} given up, {Allocator.TailDrains} into a block their size" );
		if ( Relocations > 0 )
		{
			var last = RelocationFrames[(int)((Relocations - 1) & (RelocationHistory - 1))];
			heaps.Add( $"{Relocations} moves ({RelocatedBuffers} buffers, {RelocatedTextures} textures), the last in frame {last}, {FrameNumber - Math.Min( FrameNumber, last )} frames ago" );
		}

		return string.Join( ", ", heaps );
	}

	/// <summary>
	/// The largest device-local heap: how much of it this process uses (every device it has) and the budget the OS gives
	/// it, with VK_EXT_memory_budget; without it, nothing used and the heap's size.
	/// </summary>
	public (long Used, long Budget) DeviceLocalMemory()
	{
		Span<MemoryHeapBudget> heaps = stackalloc MemoryHeapBudget[16];
		var count = Allocator.GetHeaps( heaps );
		(long Used, long Budget) largest = default;
		long largestSize = 0;
		foreach ( var heap in heaps[..count] )
		{
			if ( !heap.DeviceLocal || heap.Size <= largestSize ) continue;
			largestSize = heap.Size;
			largest = (heap.Usage, heap.Budget);
		}

		return largest;
	}

	/// <summary>
	/// Each memory heap's budget and use, written into <paramref name="heaps"/> as far as it reaches (16 is enough for any
	/// device); the number of heaps. Allocates nothing.
	/// </summary>
	public int GetMemoryHeaps( Span<MemoryHeapBudget> heaps ) => Allocator.GetHeaps( heaps );

	/// <summary>
	/// Every memory block the allocator holds and what's allocated in each, as they are now. Walks the whole allocator under
	/// its lock and allocates: for diagnostics when asked, never every frame.
	/// </summary>
	public IReadOnlyList<MemoryMapBlock> MemoryMap() => Allocator.Map();

	/// <summary>
	/// The artificial video memory budget in bytes (<see cref="DeviceOptions.MemoryBudgetBytes"/>), or 0 for the driver's.
	/// </summary>
	public long MemoryBudgetLimit
	{
		get => Allocator.BudgetLimit;
		set => Allocator.BudgetLimit = Math.Max( value, 0 );
	}

	/// <summary>Frames between reads of the driver's memory budget (<see cref="BeginFrame"/>).</summary>
	public const int MemoryBudgetFrames = 30;

	/// <summary>Read the driver's usage and budget per heap into the allocator (VK_EXT_memory_budget). Allocates nothing.</summary>
	internal void RefreshMemoryBudget()
	{
		if ( !memoryBudget )
		{
			Allocator.UpdateBudgetWithoutDriver();
			return;
		}

		var budget = new VkPhysicalDeviceMemoryBudgetPropertiesEXT { sType = VkStructureType.PhysicalDeviceMemoryBudgetPropertiesExt };
		var properties = new VkPhysicalDeviceMemoryProperties2 { sType = VkStructureType.PhysicalDeviceMemoryProperties2, pNext = &budget };
		Instance.vkGetPhysicalDeviceMemoryProperties2( PhysicalDevice, &properties );
		Allocator.UpdateBudget( new ReadOnlySpan<ulong>( budget.heapUsage, 16 ), new ReadOnlySpan<ulong>( budget.heapBudget, 16 ) );
	}

	/// <summary>The families resources are shared between: one, or graphics and compute.</summary>
	internal uint[] queueFamilies;

	[UnmanagedCallersOnly]
	static uint DebugCallback( uint severity, uint types, VkDebugUtilsMessengerCallbackDataEXT* data, void* user )
	{
		try
		{
			if ( GCHandle.FromIntPtr( (nint)user ).Target is not Device device ) return 0;
			if ( (types & Vulkan.Vk.MessageDeviceAddressBinding) != 0 )
			{
				device.NoteAddressBinding( data );
				return 0;
			}

			// Information is asked for only for the binding reports
			if ( severity < Vulkan.Vk.SeverityWarning ) return 0;

			var level = severity >= Vulkan.Vk.SeverityError ? MessageSeverity.Error
				: severity >= Vulkan.Vk.SeverityWarning ? MessageSeverity.Warning
				: severity >= Vulkan.Vk.SeverityInfo ? MessageSeverity.Info
				: MessageSeverity.Verbose;

			var id = data->pMessageIdName != null ? Marshal.PtrToStringUTF8( (nint)data->pMessageIdName ) : "";
			var text = data->pMessage != null ? Marshal.PtrToStringUTF8( (nint)data->pMessage ) : "";
			if ( level >= MessageSeverity.Warning ) text += DescribeObjects( data );

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
	/// What a validation message is about, by the names Borealis.Gpu gave it: the named objects it lists (the pipeline,
	/// the image) and the command-buffer labels open at the call (the pass), as the layer lists them. Empty when none is named.
	/// </summary>
	internal static string DescribeObjects( VkDebugUtilsMessengerCallbackDataEXT* data )
	{
		var names = new List<string>();
		var objects = (VkDebugUtilsObjectNameInfoEXT*)data->pObjects;
		for ( int i = 0; i < data->objectCount && objects != null; i++ )
		{
			if ( objects[i].pObjectName == null ) continue;
			names.Add( $"{objects[i].objectType} '{Marshal.PtrToStringUTF8( (nint)objects[i].pObjectName )}'" );
		}

		var labels = (VkDebugUtilsLabelEXT*)data->pCmdBufLabels;
		var open = new List<string>();
		for ( int i = 0; i < data->cmdBufLabelCount && labels != null; i++ )
		{
			if ( labels[i].pLabelName != null ) open.Add( Marshal.PtrToStringUTF8( (nint)labels[i].pLabelName ) );
		}
		if ( open.Count > 0 ) names.Add( $"labels '{string.Join( "' '", open )}'" );

		return names.Count == 0 ? "" : $" [{string.Join( ", ", names )}]";
	}

	/// <summary>
	/// Give a Vulkan object a name debuggers and validation messages show: the last 80 characters of a longer one, where a
	/// path names its file.
	/// </summary>
	internal void SetName( VkObjectType type, ulong handle, string name )
	{
		if ( name is null || Vk is null || Instance.vkSetDebugUtilsObjectNameEXT == null || handle == 0 ) return;

		Span<byte> utf8 = stackalloc byte[256];
		var length = ShortName( name, utf8 );
		utf8[length] = 0;

		fixed ( byte* p = utf8 )
		{
			var info = new VkDebugUtilsObjectNameInfoEXT { sType = VkStructureType.DebugUtilsObjectNameInfoExt, objectType = type, objectHandle = handle, pObjectName = p };
			Instance.vkSetDebugUtilsObjectNameEXT( Vk.Device, &info );
		}
	}

	/// <summary>
	/// A name as <see cref="SetName"/> gives it, in UTF-8 into <paramref name="utf8"/> (256 bytes): up to 80 characters, the
	/// end of a longer one after "...". Returns its length.
	/// </summary>
	internal static int ShortName( string name, Span<byte> utf8 )
	{
		if ( name.Length <= 80 ) return Encoding.UTF8.GetBytes( name, utf8 );
		"..."u8.CopyTo( utf8 );
		return 3 + Encoding.UTF8.GetBytes( name.AsSpan( name.Length - 77 ), utf8[3..] );
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
		using var _ = Enter( "WaitIdle" );
		Flush();
		WaitFlushed();
	}

	/// <summary>
	/// Wait for what reached the GPU, each queue's timeline first so a GPU that stopped is noticed
	/// (<see cref="HangSeconds"/>) rather than waited for forever. Nothing when the device is lost.
	/// </summary>
	void WaitFlushed()
	{
		if ( IsLost ) return;
		graphicsQueue.Wait( Volatile.Read( ref graphicsQueue.Flushed ) );
		if ( computeQueue != graphicsQueue ) computeQueue.Wait( Volatile.Read( ref computeQueue.Flushed ) );
		if ( !IsLost ) Check( Vk.vkDeviceWaitIdle( Vk.Device ), "vkDeviceWaitIdle" );
	}

	/// <summary>
	/// Wait for the GPU, then free everything: resources still alive, swap chains, the device and instance.
	/// </summary>
	public void Dispose()
	{
		if ( disposed ) return;
		disposed = true;
		StopWatch();

		// Upload pages being made ahead finish first: they allocate on this device
		while ( Volatile.Read( ref makingUploadPages ) != 0 ) Thread.Sleep( 1 );

		// Bounded: a GPU that stopped mustn't hold the process at exit
		try { WaitFlushed(); }
		catch ( GpuException ) { }
		try { spirvLoad?.Wait(); }
		catch ( AggregateException ) { }
		StopBuilding();
		StopOptimising();
		foreach ( var swapChain in swapChains.ToArray() ) swapChain.Dispose();

		// Nothing reads the table any more, and the defaults are about to go with everything else
		Bindless.ForgetDefaults();
		DestroyFrames();
		DestroyQueryPools();
		CollectGarbage( force: true );
		DestroyAllResources();
		Bindless.Dispose();
		SavePipelineCache();
		PruneSpirv();
		DestroyPipelineCache();
		graphicsQueue.Dispose();
		if ( computeQueue != graphicsQueue ) computeQueue.Dispose();
		Allocator.Dispose();
		Counters?.Close();
		Vk.vkDestroyDevice( Vk.Device, null );
		DestroyInstance();
	}

	void DestroyInstance()
	{
		if ( bindingMessenger != 0 ) Instance.vkDestroyDebugUtilsMessengerEXT( Instance.Instance, bindingMessenger, null );
		if ( messenger != 0 ) Instance.vkDestroyDebugUtilsMessengerEXT( Instance.Instance, messenger, null );
		Instance.vkDestroyInstance( Instance.Instance, null );
		if ( countersPrepared ) GpuCounters.Release();
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
