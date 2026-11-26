namespace Sandbox;

internal static partial class DebugOverlay
{
	public class NetworkGraph
	{
		private static readonly Dictionary<NetworkDebugSystem.MessageType, Color> Colors = new()
		{
			[NetworkDebugSystem.MessageType.Rpc] = Sandbox.Diagnostics.DevToolsStyles.Series[0],
			[NetworkDebugSystem.MessageType.Spawn] = Sandbox.Diagnostics.DevToolsStyles.Series[1],
			[NetworkDebugSystem.MessageType.Refresh] = Sandbox.Diagnostics.DevToolsStyles.Series[2],
			[NetworkDebugSystem.MessageType.Snapshot] = Sandbox.Diagnostics.DevToolsStyles.Series[3],
			[NetworkDebugSystem.MessageType.SyncVars] = Sandbox.Diagnostics.DevToolsStyles.Series[4],
			[NetworkDebugSystem.MessageType.Culling] = Sandbox.Diagnostics.DevToolsStyles.Series[5],
			[NetworkDebugSystem.MessageType.StringTable] = Sandbox.Diagnostics.DevToolsStyles.Series[6],
			[NetworkDebugSystem.MessageType.UserCommands] = Sandbox.Diagnostics.DevToolsStyles.Series[7]
		};

		private static readonly (NetworkDebugSystem.MessageType Type, Color Color, string Name)[] Legend = Colors
			.OrderBy( k => (int)k.Key )
			.Select( k => (k.Key, k.Value, k.Key.ToString()) )
			.ToArray();

		private static readonly List<float> RulerTicks = new();

		private static readonly string DurationText = $"Last ~{MathF.Round( NetworkDebugSystem.MaxSamples * NetworkDebugSystem.SampleRate ):0} seconds";

		static int TotalBytes( NetworkDebugSystem.Sample sample )
		{
			var total = 0;
			foreach ( var bytes in sample.BytesPerType.Values )
				total += bytes;

			return total;
		}

		private static float _smoothedInKbpsIn;
		private static float _smoothedScale = 10f;

		internal static void Draw( Painter painter, ref Vector2 position )
		{
			var system = NetworkDebugSystem.Current;
			if ( system is null || system.Samples.Count == 0 ) return;

			var graphHeight = 150f;
			var graphWidth = 400f;

			TextRendering.Scope scope;
			var fontWeight = 600;
			var fontName = "Roboto Mono";

			const float legendRowHeight = 12f;
			const float legendBoxHeight = 8f;
			const float legendBoxWidth = 24f;
			const float rulerTickLabelWidth = 60f;
			var biggestWidth = 0f;

			foreach ( var (_, _, name) in Legend )
			{
				scope = new TextRendering.Scope( name, Color.White, 11, fontName, fontWeight );
				var size = scope.Measure();

				if ( size.x > biggestWidth )
					biggestWidth = size.x;
			}

			biggestWidth += legendBoxWidth + rulerTickLabelWidth + 8f;

			var graphX = position.x + biggestWidth + 16f;
			var graphY = position.y;

			var maxTotalBytes = 1;
			foreach ( var sample in system.Samples )
				maxTotalBytes = Math.Max( maxTotalBytes, TotalBytes( sample ) );

			var visibleKb = MathF.Max( 1f, maxTotalBytes / 1024f ); // Always show at least 1KB of height
			var targetScale = graphHeight / visibleKb;

			_smoothedScale = _smoothedScale.LerpTo( targetScale, Time.Delta * 5f );

			painter.BorderedRect( new Rect( graphX, graphY, graphWidth, graphHeight ).SnapToGrid(), Color.Black.WithAlpha( 0.2f ), cornerRadius: default, borderWidth: 1, borderColor: Color.White.WithAlpha( 0.1f ) );

			var barWidth = graphWidth / NetworkDebugSystem.MaxSamples;
			var sampleCount = system.Samples.Count;
			var sampleIndex = 0;

			foreach ( var sample in system.Samples )
			{
				var x = graphX + graphWidth - ((sampleCount - sampleIndex++) * barWidth);
				var y = graphY + graphHeight;
				var accumulatedHeight = 0f;

				foreach ( var (type, color, _) in Legend )
				{
					if ( !sample.BytesPerType.TryGetValue( type, out var bytes ) )
						continue;

					var height = (bytes / 1024f) * _smoothedScale;

					// Prevent total from overflowing the graph
					if ( accumulatedHeight + height > graphHeight )
					{
						height = graphHeight - accumulatedHeight;
						if ( height <= 0f ) break;
					}

					painter.Fill = color.WithAlpha( 0.9f );
					painter.Stroke = Stroke.None;
					painter.Rect( new Rect( x, y - height - accumulatedHeight, barWidth, height ).SnapToGrid() );

					accumulatedHeight += height;
				}
			}

			var maxRulerKb = maxTotalBytes / 1024f;
			var rulerTicks = RulerTicks;
			rulerTicks.Clear();
			var rulerStep = maxRulerKb switch
			{
				> 100f => 20f,
				> 50f => 10f,
				> 10f => 5f,
				_ => 1f
			};

			// Always show 0.1 KB if scale allows
			if ( _smoothedScale * 0.1f <= graphHeight )
			{
				rulerTicks.Add( 0.1f );
			}

			for ( var kb = rulerStep; kb <= maxRulerKb; kb += rulerStep )
			{
				if ( !rulerTicks.Contains( kb ) )
					rulerTicks.Add( kb );
			}

			rulerTicks.Sort();

			foreach ( var kb in rulerTicks )
			{
				var yOffset = kb * _smoothedScale;
				var y = graphY + graphHeight - yOffset;

				if ( y < graphY )
					continue;

				painter.Fill = Color.White.WithAlpha( 0.2f );
				painter.Stroke = Stroke.None;
				painter.Rect( new Rect( graphX, y, graphWidth, 1 ).SnapToGrid() );

				scope = new TextRendering.Scope( $"↑ {kb:0.##} KB", Color.White.WithAlpha( 0.8f ), 11, fontName, fontWeight )
				{
					Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 2 }
				};

				DebugOverlay.DrawText( painter,
					scope,
					new Rect( graphX - rulerTickLabelWidth, y - 5f, 50f, 10f ),
					TextFlag.RightCenter
				);
			}

			var legendHeight = Legend.Length * (legendRowHeight + 2);
			var legendPos = new Vector2( position.x, position.y + graphHeight - legendHeight );

			foreach ( var (_, color, name) in Legend )
			{
				painter.Fill = color;
				painter.Stroke = Stroke.None;
				painter.Rect( new Rect( legendPos.x, legendPos.y, legendBoxWidth, legendBoxHeight ).SnapToGrid() );

				scope = new TextRendering.Scope( name, Color.White.WithAlpha( 0.8f ), 11, fontName, fontWeight )
				{
					Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 2 }
				};

				DebugOverlay.DrawText( painter,
					scope,
					new Rect( legendPos.x + legendBoxWidth + 2, legendPos.y - 2, 100f, legendRowHeight ),
					TextFlag.LeftCenter
				);

				legendPos.y += legendRowHeight + 2;
			}

			var samplesToAverage = (1.0f / NetworkDebugSystem.SampleRate).CeilToInt();
			var skip = system.Samples.Count - samplesToAverage;
			var totalBytes = 0;
			foreach ( var sample in system.Samples )
			{
				if ( skip-- > 0 )
					continue;

				totalBytes += TotalBytes( sample );
			}

			var kbPerSecondIn = totalBytes / 1024f;

			_smoothedInKbpsIn = _smoothedInKbpsIn.LerpTo( kbPerSecondIn, Time.Delta * 5f );

			string rateText;
			if ( _smoothedInKbpsIn < 0.1f )
			{
				var bytesPerSec = (int)(_smoothedInKbpsIn * 1024f);
				rateText = $"IN: {bytesPerSec} B/s";
			}
			else
			{
				rateText = $"IN: {_smoothedInKbpsIn:0.0} KB/s";
			}

			scope = new TextRendering.Scope( rateText, Color.White.WithAlpha( 0.7f ), 11, fontName, fontWeight )
			{
				Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 2 }
			};

			DebugOverlay.DrawText( painter,
				scope,
				new Rect( graphX + graphWidth - 80f, graphY + 4f, 80f, 10f ),
				TextFlag.RightTop
			);

			scope = new TextRendering.Scope( DurationText, Color.White.WithAlpha( 0.8f ), 11, fontName, fontWeight );

			scope.Outline = new TextRendering.Outline
			{
				Color = Color.Black,
				Enabled = true,
				Size = 2
			};

			DebugOverlay.DrawText( painter,
				scope,
				new Rect( graphX, graphY + graphHeight + 4f, graphWidth, 16f ),
				TextFlag.CenterTop
			);

			position.y += graphHeight + 20f;
		}
	}
}
