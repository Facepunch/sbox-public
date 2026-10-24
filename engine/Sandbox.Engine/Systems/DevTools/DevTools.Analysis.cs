using Sandbox.Engine;
using Sandbox.Engine.Settings;

namespace Sandbox.Diagnostics;

internal static partial class DevTools
{
	/// <summary>
	/// What's most likely limiting the frame rate right now. An inference from the timings we have, not a measurement.
	/// </summary>
	internal enum Limiter
	{
		/// <summary>
		/// Not enough frames to say.
		/// </summary>
		Unknown,

		/// <summary>
		/// Waiting on fps_max (or another cap) with time to spare.
		/// </summary>
		Capped,

		/// <summary>
		/// Frames land on the display's refresh interval - vsync is doing the waiting.
		/// </summary>
		VSync,

		/// <summary>
		/// The GPU takes about as long as the whole frame.
		/// </summary>
		Gpu,

		/// <summary>
		/// The GPU has slack, so the engine's own frame work is what takes the time.
		/// </summary>
		Cpu,
	}

	internal enum Severity
	{
		Good,
		Info,
		Warning,
		Bad,
	}

	/// <summary>
	/// Something worth looking at: what was seen, the evidence for it, and the tab that explains it.
	/// </summary>
	internal readonly record struct Issue( Severity Severity, string Title, string Detail, string Tab );

	/// <summary>
	/// One named cost, with the colour it's drawn in.
	/// </summary>
	internal readonly record struct Cost( string Name, Color Color, float AvgMs, float MaxMs, string Tooltip = null );

	/// <summary>
	/// One per-frame series over the analysis window.
	/// </summary>
	internal readonly record struct Stat( float Avg, float P50, float P99, float Max );

	/// <summary>
	/// Everything the dev tools shows, worked out a few times a second from the raw stats. Panels read this rather than the
	/// stats themselves, so they all agree and none of them does the maths twice.
	/// </summary>
	internal sealed class Summary
	{
		/// <summary>
		/// How many frames the stats below cover, and how long they took. The ring holds a fixed number of frames, so
		/// at a high frame rate this is less than the window we ask for - say what it really is.
		/// </summary>
		public int Frames;
		public float WindowMs;

		/// <summary>
		/// Observed frame intervals.
		/// </summary>
		public float FrameAvg, FrameMedian, FrameP99, FrameMax;
		public float Fps;

		/// <summary>
		/// The same, per series: the frame, the engine's work on it, the GPU, the cap wait and the dev tools.
		/// </summary>
		public Stat FrameTime, CpuTime, GpuTime, WaitTime, SelfTime;

		/// <summary>
		/// How much frame times spread around their average - pacing, where the percentiles are about the tail.
		/// </summary>
		public float FrameStdDev;

		/// <summary>
		/// Frame time minus the cap wait and the dev tools' own measured cost - the engine's work on the frame.
		/// </summary>
		public float CpuWorkAvg;
		public float CapWaitAvg;
		public float GpuAvg;

		/// <summary>
		/// What the dev tools itself costs a frame, on the main thread.
		/// </summary>
		public float SelfAvg;

		/// <summary>
		/// The frame time we're aiming for, and where that number came from.
		/// </summary>
		public float TargetMs;
		public float TargetFps => TargetMs > 0 ? 1000f / TargetMs : 0;
		public string TargetSource = "";

		public bool MeetingTarget;
		public Limiter Limiter;
		public Severity Rating;

		/// <summary>
		/// What's limiting the frame rate, in a word or two: the cap's name, "vsync 144 Hz", "GPU", "CPU".
		/// </summary>
		public string LimitText = "";

		/// <summary>
		/// Frames over <see cref="HitchMs"/> in the window - the hitches you feel.
		/// </summary>
		public int Spikes;
		public float WorstSpikeMs;
		public float HitchMs;

		/// <summary>
		/// Fraction of loop iterations that didn't render a frame.
		/// </summary>
		public float NotRendered;

		/// <summary>
		/// Main thread categories, and leaf GPU passes - passes with no measured children, where inclusive time is
		/// also the pass's own time.
		/// </summary>
		public readonly List<Cost> TopCpu = new();
		public readonly List<Cost> TopGpu = new();
		public readonly List<Issue> Issues = new();

		/// <summary>
		/// Each of the <see cref="Categories"/> over the window, frame by frame - what's typical for it, to hold a single
		/// frame up against.
		/// </summary>
		public readonly Stat[] CategoryTime = new Stat[Categories.Length];

		/// <summary>
		/// Managed allocation per frame over the window, in KiB.
		/// </summary>
		public Stat AllocKb;

		/// <summary>
		/// The worst an issue pointing at this tab gets, if any does.
		/// </summary>
		public Severity? TabSeverity( string tab )
		{
			Severity? worst = null;
			foreach ( var issue in Issues )
			{
				if ( issue.Tab != tab || issue.Severity < Severity.Warning ) continue;
				if ( worst is null || issue.Severity > worst ) worst = issue.Severity;
			}

			if ( !MeetingTarget && Frames > 0 && (tab == "cpu" && Limiter == Limiter.Cpu || tab == "gpu" && Limiter == Limiter.Gpu) )
				worst = worst is { } w && w > Rating ? w : Rating;

			return worst;
		}

		public int CountIssues( Severity severity ) => Issues.Count( x => x.Severity == severity );
	}

	/// <summary>
	/// The latest analysis. Replaced, never mutated, so a panel holding the old one keeps a consistent view.
	/// </summary>
	internal static Summary Current { get; private set; } = new();

	/// <summary>
	/// Hold everything the dev tools shows where it is. Collection carries on underneath; resuming picks up live.
	/// </summary>
	internal static bool Paused
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			_frozenFrames = value ? Frames.Clone() : null;
			PausedAt = value ? DateTime.Now : default;

			// A selected frame is only somewhere in the frozen frames
			if ( !value ) SelectedFrame = null;
		}
	}

	internal static DateTime PausedAt { get; private set; }

	static FrameHistory _frozenFrames;

	/// <summary>
	/// The frames to draw: the live ring, or the copy taken when the dev tools was paused.
	/// </summary>
	internal static FrameHistory ShownFrames => _frozenFrames ?? Frames;

	const float AnalysisWindowMs = 2000f;
	const float AnalysisInterval = 0.25f;
	static RealTimeSince _sinceAnalysis = AnalysisInterval;
	static readonly float[] _sorted = new float[FrameHistory.Capacity];
	static long _prevLoopFrames, _prevRenderedFrames;
	static RealTimeSince _sinceFrameCounts;
	static float _notRendered;
	static int _prevExceptions = -1;
	static int _exceptionsLastSecond;

	/// <summary>
	/// Rebuild <see cref="Current"/> if it's due. Cheap to call every frame.
	/// </summary>
	internal static void Analyse( bool force = false )
	{
		if ( Paused )
			return;

		if ( !force && _sinceAnalysis < AnalysisInterval )
			return;

		_sinceAnalysis = 0;
		Current = Summarise();
		UpdateGpuTimeline();
	}

	/// <summary>
	/// A fresh analysis of the live frames, whether or not the dev tools is open or paused.
	/// </summary>
	internal static Summary Summarise()
	{
		UpdateRates();

		var s = new Summary();
		AnalyseFrames( s );
		AnalyseCosts( s );
		FindIssues( s );
		return s;
	}

	/// <summary>
	/// Every named main thread scope with time in it, not just the categories, biggest first. Scopes nest, so they overlap.
	/// </summary>
	internal static void GetScopes( List<Cost> into )
	{
		into.Clear();
		foreach ( var (_, timing) in PerformanceStats.Timings.All )
		{
			if ( timing == PerformanceStats.Timings.Idle ) continue;

			var m = timing.GetMetric( 120 );
			if ( m.Avg < 0.01f ) continue;

			into.Add( new Cost( timing.Name, timing.Color, m.Avg, m.Max ) );
		}

		into.Sort( static ( a, b ) => b.AvgMs.CompareTo( a.AvgMs ) );
	}

	/// <summary>
	/// One GPU pass in submission order. <see cref="BranchMs"/> is the most any pass under it (itself included) took,
	/// for deciding what's worth listing - scopes overlap, so a parent's time isn't the sum of its children's.
	/// </summary>
	internal readonly record struct GpuPass( uint Id, string Name, int Depth, bool Measured, float AvgMs, float MaxMs, float BranchMs );

	static int[] _gpuDepth = [];
	static float[] _gpuBranch = [];

	/// <summary>
	/// Every GPU pass in the order it was submitted, with its depth under its parent.
	/// </summary>
	internal static void GetGpuPasses( List<GpuPass> into )
	{
		into.Clear();

		var rows = GpuProfilerStats.Rows;
		if ( _gpuDepth.Length < rows.Length )
		{
			_gpuDepth = new int[rows.Length];
			_gpuBranch = new float[rows.Length];
		}

		// Parents come before their children, so depth fills forwards and branch time backwards
		for ( int i = 0; i < rows.Length; i++ )
		{
			var parent = rows[i].Parent;
			_gpuDepth[i] = parent >= 0 && parent < i ? _gpuDepth[parent] + 1 : 0;
			_gpuBranch[i] = rows[i].Measured ? GpuProfilerStats.GetSmoothedDuration( rows[i].StableId ) : 0;
		}

		for ( int i = rows.Length - 1; i >= 0; i-- )
		{
			var parent = rows[i].Parent;
			if ( parent >= 0 && parent < i ) _gpuBranch[parent] = MathF.Max( _gpuBranch[parent], _gpuBranch[i] );
		}

		for ( int i = 0; i < rows.Length; i++ )
		{
			var row = rows[i];
			var avg = row.Measured ? GpuProfilerStats.GetSmoothedDuration( row.StableId ) : 0;
			var max = row.Measured ? GpuProfilerStats.GetMaxDuration( row.StableId ) : 0;
			into.Add( new GpuPass( row.StableId, row.Name, _gpuDepth[i], row.Measured, avg, max, _gpuBranch[i] ) );
		}
	}

	static void UpdateRates()
	{
		// Loop vs rendered frames, and exceptions, over about a second
		var elapsed = _sinceFrameCounts.Relative;
		if ( elapsed < 1f )
			return;

		if ( elapsed < 3f )
		{
			var loops = EngineLoop.LoopFrames - _prevLoopFrames;
			var rendered = EngineLoop.RenderedFrames - _prevRenderedFrames;
			_notRendered = loops > 0 ? Math.Clamp( 1f - rendered / (float)loops, 0, 1 ) : 0;
		}

		_prevLoopFrames = EngineLoop.LoopFrames;
		_prevRenderedFrames = EngineLoop.RenderedFrames;

		var exceptions = Application.ExceptionCount;
		_exceptionsLastSecond = _prevExceptions < 0 ? 0 : Math.Max( 0, exceptions - _prevExceptions );
		_prevExceptions = exceptions;

		_sinceFrameCounts = 0;
	}

	static void AnalyseFrames( Summary s )
	{
		var frames = Frames;
		var n = frames.FramesCovering( AnalysisWindowMs );
		s.Frames = n;
		s.NotRendered = _notRendered;
		(s.TargetMs, s.TargetSource) = GetTarget();

		if ( n < 8 )
		{
			s.Limiter = Limiter.Unknown;
			s.Rating = Severity.Info;
			return;
		}

		s.FrameTime = Measure( n, frames.FrameMs );
		s.CpuTime = Measure( n, frames.BusyMs );
		s.WaitTime = Measure( n, frames.IdleMs );
		s.GpuTime = Measure( n, frames.GpuMs, skipZero: true );
		s.SelfTime = Measure( n, frames.SelfMs );
		s.AllocKb = Measure( n, frames.AllocKb );
		for ( int c = 0; c < Categories.Length; c++ )
			s.CategoryTime[c] = Measure( n, age => frames.CategoryMs( age, c ) );

		for ( int i = 0; i < n; i++ ) s.WindowMs += frames.FrameMs( i );
		double variance = 0;
		for ( int i = 0; i < n; i++ ) variance += Math.Pow( frames.FrameMs( i ) - s.FrameTime.Avg, 2 );
		s.FrameStdDev = (float)Math.Sqrt( variance / n );
		s.FrameAvg = s.FrameTime.Avg;
		s.FrameMedian = s.FrameTime.P50;
		s.FrameP99 = s.FrameTime.P99;
		s.FrameMax = s.FrameTime.Max;
		s.CpuWorkAvg = s.CpuTime.Avg;
		s.CapWaitAvg = s.WaitTime.Avg;
		s.GpuAvg = s.GpuTime.Avg;
		s.SelfAvg = s.SelfTime.Avg;
		s.Fps = s.FrameAvg > 0 ? 1000f / s.FrameAvg : 0;

		// A hitch is a frame you'd feel: twice the usual, and well over what we're aiming for
		s.HitchMs = MathF.Max( s.FrameMedian * 2f, s.TargetMs * 1.5f );
		for ( int i = 0; i < n; i++ )
		{
			var f = frames.FrameMs( i );
			if ( f < s.HitchMs ) continue;
			s.Spikes++;
			s.WorstSpikeMs = MathF.Max( s.WorstSpikeMs, f );
		}

		Judge( s );
	}

	static Stat Measure( int n, Func<int, float> sample, bool skipZero = false )
	{
		var count = 0;
		double sum = 0;
		for ( int i = 0; i < n; i++ )
		{
			var v = sample( i );
			if ( skipZero && v <= 0 ) continue;
			_sorted[count++] = v;
			sum += v;
		}

		if ( count == 0 ) return default;

		Array.Sort( _sorted, 0, count );
		var p99 = _sorted[Math.Clamp( (int)MathF.Ceiling( 0.99f * (count - 1) ), 0, count - 1 )];
		return new Stat( (float)(sum / count), _sorted[count / 2], p99, _sorted[count - 1] );
	}

	/// <summary>
	/// The frame time we'd like to hit: the frame cap if there is one, else the display's refresh, else 60.
	/// </summary>
	static (float Ms, string Source) GetTarget()
	{
		var limit = EngineLoop.FrameRateLimit;
		if ( limit.FramesPerSecond > 0 )
			return ((float)(1000.0 / limit.FramesPerSecond), limit.Source);

		if ( GameSurface.Current is { } surface && surface.RefreshRate > 0 )
			return (1000f / surface.RefreshRate, "display refresh");

		return (1000f / 60f, "default");
	}

	/// <summary>
	/// Two separate calls: are we meeting the target, and what's most likely limiting us.
	/// </summary>
	static void Judge( Summary s )
	{
		// Judge the work as it would be without the dev tools open - an estimate, since the dev tools' cost isn't the only thing
		// that would change
		var frame = MathF.Max( 0, s.FrameAvg - s.SelfAvg );
		var gpu = s.GpuAvg;
		var vsync = GameSurface.Current?.VSync ?? RenderSettings.Instance.VSync;
		var refresh = GameSurface.Current?.RefreshRate ?? 0;

		if ( s.CapWaitAvg > frame * 0.1f )
		{
			s.Limiter = Limiter.Capped;
			s.LimitText = s.TargetSource;
		}
		// Vsync waits inside present, so it shows as frame time rather than cap wait
		else if ( vsync && refresh > 0 && MathF.Abs( frame - 1000f / refresh ) < (1000f / refresh) * 0.05f && gpu < frame * 0.85f )
		{
			s.Limiter = Limiter.VSync;
			s.LimitText = $"vsync {refresh:0} Hz";
		}
		else if ( gpu > 0 && gpu >= frame * 0.85f )
		{
			s.Limiter = Limiter.Gpu;
			s.LimitText = "GPU";
		}
		else
		{
			s.Limiter = Limiter.Cpu;
			s.LimitText = "CPU";
		}

		s.MeetingTarget = frame <= s.TargetMs * 1.05f || s.Limiter is Limiter.Capped or Limiter.VSync;
		s.Rating = s.MeetingTarget ? (s.Spikes > 0 ? Severity.Warning : Severity.Good)
			: frame <= s.TargetMs * 1.5f ? Severity.Warning
			: Severity.Bad;
	}

	static void AnalyseCosts( Summary s )
	{
		// Main thread categories. Idle is the cap, not a cost.
		foreach ( var t in PerformanceStats.Timings.GetMain() )
		{
			if ( t == PerformanceStats.Timings.Idle ) continue;

			var m = t.GetMetric( 120 );
			if ( m.Avg < 0.01f && m.Max < 0.05f ) continue;

			s.TopCpu.Add( new Cost( t.Name, t.Color, m.Avg, m.Max ) );
		}

		s.TopCpu.Sort( static ( a, b ) => b.AvgMs.CompareTo( a.AvgMs ) );

		// Leaf GPU passes. Scopes can overlap, so subtracting children from a parent doesn't give its own time - but a
		// pass with no measured children has nothing to subtract.
		var rows = GpuProfilerStats.Rows;
		Span<bool> hasChildren = rows.Length <= 1024 ? stackalloc bool[rows.Length] : new bool[rows.Length];
		for ( int i = 0; i < rows.Length; i++ )
		{
			var parent = rows[i].Parent;
			if ( rows[i].Measured && parent >= 0 && parent < rows.Length ) hasChildren[parent] = true;
		}

		for ( int i = 0; i < rows.Length; i++ )
		{
			var row = rows[i];
			if ( !row.Measured || hasChildren[i] ) continue;

			var avg = GpuProfilerStats.GetSmoothedDuration( row.StableId );
			if ( avg < 0.02f ) continue;

			var path = row.Parent >= 0 && row.Parent < rows.Length ? $"{rows[row.Parent].Name} / {row.Name}" : row.Name;
			s.TopGpu.Add( new Cost( row.Name, GpuColor( row.Parent >= 0 ? rows[row.Parent].StableId : row.StableId ), avg, GpuProfilerStats.GetMaxDuration( row.StableId ), path ) );
		}

		s.TopGpu.Sort( static ( a, b ) => b.AvgMs.CompareTo( a.AvgMs ) );
	}

	/// <summary>
	/// A stable colour for a GPU pass family - children share their parent's, so related passes read as one.
	/// </summary>
	static Color GpuColor( uint familyId ) => DevToolsStyles.Series[familyId % (uint)DevToolsStyles.Series.Length];

	static void FindIssues( Summary s )
	{
		var issues = s.Issues;

		if ( s.Spikes > 0 )
		{
			var sev = s.Spikes > 4 || s.WorstSpikeMs > 100 ? Severity.Bad : Severity.Warning;
			issues.Add( new Issue( sev, s.Spikes == 1 ? "Hitch" : $"{s.Spikes} hitches",
				$"over {s.HitchMs:0} ms in {s.WindowMs / 1000:0.0} s, worst {s.WorstSpikeMs:0} ms", "cpu" ) );
		}

		var second = PerformanceStats.LastSecond;
		var gcMs = (float)TimeSpan.FromTicks( second.GcPause ).TotalMilliseconds;
		if ( gcMs > 2f || second.Gc2 > 0 )
		{
			var sev = gcMs > 8f || second.Gc2 > 0 ? Severity.Bad : Severity.Warning;
			var gen = second.Gc2 > 0 ? $", {second.Gc2} gen 2" : "";
			issues.Add( new Issue( sev, "GC pauses", $"{gcMs:0.0} ms in the last second{gen}", "memory" ) );
		}

		var allocPerSecond = (double)second.ByteAlloc;
		if ( allocPerSecond > 20 * 1024 * 1024 )
		{
			issues.Add( new Issue( allocPerSecond > 100 * 1024 * 1024 ? Severity.Bad : Severity.Warning, "Allocations",
				$"{DevToolsFormat.Bytes( allocPerSecond )}/s managed", "memory" ) );
		}

		if ( GpuProfilerStats.VideoMemoryBudget > 0 )
		{
			var vram = GpuProfilerStats.VideoMemoryUsageFraction;
			if ( vram > 0.8f )
			{
				issues.Add( new Issue( vram > 0.95f ? Severity.Bad : Severity.Warning, "Video memory",
					$"{DevToolsFormat.BytesOf( GpuProfilerStats.VideoMemoryUsed, GpuProfilerStats.VideoMemoryBudget )} ({vram * 100:0}%)", "memory" ) );
			}
		}

		var stats = FrameStats.Current;
		if ( stats.TexturePoolLimitBytes > 0 && stats.TexturePoolUsedBytes > stats.TexturePoolLimitBytes * 0.95 )
		{
			issues.Add( new Issue( Severity.Warning, "Texture pool full",
				$"{DevToolsFormat.BytesOf( stats.TexturePoolUsedBytes, stats.TexturePoolLimitBytes )}, streaming will drop mips", "rendering" ) );
		}

		if ( stats.PendingStreamingRequests > 32 )
		{
			issues.Add( new Issue( Severity.Info, "Streaming backlog", $"{stats.PendingStreamingRequests} pending", "rendering" ) );
		}

		if ( s.NotRendered > 0.05f )
		{
			issues.Add( new Issue( Severity.Warning, "Frames not presented", $"{s.NotRendered * 100:0}% of loop iterations", "cpu" ) );
		}

		if ( _exceptionsLastSecond > 0 )
		{
			issues.Add( new Issue( Severity.Bad, "Exceptions", $"{_exceptionsLastSecond} in the last second, see console", null ) );
		}

		if ( Networking.IsActive && !Networking.IsHost && Connection.Host is { } host )
		{
			var ping = host.Stats.Ping;
			if ( ping > 150 )
				issues.Add( new Issue( ping > 300 ? Severity.Bad : Severity.Warning, "Ping", $"{ping} ms to host", "network" ) );

			if ( host.Stats.ConnectionQuality is > 0 and < 0.8f )
				issues.Add( new Issue( Severity.Warning, "Connection quality", $"{host.Stats.ConnectionQuality * 100:0}%", "network" ) );
		}

		issues.Sort( static ( a, b ) => b.Severity.CompareTo( a.Severity ) );
	}
}

/// <summary>
/// Number formatting the dev tools shares.
/// </summary>
internal static class DevToolsFormat
{
	/// <summary>
	/// Bytes in binary units, the way memory is counted: 842 MiB, 22.19 GiB.
	/// </summary>
	public static string Bytes( double bytes )
	{
		const double KiB = 1024, MiB = KiB * 1024, GiB = MiB * 1024;
		return bytes switch
		{
			>= GiB => $"{bytes / GiB:0.00} GiB",
			>= MiB * 100 => $"{bytes / MiB:0} MiB",
			>= MiB => $"{bytes / MiB:0.0} MiB",
			>= KiB => $"{bytes / KiB:0.0} KiB",
			_ => $"{bytes:0} B",
		};
	}

	/// <summary>
	/// A used/total pair in the total's unit, so it reads as one figure: 0.79 / 22.19 GiB.
	/// </summary>
	public static string BytesOf( double used, double total )
	{
		const double KiB = 1024, MiB = KiB * 1024, GiB = MiB * 1024;
		return total switch
		{
			>= GiB => $"{used / GiB:0.00} / {total / GiB:0.00} GiB",
			>= MiB => $"{used / MiB:0} / {total / MiB:0} MiB",
			_ => $"{used / KiB:0} / {total / KiB:0} KiB",
		};
	}

	/// <summary>
	/// A large count, compactly: 13,907 / 14k / 1.2M.
	/// </summary>
	public static string Count( double value ) => value switch
	{
		>= 1_000_000 => $"{value / 1_000_000:0.0}M",
		>= 100_000 => $"{value / 1000:0}k",
		_ => $"{value:N0}",
	};

	/// <summary>
	/// Milliseconds with the precision small values need.
	/// </summary>
	public static string Ms( double ms ) => ms >= 10 ? $"{ms:0.0}" : $"{ms:0.00}";
}
