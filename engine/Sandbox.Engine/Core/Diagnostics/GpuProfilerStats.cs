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

		public bool Unparented;
	}

	private static readonly List<Row> _rows = new();
	private static readonly Dictionary<uint, Sample> _samples = new();
	private static readonly List<uint> _prune = new();

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
	internal static Row GetRow( int index ) => _rows[index];

	internal static float GetSmoothedDuration( uint stableId ) => _samples.TryGetValue( stableId, out var s ) ? s.Smoothed : 0f;
	internal static float GetMaxDuration( uint stableId ) => _samples.TryGetValue( stableId, out var s ) ? s.Max : 0f;

	internal static void Update()
	{
		if ( !_enabled )
		{
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

		int count = NativeEngine.CSceneSystem.GetGpuTimestampCount();
		for ( int i = 0; i < count; i++ )
		{
			var row = new Row
			{
				Name = NativeEngine.CSceneSystem.GetGpuTimestampName( i ),
				Parent = NativeEngine.CSceneSystem.GetGpuTimestampParent( i ),
				StableId = NativeEngine.CSceneSystem.GetGpuTimestampStableId( i ),
				Measured = NativeEngine.CSceneSystem.GetGpuTimestampMeasured( i ),
				Unparented = NativeEngine.CSceneSystem.GetGpuTimestampUnparented( i ),
			};

			_rows.Add( row );

			if ( !row.Measured )
				continue;

			var duration = NativeEngine.CSceneSystem.GetGpuTimestampDuration( i );

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
