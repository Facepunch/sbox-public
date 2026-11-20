using Borealis.Gpu.Memory;
using Borealis.Gpu.Vulkan;
using System.Threading;

namespace Borealis.Gpu;

/// <summary>
/// Moving device buffers that aren't content out of memory blocks being drained (<see cref="MemoryAllocator.Relocation"/>),
/// in place: the handle, its bindless slot and the persistent sets that bind it stay; only the Vulkan buffer and its
/// memory change. Done in <see cref="BeginFrame"/> once nothing records or is pending on the GPU: the bindless table and
/// persistent sets can't be written while work that may read them is.
/// </summary>
public sealed unsafe partial class Device
{
	// The gate: recording lists, persistent set writes, bindless additions and destruction hold it, so a move never
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

	/// <summary>Device buffers moved out of blocks being drained so far, and their bytes.</summary>
	public long RelocatedBuffers { get; private set; }

	/// <inheritdoc cref="RelocatedBuffers"/>
	public long RelocatedBufferBytes { get; private set; }

	/// <summary>Moves done (each waits for the GPU to finish the frames in flight), and checks put off because something was recording.</summary>
	public long Relocations { get; private set; }

	/// <inheritdoc cref="Relocations"/>
	public long RelocationsPutOff { get; private set; }

	/// <summary>Bindings of persistent sets written again because what they name moved, for the tests.</summary>
	internal int RelocatedBindings { get; private set; }

	// The buffers bound to the bindless vertex cache and pipeline texture bindings (SetBindlessBuffer), rewritten if moved
	BufferHandle vertexCacheBuffer, pipelineTexturesBuffer;

	// Scratch, kept so a move allocates nothing once warm
	readonly List<MemoryBlock> drainingBlocks = new();
	readonly List<ulong> owners = new();
	readonly List<BufferHandle> movedFrom = new();
	readonly HashSet<BufferHandle> movedBuffers = new();

	/// <summary>
	/// In <see cref="BeginFrame"/>: while the allocator drains a block that isn't content, move what's in it, once nothing
	/// records and the GPU has finished what was sent. Puts it off to the next frame while anything records.
	/// </summary>
	void RelocateDraining()
	{
		if ( !Allocator.AnyDrainingToMove || IsLost || !AnythingToMove() ) return;

		Interlocked.Exchange( ref gateMoving, 1 );
		if ( Volatile.Read( ref gateUsers ) != 0 )
		{
			Volatile.Write( ref gateMoving, 0 );
			RelocationsPutOff++;
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

			drainingBlocks.Clear();
			Allocator.DrainingToMove( drainingBlocks );
			CommandList list = null;
			foreach ( var block in drainingBlocks )
			{
				owners.Clear();
				Allocator.CollectOwners( block, owners );
				foreach ( var owner in owners )
				{
					if ( block.Images || MoveBuffer( new BufferHandle( (uint)owner, (uint)(owner >> 32) ), ref list ) ) continue;

					// Something in it can't move: the block won't empty
					Allocator.Abandon( block );
					break;
				}
			}

			if ( list is not null )
			{
				list.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite );
				SubmitAndRecycle( list );
			}

			// The bindings that name what moved point at the new buffers; the old ones go now, as nothing pending uses them
			if ( movedBuffers.Count > 0 ) RelocatedBindings += RewriteSets( new BuffersIn( movedBuffers ) );
			foreach ( var old in movedFrom ) FreeBufferNow( Buffers.Remove( old.Index, old.Generation ) );
		}
		finally
		{
			movedFrom.Clear();
			movedBuffers.Clear();
			drainingBlocks.Clear();
			owners.Clear();
			Volatile.Write( ref moverThread, 0 );
			Volatile.Write( ref gateMoving, 0 );
		}
	}

	/// <summary>
	/// Whether a block being drained still holds something alive: what's left once everything has moved is what was
	/// destroyed and goes with its frame, which a move (and its wait for the GPU) would only repeat.
	/// </summary>
	bool AnythingToMove()
	{
		drainingBlocks.Clear();
		Allocator.DrainingToMove( drainingBlocks );
		var any = false;
		foreach ( var block in drainingBlocks )
		{
			owners.Clear();
			Allocator.CollectOwners( block, owners );
			foreach ( var owner in owners )
			{
				any |= block.Images ? Textures.IsAlive( (uint)owner, (uint)(owner >> 32) ) : Buffers.IsAlive( (uint)owner, (uint)(owner >> 32) );
			}
		}

		drainingBlocks.Clear();
		owners.Clear();
		return any;
	}

	readonly struct BuffersIn( HashSet<BufferHandle> buffers ) : IBindingFilter
	{
		public bool Names( in Binding binding ) => (binding.Type is BindingType.UniformBuffer or BindingType.UniformBufferDynamic or BindingType.StorageBuffer) && buffers.Contains( binding.Buffer );
	}

	/// <summary>
	/// Move a device buffer into room other blocks have: a new buffer is made and copied into on <paramref name="list"/>,
	/// then the two swap their Vulkan buffers and memory, so the handle names the new one and the old is freed with a
	/// stand-in handle. False when it can't move (not device memory, pinned, or no room without a new block).
	/// </summary>
	bool MoveBuffer( BufferHandle buffer, ref CommandList list )
	{
		// An allocation whose buffer was destroyed is freed with its frame: it goes by itself
		if ( !Buffers.IsAlive( buffer.Index, buffer.Generation ) ) return true;
		ref var record = ref Get( buffer );
		if ( record.Location != MemoryLocation.Device || record.Pinned ) return false;

		BufferHandle copy;
		MemoryAllocator.ExistingBlocksOnly = true;
		try
		{
			copy = CreateBuffer( new BufferInfo { Size = record.Size, Usage = record.Usage, Memory = record.Location, Name = record.Name } );
		}
		catch ( GpuException e ) when ( e.Result == (int)VkResult.ErrorFragmentedPool )
		{
			return false;
		}
		finally
		{
			MemoryAllocator.ExistingBlocksOnly = false;
		}

		if ( list is null )
		{
			list = BeginCommandList( QueueType.Graphics, "Relocation" );
			list.Barrier( PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferRead );
		}

		list.CopyBuffer( buffer, 0, copy, 0, record.Size );

		// Making the copy may have moved nothing (records live in fixed pages), but read both again to be plain about it
		ref var moved = ref Get( buffer );
		ref var stand = ref Get( copy );
		(moved.Buffer, stand.Buffer) = (stand.Buffer, moved.Buffer);
		(moved.Memory, stand.Memory) = (stand.Memory, moved.Memory);
		Allocator.SetOwner( moved.Memory, OwnerOf( buffer ) );
		Allocator.SetOwner( stand.Memory, OwnerOf( copy ) );

		if ( moved.BindlessSlot > 0 ) Bindless.SetBufferSlot( moved.BindlessSlot - 1, moved.Buffer, (ulong)moved.Size );
		if ( buffer == vertexCacheBuffer ) Bindless.SetBuffer( ShaderAbi.BindlessVertexCache, moved.Buffer, (ulong)moved.Size );
		if ( buffer == pipelineTexturesBuffer ) Bindless.SetBuffer( ShaderAbi.BindlessPipelineTextures, moved.Buffer, (ulong)moved.Size );

		movedBuffers.Add( buffer );
		movedFrom.Add( copy );
		RelocatedBuffers++;
		RelocatedBufferBytes += moved.Size;
		return true;
	}

	/// <summary>What an allocation holds, for <see cref="MemoryAllocator.SetOwner"/>: a handle's index and generation.</summary>
	static ulong OwnerOf( BufferHandle buffer ) => buffer.Index | ((ulong)buffer.Generation << 32);

	/// <inheritdoc cref="OwnerOf(BufferHandle)"/>
	static ulong OwnerOf( TextureHandle texture ) => texture.Index | ((ulong)texture.Generation << 32);

	/// <summary>Free a buffer at once: only when no pending work can use it.</summary>
	void FreeBufferNow( in BufferRecord record )
	{
		if ( record.BindlessSlot > 0 ) Bindless.RemoveBuffer( record.BindlessSlot - 1 );
		Vk.vkDestroyBuffer( Vk.Device, record.Buffer, null );
		Allocator.Free( record.Memory );
	}
}
