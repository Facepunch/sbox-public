using Borealis.Gpu;
using System.Runtime.InteropServices;

namespace Borealis.Backend;

/// <summary>
/// What the GPU culls let through, for the draw counters: cumulative counters the culling shaders add to (a slot a render
/// system, a pass kind of each: batches with a surviving fragment, surviving fragments, their triangles), copied to a ring of
/// readback buffers each frame. A frame's counts are the difference of two copies, a few frames late.
/// </summary>
internal sealed class CullStats : IDisposable
{
	/// <summary>Render systems that count; slot 0 is unused, which the shaders read as "no slot".</summary>
	public const int Slots = 16;

	/// <summary>Bytes of a slot: three pass kinds (forward, depth, shadow) of 16 bytes: batches, fragments, triangles' low and high words (borealis_gpu_driven_cull_cs, borealis_resident_cull_cs).</summary>
	public const int SlotBytes = 64;

	const int Words = Slots * SlotBytes / 4;
	const int Ring = 4;

	readonly Device gpu;
	readonly BufferHandle[] ring = new BufferHandle[Ring];
	readonly uint[] last = new uint[Words];
	readonly CulledCounts[] counts = new CulledCounts[Slots];
	readonly object gate = new();
	int taken = 1;
	bool haveLast;
	long frame;

	/// <summary>The counters the culls add to.</summary>
	public BufferHandle Buffer { get; }

	public CullStats( Device gpu )
	{
		this.gpu = gpu;
		Buffer = gpu.CreateBuffer( new BufferInfo { Size = Slots * SlotBytes, Usage = BufferUsage.Storage | BufferUsage.CopyDestination | BufferUsage.CopySource, Name = "Borealis cull stats" } );
		var list = gpu.BeginCommandList( QueueType.Graphics, "Cull stats clear" );
		list.FillBuffer( Buffer, 0 );
		list.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
		gpu.Submit( list );
	}

	/// <summary>A free slot, 1 to <see cref="Slots"/> - 1, or 0 when they're all taken.</summary>
	public int Acquire()
	{
		lock ( gate )
		{
			for ( int slot = 1; slot < Slots; slot++ )
			{
				if ( (taken & 1 << slot) != 0 ) continue;
				taken |= 1 << slot;
				counts[slot] = default;
				return slot;
			}
		}

		return 0;
	}

	public void Release( int slot )
	{
		if ( slot <= 0 ) return;
		lock ( gate ) taken &= ~(1 << slot);
	}

	/// <summary>The slot's counts for the frame whose copy came back last.</summary>
	public CulledCounts Counts( int slot ) => counts[slot];

	/// <summary>
	/// At a frame's start: take the counts of the copy made <see cref="Ring"/> frames ago, then copy the counters for a later
	/// one. The copy goes with the queue's next submission (<see cref="Device.SubmitLater"/>), in the same place in its order.
	/// </summary>
	public void BeginFrame()
	{
		var at = (int)(frame++ % Ring);
		if ( ring[at].IsValid ) Receive( MemoryMarshal.Cast<byte, uint>( gpu.Map( ring[at] )[..(Words * 4)] ) );
		else ring[at] = gpu.CreateBuffer( new BufferInfo { Size = Words * 4, Usage = BufferUsage.CopyDestination, Memory = MemoryLocation.Readback, Name = "Borealis cull stats readback" } );

		var list = gpu.BeginCommandList( QueueType.Graphics, "Cull stats" );
		list.Barrier( PipelineStage.AllCommands, MemoryAccess.ShaderWrite, PipelineStage.Transfer, MemoryAccess.TransferRead );
		list.CopyBuffer( Buffer, 0, ring[at], 0, Words * 4 );
		list.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.Host, MemoryAccess.HostRead );
		gpu.SubmitLater( list );
	}

	void Receive( ReadOnlySpan<uint> now )
	{
		if ( haveLast )
		{
			for ( int slot = 1; slot < Slots; slot++ )
			{
				var at = slot * SlotBytes / 4;
				var forward = Triangles( now, last, at );
				var depth = Triangles( now, last, at + 4 );
				var shadow = Triangles( now, last, at + 8 );
				counts[slot] = new CulledCounts
				{
					Draws = (int)(now[at] - last[at]),
					Instances = (int)(now[at + 1] - last[at + 1]),
					Triangles = forward,
					DepthDraws = (int)(now[at + 4] - last[at + 4]),
					ShadowDraws = (int)(now[at + 8] - last[at + 8]),
					DrawnTriangles = forward + depth + shadow,
				};
			}
		}

		now.CopyTo( last );
		haveLast = true;
	}

	static long Triangles( ReadOnlySpan<uint> now, ReadOnlySpan<uint> before, int at )
		=> (long)((ulong)now[at + 3] << 32 | now[at + 2]) - (long)((ulong)before[at + 3] << 32 | before[at + 2]);

	public void Dispose()
	{
		gpu.Destroy( Buffer );
		foreach ( var buffer in ring )
		{
			if ( buffer.IsValid ) gpu.Destroy( buffer );
		}
	}
}
