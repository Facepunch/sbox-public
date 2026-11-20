namespace Sandbox.Diagnostics;

/// <summary>
/// GPU profiler stats collected from the scene system timestamp manager
/// </summary>
public static class GpuProfilerStats
{
	internal struct Row
	{
		public string Name;
		public int Parent;
		public uint StableId;
		public bool Measured;
		public float Duration;

		public bool Unparented;
	}

	private static readonly List<Row> _rows = new();
	private static readonly Dictionary<uint, Sample> _samples = new();
	private static readonly List<uint> _prune = new();

	// Scope names by StableId, decoded from native once instead of allocated per row every frame. The id already
	// identifies the scope - the smoothed timings and the overlay's rows are keyed by it - so its name doesn't change.
	private static readonly Dictionary<uint, (string Name, int LastSeenFrame)> _names = new();

	private struct Sample
	{
		public float Smoothed;
		public float Max;
		public int LastSeenFrame;
	}

	private static int _frame;
	private static bool _enabled;
	private static RealTimeSince _lastMemoryStatsUpdate;
	private static bool _hasMemoryStats;

	/// <summary>
	/// Whether GPU profiling is enabled
	/// </summary>
	public static bool Enabled
	{
		get => _enabled;
		set
		{
			if ( _enabled == value )
				return;

			_enabled = value;
			if ( !value )
			{
				_samples.Clear();
				_names.Clear();
				_paths = null;
			}
		}
	}

	/// <summary>
	/// GPU video memory budget in bytes.
	/// </summary>
	public static ulong VideoMemoryBudget { get; private set; }

	/// <summary>
	/// GPU video memory used by the engine in bytes.
	/// </summary>
	public static ulong VideoMemoryUsed { get; private set; }

	/// <summary>
	/// GPU video memory free within the current budget in bytes.
	/// </summary>
	public static ulong VideoMemoryFree { get; private set; }

	/// <summary>
	/// GPU video memory usage as a 0-1 fraction of budget.
	/// </summary>
	public static float VideoMemoryUsageFraction { get; private set; }

	internal static int RowCount => _rows.Count;
	internal static ulong FrameId { get; private set; }
	internal static string Renderer { get; private set; } = "unknown";
	internal static ReadOnlySpan<Row> Rows => System.Runtime.InteropServices.CollectionsMarshal.AsSpan( _rows );

	/// <summary>
	/// Borealis.Gpu's scopes of the last read frame, in the order of <see cref="Rows"/>.
	/// </summary>
	internal static ReadOnlySpan<GpuScope> Scopes => System.Runtime.InteropServices.CollectionsMarshal.AsSpan( _borealisScopes );
	internal static Row GetRow( int index ) => _rows[index];

	/// <summary>
	/// The graphics queue's measured passes this frame, added up: the time it was busy. Leaf passes on one queue
	/// don't overlap, so this is close to the frame's GPU time where nothing times the whole frame - the editor draws
	/// into widget swap chains, and only a main window's frame is timed.
	/// </summary>
	internal static float BusyMs { get; private set; }

	internal static float GetSmoothedDuration( uint stableId ) => _samples.TryGetValue( stableId, out var s ) ? s.Smoothed : 0f;
	internal static float GetMaxDuration( uint stableId ) => _samples.TryGetValue( stableId, out var s ) ? s.Max : 0f;

	internal static void Update()
	{
		if ( !_enabled )
		{
			FrameId = 0;
			Renderer = "unknown";
			BusyMs = 0;
			_rows.Clear();
			_paths = null;
			return;
		}

		if ( !_hasMemoryStats || _lastMemoryStatsUpdate >= 1f )
		{
			UpdateMemoryStats();
		}

		_frame++;

		// Borealis.Gpu's lists and passes (the busy-time pass timer's); a process without it draws nothing to time
		if ( RenderBackend.Default is BorealisGpuRenderBackend borealis )
		{
			UpdateFromBorealis( borealis );
			return;
		}

		FrameId = 0;
		Renderer = "none";
		BusyMs = 0;
		_rows.Clear();
		_paths = null;
	}

	static void AddSample( uint stableId, float duration, float delta )
	{
		if ( _samples.TryGetValue( stableId, out var sample ) )
		{
			sample.Smoothed = MathX.LerpTo( sample.Smoothed, duration, delta );
			sample.Max = duration > sample.Max ? duration : MathX.LerpTo( sample.Max, duration, delta * 0.25f );
		}
		else
		{
			sample.Smoothed = duration;
			sample.Max = duration;
		}

		sample.LastSeenFrame = _frame;
		_samples[stableId] = sample;
	}

	/// <summary>A timed GPU scope of another device's frame: a command list, or a pass or other work inside one.</summary>
	internal readonly record struct GpuScope( string Name, bool List, double Start, double End )
	{
		public double Ms => End - Start;
	}

	static ulong _borealisFrame;
	static RealTimeSince _sinceBorealisFrame;
	static readonly List<GpuScope> _borealisScopes = new();
	static readonly List<int> _borealisOpen = new();
	static readonly Dictionary<string, int> _borealisSeen = new();

	/// <summary>
	/// Rows from Borealis.Gpu's last read frame (<see cref="BuildRows"/>). Kept between reads (one every few frames), so the
	/// rows and their smoothing only move when a new frame was read.
	/// </summary>
	static void UpdateFromBorealis( BorealisGpuRenderBackend borealis )
	{
		Renderer = "borealis";
		var frame = borealis.GpuBusyFrame;
		if ( frame == _borealisFrame )
		{
			// Keep the rows' samples from being pruned while no new frame is read
			foreach ( var row in _rows )
			{
				if ( !_samples.TryGetValue( row.StableId, out var sample ) ) continue;
				sample.LastSeenFrame = _frame;
				_samples[row.StableId] = sample;
			}

			return;
		}

		var delta = (float)_sinceBorealisFrame;
		_sinceBorealisFrame = 0;
		_borealisFrame = frame;
		FrameId = frame;
		_paths = null;

		_borealisScopes.Clear();
		foreach ( var timing in borealis.GpuBusyTimings )
			_borealisScopes.Add( new GpuScope( timing.Name, timing.Kind == Borealis.Backend.PassTimer.ScopeKind.List, timing.Start, timing.End ) );

		BuildRows( _borealisScopes, _rows, _borealisOpen, _borealisSeen );

		float busy = 0;
		for ( int i = 0; i < _rows.Count; i++ )
		{
			AddSample( _rows[i].StableId, _rows[i].Duration, delta );
			if ( _rows[i].Parent < 0 ) busy += _rows[i].Duration;
		}

		PruneSamples();
		BusyMs = busy;
	}

	/// <summary>
	/// Rows for <paramref name="scopes"/> in the order they began on the GPU: lists at the top, each pass under the list or
	/// pass it ran inside. A name repeated in a frame (each view's prepass) gets an id per occurrence. Sorts
	/// <paramref name="scopes"/>; <paramref name="open"/> and <paramref name="seen"/> are scratch. Allocates nothing once they've grown.
	/// </summary>
	internal static void BuildRows( List<GpuScope> scopes, List<Row> rows, List<int> open, Dictionary<string, int> seen )
	{
		// Outer scopes first where they start together: lists, then the longer scope
		scopes.Sort( static ( a, b ) => a.Start != b.Start ? a.Start.CompareTo( b.Start ) : a.List != b.List ? (a.List ? -1 : 1) : b.End.CompareTo( a.End ) );

		rows.Clear();
		open.Clear();
		seen.Clear();
		foreach ( var scope in scopes )
		{
			while ( open.Count > 0 && scopes[open[^1]].End <= scope.Start ) open.RemoveAt( open.Count - 1 );
			var parent = open.Count > 0 ? open[^1] : -1;

			seen.TryGetValue( scope.Name, out var count );
			seen[scope.Name] = count + 1;

			rows.Add( new Row { Name = scope.Name, Parent = parent, StableId = (uint)HashCode.Combine( scope.Name, count, scope.List ), Measured = true, Duration = (float)scope.Ms } );
			open.Add( rows.Count - 1 );
		}
	}

	/// <summary>
	/// Drop rows that no longer appear for more than 120 frames, so when this scope shows up again it doesn't use 
	/// smoothened value from the last sample it used before
	/// </summary>
	private static void PruneSamples()
	{
		const int window = 120;

		if ( _frame % window != 0 )
			return;

		_prune.Clear();

		foreach ( var kv in _samples )
		{
			if ( _frame - kv.Value.LastSeenFrame > window )
				_prune.Add( kv.Key );
		}

		foreach ( var key in _prune )
			_samples.Remove( key );

		_prune.Clear();

		foreach ( var kv in _names )
		{
			if ( _frame - kv.Value.LastSeenFrame > window )
				_prune.Add( kv.Key );
		}

		foreach ( var key in _prune )
			_names.Remove( key );
	}

	private static void UpdateMemoryStats()
	{
		VideoMemoryBudget = Graphics.VideoMemoryBudget;
		VideoMemoryUsed = Graphics.VideoMemoryUsed;
		VideoMemoryFree = VideoMemoryUsed >= VideoMemoryBudget ? 0 : VideoMemoryBudget - VideoMemoryUsed;
		VideoMemoryUsageFraction = VideoMemoryBudget > 0
			? Math.Clamp( VideoMemoryUsed / (float)VideoMemoryBudget, 0f, 1f )
			: 0f;

		_lastMemoryStatsUpdate = 0;
		_hasMemoryStats = true;
	}

	// Path-based access, kept for compatibility. Nothing in the engine uses it, the overlay walks the rows
	// directly, so the strings are only built if something actually asks for them.
	private static List<string> _paths;
	private static Dictionary<string, uint> _pathIds;

	private static void EnsurePaths()
	{
		if ( _paths is not null )
			return;

		_paths = new List<string>( _rows.Count );
		_pathIds ??= new Dictionary<string, uint>();
		_pathIds.Clear();

		for ( int i = 0; i < _rows.Count; i++ )
		{
			var path = _rows[i].Name;

			for ( int p = _rows[i].Parent; p >= 0; p = _rows[p].Parent )
				path = string.Concat( _rows[p].Name, "/", path );

			_paths.Add( path );
			_pathIds[path] = _rows[i].StableId;
		}
	}

	/// <summary>
	/// Full '/'-separated paths of the current GPU timing scopes (split to build the tree).
	/// </summary>
	public static IReadOnlyList<string> Entries
	{
		get
		{
			EnsurePaths();
			return _paths;
		}
	}

	/// <summary>
	/// Get a smoothed duration for a given name (for display purposes)
	/// </summary>
	public static float GetSmoothedDuration( string name )
	{
		EnsurePaths();
		return _pathIds.TryGetValue( name, out var id ) ? GetSmoothedDuration( id ) : 0f;
	}

	/// <summary>
	/// Get a decayed max duration for a given name (for display purposes)
	/// </summary>
	public static float GetMaxDuration( string name )
	{
		EnsurePaths();
		return _pathIds.TryGetValue( name, out var id ) ? GetMaxDuration( id ) : 0f;
	}
}
