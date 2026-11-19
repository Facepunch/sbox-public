using System.Runtime.InteropServices;

namespace Borealis.Gpu.Vulkan;

// Structs from vulkan_core.h and the platform headers, in declaration order. Sequential layout gives the C layout on
// every 64-bit target this runs on. Dispatchable handles are nint, non-dispatchable ones ulong, VkBool32 uint.

#pragma warning disable CS0649 // Fields are written by Vulkan

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkApplicationInfo
{
	public VkStructureType sType;
	public void* pNext;
	public byte* pApplicationName;
	public uint applicationVersion;
	public byte* pEngineName;
	public uint engineVersion;
	public uint apiVersion;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkInstanceCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public VkApplicationInfo* pApplicationInfo;
	public uint enabledLayerCount;
	public byte** ppEnabledLayerNames;
	public uint enabledExtensionCount;
	public byte** ppEnabledExtensionNames;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkExtensionProperties
{
	public fixed byte extensionName[256];
	public uint specVersion;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkLayerProperties
{
	public fixed byte layerName[256];
	public uint specVersion;
	public uint implementationVersion;
	public fixed byte description[256];
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkDebugUtilsMessengerCreateInfoEXT
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public uint messageSeverity;
	public uint messageType;
	public delegate* unmanaged< uint, uint, VkDebugUtilsMessengerCallbackDataEXT*, void*, uint > pfnUserCallback;
	public void* pUserData;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkDebugUtilsMessengerCallbackDataEXT
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public byte* pMessageIdName;
	public int messageIdNumber;
	public byte* pMessage;
	public uint queueLabelCount;
	public void* pQueueLabels;
	public uint cmdBufLabelCount;
	public void* pCmdBufLabels;
	public uint objectCount;
	public void* pObjects;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkDebugUtilsObjectNameInfoEXT
{
	public VkStructureType sType;
	public void* pNext;
	public VkObjectType objectType;
	public ulong objectHandle;
	public byte* pObjectName;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkDebugUtilsLabelEXT
{
	public VkStructureType sType;
	public void* pNext;
	public byte* pLabelName;
	public fixed float color[4];
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDeviceLimits
{
	public uint maxImageDimension1D;
	public uint maxImageDimension2D;
	public uint maxImageDimension3D;
	public uint maxImageDimensionCube;
	public uint maxImageArrayLayers;
	public uint maxTexelBufferElements;
	public uint maxUniformBufferRange;
	public uint maxStorageBufferRange;
	public uint maxPushConstantsSize;
	public uint maxMemoryAllocationCount;
	public uint maxSamplerAllocationCount;
	public ulong bufferImageGranularity;
	public ulong sparseAddressSpaceSize;
	public uint maxBoundDescriptorSets;
	public uint maxPerStageDescriptorSamplers;
	public uint maxPerStageDescriptorUniformBuffers;
	public uint maxPerStageDescriptorStorageBuffers;
	public uint maxPerStageDescriptorSampledImages;
	public uint maxPerStageDescriptorStorageImages;
	public uint maxPerStageDescriptorInputAttachments;
	public uint maxPerStageResources;
	public uint maxDescriptorSetSamplers;
	public uint maxDescriptorSetUniformBuffers;
	public uint maxDescriptorSetUniformBuffersDynamic;
	public uint maxDescriptorSetStorageBuffers;
	public uint maxDescriptorSetStorageBuffersDynamic;
	public uint maxDescriptorSetSampledImages;
	public uint maxDescriptorSetStorageImages;
	public uint maxDescriptorSetInputAttachments;
	public uint maxVertexInputAttributes;
	public uint maxVertexInputBindings;
	public uint maxVertexInputAttributeOffset;
	public uint maxVertexInputBindingStride;
	public uint maxVertexOutputComponents;
	public uint maxTessellationGenerationLevel;
	public uint maxTessellationPatchSize;
	public uint maxTessellationControlPerVertexInputComponents;
	public uint maxTessellationControlPerVertexOutputComponents;
	public uint maxTessellationControlPerPatchOutputComponents;
	public uint maxTessellationControlTotalOutputComponents;
	public uint maxTessellationEvaluationInputComponents;
	public uint maxTessellationEvaluationOutputComponents;
	public uint maxGeometryShaderInvocations;
	public uint maxGeometryInputComponents;
	public uint maxGeometryOutputComponents;
	public uint maxGeometryOutputVertices;
	public uint maxGeometryTotalOutputComponents;
	public uint maxFragmentInputComponents;
	public uint maxFragmentOutputAttachments;
	public uint maxFragmentDualSrcAttachments;
	public uint maxFragmentCombinedOutputResources;
	public uint maxComputeSharedMemorySize;
	public fixed uint maxComputeWorkGroupCount[3];
	public uint maxComputeWorkGroupInvocations;
	public fixed uint maxComputeWorkGroupSize[3];
	public uint subPixelPrecisionBits;
	public uint subTexelPrecisionBits;
	public uint mipmapPrecisionBits;
	public uint maxDrawIndexedIndexValue;
	public uint maxDrawIndirectCount;
	public float maxSamplerLodBias;
	public float maxSamplerAnisotropy;
	public uint maxViewports;
	public fixed uint maxViewportDimensions[2];
	public fixed float viewportBoundsRange[2];
	public uint viewportSubPixelBits;
	public nuint minMemoryMapAlignment;
	public ulong minTexelBufferOffsetAlignment;
	public ulong minUniformBufferOffsetAlignment;
	public ulong minStorageBufferOffsetAlignment;
	public int minTexelOffset;
	public uint maxTexelOffset;
	public int minTexelGatherOffset;
	public uint maxTexelGatherOffset;
	public float minInterpolationOffset;
	public float maxInterpolationOffset;
	public uint subPixelInterpolationOffsetBits;
	public uint maxFramebufferWidth;
	public uint maxFramebufferHeight;
	public uint maxFramebufferLayers;
	public uint framebufferColorSampleCounts;
	public uint framebufferDepthSampleCounts;
	public uint framebufferStencilSampleCounts;
	public uint framebufferNoAttachmentsSampleCounts;
	public uint maxColorAttachments;
	public uint sampledImageColorSampleCounts;
	public uint sampledImageIntegerSampleCounts;
	public uint sampledImageDepthSampleCounts;
	public uint sampledImageStencilSampleCounts;
	public uint storageImageSampleCounts;
	public uint maxSampleMaskWords;
	public uint timestampComputeAndGraphics;
	public float timestampPeriod;
	public uint maxClipDistances;
	public uint maxCullDistances;
	public uint maxCombinedClipAndCullDistances;
	public uint discreteQueuePriorities;
	public fixed float pointSizeRange[2];
	public fixed float lineWidthRange[2];
	public float pointSizeGranularity;
	public float lineWidthGranularity;
	public uint strictLines;
	public uint standardSampleLocations;
	public ulong optimalBufferCopyOffsetAlignment;
	public ulong optimalBufferCopyRowPitchAlignment;
	public ulong nonCoherentAtomSize;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDeviceProperties
{
	public uint apiVersion;
	public uint driverVersion;
	public uint vendorID;
	public uint deviceID;
	public int deviceType;
	public fixed byte deviceName[256];
	public fixed byte pipelineCacheUUID[16];
	public VkPhysicalDeviceLimits limits;
	public fixed uint sparseProperties[5];
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDeviceProperties2
{
	public VkStructureType sType;
	public void* pNext;
	public VkPhysicalDeviceProperties properties;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDeviceSubgroupProperties
{
	public VkStructureType sType;
	public void* pNext;
	public uint subgroupSize;
	public uint supportedStages;
	public uint supportedOperations;
	public uint quadOperationsInAllStages;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDeviceDriverProperties
{
	public VkStructureType sType;
	public void* pNext;
	public uint driverID;
	public fixed byte driverName[256];
	public fixed byte driverInfo[256];
	public uint conformanceVersion;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDeviceDescriptorIndexingProperties
{
	public VkStructureType sType;
	public void* pNext;
	public uint maxUpdateAfterBindDescriptorsInAllPools;
	public uint shaderUniformBufferArrayNonUniformIndexingNative;
	public uint shaderSampledImageArrayNonUniformIndexingNative;
	public uint shaderStorageBufferArrayNonUniformIndexingNative;
	public uint shaderStorageImageArrayNonUniformIndexingNative;
	public uint shaderInputAttachmentArrayNonUniformIndexingNative;
	public uint robustBufferAccessUpdateAfterBind;
	public uint quadDivergentImplicitLod;
	public uint maxPerStageDescriptorUpdateAfterBindSamplers;
	public uint maxPerStageDescriptorUpdateAfterBindUniformBuffers;
	public uint maxPerStageDescriptorUpdateAfterBindStorageBuffers;
	public uint maxPerStageDescriptorUpdateAfterBindSampledImages;
	public uint maxPerStageDescriptorUpdateAfterBindStorageImages;
	public uint maxPerStageDescriptorUpdateAfterBindInputAttachments;
	public uint maxPerStageUpdateAfterBindResources;
	public uint maxDescriptorSetUpdateAfterBindSamplers;
	public uint maxDescriptorSetUpdateAfterBindUniformBuffers;
	public uint maxDescriptorSetUpdateAfterBindUniformBuffersDynamic;
	public uint maxDescriptorSetUpdateAfterBindStorageBuffers;
	public uint maxDescriptorSetUpdateAfterBindStorageBuffersDynamic;
	public uint maxDescriptorSetUpdateAfterBindSampledImages;
	public uint maxDescriptorSetUpdateAfterBindStorageImages;
	public uint maxDescriptorSetUpdateAfterBindInputAttachments;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkValidationFeaturesEXT
{
	public VkStructureType sType;
	public void* pNext;
	public uint enabledValidationFeatureCount;
	public int* pEnabledValidationFeatures;
	public uint disabledValidationFeatureCount;
	public int* pDisabledValidationFeatures;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkLayerSettingEXT
{
	public byte* pLayerName;
	public byte* pSettingName;
	public int type;
	public uint valueCount;
	public void* pValues;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkLayerSettingsCreateInfoEXT
{
	public VkStructureType sType;
	public void* pNext;
	public uint settingCount;
	public VkLayerSettingEXT* pSettings;
}

/// <summary>VkPhysicalDeviceFeatures: 55 VkBool32s, indexed by <see cref="VkFeature"/>.</summary>
[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDeviceFeatures
{
	public fixed uint values[55];
}

internal enum VkFeature
{
	FullDrawIndexUint32 = 1,
	ImageCubeArray = 2,
	IndependentBlend = 3,
	GeometryShader = 4,
	SampleRateShading = 6,
	MultiDrawIndirect = 9,
	DrawIndirectFirstInstance = 10,
	DepthClamp = 11,
	DepthBiasClamp = 12,
	FillModeNonSolid = 13,
	SamplerAnisotropy = 19,
	TextureCompressionBc = 22,
	OcclusionQueryPrecise = 23,
	PipelineStatisticsQuery = 24,
	FragmentStoresAndAtomics = 26,
	ShaderImageGatherExtended = 28,
	ShaderStorageImageExtendedFormats = 29,
	ShaderStorageImageReadWithoutFormat = 31,
	ShaderStorageImageWriteWithoutFormat = 32,
	ShaderUniformBufferArrayDynamicIndexing = 33,
	ShaderSampledImageArrayDynamicIndexing = 34,
	ShaderStorageBufferArrayDynamicIndexing = 35,
	ShaderStorageImageArrayDynamicIndexing = 36,
	ShaderClipDistance = 37,
	ShaderInt64 = 40,
	ShaderInt16 = 41,
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDeviceFeatures2
{
	public VkStructureType sType;
	public void* pNext;
	public VkPhysicalDeviceFeatures features;
}

/// <summary>VkPhysicalDeviceVulkan11Features: 12 VkBool32s.</summary>
[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDeviceVulkan11Features
{
	public VkStructureType sType;
	public void* pNext;
	public fixed uint values[12];

	public const int ShaderDrawParameters = 11;
}

/// <summary>VkPhysicalDeviceVulkan12Features: 47 VkBool32s.</summary>
[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDeviceVulkan12Features
{
	public VkStructureType sType;
	public void* pNext;
	public fixed uint values[47];

	public const int DrawIndirectCount = 1;
	public const int ShaderFloat16 = 7;
	public const int ShaderInt8 = 8;
	public const int DescriptorIndexing = 9;
	public const int ShaderSampledImageArrayNonUniformIndexing = 14;
	public const int ShaderStorageBufferArrayNonUniformIndexing = 15;
	public const int ShaderStorageImageArrayNonUniformIndexing = 16;
	public const int DescriptorBindingSampledImageUpdateAfterBind = 21;
	public const int DescriptorBindingStorageImageUpdateAfterBind = 22;
	public const int DescriptorBindingStorageBufferUpdateAfterBind = 23;
	public const int DescriptorBindingUpdateUnusedWhilePending = 26;
	public const int DescriptorBindingPartiallyBound = 27;
	public const int DescriptorBindingVariableDescriptorCount = 28;
	public const int RuntimeDescriptorArray = 29;
	public const int ScalarBlockLayout = 31;
	public const int UniformBufferStandardLayout = 33;
	public const int SeparateDepthStencilLayouts = 35;
	public const int HostQueryReset = 36;
	public const int TimelineSemaphore = 37;
	public const int BufferDeviceAddress = 38;
	public const int VulkanMemoryModel = 41;
	public const int VulkanMemoryModelDeviceScope = 42;
}

/// <summary>VkPhysicalDeviceVulkan13Features: 15 VkBool32s.</summary>
[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDeviceVulkan13Features
{
	public VkStructureType sType;
	public void* pNext;
	public fixed uint values[15];

	public const int ShaderDemoteToHelperInvocation = 5;
	public const int SubgroupSizeControl = 7;
	public const int Synchronization2 = 9;
	public const int DynamicRendering = 12;
	public const int Maintenance4 = 14;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDeviceMutableDescriptorTypeFeaturesEXT
{
	public VkStructureType sType;
	public void* pNext;
	public uint mutableDescriptorType;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkQueueFamilyProperties
{
	public uint queueFlags;
	public uint queueCount;
	public uint timestampValidBits;
	public VkExtent3D minImageTransferGranularity;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkExtent2D
{
	public uint width;
	public uint height;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkExtent3D
{
	public uint width;
	public uint height;
	public uint depth;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkOffset2D
{
	public int x;
	public int y;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkOffset3D
{
	public int x;
	public int y;
	public int z;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkRect2D
{
	public VkOffset2D offset;
	public VkExtent2D extent;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkViewport
{
	public float x;
	public float y;
	public float width;
	public float height;
	public float minDepth;
	public float maxDepth;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkMemoryType
{
	public uint propertyFlags;
	public uint heapIndex;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkMemoryHeap
{
	public ulong size;
	public uint flags;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDeviceMemoryProperties
{
	public uint memoryTypeCount;
	public fixed byte memoryTypes[32 * 8];
	public uint memoryHeapCount;
	public fixed byte memoryHeaps[16 * 16];

	public VkMemoryType Type( int i )
	{
		fixed ( byte* p = memoryTypes ) return ((VkMemoryType*)p)[i];
	}

	public VkMemoryHeap Heap( int i )
	{
		fixed ( byte* p = memoryHeaps ) return ((VkMemoryHeap*)p)[i];
	}
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDeviceMemoryProperties2
{
	public VkStructureType sType;
	public void* pNext;
	public VkPhysicalDeviceMemoryProperties memoryProperties;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDeviceMemoryBudgetPropertiesEXT
{
	public VkStructureType sType;
	public void* pNext;
	public fixed ulong heapBudget[16];
	public fixed ulong heapUsage[16];
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkFormatProperties
{
	public uint linearTilingFeatures;
	public uint optimalTilingFeatures;
	public uint bufferFeatures;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkDeviceQueueCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public uint queueFamilyIndex;
	public uint queueCount;
	public float* pQueuePriorities;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkDeviceCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public uint queueCreateInfoCount;
	public VkDeviceQueueCreateInfo* pQueueCreateInfos;
	public uint enabledLayerCount;
	public byte** ppEnabledLayerNames;
	public uint enabledExtensionCount;
	public byte** ppEnabledExtensionNames;
	public VkPhysicalDeviceFeatures* pEnabledFeatures;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkMemoryAllocateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public ulong allocationSize;
	public uint memoryTypeIndex;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkMemoryRequirements
{
	public ulong size;
	public ulong alignment;
	public uint memoryTypeBits;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkMappedMemoryRange
{
	public VkStructureType sType;
	public void* pNext;
	public ulong memory;
	public ulong offset;
	public ulong size;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkBufferCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public ulong size;
	public uint usage;
	public int sharingMode;
	public uint queueFamilyIndexCount;
	public uint* pQueueFamilyIndices;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkImageCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public int imageType;
	public VkFormat format;
	public VkExtent3D extent;
	public uint mipLevels;
	public uint arrayLayers;
	public uint samples;
	public int tiling;
	public uint usage;
	public int sharingMode;
	public uint queueFamilyIndexCount;
	public uint* pQueueFamilyIndices;
	public VkImageLayout initialLayout;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkComponentMapping
{
	public int r;
	public int g;
	public int b;
	public int a;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkImageSubresourceRange
{
	public uint aspectMask;
	public uint baseMipLevel;
	public uint levelCount;
	public uint baseArrayLayer;
	public uint layerCount;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkImageSubresourceLayers
{
	public uint aspectMask;
	public uint mipLevel;
	public uint baseArrayLayer;
	public uint layerCount;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkImageViewCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public ulong image;
	public int viewType;
	public VkFormat format;
	public VkComponentMapping components;
	public VkImageSubresourceRange subresourceRange;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkImageViewUsageCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint usage;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkSamplerCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public int magFilter;
	public int minFilter;
	public int mipmapMode;
	public int addressModeU;
	public int addressModeV;
	public int addressModeW;
	public float mipLodBias;
	public uint anisotropyEnable;
	public float maxAnisotropy;
	public uint compareEnable;
	public int compareOp;
	public float minLod;
	public float maxLod;
	public int borderColor;
	public uint unnormalizedCoordinates;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkShaderModuleCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public nuint codeSize;
	public uint* pCode;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPipelineCacheCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public nuint initialDataSize;
	public void* pInitialData;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkSpecializationMapEntry
{
	public uint constantID;
	public uint offset;
	public nuint size;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkSpecializationInfo
{
	public uint mapEntryCount;
	public VkSpecializationMapEntry* pMapEntries;
	public nuint dataSize;
	public void* pData;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPipelineShaderStageCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public uint stage;
	public ulong module;
	public byte* pName;
	public VkSpecializationInfo* pSpecializationInfo;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkVertexInputBindingDescription
{
	public uint binding;
	public uint stride;
	public int inputRate;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkVertexInputAttributeDescription
{
	public uint location;
	public uint binding;
	public VkFormat format;
	public uint offset;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPipelineVertexInputStateCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public uint vertexBindingDescriptionCount;
	public VkVertexInputBindingDescription* pVertexBindingDescriptions;
	public uint vertexAttributeDescriptionCount;
	public VkVertexInputAttributeDescription* pVertexAttributeDescriptions;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPipelineInputAssemblyStateCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public int topology;
	public uint primitiveRestartEnable;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPipelineViewportStateCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public uint viewportCount;
	public VkViewport* pViewports;
	public uint scissorCount;
	public VkRect2D* pScissors;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPipelineRasterizationStateCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public uint depthClampEnable;
	public uint rasterizerDiscardEnable;
	public int polygonMode;
	public uint cullMode;
	public int frontFace;
	public uint depthBiasEnable;
	public float depthBiasConstantFactor;
	public float depthBiasClamp;
	public float depthBiasSlopeFactor;
	public float lineWidth;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPipelineMultisampleStateCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public uint rasterizationSamples;
	public uint sampleShadingEnable;
	public float minSampleShading;
	public uint* pSampleMask;
	public uint alphaToCoverageEnable;
	public uint alphaToOneEnable;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkStencilOpState
{
	public int failOp;
	public int passOp;
	public int depthFailOp;
	public int compareOp;
	public uint compareMask;
	public uint writeMask;
	public uint reference;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPipelineDepthStencilStateCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public uint depthTestEnable;
	public uint depthWriteEnable;
	public int depthCompareOp;
	public uint depthBoundsTestEnable;
	public uint stencilTestEnable;
	public VkStencilOpState front;
	public VkStencilOpState back;
	public float minDepthBounds;
	public float maxDepthBounds;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkPipelineColorBlendAttachmentState
{
	public uint blendEnable;
	public int srcColorBlendFactor;
	public int dstColorBlendFactor;
	public int colorBlendOp;
	public int srcAlphaBlendFactor;
	public int dstAlphaBlendFactor;
	public int alphaBlendOp;
	public uint colorWriteMask;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPipelineColorBlendStateCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public uint logicOpEnable;
	public int logicOp;
	public uint attachmentCount;
	public VkPipelineColorBlendAttachmentState* pAttachments;
	public fixed float blendConstants[4];
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPipelineDynamicStateCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public uint dynamicStateCount;
	public int* pDynamicStates;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPipelineRenderingCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint viewMask;
	public uint colorAttachmentCount;
	public VkFormat* pColorAttachmentFormats;
	public VkFormat depthAttachmentFormat;
	public VkFormat stencilAttachmentFormat;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkGraphicsPipelineCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public uint stageCount;
	public VkPipelineShaderStageCreateInfo* pStages;
	public VkPipelineVertexInputStateCreateInfo* pVertexInputState;
	public VkPipelineInputAssemblyStateCreateInfo* pInputAssemblyState;
	public void* pTessellationState;
	public VkPipelineViewportStateCreateInfo* pViewportState;
	public VkPipelineRasterizationStateCreateInfo* pRasterizationState;
	public VkPipelineMultisampleStateCreateInfo* pMultisampleState;
	public VkPipelineDepthStencilStateCreateInfo* pDepthStencilState;
	public VkPipelineColorBlendStateCreateInfo* pColorBlendState;
	public VkPipelineDynamicStateCreateInfo* pDynamicState;
	public ulong layout;
	public ulong renderPass;
	public uint subpass;
	public ulong basePipelineHandle;
	public int basePipelineIndex;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkGraphicsPipelineLibraryCreateInfoEXT
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPipelineLibraryCreateInfoKHR
{
	public VkStructureType sType;
	public void* pNext;
	public uint libraryCount;
	public ulong* pLibraries;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDeviceGraphicsPipelineLibraryFeaturesEXT
{
	public VkStructureType sType;
	public void* pNext;
	public uint graphicsPipelineLibrary;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDeviceGraphicsPipelineLibraryPropertiesEXT
{
	public VkStructureType sType;
	public void* pNext;
	public uint graphicsPipelineLibraryFastLinking;
	public uint graphicsPipelineLibraryIndependentInterpolationDecoration;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkComputePipelineCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public VkPipelineShaderStageCreateInfo stage;
	public ulong layout;
	public ulong basePipelineHandle;
	public int basePipelineIndex;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkPushConstantRange
{
	public uint stageFlags;
	public uint offset;
	public uint size;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPipelineLayoutCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public uint setLayoutCount;
	public ulong* pSetLayouts;
	public uint pushConstantRangeCount;
	public VkPushConstantRange* pPushConstantRanges;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkDescriptorSetLayoutBinding
{
	public uint binding;
	public VkDescriptorType descriptorType;
	public uint descriptorCount;
	public uint stageFlags;
	public ulong* pImmutableSamplers;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkDescriptorSetLayoutCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public uint bindingCount;
	public VkDescriptorSetLayoutBinding* pBindings;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkDescriptorSetLayoutBindingFlagsCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint bindingCount;
	public uint* pBindingFlags;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkDescriptorPoolSize
{
	public VkDescriptorType type;
	public uint descriptorCount;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkDescriptorPoolCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public uint maxSets;
	public uint poolSizeCount;
	public VkDescriptorPoolSize* pPoolSizes;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkDescriptorSetAllocateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public ulong descriptorPool;
	public uint descriptorSetCount;
	public ulong* pSetLayouts;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkDescriptorImageInfo
{
	public ulong sampler;
	public ulong imageView;
	public VkImageLayout imageLayout;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkDescriptorBufferInfo
{
	public ulong buffer;
	public ulong offset;
	public ulong range;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkWriteDescriptorSet
{
	public VkStructureType sType;
	public void* pNext;
	public ulong dstSet;
	public uint dstBinding;
	public uint dstArrayElement;
	public uint descriptorCount;
	public VkDescriptorType descriptorType;
	public VkDescriptorImageInfo* pImageInfo;
	public VkDescriptorBufferInfo* pBufferInfo;
	public ulong* pTexelBufferView;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkCommandPoolCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public uint queueFamilyIndex;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkCommandBufferAllocateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public ulong commandPool;
	public int level;
	public uint commandBufferCount;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkCommandBufferBeginInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public void* pInheritanceInfo;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkSemaphoreCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkSemaphoreTypeCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public int semaphoreType;
	public ulong initialValue;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkSemaphoreSignalInfo
{
	public VkStructureType sType;
	public void* pNext;
	public ulong semaphore;
	public ulong value;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkSemaphoreWaitInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public uint semaphoreCount;
	public ulong* pSemaphores;
	public ulong* pValues;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkSemaphoreSubmitInfo
{
	public VkStructureType sType;
	public void* pNext;
	public ulong semaphore;
	public ulong value;
	public ulong stageMask;
	public uint deviceIndex;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkCommandBufferSubmitInfo
{
	public VkStructureType sType;
	public void* pNext;
	public nint commandBuffer;
	public uint deviceMask;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkSubmitInfo2
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public uint waitSemaphoreInfoCount;
	public VkSemaphoreSubmitInfo* pWaitSemaphoreInfos;
	public uint commandBufferInfoCount;
	public VkCommandBufferSubmitInfo* pCommandBufferInfos;
	public uint signalSemaphoreInfoCount;
	public VkSemaphoreSubmitInfo* pSignalSemaphoreInfos;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkMemoryBarrier2
{
	public VkStructureType sType;
	public void* pNext;
	public ulong srcStageMask;
	public ulong srcAccessMask;
	public ulong dstStageMask;
	public ulong dstAccessMask;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkBufferMemoryBarrier2
{
	public VkStructureType sType;
	public void* pNext;
	public ulong srcStageMask;
	public ulong srcAccessMask;
	public ulong dstStageMask;
	public ulong dstAccessMask;
	public uint srcQueueFamilyIndex;
	public uint dstQueueFamilyIndex;
	public ulong buffer;
	public ulong offset;
	public ulong size;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkImageMemoryBarrier2
{
	public VkStructureType sType;
	public void* pNext;
	public ulong srcStageMask;
	public ulong srcAccessMask;
	public ulong dstStageMask;
	public ulong dstAccessMask;
	public VkImageLayout oldLayout;
	public VkImageLayout newLayout;
	public uint srcQueueFamilyIndex;
	public uint dstQueueFamilyIndex;
	public ulong image;
	public VkImageSubresourceRange subresourceRange;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkDependencyInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint dependencyFlags;
	public uint memoryBarrierCount;
	public VkMemoryBarrier2* pMemoryBarriers;
	public uint bufferMemoryBarrierCount;
	public VkBufferMemoryBarrier2* pBufferMemoryBarriers;
	public uint imageMemoryBarrierCount;
	public VkImageMemoryBarrier2* pImageMemoryBarriers;
}

[StructLayout( LayoutKind.Explicit, Size = 16 )]
internal unsafe struct VkClearValue
{
	[FieldOffset( 0 )] public fixed float color[4];
	[FieldOffset( 0 )] public float depth;
	[FieldOffset( 4 )] public uint stencil;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkRenderingAttachmentInfo
{
	public VkStructureType sType;
	public void* pNext;
	public ulong imageView;
	public VkImageLayout imageLayout;
	public uint resolveMode;
	public ulong resolveImageView;
	public VkImageLayout resolveImageLayout;
	public int loadOp;
	public int storeOp;
	public VkClearValue clearValue;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkRenderingInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public VkRect2D renderArea;
	public uint layerCount;
	public uint viewMask;
	public uint colorAttachmentCount;
	public VkRenderingAttachmentInfo* pColorAttachments;
	public VkRenderingAttachmentInfo* pDepthAttachment;
	public VkRenderingAttachmentInfo* pStencilAttachment;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkBufferCopy
{
	public ulong srcOffset;
	public ulong dstOffset;
	public ulong size;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkBufferImageCopy
{
	public ulong bufferOffset;
	public uint bufferRowLength;
	public uint bufferImageHeight;
	public VkImageSubresourceLayers imageSubresource;
	public VkOffset3D imageOffset;
	public VkExtent3D imageExtent;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkImageCopy
{
	public VkImageSubresourceLayers srcSubresource;
	public VkOffset3D srcOffset;
	public VkImageSubresourceLayers dstSubresource;
	public VkOffset3D dstOffset;
	public VkExtent3D extent;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkImageBlit
{
	public VkImageSubresourceLayers srcSubresource;
	public VkOffset3D srcOffset0;
	public VkOffset3D srcOffset1;
	public VkImageSubresourceLayers dstSubresource;
	public VkOffset3D dstOffset0;
	public VkOffset3D dstOffset1;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkImageResolve
{
	public VkImageSubresourceLayers srcSubresource;
	public VkOffset3D srcOffset;
	public VkImageSubresourceLayers dstSubresource;
	public VkOffset3D dstOffset;
	public VkExtent3D extent;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkQueryPoolCreateInfo
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public int queryType;
	public uint queryCount;
	public uint pipelineStatistics;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkSurfaceCapabilitiesKHR
{
	public uint minImageCount;
	public uint maxImageCount;
	public VkExtent2D currentExtent;
	public VkExtent2D minImageExtent;
	public VkExtent2D maxImageExtent;
	public uint maxImageArrayLayers;
	public uint supportedTransforms;
	public uint currentTransform;
	public uint supportedCompositeAlpha;
	public uint supportedUsageFlags;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkSurfaceFormatKHR
{
	public VkFormat format;
	public int colorSpace;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkSwapchainCreateInfoKHR
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public ulong surface;
	public uint minImageCount;
	public VkFormat imageFormat;
	public int imageColorSpace;
	public VkExtent2D imageExtent;
	public uint imageArrayLayers;
	public uint imageUsage;
	public int imageSharingMode;
	public uint queueFamilyIndexCount;
	public uint* pQueueFamilyIndices;
	public uint preTransform;
	public uint compositeAlpha;
	public VkPresentMode presentMode;
	public uint clipped;
	public ulong oldSwapchain;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPresentInfoKHR
{
	public VkStructureType sType;
	public void* pNext;
	public uint waitSemaphoreCount;
	public ulong* pWaitSemaphores;
	public uint swapchainCount;
	public ulong* pSwapchains;
	public uint* pImageIndices;
	public VkResult* pResults;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkWin32SurfaceCreateInfoKHR
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public nint hinstance;
	public nint hwnd;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkXlibSurfaceCreateInfoKHR
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public nint dpy;
	public nuint window;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkWaylandSurfaceCreateInfoKHR
{
	public VkStructureType sType;
	public void* pNext;
	public uint flags;
	public nint display;
	public nint surface;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDeviceMeshShaderFeaturesEXT
{
	public VkStructureType sType;
	public void* pNext;
	public uint taskShader;
	public uint meshShader;
	public uint multiviewMeshShader;
	public uint primitiveFragmentShadingRateMeshShader;
	public uint meshShaderQueries;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDeviceFaultFeaturesEXT
{
	public VkStructureType sType;
	public void* pNext;
	public uint deviceFault;
	public uint deviceFaultVendorBinary;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkDeviceFaultCountsEXT
{
	public VkStructureType sType;
	public void* pNext;
	public uint addressInfoCount;
	public uint vendorInfoCount;
	public ulong vendorBinarySize;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VkDeviceFaultAddressInfoEXT
{
	public int addressType;
	public ulong reportedAddress;
	public ulong addressPrecision;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkDeviceFaultVendorInfoEXT
{
	public fixed byte description[256];
	public ulong vendorFaultCode;
	public ulong vendorFaultData;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkDeviceFaultInfoEXT
{
	public VkStructureType sType;
	public void* pNext;
	public fixed byte description[256];
	public VkDeviceFaultAddressInfoEXT* pAddressInfos;
	public VkDeviceFaultVendorInfoEXT* pVendorInfos;
	public void* pVendorBinaryData;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPhysicalDevicePipelineExecutablePropertiesFeaturesKHR
{
	public VkStructureType sType;
	public void* pNext;
	public uint pipelineExecutableInfo;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPipelineInfoKHR
{
	public VkStructureType sType;
	public void* pNext;
	public ulong pipeline;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPipelineExecutablePropertiesKHR
{
	public VkStructureType sType;
	public void* pNext;
	public uint stages;
	public fixed byte name[256];
	public fixed byte description[256];
	public uint subgroupSize;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPipelineExecutableInfoKHR
{
	public VkStructureType sType;
	public void* pNext;
	public ulong pipeline;
	public uint executableIndex;
}

/// <summary>A statistic: its format is 0 (bool32), 1 (int64), 2 (uint64) or 3 (float64), its value the union's 8 bytes.</summary>
[StructLayout( LayoutKind.Sequential )]
internal unsafe struct VkPipelineExecutableStatisticKHR
{
	public VkStructureType sType;
	public void* pNext;
	public fixed byte name[256];
	public fixed byte description[256];
	public int format;
	public ulong value;
}

#pragma warning restore CS0649
