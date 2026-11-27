using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Borealis.Backend;

/// <summary>
/// What the scratch arrays of LOD and meshlet builds and the cache reads around them (<see cref="ScratchPool{T}"/>) keep
/// between uses, and letting it go once nothing has rented any for a while. Kept in <c>ArrayPool&lt;T&gt;.Shared</c>,
/// Construct's load left about 240 MB there for minutes: that pool lets go only on full collections, and slowly.
/// </summary>
public static class ScratchPools
{
	/// <summary>Seconds without a rent after which <see cref="TrimIdle"/> lets the kept arrays go.</summary>
	public const double IdleSeconds = 3;

	/// <summary>Bytes of returned arrays kept for the next rent at most, across every element type (512 MiB).</summary>
	public static long MaxKeptBytes { get; set; } = 512L << 20;

	internal static long keptBytes, lastRentTicks;

	/// <summary>Bytes of arrays kept now, for tests and stats.</summary>
	public static long KeptBytes => Interlocked.Read( ref keptBytes );

	// Each element type's Clear, added as its pool is first used
	static Action[] clears = [];
	static readonly Lock adding = new();

	internal static void Add( Action clear )
	{
		lock ( adding ) Volatile.Write( ref clears, [.. clears, clear] );
	}

	/// <summary>Let the kept arrays go once nothing has been rented for <paramref name="seconds"/>. Cheap enough for every frame.</summary>
	public static void TrimIdle( double seconds = IdleSeconds )
	{
		if ( Interlocked.Read( ref keptBytes ) == 0 ) return;
		if ( System.Diagnostics.Stopwatch.GetElapsedTime( Volatile.Read( ref lastRentTicks ) ).TotalSeconds < seconds ) return;
		Clear();
	}

	/// <summary>Let every kept array go.</summary>
	public static void Clear()
	{
		foreach ( var clear in Volatile.Read( ref clears ) ) clear();
	}
}

/// <summary>
/// Scratch arrays of <typeparamref name="T"/> for LOD and meshlet builds: rented, given back, and kept for the next rent
/// within <see cref="ScratchPools.MaxKeptBytes"/> until <see cref="ScratchPools.TrimIdle"/> lets them go. Sizes round up
/// to a power of two; past <see cref="LargestPooled"/> elements they're made each time. Any thread.
/// </summary>
internal static class ScratchPool<T>
{
	const int SmallestShift = 4, LargestShift = 28;

	/// <summary>The most elements an array kept between uses has.</summary>
	public const int LargestPooled = 1 << LargestShift;

	static readonly Stack<T[]>[] free = MakeFree();

	static Stack<T[]>[] MakeFree()
	{
		var stacks = new Stack<T[]>[LargestShift - SmallestShift + 1];
		for ( int i = 0; i < stacks.Length; i++ ) stacks[i] = new Stack<T[]>();
		ScratchPools.Add( Clear );
		return stacks;
	}

	static long BytesOf( T[] array ) => (long)array.Length * Unsafe.SizeOf<T>();

	static int ShiftOf( int length ) => Math.Max( BitOperations.Log2( (uint)Math.Max( length, 1 ) - 1 ) + 1, SmallestShift );

	/// <summary>An array of at least <paramref name="length"/> elements, its contents undefined. Give it back with <see cref="Return"/>.</summary>
	public static T[] Rent( int length )
	{
		if ( length > LargestPooled ) return GC.AllocateUninitializedArray<T>( length );

		Volatile.Write( ref ScratchPools.lastRentTicks, System.Diagnostics.Stopwatch.GetTimestamp() );
		var shift = ShiftOf( length );
		var stack = free[shift - SmallestShift];
		lock ( stack )
		{
			if ( stack.TryPop( out var kept ) )
			{
				Interlocked.Add( ref ScratchPools.keptBytes, -BytesOf( kept ) );
				return kept;
			}
		}

		return GC.AllocateUninitializedArray<T>( 1 << shift );
	}

	/// <summary>An array from <see cref="Rent"/> is no longer used: kept for the next rent while there's room.</summary>
	public static void Return( T[] array )
	{
		if ( array is null || array.Length > LargestPooled || array.Length < 1 << SmallestShift || !BitOperations.IsPow2( array.Length ) ) return;
		var bytes = BytesOf( array );
		if ( Interlocked.Add( ref ScratchPools.keptBytes, bytes ) > ScratchPools.MaxKeptBytes )
		{
			Interlocked.Add( ref ScratchPools.keptBytes, -bytes );
			return;
		}

		var stack = free[BitOperations.Log2( (uint)array.Length ) - SmallestShift];
		lock ( stack ) stack.Push( array );
	}

	static void Clear()
	{
		foreach ( var stack in free )
		{
			lock ( stack )
			{
				while ( stack.TryPop( out var array ) ) Interlocked.Add( ref ScratchPools.keptBytes, -BytesOf( array ) );
			}
		}
	}
}
