using Basalt.Gpu.Vulkan;

namespace Basalt.Gpu;

/// <summary>
/// A descriptor set that lives until it's destroyed (<see cref="Device.CreateBindingSet"/>), written once and bound by
/// <see cref="CommandList.SetBindingSet"/> as often as needed. Default is none.
/// </summary>
public readonly record struct BindingSetHandle( uint Index, uint Generation )
{
	/// <summary>Whether this names a set (it may since have been destroyed).</summary>
	public bool IsValid => Generation != 0;
}

internal struct BindingSetRecord
{
	public ulong Set;
	public ulong SetLayout;
}

public sealed unsafe partial class Device
{
	internal readonly HandlePool<BindingSetRecord> BindingSets = new( "binding set" );
	internal ref BindingSetRecord Get( BindingSetHandle handle ) => ref BindingSets.Get( handle.Index, handle.Generation );

	// Persistent sets come from pools that are never reset; a destroyed set goes back to a free list for its layout once
	// the GPU is done with it, so pools need no free flag
	readonly List<ulong> bindingSetPools = new();
	readonly Dictionary<ulong, Stack<ulong>> freeBindingSets = new();

	/// <summary>
	/// Make a persistent descriptor set of <paramref name="layout"/> holding <paramref name="bindings"/>. Its contents stay
	/// until <see cref="WriteBindingSet"/> or <see cref="Destroy(BindingSetHandle)"/>.
	/// </summary>
	public BindingSetHandle CreateBindingSet( BindingLayoutHandle layout, ReadOnlySpan<Binding> bindings )
	{
		var setLayout = Get( layout ).SetLayout;
		ulong set;
		lock ( bindingSetPools )
		{
			set = freeBindingSets.TryGetValue( setLayout, out var free ) && free.Count > 0 ? free.Pop() : AllocatePersistentSet( setLayout );
		}

		WriteDescriptors( set, bindings );
		var (index, generation) = BindingSets.Add( new BindingSetRecord { Set = set, SetLayout = setLayout } );
		return new BindingSetHandle( index, generation );
	}

	/// <summary>
	/// Rewrite some of a persistent set's bindings. The caller makes sure no submitted work that hasn't finished uses it.
	/// </summary>
	public void WriteBindingSet( BindingSetHandle bindingSet, ReadOnlySpan<Binding> bindings ) => WriteDescriptors( Get( bindingSet ).Set, bindings );

	/// <summary>Destroy a persistent set once the GPU is done with it.</summary>
	public void Destroy( BindingSetHandle bindingSet )
	{
		var record = BindingSets.Remove( bindingSet.Index, bindingSet.Generation );
		Defer( () =>
		{
			lock ( bindingSetPools )
			{
				if ( !freeBindingSets.TryGetValue( record.SetLayout, out var free ) ) freeBindingSets[record.SetLayout] = free = new();
				free.Push( record.Set );
			}
		} );
	}

	/// <summary>
	/// <see cref="DefaultViewSet"/> over zeroed buffers of its own, so a pipeline whose shaders read the view set can draw
	/// on any list.
	/// </summary>
	void CreateDefaultViewSet()
	{
		const int uniformSize = 16 * 1024, storageSize = 64 * 1024;
		var uniform = CreateBuffer( new BufferInfo { Size = uniformSize, Usage = BufferUsage.Uniform, Name = "View set placeholder constants" } );
		var storage = CreateBuffer( new BufferInfo { Size = storageSize, Usage = BufferUsage.Storage, Name = "View set placeholder buffer" } );
		var zeros = new byte[storageSize];
		WriteBuffer( uniform, zeros.AsSpan( 0, uniformSize ) );
		WriteBuffer( storage, zeros );

		var bindings = new Binding[ShaderAbi.ViewSetBindings.Length];
		for ( int i = 0; i < bindings.Length; i++ )
		{
			var (binding, type, _) = ShaderAbi.ViewSetBindings[i];
			bindings[i] = type == BindingType.UniformBuffer ? Binding.Uniform( binding, uniform, 0, uniformSize ) : Binding.Storage( binding, storage );
		}

		DefaultViewSet = CreateBindingSet( ViewSetLayout, bindings );
	}

	ulong AllocatePersistentSet( ulong setLayout )
	{
		for ( int attempt = 0; ; attempt++ )
		{
			if ( bindingSetPools.Count > 0 )
			{
				var pool = bindingSetPools[^1];
				var allocate = new VkDescriptorSetAllocateInfo { sType = VkStructureType.DescriptorSetAllocateInfo, descriptorPool = pool, descriptorSetCount = 1, pSetLayouts = &setLayout };
				ulong set;
				var result = Vk.vkAllocateDescriptorSets( Vk.Device, &allocate, &set );
				if ( result == VkResult.Success ) return set;
				if ( result is not (VkResult.ErrorOutOfPoolMemory or VkResult.ErrorFragmentedPool) || attempt > 0 ) result.Check( "vkAllocateDescriptorSets" );
			}

			bindingSetPools.Add( CreatePersistentPool() );
		}
	}

	ulong CreatePersistentPool()
	{
		var sizes = stackalloc VkDescriptorPoolSize[6];
		sizes[0] = new VkDescriptorPoolSize { type = VkDescriptorType.UniformBuffer, descriptorCount = 2048 };
		sizes[1] = new VkDescriptorPoolSize { type = VkDescriptorType.UniformBufferDynamic, descriptorCount = 4096 };
		sizes[2] = new VkDescriptorPoolSize { type = VkDescriptorType.StorageBuffer, descriptorCount = 8192 };
		sizes[3] = new VkDescriptorPoolSize { type = VkDescriptorType.SampledImage, descriptorCount = 16384 };
		sizes[4] = new VkDescriptorPoolSize { type = VkDescriptorType.StorageImage, descriptorCount = 256 };
		sizes[5] = new VkDescriptorPoolSize { type = VkDescriptorType.Sampler, descriptorCount = 8192 };
		var info = new VkDescriptorPoolCreateInfo { sType = VkStructureType.DescriptorPoolCreateInfo, maxSets = 1024, poolSizeCount = 6, pPoolSizes = sizes };
		ulong created;
		Vk.vkCreateDescriptorPool( Vk.Device, &info, null, &created ).Check( "vkCreateDescriptorPool" );
		return created;
	}

	void DestroyBindingSetPools()
	{
		lock ( bindingSetPools )
		{
			foreach ( var pool in bindingSetPools ) Vk.vkDestroyDescriptorPool( Vk.Device, pool, null );
			bindingSetPools.Clear();
			freeBindingSets.Clear();
		}
	}

	/// <summary>Write <paramref name="bindings"/> into a descriptor set.</summary>
	internal void WriteDescriptors( ulong descriptorSet, ReadOnlySpan<Binding> bindings )
	{
		var writes = stackalloc VkWriteDescriptorSet[Math.Max( bindings.Length, 1 )];
		var buffers = stackalloc VkDescriptorBufferInfo[Math.Max( bindings.Length, 1 )];
		var images = stackalloc VkDescriptorImageInfo[Math.Max( bindings.Length, 1 )];

		for ( int i = 0; i < bindings.Length; i++ )
		{
			ref readonly var binding = ref bindings[i];
			writes[i] = new VkWriteDescriptorSet
			{
				sType = VkStructureType.WriteDescriptorSet,
				dstSet = descriptorSet,
				dstBinding = (uint)binding.Slot,
				dstArrayElement = (uint)binding.ArrayElement,
				descriptorCount = 1,
			};

			switch ( binding.Type )
			{
				case BindingType.UniformBuffer:
				case BindingType.UniformBufferDynamic:
				case BindingType.StorageBuffer:
					{
						ref var buffer = ref Get( binding.Buffer );
						buffers[i] = new VkDescriptorBufferInfo { buffer = buffer.Buffer, offset = (ulong)binding.Offset, range = binding.Size > 0 ? (ulong)binding.Size : Vulkan.Vk.WholeSize };
						writes[i].descriptorType = binding.Type switch
						{
							BindingType.UniformBuffer => VkDescriptorType.UniformBuffer,
							BindingType.UniformBufferDynamic => VkDescriptorType.UniformBufferDynamic,
							_ => VkDescriptorType.StorageBuffer,
						};
						writes[i].pBufferInfo = &buffers[i];
						break;
					}

				case BindingType.SampledTexture:
					{
						var view = binding.View.IsValid ? Get( binding.View ).View : binding.Srgb ? Get( binding.Texture ).SrgbView : Get( binding.Texture ).View;
						var imageLayout = binding.Layout == ImageLayout.Undefined ? VkImageLayout.ReadOnlyOptimal : binding.Layout.ToVk();
						images[i] = new VkDescriptorImageInfo { imageView = view, imageLayout = imageLayout };
						writes[i].descriptorType = VkDescriptorType.SampledImage;
						writes[i].pImageInfo = &images[i];
						break;
					}

				case BindingType.StorageTexture:
					{
						var view = binding.View.IsValid ? Get( binding.View ).View : Get( binding.Texture ).StorageView;
						images[i] = new VkDescriptorImageInfo { imageView = view, imageLayout = VkImageLayout.General };
						writes[i].descriptorType = VkDescriptorType.StorageImage;
						writes[i].pImageInfo = &images[i];
						break;
					}

				default:
					images[i] = new VkDescriptorImageInfo { sampler = Get( binding.Sampler ).Sampler };
					writes[i].descriptorType = VkDescriptorType.Sampler;
					writes[i].pImageInfo = &images[i];
					break;
			}
		}

		Vk.vkUpdateDescriptorSets( Vk.Device, (uint)bindings.Length, writes, 0, null );
	}
}
