using System.Numerics;
using System.Threading;

namespace Borealis.Source2;

/// <summary>
/// Arrays that content is read and decoded into while it streams (a texture file's bytes, its mips), used again rather than
/// left for the collector: a map's textures streaming in made megabytes of large-object garbage a second. Sizes round up to
/// a power of two. At most <see cref="MaxKeptBytes"/> are kept between uses: when they're full, arrays of the size least
/// lately asked for make room for one more lately asked for (what's streamed moves from coarse mips to fine and back), then
/// those of the size holding the most (a load leaves hundreds of small mips' arrays kept, which mustn't crowd out the big
/// file buffers streaming rents now and then), and past that, and above <see cref="LargestPooled"/>, returned arrays are let
/// go. Any thread.
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

		Volatile.Write( ref lastRentTicks, System.Diagnostics.Stopwatch.GetTimestamp() );
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
			if ( !LetGoStalest( index ) && !LetGoFullest( index, array.Length ) )
			{
				Interlocked.Add( ref droppedBytes, array.Length );
				return;
			}
		}

		Interlocked.Add( ref keptBytes, array.Length );
		var stack = free[index];
		lock ( stack ) stack.Push( array );
	}

	/// <summary>
	/// With no size rented less lately than the one at <paramref name="index"/>, let go of one kept array of the size whose kept
	/// arrays add up to the most, if that's more than <paramref name="length"/>: a big file's buffer, rented less often than the
	/// mips streaming decodes, was otherwise always the one let go, and made again for the next big file while hundreds of
	/// small mips' arrays from the load stayed kept.
	/// </summary>
	static bool LetGoFullest( int index, int length )
	{
		int fullest = -1;
		long most = length;
		for ( int i = 0; i < free.Length; i++ )
		{
			if ( i == index ) continue;
			var bytes = (long)free[i].Count << (i + SmallestShift);
			if ( bytes <= most ) continue;
			most = bytes;
			fullest = i;
		}

		if ( fullest < 0 ) return false;
		var stack = free[fullest];
		lock ( stack )
		{
			if ( !stack.TryPop( out var old ) ) return true;
			Interlocked.Add( ref keptBytes, -old.Length );
			Interlocked.Add( ref droppedBytes, old.Length );
		}

		return true;
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

	static long lastRentTicks;

	/// <summary>Seconds without a rent after which <see cref="TrimIdle"/> lets the kept arrays go.</summary>
	public const double IdleSeconds = 30;

	/// <summary>Let the kept arrays go once nothing has been rented for <paramref name="seconds"/>: nothing streams (a map has
	/// gone, or everything it shows is in). Cheap enough for every frame.</summary>
	public static void TrimIdle( double seconds = IdleSeconds )
	{
		if ( Interlocked.Read( ref keptBytes ) == 0 ) return;
		if ( System.Diagnostics.Stopwatch.GetElapsedTime( Volatile.Read( ref lastRentTicks ) ).TotalSeconds < seconds ) return;
		Clear();
	}

	/// <summary>Let every kept array go.</summary>
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
