using Sandbox.Diagnostics;
using System.IO;

namespace Editor.Mcp;

internal static partial class PerfTools
{
	[McpTool( "profiler_capture" )]
	[Description( "Record a profiler capture: every instrumented C# and C++ scope on every thread, then summarise it. Returns a capture ID, frame time stats, the main thread scopes with the most self time inside frames, threads, GC pauses and sampled allocations. Drill in with profiler_frames (slowest frames), profiler_call_tree (what a frame or scope spent its time on), profiler_scopes (find a scope) and profiler_timeline (raw instances), passing the capture ID. Times are wall time, including waits. The first capture of a session also registers scopes, so take another for clean numbers." )]
	internal static async Task<object> ProfilerCapture(
		[Description( "Seconds to record, 0.05 to 10. Default 1." )] float seconds = 1,
		[Description( "How many scopes to list, by self time. Default 25, max 200." )] int limit = 25,
		[Description( "Also write the scopes to a .sprof file in the profiles folder, which keeps recording past the in-memory limit; GC pauses and allocations are only in the summary. Files stay in the folder until you delete them. Load parts of one with profiler_load." )] bool streamToDisk = false )
	{
		if ( !float.IsFinite( seconds ) || seconds < 0.05f || seconds > 10 ) throw ProfilerError( "seconds must be 0.05 to 10." );
		if ( Profiler.RecordingId != 0 )
			throw ProfilerError( $"Capture {Profiler.RecordingId} is still recording or processing. Wait for it, then read it by its ID." );

		Task<Profiler.Capture> task;
		try { task = Profiler.StartCapture( seconds, streamToDisk: streamToDisk ); }
		catch ( Exception e ) when ( e is InvalidOperationException or IOException or UnauthorizedAccessException ) { throw ProfilerError( e.Message ); }
		var id = Profiler.RecordingId;

		Profiler.Capture capture;
		try
		{
			capture = await task.WaitAsync( TimeSpan.FromSeconds( 60 ), McpSession.Current?.Cancellation ?? default );
		}
		catch ( Exception e ) when ( e is TimeoutException or OperationCanceledException )
		{
			await MainThread.Wait();
			if ( Profiler.RecordingId == id ) Profiler.StopCapture();
			throw;
		}
		catch ( Exception e ) when ( e is InvalidOperationException or IOException )
		{
			throw ProfilerError( e.Message );
		}

		return await Task.Run( () => ProfilerSummary( capture, limit ) );
	}

	[McpTool( "profiler_start" )]
	[Description( "Start an open-ended profiler capture, to record something you trigger with other tools. Finish it with profiler_stop, which returns the same summary as profiler_capture. It stops itself when it fills the memory budget, or with streamToDisk when the file reaches its disk limit; profiler_stop then still returns it, with a warning." )]
	internal static object ProfilerStart(
		[Description( "Also write the scopes to a .sprof file, like profiler_capture's streamToDisk." )] bool streamToDisk = false )
	{
		if ( Profiler.RecordingId != 0 )
			throw ProfilerError( $"Capture {Profiler.RecordingId} is still recording or processing. Stop it with profiler_stop." );
		try { _started = Profiler.StartCapture( float.PositiveInfinity, streamToDisk: streamToDisk ); }
		catch ( Exception e ) when ( e is InvalidOperationException or IOException or UnauthorizedAccessException ) { throw ProfilerError( e.Message ); }
		return new { CaptureId = Profiler.RecordingId, Recording = true };
	}

	// The profiler_start capture, so profiler_stop can return it after it stopped itself
	static Task<Profiler.Capture> _started;

	[McpTool( "profiler_stop" )]
	[Description( "Stop the capture started with profiler_start (or any running capture) and summarise it like profiler_capture, also when it already stopped itself." )]
	internal static async Task<object> ProfilerStop(
		[Description( "How many scopes to list, by self time. Default 25, max 200." )] int limit = 25 )
	{
		var task = Profiler.PendingCompletion ?? _started;
		_started = null;
		if ( task is null ) throw ProfilerError( "No profiler capture is recording. Start one with profiler_start." );
		Profiler.StopCapture();

		Profiler.Capture capture;
		try { capture = await task.WaitAsync( TimeSpan.FromSeconds( 60 ), McpSession.Current?.Cancellation ?? default ); }
		catch ( Exception e ) when ( e is InvalidOperationException or IOException ) { throw ProfilerError( e.Message ); }
		return await Task.Run( () => ProfilerSummary( capture, limit ) );
	}

	[McpTool.ReadOnly( "profiler_frames" )]
	[Description( "Frames of a profiler capture with their duration and main thread allocations, slowest first by default. Pass a frame's SpanIndex to profiler_call_tree to see what it spent its time on." )]
	internal static async Task<object> ProfilerFrames(
		[Description( "Capture ID from profiler_capture. 0 for the latest." )] long captureId = 0,
		[Description( "\"slowest\" (default) or \"time\" for capture order." )] string order = "slowest",
		[Description( "How many frames. Default 20, max 1000." )] int limit = 20,
		[Description( "Frames to skip, for paging." )] int offset = 0 )
	{
		var capture = FindCapture( captureId );
		if ( order is not ("slowest" or "time") ) throw ProfilerError( "order is \"slowest\" or \"time\"." );

		return await Task.Run( () =>
		{
			var frames = order == "time" ? capture.Frames : capture.Frames.OrderByDescending( x => x.DurationMs ).ToArray();
			var start = Math.Clamp( offset, 0, frames.Length );
			var page = frames.Skip( start ).Take( Math.Clamp( limit, 1, 1000 ) ).ToArray();
			return (object)new
			{
				CaptureId = capture.Id,
				Stats = CaptureFrameStats( capture ),
				MainThreadKiBPerFrame = CaptureAllocationStats( capture ),
				TotalFrames = frames.Length,
				NextOffset = NextOffset( start, page.Length, frames.Length ),
				Frames = page.Select( x => new
				{
					x.SpanIndex,
					x.Index,
					StartMs = Round( x.StartMs, 3 ),
					DurationMs = Round( x.DurationMs, 3 ),
					AllocatedKiB = x.AllocatedBytes is { } bytes ? KiB( bytes ) : (double?)null,
					x.Incomplete
				} ).ToArray()
			};
		} );
	}

	[McpTool.ReadOnly( "profiler_call_tree" )]
	[Description( "Break one scope instance down into its nested children, with inclusive time, self time and share of the root. Takes a SpanIndex from profiler_frames, profiler_scopes or profiler_timeline. Children are sorted by time, and when the row limit cuts the tree the widest branches near the root are kept. Self time includes uninstrumented work and waits." )]
	internal static async Task<object> ProfilerCallTree(
		[Description( "SpanIndex of the instance to break down." )] int spanIndex,
		[Description( "Capture ID from profiler_capture. 0 for the latest." )] long captureId = 0,
		[Description( "How many rows. Default 100, max 2000." )] int limit = 100,
		[Description( "Leave out children shorter than this, in ms. Default 0.01." )] double minMs = 0.01 )
	{
		var capture = FindCapture( captureId );
		if ( capture.Spans.Length == 0 ) throw ProfilerError( $"Capture {capture.Id} has no scope instances." );
		if ( spanIndex < 0 || spanIndex >= capture.Spans.Length ) throw ProfilerError( $"spanIndex must be 0 to {capture.Spans.Length - 1}." );

		return await Task.Run( () =>
		{
			var names = CaptureSiteNames( capture );
			var root = capture.Spans[spanIndex];
			var rows = Profiler.CallTree( capture, spanIndex, Math.Clamp( limit, 1, 2000 ), Math.Max( 0, minMs ), out var truncated );
			return (object)new
			{
				CaptureId = capture.Id,
				Root = new { Name = names[root.ScopeId], Thread = CaptureThreadName( capture, root.ThreadKey ), StartMs = Round( root.StartMs, 3 ), DurationMs = Round( root.DurationMs, 3 ), root.Incomplete },
				Truncated = truncated,
				Rows = rows.Select( x => new
				{
					x.SpanIndex,
					x.Depth,
					Name = names[x.ScopeId],
					InclusiveMs = Round( x.InclusiveMs, 3 ),
					SelfMs = RoundOrNull( x.SelfMs, 3 ),
					PercentOfRoot = RoundOrNull( x.PercentOfRoot, 1 ),
					PercentOfParent = RoundOrNull( x.PercentOfParent, 1 )
				} ).ToArray()
			};
		} );
	}

	[McpTool.ReadOnly( "profiler_scopes" )]
	[Description( "Find scopes in a profiler capture by name or source file. Lists each matching scope with its call count and duration percentiles, then their instances, slowest first. Pass an instance's SpanIndex to profiler_call_tree for its breakdown. Unfinished instances are left out." )]
	internal static async Task<object> ProfilerScopes(
		[Description( "Case-insensitive text to find in scope names and source paths, like \"PlayerController\" or \"Physics.\". Empty matches every scope." )] string query = "",
		[Description( "Capture ID from profiler_capture. 0 for the latest." )] long captureId = 0,
		[Description( "Only this thread, by Key from the capture's Threads. 0 for all." )] ulong threadKey = 0,
		[Description( "Only instances at least this long, in ms. Also applies to the scope stats." )] double minMs = 0,
		[Description( "How many instances. Default 20, max 1000." )] int limit = 20,
		[Description( "Instances to skip, for paging." )] int offset = 0 )
	{
		var capture = FindCapture( captureId );
		ValidateThread( capture, threadKey );
		query ??= "";

		return await Task.Run( () =>
		{
			var names = CaptureSiteNames( capture );
			var sites = capture.Sites.Where( x => x.Name.Contains( query, StringComparison.OrdinalIgnoreCase ) || x.File.Contains( query, StringComparison.OrdinalIgnoreCase ) ).ToDictionary( x => x.Id );
			// Durations per scope for the stats, and only the requested page's worth of slowest instances
			var source = threadKey == 0 ? new ArraySegment<Profiler.Span>( capture.Spans ) : capture.ThreadSpans[threadKey];
			var durations = new Dictionary<uint, List<double>>();
			var keep = (int)Math.Min( (long)Math.Max( 0, offset ) + Math.Clamp( limit, 1, 1000 ), source.Count );
			var slowest = new PriorityQueue<Profiler.Span, (double, int)>();
			var total = 0;
			foreach ( var x in source )
			{
				if ( !sites.ContainsKey( x.ScopeId ) || x.Incomplete || x.DurationMs < minMs ) continue;
				total++;
				if ( !durations.TryGetValue( x.ScopeId, out var list ) ) durations[x.ScopeId] = list = new();
				list.Add( x.DurationMs );
				if ( slowest.Count < keep ) slowest.Enqueue( x, (x.DurationMs, -x.Index) );
				else slowest.EnqueueDequeue( x, (x.DurationMs, -x.Index) );
			}

			var scopes = durations.Select( g =>
			{
				var stats = Profiler.DescribeDurations( g.Value );
				return new
				{
					Name = names[g.Key],
					Source = Profiler.SourceLocation( sites[g.Key] ),
					Calls = stats.Count,
					TotalMs = Round( g.Value.Sum(), 3 ),
					P50Ms = Round( stats.P50Ms, 3 ),
					P95Ms = Round( stats.P95Ms, 3 ),
					MaxMs = Round( stats.MaxMs, 3 )
				};
			} ).OrderByDescending( x => x.TotalMs ).ToArray();

			var ordered = new Profiler.Span[slowest.Count];
			for ( int i = ordered.Length - 1; i >= 0; i-- ) ordered[i] = slowest.Dequeue();
			var start = Math.Clamp( offset, 0, total );
			var page = ordered.Skip( start ).ToArray();
			return (object)new
			{
				CaptureId = capture.Id,
				MatchingScopes = scopes.Length,
				Scopes = scopes.Take( 50 ).ToArray(),
				TotalInstances = total,
				NextOffset = NextOffset( start, page.Length, total ),
				Instances = page.Select( x => new
				{
					SpanIndex = x.Index,
					Name = names[x.ScopeId],
					Thread = CaptureThreadName( capture, x.ThreadKey ),
					StartMs = Round( x.StartMs, 3 ),
					DurationMs = Round( x.DurationMs, 3 ),
					SelfMs = RoundOrNull( x.SelfMs, 3 )
				} ).ToArray()
			};
		} );
	}

	[McpTool.ReadOnly( "profiler_timeline" )]
	[Description( "Raw scope instances of a profiler capture, in start order on each thread, with parent links: exactly what ran when. Filter by thread, time window and duration, and page with offset. For where the time went, profiler_call_tree is usually the better view." )]
	internal static async Task<object> ProfilerTimeline(
		[Description( "Capture ID from profiler_capture. 0 for the latest." )] long captureId = 0,
		[Description( "Only this thread, by Key from the capture's Threads. 0 for all." )] ulong threadKey = 0,
		[Description( "Window start, in ms from the start of the capture." )] double startMs = 0,
		[Description( "Window end, in ms from the start of the capture. 0 for the end." )] double endMs = 0,
		[Description( "Only instances at least this long, in ms." )] double minMs = 0,
		[Description( "How many instances. Default 200, max 5000." )] int limit = 200,
		[Description( "Instances to skip, for paging." )] int offset = 0 )
	{
		var capture = FindCapture( captureId );
		ValidateThread( capture, threadKey );
		if ( endMs <= 0 ) endMs = capture.DurationMs;
		if ( startMs > endMs ) throw ProfilerError( $"startMs must be before endMs ({endMs:0.###})." );

		return await Task.Run( () =>
		{
			var names = CaptureSiteNames( capture );
			var source = threadKey == 0 ? new ArraySegment<Profiler.Span>( capture.Spans ) : capture.ThreadSpans[threadKey];
			var start = Math.Max( 0, offset );
			var take = Math.Clamp( limit, 1, 5000 );
			var page = new List<Profiler.Span>( Math.Min( take, 256 ) );
			var total = 0;
			foreach ( var x in source )
			{
				if ( x.EndMs < startMs || x.StartMs > endMs || x.DurationMs < minMs ) continue;
				if ( total >= start && page.Count < take ) page.Add( x );
				total++;
			}
			start = Math.Min( start, total );
			return (object)new
			{
				CaptureId = capture.Id,
				TotalInstances = total,
				NextOffset = NextOffset( start, page.Count, total ),
				Instances = page.Select( x => new
				{
					SpanIndex = x.Index,
					x.Parent,
					x.Depth,
					x.ThreadKey,
					Name = names[x.ScopeId],
					StartMs = Round( x.StartMs, 3 ),
					DurationMs = Round( x.DurationMs, 3 ),
					SelfMs = RoundOrNull( x.SelfMs, 3 ),
					x.Incomplete
				} ).ToArray()
			};
		} );
	}

	[McpTool( "profiler_save" )]
	[Description( "Save a profiler capture as a .sprof file in the profiles folder, with scope names and source paths. The file is named by the time and capture ID; the result gives the name to load it by." )]
	internal static async Task<object> ProfilerSave(
		[Description( "Capture ID from profiler_capture. 0 for the latest." )] long captureId = 0 )
	{
		var capture = FindCapture( captureId );

		try
		{
			var path = await Profiler.SaveCapture( capture, Profiler.CaptureFileName( capture ) );
			return new { CaptureId = capture.Id, Name = Path.GetFileName( path ), Path = path };
		}
		catch ( Exception e ) when ( e is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException )
		{
			throw ProfilerError( e.Message );
		}
	}

	[McpTool( "profiler_load" )]
	[Description( "Load a .sprof file from the profiles folder as a new capture and summarise it like profiler_capture. For files recorded with streamToDisk, startMs and durationMs pick a window, for recordings too long to load whole." )]
	internal static async Task<object> ProfilerLoad(
		[Description( "File name from profiler_files." )] string name,
		[Description( "Window start in ms, for streamed files. Default 0." )] double startMs = 0,
		[Description( "Window length in ms, for streamed files. 0 loads the rest." )] double durationMs = 0,
		[Description( "How many scopes to list, by self time. Default 25, max 200." )] int limit = 25 )
	{
		Profiler.Capture capture;
		try
		{
			capture = await Profiler.LoadCapture( name, startMs, durationMs );
		}
		catch ( FileNotFoundException )
		{
			throw ProfilerError( $"No capture named {name}. profiler_files lists them." );
		}
		catch ( Exception e ) when ( e is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException )
		{
			throw ProfilerError( e.Message );
		}
		return await Task.Run( () => ProfilerSummary( capture, limit ) );
	}

	[McpTool.ReadOnly( "profiler_files" )]
	[Description( "List the saved .sprof captures in the profiles folder, newest first." )]
	internal static object ProfilerFiles( [Description( "How many files. Default 20, max 200." )] int limit = 20 )
	{
		var files = Profiler.SavedCaptures();
		return new { Directory = Profiler.CaptureDirectory, TotalFiles = files.Length, Files = files.Take( Math.Clamp( limit, 1, 200 ) ).ToArray() };
	}

	static object ProfilerSummary( Profiler.Capture capture, int limit )
	{
		var sites = capture.Sites.ToDictionary( x => x.Id );
		var gc = capture.ThreadSpans.GetValueOrDefault( Profiler.GcThreadKey, ArraySegment<Profiler.Span>.Empty );
		var (mainThread, costs) = FrameCosts( capture );
		return new
		{
			CaptureId = capture.Id,
			DurationMs = Round( capture.DurationMs ),
			Warnings = CaptureWarnings( capture ),
			Frames = CaptureFrameStats( capture ),
			MainThreadKiBPerFrame = CaptureAllocationStats( capture ),
			Threads = CaptureThreads( capture ),
			TopScopesThread = mainThread == 0 ? "all" : CaptureThreadName( capture, mainThread ),
			TopScopes = costs.Take( Math.Clamp( limit, 1, 200 ) ).Select( x => new
			{
				sites[x.ScopeId].Name,
				x.Calls,
				SelfMs = RoundOrNull( x.SelfMs, 3 ),
				InclusiveMs = Round( x.InclusiveMs, 3 ),
				MaxMs = Round( x.MaxMs, 3 ),
				Source = Profiler.SourceLocation( sites[x.ScopeId] )
			} ).ToArray(),
			GcPauses = gc.Count == 0 ? null : new { gc.Count, TotalMs = Round( gc.Sum( x => x.DurationMs ), 3 ), MaxMs = Round( gc.Max( x => x.DurationMs ), 3 ) },
			AllocationsByScope = Profiler.AllocationsByScope( capture ).Take( 10 ).Select( x => new
			{
				Scope = sites.GetValueOrDefault( x.ScopeId )?.Name,
				TopThread = CaptureThreadName( capture, x.ThreadKey ),
				KiB = KiB( x.Bytes ),
				TopType = x.Type
			} ).ToArray(),
			AllocationsByType = Profiler.AllocationsByType( capture ).Take( 10 ).Select( x => new { x.Type, KiB = KiB( x.Bytes ) } ).ToArray(),
			capture.SourceFile,
			Collector = capture.Overhead is { } o ? new { MedianDrainMs = Round( o.MedianDrainMs, 3 ), MaxDrainMs = Round( o.MaxDrainMs, 3 ), FinalizationMs = Round( o.FinalizationMs, 1 ) } : null,
			Notes = "TopScopes is main thread work inside frames, so frame cap waits are left out; profiler_scopes covers every thread. Allocations are sampled about every 100 KB, charged to the innermost open scope (null Scope: none), so small numbers are noisy. Self time is null for instances cut off by the capture edges or lost events."
		};
	}

	// Main thread scopes inside frames; every thread when there are no frames
	static (ulong Thread, Profiler.Cost[] Costs) FrameCosts( Profiler.Capture capture )
	{
		if ( capture.Frames.Length == 0 ) return (0, capture.Costs);
		var thread = capture.Spans[capture.Frames[0].SpanIndex].ThreadKey;
		var spans = capture.ThreadSpans[thread];
		var frames = capture.Frames.Select( x => x.SpanIndex ).ToHashSet();
		var inside = new bool[spans.Count];
		var kept = new List<Profiler.Span>( spans.Count );
		for ( int i = 0; i < spans.Count; i++ )
		{
			var span = spans[i];
			inside[i] = frames.Contains( span.Index ) || (span.Parent >= 0 && inside[span.Parent - spans.Offset]);
			if ( inside[i] ) kept.Add( span );
		}
		return (thread, Profiler.AggregateCosts( System.Runtime.InteropServices.CollectionsMarshal.AsSpan( kept ), double.NegativeInfinity, double.PositiveInfinity ));
	}

	static string[] CaptureWarnings( Profiler.Capture capture )
	{
		var warnings = new List<string>();
		var window = capture.SourceFile is not null && capture.RecordedDurationMs > capture.DurationMs;
		if ( window )
			warnings.Add( $"Covers {capture.SourceOffsetMs:0} to {capture.SourceOffsetMs + capture.DurationMs:0} ms of the file's {capture.RecordedDurationMs:0} ms; load other parts with profiler_load startMs and durationMs." );
		if ( capture.LimitReason is not null )
			warnings.Add( window ? $"Reached the {capture.LimitReason}." : $"Stopped early at the {capture.LimitReason}, after {capture.DurationMs:0} ms." );
		if ( capture.DroppedEvents > 0 ) warnings.Add( $"{capture.DroppedEvents} events were lost. Instances spanning the gaps are cut short and have no self time." );
		if ( capture.StorageError is not null ) warnings.Add( $"Stream to disk: {capture.StorageError}" );
		if ( capture.Frames.Length == 0 ) warnings.Add( $"No {Profiler.FrameScopeName} scopes, so no frame stats." );
		if ( !capture.HasGcData ) warnings.Add( "No GC pauses or allocation samples were recorded, so empty GC and allocation tables mean unknown, not none. Streamed files hold scopes only." );
		if ( capture.AllocationCutoffMs is { } cutoff ) warnings.Add( $"Allocation samples stop at {cutoff:0} ms, where the capture hit its sample limit; later allocations aren't in the tables." );
		return warnings.Count > 0 ? warnings.ToArray() : null;
	}

	static object CaptureFrameStats( Profiler.Capture capture )
	{
		var complete = capture.Frames.Where( x => !x.Incomplete ).ToArray();
		if ( complete.Length == 0 ) return null;
		var stats = Profiler.DescribeDurations( complete.Select( x => x.DurationMs ) );
		return new
		{
			stats.Count,
			P50Ms = Round( stats.P50Ms, 3 ),
			P95Ms = Round( stats.P95Ms, 3 ),
			P99Ms = Round( stats.P99Ms, 3 ),
			MaxMs = Round( stats.MaxMs, 3 ),
			SlowestSpanIndex = complete.MaxBy( x => x.DurationMs ).SpanIndex
		};
	}

	static object CaptureAllocationStats( Profiler.Capture capture )
	{
		var stats = Profiler.FrameAllocationStats( capture );
		return stats.Count == 0 ? null : new { P50 = KiB( stats.P50Ms ), P95 = KiB( stats.P95Ms ), Max = KiB( stats.MaxMs ), Mean = KiB( stats.MeanMs ) };
	}

	static object CaptureThreads( Profiler.Capture capture ) => capture.Threads.Select( x => new { x.Key, x.Name, Instances = capture.ThreadSpans.GetValueOrDefault( x.Key ).Count } )
		.OrderByDescending( x => x.Instances ).ToArray();

	static string CaptureThreadName( Profiler.Capture capture, ulong key ) => capture.Threads.FirstOrDefault( x => x.Key == key )?.Name;

	static Dictionary<uint, string> CaptureSiteNames( Profiler.Capture capture ) => capture.Sites.ToDictionary( x => x.Id, x => x.Name );

	static McpException ProfilerError( string message ) => new( JsonRpcError.InvalidParams, message );

	static Profiler.Capture FindCapture( long captureId ) => Profiler.Find( captureId ) ?? throw ProfilerError( captureId == 0
		? "No profiler capture yet. Record one with profiler_capture."
		: $"Capture {captureId} isn't kept any more: at most the last four are, fewer when they're large. Record one with profiler_capture, or load a saved one with profiler_load." );

	static void ValidateThread( Profiler.Capture capture, ulong threadKey )
	{
		if ( threadKey != 0 && !capture.ThreadSpans.ContainsKey( threadKey ) )
			throw ProfilerError( $"No thread {threadKey} in capture {capture.Id}. Keys: {string.Join( ", ", capture.Threads.Select( x => $"{x.Key} ({x.Name})" ) )}." );
	}

	static int? NextOffset( int offset, int count, int total ) => offset + count < total ? offset + count : null;

	static double? RoundOrNull( double? value, int digits ) => value is { } v ? Round( v, digits ) : null;
}
