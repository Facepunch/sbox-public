using Borealis.Gpu.Memory;
using Borealis.Gpu.Vulkan;
using System.Threading;

namespace Borealis.Gpu;

/// <summary>
/// Moving device buffers and textures that aren't content out of memory blocks being drained
/// (<see cref="MemoryAllocator.Relocation"/>), in place: the handle, its bindless slots, its views and the persistent sets
/// that bind it stay; only the Vulkan buffer or image, its views and its memory change. Done in <see cref="BeginFrame"/>
/// once nothing records or is pending on the GPU: the bindless table and persistent sets can't be written while work that
/// may read them is.
/// </summary>
public sealed unsafe partial class Device
{
	// The gate: recording lists, persistent set writes, bindless additions, views and destruction hold it, so a move never
	// happens under them. Users count up; a move sets 'moving' and goes ahead only when no one is in
	int gateUsers, gateMoving, moverThread;

	/// <summary>
	/// Enter the gate: false (and nothing to exit) on the thread doing a move, which uses the device itself. Waits while a
	/// move is going on.
	/// </summary>
	internal bool EnterGate()
	{
		if ( Volatile.Read( ref moverThread ) == Environment.CurrentManagedThreadId ) return false;
		while ( true )
		{
			Interlocked.Increment( ref gateUsers );
			if ( Volatile.Read( ref gateMoving ) == 0 ) return true;
			Interlocked.Decrement( ref gateUsers );
			var spin = new SpinWait();
			while ( Volatile.Read( ref gateMoving ) != 0 ) spin.SpinOnce();
		}
	}

	internal void ExitGate() => Interlocked.Decrement( ref gateUsers );

	/// <summary>
	/// Hold moves (set while a load is going on, <c>BackendDevice.Loading</c>): what loads makes and uploads all the time, and
	/// a move waits for the GPU; blocks drain once it's over.
	/// </summary>
	public bool HoldRelocation
	{
		get => Volatile.Read( ref holdRelocation );
		set => Volatile.Write( ref holdRelocation, value );
	}

	bool holdRelocation;

	/// <summary>Device buffers moved out of blocks being drained so far, and their bytes.</summary>
	public long RelocatedBuffers { get; private set; }

	/// <inheritdoc cref="RelocatedBuffers"/>
	public long RelocatedBufferBytes { get; private set; }

	/// <summary>Textures moved out of blocks being drained so far, and their bytes.</summary>
	public long RelocatedTextures { get; private set; }

	/// <inheritdoc cref="RelocatedTextures"/>
	public long RelocatedTextureBytes { get; private set; }

	/// <summary>Moves done (each waits for the GPU to finish what was sent), and moves put off because something was recording.</summary>
	public long Relocations { get; private set; }

	/// <inheritdoc cref="Relocations"/>
	public long RelocationsPutOff { get; private set; }

	/// <summary>Blocks left undrained because something in them can't move, or had no room in the others, so far.</summary>
	public long RelocationsRefused { get; private set; }

	/// <inheritdoc cref="RelocationsRefused"/>
	public long RelocationsWithoutRoom { get; private set; }

	/// <summary>Frames <see cref="FrameStarts"/> remembers, a power of two.</summary>
	internal const int FrameHistory = 16384;

	/// <summary>
	/// When each of the last <see cref="FrameHistory"/> frames began (<see cref="System.Diagnostics.Stopwatch"/> ticks at
	/// <see cref="BeginFrame"/>, by frame number modulo the length), for finding long frames after the fact.
	/// </summary>
	internal readonly long[] FrameStarts = new long[FrameHistory];

	/// <summary>Moves <see cref="RelocationFrames"/> remembers, a power of two.</summary>
	internal const int RelocationHistory = 256;

	/// <summary>
	/// The last <see cref="RelocationHistory"/> moves (by <see cref="Relocations"/> modulo the length): the frame each ran
	/// in, its ticks in all (copies made, the wait, the copy, the swaps), and its ticks up to the GPU going idle.
	/// </summary>
	internal readonly ulong[] RelocationFrames = new ulong[RelocationHistory];

	/// <inheritdoc cref="RelocationFrames"/>
	internal readonly long[] RelocationTicks = new long[RelocationHistory], RelocationWaitTicks = new long[RelocationHistory];

	/// <summary>Bindings of persistent sets written again because what they name moved, for the tests.</summary>
	internal int RelocatedBindings { get; private set; }

	// The buffers bound to the bindless vertex cache and pipeline texture bindings (SetBindlessBuffer), rewritten if moved
	BufferHandle vertexCacheBuffer, pipelineTexturesBuffer;

	// Scratch, kept so a move allocates nothing once warm
	readonly List<MemoryBlock> drainingBlocks = new();
	readonly List<ulong> owners = new();
	readonly List<(BufferHandle Moving, BufferHandle Copy)> bufferMoves = new();
	readonly List<(TextureHandle Moving, TextureHandle Copy)> textureMoves = new();
	readonly HashSet<BufferHandle> movedBuffers = new();
	readonly HashSet<TextureHandle> movedTextures = new();
	readonly HashSet<TextureViewHandle> movedViews = new();
	readonly List<ulong> oldViews = new();

	/// <summary>
	/// In <see cref="BeginFrame"/>: while the allocator drains a block that isn't content, move what's in it. The copies are
	/// made first, in room the other blocks have; if they all fit and nothing records, the device waits for the GPU to finish
	/// what was sent, copies, and swaps them in. A block with something that can't move, or no room for it, is left without
	/// waiting. Put off to the next frame while anything records.
	/// </summary>
	void RelocateDraining()
	{
		if ( !Allocator.AnyDrainingToMove || IsLost || HoldRelocation ) return;
		var started = System.Diagnostics.Stopwatch.GetTimestamp();
		if ( !PrepareMoves() ) return;

		Interlocked.Exchange( ref gateMoving, 1 );
		if ( Volatile.Read( ref gateUsers ) != 0 )
		{
			Volatile.Write( ref gateMoving, 0 );
			RelocationsPutOff++;
			ReleaseCopies( 0, 0 );
			return;
		}

		Volatile.Write( ref moverThread, Environment.CurrentManagedThreadId );
		try
		{
			// Everything recorded has been sent; once the GPU has run it, nothing pending names the old memory
			Flush();
			graphicsQueue.Wait( graphicsQueue.Submitted );
			computeQueue.Wait( computeQueue.Submitted );
			Relocations++;
			var waited = System.Diagnostics.Stopwatch.GetTimestamp();

			var list = BeginCommandList( QueueType.Graphics, "Relocation" );
			list.Barrier( PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferRead | MemoryAccess.TransferWrite );
			foreach ( var (moving, copy) in bufferMoves ) MoveBuffer( moving, copy, list );
			foreach ( var (moving, copy) in textureMoves ) MoveTexture( moving, copy, list );
			list.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite );
			SubmitAndRecycle( list );

			// Views made of moved textures are made again over their new images, in their slots
			if ( movedTextures.Count > 0 ) RemakeViews();

			// The bindings that name what moved point at the new buffers and images. The old ones (the copies' handles hold them
			// now) go as anything destroyed does, once the frame is done: work recorded from here on can't name them, but a stale
			// bindless index (a view destroyed this frame, still in some record) reads memory that's still there
			if ( movedBuffers.Count > 0 ) RelocatedBindings += RewriteSets( new BuffersIn( movedBuffers ) );
			if ( movedTextures.Count > 0 ) RelocatedBindings += RewriteSets( new TexturesIn( movedTextures, movedViews ) );
			if ( FreeMovedAtOnce )
			{
				ReleaseCopies( 0, 0 );
				foreach ( var view in oldViews ) Vk.vkDestroyImageView( Vk.Device, view, null );
			}
			else
			{
				foreach ( var (_, copy) in bufferMoves ) Destroy( copy );
				foreach ( var (_, copy) in textureMoves ) Destroy( copy );
				foreach ( var view in oldViews ) DestroyViewLater( view );
			}

			var slot = (int)((Relocations - 1) & (RelocationHistory - 1));
			RelocationFrames[slot] = FrameNumber;
			RelocationTicks[slot] = System.Diagnostics.Stopwatch.GetTimestamp() - started;
			RelocationWaitTicks[slot] = waited - started;
		}
		finally
		{
			bufferMoves.Clear();
			textureMoves.Clear();
			movedBuffers.Clear();
			movedTextures.Clear();
			movedViews.Clear();
			oldViews.Clear();
			Volatile.Write( ref moverThread, 0 );
			Volatile.Write( ref gateMoving, 0 );
		}
	}

	/// <summary>
	/// Make a copy in another block of everything alive in the blocks being drained (<see cref="bufferMoves"/>,
	/// <see cref="textureMoves"/>), before waiting for anything. A block with something that can't move, or without room for
	/// all of it, is given up and its copies freed. Whether anything is left to move: once everything has, what's left in a
	/// block was destroyed and goes with its frame.
	/// </summary>
	bool PrepareMoves()
	{
		drainingBlocks.Clear();
		Allocator.DrainingToMove( drainingBlocks );
		foreach ( var block in drainingBlocks )
		{
			var (buffersFrom, texturesFrom) = (bufferMoves.Count, textureMoves.Count);
			owners.Clear();
			Allocator.CollectOwners( block, owners );
			foreach ( var owner in owners )
			{
				var movable = Movable( owner, block.Images );
				if ( movable is null ) continue;

				var (index, generation) = ((uint)owner, (uint)(owner >> 32));
				var made = movable.Value && (block.Images ? CopyFor( new TextureHandle( index, generation ) ) : CopyFor( new BufferHandle( index, generation ) ));
				if ( made ) continue;

				// Something in it can never move, or has no room in the others: leave the block, without waiting for the GPU
				if ( movable.Value ) RelocationsWithoutRoom++;
				else RelocationsRefused++;
				Allocator.Abandon( block );
				ReleaseCopies( buffersFrom, texturesFrom );
				break;
			}
		}

		drainingBlocks.Clear();
		owners.Clear();
		return bufferMoves.Count + textureMoves.Count > 0;
	}

	/// <summary>
	/// Whether what an allocation holds can move: null when it was destroyed (it goes with its frame), false for a pinned
	/// resource, a swap chain image, a texture whose layout isn't known and a buffer outside device memory. Reads the
	/// records without throwing, as it runs outside the gate.
	/// </summary>
	bool? Movable( ulong owner, bool images )
	{
		var (index, generation) = ((uint)owner, (uint)(owner >> 32));
		if ( images )
		{
			if ( index == 0 || index >= Textures.Limit ) return null;
			ref var texture = ref Textures.At( index, out var current );
			if ( current != generation || texture.Image == 0 ) return null;
			return !texture.External && !texture.Pinned && !texture.Layouts.Unknown;
		}

		if ( index == 0 || index >= Buffers.Limit ) return null;
		ref var buffer = ref Buffers.At( index, out var now );
		if ( now != generation || buffer.Buffer == 0 ) return null;
		return buffer.Location == MemoryLocation.Device && !buffer.Pinned;
	}

	/// <summary>A copy of a buffer (its info, not its contents yet) in room other blocks have, into <see cref="bufferMoves"/>; false when there's none.</summary>
	bool CopyFor( BufferHandle buffer )
	{
		MemoryAllocator.ExistingBlocksOnly = true;
		try
		{
			// Read outside the gate: something may have destroyed it since it was found, which the move then skips
			if ( !Buffers.IsAlive( buffer.Index, buffer.Generation ) ) return true;
			var record = Get( buffer );
			bufferMoves.Add( (buffer, CreateBuffer( new BufferInfo { Size = record.Size, Usage = record.Usage, Memory = record.Location, Name = record.Name } )) );
			return true;
		}
		catch ( GpuException e ) when ( e.Result == (int)VkResult.ErrorFragmentedPool )
		{
			return false;
		}
		catch ( ArgumentException )
		{
			return true;
		}
		finally
		{
			MemoryAllocator.ExistingBlocksOnly = false;
		}
	}

	/// <summary>A copy of a texture (its info) in room other blocks have, into <see cref="textureMoves"/>; false when there's none.</summary>
	bool CopyFor( TextureHandle texture )
	{
		MemoryAllocator.ExistingBlocksOnly = true;
		try
		{
			// Read outside the gate: something may have destroyed it since it was found
			if ( !Textures.IsAlive( texture.Index, texture.Generation ) ) return true;
			var info = Get( texture ).Info;
			textureMoves.Add( (texture, CreateTexture( info )) );
			return true;
		}
		catch ( GpuException e ) when ( e.Result == (int)VkResult.ErrorFragmentedPool )
		{
			return false;
		}
		catch ( ArgumentException )
		{
			return true;
		}
		finally
		{
			MemoryAllocator.ExistingBlocksOnly = false;
		}
	}

	/// <summary>
	/// Free the copies from these indices on, and forget them: a copy not moved in (its block given up, or the move put off)
	/// never reached the GPU; after a move each holds the old buffer or image, which nothing pending uses.
	/// </summary>
	void ReleaseCopies( int buffersFrom, int texturesFrom )
	{
		for ( int i = buffersFrom; i < bufferMoves.Count; i++ ) FreeBufferNow( Buffers.Remove( bufferMoves[i].Copy.Index, bufferMoves[i].Copy.Generation ) );
		for ( int i = texturesFrom; i < textureMoves.Count; i++ ) FreeTexture( Textures.Remove( textureMoves[i].Copy.Index, textureMoves[i].Copy.Generation ) );
		bufferMoves.RemoveRange( buffersFrom, bufferMoves.Count - buffersFrom );
		textureMoves.RemoveRange( texturesFrom, textureMoves.Count - texturesFrom );
	}

	/// <summary>
	/// Move a device buffer into its copy (<see cref="CopyFor(BufferHandle)"/>): copied on <paramref name="list"/>, then the
	/// two swap their Vulkan buffers and memory, so the handle names the new one and the copy's handle the old, which goes
	/// with it. Its bindless slot and the bindless buffers it's bound to point at the new one.
	/// </summary>
	void MoveBuffer( BufferHandle buffer, BufferHandle copy, CommandList list )
	{
		// Destroyed meanwhile: its copy goes, and it goes with its frame
		if ( !Buffers.IsAlive( buffer.Index, buffer.Generation ) ) return;
		ref var moved = ref Get( buffer );
		ref var stand = ref Get( copy );
		list.CopyBuffer( buffer, 0, copy, 0, moved.Size );

		(moved.Buffer, stand.Buffer) = (stand.Buffer, moved.Buffer);
		(moved.Memory, stand.Memory) = (stand.Memory, moved.Memory);
		Allocator.SetOwner( moved.Memory, OwnerOf( buffer ) );
		Allocator.SetOwner( stand.Memory, OwnerOf( copy ) );

		if ( moved.BindlessSlot > 0 ) Bindless.SetBufferSlot( moved.BindlessSlot - 1, moved.Buffer, (ulong)moved.Size );
		if ( buffer == vertexCacheBuffer ) Bindless.SetBuffer( ShaderAbi.BindlessVertexCache, moved.Buffer, (ulong)moved.Size );
		if ( buffer == pipelineTexturesBuffer ) Bindless.SetBuffer( ShaderAbi.BindlessPipelineTextures, moved.Buffer, (ulong)moved.Size );

		movedBuffers.Add( buffer );
		RelocatedBuffers++;
		RelocatedBufferBytes += moved.Size;
	}

	/// <summary>
	/// Move a texture into its copy (<see cref="CopyFor(TextureHandle)"/>), as <see cref="MoveBuffer"/>: every mip and layer
	/// copied from and back to the layout it's in (<see cref="SubresourceLayouts"/>), then the two swap their images, memory
	/// and views. Its bindless slots point at the new image; views made of it are made again after (<see cref="RemakeViews"/>).
	/// </summary>
	void MoveTexture( TextureHandle texture, TextureHandle copy, CommandList list )
	{
		if ( !Textures.IsAlive( texture.Index, texture.Generation ) ) return;
		CopyTextureInPlace( list, texture, copy );

		ref var moved = ref Get( texture );
		ref var stand = ref Get( copy );
		(moved.Image, stand.Image) = (stand.Image, moved.Image);
		(moved.Memory, stand.Memory) = (stand.Memory, moved.Memory);
		(moved.View, stand.View) = (stand.View, moved.View);
		(moved.SrgbView, stand.SrgbView) = (stand.SrgbView, moved.SrgbView);
		(moved.StorageView, stand.StorageView) = (stand.StorageView, moved.StorageView);
		(moved.Attachments, stand.Attachments) = (stand.Attachments, moved.Attachments);
		Allocator.SetOwner( moved.Memory, OwnerOf( texture ) );
		Allocator.SetOwner( stand.Memory, OwnerOf( copy ) );

		if ( moved.BindlessIndex >= 0 ) Bindless.SetTexture( moved.BindlessIndex, moved.View, moved.SrgbView );
		if ( moved.StorageIndex >= 0 ) Bindless.SetStorage( moved.StorageIndex, moved.StorageView );

		movedTextures.Add( texture );
		RelocatedTextures++;
		RelocatedTextureBytes += (long)moved.Memory.Size;
	}

	/// <summary>
	/// Copy every mip and layer of <paramref name="from"/> into <paramref name="to"/> (new, undefined, the same info) and
	/// leave both in the layouts <paramref name="from"/> was in; subresources in no layout (undefined) aren't copied.
	/// </summary>
	void CopyTextureInPlace( CommandList list, TextureHandle from, TextureHandle to )
	{
		ref var source = ref Get( from );
		var info = source.Info;
		var layers = source.ArrayLayers;
		var layouts = source.Layouts;
		for ( int mip = 0; mip < info.Mips; mip++ )
		{
			for ( int layer = 0; layer < layers; layer++ )
			{
				// One copy of the whole image when its layout is one
				var whole = !layouts.Mixed;
				var layout = layouts.Of( mip, layer, layers );
				var mipCount = whole ? 0 : 1;
				var layerCount = whole ? 0 : 1;
				if ( layout != ImageLayout.Undefined )
				{
					list.Barrier( new TextureBarrier { Texture = from, OldLayout = layout, NewLayout = ImageLayout.CopySource, SourceStage = PipelineStage.AllCommands, SourceAccess = MemoryAccess.MemoryWrite, DestinationStage = PipelineStage.Transfer, DestinationAccess = MemoryAccess.TransferRead, BaseMip = mip, MipCount = mipCount, BaseLayer = layer, LayerCount = layerCount } );
					list.Barrier( new TextureBarrier { Texture = to, OldLayout = ImageLayout.Undefined, NewLayout = ImageLayout.CopyDestination, SourceStage = PipelineStage.AllCommands, SourceAccess = MemoryAccess.None, DestinationStage = PipelineStage.Transfer, DestinationAccess = MemoryAccess.TransferWrite, BaseMip = mip, MipCount = mipCount, BaseLayer = layer, LayerCount = layerCount } );
					if ( whole )
					{
						for ( int m = 0; m < info.Mips; m++ ) list.CopyImage( from, to, m, 0, layers );
					}
					else list.CopyImage( from, to, mip, layer, 1 );

					list.Barrier( new TextureBarrier { Texture = from, OldLayout = ImageLayout.CopySource, NewLayout = layout, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferRead, DestinationStage = PipelineStage.AllCommands, DestinationAccess = MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite, BaseMip = mip, MipCount = mipCount, BaseLayer = layer, LayerCount = layerCount } );
					list.Barrier( new TextureBarrier { Texture = to, OldLayout = ImageLayout.CopyDestination, NewLayout = layout, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.AllCommands, DestinationAccess = MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite, BaseMip = mip, MipCount = mipCount, BaseLayer = layer, LayerCount = layerCount } );
				}

				if ( whole ) return;
			}
		}
	}

	/// <summary>Make every view of a moved texture again over its new image, keeping its handle and bindless slots.</summary>
	void RemakeViews()
	{
		var limit = Views.Limit;
		for ( uint index = 1; index < limit; index++ )
		{
			ref var view = ref Views.At( index, out var generation );
			if ( view.View == 0 || !movedTextures.Contains( view.Texture ) ) continue;

			oldViews.Add( view.View );
			view.View = MakeViewOf( Get( view.Texture ), view.Info, out _ );
			if ( view.BindlessIndex >= 0 ) Bindless.SetTexture( view.BindlessIndex, view.View, view.View );
			if ( view.StorageIndex >= 0 ) Bindless.SetStorage( view.StorageIndex, view.View );
			movedViews.Add( new TextureViewHandle( index, generation ) );
		}
	}

	readonly struct BuffersIn( HashSet<BufferHandle> buffers ) : IBindingFilter
	{
		public bool Names( in Binding binding ) => (binding.Type is BindingType.UniformBuffer or BindingType.UniformBufferDynamic or BindingType.StorageBuffer) && buffers.Contains( binding.Buffer );
	}

	readonly struct TexturesIn( HashSet<TextureHandle> textures, HashSet<TextureViewHandle> views ) : IBindingFilter
	{
		public bool Names( in Binding binding ) => (binding.Type is BindingType.SampledTexture or BindingType.StorageTexture) && (binding.View.IsValid ? views.Contains( binding.View ) : textures.Contains( binding.Texture ));
	}

	/// <summary>
	/// A submitted list's texture barriers, in the order recorded: the layouts its textures are left in once it has run.
	/// Under its queue's submit lock, so a queue's lists apply in the order they run.
	/// </summary>
	void ApplyLayouts( CommandList list )
	{
		var changes = list.LayoutChanges;
		if ( changes.IsEmpty ) return;
		lock ( layoutLock )
		{
			foreach ( ref readonly var change in changes )
			{
				if ( !Textures.IsAlive( change.Texture.Index, change.Texture.Generation ) ) continue;
				ref var record = ref Get( change.Texture );
				record.Layouts.Apply( change.BaseMip, change.MipCount, change.BaseLayer, change.LayerCount, change.NewLayout, record.Info.Mips, record.ArrayLayers );
			}
		}
	}

	readonly object layoutLock = new();

	/// <summary>The layout the device last saw a texture's mip and layer left in by submitted work, for the tests.</summary>
	internal ImageLayout LayoutOf( TextureHandle texture, int mip = 0, int layer = 0 )
	{
		ref var record = ref Get( texture );
		return record.Layouts.Unknown ? (ImageLayout)(-1) : record.Layouts.Of( mip, layer, record.ArrayLayers );
	}

	/// <summary>What an allocation holds, for <see cref="MemoryAllocator.SetOwner"/>: a handle's index and generation.</summary>
	static ulong OwnerOf( BufferHandle buffer ) => buffer.Index | ((ulong)buffer.Generation << 32);

	/// <inheritdoc cref="OwnerOf(BufferHandle)"/>
	static ulong OwnerOf( TextureHandle texture ) => texture.Index | ((ulong)texture.Generation << 32);

	/// <summary>
	/// Free what moves leave behind at once (<c>BOREALIS_RELOCATE_FREE_AT_ONCE=1</c>), rather than once the frame is done like
	/// anything destroyed (the default).
	/// </summary>
	internal static bool FreeMovedAtOnce { get; set; } = Environment.GetEnvironmentVariable( "BOREALIS_RELOCATE_FREE_AT_ONCE" ) == "1";

	void DestroyViewLater( ulong view ) => Defer( new Garbage( new ViewRecord { View = view }, imageViewOnly: true ) );

	/// <summary>Free a buffer at once: only when no pending work can use it.</summary>
	void FreeBufferNow( in BufferRecord record )
	{
		if ( record.BindlessSlot > 0 ) Bindless.RemoveBuffer( record.BindlessSlot - 1 );
		Vk.vkDestroyBuffer( Vk.Device, record.Buffer, null );
		FreeMemory( record.Memory );
	}
}
