using Borealis.Gpu;
using Borealis.Gpu.Memory;

namespace BorealisGpuTests;

/// <summary>
/// Device buffers that aren't content move out of a block being drained in place (<see cref="MemoryAllocator.Relocation"/>):
/// the handle, its contents, its bindless slot and the persistent sets that bind it stay; only its memory changes, and the
/// block empties. Moves wait for a frame in which nothing records.
/// </summary>
[TestClass]
[DoNotParallelize]
public class RelocationTests : GpuTest
{
	const long Size = 1 << 20;

	/// <summary>A sparse block of device buffers: one buffer left in it, with room for it in another block.</summary>
	(BufferHandle Kept, MemoryBlock Block, List<BufferHandle> Others) SparseBlock()
	{
		var buffers = new List<BufferHandle>();
		for ( int i = 0; i < 48; i++ ) buffers.Add( Device.CreateBuffer( new BufferInfo { Size = Size, Usage = BufferUsage.Storage, Name = $"Filler {i}" } ) );

		var kept = buffers[^1];
		var block = Device.Get( kept ).Memory.Block;
		Assert.IsFalse( block.Dedicated, "1 MiB buffers share blocks" );

		var rest = new List<BufferHandle>();
		var roomMade = false;
		foreach ( var buffer in buffers )
		{
			if ( buffer == kept ) continue;
			var inBlock = Device.Get( buffer ).Memory.Block == block;
			if ( inBlock || !roomMade )
			{
				roomMade |= !inBlock;
				Device.Destroy( buffer );
			}
			else rest.Add( buffer );
		}

		// What was destroyed goes once its frames are done
		for ( int i = 0; i <= Device.FramesInFlight; i++ ) Device.BeginFrame();
		Assert.AreEqual( (ulong)Size, block.Used, "only the kept buffer is left in its block" );
		return (kept, block, rest);
	}

	byte[] Contents( BufferHandle buffer )
	{
		var readback = Device.CreateBuffer( new BufferInfo { Size = Size, Memory = MemoryLocation.Readback, Name = "Readback" } );
		var list = Device.BeginCommandList( QueueType.Graphics, "Read back" );
		list.CopyBuffer( buffer, 0, readback, 0, Size );
		list.Barrier( new BufferBarrier { Buffer = readback, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		Device.Wait( Device.Submit( list ) );
		var bytes = Device.Map( readback ).ToArray();
		Device.Destroy( readback );
		return bytes;
	}

	[TestMethod]
	public void DeviceBuffersMoveInPlaceOutOfDrainingBlocks()
	{
		var (kept, block, rest) = SparseBlock();
		var data = new byte[Size];
		for ( int i = 0; i < data.Length; i++ ) data[i] = (byte)(i * 13 + 7);
		Device.WriteBuffer( kept, data );
		var slot = Device.BindlessIndex( kept );
		var layout = Device.CreateBindingLayout( [new BindingSlot( 0, BindingType.StorageBuffer, ShaderStages.Compute )] );
		var set = Device.CreateBindingSet( layout, [Binding.Storage( 0, kept )] );

		Device.Allocator.Drain( block );
		Device.BeginFrame();

		Assert.AreEqual( 1, Device.RelocatedBuffers );
		Assert.AreNotSame( block, Device.Get( kept ).Memory.Block, "moved out of the draining block" );
		Assert.AreEqual( 0ul, block.Used, "which is empty" );
		Assert.IsFalse( Device.Allocator.AnyDrainingToMove, "and done draining" );
		Assert.AreEqual( slot, Device.BindlessIndex( kept ), "the bindless slot is kept" );
		Assert.AreEqual( 1, Device.RelocatedBindings, "the persistent set that binds it is written again" );
		CollectionAssert.AreEqual( data, Contents( kept ), "with its contents" );

		Device.Destroy( set );
		Device.Destroy( kept );
		foreach ( var buffer in rest ) Device.Destroy( buffer );
		CollectionAssert.AreEqual( Array.Empty<string>(), TakeValidationErrors() );
	}

	/// <summary>A list recording when the frame begins (one carried into it) puts the move off until it's submitted.</summary>
	[TestMethod]
	public void MovesWaitForListsBeingRecorded()
	{
		var (kept, block, rest) = SparseBlock();
		Device.Allocator.Drain( block );

		var recording = Device.BeginCommandList( QueueType.Graphics, "Recording across the frame" );
		Device.BeginFrame();
		Assert.AreEqual( 0, Device.RelocatedBuffers, "put off while a list records" );
		Assert.AreEqual( 1, Device.RelocationsPutOff );
		Assert.AreSame( block, Device.Get( kept ).Memory.Block );

		Device.Submit( recording );
		Device.BeginFrame();
		Assert.AreEqual( 1, Device.RelocatedBuffers );
		Assert.AreNotSame( block, Device.Get( kept ).Memory.Block );

		Device.Destroy( kept );
		foreach ( var buffer in rest ) Device.Destroy( buffer );
		CollectionAssert.AreEqual( Array.Empty<string>(), TakeValidationErrors() );
	}
}
