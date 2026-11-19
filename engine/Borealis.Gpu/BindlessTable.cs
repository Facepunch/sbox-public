using Borealis.Gpu.Vulkan;

namespace Borealis.Gpu;

/// <summary>
/// The bindless descriptor set every pipeline has at set 4, laid out as s&amp;box's shaders declare
/// <c>g_globalLateBoundBindlessSet</c> (<see cref="ShaderAbi"/>): samplers at 29, the vertex cache at 49, pipeline
/// texture indices at 60, sampled textures at 62 and storage textures at 190. Textures take linear/sRGB pairs of slots.
/// Every binding is partially bound and update-after-bind; a freed slot is reused only once the device has finished
/// the frames that could read it (the device defers removals). Texture slots 0 and 1 are never handed out: s&amp;box's
/// shaders read a texture index of 0 as none (an image-less UI box, a disabled pipeline texture), as native's table does.
/// Once <see cref="SetDefaults"/> has run, a slot that holds nothing (slots 0 and 1, every slot not yet handed out, and
/// every slot once it's freed) points at the device's default of its kind, as native points freed slots at its error
/// texture: a draw that reads an index it was never given reads zeros, never a descriptor that was never written or whose
/// resource is gone. No slot of any array is left unwritten, so nothing relies on robustness or null descriptors.
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
	readonly SlotAllocator buffers;

	/// <summary>How many raw buffers binding 63 holds.</summary>
	public const int BufferCount = 1024;

	public ulong SetLayout { get; }
	public ulong Set { get; }

	readonly bool buffersUpdateAfterBind;

	// What a slot that holds nothing points at (SetDefaults), or 0 before it's run
	ulong defaultTexture, defaultStorage, defaultSampler, defaultBuffer, defaultBufferSize;

	/// <summary>
	/// Hands out slots from <paramref name="first"/>: never-used ones first, then freed ones.
	/// </summary>
	sealed class SlotAllocator( int capacity, int stride, string kind, int first = 0 )
	{
		readonly Stack<int> free = new();
		int next = first;

		/// <summary>How many slots the array has.</summary>
		public int Capacity => capacity;

		/// <summary>The first slot never handed out: it and every slot after it hold nothing.</summary>
		public int Next
		{
			get { lock ( free ) return next; }
		}

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
		buffersUpdateAfterBind = storageBufferUpdateAfterBind;
		textures = new SlotAllocator( textureCount, 2, "texture", first: 2 );
		samplers = new SlotAllocator( samplerCount, 1, "sampler" );
		storage = new SlotAllocator( storageCount, 1, "storage texture" );
		buffers = new SlotAllocator( BufferCount, 1, "raw buffer" );

		var all = device.StageAll;
		var bindings = stackalloc VkDescriptorSetLayoutBinding[6];
		bindings[0] = new VkDescriptorSetLayoutBinding { binding = ShaderAbi.BindlessSamplers, descriptorType = VkDescriptorType.Sampler, descriptorCount = (uint)samplerCount, stageFlags = all };
		bindings[1] = new VkDescriptorSetLayoutBinding { binding = ShaderAbi.BindlessVertexCache, descriptorType = VkDescriptorType.StorageBuffer, descriptorCount = 1, stageFlags = Vulkan.Vk.StageVertex | Vulkan.Vk.StageCompute };
		bindings[2] = new VkDescriptorSetLayoutBinding { binding = ShaderAbi.BindlessPipelineTextures, descriptorType = VkDescriptorType.StorageBuffer, descriptorCount = 1, stageFlags = all };
		bindings[3] = new VkDescriptorSetLayoutBinding { binding = ShaderAbi.BindlessTextures, descriptorType = VkDescriptorType.SampledImage, descriptorCount = (uint)textureCount, stageFlags = all };
		bindings[4] = new VkDescriptorSetLayoutBinding { binding = ShaderAbi.BindlessStorageTextures, descriptorType = VkDescriptorType.StorageImage, descriptorCount = (uint)storageCount, stageFlags = Vulkan.Vk.StageCompute };
		bindings[5] = new VkDescriptorSetLayoutBinding { binding = ShaderAbi.BindlessBuffers, descriptorType = VkDescriptorType.StorageBuffer, descriptorCount = BufferCount, stageFlags = all };

		var updatable = Vulkan.Vk.BindingPartiallyBound | Vulkan.Vk.BindingUpdateAfterBind | Vulkan.Vk.BindingUpdateUnusedWhilePending;
		var bufferFlags = storageBufferUpdateAfterBind ? updatable : Vulkan.Vk.BindingPartiallyBound;
		var flags = stackalloc uint[6] { updatable, bufferFlags, bufferFlags, updatable, updatable, bufferFlags };
		var flagsInfo = new VkDescriptorSetLayoutBindingFlagsCreateInfo { sType = VkStructureType.DescriptorSetLayoutBindingFlagsCreateInfo, bindingCount = 6, pBindingFlags = flags };

		var layoutInfo = new VkDescriptorSetLayoutCreateInfo
		{
			sType = VkStructureType.DescriptorSetLayoutCreateInfo,
			pNext = &flagsInfo,
			flags = Vulkan.Vk.SetLayoutUpdateAfterBindPool,
			bindingCount = 6,
			pBindings = bindings,
		};

		ulong layout;
		vk.vkCreateDescriptorSetLayout( vk.Device, &layoutInfo, null, &layout ).Check( "vkCreateDescriptorSetLayout (bindless)" );
		SetLayout = layout;

		var sizes = stackalloc VkDescriptorPoolSize[4];
		sizes[0] = new VkDescriptorPoolSize { type = VkDescriptorType.Sampler, descriptorCount = (uint)samplerCount };
		sizes[1] = new VkDescriptorPoolSize { type = VkDescriptorType.StorageBuffer, descriptorCount = 2 + BufferCount };
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

	public void RemoveTexture( int slot )
	{
		WriteDefaultTexture( slot );
		textures.Release( slot );
	}

	/// <summary>Point a texture slot pair at the default texture.</summary>
	void WriteDefaultTexture( int slot )
	{
		if ( defaultTexture == 0 ) return;
		var info = new VkDescriptorImageInfo { imageView = defaultTexture, imageLayout = VkImageLayout.ReadOnlyOptimal };
		Write( ShaderAbi.BindlessTextures, (uint)slot, VkDescriptorType.SampledImage, &info, null );
		Write( ShaderAbi.BindlessTextures, (uint)slot + 1, VkDescriptorType.SampledImage, &info, null );
	}

	public int AddStorage( ulong view )
	{
		var slot = storage.Allocate();
		var info = new VkDescriptorImageInfo { imageView = view, imageLayout = VkImageLayout.General };
		Write( ShaderAbi.BindlessStorageTextures, (uint)slot, VkDescriptorType.StorageImage, &info, null );
		return slot;
	}

	public void RemoveStorage( int slot )
	{
		if ( defaultStorage != 0 )
		{
			var info = new VkDescriptorImageInfo { imageView = defaultStorage, imageLayout = VkImageLayout.General };
			Write( ShaderAbi.BindlessStorageTextures, (uint)slot, VkDescriptorType.StorageImage, &info, null );
		}

		storage.Release( slot );
	}

	public int AddSampler( ulong sampler )
	{
		var slot = samplers.Allocate();
		var info = new VkDescriptorImageInfo { sampler = sampler };
		Write( ShaderAbi.BindlessSamplers, (uint)slot, VkDescriptorType.Sampler, &info, null );
		return slot;
	}

	/// <summary>Point a sampler slot at another sampler (the same sampler remade with new state).</summary>
	public void SetSampler( int slot, ulong sampler )
	{
		var info = new VkDescriptorImageInfo { sampler = sampler };
		Write( ShaderAbi.BindlessSamplers, (uint)slot, VkDescriptorType.Sampler, &info, null );
	}

	public void RemoveSampler( int slot )
	{
		if ( defaultSampler != 0 )
		{
			var info = new VkDescriptorImageInfo { sampler = defaultSampler };
			Write( ShaderAbi.BindlessSamplers, (uint)slot, VkDescriptorType.Sampler, &info, null );
		}

		samplers.Release( slot );
	}

	/// <summary>
	/// Add a range of a storage buffer to the raw buffer array (binding 63) and return its slot. Without
	/// update-after-bind for storage buffers, only while no submitted work uses the table.
	/// </summary>
	public int AddBuffer( ulong buffer, ulong offset, ulong size )
	{
		var slot = buffers.Allocate();
		var info = new VkDescriptorBufferInfo { buffer = buffer, offset = offset, range = size == 0 ? Vulkan.Vk.WholeSize : size };
		Write( ShaderAbi.BindlessBuffers, (uint)slot, VkDescriptorType.StorageBuffer, null, &info );
		return slot;
	}

	public void RemoveBuffer( int slot )
	{
		// Without update-after-bind for storage buffers the slot can't be written while other work uses the table
		if ( defaultBuffer != 0 && buffersUpdateAfterBind )
		{
			var info = new VkDescriptorBufferInfo { buffer = defaultBuffer, range = defaultBufferSize };
			Write( ShaderAbi.BindlessBuffers, (uint)slot, VkDescriptorType.StorageBuffer, null, &info );
		}

		buffers.Release( slot );
	}

	/// <summary>
	/// What slots that hold nothing point at from now on: a sampled view (in <see cref="ImageLayout.ShaderRead"/>), a
	/// storage view (in <see cref="ImageLayout.General"/>), a sampler and a storage buffer. Texture slots 0 and 1, every
	/// slot of each array not yet handed out, and the vertex cache and pipeline texture buffers are pointed at them now.
	/// Before any work that uses the table is submitted.
	/// </summary>
	public void SetDefaults( ulong texture, ulong storageView, ulong sampler, ulong buffer, ulong bufferSize )
	{
		defaultTexture = texture;
		defaultStorage = storageView;
		defaultSampler = sampler;
		defaultBuffer = buffer;
		defaultBufferSize = bufferSize;
		WriteDefaultTexture( 0 );

		var image = new VkDescriptorImageInfo { imageView = texture, imageLayout = VkImageLayout.ReadOnlyOptimal };
		WriteRest( ShaderAbi.BindlessTextures, VkDescriptorType.SampledImage, textures, image, default );
		WriteRest( ShaderAbi.BindlessStorageTextures, VkDescriptorType.StorageImage, storage, new VkDescriptorImageInfo { imageView = storageView, imageLayout = VkImageLayout.General }, default );
		WriteRest( ShaderAbi.BindlessSamplers, VkDescriptorType.Sampler, samplers, new VkDescriptorImageInfo { sampler = sampler }, default );
		WriteRest( ShaderAbi.BindlessBuffers, VkDescriptorType.StorageBuffer, buffers, default, new VkDescriptorBufferInfo { buffer = buffer, range = bufferSize } );

		SetBuffer( ShaderAbi.BindlessVertexCache, buffer, bufferSize );
		SetBuffer( ShaderAbi.BindlessPipelineTextures, buffer, bufferSize );
	}

	/// <summary>Point every slot of an array from the first never handed out to its end at a default, in one write.</summary>
	void WriteRest( uint binding, VkDescriptorType type, SlotAllocator slots, VkDescriptorImageInfo image, VkDescriptorBufferInfo buffer )
	{
		var first = slots.Next;
		var count = slots.Capacity - first;
		if ( count <= 0 ) return;

		var isBuffer = type == VkDescriptorType.StorageBuffer;
		var images = isBuffer ? null : new VkDescriptorImageInfo[count];
		var buffers = isBuffer ? new VkDescriptorBufferInfo[count] : null;
		if ( isBuffer ) Array.Fill( buffers, buffer );
		else Array.Fill( images, image );

		fixed ( VkDescriptorImageInfo* i = images )
		fixed ( VkDescriptorBufferInfo* b = buffers )
		{
			var write = new VkWriteDescriptorSet
			{
				sType = VkStructureType.WriteDescriptorSet,
				dstSet = Set,
				dstBinding = binding,
				dstArrayElement = (uint)first,
				descriptorCount = (uint)count,
				descriptorType = type,
				pImageInfo = i,
				pBufferInfo = b,
			};

			lock ( writeLock ) vk.vkUpdateDescriptorSets( vk.Device, 1, &write, 0, null );
		}
	}

	/// <summary>
	/// Point binding 49 (vertex cache) or 60 (pipeline texture indices) at a buffer. Only between frames: these aren't
	/// safe to change while work that reads them is in flight.
	/// </summary>
	public void SetBuffer( int binding, ulong buffer, ulong size )
	{
		var info = new VkDescriptorBufferInfo { buffer = buffer, range = size == 0 ? Vulkan.Vk.WholeSize : size };
		Write( (uint)binding, 0, VkDescriptorType.StorageBuffer, null, &info );
	}

	/// <summary>Stop pointing freed slots at the defaults: the device is going, and they go with it.</summary>
	public void ForgetDefaults() => defaultTexture = defaultStorage = defaultSampler = defaultBuffer = 0;

	public void Dispose()
	{
		vk.vkDestroyDescriptorPool( vk.Device, pool, null );
		vk.vkDestroyDescriptorSetLayout( vk.Device, SetLayout, null );
	}
}
