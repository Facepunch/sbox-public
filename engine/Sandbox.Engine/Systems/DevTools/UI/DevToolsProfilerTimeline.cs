using Sandbox.UI;

namespace Sandbox.Diagnostics;

internal sealed class DevToolsProfilerTimeline : Panel
{
	const float RowHeight = 18, HeaderHeight = 22, TickHeight = 15, GcStripHeight = 12;
	static readonly Color CursorColor = new( 0.5f, 0.75f, 1f );
	readonly TimeRuler _ruler;
	readonly Label _status, _rangeLabel, _threadButton, _backButton, _recordButton;
	readonly Label _captureShort, _captureLong, _save, _load;
	readonly TextEntry _loadStart, _loadDuration;
	readonly Checkbox _streamToDisk, _emptyThreads;
	string _fileError, _savedFile;
	readonly DevToolsProfilerSearchEntry _search;
	DevToolsProfilerSearchEntry.Suggestion[] _suggestions = [];
	readonly DevToolsProfilerOverview _overview;
	readonly DevToolsProfilerScopeDetails _inspector;
	readonly DevToolsProfilerMemory _memory;
	readonly List<(Rect Rect, Profiler.Span Span)> _hits = new();
	readonly List<(Rect Rect, ActivityMark Mark)> _activityHits = new();
	readonly Dictionary<uint, Profiler.Site> _sites = new();
	readonly Dictionary<ulong, ArraySegment<Profiler.Span>> _threads = new();
	readonly Dictionary<ulong, List<Profiler.Span>> _visible = new();
	readonly Dictionary<ulong, List<ActivityMark>> _activity = new();
	readonly Dictionary<uint, Color> _colors = new();
	readonly Dictionary<ulong, float> _threadY = new();
	readonly Dictionary<ulong, string> _threadLabels = new();
	readonly HashSet<ulong> _collapsed = new();
	readonly List<DevToolsProfilerGc.Mark> _gcMarks = new();
	readonly Dictionary<ulong, float> _laneHeight = new();
	readonly List<(Rect Rect, DevToolsProfilerGc.Mark Mark)> _gcHits = new();
	readonly Stack<(double Start, double End)> _back = new();
	Profiler.Capture _capture;
	double _start, _end;
	uint _selected;
	int _focused = -1;
	string _query = "";
	string _pendingQuery = "";
	RealTimeSince _sinceSearchChange;
	RealTimeSince _sinceWheelZoom;
	bool _wheelZooming;
	bool _initialCapture, _mainOnly, _ignoreClick, _showEmpty;
	float _drawWidth = -1;

	internal double RangeStart => _start;
	internal double RangeEnd => _end;
	internal uint SelectedSite => _selected;
	internal Profiler.Capture Capture => _capture;
	float? CursorFraction => HasHovered || _ruler.HasHovered ? Math.Clamp( MousePosition.x / Math.Max( 1, Box.Rect.Width ), 0, 1 ) : null;

	internal DevToolsProfilerTimeline( DevToolsSection section, DevToolsSection analysis, DevToolsSection memory )
	{
		_status = section.Aside;
		AddClass( "overlay-view profiler-timeline" );
		Style.Height = 64;
		_recordButton = AddButton( section.Head, "Start", () =>
		{
			if ( Profiler.IsRecording ) Profiler.StopCapture();
			else Record( float.PositiveInfinity );
		} );
		_recordButton.Tooltip = "Record until stopped, or until the capture fills profiler_memory_mb";
		_captureShort = AddButton( section.Head, "1 s", () => Record( 1 ) );
		_captureShort.Tooltip = "Record one second";
		_captureLong = AddButton( section.Head, "10 s", () => Record( 10 ) );
		_captureLong.Tooltip = "Record ten seconds";
		_streamToDisk = section.Head.AddChild( new Checkbox { LabelText = "Stream to disk" } );
		_streamToDisk.AddClass( "profiler-stream-option" );
		_streamToDisk.Tooltip = "Record an indexed file beyond the RAM preview limit. A slow/full disk stops recording; scope calls never wait for disk I/O.";
		_loadStart = section.Head.AddChild( new TextEntry { Placeholder = "Start ms" } );
		_loadStart.Style.Width = 72;
		_loadStart.AddClass( "profiler-search" );
		_loadStart.Style.MinWidth = 72;
		_loadStart.Tooltip = "Start offset in a streamed recording. Empty means 0.";
		_loadDuration = section.Head.AddChild( new TextEntry { Placeholder = "Length ms" } );
		_loadDuration.Style.Width = 76;
		_loadDuration.AddClass( "profiler-search" );
		_loadDuration.Style.MinWidth = 76;
		_loadDuration.Tooltip = "Time range to load from a streamed recording. Empty means the remainder, subject to the RAM limit.";
		_save = AddButton( section.Head, "Save", () => _ = Save() );
		_save.Tooltip = $"Save this capture to {Profiler.CaptureDirectory}, named by the time, with scope names and local source paths";
		_load = AddButton( section.Head, "Load", null );
		_load.Tooltip = $"Load a capture saved in {Profiler.CaptureDirectory}";
		_load.AddEventListener( "onclick", () =>
		{
			if ( _load.HasClass( "disabled" ) ) return;
			var position = DevTools.GameDockOpen && !DevTools.PoppedOut ? Sandbox.UI.Popup.PositionMode.AboveLeft : Sandbox.UI.Popup.PositionMode.BelowLeft;
			DevToolsMenus.OpenCaptures( _load, position, Profiler.SavedCaptures(), name => _ = Load( name ) );
		} );
		_overview = section.Body.AddChild( new DevToolsProfilerOverview( this ) );
		var toolbar = section.Body.AddChild( new Panel( null, "profiler-toolbar" ) );
		_backButton = AddButton( toolbar, "Back", Back );
		AddButton( toolbar, "+", () => Zoom( 0.5 ) );
		AddButton( toolbar, "−", () => Zoom( 2 ) );
		AddButton( toolbar, "Reset zoom", () => SetRange( 0, _capture?.DurationMs ?? 1 ) );
		AddButton( toolbar, "Slowest frame", () =>
		{
			if ( _capture?.Frames.Where( x => !x.Incomplete ).OrderByDescending( x => x.DurationMs ).FirstOrDefault() is { } frame && frame.DurationMs > 0 )
				SetRange( frame.StartMs, frame.EndMs );
		} );
		_threadButton = AddButton( toolbar, "All threads", () =>
		{
			_mainOnly = !_mainOnly;
			_search.CancelSuggestions();
			_threadButton.Text = _mainOnly ? "Main thread" : "All threads";
			LayoutThreads();
			RefreshPopulation();
			_focused = -1;
			if ( _suggestions.Any( x => x.SiteId == _selected ) ) SelectSite( _selected );
			else
			{
				_selected = 0;
				_inspector.Clear();
			}
		} );
		_emptyThreads = toolbar.AddChild( new Checkbox { LabelText = "Empty threads" } );
		_emptyThreads.AddClass( "profiler-stream-option" );
		_emptyThreads.Tooltip = "Show threads that recorded no scopes in this capture";
		_rangeLabel = toolbar.AddChild( new Label( "", "hint" ) );
		_ruler = section.Body.AddChild( new TimeRuler( this ) );
		var scroll = section.Body.AddChild( new Panel( null, "profiler-lanes" ) );
		Parent = scroll;
		section.Body.AddChild( new TimelineDivider( scroll ) );
		_inspector = analysis.Body.AddChild( new DevToolsProfilerScopeDetails( this ) );
		_memory = new DevToolsProfilerMemory( this, memory );
		_search = _inspector.SearchEntry;
		_search.AutoComplete = CompleteSearch;
		_search.Accepted = AcceptSuggestion;
		_search.AddEventListener( "onsubmit", () =>
		{
			if ( CompleteSearch( _search.Text ).FirstOrDefault() is TextEntry.AutocompleteEntry { Value: DevToolsProfilerSearchEntry.Suggestion suggestion } )
				AcceptSuggestion( suggestion );
		} );
		TooltipDelay = 0;
		TooltipOffset = 6;
		TooltipClass = "profiler-scope-tooltip";
	}

	static Label AddButton( Panel parent, string title, Action action )
	{
		var button = parent.AddChild( new Label( title, "profiler-capture-button" ) );
		if ( action is not null ) button.AddEventListener( "onclick", () => { if ( !button.HasClass( "disabled" ) ) action(); } );
		return button;
	}

	async Task Save()
	{
		try
		{
			_fileError = null;
			if ( _capture is null ) return;
			var path = await Profiler.SaveCapture( _capture, Profiler.CaptureFileName( _capture ) );
			await Sandbox.MainThread.Wait();
			_savedFile = System.IO.Path.GetFileName( path );
		}
		catch ( Exception e ) { await Sandbox.MainThread.Wait(); _fileError = e.Message; }
	}

	async Task Load( string name )
	{
		try
		{
			_fileError = null;
			static double ReadTime( TextEntry entry ) => string.IsNullOrWhiteSpace( entry.Text ) ? 0 :
				double.TryParse( entry.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value ) && double.IsFinite( value ) && value >= 0 ? value :
				throw new ArgumentException( "Enter non-negative times in milliseconds." );
			await Profiler.LoadCapture( name, ReadTime( _loadStart ), ReadTime( _loadDuration ) );
			await Sandbox.MainThread.Wait();
			DevTools.Paused = false;
		}
		catch ( Exception e ) { await Sandbox.MainThread.Wait(); _fileError = e.Message; }
	}

	void Record( float seconds )
	{
		if ( Profiler.RecordingId != 0 || Profiler.FileBusy ) return;
		DevTools.Paused = false;
		_ = RecordAsync( seconds );
	}

	async Task RecordAsync( float seconds )
	{
		try
		{
			_fileError = null;
			var capture = await Profiler.StartCapture( seconds, streamToDisk: _streamToDisk.Checked );
			await Sandbox.MainThread.Wait();
			_fileError = capture.StorageError;
		}
		catch ( Exception e ) { await Sandbox.MainThread.Wait(); _fileError = e.Message; }
	}

	internal void SetRange( double start, double end, bool remember = true )
	{
		if ( _capture is null || !double.IsFinite( start ) || !double.IsFinite( end ) ) return;
		start = Math.Clamp( start, 0, Math.Max( 0, _capture.DurationMs - 0.001 ) );
		end = Math.Clamp( end, start + 0.001, Math.Max( start + 0.001, _capture.DurationMs ) );
		if ( start == _start && end == _end ) return;
		if ( remember )
		{
			_back.Push( (_start, _end) );
			_wheelZooming = false;
		}
		_start = start;
		_end = end;
		_drawWidth = -1;
		LayoutThreads();
	}

	void Back()
	{
		_wheelZooming = false;
		if ( _back.TryPop( out var range ) ) SetRange( range.Start, range.End, false );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();
		Profiler.SetViewedCapture( this, null );
	}

	public override void Tick()
	{
		base.Tick();
		_save.SetClass( "disabled", _capture is null || Profiler.FileBusy || Profiler.RecordingId != 0 );
		_load.SetClass( "disabled", Profiler.FileBusy || Profiler.RecordingId != 0 );
		if ( !DevTools.IsShowing( "profiler" ) )
		{
			_search.CancelSuggestions();
			return;
		}
		if ( !_initialCapture )
		{
			_initialCapture = true;
			if ( Profiler.Latest is null && !DevTools.Paused ) Record( 1 );
		}
		_streamToDisk.SetClass( "disabled", Profiler.RecordingId != 0 );
		_captureShort.SetClass( "disabled", Profiler.RecordingId != 0 || Profiler.FileBusy );
		_captureLong.SetClass( "disabled", Profiler.RecordingId != 0 || Profiler.FileBusy );
		_recordButton.Text = Profiler.IsRecording ? $"Stop · {Profiler.RecordingSeconds:0.0} s" : "Start";
		_recordButton.SetClass( "active", Profiler.IsRecording );
		_recordButton.SetClass( "disabled", !Profiler.IsRecording && (Profiler.RecordingId != 0 || Profiler.FileBusy) );
		_backButton.SetClass( "disabled", _back.Count == 0 );
		if ( _emptyThreads.Checked != _showEmpty )
		{
			_showEmpty = _emptyThreads.Checked;
			LayoutThreads();
		}
		_rangeLabel.Text = _capture is null ? "" : $"{_end - _start:0.###} ms";
		_rangeLabel.Tooltip = $"{_start:0.000}–{_end:0.000} ms from capture start. Ctrl/Alt + wheel to zoom. Shift + wheel or horizontal scroll to pan.";
		_threadButton.SetClass( "disabled", MainThread == 0 );
		_threadButton.Tooltip = MainThread == 0 ? "No frame scopes identify the main thread in this capture" : "Filter the timeline and analysis to the main thread";
		_status.Text = Profiler.IsRecording ? Profiler.RecordingIsOpenEnded ? $"Recording {Profiler.RecordingSeconds:0.0} s…" : $"Recording {Profiler.RecordingProgress:P0}…" : Profiler.RecordingId != 0 ? "Processing capture…" : _capture is null ? "No capture" :
			$"{_capture.DurationMs / 1000:0.00} s · {_capture.Spans.Length:N0} instances{(_capture.DroppedEvents > 0 ? $" · {_capture.DroppedEvents:N0} lost" : "")}{(_capture.IncompleteScopes > 0 ? $" · {_capture.IncompleteScopes} unfinished" : "")}{(_capture.EventLimitReached ? " · " + (_capture.LimitReason ?? "event limit") : "")}";
		if ( !DevTools.Paused && Profiler.Latest is { } capture && !ReferenceEquals( capture, _capture ) )
		{
			_search.CancelSuggestions();
			_capture = capture;
			Profiler.SetViewedCapture( this, capture );
			if ( MainThread == 0 )
			{
				_mainOnly = false;
				_threadButton.Text = "All threads";
			}
			_start = 0;
			_end = Math.Max( 0.001, capture.DurationMs );
			_back.Clear();
			_wheelZooming = false;
			_selected = 0;
			_focused = -1;
			_sites.Clear();
			_colors.Clear();
			_gcMarks.Clear();
			_gcHits.Clear();
			_hits.Clear();
			_activityHits.Clear();
			_drawWidth = -1;
			foreach ( var site in capture.Sites ) { _sites.Add( site.Id, site ); _colors.Add( site.Id, SiteColor( site ) ); }
			_threads.Clear();
			foreach ( var (key, spans) in capture.ThreadSpans ) _threads[key] = spans;
			_collapsed.Clear();
			_threadLabels.Clear();
			foreach ( var thread in capture.Threads )
			{
				var name = thread.Key == MainThread ? "Main thread" : IsRenderThread( thread ) ? "Render thread" : thread.Name;
				_threadLabels.Add( thread.Key, thread.Key == Profiler.GcThreadKey ? name : $"{name} · {thread.Id}" );
				if ( thread.Key != MainThread && !IsRenderThread( thread ) && thread.Key != Profiler.GcThreadKey ) _collapsed.Add( thread.Key );
			}
			_overview.SetCapture( capture );
			_ruler.Style.Height = TickHeight + (DevToolsProfilerGc.Pauses( capture ).Count > 0 ? GcStripHeight : 0);
			_memory.SetCapture( capture, _sites );
			LayoutThreads();
			RefreshPopulation();
			if ( capture.Overhead is { } overhead )
				_status.Tooltip = $"Collector {overhead.MedianDrainMs * 1000:0.0} µs/pass median · p95 {overhead.P95DrainMs * 1000:0.0} µs · snapshot {overhead.SnapshotMs:0.0} ms · worker analysis {overhead.FinalizationMs:0.0} ms (scope emission excluded)";
			else _status.Tooltip = null;
			_savedFile = null;
			if ( capture.SourceFile is { } file )
			{
				_status.Tooltip = $"{file}\nRange {capture.SourceOffsetMs:0.###}–{capture.SourceOffsetMs + capture.DurationMs:0.###} ms of {Math.Max( capture.RecordedDurationMs, capture.SourceOffsetMs + capture.DurationMs ):0.###} ms\n{_status.Tooltip}";
			}
			_inspector.Clear();
		}
		if ( _fileError is not null ) _status.Text = _fileError;
		else if ( Profiler.FileBusy ) _status.Text = "Reading/writing capture…";
		else if ( !Profiler.IsRecording && Profiler.RecordingId == 0 && (_savedFile ?? _capture?.SourceFile) is { } saved ) _status.Text += $" · {saved}";
		if ( _search.IsPreviewing ) return;
		var query = _search.Text?.Trim() ?? "";
		if ( query != _pendingQuery ) { _pendingQuery = query; _sinceSearchChange = 0; }
		if ( query != _query && (query.Length == 0 || _sinceSearchChange > 0.15f) )
		{
			_query = query;
			_selected = 0;
			_focused = -1;
			_inspector.Clear();
		}
	}

	ulong MainThread => _capture is { Frames.Length: > 0 } ? _capture.Spans[_capture.Frames[0].SpanIndex].ThreadKey : 0;
	static bool IsRenderThread( Profiler.ThreadInfo thread ) => thread.Name == "VKRenderThread";
	internal string ThreadLabel( ulong key ) => _threadLabels.TryGetValue( key, out var label ) ? label : $"Thread {key}";

	void LayoutThreads()
	{
		if ( _capture is null ) return;
		_drawWidth = -1;
		_threadY.Clear();
		float y = 0;
		foreach ( var thread in _capture.Threads.OrderBy( x => x.Key == MainThread ? 0 : IsRenderThread( x ) ? 1 : 2 ).ThenBy( x => x.Name ).ThenBy( x => x.Key ) )
		{
			if ( (_mainOnly && thread.Key != MainThread) || thread.Key == Profiler.GcThreadKey ) continue;
			if ( !_showEmpty && _threads.GetValueOrDefault( thread.Key ).Count == 0 ) continue;
			_threadY[thread.Key] = y;
			int depth = 0;
			if ( !_collapsed.Contains( thread.Key ) )
				foreach ( var span in _threads.GetValueOrDefault( thread.Key, ArraySegment<Profiler.Span>.Empty ) )
				{
					if ( span.StartMs >= _end ) break;
					if ( span.EndMs > _start ) depth = Math.Max( depth, span.Depth );
				}
			_laneHeight[thread.Key] = _collapsed.Contains( thread.Key ) ? 0 : (Math.Min( depth, 63 ) + 1) * RowHeight + 8;
			y += HeaderHeight + _laneHeight[thread.Key];
		}
		Style.Height = Math.Max( 64, y );
	}


	object[] CompleteSearch( string text )
	{
		var query = text?.Trim() ?? "";
		return _suggestions.Where( x => query.Length == 0 || x.Name.Contains( query, StringComparison.OrdinalIgnoreCase ) || x.File.Contains( query, StringComparison.OrdinalIgnoreCase )
				|| DevToolsProfilerGc.Label( x.Name ).Contains( query, StringComparison.OrdinalIgnoreCase ) )
			.OrderBy( x => x.Name.Equals( query, StringComparison.OrdinalIgnoreCase ) ? 0 : x.Name.StartsWith( query, StringComparison.OrdinalIgnoreCase ) ? 1 : x.Name.Contains( query, StringComparison.OrdinalIgnoreCase ) ? 2 : 3 )
			.ThenByDescending( x => x.SelfMs ).ThenBy( x => x.Name, StringComparer.OrdinalIgnoreCase ).ThenBy( x => x.SiteId )
			.Take( 8 ).Select( x => (object)new TextEntry.AutocompleteEntry
			{
				Title = $"{x.Name} · {x.Calls:N0} instances{(Profiler.SourceLocation( new( x.SiteId, x.Name, x.File, x.Line ) ) is { Length: > 0 } source ? $" · {source}" : "")}",
				Value = x
			} ).ToArray();
	}

	void AcceptSuggestion( DevToolsProfilerSearchEntry.Suggestion suggestion )
	{
		_search.DestroyAutoComplete();
		if ( _capture?.Id != suggestion.CaptureId || !_sites.ContainsKey( suggestion.SiteId ) ) return;
		_search.Text = suggestion.Name;
		_query = _pendingQuery = suggestion.Name;
		SelectSite( suggestion.SiteId );
	}

	void RefreshPopulation()
	{
		if ( _capture is null ) return;
		// Pauses stay findable with the main thread filter on
		var costs = _mainOnly ? Profiler.CostsInRange( _capture, 0, _capture.DurationMs, MainThread ).Concat( Profiler.CostsInRange( _capture, 0, _capture.DurationMs, Profiler.GcThreadKey ) ).ToArray() : _capture.Costs;
		_suggestions = costs.Select( x =>
		{
			var site = _sites[x.ScopeId];
			return new DevToolsProfilerSearchEntry.Suggestion( _capture.Id, site.Id, site.Name, site.File, site.Line, x.Calls, x.SelfMs );
		} ).ToArray();
	}

	void SelectSite( uint id )
	{
		ShowSite( id );
		var thread = SiteThread( id );
		var instance = _capture.Spans.Where( x => x.ScopeId == id && !x.Incomplete && (thread == 0 || x.ThreadKey == thread) )
			.OrderByDescending( x => x.DurationMs ).FirstOrDefault();
		if ( instance.DurationMs > 0 ) _inspector.SelectInstance( instance.Index );
	}

	void ShowSite( uint id )
	{
		_selected = id;
		_focused = -1;
		_inspector.ShowSite( _capture, id, SiteThread( id ) );
	}

	// The main thread filter doesn't apply to pauses, which have a lane of their own
	ulong SiteThread( uint id ) => _mainOnly && !DevToolsProfilerGc.IsPauseSite( _sites.GetValueOrDefault( id ) ) ? MainThread : 0;

	internal void InspectSite( uint id )
	{
		if ( _capture is null || !_sites.TryGetValue( id, out var site ) ) return;
		_search.CancelSuggestions();
		_search.Text = _query = _pendingQuery = SearchName( site );
		SelectSite( id );
	}

	static string SearchName( Profiler.Site site ) => DevToolsProfilerGc.IsPauseSite( site ) ? DevToolsProfilerGc.Label( site.Name ) : site.Name;

	internal void SelectInstanceFocus( Profiler.Span span )
	{
		_focused = span.Index;
		_collapsed.Remove( span.ThreadKey );
		LayoutThreads();
	}

	internal void FocusInstance( Profiler.Span span )
	{
		SelectInstanceFocus( span );
		SetRange( span.StartMs, span.EndMs );
		if ( _threadY.TryGetValue( span.ThreadKey, out var y ) )
		{
			var bottom = y + HeaderHeight + (Math.Min( span.Depth, 63 ) + 1) * RowHeight;
			var top = Math.Max( y - HeaderHeight, bottom - Parent.Box.Rect.Height / ScaleToScreen + RowHeight );
			Parent.ScrollOffset = new Vector2( 0, Math.Max( 0, top ) * ScaleToScreen );
		}
	}

	internal void InspectInstance( int index )
	{
		if ( _capture is null || index < 0 || index >= _capture.Spans.Length ) return;
		var site = _sites[_capture.Spans[index].ScopeId];
		_search.CancelSuggestions();
		_search.Text = _query = _pendingQuery = SearchName( site );
		ShowSite( site.Id );
		_inspector.SelectInstance( index );
	}

	internal void ClearInstanceFocus()
	{
		_focused = -1;
	}

	protected override void OnClick( MousePanelEvent e )
	{
		if ( _ignoreClick ) { _ignoreClick = false; return; }
		var position = e.LocalPosition / ScaleToScreen;
		foreach ( var (key, y) in _threadY )
		{
			if ( position.y < y || position.y >= y + HeaderHeight ) continue;
			if ( !_collapsed.Add( key ) ) _collapsed.Remove( key );
			LayoutThreads();
			return;
		}
		for ( int i = _hits.Count - 1; i >= 0; i-- )
		{
			var (rect, span) = _hits[i];
			if ( !rect.IsInside( position ) || span.DurationMs <= 0 ) continue;
			InspectInstance( span.Index );
			break;
		}
	}

	protected override void OnRightClick( MousePanelEvent e ) => Back();
	protected override void OnMouseDown( MousePanelEvent e ) => _ignoreClick = false;
	public override bool HasTooltip
	{
		get
		{
			UpdateHoverTooltip( MousePosition / ScaleToScreen );
			return base.HasTooltip;
		}
	}

	protected override void OnDoubleClick( MousePanelEvent e )
	{
		var position = e.LocalPosition / ScaleToScreen;
		for ( int i = _hits.Count - 1; i >= 0; i-- )
		{
			var (rect, span) = _hits[i];
			if ( !rect.IsInside( position ) || span.DurationMs <= 0 ) continue;
			InspectInstance( span.Index );
			var padding = Math.Max( 0.001, span.DurationMs * 0.05 );
			SetRange( span.StartMs - padding, span.EndMs + padding );
			_ignoreClick = true;
			return;
		}
		foreach ( var (rect, mark) in _activityHits )
		{
			if ( !rect.IsInside( position ) ) continue;
			var padding = Math.Max( 0.0005, mark.EndMs - mark.StartMs );
			SetRange( mark.StartMs - padding, mark.EndMs + padding );
			_ignoreClick = true;
			return;
		}
	}

	public override void OnMouseWheel( Vector2 value ) => Wheel( value, MousePosition.x / Math.Max( 1, Box.Rect.Width ) );

	void Wheel( Vector2 value, float anchor )
	{
		var modifiers = UISystem?.Input.WheelModifiers ?? KeyboardModifiers.None;
		if ( modifiers.Contains( KeyboardModifiers.Ctrl ) || modifiers.Contains( KeyboardModifiers.Alt ) )
		{
			var delta = value.y != 0 ? value.y : -value.x;
			if ( _capture is null || !float.IsFinite( delta ) || delta == 0 ) return;
			ZoomAt( Math.Pow( 1.2, Math.Clamp( delta, -20, 20 ) ), anchor, !_wheelZooming || _sinceWheelZoom > 0.3f );
			_wheelZooming = true;
			_sinceWheelZoom = 0;
			return;
		}
		_wheelZooming = false;
		var shiftPan = modifiers.Contains( KeyboardModifiers.Shift ) && value.x == 0;
		var pan = shiftPan ? value.y : value.x;
		if ( _capture is not null && float.IsFinite( pan ) && pan != 0 )
		{
			var duration = _end - _start;
			var start = Math.Clamp( _start - pan * duration * 0.1, 0, Math.Max( 0, _capture.DurationMs - duration ) );
			SetRange( start, start + duration, remember: false );
		}
		if ( !shiftPan ) base.OnMouseWheel( value.WithX( 0 ) );
	}

	void Zoom( double factor )
	{
		ZoomAt( factor, 0.5, true );
	}

	void ZoomAt( double factor, double anchor, bool remember )
	{
		if ( _capture is null ) return;
		if ( !double.IsFinite( factor ) || !double.IsFinite( anchor ) ) return;
		anchor = Math.Clamp( anchor, 0, 1 );
		var next = Math.Clamp( (_end - _start) * factor, 0.001, Math.Max( 0.001, _capture.DurationMs ) );
		var start = Math.Clamp( _start + ((_end - _start) - next) * anchor, 0, Math.Max( 0, _capture.DurationMs - next ) );
		SetRange( start, start + next, remember );
	}
	internal static Color SiteColor( Profiler.Site site )
	{
		if ( DevToolsProfilerGc.IsPauseSite( site ) ) return DevToolsProfilerGc.Color( Profiler.GcPauseKind( site.Name ) );
		// Stable across runs, unlike runtime-assigned site IDs or randomized string hashes.
		uint hash = 2166136261;
		foreach ( var c in site.Name ) hash = (hash ^ c) * 16777619;
		return DevToolsStyles.Series[(int)(hash % (uint)DevToolsStyles.Series.Length)];
	}

	void PrepareVisibleSpans( float width )
	{
		if ( _drawWidth == width ) return;
		_drawWidth = width;
		_visible.Clear();
		_activity.Clear();
		DevToolsProfilerGc.Marks( _capture, _start, _end, width, _gcMarks );
		foreach ( var (key, spans) in _threads )
		{
			if ( !_threadY.ContainsKey( key ) || _collapsed.Contains( key ) ) continue;
			BuildLane( spans.AsSpan(), _start, _end, width, out var visible, out var activity );
			_visible[key] = visible;
			_activity[key] = activity;
		}
	}

	internal readonly record struct ActivityMark( int Pixel, int Depth, int Count, double StartMs, double EndMs );

	internal static void BuildLane( ReadOnlySpan<Profiler.Span> spans, double start, double end, float width,
		out List<Profiler.Span> visible, out List<ActivityMark> activity )
	{
		visible = new();
		activity = new();
		if ( width <= 0 || end <= start ) return;
		var marks = new Dictionary<(int Pixel, int Depth), ActivityMark>();
		foreach ( var span in spans )
		{
			if ( span.StartMs >= end ) break;
			if ( span.EndMs < start || span.Depth > 63 ) continue;
			var left = (Math.Max( start, span.StartMs ) - start) / (end - start) * width;
			var right = (Math.Min( end, span.EndMs ) - start) / (end - start) * width;
			if ( right - left < 0.1 ) continue;
			if ( right - left >= 0.5 ) { visible.Add( span ); continue; }

			var pixel = Math.Clamp( (int)((left + right) * 0.5), 0, Math.Max( 0, (int)Math.Ceiling( width ) - 1 ) );
			var key = (pixel, span.Depth);
			marks[key] = marks.TryGetValue( key, out var mark )
				? mark with { Count = mark.Count + 1, StartMs = Math.Min( mark.StartMs, span.StartMs ), EndMs = Math.Max( mark.EndMs, span.EndMs ) }
				: new( pixel, span.Depth, 1, span.StartMs, span.EndMs );
		}
		activity.AddRange( marks.Values );
	}

	public override void OnDraw( Painter painter )
	{
		using var state = painter.Scope();
		painter.Scale( ScaleToScreen );
		painter.Stroke = Stroke.None;
		_hits.Clear();
		_activityHits.Clear();
		var width = Box.Rect.Width / ScaleToScreen;
		var mouse = MousePosition / ScaleToScreen;
		if ( _capture is null || width < 100 ) return;
		PrepareVisibleSpans( width );
		var range = Math.Max( 0.001, _end - _start );
		var ticks = Math.Max( 1, (int)(width / 120) );
		for ( int i = 0; i <= ticks; i++ )
		{
			painter.Fill = DevToolsStyles.Line.WithAlpha( 0.5f );
			painter.Rect( new Rect( width * i / ticks, 0, 1, Box.Rect.Height / ScaleToScreen ) );
		}
		foreach ( var thread in _capture.Threads )
		{
			if ( !_threadY.TryGetValue( thread.Key, out var y ) ) continue;
			painter.Fill = DevToolsStyles.Raised;
			painter.Rect( new Rect( 0, y, width, HeaderHeight ) );
			Text( painter, $"{(_collapsed.Contains( thread.Key ) ? "+" : "−")}  {ThreadLabel( thread.Key )}", new Rect( 5, y, width - 10, HeaderHeight ), DevToolsStyles.Text );
			if ( _collapsed.Contains( thread.Key ) || !_visible.TryGetValue( thread.Key, out var spans ) ) continue;
			foreach ( var mark in _activity[thread.Key] )
			{
				var rect = new Rect( mark.Pixel, y + HeaderHeight + mark.Depth * RowHeight, 1, RowHeight - 1 );
				painter.Fill = DevToolsStyles.Dim.WithAlpha( 0.85f );
				painter.Rect( rect );
				_activityHits.Add( (rect, mark) );
			}
			foreach ( var span in spans )
			{
				if ( span.StartMs > _end ) break;
				if ( span.EndMs < _start || span.Depth > 63 ) continue;
				var x = (float)((Math.Max( _start, span.StartMs ) - _start) / range * width);
				var right = (float)((Math.Min( _end, span.EndMs ) - _start) / range * width);
				var rect = new Rect( x, y + HeaderHeight + span.Depth * RowHeight, Math.Max( 0.75f, right - x - 1 ), RowHeight - 1 );
				var focused = span.Index == _focused;
				var hovered = HasHovered && rect.IsInside( mouse );
				var color = _colors[span.ScopeId];
				// Opaque, so grid lines behind don't show through the labels
				if ( focused || hovered ) color = color.LerpTo( Color.White, focused ? 0.3f : 0.2f );
				else color = color.LerpTo( DevToolsStyles.Plot, span.Incomplete ? 0.7f : 0.2f );
				painter.Fill = color.WithAlpha( 1 );
				painter.Rect( rect );
				if ( focused )
				{
					painter.Fill = Color.White;
					painter.Rect( new Rect( rect.Left, rect.Top, rect.Width, 1 ) );
					painter.Rect( new Rect( rect.Left, rect.Bottom - 1, rect.Width, 1 ) );
					painter.Rect( new Rect( rect.Left, rect.Top, 1, rect.Height ) );
					painter.Rect( new Rect( rect.Right - 1, rect.Top, 1, rect.Height ) );
				}
				if ( span.ScopeId == _selected ) { painter.Fill = Color.White; painter.Rect( new Rect( rect.Left, rect.Bottom - 1, rect.Width, 1 ) ); }
				_hits.Add( (rect, span) );
				var nameText = _capture.InstanceName( span, _sites[span.ScopeId].Name );
				if ( rect.Width >= 10 )
				{
					using var labelState = painter.Scope();
					painter.Clip( new Rect( rect.Left + 2, rect.Top, rect.Width - 4, rect.Height ) );
					Text( painter, nameText, new Rect( rect.Left + 2, rect.Top, rect.Width - 4, rect.Height ), Color.White );
				}
			}
		}
		if ( CursorFraction is { } cursor )
		{
			painter.Fill = CursorColor;
			painter.Rect( new Rect( cursor * width, 0, 1, Box.Rect.Height / ScaleToScreen ) );
		}
	}

	bool IsSelectedPause( DevToolsProfilerGc.Mark mark ) => _focused >= 0 && _capture.Spans[_focused] is { ThreadKey: Profiler.GcThreadKey } span
		&& span.StartMs >= mark.StartMs && span.StartMs <= mark.EndMs;

	void UpdateHoverTooltip( Vector2 mouse )
	{
		Tooltip = null;
		foreach ( var (rect, mark) in _activityHits )
		{
			if ( !rect.IsInside( mouse ) ) continue;
			Tooltip = $"{mark.Count:N0} short scope{(mark.Count == 1 ? "" : "s")} at this pixel\n{mark.StartMs:0.000}–{mark.EndMs:0.000} ms";
			break;
		}
		for ( int i = _hits.Count - 1; i >= 0; i-- )
		{
			var (rect, span) = _hits[i];
			if ( !rect.IsInside( mouse ) ) continue;
			var site = _sites[span.ScopeId];
			Tooltip = $"{_capture.InstanceName( span, site.Name )}\n{span.DurationMs:0.000} ms · self {(span.SelfMs is { } self ? $"{self:0.000} ms" : "unavailable")}{(span.Incomplete ? " · unfinished" : "")}\n{Profiler.SourceLocation( site )}{DevToolsProfilerGc.OverlapLine( _capture, span.StartMs, span.EndMs )}";
			break;
		}
	}

	sealed class TimelineDivider : Panel
	{
		readonly Panel _lanes;
		bool _dragging;
		float _startY, _startHeight;

		internal TimelineDivider( Panel lanes )
		{
			_lanes = lanes;
			AddClass( "profiler-timeline-divider" );
		}

		protected override void OnMouseDown( MousePanelEvent e )
		{
			if ( e.MouseButton != MouseButtons.Left ) return;
			e.StopPropagation();
			_dragging = true;
			_startY = FindRootPanel().MousePosition.y * ScaleFromScreen;
			_startHeight = _lanes.Box.Rect.Height * ScaleFromScreen;
		}

		protected override void OnMouseUp( MousePanelEvent e )
		{
			if ( e.MouseButton == MouseButtons.Left ) _dragging = false;
		}

		public override void Tick()
		{
			base.Tick();
			if ( !HasActive ) _dragging = false;
			SetClass( "dragging", _dragging );
			if ( !_dragging ) return;
			var root = FindRootPanel();
			var maximum = Math.Max( 80, (root.Box.Rect.Bottom - _lanes.Box.Rect.Top) * ScaleFromScreen - 150 );
			var height = Math.Clamp( _startHeight + root.MousePosition.y * ScaleFromScreen - _startY, 80, maximum );
			_lanes.Style.Height = height;
			_lanes.Style.MaxHeight = height;
		}
	}

	internal static string FormatTimestamp( double milliseconds )
	{
		var microseconds = (long)Math.Round( Math.Max( 0, milliseconds ) * 1000 );
		var seconds = microseconds / 1_000_000;
		var ms = microseconds / 1000 % 1000;
		var us = microseconds % 1000;
		return seconds > 0 ? $"{seconds} s {ms:000} ms {us:000} µs"
			: ms > 0 ? $"{ms} ms {us:000} µs" : $"{us} µs";
	}

	sealed class TimeRuler : Panel
	{
		readonly DevToolsProfilerTimeline _timeline;

		internal TimeRuler( DevToolsProfilerTimeline timeline )
		{
			_timeline = timeline;
			AddClass( "profiler-time-ruler" );
			Style.Height = TickHeight;
		}

		public override void OnMouseWheel( Vector2 value ) => _timeline.Wheel( value, MousePosition.x / Math.Max( 1, _timeline.Box.Rect.Width ) );

		public override void OnDraw( Painter painter )
		{
			if ( _timeline._capture is null ) return;
			using var state = painter.Scope();
			painter.Scale( ScaleToScreen );
			painter.Stroke = Stroke.None;
			var width = _timeline.Box.Rect.Width / ScaleToScreen;
			if ( width < 100 ) return;
			// The ruler draws first: build this frame's GC marks before the strip uses them
			_timeline.PrepareVisibleSpans( width );
			var ticks = Math.Max( 1, (int)(width / 120) );
			// One decimal count for every label, enough to tell neighbouring ticks apart
			var step = (_timeline._end - _timeline._start) / ticks;
			var format = TickFormats[Math.Clamp( (int)Math.Ceiling( -Math.Log10( Math.Max( step, 1e-6 ) ) ) + 1, 0, TickFormats.Length - 1 )];
			for ( int i = 0; i <= ticks; i++ )
			{
				var x = width * i / ticks;
				painter.Fill = DevToolsStyles.Line;
				painter.Rect( new Rect( x, TickHeight - 5, 1, 5 ) );
				if ( i == ticks ) continue;
				_timeline.Text( painter, $"{(_timeline._start + step * i).ToString( format )} ms",
					new Rect( x + 3, 0, width / ticks - 6, TickHeight - 2 ), DevToolsStyles.Dim );
				for ( int minor = 1; minor < 4; minor++ )
					painter.Rect( new Rect( x + width / ticks * minor / 4, TickHeight - 3, 1, 3 ) );
			}
			DrawGcStrip( painter );
			if ( _timeline.CursorFraction is { } cursor )
			{
				var x = cursor * width;
				var timestamp = FormatTimestamp( _timeline._start + (_timeline._end - _timeline._start) * cursor );
				var labelWidth = Math.Min( timestamp.Length * 7 + 8, width );
				var label = new Rect( Math.Clamp( x, 0, width - labelWidth ), 0, labelWidth, TickHeight );
				painter.Fill = CursorColor;
				painter.Rect( new Rect( x, 0, 1, Box.Rect.Height / ScaleToScreen ) );
				painter.Rect( label );
				_timeline.Text( painter, timestamp,
					new Rect( label.Left + 4, 0, label.Width - 8, TickHeight ), DevToolsStyles.Plot );
			}
		}

		static readonly string[] TickFormats = ["0", "0.0", "0.00", "0.000", "0.0000", "0.00000"];

		// Pinned under the ticks, so pauses stay visible whatever the lanes are scrolled to or filtered by
		void DrawGcStrip( Painter painter )
		{
			var marks = _timeline._gcMarks;
			var hits = _timeline._gcHits;
			hits.Clear();
			var mouse = MousePosition / ScaleToScreen;
			// Names the row; drawn first, so pause marks cover it
			if ( marks.Count > 0 )
				DebugOverlay.DrawText( painter, new TextRendering.Scope( "GC", DevToolsStyles.Faint, 9 / ScaleToScreen, "Inter", 600 ), new Rect( 3, TickHeight, 20, GcStripHeight ), TextFlag.LeftCenter );
			for ( int i = 0; i < marks.Count; i++ )
			{
				var mark = marks[i];
				var width = mark.Right - mark.Left;
				var rect = new Rect( mark.Left, TickHeight, width, GcStripHeight );
				var hit = new Rect( mark.Left, TickHeight, width, GcStripHeight ).Grow( 2, 0 );
				var hovered = HasHovered && hit.IsInside( mouse );
				hits.Add( (hit, mark) );
				var fill = DevToolsProfilerGc.Color( mark.Kind ).LerpTo( Color.White, hovered ? 0.2f : 0 );
				painter.Fill = fill;
				painter.Rect( rect );
				if ( _timeline.IsSelectedPause( mark ) )
				{
					painter.Fill = Color.White;
					painter.Rect( new Rect( rect.Left, rect.Top, rect.Width, 1 ) );
					painter.Rect( new Rect( rect.Left, rect.Bottom - 1, rect.Width, 1 ) );
					painter.Rect( new Rect( rect.Left, rect.Top, 1, rect.Height ) );
					painter.Rect( new Rect( rect.Right - 1, rect.Top, 1, rect.Height ) );
				}
				var label = width >= mark.Label.Length * 5.8f + 8 ? mark.Label : width >= mark.ShortLabel.Length * 5.8f + 8 ? mark.ShortLabel : null;
				if ( label is null ) continue;
				using var clip = painter.Scope();
				painter.Clip( rect.Shrink( 4, 0 ) );
				_timeline.Text( painter, label, rect.Shrink( 4, 0 ), DevToolsProfilerGc.TextOn( fill ) );
			}
		}

		DevToolsProfilerGc.Mark? HitMark( Vector2 position )
		{
			foreach ( var (rect, mark) in _timeline._gcHits )
				if ( rect.IsInside( position ) ) return mark;
			return null;
		}

		protected override void OnClick( MousePanelEvent e )
		{
			if ( HitMark( e.LocalPosition / ScaleToScreen ) is { } mark ) _timeline.InspectInstance( mark.Longest );
		}

		protected override void OnDoubleClick( MousePanelEvent e )
		{
			if ( HitMark( e.LocalPosition / ScaleToScreen ) is not { } mark ) return;
			var padding = Math.Max( 0.05, (mark.EndMs - mark.StartMs) * 0.5 );
			_timeline.SetRange( mark.StartMs - padding, mark.EndMs + padding );
		}

		public override bool HasTooltip
		{
			get
			{
				var capture = _timeline._capture;
				var position = MousePosition / ScaleToScreen;
				Tooltip = capture is null || _timeline._gcMarks.Count == 0 || position.y < TickHeight ? null
					: HitMark( position ) is not { } mark ? "GC and runtime pauses. Click one to inspect."
					: mark.Count == 1 ? $"{DevToolsProfilerGc.Describe( capture, capture.Spans[mark.Longest], _timeline._sites[capture.Spans[mark.Longest].ScopeId].Name )}\nClick to inspect, double-click to zoom"
					: $"{mark.Count} pauses · {mark.TotalMs:0.000} ms in all · longest {capture.Spans[mark.Longest].DurationMs:0.000} ms\n{mark.StartMs:0.000}–{mark.EndMs:0.000} ms\nClick for the longest, double-click to zoom";
				return base.HasTooltip;
			}
		}
	}
	void Text( Painter painter, string text, Rect rect, Color color ) =>
		DebugOverlay.DrawText( painter, new TextRendering.Scope( text, color, 10.5f / ScaleToScreen, "Inter", 500 ), rect, TextFlag.LeftCenter );
}

/// <summary>
/// GC pauses of a capture for the profiler views. Captures without GC data have no pauses, never "0 pauses".
/// </summary>
internal static class DevToolsProfilerGc
{
	static readonly string[] Labels = ["Gen 0 GC pause", "Gen 1 GC pause", "Gen 2 GC pause", "Background GC pause (stop-the-world part)", "Runtime suspend (not a GC)"];
	static readonly string[] KindNames = ["Gen 0", "Gen 1", "Gen 2", "Background", "Suspend"];
	static readonly string[] SeverityNames = ["", "runtime suspend", "Gen 0/1 or background", "Gen 2"];

	internal static ArraySegment<Profiler.Span> Pauses( Profiler.Capture capture ) =>
		capture?.ThreadSpans.GetValueOrDefault( Profiler.GcThreadKey, ArraySegment<Profiler.Span>.Empty ) ?? ArraySegment<Profiler.Span>.Empty;

	// Pauses are sorted and don't overlap, so their ends are sorted too
	internal static int First( ArraySegment<Profiler.Span> pauses, double ms )
	{
		int lo = 0, hi = pauses.Count;
		while ( lo < hi )
		{
			var mid = (lo + hi) / 2;
			if ( pauses[mid].EndMs <= ms ) lo = mid + 1;
			else hi = mid;
		}
		return lo;
	}

	/// <summary>3 for Gen 2, 2 for the other collections, 1 for a runtime suspend that isn't a GC.</summary>
	internal static int Severity( string scopeName ) => KindSeverity( Profiler.GcPauseKind( scopeName ) );
	static int KindSeverity( int kind ) => kind switch { 2 => 3, 4 => 1, _ => 2 };
	internal static Color Color( int kind ) => DevToolsStyles.GcKinds[Math.Clamp( kind, 0, DevToolsStyles.GcKinds.Length - 1 )];
	internal static Color TextOn( Color fill ) => fill.Luminance > 0.4f ? DevToolsStyles.Plot : DevToolsStyles.Bright;
	internal static string Label( string scopeName ) => Profiler.GcPauseKind( scopeName ) is >= 0 and var kind ? Labels[kind] : scopeName;
	internal static string SeverityName( int severity ) => SeverityNames[Math.Clamp( severity, 0, SeverityNames.Length - 1 )];
	internal static bool IsPauseSite( Profiler.Site site ) => site is not null && Profiler.GcPauseKind( site.Name ) >= 0;

	static int Severity( Profiler.Capture capture, Profiler.Span pause ) =>
		capture.Sites.FirstOrDefault( x => x.Id == pause.ScopeId ) is { } site ? Severity( site.Name ) : 1;

	internal static string Duration( double ms ) => ms >= 10 ? $"{ms:0.0} ms" : ms >= 1 ? $"{ms:0.00} ms" : $"{ms * 1000:0} µs";

	/// <summary>
	/// Pauses close together on screen merge into one mark, at least 2 px wide, with a long and a short label.
	/// </summary>
	internal readonly record struct Mark( float Left, float Right, int Count, double TotalMs, int Kind, int Longest, double StartMs, double EndMs, string Label, string ShortLabel );

	internal static void Marks( Profiler.Capture capture, double startMs, double endMs, float width, List<Mark> marks )
	{
		marks.Clear();
		var pauses = Pauses( capture );
		if ( pauses.Count == 0 || width <= 0 ) return;
		var kinds = capture.Sites.ToDictionary( x => x.Id, x => Profiler.GcPauseKind( x.Name ) );
		var range = Math.Max( 0.001, endMs - startMs );
		for ( int i = First( pauses, startMs ); i < pauses.Count && pauses[i].StartMs < endMs; i++ )
		{
			var span = pauses[i];
			var left = (float)((Math.Max( startMs, span.StartMs ) - startMs) / range * width);
			var right = Math.Max( left + 2, (float)((Math.Min( endMs, span.EndMs ) - startMs) / range * width) );
			var kind = kinds.GetValueOrDefault( span.ScopeId, 4 );
			// Only narrow marks merge, so a wide pause keeps its own segment and label
			var narrow = right - left < 6;
			if ( marks.Count > 0 && marks[^1] is var last && left <= last.Right + 2 && narrow && last.Right - last.Left < 6 )
			{
				var count = last.Count + 1;
				var total = last.TotalMs + span.DurationMs;
				var longest = span.DurationMs > capture.Spans[last.Longest].DurationMs ? span.Index : last.Longest;
				marks[^1] = last with { Right = Math.Max( last.Right, right ), Count = count, TotalMs = total, Kind = KindSeverity( kind ) > KindSeverity( last.Kind ) ? kind : last.Kind, Longest = longest, EndMs = span.EndMs, Label = $"{count} pauses · {Duration( total )}", ShortLabel = $"×{count}" };
				continue;
			}
			if ( marks.Count > 0 && left < marks[^1].Right )
			{
				left = marks[^1].Right;
				right = Math.Max( right, left + 2 );
			}
			var name = kind >= 0 ? KindNames[kind] : "Pause";
			marks.Add( new( left, right, 1, span.DurationMs, kind, span.Index, span.StartMs, span.EndMs, $"{name} · {Duration( span.DurationMs )}", name ) );
		}
	}

	/// <summary>
	/// Pauses overlapping a time range, the overlapped time, and the worst pause kind.
	/// </summary>
	internal static (int Count, double Ms, int Worst) Overlap( Profiler.Capture capture, double start, double end )
	{
		var pauses = Pauses( capture );
		int count = 0, worst = 0;
		double ms = 0;
		for ( int i = First( pauses, start ); i < pauses.Count && pauses[i].StartMs < end; i++ )
		{
			count++;
			ms += Math.Min( end, pauses[i].EndMs ) - Math.Max( start, pauses[i].StartMs );
			worst = Math.Max( worst, Severity( capture, pauses[i] ) );
		}
		return (count, ms, worst);
	}

	internal static string OverlapLine( Profiler.Capture capture, double start, double end )
	{
		var (count, ms, worst) = Overlap( capture, start, end );
		return count == 0 ? "" : count == 1 ? $"\nOverlaps a GC pause: {ms:0.000} ms ({SeverityNames[worst]})"
			: $"\nOverlaps {count} GC pauses: {ms:0.000} ms, worst {SeverityNames[worst]}";
	}

	internal static Profiler.Frame? FrameAt( Profiler.Capture capture, double ms )
	{
		var frames = capture.Frames;
		int lo = 0, hi = frames.Length - 1, found = -1;
		while ( lo <= hi )
		{
			var mid = (lo + hi) / 2;
			if ( frames[mid].StartMs <= ms ) { found = mid; lo = mid + 1; }
			else hi = mid - 1;
		}
		return found >= 0 && frames[found].EndMs >= ms ? frames[found] : null;
	}

	internal static string Describe( Profiler.Capture capture, Profiler.Span pause, string scopeName ) =>
		$"{Label( scopeName )}\n{pause.DurationMs:0.000} ms · {pause.StartMs:0.000}–{pause.EndMs:0.000} ms" +
		(FrameAt( capture, pause.StartMs ) is { } frame ? $" · frame {frame.Index + 1} ({frame.DurationMs:0.000} ms)" : "");

	/// <summary>
	/// "GC 3 pauses, 4.21 ms", then runtime suspends on their own; empty without pauses.
	/// </summary>
	internal static string Summary( Profiler.Capture capture, double startMs = double.NegativeInfinity, double endMs = double.PositiveInfinity )
	{
		var pauses = Pauses( capture );
		var kinds = capture.Sites.ToDictionary( x => x.Id, x => Profiler.GcPauseKind( x.Name ) );
		int gc = 0, suspends = 0;
		double gcMs = 0, suspendMs = 0;
		for ( int i = First( pauses, startMs ); i < pauses.Count && pauses[i].StartMs < endMs; i++ )
		{
			var ms = Math.Min( endMs, pauses[i].EndMs ) - Math.Max( startMs, pauses[i].StartMs );
			if ( kinds.GetValueOrDefault( pauses[i].ScopeId, 4 ) == 4 ) { suspends++; suspendMs += ms; }
			else { gc++; gcMs += ms; }
		}
		var text = gc > 0 ? $"GC {gc:N0} pause{(gc == 1 ? "" : "s")}, {gcMs:0.00} ms" : "";
		if ( suspends > 0 ) text += $"{(text.Length > 0 ? " · " : "")}{suspends:N0} runtime suspend{(suspends == 1 ? "" : "s")}, {suspendMs:0.00} ms";
		return text;
	}
}
