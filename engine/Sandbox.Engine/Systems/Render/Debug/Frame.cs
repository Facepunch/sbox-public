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

		// I pulled these out of my ass based on vibes
		private const double DrawCallsGreen = 1500;
		private const double DrawCallsYellow = 3000;
		private const double DrawCallsOrange = 5000;
		// Material changes per draw call (1.0 = rebind every draw; lower is better)
		private const double MatChangePerDrawGreen = 0.3;
		private const double MatChangePerDrawYellow = 0.5;
		// Triangles per draw (higher = better batching) — NVIDIA min-efficient batch size
		private const double TrisPerDrawGreen = 4000;
		private const double TrisPerDrawYellow = 1000;
		// Aggregation rate % (higher = better)
		private const double AggRateGreen = 50;
		private const double AggRateNeutral = 20;
		// Batch density (objects per batchlist; higher = better)
		private const double BatchDensityGreen = 4;
		private const double BatchDensityNeutral = 2;
		// Cull efficiency % (higher = better)
		private const double CullEffGreen = 70;
		private const double CullEffNeutral = 40;
		// Texture pool % of dynamically-tuned streamer limit (lower = better; near 100% = streamer near its budget)
		private const double TexPoolPctGreen = 70;
		private const double TexPoolPctYellow = 85;
		private const double TexPoolPctOrange = 95;
		// Streaming pressure (sustained pending requests; 0 = idle)
		private const int StreamingNeutralMax = 4;
		private const int StreamingWarnMax = 32;

		// Palette
		private static readonly Color ColGood = new( 0.45f, 0.95f, 0.55f );
		private static readonly Color ColNeutral = Color.White;
		private static readonly Color ColWarn = new( 1.0f, 0.78f, 0.30f );
		private static readonly Color ColBad = new( 1.0f, 0.45f, 0.30f );
		private static readonly Color ColCrit = new( 1.0f, 0.30f, 0.30f );
		private static readonly Color ColDim = Color.White.WithAlpha( 0.75f );

		// Composition-bar colours
		private static readonly Color BarBase = new( 0.40f, 0.70f, 1.00f );
		private static readonly Color BarAnim = new( 0.95f, 0.65f, 0.30f );
		private static readonly Color BarAgg = new( 0.55f, 0.95f, 0.55f );
		private static readonly Color BarMatColor = new( 0.55f, 0.85f, 1.00f );
		private static readonly Color BarMatDepth = new( 0.70f, 0.70f, 0.80f );
		private static readonly Color BarMatDepthAT = new( 1.00f, 0.78f, 0.30f );

		internal static void Draw( ref Vector2 pos )
		{
			Draw( ref pos, 3 );
		}

		internal static void Draw( ref Vector2 pos, int verbosity )
		{
			if ( verbosity <= 0 ) return;
			verbosity = Math.Clamp( verbosity, 1, 3 );

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

			DrawSectionHeader( ref drawPos, "Geometry" );
			drawPos.y += 4;

			double totalObjPrim = f.BaseObjectDraws + f.AnimatableObjectDraws + f.AggregateObjectDraws;
			Row( ref drawPos, "Objects", f.ObjectsRendered, $"{f.BaseObjectDraws:N0} base, {f.AnimatableObjectDraws:N0} anim, {f.AggregateObjectDraws:N0} agg" );

			double trisPerDraw = SafeRatio( f.TrianglesRendered, f.DrawCalls );
			Row( ref drawPos, "Draw Calls", f.DrawCalls, $"{trisPerDraw:N0} tris/draw", valueColor: ColourForDrawCalls( f.DrawCalls ) );
			Row( ref drawPos, "Triangles", f.TrianglesRendered, valueColor: ColourForTrisPerDraw( trisPerDraw ) );
			if ( f.AggregateObjectDrawCalls > 0 )
				Row( ref drawPos, "Aggregate Draws", f.AggregateObjectDrawCalls, $"{SafeRatio( f.AggregateObjectDraws, f.AggregateObjectDrawCalls ):N1} frags/draw, {f.AggregateObjectsFullyCulled:N0} fully culled" );
			if ( f.ObjectsFading > 0 ) Row( ref drawPos, "Objects Fading", f.ObjectsFading );
			Row( ref drawPos, "Display Lists", f.DisplayLists );
			Row( ref drawPos, "Views", f.SceneViewsRendered );
			Row( ref drawPos, "Resolves", f.RenderTargetResolves );

			if ( verbosity >= 2 )
			{
				drawPos.y += 8;
				DrawSectionHeader( ref drawPos, "Batching" );
				drawPos.y += 4;

				double aggRate = totalObjPrim > 0 ? (f.AggregateObjectDraws / totalObjPrim) * 100.0 : 0;
				double batchDensity = SafeRatio( f.ObjectsRendered, f.RenderBatchDraws );
				double totalMatChanges2 = f.MaterialChanges + f.ShadowMaterialChanges;
				double matPerDraw = SafeRatio( totalMatChanges2, f.DrawCalls );
				double totalMatSets = f.FullMaterialSets + f.SimilarMaterialSets;
				double matReuse = totalMatSets > 0 ? (f.SimilarMaterialSets / totalMatSets) * 100.0 : 0;

				Row( ref drawPos, "Aggregation rate", aggRate, $"{f.AggregateObjectDraws:N0} agg / {totalObjPrim:N0} total prims", valueText: $"{aggRate:N1}%", valueColor: ColourForAggRate( aggRate ) );
				Row( ref drawPos, "Batch density", batchDensity, $"{f.ObjectsRendered:N0} objs / {f.RenderBatchDraws:N0} batchlists", valueText: $"{batchDensity:N1}", valueColor: ColourForBatchDensity( batchDensity ) );
				Row( ref drawPos, "Mat changes/draw", matPerDraw, $"{totalMatChanges2:N0} changes / {f.DrawCalls:N0} draws", valueText: $"{matPerDraw:N2}", valueColor: ColourForMatPerDraw( matPerDraw ) );
				if ( totalMatSets > 0 )
					Row( ref drawPos, "Material reuse", matReuse, $"{f.SimilarMaterialSets:N0} similar / {totalMatSets:N0} sets", valueText: $"{matReuse:N1}%" );
				if ( f.UnbatchableMaterialDraws > 0 )
					Row( ref drawPos, "Unbatchable Mats", f.UnbatchableMaterialDraws, valueColor: ColCrit );
				if ( f.UniqueMaterials > 0 ) Row( ref drawPos, "Unique Materials", f.UniqueMaterials );

				drawPos.y += 8;
				DrawSectionHeader( ref drawPos, "Culling" );
				drawPos.y += 4;

				double totalCulled = f.ObjectsCulledByVis + f.ObjectsCulledByScreenSize + f.ObjectsCulledByFade;
				double cullEff = SafePercent( totalCulled, f.ObjectsTested );
				double visPct = SafePercent( f.ObjectsCulledByVis, f.ObjectsTested );
				double sizePct = SafePercent( f.ObjectsCulledByScreenSize, f.ObjectsTested );
				double fadePct = SafePercent( f.ObjectsCulledByFade, f.ObjectsTested );
				Row( ref drawPos, "Cull efficiency", cullEff, $"{f.ObjectsPreCull:N0} pre-cull, {f.ObjectsTested:N0} tested  ·  vis {visPct:N1}% / size {sizePct:N1}% / fade {fadePct:N1}%", valueText: $"{cullEff:N1}%", valueColor: ColourForCullEff( cullEff ) );

				drawPos.y += 8;
				DrawSectionHeader( ref drawPos, "Material Changes" );
				drawPos.y += 4;

				double totalMatChanges = f.MaterialChanges + f.ShadowMaterialChanges;
				Row( ref drawPos, "Material Changes", totalMatChanges, $"{f.ShadowMaterialChanges:N0} depth-only, {f.ShadowMaterialChangesAlphaTested:N0} depth-AT" );
				Row( ref drawPos, "Initial Materials", f.InitialMaterialChanges, $"{f.InitialShadowMaterialChanges:N0} depth-only, {f.CopyMaterialChanges:N0} copy" );
				if ( f.MaterialComputes > 0 || f.FullMaterialSets > 0 || f.SimilarMaterialSets > 0 || f.TextureOnlyMaterialSets > 0 )
					Row( ref drawPos, "Material Sets", f.FullMaterialSets, $"{f.MaterialComputes:N0} computes, {f.SimilarMaterialSets:N0} similar, {f.TextureOnlyMaterialSets:N0} tex-only" );
				if ( f.VfxEvals > 0 || f.VfxRuleChecks > 0 )
					Row( ref drawPos, "Vfx Evals", f.VfxEvals, $"{f.VfxRuleChecks:N0} rule checks, {f.ConstantBufferUpdates:N0} cb updates ({f.ConstantBufferBytes / 1024.0:N1} KB)" );
				Row( ref drawPos, "Contexts", f.PrimaryContexts + f.SecondaryContexts, $"{f.PrimaryContexts:N0} primary, {f.SecondaryContexts:N0} secondary" );
			}

			drawPos.y += 8;
			DrawSectionHeader( ref drawPos, "Lights" );
			drawPos.y += 4;
			Row( ref drawPos, "Shadowed Lights", f.ShadowedLightsInView );
			Row( ref drawPos, "Unshadowed Lights", f.UnshadowedLightsInView );
			Row( ref drawPos, "Shadow Maps", f.ShadowMaps, $"{SafeRatio( f.ShadowMaps, f.ShadowedLightsInView ):N1} maps per shadowed light" );

			drawPos.y += 8;
			DrawSectionHeader( ref drawPos, "Memory" );
			drawPos.y += 4;

			if ( f.TexturePoolLimitBytes > 0 )
			{
				double usedMB = f.TexturePoolUsedBytes / (1024.0 * 1024.0);
				double limitMB = f.TexturePoolLimitBytes / (1024.0 * 1024.0);
				double pinnedMB = f.TexturePoolNonEvictableBytes / (1024.0 * 1024.0);
				double pctUsed = limitMB > 0 ? (usedMB / limitMB) * 100.0 : 0;
				Row( ref drawPos, "Texture Pool", pctUsed,
					$"{usedMB:N0} / {limitMB:N0} MB · {pinnedMB:N0} MB pinned",
					valueText: $"{pctUsed:N1}%",
					valueColor: ColourForGpuMemPct( pctUsed ) );
			}
			Row( ref drawPos, "Streaming Reqs", f.PendingStreamingRequests,
				detail: f.PendingStreamingRequests > 0 ? "loading…" : null,
				valueColor: ColourForStreamingPressure( f.PendingStreamingRequests ) );

			if ( verbosity >= 3 && !string.IsNullOrEmpty( f.GpuStatsSummary ) )
			{
				drawPos.y += 8;
				DrawSectionHeader( ref drawPos, "GPU Resources" );
				drawPos.y += 4;
				DrawMultilineBlock( ref drawPos, f.GpuStatsSummary );
			}

			pos.y += MathF.Max( 0, drawPos.y - startY );
		}

		static void DrawMultilineBlock( ref Vector2 pos, string block )
		{
			var lines = block.Split( '\n' );
			foreach ( var raw in lines )
			{
				var line = raw.TrimEnd( '\r' );
				if ( line.Length == 0 ) { pos.y += 4; continue; }
				var rect = new Rect( pos.x + 128, pos.y, 420, 13 );
				var scope = new TextRendering.Scope( line, ColDim, 10, "Roboto Mono", 500 ) { Outline = _outline };
				Hud.DrawText( scope, rect, TextFlag.LeftCenter );
				pos.y += 13;
			}
		}

		static Color ColourForDrawCalls( double dc )
		{
			if ( dc <= 0 ) return ColDim;
			if ( dc < DrawCallsGreen ) return ColGood;
			if ( dc < DrawCallsYellow ) return ColNeutral;
			if ( dc < DrawCallsOrange ) return ColWarn;
			return ColBad;
		}

		static Color ColourForMatPerDraw( double r )
		{
			if ( r <= 0 ) return ColDim;
			if ( r < MatChangePerDrawGreen ) return ColGood;
			if ( r < MatChangePerDrawYellow ) return ColNeutral;
			return ColBad;
		}

		static Color ColourForTrisPerDraw( double r )
		{
			if ( r <= 0 ) return ColDim;
			if ( r >= TrisPerDrawGreen ) return ColGood;
			if ( r >= TrisPerDrawYellow ) return ColNeutral;
			return ColWarn;
		}

		static Color ColourForAggRate( double r )
		{
			if ( r <= 0 ) return ColDim;
			if ( r >= AggRateGreen ) return ColGood;
			if ( r >= AggRateNeutral ) return ColNeutral;
			return ColWarn;
		}

		static Color ColourForBatchDensity( double d )
		{
			if ( d <= 0 ) return ColDim;
			if ( d >= BatchDensityGreen ) return ColGood;
			if ( d >= BatchDensityNeutral ) return ColNeutral;
			return ColWarn;
		}

		static Color ColourForCullEff( double pct )
		{
			if ( pct <= 0 ) return ColDim;
			if ( pct >= CullEffGreen ) return ColGood;
			if ( pct >= CullEffNeutral ) return ColNeutral;
			return ColWarn;
		}

		static Color ColourForGpuMemPct( double pct )
		{
			if ( pct <= 0 ) return ColDim;
			if ( pct < TexPoolPctGreen ) return ColGood;
			if ( pct < TexPoolPctYellow ) return ColNeutral;
			if ( pct < TexPoolPctOrange ) return ColWarn;
			return ColCrit;
		}

		static Color ColourForStreamingPressure( int pending )
		{
			if ( pending <= 0 ) return ColDim;
			if ( pending <= StreamingNeutralMax ) return ColNeutral;
			if ( pending <= StreamingWarnMax ) return ColWarn;
			return ColBad;
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
			var color = lastMs > 33.3f ? ColCrit : lastMs > 16.67f ? ColWarn : ColNeutral;
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
			var scope = new TextRendering.Scope( label, new Color( 0.65f, 0.85f, 1f, 0.95f ), 12, "Roboto Mono", 700 ) { Outline = _outline };
			Hud.DrawText( scope, rect, TextFlag.LeftCenter );
			// Underline accent
			Hud.DrawRect( new Rect( rect.Left, rect.Top + rect.Height - 1, 540, 1 ), new Color( 0.65f, 0.85f, 1f, 0.25f ) );
			pos.y += HeaderHeight;
		}

		static void Row( ref Vector2 pos, string label, double value, string detail = null, string valueText = null, Color? valueColor = null )
		{
			var rect = new Rect( pos, new Vector2( 560, 14 ) );
			var scope = new TextRendering.Scope( label, Color.White.WithAlpha( 0.8f ), 11, "Roboto Mono", 600 ) { Outline = _outline };
			Hud.DrawText( scope, rect with { Width = 120 }, TextFlag.RightCenter );
			scope.TextColor = valueColor ?? (value > 0 ? ColNeutral : ColDim);
			scope.Text = valueText ?? value.ToString( "N0" );
			Hud.DrawText( scope, rect with { Left = rect.Left + 128, Width = detail is null ? 420 : 90 }, TextFlag.LeftCenter );
			if ( detail is not null )
			{
				scope.TextColor = ColDim;
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

		private const float HeaderHeight = 20f;
	}
}
