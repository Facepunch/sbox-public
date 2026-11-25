using System.Text;

namespace Borealis.Gpu.Memory;

/// <summary>
/// A record of the driver's allocations and frees (<c>vkAllocateMemory</c>, <c>vkFreeMemory</c>) for finding where a burst
/// of video memory comes from: size, memory type, kind, thread, how long the call took and the allocator's frame. Off unless
/// <c>BOREALIS_ALLOC_TRACE=1</c>; then a fixed ring of <see cref="Capacity"/> entries, read with <see cref="Dump"/>.
/// </summary>
internal static class AllocationTrace
{
	public static readonly bool Enabled = Environment.GetEnvironmentVariable( "BOREALIS_ALLOC_TRACE" ) == "1";

	public const int Capacity = 1 << 18;

	struct Entry
	{
		public long Ticks, Duration;
		public ulong Size, Frame;
		public int Type, Thread;
		public byte Flags;
		public bool Free;
	}

	static readonly Entry[] entries = Enabled ? new Entry[Capacity] : null;
	static long next;

	/// <summary>Flags for an entry: an image, content, large block, own allocation, made ahead on a worker; or a present.</summary>
	public const byte Image = 1, Content = 2, Large = 4, Dedicated = 8, Ahead = 16, Present = 32;

	public static void Note( bool free, ulong size, int type, byte flags, ulong frame, long start, long duration )
	{
		var i = System.Threading.Interlocked.Increment( ref next ) - 1;
		entries[i & (Capacity - 1)] = new Entry
		{
			Ticks = start,
			Duration = duration,
			Size = size,
			Frame = frame,
			Type = type,
			Thread = Environment.CurrentManagedThreadId,
			Flags = flags,
			Free = free,
		};
	}

	/// <summary>
	/// The entries noted since <paramref name="sinceTicks"/> (<see cref="System.Diagnostics.Stopwatch"/> ticks, 0 for all), one
	/// per line: ms since <paramref name="sinceTicks"/> (or the first), alloc or free, MiB, type, kind, thread, ms the call took, frame.
	/// </summary>
	public static string Dump( long sinceTicks = 0 )
	{
		if ( !Enabled ) return "allocation trace off (BOREALIS_ALLOC_TRACE=1)";
		var count = Math.Min( System.Threading.Interlocked.Read( ref next ), Capacity );
		var first = System.Threading.Interlocked.Read( ref next ) - count;
		var sb = new StringBuilder();
		var origin = sinceTicks;
		for ( var n = first; n < first + count; n++ )
		{
			var e = entries[n & (Capacity - 1)];
			if ( e.Ticks < sinceTicks ) continue;
			if ( origin == 0 ) origin = e.Ticks;
			var ms = (e.Ticks - origin) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			var took = e.Duration * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			if ( (e.Flags & Present) != 0 )
			{
				sb.Append( $"{ms:F1} present th{e.Thread} {took:F2}\n" );
				continue;
			}

			sb.Append( $"{ms:F1} {(e.Free ? "free" : "alloc")} {e.Size / 1048576.0:F2} t{e.Type} " );
			sb.Append( (e.Flags & Image) != 0 ? "img" : "buf" );
			if ( (e.Flags & Content) != 0 ) sb.Append( "+content" );
			if ( (e.Flags & Large) != 0 ) sb.Append( "+large" );
			if ( (e.Flags & Dedicated) != 0 ) sb.Append( "+own" );
			if ( (e.Flags & Ahead) != 0 ) sb.Append( "+ahead" );
			sb.Append( $" th{e.Thread} {took:F2} f{e.Frame}\n" );
		}

		return sb.ToString();
	}
}
