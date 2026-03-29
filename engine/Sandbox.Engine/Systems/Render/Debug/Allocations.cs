namespace Sandbox;

internal static partial class DebugOverlay
{
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
		static long _pauseTicksMin = long.MaxValue;
		static long _pauseTicksMax = 0;
		static long _gen0Sum = 0;
		static long _gen1Sum = 0;
		static long _gen2Sum = 0;
		static long _allocBytesAtOpen = 0;

		internal static void Disabled()
		{
			_scope?.Stop();
			_scope = null;
			_openTime = -1;
			_lastFlushTime = -1;
			_frameCount = 0;
			_pauseTicksSum = 0;
			_stutterCount = 0;
			_pauseTicksMin = long.MaxValue;
			_pauseTicksMax = 0;
			_gen0Sum = 0;
			_gen1Sum = 0;
			_gen2Sum = 0;
			_allocBytesAtOpen = 0;
			_accumByType.Clear();
			_topAllocs.Clear();
		}

		internal static void Draw( ref Vector2 pos )
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

			var ls = Sandbox.Diagnostics.PerformanceStats.LastSecond;

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

				_gen0Sum += ls.Gc0;
				_gen1Sum += ls.Gc1;
				_gen2Sum += ls.Gc2;
			}

			var gcPause = Sandbox.Diagnostics.PerformanceStats.GcPause;
			_frameCount++;
			_pauseTicksSum += gcPause;
			if ( gcPause < _pauseTicksMin ) _pauseTicksMin = gcPause;
			if ( gcPause > _pauseTicksMax ) _pauseTicksMax = gcPause;
			if ( gcPause >= StutterThresholdTicks ) _stutterCount++;

			var liveElapsed = now - _openTime;
			if ( liveElapsed < 1.0 )
				return;

			var x = pos.x;
			var y = pos.y;

			var scope = new TextRendering.Scope( "", Color.White, 11, "Roboto Mono", 600 )
			{
				Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 2 }
			};
			var headerScope = new TextRendering.Scope( "", Color.White.WithAlpha( 0.9f ), 11, "Roboto Mono", 700 )
			{
				Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 2 }
			};
			var dimScope = new TextRendering.Scope( "", Color.White.WithAlpha( 0.78f ), 10, "Roboto Mono", 600 )
			{
				Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 2 }
			};

			var lowestPauseMs = TimeSpan.FromTicks( _pauseTicksMin == long.MaxValue ? 0 : _pauseTicksMin ).TotalMilliseconds;
			var highestPauseMs = TimeSpan.FromTicks( _pauseTicksMax ).TotalMilliseconds;
			var sumMs = TimeSpan.FromTicks( _pauseTicksSum ).TotalMilliseconds;
			var avgMs = _frameCount > 0 ? TimeSpan.FromTicks( _pauseTicksSum / _frameCount ).TotalMilliseconds : 0.0;

			var gcMemInfo = GC.GetGCMemoryInfo();
			var allocBytesTotal = GC.GetTotalAllocatedBytes( precise: false ) - _allocBytesAtOpen;
			var mbPerSec = liveElapsed > 0 ? allocBytesTotal / (1024.0 * 1024.0 * liveElapsed) : 0.0;
			var mbTotal = allocBytesTotal / (1024.0 * 1024.0);
			var elapsedSecondsInt = (int)liveElapsed;
			var windowLabel = elapsedSecondsInt >= 60 ? $"{elapsedSecondsInt / 60}m {elapsedSecondsInt % 60}s" : $"{elapsedSecondsInt}s";

			headerScope.Text = $"Allocations & GC ({windowLabel} window)";
			Hud.DrawText( headerScope, new Rect( x, y, 512, 14 ), TextFlag.LeftTop );
			y += 16;

			DrawSummaryRow( x, ref y, scope, dimScope, "Gen (0/1/2):", $"{_gen0Sum + ls.Gc0} / {_gen1Sum + ls.Gc1} / {_gen2Sum + ls.Gc2}" );
			DrawSummaryRow( x, ref y, scope, dimScope, "Total / Rate:", $"{mbTotal:N1} MB / {mbPerSec:N2} MB/s" );
			DrawSummaryRow( x, ref y, scope, dimScope, "Pause Avg / Min / Max:", $"{avgMs:N2}ms / {lowestPauseMs:N2}ms / {highestPauseMs:N2}ms" );
			DrawSummaryRow( x, ref y, scope, dimScope, "GC Pause Sum:", $"{sumMs:N2}ms" );
			DrawSummaryRow( x, ref y, scope, dimScope, "GC Pause %:", $"{sumMs / (liveElapsed * 1000.0) * 100.0:N2}% window, {gcMemInfo.PauseTimePercentage:N2}% lifetime" );
			DrawSummaryRow( x, ref y, scope, dimScope, $"Stutters (>{StutterThresholdTicks / TimeSpan.TicksPerMillisecond}ms):", $"{_stutterCount} frames" );

			y += 8;
			headerScope.TextColor = new Color( 0.5f, 1f, 1f );
			headerScope.Text = "Managed Heap (last GC)";
			Hud.DrawText( headerScope, new Rect( x, y, 512, 14 ), TextFlag.LeftTop );
			y += 16;

			// Stats from GCMemoryInfo — these are the modern .NET equivalents of the
			// .NET Framework "Memory performance counters" (PerformanceCounter objects).
			var genInfo = gcMemInfo.GenerationInfo;
			var heapFragPct = gcMemInfo.HeapSizeBytes > 0 ? (double)gcMemInfo.FragmentedBytes / gcMemInfo.HeapSizeBytes * 100.0 : 0.0;
			DrawSummaryRow( x, ref y, scope, dimScope, "Heap (frag):", $"{gcMemInfo.HeapSizeBytes.FormatBytes()} ({heapFragPct:N1}%)" );
			DrawSummaryRow( x, ref y, scope, dimScope, "Committed:", gcMemInfo.TotalCommittedBytes.FormatBytes() );
			var g0 = genInfo.Length > 0 ? genInfo[0].SizeAfterBytes.FormatBytes() : "?";
			var g1 = genInfo.Length > 1 ? genInfo[1].SizeAfterBytes.FormatBytes() : "?";
			var g2 = genInfo.Length > 2 ? genInfo[2].SizeAfterBytes.FormatBytes() : "?";
			DrawSummaryRow( x, ref y, scope, dimScope, "Gen (0/1/2):", $"{g0} / {g1} / {g2}" );
			if ( genInfo.Length > 3 ) DrawSummaryRow( x, ref y, scope, dimScope, "LOH:", genInfo[3].SizeAfterBytes.FormatBytes() );
			DrawSummaryRow( x, ref y, scope, dimScope, "Pinned / Pending Finalizer:", $"{gcMemInfo.PinnedObjectsCount} / {gcMemInfo.FinalizationPendingCount}" );

			y += 8;
			headerScope.TextColor = new Color( 1f, 1f, 0.5f );
			headerScope.Text = $"Top {_topAllocs.Count} Allocations";
			Hud.DrawText( headerScope, new Rect( x, y, 512, 14 ), TextFlag.LeftTop );
			y += 14;

			dimScope.Text = "bytes";
			Hud.DrawText( dimScope, new Rect( x + 10, y, 64, 12 ), TextFlag.RightTop );
			dimScope.Text = "count";
			Hud.DrawText( dimScope, new Rect( x + 80, y, 62, 12 ), TextFlag.RightTop );
			dimScope.Text = "type";
			Hud.DrawText( dimScope, new Rect( x + 152, y, 320, 12 ), TextFlag.LeftTop );
			y += 14;

			foreach ( var e in _topAllocs )
			{
				scope.TextColor = GetLineColor( e.Name );

				{
					scope.Text = e.Bytes.FormatBytes();
					Hud.DrawText( scope, new Rect( x + 10, y, 64, 13 ), TextFlag.RightTop );
				}

				{
					scope.Text = e.Count.KiloFormat();
					Hud.DrawText( scope, new Rect( x + 80, y, 62, 13 ), TextFlag.RightTop );
				}

				{
					scope.Text = e.Name;
					Hud.DrawText( scope, new Vector2( x + 152, y ), TextFlag.LeftTop );
				}

				y += 14;
			}

			pos.y = y;
		}

		static Color GetLineColor( string name )
		{
			if ( name.StartsWith( "System." ) ) return new Color( 0.7f, 1f, 0.7f );
			if ( name.StartsWith( "<GetAll>" ) ) return new Color( 1f, 1f, 0.7f );

			return Color.White;
		}

		static void DrawSummaryRow( float x, ref float y, TextRendering.Scope valueScope, TextRendering.Scope labelScope, string label, string value )
		{
			labelScope.Text = label;
			Hud.DrawText( labelScope, new Rect( x, y, 160, 13 ), TextFlag.LeftTop );
			valueScope.Text = value;
			valueScope.TextColor = Color.White.WithAlpha( 0.9f );
			Hud.DrawText( valueScope, new Rect( x + 168, y, 320, 13 ), TextFlag.LeftTop );
			y += 13;
		}
	}
}
