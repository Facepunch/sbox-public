using Sandbox.UI;

namespace Sandbox.Diagnostics;

internal sealed class DevToolsProfilerScopeDetails : Panel
{
	readonly DevToolsProfilerTimeline _timeline;
	readonly Label _summary, _treeTitle, _pageLabel;
	readonly Label _previous, _next, _parentButton, _durationReset, _zoomPause, _inspectFrame;
	readonly List<Label> _treeButtons = new();
	readonly DevToolsFacts _pauseFacts;
	Profiler.Span _pause;
	int _pauseFrame = -1;
	readonly DevToolsProfilerHistogram _histogram;
	readonly DevToolsFacts _distributionFacts;
	readonly DevToolsTable _instances, _tree, _pauseThreads;
	readonly Panel _instancesPanel, _treePanel, _treeControls, _distribution, _distributionControls;
	internal DevToolsProfilerSearchEntry SearchEntry { get; }
	readonly Dictionary<uint, Profiler.Site> _sites = new();
	Profiler.Capture _capture;
	int[] _matches = [], _filtered = [];
	Profiler.TreeRow[] _treeRows = [];
	readonly HashSet<int> _collapsed = new();
	readonly HashSet<int> _parents = new();
	int _page;
	int _selected = -1;
	int _treeSelected = -1;
	bool _revealInstance;
	internal const int PageSize = 50;

	internal DevToolsProfilerScopeDetails( DevToolsProfilerTimeline timeline )
	{
		_timeline = timeline;
		AddClass( "profiler-inspector" );
		var statistics = AddChild( new Panel( null, "profiler-statistics" ) );
		var heading = statistics.AddChild( new Panel( null, "profiler-scope-heading" ) );
		_summary = heading.AddChild( new Label( "Select a scope in the flame graph or search above", "profiler-details" ) );
		_summary.TooltipDelay = 0;
		_summary.TooltipOffset = 6;
		_summary.TooltipClass = "profiler-scope-tooltip";
		_distributionControls = heading.AddChild( new Panel( null, "profiler-toolbar profiler-distribution-controls hidden" ) );
		_distribution = statistics.AddChild( new Panel( null, "profiler-distribution hidden" ) );
		_histogram = _distribution.AddChild( new DevToolsProfilerHistogram() );
		_distributionFacts = _distribution.AddChild( new DevToolsFacts() );
		_distributionFacts.AddClass( "profiler-distribution-facts" );
		_histogram.Selected = ( min, max ) =>
		{
			_filtered = _matches.Where( i => _capture.Spans[i].DurationMs >= min && _capture.Spans[i].DurationMs < max ).ToArray();
			var upper = double.IsPositiveInfinity( max ) ? Math.Max( min, _histogram.Data.MaxMs ) : max;
			_durationReset.Text = $"{DevToolsProfilerHistogram.Duration( min )}–{DevToolsProfilerHistogram.Duration( upper )} ×";
			_durationReset.SetClass( "active", true );
			_page = 0;
			if ( !_filtered.Contains( _selected ) )
			{
				ClearInstance();
				_timeline.ClearInstanceFocus();
			}
			RefreshInstances();
			_instances.Parent.ScrollOffset = 0;
		};
		_durationReset = Button( _distributionControls, "All durations", () =>
		{
			_filtered = _matches;
			_histogram.ClearSelection();
			_durationReset.Text = "All durations";
			_durationReset.SetClass( "active", false );
			_page = 0;
			RefreshInstances();
			_instances.Parent.ScrollOffset = 0;
		} );
		_durationReset.Tooltip = "Clear the duration filter";
		var logCounts = _distributionControls.AddChild( new Label( "Log counts", "profiler-capture-button" ) );
		logCounts.Tooltip = "Use a logarithmic instance-count axis to make rare durations easier to see. Duration bins stay linear.";
		logCounts.AddEventListener( "onclick", () =>
		{
			_histogram.LogCounts = !_histogram.LogCounts;
			logCounts.SetClass( "active", _histogram.LogCounts );
		} );
		var searchBar = AddChild( new Panel( null, "profiler-toolbar" ) );
		searchBar.AddChild( new Label( "Find scope", "profiler-analysis-title" ) );
		SearchEntry = searchBar.AddChild( new DevToolsProfilerSearchEntry { Placeholder = "Scope name or source file…" } );
		SearchEntry.AddClass( "profiler-search" );
		_instancesPanel = statistics.AddChild( new Panel( null, "profiler-inspector-column hidden" ) );
		var bar = _instancesPanel.AddChild( new Panel( null, "profiler-toolbar" ) );
		_previous = Button( bar, "‹", () => { _page--; RefreshInstances(); _instances.Parent.ScrollOffset = 0; } );
		_next = Button( bar, "›", () => { _page++; RefreshInstances(); _instances.Parent.ScrollOffset = 0; } );
		_pageLabel = bar.AddChild( new Label( "", "hint" ) );
		_pageLabel.Tooltip = "Complete instances, sorted slowest first";
		_instances = _instancesPanel.AddChild( new Panel( null, "profiler-instance-scroll" ) ).AddChild(
			new DevToolsTable( "minmax(0, 1fr) 80px 80px", "Start ms ↗", "#total ms", "#self ms" ) { TooltipDelay = 0, TooltipOffset = 6, TooltipClass = "profiler-scope-tooltip" } );
		_treePanel = AddChild( new Panel( null, "profiler-inspector-column hidden" ) );
		_treeControls = _treePanel.AddChild( new Panel( null, "profiler-toolbar" ) );
		_treeTitle = _treeControls.AddChild( new Label( "", "profiler-analysis-title" ) );
		_parentButton = Button( _treeControls, "Parent scope", () => _timeline.InspectInstance( _capture.Spans[_selected].Parent ) );
		_treeButtons.Add( _parentButton );
		_treeButtons.Add( Button( _treeControls, "Expand all", () => { _collapsed.Clear(); RefreshTree(); } ) );
		_treeButtons.Add( Button( _treeControls, "Collapse all", () => { _collapsed.UnionWith( _parents ); RefreshTree(); } ) );
		_treeButtons.Add( Button( _treeControls, "Focus row", () => { if ( _capture is not null && _treeSelected >= 0 ) _timeline.FocusInstance( _capture.Spans[_treeSelected] ); } ) );
		var inspectRow = Button( _treeControls, "Inspect row", () => _timeline.InspectInstance( _treeSelected ) );
		inspectRow.Tooltip = "Make this row the selected scope and reference root";
		_treeButtons.Add( inspectRow );
		_zoomPause = Button( _treeControls, "Zoom to pause", () =>
		{
			var padding = Math.Max( 0.05, _pause.DurationMs * 0.5 );
			_timeline.SetRange( _pause.StartMs - padding, _pause.EndMs + padding );
		} );
		_inspectFrame = Button( _treeControls, "Inspect frame", () => { if ( _pauseFrame >= 0 ) _timeline.InspectInstance( _pauseFrame ); } );
		_pauseFacts = _treePanel.AddChild( new DevToolsFacts() );
		_pauseFacts.AddClass( "profiler-distribution-facts" );
		_tree = _treePanel.AddChild( new Panel( null, "profiler-tree-scroll" ) ).AddChild(
			new DevToolsTable( "minmax(0, 1fr) 80px 76px 65px 82px", "Selected instance and children", "#total ms", "#self ms", "#root %", "#parent %" ) { TooltipDelay = 0, TooltipOffset = 6, TooltipClass = "profiler-scope-tooltip" } );
		_pauseThreads = _treePanel.AddChild( new Panel( null, "profiler-tree-scroll hidden" ) ).AddChild(
			new DevToolsTable( "minmax(0, 1fr) minmax(0, 1fr) 80px", "Thread", "Scope open when the pause began", "#scope ms" ) { TooltipDelay = 0, TooltipOffset = 6, TooltipClass = "profiler-scope-tooltip" } );
		var details = AddChild( new Panel( null, "profiler-detail-columns" ) );
		_treePanel.Parent = details;
		statistics.Parent = details;
		SetChildIndex( searchBar, 0 );
	}

	static Label Button( Panel panel, string text, Action action )
	{
		var button = panel.AddChild( new Label( text, "profiler-capture-button" ) );
		button.AddEventListener( "onclick", () => { if ( !button.HasClass( "disabled" ) ) action(); } );
		return button;
	}

	internal void ShowSite( Profiler.Capture capture, uint siteId, ulong thread = 0 )
	{
		_capture = capture;
		ClearInstance();
		_page = 0;
		_sites.Clear();
		foreach ( var site in capture.Sites ) _sites[site.Id] = site;
		_matches = capture.Spans.Where( x => x.ScopeId == siteId && !x.Incomplete && (thread == 0 || x.ThreadKey == thread) ).Select( x => x.Index ).ToArray();
		Array.Sort( _matches, ( a, b ) => capture.Spans[b].DurationMs.CompareTo( capture.Spans[a].DurationMs ) );
		_filtered = _matches;
		_histogram.SetData( _matches.Select( i => capture.Spans[i].DurationMs ) );
		_durationReset.Text = "All durations";
		_durationReset.SetClass( "active", false );
		var stats = _histogram.Data;
		_summary.Text = DevToolsProfilerGc.IsPauseSite( _sites[siteId] ) ? DevToolsProfilerGc.Label( _sites[siteId].Name ) : _sites[siteId].Name;
		_summary.Tooltip = Profiler.SourceLocation( _sites[siteId] );
		_distributionFacts.Set( "Instances", $"{stats.Count:N0}" );
		_distributionFacts.Set( "Median ms", stats.Count > 0 ? $"{stats.P50Ms:0.000}" : "-" );
		_distributionFacts.Set( "p95 ms", stats.Count > 0 ? $"{stats.P95Ms:0.000}" : "-" );
		_distributionFacts.Set( "Max ms", stats.Count > 0 ? $"{stats.MaxMs:0.000}" : "-" );
		_distributionControls.SetClass( "hidden", stats.Count == 0 );
		_distribution.SetClass( "hidden", false );
		_instancesPanel.SetClass( "hidden", false );
		RefreshInstances();
		_instances.Parent.ScrollOffset = 0;
	}

	internal void Clear()
	{
		_distributionControls.SetClass( "hidden", true );
		_distribution.SetClass( "hidden", true );
		_instancesPanel.SetClass( "hidden", true );
		_summary.Text = "Select a scope in the flame graph or search above";
		_summary.Tooltip = null;
		_capture = null;
		_matches = _filtered = [];
		_sites.Clear();
		ClearInstance();
	}

	void ClearInstance()
	{
		_treePanel.SetClass( "hidden", true );
		_treeRows = [];
		_collapsed.Clear();
		_parents.Clear();
		_selected = _treeSelected = -1;
		_revealInstance = false;
		_tree.Begin();
		_tree.End();
	}

	void RefreshInstances()
	{
		_page = Math.Clamp( _page, 0, Math.Max( 0, (_filtered.Length - 1) / PageSize ) );
		_previous.SetClass( "disabled", _page == 0 );
		_next.SetClass( "disabled", (_page + 1) * PageSize >= _filtered.Length );
		_instances.Begin();
		int rowIndex = 0;
		foreach ( var index in _filtered.Skip( _page * PageSize ).Take( PageSize ) )
		{
			var span = _capture.Spans[index];
			var site = _sites[span.ScopeId];
			// DevToolsTable caches keyed rows. Reuse page slots rather than retaining every instance ever visited.
			_instances.Row( $"instance:{rowIndex++}", [new( $"{span.StartMs:0.000}", Swatch: DevToolsProfilerTimeline.SiteColor( site ) ),
				new( $"{span.DurationMs:0.000}", "number" ), new( span.SelfMs is { } self ? $"{self:0.000}" : "-", "number" )],
				() => SelectInstance( span.Index ), $"{_capture.InstanceName( span, site.Name )}\n{_timeline.ThreadLabel( span.ThreadKey )}\n{Profiler.SourceLocation( site )}", span.Index == _selected ? "selected" : null );
		}
		_instances.End( _matches.Length == 0 ? "No complete instances recorded" : "No instances in this duration bin" );
		_pageLabel.Text = _filtered.Length == _matches.Length ? $"{_filtered.Length:N0} instances · slowest first" : $"{_filtered.Length:N0} of {_matches.Length:N0} instances";
		if ( _filtered.Length > PageSize ) _pageLabel.Text += $" · page {_page + 1}/{(_filtered.Length + PageSize - 1) / PageSize}";
	}

	internal void SelectInstance( int index )
	{
		if ( _capture is null || index < 0 || index >= _capture.Spans.Length ) return;
		_selected = index;
		_treePanel.SetClass( "hidden", false );
		var span = _capture.Spans[index];
		_timeline.SelectInstanceFocus( span );
		_parentButton.SetClass( "disabled", span.Parent < 0 );
		var position = Array.IndexOf( _filtered, index );
		if ( position >= 0 ) _page = position / PageSize;
		RefreshInstances();
		_revealInstance = position >= 0;
		_treeRows = Profiler.CallTree( _capture, index, 1000, 0, out var truncated );
		_treeControls.SetClass( "hidden", _treeRows.Length == 0 );
		_treeSelected = index;
		_collapsed.Clear();
		_parents.Clear();
		foreach ( var row in _treeRows.Skip( 1 ) ) _parents.Add( _capture.Spans[row.SpanIndex].Parent );
		foreach ( var row in _treeRows.Where( x => x.Depth >= 1 && _parents.Contains( x.SpanIndex ) ) ) _collapsed.Add( row.SpanIndex );
		var pause = span.ThreadKey == Profiler.GcThreadKey;
		_tree.Parent.SetClass( "hidden", pause );
		_pauseThreads.Parent.SetClass( "hidden", !pause );
		_pauseFacts.SetClass( "hidden", true );
		_zoomPause.SetClass( "hidden", !pause );
		_inspectFrame.SetClass( "hidden", true );
		foreach ( var button in _treeButtons ) button.SetClass( "hidden", pause );
		if ( pause )
		{
			ShowPause( span );
			return;
		}
		var (gcCount, gcMs, _) = DevToolsProfilerGc.Overlap( _capture, span.StartMs, span.EndMs );
		_treeTitle.Text = $"Breakdown{(span.Incomplete ? " · unfinished" : "")}{(truncated ? " · 1,000-node limit" : "")}{(gcCount > 0 ? $" · overlaps GC pause {gcMs:0.000} ms" : "")}";
		_treeTitle.Tooltip = "% root = selected instance; % parent = immediate parent. Click rows to expand." +
			(truncated ? "\nThe limit favors children near the root. Inspect a row to explore its deeper children." : "") +
			(gcCount > 0 ? "\nGC pause time is included in wall and self time; the runtime doesn't say which threads it stopped." : "");
		RefreshTree();
	}

	// What each thread was inside when the pause began. Overlap is correlation, so self times stay as recorded.
	void ShowPause( Profiler.Span pause )
	{
		_pause = pause;
		_treeTitle.Text = $"{DevToolsProfilerGc.Label( _sites[pause.ScopeId].Name )} · {pause.DurationMs:0.000} ms · {pause.StartMs:0.000}–{pause.EndMs:0.000} ms";
		var frame = DevToolsProfilerGc.FrameAt( _capture, pause.StartMs );
		_pauseFrame = frame?.SpanIndex ?? -1;
		_inspectFrame.SetClass( "hidden", frame is null );
		// The title has the pause itself; the facts are about its frame, so none between frames
		_pauseFacts.SetClass( "hidden", frame is null );
		if ( frame is { } f )
		{
			_inspectFrame.Text = $"Inspect frame {f.Index + 1}";
			_pauseFacts.Set( "Frame", $"{f.Index + 1} · {f.DurationMs:0.000} ms" );
			_pauseFacts.Set( "Share of frame", $"{(f.DurationMs > 0 ? Math.Min( pause.EndMs, f.EndMs ) - pause.StartMs : 0) / Math.Max( 0.001, f.DurationMs ) * 100:0}%" );
			if ( f.AllocatedBytes is { } bytes ) _pauseFacts.Set( "Allocated in frame", DevToolsFormat.Bytes( bytes ), tooltip: "Main thread" );
			else _pauseFacts.Hide( "Allocated in frame" );
		}
		else
		{
			_pauseFacts.Hide( "Frame" );
			_pauseFacts.Hide( "Share of frame" );
			_pauseFacts.Hide( "Allocated in frame" );
		}
		_treeTitle.Tooltip = "A stop-the-world runtime suspension. The runtime doesn't say which threads it stopped; native threads may keep running.";
		_pauseThreads.Begin();
		int rowIndex = 0;
		foreach ( var thread in _capture.Threads.OrderBy( x => _timeline.ThreadLabel( x.Key ).StartsWith( "Main thread" ) ? 0 : 1 ).ThenBy( x => _timeline.ThreadLabel( x.Key ) ) )
		{
			if ( thread.Key == Profiler.GcThreadKey || !_capture.ThreadSpans.TryGetValue( thread.Key, out var spans ) ) continue;
			var index = Profiler.InnermostSpan( spans, pause.StartMs );
			if ( index < 0 ) continue;
			var span = _capture.Spans[index];
			var site = _sites[span.ScopeId];
			_pauseThreads.Row( $"pause:{rowIndex++}", [new( _timeline.ThreadLabel( thread.Key ) ), new( site.Name, Swatch: DevToolsProfilerTimeline.SiteColor( site ) ), new( $"{span.DurationMs:0.000}", "number" )],
				() => _timeline.InspectInstance( index ), Profiler.SourceLocation( site ) );
		}
		_pauseThreads.End( "No thread was inside a scope when the pause began" );
	}

	protected override void FinalLayoutChildren( Vector2 offset )
	{
		base.FinalLayoutChildren( offset );
		static void Reveal( DevToolsTable table )
		{
			var selected = table.Children.FirstOrDefault( x => x.HasClass( "selected" ) && !x.HasClass( "hidden" ) );
			if ( selected is not null ) table.Parent.ScrollIntoView( selected.Box.Rect );
		}
		if ( _revealInstance ) Reveal( _instances );
		_revealInstance = false;
	}

	void RefreshTree()
	{
		_tree.Begin();
		var hiddenBelowDepth = int.MaxValue;
		int rowIndex = 0;
		foreach ( var row in _treeRows )
		{
			if ( row.Depth > hiddenBelowDepth ) continue;
			hiddenBelowDepth = _collapsed.Contains( row.SpanIndex ) ? row.Depth : int.MaxValue;
			var site = _sites[row.ScopeId];
			var marker = _parents.Contains( row.SpanIndex ) ? _collapsed.Contains( row.SpanIndex ) ? "+" : "−" : "";
			_tree.Row( $"tree:{rowIndex++}", [new( _capture.InstanceName( _capture.Spans[row.SpanIndex], site.Name ), Indent: Math.Min( row.Depth, 24 ) * 14, Marker: marker ),
				new( $"{row.InclusiveMs:0.000}", "number" ), new( row.SelfMs is { } self ? $"{self:0.000}" : "-", "number" ),
				new( row.PercentOfRoot is { } root ? $"{root:0.0}%" : "-", "number" ), new( row.PercentOfParent is { } parent ? $"{parent:0.0}%" : "-", "number" )],
				() =>
				{
					_treeSelected = row.SpanIndex;
					if ( _parents.Contains( row.SpanIndex ) && !_collapsed.Add( row.SpanIndex ) ) _collapsed.Remove( row.SpanIndex );
					RefreshTree();
				},
				$"{Profiler.SourceLocation( site )}{(row.Incomplete ? "\nUnfinished: percentages unavailable" : "")}", row.SpanIndex == _treeSelected ? "selected" : null );
		}
		_tree.End( "Select an instance to see its child tree" );
	}
}
