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
			NativeEngine.CSceneSystem.SetGPUProfilerMode( value ? NativeEngine.SceneSystemGPUProfilerMode.SCENE_GPU_PROFILER_TIMESTAMP_ONLY : NativeEngine.SceneSystemGPUProfilerMode.SCENE_GPU_PROFILER_DISABLE );

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
		_rows.Clear();
		_paths = null;

		NativeEngine.CSceneSystem.RefreshGpuTimestampSnapshot();
		FrameId = NativeEngine.CSceneSystem.GetGpuTimestampFrameId();
		Renderer = NativeEngine.CSceneSystem.GetGpuTimestampRenderer() switch
		{
			1 => "native",
			2 => "managed",
			3 => "managed-async",
			_ => "unknown"
		};

		int count = NativeEngine.CSceneSystem.GetGpuTimestampCount();
		for ( int i = 0; i < count; i++ )
		{
			var stableId = NativeEngine.CSceneSystem.GetGpuTimestampStableId( i );
			var name = _names.TryGetValue( stableId, out var known ) ? known.Name : NativeEngine.CSceneSystem.GetGpuTimestampName( i );
			_names[stableId] = (name, _frame);

			var row = new Row
			{
				Name = name,
				Parent = NativeEngine.CSceneSystem.GetGpuTimestampParent( i ),
				StableId = stableId,
				Measured = NativeEngine.CSceneSystem.GetGpuTimestampMeasured( i ),
				Unparented = NativeEngine.CSceneSystem.GetGpuTimestampUnparented( i ),
				Duration = NativeEngine.CSceneSystem.GetGpuTimestampDuration( i ),
			};

			_rows.Add( row );

			if ( !row.Measured )
				continue;

			var duration = row.Duration;

			if ( _samples.TryGetValue( row.StableId, out var sample ) )
			{
				sample.Smoothed = MathX.LerpTo( sample.Smoothed, duration, Time.Delta );
				sample.Max = duration > sample.Max ? duration : MathX.LerpTo( sample.Max, duration, Time.Delta * 0.25f );
			}
			else
			{
				sample.Smoothed = duration;
				sample.Max = duration;
			}

			sample.LastSeenFrame = _frame;
			_samples[row.StableId] = sample;
		}

		PruneSamples();
		BusyMs = GraphicsBusy();
	}

	/// <summary>
	/// Add up the measured passes with no measured pass inside them, outside the async compute queue.
	/// </summary>
	static float GraphicsBusy()
	{
		Span<bool> hasChild = _rows.Count <= 1024 ? stackalloc bool[_rows.Count] : new bool[_rows.Count];
		for ( int i = 0; i < _rows.Count; i++ )
		{
			var parent = _rows[i].Parent;
			if ( _rows[i].Measured && parent >= 0 && parent < _rows.Count ) hasChild[parent] = true;
		}

		float busy = 0;
		for ( int i = 0; i < _rows.Count; i++ )
		{
			if ( !_rows[i].Measured || hasChild[i] ) continue;

			var root = i;
			while ( _rows[root].Parent >= 0 && _rows[root].Parent < _rows.Count && _rows[root].Parent != root ) root = _rows[root].Parent;
			if ( _rows[root].Name == "Async compute" ) continue;

			busy += _rows[i].Duration;
		}

		return busy;
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
