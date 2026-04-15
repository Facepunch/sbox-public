namespace Sandbox;

internal static partial class DebugOverlay
{
	[ConVar( "overlay_ui", Help = "Draws an overlay showing UI batching stats" )]
	internal static int overlay_ui { get; set; } = 0;

	public partial class UI
	{
		static readonly TextRendering.Outline _outline = new() { Color = Color.Black, Size = 2, Enabled = true };

		internal static void Draw( ref Vector2 pos )
		{
			var s = Sandbox.UI.PanelRenderer.Stats;
			var drawPos = new Vector2( pos.x + 24, pos.y );
			var startY = drawPos.y;

			DrawHeader( ref drawPos, "UI Batching" );
			Row( ref drawPos, "Panels", s.Panels, $"({s.BatchedPanels} batched, {s.InlinePanels} inline, {s.LayerPanels} layer)" );
			Row( ref drawPos, "Draw Calls", s.DrawCalls );
			Row( ref drawPos, "Flushes", s.FlushCount, s.FlushCount > 0 ? $"avg {s.InstanceCount / s.FlushCount} instances" : null );
			Row( ref drawPos, "Instances", s.InstanceCount );
			Row( ref drawPos, "Scissors", s.ScissorCount );

			drawPos.y += 6;
			DrawHeader( ref drawPos, "UI Memory" );
			Row( ref drawPos, "RenderLayers", Sandbox.UI.RenderLayer.ActiveCount, $"({Sandbox.UI.RenderLayer.PoolCount} pooled)" );
			Row( ref drawPos, "GpuBuffers", s.GpuBufferCount );

			pos.y += MathF.Max( 0, drawPos.y - startY );
		}

		static void DrawHeader( ref Vector2 pos, string label )
		{
			var rect = new Rect( pos, new Vector2( 512, 18 ) );
			var scope = new TextRendering.Scope( label, Color.White.WithAlpha( 0.9f ), 11, "Roboto Mono", 700 ) { Outline = _outline };
			Hud.DrawText( scope, rect, TextFlag.LeftCenter );
			pos.y += 18;
		}

		static void Row( ref Vector2 pos, string label, int value, string detail = null )
		{
			var rect = new Rect( pos, new Vector2( 560, 14 ) );
			var scope = new TextRendering.Scope( label, Color.White.WithAlpha( 0.8f ), 11, "Roboto Mono", 600 ) { Outline = _outline };
			Hud.DrawText( scope, rect with { Width = 120 }, TextFlag.RightCenter );
			scope.TextColor = value > 0 ? Color.White : Color.White.WithAlpha( 0.5f );
			scope.Text = value.ToString( "N0" );
			Hud.DrawText( scope, rect with { Left = rect.Left + 128, Width = detail is null ? 420 : 90 }, TextFlag.LeftCenter );
			if ( detail is not null )
			{
				scope.TextColor = Color.White.WithAlpha( 0.75f );
				scope.Text = detail;
				Hud.DrawText( scope, rect with { Left = rect.Left + 228, Width = 320 }, TextFlag.LeftCenter );
			}
			pos.y += rect.Height;
		}
	}
}
