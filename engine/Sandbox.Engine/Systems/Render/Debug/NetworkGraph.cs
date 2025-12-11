namespace Sandbox;

internal static partial class DebugOverlay
{
	public class NetworkGraph
	{
		private static readonly Dictionary<NetworkDebugSystem.MessageType, Color> Colors = new()
		{
			[NetworkDebugSystem.MessageType.Rpc] = Color.Blue,
			[NetworkDebugSystem.MessageType.Spawn] = Color.Magenta,
			[NetworkDebugSystem.MessageType.Refresh] = Color.Orange,
			[NetworkDebugSystem.MessageType.Snapshot] = Color.Red,
			[NetworkDebugSystem.MessageType.SyncVars] = Color.Cyan,
			[NetworkDebugSystem.MessageType.Culling] = Color.Yellow,
			[NetworkDebugSystem.MessageType.StringTable] = Color.Green,
			[NetworkDebugSystem.MessageType.UserCommands] = Color.Black
		};

		private static float _smoothedInKbpsIn;
		private static float _smoothedScale = 10f;

		internal static void Draw( ref Vector2 position )
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

			foreach ( var (type, _) in Colors )
			{
				scope = new TextRendering.Scope( $"{type}", Color.White, 11, fontName, fontWeight );
				var size = scope.Measure();

				if ( size.x > biggestWidth )
					biggestWidth = size.x;
			}

			biggestWidth += legendBoxWidth + rulerTickLabelWidth + 8f;

			var graphX = position.x + biggestWidth + 16f;
			var graphY = position.y;

			var maxTotalBytes = Math.Max( system.Samples.Max( s => s.BytesPerType.Values.Sum() ), 1 );
			var visibleKb = MathF.Max( 1f, maxTotalBytes / 1024f ); // Always show at least 1KB of height
			var targetScale = graphHeight / visibleKb;

			_smoothedScale = _smoothedScale.LerpTo( targetScale, Time.Delta * 5f );

			Hud.DrawRect( new Rect( graphX, graphY, graphWidth, graphHeight ), Color.Black.WithAlpha( 0.2f ), borderWidth: 1, borderColor: Color.White.WithAlpha( 0.1f ) );

			var barWidth = graphWidth / NetworkDebugSystem.MaxSamples;
			var samples = system.Samples.ToArray();

			for ( var i = 0; i < samples.Length; i++ )
			{
				var sampleIndex = samples.Length - 1 - i;
				var sample = samples[sampleIndex];

				var x = graphX + graphWidth - ((i + 1) * barWidth);
				var y = graphY + graphHeight;
				var accumulatedHeight = 0f;

				foreach ( var kv in sample.BytesPerType.OrderBy( k => (int)k.Key ) )
				{
					var height = (kv.Value / 1024f) * _smoothedScale;
					var color = Colors[kv.Key];

					// Prevent total from overflowing the graph
					if ( accumulatedHeight + height > graphHeight )
					{
						height = graphHeight - accumulatedHeight;
						if ( height <= 0f ) break;
					}

					Hud.DrawRect(
						new Rect( x, y - height - accumulatedHeight, barWidth, height ),
						color.WithAlpha( 0.9f )
					);

					accumulatedHeight += height;
				}
			}

			var maxRulerKb = maxTotalBytes / 1024f;
			var rulerTicks = new List<float>();
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

				Hud.DrawRect( new Rect( graphX, y, graphWidth, 1 ), Color.White.WithAlpha( 0.2f ) );

				scope = new TextRendering.Scope( $"↑ {kb:0.##} KB", Color.White.WithAlpha( 0.8f ), 11, fontName, fontWeight )
				{
					Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 2 }
				};

				Hud.DrawText(
					scope,
					new Rect( graphX - rulerTickLabelWidth, y - 5f, 50f, 10f ),
					TextFlag.RightCenter
				);
			}

			var legendEntries = Colors.OrderBy( k => (int)k.Key ).ToArray();
			var legendHeight = legendEntries.Length * (legendRowHeight + 2);
			var legendPos = new Vector2( position.x, position.y + graphHeight - legendHeight );

			foreach ( var (type, color) in Colors.OrderBy( k => (int)k.Key ) )
			{
				Hud.DrawRect( new Rect( legendPos.x, legendPos.y, legendBoxWidth, legendBoxHeight ), color );

				scope = new TextRendering.Scope( $"{type}", Color.White.WithAlpha( 0.8f ), 11, fontName, fontWeight )
				{
					Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 2 }
				};

				Hud.DrawText(
					scope,
					new Rect( legendPos.x + legendBoxWidth + 2, legendPos.y - 2, 100f, legendRowHeight ),
					TextFlag.LeftCenter
				);

				legendPos.y += legendRowHeight + 2;
			}

			var samplesToAverage = (NetworkDebugSystem.SampleRate * 1).CeilToInt();
			var recent = system.Samples.TakeLast( samplesToAverage );
			var totalBytes = recent.Sum( s => s.BytesPerType.Values.Sum() );
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

			Hud.DrawText(
				scope,
				new Rect( graphX + graphWidth - 80f, graphY + 4f, 80f, 10f ),
				TextFlag.RightTop
			);

			var durationSeconds = MathF.Round( NetworkDebugSystem.MaxSamples * NetworkDebugSystem.SampleRate );
			var durationLabel = $"{durationSeconds:0} seconds";

			scope = new TextRendering.Scope(
				$"Last ~{durationLabel}",
				Color.White.WithAlpha( 0.8f ), 11, fontName, fontWeight
			);

			scope.Outline = new TextRendering.Outline
			{
				Color = Color.Black,
				Enabled = true,
				Size = 2
			};

			Hud.DrawText(
				scope,
				new Rect( graphX, graphY + graphHeight + 4f, graphWidth, 16f ),
				TextFlag.CenterTop
			);

			position.y += graphHeight;
		}
	}
}
