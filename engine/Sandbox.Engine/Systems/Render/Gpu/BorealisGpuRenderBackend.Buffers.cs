using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Runtime.CompilerServices;
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
		var from = BufferOf( source );
		var to = BufferOf( destination );
		if ( list is not null )
		{
			context.CopyBuffer( from, sourceOffset, to, destinationOffset, size );
			return;
		}

		TransferList().CopyBuffer( from, sourceOffset, to, destinationOffset, size );
	}

	/// <summary>Every 32-bit value of the buffer's elements, as native fills the whole buffer.</summary>
	public override void FillBuffer( GpuBuffer buffer, uint value )
	{
		var size = (long)buffer.ElementCount * buffer.ElementSize & ~3L;
		if ( size == 0 ) return;
		Fill( BufferOf( buffer ), value, 0, size );
	}

	void Fill( BufferHandle buffer, uint value, long offset, long size )
	{
		if ( list is not null )
		{
			context.FillBuffer( buffer, value, offset, size );
			return;
		}

		TransferList().FillBuffer( buffer, value, offset, size );
	}

	/// <summary>
	/// The list of uploads, with everything recorded in it so far finished before what's recorded next: a copy or fill outside a
	/// block, which may read or write what an upload before it wrote.
	/// </summary>
	GpuCommandList TransferList()
	{
		var uploads = UploadList();
		uploads.Barrier( PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite );
		written.Clear();
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
		counters.Add( buffer, new Backing<BufferHandle>( handle ) );
		ownedBuffers.Add( handle );
		return handle;
	}

	/// <summary>The counter to bind beside a buffer: an append buffer's always, another's once it has one.</summary>
	BufferHandle? BoundCounter( GpuBuffer buffer )
	{
		if ( counters.TryGetValue( buffer, out var backing ) ) return backing.Handle;
		return buffer.Usage.HasFlag( GpuBuffer.UsageFlags.Append ) ? CounterOf( buffer ) : null;
	}

	readonly Dictionary<string, string> counterNames = new();

	/// <summary>Set a buffer and, when it has one, its hidden counter (<see cref="ShaderResource.CounterName"/>) under <paramref name="name"/>.</summary>
	void LowerBuffer( string name, GpuBuffer buffer )
	{
		lowered.Set( name, BufferOf( buffer ) );
		if ( BoundCounter( buffer ) is not { } counter ) return;
		if ( !counterNames.TryGetValue( name, out var counterName ) ) counterNames[name] = counterName = ShaderResource.CounterName( name );
		lowered.Set( counterName, counter );
	}

	void ForgetCounter( GpuBuffer buffer )
	{
		if ( !counters.TryGetValue( buffer, out var backing ) ) return;
		counters.Remove( buffer );
		ownedBuffers.Remove( backing.Handle );
		gpu.Destroy( backing.Handle );
	}

	public override void SetStructureCount( GpuBuffer buffer, uint value ) => Fill( CounterOf( buffer ), value, 0, sizeof( uint ) );

	public override void CopyStructureCount( GpuBuffer source, GpuBuffer destination, uint destinationOffset )
	{
		var counter = CounterOf( source );
		var to = BufferOf( destination );
		if ( list is not null )
		{
			context.CopyBuffer( counter, 0, to, destinationOffset, sizeof( uint ) );
			return;
		}

		TransferList().CopyBuffer( counter, 0, to, destinationOffset, sizeof( uint ) );
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
	}

	// Recorded in the block being recorded, or in the uploads, and not yet submitted
	readonly List<Readback> blockReadbacks = new();
	readonly List<Readback> uploadReadbacks = new();

	// Submitted (or lent), waiting for the GPU
	readonly List<Readback> pendingReadbacks = new();

	// Read by their callbacks, to destroy on the next poll
	readonly System.Collections.Concurrent.ConcurrentQueue<BufferHandle> readBuffers = new();

	/// <summary>
	/// Copy part of a buffer back and hand it to <paramref name="callback"/> on a worker once the GPU has run the copy, as
	/// native's asynchronous readback does; the bytes are only valid during the callback.
	/// </summary>
	public override void ReadBufferAsync( GpuBuffer buffer, AsyncGPUReadback.BufferReadDelegate callback, int offset, int size )
	{
		if ( size <= 0 ) return;
		var source = BufferOf( buffer );
		var readback = new Readback
		{
			Buffer = gpu.CreateBuffer( new BufferInfo { Size = size, Memory = MemoryLocation.Readback, Name = "Buffer readback" } ),
			Size = size,
			Callback = callback,
			Frame = gpu.FrameNumber,
		};

		if ( list is not null )
		{
			context.CopyToReadback( source, offset, readback.Buffer, size );
			blockReadbacks.Add( readback );
			return;
		}

		var uploads = TransferList();
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
	/// Hand back the readbacks the GPU has finished, each to its callback on a worker, and free the buffers callbacks are done
	/// with.
	/// </summary>
	internal unsafe void PollReadbacks()
	{
		while ( readBuffers.TryDequeue( out var done ) ) gpu.Destroy( done );
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
			var data = (nint)Unsafe.AsPointer( ref gpu.Map( readback.Buffer )[0] );
			var queue = readBuffers;
			System.Threading.Tasks.Task.Run( () =>
			{
				try
				{
					readback.Callback( new Span<byte>( (void*)data, readback.Size ) );
				}
				finally
				{
					queue.Enqueue( readback.Buffer );
				}
			} );
		}
	}

	/// <summary>Drop every readback not handed back, and free their buffers.</summary>
	void DestroyReadbacks()
	{
		foreach ( var readback in pendingReadbacks.Concat( blockReadbacks ).Concat( uploadReadbacks ) ) gpu.Destroy( readback.Buffer );
		pendingReadbacks.Clear();
		blockReadbacks.Clear();
		uploadReadbacks.Clear();
		while ( readBuffers.TryDequeue( out var done ) ) gpu.Destroy( done );
	}
}
