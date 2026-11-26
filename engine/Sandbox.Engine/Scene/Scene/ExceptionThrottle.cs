using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// Stops per-frame callbacks that throw every frame from flooding the log. The same error is logged a few times,
/// then suppressed until it has stopped happening for a while. Cleared when a game session starts or ends.
/// </summary>
static class ExceptionThrottle
{
	const int MaxLogged = 5;
	const float QuietSeconds = 300;

	struct Entry
	{
		public int Logged;
		public int Suppressed;
		public RealTimeSince LastSeen;
	}

	// Keyed by hash rather than by Type and MethodBase so old assemblies aren't kept alive across hotloads
	static readonly Dictionary<int, Entry> _entries = new();

	internal static void Clear()
	{
		lock ( _entries ) _entries.Clear();
	}

	/// <summary>
	/// Whether this exception should be logged. Check before building the message so suppressed repeats don't allocate.
	/// <paramref name="suffix"/> goes on the end of the message, it's empty unless there is something to say about the repeats.
	/// </summary>
	public static bool ShouldLog( Exception e, Type target, string call, out string suffix )
	{
		var key = HashCode.Combine( target, call, e.GetType(), e.TargetSite );
		var suppressed = 0;
		int logged;

		lock ( _entries )
		{
			ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault( _entries, key, out var exists );

			if ( exists && entry.Logged >= MaxLogged && entry.LastSeen > QuietSeconds )
			{
				suppressed = entry.Suppressed;
				entry.Logged = 0;
				entry.Suppressed = 0;
			}

			entry.LastSeen = 0;

			if ( entry.Logged >= MaxLogged )
			{
				entry.Suppressed++;
				suffix = null;
				return false;
			}

			logged = ++entry.Logged;
		}

		suffix = "";

		if ( suppressed > 0 )
			suffix = $" ({suppressed:n0} earlier occurrences were not logged)";

		if ( logged == MaxLogged )
			suffix += $" (repeated {MaxLogged} times, further occurrences will not be logged until it stops for {QuietSeconds / 60:0} minutes)";

		return true;
	}
}
