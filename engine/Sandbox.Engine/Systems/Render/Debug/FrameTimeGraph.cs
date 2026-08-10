using Sandbox.Diagnostics;
using Sandbox.Engine.Settings;

namespace Sandbox;

internal static partial class DebugOverlay
{
	/// <summary>
	/// On-screen frametime overlay (overlay_fps 1): a live frametime strip (most recent frames, newest
	/// on the right) and a distribution histogram, plus headline stats. Mirrors the network graph
	/// overlay. Samples are only collected while the overlay is enabled.
	/// </summary>
	public class FrameTimeGraph
	{
		const int Capacity = 256;
		const int Buckets = 60;
		const int WorstCount = 5;
		static readonly double[] samples = new double[Capacity];
		static readonly double[] scratch = new double[Capacity];       // reused snapshot buffer, no per-frame alloc
		static readonly double[] scratchSorted = new double[Capacity]; // reused buffer for percentile sorting
		static readonly int[] histogram = new int[Buckets];            // reused histogram bins
		static int head;   // next write index
		static int count;

		static readonly Color Good = new( 0.40f, 0.85f, 0.50f );
		static readonly Color Warn = new( 1.00f, 0.72f, 0.20f );
		static readonly Color Bad = new( 0.95f, 0.32f, 0.22f );
		static readonly Color Dim = Color.White.WithAlpha( 0.8f );

		const string FontName = "Roboto Mono";
		const int FontWeight = 600;

		static float _smoothedMax = 20f;

		// Info lines are rebuilt on a timer, never per frame, so drawing stays allocation free.
		static long _prevLoopFrames, _prevRenderedFrames;
		static FastTimer _infoTimer = FastTimer.StartNew();
		static string _infoRates = "", _infoDisplay = "", _infoCaps = "", _infoWorst = "";
		static string _infoHeader = "", _labelMedian = "", _labelYMax = "";
		static double _notRendered;

		// Stats stashed by Draw so UpdateInfo can format them off the per-frame path.
		static double _sFps, _sAvg, _sStddev, _sLow1, _sMax, _sMedian, _sYMax;

		static readonly string[] worstNames = new string[WorstCount];
		static readonly float[] worstMax = new float[WorstCount];
		static readonly float[] worstAvg = new float[WorstCount];

		private static double prevShuttle;

		/// <summary>Push one frame's duration (ms). No-op unless the overlay is enabled.</summary>
		internal static void Sample( double frameMs )
		{
			if ( overlay_fps <= 0 )
				return;

			samples[head] = frameMs;
			head = (head + 1) % Capacity;
			if ( count < Capacity )
				count++;

			UpdateInfo();
		}

		static void UpdateInfo()
		{
			var elapsed = _infoTimer.ElapsedSeconds;
			if ( elapsed < 0.5 && _infoRates.Length > 0 )
				return;

			double loopRate = 0, renderedRate = 0;

			// Sampling stops while the overlay is off, so a long gap means a stale baseline.
			if ( elapsed <= 2.0 )
			{
				loopRate = (EngineLoop.LoopFrames - _prevLoopFrames) / elapsed;
				renderedRate = (EngineLoop.RenderedFrames - _prevRenderedFrames) / elapsed;
			}

			_prevLoopFrames = EngineLoop.LoopFrames;
			_prevRenderedFrames = EngineLoop.RenderedFrames;
			_infoTimer = FastTimer.StartNew();

			_notRendered = loopRate > 0 ? 1.0 - (renderedRate / loopRate) : 0;

			_infoRates = $"Frames/s   main loop {loopRate:0.0}   rendered {renderedRate:0.0}   GPU {PerformanceStats.GpuFrametime:0.0}ms"
				+ (_notRendered > 0.01 ? $"   {_notRendered * 100:0}% NOT RENDERED" : "");

			var rs = RenderSettings.Instance;
			var displayMode = rs.Fullscreen ? "Exclusive FS" : (rs.Borderless ? "Borderless" : "Windowed");
			_infoDisplay = $"{displayMode}   VSync {(rs.VSync ? "on" : "off")}   {EngineLoop.DisplayRefreshRate:0}Hz   "
				+ $"{Screen.Width:0}x{Screen.Height:0}   {rs.AntiAliasQuality}   Upscale {rs.UpscalerMode}";

			var effective = EngineLoop.EffectiveMaxFrameRate;
			_infoCaps = $"fps_max {rs.MaxFrameRate}   fps_max_menu {rs.MaxFrameRateMenu}   fps_max_inactive {rs.MaxFrameRateInactive}   "
				+ (effective > 0 ? $"-> capped at {effective:0} by {EngineLoop.MaxFrameRateSource}" : "-> uncapped");

			var found = CollectWorst();
			_infoWorst = "Slowest ms, worst/avg frame of last 60   ";
			for ( var i = 0; i < found; i++ )
				_infoWorst += $"{worstNames[i]} {worstMax[i]:0.0}/{worstAvg[i]:0.0}   ";

			_infoHeader = $"FrameTime   {_sFps:0} fps   {_sAvg:0.0}ms   stddev {_sStddev:0.0}ms   1%low {_sLow1:0} fps   max {_sMax:0.0}ms";
			_labelMedian = $"{_sMedian:0.0}ms";
			_labelYMax = $"{_sYMax:0.0}ms";
		}

		// Fills the worst* arrays with the slowest subsystems by worst frame. Returns how many are set.
		static int CollectWorst()
		{
			Array.Clear( worstMax, 0, WorstCount );
			Array.Clear( worstAvg, 0, WorstCount );
			Array.Clear( worstNames, 0, WorstCount );

			var found = 0;

			foreach ( var timing in PerformanceStats.Timings.GetMain() )
			{
				var metric = timing.GetMetric( 60 );
				if ( metric.Max < 0.05f )
					continue;

				for ( var i = 0; i < WorstCount; i++ )
				{
					if ( metric.Max <= worstMax[i] )
						continue;

					for ( var j = WorstCount - 1; j > i; j-- )
					{
						worstMax[j] = worstMax[j - 1];
						worstAvg[j] = worstAvg[j - 1];
						worstNames[j] = worstNames[j - 1];
					}

					worstMax[i] = metric.Max;
					worstAvg[i] = metric.Avg;
					worstNames[i] = timing.Name;

					if ( found < WorstCount ) found++;
					break;
				}
			}

			return found;
		}

		static Color ColorFor( double ms, double reference )
		{
			if ( ms <= reference * 1.25f ) return Good;
			if ( ms <= reference * 2.0f ) return Warn;
			return Bad;
		}

		internal static void Draw( ref Vector2 position, int verbosity )
		{
			if ( count == 0 )
				return;

			// Snapshot the ring oldest -> newest into the reused scratch buffer.
			var buf = scratch;
			for ( var i = 0; i < count; i++ )
				buf[i] = samples[(head - count + i + Capacity) % Capacity];

			// Stats.
			double sum = 0, sumSq = 0, min = double.MaxValue, max = 0;
			for ( var i = 0; i < count; i++ )
			{
				var ms = buf[i];
				sum += ms;
				sumSq += ms * ms;
				if ( ms < min ) min = ms;
				if ( ms > max ) max = ms;
			}
			var avg = sum / count;
			var variance = (sumSq / count) - (avg * avg);
			var stddev = variance > 0 ? MathF.Sqrt( (float)variance ) : 0;
			var fps = avg > 0 ? 1000.0 / avg : 0;

			Array.Copy( buf, scratchSorted, count );
			Array.Sort( scratchSorted, 0, count );
			var median = scratchSorted[count / 2];
			var p99 = scratchSorted[Math.Clamp( (int)MathF.Ceiling( 0.99f * (count - 1) ), 0, count - 1 )];
			var low1Fps = p99 > 0 ? 1000.0 / p99 : 0;

			// Shared vertical scale for both graphs — a bit above the worst recent frame, smoothed.
			var targetMax = MathF.Max( (float)max * 1.1f, (float)median * 2.5f );
			_smoothedMax = _smoothedMax.LerpTo( targetMax, Time.Delta * 5f );
			var yMax = MathF.Max( _smoothedMax, 1f );

			_sFps = fps; _sAvg = avg; _sStddev = stddev; _sLow1 = low1Fps; _sMax = max;
			_sMedian = median; _sYMax = yMax;

			const float graphWidth = 560f;
			const float stripHeight = 56f;
			const float histHeight = 64f;
			const float headerHeight = 16f;
			const float lineHeight = 13f;
			const int infoLines = 4;
			const float gap = 10f;

			var x = position.x;
			var y = position.y;

			// ---- header stats ----
			DrawLabel( _infoHeader, new Rect( x, y, graphWidth, headerHeight ), TextFlag.LeftTop, Color.White );
			y += headerHeight + 2f;

			// ---- info lines, built in UpdateInfo ----
			var rateColor = _notRendered > 0.05 ? Bad : (_notRendered > 0.01 ? Warn : Good);
			DrawLabel( _infoRates, new Rect( x, y, graphWidth, lineHeight ), TextFlag.LeftTop, rateColor );
			y += lineHeight;

			DrawLabel( _infoDisplay, new Rect( x, y, graphWidth, lineHeight ), TextFlag.LeftTop, Dim );
			y += lineHeight;

			DrawLabel( _infoCaps, new Rect( x, y, graphWidth, lineHeight ), TextFlag.LeftTop, Dim );
			y += lineHeight;

			DrawLabel( _infoWorst, new Rect( x, y, graphWidth, lineHeight ), TextFlag.LeftTop, Dim );
			y += lineHeight + gap;

			var stripRect = new Rect( x, y, graphWidth, stripHeight );
			Hud.DrawRect( stripRect, Color.Black.WithAlpha( 0.25f ), borderWidth: 1, borderColor: Color.White.WithAlpha( 0.1f ) );

			if ( verbosity < 2 )
			{
				// ---- live frametime strip (time series, newest on the right) ----

				var barW = graphWidth / Capacity;
				var stripBottom = y + stripHeight;
				for ( var i = 0; i < count; i++ )
				{
					var ms = buf[count - 1 - i]; // newest first
					var h = MathF.Min( stripHeight, (float)(ms / yMax) * stripHeight );
					var bx = x + graphWidth - (i + 1) * barW;
					Hud.DrawRect( new Rect( bx, stripBottom - h, MathF.Max( barW, 1f ), h ), ColorFor( ms, median ).WithAlpha( 0.9f ) );
				}

				// median (cadence) reference line on the strip
				var medY = stripBottom - MathF.Min( stripHeight, (float)(median / yMax) * stripHeight );
				Hud.DrawRect( new Rect( x, medY, graphWidth, 1f ), Color.White.WithAlpha( 0.35f ) );
				DrawLabel( _labelMedian, new Rect( x + graphWidth - 52f, medY - 11f, 50f, 10f ), TextFlag.RightBottom, Color.White.WithAlpha( 0.7f ) );
			}
			else
			{
				// ---- live frametime shuttle (fires left-right once a second), for slow-mo screen recordings ----

				var nextShuttle = RealTime.NowDouble;

				var shuttleX = (float)((prevShuttle - Math.Floor( prevShuttle )) * graphWidth);
				var shuttleWidth = (float)((nextShuttle - prevShuttle) * graphWidth);
				var shuttleColor = ColorFor( (nextShuttle - prevShuttle) * 1000.0, median ).WithAlpha( 0.9f );

				prevShuttle = nextShuttle;

				if ( shuttleX + shuttleWidth > graphWidth )
				{
					Hud.DrawRect( new Rect( x, y, Math.Min( shuttleX + shuttleWidth - graphWidth, graphWidth ), stripHeight ), shuttleColor );
				}

				Hud.DrawRect( new Rect( x + shuttleX, y, Math.Min( shuttleWidth, graphWidth - shuttleX ), stripHeight ), shuttleColor );
			}

			y += stripHeight + gap;

			// ---- distribution histogram ----
			const int buckets = Buckets;
			var bucketMs = yMax / buckets;
			var hist = histogram;
			Array.Clear( hist, 0, buckets );
			var histMaxCount = 1;
			for ( var i = 0; i < count; i++ )
			{
				var b = Math.Clamp( (int)(buf[i] / bucketMs), 0, buckets - 1 );
				if ( ++hist[b] > histMaxCount ) histMaxCount = hist[b];
			}

			var histRect = new Rect( x, y, graphWidth, histHeight );
			Hud.DrawRect( histRect, Color.Black.WithAlpha( 0.25f ), borderWidth: 1, borderColor: Color.White.WithAlpha( 0.1f ) );

			var hBarW = graphWidth / buckets;
			var histBottom = y + histHeight;
			for ( var b = 0; b < buckets; b++ )
			{
				if ( hist[b] == 0 ) continue;
				var h = MathF.Max( 1f, (float)hist[b] / histMaxCount * (histHeight - 2f) );
				var bx = x + b * hBarW;
				var msCenter = (b + 0.5) * bucketMs;
				Hud.DrawRect( new Rect( bx, histBottom - h, MathF.Max( hBarW - 0.5f, 1f ), h ), ColorFor( msCenter, median ).WithAlpha( 0.9f ) );
			}

			// median marker on the histogram
			var medX = x + MathF.Min( graphWidth, (float)(median / yMax) * graphWidth );
			Hud.DrawRect( new Rect( medX, y, 1f, histHeight ), Color.White.WithAlpha( 0.35f ) );

			// x-axis labels: 0, median, yMax
			DrawLabel( "0", new Rect( x, histBottom + 2f, 40f, 10f ), TextFlag.LeftTop, Color.White.WithAlpha( 0.6f ) );
			DrawLabel( _labelMedian, new Rect( medX - 25f, histBottom + 2f, 50f, 10f ), TextFlag.CenterTop, Color.White.WithAlpha( 0.6f ) );
			DrawLabel( _labelYMax, new Rect( x + graphWidth - 40f, histBottom + 2f, 40f, 10f ), TextFlag.RightTop, Color.White.WithAlpha( 0.6f ) );

			position.y += headerHeight + 2f + (infoLines * lineHeight) + stripHeight + gap + histHeight + 16f;
		}

		static void DrawLabel( string text, Rect rect, TextFlag flags, Color color )
		{
			var scope = new TextRendering.Scope( text, color, 11, FontName, FontWeight )
			{
				Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 2 }
			};
			Hud.DrawText( scope, rect, flags );
		}
	}
}
