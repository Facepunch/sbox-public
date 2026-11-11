using Basalt.Gpu.Vulkan;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

namespace Basalt.Gpu;

/// <summary>
/// A Vulkan queue and the timeline semaphore its submissions signal, one value each.
/// </summary>
internal sealed unsafe class DeviceQueue : IDisposable
{
	readonly VkDeviceApi vk;
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

	/// <summary>The value the last submission signals.</summary>
	public ulong Submitted => Volatile.Read( ref submitted );

	public DeviceQueue( Device device, VkDeviceApi vk, uint family, QueueType type )
	{
		this.vk = vk;
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

	/// <summary>The value the GPU has reached.</summary>
	public ulong Completed
	{
		get
		{
			if ( Volatile.Read( ref completed ) >= Submitted ) return completed;

			ulong value;
			vk.vkGetSemaphoreCounterValue( vk.Device, Timeline, &value ).Check( "vkGetSemaphoreCounterValue" );
			if ( value > Volatile.Read( ref completed ) ) Volatile.Write( ref completed, value );
			return value;
		}
	}

	/// <summary>Take the next value for a submission. Call with <see cref="SubmitLock"/> held.</summary>
	public ulong Next() => ++submitted;

	public void Wait( ulong value )
	{
		if ( value == 0 || Completed >= value ) return;

		var semaphore = Timeline;
		var info = new VkSemaphoreWaitInfo { sType = VkStructureType.SemaphoreWaitInfo, semaphoreCount = 1, pSemaphores = &semaphore, pValues = &value };
		vk.vkWaitSemaphores( vk.Device, &info, ulong.MaxValue ).Check( "vkWaitSemaphores" );
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
	/// then recycle its command lists, upload space and descriptor sets, and free what's been destroyed since.
	/// </summary>
	public void BeginFrame()
	{
		Flush();
		frame.Graphics = graphicsQueue.Submitted;
		frame.Compute = computeQueue.Submitted;

		FrameNumber++;
		frame = frames[FrameSlot];

		graphicsQueue.Wait( frame.Graphics );
		computeQueue.Wait( frame.Compute );

		List<CommandList> recycle;
		lock ( frame.Lists )
		{
			recycle = [.. frame.Lists];
			frame.Lists.Clear();
		}

		foreach ( var list in recycle )
		{
			list.Recycle();
			freeLists[(int)list.Queue].Add( list );
		}

		CollectGarbage();
	}

	/// <summary>
	/// A command list to record into, from the current frame. Submit it with <see cref="Submit"/> in the same frame; it's
	/// recycled once that frame's work has finished.
	/// </summary>
	public CommandList BeginCommandList( QueueType queue = QueueType.Graphics, string name = null )
	{
		if ( queue == QueueType.Compute && !Capabilities.AsyncCompute ) queue = QueueType.Graphics;

		if ( !freeLists[(int)queue].TryTake( out var list ) ) list = new CommandList( this, QueueFor( queue ), queue );

		lock ( frame.Lists ) frame.Lists.Add( list );
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

			var value = queue.Next();
			signals[0] = new VkSemaphoreSubmitInfo { sType = VkStructureType.SemaphoreSubmitInfo, semaphore = queue.Timeline, value = value, stageMask = Vulkan.Vk.Stage2AllCommands };

			var count = queue.Pending.Count + 1;
			var commandBuffers = stackalloc VkCommandBufferSubmitInfo[count];
			for ( int i = 0; i < queue.Pending.Count; i++ ) commandBuffers[i] = new VkCommandBufferSubmitInfo { sType = VkStructureType.CommandBufferSubmitInfo, commandBuffer = queue.Pending[i] };
			commandBuffers[count - 1] = new VkCommandBufferSubmitInfo { sType = VkStructureType.CommandBufferSubmitInfo, commandBuffer = list.Handle };

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

			Vk.vkQueueSubmit2( queue.Handle, 1, &submit, 0 ).Check( "vkQueueSubmit2" );
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
			var value = queue.Next();
			queue.Pending.Add( list.Handle );
			return list.Submitted = new SyncPoint( list.Queue, value );
		}
	}

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

		Vk.vkQueueSubmit2( queue.Handle, 1, &submit, 0 ).Check( "vkQueueSubmit2" );
		Interlocked.Increment( ref submissions );
		queue.Pending.Clear();
		Volatile.Write( ref queue.Flushed, value );
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

	/// <summary>
	/// Copy <paramref name="data"/> into a buffer at <paramref name="offset"/> now, and wait for it. For loading; per-frame
	/// data goes through <see cref="CommandList.UpdateBuffer"/>.
	/// </summary>
	public void WriteBuffer( BufferHandle buffer, ReadOnlySpan<byte> data, long offset = 0 )
	{
		ref var record = ref Get( buffer );
		if ( record.Memory.Mapped != 0 )
		{
			data.CopyTo( new Span<byte>( (void*)(record.Memory.Mapped + (nint)offset), data.Length ) );
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
	/// Fill one mip of one layer of a texture now, from tightly packed rows, and wait. It's left in
	/// <see cref="ImageLayout.ShaderRead"/>, after <paramref name="from"/> (Undefined for a new texture, which is also what
	/// discards the rest of its contents).
	/// </summary>
	public void WriteTexture( TextureHandle texture, ReadOnlySpan<byte> data, int mip = 0, int layer = 0, ImageLayout from = ImageLayout.Undefined )
	{
		var list = BeginCommandList( QueueType.Graphics, "WriteTexture" );
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

		SubmitAndRecycle( list );
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
