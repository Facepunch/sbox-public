using Sandbox.Rendering;

namespace Sandbox.Diagnostics;

/// <summary>
/// The GPU allocator's blocks and what's in each, as they were when captured. Memory comes from the driver a block at
/// a time and a block only goes back once it's empty, so a few small allocations can hold a whole one. This is where to
/// see which. Capturing walks the whole allocator, so it's done when asked for, never every frame.
/// </summary>
internal sealed class GpuMemoryMap
{
	internal enum Kind
	{
		Texture,
		RenderTarget,
		Buffer,
		Other,
	}

	internal readonly record struct Allocation( long Offset, long Size, Kind Kind, string Name );

	/// <summary>
	/// Neighbouring allocations of one kind, as one stretch to draw.
	/// </summary>
	internal readonly record struct Run( long Offset, long Size, Kind Kind );

	internal sealed class Block
	{
		public long Size;
		public long Used;
		public long LargestFree;

		/// <summary>
		/// Blocks in the shared pools can have their contents moved out to empty them. The small pools for things that
		/// can't move are left as they are.
		/// </summary>
		public bool Movable;

		/// <summary>
		/// In offset order.
		/// </summary>
		public readonly List<Allocation> Allocations = new();
		public readonly List<Run> Runs = new();

		public float Fill => Size > 0 ? (float)Used / Size : 0;

		/// <summary>
		/// What's at an offset: the allocation there, or nothing for free space.
		/// </summary>
		public Allocation? At( long offset )
		{
			int lo = 0, hi = Allocations.Count - 1;
			while ( lo <= hi )
			{
				var mid = (lo + hi) / 2;
				var a = Allocations[mid];
				if ( offset < a.Offset ) hi = mid - 1;
				else if ( offset >= a.Offset + a.Size ) lo = mid + 1;
				else return a;
			}

			return null;
		}

		public long Bytes( Kind kind )
		{
			long bytes = 0;
			foreach ( var a in Allocations )
				if ( a.Kind == kind ) bytes += a.Size;
			return bytes;
		}
	}

	/// <summary>
	/// Emptiest first: those are the ones holding memory for little.
	/// </summary>
	public readonly List<Block> Blocks = new();

	/// <summary>
	/// Allocations with memory of their own rather than a slice of a block, biggest first. Freed the moment they go.
	/// </summary>
	public readonly List<Allocation> Dedicated = new();

	public long BlockBytes;
	public long UsedBytes;
	public long DedicatedBytes;
	public long UnusedBytes => BlockBytes - UsedBytes;

	/// <summary>
	/// What the driver says this process uses and may use, which is what the system sees.
	/// </summary>
	public long DriverUsage;
	public long DriverBudget;

	public double CapturedAt;

	/// <summary>
	/// Null where there's no allocator to ask: a headless run, or a process without Borealis.Gpu.
	/// </summary>
	public static GpuMemoryMap Capture()
	{
		if ( RenderBackend.Default is not BorealisGpuRenderBackend { Device: { } device } ) return null;

		var map = new GpuMemoryMap { CapturedAt = RealTime.Now };

		Span<Borealis.Gpu.MemoryHeapBudget> heaps = stackalloc Borealis.Gpu.MemoryHeapBudget[16];
		var count = Math.Min( device.GetMemoryHeaps( heaps ), heaps.Length );
		foreach ( var heap in heaps[..count] )
		{
			if ( !heap.DeviceLocal ) continue;
			map.DriverUsage += heap.Usage;
			map.DriverBudget += heap.Budget;
		}

		foreach ( var from in device.MemoryMap() )
		{
			if ( from.Dedicated )
			{
				map.DedicatedBytes += from.Size;
				map.Dedicated.Add( new Allocation( 0, from.Size, from.Images ? Kind.RenderTarget : Kind.Buffer, null ) );
				continue;
			}

			var block = new Block { Size = from.Size, Used = from.Used, LargestFree = from.LargestFree, Movable = from.Content };
			var kind = from.Images ? Kind.Texture : Kind.Buffer;
			foreach ( var (offset, size) in from.Allocations )
				block.Allocations.Add( new Allocation( offset, size, kind, null ) );

			BuildRuns( block );
			map.BlockBytes += block.Size;
			map.UsedBytes += block.Used;
			map.Blocks.Add( block );
		}

		map.Blocks.Sort( ( a, b ) => a.Fill.CompareTo( b.Fill ) );
		map.Dedicated.Sort( ( a, b ) => b.Size.CompareTo( a.Size ) );
		return map;
	}

	/// <summary>
	/// A block can hold thousands of allocations, far more than there are pixels to draw them in. Join neighbours of
	/// one kind, across gaps too small to see.
	/// </summary>
	static void BuildRuns( Block block )
	{
		var tiny = block.Size / 4096;
		foreach ( var a in block.Allocations )
		{
			if ( block.Runs.Count > 0 )
			{
				var last = block.Runs[^1];
				if ( last.Kind == a.Kind && a.Offset - (last.Offset + last.Size) <= tiny )
				{
					block.Runs[^1] = last with { Size = a.Offset + a.Size - last.Offset };
					continue;
				}
			}

			block.Runs.Add( new Run( a.Offset, a.Size, a.Kind ) );
		}
	}

	public static string Describe( Kind kind ) => kind switch
	{
		Kind.Texture => "texture",
		Kind.RenderTarget => "render target",
		Kind.Buffer => "buffer",
		_ => "other",
	};
}
