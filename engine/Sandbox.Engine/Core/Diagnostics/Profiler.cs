using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Native = NativeEngine.Profiler;

namespace Sandbox.Diagnostics;

/// <summary>
/// Collects native scope events into captures. Times are elapsed wall time, including waits.
/// </summary>
[SkipHotload]
internal static partial class Profiler
{
	internal static volatile bool IsRecording;

	// The scope whose instances are frames
	internal const string FrameScopeName = "Engine.Frame";

	[ConVar( "profiler_memory_mb", ConVarFlags.Protected | ConVarFlags.Saved, Min = 512, Max = 2048, Help = "Profiler capture storage budget in MiB, including collection, analysis and retained captures" )]
	internal static int MemoryBudgetMegabytes { get; set; } = 1024;

	const long MemoryReserveBytes = 256L * 1024 * 1024;
	const int RetainedBytesPerSpan = 64, PendingSpanBytes = 32, RecordingBytesPerSpan = 112;
	internal const int MaxEvents = 33_554_432;
	const int MaxRetainedSpans = 6_291_456;
	internal const int RingCapacity = 32768; // Matches tier0/profiler.cpp
	const int RetainedCaptures = 4;
	const int MaxDrainTimes = 65536;

	static NativeEvent[] Scratch;
	static readonly Dictionary<uint, Site> Sites = new();
	static readonly Queue<Capture> History = new();
	static readonly object HistoryLock = new();
	static readonly object CollectorLock = new();
	static readonly ConditionalWeakTable<object, Capture> ViewedCaptures = new();
	static Builder _builder;
	static volatile Builder _finishing;
	static volatile Capture _latest;
	static long _nextId;
	static int _retainedSpans;

	internal static Capture Latest => _latest;
	internal static long RecordingId => _builder?.Id ?? _finishing?.Id ?? 0;
	internal static Task<Capture> PendingCompletion => (_builder ?? _finishing)?.Completion.Task;
	internal static double RecordingProgress => _builder is { } b ? Math.Clamp( (double)(Native.Now() - b.Start) / (b.Deadline - b.Start), 0, 1 ) : 0;
	internal static double RecordingSeconds => _builder is { } b ? (Native.Now() - b.Start) / (double)Native.Frequency() : 0;
	internal static bool RecordingIsOpenEnded => _builder?.Deadline == ulong.MaxValue;

	// Layouts must match tier0/profiler.h
	[StructLayout( LayoutKind.Sequential )]
	struct NativeEvent
	{
		public ulong Ticks, Token;
		public uint Scope, Session;
	}

	[StructLayout( LayoutKind.Sequential )]
	unsafe struct NativeThread
	{
		public ulong Id, Key;
		public fixed byte Name[64];

		public string ReadName()
		{
			fixed ( byte* name = Name )
			{
				var bytes = new ReadOnlySpan<byte>( name, 64 );
				var length = bytes.IndexOf( (byte)0 );
				return System.Text.Encoding.UTF8.GetString( length < 0 ? bytes : bytes[..length] );
			}
		}
	}

	internal sealed record Site( uint Id, string Name, string File, int Line );
	internal sealed record ThreadInfo( ulong Key, ulong Id, string Name );

	[StructLayout( LayoutKind.Sequential, Pack = 8 )]
	internal readonly record struct Span
	{
		public double StartMs { get; }
		public double EndMs { get; }
		readonly double _self;
		public ulong ThreadKey { get; }
		public int Index { get; }
		public uint ScopeId { get; }
		public int Parent { get; }
		readonly ushort _depth, _flags;
		public int Depth => _depth;
		public bool Incomplete => (_flags & 1) != 0;
		public double? SelfMs => double.IsNaN( _self ) ? null : _self;
		public double DurationMs => EndMs - StartMs;
		internal bool ValidFlags => _flags <= 1;

		public Span( int index, uint scopeId, ulong threadKey, int parent, int depth, double startMs, double endMs, double? selfMs, bool incomplete )
		{
			Index = index; ScopeId = scopeId; ThreadKey = threadKey; Parent = parent;
			_depth = checked((ushort)depth); _flags = (ushort)(incomplete ? 1 : 0);
			StartMs = startMs; EndMs = endMs; _self = selfMs ?? double.NaN;
		}
	}

	internal sealed record Cost( uint ScopeId, int Calls, double InclusiveMs, double? SelfMs, double MaxMs );
	internal sealed record RecorderOverhead( int DrainCalls, double MedianDrainMs, double P95DrainMs, double MaxDrainMs, double SnapshotMs, double FinalizationMs );

	internal sealed record Capture( long Id, double DurationMs, ulong DroppedEvents, bool EventLimitReached,
		int IncompleteScopes, ThreadInfo[] Threads, Site[] Sites, Span[] Spans, Cost[] Costs )
	{
		// False means the capture has gaps; retained parent links are still usable.
		public bool HierarchyReliable => DroppedEvents == 0 && !EventLimitReached;
		public Frame[] Frames { get; init; } = [];
		public RecorderOverhead Overhead { get; init; }
		public int InstanceLimit { get; init; }
		public string SourceFile { get; init; }
		public double SourceOffsetMs { get; init; }
		public double RecordedDurationMs { get; init; }
		public string StorageError { get; init; }
		public string LimitReason { get; init; }
		public Dictionary<ulong, ArraySegment<Span>> ThreadSpans { get; init; } = new();
		public AllocationSample[] Allocations { get; init; } = [];
		// False when GC pauses weren't recorded, as in streamed files, so no pauses means no data rather than none happened
		public bool HasGcData { get; init; }
		// Where allocation samples stop because the capture hit its sample limit, or null
		public double? AllocationCutoffMs { get; init; }
	}

	struct PendingSpan
	{
		public ulong Start, End;
		public uint Scope;
		public int Parent, Depth;
		public bool Incomplete;
	}

	sealed class PendingSpans : IDisposable
	{
		const int PageSize = 4096;
		// Pages are large object heap arrays, reused so recording doesn't feed the GC it measures
		const int MaxPooledPages = 128;
		static readonly System.Collections.Concurrent.ConcurrentStack<PendingSpan[]> PagePool = new();
		readonly List<PendingSpan[]> _pages = new();
		public int Count { get; private set; }
		public int NextAllocationBytes => Count % PageSize == 0 ? PageSize * PendingSpanBytes : 0;
		public ref PendingSpan this[int index] => ref _pages[index / PageSize][index % PageSize];

		public void Add( PendingSpan span )
		{
			if ( Count % PageSize == 0 ) _pages.Add( PagePool.TryPop( out var page ) ? page : GC.AllocateUninitializedArray<PendingSpan>( PageSize ) );
			this[Count++] = span;
		}

		public void Dispose()
		{
			foreach ( var page in _pages )
			{
				if ( PagePool.Count >= MaxPooledPages ) break;
				PagePool.Push( page );
			}
			_pages.Clear();
			Count = 0;
		}
	}

	sealed class ThreadState
	{
		public NativeThread Info;
		public string Name;
		public ulong LastTicks;
		public readonly PendingSpans Spans = new();
		public readonly List<(int Index, ulong Token)> Stack = new();
	}

	sealed class Builder
	{
		public long Id;
		public uint Session;
		public ulong Start, Deadline, DroppedAtStart;
		public int Events, EventLimit, SpanCount;
		public bool MemoryBounded;
		public volatile bool LimitReached;
		public volatile Exception CollectorError;
		public readonly ManualResetEventSlim StopCollector = new();
		public Thread Collector;
		public ulong LimitEnd, LimitDropped, RecordingEnd, RejectedTick;
		public long PendingBudgetBytes, AllocatedPendingBytes;
		public ArchiveWriter Archive;
		public string LimitCause;
		// The latest passes for the percentiles; count and max cover every pass
		public readonly List<double> DrainTimes = new();
		public int DrainCount;
		public double MaxDrainMs;
		public readonly Dictionary<ulong, ThreadState> Threads = new();
		public readonly HashSet<uint> ScopeIds = new();
		public GcPauseListener GcPauses;
		public readonly List<(ulong Ticks, long Bytes)> FrameAllocations = new( 4096 );
		public bool FrameAllocationsDone;
		public readonly TaskCompletionSource<Capture> Completion = new( TaskCreationOptions.RunContinuationsAsynchronously );

		// A RAM capture ends at its limit; a streamed one keeps writing until the disk writer stops it
		public bool Finished => (LimitReached && Archive is null) || Archive?.StopReason is not null;

		public void Accept( NativeThread thread, NativeEvent e, ref ThreadState state )
		{
			if ( LimitReached || e.Session != Session || e.Ticks < Start ) return;
			if ( Events++ >= EventLimit ) { Reject( e.Ticks, null ); return; }
			if ( state is null && !Threads.TryGetValue( thread.Key, out state ) )
				Threads.Add( thread.Key, state = new() { Info = thread, Name = thread.ReadName() } );
			var stack = state.Stack;
			var spans = state.Spans;

			// Lost events: everything open on this thread ends at the last event before the gap
			if ( e.Scope == uint.MaxValue )
			{
				foreach ( var (index, _) in stack )
				{
					ref var span = ref spans[index];
					span.End = Math.Max( span.Start, state.LastTicks );
					span.Incomplete = true;
				}
				stack.Clear();
				state.LastTicks = e.Ticks;
				return;
			}

			state.LastTicks = e.Ticks;
			if ( e.Scope != 0 )
			{
				if ( stack.Count >= ushort.MaxValue ) { Reject( e.Ticks, "nesting limit" ); return; }
				if ( SpanCount >= EventLimit / 2 ) { Reject( e.Ticks, null ); return; }
				if ( AllocatedPendingBytes + spans.NextAllocationBytes + (SpanCount + 1L) * (RecordingBytesPerSpan - PendingSpanBytes) > PendingBudgetBytes )
				{ Reject( e.Ticks, "memory limit" ); return; }
				AllocatedPendingBytes += spans.NextAllocationBytes;
				var index = spans.Count;
				SpanCount++;
				ScopeIds.Add( e.Scope );
				spans.Add( new() { Scope = e.Scope, Start = e.Ticks, Parent = stack.Count == 0 ? -1 : stack[^1].Index, Depth = stack.Count } );
				stack.Add( (index, e.Token) );
				return;
			}

			// Ends of scopes entered before the capture have no matching begin
			for ( int i = stack.Count - 1; i >= 0; i-- )
			{
				if ( stack[i].Token != e.Token ) continue;
				for ( int j = stack.Count - 1; j >= i; j-- )
				{
					ref var span = ref spans[stack[j].Index];
					span.End = e.Ticks;
					span.Incomplete |= j != i;
				}
				stack.RemoveRange( i, stack.Count - i );
				break;
			}
		}

		void Reject( ulong ticks, string cause )
		{
			LimitCause ??= cause;
			RejectedTick = ticks;
			LimitReached = true;
		}
	}

	/// <summary>
	/// Start a capture. A scope's first recorded use registers it and may allocate; warmed scopes don't.
	/// Infinite seconds record until <see cref="StopCapture"/> or the memory limit.
	/// </summary>
	internal static Task<Capture> StartCapture( float seconds = 1, int eventLimit = MaxEvents, bool collector = true, bool streamToDisk = false )
	{
		ThreadSafe.AssertIsMainThread();
		if ( FileBusy ) throw new InvalidOperationException( "A profiler capture file is being saved or loaded." );
		if ( RecordingId != 0 ) throw new InvalidOperationException( "A profiler capture is already recording or processing." );
		if ( !float.IsPositiveInfinity( seconds ) && (!float.IsFinite( seconds ) || seconds < 0.05f || seconds > 10) )
			throw new ArgumentOutOfRangeException( nameof( seconds ), "Capture duration must be between 0.05 and 10 seconds, or open-ended." );
		if ( eventLimit < 2 || eventLimit > MaxEvents ) throw new ArgumentOutOfRangeException( nameof( eventLimit ) );

		Scratch ??= new NativeEvent[RingCapacity];
		Drain( null ); // Discards old sessions and releases retired thread slots

		int instanceLimit;
		lock ( HistoryLock ) instanceLimit = ReserveInstances();
		if ( instanceLimit == 0 ) throw new InvalidOperationException( "The captures open in the devtools profiler use the whole memory budget. Increase profiler_memory_mb." );

		var builder = new Builder
		{
			Id = Interlocked.Increment( ref _nextId ),
			EventLimit = Math.Min( eventLimit, instanceLimit * 2 ),
			MemoryBounded = instanceLimit * 2 <= eventLimit,
			PendingBudgetBytes = instanceLimit * (long)RecordingBytesPerSpan,
		};

		try
		{
			if ( streamToDisk ) builder.Archive = new ArchiveWriter( Native.Frequency() );
			// Set up before the capture starts, so its calibration allocation isn't recorded
			builder.GcPauses = StartGcPauses();
			builder.Start = Native.Now();
			builder.Deadline = float.IsPositiveInfinity( seconds ) ? ulong.MaxValue : builder.Start + (ulong)(seconds * Native.Frequency());
			builder.Archive?.SetStart( builder.Start );
		}
		catch
		{
			builder.GcPauses?.Dispose();
			if ( builder.Archive is not null ) _ = Task.Run( builder.Archive.Abort );
			throw;
		}

		builder.DroppedAtStart = Native.DroppedEvents();
		builder.Session = Native.Start();
		_builder = builder;
		IsRecording = true;

		if ( collector )
		{
			builder.Collector = new Thread( () => Collect( builder ) ) { IsBackground = true, Name = "Profiler collector" };
			try { builder.Collector.Start(); }
			catch
			{
				IsRecording = false;
				Native.Stop();
				_builder = null;
				builder.GcPauses?.Dispose();
				if ( builder.Archive is not null ) _ = Task.Run( builder.Archive.Abort );
				throw;
			}
		}

		return builder.Completion.Task;
	}

	static void Collect( Builder builder )
	{
		try
		{
			while ( !builder.StopCollector.IsSet && !builder.Finished )
			{
				lock ( CollectorLock ) DrainMeasured( builder );
				builder.StopCollector.Wait( 1 );
			}
		}
		catch ( Exception e ) { builder.CollectorError = e; }
		finally
		{
			if ( builder.CollectorError is not null || builder.Archive?.StopReason is not null )
			{
				IsRecording = false;
				Native.Stop();
				builder.RecordingEnd = builder.Archive?.StopTick is > 0 ? builder.Archive.StopTick : Native.Now();
			}
		}
	}

	/// <summary>
	/// Called at the start of every frame. Stops the capture at its deadline or limit.
	/// </summary>
	internal static void Update( bool frameBoundary = false )
	{
		var builder = _builder;
		if ( builder is null ) return;

		// Frames past the limit aren't in the capture: one more sample closes the last one, then stop
		if ( frameBoundary && !builder.FrameAllocationsDone )
		{
			builder.FrameAllocations.Add( (Native.Now(), GC.GetAllocatedBytesForCurrentThread()) );
			builder.FrameAllocationsDone = builder.LimitReached;
		}
		if ( builder.CollectorError is not null || builder.Finished || (frameBoundary && Native.Now() >= builder.Deadline) )
		{
			StopCapture();
			return;
		}

		if ( builder.Collector is not null || !Monitor.TryEnter( CollectorLock ) ) return;
		try { DrainMeasured( builder ); }
		finally { Monitor.Exit( CollectorLock ); }
		if ( builder.Finished ) StopCapture();
	}

	internal static void StopCapture()
	{
		var builder = _builder;
		if ( builder is null ) return;
		ThreadSafe.AssertIsMainThread();

		IsRecording = false;
		Native.Stop();
		var end = Native.Now();
		builder.StopCollector.Set();
		builder.Collector?.Join();
		builder.StopCollector.Dispose();
		if ( builder.RecordingEnd != 0 ) end = builder.RecordingEnd;
		_finishing = builder;
		_builder = null;

		try
		{
			if ( builder.CollectorError is not null ) throw new InvalidOperationException( "Profiler collector failed.", builder.CollectorError );
			DrainMeasured( builder );

			// Everything native is read here: the worker can outlive the native DLLs at shutdown
			var snapshotStart = Stopwatch.GetTimestamp();
			var dropped = Native.DroppedEvents() - builder.DroppedAtStart;
			var clockToMs = 1000.0 / Native.Frequency();
			var sites = builder.ScopeIds.Select( GetSite ).ToArray();
			var threads = builder.Threads.Values.Select( x => new ThreadInfo( x.Info.Key, x.Info.Id, string.IsNullOrWhiteSpace( x.Name ) ? $"Thread {x.Info.Id}" : x.Name ) ).ToArray();
			var snapshotMs = Stopwatch.GetElapsedTime( snapshotStart ).TotalMilliseconds;
			_ = Task.Run( () => FinishOnWorker( builder, end, dropped, clockToMs, sites, threads, snapshotMs ) );
		}
		catch ( Exception e )
		{
			Release( builder );
			_finishing = null;
			builder.Completion.TrySetException( e );
			if ( builder.Archive is not null ) _ = Task.Run( builder.Archive.Abort );
			Log.Warning( $"Profiler capture failed: {e.Message}" );
		}
	}

	/// <summary>
	/// Stop any capture at shutdown, giving a streamed file a moment to finish writing.
	/// </summary>
	internal static void Shutdown()
	{
		var streaming = (_builder ?? _finishing)?.Archive is not null ? PendingCompletion : null;
		try
		{
			StopCapture();
			streaming?.Wait( TimeSpan.FromSeconds( 5 ) );
		}
		catch ( Exception e ) { Log.Warning( $"Profiler capture shutdown: {e.Message}" ); }
	}

	static void FinishOnWorker( Builder builder, ulong end, ulong dropped, double clockToMs, Site[] sites, ThreadInfo[] threads, double snapshotMs )
	{
		Capture capture = null;
		Exception error = null;
		try
		{
			var start = Stopwatch.GetTimestamp();
			string archiveFile = null, storageError = null;
			if ( builder.Archive is not null )
			{
				var metadata = new Capture( builder.Id, (end - builder.Start) * clockToMs, dropped, false, 0, threads, sites, [], [] )
				{ ThreadSpans = threads.ToDictionary( x => x.Key, _ => ArraySegment<Span>.Empty ) };
				try { archiveFile = builder.Archive.Finish( metadata, end ); }
				catch ( Exception e ) { storageError = e.Message; }
				if ( builder.Archive.StopTick != 0 ) end = Math.Max( builder.Start, Math.Min( end, builder.Archive.StopTick ) );
			}

			var limited = builder.LimitEnd != 0;
			var captureEnd = limited ? Math.Min( builder.LimitEnd, end ) : end;
			var rawAllocations = AddGcPauses( builder, captureEnd, ref sites, ref threads, out var allocationCutoff );
			capture = Finish( builder, captureEnd, limited ? builder.LimitDropped - builder.DroppedAtStart : dropped, clockToMs, sites, threads );

			var frameAllocations = builder.FrameAllocations.Select( x => ((double)((long)x.Ticks - (long)builder.Start) * clockToMs, x.Bytes) ).ToList();
			var drains = DescribeDurations( builder.DrainTimes );
			capture = capture with
			{
				SourceFile = archiveFile,
				RecordedDurationMs = (end - builder.Start) * clockToMs,
				StorageError = storageError ?? builder.Archive?.StopReason,
				Allocations = AttributeAllocations( capture, rawAllocations, builder.Start, captureEnd, clockToMs ),
				HasGcData = builder.GcPauses is not null,
				AllocationCutoffMs = allocationCutoff != 0 && allocationCutoff < captureEnd ? Math.Max( 0, (double)((long)allocationCutoff - (long)builder.Start) ) * clockToMs : null,
				Frames = AttachFrameAllocations( capture.Frames, frameAllocations ),
				Overhead = new( builder.DrainCount, drains.P50Ms, drains.P95Ms, builder.MaxDrainMs, snapshotMs, Stopwatch.GetElapsedTime( start ).TotalMilliseconds )
			};
			Publish( capture );
		}
		catch ( Exception e ) { error = e; }
		finally
		{
			Release( builder );
			_finishing = null;
		}

		if ( error is not null ) builder.Completion.TrySetException( error );
		else builder.Completion.TrySetResult( capture );
	}

	static void Release( Builder builder )
	{
		foreach ( var state in builder.Threads.Values ) state.Spans.Dispose();
		builder.GcPauses?.Dispose();
	}

	// Reserve recording, finalization and view storage before accepting each new instance
	internal static int CaptureInstanceLimit( long retainedSpans, long budgetBytes ) => (int)Math.Clamp(
		(budgetBytes - MemoryReserveBytes - retainedSpans * RetainedBytesPerSpan) / RecordingBytesPerSpan, 0, MaxEvents / 2 );

	// Drops the oldest retained captures while they would leave a new one less than half the budget
	static int ReserveInstances()
	{
		var budget = MemoryBudgetMegabytes * 1024L * 1024;
		var target = CaptureInstanceLimit( 0, budget ) / 2;
		var limit = CaptureInstanceLimit( RetainedInstances(), budget );
		while ( limit < target && History.Count > 0 )
		{
			var evicted = History.Dequeue();
			_retainedSpans -= evicted.Spans.Length;
			// Let it go: it isn't counted any more
			if ( ReferenceEquals( evicted, _latest ) ) _latest = null;
			limit = CaptureInstanceLimit( RetainedInstances(), budget );
		}
		return limit;
	}

	static long RetainedInstances() => History.Concat( ViewedCaptures.Select( x => x.Value ) ).Select( x => x.Spans ).Distinct().Sum( x => (long)x.Length );

	internal static void SetViewedCapture( object owner, Capture capture )
	{
		lock ( HistoryLock )
		{
			ViewedCaptures.Remove( owner );
			if ( capture is not null ) ViewedCaptures.Add( owner, capture );
		}
	}

	static void Publish( Capture capture )
	{
		lock ( HistoryLock )
		{
			History.Enqueue( capture );
			_retainedSpans += capture.Spans.Length;
			while ( History.Count > RetainedCaptures || (History.Count > 1 && _retainedSpans > MaxRetainedSpans) )
				_retainedSpans -= History.Dequeue().Spans.Length;
			_latest = capture;
		}
	}

	internal static Capture Find( long id )
	{
		lock ( HistoryLock ) return id == 0 ? Latest : History.FirstOrDefault( x => x.Id == id );
	}

	static void DrainMeasured( Builder builder )
	{
		var start = Stopwatch.GetTimestamp();
		Drain( builder );
		if ( builder.LimitReached && builder.LimitEnd == 0 )
		{
			builder.LimitEnd = builder.RejectedTick != 0 ? builder.RejectedTick : Native.Now();
			builder.LimitDropped = Native.DroppedEvents();
		}
		if ( builder.Finished && IsRecording )
		{
			IsRecording = false;
			Native.Stop();
		}
		var ms = Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
		if ( builder.DrainTimes.Count < MaxDrainTimes ) builder.DrainTimes.Add( ms );
		else builder.DrainTimes[builder.DrainCount % MaxDrainTimes] = ms;
		builder.DrainCount++;
		builder.MaxDrainMs = Math.Max( builder.MaxDrainMs, ms );
	}

	static unsafe void Drain( Builder builder )
	{
		var slots = Native.ThreadSlots();
		fixed ( NativeEvent* events = Scratch )
		{
			for ( int slot = 0; slot < slots; slot++ )
			{
				NativeThread thread = default;
				var count = Native.Drain( slot, (IntPtr)events, Scratch.Length, (IntPtr)(&thread) );
				if ( builder is null || count == 0 ) continue;
				if ( builder.Archive is not null )
				{
					var filtered = 0;
					for ( int i = 0; i < count; i++ )
					{
						var e = Scratch[i];
						if ( e.Session != builder.Session || e.Ticks < builder.Start ) continue;
						Scratch[filtered++] = e;
						if ( e.Scope != 0 && e.Scope != uint.MaxValue ) builder.ScopeIds.Add( e.Scope );
					}
					count = filtered;
					if ( count == 0 ) continue;
					if ( !builder.Threads.ContainsKey( thread.Key ) ) builder.Threads.Add( thread.Key, new() { Info = thread, Name = thread.ReadName() } );
					builder.Archive.Enqueue( thread, Scratch, count );
				}
				ThreadState state = null;
				for ( int i = 0; i < count; i++ ) builder.Accept( thread, Scratch[i], ref state );
			}
		}
	}

	static Site GetSite( uint id )
	{
		if ( !Sites.TryGetValue( id, out var site ) )
			Sites[id] = site = new( id, Native.GetScopeName( id ), Native.GetScopeFile( id ), Native.GetScopeLine( id ) );
		return site;
	}

	static Capture Finish( Builder builder, ulong end, ulong dropped, double toMs, Site[] sites, ThreadInfo[] threads )
	{
		var duration = (end - builder.Start) * toMs;
		var spans = new Span[builder.SpanCount];
		var ranges = new Dictionary<ulong, ArraySegment<Span>>();
		var offset = 0;
		var incompleteCount = 0;
		foreach ( var state in builder.Threads.Values )
		{
			var childMs = new double[state.Spans.Count];
			var incompleteChildren = new bool[state.Spans.Count];
			for ( int i = state.Spans.Count - 1; i >= 0; i-- )
			{
				var s = state.Spans[i];
				var incomplete = s.Incomplete || s.End == 0 || s.End > end || s.End < s.Start;
				if ( incomplete ) incompleteCount++;
				var startMs = Math.Clamp( (s.Start - builder.Start) * toMs, 0, duration );
				var endTicks = s.End == 0 ? end : Math.Min( s.End, end );
				var endMs = Math.Clamp( (endTicks >= builder.Start ? endTicks - builder.Start : 0) * toMs, startMs, duration );
				var elapsed = endMs - startMs;
				double? self = !incomplete && !incompleteChildren[i] ? Math.Max( 0, elapsed - childMs[i] ) : null;
				spans[offset + i] = new( offset + i, s.Scope, state.Info.Key, s.Parent < 0 ? -1 : offset + s.Parent, s.Depth, startMs, endMs, self, incomplete );
				if ( s.Parent >= 0 )
				{
					childMs[s.Parent] += elapsed;
					incompleteChildren[s.Parent] |= incomplete || incompleteChildren[i];
				}
			}
			ranges.Add( state.Info.Key, new( spans, offset, state.Spans.Count ) );
			offset += state.Spans.Count;
		}

		var threadMap = threads.ToDictionary( x => x.Key );
		var orderedThreads = builder.Threads.Keys.Select( key => threadMap[key] ).ToArray();
		return Analyze( new( builder.Id, duration, dropped, builder.LimitReached, incompleteCount, orderedThreads, sites, spans, [] )
		{
			ThreadSpans = ranges,
			InstanceLimit = Math.Max( builder.EventLimit / 2, builder.SpanCount ),
			LimitReason = builder.LimitReached ? (builder.LimitCause ?? (builder.MemoryBounded ? "memory limit" : "event limit")) : null
		} );
	}

	static Capture Analyze( Capture capture )
	{
		var frameSites = capture.Sites.Where( x => x.Name == FrameScopeName ).Select( x => x.Id ).ToHashSet();
		var frames = capture.Spans.Where( x => frameSites.Contains( x.ScopeId ) ).OrderBy( x => x.StartMs )
			.Select( ( x, i ) => new Frame( i, x.Index, x.StartMs, x.DurationMs, x.Incomplete ) ).ToArray();
		return capture with { Costs = AggregateCosts( capture.Spans, double.NegativeInfinity, double.PositiveInfinity ), Frames = frames };
	}
}
