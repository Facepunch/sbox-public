using System;
using System.Collections.Generic;

namespace Sandbox.Engine;

/// <summary>A rolling one-minute budget with memory bounded by the total report limit.</summary>
internal sealed class ExceptionReportLimiter
{
	readonly object gate = new();
	readonly Queue<(long Time, string Signature)> accepted = new();
	readonly Dictionary<string, int> counts = new();
	readonly int totalLimit;
	readonly int repeatedLimit;
	readonly Func<long> clock;
	long suppressed;

	internal ExceptionReportLimiter( int totalLimit, int repeatedLimit, Func<long> clock = null )
	{
		this.totalLimit = totalLimit;
		this.repeatedLimit = repeatedLimit;
		this.clock = clock ?? (() => Environment.TickCount64);
	}

	internal bool TryAccept( string signature, out long dropped )
	{
		lock ( gate )
		{
			var now = clock();
			while ( accepted.TryPeek( out var old ) && now - old.Time >= 60_000 )
			{
				accepted.Dequeue();
				if ( --counts[old.Signature] == 0 ) counts.Remove( old.Signature );
			}
			counts.TryGetValue( signature, out var count );
			dropped = 0;
			if ( accepted.Count >= totalLimit || count >= repeatedLimit )
			{
				suppressed++;
				return false;
			}
			accepted.Enqueue( (now, signature) );
			counts[signature] = count + 1;
			dropped = suppressed;
			suppressed = 0;
			return true;
		}
	}
}
