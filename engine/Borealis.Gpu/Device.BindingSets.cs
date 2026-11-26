using Borealis.Gpu.Vulkan;

namespace Borealis.Gpu;

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
	public BindingLayoutHandle Layout;

	/// <summary>What it holds, as last written, so a resource remade or moved in place can be written again.</summary>
	public List<Binding> Bindings;
}

public sealed unsafe partial class Device
{
	internal readonly HandlePool<BindingSetRecord> BindingSets = new( "binding set" );
	internal ref BindingSetRecord Get( BindingSetHandle handle ) => ref BindingSets.Get( handle.Index, handle.Generation );

	// Persistent sets come from pools that are never reset; a destroyed set goes back to a free list for its layout once
	// the GPU is done with it, so pools need no free flag. The lists go with their layout: the driver may give a later
	// layout the same Vulkan handle
	readonly List<ulong> bindingSetPools = new();
	readonly Dictionary<BindingLayoutHandle, Stack<ulong>> freeBindingSets = new();

	/// <summary>
	/// Make a persistent descriptor set of <paramref name="layout"/> holding <paramref name="bindings"/>. Its contents stay
	/// until <see cref="WriteBindingSet"/> or <see cref="Destroy(BindingSetHandle)"/>.
	/// </summary>
	public BindingSetHandle CreateBindingSet( BindingLayoutHandle layout, ReadOnlySpan<Binding> bindings )
	{
		var setLayout = Get( layout ).SetLayout;
		ulong set;
		List<Binding> held;
		lock ( bindingSetPools )
		{
			set = freeBindingSets.TryGetValue( layout, out var free ) && free.Count > 0 ? free.Pop() : AllocatePersistentSet( setLayout );
			held = freeBindingLists.TryPop( out var list ) ? list : new List<Binding>( bindings.Length );
		}

		var record = new BindingSetRecord { Set = set, SetLayout = setLayout, Layout = layout, Bindings = held };
		var gated = EnterGate();
		try
		{
			WriteDescriptors( set, bindings );
			Track( held, bindings );
			var (index, generation) = BindingSets.Add( record );
			return new BindingSetHandle( index, generation );
		}
		catch
		{
			FreeSet( record );
			throw;
		}
		finally
		{
			if ( gated ) ExitGate();
		}
	}

	/// <summary>
	/// Rewrite some of a persistent set's bindings. The caller makes sure no submitted work that hasn't finished uses it.
	/// </summary>
	public void WriteBindingSet( BindingSetHandle bindingSet, ReadOnlySpan<Binding> bindings )
	{
		var gated = EnterGate();
		try
		{
			ref var record = ref Get( bindingSet );
			WriteDescriptors( record.Set, bindings );
			Track( record.Bindings, bindings );
		}
		finally
		{
			if ( gated ) ExitGate();
		}
	}

	// Lists of what persistent sets hold, kept for the next set: view sets are made and destroyed every frame
	readonly Stack<List<Binding>> freeBindingLists = new();

	/// <summary>What a persistent set holds after <paramref name="bindings"/> are written to it; allocates nothing once the list has room.</summary>
	static void Track( List<Binding> held, ReadOnlySpan<Binding> bindings )
	{
		foreach ( ref readonly var binding in bindings )
		{
			var i = held.Count - 1;
			while ( i >= 0 && (held[i].Slot != binding.Slot || held[i].ArrayElement != binding.ArrayElement) ) i--;
			if ( i >= 0 ) held[i] = binding;
			else held.Add( binding );
		}
	}

	readonly List<Binding> rewrites = new();

	/// <summary>
	/// Write again every binding of every persistent set that names a resource <paramref name="names"/> picks: one remade
	/// or moved in place, whose Vulkan object changed under the same handle. Only while no work that uses the sets is
	/// pending. Allocates nothing once its scratch has room. Returns the bindings written.
	/// </summary>
	int RewriteSets<T>( T names ) where T : IBindingFilter
	{
		var written = 0;
		var limit = BindingSets.Limit;
		for ( uint index = 1; index < limit; index++ )
		{
			ref var record = ref BindingSets.At( index, out _ );
			if ( record.Set == 0 || record.Bindings is not { } held ) continue;

			rewrites.Clear();
			foreach ( var binding in held )
			{
				if ( names.Names( binding ) ) rewrites.Add( binding );
			}

			if ( rewrites.Count == 0 ) continue;
			WriteDescriptors( record.Set, System.Runtime.InteropServices.CollectionsMarshal.AsSpan( rewrites ) );
			written += rewrites.Count;
		}

		rewrites.Clear();
		return written;
	}

	/// <summary>Which bindings <see cref="RewriteSets"/> writes again.</summary>
	internal interface IBindingFilter
	{
		bool Names( in Binding binding );
	}

	/// <summary>Destroy a persistent set once the GPU is done with it.</summary>
	public void Destroy( BindingSetHandle bindingSet )
	{
		var gated = EnterGate();
		try
		{
			DeferSet( BindingSets.Remove( bindingSet.Index, bindingSet.Generation ) );
		}
		finally
		{
			if ( gated ) ExitGate();
		}
	}

	void FreeSet( in BindingSetRecord record )
	{
		lock ( bindingSetPools )
		{
			if ( record.Bindings is { } held )
			{
				held.Clear();
				freeBindingLists.Push( held );
			}

			if ( !Layouts.IsAlive( record.Layout.Index, record.Layout.Generation ) ) return;
			if ( !freeBindingSets.TryGetValue( record.Layout, out var free ) ) freeBindingSets[record.Layout] = free = new();
			free.Push( record.Set );
		}
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

	/// <summary>
	/// The resources bindless slots that hold nothing point at (<see cref="BindlessTable.SetDefaults"/>): a transparent black
	/// 1x1 texture and storage texture, a point sampler and a zeroed buffer of the device's own.
	/// </summary>
	void CreateBindlessDefaults()
	{
		const int bufferSize = 256;
		var texture = CreateTexture( TextureInfo.Texture2D( 1, 1, Format.RGBA8Unorm, name: "Bindless default texture" ) );
		WriteTexture( texture, stackalloc byte[4] );

		var storageInfo = TextureInfo.Texture2D( 1, 1, Format.RGBA8Unorm, name: "Bindless default storage texture" );
		storageInfo.Usage = TextureUsage.Storage | TextureUsage.CopyDestination;
		var storage = CreateTexture( storageInfo );
		var list = BeginCommandList( QueueType.Graphics, "Bindless defaults" );
		list.Barrier( new TextureBarrier { Texture = storage, OldLayout = ImageLayout.Undefined, NewLayout = ImageLayout.CopyDestination, SourceStage = PipelineStage.AllCommands, SourceAccess = MemoryAccess.None, DestinationStage = PipelineStage.Transfer, DestinationAccess = MemoryAccess.TransferWrite, MipCount = 1, LayerCount = 1 } );
		list.ClearTexture( storage );
		list.Barrier( new TextureBarrier { Texture = storage, OldLayout = ImageLayout.CopyDestination, NewLayout = ImageLayout.General, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.AllCommands, DestinationAccess = MemoryAccess.ShaderRead, MipCount = 1, LayerCount = 1 } );
		SubmitAndRecycle( list );

		var sampler = CreateSampler( SamplerInfo.Point() );
		var buffer = CreateBuffer( new BufferInfo { Size = bufferSize, Usage = BufferUsage.Storage, Name = "Bindless default buffer" } );
		WriteBuffer( buffer, new byte[bufferSize] );

		// What empty slots point at stays where it is, and so do their blocks
		Get( buffer ).Pinned = true;
		Get( texture ).Pinned = true;
		Get( storage ).Pinned = true;
		Allocator.Pin( Get( buffer ).Memory );
		Allocator.Pin( Get( texture ).Memory );
		Allocator.Pin( Get( storage ).Memory );
		Bindless.SetDefaults( Get( texture ).View, Get( storage ).StorageView, Get( sampler ).Sampler, Get( buffer ).Buffer, bufferSize );
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
						if ( view == 0 ) throw NoView( binding, "sampled" );
						var imageLayout = binding.Layout == ImageLayout.Undefined ? VkImageLayout.ReadOnlyOptimal : binding.Layout.ToVk();
						images[i] = new VkDescriptorImageInfo { imageView = view, imageLayout = imageLayout };
						writes[i].descriptorType = VkDescriptorType.SampledImage;
						writes[i].pImageInfo = &images[i];
						break;
					}

				case BindingType.StorageTexture:
					{
						var view = binding.View.IsValid ? Get( binding.View ).View : Get( binding.Texture ).StorageView;
						if ( view == 0 ) throw NoView( binding, "storage" );
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

	// Without the null descriptor feature an empty view is undefined behaviour on the GPU (a write through one can fault): the
	// binding is refused on the CPU instead
	ArgumentException NoView( in Binding binding, string kind ) => new( $"Binding {binding.Slot}: {Get( binding.Texture ).Info.Name ?? "a texture"} has no {kind} view (made without {(kind == "storage" ? "TextureUsage.Storage" : "TextureUsage.Sampled")})" );
}
