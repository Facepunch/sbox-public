using System.Runtime.InteropServices;

namespace Sandbox.Diagnostics;

internal static partial class Profiler
{
	internal readonly record struct Frame( int Index, int SpanIndex, double StartMs, double DurationMs, bool Incomplete )
	{
		public double EndMs => StartMs + DurationMs;

		/// <summary>Managed bytes the main thread allocated during the frame, null when the capture didn't measure it.</summary>
		public long? AllocatedBytes { get; init; }
	}

	internal sealed record Distribution( int Count, double MinMs, double P50Ms, double P95Ms, double P99Ms,
		double MaxMs, double MeanMs, double BinWidthMs, int[] Bins );

	/// <summary>Nearest-rank percentiles and a linear duration histogram. Input is not modified.</summary>
	internal static Distribution DescribeDurations( IEnumerable<double> durations, int binCount = 32 )
	{
		binCount = Math.Clamp( binCount, 1, 128 );
		var values = durations.Where( x => double.IsFinite( x ) && x >= 0 ).Order().ToArray();
		var bins = new int[binCount];
		if ( values.Length == 0 ) return new( 0, 0, 0, 0, 0, 0, 0, 0.001, bins );
		var width = Math.Max( 0.000001, values[^1] / binCount );
		foreach ( var value in values ) bins[Math.Min( binCount - 1, (int)(value / width) )]++;
		double Percentile( double p ) => values[Math.Clamp( (int)Math.Ceiling( values.Length * p ) - 1, 0, values.Length - 1 )];
		return new( values.Length, values[0], Percentile( 0.5 ), Percentile( 0.95 ), Percentile( 0.99 ),
			values[^1], values.Average(), width, bins );
	}

	/// <summary>Window totals clip overlapping spans. A clipped span has no trustworthy self time.</summary>
	internal static Cost[] CostsInRange( Capture capture, double start, double end, ulong threadKey = 0 ) =>
		AggregateCosts( capture.Spans, start, end, threadKey );

	struct CostAccumulator
	{
		public int Calls;
		public double Inclusive, Self, Max;
		public bool UnknownSelf;
	}

	internal static Cost[] AggregateCosts( ReadOnlySpan<Span> spans, double start, double end, ulong threadKey = 0 )
	{
		var totals = new Dictionary<uint, CostAccumulator>();
		foreach ( ref readonly var span in spans )
		{
			if ( (threadKey != 0 && span.ThreadKey != threadKey) || span.EndMs <= start || span.StartMs >= end ) continue;
			var duration = Math.Max( 0, Math.Min( end, span.EndMs ) - Math.Max( start, span.StartMs ) );
			ref var cost = ref CollectionsMarshal.GetValueRefOrAddDefault( totals, span.ScopeId, out _ );
			cost.Calls++;
			cost.Inclusive += duration;
			cost.Self += span.SelfMs ?? 0;
			cost.UnknownSelf |= !span.SelfMs.HasValue || span.StartMs < start || span.EndMs > end;
			cost.Max = Math.Max( cost.Max, duration );
		}
		return totals.Select( x => new Cost( x.Key, x.Value.Calls, x.Value.Inclusive,
			x.Value.UnknownSelf ? null : x.Value.Self, x.Value.Max ) ).OrderByDescending( x => x.SelfMs ?? double.NegativeInfinity )
			.ThenByDescending( x => x.InclusiveMs ).ThenBy( x => x.ScopeId ).ToArray();
	}

	internal sealed record TreeRow( int SpanIndex, uint ScopeId, int Depth, double InclusiveMs, double? SelfMs,
		double? PercentOfRoot, double? PercentOfParent, bool Incomplete );

	internal static TreeRow[] CallTree( Capture capture, int spanIndex, int limit = 1000 ) => CallTree( capture, spanIndex, limit, 0, out _ );

	/// <summary>
	/// An instance and its descendants in depth-first order, leaving out children shorter than <paramref name="minMs"/>.
	/// </summary>
	internal static TreeRow[] CallTree( Capture capture, int spanIndex, int limit, double minMs, out bool truncated )
	{
		truncated = false;
		if ( spanIndex < 0 || spanIndex >= capture.Spans.Length ) return [];
		limit = Math.Clamp( limit, 1, 5000 );
		var root = capture.Spans[spanIndex];
		var children = new Dictionary<int, List<int>>();
		// Per-thread begin order lets us stop past the root's interval.
		var threadEnd = capture.ThreadSpans.TryGetValue( root.ThreadKey, out var range ) ? range.Offset + range.Count : capture.Spans.Length;
		for ( int i = root.Index + 1; i < threadEnd; i++ )
		{
			ref readonly var child = ref capture.Spans[i];
			if ( child.ThreadKey != root.ThreadKey ) continue;
			if ( child.StartMs > root.EndMs ) break;
			if ( child.Parent < 0 || child.DurationMs < minMs ) continue;
			if ( !children.TryGetValue( child.Parent, out var siblings ) ) children.Add( child.Parent, siblings = new() );
			siblings.Add( i );
		}
		// Budget breadth-first so a large collapsed branch cannot hide the root's other children.
		var included = new HashSet<int> { root.Index };
		var frontier = new Queue<int>();
		frontier.Enqueue( root.Index );
		while ( frontier.TryDequeue( out var index ) )
		{
			if ( !children.TryGetValue( index, out var nested ) ) continue;
			if ( included.Count == limit ) { truncated = true; continue; }
			nested.Sort( ( a, b ) =>
			{
				var duration = capture.Spans[b].DurationMs.CompareTo( capture.Spans[a].DurationMs );
				return duration != 0 ? duration : a.CompareTo( b );
			} );
			foreach ( var child in nested )
			{
				if ( included.Count == limit ) { truncated = true; break; }
				included.Add( child );
				frontier.Enqueue( child );
			}
		}
		var result = new List<TreeRow>( included.Count );
		var pending = new Stack<(Span Span, int Depth)>();
		pending.Push( (root, 0) );
		while ( pending.TryPop( out var item ) )
		{
			var span = item.Span;
			var parent = span.Index == root.Index ? root : capture.Spans[span.Parent];
			double? Percent( Span denominator ) => span.Incomplete || denominator.Incomplete ? null :
				denominator.DurationMs > 0 ? span.DurationMs / denominator.DurationMs * 100 : 0;
			result.Add( new( span.Index, span.ScopeId, item.Depth, span.DurationMs, span.SelfMs, Percent( root ), Percent( parent ), span.Incomplete ) );
			if ( !children.TryGetValue( span.Index, out var nested ) ) continue;
			for ( int i = nested.Count - 1; i >= 0; i-- )
				if ( included.Contains( nested[i] ) ) pending.Push( (capture.Spans[nested[i]], item.Depth + 1) );
		}
		return result.ToArray();
	}

	/// <summary>
	/// Where a scope is declared, as module/file:line.
	/// </summary>
	internal static string SourceLocation( Site site )
	{
		var parts = (site.File ?? "").Replace( '\\', '/' ).Split( '/', StringSplitOptions.RemoveEmptyEntries );
		if ( parts.Length == 0 ) return "";
		var file = parts[^1];
		if ( parts.Length > 1 )
		{
			var module = parts[^2];
			for ( int i = parts.Length - 3; i >= 0; i-- )
			{
				if ( !parts[i].Equals( "src", StringComparison.OrdinalIgnoreCase ) && !parts[i].Equals( "engine", StringComparison.OrdinalIgnoreCase ) && !parts[i].Equals( "addons", StringComparison.OrdinalIgnoreCase ) ) continue;
				module = parts[i + 1];
				if ( module == "public" && i + 2 < parts.Length - 1 ) module = parts[i + 2];
				break;
			}
			file = $"{module}/{file}";
		}
		return site.Line > 0 ? $"{file}:{site.Line}" : file;
	}
}
