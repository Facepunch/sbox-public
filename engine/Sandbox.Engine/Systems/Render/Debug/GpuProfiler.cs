using Sandbox.Diagnostics;

namespace Sandbox;

internal static partial class DebugOverlay
{
	public partial class GpuProfiler
	{
		private static readonly Color[] PassColors = new[]
		{
			new Color( 0.4f, 0.7f, 1.0f ),   // Blue
			new Color( 0.4f, 1.0f, 0.5f ),   // Green
			new Color( 1.0f, 0.7f, 0.3f ),   // Orange
			new Color( 1.0f, 0.4f, 0.4f ),   // Red
			new Color( 0.8f, 0.5f, 1.0f ),   // Purple
			new Color( 1.0f, 1.0f, 0.4f ),   // Yellow
			new Color( 0.5f, 1.0f, 1.0f ),   // Cyan
			new Color( 1.0f, 0.5f, 0.8f ),   // Pink
		};

		private static readonly TextRendering.Outline _outline = new() { Color = Color.Black, Size = 2, Enabled = true };
		private static readonly List<RowData> _rows = new( 32 );

		private const float RowHeight = 16f;
		private const float NameWidth = 234f;
		private const float GaugeWidth = 160f;
		private const float ValueWidth = 74f;
		private const float GaugeScaleMs = 16f;
		private const int MaxRows = 20;
		private const float MinVisibleMs = 0.02f;

		private readonly struct RowData
		{
			public readonly string Name;
			public readonly float AvgMs;
			public readonly float MaxMs;
			public readonly Color Color;

			public RowData( string name, float avgMs, float maxMs, Color color )
			{
				Name = name;
				AvgMs = avgMs;
				MaxMs = maxMs;
				Color = color;
			}
		}

		internal static void Draw( ref Vector2 pos )
		{
			var entries = GpuProfilerStats.Entries;
			if ( entries.Count == 0 )
			{
				DrawNoData( ref pos );
				return;
			}

			_rows.Clear();

			for ( var i = 0; i < entries.Count; i++ )
			{
				var entry = entries[i];
				var avgMs = GpuProfilerStats.GetSmoothedDuration( entry.Name );
				var maxMs = GpuProfilerStats.GetMaxDuration( entry.Name );

				if ( avgMs < MinVisibleMs || entry.Name.StartsWith( "Managed:", StringComparison.Ordinal ) )
					continue;

				_rows.Add( new RowData( entry.Name, avgMs, maxMs, PassColors[i % PassColors.Length] ) );
			}

			_rows.Sort( static ( a, b ) => b.AvgMs.CompareTo( a.AvgMs ) );

			if ( _rows.Count > MaxRows )
				_rows.RemoveRange( MaxRows, _rows.Count - MaxRows );

			if ( _rows.Count == 0 )
			{
				DrawNoData( ref pos );
				return;
			}

			var totalMs = MathF.Max( GpuProfilerStats.TotalGpuTimeMs, 0.001f );
			var scaleMs = GaugeScaleMs;

			var x = pos.x;
			var y = pos.y;
			var colName = x;
			var colGauge = colName + NameWidth + 8;
			var colAvg = colGauge + GaugeWidth + 10;
			var colMax = colAvg + ValueWidth;
			var colShare = colMax + ValueWidth;

			DrawTitle( ref y, x );
			DrawSummary( ref y, x, totalMs );
			DrawHeader( ref y, colName, colAvg, colMax, colShare );

			for ( var i = 0; i < _rows.Count; i++ )
			{
				DrawRow( ref y, _rows[i], totalMs, scaleMs, colName, colGauge, colAvg, colMax, colShare );
			}

			pos.y = y;
		}

		private static void DrawTitle( ref float y, float x )
		{
			var scope = new TextRendering.Scope( "GPU Timings", Color.White.WithAlpha( 0.9f ), 11, "Roboto Mono", 700 ) { Outline = _outline };
			Hud.DrawText( scope, new Rect( x, y, 560, RowHeight ), TextFlag.LeftCenter );
			y += RowHeight;
		}

		private static void DrawSummary( ref float y, float x, float totalMs )
		{
			var fpsMax = 1000f / totalMs;
			var color = totalMs > 16.67f ? new Color( 1f, 0.65f, 0.35f ) : Color.White.WithAlpha( 0.9f );
			var scope = new TextRendering.Scope( $"GPU total {totalMs:F2}ms  ({fpsMax:F0} fps max)", color, 11, "Roboto Mono", 700 ) { Outline = _outline };
			Hud.DrawText( scope, new Rect( x, y, 560, RowHeight ), TextFlag.LeftCenter );
			y += RowHeight;
		}

		private static void DrawHeader( ref float y, float colName, float colAvg, float colMax, float colShare )
		{
			var dim = Color.White.WithAlpha( 0.55f );
			DrawCell( "pass", dim, colName, y, NameWidth, TextFlag.LeftCenter );
			DrawCell( "avg", dim, colAvg, y, ValueWidth, TextFlag.LeftCenter );
			DrawCell( "max", dim, colMax, y, ValueWidth, TextFlag.LeftCenter );
			DrawCell( "%", dim, colShare, y, ValueWidth, TextFlag.LeftCenter );
			y += RowHeight;
		}

		private static void DrawRow( ref float y, RowData row, float totalMs, float scaleMs, float colName, float colGauge, float colAvg, float colMax, float colShare )
		{
			DrawCell( row.Name, row.Color.Lighten( 0.45f ), colName, y, NameWidth, TextFlag.LeftCenter );

			var gauge = new Rect( colGauge, y + 1, GaugeWidth, RowHeight - 2 );
			Hud.DrawRect( gauge, Color.Black.WithAlpha( 0.1f ) );

			var maxW = MathF.Min( gauge.Width, (row.MaxMs / scaleMs) * gauge.Width );
			Hud.DrawRect( new Rect( gauge.Left, gauge.Top, MathF.Max( 1, maxW ), gauge.Height ), row.Color.WithAlpha( 0.14f ) );

			var avgW = MathF.Min( gauge.Width, (row.AvgMs / scaleMs) * gauge.Width );
			Hud.DrawRect( new Rect( gauge.Left, gauge.Top, MathF.Max( 1, avgW ), gauge.Height ), row.Color.WithAlpha( 0.65f ) );

			var sharePct = (row.AvgMs / totalMs) * 100f;
			var valueColor = Color.White.WithAlpha( 0.85f );
			DrawCell( $"{row.AvgMs:F2}ms", valueColor, colAvg, y, ValueWidth, TextFlag.LeftCenter );
			DrawCell( $"{row.MaxMs:F2}ms", valueColor, colMax, y, ValueWidth, TextFlag.LeftCenter );
			DrawCell( $"{sharePct:F1}%", valueColor, colShare, y, ValueWidth, TextFlag.LeftCenter );

			y += RowHeight + 1;
		}

		private static void DrawCell( string text, Color color, float x, float y, float width, TextFlag flag )
		{
			var scope = new TextRendering.Scope( text, color, 11, "Roboto Mono", 600 ) { Outline = _outline };
			Hud.DrawText( scope, new Rect( x, y, width, RowHeight ), flag );
		}

		private static void DrawNoData( ref Vector2 pos )
		{
			var scope = new TextRendering.Scope( "GPU profiler: waiting for data...", Color.White.WithAlpha( 0.6f ), 11, "Roboto Mono", 600 ) { Outline = _outline };
			Hud.DrawText( scope, new Rect( pos, new Vector2( 320, RowHeight ) ), TextFlag.LeftCenter );
			pos.y += RowHeight;
		}

	}
}
