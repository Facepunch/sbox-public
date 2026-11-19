using Borealis.Gpu.Memory;
using System.Diagnostics;

namespace BorealisGpuTests;

/// <summary>
/// A memory block's free-space lookup (two-level segregated fit), without a GPU.
/// </summary>
[TestClass]
public class MemoryBlockTests
{
	/// <summary>
	/// Allocation honours alignment, leaving the padding before it free, and freeing merges neighbours back into one range.
	/// </summary>
	[TestMethod]
	public void BlocksAllocateAlignedAndCoalesce()
	{
		var block = new MemoryBlock( 1024 );

		Assert.IsTrue( block.TryAllocate( 100, 1, out var a, out var aNode ) );
		Assert.IsTrue( block.TryAllocate( 100, 256, out var b, out var bNode ) );
		Assert.IsTrue( block.TryAllocate( 300, 64, out var c, out var cNode ) );
		Assert.AreEqual( 0ul, a );
		Assert.AreEqual( 256ul, b );
		Assert.AreEqual( 384ul, c );
		Assert.AreEqual( 500ul, block.Used );
		Assert.IsFalse( block.TryAllocate( 400, 1, out _, out _ ), "only 340 bytes are left at the end" );
		Assert.IsTrue( block.Fits( 156, 1 ), "the padding before the aligned allocation is free" );
		Assert.AreEqual( 340ul, block.LargestFree );

		block.Release( bNode );
		block.Release( aNode );
		block.Release( cNode );
		Assert.AreEqual( 1, block.FreeRanges );
		CollectionAssert.AreEqual( new[] { (0ul, 1024ul) }, block.FreeList() );
		Assert.AreEqual( 0ul, block.Used );
	}

	/// <summary>
	/// Random allocations and frees against a byte map of what's taken: nothing handed out overlaps, every offset is
	/// aligned, free ranges never touch, and a range that fits is always found.
	/// </summary>
	[TestMethod]
	public void RandomChurnKeepsRangesConsistent()
	{
		const ulong Size = 1 << 16;
		var random = new Random( 7 );
		var block = new MemoryBlock( Size );
		var taken = new bool[Size];
		var live = new List<(ulong Offset, ulong Size, int Node)>();

		for ( int step = 0; step < 20000; step++ )
		{
			if ( live.Count > 0 && random.Next( 2 ) == 0 )
			{
				var index = random.Next( live.Count );
				var (offset, size, node) = live[index];
				live[index] = live[^1];
				live.RemoveAt( live.Count - 1 );
				block.Release( node );
				for ( var i = offset; i < offset + size; i++ ) taken[i] = false;
			}
			else
			{
				var size = (ulong)random.Next( 1, 2048 );
				var alignment = 1ul << random.Next( 0, 10 );
				if ( block.TryAllocate( size, alignment, out var offset, out var node ) )
				{
					Assert.AreEqual( 0ul, offset % alignment, "aligned" );
					for ( var i = offset; i < offset + size; i++ )
					{
						Assert.IsFalse( taken[i], "allocations don't overlap" );
						taken[i] = true;
					}

					live.Add( (offset, size, node) );
				}
				else
				{
					// A free range of at least size + alignment - 1 always fits; the lookup can miss tighter ones
					Assert.IsFalse( FitsSomewhere( taken, size + alignment - 1, 1 ), $"{size} at {alignment} has room" );
				}
			}

			if ( step % 997 == 0 ) CheckRanges( block, taken );
		}

		foreach ( var (_, _, node) in live ) block.Release( node );
		Assert.AreEqual( 1, block.FreeRanges );
		Assert.AreEqual( 0ul, block.Used );
	}

	static bool FitsSomewhere( bool[] taken, ulong size, ulong alignment )
	{
		for ( ulong start = 0; start + size <= (ulong)taken.Length; start += alignment )
		{
			var free = true;
			for ( var i = start; i < start + size && free; i++ ) free = !taken[i];
			if ( free ) return true;
		}

		return false;
	}

	// The free list is exactly the untaken bytes, in ranges that don't touch
	static void CheckRanges( MemoryBlock block, bool[] taken )
	{
		var expected = new List<(ulong, ulong)>();
		for ( ulong i = 0; i < (ulong)taken.Length; )
		{
			if ( taken[i] )
			{
				i++;
				continue;
			}

			var start = i;
			while ( i < (ulong)taken.Length && !taken[i] ) i++;
			expected.Add( (start, i - start) );
		}

		CollectionAssert.AreEqual( expected, block.FreeList() );
		Assert.AreEqual( expected.Count, block.FreeRanges );
	}

	/// <summary>
	/// Allocate and free cost in a block kept about 90% full with sizes from <paramref name="smallest"/> to
	/// <paramref name="largest"/> (log-uniform), freeing and refilling 64 at a time. Prints nanoseconds per call.
	/// </summary>
	[TestMethod]
	[TestCategory( "Slow" )]
	[DataRow( 16ul << 20, 256ul, 256ul << 10, 256ul, DisplayName = "small block, buffers" )]
	[DataRow( 128ul << 20, 4ul << 10, 8ul << 20, 65536ul, DisplayName = "large block, textures" )]
	[DataRow( 128ul << 20, 256ul, 1ul << 20, 4096ul, DisplayName = "large block, mixed" )]
	public void Churn( ulong blockSize, ulong smallest, ulong largest, ulong alignment )
	{
		var random = new Random( 1234 );
		ulong Size()
		{
			var log = Math.Log2( smallest ) + random.NextDouble() * (Math.Log2( largest ) - Math.Log2( smallest ));
			return Math.Max( (ulong)Math.Pow( 2, log ), 1 );
		}

		ulong Alignment() => random.Next( 4 ) == 0 ? 256 : alignment;

		var block = new MemoryBlock( blockSize );
		var live = new List<int>();
		while ( block.Used < blockSize * 9 / 10 )
		{
			if ( !block.TryAllocate( Size(), Alignment(), out _, out var node ) ) break;
			live.Add( node );
		}

		const int Batch = 64;
		var sizes = new (ulong Size, ulong Alignment)[Batch];
		var freeing = new int[Batch];
		var placed = new int[Batch];
		long allocateTicks = 0, freeTicks = 0, allocations = 0, frees = 0, failed = 0;
		for ( int round = 0; round < 4000; round++ )
		{
			// Free a batch at random
			var count = Math.Min( Batch, live.Count );
			for ( int i = 0; i < count; i++ )
			{
				var index = random.Next( live.Count );
				freeing[i] = live[index];
				live[index] = live[^1];
				live.RemoveAt( live.Count - 1 );
			}

			var start = Stopwatch.GetTimestamp();
			for ( int i = 0; i < count; i++ ) block.Release( freeing[i] );
			freeTicks += Stopwatch.GetTimestamp() - start;
			frees += count;

			// And allocate a batch of new sizes, as many as fit
			for ( int i = 0; i < Batch; i++ ) sizes[i] = (Size(), Alignment());
			var placedCount = 0;
			start = Stopwatch.GetTimestamp();
			foreach ( var (size, align) in sizes )
			{
				if ( block.TryAllocate( size, align, out _, out var node ) ) placed[placedCount++] = node;
				else failed++;
			}

			allocateTicks += Stopwatch.GetTimestamp() - start;
			allocations += Batch;
			for ( int i = 0; i < placedCount; i++ ) live.Add( placed[i] );
		}

		var tick = 1e9 / Stopwatch.Frequency;
		Console.WriteLine( $"allocate {allocateTicks * tick / allocations:F0} ns, free {freeTicks * tick / frees:F0} ns, {live.Count} live, {100.0 * block.Used / blockSize:F0}% used, {failed} of {allocations} didn't fit, {block.FreeRanges} free ranges" );
	}
}
