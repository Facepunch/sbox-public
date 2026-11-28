using System.IO;
using System.Text;
using NativeEngine;
using Native = NativeEngine.Profiler;

namespace Sandbox.Diagnostics;

/// <summary>
/// GPU passes in a capture, from the scene system's timestamps: one lane per queue beside the CPU threads, on the
/// capture's clock. A GPU frame's results arrive a few frames late, so a stopped capture waits for them before it finishes.
/// </summary>
internal static partial class Profiler
{
	// Below the GC lane's key, far above native thread keys
	internal const ulong GpuGraphicsKey = uint.MaxValue - 1, GpuComputeKey = uint.MaxValue - 2;
	internal static bool IsGpuLane( ulong key ) => key is GpuGraphicsKey or GpuComputeKey;
	const string GpuScopePrefix = "GPU.";
	// GPU pass scopes are managed only, numbered above every native scope id; past the cap, passes share GPU.Other
	const uint GpuScopeBase = 0x10000;
	internal const int MaxGpuScopes = 4096;
	// Raw passes a streamed recording keeps for its file
	const int MaxStreamedGpuSpans = 1 << 21;
	const int MaxGpuNameBytes = 128;
	// Frames to keep reading GPU results after a capture stops, and how long at most
	const int GpuDrainFrames = 6;
	const double GpuDrainSeconds = 0.5;
	// By a hash of the name's bytes, so reading a frame doesn't allocate its names
	static readonly Dictionary<ulong, uint> GpuScopes = new();
	// Read by the finishing worker, which mustn't call native
	static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, Site> GpuSites = new();
	static GpuTimestampEventData[] _gpuEvents = [];
	static int[] _gpuIndex = [];
	static uint[] _gpuScopeIds = [];
	static bool _shuttingDown;

	internal readonly record struct RawGpuSpan( ulong Start, ulong End, uint Scope, int Parent, bool Compute );

	/// <summary>
	/// A GPU pass placed on its lane: clipped to its parent and the capture, after its earlier siblings.
	/// </summary>
	internal readonly record struct PlacedGpuSpan( ulong Start, ulong End, uint Scope, int Parent, int Depth, bool Incomplete );

	/// <summary>
	/// A streamed recording's GPU passes as recorded, to build lanes for any window of it.
	/// </summary>
	internal sealed record GpuTrack( Site[] Sites, RawGpuSpan[] Spans );

	/// <summary>
	/// Whether captures want the scene system's GPU timestamps on: while one records, and while a stopped one waits for its last frames.
	/// </summary>
	internal static bool WantsGpuTimings => _builder is not null || _gpuDraining is not null;

	static Builder _gpuDraining;

	/// <summary>
	/// Read the latest completed GPU frame, once, mapping its passes to the capture clock through its first host submit,
	/// sampled with the GPU clock. Its GPU time goes to the frame that submit was made in.
	/// </summary>
	static unsafe void SampleGpu( Builder builder )
	{
		if ( !GpuProfilerStats.Enabled || GpuProfilerStats.FrameId == 0 || GpuProfilerStats.FrameId == builder.GpuFrame ) return;
		builder.GpuFrame = GpuProfilerStats.FrameId;
		builder.GpuFrames++;

		var count = CSceneSystem.GetGpuTimestampEventCount();
		if ( count <= 0 ) return;
		if ( _gpuEvents.Length < count )
		{
			_gpuEvents = new GpuTimestampEventData[count];
			_gpuIndex = new int[count];
			_gpuScopeIds = new uint[count];
		}

		int submit = -1, passes = 0;
		double gpuStart = double.MaxValue, gpuEnd = double.MinValue, passStart = double.MaxValue, passEnd = double.MinValue;
		for ( int i = 0; i < count; i++ )
		{
			var name = CSceneSystem.GetGpuTimestampEventName( i, out _gpuEvents[i] );
			ref readonly var e = ref _gpuEvents[i];
			// A corrupt or discontinuous clock makes the whole frame unusable
			if ( name == IntPtr.Zero || !double.IsFinite( e.StartMs ) || !double.IsFinite( e.EndMs ) || e.StartMs < 0 || e.EndMs < e.StartMs || e.EndMs > 10_000 || e.Queue is < 0 or > 1 ) return;
			_gpuScopeIds[i] = 0;
			if ( e.Kind == 3 && e.HostTicks > 0 && (submit < 0 || e.HostTicks < _gpuEvents[submit].HostTicks) ) submit = i;
			if ( e.Kind == 1 ) { gpuStart = Math.Min( gpuStart, e.StartMs ); gpuEnd = Math.Max( gpuEnd, e.EndMs ); }
			if ( e.Kind != 0 ) continue;
			if ( (_gpuScopeIds[i] = GpuScope( (byte*)name )) == 0 ) continue;
			passStart = Math.Min( passStart, e.StartMs );
			passEnd = Math.Max( passEnd, e.EndMs );
			passes++;
		}
		if ( submit < 0 ) return;

		if ( gpuEnd < gpuStart ) { gpuStart = passStart; gpuEnd = passEnd; }
		if ( gpuEnd >= gpuStart && builder.GpuFrameTimes.Count < MaxCounterSamples ) builder.GpuFrameTimes.Add( (_gpuEvents[submit].HostTicks, (float)(gpuEnd - gpuStart)) );

		var streamed = builder.Archive is not null;
		var room = streamed ? MaxStreamedGpuSpans - builder.GpuSpans.Count : builder.EventLimit / 2 - builder.SpanCount - builder.GpuReserved;
		if ( passes > room ) { builder.GpuLost += passes; return; }

		var ticksPerMs = Native.Frequency() / 1000.0;
		var origin = _gpuEvents[submit].HostTicks - _gpuEvents[submit].StartMs * ticksPerMs;
		// Where each pass lands in GpuSpans, so children can point at their parent wherever it is in the snapshot
		var next = builder.GpuSpans.Count;
		for ( int i = 0; i < count; i++ ) _gpuIndex[i] = _gpuScopeIds[i] != 0 ? next++ : -1;
		for ( int i = 0; i < count; i++ )
		{
			if ( _gpuIndex[i] < 0 ) continue;
			ref readonly var e = ref _gpuEvents[i];
			var parent = e.Parent >= 0 && e.Parent < count && e.Parent != i ? _gpuIndex[e.Parent] : -1;
			builder.GpuSpans.Add( new( (ulong)Math.Max( 0, origin + e.StartMs * ticksPerMs ), (ulong)Math.Max( 0, origin + e.EndMs * ticksPerMs ), _gpuScopeIds[i], parent, e.Queue != 0 ) );
		}
		if ( !streamed ) builder.GpuReserved += passes;
	}

	static unsafe uint GpuScope( byte* name )
	{
		var length = 0;
		while ( length < MaxGpuNameBytes && name[length] != 0 ) length++;
		if ( length == 0 ) return 0;
		var bytes = new ReadOnlySpan<byte>( name, length );
		var hash = Sandbox.Hashing.XxHash3.HashToUInt64( bytes );
		if ( GpuScopes.TryGetValue( hash, out var id ) ) return id;
		if ( GpuScopes.Count >= MaxGpuScopes ) return GpuOverflowScope();
		id = GpuScopeBase + 1 + (uint)GpuScopes.Count;
		GpuScopes[hash] = id;
		GpuSites[id] = new Site( id, GpuScopePrefix + Encoding.UTF8.GetString( bytes ), "", 0 );
		return id;
	}

	static uint GpuOverflowScope()
	{
		GpuSites.TryAdd( GpuScopeBase, new Site( GpuScopeBase, GpuScopePrefix + "Other", "", 0 ) );
		return GpuScopeBase;
	}

	/// <summary>
	/// Keep reading GPU frames for a stopped capture until its last frames are in, then finish it. Main thread.
	/// </summary>
	static bool DrainGpu()
	{
		if ( _gpuDraining is not { } builder ) return false;
		SampleGpu( builder );
		if ( builder.GpuFrames < builder.GpuFramesAtStop + GpuDrainFrames && Native.Now() - builder.GpuStopTicks < GpuDrainSeconds * Native.Frequency() ) return true;
		_gpuDraining = null;
		builder.FinishAfterGpu?.Invoke();
		return true;
	}

	/// <summary>
	/// The GPU passes as lanes, one per queue: in start order, nested by their passes, siblings clipped so they don't
	/// overlap. A pass whose parent wasn't placed goes under its nearest placed ancestor. At most <paramref name="limit"/>
	/// are placed, the rest counted in <paramref name="lost"/>.
	/// </summary>
	internal static (List<PlacedGpuSpan> Graphics, List<PlacedGpuSpan> Compute) GpuLanes( IReadOnlyList<RawGpuSpan> raw, ulong start, ulong end, int limit, out int lost )
	{
		lost = 0;
		var lanes = (Graphics: new List<PlacedGpuSpan>(), Compute: new List<PlacedGpuSpan>());
		// Each raw span's index in its lane, or -1
		var placed = new int[raw.Count];
		Array.Fill( placed, -1 );
		foreach ( var compute in new[] { false, true } )
		{
			var lane = compute ? lanes.Compute : lanes.Graphics;
			var stack = new List<int>();
			// Where the last span at each depth ended, so siblings don't overlap
			var lastEnd = new List<ulong>();
			var order = Enumerable.Range( 0, raw.Count ).Where( i => raw[i].Compute == compute ).OrderBy( i => raw[i].Start ).ThenByDescending( i => raw[i].End );
			foreach ( var i in order )
			{
				var r = raw[i];
				if ( r.End <= start || r.Start >= end ) continue;
				var parent = -1;
				var hops = 0;
				for ( var a = r.Parent; a >= 0 && a < raw.Count && hops++ < 64; a = raw[a].Parent )
				{
					if ( raw[a].Compute == compute && placed[a] >= 0 && stack.Contains( placed[a] ) ) { parent = placed[a]; break; }
				}
				var depth = parent < 0 ? 0 : stack.IndexOf( parent ) + 1;
				var from = Math.Max( r.Start, Math.Max( start, lastEnd.Count > depth ? lastEnd[depth] : 0 ) );
				var to = Math.Min( r.End, end );
				if ( parent >= 0 )
				{
					from = Math.Max( from, lane[parent].Start );
					to = Math.Min( to, lane[parent].End );
				}
				if ( to <= from ) continue;
				if ( lanes.Graphics.Count + lanes.Compute.Count >= limit ) { lost++; continue; }
				stack.RemoveRange( depth, stack.Count - depth );
				placed[i] = lane.Count;
				lane.Add( new( from, to, r.Scope, parent, depth, r.Start < start || r.End > end ) );
				stack.Add( placed[i] );
				// A new span's children start fresh
				if ( lastEnd.Count > depth ) lastEnd[depth] = to; else lastEnd.Add( to );
				if ( lastEnd.Count > depth + 1 ) lastEnd.RemoveRange( depth + 1, lastEnd.Count - depth - 1 );
			}
		}
		return lanes;
	}

	static void AddGpuLanes( Builder builder, ulong end, ref Site[] sites, ref ThreadInfo[] threads, IReadOnlyDictionary<uint, Site> gpuSites = null )
	{
		gpuSites ??= GpuSites;
		if ( builder.GpuSpans.Count == 0 ) return;
		var (graphics, compute) = GpuLanes( builder.GpuSpans, builder.Start, end, Math.Max( 0, builder.EventLimit / 2 - builder.SpanCount ), out var lost );
		builder.GpuLost += lost;
		var used = new HashSet<uint>();
		foreach ( var (key, name, lane) in new[] { (GpuGraphicsKey, "GPU graphics", graphics), (GpuComputeKey, "GPU async compute", compute) } )
		{
			if ( lane.Count == 0 ) continue;
			var state = new ThreadState { Info = new NativeThread { Id = 0, Key = key } };
			foreach ( var x in lane )
			{
				used.Add( x.Scope );
				state.Spans.Add( new PendingSpan { Scope = x.Scope, Start = x.Start, End = x.End, Parent = x.Parent, Depth = x.Depth, Incomplete = x.Incomplete } );
				builder.SpanCount++;
			}
			builder.Threads[key] = state;
			threads = [.. threads, new ThreadInfo( key, 0, name )];
		}

		var known = sites.Select( x => x.Id ).ToHashSet();
		sites = [.. sites, .. used.Where( known.Add ).Select( x => gpuSites[x] )];
	}

	/// <summary>
	/// How long the graphics queue was busy during one frame's wall time, from its top-level passes.
	/// </summary>
	internal static double? FrameGpuBusyMs( Capture capture, int frame )
	{
		if ( !capture.ThreadSpans.TryGetValue( GpuGraphicsKey, out var lane ) || frame < 0 || frame >= capture.Frames.Length ) return null;
		var start = capture.Frames[frame].StartMs;
		var end = capture.Frames[frame].EndMs;
		double total = 0;
		foreach ( var span in lane )
			if ( span.Depth == 0 && span.EndMs > start && span.StartMs < end ) total += Math.Min( end, span.EndMs ) - Math.Max( start, span.StartMs );
		return total;
	}

	static GpuTrack RecordedGpu( Builder builder ) => builder.GpuSpans.Count == 0 ? null
		: new( builder.GpuSpans.Select( x => x.Scope ).Distinct().Select( x => GpuSites[x] ).ToArray(), builder.GpuSpans.ToArray() );

	internal static void WriteGpuTrack( BinaryWriter writer, GpuTrack track )
	{
		writer.Write( track.Sites.Length );
		foreach ( var site in track.Sites ) { writer.Write( site.Id ); WriteText( writer, site.Name ); }
		writer.Write( track.Spans.Length );
		foreach ( var span in track.Spans )
		{
			writer.Write( span.Start ); writer.Write( span.End ); writer.Write( span.Scope ); writer.Write( span.Parent ); writer.Write( span.Compute );
		}
	}

	/// <summary>
	/// The passes overlapping a window, at most <paramref name="limit"/> of them (the rest counted in <paramref name="lost"/>),
	/// with their parent links renumbered. Only those are kept in memory.
	/// </summary>
	internal static GpuTrack ReadGpuTrack( Stream stream, ulong start, ulong end, int limit, out int lost )
	{
		lost = 0;
		using var reader = new BinaryReader( stream, Encoding.UTF8, leaveOpen: true );
		var siteCount = reader.ReadInt32();
		if ( siteCount is < 0 or > MaxGpuScopes + 1 ) throw new InvalidDataException( "Invalid profiler capture GPU passes." );
		var sites = new Site[siteCount];
		for ( int i = 0; i < siteCount; i++ ) sites[i] = new Site( reader.ReadUInt32(), ReadText( reader ), "", 0 );
		var ids = sites.Select( x => x.Id ).ToHashSet();
		var count = reader.ReadInt32();
		if ( count < 0 || (long)count * 25 != stream.Length - stream.Position ) throw new InvalidDataException( "Invalid profiler capture GPU passes." );
		var kept = new List<RawGpuSpan>();
		// Each kept span's index in the file, ascending, to renumber parents
		var keptIndex = new List<int>();
		for ( int i = 0; i < count; i++ )
		{
			var span = new RawGpuSpan( reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt32(), reader.ReadInt32(), reader.ReadBoolean() );
			if ( !ids.Contains( span.Scope ) || span.Parent >= count || span.Parent < -1 || span.End < span.Start ) throw new InvalidDataException( "Invalid profiler capture GPU pass." );
			if ( span.End <= start || span.Start >= end ) continue;
			if ( kept.Count >= limit ) { lost++; continue; }
			kept.Add( span );
			keptIndex.Add( i );
		}
		for ( int i = 0; i < kept.Count; i++ )
		{
			var parent = kept[i].Parent < 0 ? -1 : keptIndex.BinarySearch( kept[i].Parent );
			kept[i] = kept[i] with { Parent = parent < 0 ? -1 : parent };
		}
		return new( sites, kept.ToArray() );
	}
}
