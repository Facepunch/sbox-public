using Sandbox.Engine;
using Sandbox.Engine.Settings;
using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// Main thread time: frame pacing, what's capping the frame rate, the categories and every named scope - over the
/// analysis window, or for the frame picked in a chart. Clicking a category shows the scopes inside it, as a tree.
/// </summary>
internal sealed class DevToolsCpuTab : DevToolsTab
{
	public override string TabId => "cpu";
	public override string Title => "CPU";

	readonly DevToolsFacts _facts;
	readonly DevToolsFacts _caps;
	readonly DevToolsTable _categories, _scopes;
	readonly Label _scopesHint;
	readonly List<DevTools.ScopeCost> _costs = new();
	PerformanceStats.Timings _category;

	const string Columns = "minmax(0, 1fr) 52px 52px 48px 56px";

	public DevToolsCpuTab()
	{
		_facts = AddChild( new DevToolsFacts() );

		var pacing = AddChild( new DevToolsSection( "Frame time distribution" ) );
		pacing.Body.AddChild( new DevToolsHistogram() );
		_caps = AddChild( new DevToolsFacts() );

		var split = AddChild( new Panel( null, "split span-all" ) );
		_categories = split.AddChild( new Panel( null, "section" ) ).AddChild( new DevToolsTable( Columns, "Categories", "#ms", "#max", "#calls", "" ).Banded() );
		var scopes = split.AddChild( new Panel( null, "section" ) );
		_scopes = scopes.AddChild( new DevToolsTable( Columns, "Scopes", "#ms", "#max", "#calls", "" ).Banded() );
		_scopesHint = scopes.AddChild( new Label( "", "hint" ) );
	}

	public override void Refresh( DevTools.Summary s )
	{
		// A picked frame's own numbers, else the window's
		var age = DevTools.SelectedAge;
		var frames = DevTools.ShownFrames;
		var picked = age >= 0;

		_facts.Set( "Showing", picked ? $"frame {frames.Number( age )}" : $"avg of {s.WindowMs / 1000:0.0} s",
			tooltip: picked ? "The frame picked in a chart. Pick it again, or resume, to go back to the averages" : $"The last {s.Frames} frames" );
		_facts.Set( "CPU work", $"{(picked ? frames.BusyMs( age ) : s.CpuWorkAvg):0.00} ms", !picked && s.Limiter == DevTools.Limiter.Cpu && !s.MeetingTarget ? s.Rating : null,
			"The frame less its wait on the frame rate cap and what the dev tools took - which can still include time blocked on something" );
		_facts.Set( "Cap wait", $"{(picked ? frames.IdleMs( age ) : s.CapWaitAvg):0.00} ms" );
		if ( picked )
		{
			_facts.Set( "Frame", $"{frames.FrameMs( age ):0.00} ms", frames.FrameMs( age ) > s.HitchMs ? DevTools.Severity.Warning : null );
			foreach ( var key in (string[])["Frame p50", "Frame max", "Std dev", "Not presented"] ) _facts.Hide( key );
		}
		else
		{
			_facts.Hide( "Frame" );
			_facts.Set( "Frame p50", $"{s.FrameMedian:0.00} ms" );
			_facts.Set( "Frame max", $"{s.FrameMax:0.0} ms", s.FrameMax > s.HitchMs ? DevTools.Severity.Warning : null );
			_facts.Set( "Std dev", $"{s.FrameStdDev:0.00} ms", s.FrameStdDev > s.TargetMs * 0.25f ? DevTools.Severity.Warning : null, "How far frame times spread from the average" );
			_facts.Set( "Not presented", $"{s.NotRendered * 100:0}%", s.NotRendered > 0.05f ? DevTools.Severity.Warning : null, "Loop iterations that didn't present a frame" );
		}

		// What decides the cap, so a frame rate stuck at a round number can be explained
		var rs = RenderSettings.Instance;
		var surface = GameSurface.Current;
		var limit = EngineLoop.FrameRateLimit;
		_caps.Set( "Capped by", limit.FramesPerSecond > 0 ? limit.Source : "nothing", tooltip: limit.FramesPerSecond > 0 ? $"{limit.FramesPerSecond:0} fps" : null );
		_caps.Set( "Display", rs.Fullscreen ? "exclusive" : rs.Borderless ? "borderless" : "windowed" );
		_caps.Set( "VSync", (surface?.VSync ?? rs.VSync) ? $"on, {surface?.RefreshRate ?? 0:0} Hz" : "off" );
		_caps.Set( "fps_max", $"{rs.MaxFrameRate}" );
		_caps.Set( "fps_max_menu", $"{rs.MaxFrameRateMenu}" );
		_caps.Set( "fps_max_inactive", $"{rs.MaxFrameRateInactive}" );

		DevTools.GetScopes( _costs, age );
		var total = picked ? frames.BusyMs( age ) : s.CpuWorkAvg;
		RefreshCategories( total, picked );
		RefreshScopes( total, picked );
	}

	static bool IsCategory( PerformanceStats.Timings timing ) => Array.IndexOf( DevTools.Categories, timing ) >= 0;

	DevToolsTable.Cell[] Cells( in DevTools.ScopeCost cost, float total, bool picked, float indent = 10 ) =>
	[
		new( cost.Name, null, DevToolsStyles.Category( cost.Timing ), Indent: indent ),
		DevToolsTable.Number( DevToolsFormat.Ms( cost.AvgMs ) ),
		picked ? new( "", "number" ) : new( DevToolsFormat.Ms( cost.MaxMs ), "number dim" ),
		new( cost.Calls < 0.05f ? "" : cost.Calls >= 10 ? $"{cost.Calls:0}" : $"{cost.Calls:0.#}", "number dim" ),
		DevToolsTable.BarOf( total > 0 ? cost.AvgMs / total : 0 ),
	];

	/// <summary>
	/// The main thread's categories, biggest first. Clicking one shows what's inside it; clicking it again shows every scope.
	/// </summary>
	void RefreshCategories( float total, bool picked )
	{
		var t = _categories;
		t.Begin();
		foreach ( var cost in _costs )
		{
			if ( !IsCategory( cost.Timing ) ) continue;
			var timing = cost.Timing;
			t.Row( cost.Name, Cells( cost, total, picked ), () =>
			{
				_category = _category == timing ? null : timing;
				Refresh( DevTools.Current );
			}, $"{cost.Name}: click for the scopes inside it", _category == timing ? "selected" : null );
		}
		t.End( "Nothing measured" );
	}

	/// <summary>
	/// Every scope with time in it, biggest first - or, with a category picked, the scopes inside it as a tree, biggest
	/// first among siblings. Nesting is what each scope was first seen opened inside (<c>Timings.Parent</c>).
	/// </summary>
	void RefreshScopes( float total, bool picked )
	{
		var t = _scopes;
		t.Begin();

		if ( _category is null )
		{
			foreach ( var cost in _costs.Take( 60 ) )
				if ( !IsCategory( cost.Timing ) ) t.Row( cost.Name, Cells( cost, total, picked ), tooltip: Path( cost.Timing ) );
			t.End( "Nothing measured" );
			_scopesHint.Text = "Scopes nest, so these overlap. Click a category for what's inside it";
			return;
		}

		var byTiming = _costs.ToDictionary( x => x.Timing );
		void Add( PerformanceStats.Timings parent, int depth )
		{
			foreach ( var cost in _costs.Where( x => x.Timing.Parent == parent ) )
			{
				t.Row( cost.Name, Cells( cost, total, picked, 10 + depth * 14 ), tooltip: Path( cost.Timing ) );
				if ( depth < 12 ) Add( cost.Timing, depth + 1 );
			}
		}

		if ( byTiming.TryGetValue( _category, out var category ) ) t.Row( _category.Name, Cells( category, total, picked ), rowClass: "heading" );
		Add( _category, 1 );
		t.End( $"Nothing measured inside {_category.Name}" );
		_scopesHint.Text = $"What's inside {_category.Name}: what each scope was first seen opened in. Click {_category.Name} again for every scope";
	}

	/// <summary>
	/// A scope and what it's inside, outermost first.
	/// </summary>
	static string Path( PerformanceStats.Timings timing )
	{
		var names = new List<string>();
		for ( var t = timing; t is not null && names.Count < 16; t = t.Parent ) names.Add( t.Name );
		names.Reverse();
		return string.Join( " / ", names );
	}
}

/// <summary>
/// Managed heap and GC, process and video memory, streaming, what's allocating, and what the resource caches hold - and,
/// from a baseline set with a click, what's changed since: what grew after doing something.
/// </summary>
internal sealed class DevToolsMemoryTab : DevToolsTab
{
	public override string TabId => "memory";
	public override string Title => "Memory";

	readonly DevToolsFacts _facts;
	readonly DevToolsSection _sinceSection;
	readonly DevToolsFacts _since;
	readonly Label _baselineButton, _clearButton;
	readonly DevToolsSection _heapSection;
	readonly DevToolsFacts _heap;
	readonly DevToolsSection _allocSection;
	readonly DevToolsTable _allocs;
	readonly DevToolsTable _strong, _weak, _native;

	/// <summary>
	/// What memory looked like when the baseline was set.
	/// </summary>
	sealed class Baseline
	{
		public double At;
		public long Process, Heap, Allocated, Vram, TexturePool;
		public int Gen0, Gen1, Gen2;
		public TimeSpan Paused;
		public Dictionary<string, int> Strong, Weak, Native;
	}

	Baseline _baseline;

	const string ResourceColumns = "minmax(0, 1fr) 56px 56px 56px";

	public DevToolsMemoryTab()
	{
		// The headline numbers, across the top. Video memory has its own tab
		_facts = AddChild( new DevToolsFacts() );
		_facts.AddClass( "span-all" );

		// Side by side. Set a baseline, do something, and see what grew
		var managed = AddChild( new Panel( null, "split span-all" ) );
		_sinceSection = managed.AddChild( new DevToolsSection( "Since the baseline" ) );
		_baselineButton = _sinceSection.Head.AddChild( new Label( "set baseline", "head-button" ) );
		_baselineButton.AddEventListener( "onclick", SetBaseline );
		_clearButton = _sinceSection.Head.AddChild( new Label( "clear", "head-button" ) );
		_clearButton.AddEventListener( "onclick", () => { _baseline = null; Refresh( DevTools.Current ); } );
		_since = _sinceSection.Body.AddChild( new DevToolsFacts() );

		// Generations are detail: folded away
		_heapSection = managed.AddChild( new DevToolsSection( "Managed heap, at the last GC" ) );
		_heap = new DevToolsFacts();
		_heapSection.Collapsible( body => body.AddChild( _heap ) );

		_allocSection = AddChild( new DevToolsSection( "Allocations by type" ) );
		_allocs = _allocSection.Body.AddChild( new DevToolsTable( "minmax(0, 1fr) 72px 64px 64px", "type", "#bytes", "#samples", "" ) );

		var split = AddChild( new Panel( null, "split" ) );
		_strong = split.AddChild( new Panel( null, "section" ) ).AddChild( new DevToolsTable( ResourceColumns, "Resource index", "#live", "#change", "#max" ).Banded() );
		_weak = split.AddChild( new Panel( null, "section" ) ).AddChild( new DevToolsTable( ResourceColumns, "Weak index", "#live", "#change", "#max" ).Banded() );
		_native = split.AddChild( new Panel( null, "section span-all" ) ).AddChild( new DevToolsTable( ResourceColumns, "Native resource cache", "#live", "#change", "#max" ).Banded() );
	}

	/// <summary>
	/// Remember memory as it is now. Allocations by type start again from here too.
	/// </summary>
	void SetBaseline()
	{
		var stats = FrameStats.Current;
		DebugOverlay.Resources.Update();
		_baseline = new Baseline
		{
			At = RealTime.Now,
			Process = (long)PerformanceStats.ApproximateProcessMemoryUsage,
			Heap = GC.GetTotalMemory( false ),
			Allocated = GC.GetTotalAllocatedBytes( false ),
			Vram = (long)GpuProfilerStats.VideoMemoryUsed,
			TexturePool = (long)stats.TexturePoolUsedBytes,
			Gen0 = GC.CollectionCount( 0 ),
			Gen1 = GC.CollectionCount( 1 ),
			Gen2 = GC.CollectionCount( 2 ),
			Paused = GC.GetTotalPauseDuration(),
			Strong = new( DebugOverlay.Resources.Stats.StrongIndex ?? new() ),
			Weak = new( DebugOverlay.Resources.Stats.WeakIndexEntries ?? new() ),
			Native = new( DebugOverlay.Resources.NativeStats.Entries ?? new() ),
		};

		DebugOverlay.Allocations.Disabled();
		Refresh( DevTools.Current );
	}

	/// <summary>
	/// What's changed since the baseline, as a signed size.
	/// </summary>
	static string Change( double bytes ) => bytes >= 0 ? $"+{DevToolsFormat.Bytes( bytes )}" : $"-{DevToolsFormat.Bytes( -bytes )}";

	void RefreshSince( FrameStats stats )
	{
		_clearButton.SetClass( "hidden", _baseline is null );
		_baselineButton.Text = _baseline is null ? "set baseline" : "set again";

		var b = _baseline;
		if ( b is null )
		{
			_since.Set( "Baseline", "none", tooltip: "Set one, do something, and see what grew" );
			foreach ( var key in (string[])["Process", "Managed heap", "Allocated", "GCs", "GC paused", "VRAM", "Texture pool"] ) _since.Hide( key );
			_sinceSection.Aside.Text = "";
			return;
		}

		var seconds = MathF.Max( 0.001f, (float)(RealTime.Now - b.At) );
		_sinceSection.Aside.Text = $"{seconds:0} s ago";
		_since.Set( "Baseline", $"{seconds:0} s ago" );

		var process = (long)PerformanceStats.ApproximateProcessMemoryUsage - b.Process;
		var heap = GC.GetTotalMemory( false ) - b.Heap;
		var allocated = GC.GetTotalAllocatedBytes( false ) - b.Allocated;
		_since.Set( "Process", Change( process ), process > 256 * 1024 * 1024 ? DevTools.Severity.Warning : null );
		_since.Set( "Managed heap", Change( heap ), tooltip: "Live managed heap now, against then. GCs between make this move in steps" );
		_since.Set( "Allocated", DevToolsFormat.Bytes( allocated ), tooltip: $"{DevToolsFormat.Bytes( allocated / seconds )}/s since" );
		_since.Set( "GCs", $"{GC.CollectionCount( 0 ) - b.Gen0} / {GC.CollectionCount( 1 ) - b.Gen1} / {GC.CollectionCount( 2 ) - b.Gen2}",
			GC.CollectionCount( 2 ) > b.Gen2 ? DevTools.Severity.Warning : null, "gen0 / gen1 / gen2 collections since" );
		_since.Set( "GC paused", $"{(GC.GetTotalPauseDuration() - b.Paused).TotalMilliseconds:0.0} ms" );
		_since.Set( "VRAM", Change( (long)GpuProfilerStats.VideoMemoryUsed - b.Vram ) );
		_since.Set( "Texture pool", Change( (long)stats.TexturePoolUsedBytes - b.TexturePool ) );
	}

	public override void Refresh( DevTools.Summary s )
	{
		var second = PerformanceStats.LastSecond;
		var gcMs = TimeSpan.FromTicks( second.GcPause ).TotalMilliseconds;
		var stats = FrameStats.Current;

		_facts.Set( "Process", DevToolsFormat.Bytes( PerformanceStats.ApproximateProcessMemoryUsage ) );
		_facts.Set( "Managed heap", DevToolsFormat.Bytes( GC.GetTotalMemory( false ) ) );
		_facts.Set( "Alloc", $"{DevToolsFormat.Bytes( second.ByteAlloc )}/s", second.ByteAlloc > 20 * 1024 * 1024 ? DevTools.Severity.Warning : null, "Managed allocations in the last second" );
		_facts.Set( "GC pause", $"{gcMs:0.0} ms/s", gcMs > 2 || second.Gc2 > 0 ? DevTools.Severity.Warning : null );
		_facts.Set( "GCs /s", $"{second.Gc0} / {second.Gc1} / {second.Gc2}", second.Gc2 > 0 ? DevTools.Severity.Warning : null, "gen0 / gen1 / gen2 collections in the last second" );

		RefreshSince( stats );

		var info = GC.GetGCMemoryInfo();
		var gens = info.GenerationInfo;
		_heap.Set( "Heap", DevToolsFormat.Bytes( info.HeapSizeBytes ) );
		_heap.Set( "Fragmented", info.HeapSizeBytes > 0 ? $"{(double)info.FragmentedBytes / info.HeapSizeBytes * 100:0.0}%" : "-" );
		_heap.Set( "Committed", DevToolsFormat.Bytes( info.TotalCommittedBytes ) );
		_heap.Set( "Gen 0", gens.Length > 0 ? DevToolsFormat.Bytes( gens[0].SizeAfterBytes ) : "-" );
		_heap.Set( "Gen 1", gens.Length > 1 ? DevToolsFormat.Bytes( gens[1].SizeAfterBytes ) : "-" );
		_heap.Set( "Gen 2", gens.Length > 2 ? DevToolsFormat.Bytes( gens[2].SizeAfterBytes ) : "-" );
		_heap.Set( "Large objects", gens.Length > 3 ? DevToolsFormat.Bytes( gens[3].SizeAfterBytes ) : "-" );
		_heap.Set( "Pinned", $"{info.PinnedObjectsCount:N0}" );
		_heap.Set( "Finalizers", $"{info.FinalizationPendingCount:N0}", tooltip: "Waiting to finalize" );

		RefreshAllocations();
		RefreshResources();
	}

	void RefreshAllocations()
	{
		var seconds = DebugOverlay.Allocations.WindowSeconds;
		var (g0, g1, g2) = DebugOverlay.Allocations.Collections;
		var allocated = DebugOverlay.Allocations.AllocatedBytes;
		_allocSection.Aside.Text = seconds >= 1
			? $"{(_baseline is null ? "" : "since the baseline, ")}{seconds:0} s: {DevToolsFormat.Bytes( allocated )}, GCs {g0}/{g1}/{g2}, pauses {DebugOverlay.Allocations.PauseSumMs:0.0} ms (max {DebugOverlay.Allocations.PauseMaxMs:0.0}), {DebugOverlay.Allocations.Stutters} over 2 ms"
			: "";

		var top = DebugOverlay.Allocations.Top;
		var most = top.Count > 0 ? top[0].Bytes : 1;
		var t = _allocs;
		t.Begin();
		for ( int i = 0; i < Math.Min( 30, top.Count ); i++ )
		{
			var (name, bytes, count) = top[i];
			t.Row( name, [name, DevToolsTable.Number( DevToolsFormat.Bytes( bytes ) ), new( DevToolsFormat.Count( count ), "number dim" ), DevToolsTable.BarOf( (double)bytes / most )], tooltip: name );
		}
		t.End( "Tracking since the tab opened. Types show after a second." );
		_allocs.Tooltip = "Bytes by type come from the runtime's allocation samples, one about every 100 KB - samples is how many, not how many objects";
	}

	void RefreshResources()
	{
		DebugOverlay.Resources.Update();
		var stats = DebugOverlay.Resources.Stats;
		var native = DebugOverlay.Resources.NativeStats;

		Counts( _strong, stats.StrongIndex, DebugOverlay.Resources.MaxStrong, _baseline?.Strong );
		Counts( _weak, stats.WeakIndexEntries, DebugOverlay.Resources.MaxWeak, _baseline?.Weak );
		Counts( _native, native.Entries, DebugOverlay.Resources.MaxNative, _baseline?.Native );
	}

	/// <summary>
	/// Live count by type, the change since the baseline, and the most there have been. A count well under its max is
	/// something that was let go; dead entries are references whose target has gone. Types gone since the baseline stay
	/// listed, at none.
	/// </summary>
	static void Counts( DevToolsTable t, Dictionary<string, int> current, Dictionary<string, int> max, Dictionary<string, int> baseline )
	{
		t.Begin();
		if ( current is not null )
		{
			foreach ( var (name, count) in current.OrderByDescending( x => x.Value ) )
			{
				var dead = name == "(dead)";
				var peak = max.GetValueOrDefault( name );
				var change = baseline is null ? 0 : count - baseline.GetValueOrDefault( name );
				t.Row( name, [new( name, dead ? "warning" : null ), DevToolsTable.Number( $"{count:N0}", dead && count > 0 ? DevTools.Severity.Warning : null ),
					new( baseline is null || change == 0 ? "" : change > 0 ? $"+{change:N0}" : $"{change:N0}", "number dim" ),
					new( dead ? "" : $"{peak:N0}", "number dim" )] );
			}
		}

		// Types there were at the baseline and none of now: all of them let go
		if ( baseline is not null )
		{
			foreach ( var (name, then) in baseline )
			{
				if ( then == 0 || current?.ContainsKey( name ) == true ) continue;
				t.Row( name, [new( name ), DevToolsTable.Number( "0" ), new( $"{-then:N0}", "number dim" ),
					new( name == "(dead)" ? "" : $"{max.GetValueOrDefault( name ):N0}", "number dim" )] );
			}
		}

		t.End( "Nothing indexed" );
	}
}

/// <summary>
/// Sounds, mixers and the acoustic simulation, and video decode. Needs cheats: the sound list and the world markers
/// drawn while this tab is open show where things are through walls.
/// </summary>
internal sealed class DevToolsAudioTab : DevToolsTab
{
	public override string TabId => "audio";
	public override string Title => "Audio";
	public override bool Available => DevTools.ShowCheatTabs;

	public DevToolsAudioTab()
	{
		var audio = AddChild( new DevToolsSection( "Audio" ) );
		audio.Aside.Text = "sounds are marked in the world while this tab is open";
		audio.Body.AddChild( new DevToolsOverlayView( DebugOverlay.Audio.Draw ) );
		AddChild( new DevToolsSection( "Video" ) ).Body.AddChild( new DevToolsOverlayView( DebugOverlay.Video.Draw ) );
	}

	public override void Refresh( DevTools.Summary s )
	{
	}
}
