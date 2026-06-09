namespace Sandbox.Diagnostics;

/// <summary>
/// GPU timing data for a single render pass/group
/// </summary>
public readonly struct GpuTimingEntry
{
	public string Name { get; init; }
	public float DurationMs { get; init; }
}

/// <summary>
/// GPU profiler stats collected from the scene system timestamp manager
/// </summary>
public static class GpuProfilerStats
{
	private static readonly List<GpuTimingEntry> _entries = new();
	private static readonly Dictionary<string, float> _smoothedDurations = new();
	private static readonly Dictionary<string, float> _maxDurations = new();
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
				_smoothedDurations.Clear();
				_maxDurations.Clear();
			}
		}
	}

	/// <summary>
	/// Total GPU time for all tracked passes
	/// </summary>
	public static float TotalGpuTimeMs { get; private set; }

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

	/// <summary>
	/// Get the current GPU timing entries
	/// </summary>
	public static IReadOnlyList<GpuTimingEntry> Entries => _entries;

	/// <summary>
	/// Get a smoothed duration for a given name (for display purposes)
	/// </summary>
	public static float GetSmoothedDuration( string name )
	{
		return _smoothedDurations.GetValueOrDefault( name, 0f );
	}

	/// <summary>
	/// Get a decayed max duration for a given name (for display purposes)
	/// </summary>
	public static float GetMaxDuration( string name )
	{
		return _maxDurations.GetValueOrDefault( name, 0f );
	}

	internal static void Update()
	{
		if ( !_enabled )
		{
			_entries.Clear();
			TotalGpuTimeMs = 0;
			return;
		}

		if ( !_hasMemoryStats || _lastMemoryStatsUpdate >= 1f )
		{
			UpdateMemoryStats();
		}

		_entries.Clear();
		float total = 0;

		int count = NativeEngine.CSceneSystem.GetGpuTimestampCount();
		for ( int i = 0; i < count; i++ )
		{
			var name = NativeEngine.CSceneSystem.GetGpuTimestampName( i );

			if ( string.IsNullOrEmpty( name ) )
				continue;

			float duration = NativeEngine.CSceneSystem.GetGpuTimestampDuration( i );
			total += duration;

			// Smooth the duration for display
			if ( _smoothedDurations.TryGetValue( name, out var smoothed ) )
			{
				smoothed = MathX.LerpTo( smoothed, duration, Time.Delta );
			}
			else
			{
				smoothed = duration;
			}
			_smoothedDurations[name] = smoothed;

			if ( _maxDurations.TryGetValue( name, out var maxDuration ) )
			{
				maxDuration = duration > maxDuration ? duration : MathX.LerpTo( maxDuration, duration, Time.Delta * 0.25f );
			}
			else
			{
				maxDuration = duration;
			}
			_maxDurations[name] = maxDuration;

			_entries.Add( new GpuTimingEntry
			{
				Name = name,
				DurationMs = duration
			} );
		}

		TotalGpuTimeMs = total;
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
}
