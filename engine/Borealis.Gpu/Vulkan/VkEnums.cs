namespace Borealis.Gpu.Vulkan;

// The parts of vulkan_core.h this backend uses, written out by hand. Values are the header's; names drop the VK_ prefix
// and the enum's own prefix.

internal enum VkResult
{
	Success = 0,
	NotReady = 1,
	Timeout = 2,
	EventSet = 3,
	EventReset = 4,
	Incomplete = 5,
	ErrorOutOfHostMemory = -1,
	ErrorOutOfDeviceMemory = -2,
	ErrorInitializationFailed = -3,
	ErrorDeviceLost = -4,
	ErrorMemoryMapFailed = -5,
	ErrorLayerNotPresent = -6,
	ErrorExtensionNotPresent = -7,
	ErrorFeatureNotPresent = -8,
	ErrorIncompatibleDriver = -9,
	ErrorTooManyObjects = -10,
	ErrorFormatNotSupported = -11,
	ErrorFragmentedPool = -12,
	ErrorOutOfPoolMemory = -1000069000,
	ErrorSurfaceLost = -1000000000,
	ErrorNativeWindowInUse = -1000000001,
	Suboptimal = 1000001003,
	ErrorOutOfDate = -1000001004,
}

internal enum VkStructureType
{
	ApplicationInfo = 0,
	InstanceCreateInfo = 1,
	DeviceQueueCreateInfo = 2,
	DeviceCreateInfo = 3,
	MemoryAllocateInfo = 5,
	MappedMemoryRange = 6,
	FenceCreateInfo = 8,
	SemaphoreCreateInfo = 9,
	QueryPoolCreateInfo = 11,
	BufferCreateInfo = 12,
	ImageCreateInfo = 14,
	ImageViewCreateInfo = 15,
	ShaderModuleCreateInfo = 16,
	PipelineCacheCreateInfo = 17,
	PipelineShaderStageCreateInfo = 18,
	PipelineVertexInputStateCreateInfo = 19,
	PipelineInputAssemblyStateCreateInfo = 20,
	PipelineViewportStateCreateInfo = 22,
	PipelineRasterizationStateCreateInfo = 23,
	PipelineMultisampleStateCreateInfo = 24,
	PipelineDepthStencilStateCreateInfo = 25,
	PipelineColorBlendStateCreateInfo = 26,
	PipelineDynamicStateCreateInfo = 27,
	GraphicsPipelineCreateInfo = 28,
	ComputePipelineCreateInfo = 29,
	PipelineLayoutCreateInfo = 30,
	SamplerCreateInfo = 31,
	DescriptorSetLayoutCreateInfo = 32,
	DescriptorPoolCreateInfo = 33,
	DescriptorSetAllocateInfo = 34,
	WriteDescriptorSet = 35,
	CommandPoolCreateInfo = 39,
	CommandBufferAllocateInfo = 40,
	CommandBufferBeginInfo = 42,
	PhysicalDeviceVulkan11Features = 49,
	PhysicalDeviceVulkan12Features = 51,
	PhysicalDeviceVulkan13Features = 53,
	SwapchainCreateInfoKhr = 1000001000,
	PresentInfoKhr = 1000001001,
	XlibSurfaceCreateInfoKhr = 1000004000,
	WaylandSurfaceCreateInfoKhr = 1000006000,
	Win32SurfaceCreateInfoKhr = 1000009000,
	RenderingInfo = 1000044000,
	RenderingAttachmentInfo = 1000044001,
	PipelineRenderingCreateInfo = 1000044002,
	PhysicalDeviceFeatures2 = 1000059000,
	PhysicalDeviceProperties2 = 1000059001,
	PhysicalDeviceMemoryProperties2 = 1000059006,
	DebugUtilsObjectNameInfoExt = 1000128000,
	DebugUtilsLabelExt = 1000128002,
	DebugUtilsMessengerCallbackDataExt = 1000128003,
	DebugUtilsMessengerCreateInfoExt = 1000128004,
	ImageViewUsageCreateInfo = 1000117002,
	DescriptorSetLayoutBindingFlagsCreateInfo = 1000161000,
	PhysicalDeviceDescriptorIndexingProperties = 1000161002,
	PhysicalDeviceSubgroupProperties = 1000094000,
	SemaphoreTypeCreateInfo = 1000207002,
	SemaphoreWaitInfo = 1000207004,
	SemaphoreSignalInfo = 1000207005,
	PhysicalDeviceMemoryBudgetPropertiesExt = 1000237000,
	ValidationFeaturesExt = 1000247000,
	MemoryBarrier2 = 1000314000,
	BufferMemoryBarrier2 = 1000314001,
	ImageMemoryBarrier2 = 1000314002,
	DependencyInfo = 1000314003,
	SubmitInfo2 = 1000314004,
	SemaphoreSubmitInfo = 1000314005,
	CommandBufferSubmitInfo = 1000314006,
	PhysicalDevicePipelineExecutablePropertiesFeaturesKhr = 1000269000,
	PipelineInfoKhr = 1000269001,
	PipelineExecutablePropertiesKhr = 1000269002,
	PipelineExecutableInfoKhr = 1000269003,
	PipelineExecutableStatisticKhr = 1000269004,
	PhysicalDeviceMeshShaderFeaturesExt = 1000328000,
	PipelineLibraryCreateInfoKhr = 1000290000,
	PhysicalDeviceGraphicsPipelineLibraryFeaturesExt = 1000320000,
	PhysicalDeviceGraphicsPipelineLibraryPropertiesExt = 1000320001,
	GraphicsPipelineLibraryCreateInfoExt = 1000320002,
	PhysicalDeviceFaultFeaturesExt = 1000341000,
	DeviceFaultCountsExt = 1000341001,
	DeviceFaultInfoExt = 1000341002,
	MutableDescriptorTypeCreateInfoExt = 1000351002,
}

internal enum VkFormat
{
	Undefined = 0,
	R8Unorm = 9,
	R8Uint = 13,
	R8Sint = 14,
	R8G8Unorm = 16,
	R8G8B8A8Unorm = 37,
	R8G8B8A8Snorm = 38,
	R8G8B8A8Uint = 41,
	R8G8B8A8Srgb = 43,
	B8G8R8A8Unorm = 44,
	B8G8R8A8Srgb = 50,
	A2R10G10B10UnormPack32 = 58,
	A2B10G10R10UnormPack32 = 64,
	R16Unorm = 70,
	R16Uint = 74,
	R16Sfloat = 76,
	R16G16Unorm = 77,
	R16G16Sfloat = 83,
	R16G16B16A16Unorm = 91,
	R16G16B16A16Uint = 95,
	R16G16B16A16Sfloat = 97,
	R32Uint = 98,
	R32Sint = 99,
	R32Sfloat = 100,
	R32G32Uint = 101,
	R32G32Sfloat = 103,
	R32G32B32Uint = 104,
	R32G32B32Sfloat = 106,
	R32G32B32A32Uint = 107,
	R32G32B32A32Sfloat = 109,
	B10G11R11UfloatPack32 = 122,
	D16Unorm = 124,
	D32Sfloat = 126,
	D24UnormS8Uint = 129,
	D32SfloatS8Uint = 130,
	Bc1RgbaUnorm = 133,
	Bc1RgbaSrgb = 134,
	Bc3Unorm = 137,
	Bc3Srgb = 138,
	Bc4Unorm = 139,
	Bc5Unorm = 141,
	Bc6hUfloat = 143,
	Bc7Unorm = 145,
	Bc7Srgb = 146,
}

internal enum VkImageLayout
{
	Undefined = 0,
	General = 1,
	ColorAttachmentOptimal = 2,
	DepthStencilAttachmentOptimal = 3,
	DepthStencilReadOnlyOptimal = 4,
	ShaderReadOnlyOptimal = 5,
	TransferSrcOptimal = 6,
	TransferDstOptimal = 7,
	PresentSrc = 1000001002,
	ReadOnlyOptimal = 1000314000,
	AttachmentOptimal = 1000314001,
}

internal enum VkObjectType
{
	Unknown = 0,
	Queue = 4,
	Semaphore = 5,
	CommandBuffer = 6,
	DeviceMemory = 8,
	Buffer = 9,
	Image = 10,
	ImageView = 14,
	ShaderModule = 15,
	PipelineLayout = 17,
	Pipeline = 19,
	DescriptorSetLayout = 20,
	Sampler = 21,
	DescriptorSet = 23,
	CommandPool = 25,
	Swapchain = 1000001000,
}

internal enum VkDescriptorType
{
	Sampler = 0,
	CombinedImageSampler = 1,
	SampledImage = 2,
	StorageImage = 3,
	UniformBuffer = 6,
	StorageBuffer = 7,
	UniformBufferDynamic = 8,
}

internal enum VkPresentMode
{
	Immediate = 0,
	Mailbox = 1,
	Fifo = 2,
	FifoRelaxed = 3,
}

/// <summary>Flag values, grouped by the flags type they belong to.</summary>
internal static class Vk
{
	public const uint ApiVersion13 = (1u << 22) | (3u << 12);
	public const ulong WholeSize = ulong.MaxValue;
	public const uint QueueFamilyIgnored = ~0u;
	public const uint RemainingMipLevels = ~0u;
	public const uint RemainingArrayLayers = ~0u;

	public static uint ApiMajor( uint version ) => (version >> 22) & 0x7F;
	public static uint ApiMinor( uint version ) => (version >> 12) & 0x3FF;
	public static uint ApiPatch( uint version ) => version & 0xFFF;

	// VkQueueFlagBits
	public const uint QueueGraphics = 0x1;
	public const uint QueueCompute = 0x2;
	public const uint QueueTransfer = 0x4;

	// VkMemoryPropertyFlagBits
	public const uint MemoryDeviceLocal = 0x1;
	public const uint MemoryHostVisible = 0x2;
	public const uint MemoryHostCoherent = 0x4;
	public const uint MemoryHostCached = 0x8;
	public const uint MemoryLazilyAllocated = 0x10;

	// VkMemoryHeapFlagBits
	public const uint HeapDeviceLocal = 0x1;

	// VkBufferUsageFlagBits
	public const uint BufferTransferSrc = 0x1;
	public const uint BufferTransferDst = 0x2;
	public const uint BufferUniform = 0x10;
	public const uint BufferStorage = 0x20;
	public const uint BufferIndex = 0x40;
	public const uint BufferVertex = 0x80;
	public const uint BufferIndirect = 0x100;

	// VkImageUsageFlagBits
	public const uint ImageTransferSrc = 0x1;
	public const uint ImageTransferDst = 0x2;
	public const uint ImageSampled = 0x4;
	public const uint ImageStorage = 0x8;
	public const uint ImageColorAttachment = 0x10;
	public const uint ImageDepthStencilAttachment = 0x20;

	// VkImageCreateFlagBits
	public const uint ImageCreateMutableFormat = 0x8;
	public const uint ImageCreateCubeCompatible = 0x10;
	public const uint ImageCreateExtendedUsage = 0x100;

	// VkImageType, VkImageViewType
	public const int ImageType2D = 1;
	public const int ImageType3D = 2;
	public const int ViewType2D = 1;
	public const int ViewType3D = 2;
	public const int ViewTypeCube = 3;
	public const int ViewType2DArray = 5;
	public const int ViewTypeCubeArray = 6;

	// VkImageAspectFlagBits
	public const uint AspectColor = 0x1;
	public const uint AspectDepth = 0x2;
	public const uint AspectStencil = 0x4;

	// VkFormatFeatureFlagBits
	public const uint FormatFeatureSampledImage = 0x1;
	public const uint FormatFeatureStorageImage = 0x2;
	public const uint FormatFeatureColorAttachment = 0x80;
	public const uint FormatFeatureColorAttachmentBlend = 0x100;
	public const uint FormatFeatureDepthStencilAttachment = 0x200;

	// VkShaderStageFlagBits
	public const uint StageVertex = 0x1;
	public const uint StageFragment = 0x10;
	public const uint StageCompute = 0x20;
	public const uint StageTask = 0x40;
	public const uint StageMesh = 0x80;
	public const uint StageAllGraphics = 0x1F;
	public const uint StageAll = 0x7FFFFFFF;

	// VkPipelineStageFlagBits2
	public const ulong Stage2None = 0;
	public const ulong Stage2TopOfPipe = 0x1;
	public const ulong Stage2DrawIndirect = 0x2;
	public const ulong Stage2VertexInput = 0x4;
	public const ulong Stage2VertexShader = 0x8;
	public const ulong Stage2FragmentShader = 0x80;
	public const ulong Stage2EarlyFragmentTests = 0x100;
	public const ulong Stage2LateFragmentTests = 0x200;
	public const ulong Stage2ColorAttachmentOutput = 0x400;
	public const ulong Stage2ComputeShader = 0x800;
	public const ulong Stage2AllTransfer = 0x1000;
	public const ulong Stage2BottomOfPipe = 0x2000;
	public const ulong Stage2Host = 0x4000;
	public const ulong Stage2AllGraphics = 0x8000;
	public const ulong Stage2AllCommands = 0x10000;
	public const ulong Stage2PreRasterizationShaders = 0x4000000000;

	// VkAccessFlagBits2
	public const ulong Access2None = 0;
	public const ulong Access2IndirectCommandRead = 0x1;
	public const ulong Access2IndexRead = 0x2;
	public const ulong Access2VertexAttributeRead = 0x4;
	public const ulong Access2UniformRead = 0x8;
	public const ulong Access2ShaderRead = 0x20;
	public const ulong Access2ShaderWrite = 0x40;
	public const ulong Access2ColorAttachmentRead = 0x80;
	public const ulong Access2ColorAttachmentWrite = 0x100;
	public const ulong Access2DepthStencilAttachmentRead = 0x200;
	public const ulong Access2DepthStencilAttachmentWrite = 0x400;
	public const ulong Access2TransferRead = 0x800;
	public const ulong Access2TransferWrite = 0x1000;
	public const ulong Access2HostRead = 0x2000;
	public const ulong Access2HostWrite = 0x4000;
	public const ulong Access2MemoryRead = 0x8000;
	public const ulong Access2MemoryWrite = 0x10000;

	// VkDescriptorBindingFlagBits
	public const uint BindingUpdateAfterBind = 0x1;
	public const uint BindingUpdateUnusedWhilePending = 0x2;
	public const uint BindingPartiallyBound = 0x4;

	// VkDescriptorSetLayoutCreateFlagBits, VkDescriptorPoolCreateFlagBits
	public const uint SetLayoutPushDescriptor = 0x1;
	public const uint SetLayoutUpdateAfterBindPool = 0x2;
	public const uint PoolUpdateAfterBind = 0x2;

	// VkCommandPoolCreateFlagBits, VkCommandBufferUsageFlagBits
	public const uint CommandPoolTransient = 0x1;

	// VkCommandPoolResetFlagBits
	public const uint CommandPoolResetReleaseResources = 0x1;
	public const uint CommandBufferOneTimeSubmit = 0x1;

	// VkPipelineBindPoint
	public const int BindPointGraphics = 0;
	public const int BindPointCompute = 1;

	// VkAttachmentLoadOp, VkAttachmentStoreOp
	public const int LoadOpLoad = 0;
	public const int LoadOpClear = 1;
	public const int LoadOpDontCare = 2;
	public const int StoreOpStore = 0;
	public const int StoreOpDontCare = 1;

	// VkRenderingFlagBits
	public const uint RenderingSuspending = 0x2;
	public const uint RenderingResuming = 0x4;

	// VkResolveModeFlagBits
	public const uint ResolveModeNone = 0;
	public const uint ResolveModeSampleZero = 0x1;
	public const uint ResolveModeAverage = 0x2;

	// VkDynamicState
	public const int DynamicViewport = 0;
	public const int DynamicScissor = 1;
	public const int DynamicDepthBias = 3;
	public const int DynamicStencilReference = 8;
	public const int DynamicStencilCompareMask = 6;
	public const int DynamicStencilWriteMask = 7;
	public const int DynamicCullMode = 1000267000;
	public const int DynamicFrontFace = 1000267001;
	public const int DynamicDepthTestEnable = 1000267006;
	public const int DynamicDepthWriteEnable = 1000267007;
	public const int DynamicDepthCompareOp = 1000267008;
	public const int DynamicStencilTestEnable = 1000267010;
	public const int DynamicStencilOp = 1000267011;
	public const int DynamicDepthBiasEnable = 1000377002;

	// VkPipelineCreateFlagBits, VkGraphicsPipelineLibraryFlagBitsEXT
	public const uint PipelineCreateLinkTimeOptimization = 0x400;
	public const uint PipelineCreateLibrary = 0x800;
	public const uint PipelineCreateRetainLinkTimeOptimizationInfo = 0x00800000;
	public const uint LibraryVertexInput = 0x1;
	public const uint LibraryPreRasterization = 0x2;
	public const uint LibraryFragmentShader = 0x4;
	public const uint LibraryFragmentOutput = 0x8;
	public const uint PipelineLayoutIndependentSets = 0x2;

	// VkSemaphoreType
	public const int SemaphoreBinary = 0;
	public const int SemaphoreTimeline = 1;

	// VkDebugUtilsMessageSeverityFlagBitsEXT, VkDebugUtilsMessageTypeFlagBitsEXT
	public const uint SeverityVerbose = 0x1;
	public const uint SeverityInfo = 0x10;
	public const uint SeverityWarning = 0x100;
	public const uint SeverityError = 0x1000;
	public const uint MessageGeneral = 0x1;
	public const uint MessageValidation = 0x2;
	public const uint MessagePerformance = 0x4;

	// VkSurfaceTransformFlagBitsKHR, VkCompositeAlphaFlagBitsKHR, VkColorSpaceKHR
	public const uint TransformIdentity = 0x1;
	public const uint CompositeAlphaOpaque = 0x1;
	public const int ColorSpaceSrgbNonlinear = 0;

	// VkColorComponentFlagBits
	public const uint ColorComponentAll = 0xF;

	// VkQueryType
	public const int QueryTimestamp = 2;
}
