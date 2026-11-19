using System.Numerics;
using System.Threading;

namespace Borealis.Source2;

/// <summary>
/// Arrays that content is read and decoded into while it streams (a texture file's bytes, its mips), used again rather than
/// left for the collector: a map's textures streaming in made megabytes of large-object garbage a second. Sizes round up to
/// a power of two. At most <see cref="MaxKeptBytes"/> are kept between uses: when they're full, arrays of the size least
/// lately asked for make room for one more lately asked for (what's streamed moves from coarse mips to fine and back), and
/// past that, and above <see cref="LargestPooled"/>, returned arrays are let go. Any thread.
/// </summary>
public static class ContentBuffers
{
	const int SmallestShift = 8, LargestShift = 26;

	/// <summary>The largest array kept between uses (64 MiB); larger ones are made each time.</summary>
	public const int LargestPooled = 1 << LargestShift;

	static readonly Stack<byte[]>[] free = MakeFree();
	static long keptBytes;

	// Each size's last rent, in rents so far, for which to let go when full
	static readonly long[] lastRent = new long[LargestShift - SmallestShift + 1];
	static long rents;

	static Stack<byte[]>[] MakeFree()
	{
		var stacks = new Stack<byte[]>[LargestShift - SmallestShift + 1];
		for ( int i = 0; i < stacks.Length; i++ ) stacks[i] = new Stack<byte[]>();
		return stacks;
	}

	/// <summary>Bytes of returned arrays kept for the next rent at most (128 MiB).</summary>
	public static long MaxKeptBytes { get; set; } = 128L << 20;

	/// <summary>Bytes of arrays kept now, for tests and stats.</summary>
	public static long KeptBytes => Interlocked.Read( ref keptBytes );

	static long madeBytes, droppedBytes;

	/// <summary>Bytes of arrays made because none was kept that fitted, so far: what streaming still allocates.</summary>
	public static long MadeBytes => Interlocked.Read( ref madeBytes );

	/// <summary>Bytes of returned arrays let go because <see cref="MaxKeptBytes"/> were kept already, so far.</summary>
	public static long DroppedBytes => Interlocked.Read( ref droppedBytes );

	static int ShiftOf( int size ) => Math.Max( BitOperations.Log2( (uint)Math.Max( size, 1 ) - 1 ) + 1, SmallestShift );

	/// <summary>An array of at least <paramref name="size"/> bytes, its contents undefined. Give it back with <see cref="Return"/>.</summary>
	public static byte[] Rent( int size )
	{
		if ( size > LargestPooled )
		{
			Interlocked.Add( ref madeBytes, size );
			return GC.AllocateUninitializedArray<byte>( size );
		}

		var shift = ShiftOf( size );
		var stack = free[shift - SmallestShift];
		Volatile.Write( ref lastRent[shift - SmallestShift], Interlocked.Increment( ref rents ) );
		lock ( stack )
		{
			if ( stack.TryPop( out var kept ) )
			{
				Interlocked.Add( ref keptBytes, -kept.Length );
				return kept;
			}
		}

		Interlocked.Add( ref madeBytes, 1L << shift );
		return GC.AllocateUninitializedArray<byte>( 1 << shift );
	}

	/// <summary>An array from <see cref="Rent"/> is no longer used: kept for the next rent while there's room.</summary>
	public static void Return( byte[] array )
	{
		if ( array is null || array.Length > LargestPooled || !BitOperations.IsPow2( array.Length ) || array.Length < 1 << SmallestShift ) return;
		var index = BitOperations.Log2( (uint)array.Length ) - SmallestShift;
		while ( Interlocked.Read( ref keptBytes ) + array.Length > MaxKeptBytes )
		{
			if ( !LetGoStalest( index ) )
			{
				Interlocked.Add( ref droppedBytes, array.Length );
				return;
			}
		}

		Interlocked.Add( ref keptBytes, array.Length );
		var stack = free[index];
		lock ( stack ) stack.Push( array );
	}

	// Let go of one kept array of the size least lately rented, if that's longer ago than the size at index
	static bool LetGoStalest( int index )
	{
		int stalest = -1;
		var oldest = Volatile.Read( ref lastRent[index] );
		for ( int i = 0; i < free.Length; i++ )
		{
			if ( i == index || free[i].Count == 0 ) continue;
			var last = Volatile.Read( ref lastRent[i] );
			if ( last >= oldest ) continue;
			oldest = last;
			stalest = i;
		}

		if ( stalest < 0 ) return false;
		var stack = free[stalest];
		lock ( stack )
		{
			if ( !stack.TryPop( out var old ) ) return true;
			Interlocked.Add( ref keptBytes, -old.Length );
			Interlocked.Add( ref droppedBytes, old.Length );
		}

		return true;
	}

	/// <summary>Let every kept array go, for tests.</summary>
	public static void Clear()
	{
		foreach ( var stack in free )
		{
			lock ( stack )
			{
				while ( stack.TryPop( out var array ) ) Interlocked.Add( ref keptBytes, -array.Length );
			}
		}
	}
}
