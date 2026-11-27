using Sandbox.UI;

namespace Sandbox.Diagnostics;

internal sealed class DevToolsProfilerHistogram : Panel
{
	internal Profiler.Distribution Data { get; private set; } = Profiler.DescribeDurations( [] );
	internal Action<double, double> Selected;
	internal bool LogCounts { get; set; }
	int _selected = -1;
	const float PlotLeft = 44, PlotTop = 16, PlotBottom = 44, RightPadding = 12;
	Rect Plot => new( PlotLeft, PlotTop, Math.Max( 0, Box.Rect.Width / ScaleToScreen - PlotLeft - RightPadding ), PlotBottom - PlotTop );

	internal DevToolsProfilerHistogram()
	{
		AddClass( "profiler-histogram" );
		Style.Height = 64;
	}

	internal void SetData( IEnumerable<double> values ) { Data = Profiler.DescribeDurations( values ); _selected = -1; }
	internal void ClearSelection() => _selected = -1;

	protected override void OnClick( MousePanelEvent e )
	{
		var plot = Plot;
		var point = e.LocalPosition / ScaleToScreen;
		if ( plot.Width <= 0 || point.x < plot.Left || point.x >= plot.Right || point.y < plot.Top || point.y > plot.Bottom || Data.Count == 0 ) return;
		_selected = Math.Clamp( (int)((point.x - plot.Left) / plot.Width * Data.Bins.Length), 0, Data.Bins.Length - 1 );
		Selected?.Invoke( _selected * Data.BinWidthMs, _selected == Data.Bins.Length - 1 ? double.PositiveInfinity : (_selected + 1) * Data.BinWidthMs );
	}

	public override void OnDraw( Painter painter )
	{
		using var state = painter.Scope();
		painter.Scale( ScaleToScreen );
		painter.Stroke = Stroke.None;
		var plot = Plot;
		if ( plot.Width <= 0 ) return;
		if ( Data.Count == 0 )
		{
			DrawText( painter, "No complete instances recorded", new Rect( 6, 0, plot.Right, 64 ) );
			Tooltip = "Unfinished instances are excluded from duration statistics";
			return;
		}
		DrawText( painter, LogCounts ? "Instances (log)" : "Instances", new Rect( PlotLeft, 0, 90, 16 ) );
		var max = Math.Max( 1, Data.Bins.Max() );
		float Fraction( int count ) => (float)(LogCounts ? Math.Log2( count + 1 ) / Math.Log2( max + 1 ) : (double)count / max);
		void CountTick( Painter painter, int count )
		{
			var y = plot.Bottom - Fraction( count ) * plot.Height;
			painter.Fill = DevToolsStyles.Line;
			painter.Rect( new Rect( plot.Left, y, plot.Width, 1 ) );
			DrawText( painter, $"{count:N0}", new Rect( 0, y - 7, PlotLeft - 6, 14 ), TextFlag.RightCenter );
		}
		CountTick( painter, 0 );
		var middle = (int)Math.Round( LogCounts ? Math.Sqrt( max + 1 ) - 1 : max / 2.0 );
		if ( middle > 0 && middle < max ) CountTick( painter, middle );
		CountTick( painter, max );
		var barWidth = plot.Width / Data.Bins.Length;
		for ( int i = 0; i < Data.Bins.Length; i++ )
		{
			var height = Fraction( Data.Bins[i] ) * plot.Height;
			painter.Fill = i == _selected ? DevToolsStyles.Warning : DevToolsStyles.Series[1];
			painter.Rect( new Rect( plot.Left + i * barWidth, plot.Bottom - height, Math.Max( 0.5f, barWidth - 1 ), height ) );
		}
		var duration = Data.BinWidthMs * Data.Bins.Length;
		var ticks = plot.Width < 360 ? 2 : 4;
		for ( int i = 0; i <= ticks; i++ )
		{
			var x = plot.Left + plot.Width * i / ticks;
			var value = duration * i / ticks;
			var label = $"{(duration < 1 ? value * 1000 : value):0.##}";
			if ( i == ticks ) label += duration < 1 ? " µs" : " ms";
			var rect = i == 0 ? new Rect( x, plot.Bottom + 2, 72, 20 )
				: i == ticks ? new Rect( x - 72, plot.Bottom + 2, 72, 20 )
				: new Rect( x - 36, plot.Bottom + 2, 72, 20 );
			DrawText( painter, label, rect, i == 0 ? TextFlag.LeftCenter : i == ticks ? TextFlag.RightCenter : TextFlag.Center );
		}
		if ( !HasHovered ) return;
		var point = MousePosition / ScaleToScreen;
		if ( point.x < plot.Left || point.x >= plot.Right || point.y < plot.Top || point.y > plot.Bottom )
		{
			Tooltip = "Scope duration on the horizontal axis; number of instances on the vertical axis. Click a bar to filter instances.";
			return;
		}
		var index = Math.Clamp( (int)((point.x - plot.Left) / plot.Width * Data.Bins.Length), 0, Data.Bins.Length - 1 );
		Tooltip = $"{Duration( index * Data.BinWidthMs )}–{Duration( (index + 1) * Data.BinWidthMs )}\n{Data.Bins[index]:N0} instances · click to filter";
	}
	internal static string Duration( double ms ) => ms < 1 ? $"{ms * 1000:0.###} µs" : $"{ms:0.###} ms";

	void DrawText( Painter painter, string text, Rect rect, TextFlag alignment = TextFlag.LeftCenter ) => DebugOverlay.DrawText( painter,
		new TextRendering.Scope( text, DevToolsStyles.Dim, 10 / ScaleToScreen, "Inter", 500 ), rect, alignment );
}

/// <summary>
/// One bar per frame across the whole capture, with the timeline's range marked: frame time, or with
/// <c>memory</c> the main thread's allocations per frame. GC pauses sit on a rail along the top.
/// </summary>
internal sealed class DevToolsProfilerOverview : Panel
{
	const float Height = 48, RailHeight = 6;
	readonly DevToolsProfilerTimeline _timeline;
	readonly bool _memory;
	readonly List<(Rect Rect, Profiler.Frame Frame)> _hits = new();
	Profiler.Capture _capture;
	Profiler.Distribution _stats;
	(int Count, double Ms, int Worst)[] _frameGc = [];
	string _summary, _label, _tooltip;
	float _top;
	int _tooltipFrame = -2;
	float? _dragStart;
	float _dragEnd;
	bool _dragged;

	internal DevToolsProfilerOverview( DevToolsProfilerTimeline timeline, bool memory = false )
	{
		_timeline = timeline;
		_memory = memory;
		AddClass( "overlay-view profiler-frame-overview" );
		Style.Height = Height;
		if ( memory ) TooltipClass = "profiler-scope-tooltip";
	}

	internal void SetCapture( Profiler.Capture capture )
	{
		_capture = capture;
		_stats = Profiler.DescribeDurations( capture.Frames.Where( x => !x.Incomplete ).Select( x => x.DurationMs ) );
		_summary = _stats.Count > 0 ? $"FRAMES  {_stats.Count:N0} complete · median {_stats.P50Ms:0.00} ms · p95 {_stats.P95Ms:0.00} ms · max {_stats.MaxMs:0.00} ms"
			: capture.Frames.Length > 0 ? "Frames · no complete frames recorded" : "Frames · no frame scopes recorded";
		_frameGc = !_memory ? [] : capture.Frames.Select( x => DevToolsProfilerGc.Overlap( capture, x.StartMs, x.EndMs ) ).ToArray();
		var gc = DevToolsProfilerGc.Summary( capture );
		if ( gc.Length > 0 ) _summary += $" · {gc}";
		else if ( !capture.HasGcData ) _summary += " · no GC data";
		_top = 0;
		_tooltipFrame = -2;
		_dragStart = null;
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		if ( e.MouseButton != MouseButtons.Left || _capture is null ) return;
		_dragStart = e.LocalPosition.x / ScaleToScreen;
		_dragEnd = _dragStart.Value;
		_dragged = false;
	}
	protected override void OnMouseMove( MousePanelEvent e )
	{
		if ( _dragStart is null ) return;
		_dragEnd = e.LocalPosition.x / ScaleToScreen;
	}
	protected override void OnMouseUp( MousePanelEvent e )
	{
		if ( e.MouseButton != MouseButtons.Left || _dragStart is not { } start ) return;
		_dragStart = null;
		var end = e.LocalPosition.x / ScaleToScreen;
		_dragged = Math.Abs( start - end ) > 4;
		if ( !_dragged || _capture is null ) return;
		var width = Math.Max( 1, Box.Rect.Width / ScaleToScreen );
		_timeline.SetRange( Math.Min( start, end ) / width * _capture.DurationMs, Math.Max( start, end ) / width * _capture.DurationMs );
	}
	protected override void OnClick( MousePanelEvent e )
	{
		if ( _dragged ) { _dragged = false; return; }
		var point = e.LocalPosition / ScaleToScreen;
		foreach ( var hit in _hits )
			if ( hit.Rect.IsInside( point ) ) { _timeline.SetRange( hit.Frame.StartMs, hit.Frame.EndMs ); break; }
	}

	public override void OnDraw( Painter painter )
	{
		using var state = painter.Scope();
		painter.Scale( ScaleToScreen );
		painter.Stroke = Stroke.None;
		_hits.Clear();
		var width = Box.Rect.Width / ScaleToScreen;
		if ( _capture is null || _capture.DurationMs <= 0 || width < 1 ) return;
		var mouse = MousePosition / ScaleToScreen;
		painter.Fill = DevToolsStyles.Plot;
		painter.Rect( new Rect( 0, 0, width, Height ) );

		// The memory strip keeps room along the top for its scale label
		var barHeight = _memory ? Height - RailHeight - 2 : Height - 2;
		var top = _memory ? MemoryScale() : (float)Math.Max( 16.667, _stats.MaxMs );
		var labelWidth = _memory && _label is not null ? Math.Min( width - 4, _label.Length * 6 + 8 ) : 0;
		if ( _memory )
		{
			painter.Fill = DevToolsStyles.Line;
			painter.Rect( new Rect( labelWidth + 4, Height - barHeight, Math.Max( 0, width - labelWidth - 4 ), 1 ) );
		}
		for ( int i = 0; i < _capture.Frames.Length; i++ )
		{
			var frame = _capture.Frames[i];
			var x = (float)(frame.StartMs / _capture.DurationMs * width);
			var w = Math.Max( 1, (float)(frame.DurationMs / _capture.DurationMs * width) );
			_hits.Add( (new Rect( x, 0, w, Height ), frame) );
			var value = _memory ? frame.AllocatedBytes ?? 0 : frame.DurationMs;
			if ( value <= 0 ) continue;
			var h = (float)Math.Clamp( value / top * barHeight, 1, barHeight );
			var hovered = HasHovered && mouse.x >= x && mouse.x < x + w;
			painter.Fill = _memory ? DevToolsStyles.Cpu.WithAlpha( hovered ? 1 : 0.8f )
				: (frame.DurationMs > 33.334 ? DevToolsStyles.Bad : frame.DurationMs > 16.667 ? DevToolsStyles.Warning : DevToolsStyles.Good).WithAlpha( frame.Incomplete ? 0.3f : 0.8f );
			painter.Rect( new Rect( x, Height - h, Math.Max( 0.75f, w - 1 ), h ) );
		}
		// The label's backing goes under the range mask, so it darkens with it; the text goes on top
		if ( _memory && _label is not null )
		{
			painter.Fill = DevToolsStyles.Plot;
			painter.Rect( new Rect( 2, RailHeight, labelWidth, 14 ) );
		}

		var left = (float)(_timeline.RangeStart / _capture.DurationMs * width);
		var right = (float)(_timeline.RangeEnd / _capture.DurationMs * width);
		painter.Fill = Color.Black.WithAlpha( 0.6f );
		painter.Rect( new Rect( 0, 0, left, Height ) );
		painter.Rect( new Rect( right, 0, Math.Max( 0, width - right ), Height ) );
		painter.Fill = Color.White.WithAlpha( 0.7f );
		painter.Rect( new Rect( left, 0, 1, Height ) );
		painter.Rect( new Rect( right - 1, 0, 1, Height ) );
		if ( _memory && _label is not null )
		{
			var label = new Rect( 2, RailHeight, labelWidth, 14 );
			DebugOverlay.DrawText( painter, new TextRendering.Scope( _label, DevToolsStyles.Dim, 10 / ScaleToScreen, "Inter", 500 ), label.Shrink( 4, 0 ), TextFlag.LeftCenter );
		}
		if ( _dragStart is { } start )
		{
			painter.Fill = Color.White.WithAlpha( 0.2f );
			painter.Rect( new Rect( Math.Min( start, _dragEnd ), 0, Math.Abs( start - _dragEnd ), Height ) );
		}
		if ( !HasHovered ) return;
		var found = -1;
		for ( int i = _hits.Count - 1; i >= 0; i-- ) if ( _hits[i].Rect.IsInside( mouse ) ) { found = i; break; }
		if ( found != _tooltipFrame ) { _tooltipFrame = found; _tooltip = found < 0 ? null : FrameTooltip( found ); }
		Tooltip = _tooltip ?? (_memory ? "Main thread allocations per frame across the whole capture. Click a frame to zoom, or drag to select a range." : $"{_summary}\n0–{_capture.DurationMs:0} ms · Drag to select a time range");
	}

	// Rescales only when the visible range needs it, to a round number
	float MemoryScale()
	{
		long largest = 64 * 1024;
		foreach ( var frame in _capture.Frames )
			if ( frame.AllocatedBytes is { } bytes && frame.EndMs >= _timeline.RangeStart && frame.StartMs <= _timeline.RangeEnd ) largest = Math.Max( largest, bytes );
		// Held until the visible frames need under half of it, then two significant figures in KiB or MiB
		if ( _top > 0 && largest <= _top && largest >= _top * 0.5f ) return _top;
		var mib = largest >= 1024 * 1024;
		var unit = mib ? 1024f * 1024 : 1024f;
		var value = largest / unit;
		var power = MathF.Pow( 10, MathF.Floor( MathF.Log10( value ) ) - 1 );
		value = MathF.Ceiling( value / power ) * power;
		_top = value * unit;
		_label = $"{value:0.#} {(mib ? "MiB" : "KiB")} · main thread per frame";
		return _top;
	}

	string FrameTooltip( int index )
	{
		var frame = _capture.Frames[index];
		var head = _memory
			? $"Frame {frame.Index + 1} · {(frame.AllocatedBytes is { } bytes ? DevToolsFormat.Bytes( bytes ) : "not measured")} on the main thread · {frame.DurationMs:0.000} ms"
			: $"Frame {frame.Index + 1} · {frame.DurationMs:0.000} ms{(frame.Incomplete ? " · unfinished" : "")}";
		var gc = DevToolsProfilerGc.OverlapLine( _capture, frame.StartMs, frame.EndMs );
		if ( gc.Length == 0 && _memory )
		{
			for ( int i = index + 1; i < Math.Min( _frameGc.Length, index + 11 ); i++ )
			{
				if ( _frameGc[i].Count == 0 ) continue;
				gc = $"\nGC pause {i - index} frame{(i - index == 1 ? "" : "s")} later ({DevToolsProfilerGc.SeverityName( _frameGc[i].Worst )}, {_frameGc[i].Ms:0.000} ms)";
				break;
			}
		}
		return $"{head}{gc}\nClick to zoom, or drag to select a range.{(_memory ? "" : " Colours mark 16.7 / 33.3 ms.")}";
	}
}

/// <summary>
/// Allocations for the timeline's visible range: a per-frame strip over the whole capture, then the sampled
/// allocations by scope and by type. Follows the range once it has been still for a moment.
/// </summary>
internal sealed class DevToolsProfilerMemory : Panel
{
	const string Columns = "minmax(0, 1fr) minmax(0, 0.6fr) 56px 64px";
	const int ShortRows = 6, LongRows = 30;
	readonly DevToolsProfilerTimeline _timeline;
	readonly Label _aside, _whole, _hint, _tablesHint;
	readonly DevToolsProfilerOverview _strip;
	readonly Panel _tables;
	readonly DevToolsTable _scopes, _types;
	Profiler.Capture _capture;
	IReadOnlyDictionary<uint, Profiler.Site> _sites;
	(double Start, double End) _range, _shown = (-1, -1);
	RealTimeSince _sinceRangeChange;
	uint _shownSelection;
	bool _expanded;

	internal DevToolsProfilerMemory( DevToolsProfilerTimeline timeline, DevToolsSection section )
	{
		_timeline = timeline;
		_aside = section.Aside;
		_aside.Tooltip = "Main thread numbers are exact, counted at frame boundaries. All-thread numbers and the tables are estimates from samples the runtime takes about every 100 KB, charged to the innermost open scope.";
		_whole = section.Head.AddChild( new Label( "Whole capture", "profiler-capture-button hidden" ) );
		_whole.AddEventListener( "onclick", () => { if ( _capture is not null ) _timeline.SetRange( 0, _capture.DurationMs ); } );
		section.Head.SetChildIndex( _whole, 1 );
		section.Body.AddChild( this );
		AddClass( "profiler-memory" );
		_hint = AddChild( new Label( "Record a capture to see allocations", "hint" ) );
		_strip = AddChild( new DevToolsProfilerOverview( timeline, memory: true ) );
		_tablesHint = AddChild( new Label( "", "hint hidden" ) );
		_tables = AddChild( new Panel( null, "profiler-memory-tables" ) );
		_scopes = _tables.AddChild( new DevToolsTable( Columns, "Scope", "Top type", "#≈ MiB", "" ) { TooltipClass = "profiler-scope-tooltip" } );
		_types = _tables.AddChild( new DevToolsTable( Columns, "Type", "Top scope", "#≈ MiB", "" ) { TooltipClass = "profiler-scope-tooltip" } );
		_types.AddClass( "profiler-memory-types" );
		Show( false, false, false );
	}

	void Show( bool strip, bool tables, bool hint )
	{
		_strip.SetClass( "hidden", !strip );
		_tables.SetClass( "hidden", !tables );
		_tablesHint.SetClass( "hidden", !hint );
		_hint.SetClass( "hidden", _capture is not null );
	}

	internal void SetCapture( Profiler.Capture capture, IReadOnlyDictionary<uint, Profiler.Site> sites )
	{
		_capture = capture;
		_sites = sites;
		_strip.SetCapture( capture );
		_shown = (-1, -1);
		_expanded = false;
	}

	public override void Tick()
	{
		base.Tick();
		if ( _capture is null )
		{
			_hint.Text = Profiler.RecordingId != 0 ? "Recording… allocations appear when the capture finishes" : "Record a capture to see allocations";
			return;
		}
		var range = (_timeline.RangeStart, _timeline.RangeEnd);
		if ( range != _range ) { _range = range; _sinceRangeChange = 0; }
		if ( (_shown != _range && (_shown.Start < 0 || _sinceRangeChange > 0.15f)) || _shownSelection != _timeline.SelectedSite ) Refresh();
	}

	void Refresh()
	{
		_shown = _range;
		_shownSelection = _timeline.SelectedSite;
		var capture = _capture;
		var measured = capture.Frames.Any( x => x.AllocatedBytes is not null );
		var sampled = capture.Allocations.Length > 0;
		if ( !measured && !sampled )
		{
			_aside.Text = "no allocation data in this capture";
			_aside.Tooltip = capture.HasGcData ? null : "Streamed files carry scopes only, without allocations.";
			_whole.SetClass( "hidden", true );
			Show( false, false, false );
			return;
		}

		// Samples saved without times can only describe the whole capture
		var whole = (_range.Start <= 0 && _range.End >= capture.DurationMs - 0.001) || !Profiler.AllocationsHaveTimes( capture );
		var (start, end) = whole ? (double.NegativeInfinity, double.PositiveInfinity) : _range;
		_whole.SetClass( "hidden", _range.Start <= 0 && _range.End >= capture.DurationMs - 0.001 );
		var samples = Profiler.AllocationsIn( capture, start, end ).ToArray();
		var mainBytes = capture.Frames.Where( x => x.AllocatedBytes is not null && x.StartMs >= _range.Start - 0.001 && x.EndMs <= _range.End + 0.001 ).ToArray();
		_aside.Text = Summary( capture, whole, mainBytes, samples.Sum( x => x.Bytes ) );
		if ( capture.AllocationCutoffMs is { } cutoff ) _aside.Text += $" · samples stop at {cutoff:0} ms (sample limit)";
		_aside.Tooltip = "Main thread numbers are exact, counted at frame boundaries. All-thread numbers and the tables are estimates from samples the runtime takes about every 100 KB, charged to the innermost open scope.";

		var scopes = Profiler.AllocationsByScope( capture, start, end );
		var types = Profiler.AllocationsByType( capture, start, end );
		_tablesHint.Text = !sampled ? "Nothing sampled. Samples are taken about every 100 KB."
			: mainBytes.Length == 0 ? "No samples in this range. Samples are taken about every 100 KB; widen the range."
			: $"No samples in this range (main thread allocated {DevToolsFormat.Bytes( mainBytes.Sum( x => x.AllocatedBytes.Value ) )}). Samples are taken about every 100 KB; widen the range.";
		Show( measured, scopes.Length > 0, scopes.Length == 0 );
		if ( scopes.Length == 0 ) return;

		var total = (double)scopes.Sum( x => x.Bytes );
		var limit = _expanded ? LongRows : ShortRows;
		var shownScopes = scopes.Take( limit ).ToList();
		if ( _timeline.SelectedSite != 0 && scopes.Skip( limit ).FirstOrDefault( x => x.ScopeId == _timeline.SelectedSite ) is { } selected ) shownScopes.Add( selected );
		var shownTypes = types.Take( limit ).ToArray();
		var names = ShortNames( shownScopes.Select( x => x.Type ).Concat( shownTypes.Select( x => x.Type ) ) );

		_scopes.Begin();
		int row = 0;
		foreach ( var scope in shownScopes )
		{
			var inScope = samples.Where( x => x.ScopeId == scope.ScopeId ).ToArray();
			var topTypes = string.Join( "\n", inScope.GroupBy( x => x.Type ).OrderByDescending( x => x.Sum( y => y.Bytes ) ).Take( 3 ).Select( x => $"  {names( x.Key )} ≈{Mib( x.Sum( y => y.Bytes ) )} MiB" ) );
			var threads = inScope.GroupBy( x => x.ThreadKey ).OrderByDescending( x => x.Sum( y => y.Bytes ) ).Select( x => x.Key ).ToArray();
			var threadText = string.Join( ", ", threads.Take( 4 ).Select( ThreadName ) ) + (threads.Length > 4 ? $" and {threads.Length - 4} more" : "");
			var amount = new DevToolsTable.Cell( Mib( scope.Bytes ), scope.Samples < 3 ? "number dim" : "number" );
			if ( !_sites.TryGetValue( scope.ScopeId, out var site ) )
			{
				_scopes.Row( $"scope:{row++}", [new( $"No scope · {threads.Length} thread{(threads.Length == 1 ? "" : "s")}", "dim", DevToolsStyles.Faint ), DevToolsTable.Note( names( scope.Type ) ), amount, DevToolsTable.BarOf( scope.Bytes / total )],
					tooltip: $"Allocations outside any scope · ≈{Mib( scope.Bytes )} MiB from {scope.Samples:N0} samples\n{topTypes}\n{threadText}" );
				continue;
			}
			_scopes.Row( $"scope:{row++}", [new( site.Name, Swatch: DevToolsProfilerTimeline.SiteColor( site ) ), DevToolsTable.Note( names( scope.Type ) ), amount, DevToolsTable.BarOf( scope.Bytes / total )],
				() => _timeline.InspectSite( site.Id ),
				$"{site.Name} · ≈{Mib( scope.Bytes )} MiB from {scope.Samples:N0} samples\n{topTypes}\n{threadText}\n{Profiler.SourceLocation( site )}\nClick to inspect in the timeline",
				site.Id == _timeline.SelectedSite ? "selected" : null );
		}
		Rollup( _scopes, scopes.Length, limit, scopes.Skip( limit ).Sum( x => x.Bytes ) );
		_scopes.End();

		_types.Begin();
		row = 0;
		foreach ( var type in shownTypes )
		{
			var topScopes = samples.Where( x => x.Type == type.Type ).GroupBy( x => x.ScopeId ).OrderByDescending( x => x.Sum( y => y.Bytes ) ).Take( 3 ).ToArray();
			var scopeText = string.Join( "\n", topScopes.Select( x => $"  {ScopeName( x.Key )} ≈{Mib( x.Sum( y => y.Bytes ) )} MiB" ) );
			_types.Row( $"type:{row++}", [names( type.Type ), DevToolsTable.Note( ScopeName( topScopes[0].Key ) ), new( Mib( type.Bytes ), type.Samples < 3 ? "number dim" : "number" ), DevToolsTable.BarOf( type.Bytes / total )],
				tooltip: $"{type.Type}\n≈{Mib( type.Bytes )} MiB from {type.Samples:N0} samples\n{scopeText}" );
		}
		Rollup( _types, types.Length, limit, types.Skip( limit ).Sum( x => x.Bytes ) );
		_types.End();
	}

	void Rollup( DevToolsTable table, int count, int limit, long rest )
	{
		if ( count <= ShortRows ) return;
		var text = _expanded ? "Show fewer" : $"+ {count - limit:N0} more · {Mib( rest )} MiB";
		table.Row( "rollup", [text], () => { _expanded = !_expanded; Refresh(); }, rowClass: "group" );
	}

	string Summary( Profiler.Capture capture, bool whole, Profiler.Frame[] frames, long sampled )
	{
		var all = $"all threads ≈{Mib( sampled )} MiB";
		var pauses = whole ? DevToolsProfilerGc.Summary( capture ) : DevToolsProfilerGc.Summary( capture, _range.Start, _range.End );
		var gc = pauses.Length > 0 ? $" · {pauses}" : "";
		if ( whole )
		{
			var stats = Profiler.FrameAllocationStats( capture );
			var main = stats.Count > 0 ? $"main thread {DevToolsFormat.Bytes( stats.P50Ms )}/frame median, {DevToolsFormat.Bytes( stats.P95Ms )} p95, {DevToolsFormat.Bytes( stats.MaxMs )} max · " : "";
			return $"whole capture · {main}{all}{gc}";
		}
		var bytes = DevToolsFormat.Bytes( frames.Sum( x => x.AllocatedBytes.Value ) );
		return frames.Length switch
		{
			0 => $"{_range.Start:0.000}–{_range.End:0.000} ms · {all}{gc}",
			1 => $"frame {frames[0].Index + 1} · main thread {bytes} · {all}{gc}",
			_ => $"{frames.Length} frames, {frames[0].StartMs:0.0}–{frames[^1].EndMs:0.0} ms · main thread {bytes} · {all}{gc}",
		};
	}

	static string Mib( long bytes ) => $"{bytes / (1024.0 * 1024.0):0.0}";
	string ThreadName( ulong key ) => key == 0 ? "threads without scopes" : _timeline.ThreadLabel( key );
	string ScopeName( uint id ) => _sites.TryGetValue( id, out var site ) ? site.Name : "No scope";

	// Short type names, unless two shown rows would read the same
	static Func<string, string> ShortNames( IEnumerable<string> shown )
	{
		var clashes = shown.Distinct().GroupBy( ShortTypeName ).Where( x => x.Count() > 1 ).SelectMany( x => x ).ToHashSet();
		return name => clashes.Contains( name ) ? name : ShortTypeName( name );
	}

	/// <summary>
	/// <c>System.Func`2[System.Linq.Expressions.ParameterExpression,System.Boolean]</c> as <c>Func&lt;ParameterExpression, Boolean&gt;</c>.
	/// </summary>
	internal static string ShortTypeName( string name )
	{
		var text = System.Text.RegularExpressions.Regex.Replace( name, @"`\d+", "" ).Replace( "[]", "\u0001" ).Replace( '[', '<' ).Replace( ']', '>' ).Replace( "\u0001", "[]" );
		text = System.Text.RegularExpressions.Regex.Replace( text, @"[\w.+]+", m => m.Value[(m.Value.LastIndexOf( '.' ) + 1)..].Replace( '+', '.' ) );
		return text.Replace( ",", ", " );
	}
}
