using Borealis.Gpu.Vulkan;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Borealis.Gpu;

/// <summary>
/// A Vulkan queue and the timeline semaphore its submissions signal, one value each.
/// </summary>
internal sealed unsafe class DeviceQueue : IDisposable
{
	readonly VkDeviceApi vk;
	readonly Device device;
	public readonly nint Handle;
	public readonly uint Family;
	public readonly QueueType Type;
	public readonly ulong Timeline;
	public readonly object SubmitLock = new();

	ulong submitted;
	ulong completed;

	/// <summary>Lists submitted to go with the next submission (<see cref="Device.SubmitLater"/>). Use with <see cref="SubmitLock"/> held.</summary>
	public readonly List<nint> Pending = new();

	/// <summary>The last value a submission made to the GPU signals; <see cref="Submitted"/> is past it while lists are pending.</summary>
	public ulong Flushed;

	/// <summary>The last pending list, when it ended with its rendering scope suspended (<see cref="CommandList.EndsSuspended"/>).</summary>
	public CommandList Suspended;

	/// <summary>The value the last submission signals.</summary>
	public ulong Submitted => Volatile.Read( ref submitted );

	public DeviceQueue( Device device, VkDeviceApi vk, uint family, QueueType type )
	{
		this.vk = vk;
		this.device = device;
		Family = family;
		Type = type;

		nint queue;
		vk.vkGetDeviceQueue( vk.Device, family, 0, &queue );
		Handle = queue;

		var timelineInfo = new VkSemaphoreTypeCreateInfo { sType = VkStructureType.SemaphoreTypeCreateInfo, semaphoreType = Vulkan.Vk.SemaphoreTimeline };
		var info = new VkSemaphoreCreateInfo { sType = VkStructureType.SemaphoreCreateInfo, pNext = &timelineInfo };
		ulong semaphore;
		vk.vkCreateSemaphore( vk.Device, &info, null, &semaphore ).Check( "vkCreateSemaphore (timeline)" );
		Timeline = semaphore;
		device.SetName( VkObjectType.Semaphore, semaphore, $"{type} timeline" );
	}

	/// <summary>The value the GPU has reached; everything submitted once the device is lost.</summary>
	public ulong Completed
	{
		get
		{
			if ( Volatile.Read( ref completed ) >= Submitted ) return completed;

			// Nothing more will finish on a lost device: what was submitted counts as done, so its resources can be freed
			if ( device.IsLost ) return Submitted;

			ulong value;
			device.Check( vk.vkGetSemaphoreCounterValue( vk.Device, Timeline, &value ), "vkGetSemaphoreCounterValue" );

			// Past anything submitted: the driver signalled everything pending because the device is lost, before any call
			// returned VK_ERROR_DEVICE_LOST. Nothing may be submitted after it (a signal below the timeline's value is invalid)
			if ( value > Submitted )
			{
				device.MarkLost( $"The {Type} timeline jumped to {value} past the {Submitted} submitted", byDriver: true );
				return Submitted;
			}

			if ( value > Volatile.Read( ref completed ) ) Volatile.Write( ref completed, value );
			return value;
		}
	}

	/// <summary>The value the GPU had reached when last asked, without asking it.</summary>
	public ulong CompletedSeen => Volatile.Read( ref completed );

	/// <summary>Take the next value for a submission. Call with <see cref="SubmitLock"/> held.</summary>
	public ulong Next() => ++submitted;

	/// <summary>
	/// Wait on the CPU until the GPU reaches <paramref name="value"/>, which must have been submitted. Returns at once when the
	/// device is lost, and takes it as lost if the GPU stops getting anywhere (<see cref="Device.HangSeconds"/>).
	/// </summary>
	public void Wait( ulong value )
	{
		if ( value == 0 || device.IsLost || Completed >= value ) return;

		// A value nothing will signal would be waited for forever
		if ( value > Volatile.Read( ref Flushed ) ) throw new InvalidOperationException( $"{Type} {value} hasn't been submitted to the GPU (flushed {Volatile.Read( ref Flushed )}, submitted {Submitted})" );

		device.WaitForTimeline( this, value );
	}

	public void Dispose() => vk.vkDestroySemaphore( vk.Device, Timeline, null );
}

public sealed unsafe partial class Device
{
	DeviceQueue graphicsQueue;
	DeviceQueue computeQueue;

	internal DeviceQueue QueueFor( QueueType type ) => type == QueueType.Compute ? computeQueue : graphicsQueue;

	/// <summary>
	/// What one frame in flight used: the command lists recorded in it and how far each queue had got when it closed.
	/// </summary>
	sealed class Frame
	{
		public ulong Graphics, Compute;
		public readonly List<CommandList> Lists = new();
	}

	Frame[] frames;
	Frame frame;
	readonly ConcurrentBag<CommandList>[] freeLists = [new(), new()];
	internal readonly ConcurrentBag<UploadPage> FreePages = new();

	// Descriptor pools for command lists' transient sets, shared by every list: a list keeps one when its frame retires and
	// gives the rest back reset, and a recording thread takes one rather than making one (making them cost ~70 ms of
	// recording in a map's first seconds). When few are left, more are made ahead on a worker
	readonly ConcurrentBag<ulong> freeDescriptorPools = new();
	int makingDescriptorPools;
	const int DescriptorPoolsLow = 8;

	/// <summary>Descriptor pools made for command lists so far, for tests and reports.</summary>
	public int DescriptorPoolsMade => descriptorPoolsMade;

	int descriptorPoolsMade;

	/// <summary>Descriptor pools made where a recording thread asked, for want of a spare, for tests.</summary>
	internal int DescriptorPoolsMadeTaking => descriptorPoolsMadeTaking;

	int descriptorPoolsMadeTaking;

	/// <summary>A reset descriptor pool for a command list's transient sets: a spare, else a new one.</summary>
	internal ulong TakeDescriptorPool()
	{
		if ( !freeDescriptorPools.TryTake( out var pool ) )
		{
			pool = CreateDescriptorPool();
			Interlocked.Increment( ref descriptorPoolsMadeTaking );
		}

		if ( freeDescriptorPools.Count < DescriptorPoolsLow && Interlocked.CompareExchange( ref makingDescriptorPools, 1, 0 ) == 0 )
		{
			ThreadPool.UnsafeQueueUserWorkItem( static device =>
			{
				try
				{
					for ( int i = 0; i < DescriptorPoolsLow; i++ ) device.freeDescriptorPools.Add( device.CreateDescriptorPool() );
				}
				finally
				{
					Volatile.Write( ref device.makingDescriptorPools, 0 );
				}
			}, this, preferLocal: false );
		}

		return pool;
	}

	/// <summary>A command list's descriptor pool, reset, back for any list.</summary>
	internal void ReturnDescriptorPool( ulong pool ) => freeDescriptorPools.Add( pool );

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
		Vk.vkCreateDescriptorPool( Vk.Device, &info, null, &descriptorPool ).Check( "vkCreateDescriptorPool" );
		Interlocked.Increment( ref descriptorPoolsMade );
		return descriptorPool;
	}
	readonly List<SwapChain> swapChains = new();

	/// <summary>Frames begun so far; the first is 0.</summary>
	public ulong FrameNumber { get; private set; }

	/// <summary>Which of the <see cref="FramesInFlight"/> slots the current frame uses.</summary>
	public int FrameSlot => (int)(FrameNumber % (ulong)FramesInFlight);

	void CreateFrames()
	{
		frames = new Frame[FramesInFlight];
		for ( int i = 0; i < frames.Length; i++ ) frames[i] = new Frame();
		frame = frames[0];
	}

	/// <summary>
	/// Close the current frame and start the next: wait until the GPU has finished the frame that last used this slot,
	/// then recycle its command lists, upload space and descriptor sets, and free what's been destroyed since. A list of the
	/// closing frame still recording goes with the next frame instead, once (see <see cref="CarryRecording"/>).
	/// </summary>
	public void BeginFrame()
	{
		var carried = CarryRecording();
		RememberEvent( "frame starts:", (long)FrameNumber + 1 );

		Flush();
		frame.Graphics = graphicsQueue.Submitted;
		frame.Compute = computeQueue.Submitted;

		// What was destroyed waits for the carried lists' frame as well: they may use it
		if ( carried is null ) TagDestroyed();

		FrameNumber++;
		frame = frames[FrameSlot];

		graphicsQueue.Wait( frame.Graphics );
		computeQueue.Wait( frame.Compute );

		var recycle = recycling;
		lock ( frame.Lists )
		{
			recycle.AddRange( frame.Lists );
			frame.Lists.Clear();
			if ( carried is not null ) frame.Lists.AddRange( carried );
		}

		// Spare lists before the frame's come back: those every frame in flight left unused (TrimSpares)
		for ( int q = 0; q < freeLists.Length; q++ ) freeListsLow[q] = Math.Min( freeListsLow[q], freeLists[q].Count );

		foreach ( var list in recycle )
		{
			list.Recycle();
			freeLists[(int)list.Queue].Add( list );
		}

		recycle.Clear();
		CollectGarbage();
		TrimSpares();
		Allocator.Trim( FrameNumber );
	}

	// The frame's lists as BeginFrame recycles them, kept
	readonly List<CommandList> recycling = new();

	/// <summary>Lists carried into the frame after the one they began in, for the tests.</summary>
	internal int ListsCarried { get; private set; }

	bool warnedCarried;

	/// <summary>
	/// Take the closing frame's lists that began in it and are still recording: one begun before something closed the
	/// frame in the middle of its recording is submitted in the next frame, so it's recycled with that one. Recycled with
	/// its own, its command buffer and upload pages would be reused while the GPU may still be running it. A list still
	/// recording a frame later was never submitted, and is recycled as before.
	/// </summary>
	List<CommandList> CarryRecording()
	{
		List<CommandList> carried = null;
		lock ( frame.Lists )
		{
			for ( int i = 0; i < frame.Lists.Count; i++ )
			{
				var list = frame.Lists[i];
				if ( !list.IsRecording || list.Frame != FrameNumber ) continue;
				(carried ??= new()).Add( list );
				frame.Lists.RemoveAt( i-- );
			}
		}

		if ( carried is null ) return null;
		ListsCarried += carried.Count;
		if ( !warnedCarried )
		{
			warnedCarried = true;
			message?.Invoke( new DeviceMessage( MessageSeverity.Warning, "Borealis.Gpu.ListCarried", $"Frame {FrameNumber} closed while '{carried[0].Name}' was recording; it goes with the next frame. Start frames before recording into them." ) );
		}

		return carried;
	}

	/// <summary>Free upload pages kept for later lists however few frames use: about 32 MiB.</summary>
	internal const int KeptFreeUploadPages = 8;

	/// <summary>Frames a free upload page has to go unused before it's freed.</summary>
	internal const ulong UploadPageIdleFrames = 300;

	/// <summary>Spare command lists of each queue kept however few frames use.</summary>
	internal const int KeptFreeCommandLists = 16;

	int freePagesLow = int.MaxValue;
	readonly int[] freeListsLow = [int.MaxValue, int.MaxValue];
	ulong trimFrom;

	/// <summary>
	/// Free upload pages and spare command lists that stayed unused for <see cref="UploadPageIdleFrames"/> frames, past
	/// <see cref="KeptFreeUploadPages"/> and <see cref="KeptFreeCommandLists"/>: a load's uploads take hundreds of pages
	/// (Construct's held 1.4 GB of mapped memory from then on), and a map's frames take over a hundred lists, whose command
	/// pools keep the driver's command memory (Construct's held ~370 MB of video memory in the menu after it); steady
	/// frames take a few.
	/// </summary>
	void TrimSpares()
	{
		freePagesLow = Math.Min( freePagesLow, FreePages.Count );
		if ( FrameNumber - trimFrom < UploadPageIdleFrames ) return;

		for ( int i = KeptFreeUploadPages; i < freePagesLow && FreePages.TryTake( out var page ); i++ ) Destroy( page.Buffer );
		freePagesLow = int.MaxValue;

		// A spare list's frame has finished: its command pool and descriptor pool can go at once
		for ( int q = 0; q < freeLists.Length; q++ )
		{
			int freed = 0;
			for ( int i = KeptFreeCommandLists; i < freeListsLow[q] && freeLists[q].TryTake( out var list ); i++ )
			{
				list.Destroy();
				freed++;
			}

			freeListsLow[q] = int.MaxValue;
			if ( freed == 0 ) continue;

			// The demand dropped: the spares kept still hold what the busy frames grew their pools to
			commandListsFreed += freed;
			int kept = 0;
			while ( kept < keptLists.Length && freeLists[q].TryTake( out var list ) ) keptLists[kept++] = list;
			for ( int i = 0; i < kept; i++ )
			{
				keptLists[i].ReleaseCommandMemory();
				freeLists[q].Add( keptLists[i] );
				keptLists[i] = null;
			}
		}

		trimFrom = FrameNumber;
	}

	readonly CommandList[] keptLists = new CommandList[KeptFreeCommandLists];

	int commandListsFreed;

	/// <summary>Spare command lists freed for going unused, for tests and reports.</summary>
	public int CommandListsFreed => commandListsFreed;

	/// <summary>Spare command lists of a queue, for tests.</summary>
	internal int FreeCommandLists( QueueType queue ) => freeLists[(int)queue].Count;

	/// <summary>
	/// A command list to record into, from the current frame. Submit it with <see cref="Submit"/> in the same frame; it's
	/// recycled once that frame's work has finished.
	/// </summary>
	public CommandList BeginCommandList( QueueType queue = QueueType.Graphics, string name = null )
	{
		if ( queue == QueueType.Compute && !Capabilities.AsyncCompute ) queue = QueueType.Graphics;

		if ( !freeLists[(int)queue].TryTake( out var list ) ) list = new CommandList( this, QueueFor( queue ), queue );

		lock ( frame.Lists ) frame.Lists.Add( list );
		list.Frame = FrameNumber;
		list.Begin( name );
		return list;
	}

	/// <summary>
	/// Submit a recorded command list to its queue, after the work in <paramref name="waits"/> (on any queue). With a
	/// swap chain, it also waits for the image <see cref="SwapChain.Acquire"/> returned and lets it be presented. Returns
	/// the point on the queue's timeline that's reached when it's done.
	/// </summary>
	public SyncPoint Submit( CommandList list, ReadOnlySpan<SyncPoint> waits = default, SwapChain swapChain = null )
	{
		list.End();
		ThrowIfLost( "vkQueueSubmit2" );

		var queue = QueueFor( list.Queue );
		var waitInfos = stackalloc VkSemaphoreSubmitInfo[waits.Length + 1];
		var waitCount = 0u;

		foreach ( var wait in waits )
		{
			if ( !wait.IsValid ) continue;
			var from = QueueFor( wait.Queue );

			// Barriers already order work on one queue
			if ( from == queue || from.Completed >= wait.Value ) continue;

			// What it waits for has to reach the GPU first
			if ( wait.Value > Volatile.Read( ref from.Flushed ) ) Flush( from );
			waitInfos[waitCount++] = new VkSemaphoreSubmitInfo { sType = VkStructureType.SemaphoreSubmitInfo, semaphore = from.Timeline, value = wait.Value, stageMask = Vulkan.Vk.Stage2AllCommands };
		}

		var signals = stackalloc VkSemaphoreSubmitInfo[2];
		var signalCount = 1u;

		if ( swapChain is not null )
		{
			if ( !swapChain.HasImage ) throw new InvalidOperationException( "The swap chain has no acquired image to submit for" );
			waitInfos[waitCount++] = new VkSemaphoreSubmitInfo { sType = VkStructureType.SemaphoreSubmitInfo, semaphore = swapChain.AcquireSemaphore, stageMask = Vulkan.Vk.Stage2AllCommands };
			signals[signalCount++] = new VkSemaphoreSubmitInfo { sType = VkStructureType.SemaphoreSubmitInfo, semaphore = swapChain.PresentSemaphore, stageMask = Vulkan.Vk.Stage2AllCommands };
		}

		lock ( queue.SubmitLock )
		{
			// Lists waiting to go with this one go first, in one submission, unless this one waits: its waits are its own
			if ( waitCount > 0 || swapChain is not null ) FlushLocked( queue );
			PairScopes( queue, list );

			var value = queue.Next();
			Remember( queue, value, list, queue.Pending.Count );
			signals[0] = new VkSemaphoreSubmitInfo { sType = VkStructureType.SemaphoreSubmitInfo, semaphore = queue.Timeline, value = value, stageMask = Vulkan.Vk.Stage2AllCommands };

			// A scope it leaves suspended ends in this submission: nothing after it can resume it
			var ending = list.EndsSuspended ? ScopeList( queue, list.SuspendedTargets, Vulkan.Vk.RenderingResuming ) : null;
			var count = queue.Pending.Count + (ending is null ? 1 : 2);
			var commandBuffers = stackalloc VkCommandBufferSubmitInfo[count];
			for ( int i = 0; i < queue.Pending.Count; i++ ) commandBuffers[i] = new VkCommandBufferSubmitInfo { sType = VkStructureType.CommandBufferSubmitInfo, commandBuffer = queue.Pending[i] };
			commandBuffers[queue.Pending.Count] = new VkCommandBufferSubmitInfo { sType = VkStructureType.CommandBufferSubmitInfo, commandBuffer = list.Handle };
			if ( ending is not null ) commandBuffers[count - 1] = new VkCommandBufferSubmitInfo { sType = VkStructureType.CommandBufferSubmitInfo, commandBuffer = ending.Handle };

			var submit = new VkSubmitInfo2
			{
				sType = VkStructureType.SubmitInfo2,
				waitSemaphoreInfoCount = waitCount,
				pWaitSemaphoreInfos = waitInfos,
				commandBufferInfoCount = (uint)count,
				pCommandBufferInfos = commandBuffers,
				signalSemaphoreInfoCount = signalCount,
				pSignalSemaphoreInfos = signals,
			};

			Check( Vk.vkQueueSubmit2( queue.Handle, 1, &submit, 0 ), "vkQueueSubmit2" );
			Interlocked.Increment( ref submissions );
			queue.Pending.Clear();
			Volatile.Write( ref queue.Flushed, value );
			return list.Submitted = new SyncPoint( list.Queue, value );
		}
	}

	/// <summary>
	/// Submit a recorded command list to go to the GPU with the next submission to its queue, in the same
	/// <c>vkQueueSubmit</c>: one submission of many lists runs without the drain and cache flush between submissions. It's
	/// ordered after everything submitted before it like any other. It reaches the GPU with the next <see cref="Submit"/>
	/// or <see cref="Flush()"/>, or when anything waits for it (<see cref="Wait"/>, <see cref="WaitIdle"/>,
	/// <see cref="BeginFrame"/>, a submission on another queue). Returns the point reached when it's done.
	/// </summary>
	public SyncPoint SubmitLater( CommandList list )
	{
		list.End();

		var queue = QueueFor( list.Queue );
		lock ( queue.SubmitLock )
		{
			PairScopes( queue, list );
			var value = queue.Next();
			Remember( queue, value, list, -1 );
			queue.Pending.Add( list.Handle );
			queue.Suspended = list.EndsSuspended ? list : null;
			return list.Submitted = new SyncPoint( list.Queue, value );
		}
	}

	/// <summary>
	/// Before <paramref name="list"/> goes after the pending lists (with <see cref="DeviceQueue.SubmitLock"/> held): a
	/// rendering scope the last one left suspended that this one doesn't resume on the same targets is ended, and a scope this
	/// one resumes that nothing left suspended is begun, each by a list of its own between them.
	/// </summary>
	void PairScopes( DeviceQueue queue, CommandList list )
	{
		var open = queue.Suspended;
		queue.Suspended = null;
		if ( open is not null && !(list.StartsResumed && open.SuspendedTargets.Matches( list.ResumedTargets )) )
		{
			queue.Pending.Add( ScopeList( queue, open.SuspendedTargets, Vulkan.Vk.RenderingResuming ).Handle );
			open = null;
		}

		if ( list.StartsResumed && open is null ) queue.Pending.Add( ScopeList( queue, list.ResumedTargets, Vulkan.Vk.RenderingSuspending ).Handle );
	}

	/// <summary>A list holding one empty rendering scope on <paramref name="targets"/>, to pair suspended and resumed scopes.</summary>
	CommandList ScopeList( DeviceQueue queue, RenderingTargets targets, uint flags )
	{
		var list = BeginCommandList( queue.Type, "Rendering scope pairing" );
		list.RecordScope( targets, flags );
		list.End();
		Interlocked.Increment( ref scopePairings );
		lock ( recentSubmissions ) Tally( list );
		return list;
	}

	/// <summary>
	/// Lists added between submitted ones to end a rendering scope left suspended that the next didn't resume, or to begin one
	/// it resumed that nothing left suspended (<see cref="RenderingFlags"/>). For profiling and tests: each costs an empty
	/// render pass on the GPU.
	/// </summary>
	public long ScopePairings => Interlocked.Read( ref scopePairings );

	long scopePairings;

	/// <summary>Submit every list <see cref="SubmitLater"/> is holding, on every queue.</summary>
	public void Flush()
	{
		Flush( graphicsQueue );
		if ( computeQueue != graphicsQueue ) Flush( computeQueue );
	}

	void Flush( DeviceQueue queue )
	{
		lock ( queue.SubmitLock ) FlushLocked( queue );
	}

	void FlushLocked( DeviceQueue queue )
	{
		if ( queue.Pending.Count == 0 ) return;

		// Nothing reaches a lost device: what was held is dropped, its lists recycled with their frame
		if ( IsLost )
		{
			queue.Pending.Clear();
			queue.Suspended = null;
			Volatile.Write( ref queue.Flushed, queue.Submitted );
			return;
		}

		// A scope the last list left suspended ends in this submission
		if ( queue.Suspended is { } open )
		{
			queue.Pending.Add( ScopeList( queue, open.SuspendedTargets, Vulkan.Vk.RenderingResuming ).Handle );
			queue.Suspended = null;
		}

		// The lists took values up to the queue's last; signalling it reaches all of them
		var value = queue.Submitted;
		var signal = new VkSemaphoreSubmitInfo { sType = VkStructureType.SemaphoreSubmitInfo, semaphore = queue.Timeline, value = value, stageMask = Vulkan.Vk.Stage2AllCommands };
		var count = queue.Pending.Count;
		var commandBuffers = stackalloc VkCommandBufferSubmitInfo[count];
		for ( int i = 0; i < count; i++ ) commandBuffers[i] = new VkCommandBufferSubmitInfo { sType = VkStructureType.CommandBufferSubmitInfo, commandBuffer = queue.Pending[i] };

		var submit = new VkSubmitInfo2
		{
			sType = VkStructureType.SubmitInfo2,
			commandBufferInfoCount = (uint)count,
			pCommandBufferInfos = commandBuffers,
			signalSemaphoreInfoCount = 1,
			pSignalSemaphoreInfos = &signal,
		};

		Check( Vk.vkQueueSubmit2( queue.Handle, 1, &submit, 0 ), "vkQueueSubmit2" );
		Interlocked.Increment( ref submissions );
		queue.Pending.Clear();
		Volatile.Write( ref queue.Flushed, value );
	}

	// The last lists submitted, for what was in flight when the device is lost (DescribeRecentSubmissions)
	readonly (QueueType Queue, ulong Value, string Name, int HeldBefore, long Ticks, int Markers)[] recentSubmissions = new (QueueType, ulong, string, int, long, int)[1024];
	int recentNext;

	/// <summary>The latest submissions a loss report lists, after those around the first not finished.</summary>
	const int RecentSubmissionsShown = 48;

	/// <summary>
	/// Note something that isn't a submission among them (an acquire, a present, a frame starting), for loss reports: it's
	/// listed after the submissions made before it. <paramref name="what"/> is a constant, so nothing is made each frame.
	/// </summary>
	internal void RememberEvent( string what, long detail )
	{
		lock ( recentSubmissions )
		{
			recentSubmissions[recentNext] = (QueueType.Graphics, graphicsQueue.Submitted, what, -2, Stopwatch.GetTimestamp(), (int)detail);
			recentNext = (recentNext + 1) % recentSubmissions.Length;
		}
	}

	void Remember( DeviceQueue queue, ulong value, CommandList list, int heldBefore )
	{
		lock ( recentSubmissions )
		{
			recentSubmissions[recentNext] = (queue.Type, value, list.Name, heldBefore, Stopwatch.GetTimestamp(), list.MarkerSlot);
			recentNext = (recentNext + 1) % recentSubmissions.Length;
			Tally( list );
		}
	}

	/// <summary>
	/// What the lists submitted so far recorded, added up (<see cref="CommandList.Counts"/>): lists, rendering scopes begun
	/// (resumed ones included), barrier calls, those that wait for every stage, and resumed scopes. For profiling: take the
	/// difference over a run of frames.
	/// </summary>
	public (long Lists, long Scopes, long Barriers, long FullBarriers, long Resumed) Recorded
	{
		get { lock ( recentSubmissions ) return recorded; }
	}

	(long Lists, long Scopes, long Barriers, long FullBarriers, long Resumed) recorded;

	void Tally( CommandList list )
	{
		var c = list.Counts;
		recorded = (recorded.Lists + 1, recorded.Scopes + c.Scopes, recorded.Barriers + c.Barriers, recorded.FullBarriers + c.FullBarriers, recorded.Resumed + c.Resumed);
	}

	/// <summary>
	/// For after the device is lost: the driver's fault report (only valid then), the process's memory use
	/// (<see cref="DescribeMemory"/>), each queue's timeline, then the last lists submitted, oldest first, each with its
	/// queue, timeline value, when it was submitted (a list held for one submission is "held"; one submitted directly says
	/// how many held lists went with it), whether the timeline had passed it when last read before the loss, and with
	/// <see cref="DeviceOptions.ProgressMarkers"/> how far the GPU got in it. A timeline read after the loss can't be
	/// trusted: drivers may signal everything pending.
	/// </summary>
	public string DescribeRecentSubmissions()
	{
		var text = new System.Text.StringBuilder();
		DescribeFault( text );
		text.Append( $"memory: {DescribeMemory()}; " );
		var seen = new Dictionary<QueueType, ulong>();
		foreach ( var queue in (DeviceQueue[])[graphicsQueue, computeQueue] )
		{
			seen[queue.Type] = queue.CompletedSeen;
			string now;
			try { now = IsLost ? "unknown" : queue.Completed.ToString(); } catch ( GpuException ) { now = "unknown"; }
			text.Append( $"{queue.Type} queue: submitted {queue.Submitted}, flushed {Volatile.Read( ref queue.Flushed )}, completed {seen[queue.Type]} when last read, {now} read now; " );
			if ( computeQueue == graphicsQueue ) break;
		}

		lock ( recentSubmissions )
		{
			// The submissions around the first the GPU hadn't finished (where a hang or fault usually is), any the GPU got part
			// way through (the list it stopped in, when finished lists not yet read as done come before it), then the latest
			var now = Stopwatch.GetTimestamp();
			var skipped = false;
			for ( int i = 0; i < recentSubmissions.Length; i++ )
			{
				var (queue, value, name, held, ticks, markers) = recentSubmissions[(recentNext + i) % recentSubmissions.Length];
				if ( name is null && value == 0 ) continue;
				var completed = seen.GetValueOrDefault( queue );
				if ( i < recentSubmissions.Length - RecentSubmissionsShown && (value + 4 <= completed || value > completed + 16) && !MarkersPartway( markers ) )
				{
					if ( !skipped ) text.Append( "\n  ..." );
					skipped = true;
					continue;
				}

				skipped = false;
				if ( held == -2 )
				{
					text.Append( $"\n  {name} {markers}, after {queue} {value} ({Stopwatch.GetElapsedTime( ticks, now ).TotalMilliseconds:F0} ms ago)" );
					continue;
				}

				var done = seen.GetValueOrDefault( queue ) >= value ? "done" : "not seen done";
				text.Append( $"\n  {queue} {value} {name} ({(held < 0 ? "held" : $"with {held} held")}, {Stopwatch.GetElapsedTime( ticks, now ).TotalMilliseconds:F0} ms ago) {done}{DescribeMarkers( markers )}" );
			}
		}

		return text.ToString();
	}

	/// <summary>The driver's VK_EXT_device_fault report after the device is lost: its description and the faulting addresses.</summary>
	void DescribeFault( System.Text.StringBuilder text )
	{
		if ( !faultReports || Vk.vkGetDeviceFaultInfoEXT == null )
		{
			text.Append( "no device fault report; " );
			return;
		}

		// The driver only has a report once a call has returned VK_ERROR_DEVICE_LOST. One that signalled everything pending
		// has lost the device without saying so yet: waiting for the device then returns at once and says it
		var waited = VkResult.Success;
		if ( driverLost && !lostReturned && (waited = Vk.vkDeviceWaitIdle( Vk.Device )) == VkResult.ErrorDeviceLost ) lostReturned = true;
		if ( !lostReturned )
		{
			text.Append( $"no device fault report (the driver hasn't said the device is lost; waiting for it returned {waited}); " );
			return;
		}

		var counts = new VkDeviceFaultCountsEXT { sType = VkStructureType.DeviceFaultCountsExt };
		var result = Vk.vkGetDeviceFaultInfoEXT( Vk.Device, &counts, null );
		if ( result < 0 )
		{
			text.Append( $"device fault counts failed ({result}); " );
			return;
		}

		var addresses = new VkDeviceFaultAddressInfoEXT[Math.Max( counts.addressInfoCount, 1u )];
		var vendors = new VkDeviceFaultVendorInfoEXT[Math.Max( counts.vendorInfoCount, 1u )];
		counts.vendorBinarySize = 0;
		var info = new VkDeviceFaultInfoEXT { sType = VkStructureType.DeviceFaultInfoExt };
		fixed ( VkDeviceFaultAddressInfoEXT* a = addresses )
		fixed ( VkDeviceFaultVendorInfoEXT* v = vendors )
		{
			info.pAddressInfos = a;
			info.pVendorInfos = v;
			result = Vk.vkGetDeviceFaultInfoEXT( Vk.Device, &counts, &info );
		}

		if ( result < 0 )
		{
			text.Append( $"device fault info failed ({result}); " );
			return;
		}

		ReadOnlySpan<string> kinds = ["none", "read invalid", "write invalid", "execute invalid", "ip unknown frame", "ip invalid frame", "ip fault frame"];
		text.Append( $"device fault: \"{Marshal.PtrToStringUTF8( (nint)info.description )}\", {counts.addressInfoCount} addresses, {counts.vendorInfoCount} vendor records" );
		for ( int i = 0; i < counts.addressInfoCount; i++ )
		{
			var address = addresses[i];
			var kind = address.addressType >= 0 && address.addressType < kinds.Length ? kinds[address.addressType] : address.addressType.ToString();
			text.Append( $"\n  fault address {kind} 0x{address.reportedAddress:X} (precision 0x{address.addressPrecision:X})" );
		}

		for ( int i = 0; i < counts.vendorInfoCount; i++ )
		{
			fixed ( VkDeviceFaultVendorInfoEXT* v = &vendors[i] )
				text.Append( $"\n  vendor \"{Marshal.PtrToStringUTF8( (nint)v->description )}\" code 0x{v->vendorFaultCode:X} data 0x{v->vendorFaultData:X}" );
		}

		text.Append( "\n" );
	}

	/// <summary>Submissions made to the GPU (<c>vkQueueSubmit2</c> calls) so far, on every queue. For profiling and tests.</summary>
	public long Submissions => Interlocked.Read( ref submissions );

	long submissions;

	/// <summary>Wait on the CPU until the GPU reaches <paramref name="point"/>.</summary>
	public void Wait( SyncPoint point )
	{
		var queue = QueueFor( point.Queue );
		if ( point.Value > Volatile.Read( ref queue.Flushed ) ) Flush( queue );
		queue.Wait( point.Value );
	}

	/// <summary>Whether the GPU has reached <paramref name="point"/>.</summary>
	public bool IsComplete( SyncPoint point ) => QueueFor( point.Queue ).Completed >= point.Value;

	/// <summary>The last point submitted to a queue.</summary>
	public SyncPoint LastSubmitted( QueueType queue ) => new( queue, QueueFor( queue ).Submitted );

	internal void Register( SwapChain swapChain )
	{
		lock ( swapChains ) swapChains.Add( swapChain );
	}

	internal void Unregister( SwapChain swapChain )
	{
		lock ( swapChains ) swapChains.Remove( swapChain );
	}

	//
	// Copies outside any frame, which wait
	//

	// A thread's deferred uploads (DeferUploads): its writes on this device go into one list, not a submission each
	[ThreadStatic] static Device deferringDevice;
	[ThreadStatic] static CommandList deferredList;

	/// <summary>
	/// Until the scope is disposed, this thread's <see cref="WriteBuffer"/> and <see cref="WriteTexture"/> on this device are
	/// recorded into one list rather than each submitted and waited for. Disposing it ends the list with a barrier that makes
	/// the writes visible to everything after it and sends it with the next submission (<see cref="SubmitLater"/>), so work
	/// submitted after it reads them. For loading many resources in a frame without stalling on each. A scope inside
	/// another on the same thread shares the outer one.
	/// </summary>
	public UploadScope DeferUploads()
	{
		if ( deferringDevice is not null ) return default;
		deferringDevice = this;
		return new UploadScope( this );
	}

	/// <summary>See <see cref="DeferUploads"/>.</summary>
	public readonly struct UploadScope : IDisposable
	{
		readonly Device device;

		internal UploadScope( Device device ) => this.device = device;

		/// <summary>Send what the scope recorded with the next submission.</summary>
		public void Dispose()
		{
			if ( device is null ) return;
			var list = deferredList;
			deferringDevice = null;
			deferredList = null;
			if ( list is null ) return;

			list.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead );
			device.SubmitLater( list );
		}
	}

	// The list this thread's writes go into while it defers them, or null
	CommandList DeferredUploads() => deferringDevice == this ? deferredList ??= BeginCommandList( QueueType.Graphics, "Deferred uploads" ) : null;

	/// <summary>
	/// The list this thread's writes go into in a <see cref="DeferUploads"/> scope (begun on first use), or null outside one:
	/// for other loading work (a new texture's clear) to go with them rather than in a submission of its own. Transfers only:
	/// the scope ends it with a barrier from transfers to everything after.
	/// </summary>
	public CommandList DeferredList => DeferredUploads();

	long deferredBytes;

	/// <summary>Bytes written in <see cref="DeferUploads"/> scopes so far, for capping how much a frame uploads.</summary>
	public long DeferredUploadBytes => Interlocked.Read( ref deferredBytes );

	/// <summary>
	/// Copy <paramref name="data"/> into a buffer at <paramref name="offset"/> now, and wait for it (or, in a
	/// <see cref="DeferUploads"/> scope, with the scope's list). For loading; per-frame data goes through
	/// <see cref="CommandList.UpdateBuffer"/>.
	/// </summary>
	public void WriteBuffer( BufferHandle buffer, ReadOnlySpan<byte> data, long offset = 0 )
	{
		ref var record = ref Get( buffer );
		if ( record.Memory.Mapped != 0 )
		{
			data.CopyTo( new Span<byte>( (void*)(record.Memory.Mapped + (nint)offset), data.Length ) );
			return;
		}

		if ( DeferredUploads() is { } deferred )
		{
			deferred.UpdateBuffer( buffer, data, offset );
			Interlocked.Add( ref deferredBytes, data.Length );
			return;
		}

		var list = BeginCommandList( QueueType.Graphics, "WriteBuffer" );
		list.UpdateBuffer( buffer, data, offset );
		SubmitAndRecycle( list );
	}

	/// <summary>
	/// Submit a list, wait for it, and recycle it at once rather than with its frame: a load that writes many textures
	/// before the first frame would otherwise keep every write's upload memory until then.
	/// </summary>
	void SubmitAndRecycle( CommandList list )
	{
		Wait( Submit( list ) );

		bool owned;
		lock ( frame.Lists ) owned = frame.Lists.Remove( list );
		if ( !owned ) return;

		list.Recycle();
		freeLists[(int)list.Queue].Add( list );
	}

	/// <summary>
	/// Copy <paramref name="data"/> into a buffer now, and wait for it.
	/// </summary>
	public void WriteBuffer<T>( BufferHandle buffer, ReadOnlySpan<T> data, long offset = 0 ) where T : unmanaged => WriteBuffer( buffer, MemoryMarshal.AsBytes( data ), offset );

	/// <summary>
	/// Fill one mip of one layer of a texture now, from tightly packed rows, and wait (or, in a <see cref="DeferUploads"/>
	/// scope, with the scope's list). It's left in
	/// <see cref="ImageLayout.ShaderRead"/>, after <paramref name="from"/> (Undefined for a new texture, which is also what
	/// discards the rest of its contents).
	/// </summary>
	public void WriteTexture( TextureHandle texture, ReadOnlySpan<byte> data, int mip = 0, int layer = 0, ImageLayout from = ImageLayout.Undefined )
	{
		var deferred = DeferredUploads();
		var list = deferred ?? BeginCommandList( QueueType.Graphics, "WriteTexture" );
		var info = InfoOf( texture );
		list.Barrier( new TextureBarrier
		{
			Texture = texture,
			OldLayout = from,
			NewLayout = ImageLayout.CopyDestination,
			SourceStage = PipelineStage.AllCommands,
			SourceAccess = MemoryAccess.MemoryWrite,
			DestinationStage = PipelineStage.Transfer,
			DestinationAccess = MemoryAccess.TransferWrite,
			BaseMip = mip,
			MipCount = 1,
			BaseLayer = layer,
			LayerCount = 1,
		} );

		list.UpdateTexture( texture, data, mip, layer, Math.Max( info.Width >> mip, 1 ), Math.Max( info.Height >> mip, 1 ) );
		list.Barrier( new TextureBarrier
		{
			Texture = texture,
			OldLayout = ImageLayout.CopyDestination,
			NewLayout = ImageLayout.ShaderRead,
			SourceStage = PipelineStage.Transfer,
			SourceAccess = MemoryAccess.TransferWrite,
			DestinationStage = PipelineStage.AllCommands,
			DestinationAccess = MemoryAccess.ShaderRead,
			BaseMip = mip,
			MipCount = 1,
			BaseLayer = layer,
			LayerCount = 1,
		} );

		if ( deferred is null ) SubmitAndRecycle( list );
		else Interlocked.Add( ref deferredBytes, data.Length );
	}

	/// <summary>
	/// Point the bindless table's vertex cache (binding 49) or pipeline texture indices (binding 60) at a storage buffer.
	/// Only while no submitted work reads them.
	/// </summary>
	public void SetBindlessBuffer( int binding, BufferHandle buffer )
	{
		if ( binding != ShaderAbi.BindlessVertexCache && binding != ShaderAbi.BindlessPipelineTextures ) throw new ArgumentException( $"Binding {binding} isn't a bindless buffer" );
		ref var record = ref Get( buffer );
		Bindless.SetBuffer( binding, record.Buffer, (ulong)record.Size );
	}

	/// <summary>
	/// A storage buffer's slot in the bindless raw buffer array (<c>g_bindless_VertexBuffers</c>, binding 63), added the
	/// first time it's asked for and freed with the buffer. Ask before recording work that reads it; without
	/// update-after-bind for storage buffers, only while no submitted work uses the table.
	/// </summary>
	public int BindlessIndex( BufferHandle buffer )
	{
		ref var record = ref Get( buffer );
		if ( record.BindlessSlot > 0 ) return record.BindlessSlot - 1;
		if ( (record.Usage & BufferUsage.Storage) == 0 ) throw new ArgumentException( $"{record.Name} isn't a storage buffer" );

		lock ( Bindless )
		{
			if ( record.BindlessSlot > 0 ) return record.BindlessSlot - 1;
			var slot = Bindless.AddBuffer( record.Buffer, 0, (ulong)record.Size );
			record.BindlessSlot = slot + 1;
			return slot;
		}
	}

	void DestroyFrames()
	{
		if ( frames is null ) return;

		foreach ( var f in frames )
		{
			foreach ( var list in f.Lists ) list.Destroy();
			f.Lists.Clear();
		}

		foreach ( var bag in freeLists )
		{
			while ( bag.TryTake( out var list ) ) list.Destroy();
		}

		while ( FreePages.TryTake( out var page ) ) Destroy( page.Buffer );

		// Pools being made ahead first
		while ( Volatile.Read( ref makingDescriptorPools ) != 0 ) Thread.Sleep( 1 );
		while ( freeDescriptorPools.TryTake( out var pool ) ) Vk.vkDestroyDescriptorPool( Vk.Device, pool, null );
	}
}

/// <summary>
/// A chunk of always-mapped upload memory a command list sub-allocates per-frame data from.
/// </summary>
internal sealed class UploadPage
{
	public BufferHandle Buffer;
	public ulong VkBuffer;
	public nint Mapped;
	public long Size;
	public long Used;
}
