using Sandbox.Diagnostics;

namespace Editor.Mcp;

internal static partial class PerfTools
{
	[McpTool.ReadOnly( "profiler_compare" )]
	[Description( "Compare two profiler captures, recorded or loaded with profiler_load: frame time stats of both, the scopes whose self time per frame grew or shrank most (matched by name, all CPU threads), and allocation and GC changes. Per frame means work inside each capture's complete frames divided by their count, or per second over the whole capture when either has none. Page the scope lists with offset." )]
	internal static async Task<object> ProfilerCompare(
		[Description( "Capture ID of the baseline." )] long baselineId,
		[Description( "Capture ID of the candidate. 0 for the latest." )] long candidateId = 0,
		[Description( "How many regressions and improvements to list. Default 10, max 100." )] int limit = 10,
		[Description( "Scopes to skip in each list, for paging." )] int offset = 0,
		[Description( "Leave out scopes whose self time changed less than this, in ms per frame. Default 0.001." )] double minDeltaMs = 0.001 )
	{
		var baseline = FindCapture( baselineId );
		var candidate = FindCapture( candidateId );
		if ( baseline.Id == candidate.Id ) throw ProfilerError( "Pick two different captures." );

		return await Task.Run( () => CompareCaptures( baseline, candidate, limit, offset, minDeltaMs ) );
	}

	internal static object CompareCaptures( Profiler.Capture baseline, Profiler.Capture candidate, int limit = 10, int offset = 0, double minDeltaMs = 0.001 )
	{
		var perFrame = baseline.Frames.Any( x => !x.Incomplete ) && candidate.Frames.Any( x => !x.Incomplete );
		var inBaseline = CompareWindow.Of( baseline, perFrame );
		var inCandidate = CompareWindow.Of( candidate, perFrame );
		var a = ScopeTotals( baseline, inBaseline );
		var b = ScopeTotals( candidate, inCandidate );
		var rows = a.Keys.Union( b.Keys ).Select( name =>
		{
			var x = a.GetValueOrDefault( name );
			var y = b.GetValueOrDefault( name );
			return new ScopeDelta( name, Round( x.Self, 3 ), Round( y.Self, 3 ), Round( y.Self - x.Self, 3 ), Round( y.Total - x.Total, 3 ), Round( x.Calls, 1 ), Round( y.Calls, 1 ) );
		} ).Where( x => Math.Abs( x.DeltaSelfMs ) >= minDeltaMs ).ToArray();
		var take = Math.Clamp( limit, 1, 100 );
		var start = Math.Max( 0, offset );
		var regressions = rows.Where( x => x.DeltaSelfMs > 0 ).OrderByDescending( x => x.DeltaSelfMs ).ToArray();
		var improvements = rows.Where( x => x.DeltaSelfMs < 0 ).OrderBy( x => x.DeltaSelfMs ).ToArray();

		return (object)new
		{
			BaselineId = baseline.Id,
			CandidateId = candidate.Id,
			Unit = perFrame ? "per frame" : "per second",
			Frames = new { Baseline = CaptureFrameStats( baseline ), Candidate = CaptureFrameStats( candidate ) },
			Regressions = regressions.Skip( start ).Take( take ).ToArray(),
			Improvements = improvements.Skip( start ).Take( take ).ToArray(),
			TotalRegressions = regressions.Length,
			TotalImprovements = improvements.Length,
			NextOffset = start + take < Math.Max( regressions.Length, improvements.Length ) ? start + take : (int?)null,
			Memory = new
			{
				MainThreadKiBPerFrame = new { Baseline = CaptureAllocationStats( baseline ), Candidate = CaptureAllocationStats( candidate ) },
				SampledKiB = new { Baseline = KiB( SampledBytes( baseline, inBaseline ).Sum( x => x.Bytes ) / inBaseline.Units ), Candidate = KiB( SampledBytes( candidate, inCandidate ).Sum( x => x.Bytes ) / inCandidate.Units ) },
				GcPauses = new { Baseline = GcTotals( baseline, inBaseline ), Candidate = GcTotals( candidate, inCandidate ) },
				TypeChanges = TypeDeltas( baseline, inBaseline, candidate, inCandidate )
			},
			Warnings = CompareWarnings( baseline, candidate )
		};
	}

	/// <summary>
	/// What a comparison counts: in per frame mode, work starting inside the capture's complete frames, divided by
	/// their count; otherwise everything, per second.
	/// </summary>
	readonly record struct CompareWindow( (double Start, double End)[] Frames, double Units )
	{
		public static CompareWindow Of( Profiler.Capture capture, bool perFrame )
		{
			if ( !perFrame ) return new( null, Math.Max( 0.001, capture.DurationMs / 1000 ) );
			var frames = capture.Frames.Where( x => !x.Incomplete ).Select( x => (x.StartMs, x.EndMs) ).OrderBy( x => x.StartMs ).ToArray();
			return new( frames, frames.Length );
		}

		public bool Contains( double ms )
		{
			if ( Frames is null ) return true;
			int lo = 0, hi = Frames.Length - 1;
			while ( lo <= hi )
			{
				var mid = (lo + hi) / 2;
				if ( Frames[mid].Start > ms ) hi = mid - 1;
				else if ( Frames[mid].End <= ms ) lo = mid + 1;
				else return true;
			}
			return false;
		}
	}

	static IEnumerable<Profiler.AllocationSample> SampledBytes( Profiler.Capture capture, CompareWindow window ) =>
		window.Frames is null || !Profiler.AllocationsHaveTimes( capture ) ? capture.Allocations : capture.Allocations.Where( x => window.Contains( x.TimeMs ) );

	sealed record ScopeDelta( string Name, double BaselineSelfMs, double CandidateSelfMs, double DeltaSelfMs, double DeltaTotalMs, double BaselineCalls, double CandidateCalls );

	// CPU threads only: GC pauses are compared on their own
	static Dictionary<string, (double Self, double Total, double Calls)> ScopeTotals( Profiler.Capture capture, CompareWindow window )
	{
		var names = CaptureSiteNames( capture );
		var totals = new Dictionary<string, (double Self, double Total, double Calls)>();
		var units = window.Units;
		foreach ( ref readonly var span in capture.Spans.AsSpan() )
		{
			if ( span.ThreadKey == Profiler.GcThreadKey || !window.Contains( span.StartMs ) ) continue;
			var name = names[span.ScopeId];
			var total = totals.GetValueOrDefault( name );
			totals[name] = (total.Self + (span.SelfMs ?? 0) / units, total.Total + span.DurationMs / units, total.Calls + 1 / units);
		}
		return totals;
	}

	static object GcTotals( Profiler.Capture capture, CompareWindow window )
	{
		if ( !capture.HasGcData ) return null;
		var pauses = capture.ThreadSpans.GetValueOrDefault( Profiler.GcThreadKey, ArraySegment<Profiler.Span>.Empty ).Where( x => window.Contains( x.StartMs ) ).ToArray();
		return new { Count = Round( pauses.Length / window.Units, 3 ), Ms = Round( pauses.Sum( x => x.DurationMs ) / window.Units, 3 ) };
	}

	// Sampled allocation types whose bytes per frame changed most
	static object TypeDeltas( Profiler.Capture a, CompareWindow aWindow, Profiler.Capture b, CompareWindow bWindow )
	{
		if ( a.Allocations.Length == 0 && b.Allocations.Length == 0 ) return null;
		var x = SampledBytes( a, aWindow ).GroupBy( t => t.Type ).ToDictionary( t => t.Key, t => t.Sum( s => s.Bytes ) / aWindow.Units );
		var y = SampledBytes( b, bWindow ).GroupBy( t => t.Type ).ToDictionary( t => t.Key, t => t.Sum( s => s.Bytes ) / bWindow.Units );
		return x.Keys.Union( y.Keys ).Select( t => (Type: t, Delta: y.GetValueOrDefault( t ) - x.GetValueOrDefault( t )) )
			.OrderByDescending( t => Math.Abs( t.Delta ) ).Take( 5 ).Select( t => new { t.Type, DeltaKiB = KiB( t.Delta ) } ).ToArray();
	}

	static string[] CompareWarnings( Profiler.Capture a, Profiler.Capture b )
	{
		var warnings = new List<string>();
		foreach ( var (label, capture) in new[] { ("Baseline", a), ("Candidate", b) } )
		{
			if ( capture.DroppedEvents > 0 || capture.EventLimitReached ) warnings.Add( $"{label} lost events or stopped at a limit; its totals are low." );
			if ( !capture.HasGcData ) warnings.Add( $"{label} has no GC or allocation data." );
		}
		return warnings.Count > 0 ? warnings.ToArray() : null;
	}
}
