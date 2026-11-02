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

	/// <summary>
	/// What a GPU segment does when clicked: open the GPU's own timeline under the ruler.
	/// </summary>
	const string ZoomGpu = "gpu-zoom";

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
			if ( hit.Tab == ZoomGpu ) DevTools.GpuZoom = !DevTools.GpuZoom;
			else if ( DevTools.ShowAdvanced ) DevTools.ActiveTab = hit.Tab;
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
		var gpuMs = MathF.Max( picked ? frames.GpuMs( sel ) : s.GpuAvg, DevTools.GpuTimeline.ElapsedMs );

		// Room for the budget and a little past it, or the p99 frame - but a long hitch is clipped rather than allowed to
		// shrink the budget to a sliver
		var wanted = MathF.Max( target * 1.15f, MathF.Max( picked ? frameMs : s.FrameP99, gpuMs ) * 1.05f );
		wanted = MathF.Min( wanted, target * 2.5f );
		_scaleMs = _scaleMs.LerpTo( wanted, Time.Delta * 4f );
		if ( _scaleMs < wanted * 0.8f || _scaleMs > wanted * 1.5f ) _scaleMs = wanted;

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
		var timeline = DevTools.GpuTimeline;
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
				gx = Segment( painter, gx, gpuY, CostRowH, p.AvgMs, p.Color, p.Name, px, "gpu" );
				_legend.Add( (p.Color, p.Name, p.AvgMs) );
				gpuSum += p.AvgMs;
			}

			if ( s.GpuAvg > gpuSum + 0.05f ) gx = Segment( painter, gx, gpuY, CostRowH, s.GpuAvg - gpuSum, DevToolsStyles.Raised, "other", px, "gpu", DevToolsStyles.Dim );
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
			painter.Fill = DevToolsStyles.Series[span.Family];
			painter.Rect( rect );
			_hits.Add( new Hit( rect, span.Async ? $"{span.Name} (async)" : span.Name, span.Ms, ZoomGpu ) );

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
			_legend.Add( (DevToolsStyles.Series[span.Family], span.Name, span.Ms) );
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
/// A thin bar of segments against a budget - how much of it each part takes, with the budget marked.
/// </summary>
internal sealed class DevToolsStackBar : Panel
{
	public Func<IEnumerable<(Color Color, float Value)>> Segments { get; set; }
	public Func<float> Budget { get; set; } = () => 1;
	public bool BudgetLine { get; set; } = true;

	public DevToolsStackBar()
	{
		AddClass( "stack-bar" );
	}

	public override void OnDraw( Painter painter )
	{
		if ( Segments is null ) return;

		var scale = ScaleToScreen;
		var w = Box.Rect.Width / scale;
		var h = Box.Rect.Height / scale;
		var budget = MathF.Max( 0.0001f, Budget() );

		using var state = painter.Scope();
		painter.Scale( scale );
		painter.Stroke = Stroke.None;

		painter.Fill = DevToolsStyles.Raised;
		painter.Rect( new Rect( 0, 0, w, h ) );

		// The budget sits at 80% of the width, so going over it shows
		var full = BudgetLine ? budget * 1.25f : budget;
		float x = 0;
		foreach ( var (color, value) in Segments() )
		{
			var sw = value / full * w;
			if ( x + sw > w ) sw = w - x;
			if ( sw <= 0 ) break;
			painter.Fill = color;
			painter.Rect( new Rect( x, 0, MathF.Max( 1, sw - 1 ), h ) );
			x += sw;
		}

		if ( BudgetLine )
		{
			painter.Fill = DevToolsStyles.Text;
			painter.Rect( new Rect( w * 0.8f - 1, -2, 2, h + 4 ) );
		}
	}
}


/// <summary>
/// Docked, for reading at a glance: a row of gauges over the frame drawn against its budget. A gauge is a name, a number
/// and a chart, edged amber or red when it's out of line; everything else it knows is in its tooltip.
/// </summary>
internal sealed class DevToolsGlanceTab : DevToolsTab
{
	public override string TabId => DevTools.OverviewTabId;
	public override string Title => "Overview";
	public override bool Advanced => false;

	sealed class Gauge : Panel
	{
		public Label Value, Unit;
		public Panel Chart;
	}

	/// <summary>
	/// The rendering counters only update once a second, so they keep a minute of history of their own.
	/// </summary>
	sealed class Counter
	{
		public const int Size = 60;
		readonly float[] _values = new float[Size];
		int _head;
		public int Count { get; private set; }

		public void Push( float value )
		{
			_values[_head] = value;
			_head = (_head + 1) % Size;
			Count = Math.Min( Count + 1, Size );
		}

		public float At( int age ) => _values[(_head - 1 - age + Size * 2) % Size];
	}

	readonly Gauge _fps, _p99, _cpu, _gpu, _memory, _vram, _draws, _triangles, _objects, _lights;
	readonly Counter _drawHistory = new(), _triangleHistory = new(), _objectHistory = new(), _lightHistory = new();
	RealTimeSince _sinceSample = 1;
	readonly DevToolsGpuZoom _zoom;
	bool _zoomShown;
	float _zoomGrew;

	public DevToolsGlanceTab()
	{
		AddClass( "dock" );
		AddClass( "dock-glance" );

		var row = AddChild( new Panel( null, "glance-gauges" ) );
		Gauge Add( string key, string tab )
		{
			var g = row.AddChild( new Gauge() );
			g.AddClass( "gauge" );
			g.AddChild( new Label( key, "gauge-key" ) );
			var line = g.AddChild( new Panel( null, "gauge-line" ) );
			g.Value = line.AddChild( new Label( "-", "gauge-value" ) );
			g.Unit = line.AddChild( new Label( "", "gauge-unit" ) );
			g.Chart = g.AddChild( new Panel( null, "gauge-chart" ) );
			if ( tab is not null ) g.AddEventListener( "onclick", () => { if ( DevTools.ShowAdvanced ) DevTools.ActiveTab = tab; } );
			return g;
		}

		void History( Gauge g, Counter counter ) =>
			g.Chart.AddChild( new DevToolsSparkline { Sample = counter.At, Count = () => counter.Count, Capacity = Counter.Size, Floor = 1, Color = DevToolsStyles.Cpu } );

		Func<float> target = () => DevTools.Current.TargetMs;

		_fps = Add( "frame rate", "cpu" );
		_fps.Chart.AddChild( new DevToolsSparkline { Sample = age => DevTools.ShownFrames.FrameMs( age ), Floor = 4, Target = target, Selectable = true } );

		_p99 = Add( "p99 frame", "cpu" );
		_p99.Chart.AddChild( new DevToolsHistogram { ShowLabels = false } );

		_cpu = Add( "cpu", "cpu" );
		_cpu.Chart.AddChild( new DevToolsSparkline { Sample = age => DevTools.ShownFrames.BusyMs( age ), Floor = 4, Target = target, Selectable = true } );

		_gpu = Add( "gpu", "gpu" );
		_gpu.Chart.AddChild( new DevToolsSparkline { Sample = age => DevTools.ShownFrames.GpuMs( age ), Floor = 4, Target = target, Color = DevToolsStyles.Gpu, Selectable = true } );

		_memory = Add( "memory", "memory" );
		_memory.Chart.AddChild( new DevToolsSparkline { Sample = age => DevTools.ShownFrames.AllocKb( age ), Floor = 64, Color = DevToolsStyles.Dim, Selectable = true } );

		_vram = Add( "vram", "memory" );
		_vram.Chart.AddChild( new DevToolsStackBar { Segments = VramSegments, Budget = () => 1, BudgetLine = false } );

		_draws = Add( "draws", "rendering" );
		History( _draws, _drawHistory );

		_triangles = Add( "triangles", "rendering" );
		History( _triangles, _triangleHistory );

		_objects = Add( "objects", "rendering" );
		History( _objects, _objectHistory );

		_lights = Add( "lights", "shadows" );
		History( _lights, _lightHistory );

		var ruler = AddChild( new Panel( null, "glance-ruler" ) );
		ruler.AddChild( new DevToolsBudget { ShowTotals = false } );
		_zoom = ruler.AddChild( new DevToolsGpuZoom() );
		var history = ruler.AddChild( new DevToolsSparkline { Sample = age => DevTools.ShownFrames.FrameMs( age ), Floor = 4, Target = target, Selectable = true } );
		history.AddClass( "dock-history" );
		history.Style.MarginRight = 14;
	}

	public override void Tick()
	{
		base.Tick();

		// The zoomed GPU frame opens under the ruler, and the window grows to fit it rather than scrolling
		var open = DevTools.GpuZoom;
		_zoom.SetClass( "hidden", !open );
		if ( open == _zoomShown ) return;
		_zoomShown = open;

		if ( SurfaceWindow.FromPanel( this ) is not { } window || window.IsMaximized ) return;
		if ( open )
		{
			_zoomGrew = DevToolsGpuZoom.HeightFor( DevTools.GpuTimeline ) + 8;
			window.Size += new Vector2( 0, _zoomGrew );
		}
		else if ( _zoomGrew > 0 )
		{
			window.Size -= new Vector2( 0, _zoomGrew );
			_zoomGrew = 0;
		}
	}

	static IEnumerable<(Color, float)> VramSegments()
	{
		var f = GpuProfilerStats.VideoMemoryUsageFraction;
		yield return (f > 0.95f ? DevToolsStyles.Bad : f > 0.8f ? DevToolsStyles.Warning : DevToolsStyles.Cpu, f);
	}

	static void Set( Gauge g, string value, string unit, string tooltip, DevTools.Severity? severity = null )
	{
		g.Value.Text = value;
		g.Unit.Text = unit;
		g.Tooltip = tooltip;
		g.SetClass( "warning", severity == DevTools.Severity.Warning );
		g.SetClass( "bad", severity == DevTools.Severity.Bad );
	}

	/// <summary>
	/// Memory sizes side by side read in one unit: GiB, to two places.
	/// </summary>
	static string Gib( double bytes ) => $"{bytes / (1024.0 * 1024 * 1024):0.00}";

	public override void Refresh( DevTools.Summary s )
	{
		var none = s.Frames == 0;
		var second = PerformanceStats.LastSecond;
		var gcMs = TimeSpan.FromTicks( second.GcPause ).TotalMilliseconds;

		Set( _fps, none ? "-" : $"{s.Fps:0}", "fps",
			none ? null : $"{s.FrameAvg:0.00} ms a frame, target {s.TargetFps:0} fps from {s.TargetSource}\nLimited by {s.LimitText}: {DevToolsWindow.LimitTooltip( s )}",
			none || s.MeetingTarget ? null : s.Rating );

		Set( _p99, none ? "-" : $"{s.FrameP99:0.0}", "ms",
			$"99% of frames come in under this\nHitches (over {s.HitchMs:0.0} ms): {s.Spikes}{(s.Spikes > 0 ? $", worst {s.WorstSpikeMs:0} ms" : "")}\nFrame time spread, the budget dashed",
			s.Spikes > 4 || s.WorstSpikeMs > 100 ? DevTools.Severity.Bad : s.Spikes > 0 || s.FrameP99 > s.TargetMs * 1.25f ? DevTools.Severity.Warning : null );

		Set( _cpu, none ? "-" : $"{s.CpuWorkAvg:0.00}", "ms",
			none ? null : $"Main thread work a frame, {s.CpuWorkAvg / s.TargetMs * 100:0}% of the budget\np99 {s.CpuTime.P99:0.00} ms, max {s.CpuTime.Max:0.0} ms",
			s.Limiter == DevTools.Limiter.Cpu && !s.MeetingTarget ? s.Rating : null );

		Set( _gpu, s.GpuAvg > 0 ? $"{s.GpuAvg:0.00}" : "-", "ms",
			s.GpuAvg > 0 ? $"{(DevTools.GpuFromPasses ? "GPU busy a frame, its passes added up" : "GPU time a frame")}, {s.GpuAvg / s.TargetMs * 100:0}% of the budget\np99 {s.GpuTime.P99:0.00} ms, max {s.GpuTime.Max:0.0} ms" : "No GPU timings yet",
			s.Limiter == DevTools.Limiter.Gpu && !s.MeetingTarget ? s.Rating : null );

		Set( _memory, Gib( PerformanceStats.ApproximateProcessMemoryUsage ), "GiB",
			$"Process memory\nAllocating {DevToolsFormat.Bytes( second.ByteAlloc )}/s managed, paused {gcMs:0.0} ms/s for GC (gen0 {second.Gc0}, gen1 {second.Gc1}, gen2 {second.Gc2})\nChart: allocation per frame",
			gcMs > 8 || second.Gc2 > 0 ? DevTools.Severity.Bad : gcMs > 2 || second.ByteAlloc > 20 * 1024 * 1024 ? DevTools.Severity.Warning : null );

		var vram = GpuProfilerStats.VideoMemoryUsageFraction;
		var hasVram = GpuProfilerStats.VideoMemoryBudget > 0;
		Set( _vram, hasVram ? Gib( GpuProfilerStats.VideoMemoryUsed ) : "-", "GiB",
			hasVram ? $"Video memory, {vram * 100:0}% of the {Gib( GpuProfilerStats.VideoMemoryBudget )} GiB budget" : null,
			vram > 0.95f ? DevTools.Severity.Bad : vram > 0.8f ? DevTools.Severity.Warning : null );

		// Rendering counters, which already include the managed renderer's
		var f = FrameStats.Current;
		if ( _sinceSample >= 1 && !DevTools.Paused )
		{
			_sinceSample = 0;
			_drawHistory.Push( (float)f.DrawCalls );
			_triangleHistory.Push( (float)f.TrianglesRendered );
			_objectHistory.Push( (float)f.ObjectsRendered );
			_lightHistory.Push( Lights( f ) );
		}

		Set( _draws, DevToolsFormat.Count( f.DrawCalls ), "",
			$"Draw calls a frame, counted once a second\n{f.MaterialChanges:N0} material changes, {f.RenderTargetResolves:N0} resolves",
			f.DrawCalls > 5000 ? DevTools.Severity.Bad : f.DrawCalls > 3000 ? DevTools.Severity.Warning : null );
		Set( _triangles, DevToolsFormat.Count( f.TrianglesRendered ), "",
			f.DrawCalls > 0 ? $"Triangles a frame, {f.TrianglesRendered / f.DrawCalls:N0} a draw" : "Triangles a frame" );
		Set( _objects, DevToolsFormat.Count( f.ObjectsRendered ), "",
			$"Objects drawn, of {f.ObjectsTested:N0} tested\nCulled: {f.ObjectsCulledByVis:N0} by visibility, {f.ObjectsCulledByScreenSize:N0} by size, {f.ObjectsCulledByFade:N0} by fade" );

		var managed = f.Managed.Renders > 0;
		Set( _lights, $"{Lights( f ):N0}", "",
			managed ? $"Lights in view\n{f.Managed.ShadowViews:N0} shadow views rendered" : $"Lights in view, {f.ShadowedLightsInView:N0} shadowed\n{f.ShadowMaps:N0} shadow maps" );
	}

	static float Lights( FrameStats f ) => f.Managed.Renders > 0 ? f.Managed.Lights : (float)(f.ShadowedLightsInView + f.UnshadowedLightsInView);
}

/// <summary>
/// The GPU's frame at its own scale: every pass where it ran, nested under its parent like a flame chart, graphics
/// and async compute apart, the CPU's submits marked. Opened from the budget ruler's GPU row, where the GPU is often too
/// small to read; the scale here is the GPU frame's, not the budget's, and says so.
/// </summary>
internal sealed class DevToolsGpuZoom : Panel
{
	const float LabelW = 56, AxisH = 20, RowH = 16, Right = 14, Gap = 6;

	readonly List<(Rect Rect, DevTools.GpuSpan Span)> _hits = new();

	public DevToolsGpuZoom()
	{
		AddClass( "gpu-zoom" );
	}

	/// <summary>
	/// The height the layout needs: an axis, a row a depth on each queue.
	/// </summary>
	public static float HeightFor( DevTools.GpuLayout layout ) =>
		AxisH + (layout.MaxDepth + 1) * RowH + (layout.HasAsync ? Gap + (layout.MaxAsyncDepth + 1) * RowH : 0) + 8;

	public override void Tick()
	{
		base.Tick();
		Style.Height = HeightFor( DevTools.GpuTimeline );
	}

	protected override void OnClick( MousePanelEvent e )
	{
		// The label column closes it again
		if ( e.LocalPosition.x / ScaleToScreen < LabelW ) DevTools.GpuZoom = false;
	}

	public override void OnDraw( Painter painter )
	{
		var timeline = DevTools.GpuTimeline;
		var scale = ScaleToScreen;
		var w = Box.Rect.Width / scale;
		var h = Box.Rect.Height / scale;
		if ( w < 200 ) return;

		using var state = painter.Scope();
		painter.Scale( scale );
		painter.Stroke = Stroke.None;
		_hits.Clear();

		var plotX = LabelW;
		var plotW = w - LabelW - Right;

		if ( timeline.Spans.Count == 0 || timeline.ElapsedMs <= 0 )
		{
			Text( painter, "Waiting for a GPU timeline", new Rect( plotX, 0, plotW, h ), TextFlag.LeftCenter, DevToolsStyles.Faint, 11, "Inter" );
			return;
		}

		var px = plotW / timeline.ElapsedMs;
		float X( float ms ) => plotX + ms * px;
		var rowsTop = AxisH;
		var asyncTop = rowsTop + (timeline.MaxDepth + 1) * RowH + Gap;
		var bottom = timeline.HasAsync ? asyncTop + (timeline.MaxAsyncDepth + 1) * RowH : asyncTop - Gap;

		painter.Fill = DevToolsStyles.Plot;
		painter.Rect( new Rect( plotX, rowsTop, plotW, bottom - rowsTop ) );

		// Axis, in the GPU frame's own ms
		ReadOnlySpan<float> steps = [0.01f, 0.02f, 0.05f, 0.1f, 0.2f, 0.25f, 0.5f, 1, 2, 5, 10];
		var step = steps[^1];
		foreach ( var st in steps ) { if ( st * px >= 64 ) { step = st; break; } }
		for ( float t = 0; t <= timeline.ElapsedMs + 0.0001f; t += step )
		{
			var x = X( t );
			painter.Fill = DevToolsStyles.Line;
			painter.Rect( new Rect( x, AxisH - 5, 1, 5 ) );
			painter.Fill = Color.White.WithAlpha( 0.035f );
			painter.Rect( new Rect( x, rowsTop, 1, bottom - rowsTop ) );
			Text( painter, t == 0 ? "0" : $"{t:0.##}", new Rect( x - 30, 2, 60, AxisH - 8 ), TextFlag.Center, DevToolsStyles.Faint, 10 );
		}

		var busyShare = timeline.ElapsedMs > 0 ? timeline.BusyMs / timeline.ElapsedMs : 0;
		var summary = $"GPU frame zoomed: busy {timeline.BusyMs:0.00} of {timeline.ElapsedMs:0.00} ms ({busyShare * 100:0}%)";
		var summaryW = summary.Length * 6.2f + 12;
		painter.Fill = DevToolsStyles.Plot;
		painter.Rect( new Rect( plotX + plotW - summaryW, 1, summaryW, AxisH - 7 ) );
		Text( painter, summary, new Rect( plotX, 2, plotW - 4, AxisH - 8 ), TextFlag.RightCenter, DevToolsStyles.Bright, 10 );

		Text( painter, "graphics", new Rect( 8, rowsTop, LabelW - 10, RowH ), TextFlag.LeftCenter, DevToolsStyles.Dim, 11, "Inter" );
		if ( timeline.HasAsync ) Text( painter, "async", new Rect( 8, asyncTop, LabelW - 10, RowH ), TextFlag.LeftCenter, DevToolsStyles.Dim, 11, "Inter" );
		Text( painter, "close", new Rect( 8, 2, LabelW - 10, AxisH - 8 ), TextFlag.LeftCenter, DevToolsStyles.Faint, 10, "Inter" );

		// Submits: a tick on the axis and a faint line down, so a pass waiting on one lines up with it
		foreach ( var t in timeline.Submits )
		{
			var x = X( t );
			painter.Fill = DevToolsStyles.Text.WithAlpha( 0.12f );
			painter.Rect( new Rect( x, rowsTop, 1, bottom - rowsTop ) );
			painter.Fill = DevToolsStyles.Text;
			painter.Rect( new Rect( x - 0.5f, AxisH - 6, 2, 6 ) );
		}

		foreach ( var span in timeline.Spans )
		{
			var y = (span.Async ? asyncTop : rowsTop) + span.Depth * RowH;
			var left = X( span.StartMs );
			var width = MathF.Max( 0.75f, span.Ms * px - 1 );
			var rect = new Rect( left, y, width, RowH - 1 );

			// Parents dim, so the leaves - where the time is spent - stand out
			var color = DevToolsStyles.Series[span.Family];
			painter.Fill = span.Leaf ? color : color.WithAlpha( 0.35f );
			painter.Rect( rect );
			_hits.Add( (rect, span) );

			var ink = span.Leaf ? Color.Parse( "#141414" ).Value : DevToolsStyles.Text;
			var withMs = $"{span.Name} {DevToolsFormat.Ms( span.Ms )}";
			if ( width > withMs.Length * 6.2f + 8 )
				Text( painter, withMs, new Rect( left + 4, y, width - 6, RowH - 1 ), TextFlag.LeftCenter, ink, 10.5f, "Inter" );
			else if ( width > span.Name.Length * 6.2f + 8 )
				Text( painter, span.Name, new Rect( left + 4, y, width - 6, RowH - 1 ), TextFlag.LeftCenter, ink, 10.5f, "Inter" );
		}

		if ( HasHovered )
		{
			var m = MousePosition / scale;
			for ( int i = _hits.Count - 1; i >= 0; i-- )
			{
				var (rect, span) = _hits[i];
				if ( !rect.IsInside( m ) ) continue;

				painter.Stroke = Stroke.Solid( Color.White, 1 );
				painter.Fill = Color.Transparent;
				painter.Rect( rect );
				painter.Stroke = Stroke.None;

				var box = new Rect( MathF.Min( m.x + 12, w - 210 ), MathF.Min( m.y + 12, h - 36 ), 200, 34 );
				painter.Fill = Color.Parse( "#1C1C1C" ).Value;
				painter.Stroke = Stroke.Solid( DevToolsStyles.Line, 1 );
				painter.Rect( box );
				painter.Stroke = Stroke.None;
				Text( painter, span.Name, new Rect( box.Left + 6, box.Top + 2, box.Width - 12, 15 ), TextFlag.LeftCenter, DevToolsStyles.Text, 11, "Inter" );
				Text( painter, $"{span.Ms:0.000} ms, {span.StartMs:0.00} to {span.EndMs:0.00}", new Rect( box.Left + 6, box.Top + 17, box.Width - 12, 15 ), TextFlag.LeftCenter, DevToolsStyles.Dim, 10 );
				break;
			}
		}
	}

	static void Text( Painter painter, string text, Rect rect, TextFlag flags, Color color, float size = 11, string font = "Roboto Mono" )
	{
		var scope = new TextRendering.Scope( text, color, size, font, 500 );
		DebugOverlay.DrawText( painter, scope, rect, flags );
	}
}
