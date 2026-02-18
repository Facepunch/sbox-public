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

		internal static void Draw( ref Vector2 pos )
		{
			var entries = GpuProfilerStats.Entries;
			if ( entries.Count == 0 )
			{
				DrawNoData( ref pos );
				return;
			}

			float labelWidth = 140;
			float height = 14;
			float y = pos.y;
			var left = pos.x;
			var mul = 200 / 16.0f; // Scale: 16ms = 200px

			// Draw header
			var headerRect = new Rect( left, y, 400, height );
			var headerScope = new TextRendering.Scope( "GPU Timings", Color.White, 12, "Roboto Mono", 700 )
			{
				Outline = new TextRendering.Outline { Color = Color.Black, Size = 3, Enabled = true }
			};
			Hud.DrawText( headerScope, headerRect, TextFlag.LeftCenter );
			y += height + 4;

			// Draw total
			float totalMs = GpuProfilerStats.TotalGpuTimeMs;
			var totalRect = new Rect( left, y, 400, height );
			var totalScope = new TextRendering.Scope( $"Total: {totalMs:F2}ms ({1000f / MathF.Max( totalMs, 0.001f ):F0} fps max)",
				totalMs > 16.67f ? new Color( 1f, 0.5f, 0.3f ) : Color.White.WithAlpha( 0.9f ), 11, "Roboto Mono", 600 )
			{
				Outline = new TextRendering.Outline { Color = Color.Black, Size = 2, Enabled = true }
			};
			Hud.DrawText( totalScope, totalRect, TextFlag.LeftCenter );
			y += height + 6;

			// Sort entries by smoothed duration (descending)
			var sortedEntries = entries
				.Select( ( entry, index ) => (entry, index, smoothed: GpuProfilerStats.GetSmoothedDuration( entry.Name )) )
				.OrderByDescending( x => x.smoothed )
				.ToList();

			// Draw each entry
			for ( int i = 0; i < sortedEntries.Count; i++ )
			{
				var (entry, originalIndex, smoothedDuration) = sortedEntries[i];
				var color = PassColors[originalIndex % PassColors.Length];

				// Too irrelevant, skip
				if ( smoothedDuration < 0.02 )
					continue;

				// Clean up managed marker names for display, we still need to collect them from native side otherwise it'll redundantly add timings to the next in list
				if ( entry.Name.StartsWith( "Managed:" ) )
					continue;

				var rowRect = new Rect( left, y, 500, height );

				// Draw label
				var labelScope = new TextRendering.Scope( entry.Name, color.Lighten( 0.5f ), 11, "Roboto Mono", 600 )
				{
					Outline = new TextRendering.Outline { Color = Color.Black, Size = 2, Enabled = true }
				};
				Hud.DrawText( labelScope, rowRect with { Width = labelWidth }, TextFlag.RightCenter );

				// Draw bar
				var barRect = rowRect with { Left = left + labelWidth + 8 };
				var barWidth = MathF.Max( smoothedDuration * mul, 2 );

				Hud.DrawRect( (barRect with { Width = entry.DurationMs * mul }).Shrink( 1 ), color.WithAlpha( 0.8f ), cornerRadius: new Vector4( 2 ) );
				Hud.DrawRect( barRect with { Width = barWidth }, color.WithAlpha( 0.2f ), borderWidth: new Vector4( 1 ), borderColor: Color.Black, cornerRadius: new Vector4( 2 ) );

				// Draw duration text
				var textRect = barRect with { Left = left + labelWidth + 16 + barWidth };
				var durationScope = new TextRendering.Scope( $"{entry.DurationMs:F2}ms", color.Lighten( 0.5f ), 11, "Roboto Mono", 600 )
				{
					Outline = new TextRendering.Outline { Color = Color.Black.WithAlpha( 0.8f ), Size = 2, Enabled = true }
				};
				Hud.DrawText( durationScope, textRect, TextFlag.LeftCenter );

				y += height + 2;
			}

			pos.y = y;
		}

		private static void DrawNoData( ref Vector2 pos )
		{
			var rect = new Rect( pos, new Vector2( 300, 14 ) );
			var scope = new TextRendering.Scope( "GPU Profiler: Waiting for data...", Color.White.WithAlpha( 0.6f ), 11, "Roboto Mono", 600 )
			{
				Outline = new TextRendering.Outline { Color = Color.Black, Size = 2, Enabled = true }
			};
			Hud.DrawText( scope, rect, TextFlag.LeftCenter );
			pos.y += rect.Height;
		}
	}
}
