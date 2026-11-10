using Basalt.Gpu.Vulkan;

namespace Basalt.Gpu;

/// <summary>
/// The bindless descriptor set every pipeline has at set 4, laid out as s&amp;box's shaders declare
/// <c>g_globalLateBoundBindlessSet</c> (<see cref="ShaderAbi"/>): samplers at 29, the vertex cache at 49, pipeline
/// texture indices at 60, sampled textures at 62 and storage textures at 190. Textures take linear/sRGB pairs of slots.
/// Every binding is partially bound and update-after-bind; a freed slot is reused only once the device has finished
/// the frames that could read it (the device defers removals). Texture slots 0 and 1 are never handed out: s&amp;box's
/// shaders read a texture index of 0 as none (an image-less UI box, a disabled pipeline texture), as native's table does.
/// </summary>
internal sealed unsafe class BindlessTable : IDisposable
{
	readonly Device device;
	readonly VkDeviceApi vk;
	readonly ulong pool;
	readonly object writeLock = new();

	readonly SlotAllocator textures;
	readonly SlotAllocator samplers;
	readonly SlotAllocator storage;

	public ulong SetLayout { get; }
	public ulong Set { get; }

	/// <summary>
	/// Hands out slots from <paramref name="first"/>: never-used ones first, then freed ones.
	/// </summary>
	sealed class SlotAllocator( int capacity, int stride, string kind, int first = 0 )
	{
		readonly Stack<int> free = new();
		int next = first;

		public int Allocate()
		{
			lock ( free )
			{
				if ( free.Count > 0 ) return free.Pop();
				if ( next + stride > capacity ) throw new GpuException( $"The bindless table is out of {kind} slots ({capacity})" );
				var slot = next;
				next += stride;
				return slot;
			}
		}

		public void Release( int slot )
		{
			lock ( free ) free.Push( slot );
		}
	}

	public BindlessTable( Device device, int textureCount, int samplerCount, int storageCount, bool storageBufferUpdateAfterBind )
	{
		this.device = device;
		vk = device.Vk;
		textures = new SlotAllocator( textureCount, 2, "texture", first: 2 );
		samplers = new SlotAllocator( samplerCount, 1, "sampler" );
		storage = new SlotAllocator( storageCount, 1, "storage texture" );

		var all = Vulkan.Vk.StageAllGraphics | Vulkan.Vk.StageCompute;
		var bindings = stackalloc VkDescriptorSetLayoutBinding[5];
		bindings[0] = new VkDescriptorSetLayoutBinding { binding = ShaderAbi.BindlessSamplers, descriptorType = VkDescriptorType.Sampler, descriptorCount = (uint)samplerCount, stageFlags = all };
		bindings[1] = new VkDescriptorSetLayoutBinding { binding = ShaderAbi.BindlessVertexCache, descriptorType = VkDescriptorType.StorageBuffer, descriptorCount = 1, stageFlags = Vulkan.Vk.StageVertex | Vulkan.Vk.StageCompute };
		bindings[2] = new VkDescriptorSetLayoutBinding { binding = ShaderAbi.BindlessPipelineTextures, descriptorType = VkDescriptorType.StorageBuffer, descriptorCount = 1, stageFlags = all };
		bindings[3] = new VkDescriptorSetLayoutBinding { binding = ShaderAbi.BindlessTextures, descriptorType = VkDescriptorType.SampledImage, descriptorCount = (uint)textureCount, stageFlags = all };
		bindings[4] = new VkDescriptorSetLayoutBinding { binding = ShaderAbi.BindlessStorageTextures, descriptorType = VkDescriptorType.StorageImage, descriptorCount = (uint)storageCount, stageFlags = Vulkan.Vk.StageCompute };

		var updatable = Vulkan.Vk.BindingPartiallyBound | Vulkan.Vk.BindingUpdateAfterBind | Vulkan.Vk.BindingUpdateUnusedWhilePending;
		var bufferFlags = storageBufferUpdateAfterBind ? updatable : Vulkan.Vk.BindingPartiallyBound;
		var flags = stackalloc uint[5] { updatable, bufferFlags, bufferFlags, updatable, updatable };
		var flagsInfo = new VkDescriptorSetLayoutBindingFlagsCreateInfo { sType = VkStructureType.DescriptorSetLayoutBindingFlagsCreateInfo, bindingCount = 5, pBindingFlags = flags };

		var layoutInfo = new VkDescriptorSetLayoutCreateInfo
		{
			sType = VkStructureType.DescriptorSetLayoutCreateInfo,
			pNext = &flagsInfo,
			flags = Vulkan.Vk.SetLayoutUpdateAfterBindPool,
			bindingCount = 5,
			pBindings = bindings,
		};

		ulong layout;
		vk.vkCreateDescriptorSetLayout( vk.Device, &layoutInfo, null, &layout ).Check( "vkCreateDescriptorSetLayout (bindless)" );
		SetLayout = layout;

		var sizes = stackalloc VkDescriptorPoolSize[4];
		sizes[0] = new VkDescriptorPoolSize { type = VkDescriptorType.Sampler, descriptorCount = (uint)samplerCount };
		sizes[1] = new VkDescriptorPoolSize { type = VkDescriptorType.StorageBuffer, descriptorCount = 2 };
		sizes[2] = new VkDescriptorPoolSize { type = VkDescriptorType.SampledImage, descriptorCount = (uint)textureCount };
		sizes[3] = new VkDescriptorPoolSize { type = VkDescriptorType.StorageImage, descriptorCount = (uint)storageCount };

		var poolInfo = new VkDescriptorPoolCreateInfo
		{
			sType = VkStructureType.DescriptorPoolCreateInfo,
			flags = Vulkan.Vk.PoolUpdateAfterBind,
			maxSets = 1,
			poolSizeCount = 4,
			pPoolSizes = sizes,
		};

		ulong descriptorPool;
		vk.vkCreateDescriptorPool( vk.Device, &poolInfo, null, &descriptorPool ).Check( "vkCreateDescriptorPool (bindless)" );
		pool = descriptorPool;

		var allocate = new VkDescriptorSetAllocateInfo { sType = VkStructureType.DescriptorSetAllocateInfo, descriptorPool = pool, descriptorSetCount = 1, pSetLayouts = &layout };
		ulong set;
		vk.vkAllocateDescriptorSets( vk.Device, &allocate, &set ).Check( "vkAllocateDescriptorSets (bindless)" );
		Set = set;
		device.SetName( VkObjectType.DescriptorSet, set, "Bindless" );
	}

	void Write( uint binding, uint element, VkDescriptorType type, VkDescriptorImageInfo* image, VkDescriptorBufferInfo* buffer )
	{
		var write = new VkWriteDescriptorSet
		{
			sType = VkStructureType.WriteDescriptorSet,
			dstSet = Set,
			dstBinding = binding,
			dstArrayElement = element,
			descriptorCount = 1,
			descriptorType = type,
			pImageInfo = image,
			pBufferInfo = buffer,
		};

		lock ( writeLock ) vk.vkUpdateDescriptorSets( vk.Device, 1, &write, 0, null );
	}

	/// <summary>
	/// Add a texture's linear and sRGB views (the same view for formats without sRGB) and return the linear one's slot.
	/// </summary>
	public int AddTexture( ulong linear, ulong srgb )
	{
		var slot = textures.Allocate();
		var info = new VkDescriptorImageInfo { imageView = linear, imageLayout = VkImageLayout.ReadOnlyOptimal };
		Write( ShaderAbi.BindlessTextures, (uint)slot, VkDescriptorType.SampledImage, &info, null );
		info.imageView = srgb;
		Write( ShaderAbi.BindlessTextures, (uint)slot + 1, VkDescriptorType.SampledImage, &info, null );
		return slot;
	}

	public void RemoveTexture( int slot ) => textures.Release( slot );

	public int AddStorage( ulong view )
	{
		var slot = storage.Allocate();
		var info = new VkDescriptorImageInfo { imageView = view, imageLayout = VkImageLayout.General };
		Write( ShaderAbi.BindlessStorageTextures, (uint)slot, VkDescriptorType.StorageImage, &info, null );
		return slot;
	}

	public void RemoveStorage( int slot ) => storage.Release( slot );

	public int AddSampler( ulong sampler )
	{
		var slot = samplers.Allocate();
		var info = new VkDescriptorImageInfo { sampler = sampler };
		Write( ShaderAbi.BindlessSamplers, (uint)slot, VkDescriptorType.Sampler, &info, null );
		return slot;
	}

	public void RemoveSampler( int slot ) => samplers.Release( slot );

	/// <summary>
	/// Point binding 49 (vertex cache) or 60 (pipeline texture indices) at a buffer. Only between frames: these aren't
	/// safe to change while work that reads them is in flight.
	/// </summary>
	public void SetBuffer( int binding, ulong buffer, ulong size )
	{
		var info = new VkDescriptorBufferInfo { buffer = buffer, range = size == 0 ? Vulkan.Vk.WholeSize : size };
		Write( (uint)binding, 0, VkDescriptorType.StorageBuffer, null, &info );
	}

	public void Dispose()
	{
		vk.vkDestroyDescriptorPool( vk.Device, pool, null );
		vk.vkDestroyDescriptorSetLayout( vk.Device, SetLayout, null );
	}
}
