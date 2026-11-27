using System.Diagnostics.Tracing;
using System.Threading;
using Native = NativeEngine.Profiler;

namespace Sandbox.Diagnostics;

/// <summary>
/// GC pauses from the runtime's GC events, as spans on a "GC pauses" lane, and allocation samples (about one per 100 KB).
/// </summary>
internal static partial class Profiler
{
	/// <summary>
	/// Sampled allocations of one type on one thread inside one scope (0 for none). Bytes are an estimate. A capture
	/// keeps one per sample, in time order; TimeMs is NaN for sums and for files saved before samples had times.
	/// </summary>
	internal sealed record AllocationSample( string Type, ulong ThreadKey, uint ScopeId, int Samples, long Bytes, double TimeMs = double.NaN );

	readonly record struct RawAllocation( ulong Ticks, ulong Thread, string Type, long Bytes );

	// Small enough to survive a JSON double; native thread keys count up from 1
	internal const ulong GcThreadKey = uint.MaxValue;
	const string GcThreadName = "GC pauses";

	// Indexed by GcPauseListener kind
	static readonly string[] GcPauseNames = ["GC.Pause.Gen0", "GC.Pause.Gen1", "GC.Pause.Gen2", "GC.Pause.Background", "Runtime.Suspend"];
	static uint[] _gcPauseScopes;

	/// <summary>
	/// 0 to 4 for GC.Pause.Gen0, Gen1, Gen2, Background and Runtime.Suspend; -1 for any other scope.
	/// </summary>
	internal static int GcPauseKind( string scopeName ) => Array.IndexOf( GcPauseNames, scopeName );

	static GcPauseListener StartGcPauses()
	{
		_gcPauseScopes ??= GcPauseNames.Select( x => Native.RegisterScope( x, "", 0 ) ).ToArray();
		GcPauseListener listener = null;
		try
		{
			listener = new GcPauseListener( Native.Frequency() );
			listener.Calibrate();
			return listener;
		}
		catch ( Exception e )
		{
			listener?.Dispose();
			Log.Warning( $"Profiler capture can't record GC pauses: {e.Message}" );
			return null;
		}
	}

	static List<RawAllocation> AddGcPauses( Builder builder, ulong end, ref Site[] sites, ref ThreadInfo[] threads, out ulong allocationCutoff )
	{
		allocationCutoff = 0;
		var listener = builder.GcPauses;
		if ( listener is null ) return [];

		var (pauses, allocations, cutoff) = listener.Finish( end );
		allocationCutoff = cutoff;

		var scopes = _gcPauseScopes;

		// The lane and its sites only exist in captures that had a pause
		var state = new ThreadState { Info = new NativeThread { Id = 0, Key = GcThreadKey } };
		var usedKinds = new bool[GcPauseNames.Length];
		var lastEnd = builder.Start;
		foreach ( var (start, stop, kind) in pauses.OrderBy( x => x.Start ) )
		{
			if ( stop <= lastEnd || start >= end ) continue;
			var scope = scopes[kind];
			if ( scope == 0 ) continue;
			usedKinds[kind] = true;

			// One lane: overlapping pauses are clipped to the previous one
			state.Spans.Add( new PendingSpan
			{
				Scope = scope,
				Start = Math.Max( start, lastEnd ),
				End = Math.Min( stop, end ),
				Parent = -1,
				Incomplete = start < builder.Start || stop > end
			} );
			lastEnd = Math.Min( stop, end );
			builder.SpanCount++;
		}

		if ( state.Spans.Count == 0 ) return allocations;

		builder.Threads[GcThreadKey] = state;
		threads = [.. threads, new ThreadInfo( GcThreadKey, 0, GcThreadName )];

		var used = new List<Site>( sites );
		for ( int kind = 0; kind < usedKinds.Length; kind++ )
		{
			if ( usedKinds[kind] && used.All( x => x.Id != scopes[kind] ) )
				used.Add( new Site( scopes[kind], GcPauseNames[kind], "", 0 ) );
		}
		sites = used.ToArray();

		return allocations;
	}

	/// <summary>
	/// Charge each allocation sample to its thread and the innermost scope open on that thread when it was taken.
	/// </summary>
	static AllocationSample[] AttributeAllocations( Capture capture, List<RawAllocation> raw, ulong start, ulong end, double toMs )
	{
		var byOsThread = capture.Threads.Where( x => x.Key != GcThreadKey ).ToLookup( x => x.Id );
		var types = new Dictionary<string, string>();
		var result = new List<AllocationSample>( raw.Count );
		foreach ( var sample in raw.OrderBy( x => x.Ticks ) )
		{
			if ( sample.Ticks < start || sample.Ticks > end || IsProfilerAllocation( sample.Type ) ) continue;
			var ms = ((double)sample.Ticks - start) * toMs;
			var thread = FindThread( capture, byOsThread[sample.Thread], ms );
			var scope = thread != 0 && capture.ThreadSpans.TryGetValue( thread, out var spans ) ? InnermostScope( spans, ms ) : 0;
			// Each event carries its own copy of the type name
			if ( !types.TryGetValue( sample.Type, out var type ) ) types[sample.Type] = type = sample.Type;
			result.Add( new AllocationSample( type, thread, scope, 1, sample.Bytes, ms ) );
		}
		return result.ToArray();
	}

	/// <summary>
	/// Whether the samples have times, so they can be narrowed to a range. Files saved before they did can't.
	/// </summary>
	internal static bool AllocationsHaveTimes( Capture capture ) => capture.Allocations.Length == 0 || !double.IsNaN( capture.Allocations[0].TimeMs );

	internal static IEnumerable<AllocationSample> AllocationsIn( Capture capture, double startMs, double endMs ) =>
		startMs <= 0 && endMs >= capture.DurationMs ? capture.Allocations : capture.Allocations.Where( x => x.TimeMs >= startMs && x.TimeMs <= endMs );

	// The profiler's span pages and the event listener's own event objects aren't the game's allocations.
	// The runtime names nested types without their declaring type.
	static readonly HashSet<string> ProfilerTypes = ["PendingSpan[]", "NativeEvent[]", "EventBlock[]", "MoreEventInfo"];
	static bool IsProfilerAllocation( string type ) => ProfilerTypes.Contains( type )
		|| type.StartsWith( "Sandbox.Diagnostics.Profiler", StringComparison.Ordinal )
		|| type.StartsWith( "System.Diagnostics.Tracing.", StringComparison.Ordinal );

	// OS thread ids are reused: prefer the thread whose recorded spans cover the sample
	static ulong FindThread( Capture capture, IEnumerable<ThreadInfo> candidates, double ms )
	{
		ulong best = 0;
		var bestStart = double.NegativeInfinity;
		foreach ( var thread in candidates )
		{
			if ( !capture.ThreadSpans.TryGetValue( thread.Key, out var spans ) || spans.Count == 0 )
			{
				if ( best == 0 ) best = thread.Key;
				continue;
			}
			var first = spans[0].StartMs;
			if ( first <= ms && first > bestStart ) { best = thread.Key; bestStart = first; }
			else if ( best == 0 ) best = thread.Key;
		}
		return best;
	}

	/// <summary>
	/// Allocation samples summed per scope across threads (0 for none), largest first. Type and ThreadKey are the
	/// scope's largest type and the thread that allocated most in it.
	/// </summary>
	internal static AllocationSample[] AllocationsByScope( Capture capture, double startMs = double.NegativeInfinity, double endMs = double.PositiveInfinity ) =>
		AllocationsIn( capture, startMs, endMs ).GroupBy( x => x.ScopeId )
		.Select( g => new AllocationSample(
			g.GroupBy( x => x.Type ).MaxBy( x => x.Sum( y => y.Bytes ) ).Key,
			g.GroupBy( x => x.ThreadKey ).MaxBy( x => x.Sum( y => y.Bytes ) ).Key,
			g.Key, g.Sum( x => x.Samples ), g.Sum( x => x.Bytes ) ) )
		.OrderByDescending( x => x.Bytes ).ToArray();

	/// <summary>
	/// Allocation samples summed per type across threads and scopes, largest first.
	/// </summary>
	internal static AllocationSample[] AllocationsByType( Capture capture, double startMs = double.NegativeInfinity, double endMs = double.PositiveInfinity ) =>
		AllocationsIn( capture, startMs, endMs ).GroupBy( x => x.Type )
		.Select( g => new AllocationSample( g.Key, 0, 0, g.Sum( x => x.Samples ), g.Sum( x => x.Bytes ) ) )
		.OrderByDescending( x => x.Bytes ).ToArray();

	/// <summary>
	/// Main thread bytes allocated per frame, for frames that measured it.
	/// </summary>
	internal static Distribution FrameAllocationStats( Capture capture ) =>
		DescribeDurations( capture.Frames.Where( x => x.AllocatedBytes is not null ).Select( x => (double)x.AllocatedBytes.Value ) );

	/// <summary>
	/// Give each frame the main thread's allocations between the samples taken at its start and at the next frame's start.
	/// </summary>
	static Frame[] AttachFrameAllocations( Frame[] frames, List<(double Ms, long Bytes)> samples )
	{
		if ( samples.Count < 2 ) return frames;

		var result = new Frame[frames.Length];
		var s = 0;
		for ( int i = 0; i < frames.Length; i++ )
		{
			var frame = frames[i];
			result[i] = frame;
			// A cut-off frame would get the whole frame's allocations
			if ( frame.Incomplete ) continue;

			// The sample just before this frame began, and the first one after it ended
			while ( s + 1 < samples.Count && samples[s + 1].Ms <= frame.StartMs ) s++;
			if ( samples[s].Ms > frame.StartMs ) continue;

			var e = s + 1;
			while ( e < samples.Count && samples[e].Ms < frame.EndMs ) e++;
			if ( e >= samples.Count ) continue;

			result[i] = frame with { AllocatedBytes = samples[e].Bytes - samples[s].Bytes };
		}

		return result;
	}

	static uint InnermostScope( ArraySegment<Span> spans, double ms ) => InnermostSpan( spans, ms ) is >= 0 and var index ? spans.Array[index].ScopeId : 0;

	/// <summary>
	/// The capture index of the innermost span open on a thread at a time, or -1.
	/// </summary>
	internal static int InnermostSpan( ArraySegment<Span> spans, double ms )
	{
		// Spans are in start order: take the last one started by then, or its first ancestor still open
		int lo = 0, hi = spans.Count - 1, found = -1;
		while ( lo <= hi )
		{
			var mid = (lo + hi) / 2;
			if ( spans[mid].StartMs <= ms ) { found = mid; lo = mid + 1; }
			else hi = mid - 1;
		}
		if ( found < 0 ) return -1;

		var span = spans[found];
		while ( span.EndMs < ms )
		{
			if ( span.Parent < 0 ) return -1;
			span = spans.Array[span.Parent];
		}
		return span.Index;
	}

	sealed class GcPauseListener : EventListener
	{
		const string RuntimeSource = "Microsoft-Windows-DotNETRuntime";
		const int GCStart = 1, GCEnd = 2, RestartEEEnd = 3, SuspendEEBegin = 9, AllocationTick = 10;
		const EventKeywords GcKeyword = (EventKeywords)0x1;

		ulong _syncTicks;
		DateTime _syncTime;
		double _ticksPerDateTimeTick;
		readonly List<(ulong Start, ulong End, int Kind)> _pauses = new();
		readonly object _lock = new();
		readonly List<RawAllocation> _allocations = new();
		const int MaxAllocations = 200_000;
		ulong _allocationCutoff;

		// The runtime stamps events from a coarse wall clock anchor, so a probe allocation measures the offset to our clock
		const int ProbeLength = 123_457;
		const int WakeLength = 110_000;
		long _offset;
		bool _probing, _calibrated, _waking;
		ulong _probeTicks, _probeEventTicks;

		ulong _suspendStart;
		int _kind = -1;
		bool _backgroundGc, _suspendForGc;
		ulong _lastSeen;

		public GcPauseListener( ulong frequency )
		{
			// Events delivered by the base constructor are ignored until the clocks are paired
			_ticksPerDateTimeTick = frequency / (double)TimeSpan.TicksPerSecond;
			_syncTime = DateTime.UtcNow;
			var ticks = Native.Now();
			Volatile.Write( ref _syncTicks, ticks );
		}

		protected override void OnEventSourceCreated( EventSource source )
		{
			if ( source.Name == RuntimeSource )
				EnableEvents( source, EventLevel.Verbose, GcKeyword );
		}

		public void Calibrate()
		{
			lock ( _lock ) _probing = true;
			var before = Native.Now();
			var probe = GC.AllocateUninitializedArray<byte>( ProbeLength );
			var after = Native.Now();
			GC.KeepAlive( probe );
			lock ( _lock )
			{
				_probeTicks = before + (after - before) / 2;
				if ( _probeEventTicks != 0 ) Calibrated( _probeEventTicks );
			}
		}

		// The probe's event can arrive before or after Calibrate records its time
		void Calibrated( ulong eventTicks )
		{
			_offset = (long)_probeTicks - (long)eventTicks;
			_calibrated = true;
			_probing = false;
		}

		ulong ToTicks( DateTime time )
		{
			var delta = (time.ToUniversalTime() - _syncTime).Ticks * _ticksPerDateTimeTick;
			return delta <= -(double)_syncTicks ? 0 : (ulong)((double)_syncTicks + delta);
		}

		protected override void OnEventWritten( EventWrittenEventArgs e )
		{
			if ( e.EventSource?.Name != RuntimeSource ) return;
			if ( Volatile.Read( ref _syncTicks ) == 0 ) return;

			var ticks = ToTicks( e.TimeStamp );
			lock ( _lock )
			{
				_lastSeen = Math.Max( _lastSeen, ticks );

				switch ( e.EventId )
				{
					case AllocationTick:
						var typeName = Payload( e, "TypeName" ) as string ?? "?";
						var objectSize = typeName == "System.Byte[]" ? Convert.ToInt64( Payload( e, "ObjectSize" ) ?? 0L ) : 0;
						if ( _probing && objectSize is >= ProbeLength and < ProbeLength + 64 )
						{
							if ( _probeTicks != 0 ) Calibrated( ticks );
							else _probeEventTicks = ticks;
							break;
						}
						if ( _waking && objectSize is >= WakeLength and < WakeLength + 64 ) break;
						if ( _allocations.Count < MaxAllocations )
							_allocations.Add( new( ticks, (ulong)e.OSThreadId, typeName, Convert.ToInt64( Payload( e, "AllocationAmount64" ) ?? Payload( e, "AllocationAmount" ) ?? 0L ) ) );
						else if ( _allocationCutoff == 0 ) _allocationCutoff = ticks;
						break;

					case SuspendEEBegin:
						_suspendStart = ticks;
						_kind = -1;
						// SuspendForGC or SuspendForGCPrep; anything else isn't a collection
						_suspendForGc = Convert.ToInt32( Payload( e, "Reason" ) ?? 0 ) is 1 or 6;
						break;

					case GCStart:
						var depth = Convert.ToInt32( Payload( e, "Depth" ) ?? 0 );
						var type = Convert.ToInt32( Payload( e, "Type" ) ?? 0 );
						if ( type == 1 ) { _backgroundGc = true; _kind = 3; }
						else _kind = Math.Clamp( depth, 0, 2 );
						break;

					case GCEnd:
						if ( Convert.ToInt32( Payload( e, "Depth" ) ?? 0 ) == 2 ) _backgroundGc = false;
						break;

					case RestartEEEnd:
						if ( _suspendStart == 0 ) break;
						_pauses.Add( (_suspendStart, Math.Max( ticks, _suspendStart ), PendingKind) );
						_suspendStart = 0;
						_kind = -1;
						break;
				}
			}
		}

		int PendingKind => _kind >= 0 ? _kind : _backgroundGc && _suspendForGc ? 3 : 4;

		static object Payload( EventWrittenEventArgs e, string name )
		{
			var index = e.PayloadNames?.IndexOf( name ) ?? -1;
			return index >= 0 && e.Payload is { } payload && index < payload.Count ? payload[index] : null;
		}

		/// <summary>
		/// Stop listening and return the pauses and allocation samples on our clock, after giving the runtime a moment
		/// to deliver ones before <paramref name="end"/>.
		/// </summary>
		public (List<(ulong Start, ulong End, int Kind)> Pauses, List<RawAllocation> Allocations, ulong AllocationCutoff) Finish( ulong end )
		{
			// An allocation tick stamped after the end tells us everything before it was delivered
			lock ( _lock ) _waking = true;
			GC.KeepAlive( GC.AllocateUninitializedArray<byte>( WakeLength ) );
			var wait = System.Diagnostics.Stopwatch.StartNew();
			while ( wait.ElapsedMilliseconds < 300 )
			{
				lock ( _lock ) { if ( _calibrated && Shift( _lastSeen ) >= end && _suspendStart == 0 ) break; }
				Thread.Sleep( 10 );
			}

			Dispose();
			lock ( _lock )
			{
				if ( !_calibrated ) Log.Warning( "Profiler capture: GC event clock wasn't calibrated, GC pauses may be offset by a few milliseconds." );
				var pauses = _pauses.Select( x => (Shift( x.Start ), Shift( x.End ), x.Kind) ).ToList();
				// A pause still open at the end is kept, cut off there
				if ( _suspendStart != 0 && Shift( _suspendStart ) < end ) pauses.Add( (Shift( _suspendStart ), end + 1, PendingKind) );
				return (pauses, _allocations.Select( x => x with { Ticks = Shift( x.Ticks ) } ).ToList(), _allocationCutoff == 0 ? 0 : Shift( _allocationCutoff ));
			}
		}

		ulong Shift( ulong ticks ) => (ulong)Math.Max( 0, (long)ticks + _offset );
	}
}
