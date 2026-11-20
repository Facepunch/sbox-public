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
	(BufferHandle Kept, MemoryBlock Block, List<BufferHandle> Others) SparseBlock( bool makeRoom = true )
	{
		var buffers = new List<BufferHandle>();
		for ( int i = 0; i < 48; i++ ) buffers.Add( Device.CreateBuffer( new BufferInfo { Size = Size, Usage = BufferUsage.Storage, Name = $"Filler {i}" } ) );

		var kept = buffers[^1];
		var block = Device.Get( kept ).Memory.Block;
		Assert.IsFalse( block.Dedicated, "1 MiB buffers share blocks" );

		var rest = new List<BufferHandle>();
		var roomMade = !makeRoom;
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
		Assert.AreNotEqual( 0ul, block.Used, "the old buffer is kept until the frame is done: a stale index still reads memory that's there" );

		// The old buffer goes once the frame is done, as anything destroyed does
		for ( int i = 0; i <= Device.FramesInFlight; i++ ) Device.BeginFrame();
		Assert.AreEqual( 0ul, block.Used, "which is empty" );
		Assert.IsFalse( Device.Allocator.AnyDrainingToMove, "and done draining" );
		Assert.AreEqual( 1, Device.RelocatedBuffers, "and nothing moved again" );
		Assert.AreEqual( slot, Device.BindlessIndex( kept ), "the bindless slot is kept" );
		Assert.AreEqual( 1, Device.RelocatedBindings, "the persistent set that binds it is written again" );
		CollectionAssert.AreEqual( data, Contents( kept ), "with its contents" );

		Device.Destroy( set );
		Device.Destroy( kept );
		foreach ( var buffer in rest ) Device.Destroy( buffer );
		CollectionAssert.AreEqual( Array.Empty<string>(), TakeValidationErrors() );
	}

	/// <summary>A block whose contents have no room in the others is left as it is, without waiting for the GPU.</summary>
	[TestMethod]
	public void BlocksWithoutRoomAreLeftWithoutWaiting()
	{
		var (kept, block, rest) = SparseBlock( makeRoom: false );
		Device.Allocator.Drain( block );
		Device.BeginFrame();

		Assert.AreEqual( 0, Device.Relocations, "no wait for the GPU" );
		Assert.AreEqual( 1, Device.RelocationsWithoutRoom );
		Assert.IsFalse( Device.Allocator.AnyDrainingToMove, "the block is left" );
		Assert.AreSame( block, Device.Get( kept ).Memory.Block );

		Device.Destroy( kept );
		foreach ( var buffer in rest ) Device.Destroy( buffer );
		CollectionAssert.AreEqual( Array.Empty<string>(), TakeValidationErrors() );
	}

	/// <summary>While a load holds moves (<see cref="Device.HoldRelocation"/>) nothing moves; once it lets go, the block drains.</summary>
	[TestMethod]
	public void LoadsHoldMoves()
	{
		var (kept, block, rest) = SparseBlock();
		Device.Allocator.Drain( block );
		Device.HoldRelocation = true;
		Device.BeginFrame();
		Assert.AreEqual( 0, Device.Relocations, "held while loading" );
		Assert.AreSame( block, Device.Get( kept ).Memory.Block );

		Device.HoldRelocation = false;
		Device.BeginFrame();
		Assert.AreEqual( 1, Device.RelocatedBuffers );
		Assert.AreNotSame( block, Device.Get( kept ).Memory.Block );

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

	/// <summary>A sparse block of non-content images (512x512 targets): one left in it, with room for it in another block.</summary>
	(TextureHandle Kept, MemoryBlock Block, List<TextureHandle> Others) SparseImageBlock()
	{
		var textures = new List<TextureHandle>();
		for ( int i = 0; i < 48; i++ )
		{
			var info = TextureInfo.RenderTarget( 512, 512, Format.RGBA8Unorm, name: $"Target {i}" );
			info.Usage |= TextureUsage.Storage;
			textures.Add( Device.CreateTexture( info ) );
		}

		var kept = textures[^1];
		var block = Device.Get( kept ).Memory.Block;
		Assert.IsFalse( block.Dedicated, "1 MiB targets share blocks" );

		var rest = new List<TextureHandle>();
		var roomMade = false;
		foreach ( var texture in textures )
		{
			if ( texture == kept ) continue;
			var inBlock = Device.Get( texture ).Memory.Block == block;
			if ( inBlock || !roomMade )
			{
				roomMade |= !inBlock;
				Device.Destroy( texture );
			}
			else rest.Add( texture );
		}

		for ( int i = 0; i <= Device.FramesInFlight; i++ ) Device.BeginFrame();
		Assert.AreEqual( Device.Get( kept ).Memory.Size, block.Used, "only the kept target is left in its block" );
		return (kept, block, rest);
	}

	/// <summary>
	/// A target moves in place like a buffer: its texels (copied from and back to the layout submitted barriers left it in),
	/// its bindless slots, the views made of it (remade in their slots) and the persistent sets that bind them.
	/// </summary>
	[TestMethod]
	public void TexturesMoveInPlaceOutOfDrainingBlocks()
	{
		var (kept, block, rest) = SparseImageBlock();
		var texels = new byte[512 * 512 * 4];
		for ( int i = 0; i < texels.Length; i++ ) texels[i] = (byte)(i * 7 + 3);
		Device.WriteTexture( kept, texels );
		Assert.AreEqual( ImageLayout.ShaderRead, Device.LayoutOf( kept ), "the device follows the layout the upload left" );

		var slot = Device.BindlessIndex( kept );
		var storageSlot = Device.StorageIndex( kept );
		var view = Device.CreateView( kept, new TextureViewInfo { Dimension = TextureDimension.Texture2D, MipCount = 1 }, bindless: true );
		var viewSlot = Device.BindlessIndex( view );
		var layout = Device.CreateBindingLayout( [new BindingSlot( 0, BindingType.SampledTexture, ShaderStages.Compute ), new BindingSlot( 1, BindingType.SampledTexture, ShaderStages.Compute )] );
		var set = Device.CreateBindingSet( layout, [Binding.Sampled( 0, kept ), Binding.Sampled( 1, view )] );

		Device.Allocator.Drain( block );
		Device.BeginFrame();

		Assert.AreEqual( 1, Device.RelocatedTextures );
		Assert.AreNotSame( block, Device.Get( kept ).Memory.Block, "moved out of the draining block" );
		for ( int i = 0; i <= Device.FramesInFlight; i++ ) Device.BeginFrame();
		Assert.AreEqual( 0ul, block.Used, "empty once the frame is done" );
		Assert.AreEqual( slot, Device.BindlessIndex( kept ), "the bindless slots are kept" );
		Assert.AreEqual( storageSlot, Device.StorageIndex( kept ) );
		Assert.AreEqual( viewSlot, Device.BindlessIndex( view ), "and the view's" );
		Assert.AreEqual( 2, Device.RelocatedBindings, "the set's bindings of the texture and its view are written again" );
		Assert.AreEqual( ImageLayout.ShaderRead, Device.LayoutOf( kept ) );
		CollectionAssert.AreEqual( texels, ReadBack( kept, ImageLayout.ShaderRead ), "with its texels" );

		Device.Destroy( set );
		Device.Destroy( view );
		Device.Destroy( kept );
		foreach ( var texture in rest ) Device.Destroy( texture );
		CollectionAssert.AreEqual( Array.Empty<string>(), TakeValidationErrors() );
	}

	/// <summary>
	/// The device follows a texture's layout per mip and layer from the barriers of submitted lists, in the order they were
	/// recorded; a list not yet submitted changes nothing.
	/// </summary>
	[TestMethod]
	public void LayoutsFollowSubmittedBarriers()
	{
		var info = new TextureInfo { Width = 64, Height = 64, Mips = 3, Layers = 2, Dimension = TextureDimension.Texture2DArray, Format = Format.RGBA8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination, Name = "Layouts" };
		var texture = Device.CreateTexture( info );
		Assert.AreEqual( ImageLayout.Undefined, Device.LayoutOf( texture ) );

		var list = Device.BeginCommandList( QueueType.Graphics, "Layouts" );
		list.Barrier( TextureBarrier.Transition( texture, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.AllCommands, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		list.Barrier( new TextureBarrier { Texture = texture, OldLayout = ImageLayout.CopyDestination, NewLayout = ImageLayout.ShaderRead, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.AllCommands, DestinationAccess = MemoryAccess.ShaderRead, BaseMip = 1, MipCount = 1, BaseLayer = 1, LayerCount = 1 } );
		Assert.AreEqual( ImageLayout.Undefined, Device.LayoutOf( texture ), "not until it's submitted" );
		Device.Wait( Device.Submit( list ) );

		Assert.AreEqual( ImageLayout.CopyDestination, Device.LayoutOf( texture, 0, 0 ) );
		Assert.AreEqual( ImageLayout.CopyDestination, Device.LayoutOf( texture, 1, 0 ) );
		Assert.AreEqual( ImageLayout.ShaderRead, Device.LayoutOf( texture, 1, 1 ) );
		Assert.AreEqual( ImageLayout.CopyDestination, Device.LayoutOf( texture, 2, 1 ) );

		list = Device.BeginCommandList( QueueType.Graphics, "Whole" );
		list.Barrier( new TextureBarrier { Texture = texture, OldLayout = ImageLayout.Undefined, NewLayout = ImageLayout.ShaderRead, SourceStage = PipelineStage.AllCommands, SourceAccess = MemoryAccess.None, DestinationStage = PipelineStage.AllCommands, DestinationAccess = MemoryAccess.ShaderRead } );
		Device.Wait( Device.Submit( list ) );
		Assert.AreEqual( ImageLayout.ShaderRead, Device.LayoutOf( texture, 1, 0 ), "a whole barrier makes it one layout again" );

		Device.Destroy( texture );
		CollectionAssert.AreEqual( Array.Empty<string>(), TakeValidationErrors() );
	}
}
