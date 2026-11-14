using Borealis.Gpu.Vulkan;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Borealis.Gpu;

/// <summary>
/// Space in a command list's upload memory (<see cref="CommandList.AllocateUpload"/>), valid for the frame it was made in.
/// </summary>
public readonly ref struct UploadAllocation
{
	/// <summary>The buffer it's in, which can be bound as a constant, storage, vertex or index buffer, or copied from.</summary>
	public BufferHandle Buffer { get; init; }

	/// <summary>Where in the buffer it starts.</summary>
	public long Offset { get; init; }

	/// <summary>Its memory, to write.</summary>
	public Span<byte> Data { get; init; }
}

/// <summary>
/// Records work for one queue (<see cref="Device.BeginCommandList"/>). One thread records it at a time; it's submitted
/// with <see cref="Device.Submit"/> and recycled once its frame has finished on the GPU.
/// </summary>
public sealed unsafe class CommandList
{
	const long PageSize = 4 << 20;

	readonly Device device;
	readonly VkDeviceApi vk;
	readonly ulong pool;

	/// <summary>The queue it records for.</summary>
	public QueueType Queue { get; }

	/// <summary>Its name, for debuggers.</summary>
	public string Name { get; private set; }

	internal readonly nint Handle;
	internal SyncPoint Submitted;

	/// <summary>Its progress marker slot (<see cref="DeviceOptions.ProgressMarkers"/>), -1 without.</summary>
	internal int MarkerSlot = -1;

	// The labels open, for naming the markers that end them
	readonly Stack<string> openLabels = new();

	/// <summary>
	/// What it has recorded since it began, for profiling: draw calls (an indirect call counts once), pipeline and descriptor
	/// set binds, rendering scopes begun (resumed ones included), barrier calls, those of them that wait for every stage
	/// (<see cref="PipelineStage.AllCommands"/> or <see cref="PipelineStage.AllGraphics"/> as a source), and scopes that
	/// resumed one suspended before.
	/// </summary>
	public (int Draws, int Pipelines, int Sets, int Scopes, int Barriers, int FullBarriers, int Resumed) Counts => (draws, pipelines, setBinds, scopes, barriers, fullBarriers, resumed);

	int draws, pipelines, setBinds, scopes, barriers, fullBarriers, resumed;

	bool recording;
	bool rendering;
	bool labelled;

	// Suspended rendering scopes (RenderingFlags): whether anything that must not sit between a suspended scope and the one
	// resuming it has been recorded yet, whether the scope open now suspends, and whether the last one ended suspended
	bool acted;
	bool suspending;
	bool suspended;

	/// <summary>The targets of the first scope when it resumes one the list before left suspended (<see cref="StartsResumed"/>).</summary>
	internal readonly RenderingTargets ResumedTargets = new();

	/// <summary>The targets of the last scope, while it's suspended (<see cref="EndsSuspended"/>).</summary>
	internal readonly RenderingTargets SuspendedTargets = new();

	/// <summary>Its first command is a scope resuming one suspended in the list submitted before it.</summary>
	internal bool StartsResumed { get; private set; }

	/// <summary>It ended with its last scope suspended, for the next list submitted to resume.</summary>
	internal bool EndsSuspended { get; private set; }

	// Barriers wait here until the next command that isn't one
	VkImageMemoryBarrier2[] imageBarriers = new VkImageMemoryBarrier2[16];
	VkBufferMemoryBarrier2[] bufferBarriers = new VkBufferMemoryBarrier2[16];
	int imageBarrierCount, bufferBarrierCount;
	VkMemoryBarrier2 memoryBarrier;
	bool hasMemoryBarrier;

	// Pipeline state per bind point (graphics, compute)
	readonly ulong[] layouts = new ulong[2];
	readonly (ulong, ulong, ulong, ulong)[] layoutSets = new (ulong, ulong, ulong, ulong)[2];
	readonly int[] dirty = new int[2];
	ulong lastLayout;
	readonly ulong[] sets = new ulong[4];
	readonly ulong[] setLayouts = new ulong[4];
	const int MaxDynamicOffsets = 16;
	readonly uint[] setOffsets = new uint[4 * MaxDynamicOffsets];
	readonly int[] setOffsetCounts = new int[4];

	// The view set (ShaderAbi.ViewSet), bound beside the bindless set
	ulong viewSet;

	// Transient descriptor sets and upload memory, recycled with the frame
	readonly List<ulong> descriptorPools = new();
	int descriptorPool;
	readonly List<UploadPage> pages = new();
	UploadPage page;

	internal CommandList( Device device, DeviceQueue queue, QueueType type )
	{
		this.device = device;
		vk = device.Vk;
		Queue = type;

		var poolInfo = new VkCommandPoolCreateInfo { sType = VkStructureType.CommandPoolCreateInfo, flags = Vulkan.Vk.CommandPoolTransient, queueFamilyIndex = queue.Family };
		ulong commandPool;
		vk.vkCreateCommandPool( vk.Device, &poolInfo, null, &commandPool ).Check( "vkCreateCommandPool" );
		pool = commandPool;

		var allocate = new VkCommandBufferAllocateInfo { sType = VkStructureType.CommandBufferAllocateInfo, commandPool = pool, commandBufferCount = 1 };
		nint buffer;
		vk.vkAllocateCommandBuffers( vk.Device, &allocate, &buffer ).Check( "vkAllocateCommandBuffers" );
		Handle = buffer;
	}

	internal void Begin( string name )
	{
		Name = name;
		var info = new VkCommandBufferBeginInfo { sType = VkStructureType.CommandBufferBeginInfo, flags = Vulkan.Vk.CommandBufferOneTimeSubmit };
		vk.vkBeginCommandBuffer( Handle, &info ).Check( "vkBeginCommandBuffer" );

		recording = true;
		rendering = false;
		layouts[0] = layouts[1] = 0;
		lastLayout = 0;
		dirty[0] = dirty[1] = 0;
		Array.Clear( sets );
		Array.Clear( setLayouts );
		Array.Clear( setOffsetCounts );
		viewSet = 0;
		Submitted = default;
		draws = pipelines = setBinds = scopes = barriers = fullBarriers = resumed = 0;
		acted = suspending = suspended = false;
		StartsResumed = EndsSuspended = false;

		MarkerSlot = device.ProgressMarkers ? device.TakeMarkerSlot( name ) : -1;
		openLabels.Clear();
		Marker( "begin" );
		labelled = name is not null && device.Instance.vkCmdBeginDebugUtilsLabelEXT != null;
		if ( labelled ) BeginLabel( name );
	}

	internal void End()
	{
		if ( !recording ) throw new InvalidOperationException( "This command list was already submitted" );
		if ( rendering ) throw new InvalidOperationException( "Rendering wasn't ended" );

		FlushBarriers();
		if ( labelled ) EndLabel();
		Marker( "end" );
		EndsSuspended = suspended;
		vk.vkEndCommandBuffer( Handle ).Check( "vkEndCommandBuffer" );
		recording = false;
	}

	/// <summary>Its frame has finished: reset it for reuse.</summary>
	internal void Recycle()
	{
		if ( recording )
		{
			// Recorded but never submitted
			vk.vkEndCommandBuffer( Handle );
			recording = false;
			rendering = false;
		}

		vk.vkResetCommandPool( vk.Device, pool, 0 ).Check( "vkResetCommandPool" );
		foreach ( var p in descriptorPools ) vk.vkResetDescriptorPool( vk.Device, p, 0 );
		descriptorPool = 0;

		foreach ( var p in pages )
		{
			if ( p.Size == PageSize )
			{
				p.Used = 0;
				device.FreePages.Add( p );
			}
			else
			{
				device.Destroy( p.Buffer );
			}
		}

		pages.Clear();
		page = null;
	}

	internal void Destroy()
	{
		foreach ( var p in descriptorPools ) vk.vkDestroyDescriptorPool( vk.Device, p, null );
		descriptorPools.Clear();
		foreach ( var p in pages ) device.Destroy( p.Buffer );
		pages.Clear();
		vk.vkDestroyCommandPool( vk.Device, pool, null );
	}

	void Recording()
	{
		if ( !recording ) throw new InvalidOperationException( "This command list isn't recording" );
	}

	void OutsideRendering( [CallerMemberName] string call = null )
	{
		Recording();
		if ( rendering ) throw new InvalidOperationException( $"{call} isn't allowed while rendering: end rendering first" );
		Act();
	}

	/// <summary>
	/// Work outside rendering is about to be recorded: nothing may sit between a suspended scope and the one resuming it, so
	/// a scope left suspended is ended first, and a scope begun after this can't resume one from the list before.
	/// </summary>
	void Act()
	{
		if ( suspended ) EndSuspended();
		acted = true;
	}

	/// <summary>End the scope left suspended in this list: resume it and end it at once.</summary>
	void EndSuspended()
	{
		suspended = false;
		RecordScope( SuspendedTargets, Vulkan.Vk.RenderingResuming );
	}

	/// <summary>Begin a scope on <paramref name="targets"/> with <paramref name="flags"/> and end it at once, drawing nothing.</summary>
	internal void RecordScope( RenderingTargets targets, uint flags )
	{
		var attachments = stackalloc VkRenderingAttachmentInfo[RenderingTargets.Max];
		var info = targets.Info( attachments, flags );
		vk.vkCmdBeginRendering( Handle, &info );
		vk.vkCmdEndRendering( Handle );
		scopes++;
		if ( (flags & Vulkan.Vk.RenderingResuming) != 0 ) resumed++;
	}

	//
	// Timestamps
	//

	/// <summary>Reset timestamps before writing them again (outside rendering).</summary>
	public void ResetTimestamps( TimestampQueries queries, int first, int count )
	{
		OutsideRendering();
		FlushBarriers();
		vk.vkCmdResetQueryPool( Handle, queries.Pool, (uint)first, (uint)count );
	}

	/// <summary>Write timestamp <paramref name="index"/> once the work before it has passed <paramref name="stage"/>.</summary>
	public void WriteTimestamp( TimestampQueries queries, int index, PipelineStage stage = PipelineStage.AllCommands )
	{
		Recording();
		if ( !rendering ) Act();
		FlushBarriers();
		vk.vkCmdWriteTimestamp2( Handle, (ulong)stage, queries.Pool, (uint)index );
	}

	//
	// Occlusion queries
	//

	/// <summary>Reset occlusion queries before counting into them again (outside rendering).</summary>
	public void ResetQueries( OcclusionQueries queries, int first, int count )
	{
		OutsideRendering();
		FlushBarriers();
		vk.vkCmdResetQueryPool( Handle, queries.Pool, (uint)first, (uint)count );
	}

	/// <summary>
	/// Start counting the samples the draws after this pass into query <paramref name="index"/>, exactly where the device
	/// can (<see cref="DeviceCapabilities.PreciseOcclusion"/>). Begin and end it in one rendering scope.
	/// </summary>
	public void BeginQuery( OcclusionQueries queries, int index )
	{
		if ( !rendering ) throw new InvalidOperationException( "Occlusion queries count inside a rendering scope" );

		// VK_QUERY_CONTROL_PRECISE_BIT
		vk.vkCmdBeginQuery( Handle, queries.Pool, (uint)index, device.Capabilities.PreciseOcclusion ? 1u : 0u );
	}

	/// <summary>Stop counting into query <paramref name="index"/>.</summary>
	public void EndQuery( OcclusionQueries queries, int index )
	{
		if ( !rendering ) throw new InvalidOperationException( "Occlusion queries count inside a rendering scope" );
		vk.vkCmdEndQuery( Handle, queries.Pool, (uint)index );
	}

	//
	// Pipeline statistics queries
	//

	/// <summary>Reset pipeline statistics queries before counting into them again (outside rendering).</summary>
	public void ResetQueries( PipelineStatistics queries, int first, int count )
	{
		OutsideRendering();
		FlushBarriers();
		vk.vkCmdResetQueryPool( Handle, queries.Pool, (uint)first, (uint)count );
	}

	/// <summary>
	/// Start counting what the work after this does into query <paramref name="index"/>. End it where it began: both
	/// outside rendering, or both in one rendering scope.
	/// </summary>
	public void BeginQuery( PipelineStatistics queries, int index )
	{
		Recording();
		if ( !rendering ) Act();
		FlushBarriers();
		vk.vkCmdBeginQuery( Handle, queries.Pool, (uint)index, 0 );
	}

	/// <summary>Stop counting into query <paramref name="index"/>.</summary>
	public void EndQuery( PipelineStatistics queries, int index )
	{
		Recording();
		if ( !rendering ) Act();
		vk.vkCmdEndQuery( Handle, queries.Pool, (uint)index );
	}

	//
	// Debug labels
	//

	/// <summary>
	/// A progress marker (<see cref="DeviceOptions.ProgressMarkers"/>): after the device is lost, its report says whether the
	/// GPU reached it and finished the work before it. Labels write one at either end. Nothing without markers.
	/// </summary>
	public void Marker( string name )
	{
		if ( MarkerSlot < 0 ) return;
		Recording();
		if ( !rendering ) Act();
		device.WriteMarker( Handle, MarkerSlot, name );
	}

	/// <summary>Start a named region, which RenderDoc and validation messages show.</summary>
	public void BeginLabel( string name )
	{
		if ( MarkerSlot >= 0 )
		{
			openLabels.Push( name );
			Marker( name );
		}

		if ( device.Instance.vkCmdBeginDebugUtilsLabelEXT == null ) return;

		Span<byte> utf8 = stackalloc byte[256];
		var length = Encoding.UTF8.GetBytes( name.AsSpan( 0, Math.Min( name.Length, 80 ) ), utf8 );
		utf8[length] = 0;

		fixed ( byte* p = utf8 )
		{
			var label = new VkDebugUtilsLabelEXT { sType = VkStructureType.DebugUtilsLabelExt, pLabelName = p };
			device.Instance.vkCmdBeginDebugUtilsLabelEXT( Handle, &label );
		}
	}

	/// <summary>End the region <see cref="BeginLabel"/> started.</summary>
	public void EndLabel()
	{
		if ( MarkerSlot >= 0 ) Marker( openLabels.TryPop( out var label ) ? $"end {label}" : "end" );
		if ( device.Instance.vkCmdEndDebugUtilsLabelEXT == null ) return;
		device.Instance.vkCmdEndDebugUtilsLabelEXT( Handle );
	}

	//
	// Barriers
	//

	/// <summary>
	/// Order accesses to a texture and move it between layouts. Batched with neighbouring barriers.
	/// </summary>
	public void Barrier( in TextureBarrier barrier )
	{
		OutsideRendering();
		ref var record = ref device.Get( barrier.Texture );
		var mips = barrier.MipCount > 0 ? (uint)barrier.MipCount : Vulkan.Vk.RemainingMipLevels;
		var layers = barrier.LayerCount > 0 ? (uint)barrier.LayerCount : Vulkan.Vk.RemainingArrayLayers;

		if ( imageBarrierCount == imageBarriers.Length ) Array.Resize( ref imageBarriers, imageBarriers.Length * 2 );
		imageBarriers[imageBarrierCount++] = new VkImageMemoryBarrier2
		{
			sType = VkStructureType.ImageMemoryBarrier2,
			srcStageMask = (ulong)barrier.SourceStage,
			srcAccessMask = (ulong)barrier.SourceAccess,
			dstStageMask = (ulong)barrier.DestinationStage,
			dstAccessMask = (ulong)barrier.DestinationAccess,
			oldLayout = barrier.OldLayout.ToVk(),
			newLayout = barrier.NewLayout.ToVk(),
			srcQueueFamilyIndex = Vulkan.Vk.QueueFamilyIgnored,
			dstQueueFamilyIndex = Vulkan.Vk.QueueFamilyIgnored,
			image = record.Image,
			subresourceRange = new VkImageSubresourceRange { aspectMask = record.Aspect, baseMipLevel = (uint)barrier.BaseMip, levelCount = mips, baseArrayLayer = (uint)barrier.BaseLayer, layerCount = layers },
		};
	}

	/// <summary>
	/// Order accesses to a buffer. Batched with neighbouring barriers.
	/// </summary>
	public void Barrier( in BufferBarrier barrier )
	{
		OutsideRendering();
		ref var record = ref device.Get( barrier.Buffer );

		if ( bufferBarrierCount == bufferBarriers.Length ) Array.Resize( ref bufferBarriers, bufferBarriers.Length * 2 );
		bufferBarriers[bufferBarrierCount++] = new VkBufferMemoryBarrier2
		{
			sType = VkStructureType.BufferMemoryBarrier2,
			srcStageMask = (ulong)barrier.SourceStage,
			srcAccessMask = (ulong)barrier.SourceAccess,
			dstStageMask = (ulong)barrier.DestinationStage,
			dstAccessMask = (ulong)barrier.DestinationAccess,
			srcQueueFamilyIndex = Vulkan.Vk.QueueFamilyIgnored,
			dstQueueFamilyIndex = Vulkan.Vk.QueueFamilyIgnored,
			buffer = record.Buffer,
			offset = (ulong)barrier.Offset,
			size = barrier.Size > 0 ? (ulong)barrier.Size : Vulkan.Vk.WholeSize,
		};
	}

	/// <summary>
	/// Order every access of the source stages before the destination stages: no layout changes.
	/// </summary>
	public void Barrier( PipelineStage sourceStage, MemoryAccess sourceAccess, PipelineStage destinationStage, MemoryAccess destinationAccess )
	{
		OutsideRendering();
		if ( !hasMemoryBarrier ) memoryBarrier = new VkMemoryBarrier2 { sType = VkStructureType.MemoryBarrier2 };
		memoryBarrier.srcStageMask |= (ulong)sourceStage;
		memoryBarrier.srcAccessMask |= (ulong)sourceAccess;
		memoryBarrier.dstStageMask |= (ulong)destinationStage;
		memoryBarrier.dstAccessMask |= (ulong)destinationAccess;
		hasMemoryBarrier = true;
	}

	void FlushBarriers()
	{
		if ( imageBarrierCount == 0 && bufferBarrierCount == 0 && !hasMemoryBarrier ) return;

		fixed ( VkImageMemoryBarrier2* images = imageBarriers )
		fixed ( VkBufferMemoryBarrier2* buffers = bufferBarriers )
		fixed ( VkMemoryBarrier2* memory = &memoryBarrier )
		{
			var info = new VkDependencyInfo
			{
				sType = VkStructureType.DependencyInfo,
				memoryBarrierCount = hasMemoryBarrier ? 1u : 0u,
				pMemoryBarriers = memory,
				bufferMemoryBarrierCount = (uint)bufferBarrierCount,
				pBufferMemoryBarriers = buffers,
				imageMemoryBarrierCount = (uint)imageBarrierCount,
				pImageMemoryBarriers = images,
			};

			vk.vkCmdPipelineBarrier2( Handle, &info );
		}

		barriers++;
		const ulong Full = (ulong)(PipelineStage.AllCommands | PipelineStage.AllGraphics);
		var full = hasMemoryBarrier && (memoryBarrier.srcStageMask & Full) != 0;
		for ( int i = 0; i < imageBarrierCount && !full; i++ ) full = (imageBarriers[i].srcStageMask & Full) != 0;
		for ( int i = 0; i < bufferBarrierCount && !full; i++ ) full = (bufferBarriers[i].srcStageMask & Full) != 0;
		if ( full ) fullBarriers++;

		imageBarrierCount = 0;
		bufferBarrierCount = 0;
		hasMemoryBarrier = false;
	}

	//
	// Rendering
	//

	/// <summary>
	/// Start drawing into colour targets and an optional depth target, which must already be in
	/// <see cref="ImageLayout.ColorTarget"/> and <see cref="ImageLayout.DepthTarget"/>. The viewport and scissor are set to
	/// the whole of the first target.
	/// </summary>
	/// <remarks>
	/// <paramref name="flags"/> carry one scope across lists recorded apart, as one render pass on the GPU:
	/// <see cref="RenderingFlags.Suspend"/> leaves the scope suspended when it ends, for the next list submitted to resume;
	/// <see cref="RenderingFlags.Resume"/> continues the scope the list submitted before left suspended (its load operations
	/// don't apply). Neither has to be matched: a scope suspended with no resume after it, or anything recorded between, is
	/// ended there, and a resume that's not the list's first command begins an ordinary scope. Lists submitted apart
	/// (<see cref="Device.Submit"/> and <see cref="Device.SubmitLater"/> both pair them) end and begin theirs on their own.
	/// </remarks>
	public void BeginRendering( ReadOnlySpan<ColorTarget> colors, in DepthTarget depth = default, RenderingFlags flags = RenderingFlags.None )
	{
		Recording();
		if ( rendering ) throw new InvalidOperationException( "BeginRendering isn't allowed while rendering: end rendering first" );

		// Only the list's first command resumes the list before's scope; a scope suspended in this list is ended first
		var resume = (flags & RenderingFlags.Resume) != 0 && !acted;
		if ( suspended ) EndSuspended();
		FlushBarriers();

		if ( colors.Length > 8 ) throw new ArgumentException( "At most 8 colour targets" );

		var attachments = stackalloc VkRenderingAttachmentInfo[Math.Max( colors.Length, 1 )];
		uint width = 0, height = 0;

		for ( int i = 0; i < colors.Length; i++ )
		{
			ref readonly var color = ref colors[i];
			ref var record = ref device.Get( color.Texture );
			if ( i == 0 )
			{
				width = (uint)Math.Max( record.Info.Width >> color.Mip, 1 );
				height = (uint)Math.Max( record.Info.Height >> color.Mip, 1 );
			}

			attachments[i] = new VkRenderingAttachmentInfo
			{
				sType = VkStructureType.RenderingAttachmentInfo,
				imageView = device.AttachmentView( color.Texture, color.Mip, color.Layer, color.Srgb ),
				imageLayout = VkImageLayout.ColorAttachmentOptimal,
				loadOp = (int)color.Load,
				storeOp = (int)color.Store,
			};

			attachments[i].clearValue.color[0] = color.ClearColor.X;
			attachments[i].clearValue.color[1] = color.ClearColor.Y;
			attachments[i].clearValue.color[2] = color.ClearColor.Z;
			attachments[i].clearValue.color[3] = color.ClearColor.W;

			if ( color.ResolveTarget.IsValid )
			{
				attachments[i].resolveMode = IsIntegerFormat( record.Info.Format ) ? Vulkan.Vk.ResolveModeSampleZero : Vulkan.Vk.ResolveModeAverage;
				attachments[i].resolveImageView = device.AttachmentView( color.ResolveTarget, 0, 0, color.Srgb );
				attachments[i].resolveImageLayout = VkImageLayout.ColorAttachmentOptimal;
			}
		}

		VkRenderingAttachmentInfo depthAttachment = default;
		var hasDepth = depth.Texture.IsValid;
		var hasStencil = false;
		if ( hasDepth )
		{
			ref var record = ref device.Get( depth.Texture );
			hasStencil = record.Info.Format.HasStencil();
			if ( colors.Length == 0 )
			{
				width = (uint)Math.Max( record.Info.Width >> depth.Mip, 1 );
				height = (uint)Math.Max( record.Info.Height >> depth.Mip, 1 );
			}

			depthAttachment = new VkRenderingAttachmentInfo
			{
				sType = VkStructureType.RenderingAttachmentInfo,
				imageView = device.AttachmentView( depth.Texture, depth.Mip, depth.Layer, false ),
				imageLayout = depth.ReadOnly ? VkImageLayout.ReadOnlyOptimal : VkImageLayout.DepthStencilAttachmentOptimal,
				loadOp = (int)depth.Load,
				storeOp = (int)depth.Store,
			};

			depthAttachment.clearValue.depth = depth.ClearDepth;
			depthAttachment.clearValue.stencil = depth.ClearStencil;
		}

		if ( width == 0 ) throw new ArgumentException( "Rendering needs a colour or depth target" );

		var suspend = (flags & RenderingFlags.Suspend) != 0;
		var info = new VkRenderingInfo
		{
			sType = VkStructureType.RenderingInfo,
			flags = (resume ? Vulkan.Vk.RenderingResuming : 0) | (suspend ? Vulkan.Vk.RenderingSuspending : 0),
			renderArea = new VkRect2D { extent = new VkExtent2D { width = width, height = height } },
			layerCount = 1,
			colorAttachmentCount = (uint)colors.Length,
			pColorAttachments = attachments,
			pDepthAttachment = hasDepth ? &depthAttachment : null,
			pStencilAttachment = hasStencil ? &depthAttachment : null,
		};

		// What pairing the scope with another list's needs: its targets
		if ( resume )
		{
			ResumedTargets.Set( attachments, colors.Length, hasDepth ? &depthAttachment : null, hasStencil, width, height );
			StartsResumed = true;
			resumed++;
		}

		if ( suspend ) SuspendedTargets.Set( attachments, colors.Length, hasDepth ? &depthAttachment : null, hasStencil, width, height );

		vk.vkCmdBeginRendering( Handle, &info );
		scopes++;
		acted = true;
		suspending = suspend;
		rendering = true;
		RenderSize = (int.CreateTruncating( width ), int.CreateTruncating( height ));

		SetViewport( 0, 0, width, height );
		SetScissor( 0, 0, (int)width, (int)height );
	}

	/// <summary>The size of what's being rendered to, while rendering.</summary>
	public (int Width, int Height) RenderSize { get; private set; }

	/// <summary>Whether a rendering scope is open.</summary>
	public bool IsRendering => rendering;

	static bool IsIntegerFormat( Format format ) => format is Format.RGBA8Uint or Format.R16Uint or Format.RGBA16Uint or Format.R32Uint or Format.R32Sint or Format.RG32Uint or Format.RGBA32Uint;

	/// <summary>Finish drawing into the targets <see cref="BeginRendering"/> named.</summary>
	public void EndRendering()
	{
		Recording();
		if ( !rendering ) throw new InvalidOperationException( "Not rendering" );
		vk.vkCmdEndRendering( Handle );
		rendering = false;
		suspended = suspending;
		suspending = false;
	}

	/// <summary>
	/// Set the viewport, top-left origin, and its depth range. Clip space is Vulkan's: shaders compiled from HLSL invert Y
	/// at output, as s&amp;box's are (Slang's <c>VulkanInvertY</c>, DXC's <c>-fvk-invert-y</c>).
	/// </summary>
	public void SetViewport( float x, float y, float width, float height, float minDepth = 0, float maxDepth = 1 )
	{
		var viewport = new VkViewport { x = x, y = y, width = width, height = height, minDepth = minDepth, maxDepth = maxDepth };
		vk.vkCmdSetViewport( Handle, 0, 1, &viewport );
	}

	/// <summary>Set the scissor rectangle.</summary>
	public void SetScissor( int x, int y, int width, int height )
	{
		var rect = new VkRect2D { offset = new VkOffset2D { x = Math.Max( x, 0 ), y = Math.Max( y, 0 ) }, extent = new VkExtent2D { width = (uint)Math.Max( width, 0 ), height = (uint)Math.Max( height, 0 ) } };
		vk.vkCmdSetScissor( Handle, 0, 1, &rect );
	}

	/// <summary>Set depth bias, for pipelines made with <see cref="GraphicsPipelineInfo.DepthBias"/>.</summary>
	public void SetDepthBias( float constant, float slope, float clamp = 0 ) => vk.vkCmdSetDepthBias( Handle, constant, clamp, slope );

	/// <summary>Set the stencil reference, for pipelines that test stencil.</summary>
	public void SetStencilReference( byte reference ) => vk.vkCmdSetStencilReference( Handle, 3, reference );

	/// <summary>
	/// Bind a pipeline. Changing to a pipeline whose sets differ keeps only the transient sets that still match.
	/// </summary>
	public void SetPipeline( PipelineHandle pipeline )
	{
		Recording();
		ref var record = ref device.Get( pipeline );
		if ( record.BindPoint == Vulkan.Vk.BindPointCompute ) OutsideRendering();
		FlushBarriers();

		vk.vkCmdBindPipeline( Handle, record.BindPoint, record.Pipeline );
		pipelines++;
		lastLayout = record.Layout;

		if ( layouts[record.BindPoint] != record.Layout )
		{
			layouts[record.BindPoint] = record.Layout;
			layoutSets[record.BindPoint] = device.SetLayoutsOf( record.Layout );
			dirty[record.BindPoint] = 0x3F;
		}
	}

	/// <summary>Bind a vertex buffer to a binding the pipeline reads.</summary>
	public void SetVertexBuffer( int slot, BufferHandle buffer, long offset = 0 )
	{
		var vkBuffer = device.Get( buffer ).Buffer;
		var vkOffset = (ulong)offset;
		vk.vkCmdBindVertexBuffers( Handle, (uint)slot, 1, &vkBuffer, &vkOffset );
	}

	/// <summary>Bind the index buffer.</summary>
	public void SetIndexBuffer( BufferHandle buffer, IndexFormat format, long offset = 0 ) => vk.vkCmdBindIndexBuffer( Handle, device.Get( buffer ).Buffer, (ulong)offset, (int)format );

	/// <summary>
	/// Fill a transient descriptor set (0 to 3) with <paramref name="bindings"/> laid out as <paramref name="layout"/>, and
	/// bind it for the draws and dispatches that follow. The set lives until the frame is recycled.
	/// </summary>
	public void SetBindings( int set, BindingLayoutHandle layout, ReadOnlySpan<Binding> bindings )
	{
		Recording();
		if ( (uint)set >= ShaderAbi.BindlessSet ) throw new ArgumentOutOfRangeException( nameof( set ), "Transient sets are 0 to 3" );

		var setLayout = device.Get( layout ).SetLayout;
		var descriptorSet = AllocateSet( setLayout );

		device.WriteDescriptors( descriptorSet, bindings );
		sets[set] = descriptorSet;
		setOffsetCounts[set] = 0;
		setLayouts[set] = setLayout;
		dirty[0] |= 1 << set;
		dirty[1] |= 1 << set;
	}

	/// <summary>
	/// Bind a persistent set (<see cref="Device.CreateBindingSet"/>) for the draws and dispatches that follow, with the offsets
	/// of its <see cref="BindingType.UniformBufferDynamic"/> bindings in binding order.
	/// </summary>
	public void SetBindingSet( int set, BindingSetHandle bindingSet, ReadOnlySpan<uint> dynamicOffsets = default )
	{
		if ( (uint)set >= ShaderAbi.BindlessSet ) throw new ArgumentOutOfRangeException( nameof( set ), "Pipeline sets are 0 to 3" );
		if ( dynamicOffsets.Length > MaxDynamicOffsets ) throw new ArgumentException( $"At most {MaxDynamicOffsets} dynamic offsets a set" );

		ref var record = ref device.Get( bindingSet );
		var changed = sets[set] != record.Set || setOffsetCounts[set] != dynamicOffsets.Length || !dynamicOffsets.SequenceEqual( setOffsets.AsSpan( set * MaxDynamicOffsets, dynamicOffsets.Length ) );
		if ( !changed ) return;

		sets[set] = record.Set;
		setLayouts[set] = record.SetLayout;
		dynamicOffsets.CopyTo( setOffsets.AsSpan( set * MaxDynamicOffsets ) );
		setOffsetCounts[set] = dynamicOffsets.Length;
		dirty[0] |= 1 << set;
		dirty[1] |= 1 << set;
	}

	/// <summary>
	/// Bind the view's set (<see cref="ShaderAbi.ViewSet"/>, laid out as <see cref="Device.ViewSetLayout"/>) for the draws
	/// and dispatches that follow, in every pipeline. Without one, a pipeline whose shaders read it can't draw.
	/// </summary>
	public void SetViewSet( BindingSetHandle bindingSet )
	{
		ref var record = ref device.Get( bindingSet );
		if ( record.SetLayout != device.Get( device.ViewSetLayout ).SetLayout ) throw new ArgumentException( "A view set is laid out as Device.ViewSetLayout" );
		if ( viewSet == record.Set ) return;

		viewSet = record.Set;
		dirty[0] |= 1 << ShaderAbi.ViewSet;
		dirty[1] |= 1 << ShaderAbi.ViewSet;
	}

	ulong AllocateSet( ulong setLayout )
	{
		while ( true )
		{
			if ( descriptorPool == descriptorPools.Count ) descriptorPools.Add( CreateDescriptorPool() );

			var allocate = new VkDescriptorSetAllocateInfo { sType = VkStructureType.DescriptorSetAllocateInfo, descriptorPool = descriptorPools[descriptorPool], descriptorSetCount = 1, pSetLayouts = &setLayout };
			ulong set;
			var result = vk.vkAllocateDescriptorSets( vk.Device, &allocate, &set );
			if ( result == VkResult.Success ) return set;
			if ( result is not (VkResult.ErrorOutOfPoolMemory or VkResult.ErrorFragmentedPool) ) result.Check( "vkAllocateDescriptorSets" );

			descriptorPool++;
		}
	}

	ulong CreateDescriptorPool()
	{
		var sizes = stackalloc VkDescriptorPoolSize[5];
		sizes[0] = new VkDescriptorPoolSize { type = VkDescriptorType.UniformBuffer, descriptorCount = 1024 };
		sizes[1] = new VkDescriptorPoolSize { type = VkDescriptorType.StorageBuffer, descriptorCount = 1024 };
		sizes[2] = new VkDescriptorPoolSize { type = VkDescriptorType.SampledImage, descriptorCount = 1024 };
		sizes[3] = new VkDescriptorPoolSize { type = VkDescriptorType.StorageImage, descriptorCount = 256 };
		sizes[4] = new VkDescriptorPoolSize { type = VkDescriptorType.Sampler, descriptorCount = 256 };

		var info = new VkDescriptorPoolCreateInfo { sType = VkStructureType.DescriptorPoolCreateInfo, maxSets = 512, poolSizeCount = 5, pPoolSizes = sizes };
		ulong descriptorPool;
		vk.vkCreateDescriptorPool( vk.Device, &info, null, &descriptorPool ).Check( "vkCreateDescriptorPool" );
		return descriptorPool;
	}

	void FlushBindings( int bindPoint )
	{
		var layout = layouts[bindPoint];
		if ( layout == 0 ) throw new InvalidOperationException( bindPoint == 0 ? "No graphics pipeline is bound" : "No compute pipeline is bound" );

		var mask = dirty[bindPoint];
		if ( mask == 0 ) return;

		var expected = layoutSets[bindPoint];
		for ( int set = 0; set < 4; set++ )
		{
			if ( (mask & (1 << set)) == 0 || sets[set] == 0 ) continue;

			var wanted = set switch { 0 => expected.Item1, 1 => expected.Item2, 2 => expected.Item3, _ => expected.Item4 };
			if ( wanted != setLayouts[set] ) continue;

			var descriptorSet = sets[set];
			fixed ( uint* offsets = &setOffsets[set * MaxDynamicOffsets] )
				vk.vkCmdBindDescriptorSets( Handle, bindPoint, layout, (uint)set, 1, &descriptorSet, (uint)setOffsetCounts[set], offsets );
			setBinds++;
		}

		if ( (mask & (1 << ShaderAbi.BindlessSet)) != 0 )
		{
			var bindless = device.Bindless.Set;
			vk.vkCmdBindDescriptorSets( Handle, bindPoint, layout, ShaderAbi.BindlessSet, 1, &bindless, 0, null );
		}

		if ( (mask & (1 << ShaderAbi.ViewSet)) != 0 )
		{
			// Outside a view (UI, tools), the device's placeholders: nothing to light with
			var view = viewSet != 0 ? viewSet : device.DefaultViewSet.IsValid ? device.Get( device.DefaultViewSet ).Set : 0;
			if ( view != 0 ) vk.vkCmdBindDescriptorSets( Handle, bindPoint, layout, ShaderAbi.ViewSet, 1, &view, 0, null );
		}

		dirty[bindPoint] = 0;
	}

	/// <summary>
	/// Set push constants, which every pipeline has <see cref="ShaderAbi.PushConstantSize"/> bytes of, for every stage.
	/// </summary>
	public void PushConstants<T>( in T data, int offset = 0 ) where T : unmanaged
	{
		if ( lastLayout == 0 ) throw new InvalidOperationException( "Bind a pipeline before setting push constants" );
		if ( offset + sizeof( T ) > ShaderAbi.PushConstantSize ) throw new ArgumentException( $"Push constants are {ShaderAbi.PushConstantSize} bytes" );

		fixed ( T* p = &data ) vk.vkCmdPushConstants( Handle, lastLayout, Vulkan.Vk.StageAllGraphics | Vulkan.Vk.StageCompute, (uint)offset, (uint)sizeof( T ), p );
	}

	//
	// Work
	//

	void BeforeDraw()
	{
		Recording();
		if ( !rendering ) throw new InvalidOperationException( "Draws happen while rendering: call BeginRendering first" );
		FlushBindings( Vulkan.Vk.BindPointGraphics );
		draws++;
	}

	/// <summary>Draw vertices.</summary>
	public void Draw( int vertexCount, int instanceCount = 1, int firstVertex = 0, int firstInstance = 0 )
	{
		BeforeDraw();
		vk.vkCmdDraw( Handle, (uint)vertexCount, (uint)instanceCount, (uint)firstVertex, (uint)firstInstance );
	}

	/// <summary>Draw indexed vertices.</summary>
	public void DrawIndexed( int indexCount, int instanceCount = 1, int firstIndex = 0, int vertexOffset = 0, int firstInstance = 0 )
	{
		BeforeDraw();
		vk.vkCmdDrawIndexed( Handle, (uint)indexCount, (uint)instanceCount, (uint)firstIndex, vertexOffset, (uint)firstInstance );
	}

	/// <summary>Draw indexed vertices with arguments from a buffer: <paramref name="count"/> draws, <paramref name="stride"/> bytes apart.</summary>
	public void DrawIndexedIndirect( BufferHandle arguments, long offset, int count, int stride = 20 )
	{
		BeforeDraw();
		vk.vkCmdDrawIndexedIndirect( Handle, device.Get( arguments ).Buffer, (ulong)offset, (uint)count, (uint)stride );
	}

	/// <summary>Draw indexed vertices with arguments and a draw count from buffers, at most <paramref name="maxCount"/> draws.</summary>
	public void DrawIndexedIndirectCount( BufferHandle arguments, long offset, BufferHandle countBuffer, long countOffset, int maxCount, int stride = 20 )
	{
		BeforeDraw();
		vk.vkCmdDrawIndexedIndirectCount( Handle, device.Get( arguments ).Buffer, (ulong)offset, device.Get( countBuffer ).Buffer, (ulong)countOffset, (uint)maxCount, (uint)stride );
	}

	/// <summary>Run the bound compute pipeline over a grid of thread groups.</summary>
	public void Dispatch( int x, int y = 1, int z = 1 )
	{
		OutsideRendering();
		FlushBarriers();
		FlushBindings( Vulkan.Vk.BindPointCompute );
		vk.vkCmdDispatch( Handle, (uint)x, (uint)y, (uint)z );
	}

	/// <summary>Run the bound compute pipeline with the group counts in a buffer.</summary>
	public void DispatchIndirect( BufferHandle arguments, long offset = 0 )
	{
		OutsideRendering();
		FlushBarriers();
		FlushBindings( Vulkan.Vk.BindPointCompute );
		vk.vkCmdDispatchIndirect( Handle, device.Get( arguments ).Buffer, (ulong)offset );
	}

	//
	// Transfers
	//

	/// <summary>
	/// Space in this frame's upload memory, aligned to <paramref name="alignment"/> bytes: write it now, read it on the GPU
	/// in this frame (as constants, vertices, or the source of a copy).
	/// </summary>
	public UploadAllocation AllocateUpload( int size, int alignment = 256 )
	{
		var align = (long)Math.Max( alignment, 16 );
		if ( page is null || (page.Used + align - 1) / align * align + size > page.Size ) NewPage( size );

		var offset = (page.Used + align - 1) / align * align;
		page.Used = offset + size;
		return new UploadAllocation { Buffer = page.Buffer, Offset = offset, Data = new Span<byte>( (void*)(page.Mapped + (nint)offset), size ) };
	}

	/// <summary>Copy <paramref name="value"/> into upload memory and return where, for binding as constants.</summary>
	public (BufferHandle Buffer, long Offset) Upload<T>( in T value ) where T : unmanaged
	{
		var allocation = AllocateUpload( sizeof( T ), device.Capabilities.UniformAlignment );
		MemoryMarshal.Write( allocation.Data, in value );
		return (allocation.Buffer, allocation.Offset);
	}

	void NewPage( int size )
	{
		UploadPage fresh;
		if ( size <= PageSize && device.FreePages.TryTake( out var recycled ) )
		{
			fresh = recycled;
		}
		else
		{
			var pageSize = Math.Max( PageSize, (size + 0xFFFF) & ~0xFFFFL );
			var buffer = device.CreateBuffer( new BufferInfo
			{
				Size = pageSize,
				Usage = BufferUsage.Uniform | BufferUsage.Storage | BufferUsage.Vertex | BufferUsage.Index | BufferUsage.Indirect,
				Memory = MemoryLocation.Upload,
				Name = "Upload page",
			} );

			ref var record = ref device.Get( buffer );
			fresh = new UploadPage { Buffer = buffer, VkBuffer = record.Buffer, Mapped = record.Memory.Mapped, Size = pageSize };
		}

		pages.Add( fresh );
		page = fresh;
	}

	/// <summary>
	/// Copy data into a buffer, through upload memory, ordered with the rest of the list. Barrier the destination for
	/// whatever reads it next.
	/// </summary>
	public void UpdateBuffer( BufferHandle buffer, ReadOnlySpan<byte> data, long offset = 0 )
	{
		OutsideRendering();
		if ( data.IsEmpty ) return;

		var allocation = AllocateUpload( data.Length, 16 );
		data.CopyTo( allocation.Data );
		CopyBuffer( allocation.Buffer, allocation.Offset, buffer, offset, data.Length );
	}

	/// <summary>
	/// Copy elements into a buffer, through upload memory, ordered with the rest of the list.
	/// </summary>
	public void UpdateBuffer<T>( BufferHandle buffer, ReadOnlySpan<T> data, long offset = 0 ) where T : unmanaged => UpdateBuffer( buffer, MemoryMarshal.AsBytes( data ), offset );

	/// <summary>
	/// Copy tightly packed rows into a region of one mip and layer of a texture, which must be in
	/// <see cref="ImageLayout.CopyDestination"/>.
	/// </summary>
	public void UpdateTexture( TextureHandle texture, ReadOnlySpan<byte> data, int mip, int layer, int width, int height, int x = 0, int y = 0 )
	{
		OutsideRendering();
		var allocation = AllocateUpload( data.Length, 16 );
		data.CopyTo( allocation.Data );
		CopyBufferToTexture( allocation.Buffer, allocation.Offset, texture, mip, layer, width, height, x, y );
	}

	/// <summary>Copy bytes between buffers.</summary>
	public void CopyBuffer( BufferHandle source, long sourceOffset, BufferHandle destination, long destinationOffset, long size )
	{
		OutsideRendering();
		FlushBarriers();
		var region = new VkBufferCopy { srcOffset = (ulong)sourceOffset, dstOffset = (ulong)destinationOffset, size = (ulong)size };
		vk.vkCmdCopyBuffer( Handle, device.Get( source ).Buffer, device.Get( destination ).Buffer, 1, &region );
	}

	/// <summary>
	/// Copy tightly packed rows from a buffer into a region of one mip and layer of a texture in
	/// <see cref="ImageLayout.CopyDestination"/>.
	/// </summary>
	public void CopyBufferToTexture( BufferHandle source, long offset, TextureHandle texture, int mip, int layer, int width, int height, int x = 0, int y = 0 )
	{
		OutsideRendering();
		FlushBarriers();
		ref var record = ref device.Get( texture );
		var region = new VkBufferImageCopy
		{
			bufferOffset = (ulong)offset,
			imageSubresource = new VkImageSubresourceLayers { aspectMask = record.Aspect & ~Vulkan.Vk.AspectStencil, mipLevel = (uint)mip, baseArrayLayer = (uint)layer, layerCount = 1 },
			imageOffset = new VkOffset3D { x = x, y = y },
			imageExtent = new VkExtent3D { width = (uint)width, height = (uint)height, depth = 1 },
		};

		vk.vkCmdCopyBufferToImage( Handle, device.Get( source ).Buffer, record.Image, VkImageLayout.TransferDstOptimal, 1, &region );
	}

	/// <summary>
	/// Copy one mip and layer of a texture in <see cref="ImageLayout.CopySource"/> into a buffer as tightly packed rows.
	/// </summary>
	public void CopyTextureToBuffer( TextureHandle texture, int mip, int layer, BufferHandle destination, long offset = 0 )
	{
		OutsideRendering();
		FlushBarriers();
		ref var record = ref device.Get( texture );
		var region = new VkBufferImageCopy
		{
			bufferOffset = (ulong)offset,
			imageSubresource = new VkImageSubresourceLayers { aspectMask = record.Aspect & ~Vulkan.Vk.AspectStencil, mipLevel = (uint)mip, baseArrayLayer = (uint)layer, layerCount = 1 },
			imageExtent = new VkExtent3D { width = (uint)Math.Max( record.Info.Width >> mip, 1 ), height = (uint)Math.Max( record.Info.Height >> mip, 1 ), depth = 1 },
		};

		vk.vkCmdCopyImageToBuffer( Handle, record.Image, VkImageLayout.TransferSrcOptimal, device.Get( destination ).Buffer, 1, &region );
	}

	/// <summary>
	/// Copy the first <paramref name="layers"/> layers of one mip between textures of the same size and format, from
	/// <see cref="ImageLayout.CopySource"/> to <see cref="ImageLayout.CopyDestination"/>.
	/// </summary>
	public void CopyTexture( TextureHandle source, TextureHandle destination, int mip = 0, int layers = 1 )
	{
		OutsideRendering();
		FlushBarriers();
		ref var src = ref device.Get( source );
		ref var dst = ref device.Get( destination );
		var region = new VkImageCopy
		{
			srcSubresource = new VkImageSubresourceLayers { aspectMask = src.Aspect, mipLevel = (uint)mip, layerCount = (uint)layers },
			dstSubresource = new VkImageSubresourceLayers { aspectMask = dst.Aspect, mipLevel = (uint)mip, layerCount = (uint)layers },
			extent = new VkExtent3D { width = (uint)Math.Max( src.Info.Width >> mip, 1 ), height = (uint)Math.Max( src.Info.Height >> mip, 1 ), depth = 1 },
		};

		vk.vkCmdCopyImage( Handle, src.Image, VkImageLayout.TransferSrcOptimal, dst.Image, VkImageLayout.TransferDstOptimal, 1, &region );
	}

	/// <summary>
	/// Copy a rectangle of mip 0, layer 0 between textures of the same format, from <see cref="ImageLayout.CopySource"/> to
	/// <see cref="ImageLayout.CopyDestination"/>.
	/// </summary>
	public void CopyTextureRegion( TextureHandle source, TextureHandle destination, int sourceX, int sourceY, int destinationX, int destinationY, int width, int height )
	{
		if ( width <= 0 || height <= 0 ) return;
		OutsideRendering();
		FlushBarriers();
		ref var src = ref device.Get( source );
		ref var dst = ref device.Get( destination );
		var region = new VkImageCopy
		{
			srcSubresource = new VkImageSubresourceLayers { aspectMask = src.Aspect, layerCount = 1 },
			srcOffset = new VkOffset3D { x = sourceX, y = sourceY },
			dstSubresource = new VkImageSubresourceLayers { aspectMask = dst.Aspect, layerCount = 1 },
			dstOffset = new VkOffset3D { x = destinationX, y = destinationY },
			extent = new VkExtent3D { width = (uint)width, height = (uint)height, depth = 1 },
		};

		vk.vkCmdCopyImage( Handle, src.Image, VkImageLayout.TransferSrcOptimal, dst.Image, VkImageLayout.TransferDstOptimal, 1, &region );
	}

	/// <summary>
	/// Scale one texture region into another with filtering, converting formats, from <see cref="ImageLayout.CopySource"/>
	/// to <see cref="ImageLayout.CopyDestination"/>.
	/// </summary>
	public void Blit( TextureHandle source, (int X, int Y, int Width, int Height) from, TextureHandle destination, (int X, int Y, int Width, int Height) to, Filter filter = Filter.Linear, int sourceMip = 0, int destinationMip = 0 )
	{
		OutsideRendering();
		FlushBarriers();
		ref var src = ref device.Get( source );
		ref var dst = ref device.Get( destination );
		var region = new VkImageBlit
		{
			srcSubresource = new VkImageSubresourceLayers { aspectMask = src.Aspect, mipLevel = (uint)sourceMip, layerCount = 1 },
			srcOffset0 = new VkOffset3D { x = from.X, y = from.Y },
			srcOffset1 = new VkOffset3D { x = from.X + from.Width, y = from.Y + from.Height, z = 1 },
			dstSubresource = new VkImageSubresourceLayers { aspectMask = dst.Aspect, mipLevel = (uint)destinationMip, layerCount = 1 },
			dstOffset0 = new VkOffset3D { x = to.X, y = to.Y },
			dstOffset1 = new VkOffset3D { x = to.X + to.Width, y = to.Y + to.Height, z = 1 },
		};

		vk.vkCmdBlitImage( Handle, src.Image, VkImageLayout.TransferSrcOptimal, dst.Image, VkImageLayout.TransferDstOptimal, 1, &region, (int)filter );
	}

	/// <summary>
	/// Resolve a multisampled texture into a single-sampled one of the same size, from <see cref="ImageLayout.CopySource"/>
	/// to <see cref="ImageLayout.CopyDestination"/>.
	/// </summary>
	public void ResolveTexture( TextureHandle source, TextureHandle destination )
	{
		OutsideRendering();
		FlushBarriers();
		ref var src = ref device.Get( source );
		ref var dst = ref device.Get( destination );
		var region = new VkImageResolve
		{
			srcSubresource = new VkImageSubresourceLayers { aspectMask = Vulkan.Vk.AspectColor, layerCount = 1 },
			dstSubresource = new VkImageSubresourceLayers { aspectMask = Vulkan.Vk.AspectColor, layerCount = 1 },
			extent = new VkExtent3D { width = (uint)src.Info.Width, height = (uint)src.Info.Height, depth = 1 },
		};

		vk.vkCmdResolveImage( Handle, src.Image, VkImageLayout.TransferSrcOptimal, dst.Image, VkImageLayout.TransferDstOptimal, 1, &region );
	}

	/// <summary>
	/// Clear a whole colour texture, or depth and stencil, in <see cref="ImageLayout.CopyDestination"/>.
	/// </summary>
	public void ClearTexture( TextureHandle texture, System.Numerics.Vector4 color = default, float depth = 0, byte stencil = 0 )
	{
		OutsideRendering();
		FlushBarriers();
		ref var record = ref device.Get( texture );
		var range = new VkImageSubresourceRange { aspectMask = record.Aspect, levelCount = Vulkan.Vk.RemainingMipLevels, layerCount = Vulkan.Vk.RemainingArrayLayers };
		var value = new VkClearValue();

		if ( record.Info.Format.IsDepth() )
		{
			value.depth = depth;
			value.stencil = stencil;
			vk.vkCmdClearDepthStencilImage( Handle, record.Image, VkImageLayout.TransferDstOptimal, &value, 1, &range );
		}
		else
		{
			value.color[0] = color.X;
			value.color[1] = color.Y;
			value.color[2] = color.Z;
			value.color[3] = color.W;
			vk.vkCmdClearColorImage( Handle, record.Image, VkImageLayout.TransferDstOptimal, &value, 1, &range );
		}
	}

	/// <summary>Fill part of a buffer with a repeated 32-bit value.</summary>
	public void FillBuffer( BufferHandle buffer, uint value, long offset = 0, long size = 0 )
	{
		OutsideRendering();
		FlushBarriers();
		vk.vkCmdFillBuffer( Handle, device.Get( buffer ).Buffer, (ulong)offset, size > 0 ? (ulong)size : Vulkan.Vk.WholeSize, value );
	}

	/// <inheritdoc/>
	public override string ToString() => Name ?? $"{Queue} command list";
}

/// <summary>
/// A rendering scope's targets, kept to pair a scope a list left suspended with the one resuming it in the next list, and to
/// begin and end either alone when they don't pair.
/// </summary>
internal sealed unsafe class RenderingTargets
{
	/// <summary>Eight colour targets and depth.</summary>
	public const int Max = 9;

	readonly VkRenderingAttachmentInfo[] attachments = new VkRenderingAttachmentInfo[Max];
	int colors;
	bool depth, stencil;
	uint width, height;

	public void Set( VkRenderingAttachmentInfo* colorInfos, int colorCount, VkRenderingAttachmentInfo* depthInfo, bool hasStencil, uint width, uint height )
	{
		for ( int i = 0; i < colorCount; i++ ) attachments[i] = colorInfos[i];
		if ( depthInfo is not null ) attachments[colorCount] = *depthInfo;
		colors = colorCount;
		depth = depthInfo is not null;
		stencil = hasStencil;
		this.width = width;
		this.height = height;
	}

	/// <summary>Whether a scope on <paramref name="other"/> draws to the same views, in the same layouts, resolves and size.</summary>
	public bool Matches( RenderingTargets other )
	{
		if ( colors != other.colors || depth != other.depth || stencil != other.stencil || width != other.width || height != other.height ) return false;
		for ( int i = 0; i < colors + (depth ? 1 : 0); i++ )
		{
			ref readonly var a = ref attachments[i];
			ref readonly var b = ref other.attachments[i];
			if ( a.imageView != b.imageView || a.imageLayout != b.imageLayout || a.resolveMode != b.resolveMode || a.resolveImageView != b.resolveImageView ) return false;
		}

		return true;
	}

	/// <summary>
	/// The scope's rendering info with <paramref name="flags"/>, its attachments copied into <paramref name="into"/>
	/// (<see cref="Max"/> long). Every target loads what it holds: a scope begun alone to be resumed keeps it.
	/// </summary>
	public VkRenderingInfo Info( VkRenderingAttachmentInfo* into, uint flags )
	{
		var count = colors + (depth ? 1 : 0);
		for ( int i = 0; i < count; i++ )
		{
			into[i] = attachments[i];
			into[i].loadOp = Vulkan.Vk.LoadOpLoad;
		}

		return new VkRenderingInfo
		{
			sType = VkStructureType.RenderingInfo,
			flags = flags,
			renderArea = new VkRect2D { extent = new VkExtent2D { width = width, height = height } },
			layerCount = 1,
			colorAttachmentCount = (uint)colors,
			pColorAttachments = into,
			pDepthAttachment = depth ? into + colors : null,
			pStencilAttachment = stencil ? into + colors : null,
		};
	}
}
