namespace Borealis.Backend;

/// <summary>
/// Ranges of a span of fixed size, handed out and given back: the first gap given back that fits, else past the highest
/// range in use. Gaps that touch are joined, and a gap at the top lowers it. Not thread-safe; callers lock.
/// </summary>
internal sealed class RangeAllocator( long capacity )
{
	/// <summary>Gaps below <see cref="Top"/>, by start; no two touch.</summary>
	readonly List<(long Start, long Length)> gaps = new();

	public long Capacity => capacity;

	/// <summary>One past the highest range in use: everything at or above it is free.</summary>
	public long Top { get; private set; }

	/// <summary>The total length of the ranges in use.</summary>
	public long Used { get; private set; }

	/// <summary>Gaps between the ranges in use.</summary>
	public int Gaps => gaps.Count;

	/// <summary>Ranges handed out from a gap given back, for the tests.</summary>
	public int Reused { get; private set; }

	/// <summary>A range of <paramref name="length"/> (more than 0): false when there's no room.</summary>
	public bool TryAllocate( long length, out long start )
	{
		for ( int i = 0; i < gaps.Count; i++ )
		{
			var (at, size) = gaps[i];
			if ( size < length ) continue;

			if ( size == length ) gaps.RemoveAt( i );
			else gaps[i] = (at + length, size - length);
			start = at;
			Used += length;
			Reused++;
			return true;
		}

		if ( length > capacity - Top )
		{
			start = -1;
			return false;
		}

		start = Top;
		Top += length;
		Used += length;
		return true;
	}

	/// <summary>Give back a range <see cref="TryAllocate"/> handed out.</summary>
	public void Free( long start, long length )
	{
		Used -= length;

		// The first gap after it
		int lo = 0, hi = gaps.Count;
		while ( lo < hi )
		{
			var mid = (lo + hi) >> 1;
			if ( gaps[mid].Start < start ) lo = mid + 1;
			else hi = mid;
		}

		var end = start + length;
		if ( lo > 0 && gaps[lo - 1].Start + gaps[lo - 1].Length == start )
		{
			lo--;
			start = gaps[lo].Start;
			gaps.RemoveAt( lo );
		}

		if ( lo < gaps.Count && gaps[lo].Start == end )
		{
			end += gaps[lo].Length;
			gaps.RemoveAt( lo );
		}

		if ( end == Top ) Top = start;
		else gaps.Insert( lo, (start, end - start) );
	}
}
