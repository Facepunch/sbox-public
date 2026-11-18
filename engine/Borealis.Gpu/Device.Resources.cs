using Borealis.Gpu.Memory;
using Borealis.Gpu.Vulkan;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Borealis.Gpu;

internal struct BufferRecord
{
	public ulong Buffer;
	public Allocation Memory;
	public long Size;
	public BufferUsage Usage;
	public MemoryLocation Location;
	public string Name;

	/// <summary>Its slot in the bindless raw buffer array plus one, or 0 when it isn't there.</summary>
	public int BindlessSlot;
}

/// <summary>
/// A texture's single-mip, single-layer views for rendering into, made when first drawn to.
/// </summary>
internal sealed class AttachmentViews
{
	public readonly Dictionary<int, ulong> Views = new();
}

internal struct TextureRecord
{
	public ulong Image;
	public Allocation Memory;
	public TextureInfo Info;
	public VkFormat Format;
	public uint Aspect;
	public int ArrayLayers;

	/// <summary>The default view: every mip and layer, linear format, the texture's dimension.</summary>
	public ulong View;

	/// <summary>The default view's sRGB twin, or <see cref="View"/> for formats without one.</summary>
	public ulong SrgbView;

	/// <summary>Mip 0 as a storage view, for storage textures.</summary>
	public ulong StorageView;

	/// <summary>The linear view's bindless index (sRGB is one more), or -1.</summary>
	public int BindlessIndex;

	/// <summary>The storage view's bindless storage index, or -1.</summary>
	public int StorageIndex;

	/// <summary>A swap chain image: its image belongs to the swap chain.</summary>
	public bool External;

	public AttachmentViews Attachments;
}

internal struct ViewRecord
{
	public ulong View;
	public TextureHandle Texture;
	public int BindlessIndex;
	public int StorageIndex;
}

internal struct SamplerRecord
{
	public ulong Sampler;
	public int Index;
}

internal struct ShaderRecord
{
	public ulong Module;

	/// <summary>Its SPIR-V's hash, with pipeline libraries: parts are keyed and recorded by it.</summary>
	public UInt128 Hash;
}

internal struct LayoutRecord
{
	public ulong SetLayout;
	public BindingSlot[] Slots;
}

internal struct PipelineRecord
{
	public ulong Pipeline;
	public ulong Layout;
	public int BindPoint;

	/// <summary>A graphics pipeline's dynamic render state (<see cref="RenderStateBits"/>), set when it's bound.</summary>
	public ulong RenderState;

	/// <summary>The parts it was linked from, or null for a pipeline made whole.</summary>
	public LinkedParts Parts;

	/// <summary>Its optimised link has replaced the fast one.</summary>
	public bool Optimised;

	/// <summary>Workers are making it (<see cref="GraphicsPipelineInfo.SkipUntilReady"/>): <see cref="Pipeline"/> is 0 until then.</summary>
	public bool Pending;
}

public sealed unsafe partial class Device
{
	internal readonly HandlePool<BufferRecord> Buffers = new( "buffer" );
	internal readonly HandlePool<TextureRecord> Textures = new( "texture" );
	internal readonly HandlePool<ViewRecord> Views = new( "texture view" );
	internal readonly HandlePool<SamplerRecord> Samplers = new( "sampler" );
	internal readonly HandlePool<ShaderRecord> Shaders = new( "shader" );
	internal readonly HandlePool<LayoutRecord> Layouts = new( "binding layout" );
	internal readonly HandlePool<PipelineRecord> Pipelines = new( "pipeline" );

	internal BindlessTable Bindless { get; }

	ulong pipelineCache;
	ulong emptySetLayout;

	/// <summary>The view set's layout (<see cref="ShaderAbi.ViewSet"/>), set 5 of every pipeline layout.</summary>
	public BindingLayoutHandle ViewSetLayout { get; private set; }

	/// <summary>
	/// The view set command lists bind when none was given (<see cref="CommandList.SetViewSet"/>): zeroed buffers, which
	/// shaders read as no lights, no sun and no volumes.
	/// </summary>
	public BindingSetHandle DefaultViewSet { get; private set; }
	readonly Dictionary<(ulong, ulong, ulong, ulong), ulong> pipelineLayouts = new();
	readonly Dictionary<ulong, (ulong, ulong, ulong, ulong)> pipelineLayoutSets = new();
	readonly List<ulong> retiredPipelineLayouts = new();

	// What each pipeline layout's sets 0 to 3 hold (their slots), so parts made with identically defined layouts are shared
	readonly Dictionary<ulong, UInt128> pipelineLayoutHashes = new();

	/// <summary>The hash of what a pipeline layout's sets hold (<see cref="SetsHash"/>).</summary>
	internal UInt128 LayoutHash( ulong pipelineLayout )
	{
		lock ( pipelineLayouts ) return pipelineLayoutHashes[pipelineLayout];
	}

	/// <summary>The slots of sets 0 to 3, a missing set as an empty one.</summary>
	internal BindingSlot[][] SlotsOf( BindingLayoutHandle[] sets )
	{
		var slots = new BindingSlot[ShaderAbi.BindlessSet][];
		for ( int s = 0; s < slots.Length; s++ ) slots[s] = sets is not null && s < sets.Length && sets[s].IsValid ? Get( sets[s] ).Slots : [];
		return slots;
	}

	UInt128 SetsHash( BindingLayoutHandle[] sets )
	{
		var key = new PartKeyWriter( 0 );
		foreach ( var set in SlotsOf( sets ) )
		{
			key.Add( set.Length );
			foreach ( var slot in set )
			{
				key.Add( slot.Binding );
				key.Add( (int)slot.Type );
				key.Add( (int)slot.Stages );
				key.Add( slot.Count );
			}
		}

		return key.Hash();
	}

	static UInt128 SpirvHash( ReadOnlySpan<byte> spirv )
	{
		Span<byte> digest = stackalloc byte[32];
		System.Security.Cryptography.SHA256.HashData( spirv, digest );
		var hash = new UInt128( System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian( digest ), System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian( digest[8..] ) );
		return hash == 0 ? 1 : hash;
	}

	/// <summary>The set layouts of sets 0 to 3 a pipeline layout was made from.</summary>
	internal (ulong, ulong, ulong, ulong) SetLayoutsOf( ulong pipelineLayout )
	{
		lock ( pipelineLayouts ) return pipelineLayoutSets[pipelineLayout];
	}

	internal ref BufferRecord Get( BufferHandle handle ) => ref Buffers.Get( handle.Index, handle.Generation );
	internal ref TextureRecord Get( TextureHandle handle ) => ref Textures.Get( handle.Index, handle.Generation );
	internal ref ViewRecord Get( TextureViewHandle handle ) => ref Views.Get( handle.Index, handle.Generation );
	internal ref SamplerRecord Get( SamplerHandle handle ) => ref Samplers.Get( handle.Index, handle.Generation );
	internal ref PipelineRecord Get( PipelineHandle handle ) => ref Pipelines.Get( handle.Index, handle.Generation );
	internal ref LayoutRecord Get( BindingLayoutHandle handle ) => ref Layouts.Get( handle.Index, handle.Generation );

	/// <summary>Bytes of device memory allocated from the driver, and handed out to resources.</summary>
	public (long Reserved, long Used) MemoryUsage => ((long)Allocator.Reserved, (long)Allocator.Used);

	//
	// Buffers
	//

	/// <summary>
	/// Create a buffer. <see cref="MemoryLocation.Upload"/> and <see cref="MemoryLocation.Readback"/> buffers stay mapped
	/// (<see cref="Map"/>).
	/// </summary>
	public BufferHandle CreateBuffer( in BufferInfo info )
	{
		ArgumentOutOfRangeException.ThrowIfLessThan( info.Size, 1 );

		uint usage = Vulkan.Vk.BufferTransferSrc | Vulkan.Vk.BufferTransferDst;
		if ( (info.Usage & BufferUsage.Vertex) != 0 ) usage |= Vulkan.Vk.BufferVertex;
		if ( (info.Usage & BufferUsage.Index) != 0 ) usage |= Vulkan.Vk.BufferIndex;
		if ( (info.Usage & BufferUsage.Uniform) != 0 ) usage |= Vulkan.Vk.BufferUniform;
		if ( (info.Usage & BufferUsage.Storage) != 0 ) usage |= Vulkan.Vk.BufferStorage;
		if ( (info.Usage & BufferUsage.Indirect) != 0 ) usage |= Vulkan.Vk.BufferIndirect;

		fixed ( uint* families = queueFamilies )
		{
			var create = new VkBufferCreateInfo
			{
				sType = VkStructureType.BufferCreateInfo,
				size = (ulong)info.Size,
				usage = usage,
				sharingMode = queueFamilies.Length > 1 ? 1 : 0,
				queueFamilyIndexCount = (uint)queueFamilies.Length,
				pQueueFamilyIndices = families,
			};

			ulong buffer;
			Vk.vkCreateBuffer( Vk.Device, &create, null, &buffer ).Check( "vkCreateBuffer" );

			VkMemoryRequirements requirements;
			Vk.vkGetBufferMemoryRequirements( Vk.Device, buffer, &requirements );

			Allocation memory;
			try
			{
				memory = Allocator.Allocate( requirements, info.Memory, image: false, info.Content );
				Vk.vkBindBufferMemory( Vk.Device, buffer, memory.Memory, memory.Offset ).Check( "vkBindBufferMemory" );
			}
			catch
			{
				Vk.vkDestroyBuffer( Vk.Device, buffer, null );
				throw;
			}

			SetName( VkObjectType.Buffer, buffer, info.Name );
			var (index, generation) = Buffers.Add( new BufferRecord { Buffer = buffer, Memory = memory, Size = info.Size, Usage = info.Usage, Location = info.Memory, Name = info.Name } );
			return new BufferHandle( index, generation );
		}
	}

	/// <summary>
	/// A host-visible buffer's memory. Writes are seen by work submitted afterwards; reads see what finished work wrote.
	/// </summary>
	public Span<byte> Map( BufferHandle buffer )
	{
		ref var record = ref Get( buffer );
		if ( record.Memory.Mapped == 0 ) throw new InvalidOperationException( $"Buffer {record.Name} is in {record.Location} memory, which isn't mappable" );
		return new Span<byte>( (void*)record.Memory.Mapped, (int)record.Size );
	}

	/// <summary>A buffer's size in bytes.</summary>
	public long SizeOf( BufferHandle buffer ) => Get( buffer ).Size;

	/// <summary>Whether a handle names a buffer that hasn't been destroyed.</summary>
	public bool IsAlive( BufferHandle buffer ) => Buffers.IsAlive( buffer.Index, buffer.Generation );

	//
	// Textures
	//

	/// <summary>
	/// Create a texture. Its contents are undefined, and it's in <see cref="ImageLayout.Undefined"/>.
	/// </summary>
	public TextureHandle CreateTexture( in TextureInfo info )
	{
		ArgumentOutOfRangeException.ThrowIfLessThan( info.Width, 1 );
		ArgumentOutOfRangeException.ThrowIfLessThan( info.Height, 1 );

		var desc = info;
		desc.Depth = Math.Max( desc.Depth, 1 );
		desc.Layers = Math.Max( desc.Layers, 1 );
		desc.Mips = Math.Max( desc.Mips, 1 );
		desc.Samples = Math.Max( desc.Samples, 1 );

		var format = ResolveFormat( desc.Format );
		var cube = desc.Dimension is TextureDimension.TextureCube or TextureDimension.TextureCubeArray;
		var arrayLayers = desc.Dimension switch
		{
			TextureDimension.Texture3D => 1,
			TextureDimension.TextureCube or TextureDimension.TextureCubeArray => 6 * desc.Layers,
			_ => desc.Layers,
		};

		var srgbPair = desc.Format.HasSrgb();
		uint usage = Vulkan.Vk.ImageTransferSrc | Vulkan.Vk.ImageTransferDst;
		if ( (desc.Usage & TextureUsage.Sampled) != 0 ) usage |= Vulkan.Vk.ImageSampled;
		if ( (desc.Usage & TextureUsage.Storage) != 0 ) usage |= Vulkan.Vk.ImageStorage;
		if ( (desc.Usage & TextureUsage.ColorTarget) != 0 ) usage |= Vulkan.Vk.ImageColorAttachment;
		if ( (desc.Usage & TextureUsage.DepthTarget) != 0 ) usage |= Vulkan.Vk.ImageDepthStencilAttachment;

		uint flags = 0;
		if ( cube ) flags |= Vulkan.Vk.ImageCreateCubeCompatible;
		if ( srgbPair ) flags |= Vulkan.Vk.ImageCreateMutableFormat | Vulkan.Vk.ImageCreateExtendedUsage;

		ulong image;
		Allocation memory;
		fixed ( uint* families = queueFamilies )
		{
			var create = new VkImageCreateInfo
			{
				sType = VkStructureType.ImageCreateInfo,
				flags = flags,
				imageType = desc.Dimension == TextureDimension.Texture3D ? Vulkan.Vk.ImageType3D : Vulkan.Vk.ImageType2D,
				format = format,
				extent = new VkExtent3D { width = (uint)desc.Width, height = (uint)desc.Height, depth = (uint)desc.Depth },
				mipLevels = (uint)desc.Mips,
				arrayLayers = (uint)arrayLayers,
				samples = (uint)desc.Samples,
				usage = usage,
				sharingMode = queueFamilies.Length > 1 ? 1 : 0,
				queueFamilyIndexCount = (uint)queueFamilies.Length,
				pQueueFamilyIndices = families,
			};

			Vk.vkCreateImage( Vk.Device, &create, null, &image ).Check( $"vkCreateImage ({desc.Name}, {desc.Format} {desc.Width}x{desc.Height})" );

			VkMemoryRequirements requirements;
			Vk.vkGetImageMemoryRequirements( Vk.Device, image, &requirements );

			try
			{
				var target = (desc.Usage & (TextureUsage.ColorTarget | TextureUsage.DepthTarget | TextureUsage.Storage)) != 0;
				memory = Allocator.Allocate( requirements, MemoryLocation.Device, image: true, target, desc.Content );
				Vk.vkBindImageMemory( Vk.Device, image, memory.Memory, memory.Offset ).Check( "vkBindImageMemory" );
			}
			catch
			{
				Vk.vkDestroyImage( Vk.Device, image, null );
				throw;
			}
		}

		SetName( VkObjectType.Image, image, desc.Name );
		var record = new TextureRecord
		{
			Image = image,
			Memory = memory,
			Info = desc,
			Format = format,
			Aspect = desc.Format.Aspect(),
			ArrayLayers = arrayLayers,
			BindlessIndex = -1,
			StorageIndex = -1,
			Attachments = new AttachmentViews(),
		};

		var sampledAspect = desc.Format.IsDepth() ? Vulkan.Vk.AspectDepth : Vulkan.Vk.AspectColor;
		var linear = desc.Format.Linear().ToVk();
		var sampledUsage = usage & ~Vulkan.Vk.ImageStorage;

		if ( (desc.Usage & (TextureUsage.Sampled | TextureUsage.Storage)) != 0 )
		{
			record.View = MakeView( image, ViewType( desc.Dimension ), linear, sampledAspect, 0, (uint)desc.Mips, 0, (uint)arrayLayers, srgbPair ? usage : 0 );
			record.SrgbView = srgbPair ? MakeView( image, ViewType( desc.Dimension ), desc.Format.Srgb().ToVk(), sampledAspect, 0, (uint)desc.Mips, 0, (uint)arrayLayers, sampledUsage ) : record.View;
		}

		if ( (desc.Usage & TextureUsage.Storage) != 0 )
		{
			var storageType = desc.Dimension switch
			{
				TextureDimension.Texture3D => Vulkan.Vk.ViewType3D,
				TextureDimension.Texture2D => Vulkan.Vk.ViewType2D,
				_ => Vulkan.Vk.ViewType2DArray,
			};

			record.StorageView = MakeView( image, storageType, linear, Vulkan.Vk.AspectColor, 0, 1, 0, (uint)arrayLayers, srgbPair ? usage : 0 );
			record.StorageIndex = Bindless.AddStorage( record.StorageView );
		}

		if ( (desc.Usage & TextureUsage.Sampled) != 0 ) record.BindlessIndex = Bindless.AddTexture( record.View, record.SrgbView );

		var (index, generation) = Textures.Add( record );
		return new TextureHandle( index, generation );
	}

	/// <summary>
	/// A swap chain image as a texture: the swap chain owns the image, the device its views.
	/// </summary>
	internal TextureHandle AddExternalTexture( ulong image, in TextureInfo info, VkFormat format )
	{
		var (index, generation) = Textures.Add( new TextureRecord
		{
			Image = image,
			Info = info,
			Format = format,
			Aspect = Vulkan.Vk.AspectColor,
			ArrayLayers = 1,
			BindlessIndex = -1,
			StorageIndex = -1,
			External = true,
			Attachments = new AttachmentViews(),
		} );

		return new TextureHandle( index, generation );
	}

	/// <summary>
	/// Forget a swap chain image now: the swap chain waits for the GPU before it lets its images go.
	/// </summary>
	internal void RemoveExternalTexture( TextureHandle texture )
	{
		var record = Textures.Remove( texture.Index, texture.Generation );
		foreach ( var view in record.Attachments.Views.Values ) Vk.vkDestroyImageView( Vk.Device, view, null );
	}

	VkFormat ResolveFormat( Format format )
	{
		var vk = format.ToVk();
		if ( vk == VkFormat.Undefined ) throw new ArgumentException( $"Format {format} has no Vulkan form" );

		// AMD has no D24S8; native makes the same swap (CTextureManagerVulkan::CreateVulkanImageInternal)
		if ( vk == VkFormat.D24UnormS8Uint )
		{
			VkFormatProperties properties;
			Instance.vkGetPhysicalDeviceFormatProperties( PhysicalDevice, vk, &properties );
			if ( (properties.optimalTilingFeatures & Vulkan.Vk.FormatFeatureDepthStencilAttachment) == 0 ) return VkFormat.D32SfloatS8Uint;
		}

		return vk;
	}

	/// <summary>
	/// Whether textures in <paramref name="format"/> can be used as <paramref name="usage"/> (optimal tiling).
	/// </summary>
	public bool Supports( Format format, TextureUsage usage )
	{
		var vk = format.ToVk();
		if ( vk == VkFormat.Undefined ) return false;
		if ( vk == VkFormat.D24UnormS8Uint ) vk = ResolveFormat( format );

		VkFormatProperties properties;
		Instance.vkGetPhysicalDeviceFormatProperties( PhysicalDevice, vk, &properties );
		var features = properties.optimalTilingFeatures;

		if ( (usage & TextureUsage.Sampled) != 0 && (features & Vulkan.Vk.FormatFeatureSampledImage) == 0 ) return false;
		if ( (usage & TextureUsage.Storage) != 0 && (features & Vulkan.Vk.FormatFeatureStorageImage) == 0 ) return false;
		if ( (usage & TextureUsage.ColorTarget) != 0 && (features & Vulkan.Vk.FormatFeatureColorAttachment) == 0 ) return false;
		if ( (usage & TextureUsage.DepthTarget) != 0 && (features & Vulkan.Vk.FormatFeatureDepthStencilAttachment) == 0 ) return false;
		return true;
	}

	/// <summary>The Vulkan format a <see cref="Format"/> is created as here (D24S8 may become D32S8).</summary>
	internal VkFormat VkFormatOf( Format format ) => format == Format.Unknown ? VkFormat.Undefined : ResolveFormat( format );

	static int ViewType( TextureDimension dimension ) => dimension switch
	{
		TextureDimension.Texture2DArray => Vulkan.Vk.ViewType2DArray,
		TextureDimension.TextureCube => Vulkan.Vk.ViewTypeCube,
		TextureDimension.TextureCubeArray => Vulkan.Vk.ViewTypeCubeArray,
		TextureDimension.Texture3D => Vulkan.Vk.ViewType3D,
		_ => Vulkan.Vk.ViewType2D,
	};

	internal ulong MakeView( ulong image, int viewType, VkFormat format, uint aspect, uint baseMip, uint mips, uint baseLayer, uint layers, uint usage )
	{
		var usageInfo = new VkImageViewUsageCreateInfo { sType = VkStructureType.ImageViewUsageCreateInfo, usage = usage };
		var info = new VkImageViewCreateInfo
		{
			sType = VkStructureType.ImageViewCreateInfo,
			pNext = usage != 0 ? &usageInfo : null,
			image = image,
			viewType = viewType,
			format = format,
			subresourceRange = new VkImageSubresourceRange { aspectMask = aspect, baseMipLevel = baseMip, levelCount = mips, baseArrayLayer = baseLayer, layerCount = layers },
		};

		ulong view;
		Vk.vkCreateImageView( Vk.Device, &info, null, &view ).Check( "vkCreateImageView" );
		return view;
	}

	/// <summary>
	/// The view a rendering scope draws to: one mip and layer, linear or sRGB. Made once per texture and kept.
	/// </summary>
	internal ulong AttachmentView( TextureHandle texture, int mip, int layer, bool srgb )
	{
		ref var record = ref Get( texture );
		var key = mip | (layer << 8) | (srgb ? 1 << 24 : 0);
		var views = record.Attachments.Views;

		lock ( views )
		{
			if ( views.TryGetValue( key, out var view ) ) return view;

			var format = record.External ? record.Format : srgb ? record.Info.Format.Srgb().ToVk() : record.Info.Format.Linear().ToVk();
			if ( !record.External && record.Info.Format.IsDepth() ) format = record.Format;

			var usage = 0u;
			if ( !record.External && record.Info.Format.HasSrgb() )
			{
				usage = (record.Info.Usage & TextureUsage.ColorTarget) != 0 ? Vulkan.Vk.ImageColorAttachment : Vulkan.Vk.ImageDepthStencilAttachment;
			}

			view = MakeView( record.Image, Vulkan.Vk.ViewType2D, format, record.Aspect, (uint)mip, 1, (uint)layer, 1, usage );
			views[key] = view;
			return view;
		}
	}

	/// <summary>
	/// Another view of a texture: a mip or layer range, another dimension or format. Add it to the bindless table with
	/// <paramref name="bindless"/>.
	/// </summary>
	public TextureViewHandle CreateView( TextureHandle texture, in TextureViewInfo info, bool bindless = false )
	{
		ref var record = ref Get( texture );
		var format = info.Format == Format.Unknown ? record.Info.Format.Linear().ToVk() : info.Format.ToVk();
		if ( record.Info.Format.IsDepth() ) format = record.Format;

		var mips = info.MipCount > 0 ? (uint)info.MipCount : (uint)(record.Info.Mips - info.BaseMip);
		var layers = info.LayerCount > 0 ? (uint)info.LayerCount : (uint)(record.ArrayLayers - info.BaseLayer);
		var aspect = record.Info.Format.IsDepth() ? Vulkan.Vk.AspectDepth : Vulkan.Vk.AspectColor;

		// An sRGB view of a storage texture mustn't claim storage use
		var usage = 0u;
		if ( record.Info.Format.HasSrgb() && info.Format != Format.Unknown && info.Format == info.Format.Srgb() && info.Format != info.Format.Linear() )
		{
			usage = Vulkan.Vk.ImageSampled;
		}

		var view = MakeView( record.Image, ViewType( info.Dimension ), format, aspect, (uint)info.BaseMip, mips, (uint)info.BaseLayer, layers, usage );
		var storage = (record.Info.Usage & TextureUsage.Storage) != 0 && mips == 1 && usage == 0 && info.Dimension != TextureDimension.TextureCube;

		var viewRecord = new ViewRecord
		{
			View = view,
			Texture = texture,
			BindlessIndex = bindless && (record.Info.Usage & TextureUsage.Sampled) != 0 ? Bindless.AddTexture( view, view ) : -1,
			StorageIndex = bindless && storage ? Bindless.AddStorage( view ) : -1,
		};

		var (index, generation) = Views.Add( viewRecord );
		return new TextureViewHandle( index, generation );
	}

	/// <summary>What a texture was created with.</summary>
	public TextureInfo InfoOf( TextureHandle texture ) => Get( texture ).Info;

	/// <summary>Whether a handle names a texture that hasn't been destroyed.</summary>
	public bool IsAlive( TextureHandle texture ) => Textures.IsAlive( texture.Index, texture.Generation );

	/// <summary>
	/// A sampled texture's index in the bindless table (<c>Bindless::GetTexture2D</c> and friends); its sRGB view is at
	/// the next index (<c>GetTexture2DSrgb</c>). -1 if it isn't sampled.
	/// </summary>
	public int BindlessIndex( TextureHandle texture ) => Get( texture ).BindlessIndex;

	/// <summary>A view's index in the bindless table, or -1 if it wasn't added.</summary>
	public int BindlessIndex( TextureViewHandle view ) => Get( view ).BindlessIndex;

	/// <summary>A storage texture's mip 0 in the bindless storage table (<c>Bindless::GetRWTexture2D</c>), or -1.</summary>
	public int StorageIndex( TextureHandle texture ) => Get( texture ).StorageIndex;

	/// <summary>A view's index in the bindless storage table, or -1.</summary>
	public int StorageIndex( TextureViewHandle view ) => Get( view ).StorageIndex;

	//
	// Samplers
	//

	/// <summary>
	/// Create a sampler and add it to the bindless sampler table (<see cref="SamplerIndex"/>).
	/// </summary>
	public SamplerHandle CreateSampler( in SamplerInfo info )
	{
		static int Address( AddressMode mode ) => mode switch { AddressMode.Mirror => 1, AddressMode.Clamp => 2, AddressMode.Border => 3, _ => 0 };

		var anisotropy = Math.Min( info.MaxAnisotropy, Capabilities.MaxAnisotropy );
		var create = new VkSamplerCreateInfo
		{
			sType = VkStructureType.SamplerCreateInfo,
			magFilter = (int)info.MagFilter,
			minFilter = (int)info.MinFilter,
			mipmapMode = (int)info.MipFilter,
			addressModeU = Address( info.AddressU ),
			addressModeV = Address( info.AddressV ),
			addressModeW = Address( info.AddressW ),
			mipLodBias = info.MipBias,
			anisotropyEnable = anisotropy > 1 ? 1u : 0u,
			maxAnisotropy = Math.Max( anisotropy, 1 ),
			compareEnable = info.Compare != CompareFunction.Never ? 1u : 0u,
			compareOp = (int)info.Compare,
			minLod = info.MinLod,
			maxLod = info.MaxLod,
		};

		ulong sampler;
		Vk.vkCreateSampler( Vk.Device, &create, null, &sampler ).Check( "vkCreateSampler" );

		var (index, generation) = Samplers.Add( new SamplerRecord { Sampler = sampler, Index = Bindless.AddSampler( sampler ) } );
		return new SamplerHandle( index, generation );
	}

	/// <summary>A sampler's index in the bindless sampler table (<c>Bindless::GetSampler</c>).</summary>
	public int SamplerIndex( SamplerHandle sampler ) => Get( sampler ).Index;

	//
	// Shaders, layouts, pipelines
	//

	/// <summary>
	/// Make a shader module from SPIR-V.
	/// </summary>
	public ShaderHandle CreateShader( ReadOnlySpan<byte> spirv, string name = null )
	{
		if ( spirv.Length < 20 || spirv.Length % 4 != 0 || MemoryMarshal.Read<uint>( spirv ) != 0x07230203 )
			throw new ArgumentException( $"Shader {name} isn't SPIR-V" );

		fixed ( byte* code = spirv )
		{
			var info = new VkShaderModuleCreateInfo { sType = VkStructureType.ShaderModuleCreateInfo, codeSize = (nuint)spirv.Length, pCode = (uint*)code };
			ulong module;
			using ( Zone( ZoneShaderModule ) ) Vk.vkCreateShaderModule( Vk.Device, &info, null, &module ).Check( $"vkCreateShaderModule ({name})" );
			SetName( VkObjectType.ShaderModule, module, name );

			var hash = pipelineLibraries ? SpirvHash( spirv ) : 0;
			var (index, generation) = Shaders.Add( new ShaderRecord { Module = module, Hash = hash } );
			var handle = new ShaderHandle( index, generation );
			if ( pipelineLibraries ) ShaderMade( handle, hash );
			return handle;
		}
	}

	static VkDescriptorType DescriptorType( BindingType type ) => type switch
	{
		BindingType.UniformBuffer => VkDescriptorType.UniformBuffer,
		BindingType.StorageBuffer => VkDescriptorType.StorageBuffer,
		BindingType.SampledTexture => VkDescriptorType.SampledImage,
		BindingType.StorageTexture => VkDescriptorType.StorageImage,
		BindingType.UniformBufferDynamic => VkDescriptorType.UniformBufferDynamic,
		_ => VkDescriptorType.Sampler,
	};

	/// <summary>
	/// Describe a descriptor set that's filled per draw or dispatch (<see cref="CommandList.SetBindings"/>).
	/// </summary>
	public BindingLayoutHandle CreateBindingLayout( ReadOnlySpan<BindingSlot> slots )
	{
		var bindings = stackalloc VkDescriptorSetLayoutBinding[Math.Max( slots.Length, 1 )];
		for ( int i = 0; i < slots.Length; i++ )
		{
			bindings[i] = new VkDescriptorSetLayoutBinding
			{
				binding = (uint)slots[i].Binding,
				descriptorType = DescriptorType( slots[i].Type ),
				descriptorCount = (uint)Math.Max( slots[i].Count, 1 ),
				stageFlags = (uint)slots[i].Stages,
			};
		}

		var info = new VkDescriptorSetLayoutCreateInfo { sType = VkStructureType.DescriptorSetLayoutCreateInfo, bindingCount = (uint)slots.Length, pBindings = bindings };
		ulong layout;
		Vk.vkCreateDescriptorSetLayout( Vk.Device, &info, null, &layout ).Check( "vkCreateDescriptorSetLayout" );

		var (index, generation) = Layouts.Add( new LayoutRecord { SetLayout = layout, Slots = slots.ToArray() } );
		return new BindingLayoutHandle( index, generation );
	}

	ulong SetLayoutOf( BindingLayoutHandle[] sets, int set ) => sets is not null && set < sets.Length && sets[set].IsValid ? Get( sets[set] ).SetLayout : emptySetLayout;

	/// <summary>
	/// The pipeline layout of pipelines with these sets. With pipeline libraries, its sets are independent
	/// (<c>VK_PIPELINE_LAYOUT_CREATE_INDEPENDENT_SETS_BIT_EXT</c>), as libraries' layouts are: on AMD's driver, with the
	/// extension on, pipelines whose layouts weren't read push constants wrong.
	/// </summary>
	ulong PipelineLayout( BindingLayoutHandle[] sets )
	{
		if ( sets is { Length: > ShaderAbi.BindlessSet } ) throw new ArgumentException( $"Sets 0 to {ShaderAbi.BindlessSet - 1} are the pipeline's; set {ShaderAbi.BindlessSet} is the bindless table" );

		var key = (SetLayoutOf( sets, 0 ), SetLayoutOf( sets, 1 ), SetLayoutOf( sets, 2 ), SetLayoutOf( sets, 3 ));
		lock ( pipelineLayouts )
		{
			if ( pipelineLayouts.TryGetValue( key, out var existing ) ) return existing;

			var setLayouts = stackalloc ulong[ShaderAbi.SetCount] { key.Item1, key.Item2, key.Item3, key.Item4, Bindless.SetLayout, Get( ViewSetLayout ).SetLayout };
			var push = new VkPushConstantRange { stageFlags = StageAll, size = ShaderAbi.PushConstantSize };
			var info = new VkPipelineLayoutCreateInfo
			{
				sType = VkStructureType.PipelineLayoutCreateInfo,
				flags = pipelineLibraries ? Vulkan.Vk.PipelineLayoutIndependentSets : 0,
				setLayoutCount = ShaderAbi.SetCount,
				pSetLayouts = setLayouts,
				pushConstantRangeCount = 1,
				pPushConstantRanges = &push,
			};

			ulong layout;
			Vk.vkCreatePipelineLayout( Vk.Device, &info, null, &layout ).Check( "vkCreatePipelineLayout" );
			pipelineLayouts[key] = layout;
			pipelineLayoutSets[layout] = key;
			pipelineLayoutHashes[layout] = SetsHash( sets );
			return layout;
		}
	}

	/// <summary>
	/// Create a compute pipeline, through the device's pipeline cache.
	/// </summary>
	public PipelineHandle CreateComputePipeline( in ComputePipelineInfo info )
	{
		using var strings = new NativeStrings();
		var layout = PipelineLayout( info.Sets );
		VkSpecializationInfo specialization;
		var create = new VkComputePipelineCreateInfo
		{
			sType = VkStructureType.ComputePipelineCreateInfo,
			stage = new VkPipelineShaderStageCreateInfo
			{
				sType = VkStructureType.PipelineShaderStageCreateInfo,
				stage = Vulkan.Vk.StageCompute,
				module = Shaders.Get( info.Shader.Index, info.Shader.Generation ).Module,
				pName = strings.Add( info.Entry ?? "main" ),
				pSpecializationInfo = Specialize( info.Constants, &specialization, strings ),
			},
			layout = layout,
			basePipelineIndex = -1,
			flags = shaderStatistics ? CaptureStatistics : 0,
		};

		ulong pipeline;
		var started = System.Diagnostics.Stopwatch.GetTimestamp();
		using ( Zone( ZonePipeline ) ) Vk.vkCreateComputePipelines( Vk.Device, pipelineCache, 1, &create, null, &pipeline ).Check( $"vkCreateComputePipelines ({info.Name})" );
		CountPipeline( started );
		Interlocked.Add( ref computeTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started );
		Interlocked.Increment( ref computeMade );
		SetName( VkObjectType.Pipeline, pipeline, info.Name );
		if ( shaderStatistics ) KeepStatistics( pipeline, info.Name );

		var (index, generation) = Pipelines.Add( new PipelineRecord { Pipeline = pipeline, Layout = layout, BindPoint = Vulkan.Vk.BindPointCompute } );
		return new PipelineHandle( index, generation );
	}

	/// <summary>
	/// Fill <paramref name="info"/> with <paramref name="constants"/>, each a 32-bit value, keeping the memory alive in
	/// <paramref name="strings"/>; null when there are none.
	/// </summary>
	static VkSpecializationInfo* Specialize( SpecializationConstant[] constants, VkSpecializationInfo* info, NativeStrings strings )
	{
		if ( constants is not { Length: > 0 } ) return null;

		var entries = (VkSpecializationMapEntry*)strings.Allocate( constants.Length * sizeof( VkSpecializationMapEntry ) );
		var values = (uint*)strings.Allocate( constants.Length * sizeof( uint ) );
		for ( int i = 0; i < constants.Length; i++ )
		{
			entries[i] = new VkSpecializationMapEntry { constantID = constants[i].Id, offset = (uint)(i * sizeof( uint )), size = sizeof( uint ) };
			values[i] = constants[i].Value;
		}

		*info = new VkSpecializationInfo { mapEntryCount = (uint)constants.Length, pMapEntries = entries, dataSize = (nuint)(constants.Length * sizeof( uint )), pData = values };
		return info;
	}

	void CreatePipelineCache( byte[] data )
	{
		var empty = new VkDescriptorSetLayoutCreateInfo { sType = VkStructureType.DescriptorSetLayoutCreateInfo };
		ulong layout;
		Vk.vkCreateDescriptorSetLayout( Vk.Device, &empty, null, &layout ).Check( "vkCreateDescriptorSetLayout" );
		emptySetLayout = layout;

		// Every pipeline layout has the view set, so shaders built against it bind whether or not they read it
		Span<BindingSlot> viewSlots = stackalloc BindingSlot[ShaderAbi.ViewSetBindings.Length];
		for ( int i = 0; i < viewSlots.Length; i++ )
		{
			var (binding, type, _) = ShaderAbi.ViewSetBindings[i];
			viewSlots[i] = new BindingSlot( binding, type, (ShaderStages)StageAll );
		}

		ViewSetLayout = CreateBindingLayout( viewSlots );

		fixed ( byte* initial = data )
		{
			var info = new VkPipelineCacheCreateInfo { sType = VkStructureType.PipelineCacheCreateInfo, initialDataSize = (nuint)(data?.Length ?? 0), pInitialData = initial };
			ulong cache;

			// Data from another driver or GPU is rejected; start empty then
			PipelineCacheLoaded = data is { Length: > 0 };
			if ( Vk.vkCreatePipelineCache( Vk.Device, &info, null, &cache ) != VkResult.Success )
			{
				PipelineCacheLoaded = false;
				info.initialDataSize = 0;
				info.pInitialData = null;
				Vk.vkCreatePipelineCache( Vk.Device, &info, null, &cache ).Check( "vkCreatePipelineCache" );
			}

			pipelineCache = cache;
		}

		savedSize = CacheSize();
	}

	// The cache's size when loaded or last saved: pipelines made from it (the part manifest's at load, say) don't grow it
	nuint savedSize;

	nuint CacheSize()
	{
		nuint size;
		return Vk.vkGetPipelineCacheData( Vk.Device, pipelineCache, &size, null ) == VkResult.Success ? size : 0;
	}

	string pipelineCachePath;
	int pipelinesMade, pipelinesSaved, computeMade;
	long pipelineTicks, computeTicks;

	/// <summary>Whether the device started from saved pipeline cache data the driver took.</summary>
	public bool PipelineCacheLoaded { get; private set; }

	/// <summary>The pipeline cache's file (<see cref="DeviceOptions.PipelineCacheFolder"/>), or null.</summary>
	public string PipelineCachePath => pipelineCachePath;

	/// <summary>Pipelines made so far and the milliseconds spent making them, for start-up and first-use measurements.</summary>
	public (int Made, double Ms) PipelineCreation => (Volatile.Read( ref pipelinesMade ), Volatile.Read( ref pipelineTicks ) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

	/// <summary>The compute pipelines among <see cref="PipelineCreation"/>, and the milliseconds spent making them.</summary>
	public (int Made, double Ms) ComputePipelineCreation => (Volatile.Read( ref computeMade ), Volatile.Read( ref computeTicks ) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

	void CountPipeline( long started )
	{
		Interlocked.Add( ref pipelineTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started );
		Interlocked.Increment( ref pipelinesMade );
	}

	/// <summary>
	/// Save the pipeline cache to its file (<see cref="DeviceOptions.PipelineCacheFolder"/>) if pipelines were made since
	/// it was loaded or last saved; false when there's no file, nothing new, or it couldn't be written. Safe to call from
	/// any thread while pipelines are made. The device saves it when disposed too.
	/// </summary>
	public bool SavePipelineCache()
	{
		if ( pipelineCachePath is null || pipelineCache == 0 ) return false;
		partManifest?.Save();

		// Parts and optimised links go into the cache too, the latter on background threads after their pipelines were made
		var made = Volatile.Read( ref pipelinesMade ) + Volatile.Read( ref partsMade ) + Volatile.Read( ref optimisedMade );
		if ( made == Volatile.Read( ref pipelinesSaved ) ) return false;

		// Made from what the cache had: nothing new
		var data = GetPipelineCacheData();
		if ( (nuint)data.Length == savedSize )
		{
			Volatile.Write( ref pipelinesSaved, made );
			return false;
		}

		if ( !PipelineCacheFile.Save( pipelineCachePath, data ) ) return false;
		savedSize = (nuint)data.Length;
		Volatile.Write( ref pipelinesSaved, made );
		return true;
	}

	/// <summary>
	/// The pipeline cache's contents, to save and pass back as <see cref="DeviceOptions.PipelineCacheData"/>.
	/// </summary>
	public byte[] GetPipelineCacheData()
	{
		nuint size;
		Vk.vkGetPipelineCacheData( Vk.Device, pipelineCache, &size, null ).Check( "vkGetPipelineCacheData" );
		var data = new byte[(int)size];
		fixed ( byte* p = data ) Vk.vkGetPipelineCacheData( Vk.Device, pipelineCache, &size, p ).Check( "vkGetPipelineCacheData" );
		return data;
	}

	void DestroyPipelineCache()
	{
		foreach ( var layout in pipelineLayouts.Values ) Vk.vkDestroyPipelineLayout( Vk.Device, layout, null );
		foreach ( var layout in retiredPipelineLayouts ) Vk.vkDestroyPipelineLayout( Vk.Device, layout, null );
		pipelineLayouts.Clear();
		retiredPipelineLayouts.Clear();
		Vk.vkDestroyDescriptorSetLayout( Vk.Device, emptySetLayout, null );
		Vk.vkDestroyPipelineCache( Vk.Device, pipelineCache, null );
	}

	//
	// Destruction: deferred until every queue has finished the frame it was destroyed in
	//

	readonly struct Garbage( ulong graphics, ulong compute, Action free )
	{
		public readonly ulong Graphics = graphics;
		public readonly ulong Compute = compute;
		public readonly Action Free = free;
	}

	readonly List<Garbage> garbage = new();

	/// <summary>What was destroyed this frame, tagged when the frame closes.</summary>
	readonly List<Action> destroyedThisFrame = new();

	/// <summary>
	/// Free something once the GPU has finished the frame it was destroyed in: every list of the frame, those still
	/// recording or held for a later submission as well as those submitted, since any of them may use it. A frame's lists
	/// all have their timeline values once <see cref="BeginFrame"/> has flushed them, so it's tagged then.
	/// </summary>
	internal void Defer( Action free )
	{
		lock ( garbage ) destroyedThisFrame.Add( free );
	}

	/// <summary>The queues' values what was destroyed in the frame <see cref="BeginFrame"/> last closed waits for, for the tests.</summary>
	internal (ulong Graphics, ulong Compute) LastGarbageTag { get; private set; }

	/// <summary>Tag what was destroyed this frame with every queue's last value, once the frame's lists are all submitted.</summary>
	void TagDestroyed()
	{
		lock ( garbage )
		{
			if ( destroyedThisFrame.Count == 0 ) return;
			var (graphics, compute) = (graphicsQueue.Submitted, computeQueue.Submitted);
			foreach ( var free in destroyedThisFrame ) garbage.Add( new Garbage( graphics, compute, free ) );
			destroyedThisFrame.Clear();
			LastGarbageTag = (graphics, compute);
		}
	}

	void CollectGarbage( bool force = false )
	{
		if ( force ) TagDestroyed();
		var graphics = graphicsQueue.Completed;
		var compute = computeQueue.Completed;

		List<Action> due = null;
		lock ( garbage )
		{
			for ( int i = 0; i < garbage.Count; i++ )
			{
				var item = garbage[i];
				if ( !force && (item.Graphics > graphics || item.Compute > compute) ) continue;

				(due ??= new()).Add( item.Free );
				garbage.RemoveAt( i-- );
			}
		}

		if ( due is null ) return;
		foreach ( var free in due ) free();
	}

	/// <summary>Destroy a buffer once the GPU is done with it.</summary>
	public void Destroy( BufferHandle buffer )
	{
		var record = Buffers.Remove( buffer.Index, buffer.Generation );
		Defer( () =>
		{
			if ( record.BindlessSlot > 0 ) Bindless.RemoveBuffer( record.BindlessSlot - 1 );
			Vk.vkDestroyBuffer( Vk.Device, record.Buffer, null );
			Allocator.Free( record.Memory );
		} );
	}

	/// <summary>Destroy a texture, its views and its bindless entries once the GPU is done with it.</summary>
	public void Destroy( TextureHandle texture )
	{
		var record = Textures.Remove( texture.Index, texture.Generation );
		if ( record.External ) throw new InvalidOperationException( "Swap chain images belong to their swap chain" );

		Defer( () => FreeTexture( record ) );
	}

	void FreeTexture( in TextureRecord record )
	{
		if ( record.BindlessIndex >= 0 ) Bindless.RemoveTexture( record.BindlessIndex );
		if ( record.StorageIndex >= 0 ) Bindless.RemoveStorage( record.StorageIndex );
		foreach ( var view in record.Attachments.Views.Values ) Vk.vkDestroyImageView( Vk.Device, view, null );
		if ( record.SrgbView != record.View ) Vk.vkDestroyImageView( Vk.Device, record.SrgbView, null );
		if ( record.View != 0 ) Vk.vkDestroyImageView( Vk.Device, record.View, null );
		if ( record.StorageView != 0 ) Vk.vkDestroyImageView( Vk.Device, record.StorageView, null );
		Vk.vkDestroyImage( Vk.Device, record.Image, null );
		Allocator.Free( record.Memory );
	}

	/// <summary>Destroy a view once the GPU is done with it.</summary>
	public void Destroy( TextureViewHandle view )
	{
		var record = Views.Remove( view.Index, view.Generation );
		Defer( () =>
		{
			if ( record.BindlessIndex >= 0 ) Bindless.RemoveTexture( record.BindlessIndex );
			if ( record.StorageIndex >= 0 ) Bindless.RemoveStorage( record.StorageIndex );
			Vk.vkDestroyImageView( Vk.Device, record.View, null );
		} );
	}

	/// <summary>Destroy a sampler once the GPU is done with it.</summary>
	public void Destroy( SamplerHandle sampler )
	{
		var record = Samplers.Remove( sampler.Index, sampler.Generation );
		Defer( () =>
		{
			Bindless.RemoveSampler( record.Index );
			Vk.vkDestroySampler( Vk.Device, record.Sampler, null );
		} );
	}

	/// <summary>Destroy a shader module. Pipelines made from it keep working.</summary>
	public void Destroy( ShaderHandle shader )
	{
		// The module goes on a worker once no pipeline is being made from a shader, so the caller never waits for one
		var record = Shaders.Remove( shader.Index, shader.Generation );
		lock ( moduleDestroys )
		{
			moduleDestroys.RemoveAll( t => t.IsCompleted );
			moduleDestroys.Add( Task.Run( () =>
			{
				shaderUse.EnterWriteLock();
				try
				{
					Vk.vkDestroyShaderModule( Vk.Device, record.Module, null );
				}
				finally
				{
					shaderUse.ExitWriteLock();
				}
			} ) );
		}

		DropParts( record.Hash );
	}

	/// <summary>Destroy a binding layout once the GPU is done with it.</summary>
	public void Destroy( BindingLayoutHandle layout )
	{
		var record = Layouts.Remove( layout.Index, layout.Generation );

		// Pipeline layouts made with it leave the cache: the driver may give the next set layout the same handle, and a cached
		// layout found by it would point at this one once it's gone. They live on, for pipelines made with them, until shutdown
		lock ( pipelineLayouts )
		{
			List<(ulong, ulong, ulong, ulong)> stale = null;
			foreach ( var key in pipelineLayouts.Keys )
			{
				if ( key.Item1 == record.SetLayout || key.Item2 == record.SetLayout || key.Item3 == record.SetLayout || key.Item4 == record.SetLayout ) (stale ??= new()).Add( key );
			}

			foreach ( var key in stale ?? [] )
			{
				retiredPipelineLayouts.Add( pipelineLayouts[key] );
				pipelineLayouts.Remove( key );
			}
		}

		Defer( () => Vk.vkDestroyDescriptorSetLayout( Vk.Device, record.SetLayout, null ) );
	}

	/// <summary>Destroy a pipeline once the GPU is done with it.</summary>
	public void Destroy( PipelineHandle pipeline )
	{
		// Under the parts' lock, so an optimised link finishing now either replaces the pipeline first or sees it gone
		PipelineRecord record;
		lock ( partLock ) record = Pipelines.Remove( pipeline.Index, pipeline.Generation );
		Defer( () =>
		{
			if ( record.Pipeline != 0 ) Vk.vkDestroyPipeline( Vk.Device, record.Pipeline, null );
			if ( record.Parts is { } parts ) ReleaseParts( parts );
		} );
	}

	readonly List<Task> moduleDestroys = new();

	void DestroyAllResources()
	{
		lock ( moduleDestroys ) Task.WaitAll( [.. moduleDestroys] );
		DestroyBindingSetPools();
		var pipelines = Pipelines.All();
		foreach ( var record in pipelines )
		{
			if ( record.Pipeline != 0 ) Vk.vkDestroyPipeline( Vk.Device, record.Pipeline, null );
		}

		DestroyParts( pipelines );
		foreach ( var record in Shaders.All() ) Vk.vkDestroyShaderModule( Vk.Device, record.Module, null );
		foreach ( var record in Layouts.All() ) Vk.vkDestroyDescriptorSetLayout( Vk.Device, record.SetLayout, null );
		foreach ( var record in Samplers.All() ) Vk.vkDestroySampler( Vk.Device, record.Sampler, null );
		foreach ( var record in Views.All() ) Vk.vkDestroyImageView( Vk.Device, record.View, null );
		foreach ( var record in Textures.All() )
		{
			if ( !record.External ) FreeTexture( record );
		}

		foreach ( var record in Buffers.All() )
		{
			Vk.vkDestroyBuffer( Vk.Device, record.Buffer, null );
			Allocator.Free( record.Memory );
		}
	}

	/// <summary>
	/// Describe what's alive, for leak checks: counts of each kind of resource.
	/// </summary>
	public string DescribeResources()
	{
		var text = new StringBuilder();
		text.Append( $"{Buffers.Count} buffers, {Textures.Count} textures, {Views.Count} views, {Samplers.Count} samplers, " );
		text.Append( $"{Shaders.Count} shaders, {Layouts.Count} layouts, {Pipelines.Count} pipelines; " );
		text.Append( $"{Allocator.Used >> 20} MiB used of {Allocator.Reserved >> 20} MiB reserved" );
		return text.ToString();
	}
}
