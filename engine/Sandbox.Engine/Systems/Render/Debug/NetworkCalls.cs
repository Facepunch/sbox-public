namespace Sandbox;

internal static partial class DebugOverlay
{
	public class NetworkCalls
	{
		private const string FontName = "Roboto Mono";
		private const int FontWeight = 600;

		static string GetShortName( string name )
		{
			var shortName = name;
			var parts = name.Split( '.' );

			if ( parts.Length >= 2 )
			{
				shortName = $"{parts[^2]}.{parts[^1]}";
			}

			return shortName;
		}

		internal static void Draw( ref Vector2 position )
		{
			var system = NetworkDebugSystem.Current;
			if ( system is null )
				return;

			if ( DrawStats( "Inbound Network Calls", system.InboundStats, ref position ) )
				position.y += 18;

			DrawStats( "Inbound Sync Vars", system.SyncVarInboundStats, ref position );
		}

		private static bool DrawStats( string title, Dictionary<string, NetworkDebugSystem.MessageStats> stats, ref Vector2 position )
		{
			if ( stats is not { Count: > 0 } )
				return false;

			var sortedStats = stats.OrderByDescending( kv => kv.Value.TotalBytes ).Take( 20 );
			var totalBytesAll = stats.Values.Sum( s => s.TotalBytes );

			var x = position.x;
			var y = position.y;

			var header = new TextRendering.Scope( title, Color.Yellow, 13, FontName, FontWeight )
			{
				Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 2 }
			};

			Hud.DrawText( header, new Rect( x, y, 300f, 14f ), TextFlag.LeftTop );
			y += 24;

			var biggestNameWidth = 0f;

			foreach ( var (name, _) in sortedStats )
			{
				var shortName = GetShortName( name );
				var scope = new TextRendering.Scope( shortName, Color.White, 11, FontName, FontWeight );
				var size = scope.Measure();

				if ( size.x > biggestNameWidth )
					biggestNameWidth = size.x;
			}

			const float barMaxWidth = 50f;
			var colName = x + barMaxWidth + 6f;
			var colNameWidth = MathF.Max( 180f, biggestNameWidth + 8f );
			var colCalls = colName + colNameWidth + 16f;
			const float colCallsWidth = 50f;
			var colKB = colCalls + colCallsWidth + 16f;
			const float colKBWidth = 60f;
			var colPercent = colKB + colKBWidth + 16f;
			const float colPercentWidth = 60f;
			var colBPerMsg = colPercent + colPercentWidth + 16f;
			const float colBPerMsgWidth = 90f;

			var headerDim = Color.White;
			var headerScope = new TextRendering.Scope( "", headerDim, 10, FontName, 700 )
			{
				Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 2 },
				Text = "NAME"
			};

			Hud.DrawText( headerScope, new Rect( colName, y, colNameWidth, 12f ), TextFlag.LeftCenter );
			headerScope.Text = "CALLS";
			Hud.DrawText( headerScope, new Rect( colCalls, y, colCallsWidth, 12f ), TextFlag.RightCenter );
			headerScope.Text = "TOTAL";
			Hud.DrawText( headerScope, new Rect( colKB, y, colKBWidth, 12f ), TextFlag.RightCenter );
			headerScope.Text = "SHARE";
			Hud.DrawText( headerScope, new Rect( colPercent, y, colPercentWidth, 12f ), TextFlag.RightCenter );
			headerScope.Text = "B/MSG";
			Hud.DrawText( headerScope, new Rect( colBPerMsg, y, colBPerMsgWidth, 12f ), TextFlag.RightCenter );

			y += 14;

			foreach ( var (name, stat) in sortedStats )
			{
				var pct = totalBytesAll > 0 ? (stat.TotalBytes / (float)totalBytesAll) : 0f;
				var color = pct switch
				{
					>= 0.5f => Color.Red.WithAlpha( 0.9f ),
					>= 0.25f => Color.Orange.WithAlpha( 0.9f ),
					>= 0.1f => Color.Yellow.WithAlpha( 0.9f ),
					_ => Color.White.WithAlpha( 0.9f )
				};

				var barHeight = 10f;
				var barWidth = barMaxWidth * pct;
				var rowHeight = 14f;

				Hud.DrawRect(
					new Rect( x, y + (rowHeight / 2f) - (barHeight / 2f), barWidth, barHeight ),
					color.WithAlpha( 0.4f )
				);

				var shortName = GetShortName( name );
				var outline = new TextRendering.Outline
				{
					Color = Color.Black,
					Enabled = true,
					Size = 2
				};

				var scope = new TextRendering.Scope( shortName, color, 11, FontName, FontWeight ) { Outline = outline };
				Hud.DrawText( scope, new Rect( colName, y, colNameWidth, rowHeight ), TextFlag.LeftCenter );

				scope = new TextRendering.Scope( $"{stat.TotalCalls:N0}x", color, 11, FontName, FontWeight ) { Outline = outline };
				Hud.DrawText( scope, new Rect( colCalls, y, colCallsWidth, rowHeight ), TextFlag.RightCenter );

				scope = new TextRendering.Scope( $"{stat.TotalBytes / 1024f:0.0} KB", color, 11, FontName, FontWeight ) { Outline = outline };
				Hud.DrawText( scope, new Rect( colKB, y, colKBWidth, rowHeight ), TextFlag.RightCenter );

				scope = new TextRendering.Scope( $"{pct * 100f:0.0}%", color, 11, FontName, FontWeight ) { Outline = outline };
				Hud.DrawText( scope, new Rect( colPercent, y, colPercentWidth, rowHeight ), TextFlag.RightCenter );

				scope = new TextRendering.Scope( $"{stat.BytesPerMessage} B/msg", color, 11, FontName, FontWeight ) { Outline = outline };
				Hud.DrawText( scope, new Rect( colBPerMsg, y, colBPerMsgWidth, rowHeight ), TextFlag.RightCenter );

				y += rowHeight;
			}

			position.y = y;
			return true;
		}
	}
}
