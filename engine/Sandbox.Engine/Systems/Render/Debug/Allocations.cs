namespace Sandbox;

internal static partial class DebugOverlay
{
	/// <summary>
	/// Managed allocations by type, and GC pauses, for the performance hub's Memory tab. Tracking starts the first
	/// time the tab samples and stops when it closes, since an allocation scope isn't free.
	/// </summary>
	public partial class Allocations
	{
		static Sandbox.Diagnostics.Allocations.Scope _scope;
		static double _openTime = -1;
		static double _lastFlushTime = -1;
		static Dictionary<string, (long Bytes, long Count)> _accumByType = new( AccumCap );
		static List<(string Name, long Bytes, long Count)> _topAllocs = new( DisplayCount );

		const int DisplayCount = 50;
		const int AccumCap = DisplayCount * 10;

		// 2ms in TimeSpan ticks (100ns each)
		static readonly long StutterThresholdTicks = TimeSpan.FromMilliseconds( 2 ).Ticks;

		static long _frameCount = 0;
		static long _pauseTicksSum = 0;
		static long _stutterCount = 0;
		static long _pauseTicksMax = 0;
		static long _gen0Sum = 0;
		static long _gen1Sum = 0;
		static long _gen2Sum = 0;
		static long _allocBytesAtOpen = 0;

		/// <summary>
		/// The types that allocated the most since tracking started, biggest first.
		/// </summary>
		internal static IReadOnlyList<(string Name, long Bytes, long Count)> Top => _topAllocs;

		/// <summary>
		/// How long tracking has been running.
		/// </summary>
		internal static double WindowSeconds => _openTime < 0 ? 0 : RealTime.Now - _openTime;

		internal static long AllocatedBytes => _openTime < 0 ? 0 : GC.GetTotalAllocatedBytes( precise: false ) - _allocBytesAtOpen;

		internal static (long Gen0, long Gen1, long Gen2) Collections
		{
			get
			{
				var ls = Sandbox.Diagnostics.PerformanceStats.LastSecond;
				return (_gen0Sum + ls.Gc0, _gen1Sum + ls.Gc1, _gen2Sum + ls.Gc2);
			}
		}

		internal static double PauseSumMs => TimeSpan.FromTicks( _pauseTicksSum ).TotalMilliseconds;
		internal static double PauseMaxMs => TimeSpan.FromTicks( _pauseTicksMax ).TotalMilliseconds;

		/// <summary>
		/// Frames with a GC pause of 2 ms or more.
		/// </summary>
		internal static long Stutters => _stutterCount;

		internal static void Disabled()
		{
			_scope?.Stop();
			_scope = null;
			_openTime = -1;
			_lastFlushTime = -1;
			_frameCount = 0;
			_pauseTicksSum = 0;
			_stutterCount = 0;
			_pauseTicksMax = 0;
			_gen0Sum = 0;
			_gen1Sum = 0;
			_gen2Sum = 0;
			_allocBytesAtOpen = 0;
			_accumByType.Clear();
			_topAllocs.Clear();
		}

		/// <summary>
		/// Call once a frame while the tab is showing.
		/// </summary>
		internal static void Sample()
		{
			_scope ??= new();
			_scope.Start();

			var now = RealTime.Now;
			if ( _openTime < 0 )
			{
				_openTime = now;
				_lastFlushTime = now;
				_allocBytesAtOpen = GC.GetTotalAllocatedBytes( precise: false );
			}

			if ( now - _lastFlushTime >= 1.0 )
			{
				_lastFlushTime = now;

				foreach ( var e in _scope.Entries )
				{
					var prev = _accumByType.GetValueOrDefault( e.Name );
					_accumByType[e.Name] = (prev.Bytes + (long)e.TotalBytes, prev.Count + (long)e.Count);
				}

				_topAllocs.Clear();
				foreach ( var kv in _accumByType.OrderByDescending( x => x.Value.Bytes ).Take( DisplayCount ) )
					_topAllocs.Add( (kv.Key, kv.Value.Bytes, kv.Value.Count) );

				if ( _accumByType.Count > AccumCap )
				{
					var toRemove = _accumByType.OrderBy( x => x.Value.Bytes ).Take( _accumByType.Count - AccumCap ).Select( x => x.Key ).ToList();
					foreach ( var key in toRemove ) _accumByType.Remove( key );
				}

				_scope.Clear();

				var ls = Sandbox.Diagnostics.PerformanceStats.LastSecond;
				_gen0Sum += ls.Gc0;
				_gen1Sum += ls.Gc1;
				_gen2Sum += ls.Gc2;
			}

			var gcPause = Sandbox.Diagnostics.PerformanceStats.GcPause;
			_frameCount++;
			_pauseTicksSum += gcPause;
			if ( gcPause > _pauseTicksMax ) _pauseTicksMax = gcPause;
			if ( gcPause >= StutterThresholdTicks ) _stutterCount++;
		}
	}
}
