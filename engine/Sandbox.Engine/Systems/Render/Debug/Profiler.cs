namespace Sandbox;

internal static partial class DebugOverlay
{
	public partial class Profiler
	{
		static readonly Dictionary<string, float> _smoothedAvgWidth = new();
		static readonly List<RowData> _rows = new( 64 );
		static readonly TextRendering.Outline _outline = new() { Color = Color.Black, Size = 2, Enabled = true };

		const float RowHeight = 16f;
		const float NameWidth = 234f;
		const float GaugeWidth = 160f;
		const float ValueWidth = 68f;
		const float GaugeScaleMs = 30f;

		readonly struct RowData
		{
			public readonly string Name;
			public readonly Color Color;
			public readonly float AvgMs;
			public readonly float MaxMs;

			public RowData( string name, Color color, float avgMs, float maxMs )
			{
				Name = name;
				Color = color;
				AvgMs = avgMs;
				MaxMs = maxMs;
			}
		}

		internal static void Draw( Painter painter, ref Vector2 pos )
		{
			var timings = PerformanceStats.Timings.GetMain();
			_rows.Clear();

			foreach ( var t in timings )
			{
				var windowMetric = t.GetMetric( 256 );
				_rows.Add( new RowData( t.Name, t.Color, windowMetric.Avg, windowMetric.Max ) );
			}

			_rows.Sort( static ( a, b ) => b.AvgMs.CompareTo( a.AvgMs ) );

			var totalFrameMs = MathF.Max( (float)(PerformanceStats.FrameTime * 1000.0), 0.001f );

			var x = pos.x;
			var y = pos.y;
			var colName = x;
			var colGauge = colName + NameWidth + 8;
			var colAvg = colGauge + GaugeWidth + 10;
			var colMax = colAvg + ValueWidth;
			var colShare = colMax + ValueWidth;

			DrawTitle( painter, ref y, x );
			DrawSummary( painter, ref y, x, totalFrameMs );
			DrawHeader( painter, ref y, x, colAvg, colMax, colShare );

			for ( var i = 0; i < _rows.Count; i++ )
			{
				var row = _rows[i];
				DrawRow( painter, ref y, row.Name, row.Color, colName, colGauge, colAvg, colMax, colShare, row.AvgMs, row.MaxMs, totalFrameMs );
			}

			pos.y = y;
		}

		static void DrawTitle( Painter painter, ref float y, float x )
		{
			var scope = new TextRendering.Scope( "Main Thread Timings", Color.White.WithAlpha( 0.9f ), 11, "Roboto Mono", 700 ) { Outline = _outline };
			DebugOverlay.DrawText( painter, scope, new Rect( x, y, 420, RowHeight ), TextFlag.LeftCenter );
			y += RowHeight;
		}

		static void DrawSummary( Painter painter, ref float y, float x, float totalMs )
		{
			var fpsMax = 1000f / MathF.Max( totalMs, 0.001f );
			var color = totalMs > 16.67f ? new Color( 1f, 0.65f, 0.35f ) : Color.White.WithAlpha( 0.9f );
			var scope = new TextRendering.Scope( $"CPU total {totalMs:F2}ms  ({fpsMax:F0} fps max)", color, 11, "Roboto Mono", 700 ) { Outline = _outline };
			DebugOverlay.DrawText( painter, scope, new Rect( x, y, 560, RowHeight ), TextFlag.LeftCenter );
			y += RowHeight;
		}

		static void DrawHeader( Painter painter, ref float y, float x, float colAvg, float colMax, float colShare )
		{
			var dim = Color.White.WithAlpha( 0.55f );
			DrawTextCell( painter, "name", dim, x, y, NameWidth, TextFlag.LeftCenter );
			DrawTextCell( painter, "avg", dim, colAvg, y, ValueWidth, TextFlag.LeftCenter );
			DrawTextCell( painter, "max", dim, colMax, y, ValueWidth, TextFlag.LeftCenter );
			DrawTextCell( painter, "%", dim, colShare, y, ValueWidth, TextFlag.LeftCenter );

			y += RowHeight;
		}

		static void DrawRow( Painter painter, ref float y, string name, Color color, float colName, float colGauge, float colAvg, float colMax, float colShare, float avgMs, float maxMs, float totalFrameMs )
		{
			DrawTextCell( painter, name, color.Lighten( 0.45f ), colName, y, NameWidth, TextFlag.LeftCenter );

			var gauge = new Rect( colGauge, y + 1, GaugeWidth, RowHeight - 2 );
			painter.Fill = Color.Black.WithAlpha( 0.1f );
			painter.Stroke = Stroke.None;
			painter.Rect( gauge.SnapToGrid() );

			var maxW = MathF.Min( gauge.Width, (maxMs / GaugeScaleMs) * gauge.Width );
			painter.Fill = color.WithAlpha( 0.14f );
			painter.Stroke = Stroke.None;
			painter.Rect( new Rect( gauge.Left, gauge.Top, MathF.Max( 1, maxW ), gauge.Height ).SnapToGrid() );

			var targetAvgWidth = MathF.Min( gauge.Width, (avgMs / GaugeScaleMs) * gauge.Width );
			if ( _smoothedAvgWidth.TryGetValue( name, out var prev ) )
				targetAvgWidth = MathX.LerpTo( prev, targetAvgWidth, Time.Delta * 14 );
			_smoothedAvgWidth[name] = targetAvgWidth;
			painter.Fill = color.WithAlpha( 0.65f );
			painter.Stroke = Stroke.None;
			painter.Rect( new Rect( gauge.Left, gauge.Top, MathF.Max( 1, targetAvgWidth ), gauge.Height ).SnapToGrid() );

			var sharePct = (avgMs / totalFrameMs) * 100f;
			var valueColor = Color.White.WithAlpha( 0.85f );
			DrawTextCell( painter, $"{avgMs:F2}ms", valueColor, colAvg, y, ValueWidth, TextFlag.LeftCenter );
			DrawTextCell( painter, $"{maxMs:F2}ms", valueColor, colMax, y, ValueWidth, TextFlag.LeftCenter );
			DrawTextCell( painter, $"{sharePct:F1}%", valueColor, colShare, y, ValueWidth, TextFlag.LeftCenter );

			y += RowHeight + 1;
		}

		static void DrawTextCell( Painter painter, string text, Color color, float x, float y, float width, TextFlag flag )
		{
			var scope = new TextRendering.Scope( text, color, 11, "Roboto Mono", 600 ) { Outline = _outline };
			DebugOverlay.DrawText( painter, scope, new Rect( x, y, width, RowHeight ), flag );
		}
	}
}
