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
			float cpuMs = (float)(PerformanceStats.FrameTime * 1000.0);
			float gpuMs = PerformanceStats.GpuFrametime;
			uint gpuFrameNo = PerformanceStats.GpuFrameNumber;

			_cpuHistory[_histHead] = cpuMs;
			if ( gpuFrameNo != _lastGpuFrameNo ) { _gpuHistory[_histHead] = gpuMs; _lastGpuFrameNo = gpuFrameNo; }
			_histHead = (_histHead + 1) % HistorySize;
			if ( _histCount < HistorySize ) _histCount++;

			CalcStats( _cpuHistory, _histCount, out float cpuAvg, out float cpuRange );
			CalcStats( _gpuHistory, _histCount, out float gpuAvg, out float gpuRange );

			TimingRow( ref pos, "Total Frame", cpuAvg, cpuRange );
			TimingRow( ref pos, "GPU Frame", gpuAvg, gpuRange );
			pos.y += 8;

			var f = FrameStats.Current;

			Row( ref pos, "Objects", f.ObjectsRendered, $"({f.BaseObjectDraws:N0} base, {f.AnimatableObjectDraws:N0} anim) in {f.RenderBatchDraws:N0} batchlists" );
			Row( ref pos, "Triangles", f.TrianglesRendered );
			Row( ref pos, "Draw Calls", f.DrawCalls );
			Row( ref pos, "Material Changes", f.MaterialChanges + f.ShadowMaterialChanges, $"({f.ShadowMaterialChanges:N0} depth-only)" );
			Row( ref pos, "Initial Materials", f.InitialMaterialChanges );
			if ( f.UniqueMaterials > 0 ) Row( ref pos, "Unique Materials", f.UniqueMaterials );
			Row( ref pos, "Display Lists", f.DisplayLists );
			Row( ref pos, "Views", f.SceneViewsRendered );
			Row( ref pos, "Resolves", f.RenderTargetResolves );
			Row( ref pos, "Contexts", f.PrimaryContexts + f.SecondaryContexts, $"({f.PrimaryContexts:N0} primary, {f.SecondaryContexts:N0} secondary)" );
			Row( ref pos, "Pre-Cull", f.ObjectsPreCull, $"({f.ObjectsTested:N0} tested)" );
			Row( ref pos, "Vis Culls", f.ObjectsCulledByVis );
			Row( ref pos, "Screensize Culls", f.ObjectsCulledByScreenSize );
			if ( f.ObjectsFading > 0 ) Row( ref pos, "Objects Fading", f.ObjectsFading );
			Row( ref pos, "Shadowed Lights", f.ShadowedLightsInView );
			Row( ref pos, "Unshadowed Lights", f.UnshadowedLightsInView );
			Row( ref pos, "Shadow Maps", f.ShadowMaps );
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

		static void TimingRow( ref Vector2 pos, string label, float avgMs, float rangeMs )
		{
			int fps = avgMs > 0 ? (int)(1000f / avgMs) : 0;
			var color = avgMs > 33.3f ? new Color( 1f, 0.3f, 0.3f ) : avgMs > 16.67f ? new Color( 1f, 0.6f, 0.2f ) : Color.White;
			var rect = new Rect( pos, new Vector2( 512, 14 ) );
			var scope = new TextRendering.Scope( label, Color.White.WithAlpha( 0.8f ), 11, "Roboto Mono", 600 ) { Outline = _outline };

			Hud.DrawText( scope, rect with { Width = 130 }, TextFlag.RightCenter );
			scope.TextColor = color; scope.Text = $"{avgMs:F3}ms";
			Hud.DrawText( scope, rect with { Left = rect.Left + 138, Width = 80 }, TextFlag.LeftCenter );
			scope.TextColor = Color.White.WithAlpha( 0.55f ); scope.Text = $"+/- {rangeMs:F3}ms";
			Hud.DrawText( scope, rect with { Left = rect.Left + 218, Width = 117 }, TextFlag.LeftCenter );
			scope.TextColor = Color.White.WithAlpha( 0.8f ); scope.Text = $"{fps} fps";
			Hud.DrawText( scope, rect with { Left = rect.Left + 335 }, TextFlag.LeftCenter );

			pos.y += rect.Height;
		}

		static void Row( ref Vector2 pos, string label, double value, string detail = null )
		{
			var rect = new Rect( pos, new Vector2( 512, 14 ) );
			var scope = new TextRendering.Scope( label, Color.White.WithAlpha( 0.8f ), 11, "Roboto Mono", 600 ) { Outline = _outline };
			Hud.DrawText( scope, rect with { Width = 130 }, TextFlag.RightCenter );
			scope.TextColor = value > 0 ? Color.White : Color.White.WithAlpha( 0.5f );
			scope.Text = value.ToString( "N0" );
			Hud.DrawText( scope, rect with { Left = rect.Left + 138, Width = detail is null ? 374 : 80 }, TextFlag.LeftCenter );
			if ( detail is not null )
			{
				scope.TextColor = Color.White.WithAlpha( 0.5f );
				scope.Text = detail;
				Hud.DrawText( scope, rect with { Left = rect.Left + 225 }, TextFlag.LeftCenter );
			}
			pos.y += rect.Height;
		}
	}
}
