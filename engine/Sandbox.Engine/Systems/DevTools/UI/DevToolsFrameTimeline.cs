using Sandbox.Rendering;
using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// The frame on one clock, laid out the way Unreal Insights, PIX and Tracy lay out a capture: an overview of the whole
/// frame with the part in view outlined, a ruler, the GPU's passes nested under their parents - graphics, then async
/// compute - and, through the managed renderer, the CPU making the frame under them, lined up at its first submit.
/// It opens fitted to the GPU's work. The wheel zooms about the cursor, dragging pans, dragging the ruler measures,
/// double clicking a pass zooms to it and double clicking nothing goes back. Clicking a pass picks it, as picking it
/// in the pass tree does; picking one there pans to it.
/// </summary>
internal sealed class DevToolsFrameTimeline : Panel
{
	const float Gutter = 104, Right = 10;
	const float OverviewH = 28, RulerH = 22, RowH = 20, WorkerH = 22, ThreadH = 16, Gap = 6, Separator = 10, LegendH = 20;

	readonly record struct Hit( Rect Rect, string Key, string Title, string Detail );
	readonly List<Hit> _hits = new();

	/// <summary>
	/// The passes' keys, so a click picks the same row the tree shows (<see cref="DevTools.RenderPass.Key"/>).
	/// </summary>
	static string KeyOf( DevTools.GpuSpan span ) => (span.Async ? "async:" : "gfx:") + span.Path;

	/// <summary>
	/// What's in view, and where it's easing to, on the axis (<see cref="U"/>) - ms, but for the elided gap.
	/// </summary>
	float _from, _to, _goalFrom, _goalTo;

	/// <summary>
	/// The stretch between the CPU handing the frame over and the GPU starting on it, in ms, and how long it's drawn as.
	/// The GPU spends it finishing the frame before, which isn't captured, so drawn to scale it was mostly empty space.
	/// </summary>
	float _gapFrom, _gapTo, _gapShown;

	/// <summary>
	/// Whether the view is the reader's own. Until it is, it follows the GPU's work as the averages move.
	/// </summary>
	bool _placed;

	readonly Stack<(float From, float To)> _back = new();
	bool _workersOpen;
	string _selected;

	enum Drag { None, Pan, Measure, Overview }
	Drag _drag;
	Vector2 _pressedAt;
	float _pressedFrom, _pressedTo;
	bool _dragged;

	/// <summary>
	/// A measured range, in ms.
	/// </summary>
	(float From, float To)? _measure;

	/// <summary>
	/// Where the sections were last drawn, for input.
	/// </summary>
	float _w, _rulerTop, _tracksTop, _tracksBottom, _workersTop;

	/// <summary>
	/// What's in view, for the section's band.
	/// </summary>
	public Label Status { get; set; }

	public DevToolsFrameTimeline()
	{
		AddClass( "frame-timeline" );
	}

	/// <summary>
	/// The managed frame the layout was built with, so the CPU holds still with the GPU - and with everything else when
	/// the dev tools is paused.
	/// </summary>
	static ManagedFramePlan Plan => DevTools.ShownGpuTimeline.Plan;

	/// <summary>
	/// Everything the timeline writes that depends only on the frame it shows - labels, tooltips, the workers - made once
	/// for each new layout rather than on every draw, which made thousands of strings a second.
	/// </summary>
	sealed class Words
	{
		public readonly DevTools.GpuLayout Timeline;
		public readonly ManagedFramePlan Plan;
		public readonly List<int> Workers;
		readonly Dictionary<(string Path, bool Async), SpanWords> _spans = new();
		readonly Dictionary<int, (string Title, string Detail)> _submits = new();
		readonly Dictionary<int, (string Title, string Detail, string Label)> _segments = new();
		readonly Dictionary<string, string> _phases = new();
		public string Legend( DevTools.PassKind kind, float ms ) => _legend.TryGetValue( kind, out var text ) ? text : _legend[kind] = $"{DevToolsStyles.PassKindName( kind )} {DevToolsFormat.Ms( ms )}";
		readonly Dictionary<DevTools.PassKind, string> _legend = new();
		public string GapLabel, GapTitle, CpuBefore, CpuAfter, WorkersTitle, WorkersDetail;

		public Words( DevTools.GpuLayout timeline, ManagedFramePlan plan )
		{
			Timeline = timeline;
			Plan = plan;
			Workers = DevToolsFrameTimeline.Workers( plan );
		}

		public readonly record struct SpanWords( string Key, string Name, string Label, string Detail );

		public SpanWords Span( in DevTools.GpuSpan span )
		{
			if ( _spans.TryGetValue( (span.Path, span.Async), out var words ) ) return words;

			// The managed renderer's stage hooks all start "Managed: ", which says nothing here
			var name = Short( span.Name );
			words = new SpanWords( KeyOf( span ), name, $"{name} {DevToolsFormat.Ms( span.AvgMs )}",
				span.MaxMs > 0 ? $"{span.AvgMs:0.000} ms avg, {span.MaxMs:0.000} max · {span.StartMs:0.00} to {span.EndMs:0.00}" : $"{span.AvgMs:0.000} ms · {span.StartMs:0.00} to {span.EndMs:0.00}" );
			_spans[(span.Path, span.Async)] = words;
			return words;
		}

		public (string Title, string Detail) Submit( int index, float at ) =>
			_submits.TryGetValue( index, out var words ) ? words : _submits[index] = ($"submit {index + 1}", $"at {at:0.000} ms");

		public (string Title, string Detail, string Label) Segment( int index, in ManagedFramePlan.Segment segment, string where )
		{
			if ( _segments.TryGetValue( index, out var words ) ) return words;
			var name = Short( segment.First ?? "" );
			return _segments[index] = (name, $"{segment.Ms:0.000} ms recording {where}, ~{segment.Draws:N0} draws{(segment.Async ? ", async compute" : "")}", name);
		}

		public string Phase( string name, double ms ) => _phases.TryGetValue( name, out var text ) ? text : _phases[name] = $"{name} {DevToolsFormat.Ms( (float)ms )}";

		/// <summary>
		/// Each kind's GPU time: its leaves' time added up.
		/// </summary>
		public float KindMs( DevTools.PassKind kind )
		{
			if ( _kindMs is null )
			{
				_kindMs = new float[Enum.GetValues<DevTools.PassKind>().Length];
				foreach ( var span in Timeline.Spans )
					if ( span.Leaf ) _kindMs[(int)span.Kind] += span.AvgMs;
			}

			return _kindMs[(int)kind];
		}

		float[] _kindMs;

		/// <summary>
		/// How many workers were recording what at each moment: a step between each segment starting or ending, and the
		/// colours of what's being recorded through it, stacked the same way each time.
		/// </summary>
		public List<(float From, float To, Color[] Colors)> Workload => _workload ??= BuildWorkload();
		List<(float From, float To, Color[] Colors)> _workload;

		List<(float From, float To, Color[] Colors)> BuildWorkload()
		{
			var steps = new List<(float, float, Color[])>();
			if ( Plan is null ) return steps;

			var segments = Plan.Segments.Where( x => x.EndTicks > 0 && x.Thread != Plan.MainThread ).ToList();
			var edges = segments.SelectMany( x => new[] { (float)x.StartMs, (float)x.EndMs } ).Distinct().Order().ToList();
			for ( int i = 0; i + 1 < edges.Count; i++ )
			{
				var (from, to) = (edges[i], edges[i + 1]);
				var colors = segments.Where( x => x.StartMs <= from && x.EndMs >= to ).Select( x => SegmentColor( x ) ).OrderBy( x => x.r + x.g * 2 + x.b * 3 ).ToArray();
				if ( colors.Length > 0 ) steps.Add( (from, to, colors) );
			}

			return steps;
		}
	}

	Words _words;

	Words WordsFor( DevTools.GpuLayout timeline, ManagedFramePlan plan )
	{
		if ( _words is null || _words.Timeline != timeline || _words.Plan != plan ) _words = new Words( timeline, plan );
		return _words;
	}

	/// <summary>
	/// The worker threads that recorded the frame, in a stable order.
	/// </summary>
	static List<int> Workers( ManagedFramePlan plan )
	{
		var workers = new List<int>();
		if ( plan is null ) return workers;
		foreach ( var segment in plan.Segments )
			if ( segment.Thread != plan.MainThread && segment.EndTicks > 0 && !workers.Contains( segment.Thread ) ) workers.Add( segment.Thread );
		workers.Sort();
		return workers;
	}

	static float CpuEnd( ManagedFramePlan plan )
	{
		var end = (float)(plan.SubmitStartMs + plan.SubmitMs);
		foreach ( var segment in plan.Segments ) end = MathF.Max( end, (float)segment.EndMs );
		return end;
	}

	/// <summary>
	/// The GPU's work, from the first pass starting to the last ending, in ms.
	/// </summary>
	static (float From, float To) GpuExtent( DevTools.GpuLayout timeline )
	{
		if ( timeline.Spans.Count == 0 ) return (0, 1);

		// The spans are in the order they started
		var from = timeline.Spans[0].StartMs;
		return (from, MathF.Max( from + 0.01f, timeline.ElapsedMs ));
	}

	/// <summary>
	/// Everything there is, in ms: the CPU starting on the frame, through the GPU finishing it.
	/// </summary>
	static (float From, float To) FullExtent( DevTools.GpuLayout timeline, ManagedFramePlan plan )
	{
		var (from, to) = GpuExtent( timeline );
		if ( plan is null ) return (MathF.Min( 0, from ), to);
		return (MathF.Min( 0, MathF.Min( from, (float)plan.StartMs ) ), MathF.Max( to, CpuEnd( plan ) ));
	}

	/// <summary>
	/// Find the gap: from the last submit or the CPU's last recording to the GPU's first pass. It's elided when it's over
	/// half a millisecond and a good part of the frame, and then drawn as a twentieth of the rest.
	/// </summary>
	void FindGap( DevTools.GpuLayout timeline, ManagedFramePlan plan )
	{
		_gapShown = 0;
		if ( timeline.Spans.Count == 0 ) return;

		var cpuEnd = timeline.Submits.Count > 0 ? timeline.Submits.Max() : 0;
		if ( plan is not null ) cpuEnd = MathF.Max( cpuEnd, CpuEnd( plan ) );
		var (gpuStart, gpuEnd) = GpuExtent( timeline );
		var (from, _) = FullExtent( timeline, plan );

		var gap = gpuStart - cpuEnd;
		var rest = gpuEnd - from - gap;
		if ( gap < 0.5f || gap < rest * 0.15f ) return;

		(_gapFrom, _gapTo) = (cpuEnd, gpuStart);
		_gapShown = MathF.Max( 0.05f, rest * 0.05f );
	}

	/// <summary>
	/// A time on the axis: ms, with the gap squeezed to <see cref="_gapShown"/>.
	/// </summary>
	float U( float ms )
	{
		if ( _gapShown <= 0 || ms <= _gapFrom ) return ms;
		if ( ms >= _gapTo ) return ms - (_gapTo - _gapFrom) + _gapShown;
		return _gapFrom + (ms - _gapFrom) / (_gapTo - _gapFrom) * _gapShown;
	}

	/// <summary>
	/// The time at a point on the axis.
	/// </summary>
	float T( float u )
	{
		if ( _gapShown <= 0 || u <= _gapFrom ) return u;
		if ( u >= _gapFrom + _gapShown ) return u + (_gapTo - _gapFrom) - _gapShown;
		return _gapFrom + (u - _gapFrom) / _gapShown * (_gapTo - _gapFrom);
	}

	(float From, float To) OnAxis( (float From, float To) ms ) => (U( ms.From ), U( ms.To ));

	static (float From, float To) Padded( (float From, float To) range )
	{
		var pad = (range.To - range.From) * 0.03f;
		return (range.From - pad, range.To + pad);
	}

	/// <summary>
	/// Fit the GPU's work, or the whole frame.
	/// </summary>
	public void Fit( bool everything )
	{
		var range = everything ? FullExtent( DevTools.ShownGpuTimeline, Plan ) : GpuExtent( DevTools.ShownGpuTimeline );
		_back.Clear();
		_measure = null;
		_placed = everything;
		(_goalFrom, _goalTo) = Padded( OnAxis( range ) );
	}

	/// <summary>
	/// Move the view to a range on the axis, kept within the frame and never narrower than 5 µs.
	/// </summary>
	void Place( float from, float to )
	{
		var (min, max) = Padded( OnAxis( FullExtent( DevTools.ShownGpuTimeline, Plan ) ) );
		var width = MathF.Min( MathF.Max( to - from, 0.005f ), max - min );
		from = MathF.Max( min, MathF.Min( from, max - width ) );
		(_goalFrom, _goalTo) = (from, from + width);
		_placed = true;
	}

	/// <summary>
	/// Whether any of the CPU's work is in view. Fitted to the GPU it usually isn't, and its lanes fold to one line that
	/// says where it went, so the pass tree keeps its room.
	/// </summary>
	bool CpuInView( ManagedFramePlan plan ) =>
		plan is not null && CpuEnd( plan ) >= T( _goalFrom ) && plan.StartMs <= T( _goalTo );

	float CpuHeight( ManagedFramePlan plan, int workers ) => plan is null ? 0
		: !CpuInView( plan ) ? RowH
		: RowH * 2 + (_workersOpen ? workers * ThreadH : WorkerH);

	float Height( DevTools.GpuLayout timeline, ManagedFramePlan plan, int workers )
	{
		var h = OverviewH + 4 + RulerH + (timeline.MaxDepth + 1) * RowH;
		if ( timeline.HasAsync ) h += Gap + (timeline.MaxAsyncDepth + 1) * RowH;
		if ( plan is not null ) h += Separator + CpuHeight( plan, workers );
		return h + 6 + LegendH;
	}

	public override void Tick()
	{
		base.Tick();

		var timeline = DevTools.ShownGpuTimeline;
		var plan = Plan;
		FindGap( timeline, plan );
		Style.Height = Height( timeline, plan, WordsFor( timeline, plan ).Workers.Count );

		// Until the reader moves it, the view holds the GPU's work
		if ( !_placed && timeline.Spans.Count > 0 ) (_goalFrom, _goalTo) = Padded( OnAxis( GpuExtent( timeline ) ) );

		// A pass picked in the tree comes into view - panned to, never zoomed in on
		if ( DevTools.SelectedPass != _selected )
		{
			_selected = DevTools.SelectedPass;
			if ( timeline.Spans.FirstOrDefault( x => KeyOf( x ) == _selected ) is { Path: not null } span )
			{
				var (start, end) = (U( span.StartMs ), U( span.EndMs ));
				var width = _goalTo - _goalFrom;
				var margin = width * 0.05f;
				if ( end - start > width * 0.9f ) Place( start - (end - start) * 0.1f, end + (end - start) * 0.1f );
				else if ( start < _goalFrom ) Place( start - margin, start - margin + width );
				else if ( end > _goalTo ) Place( end + margin - width, end + margin );
			}
		}

		// Ease towards the goal, so a jump reads as a move
		if ( _to <= _from ) (_from, _to) = (_goalFrom, _goalTo);
		_from = _from.LerpTo( _goalFrom, 0.35f );
		_to = _to.LerpTo( _goalTo, 0.35f );
		if ( MathF.Abs( _from - _goalFrom ) < 0.0005f && MathF.Abs( _to - _goalTo ) < 0.0005f ) (_from, _to) = (_goalFrom, _goalTo);

		// Only written when what it says changes, rather than every tick
		var status = (T( _from ), T( _to ), _measure, timeline);
		if ( Status is not null && status != _status )
		{
			_status = status;
			Status.Text = timeline.Spans.Count == 0 ? "" : _measure is { } m
				? $"measured {DevToolsFormat.Ms( MathF.Abs( m.To - m.From ) )} ms"
				: $"{T( _from ):0.00} to {T( _to ):0.00} ms · {Showing( timeline )} · 0 = first submit";
		}
	}

	(float, float, (float, float)?, DevTools.GpuLayout) _status;

	/// <summary>
	/// What the timeline is of: the averages, or one captured frame standing in for the one picked.
	/// </summary>
	internal static string Showing( DevTools.GpuLayout timeline ) => timeline.CapturedOn <= 0 ? "avg of 2 s"
		: timeline.CapturedOn == timeline.Picked ? "one frame, captured on the picked one"
		: $"one frame, captured {Math.Abs( timeline.CapturedOn - timeline.Picked )} {(timeline.CapturedOn > timeline.Picked ? "after" : "before")} the picked one";

	float PlotX => Gutter;
	float PlotW => MathF.Max( 1, _w - Gutter - Right );
	float AxisAt( float x ) => _from + (x - PlotX) / PlotW * (_to - _from);
	float ToX( float ms ) => PlotX + (U( ms ) - _from) / MathF.Max( 0.0001f, _to - _from ) * PlotW;
	float ToMs( float x ) => T( AxisAt( x ) );

	public override void OnMouseWheel( Vector2 value )
	{
		var m = MousePosition / ScaleToScreen;
		if ( m.x < PlotX || DevTools.ShownGpuTimeline.Spans.Count == 0 )
		{
			base.OnMouseWheel( value );
			return;
		}

		// About the cursor, so what's under it stays there
		var at = AxisAt( m.x );
		var scale = MathF.Pow( 1.2f, Math.Clamp( value.y, -4, 4 ) );
		Place( at - (at - _goalFrom) * scale, at + (_goalTo - at) * scale );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );
		if ( e.MouseButton != MouseButtons.Left ) return;

		var m = e.LocalPosition / ScaleToScreen;
		_pressedAt = m;
		_pressedFrom = _goalFrom;
		_pressedTo = _goalTo;
		_dragged = false;
		_drag = m.y < OverviewH ? Drag.Overview : m.y < _tracksTop && m.y >= _rulerTop ? Drag.Measure : m.x >= PlotX ? Drag.Pan : Drag.None;
		if ( _drag == Drag.Overview ) CentreOn( m.x );
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );
		if ( _drag == Drag.None ) return;

		var m = e.LocalPosition / ScaleToScreen;
		if ( MathF.Abs( m.x - _pressedAt.x ) > 3 ) _dragged = true;
		if ( !_dragged ) return;

		switch ( _drag )
		{
			case Drag.Pan:
				var shift = (m.x - _pressedAt.x) / PlotW * (_pressedTo - _pressedFrom);
				Place( _pressedFrom - shift, _pressedTo - shift );
				(_from, _to) = (_goalFrom, _goalTo);
				break;
			case Drag.Measure:
				_measure = (ToMs( _pressedAt.x ), ToMs( Math.Clamp( m.x, PlotX, PlotX + PlotW ) ));
				break;
			case Drag.Overview:
				CentreOn( m.x );
				break;
		}
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );
		if ( _drag == Drag.Measure && !_dragged ) _measure = null;
		_drag = Drag.None;
	}

	/// <summary>
	/// Centre the view where the overview was clicked, keeping its width.
	/// </summary>
	void CentreOn( float x )
	{
		var (from, to) = Padded( OnAxis( FullExtent( DevTools.ShownGpuTimeline, Plan ) ) );
		var at = from + (x - PlotX) / PlotW * (to - from);
		var width = _goalTo - _goalFrom;
		Place( at - width / 2, at + width / 2 );
		(_from, _to) = (_goalFrom, _goalTo);
	}

	protected override void OnClick( MousePanelEvent e )
	{
		if ( _dragged ) return;

		var m = e.LocalPosition / ScaleToScreen;

		// The workers' label opens and closes their lanes
		if ( m.x < PlotX && m.y >= _workersTop && m.y < _tracksBottom && CpuInView( Plan ) )
		{
			_workersOpen = !_workersOpen;
			return;
		}

		// The folded CPU line brings it into view
		if ( HitAt( m ) is { Key: FitFrame } )
		{
			Fit( true );
			return;
		}

		if ( HitAt( m ) is { Key: { } key } )
		{
			DevTools.SelectedPass = DevTools.SelectedPass == key ? null : key;
			_selected = DevTools.SelectedPass;
			e.StopPropagation();
		}
	}

	protected override void OnDoubleClick( MousePanelEvent e )
	{
		var m = e.LocalPosition / ScaleToScreen;
		if ( m.y < _tracksTop ) return;

		if ( HitAt( m ) is { } hit && hit.Key is not null && DevTools.ShownGpuTimeline.Spans.FirstOrDefault( x => KeyOf( x ) == hit.Key ) is { Path: not null } span )
		{
			_back.Push( (_goalFrom, _goalTo) );
			var (start, end) = (U( span.StartMs ), U( span.EndMs ));
			var pad = MathF.Max( (end - start) * 0.15f, 0.002f );
			Place( start - pad, end + pad );
		}
		else if ( _back.TryPop( out var previous ) )
		{
			Place( previous.From, previous.To );
		}
		else
		{
			Fit( false );
		}
	}

	Hit? HitAt( Vector2 m )
	{
		for ( int i = _hits.Count - 1; i >= 0; i-- )
			if ( _hits[i].Rect.IsInside( m ) ) return _hits[i];
		return null;
	}

	public override void OnDraw( Painter painter )
	{
		var timeline = DevTools.ShownGpuTimeline;
		var plan = Plan;
		var scale = ScaleToScreen;
		_w = Box.Rect.Width / scale;
		var h = Box.Rect.Height / scale;
		if ( _w < 300 ) return;

		using var state = painter.Scope();
		painter.Scale( scale );
		painter.Stroke = Stroke.None;
		_hits.Clear();

		if ( timeline.Spans.Count == 0 || _to <= _from )
		{
			Text( painter, "Waiting for a GPU timeline", new Rect( PlotX, 0, PlotW, OverviewH ), TextFlag.LeftCenter, DevToolsStyles.Faint, 11, "Inter" );
			return;
		}

		var words = WordsFor( timeline, plan );
		var workers = words.Workers;
		DrawOverview( painter, timeline, plan );

		_rulerTop = OverviewH + 4;
		_tracksTop = _rulerTop + RulerH;
		var gfxTop = _tracksTop;
		var asyncTop = gfxTop + (timeline.MaxDepth + 1) * RowH + Gap;
		var cpuTop = (timeline.HasAsync ? asyncTop + (timeline.MaxAsyncDepth + 1) * RowH : asyncTop - Gap) + Separator;
		_workersTop = cpuTop + RowH * 2;
		_tracksBottom = plan is null ? cpuTop - Separator : cpuTop + CpuHeight( plan, workers.Count );

		painter.Fill = DevToolsStyles.Plot;
		painter.Rect( new Rect( PlotX, _tracksTop, PlotW, _tracksBottom - _tracksTop ) );

		DrawRuler( painter, timeline, words );

		// Track names in the gutter, and a line between the GPU's queues and the CPU
		Label( painter, "graphics", gfxTop, RowH, DevToolsStyles.Text );
		if ( timeline.HasAsync ) Label( painter, "async compute", asyncTop, RowH, DevToolsStyles.Text );
		if ( plan is not null )
		{
			painter.Fill = DevToolsStyles.Line;
			painter.Rect( new Rect( 0, cpuTop - Separator / 2, _w, 1 ) );
		}

		DrawSpans( painter, timeline, words, gfxTop, asyncTop );
		if ( plan is not null ) DrawCpu( painter, plan, words, cpuTop );
		DrawGap( painter, words, gfxTop );

		// What's measured, across every track
		if ( _measure is { } m )
		{
			var a = ToX( MathF.Min( m.From, m.To ) );
			var b = ToX( MathF.Max( m.From, m.To ) );
			painter.Fill = DevToolsStyles.Text.WithAlpha( 0.08f );
			painter.Rect( new Rect( a, _rulerTop, b - a, _tracksBottom - _rulerTop ) );
			painter.Fill = DevToolsStyles.Text.WithAlpha( 0.6f );
			painter.Rect( new Rect( a, _rulerTop, 1, _tracksBottom - _rulerTop ) );
			painter.Rect( new Rect( b, _rulerTop, 1, _tracksBottom - _rulerTop ) );
			var text = $"{DevToolsFormat.Ms( MathF.Abs( m.To - m.From ) )} ms";
			var tw = text.Length * 6.4f + 12;
			var box = new Rect( (a + b) / 2 - tw / 2, _rulerTop + 2, tw, RulerH - 4 );
			painter.Fill = Color.Parse( "#2a2a2a" ).Value;
			painter.Rect( box );
			Text( painter, text, box, TextFlag.Center, DevToolsStyles.Bright, 11 );
		}

		DrawLegend( painter, timeline, words, _tracksBottom + 6 );
		if ( HasHovered ) DrawHover( painter, h );
	}

	/// <summary>
	/// The whole frame small: the CPU's work in one band, the GPU's in another, the first submit marked, and the part in
	/// view outlined with the rest shaded. Clicking or dragging it moves the view.
	/// </summary>
	void DrawOverview( Painter painter, DevTools.GpuLayout timeline, ManagedFramePlan plan )
	{
		var (from, to) = Padded( OnAxis( FullExtent( timeline, plan ) ) );
		float X( float ms ) => PlotX + (U( ms ) - from) / MathF.Max( 0.0001f, to - from ) * PlotW;
		float XU( float u ) => PlotX + (u - from) / MathF.Max( 0.0001f, to - from ) * PlotW;

		painter.Fill = DevToolsStyles.Plot;
		painter.Rect( new Rect( PlotX, 0, PlotW, OverviewH ) );
		Label( painter, "frame", 0, OverviewH, DevToolsStyles.Dim );

		const float bandH = 9;
		var cpuY = 4f;
		var gpuY = OverviewH - 4 - bandH;

		if ( plan is not null )
		{
			// The main thread from start to submit, what it and the workers recorded over it
			painter.Fill = DevToolsStyles.Cpu.WithAlpha( 0.3f );
			painter.Rect( new Rect( X( (float)plan.StartMs ), cpuY, MathF.Max( 1, X( (float)(plan.SubmitStartMs + plan.SubmitMs) ) - X( (float)plan.StartMs ) ), bandH ) );
			foreach ( var segment in plan.Segments )
			{
				if ( segment.EndTicks == 0 ) continue;
				painter.Fill = SegmentColor( segment ).WithAlpha( 0.45f );
				painter.Rect( new Rect( X( (float)segment.StartMs ), cpuY, MathF.Max( 0.5f, X( (float)segment.EndMs ) - X( (float)segment.StartMs ) ), bandH ) );
			}
		}

		foreach ( var span in timeline.Spans )
		{
			if ( !span.Leaf ) continue;
			painter.Fill = DevToolsStyles.PassColor( span.Kind );
			var y = span.Async ? gpuY + bandH / 2 : gpuY;
			painter.Rect( new Rect( X( span.StartMs ), y, MathF.Max( 0.5f, X( span.EndMs ) - X( span.StartMs ) ), span.Async ? bandH / 2 : bandH / 2 + (timeline.HasAsync ? 0 : bandH / 2) ) );
		}

		// The gap, and the first submit
		if ( _gapShown > 0 ) DrawBreak( painter, X( _gapFrom ), X( _gapTo ), 0, OverviewH );
		painter.Fill = DevToolsStyles.Bright;
		painter.Rect( new Rect( X( 0 ) - 0.5f, 1, 1.5f, OverviewH - 2 ) );

		// What's in view
		var a = Math.Clamp( XU( _from ), PlotX, PlotX + PlotW );
		var b = Math.Clamp( XU( _to ), PlotX, PlotX + PlotW );
		painter.Fill = Color.Black.WithAlpha( 0.45f );
		painter.Rect( new Rect( PlotX, 0, a - PlotX, OverviewH ) );
		painter.Rect( new Rect( b, 0, PlotX + PlotW - b, OverviewH ) );
		painter.Stroke = Stroke.Solid( DevToolsStyles.Text.WithAlpha( 0.7f ), 1 );
		painter.Fill = Color.Transparent;
		painter.Rect( new Rect( a, 0.5f, MathF.Max( 2, b - a ), OverviewH - 1 ) );
		painter.Stroke = Stroke.None;
	}

	/// <summary>
	/// Ticks at a round step for the zoom, none in the elided gap, and the submits as notches along the bottom.
	/// </summary>
	void DrawRuler( Painter painter, DevTools.GpuLayout timeline, Words words )
	{
		var top = _rulerTop;
		painter.Fill = DevToolsStyles.Raised;
		painter.Rect( new Rect( PlotX, top, PlotW, RulerH ) );
		Label( painter, "ms", top, RulerH, DevToolsStyles.Faint );

		// Outside the gap a ms on the axis is a ms
		var px = PlotW / (_to - _from);
		ReadOnlySpan<float> steps = [0.001f, 0.002f, 0.005f, 0.01f, 0.02f, 0.05f, 0.1f, 0.2f, 0.5f, 1, 2, 5, 10, 20, 50];
		var step = steps[^1];
		foreach ( var st in steps ) { if ( st * px >= 70 ) { step = st; break; } }

		bool InGap( float t ) => _gapShown > 0 && t > _gapFrom && t < _gapTo;

		var decimals = step >= 1 ? "0" : step >= 0.1f ? "0.0" : step >= 0.01f ? "0.00" : "0.000";
		var end = T( _to );
		for ( var t = MathF.Floor( T( _from ) / step ) * step; t <= end; t += step )
		{
			// Minor ticks between
			painter.Fill = DevToolsStyles.Line;
			for ( int i = 1; i < 5; i++ )
			{
				var minor = t + i * step / 5;
				if ( !InGap( minor ) ) painter.Rect( new Rect( ToX( minor ), top + RulerH - 3, 1, 3 ) );
			}

			if ( InGap( t ) ) continue;
			var x = ToX( t );
			if ( x < PlotX - 1 ) continue;

			painter.Rect( new Rect( x, top + RulerH - 6, 1, 6 ) );
			painter.Fill = Color.White.WithAlpha( 0.03f );
			painter.Rect( new Rect( x, _tracksTop, 1, _tracksBottom - _tracksTop ) );
			Text( painter, MathF.Abs( t ) < step * 0.01f ? "0" : t.ToString( decimals ), new Rect( x + 3, top + 2, 70, RulerH - 8 ), TextFlag.LeftCenter, DevToolsStyles.Dim, 10 );
		}

		for ( int i = 0; i < timeline.Submits.Count; i++ )
		{
			var x = ToX( timeline.Submits[i] );
			if ( x < PlotX || x > PlotX + PlotW ) continue;
			var notch = new Rect( x - 2, top + RulerH - 7, 5, 7 );
			painter.Fill = DevToolsStyles.Bright;
			painter.Rect( new Rect( x - 0.5f, top + RulerH - 7, 2, 7 ) );
			var (title, detail) = words.Submit( i, timeline.Submits[i] );
			_hits.Add( new Hit( notch, null, title, detail ) );
		}
	}

	/// <summary>
	/// The elided gap: a narrow break down the ruler and every track, with how long it really is.
	/// </summary>
	void DrawGap( Painter painter, Words words, float gfxTop )
	{
		if ( _gapShown <= 0 ) return;

		var a = ToX( _gapFrom );
		var b = ToX( _gapTo );
		if ( b < PlotX || a > PlotX + PlotW ) return;

		DrawBreak( painter, a, b, _rulerTop, _tracksBottom - _rulerTop );

		var gap = _gapTo - _gapFrom;
		var text = words.GapLabel ??= $"{DevToolsFormat.Ms( gap )} ms";
		var tw = text.Length * 6.4f + 10;
		var box = new Rect( (a + b) / 2 - tw / 2, gfxTop + 2, tw, RowH - 4 );
		painter.Fill = Color.Parse( "#2a2a2a" ).Value;
		painter.Rect( box );
		Text( painter, text, box, TextFlag.Center, DevToolsStyles.Text, 10 );

		_hits.Add( new Hit( new Rect( MathF.Min( a, box.Left ), _rulerTop, MathF.Max( b - a, tw ), _tracksBottom - _rulerTop ), null,
			words.GapTitle ??= $"{gap:0.00} ms from submit to the GPU starting, not to scale",
			"Not captured - usually the GPU still on the frame before" ) );
	}

	/// <summary>
	/// A break in the axis: shaded, with an edge either side.
	/// </summary>
	static void DrawBreak( Painter painter, float a, float b, float top, float height )
	{
		painter.Fill = Color.Black.WithAlpha( 0.35f );
		painter.Rect( new Rect( a, top, MathF.Max( 1, b - a ), height ) );
		painter.Fill = DevToolsStyles.Faint;
		painter.Rect( new Rect( a, top, 1, height ) );
		painter.Rect( new Rect( b - 1, top, 1, height ) );
	}

	void DrawSpans( Painter painter, DevTools.GpuLayout timeline, Words words, float gfxTop, float asyncTop )
	{
		var selected = DevTools.SelectedPass;
		Rect? selectedRect = null;

		foreach ( var span in timeline.Spans )
		{
			var left = ToX( span.StartMs );
			var right = ToX( span.EndMs );
			if ( right < PlotX || left > PlotX + PlotW ) continue;

			var y = (span.Async ? asyncTop : gfxTop) + span.Depth * RowH;
			var rect = Clip( new Rect( left, y + 1, MathF.Max( 1, right - left - 1 ), RowH - 2 ) );
			var text = words.Span( span );
			var key = text.Key;

			// Leaves in the kind's colour; parents darker, so the hierarchy reads without the colour
			var color = DevToolsStyles.PassColor( span.Kind );
			painter.Fill = span.Leaf ? color : Darken( color, 0.45f );
			painter.Rect( rect );
			_hits.Add( new Hit( rect, key, span.Name, text.Detail ) );
			if ( key == selected ) selectedRect = rect;

			// The label sits in the visible part of the pass: the time goes first when it's short of room, then the name
			// shortens
			FitLabel( painter, text.Name, text.Label, rect, span.Leaf ? Ink : DevToolsStyles.Text );
		}

		if ( selectedRect is { } sr )
		{
			painter.Stroke = Stroke.Solid( DevToolsStyles.Bright, 1.5f );
			painter.Fill = Color.Transparent;
			painter.Rect( sr.Grow( 1 ) );
			painter.Stroke = Stroke.None;
		}
	}

	static readonly Color Ink = Color.Parse( "#161616" ).Value;

	static string Short( string name ) => name.StartsWith( "Managed: " ) ? name["Managed: ".Length..] : name;

	/// <summary>
	/// What a recording segment records, by the kind of the first layer in it, so the CPU reads in the GPU's colours.
	/// </summary>
	static Color SegmentColor( in ManagedFramePlan.Segment segment ) =>
		DevToolsStyles.PassColor( segment.Async ? DevTools.PassKind.Post : DevTools.KindOf( segment.First ?? "", DevTools.PassKind.Other ) );

	/// <summary>
	/// The main thread's phases back to back up to the submit; under them, what it recorded itself; then the workers -
	/// how many were recording what at each moment, in the GPU's colours, or a lane each when opened.
	/// </summary>
	/// <summary>
	/// What the folded CPU line does when clicked.
	/// </summary>
	const string FitFrame = "fit:frame";

	void DrawCpu( Painter painter, ManagedFramePlan plan, Words words, float top )
	{
		var workers = words.Workers;
		// Out of view, the CPU folds to a line that says where its work went, and brings it into view when clicked
		if ( !CpuInView( plan ) )
		{
			Label( painter, "cpu", top, RowH, DevToolsStyles.Text );
			var cpuEnd = CpuEnd( plan );
			var before = cpuEnd < T( _goalFrom );
			var text = before
				? words.CpuBefore ??= $"◂ main thread and {workers.Count} workers, {plan.StartMs:0.00} to {cpuEnd:0.00} ms · fit frame"
				: words.CpuAfter ??= $"main thread and {workers.Count} workers, from {plan.StartMs:0.00} ms · fit frame ▸";
			var line = new Rect( PlotX, top, PlotW, RowH );
			Text( painter, text, line.Shrink( 8, 0 ), before ? TextFlag.LeftCenter : TextFlag.RightCenter, DevToolsStyles.Dim, 10 );
			_hits.Add( new Hit( line, FitFrame, "The CPU making this frame", "Click to fit the whole frame" ) );
			return;
		}

		Label( painter, "main thread", top, RowH, DevToolsStyles.Text );
		Label( painter, "   recording", top + RowH, RowH, DevToolsStyles.Dim );
		Label( painter, _workersOpen ? $"▾ workers {workers.Count}" : $"▸ workers {workers.Count}", _workersTop, _workersOpen ? ThreadH : WorkerH, DevToolsStyles.Text );

		ReadOnlySpan<(string Name, double Ms, string Detail)> phases =
		[
			("Collect", plan.CollectMs, "Culling, and gathering what's in view"),
			("Prepare", plan.PrepareMs, "Lights, shadows and per-object data"),
			("Setup", plan.SetupMs, "Frame constants and resources"),
			("Record", plan.RecordMs, $"Recording command lists, {plan.RecordWaitMs:0.00} ms of it waiting on workers"),
			("Submit", plan.SubmitMs, "Handing the command lists to the GPU"),
		];

		var t = (float)plan.StartMs;
		var shade = 0;
		foreach ( var (name, ms, detail) in phases )
		{
			if ( name == "Submit" ) t = (float)plan.SubmitStartMs;

			var rect = Clip( new Rect( ToX( t ), top + 1, MathF.Max( 1, ToX( t + (float)ms ) - ToX( t ) - 1 ), RowH - 2 ) );
			t += (float)ms;
			if ( rect.Width <= 0 ) continue;

			painter.Fill = shade++ % 2 == 0 ? PhaseDark : PhaseLight;
			painter.Rect( rect );
			var label = words.Phase( name, ms );
			_hits.Add( new Hit( rect, null, label, detail ) );
			FitLabel( painter, name, label, rect, DevToolsStyles.Bright );
		}

		// What the main thread recorded itself, then each worker's, or how many were recording at once
		for ( int i = 0; i < plan.Segments.Count; i++ )
		{
			var segment = plan.Segments[i];
			if ( segment.EndTicks == 0 ) continue;

			if ( segment.Thread == plan.MainThread )
			{
				DrawSegment( painter, words, i, segment, top + RowH, RowH, "on the main thread" );
				continue;
			}

			var lane = workers.IndexOf( segment.Thread );
			if ( _workersOpen && lane >= 0 ) DrawSegment( painter, words, i, segment, _workersTop + lane * ThreadH, ThreadH, "on a worker" );
		}

		if ( !_workersOpen ) DrawWorkload( painter, plan, words );
	}

	static readonly Color PhaseDark = Color.Parse( "#5c5c5c" ).Value, PhaseLight = Color.Parse( "#7a7a7a" ).Value;

	/// <summary>
	/// One recording segment in its lane: the kind's colour, named by the first layer it records.
	/// </summary>
	void DrawSegment( Painter painter, Words words, int index, in ManagedFramePlan.Segment segment, float y, float h, string where )
	{
		var rect = Clip( new Rect( ToX( (float)segment.StartMs ), y + 1, MathF.Max( 1, ToX( (float)segment.EndMs ) - ToX( (float)segment.StartMs ) - 1 ), h - 2 ) );
		if ( rect.Width <= 0 ) return;

		painter.Fill = SegmentColor( segment );
		painter.Rect( rect );
		var (title, detail, label) = words.Segment( index, segment, where );
		_hits.Add( new Hit( rect, null, title, detail ) );
		FitLabel( painter, label, null, rect, Ink, h < RowH ? 10 : 10.5f );
	}

	/// <summary>
	/// How many workers were recording at each moment, stacked by what they were recording.
	/// </summary>
	void DrawWorkload( Painter painter, ManagedFramePlan plan, Words words )
	{
		var workers = words.Workers;
		var steps = words.Workload;
		if ( steps.Count == 0 ) return;

		var bandTop = _workersTop + 2;
		var bandH = WorkerH - 4;
		var unit = bandH / MathF.Max( 1, workers.Count );
		var peak = 0;

		foreach ( var (from, to, colors) in steps )
		{
			peak = Math.Max( peak, colors.Length );
			var rect = Clip( new Rect( ToX( from ), 0, ToX( to ) - ToX( from ), 0 ) );
			if ( rect.Width <= 0 ) continue;

			var y = bandTop + bandH;
			foreach ( var color in colors )
			{
				y -= unit;
				painter.Fill = color;
				painter.Rect( new Rect( rect.Left, y, rect.Width, unit ) );
			}
		}

		var all = Clip( new Rect( ToX( steps[0].From ), bandTop, ToX( steps[^1].To ) - ToX( steps[0].From ), bandH ) );
		if ( all.Width > 0 )
			_hits.Add( new Hit( all, null, words.WorkersTitle ??= $"Workers recording: up to {peak} of {workers.Count} at once",
				words.WorkersDetail ??= $"{plan.WorkerMs:0.00} ms between them. Click the label for a lane each" ) );
	}

	/// <summary>
	/// Each kind's colour and its GPU time - its leaves' time added up.
	/// </summary>
	void DrawLegend( Painter painter, DevTools.GpuLayout timeline, Words words, float y )
	{
		var x = PlotX;
		foreach ( var kind in Enum.GetValues<DevTools.PassKind>() )
		{
			var ms = words.KindMs( kind );
			if ( ms <= 0 ) continue;

			var text = words.Legend( kind, ms );
			painter.Fill = DevToolsStyles.PassColor( kind );
			painter.Rect( new Rect( x, y + LegendH / 2 - 4, 8, 8 ) );
			Text( painter, text, new Rect( x + 13, y, text.Length * 6.4f + 4, LegendH ), TextFlag.LeftCenter, DevToolsStyles.Dim, 10 );
			x += text.Length * 6.4f + 28;
		}
	}

	/// <summary>
	/// A line down every track at the cursor with its time on the ruler, and what's under it.
	/// </summary>
	void DrawHover( Painter painter, float h )
	{
		var m = MousePosition / ScaleToScreen;
		if ( m.x < PlotX || m.x > PlotX + PlotW || m.y < _rulerTop || m.y > _tracksBottom ) return;

		painter.Fill = DevToolsStyles.Text.WithAlpha( 0.3f );
		painter.Rect( new Rect( m.x, _rulerTop, 1, _tracksBottom - _rulerTop ) );

		var at = $"{ToMs( m.x ):0.000}";
		var aw = at.Length * 6.4f + 10;
		var atBox = new Rect( m.x - aw / 2, _rulerTop + 1, aw, RulerH - 8 );
		painter.Fill = Color.Parse( "#2a2a2a" ).Value;
		painter.Rect( atBox );
		Text( painter, at, atBox, TextFlag.Center, DevToolsStyles.Bright, 10 );

		if ( HitAt( m ) is not { } hit ) return;

		painter.Stroke = Stroke.Solid( DevToolsStyles.Bright.WithAlpha( 0.7f ), 1 );
		painter.Fill = Color.Transparent;
		painter.Rect( hit.Rect );
		painter.Stroke = Stroke.None;

		var boxW = MathF.Max( hit.Title.Length * 6.6f, hit.Detail.Length * 6.1f ) + 16;
		var box = new Rect( MathF.Min( m.x + 14, _w - boxW - 4 ), MathF.Min( m.y + 14, h - 38 ), boxW, 36 );
		painter.Fill = Color.Parse( "#1c1c1c" ).Value;
		painter.Stroke = Stroke.Solid( DevToolsStyles.Line, 1 );
		painter.Rect( box );
		painter.Stroke = Stroke.None;
		Text( painter, hit.Title, new Rect( box.Left + 8, box.Top + 3, box.Width - 16, 15 ), TextFlag.LeftCenter, DevToolsStyles.Bright, 11, "Inter" );
		Text( painter, hit.Detail, new Rect( box.Left + 8, box.Top + 18, box.Width - 16, 15 ), TextFlag.LeftCenter, DevToolsStyles.Dim, 10 );
	}

	/// <summary>
	/// A rect cut to the plot, so nothing draws over the gutter.
	/// </summary>
	Rect Clip( Rect r )
	{
		var left = MathF.Max( r.Left, PlotX );
		var right = MathF.Min( r.Right, PlotX + PlotW );
		return new Rect( left, r.Top, MathF.Max( 0, right - left ), r.Height );
	}

	static Color Darken( Color c, float by ) => new( c.r * (1 - by), c.g * (1 - by), c.b * (1 - by), c.a );

	/// <summary>
	/// A label inside a bar: the name with its time (<paramref name="withMs"/>) if it fits, then the name alone, then the
	/// name cut short - and nothing once fewer than a few letters would show.
	/// </summary>
	static void FitLabel( Painter painter, string name, string withMs, Rect rect, Color ink, float size = 10.5f )
	{
		var charW = size * 0.64f;
		const float Pad = 8;
		var room = rect.Width - Pad;
		if ( room < charW * 4 ) return;

		string text;
		if ( withMs is not null && withMs.Length * charW <= room ) text = withMs;
		else if ( name.Length * charW <= room ) text = name;
		else
		{
			var chars = (int)(room / charW) - 1;
			if ( chars < 3 ) return;
			text = name[..chars].TrimEnd() + "…";
		}

		Text( painter, text, new Rect( rect.Left + 4, rect.Top, rect.Width - 6, rect.Height ), TextFlag.LeftCenter, ink, size, "Inter" );
	}

	static void Label( Painter painter, string text, float y, float h, Color color ) =>
		Text( painter, text, new Rect( 10, y, Gutter - 16, h ), TextFlag.LeftCenter, color, 11, "Inter" );

	static void Text( Painter painter, string text, Rect rect, TextFlag flags, Color color, float size = 11, string font = "Roboto Mono" )
	{
		var scope = new TextRendering.Scope( text, color, size, font, 500 );
		DebugOverlay.DrawText( painter, scope, rect, flags );
	}
}
