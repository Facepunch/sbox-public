using Sandbox.Diagnostics;

namespace Sandbox;

internal static partial class DebugOverlay
{
	public partial class Frame
	{
		private const int HistorySize = 30;
		private static readonly float[] _cpuHistory = new float[HistorySize];
		private static readonly float[] _gpuHistory = new float[HistorySize];
		private static int _histHead;
		private static int _histCount;
		private static uint _lastGpuFrameNo;
		private static readonly TextRendering.Outline _outline = new() { Color = Color.Black, Size = 2, Enabled = true };

		internal static void Draw( ref Vector2 pos )
		{
			var drawPos = new Vector2( pos.x + 24, pos.y );
			var startY = drawPos.y;

			float cpuMs = (float)(PerformanceStats.FrameTime * 1000.0);
			float gpuMs = PerformanceStats.GpuFrametime;
			uint gpuFrameNo = PerformanceStats.GpuFrameNumber;

			_cpuHistory[_histHead] = cpuMs;
			if ( gpuFrameNo != _lastGpuFrameNo ) { _gpuHistory[_histHead] = gpuMs; _lastGpuFrameNo = gpuFrameNo; }
			_histHead = (_histHead + 1) % HistorySize;
			if ( _histCount < HistorySize ) _histCount++;

			CalcStats( _cpuHistory, _histCount, out float cpuAvg, out float cpuRange );
			CalcStats( _gpuHistory, _histCount, out float gpuAvg, out float gpuRange );

			DrawSectionHeader( ref drawPos, "Frame Timing" );
			TimingRow( ref drawPos, "CPU Frame", cpuAvg, cpuRange, cpuMs );
			TimingRow( ref drawPos, "GPU Frame", gpuAvg, gpuRange, gpuMs );
			drawPos.y += 6;

			var f = FrameStats.Current;

			DrawSectionHeader( ref drawPos, "Render Stats" );
			drawPos.y += 12;
			Row( ref drawPos, "Objects", f.ObjectsRendered, $"({f.BaseObjectDraws:N0} base, {f.AnimatableObjectDraws:N0} anim) in {f.RenderBatchDraws:N0} batchlists" );
			if ( f.ObjectsFading > 0 ) Row( ref drawPos, "Objects Fading", f.ObjectsFading );
			Row( ref drawPos, "Triangles", f.TrianglesRendered );
			Row( ref drawPos, "Draw Calls", f.DrawCalls, $"{SafeRatio( f.TrianglesRendered, f.DrawCalls ):N0} tris/draw" );
			Row( ref drawPos, "Display Lists", f.DisplayLists );
			Row( ref drawPos, "Views", f.SceneViewsRendered );
			Row( ref drawPos, "Resolves", f.RenderTargetResolves );

			drawPos.y += 4;
			drawPos.y += 12;
			var totalCulled = f.ObjectsCulledByVis + f.ObjectsCulledByScreenSize + f.ObjectsCulledByFade;
			Row( ref drawPos, "Pre-Cull", f.ObjectsPreCull, $"{f.ObjectsTested:N0} tested" );
			Row( ref drawPos, "Total Culled", totalCulled, $"{SafePercent( totalCulled, f.ObjectsTested ):N1}% of tested" );
			Row( ref drawPos, "Vis Culls", f.ObjectsCulledByVis, $"{SafePercent( f.ObjectsCulledByVis, f.ObjectsTested ):N1}%" );
			Row( ref drawPos, "Size Culls", f.ObjectsCulledByScreenSize, $"{SafePercent( f.ObjectsCulledByScreenSize, f.ObjectsTested ):N1}%" );
			Row( ref drawPos, "Fade Culls", f.ObjectsCulledByFade, $"{SafePercent( f.ObjectsCulledByFade, f.ObjectsTested ):N1}%" );

			drawPos.y += 4;
			drawPos.y += 12;
			var totalMaterialChanges = f.MaterialChanges + f.ShadowMaterialChanges;
			Row( ref drawPos, "Material Changes", totalMaterialChanges, $"{SafePercent( totalMaterialChanges, f.DrawCalls ):N1}% vs draws" );
			Row( ref drawPos, "Initial Materials", f.InitialMaterialChanges );
			if ( f.UniqueMaterials > 0 ) Row( ref drawPos, "Unique Materials", f.UniqueMaterials );
			Row( ref drawPos, "Contexts", f.PrimaryContexts + f.SecondaryContexts, $"({f.PrimaryContexts:N0} primary, {f.SecondaryContexts:N0} secondary)" );

			drawPos.y += 4;
			drawPos.y += 12;
			Row( ref drawPos, "Shadowed Lights", f.ShadowedLightsInView );
			Row( ref drawPos, "Unshadowed Lights", f.UnshadowedLightsInView );
			Row( ref drawPos, "Shadow Maps", f.ShadowMaps, $"{SafeRatio( f.ShadowMaps, f.ShadowedLightsInView ):N1} maps per shadowed light" );

			pos.y += MathF.Max( 0, drawPos.y - startY );
		}

		static void CalcStats( float[] h, int count, out float avg, out float range )
		{
			if ( count == 0 ) { avg = 0; range = 0; return; }
			float sum = 0;
			for ( int i = 0; i < count; i++ ) sum += h[i];
			avg = sum / count;
			float dev = 0;
			for ( int i = 0; i < count; i++ ) dev = MathF.Max( dev, MathF.Abs( h[i] - avg ) );
			range = dev;
		}

		static void TimingRow( ref Vector2 pos, string label, float avgMs, float rangeMs, float lastMs )
		{
			int fps = avgMs > 0 ? (int)(1000f / avgMs) : 0;
			var color = lastMs > 33.3f ? new Color( 1f, 0.3f, 0.3f ) : lastMs > 16.67f ? new Color( 1f, 0.6f, 0.2f ) : Color.White;
			var rect = new Rect( pos, new Vector2( 560, 14 ) );
			var scope = new TextRendering.Scope( label, Color.White.WithAlpha( 0.8f ), 11, "Roboto Mono", 600 ) { Outline = _outline };

			Hud.DrawText( scope, rect with { Width = 120 }, TextFlag.RightCenter );
			scope.TextColor = color; scope.Text = $"last {lastMs:F2}ms";
			Hud.DrawText( scope, rect with { Left = rect.Left + 128, Width = 88 }, TextFlag.LeftCenter );
			scope.TextColor = Color.White.WithAlpha( 0.78f ); scope.Text = $"avg {avgMs:F2}ms";
			Hud.DrawText( scope, rect with { Left = rect.Left + 224, Width = 92 }, TextFlag.LeftCenter );
			scope.TextColor = Color.White.WithAlpha( 0.78f ); scope.Text = $"jit {rangeMs:F2}ms";
			Hud.DrawText( scope, rect with { Left = rect.Left + 326, Width = 96 }, TextFlag.LeftCenter );
			scope.TextColor = Color.White.WithAlpha( 0.8f ); scope.Text = $"{fps} fps";
			Hud.DrawText( scope, rect with { Left = rect.Left + 450, Width = 78 }, TextFlag.LeftCenter );

			pos.y += rect.Height;
		}

		static void DrawSectionHeader( ref Vector2 pos, string label )
		{
			var rect = new Rect( pos, new Vector2( 512, HeaderHeight - 2 ) );
			var scope = new TextRendering.Scope( label, Color.White.WithAlpha( 0.9f ), 11, "Roboto Mono", 700 ) { Outline = _outline };
			Hud.DrawText( scope, rect, TextFlag.LeftCenter );
			pos.y += HeaderHeight;
		}

		static void Row( ref Vector2 pos, string label, double value, string detail = null )
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

		static double SafeRatio( double numerator, double denominator )
		{
			if ( denominator <= 0 ) return 0;
			return numerator / denominator;
		}

		static double SafePercent( double numerator, double denominator )
		{
			if ( denominator <= 0 ) return 0;
			return (numerator / denominator) * 100.0;
		}

		private const float HeaderHeight = 18f;
	}
}
