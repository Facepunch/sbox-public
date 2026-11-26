using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using GpuCommandList = Borealis.Gpu.CommandList;

namespace Sandbox;

/// <summary>
/// Engine buffers on Borealis.Gpu: copies, fills, asynchronous readback, and the hidden counters of append and counter
/// buffers, each as native's render context does them. In a block they're recorded in order with its draws; outside one,
/// into the list of uploads submitted before the next block.
/// </summary>
internal sealed partial class BorealisGpuRenderBackend
{
	//
	// Copies and fills
	//

	public override void CopyBuffer( GpuBuffer source, GpuBuffer destination, uint sourceOffset, uint destinationOffset, uint size )
	{
		if ( size == 0 ) return;
		FromMain();
		var from = BufferOf( source );
		var to = BufferOf( destination );
		if ( list is not null )
		{
			context.CopyBuffer( from, sourceOffset, to, destinationOffset, size );
			return;
		}

		TransferList( to ).CopyBuffer( from, sourceOffset, to, destinationOffset, size );
	}

	/// <summary>Every 32-bit value of the buffer's elements, as native fills the whole buffer.</summary>
	public override void FillBuffer( GpuBuffer buffer, uint value )
	{
		var size = (long)buffer.ElementCount * buffer.ElementSize & ~3L;
		if ( size == 0 ) return;
		FromMain();
		Fill( BufferOf( buffer ), value, 0, size );
	}

	void Fill( BufferHandle buffer, uint value, long offset, long size )
	{
		FromMain();
		if ( list is not null )
		{
			context.FillBuffer( buffer, value, offset, size );
			return;
		}

		TransferList( buffer ).FillBuffer( buffer, value, offset, size );
	}

	/// <summary>
	/// The list of uploads, with everything recorded in it so far finished before what's recorded next: a copy or fill outside a
	/// block, which may read or write what an upload before it wrote. It writes <paramref name="destination"/>, so an upload
	/// to that buffer after it waits for it (<see cref="SetBufferData"/>): a buffer cleared and then filled in part, as a
	/// clutter batch's spheres are, keeps what was uploaded rather than the clear.
	/// </summary>
	GpuCommandList TransferList( BufferHandle destination )
	{
		var uploads = UploadList();
		uploads.Barrier( PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite );
		written.Clear();
		written.Add( destination );
		return uploads;
	}

	//
	// Hidden counters
	//

	readonly ConditionalWeakTable<GpuBuffer, Backing<BufferHandle>> counters = new();

	/// <summary>
	/// A buffer's hidden counter: 32 bits in a buffer of their own, zero when made, as native keeps one beside an append or
	/// structured buffer's elements. Made on first use, or when an append buffer is first bound.
	/// </summary>
	BufferHandle CounterOf( GpuBuffer buffer )
	{
		if ( counters.TryGetValue( buffer, out var backing ) ) return backing.Handle;

		var handle = gpu.CreateBuffer( new BufferInfo { Size = 16, Usage = BufferUsage.Storage | BufferUsage.CopySource | BufferUsage.CopyDestination, Name = "Engine buffer counter" } );
		gpu.WriteBuffer<uint>( handle, [0, 0, 0, 0] );
		counters.Add( buffer, new Backing<BufferHandle>( handle ) { Collected = collectedBuffers } );
		backingsVersion++;
		ownedBuffers.Add( handle );
		return handle;
	}

	/// <summary>The counter to bind beside a buffer: an append buffer's always, another's once it has one.</summary>
	BufferHandle? BoundCounter( GpuBuffer buffer )
	{
		if ( counters.TryGetValue( buffer, out var backing ) ) return backing.Handle;
		return buffer.Usage.HasFlag( GpuBuffer.UsageFlags.Append ) ? CounterOf( buffer ) : null;
	}


	/// <summary>Set a buffer and, when it has one, its hidden counter (<see cref="ShaderResource.CounterName"/>) under <paramref name="binding"/>'s name.</summary>
	void LowerBuffer( TokenBinding binding, GpuBuffer buffer, Borealis.Backend.ShaderAttributes lowered )
	{
		lowered.SetById( binding.Id, BufferOf( buffer ) );
		if ( BoundCounter( buffer ) is not { } counter ) return;
		if ( binding.CounterId < 0 ) binding.CounterId = AttributeIds.Of( ShaderResource.CounterName( binding.Name ) );
		lowered.SetById( binding.CounterId, counter );
	}

	void ForgetCounter( GpuBuffer buffer )
	{
		if ( !counters.TryGetValue( buffer, out var backing ) ) return;
		counters.Remove( buffer );
		backingsVersion++;
		backing.Collected = null;
		ownedBuffers.Remove( backing.Handle );
		gpu.Destroy( backing.Handle );
	}

	public override void SetStructureCount( GpuBuffer buffer, uint value )
	{
		FromMain();
		Fill( CounterOf( buffer ), value, 0, sizeof( uint ) );
	}

	public override void CopyStructureCount( GpuBuffer source, GpuBuffer destination, uint destinationOffset )
	{
		FromMain();
		var counter = CounterOf( source );
		var to = BufferOf( destination );
		if ( list is not null )
		{
			context.CopyBuffer( counter, 0, to, destinationOffset, sizeof( uint ) );
			return;
		}

		TransferList( to ).CopyBuffer( counter, 0, to, destinationOffset, sizeof( uint ) );
	}

	//
	// Readback
	//

	sealed class Readback
	{
		public BufferHandle Buffer;
		public int Size;
		public AsyncGPUReadback.BufferReadDelegate Callback;

		// The submission it's in, once known; a lent block's isn't until its lender submits, after the device frame it was in
		public SyncPoint Done;
		public ulong Frame;

		// Where the buffer goes once its callback is done with it
		public ConcurrentQueue<BufferHandle> ReturnTo;
		public nint Data;

		public unsafe void Complete()
		{
			try
			{
				Callback( new Span<byte>( (void*)Data, Size ) );
			}
			finally
			{
				ReturnTo.Enqueue( Buffer );
			}
		}
	}

	// Recorded in the block being recorded, or in the uploads, and not yet submitted
	readonly List<Readback> blockReadbacks = new();
	readonly List<Readback> uploadReadbacks = new();

	// Submitted (or lent), waiting for the GPU
	readonly List<Readback> pendingReadbacks = new();

	// Spare buffer readbacks, by power-of-two capacity. Larger reads cannot round up without exceeding Map's span length.
	readonly Dictionary<int, ConcurrentQueue<BufferHandle>> bufferReadPools = new();
	readonly ConcurrentQueue<BufferHandle> oversizedReadBuffers = new();

	/// <summary>
	/// Copy part of a buffer back and hand it to <paramref name="callback"/> on a worker once the GPU has run the copy, as
	/// native's asynchronous readback does; the bytes are only valid during the callback.
	/// </summary>
	public override void ReadBufferAsync( GpuBuffer buffer, AsyncGPUReadback.BufferReadDelegate callback, int offset, int size )
	{
		if ( size <= 0 ) return;
		FromMain();
		var source = BufferOf( buffer );
		var capacity = size;
		var pool = oversizedReadBuffers;
		BufferHandle free = default;
		if ( size <= (1 << 30) )
		{
			capacity = (int)BitOperations.RoundUpToPowerOf2( (uint)size );
			if ( !bufferReadPools.TryGetValue( capacity, out pool ) ) bufferReadPools.Add( capacity, pool = new() );
			pool.TryDequeue( out free );
		}

		var readback = new Readback
		{
			Buffer = free.IsValid ? free : gpu.CreateBuffer( new BufferInfo { Size = capacity, Memory = MemoryLocation.Readback, Name = "Buffer readback" } ),
			Size = size,
			Callback = callback,
			Frame = gpu.FrameNumber,
			ReturnTo = pool,
		};

		if ( list is not null )
		{
			context.CopyToReadback( source, offset, readback.Buffer, size );
			blockReadbacks.Add( readback );
			return;
		}

		var uploads = TransferList( readback.Buffer );
		uploads.CopyBuffer( source, offset, readback.Buffer, 0, size );
		uploads.Barrier( new BufferBarrier { Buffer = readback.Buffer, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		uploadReadbacks.Add( readback );
	}

	/// <summary>The block's readbacks are in <paramref name="point"/>'s submission, or a lender's (default) yet to come.</summary>
	void SubmittedReadbacks( List<Readback> submitted, SyncPoint point )
	{
		foreach ( var readback in submitted )
		{
			readback.Done = point;
			pendingReadbacks.Add( readback );
		}

		submitted.Clear();
	}

	/// <summary>
	/// Hand back the readbacks the GPU has finished, each to its callback on a worker, and free oversized buffers that
	/// cannot be pooled.
	/// </summary>
	internal unsafe void PollReadbacks()
	{
		while ( oversizedReadBuffers.TryDequeue( out var done ) ) gpu.Destroy( done );
		RecycleTextureReadBuffers();
		if ( pendingReadbacks.Count == 0 ) return;

		for ( int i = 0; i < pendingReadbacks.Count; i++ )
		{
			var readback = pendingReadbacks[i];

			// A lent block's list was submitted by its lender before the device's next frame
			if ( !readback.Done.IsValid )
			{
				if ( gpu.FrameNumber <= readback.Frame ) continue;
				readback.Done = gpu.LastSubmitted( QueueType.Graphics );
			}

			if ( !gpu.IsComplete( readback.Done ) ) continue;

			pendingReadbacks.RemoveAt( i-- );
			readback.Data = (nint)Unsafe.AsPointer( ref gpu.Map( readback.Buffer )[0] );
			Task.Factory.StartNew( static state => ((Readback)state).Complete(), readback, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default );
		}
	}

	/// <summary>Drop every readback not handed back, and free their buffers.</summary>
	void DestroyReadbacks()
	{
		foreach ( var readback in pendingReadbacks.Concat( blockReadbacks ).Concat( uploadReadbacks ) ) gpu.Destroy( readback.Buffer );
		pendingReadbacks.Clear();
		blockReadbacks.Clear();
		uploadReadbacks.Clear();
		foreach ( var pool in bufferReadPools.Values )
		{
			while ( pool.TryDequeue( out var free ) ) gpu.Destroy( free );
		}
		bufferReadPools.Clear();
		while ( oversizedReadBuffers.TryDequeue( out var done ) ) gpu.Destroy( done );
		DestroyTextureReadBuffers();
		if ( pixelReadback.IsValid ) gpu.Destroy( pixelReadback );
		pixelReadback = default;
		pixelReadbackSize = 0;
	}
}
