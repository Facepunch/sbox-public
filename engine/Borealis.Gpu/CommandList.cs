using Borealis.Gpu.Vulkan;
using System.Diagnostics;
using System.Threading;
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

	/// <summary>
	/// Upload page sizes: a list starts with the smallest and takes the next size up each time it fills one, so the many
	/// lists that upload a little don't each hold 4 MiB (Construct's frames took about 50 lists, 200 MiB of pages).
	/// </summary>
	internal static readonly long[] PageSizes = [256 << 10, 1 << 20, PageSize];

	readonly Device device;
	readonly VkDeviceApi vk;
	readonly ulong pool;

	/// <summary>The queue it records for.</summary>
	public QueueType Queue { get; }

	/// <summary>Its name, for debuggers.</summary>
	public string Name { get; private set; }

	internal readonly nint Handle;
	internal SyncPoint Submitted;

	/// <summary>The frame (<see cref="Device.FrameNumber"/>) it began in.</summary>
	internal ulong Frame;

	/// <summary>Its progress marker slot (<see cref="DeviceOptions.ProgressMarkers"/>), -1 without.</summary>
	internal int MarkerSlot = -1;

	// Its part of a GPU counter sample (Device.Counters), while one records: GPA's list, the sample's pass and session, and
	// whether a scope is open in it
	internal nint CounterList;
	internal int CounterPass;
	internal object CounterSession;
	internal bool CounterSampleOpen;

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

	/// <summary>Lists submitted before it since the device was made, plus one, once submitted: its place in submission order.</summary>
	public long SubmitOrder { get; internal set; }

	/// <summary>What its barrier calls held since it began, for profiling (<see cref="BarrierCounts"/>).</summary>
	public BarrierCounts BarrierDetail => barrierDetail;

	BarrierCounts barrierDetail;

	// Whether a command that does work (a scope, dispatch, copy or clear) has been recorded since the last barrier call: one
	// after none is at the same gap as the call before it
	bool workSinceBarrier;

	bool recording;

	/// <summary>Whether it has begun and has not been submitted or discarded.</summary>
	public bool IsRecording => recording;

	/// <summary>
	/// The frame (<see cref="Device.FrameNumber"/>) it began in. A list its frame's slot recycled unsubmitted goes back to the
	/// device's spares, and may begin again in a later frame for someone else.
	/// </summary>
	public ulong BeganInFrame => Frame;

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
	readonly PipelineSetLayouts[] layoutSets = new PipelineSetLayouts[2];
	readonly int[] dirty = new int[2];
	// Last pipeline bound: draws in a row often share one
	readonly ulong[] boundPipelines = new ulong[2];

	// Last dynamic state and buffers set; forgotten on begin and after native commands run, which leave unknown state
	VkViewport lastViewport;
	VkRect2D lastScissor;
	(float, float, float) lastDepthBias;
	bool viewportSet, scissorSet, depthBiasSet;
	const int MaxCachedVertexSlots = 8;
	readonly ulong[] boundVertexBuffers = new ulong[MaxCachedVertexSlots];
	readonly ulong[] boundVertexOffsets = new ulong[MaxCachedVertexSlots];
	ulong boundIndexBuffer, boundIndexOffset;
	int boundIndexFormat;

	void ForgetDynamicState()
	{
		viewportSet = scissorSet = depthBiasSet = false;
		Array.Clear( boundVertexBuffers );
		boundIndexBuffer = 0;
	}
	int lastBindPoint;

	// Push constants are shared across stages by this API. Keep the written words when graphics and compute use
	// different layout flags, so work recorded for either bind point can use its own layout even after a partial push.
	const int PushConstantWordCount = ShaderAbi.PushConstantSize / sizeof( uint );
	[InlineArray( PushConstantWordCount )]
	struct PushConstantData
	{
		uint word;
	}
	PushConstantData pushConstantData;
	uint pushConstantWords, graphicsPushConstantWords;
	readonly ulong[] sets = new ulong[4];
	readonly ulong[] setLayouts = new ulong[4];
	const int MaxDynamicOffsets = 16;
	readonly uint[] setOffsets = new uint[4 * MaxDynamicOffsets];
	readonly int[] setOffsetCounts = new int[4];

	// Each slot's last transient set written, and what it holds: the same bindings again reuse it rather than write another
	readonly ulong[] writtenSets = new ulong[4];
	readonly ulong[] writtenLayouts = new ulong[4];
	readonly Binding[][] writtenBindings = [[], [], [], []];
	readonly int[] writtenCounts = new int[4];

	// Every transient set written in this list, found by a hash of its layout and bindings: the same bindings again, in any
	// slot and after others, bind it rather than write another
	readonly Dictionary<int, int> writtenByContent = new();
	(ulong Set, ulong Layout, int Start, int Count)[] writtenList = new (ulong, ulong, int, int)[64];
	Binding[] writtenStore = new Binding[512];
	int writtenListCount, writtenStoreUsed;

	/// <summary>Transient sets written since the list began, for tests.</summary>
	internal int TransientSetsWritten { get; private set; }

	// The bound graphics pipeline isn't made yet: draws are skipped
	bool skippingDraws;

	/// <summary>Whether draws are being skipped: the bound pipeline is one workers are still making.</summary>
	public bool SkippingDraws => skippingDraws;
	int skippedDraws;

	// The dynamic render state last set (RenderStateBits), and whether any has been since the list began
	ulong renderState;
	bool renderStateSet;

	// The view set (ShaderAbi.ViewSet), bound beside the bindless set
	ulong viewSet;

	/// <summary>The view set the draws that follow bind (<see cref="SetViewSet"/>); 0 for the device's placeholders.</summary>
	internal ulong BoundViewSet => viewSet;

	// Transient descriptor sets and upload memory, recycled with the frame
	readonly List<ulong> descriptorPools = new();
	int descriptorPool;
	bool descriptorPoolHasSets;
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

	internal void Begin( string name, bool gate = true )
	{
		Name = name;
		gated = gate && device.EnterGate();
		LayoutChangeCount = 0;
		ClearPendingBarriers();
		var info = new VkCommandBufferBeginInfo { sType = VkStructureType.CommandBufferBeginInfo, flags = Vulkan.Vk.CommandBufferOneTimeSubmit };
		vk.vkBeginCommandBuffer( Handle, &info ).Check( "vkBeginCommandBuffer" );

		recording = true;
		rendering = false;
		layouts[0] = layouts[1] = 0;
		boundPipelines[0] = boundPipelines[1] = 0;
		ForgetDynamicState();
		lastBindPoint = -1;
		pushConstantWords = graphicsPushConstantWords = 0;
		dirty[0] = dirty[1] = 0;
		Array.Clear( sets );
		Array.Clear( setLayouts );
		Array.Clear( setOffsetCounts );
		viewSet = 0;
		renderStateSet = false;
		skippingDraws = false;
		skippedDraws = 0;
		Submitted = default;
		draws = pipelines = setBinds = scopes = barriers = fullBarriers = resumed = 0;
		barrierDetail = default;
		workSinceBarrier = true;
		acted = suspending = suspended = false;
		StartsResumed = EndsSuspended = false;

		MarkerSlot = device.ProgressMarkers ? device.TakeMarkerSlot( name ) : -1;
		passMarkers = MarkerSlot >= 0 && device.PassMarkers;
		openLabels.Clear();
		Marker( "begin" );
		labelled = name is not null && device.Instance.vkCmdBeginDebugUtilsLabelEXT != null;
		if ( labelled ) BeginLabel( name );
		device.Counters?.ListBegan( this );
	}

	internal void End()
	{
		if ( !recording ) throw new InvalidOperationException( "This command list was already submitted or discarded" );
		try
		{
			if ( rendering ) throw new InvalidOperationException( "Rendering wasn't ended" );

			FlushBarriers( work: false );
			device.Counters?.ListEnded( this );
			if ( labelled ) EndLabel();
			Marker( "end" );
			EndsSuspended = suspended;
			vk.vkEndCommandBuffer( Handle ).Check( "vkEndCommandBuffer" );
			recording = false;
		}
		catch
		{
			Discard();
			throw;
		}
	}

	/// <summary>
	/// Give up this recording without submitting it. Call only while you own the list, after all recording has stopped
	/// and before passing it to <see cref="Device.Submit"/> or <see cref="Device.SubmitLater"/>. Its frame recycles it.
	/// </summary>
	public void Discard()
	{
		lock ( device.QueueFor( Queue ).SubmitLock )
		{
			if ( !recording ) return;
			device.Counters?.ListEnded( this );
			if ( rendering ) vk.vkCmdEndRendering( Handle );
			vk.vkEndCommandBuffer( Handle );
			rendering = recording = false;
			ClearPendingBarriers();
			LeaveGate();
		}
	}

	// Its texture barriers in the order recorded, which the device applies to the textures' layouts when it's submitted
	TextureBarrier[] layoutChanges = new TextureBarrier[16];

	/// <summary>Texture barriers recorded (<see cref="LayoutChanges"/>).</summary>
	internal int LayoutChangeCount { get; private set; }

	/// <summary>The texture barriers recorded so far, in order.</summary>
	internal ReadOnlySpan<TextureBarrier> LayoutChanges => layoutChanges.AsSpan( 0, LayoutChangeCount );

	// Whether it holds the device's gate (Device.EnterGate) from its beginning until it's submitted: nothing moves under a
	// list that may name what would move
	bool gated;

	/// <summary>Submitted (or given up): let the device move what it names again.</summary>
	internal void LeaveGate()
	{
		if ( !gated ) return;
		gated = false;
		device.ExitGate();
	}

	/// <summary>Its frame has finished: reset it for reuse.</summary>
	internal void Recycle()
	{
		if ( recording ) throw new InvalidOperationException( "A recording command list can't be recycled" );
		LeaveGate();

		vk.vkResetCommandPool( vk.Device, pool, 0 ).Check( "vkResetCommandPool" );

		// One pool stays with the list (most lists use one); the rest go back to the device's spares for any list
		foreach ( var p in descriptorPools ) vk.vkResetDescriptorPool( vk.Device, p, 0 );
		for ( int i = descriptorPools.Count - 1; i >= 1; i-- )
		{
			device.ReturnDescriptorPool( descriptorPools[i] );
			descriptorPools.RemoveAt( i );
		}

		descriptorPool = 0;
		descriptorPoolHasSets = false;
		writtenSets.AsSpan().Clear();
		writtenByContent.Clear();
		writtenListCount = writtenStoreUsed = 0;
		TransientSetsWritten = 0;

		foreach ( var p in pages )
		{
			if ( p.Class >= 0 )
			{
				p.Used = 0;
				device.FreePages[p.Class].Add( p );
			}
			else
			{
				device.DestroyPage( p );
			}
		}

		pages.Clear();
		page = null;
	}

	internal void Destroy()
	{
		using var _ = device.Enter( "destroy command list" );
		LeaveGate();
		foreach ( var p in descriptorPools ) vk.vkDestroyDescriptorPool( vk.Device, p, null );
		descriptorPools.Clear();
		foreach ( var p in pages ) device.Destroy( p.Buffer );
		pages.Clear();
		vk.vkDestroyCommandPool( vk.Device, pool, null );
	}

	/// <summary>
	/// Give the driver back the command memory its pool kept from its biggest recording; for a recycled spare only. The
	/// next recording grows it again.
	/// </summary>
	internal void ReleaseCommandMemory()
	{
		using ( device.Enter( "release command memory" ) )
			vk.vkResetCommandPool( vk.Device, pool, Vulkan.Vk.CommandPoolResetReleaseResources ).Check( "vkResetCommandPool" );
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
		// With no suspended scope to resume, this helper starts a new pass that loads the existing contents.
		// Order it after earlier uses, even if the recorded resume had no barrier (else it couldn't resume).
		if ( flags == Vulkan.Vk.RenderingSuspending )
		{
			Barrier( PipelineStage.AllCommands, MemoryAccess.MemoryWrite,
				PipelineStage.ColorTargetOutput | PipelineStage.EarlyFragmentTests | PipelineStage.LateFragmentTests,
				MemoryAccess.ColorTargetRead | MemoryAccess.ColorTargetWrite | MemoryAccess.DepthTargetRead | MemoryAccess.DepthTargetWrite );
			FlushBarriers();
		}

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
		FlushBarriers( work: false );
		vk.vkCmdResetQueryPool( Handle, queries.Pool, (uint)first, (uint)count );
	}

	/// <summary>Write timestamp <paramref name="index"/> once the work before it has passed <paramref name="stage"/>.</summary>
	public void WriteTimestamp( TimestampQueries queries, int index, PipelineStage stage = PipelineStage.AllCommands )
	{
		Recording();
		if ( !rendering ) Act();
		FlushBarriers( work: false );

		// A graphics stage on the async compute queue: when everything before it has finished
		if ( Queue == QueueType.Compute && ((ulong)stage & ComputeQueueStages) == 0 ) stage = PipelineStage.AllCommands;
		vk.vkCmdWriteTimestamp2( Handle, (ulong)stage, queries.Pool, (uint)index );
	}

	//
	// Occlusion queries
	//

	/// <summary>Reset occlusion queries before counting into them again (outside rendering).</summary>
	public void ResetQueries( OcclusionQueries queries, int first, int count )
	{
		OutsideRendering();
		FlushBarriers( work: false );
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
		FlushBarriers( work: false );
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
		FlushBarriers( work: false );
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

	// A bounded per-thread cache: frame and pass labels normally reuse the same strings.
	const int LabelCacheSlots = 32;
	[ThreadStatic] static LabelCache labelCache;

	[InlineArray( LabelCacheSlots )]
	struct LabelCache
	{
		LabelText first;
	}

	struct LabelText
	{
		public string Name;
		public LabelBytes Utf8;
	}

	[InlineArray( 256 )]
	struct LabelBytes
	{
		byte first;
	}

	// Whether its labels write progress markers too (Device.PassMarkers), not just its beginning and end
	bool passMarkers;

	/// <summary>Start a named region, which RenderDoc and validation messages show.</summary>
	public void BeginLabel( string name )
	{
		if ( passMarkers )
		{
			openLabels.Push( name );
			Marker( name );
		}

		if ( device.Instance.vkCmdBeginDebugUtilsLabelEXT == null ) return;

		var chars = name.Length;
		ref var cached = ref labelCache[RuntimeHelpers.GetHashCode( name ) & (LabelCacheSlots - 1)];
		Span<byte> utf8 = cached.Utf8;
		if ( !ReferenceEquals( cached.Name, name ) )
		{
			var length = Encoding.UTF8.GetBytes( name.AsSpan( 0, Math.Min( chars, 80 ) ), utf8 );
			utf8[length] = 0;
			cached.Name = chars <= 80 ? name : null;
		}

		fixed ( byte* p = utf8 )
		{
			var label = new VkDebugUtilsLabelEXT { sType = VkStructureType.DebugUtilsLabelExt, pLabelName = p };
			device.Instance.vkCmdBeginDebugUtilsLabelEXT( Handle, &label );
		}
	}

	// Each label's end marker name, made once per label string (labels are mostly constants), so ending one allocates nothing
	static readonly System.Runtime.CompilerServices.ConditionalWeakTable<string, string> EndMarkers = new();

	/// <summary>End the region <see cref="BeginLabel"/> started.</summary>
	public void EndLabel()
	{
		if ( passMarkers ) Marker( openLabels.TryPop( out var label ) ? EndMarkers.GetValue( label, static l => $"end {l}" ) : "end" );
		if ( device.Instance.vkCmdEndDebugUtilsLabelEXT == null ) return;
		device.Instance.vkCmdEndDebugUtilsLabelEXT( Handle );
	}

	//
	// Recording outside Borealis.Gpu
	//

	/// <summary>
	/// The list's Vulkan command buffer, for a library that records into it itself (a vendor upscaler, with
	/// <see cref="Device.Native"/>): the barriers asked for so far are recorded first, and no rendering scope may be open.
	/// Call <see cref="EndNative"/> after, so the pipelines and sets it bound aren't taken for the list's own.
	/// </summary>
	public nint BeginNative()
	{
		OutsideRendering();
		FlushBarriers();
		return Handle;
	}

	/// <summary>
	/// After <see cref="BeginNative"/>: forget what was bound, so the next draw or dispatch binds its pipeline, every set and
	/// its render state again.
	/// </summary>
	public void EndNative()
	{
		Recording();
		layouts[0] = layouts[1] = 0;
		boundPipelines[0] = boundPipelines[1] = 0;
		ForgetDynamicState();
		dirty[0] = dirty[1] = 0x3F;
		lastBindPoint = -1;
		pushConstantWords = graphicsPushConstantWords = 0;
		renderStateSet = false;
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

		// Where it leaves the texture, for the device to follow once the list is submitted (Device.ApplyLayouts)
		if ( !record.External )
		{
			if ( LayoutChangeCount == layoutChanges.Length ) Array.Resize( ref layoutChanges, layoutChanges.Length * 2 );
			layoutChanges[LayoutChangeCount++] = barrier;
		}

		var mips = barrier.MipCount > 0 ? (uint)barrier.MipCount : Vulkan.Vk.RemainingMipLevels;
		var layers = barrier.LayerCount > 0 ? (uint)barrier.LayerCount : Vulkan.Vk.RemainingArrayLayers;
		var baseMip = (uint)barrier.BaseMip;
		var baseLayer = (uint)barrier.BaseLayer;

		// Disjoint subresources of one image can transition together. Overlapping transitions must be ordered, except
		// when a second move continues the first (starts in the layout it ends in): exactly the same subresources fold into
		// the first, and overlapping mips of the same layers fold where they overlap (FoldAlongMips).
		for ( int i = 0; i < imageBarrierCount; i++ )
		{
			ref var pending = ref imageBarriers[i];
			if ( pending.image != record.Image ) continue;

			var range = pending.subresourceRange;
			if ( !Overlaps( baseMip, mips, range.baseMipLevel, range.levelCount ) ||
				!Overlaps( baseLayer, layers, range.baseArrayLayer, range.layerCount ) ) continue;

			if ( range.baseMipLevel == baseMip && range.levelCount == mips && range.baseArrayLayer == baseLayer && range.layerCount == layers && range.aspectMask == record.Aspect && pending.newLayout == barrier.OldLayout.ToVk() )
			{
				pending.newLayout = barrier.NewLayout.ToVk();
				pending.srcStageMask |= (ulong)barrier.SourceStage;
				pending.srcAccessMask |= (ulong)barrier.SourceAccess;
				pending.dstStageMask |= (ulong)barrier.DestinationStage;
				pending.dstAccessMask |= (ulong)barrier.DestinationAccess;
				return;
			}

			if ( FoldAlongMips( i, barrier, record.Image, record.Aspect, (uint)Math.Max( record.Info.Mips, 1 ), (uint)Math.Max( record.ArrayLayers, 1 ), baseMip, mips, baseLayer, layers ) ) return;

			barrierDetail.SameImageSplits++;
			FlushBarriers( work: false );
			break;
		}

		AddImageBarrier( new VkImageMemoryBarrier2
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
			subresourceRange = new VkImageSubresourceRange { aspectMask = record.Aspect, baseMipLevel = baseMip, levelCount = mips, baseArrayLayer = baseLayer, layerCount = layers },
		} );
	}

	/// <summary>
	/// Fold <paramref name="barrier"/> into pending move <paramref name="index"/> when it continues that move over the same
	/// layers and overlapping mips (a mip chain reading one mip of a texture it has just moved whole, or moving the whole
	/// texture again after reading its last mip). With nothing recorded between them the two moves are one: the mips both
	/// cover move once, from where the pending move started to where this one ends, and the mips only one of them covers
	/// keep that move, all in the same call, rather than a call each. False, changing nothing, when they don't line up so or
	/// another pending move of the image overlaps this one.
	/// </summary>
	bool FoldAlongMips( int index, in TextureBarrier barrier, ulong image, uint aspect, uint totalMips, uint totalLayers, uint baseMip, uint mips, uint baseLayer, uint layers )
	{
		var pending = imageBarriers[index];
		var range = pending.subresourceRange;
		if ( pending.newLayout != barrier.OldLayout.ToVk() || range.aspectMask != aspect ) return false;

		// VK_REMAINING_* counts made explicit, to compare the layers and cut the mips
		var pendingLayers = range.layerCount == Vulkan.Vk.RemainingArrayLayers ? totalLayers - range.baseArrayLayer : range.layerCount;
		var movedLayers = layers == Vulkan.Vk.RemainingArrayLayers ? totalLayers - baseLayer : layers;
		if ( range.baseArrayLayer != baseLayer || pendingLayers != movedLayers || range.baseMipLevel >= totalMips || baseMip >= totalMips ) return false;
		var pendingEnd = range.levelCount == Vulkan.Vk.RemainingMipLevels ? totalMips : range.baseMipLevel + range.levelCount;
		var movedEnd = mips == Vulkan.Vk.RemainingMipLevels ? totalMips : baseMip + mips;

		// Pending moves before this one don't overlap the barrier (Barrier stops at the first that does); later ones mustn't
		for ( int j = index + 1; j < imageBarrierCount; j++ )
		{
			var other = imageBarriers[j].subresourceRange;
			if ( imageBarriers[j].image == image && Overlaps( baseMip, mips, other.baseMipLevel, other.levelCount ) && Overlaps( baseLayer, layers, other.baseArrayLayer, other.layerCount ) ) return false;
		}

		var moved = pending;
		moved.srcStageMask = (ulong)barrier.SourceStage;
		moved.srcAccessMask = (ulong)barrier.SourceAccess;
		moved.dstStageMask = (ulong)barrier.DestinationStage;
		moved.dstAccessMask = (ulong)barrier.DestinationAccess;
		moved.oldLayout = pending.newLayout;
		moved.newLayout = barrier.NewLayout.ToVk();

		// Where both move: from where the pending move starts to where this one ends, after what either waited for
		var start = Math.Max( range.baseMipLevel, baseMip );
		var end = Math.Min( pendingEnd, movedEnd );
		var both = moved;
		both.oldLayout = pending.oldLayout;
		both.srcStageMask |= pending.srcStageMask;
		both.srcAccessMask |= pending.srcAccessMask;
		both.dstStageMask |= pending.dstStageMask;
		both.dstAccessMask |= pending.dstAccessMask;
		imageBarriers[index] = WithMips( both, start, end );

		// Below and above that only one of them moves each mip, as it would have alone
		if ( range.baseMipLevel < start ) AddImageBarrier( WithMips( pending, range.baseMipLevel, start ) );
		else if ( baseMip < start ) AddImageBarrier( WithMips( moved, baseMip, start ) );
		if ( pendingEnd > end ) AddImageBarrier( WithMips( pending, end, pendingEnd ) );
		else if ( movedEnd > end ) AddImageBarrier( WithMips( moved, end, movedEnd ) );
		return true;
	}

	static VkImageMemoryBarrier2 WithMips( VkImageMemoryBarrier2 barrier, uint start, uint end )
	{
		barrier.subresourceRange.baseMipLevel = start;
		barrier.subresourceRange.levelCount = end - start;
		return barrier;
	}

	void AddImageBarrier( in VkImageMemoryBarrier2 barrier )
	{
		if ( imageBarrierCount == imageBarriers.Length ) Array.Resize( ref imageBarriers, imageBarriers.Length * 2 );
		imageBarriers[imageBarrierCount++] = barrier;
	}

	// Compare against the distance between starts so VK_REMAINING_* counts cannot overflow.
	static bool Overlaps( uint start, uint count, uint otherStart, uint otherCount )
		=> start <= otherStart ? otherStart - start < count : start - otherStart < otherCount;

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

	// What a compute-only queue's barriers may name: its stages, and the accesses those stages make
	const ulong ComputeQueueStages = (ulong)(PipelineStage.TopOfPipe | PipelineStage.DrawIndirect | PipelineStage.ComputeShader | PipelineStage.Transfer
		| PipelineStage.BottomOfPipe | PipelineStage.Host | PipelineStage.AllCommands);
	const ulong ComputeQueueAccess = (ulong)(MemoryAccess.IndirectRead | MemoryAccess.UniformRead | MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite
		| MemoryAccess.TransferRead | MemoryAccess.TransferWrite | MemoryAccess.HostRead | MemoryAccess.HostWrite | MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite);

	/// <summary>
	/// On the async compute queue, a barrier keeps only the stages and accesses that queue has: the graphics stages a use
	/// names (a texture sampled by pixel and compute shaders) have no work on it to wait for or block, and the queue that does
	/// have them is ordered by the semaphores between the two.
	/// </summary>
	static void ForComputeQueue( ref ulong stages, ref ulong access )
	{
		stages &= ComputeQueueStages;
		access = stages == 0 ? 0 : access & ComputeQueueAccess;
	}

	// Vulkan's write accesses (VK_ACCESS_2_*_WRITE_BIT): shader, colour and depth targets, transfer, host, memory, storage
	const ulong WriteAccess = (1ul << 6) | (1ul << 8) | (1ul << 10) | (1ul << 12) | (1ul << 14) | (1ul << 16) | (1ul << 35);

	void ClearPendingBarriers()
	{
		imageBarrierCount = bufferBarrierCount = 0;
		hasMemoryBarrier = false;
	}

	/// <summary>
	/// Record the barriers asked for so far as one call. <paramref name="work"/>: whether the command it's flushed for does
	/// work (a scope, dispatch, copy or clear), rather than writing a timestamp, binding a pipeline or ending the list.
	/// </summary>
	void FlushBarriers( bool work = true )
	{
		if ( imageBarrierCount == 0 && bufferBarrierCount == 0 && !hasMemoryBarrier )
		{
			workSinceBarrier |= work;
			return;
		}

		if ( Queue == QueueType.Compute )
		{
			for ( int i = 0; i < imageBarrierCount; i++ )
			{
				ref var b = ref imageBarriers[i];
				ForComputeQueue( ref b.srcStageMask, ref b.srcAccessMask );
				ForComputeQueue( ref b.dstStageMask, ref b.dstAccessMask );
			}

			for ( int i = 0; i < bufferBarrierCount; i++ )
			{
				ref var b = ref bufferBarriers[i];
				ForComputeQueue( ref b.srcStageMask, ref b.srcAccessMask );
				ForComputeQueue( ref b.dstStageMask, ref b.dstAccessMask );
			}

			if ( hasMemoryBarrier )
			{
				ForComputeQueue( ref memoryBarrier.srcStageMask, ref memoryBarrier.srcAccessMask );
				ForComputeQueue( ref memoryBarrier.dstStageMask, ref memoryBarrier.dstAccessMask );
				hasMemoryBarrier = memoryBarrier.srcStageMask != 0 || memoryBarrier.dstStageMask != 0;
				if ( imageBarrierCount == 0 && bufferBarrierCount == 0 && !hasMemoryBarrier )
				{
					workSinceBarrier |= work;
					return;
				}
			}
		}

		var timed = device.TimesBarriers ? Stopwatch.GetTimestamp() : 0;
		CountBarriers();

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

		ClearPendingBarriers();

		if ( !workSinceBarrier ) barrierDetail.SameGap++;
		workSinceBarrier = work;
		if ( timed != 0 ) device.AddBarrierTicks( Stopwatch.GetTimestamp() - timed );
	}

	/// <summary>
	/// Count the barriers about to be recorded by what they order (<see cref="BarrierCounts"/>): from a write, from nothing
	/// (a texture's first use), or from reads, to reads or to a write.
	/// </summary>
	void CountBarriers()
	{
		barrierDetail.Calls++;
		if ( hasMemoryBarrier ) barrierDetail.Memory++;

		for ( int i = 0; i < imageBarrierCount; i++ )
		{
			ref var b = ref imageBarriers[i];
			barrierDetail.Images++;
			if ( b.oldLayout == VkImageLayout.Undefined ) barrierDetail.ImagesFromUndefined++;
			else if ( (b.srcAccessMask & WriteAccess) != 0 ) barrierDetail.ImagesAfterWrite++;
			else if ( (b.dstAccessMask & WriteAccess) != 0 ) barrierDetail.ImagesReadToWrite++;
			else if ( b.oldLayout == b.newLayout ) barrierDetail.ImagesReadToReadSameLayout++;
			else barrierDetail.ImagesReadToRead++;
		}

		for ( int i = 0; i < bufferBarrierCount; i++ )
		{
			ref var b = ref bufferBarriers[i];
			barrierDetail.Buffers++;
			if ( (b.srcAccessMask & WriteAccess) != 0 ) barrierDetail.BuffersAfterWrite++;
			else if ( (b.dstAccessMask & WriteAccess) == 0 ) barrierDetail.BuffersReadToRead++;
		}
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
	/// <see cref="RenderingFlags.Resume"/> continues the scope the list submitted before left suspended if the targets
	/// and their store operations match. Its load operations don't apply. Neither has to be matched: a scope suspended
	/// with no matching resume after it is ended there, and an unmatched resume loads the existing contents. A resume
	/// that's not the list's first command begins an ordinary scope. Lists submitted apart
	/// (<see cref="Device.Submit"/> and <see cref="Device.SubmitLater"/> both pair them) end and begin theirs on their own.
	/// </remarks>
	public void BeginRendering( ReadOnlySpan<ColorTarget> colors, in DepthTarget depth = default, RenderingFlags flags = RenderingFlags.None )
		=> BeginRenderingCore( colors, depth, flags, 0, 0 );

	/// <summary>
	/// Begin rasterization without color or depth attachments, for draws whose only output is an occlusion query.
	/// The render area, viewport and scissor are <paramref name="width"/> by <paramref name="height"/>.
	/// </summary>
	public void BeginRenderingWithoutAttachments( int width, int height )
		=> BeginRenderingCore( [], default, RenderingFlags.None, checked((uint)width), checked((uint)height) );

	void BeginRenderingCore( ReadOnlySpan<ColorTarget> colors, in DepthTarget depth, RenderingFlags flags, uint width, uint height )
	{
		Recording();
		if ( rendering ) throw new InvalidOperationException( "BeginRendering isn't allowed while rendering: end rendering first" );

		// Only the list's first command resumes the list before's scope; a scope suspended in this list is ended first
		var resume = (flags & RenderingFlags.Resume) != 0 && !acted;
		if ( suspended ) EndSuspended();
		var suspend = (flags & RenderingFlags.Suspend) != 0;
		var carry = resume || suspend;

		if ( colors.Length > 8 ) throw new ArgumentException( "At most 8 colour targets" );

		var attachments = stackalloc VkRenderingAttachmentInfo[Math.Max( colors.Length, 1 )];
		var clears = stackalloc VkClearAttachment[suspend && !resume ? colors.Length + 1 : 0];
		var clearCount = 0;

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
				loadOp = (int)(carry ? LoadOp.Load : color.Load),
				storeOp = (int)color.Store,
			};

			// Vulkan matches load ops and clear values even on independently recorded segments. Use LOAD
			// and a zero clear value for every carried segment; only the first can clear, with a command.
			if ( suspend && !resume && color.Load != LoadOp.Load )
			{
				PrepareCarryLoad( color.Texture, record.Image, color.Mip, color.Layer, ImageLayout.ColorTarget, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetRead );
				if ( color.Load == LoadOp.Clear )
					clears[clearCount++] = new() { aspectMask = Vulkan.Vk.AspectColor, colorAttachment = (uint)i, clearValue = Unsafe.BitCast<System.Numerics.Vector4, VkClearValue>( color.ClearColor ) };
			}
			else if ( !carry && color.Load == LoadOp.Clear )
			{
				attachments[i].clearValue = Unsafe.BitCast<System.Numerics.Vector4, VkClearValue>( color.ClearColor );
			}

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
				loadOp = (int)(carry ? LoadOp.Load : depth.Load),
				storeOp = (int)depth.Store,
			};

			if ( suspend && !resume && depth.Load != LoadOp.Load )
			{
				PrepareCarryLoad( depth.Texture, record.Image, depth.Mip, depth.Layer, depth.ReadOnly ? ImageLayout.DepthReadOnly : ImageLayout.DepthTarget,
					PipelineStage.EarlyFragmentTests | PipelineStage.LateFragmentTests, MemoryAccess.DepthTargetRead );
				if ( depth.Load == LoadOp.Clear )
					clears[clearCount++] = new() { aspectMask = record.Aspect, clearValue = new() { depth = depth.ClearDepth, stencil = depth.ClearStencil } };
			}
			else if ( !carry && depth.Load == LoadOp.Clear )
			{
				depthAttachment.clearValue = new() { depth = depth.ClearDepth, stencil = depth.ClearStencil };
			}

			// Sample zero is the one mode every device resolves depth and stencil with
			if ( depth.ResolveTarget.IsValid )
			{
				depthAttachment.resolveMode = Vulkan.Vk.ResolveModeSampleZero;
				depthAttachment.resolveImageView = device.AttachmentView( depth.ResolveTarget, 0, 0, false );
				depthAttachment.resolveImageLayout = VkImageLayout.DepthStencilAttachmentOptimal;
			}
		}

		if ( width == 0 || height == 0 ) throw new ArgumentException( "Rendering needs an attachment or an explicit render area" );

		FlushBarriers();
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
		if ( clearCount != 0 )
		{
			var rect = new VkClearRect { rect = info.renderArea, layerCount = info.layerCount };
			vk.vkCmdClearAttachments( Handle, (uint)clearCount, clears, 1, &rect );
		}
		scopes++;
		acted = true;
		suspending = suspend;
		rendering = true;
		RenderSize = (int.CreateTruncating( width ), int.CreateTruncating( height ));

		SetViewport( 0, 0, width, height );
		SetScissor( 0, 0, (int)width, (int)height );
	}

	/// <summary>A carried scope loading before a clear or discard also needs that load to see preceding writes.</summary>
	void PrepareCarryLoad( TextureHandle texture, ulong image, int mip, int layer, ImageLayout layout, PipelineStage stage, MemoryAccess read )
	{
		var vkLayout = layout.ToVk();
		for ( int i = 0; i < imageBarrierCount; i++ )
		{
			ref var pending = ref imageBarriers[i];
			if ( pending.image != image || pending.newLayout != vkLayout ) continue;
			var range = pending.subresourceRange;
			if ( (uint)mip < range.baseMipLevel || (uint)mip - range.baseMipLevel >= range.levelCount ||
				(uint)layer < range.baseArrayLayer || (uint)layer - range.baseArrayLayer >= range.layerCount ) continue;
			pending.dstStageMask |= (ulong)stage;
			pending.dstAccessMask |= (ulong)read;
			return;
		}

		var barrier = TextureBarrier.Transition( texture, layout, layout, PipelineStage.AllCommands, MemoryAccess.MemoryWrite, stage, read );
		Barrier( barrier with { BaseMip = mip, MipCount = 1, BaseLayer = layer, LayerCount = 1 } );
	}

	/// <summary>The size of what's being rendered to, while rendering.</summary>
	public (int Width, int Height) RenderSize { get; private set; }

	/// <summary>Whether a rendering scope is open.</summary>
	public bool IsRendering => rendering;

	static bool IsIntegerFormat( Format format ) => format.IsInteger();

	/// <summary>Clear depth to zero in a render-area rectangle of the current depth attachment, without clearing stencil.</summary>
	public void ClearDepthRegion( int x, int y, int width, int height )
	{
		Recording();
		if ( !rendering ) throw new InvalidOperationException( "Not rendering" );
		var depth = new VkClearAttachment { aspectMask = Vulkan.Vk.AspectDepth };
		var rect = new VkClearRect
		{
			rect = new VkRect2D { offset = new VkOffset2D { x = x, y = y }, extent = new VkExtent2D { width = (uint)width, height = (uint)height } },
			layerCount = 1,
		};
		vk.vkCmdClearAttachments( Handle, 1, &depth, 1, &rect );
	}

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
		if ( viewportSet && lastViewport.x == x && lastViewport.y == y && lastViewport.width == width && lastViewport.height == height && lastViewport.minDepth == minDepth && lastViewport.maxDepth == maxDepth ) return;

		lastViewport = new VkViewport { x = x, y = y, width = width, height = height, minDepth = minDepth, maxDepth = maxDepth };
		viewportSet = true;
		fixed ( VkViewport* viewport = &lastViewport ) vk.vkCmdSetViewport( Handle, 0, 1, viewport );
	}

	/// <summary>Set the scissor rectangle.</summary>
	public void SetScissor( int x, int y, int width, int height )
	{
		var rect = new VkRect2D { offset = new VkOffset2D { x = Math.Max( x, 0 ), y = Math.Max( y, 0 ) }, extent = new VkExtent2D { width = (uint)Math.Max( width, 0 ), height = (uint)Math.Max( height, 0 ) } };
		if ( scissorSet && lastScissor.offset.x == rect.offset.x && lastScissor.offset.y == rect.offset.y && lastScissor.extent.width == rect.extent.width && lastScissor.extent.height == rect.extent.height ) return;

		lastScissor = rect;
		scissorSet = true;
		vk.vkCmdSetScissor( Handle, 0, 1, &rect );
	}

	/// <summary>Set depth bias, for pipelines made with <see cref="GraphicsPipelineInfo.DepthBias"/>.</summary>
	public void SetDepthBias( float constant, float slope, float clamp = 0 )
	{
		if ( depthBiasSet && lastDepthBias == (constant, slope, clamp) ) return;

		lastDepthBias = (constant, slope, clamp);
		depthBiasSet = true;
		vk.vkCmdSetDepthBias( Handle, constant, clamp, slope );
	}

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
		FlushBarriers( work: false );

		// One workers are still making (GraphicsPipelineInfo.SkipUntilReady): its draws are skipped until another is bound
		var vkPipeline = Volatile.Read( ref record.Pipeline );
		lastBindPoint = record.BindPoint;
		if ( vkPipeline == 0 )
		{
			if ( record.BindPoint == Vulkan.Vk.BindPointCompute ) throw new InvalidOperationException( "Compute pipelines are always made whole" );
			skippingDraws = true;
		}
		else
		{
			if ( record.BindPoint == Vulkan.Vk.BindPointGraphics ) skippingDraws = false;
			if ( boundPipelines[record.BindPoint] != vkPipeline )
			{
				vk.vkCmdBindPipeline( Handle, record.BindPoint, vkPipeline );
				boundPipelines[record.BindPoint] = vkPipeline;
				pipelines++;
			}
			if ( record.BindPoint == Vulkan.Vk.BindPointGraphics && (!renderStateSet || record.RenderState != renderState) ) SetRenderState( record.RenderState );
		}

		if ( layouts[record.BindPoint] != record.Layout )
		{
			var bindPoint = record.BindPoint;
			var next = device.SetLayoutsOf( record.Layout );
			var previous = layoutSets[bindPoint];

			// Every full layout has the same push ranges and sets 4 and 5. For each lower set, compatibility also
			// requires matching independent-sets flags and every layout through that set. Binding a changed set
			// can disturb every higher set, even if its own layout matches.
			dirty[bindPoint] |= layouts[bindPoint] == 0 || previous.Independent != next.Independent || previous.First.Identity != next.First.Identity ? 0x3F
				: previous.Second.Identity != next.Second.Identity ? 0x3E
				: previous.Third.Identity != next.Third.Identity ? 0x3C
				: previous.Fourth.Identity != next.Fourth.Identity ? 0x38
				: 0;
			layouts[bindPoint] = record.Layout;
			layoutSets[bindPoint] = next;
		}
	}

	/// <summary>Set the dynamic render state a graphics pipeline was made with, where it differs from what was set last.</summary>
	void SetRenderState( ulong state )
	{
		var changed = renderStateSet ? state ^ renderState : ulong.MaxValue;
		renderState = state;
		renderStateSet = true;

		static int Field( ulong state, int shift, int bits ) => RenderStateBits.Field( state, shift, bits );
		if ( (changed & RenderStateBits.CullMask) != 0 ) vk.vkCmdSetCullMode( Handle, (uint)Field( state, RenderStateBits.Cull, 2 ) );
		if ( (changed & RenderStateBits.FrontFaceMask) != 0 ) vk.vkCmdSetFrontFace( Handle, Field( state, RenderStateBits.FrontFace, 1 ) );
		if ( (changed & RenderStateBits.DepthBiasMask) != 0 ) vk.vkCmdSetDepthBiasEnable( Handle, (uint)Field( state, RenderStateBits.DepthBias, 1 ) );
		if ( (changed & RenderStateBits.DepthTestMask) != 0 ) vk.vkCmdSetDepthTestEnable( Handle, (uint)Field( state, RenderStateBits.DepthTest, 1 ) );
		if ( (changed & RenderStateBits.DepthWriteMask) != 0 ) vk.vkCmdSetDepthWriteEnable( Handle, (uint)Field( state, RenderStateBits.DepthWrite, 1 ) );
		if ( (changed & RenderStateBits.DepthCompareMask) != 0 ) vk.vkCmdSetDepthCompareOp( Handle, Field( state, RenderStateBits.DepthCompare, 3 ) );
		if ( (changed & RenderStateBits.StencilTestMask) != 0 ) vk.vkCmdSetStencilTestEnable( Handle, (uint)Field( state, RenderStateBits.StencilTest, 1 ) );
		if ( (changed & RenderStateBits.ReadMaskMask) != 0 ) vk.vkCmdSetStencilCompareMask( Handle, 3, (uint)Field( state, RenderStateBits.ReadMask, 8 ) );
		if ( (changed & RenderStateBits.WriteMaskMask) != 0 ) vk.vkCmdSetStencilWriteMask( Handle, 3, (uint)Field( state, RenderStateBits.WriteMask, 8 ) );
		if ( (changed & RenderStateBits.StencilFrontMask) != 0 ) StencilOp( 1, Field( state, RenderStateBits.StencilFront, 12 ) );
		if ( (changed & RenderStateBits.StencilBackMask) != 0 ) StencilOp( 2, Field( state, RenderStateBits.StencilBack, 12 ) );
	}

	void StencilOp( uint face, int ops ) => vk.vkCmdSetStencilOp( Handle, face, ops & 7, (ops >> 3) & 7, (ops >> 6) & 7, (ops >> 9) & 7 );

	/// <summary>Bind a vertex buffer to a binding the pipeline reads.</summary>
	public void SetVertexBuffer( int slot, BufferHandle buffer, long offset = 0 )
	{
		var vkBuffer = device.Get( buffer ).Buffer;
		var vkOffset = (ulong)offset;
		if ( (uint)slot < MaxCachedVertexSlots )
		{
			if ( boundVertexBuffers[slot] == vkBuffer && boundVertexOffsets[slot] == vkOffset ) return;
			(boundVertexBuffers[slot], boundVertexOffsets[slot]) = (vkBuffer, vkOffset);
		}

		vk.vkCmdBindVertexBuffers( Handle, (uint)slot, 1, &vkBuffer, &vkOffset );
	}

	/// <summary>Bind the index buffer.</summary>
	public void SetIndexBuffer( BufferHandle buffer, IndexFormat format, long offset = 0 )
	{
		var vkBuffer = device.Get( buffer ).Buffer;
		if ( boundIndexBuffer == vkBuffer && boundIndexOffset == (ulong)offset && boundIndexFormat == (int)format ) return;

		(boundIndexBuffer, boundIndexOffset, boundIndexFormat) = (vkBuffer, (ulong)offset, (int)format);
		vk.vkCmdBindIndexBuffer( Handle, vkBuffer, (ulong)offset, (int)format );
	}

	/// <summary>
	/// Fill a transient descriptor set (0 to 3) with <paramref name="bindings"/> laid out as <paramref name="layout"/>, and
	/// bind it for the draws and dispatches that follow. The set lives until the frame is recycled.
	/// </summary>
	public void SetBindings( int set, BindingLayoutHandle layout, ReadOnlySpan<Binding> bindings )
	{
		Recording();
		if ( (uint)set >= ShaderAbi.BindlessSet ) throw new ArgumentOutOfRangeException( nameof( set ), "Transient sets are 0 to 3" );

		var setLayout = device.Get( layout ).SetLayout;
		ulong descriptorSet;
		if ( device.ReusesTransientSets && writtenSets[set] != 0 && writtenLayouts[set] == setLayout && Same( bindings, writtenBindings[set].AsSpan( 0, writtenCounts[set] ) ) )
		{
			// Bound already, as it was: nothing to do
			descriptorSet = writtenSets[set];
			if ( sets[set] == descriptorSet && setOffsetCounts[set] == 0 ) return;
		}
		else
		{
			var hash = device.ReusesTransientSets ? ContentHash( setLayout, bindings ) : 0;
			if ( device.ReusesTransientSets && writtenByContent.TryGetValue( hash, out var at ) && writtenList[at].Layout == setLayout && Same( bindings, writtenStore.AsSpan( writtenList[at].Start, writtenList[at].Count ) ) )
			{
				// Written earlier in this list
				descriptorSet = writtenList[at].Set;
			}
			else
			{
				descriptorSet = AllocateSet( setLayout );
				device.WriteDescriptors( descriptorSet, bindings );
				TransientSetsWritten++;
				if ( device.ReusesTransientSets ) writtenByContent[hash] = Remember( descriptorSet, setLayout, bindings );
			}

			if ( writtenBindings[set].Length < bindings.Length ) writtenBindings[set] = new Binding[Math.Max( bindings.Length, 16 )];
			bindings.CopyTo( writtenBindings[set] );
			(writtenSets[set], writtenLayouts[set], writtenCounts[set]) = (descriptorSet, setLayout, bindings.Length);
		}

		sets[set] = descriptorSet;
		setOffsetCounts[set] = 0;
		setLayouts[set] = setLayout;
		dirty[0] |= 1 << set;
		dirty[1] |= 1 << set;
	}

	/// <summary>Keep a set written in this list and what it holds, for <see cref="writtenByContent"/>; its index.</summary>
	int Remember( ulong descriptorSet, ulong setLayout, ReadOnlySpan<Binding> bindings )
	{
		if ( writtenListCount == writtenList.Length ) Array.Resize( ref writtenList, writtenList.Length * 2 );
		if ( writtenStoreUsed + bindings.Length > writtenStore.Length ) Array.Resize( ref writtenStore, Math.Max( writtenStore.Length * 2, writtenStoreUsed + bindings.Length ) );
		bindings.CopyTo( writtenStore.AsSpan( writtenStoreUsed ) );
		writtenList[writtenListCount] = (descriptorSet, setLayout, writtenStoreUsed, bindings.Length);
		writtenStoreUsed += bindings.Length;
		return writtenListCount++;
	}

	static int ContentHash( ulong setLayout, ReadOnlySpan<Binding> bindings )
	{
		var hash = setLayout * 0x9E3779B97F4A7C15UL;
		foreach ( ref readonly var b in bindings )
		{
			hash = Mix( hash, (ulong)(uint)b.Slot | (ulong)(uint)b.ArrayElement << 16 | (ulong)b.Type << 32 | (ulong)b.Layout << 40 | (b.Srgb ? 1UL << 48 : 0) );
			hash = Mix( hash, (ulong)b.Buffer.Index | (ulong)b.Buffer.Generation << 32 );
			hash = Mix( hash, (ulong)b.Offset ^ (ulong)b.Size << 40 );
			hash = Mix( hash, (ulong)b.Texture.Index | (ulong)b.Texture.Generation << 32 );
			hash = Mix( hash, (ulong)b.View.Index | (ulong)b.View.Generation << 32 );
			hash = Mix( hash, (ulong)b.Sampler.Index | (ulong)b.Sampler.Generation << 32 );
		}

		return (int)(hash ^ hash >> 32);

		static ulong Mix( ulong hash, ulong value ) => (hash ^ value) * 0x100000001B3UL + (hash >> 29);
	}

	static bool Same( ReadOnlySpan<Binding> a, ReadOnlySpan<Binding> b )
	{
		if ( a.Length != b.Length ) return false;
		for ( int i = 0; i < a.Length; i++ )
		{
			if ( !a[i].Same( b[i] ) ) return false;
		}

		return true;
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
			if ( descriptorPool == descriptorPools.Count ) descriptorPools.Add( device.TakeDescriptorPool() );

			var allocate = new VkDescriptorSetAllocateInfo { sType = VkStructureType.DescriptorSetAllocateInfo, descriptorPool = descriptorPools[descriptorPool], descriptorSetCount = 1, pSetLayouts = &setLayout };
			ulong set;
			var result = vk.vkAllocateDescriptorSets( vk.Device, &allocate, &set );
			if ( result == VkResult.Success )
			{
				descriptorPoolHasSets = true;
				return set;
			}

			if ( !descriptorPoolHasSets || result is not (VkResult.ErrorOutOfPoolMemory or VkResult.ErrorFragmentedPool) )
				result.Check( "vkAllocateDescriptorSets" );

			descriptorPool++;
			descriptorPoolHasSets = false;
		}
	}

	void FlushBindings( int bindPoint )
	{
		// Vulkan tracks descriptor sets per bind point. Only binds here with this point's layout can disturb them.
		var layout = layouts[bindPoint];
		if ( layout == 0 ) throw new InvalidOperationException( bindPoint == 0 ? "No graphics pipeline is bound" : "No compute pipeline is bound" );

		var mask = dirty[bindPoint];
		if ( mask == 0 ) return;

		var expected = layoutSets[bindPoint];
		for ( int set = 0; set < 4; set++ )
		{
			if ( (mask & (1 << set)) == 0 || sets[set] == 0 ) continue;

			var wanted = set switch { 0 => expected.First.Handle, 1 => expected.Second.Handle, 2 => expected.Third.Handle, _ => expected.Fourth.Handle };
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
		if ( lastBindPoint < 0 ) throw new InvalidOperationException( "Bind a pipeline before setting push constants" );
		if ( (uint)offset > ShaderAbi.PushConstantSize - sizeof( T ) || ((offset | sizeof( T )) & 3) != 0 )
			throw new ArgumentException( $"Push constants are {ShaderAbi.PushConstantSize} bytes and must be written in four-byte words" );

		var layout = layouts[lastBindPoint];
		fixed ( T* p = &data )
		{
			vk.vkCmdPushConstants( Handle, layout, device.PushStages( lastBindPoint ), (uint)offset, (uint)sizeof( T ), p );
			if ( device.Capabilities.PipelineLibraries )
			{
				var first = offset / sizeof( uint );
				var count = sizeof( T ) / sizeof( uint );
				new ReadOnlySpan<uint>( p, count ).CopyTo( ((Span<uint>)pushConstantData)[first..] );
				var words = PushConstantMask( first, count );
				pushConstantWords |= words;
				if ( lastBindPoint == Vulkan.Vk.BindPointGraphics ) graphicsPushConstantWords |= words;
				else graphicsPushConstantWords &= ~words;
			}
		}
	}

	static uint PushConstantMask( int first, int count ) => (uint.MaxValue >> (32 - count)) << first;

	/// <summary>Re-establish any words last pushed with the other layout's independent-sets setting.</summary>
	void FlushPushConstants( int bindPoint )
	{
		var graphics = bindPoint == Vulkan.Vk.BindPointGraphics;
		var words = graphics ? pushConstantWords & ~graphicsPushConstantWords : graphicsPushConstantWords;
		if ( words == 0 ) return;

		var layout = layouts[bindPoint];
		fixed ( uint* data = &pushConstantData[0] )
		{
			do
			{
				var first = System.Numerics.BitOperations.TrailingZeroCount( words );
				var count = System.Numerics.BitOperations.TrailingZeroCount( ~(words >> first) );
				vk.vkCmdPushConstants( Handle, layout, device.PushStages( bindPoint ), (uint)first * sizeof( uint ), (uint)count * sizeof( uint ), data + first );
				words &= ~PushConstantMask( first, count );
			}
			while ( words != 0 );
		}

		graphicsPushConstantWords = graphics ? pushConstantWords : 0;
	}

	//
	// Work
	//

	/// <summary>Get ready for a draw; false when it's skipped, its pipeline not made yet.</summary>
	bool BeforeDraw()
	{
		Recording();
		if ( !rendering ) throw new InvalidOperationException( "Draws happen while rendering: call BeginRendering first" );
		if ( skippingDraws )
		{
			skippedDraws++;
			device.CountSkippedDraw();
			return false;
		}

		FlushBindings( Vulkan.Vk.BindPointGraphics );
		FlushPushConstants( Vulkan.Vk.BindPointGraphics );
		draws++;
		return true;
	}

	/// <summary>Draws skipped since the list began because their pipeline wasn't made yet.</summary>
	public int SkippedDraws => skippedDraws;

	/// <summary>Draw vertices.</summary>
	public void Draw( int vertexCount, int instanceCount = 1, int firstVertex = 0, int firstInstance = 0 )
	{
		if ( !BeforeDraw() ) return;
		vk.vkCmdDraw( Handle, (uint)vertexCount, (uint)instanceCount, (uint)firstVertex, (uint)firstInstance );
	}

	/// <summary>Draw indexed vertices.</summary>
	public void DrawIndexed( int indexCount, int instanceCount = 1, int firstIndex = 0, int vertexOffset = 0, int firstInstance = 0 )
	{
		if ( !BeforeDraw() ) return;
		vk.vkCmdDrawIndexed( Handle, (uint)indexCount, (uint)instanceCount, (uint)firstIndex, vertexOffset, (uint)firstInstance );
	}

	/// <summary>Draw vertices with arguments from a buffer: <paramref name="count"/> draws, <paramref name="stride"/> bytes apart.</summary>
	public void DrawIndirect( BufferHandle arguments, long offset, int count, int stride = 16 )
	{
		if ( !BeforeDraw() ) return;
		var buffer = device.Get( arguments ).Buffer;
		if ( !device.IndirectCounts.IsValid )
		{
			vk.vkCmdDrawIndirect( Handle, buffer, (ulong)offset, (uint)count, (uint)stride );
			return;
		}

		// Counted, as Lavapipe would otherwise cap it at an earlier counted draw's count (Device.IndirectCounts)
		var counts = device.Get( device.IndirectCounts ).Buffer;
		for ( int done = 0; done < count; done += Device.IndirectCountWords - 1 )
		{
			var draws = Math.Min( count - done, Device.IndirectCountWords - 1 );
			vk.vkCmdDrawIndirectCount( Handle, buffer, (ulong)(offset + (long)done * stride), counts, (ulong)draws * 4, (uint)draws, (uint)stride );
		}
	}

	/// <summary>Draw indexed vertices with arguments from a buffer: <paramref name="count"/> draws, <paramref name="stride"/> bytes apart.</summary>
	public void DrawIndexedIndirect( BufferHandle arguments, long offset, int count, int stride = 20 )
	{
		if ( !BeforeDraw() ) return;
		var buffer = device.Get( arguments ).Buffer;
		if ( !device.IndirectCounts.IsValid )
		{
			vk.vkCmdDrawIndexedIndirect( Handle, buffer, (ulong)offset, (uint)count, (uint)stride );
			return;
		}

		// Counted, as Lavapipe would otherwise cap it at an earlier counted draw's count (Device.IndirectCounts)
		var counts = device.Get( device.IndirectCounts ).Buffer;
		for ( int done = 0; done < count; done += Device.IndirectCountWords - 1 )
		{
			var draws = Math.Min( count - done, Device.IndirectCountWords - 1 );
			vk.vkCmdDrawIndexedIndirectCount( Handle, buffer, (ulong)(offset + (long)done * stride), counts, (ulong)draws * 4, (uint)draws, (uint)stride );
		}
	}

	/// <summary>Draw indexed vertices with arguments and a draw count from buffers, at most <paramref name="maxCount"/> draws.</summary>
	public void DrawIndexedIndirectCount( BufferHandle arguments, long offset, BufferHandle countBuffer, long countOffset, int maxCount, int stride = 20 )
	{
		if ( !BeforeDraw() ) return;
		vk.vkCmdDrawIndexedIndirectCount( Handle, device.Get( arguments ).Buffer, (ulong)offset, device.Get( countBuffer ).Buffer, (ulong)countOffset, (uint)maxCount, (uint)stride );
	}

	/// <summary>
	/// Draw with the bound task and mesh pipeline (<see cref="DeviceCapabilities.MeshShaders"/>): <paramref name="count"/>
	/// task grids, each three group counts, <paramref name="stride"/> bytes apart.
	/// </summary>
	public void DrawMeshTasksIndirect( BufferHandle arguments, long offset, int count = 1, int stride = 12 )
	{
		if ( !BeforeDraw() ) return;
		vk.vkCmdDrawMeshTasksIndirectEXT( Handle, device.Get( arguments ).Buffer, (ulong)offset, (uint)count, (uint)stride );
	}

	/// <summary>Draw task grids with the bound task and mesh pipeline, with the count from a buffer, at most <paramref name="maxCount"/>.</summary>
	public void DrawMeshTasksIndirectCount( BufferHandle arguments, long offset, BufferHandle countBuffer, long countOffset, int maxCount, int stride = 12 )
	{
		if ( !BeforeDraw() ) return;
		vk.vkCmdDrawMeshTasksIndirectCountEXT( Handle, device.Get( arguments ).Buffer, (ulong)offset, device.Get( countBuffer ).Buffer, (ulong)countOffset, (uint)maxCount, (uint)stride );
	}

	/// <summary>Run the bound compute pipeline over a grid of thread groups.</summary>
	public void Dispatch( int x, int y = 1, int z = 1 )
	{
		device.CheckGroupCounts( x, y, z );
		OutsideRendering();
		FlushBarriers();
		FlushBindings( Vulkan.Vk.BindPointCompute );
		FlushPushConstants( Vulkan.Vk.BindPointCompute );
		vk.vkCmdDispatch( Handle, (uint)x, (uint)y, (uint)z );
	}

	/// <summary>Run the bound compute pipeline with the group counts in a buffer.</summary>
	public void DispatchIndirect( BufferHandle arguments, long offset = 0 )
	{
		OutsideRendering();
		FlushBarriers();
		FlushBindings( Vulkan.Vk.BindPointCompute );
		FlushPushConstants( Vulkan.Vk.BindPointCompute );
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
		// The next size up from the list's last page, or one that holds the request
		var next = page is null ? 0 : Math.Min( page.Class < 0 ? PageSizes.Length - 1 : page.Class + 1, PageSizes.Length - 1 );
		while ( next < PageSizes.Length - 1 && PageSizes[next] < size ) next++;
		var pageClass = size <= PageSizes[next] ? next : -1;
		Interlocked.Increment( ref device.UploadPagesTaken );
		if ( pageClass >= 0 ) Interlocked.Increment( ref device.UploadPagesTakenBySize[pageClass] );

		UploadPage fresh;
		if ( pageClass >= 0 && device.FreePages[pageClass].TryTake( out var recycled ) )
		{
			fresh = recycled;
		}
		else
		{
			fresh = CreatePage( device, pageClass >= 0 ? PageSizes[pageClass] : (size + 0xFFFF) & ~0xFFFFL, pageClass );
			if ( pageClass >= 0 ) Interlocked.Increment( ref device.UploadPagesMadeBySize[pageClass] );
			if ( pageClass == PageSizes.Length - 1 ) Interlocked.Increment( ref device.UploadPageMisses );
		}

		pages.Add( fresh );
		page = fresh;
	}

	/// <summary>
	/// Whether the biggest upload pages share large blocks (<c>BOREALIS_UPLOAD_PAGES_SHARED=1</c>), 32 to a block, rather than
	/// each being an allocation of its own: a load takes hundreds, each a driver call. Pages are trimmed from the emptiest
	/// blocks first, so blocks drain.
	/// </summary>
	internal static bool SharedPages { get; set; } = Environment.GetEnvironmentVariable( "BOREALIS_UPLOAD_PAGES_SHARED" ) == "1";

	/// <summary>
	/// A new upload page of <paramref name="pageSize"/> bytes (of <see cref="PageSizes"/> class <paramref name="pageClass"/>,
	/// or -1 for one big request), on any thread. The biggest are each their own allocation, so pages freed as fewer are
	/// needed leave no holes in shared blocks, or share large blocks (<see cref="SharedPages"/>).
	/// </summary>
	internal static UploadPage CreatePage( Device device, long pageSize, int pageClass )
	{
		BufferHandle buffer;
		Memory.MemoryAllocator.OwnAllocation = pageSize > PageSize || (pageSize == PageSize && !SharedPages);
		Memory.MemoryAllocator.InLargeBlocks = pageSize == PageSize && SharedPages;
		try
		{
			buffer = device.CreateBuffer( new BufferInfo
			{
				Size = pageSize,
				Usage = BufferUsage.Uniform | BufferUsage.Storage | BufferUsage.Vertex | BufferUsage.Index | BufferUsage.Indirect,
				Memory = MemoryLocation.Upload,
				Name = "Upload page",
			} );
		}
		finally
		{
			Memory.MemoryAllocator.OwnAllocation = false;
			Memory.MemoryAllocator.InLargeBlocks = false;
		}

		ref var record = ref device.Get( buffer );
		Interlocked.Add( ref device.UploadPageBytes, pageSize );
		return new UploadPage { Buffer = buffer, VkBuffer = record.Buffer, Mapped = record.Memory.Mapped, Size = pageSize, Class = pageClass };
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

		// A volume larger than an upload page goes a page's worth of whole slices at a time, through recycled pages, rather
		// than through an upload buffer of its own made (and its memory allocated) for it
		ref var record = ref device.Get( texture );
		var bytesPerBlock = record.Info.Format.BytesPerBlock();
		// The GCD with 16 is the smaller power of two dividing the two alignments.
		var alignment = 16 * (bytesPerBlock >> Math.Min( 4, System.Numerics.BitOperations.TrailingZeroCount( (uint)bytesPerBlock ) ));
		var depth = VolumeDepth( in record.Info, mip );
		if ( data.Length > PageSize && depth > 1 && data.Length % depth == 0 )
		{
			var slice = data.Length / depth;
			var perChunk = (int)Math.Max( PageSize / slice, 1 );
			for ( int z = 0; z < depth; z += perChunk )
			{
				var slices = Math.Min( perChunk, depth - z );
				var chunk = AllocateUpload( slice * slices, alignment );
				data.Slice( z * slice, slice * slices ).CopyTo( chunk.Data );
				CopyBufferToTexture( chunk.Buffer, chunk.Offset, texture, mip, layer, width, height, x, y, z, slices );
			}

			return;
		}

		var allocation = AllocateUpload( data.Length, alignment );
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
	/// <see cref="ImageLayout.CopyDestination"/>: every slice of a volume's mip, one after another.
	/// </summary>
	public void CopyBufferToTexture( BufferHandle source, long offset, TextureHandle texture, int mip, int layer, int width, int height, int x = 0, int y = 0 ) =>
		CopyBufferToTexture( source, offset, texture, mip, layer, width, height, x, y, 0, -1 );

	/// <summary>
	/// <see cref="CopyBufferToTexture(BufferHandle, long, TextureHandle, int, int, int, int, int, int)"/> into
	/// <paramref name="slices"/> slices of a volume's mip from slice <paramref name="z"/> (-1: every slice).
	/// </summary>
	public void CopyBufferToTexture( BufferHandle source, long offset, TextureHandle texture, int mip, int layer, int width, int height, int x, int y, int z, int slices )
	{
		OutsideRendering();
		FlushBarriers();
		ref var record = ref device.Get( texture );
		var region = new VkBufferImageCopy
		{
			bufferOffset = (ulong)offset,
			imageSubresource = new VkImageSubresourceLayers { aspectMask = record.Aspect & ~Vulkan.Vk.AspectStencil, mipLevel = (uint)mip, baseArrayLayer = (uint)layer, layerCount = 1 },
			imageOffset = new VkOffset3D { x = x, y = y, z = z },
			imageExtent = new VkExtent3D { width = (uint)width, height = (uint)height, depth = (uint)(slices < 0 ? VolumeDepth( in record.Info, mip ) : slices) },
		};

		vk.vkCmdCopyBufferToImage( Handle, device.Get( source ).Buffer, record.Image, VkImageLayout.TransferDstOptimal, 1, &region );
	}

	/// <summary>A mip's depth: its slices for a volume, else 1.</summary>
	static int VolumeDepth( in TextureInfo info, int mip ) => info.Dimension == TextureDimension.Texture3D ? Math.Max( info.Depth >> mip, 1 ) : 1;

	/// <summary>
	/// Copy one mip and layer of a texture in <see cref="ImageLayout.CopySource"/> into a buffer as tightly packed rows:
	/// every slice of a volume's mip, one after another.
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
			imageExtent = new VkExtent3D { width = (uint)Math.Max( record.Info.Width >> mip, 1 ), height = (uint)Math.Max( record.Info.Height >> mip, 1 ), depth = (uint)VolumeDepth( in record.Info, mip ) },
		};

		vk.vkCmdCopyImageToBuffer( Handle, record.Image, VkImageLayout.TransferSrcOptimal, device.Get( destination ).Buffer, 1, &region );
	}

	/// <summary>
	/// Copy <paramref name="layers"/> layers from <paramref name="layer"/> of one mip between textures made alike (every
	/// aspect, every slice of a volume), from <see cref="ImageLayout.CopySource"/> to <see cref="ImageLayout.CopyDestination"/>.
	/// </summary>
	internal void CopyImage( TextureHandle source, TextureHandle destination, int mip, int layer, int layers )
	{
		OutsideRendering();
		FlushBarriers();
		ref var src = ref device.Get( source );
		ref var dst = ref device.Get( destination );
		var region = new VkImageCopy
		{
			srcSubresource = new VkImageSubresourceLayers { aspectMask = src.Aspect, mipLevel = (uint)mip, baseArrayLayer = (uint)layer, layerCount = (uint)layers },
			dstSubresource = new VkImageSubresourceLayers { aspectMask = dst.Aspect, mipLevel = (uint)mip, baseArrayLayer = (uint)layer, layerCount = (uint)layers },
			extent = new VkExtent3D { width = (uint)Math.Max( src.Info.Width >> mip, 1 ), height = (uint)Math.Max( src.Info.Height >> mip, 1 ), depth = (uint)VolumeDepth( in src.Info, mip ) },
		};

		vk.vkCmdCopyImage( Handle, src.Image, VkImageLayout.TransferSrcOptimal, dst.Image, VkImageLayout.TransferDstOptimal, 1, &region );
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
	/// Copy one mip and layer of a texture into a mip and layer of another of the same format and size there, from
	/// <see cref="ImageLayout.CopySource"/> to <see cref="ImageLayout.CopyDestination"/>.
	/// </summary>
	public void CopyTextureSubresource( TextureHandle source, int sourceMip, int sourceLayer, TextureHandle destination, int destinationMip, int destinationLayer )
	{
		OutsideRendering();
		FlushBarriers();
		ref var src = ref device.Get( source );
		ref var dst = ref device.Get( destination );
		var region = new VkImageCopy
		{
			srcSubresource = new VkImageSubresourceLayers { aspectMask = src.Aspect, mipLevel = (uint)sourceMip, baseArrayLayer = (uint)sourceLayer, layerCount = 1 },
			dstSubresource = new VkImageSubresourceLayers { aspectMask = dst.Aspect, mipLevel = (uint)destinationMip, baseArrayLayer = (uint)destinationLayer, layerCount = 1 },
			extent = new VkExtent3D { width = (uint)Math.Max( src.Info.Width >> sourceMip, 1 ), height = (uint)Math.Max( src.Info.Height >> sourceMip, 1 ), depth = 1 },
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
	public void Blit( TextureHandle source, (int X, int Y, int Width, int Height) from, TextureHandle destination, (int X, int Y, int Width, int Height) to, Filter filter = Filter.Linear, int sourceMip = 0, int destinationMip = 0 ) =>
		Blit( source, from, destination, to, filter, sourceMip, destinationMip, 0, 0 );

	/// <summary>
	/// The blit above, between
	/// array layers (a cube's faces are six layers each). A volume's whole depth at each mip is blitted.
	/// </summary>
	public void Blit( TextureHandle source, (int X, int Y, int Width, int Height) from, TextureHandle destination, (int X, int Y, int Width, int Height) to, Filter filter, int sourceMip, int destinationMip, int sourceLayer, int destinationLayer )
	{
		OutsideRendering();
		FlushBarriers();
		ref var src = ref device.Get( source );
		ref var dst = ref device.Get( destination );
		var region = new VkImageBlit
		{
			srcSubresource = new VkImageSubresourceLayers { aspectMask = src.Aspect, mipLevel = (uint)sourceMip, baseArrayLayer = (uint)sourceLayer, layerCount = 1 },
			srcOffset0 = new VkOffset3D { x = from.X, y = from.Y },
			srcOffset1 = new VkOffset3D { x = from.X + from.Width, y = from.Y + from.Height, z = VolumeDepth( in src.Info, sourceMip ) },
			dstSubresource = new VkImageSubresourceLayers { aspectMask = dst.Aspect, mipLevel = (uint)destinationMip, baseArrayLayer = (uint)destinationLayer, layerCount = 1 },
			dstOffset0 = new VkOffset3D { x = to.X, y = to.Y },
			dstOffset1 = new VkOffset3D { x = to.X + to.Width, y = to.Y + to.Height, z = VolumeDepth( in dst.Info, destinationMip ) },
		};

		vk.vkCmdBlitImage( Handle, src.Image, VkImageLayout.TransferSrcOptimal, dst.Image, VkImageLayout.TransferDstOptimal, 1, &region, (int)filter );
	}

	/// <summary>
	/// Resolve a multisampled texture or source rectangle into a single-sampled texture at its origin, from
	/// <see cref="ImageLayout.CopySource"/> to <see cref="ImageLayout.CopyDestination"/>.
	/// </summary>
	public void ResolveTexture( TextureHandle source, TextureHandle destination, (int X, int Y, int Width, int Height)? sourceRegion = null )
	{
		OutsideRendering();
		FlushBarriers();
		ref var src = ref device.Get( source );
		ref var dst = ref device.Get( destination );
		var (x, y, width, height) = sourceRegion ?? (0, 0, src.Info.Width, src.Info.Height);
		var region = new VkImageResolve
		{
			srcSubresource = new VkImageSubresourceLayers { aspectMask = Vulkan.Vk.AspectColor, layerCount = 1 },
			srcOffset = new VkOffset3D { x = x, y = y },
			dstSubresource = new VkImageSubresourceLayers { aspectMask = Vulkan.Vk.AspectColor, layerCount = 1 },
			extent = new VkExtent3D { width = (uint)width, height = (uint)height, depth = 1 },
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

	/// <summary>Whether a scope on <paramref name="other"/> has the same rendering info apart from flags.</summary>
	public bool Matches( RenderingTargets other )
	{
		if ( colors != other.colors || depth != other.depth || stencil != other.stencil || width != other.width || height != other.height ) return false;
		for ( int i = 0; i < colors + (depth ? 1 : 0); i++ )
		{
			ref readonly var a = ref attachments[i];
			ref readonly var b = ref other.attachments[i];
			// Carried scopes always use LOAD and a zero clear value.
			if ( a.imageView != b.imageView || a.imageLayout != b.imageLayout || a.storeOp != b.storeOp ||
				a.resolveMode != b.resolveMode || a.resolveImageView != b.resolveImageView || a.resolveImageLayout != b.resolveImageLayout ) return false;
		}

		return true;
	}

	/// <summary>
	/// The scope's rendering info with <paramref name="flags"/>, its attachments copied into <paramref name="into"/>
	/// (<see cref="Max"/> long). A helper scope uses exactly the info of the segment it pairs with.
	/// </summary>
	public VkRenderingInfo Info( VkRenderingAttachmentInfo* into, uint flags )
	{
		var count = colors + (depth ? 1 : 0);
		for ( int i = 0; i < count; i++ ) into[i] = attachments[i];

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
