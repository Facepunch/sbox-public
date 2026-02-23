using System.Collections.Concurrent;
using System.Diagnostics;

namespace Sandbox.Diagnostics;

public static partial class PerformanceStats
{
	public struct Block
	{
		public float FrameAvg;
		public float FrameMin;
		public float FrameMax;

		public long ByteAlloc;
		public int Gc0;
		public int Gc1;
		public int Gc2;
		public long GcPause;
	}

	/// <summary>
	/// Get the time taken, in seconds, that were required to process the previous frame.
	/// </summary>
	public static double FrameTime { get; internal set; }

	/// <summary>
	/// Latest available GPU frametime, in ms.
	/// </summary>
	public static float GpuFrametime { get; internal set; }

	/// <summary>
	/// Frame number of the last reported <see cref="GpuFrametime"/>.
	/// </summary>
	public static uint GpuFrameNumber { get; internal set; }

	/// <summary>
	/// The number of bytes that were allocated on the managed heap in the last frame.
	/// <remarks>This may not include allocations from threads other than the game thread.</remarks>
	/// </summary>
	public static long BytesAllocated { get; internal set; }

	/// <summary>
	/// Number of generation 0 (fastest) garbage collections were done in the last frame.
	/// </summary>
	public static int Gen0Collections { get; internal set; }

	/// <summary>
	/// Number of generation 1 (fast) garbage collections were done in the last frame.
	/// </summary>
	public static int Gen1Collections { get; internal set; }

	/// <summary>
	/// Number of generation 2 (slow) garbage collections were done in the last frame.
	/// </summary>
	public static int Gen2Collections { get; internal set; }

	/// <summary>
	/// How many ticks we paused in the last frame
	/// </summary>
	public static long GcPause { get; internal set; }

	/// <summary>
	/// Number of exceptions in the last frame.
	/// </summary>
	public static int Exceptions { get; internal set; }

	/// <summary>
	/// Approximate working set of this process.
	/// </summary>
	public static ulong ApproximateProcessMemoryUsage { get; internal set; }

	/// <summary>
	/// Performance statistics over the last period, which is dictated by "perf_time" console command.
	/// </summary>
	public static Block LastSecond { get; internal set; }

	private static Stopwatch frameTimer;
	private static Stopwatch secondTimer;
	private static List<Block> _history = new List<Block>( 1024 );
	private static long _prevAllocatedBytes;
	private static long _prevPauseTime;
	private static int _prevGen0, _prevGen1, _prevGen2;
	private static int _exceptions;
	private static int _lastSecond; // the actual rounded second of RealTime.Now when we last captured

	internal static bool Frame()
	{
		frameTimer ??= Stopwatch.StartNew();
		secondTimer ??= Stopwatch.StartNew();

		float frameMs = (float)frameTimer.Elapsed.TotalMilliseconds;

		PerformanceStats.FrameTime = frameTimer.Elapsed.TotalSeconds;
		frameTimer.Restart();

		if ( g_pRenderDevice.GetGPUFrameTimeMS( IntPtr.Zero, out float gpuFrametime, out uint gpuFrameNo ) )
		{
			GpuFrametime = gpuFrametime;
			GpuFrameNumber = gpuFrameNo;
		}

		var allocatedBytes = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
		PerformanceStats.BytesAllocated = allocatedBytes - _prevAllocatedBytes;
		_prevAllocatedBytes = allocatedBytes;

		var pauseTicks = GC.GetTotalPauseDuration().Ticks;
		GcPause = pauseTicks - _prevPauseTime;
		_prevPauseTime = pauseTicks;

		var gen0 = GC.CollectionCount( 0 );
		var gen1 = GC.CollectionCount( 1 );
		var gen2 = GC.CollectionCount( 2 );
		PerformanceStats.Gen0Collections = gen0 - _prevGen0;
		PerformanceStats.Gen1Collections = gen1 - _prevGen1;
		PerformanceStats.Gen2Collections = gen2 - _prevGen2;
		_prevGen0 = gen0;
		_prevGen1 = gen1;
		_prevGen2 = gen2;

		PerformanceStats.ApproximateProcessMemoryUsage = NativeEngine.EngineGlue.ApproximateProcessMemoryUsage();

		Timings.FlipAll();

		// how many exceptions happened between now and the last one
		Exceptions = Application.ExceptionCount - _exceptions;
		if ( Exceptions < 0 ) Exceptions = 0;
		_exceptions = Application.ExceptionCount;


		_history.Add( new Block
		{
			FrameAvg = frameMs,
			ByteAlloc = BytesAllocated,
			Gc0 = PerformanceStats.Gen0Collections,
			Gc1 = PerformanceStats.Gen1Collections,
			Gc2 = PerformanceStats.Gen2Collections,
			GcPause = GcPause
		} );

		var second = RealTime.Now.FloorToInt();
		if ( _lastSecond == second )
			return false;

		_lastSecond = second;

		var ls = new Block();
		ls.FrameAvg = _history.Average( x => x.FrameAvg );
		ls.FrameMin = _history.Min( x => x.FrameAvg );
		ls.FrameMax = _history.Max( x => x.FrameAvg );
		ls.ByteAlloc = _history.Sum( x => x.ByteAlloc );
		ls.Gc0 = _history.Sum( x => x.Gc0 );
		ls.Gc1 = _history.Sum( x => x.Gc1 );
		ls.Gc2 = _history.Sum( x => x.Gc2 );
		ls.GcPause = _history.Sum( x => x.GcPause );

		LastSecond = ls;

		_history.Clear();
		secondTimer.Restart();

		FrameStats._current = new FrameStats( NativeEngine.CSceneSystem.GetPerFrameStats() );

		return true;
	}

	public record struct PeriodMetric( float Min, float Max, float Avg, int Calls );
}


internal static class ObjectPool<T> where T : class, new()
{
	public static T Get()
	{
		if ( _pool.TryDequeue( out var o ) )
			return o;

		return new T();
	}

	public static void Return( T obj )
	{
		_pool.Enqueue( obj );
	}

	static ConcurrentQueue<T> _pool = new();
}
