using System.Numerics;
using System.Threading;

namespace Borealis.Source2;

/// <summary>
/// A piece of an array from <see cref="ContentBuffers"/>: <see cref="Length"/> bytes from <see cref="Offset"/> in
/// <see cref="Array"/>, which other pieces share. Give it back with <see cref="ContentBuffers.Return"/>, once.
/// </summary>
public readonly struct ContentBuffer
{
	internal ContentBuffer( byte[] array, int offset, int length, int capacity )
	{
		Array = array;
		Offset = offset;
		Length = length;
		Capacity = capacity;
	}

	/// <summary>The array the piece is in; null for none.</summary>
	public byte[] Array { get; }

	/// <summary>Where the piece starts in <see cref="Array"/>.</summary>
	public int Offset { get; }

	/// <summary>Bytes asked for (or read into it).</summary>
	public int Length { get; }

	/// <summary>Bytes of the piece, at least <see cref="Length"/>.</summary>
	public int Capacity { get; }

	/// <summary>Whether there's no piece.</summary>
	public bool IsNull => Array is null;

	/// <summary>Its <see cref="Length"/> bytes.</summary>
	public Span<byte> Span => Array is null ? default : Array.AsSpan( Offset, Length );

	/// <summary>The same piece with <paramref name="length"/> bytes of it used, at most <see cref="Capacity"/>.</summary>
	public ContentBuffer WithLength( int length )
	{
		ArgumentOutOfRangeException.ThrowIfGreaterThan( length, Capacity );
		return new ContentBuffer( Array, Offset, length, Capacity );
	}
}

/// <summary>
/// Memory content is read and decoded into while it streams (a texture file's bytes, its mips), used again rather than left
/// for the collector: a map's textures streaming in made megabytes of large-object garbage a second. Pieces up to
/// <see cref="SlabBytes"/> are cut from a few slabs of that size, each split in halves down to the power of two a piece
/// needs and joined again as pieces come back, so reads of every size share the same arrays; larger ones, up to
/// <see cref="LargestPooled"/>, are arrays of their own, kept by size. While content streams it holds up to
/// <see cref="BusyKeptBytes"/> (a load reads hundreds of megabytes at once); <see cref="TrimIdle"/> lets what's free go down
/// to <see cref="MaxKeptBytes"/> once nothing has been rented for <see cref="QuietSeconds"/>, and all of it after
/// <see cref="IdleSeconds"/>. Past that, pieces are arrays made and let go. Any thread.
/// </summary>
public static class ContentBuffers
{
	const int MinShift = 12, SlabShift = 24, LargestShift = 26;
	const int Orders = SlabShift - MinShift + 1;

	/// <summary>Bytes of a slab (16 MiB): the largest piece cut from one.</summary>
	public const int SlabBytes = 1 << SlabShift;

	/// <summary>The largest array kept between uses (64 MiB); larger ones are made each time.</summary>
	public const int LargestPooled = 1 << LargestShift;

	sealed class Slab
	{
		public byte[] Array;

		// Per order (a piece of 4 KiB << order), a bit per piece of that size that's free whole
		public ulong[][] Free;
		public int FreeBytes;
	}

	static readonly object gate = new();
	static readonly List<Slab> slabs = [];

	// Arrays above a slab, per size (32 and 64 MiB)
	static readonly Stack<byte[]>[] large = [new(), new()];

	static long slabBytes, freeSlabBytes, largeKeptBytes;
	static long madeBytes, droppedBytes, outBytes, peakOutBytes;
	static long lastRentTicks;

	/// <summary>Bytes held between uses once nothing has been rented for <see cref="QuietSeconds"/> (128 MiB).</summary>
	public static long MaxKeptBytes { get; set; } = 128L << 20;

	/// <summary>Bytes of slabs and kept arrays held at most while content streams (512 MiB).</summary>
	public static long BusyKeptBytes { get; set; } = 512L << 20;

	/// <summary>Seconds without a rent after which <see cref="TrimIdle"/> lets go down to <see cref="MaxKeptBytes"/>.</summary>
	public static double QuietSeconds { get; set; } = 2;

	/// <summary>Seconds without a rent after which <see cref="TrimIdle"/> lets everything free go.</summary>
	public const double IdleSeconds = 30;

	/// <summary>Bytes free for the next rent now (free pieces of slabs and kept arrays), for tests and stats.</summary>
	public static long KeptBytes => Interlocked.Read( ref freeSlabBytes ) + Interlocked.Read( ref largeKeptBytes );

	/// <summary>Bytes of slabs and kept arrays held now, pieces out included, for tests and stats.</summary>
	public static long HeldBytes => Interlocked.Read( ref slabBytes ) + Interlocked.Read( ref largeKeptBytes );

	/// <summary>Bytes of slabs and arrays made because nothing kept fitted, so far: what streaming still allocates.</summary>
	public static long MadeBytes => Interlocked.Read( ref madeBytes );

	/// <summary>Bytes let go: slabs and kept arrays trimmed, and pieces given back that weren't kept.</summary>
	public static long DroppedBytes => Interlocked.Read( ref droppedBytes );

	/// <summary>Bytes of pieces rented and not given back yet, for stats.</summary>
	public static long OutBytes => Interlocked.Read( ref outBytes );

	/// <summary>The most <see cref="OutBytes"/> at once so far, for stats.</summary>
	public static long PeakOutBytes => Interlocked.Read( ref peakOutBytes );

	static void CountOut( long bytes )
	{
		var now = Interlocked.Add( ref outBytes, bytes );
		for ( var peak = Interlocked.Read( ref peakOutBytes ); now > peak; peak = Interlocked.Read( ref peakOutBytes ) )
		{
			if ( Interlocked.CompareExchange( ref peakOutBytes, now, peak ) == peak ) break;
		}
	}

	static int ShiftOf( int size ) => Math.Max( BitOperations.Log2( (uint)Math.Max( size, 1 ) - 1 ) + 1, MinShift );

	/// <summary>A piece of at least <paramref name="size"/> bytes, its contents undefined. Give it back with <see cref="Return"/>.</summary>
	public static ContentBuffer Rent( int size )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( size );
		Volatile.Write( ref lastRentTicks, System.Diagnostics.Stopwatch.GetTimestamp() );
		if ( size > LargestPooled ) return Made( size, size );

		var shift = ShiftOf( size );
		if ( shift > SlabShift )
		{
			var stack = large[shift - SlabShift - 1];
			lock ( stack )
			{
				if ( stack.TryPop( out var kept ) )
				{
					Interlocked.Add( ref largeKeptBytes, -kept.Length );
					CountOut( kept.Length );
					return new ContentBuffer( kept, 0, size, kept.Length );
				}
			}

			return Made( size, 1 << shift );
		}

		var order = shift - MinShift;
		lock ( gate )
		{
			if ( !TryCut( order, out var piece ) )
			{
				// A new slab while there's room, else an array of its own let go when it comes back
				if ( HeldBytes + SlabBytes > BusyKeptBytes ) return Made( size, 1 << shift );
				AddSlab();
				TryCut( order, out piece );
			}

			CountOut( 1 << shift );
			return piece.WithLength( size );
		}
	}

	static ContentBuffer Made( int size, int capacity )
	{
		Interlocked.Add( ref madeBytes, capacity );
		CountOut( capacity );
		return new ContentBuffer( GC.AllocateUninitializedArray<byte>( capacity ), 0, size, capacity );
	}

	static void AddSlab()
	{
		var slab = new Slab { Array = GC.AllocateUninitializedArray<byte>( SlabBytes ), Free = new ulong[Orders][], FreeBytes = SlabBytes };
		for ( int o = 0; o < Orders; o++ ) slab.Free[o] = new ulong[Math.Max( (SlabBytes >> (MinShift + o)) / 64, 1 )];
		slab.Free[Orders - 1][0] = 1;
		slabs.Add( slab );
		Interlocked.Add( ref madeBytes, SlabBytes );
		Interlocked.Add( ref slabBytes, SlabBytes );
		Interlocked.Add( ref freeSlabBytes, SlabBytes );
	}

	// Under gate: the smallest free piece of at least this order in any slab, halved down to it (the halves it doesn't use
	// left free); earlier slabs first, so later ones drain and can be let go
	static bool TryCut( int order, out ContentBuffer piece )
	{
		for ( int o = order; o < Orders; o++ )
		{
			foreach ( var slab in slabs )
			{
				if ( slab.FreeBytes < 1 << (MinShift + o) ) continue;
				var bits = slab.Free[o];
				for ( int w = 0; w < bits.Length; w++ )
				{
					if ( bits[w] == 0 ) continue;
					var index = w * 64 + BitOperations.TrailingZeroCount( bits[w] );
					bits[w] &= bits[w] - 1;

					// Split down: keep the first half, free the second, at each order on the way
					for ( var at = o; at > order; at-- )
					{
						index *= 2;
						var buddy = index + 1;
						slab.Free[at - 1][buddy >> 6] |= 1UL << (buddy & 63);
					}

					var bytes = 1 << (MinShift + order);
					slab.FreeBytes -= bytes;
					Interlocked.Add( ref freeSlabBytes, -bytes );
					piece = new ContentBuffer( slab.Array, index << (MinShift + order), bytes, bytes );
					return true;
				}
			}
		}

		piece = default;
		return false;
	}

	/// <summary>A piece from <see cref="Rent"/> is no longer used: its slab's room, or its array kept for the next rent while there's room.</summary>
	public static void Return( ContentBuffer buffer )
	{
		var array = buffer.Array;
		if ( array is null ) return;
		CountOut( -buffer.Capacity );

		if ( array.Length == SlabBytes && buffer.Capacity <= SlabBytes )
		{
			lock ( gate )
			{
				foreach ( var slab in slabs )
				{
					if ( !ReferenceEquals( slab.Array, array ) ) continue;
					Join( slab, buffer.Offset, BitOperations.Log2( (uint)buffer.Capacity ) - MinShift );
					return;
				}
			}
		}

		var shift = BitOperations.Log2( (uint)array.Length );
		if ( array.Length == buffer.Capacity && BitOperations.IsPow2( array.Length ) && shift > SlabShift && shift <= LargestShift && HeldBytes + array.Length <= BusyKeptBytes )
		{
			var stack = large[shift - SlabShift - 1];
			Interlocked.Add( ref largeKeptBytes, array.Length );
			lock ( stack ) stack.Push( array );
			return;
		}

		Interlocked.Add( ref droppedBytes, array.Length );
	}

	// Under gate: a piece back in its slab, joined with its other half while that's free too
	static void Join( Slab slab, int offset, int order )
	{
		var bytes = 1 << (MinShift + order);
		slab.FreeBytes += bytes;
		Interlocked.Add( ref freeSlabBytes, bytes );
		var index = offset >> (MinShift + order);
		while ( order < Orders - 1 )
		{
			var buddy = index ^ 1;
			var bits = slab.Free[order];
			var mask = 1UL << (buddy & 63);
			if ( (bits[buddy >> 6] & mask) == 0 ) break;
			bits[buddy >> 6] &= ~mask;
			index >>= 1;
			order++;
		}

		slab.Free[order][index >> 6] |= 1UL << (index & 63);
	}

	/// <summary>
	/// Once nothing has been rented for <see cref="QuietSeconds"/>, let free memory go down to <see cref="MaxKeptBytes"/>
	/// (kept arrays first, then slabs with nothing out, the last made first); after <paramref name="seconds"/>, all of it:
	/// nothing streams (a map has gone, or everything it shows is in). Cheap enough for every frame.
	/// </summary>
	public static void TrimIdle( double seconds = IdleSeconds )
	{
		if ( KeptBytes == 0 ) return;
		var quiet = System.Diagnostics.Stopwatch.GetElapsedTime( Volatile.Read( ref lastRentTicks ) ).TotalSeconds;
		if ( quiet >= seconds ) Trim( 0 );
		else if ( quiet >= Math.Min( QuietSeconds, seconds ) && HeldBytes > MaxKeptBytes ) Trim( MaxKeptBytes );
	}

	/// <summary>Let every free slab and kept array go.</summary>
	public static void Clear() => Trim( 0 );

	static void Trim( long keep )
	{
		foreach ( var stack in large )
		{
			lock ( stack )
			{
				while ( HeldBytes > keep && stack.TryPop( out var array ) )
				{
					Interlocked.Add( ref largeKeptBytes, -array.Length );
					Interlocked.Add( ref droppedBytes, array.Length );
				}
			}
		}

		lock ( gate )
		{
			for ( int i = slabs.Count - 1; i >= 0 && HeldBytes > keep; i-- )
			{
				if ( slabs[i].FreeBytes != SlabBytes ) continue;
				slabs.RemoveAt( i );
				Interlocked.Add( ref slabBytes, -SlabBytes );
				Interlocked.Add( ref freeSlabBytes, -SlabBytes );
				Interlocked.Add( ref droppedBytes, SlabBytes );
			}
		}
	}
}
