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
		static long _allocBytesSum = 0;



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
			_allocBytesSum = 0;
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
			}

			var ls = Sandbox.Diagnostics.PerformanceStats.LastSecond;

			if ( now - _lastFlushTime >= 1.0 )
			{
				_lastFlushTime = now;

				foreach ( var e in _scope.Entries )
				{
					var prev = _accumByType.GetValueOrDefault( e.Name );
					_accumByType[e.Name] = (prev.Bytes + (long)e.TotalBytes, prev.Count + (long)e.Count);
					_allocBytesSum += (long)e.TotalBytes;
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
			var mbPerSec = liveElapsed > 0 ? _allocBytesSum / (1024.0 * 1024.0 * liveElapsed) : 0.0;
			var mbTotal = _allocBytesSum / (1024.0 * 1024.0);
			var elapsedSecondsInt = (int)liveElapsed;
			var windowLabel = elapsedSecondsInt >= 60 ? $"{elapsedSecondsInt / 60}m {elapsedSecondsInt % 60}s" : $"{elapsedSecondsInt}s";

			headerScope.Text = $"GC Pauses (tracked for {windowLabel})";
			Hud.DrawText( headerScope, new Rect( x, y, 512, 14 ), TextFlag.LeftTop );
			y += 16;

			DrawSummaryRow( x, ref y, scope, dimScope, "Gen (0/1/2):", $"{_gen0Sum + ls.Gc0} / {_gen1Sum + ls.Gc1} / {_gen2Sum + ls.Gc2}" );
			DrawSummaryRow( x, ref y, scope, dimScope, "Total:", $"{mbTotal:N1} MB" );
			DrawSummaryRow( x, ref y, scope, dimScope, "Rate:", $"{mbPerSec:N2} MB/s" );
			DrawSummaryRow( x, ref y, scope, dimScope, "Avg:", $"{avgMs:N2}ms" );
			DrawSummaryRow( x, ref y, scope, dimScope, "Min:", $"{lowestPauseMs:N2}ms" );
			DrawSummaryRow( x, ref y, scope, dimScope, "Max:", $"{highestPauseMs:N2}ms" );
			DrawSummaryRow( x, ref y, scope, dimScope, "Sum:", $"{sumMs:N2}ms" );
			DrawSummaryRow( x, ref y, scope, dimScope, $"Frames with >{StutterThresholdTicks / TimeSpan.TicksPerMillisecond}ms GC:", $"{_stutterCount} frames" );
			DrawSummaryRow( x, ref y, scope, dimScope, "GC%:", $"{sumMs / (liveElapsed * 1000.0) * 100.0:N2}%" );
			DrawSummaryRow( x, ref y, scope, dimScope, "GC% since process start:", $"{gcMemInfo.PauseTimePercentage:N2}%" );

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
