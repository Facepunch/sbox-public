namespace Sandbox;

internal static partial class DebugOverlay
{
	[ConVar( "overlay_ui", Help = "Draws an overlay showing UI batching stats" )]
	internal static int overlay_ui { get; set; } = 0;

	public partial class UI
	{
		static readonly TextRendering.Outline _outline = new() { Color = Color.Black, Size = 2, Enabled = true };

		internal static void Draw( Painter painter, ref Vector2 pos )
		{
			var system = Engine.GlobalContext.Current.UISystem;
			var s = CollectStatistics( system, out var roots );
			var drawPos = new Vector2( pos.x + 24, pos.y );
			var startY = drawPos.y;

			DrawHeader( painter, ref drawPos, "UI — Current Context" );
			Row( painter, ref drawPos, "Roots", roots );
			Row( painter, ref drawPos, "Panels", s.PanelsRendered, $"({s.PanelsCulled} culled subtrees)" );
			Row( painter, ref drawPos, "CPU", s.CpuTime.TotalMilliseconds, "ms", "0.00" );
			Row( painter, ref drawPos, "Layout", s.LayoutTime.TotalMilliseconds, "ms", "0.00" );
			Row( painter, ref drawPos, "Command Build", s.CommandBuildTime.TotalMilliseconds, "ms", "0.00" );
			Row( painter, ref drawPos, "Submission", s.SubmissionTime.TotalMilliseconds, "ms", "0.00" );
			Row( painter, ref drawPos, "Draw Calls", s.DrawCalls );
			Row( painter, ref drawPos, "Frame Grabs", s.FrameGrabs );
			Row( painter, ref drawPos, "Instances", s.Instances );
			Row( painter, ref drawPos, "Layers", s.Layers );

			drawPos.y += 6;
			DrawHeader( painter, ref drawPos, "UI Memory" );
			Row( painter, ref drawPos, "GpuBuffers", system?.RootPanels.Sum( root => root.PanelCommandList.FindResource<Painter.Context>()?.Batcher.GpuBufferCount ?? 0 ) ?? 0 );

			pos.y += MathF.Max( 0, drawPos.y - startY );
		}

		static Sandbox.UI.RootPanel.RenderStatistics CollectStatistics( UISystem system, out int roots )
		{
			roots = 0;
			var total = new Sandbox.UI.RootPanel.RenderStatistics();
			if ( system is null )
				return total;

			foreach ( var root in system.RootPanels )
			{
				if ( !root.IsValid )
					continue;

				var s = root.Stats;
				// The overlay may run before or after UI submission. Ignore roots that have stopped rendering.
				if ( s.RenderCount == 0 || s.FrameNumber + 1 < Application.FrameCount )
					continue;

				roots++;
				total = total with
				{
					LayoutTime = total.LayoutTime + s.LayoutTime,
					CommandBuildTime = total.CommandBuildTime + s.CommandBuildTime,
					SubmissionTime = total.SubmissionTime + s.SubmissionTime,
					PanelsRendered = total.PanelsRendered + s.PanelsRendered,
					PanelsCulled = total.PanelsCulled + s.PanelsCulled,
					DrawCalls = total.DrawCalls + s.DrawCalls,
					Instances = total.Instances + s.Instances,
					Layers = total.Layers + s.Layers,
					FrameGrabs = total.FrameGrabs + s.FrameGrabs
				};
			}

			return total;
		}

		static void DrawHeader( Painter painter, ref Vector2 pos, string label )
		{
			var rect = new Rect( pos, new Vector2( 512, 18 ) );
			var scope = new TextRendering.Scope( label, Color.White.WithAlpha( 0.9f ), 11, "Roboto Mono", 700 ) { Outline = _outline };
			DebugOverlay.DrawText( painter, scope, rect, TextFlag.LeftCenter );
			pos.y += 18;
		}

		static void Row( Painter painter, ref Vector2 pos, string label, double value, string detail = null, string format = "N0" )
		{
			var rect = new Rect( pos, new Vector2( 560, 14 ) );
			var scope = new TextRendering.Scope( label, Color.White.WithAlpha( 0.8f ), 11, "Roboto Mono", 600 ) { Outline = _outline };
			DebugOverlay.DrawText( painter, scope, rect with { Width = 120 }, TextFlag.RightCenter );
			scope.TextColor = value > 0 ? Color.White : Color.White.WithAlpha( 0.5f );
			scope.Text = value.ToString( format );
			DebugOverlay.DrawText( painter, scope, rect with { Left = rect.Left + 128, Width = detail is null ? 420 : 90 }, TextFlag.LeftCenter );
			if ( detail is not null )
			{
				scope.TextColor = Color.White.WithAlpha( 0.75f );
				scope.Text = detail;
				DebugOverlay.DrawText( painter, scope, rect with { Left = rect.Left + 228, Width = 320 }, TextFlag.LeftCenter );
			}
			pos.y += rect.Height;
		}
	}
}
