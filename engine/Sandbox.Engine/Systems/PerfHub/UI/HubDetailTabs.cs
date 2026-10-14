using Sandbox.Engine;
using Sandbox.Engine.Settings;
using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// Main thread time: frame pacing, what's capping the frame rate, the categories and every named scope.
/// </summary>
internal sealed class HubCpuTab : HubTab
{
	public override string TabId => "cpu";
	public override string Title => "CPU";

	readonly HubFacts _facts;
	readonly HubFacts _caps;
	readonly HubTable _categories, _scopes;
	readonly List<PerfHub.Cost> _scopeCosts = new();

	public HubCpuTab()
	{
		_facts = AddChild( new HubFacts() );

		var pacing = AddChild( new HubSection( "Frame time distribution" ) );
		pacing.Body.AddChild( new HubHistogram() );
		_caps = AddChild( new HubFacts() );

		var split = AddChild( new Panel( null, "split" ) );
		_categories = split.AddChild( new Panel( null, "section" ) ).AddChild( HubTable.ForCosts( "Categories" ) );
		var scopes = split.AddChild( new Panel( null, "section" ) );
		_scopes = scopes.AddChild( HubTable.ForCosts( "Scopes" ) );
		scopes.AddChild( new Label( "Scopes nest, so these overlap - a category includes the scopes inside it", "hint" ) );
	}

	public override void Refresh( PerfHub.Summary s )
	{
		_facts.Set( "CPU work", $"{s.CpuWorkAvg:0.00} ms", s.Limiter == PerfHub.Limiter.Cpu && !s.MeetingTarget ? s.Rating : null );
		_facts.Set( "Cap wait", $"{s.CapWaitAvg:0.00} ms" );
		_facts.Set( "Frame p50", $"{s.FrameMedian:0.00} ms" );
		_facts.Set( "Frame max", $"{s.FrameMax:0.0} ms", s.FrameMax > s.HitchMs ? PerfHub.Severity.Warning : null );
		_facts.Set( "Std dev", $"{s.FrameStdDev:0.00} ms", s.FrameStdDev > s.TargetMs * 0.25f ? PerfHub.Severity.Warning : null, "How far frame times spread from the average" );
		_facts.Set( "Not presented", $"{s.NotRendered * 100:0}%", s.NotRendered > 0.05f ? PerfHub.Severity.Warning : null, "Loop iterations that didn't present a frame" );

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

		_categories.SetCosts( s.TopCpu, 32, s.CpuWorkAvg, null, "Nothing measured" );

		// Every named scope, not just the main categories - where a category's time actually went
		PerfHub.GetScopes( _scopeCosts );
		_scopes.SetCosts( _scopeCosts, 30, s.CpuWorkAvg, null, "Nothing measured" );
	}
}

/// <summary>
/// GPU passes, by their own time, on a timeline, and as the full tree.
/// </summary>
internal sealed class HubGpuTab : HubTab
{
	public override string TabId => "gpu";
	public override string Title => "GPU";

	readonly HubFacts _facts;
	readonly HubTable _passes;
	readonly HubTable _tree;
	readonly List<PerfHub.GpuPass> _gpuPasses = new();

	public HubGpuTab()
	{
		_facts = AddChild( new HubFacts() );

		var passes = AddChild( new Panel( null, "section" ) );
		_passes = passes.AddChild( HubTable.ForCosts( "Leaf passes" ) );
		_passes.Tooltip = "Passes with no measured children, so inclusive time is their own";

		AddChild( new HubSection( "Timeline" ) ).Body.AddChild( new HubTimelineView() );
		_tree = AddChild( new Panel( null, "section" ) ).AddChild( new HubTable( "minmax(0, 1fr) 52px 52px 40px 56px", "Pass tree", "#avg", "#max", "#%", "" ).Banded() );
	}

	public override void Refresh( PerfHub.Summary s )
	{
		_facts.Set( "GPU frame", s.GpuAvg > 0 ? $"{s.GpuAvg:0.00} ms" : "-", s.Limiter == PerfHub.Limiter.Gpu && !s.MeetingTarget ? s.Rating : null );
		_facts.Set( "Of frame", s.FrameAvg > 0 && s.GpuAvg > 0 ? $"{s.GpuAvg / s.FrameAvg * 100:0}%" : "-" );
		_facts.Set( "GPU p99", s.GpuTime.P99 > 0 ? $"{s.GpuTime.P99:0.00} ms" : "-" );
		_facts.Set( "VRAM", GpuProfilerStats.VideoMemoryBudget > 0
			? HubFormat.BytesOf( GpuProfilerStats.VideoMemoryUsed, GpuProfilerStats.VideoMemoryBudget )
			: HubFormat.Bytes( GpuProfilerStats.VideoMemoryUsed ) );
		_facts.Set( "Device", GpuProfilerStats.Renderer ?? "-" );
		_facts.Set( "Scopes", $"{GpuProfilerStats.RowCount}" );

		_passes.SetCosts( s.TopGpu, 24, s.GpuAvg, null, "Waiting for GPU timings", swatches: true );
		RefreshTree( s.GpuAvg );
	}

	/// <summary>
	/// Every pass in the order it was submitted, indented under its parent. Scopes overlap, so a parent's time isn't the
	/// sum of its children's.
	/// </summary>
	void RefreshTree( float gpuMs )
	{
		PerfHub.GetGpuPasses( _gpuPasses );

		var t = _tree;
		t.Begin();
		foreach ( var pass in _gpuPasses )
		{
			if ( pass.BranchMs < 0.01f ) continue;

			var indent = pass.Depth * 14f;
			if ( !pass.Measured )
			{
				t.Row( $"{pass.Id}", [new( pass.Name, "dim", Indent: indent ), "", "", "", ""] );
				continue;
			}

			var share = gpuMs > 0 ? pass.AvgMs / gpuMs : 0;
			t.Row( $"{pass.Id}", [new( pass.Name, null, Indent: indent ), HubTable.Number( HubFormat.Ms( pass.AvgMs ) ),
				new( HubFormat.Ms( pass.MaxMs ), "number dim" ), new( $"{share * 100:0}", "number dim" ), HubTable.BarOf( share )] );
		}
		t.End( "Waiting for GPU timings" );
	}
}

/// <summary>
/// Managed heap and GC, process and video memory, streaming, what's allocating, and what the resource caches hold.
/// </summary>
internal sealed class HubMemoryTab : HubTab
{
	public override string TabId => "memory";
	public override string Title => "Memory";

	readonly HubFacts _facts;
	readonly HubFacts _heap;
	readonly HubSection _allocSection;
	readonly HubTable _allocs;
	readonly HubTable _strong, _weak, _native;

	public HubMemoryTab()
	{
		_facts = AddChild( new HubFacts() );

		var heap = AddChild( new HubSection( "Managed heap, at the last GC" ) );
		_heap = heap.Body.AddChild( new HubFacts() );

		_allocSection = AddChild( new HubSection( "Allocations by type" ) );
		_allocs = _allocSection.Body.AddChild( new HubTable( "minmax(0, 1fr) 72px 56px 64px", "type", "#bytes", "#count", "" ) );

		var split = AddChild( new Panel( null, "split" ) );
		_strong = split.AddChild( new Panel( null, "section" ) ).AddChild( new HubTable( "minmax(0, 1fr) 56px 56px", "Resource index", "#live", "#max" ).Banded() );
		_weak = split.AddChild( new Panel( null, "section" ) ).AddChild( new HubTable( "minmax(0, 1fr) 56px 56px", "Weak index", "#live", "#max" ).Banded() );
		_native = split.AddChild( new Panel( null, "section span-all" ) ).AddChild( new HubTable( "minmax(0, 1fr) 56px 56px", "Native resource cache", "#live", "#max" ).Banded() );
	}

	public override void Refresh( PerfHub.Summary s )
	{
		var second = PerformanceStats.LastSecond;
		var gcMs = TimeSpan.FromTicks( second.GcPause ).TotalMilliseconds;
		var stats = FrameStats.Current;

		_facts.Set( "Process", HubFormat.Bytes( PerformanceStats.ApproximateProcessMemoryUsage ) );
		_facts.Set( "Managed heap", HubFormat.Bytes( GC.GetTotalMemory( false ) ) );
		_facts.Set( "Alloc", $"{HubFormat.Bytes( second.ByteAlloc )}/s", second.ByteAlloc > 20 * 1024 * 1024 ? PerfHub.Severity.Warning : null, "Managed allocations in the last second" );
		_facts.Set( "GC pause", $"{gcMs:0.0} ms/s", gcMs > 2 || second.Gc2 > 0 ? PerfHub.Severity.Warning : null );
		_facts.Set( "GCs /s", $"{second.Gc0} / {second.Gc1} / {second.Gc2}", second.Gc2 > 0 ? PerfHub.Severity.Warning : null, "gen0 / gen1 / gen2 collections in the last second" );
		_facts.Set( "VRAM", GpuProfilerStats.VideoMemoryBudget > 0
			? HubFormat.BytesOf( GpuProfilerStats.VideoMemoryUsed, GpuProfilerStats.VideoMemoryBudget )
			: "-" );
		_facts.Set( "Texture pool", stats.TexturePoolLimitBytes > 0
			? HubFormat.BytesOf( stats.TexturePoolUsedBytes, stats.TexturePoolLimitBytes )
			: HubFormat.Bytes( stats.TexturePoolUsedBytes ) );
		_facts.Set( "Pinned textures", HubFormat.Bytes( stats.TexturePoolNonEvictableBytes ) );
		_facts.Set( "Streaming", $"{stats.PendingStreamingRequests} pending", stats.PendingStreamingRequests > 32 ? PerfHub.Severity.Warning : null );

		var info = GC.GetGCMemoryInfo();
		var gens = info.GenerationInfo;
		_heap.Set( "Heap", HubFormat.Bytes( info.HeapSizeBytes ) );
		_heap.Set( "Fragmented", info.HeapSizeBytes > 0 ? $"{(double)info.FragmentedBytes / info.HeapSizeBytes * 100:0.0}%" : "-" );
		_heap.Set( "Committed", HubFormat.Bytes( info.TotalCommittedBytes ) );
		_heap.Set( "Gen 0", gens.Length > 0 ? HubFormat.Bytes( gens[0].SizeAfterBytes ) : "-" );
		_heap.Set( "Gen 1", gens.Length > 1 ? HubFormat.Bytes( gens[1].SizeAfterBytes ) : "-" );
		_heap.Set( "Gen 2", gens.Length > 2 ? HubFormat.Bytes( gens[2].SizeAfterBytes ) : "-" );
		_heap.Set( "Large objects", gens.Length > 3 ? HubFormat.Bytes( gens[3].SizeAfterBytes ) : "-" );
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
			? $"{seconds:0} s: {HubFormat.Bytes( allocated )}, GCs {g0}/{g1}/{g2}, pauses {DebugOverlay.Allocations.PauseSumMs:0.0} ms (max {DebugOverlay.Allocations.PauseMaxMs:0.0}), {DebugOverlay.Allocations.Stutters} over 2 ms"
			: "";

		var top = DebugOverlay.Allocations.Top;
		var most = top.Count > 0 ? top[0].Bytes : 1;
		var t = _allocs;
		t.Begin();
		for ( int i = 0; i < Math.Min( 30, top.Count ); i++ )
		{
			var (name, bytes, count) = top[i];
			t.Row( name, [name, HubTable.Number( HubFormat.Bytes( bytes ) ), new( HubFormat.Count( count ), "number dim" ), HubTable.BarOf( (double)bytes / most )], tooltip: name );
		}
		t.End( "Tracking since the tab opened. Types show after a second." );
	}

	void RefreshResources()
	{
		DebugOverlay.Resources.Update();
		var stats = DebugOverlay.Resources.Stats;
		var native = DebugOverlay.Resources.NativeStats;

		Counts( _strong, stats.StrongIndex, DebugOverlay.Resources.MaxStrong );
		Counts( _weak, stats.WeakIndexEntries, DebugOverlay.Resources.MaxWeak );
		Counts( _native, native.Entries, DebugOverlay.Resources.MaxNative );
	}

	/// <summary>
	/// Live count by type, and the most there have been. A count well under its max is something that was let go;
	/// dead entries are references whose target has gone.
	/// </summary>
	static void Counts( HubTable t, Dictionary<string, int> current, Dictionary<string, int> max )
	{
		t.Begin();
		if ( current is not null )
		{
			foreach ( var (name, count) in current.OrderByDescending( x => x.Value ) )
			{
				var dead = name == "(dead)";
				var peak = max.GetValueOrDefault( name );
				t.Row( name, [new( name, dead ? "warning" : null ), HubTable.Number( $"{count:N0}", dead && count > 0 ? PerfHub.Severity.Warning : null ),
					new( dead ? "" : $"{peak:N0}", "number dim" )] );
			}
		}
		t.End( "Nothing indexed" );
	}
}

/// <summary>
/// Connection health, bandwidth by message type, and the busiest calls.
/// </summary>
internal sealed class HubNetworkTab : HubTab
{
	public override string TabId => "network";
	public override string Title => "Network";

	readonly HubFacts _facts;
	readonly HubSection _bandwidth, _calls;

	public HubNetworkTab()
	{
		_facts = AddChild( new HubFacts() );
		_bandwidth = AddChild( new HubSection( "Bandwidth" ) );
		_bandwidth.Body.AddChild( new HubOverlayView( DebugOverlay.NetworkGraph.Draw ) );
		_calls = AddChild( new HubSection( "Busiest calls" ) );
		_calls.Body.AddChild( new HubOverlayView( DebugOverlay.NetworkCalls.Draw ) );
	}

	public override void Refresh( PerfHub.Summary s )
	{
		_bandwidth.SetClass( "hidden", !Networking.IsActive );
		_calls.SetClass( "hidden", !Networking.IsActive );

		if ( !Networking.IsActive )
		{
			_facts.Set( "Session", "not connected" );
			foreach ( var key in NetworkKeys ) _facts.Hide( key );
			return;
		}

		_facts.Set( "Session", Networking.IsHost ? "host" : "client" );
		_facts.Set( "Connections", $"{Connection.All.Count}" );

		float bytesIn = 0, bytesOut = 0;
		foreach ( var c in Connection.All )
		{
			if ( c == Connection.Local ) continue;
			bytesIn += c.Stats.InBytesPerSecond;
			bytesOut += c.Stats.OutBytesPerSecond;
		}

		_facts.Set( "In", $"{HubFormat.Bytes( bytesIn )}/s" );
		_facts.Set( "Out", $"{HubFormat.Bytes( bytesOut )}/s" );

		if ( !Networking.IsHost && Connection.Host is { } host )
		{
			var ping = host.Stats.Ping;
			var quality = host.Stats.ConnectionQuality;
			_facts.Set( "Ping", $"{ping} ms", ping > 300 ? PerfHub.Severity.Bad : ping > 150 ? PerfHub.Severity.Warning : null );
			_facts.Set( "Quality", $"{quality * 100:0}%", quality is > 0 and < 0.8f ? PerfHub.Severity.Warning : null );

			// What the host says about itself
			var hostStats = Networking.HostStats;
			_facts.Set( "Host fps", $"{hostStats.Fps}" );
			_facts.Set( "Host in / out", $"{HubFormat.Bytes( hostStats.InBytesPerSecond )}/s / {HubFormat.Bytes( hostStats.OutBytesPerSecond )}/s" );
		}
	}

	static readonly string[] NetworkKeys = ["Connections", "In", "Out", "Ping", "Quality", "Host fps", "Host in / out"];
}

/// <summary>
/// UI roots and what each costs to lay out, build and submit.
/// </summary>
internal sealed class HubUiTab : HubTab
{
	public override string TabId => "ui";
	public override string Title => "UI";

	readonly HubFacts _facts;
	readonly HubTable _roots;

	public HubUiTab()
	{
		_facts = AddChild( new HubFacts() );
		var roots = AddChild( new Panel( null, "section" ) );
		_roots = roots.AddChild( new HubTable( "minmax(0, 1fr) 56px 48px 48px 48px 48px 40px", "Roots", "#panels", "#layout", "#build", "#submit", "#total", "#draws" ).Banded() );
	}

	public override void Refresh( PerfHub.Summary s )
	{
		var total = new RootPanel.RenderStatistics();
		var count = 0;
		var gpuBuffers = 0;

		var t = _roots;
		t.Begin();

		foreach ( var (name, root) in RenderedRoots() )
		{
			var r = root.Stats;
			count++;
			gpuBuffers += GpuBuffers( root );
			total = Sum( total, r );

			t.Row( $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( root )}", [name,
				HubTable.Number( $"{r.PanelsRendered:N0}" ),
				Ms( r.LayoutTime ), Ms( r.CommandBuildTime ), Ms( r.SubmissionTime ),
				HubTable.Number( HubFormat.Ms( r.CpuTime.TotalMilliseconds ) ),
				new( $"{r.DrawCalls:N0}", "number dim" )], tooltip: $"{name}: {r.PanelsCulled:N0} subtrees culled, {r.Layers:N0} layers, {r.FrameGrabs:N0} frame grabs" );
		}

		t.End( "No UI rendered this frame" );

		_facts.Set( "Roots", $"{count}" );
		_facts.Set( "Panels", $"{total.PanelsRendered:N0}", tooltip: $"{total.PanelsCulled:N0} subtrees culled" );
		_facts.Set( "CPU", $"{total.CpuTime.TotalMilliseconds:0.00} ms" );
		_facts.Set( "Draw calls", $"{total.DrawCalls:N0}" );
		_facts.Set( "Instances", $"{total.Instances:N0}" );
		_facts.Set( "Layers", $"{total.Layers:N0}" );
		_facts.Set( "Frame grabs", $"{total.FrameGrabs:N0}", total.FrameGrabs > 0 ? PerfHub.Severity.Info : null, "Backdrop filters and the like copy the screen" );
		_facts.Set( "GPU buffers", $"{gpuBuffers:N0}" );
	}

	/// <summary>
	/// The root panels that rendered last frame, most expensive first, game before menu. The game's UI and the menu's are
	/// separate systems; the hub's own isn't counted.
	/// </summary>
	internal static IEnumerable<(string Name, RootPanel Root)> RenderedRoots()
	{
		foreach ( var context in Contexts() )
		{
			var system = context.UISystem;
			if ( system is null ) continue;

			foreach ( var root in system.RootPanels.OrderByDescending( x => x.Stats.CpuTime ) )
			{
				if ( !root.IsValid ) continue;

				// Roots that have stopped rendering keep their last numbers
				var r = root.Stats;
				if ( r.RenderCount == 0 || r.FrameNumber + 1 < Application.FrameCount ) continue;

				yield return (context == Engine.GlobalContext.Menu ? $"{root.GetType().Name} (menu)" : root.GetType().Name, root);
			}
		}
	}

	internal static int GpuBuffers( RootPanel root ) => root.PanelCommandList.FindResource<Painter.Context>()?.Batcher.GpuBufferCount ?? 0;

	internal static RootPanel.RenderStatistics Sum( RootPanel.RenderStatistics total, RootPanel.RenderStatistics r ) => total with
	{
		LayoutTime = total.LayoutTime + r.LayoutTime,
		CommandBuildTime = total.CommandBuildTime + r.CommandBuildTime,
		SubmissionTime = total.SubmissionTime + r.SubmissionTime,
		PanelsRendered = total.PanelsRendered + r.PanelsRendered,
		PanelsCulled = total.PanelsCulled + r.PanelsCulled,
		DrawCalls = total.DrawCalls + r.DrawCalls,
		Instances = total.Instances + r.Instances,
		Layers = total.Layers + r.Layers,
		FrameGrabs = total.FrameGrabs + r.FrameGrabs,
	};

	static IEnumerable<Engine.GlobalContext> Contexts()
	{
		if ( Engine.GlobalContext.Game is { } game ) yield return game;
		if ( Engine.GlobalContext.Menu is { } menu && menu != Engine.GlobalContext.Game ) yield return menu;
	}

	static HubTable.Cell Ms( TimeSpan time ) => new( HubFormat.Ms( time.TotalMilliseconds ), "number dim" );
}

/// <summary>
/// Sounds, mixers and the acoustic simulation, and video decode. Needs cheats: the sound list and the world markers
/// drawn while this tab is open show where things are through walls.
/// </summary>
internal sealed class HubAudioTab : HubTab
{
	public override string TabId => "audio";
	public override string Title => "Audio";
	public override bool Available => PerfHub.ShowCheatTabs;

	public HubAudioTab()
	{
		var audio = AddChild( new HubSection( "Audio" ) );
		audio.Aside.Text = "sounds are marked in the world while this tab is open";
		audio.Body.AddChild( new HubOverlayView( DebugOverlay.Audio.Draw ) );
		AddChild( new HubSection( "Video" ) ).Body.AddChild( new HubOverlayView( DebugOverlay.Video.Draw ) );
	}

	public override void Refresh( PerfHub.Summary s )
	{
	}
}
