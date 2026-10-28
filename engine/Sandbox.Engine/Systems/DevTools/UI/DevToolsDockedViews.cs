using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// The frame drawn against its budget: a ruler from zero past the target, with the frame, the main thread's
/// categories and the GPU's passes laid along it as segments, biggest first. How full the frame is, and with what, in
/// one look. Past the budget is shaded. With a frame picked, the frame and CPU rows are that frame's.
/// </summary>
internal sealed class DevToolsBudget : Panel
{
	readonly record struct Hit( Rect Rect, string Name, float Ms, string Tab );

	readonly List<Hit> _hits = new();
	float _scaleMs = 20f;

	const float LabelW = 56, RulerH = 22, FrameRowH = 24, CostRowH = 34, LegendH = 16;

	/// <summary>
	/// Each row's total at its end. Off where the totals are already on screen, in bigger type.
	/// </summary>
	public bool ShowTotals { get; set; } = true;

	float Right => ShowTotals ? 64 : 14;

	readonly List<(Color Color, string Name, float Ms)> _legend = new();

	public DevToolsBudget()
	{
		AddClass( "budget" );
	}

	protected override void OnClick( MousePanelEvent e )
	{
		var m = e.LocalPosition / ScaleToScreen;
		foreach ( var hit in _hits )
		{
			if ( !hit.Rect.IsInside( m ) || hit.Tab is null ) continue;
			DevTools.ActiveTab = hit.Tab;
			break;
		}
	}

	public override void OnDraw( Painter painter )
	{
		var s = DevTools.Current;
		var frames = DevTools.ShownFrames;
		var scale = ScaleToScreen;
		var w = Box.Rect.Width / scale;
		var h = Box.Rect.Height / scale;
		if ( w < 200 || h < 60 || s.Frames == 0 ) return;

		using var state = painter.Scope();
		painter.Scale( scale );
		painter.Stroke = Stroke.None;
		_hits.Clear();

		var sel = DevTools.SelectedAge;
		var picked = sel >= 0;
		var target = s.TargetMs > 0 ? s.TargetMs : 16.67f;

		var frameMs = picked ? frames.FrameMs( sel ) : s.FrameAvg;
		var busyMs = picked ? frames.BusyMs( sel ) : s.CpuWorkAvg;
		var selfMs = picked ? frames.SelfMs( sel ) : s.SelfAvg;
		var waitMs = picked ? frames.IdleMs( sel ) : s.CapWaitAvg;
		var gpuMs = MathF.Max( picked ? frames.GpuMs( sel ) : s.GpuAvg, DevTools.ShownGpuTimeline.ElapsedMs );

		// Room for the budget and a little past it, or the p99 frame - but a long hitch is clipped rather than allowed to
		// shrink the budget to a sliver
		var wanted = MathF.Max( target * 1.15f, MathF.Max( picked ? frameMs : s.FrameP99, gpuMs ) * 1.05f );
		wanted = MathF.Min( wanted, target * 2.5f );

		// Held still until what it shows outgrows it or falls well short, then snapped: easing it every frame kept every
		// segment and tick on the move
		if ( wanted > _scaleMs || wanted < _scaleMs * 0.7f ) _scaleMs = wanted;

		var plotX = LabelW;
		var plotW = w - LabelW - Right;
		var px = plotW / _scaleMs;
		float X( float ms ) => plotX + MathF.Min( ms, _scaleMs ) * px;

		var rowsTop = RulerH + 6;
		var frameY = rowsTop;
		var cpuY = frameY + FrameRowH + 14;
		var gpuY = cpuY + CostRowH + LegendH + 6;
		var bottom = gpuY + CostRowH;

		// Over budget
		var tx = X( target );
		painter.Fill = DevToolsStyles.Bad.WithAlpha( 0.07f );
		painter.Rect( new Rect( tx, RulerH, plotX + plotW - tx, bottom - RulerH + LegendH ) );

		// Ruler: a tick every step, a label where it fits
		ReadOnlySpan<float> steps = [0.25f, 0.5f, 1, 2, 5, 10, 20, 50, 100];
		var step = steps[^1];
		foreach ( var st in steps ) { if ( st * px >= 44 ) { step = st; break; } }
		for ( float t = 0; t <= _scaleMs + 0.001f; t += step )
		{
			var x = X( t );
			painter.Fill = DevToolsStyles.Line;
			painter.Rect( new Rect( x, RulerH - 5, 1, 5 ) );
			painter.Fill = Color.White.WithAlpha( 0.035f );
			painter.Rect( new Rect( x, RulerH, 1, bottom - RulerH ) );
			if ( MathF.Abs( x - tx ) < 52 ) continue;
			Text( painter, t == 0 ? "0" : $"{t:0.##}", new Rect( x - 30, 2, 60, RulerH - 8 ), TextFlag.Center, DevToolsStyles.Faint, 10 );
		}

		painter.Fill = DevToolsStyles.Line;
		painter.Rect( new Rect( plotX, RulerH - 1, plotW, 1 ) );

		// Frame row: the engine's work, the dev tools, then the wait
		Label( painter, "Frame", frameY, FrameRowH );
		var fx = Segment( painter, plotX, frameY, FrameRowH, busyMs, DevToolsStyles.Cpu, "cpu work", px, "cpu" );
		if ( selfMs > 0.05f ) fx = Segment( painter, fx, frameY, FrameRowH, selfMs, DevToolsStyles.SelfCost, "dev tools", px, null );
		if ( waitMs > 0.05f ) fx = Segment( painter, fx, frameY, FrameRowH, waitMs, DevToolsStyles.CapWait, "cap wait", px, "cpu", DevToolsStyles.Dim );
		Total( painter, frameMs, frameY, FrameRowH, plotX + plotW, frameMs > s.HitchMs ? DevToolsStyles.Bad : frameMs > target * 1.05f ? DevToolsStyles.Warning : DevToolsStyles.Text );

		if ( !picked )
		{
			Marker( painter, s.FrameP99, "p99", frameY, FrameRowH, DevToolsStyles.Warning, X );
			if ( s.FrameMax > s.FrameP99 * 1.05f ) Marker( painter, s.FrameMax, "max", frameY, FrameRowH, DevToolsStyles.Bad, X );
		}

		// CPU row: the categories, biggest first, then whatever the categories don't cover
		Label( painter, "CPU", cpuY, CostRowH );
		Span<int> order = stackalloc int[DevTools.Categories.Length];
		for ( int c = 0; c < order.Length; c++ ) order[c] = c;
		float CategoryMs( int c ) => picked ? frames.CategoryMs( sel, c ) : s.CategoryTime[c].Avg;
		order.Sort( ( a, b ) => CategoryMs( b ).CompareTo( CategoryMs( a ) ) );

		var cx = plotX;
		float sum = 0;
		_legend.Clear();
		foreach ( var c in order )
		{
			var ms = CategoryMs( c );
			if ( ms < 0.01f ) continue;
			var timing = DevTools.Categories[c];
			cx = Segment( painter, cx, cpuY, CostRowH, ms, DevToolsStyles.Category( timing ), DevToolsStyles.CategoryName( timing ), px, "cpu" );
			_legend.Add( (DevToolsStyles.Category( timing ), DevToolsStyles.CategoryName( timing ), ms) );
			sum += ms;
		}

		if ( busyMs > sum + 0.05f ) cx = Segment( painter, cx, cpuY, CostRowH, busyMs - sum, DevToolsStyles.Raised, "other", px, "cpu", DevToolsStyles.Dim );
		Total( painter, busyMs, cpuY, CostRowH, plotX + plotW, DevToolsStyles.Text );
		Legend( painter, plotX, cpuY + CostRowH + 2, plotW );

		// GPU row: the passes where they ran, from the frame's first submit, so the gaps are the GPU waiting. Without a
		// timeline yet, the leaf passes end to end.
		Label( painter, "GPU", gpuY, CostRowH );
		_legend.Clear();
		var timeline = DevTools.ShownGpuTimeline;
		if ( timeline.Spans.Count > 0 )
		{
			DrawGpuTimeline( painter, timeline, gpuY, X );
		}
		else
		{
			var gx = plotX;
			float gpuSum = 0;
			foreach ( var p in s.TopGpu )
			{
				if ( p.AvgMs < 0.02f ) continue;
				gx = Segment( painter, gx, gpuY, CostRowH, p.AvgMs, p.Color, p.Name, px, "rendering" );
				_legend.Add( (p.Color, p.Name, p.AvgMs) );
				gpuSum += p.AvgMs;
			}

			if ( s.GpuAvg > gpuSum + 0.05f ) gx = Segment( painter, gx, gpuY, CostRowH, s.GpuAvg - gpuSum, DevToolsStyles.Raised, "other", px, "rendering", DevToolsStyles.Dim );
		}

		Total( painter, s.GpuAvg, gpuY, CostRowH, plotX + plotW, DevToolsStyles.Gpu );
		Legend( painter, plotX, gpuY + CostRowH + 2, plotW );

		// The budget, over everything
		painter.Fill = DevToolsStyles.Text;
		for ( float y = RulerH; y < bottom + LegendH; y += 6 )
		{
			// Not through the legends, so they stay readable
			if ( y >= cpuY + CostRowH && y < cpuY + CostRowH + LegendH || y >= gpuY + CostRowH ) continue;
			painter.Rect( new Rect( tx - 0.5f, y, 2, 3 ) );
		}
		Text( painter, $"budget {target:0.0} ms", new Rect( tx - 60, 2, 120, RulerH - 8 ), TextFlag.Center, DevToolsStyles.Bright, 10 );

		// What's under the cursor
		if ( HasHovered )
		{
			var m = MousePosition / scale;
			foreach ( var hit in _hits )
			{
				if ( !hit.Rect.IsInside( m ) ) continue;
				painter.Stroke = Stroke.Solid( Color.White, 1 );
				painter.Fill = Color.Transparent;
				painter.Rect( hit.Rect );
				painter.Stroke = Stroke.None;

				var box = new Rect( MathF.Min( m.x + 12, w - 170 ), bottom - 34, 160, 34 );
				painter.Fill = Color.Parse( "#1C1C1C" ).Value;
				painter.Stroke = Stroke.Solid( DevToolsStyles.Line, 1 );
				painter.Rect( box );
				painter.Stroke = Stroke.None;
				Text( painter, hit.Name, new Rect( box.Left + 6, box.Top + 2, box.Width - 12, 15 ), TextFlag.LeftCenter, DevToolsStyles.Text, 11, "Inter" );
				Text( painter, $"{hit.Ms:0.00} ms, {hit.Ms / target * 100:0}% of budget", new Rect( box.Left + 6, box.Top + 17, box.Width - 12, 15 ), TextFlag.LeftCenter, DevToolsStyles.Dim, 10 );
				break;
			}
		}
	}

	/// <summary>
	/// A segment <paramref name="ms"/> long from <paramref name="x"/>, named inside when it fits. Returns where it ends.
	/// </summary>
	float Segment( Painter painter, float x, float y, float h, float ms, Color color, string name, float px, string tab, Color? textColor = null )
	{
		var end = MathF.Min( x + ms * px, LabelW + (Box.Rect.Width / ScaleToScreen - LabelW - Right) );
		var width = end - x;
		if ( width <= 0 ) return x;

		var rect = new Rect( x, y, MathF.Max( 1, width - 1 ), h );
		painter.Fill = color;
		painter.Rect( rect );
		_hits.Add( new Hit( rect, name, ms, tab ) );

		// Clipped at the edge: say so
		if ( x + ms * px > end + 0.5f )
		{
			painter.Fill = DevToolsStyles.Bad;
			painter.Rect( new Rect( end - 3, y, 3, h ) );
		}

		var ink = textColor ?? Color.Parse( "#141414" ).Value;
		var withMs = $"{name} {DevToolsFormat.Ms( ms )}";
		if ( width > withMs.Length * 6.4f + 10 )
			Text( painter, withMs, new Rect( x + 5, y, width - 8, h ), TextFlag.LeftCenter, ink, 11, "Inter" );
		else if ( width > name.Length * 6.4f + 10 )
			Text( painter, name, new Rect( x + 5, y, width - 8, h ), TextFlag.LeftCenter, ink, 11, "Inter" );

		return end;
	}

	/// <summary>
	/// The GPU's leaf passes at their offsets on the ruler: the frame's span as a dark track, so idle shows as gaps,
	/// async compute in a thin lane under the graphics queue, and the CPU's submits as ticks above.
	/// </summary>
	void DrawGpuTimeline( Painter painter, DevTools.GpuLayout timeline, float y, Func<float, float> X )
	{
		var x0 = X( 0 );
		painter.Fill = DevToolsStyles.Raised;
		painter.Rect( new Rect( x0, y, MathF.Max( 1, X( timeline.ElapsedMs ) - x0 ), CostRowH ) );

		var laneH = timeline.HasAsync ? CostRowH - 11 : CostRowH;
		foreach ( var span in timeline.Spans )
		{
			if ( !span.Leaf ) continue;

			var left = X( span.StartMs );
			var width = MathF.Max( 0.75f, X( span.EndMs ) - left );
			var rect = span.Async ? new Rect( left, y + laneH + 1, width, CostRowH - laneH - 1 ) : new Rect( left, y, width, laneH );
			painter.Fill = DevToolsStyles.PassColor( span.Kind );
			painter.Rect( rect );
			_hits.Add( new Hit( rect, span.Async ? $"{span.Name} (async)" : span.Name, span.Ms, "rendering" ) );

			if ( !span.Async && width > span.Name.Length * 6.4f + 10 )
				Text( painter, span.Name, new Rect( left + 5, y, width - 8, laneH ), TextFlag.LeftCenter, Color.Parse( "#141414" ).Value, 11, "Inter" );
		}

		// When the CPU handed work over, on the GPU's clock
		painter.Fill = DevToolsStyles.Text.WithAlpha( 0.8f );
		foreach ( var t in timeline.Submits )
			painter.Rect( new Rect( X( t ) - 0.5f, y - 5, 1.5f, 5 ) );

		_legend.Add( (DevToolsStyles.Gpu, "busy", timeline.BusyMs) );
		_legend.Add( (DevToolsStyles.Raised, "elapsed", timeline.ElapsedMs) );
		if ( timeline.HasAsync ) _legend.Add( (DevToolsStyles.Dim, "async", timeline.AsyncBusyMs) );
		foreach ( var span in timeline.Spans.Where( x => x.Leaf ).OrderByDescending( x => x.Ms ).Take( 8 ) )
			_legend.Add( (DevToolsStyles.PassColor( span.Kind ), span.Name, span.Ms) );
	}

	/// <summary>
	/// The row's parts by name, in the order they're drawn, for the segments too thin to name inside.
	/// </summary>
	void Legend( Painter painter, float x, float y, float width )
	{
		var end = x + width;
		foreach ( var (color, name, ms) in _legend )
		{
			var text = $"{name} {DevToolsFormat.Ms( ms )}";
			var tw = text.Length * 6.1f + 18;
			if ( x + tw > end ) break;
			painter.Fill = color;
			painter.Rect( new Rect( x, y + LegendH / 2 - 4, 3, 8 ) );
			Text( painter, text, new Rect( x + 7, y, tw, LegendH ), TextFlag.LeftCenter, DevToolsStyles.Dim, 10 );
			x += tw;
		}
	}

	static void Label( Painter painter, string text, float y, float h ) =>
		Text( painter, text, new Rect( 8, y, LabelW - 12, h ), TextFlag.LeftCenter, DevToolsStyles.Dim, 11, "Inter" );

	void Total( Painter painter, float ms, float y, float h, float x, Color color )
	{
		if ( ShowTotals ) Text( painter, $"{DevToolsFormat.Ms( ms )} ms", new Rect( x + 6, y, Right - 8, h ), TextFlag.LeftCenter, color, 11 );
	}

	static void Marker( Painter painter, float ms, string name, float y, float h, Color color, Func<float, float> X )
	{
		var x = X( ms );
		painter.Fill = color;
		painter.Rect( new Rect( x - 1, y - 3, 2, h + 6 ) );
		Text( painter, $"{name} {ms:0.#}", new Rect( x + 4, y + h + 1, 90, 10 ), TextFlag.LeftCenter, color, 9 );
	}

	static void Text( Painter painter, string text, Rect rect, TextFlag flags, Color color, float size = 11, string font = "Roboto Mono" )
	{
		var scope = new TextRendering.Scope( text, color, size, font, font == "Inter" ? 500 : 500 );
		DebugOverlay.DrawText( painter, scope, rect, flags );
	}
}


/// <summary>
/// The frame at a glance, the way <c>stat unit</c> puts it: the frame, the main thread, its rendering and the GPU on one
/// line with what bounds the frame rate, recent frames under it, what's out of line, the frame's counters, and the frame
/// laid along its budget. Numbers first; colour only where something's out of line.
/// </summary>
internal sealed class DevToolsGlanceTab : DevToolsTab
{
	public override string TabId => DevTools.OverviewTabId;
	public override string Title => "Overview";

	sealed class Stat : Panel
	{
		public readonly Label Value, Unit;

		public Stat( Panel parent, string key, string tab )
		{
			AddClass( "stat" );
			parent.AddChild( this );
			AddChild( new Label( key, "stat-key" ) );
			Value = AddChild( new Label( "-", "stat-value" ) );
			Unit = AddChild( new Label( "", "stat-unit" ) );
			if ( tab is not null )
			{
				AddClass( "link" );
				AddEventListener( "onclick", () => DevTools.ActiveTab = tab );
			}
		}

		public void Set( string value, string unit, string tooltip, DevTools.Severity? severity = null )
		{
			Value.Text = value;
			Unit.Text = unit;
			Tooltip = tooltip;
			SetClass( "warning", severity == DevTools.Severity.Warning );
			SetClass( "bad", severity == DevTools.Severity.Bad );
		}
	}

	readonly Stat _frame, _cpu, _render, _gpu, _bound;
	readonly DevToolsFindings _findings;
	readonly DevToolsFacts _counters;

	/// <summary>
	/// The main thread's rendering, among the <see cref="DevTools.Categories"/>.
	/// </summary>
	static readonly int RenderCategory = Array.IndexOf( DevTools.Categories, PerformanceStats.Timings.Render );

	public DevToolsGlanceTab()
	{
		AddClass( "glance" );

		var line = AddChild( new Panel( null, "stat-line span-all" ) );
		_frame = new Stat( line, "frame", "cpu" );
		_cpu = new Stat( line, "cpu", "cpu" );
		_render = new Stat( line, "render", "cpu" );
		_gpu = new Stat( line, "gpu", "rendering" );
		// The limiter is judged from the frame's timings, not measured: it says likely
		_bound = new Stat( line, "likely bound by", null );
		_bound.AddClass( "stat-verdict" );

		var history = AddChild( new DevToolsSparkline
		{
			Sample = age => DevTools.ShownFrames.FrameMs( age ),
			Floor = 4,
			Target = () => DevTools.Current.TargetMs,
			Selectable = true,
		} );
		history.AddClass( "glance-history" );
		history.AddClass( "span-all" );

		_findings = AddChild( new DevToolsFindings() );
		_findings.AddClass( "span-all" );

		_counters = AddChild( new DevToolsFacts() );
		_counters.AddClass( "span-all" );

		var ruler = AddChild( new DevToolsBudget { ShowTotals = true } );
		ruler.AddClass( "span-all" );
	}

	public override void Refresh( DevTools.Summary s )
	{
		var none = s.Frames == 0;
		var target = s.TargetMs;
		DevTools.Severity? Over( bool limiting ) => none || s.MeetingTarget || !limiting ? null : s.Rating;

		_frame.Set( none ? "-" : $"{s.FrameAvg:0.00}", "ms",
			none ? null : $"{s.Fps:0} fps, target {s.TargetFps:0} from {s.TargetSource}\np99 {s.FrameP99:0.00} ms, max {s.FrameMax:0.0} ms, {s.Spikes} hitches over {s.HitchMs:0} ms",
			none || s.MeetingTarget ? null : s.Rating );

		_cpu.Set( none ? "-" : $"{s.CpuWorkAvg:0.00}", "ms",
			none ? null : $"Main thread work a frame, {s.CpuWorkAvg / target * 100:0}% of the budget\np99 {s.CpuTime.P99:0.00} ms, max {s.CpuTime.Max:0.0} ms",
			Over( s.Limiter == DevTools.Limiter.Cpu ) );

		var render = RenderCategory >= 0 ? s.CategoryTime[RenderCategory] : default;
		_render.Set( none ? "-" : $"{render.Avg:0.00}", "ms",
			none ? null : $"The main thread's rendering - culling, preparing, recording and submitting - p99 {render.P99:0.00} ms\nThe rest of the CPU time is the game: see the CPU tab" );

		_gpu.Set( s.GpuAvg > 0 ? $"{s.GpuAvg:0.00}" : "-", "ms",
			s.GpuAvg > 0 ? $"{(DevTools.GpuFromPasses ? "GPU busy a frame, its passes added up" : "GPU time a frame")}, {s.GpuAvg / target * 100:0}% of the budget\np99 {s.GpuTime.P99:0.00} ms, max {s.GpuTime.Max:0.0} ms\nIts passes on the Rendering tab" : "No GPU timings yet",
			Over( s.Limiter == DevTools.Limiter.Gpu ) );

		_bound.Set( none ? "-" : s.LimitText, "", none ? null : DevToolsWindow.LimitTooltip( s ), none || s.MeetingTarget ? null : s.Rating );

		_findings.Set( s.Issues );
		RefreshCounters();
	}

	void RefreshCounters()
	{
		var f = FrameStats.Current;
		var m = f.Managed;
		var managed = m.Renders > 0;
		var c = _counters;

		var draws = managed ? m.TotalDraws : f.DrawCalls;
		c.Set( "Draws", $"{draws:N0}", draws >= 5000 ? DevTools.Severity.Bad : draws >= 3000 ? DevTools.Severity.Warning : null, "Draw calls a frame. By pass on the Rendering tab" );
		c.Set( "Triangles", DevToolsFormat.Count( managed ? m.Triangles : f.TrianglesRendered ) );
		c.Set( "Objects", $"{(managed ? m.ObjectsVisible : f.ObjectsRendered):N0}", tooltip: managed ? $"Visible, of {m.Objects:N0}" : $"Drawn, of {f.ObjectsTested:N0} tested" );
		c.Set( "Lights", $"{(managed ? m.Lights : f.ShadowedLightsInView + f.UnshadowedLightsInView):N0}" );

		var second = PerformanceStats.LastSecond;
		var gcMs = TimeSpan.FromTicks( second.GcPause ).TotalMilliseconds;
		c.Set( "Memory", $"{PerformanceStats.ApproximateProcessMemoryUsage / (1024.0 * 1024 * 1024):0.00} GiB",
			gcMs > 8 || second.Gc2 > 0 ? DevTools.Severity.Bad : gcMs > 2 ? DevTools.Severity.Warning : null,
			$"Process memory\nAllocating {DevToolsFormat.Bytes( second.ByteAlloc )}/s managed, paused {gcMs:0.0} ms/s for GC" );

		if ( GpuProfilerStats.VideoMemoryBudget > 0 )
		{
			var vram = GpuProfilerStats.VideoMemoryUsageFraction;
			c.Set( "VRAM", $"{vram * 100:0}%", vram > 0.95f ? DevTools.Severity.Bad : vram > 0.8f ? DevTools.Severity.Warning : null,
				$"Video memory, {DevToolsFormat.BytesOf( GpuProfilerStats.VideoMemoryUsed, GpuProfilerStats.VideoMemoryBudget )}" );
		}
		else
		{
			c.Hide( "VRAM" );
		}
	}
}
