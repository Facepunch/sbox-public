using Sandbox.Engine;
using Sandbox.Engine.Settings;
using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// Main thread time: frame pacing, what's capping the frame rate, the categories and every named scope.
/// </summary>
internal sealed class DevToolsCpuTab : DevToolsTab
{
	public override string TabId => "cpu";
	public override string Title => "CPU";

	readonly DevToolsFacts _facts;
	readonly DevToolsFacts _caps;
	readonly DevToolsTable _categories, _scopes;
	readonly List<DevTools.Cost> _scopeCosts = new();

	public DevToolsCpuTab()
	{
		_facts = AddChild( new DevToolsFacts() );

		var pacing = AddChild( new DevToolsSection( "Frame time distribution" ) );
		pacing.Body.AddChild( new DevToolsHistogram() );
		_caps = AddChild( new DevToolsFacts() );

		var split = AddChild( new Panel( null, "split" ) );
		_categories = split.AddChild( new Panel( null, "section" ) ).AddChild( DevToolsTable.ForCosts( "Categories" ) );
		var scopes = split.AddChild( new Panel( null, "section" ) );
		_scopes = scopes.AddChild( DevToolsTable.ForCosts( "Scopes" ) );
		scopes.AddChild( new Label( "Scopes nest, so these overlap - a category includes the scopes inside it", "hint" ) );
	}

	public override void Refresh( DevTools.Summary s )
	{
		_facts.Set( "CPU work", $"{s.CpuWorkAvg:0.00} ms", s.Limiter == DevTools.Limiter.Cpu && !s.MeetingTarget ? s.Rating : null );
		_facts.Set( "Cap wait", $"{s.CapWaitAvg:0.00} ms" );
		_facts.Set( "Frame p50", $"{s.FrameMedian:0.00} ms" );
		_facts.Set( "Frame max", $"{s.FrameMax:0.0} ms", s.FrameMax > s.HitchMs ? DevTools.Severity.Warning : null );
		_facts.Set( "Std dev", $"{s.FrameStdDev:0.00} ms", s.FrameStdDev > s.TargetMs * 0.25f ? DevTools.Severity.Warning : null, "How far frame times spread from the average" );
		_facts.Set( "Not presented", $"{s.NotRendered * 100:0}%", s.NotRendered > 0.05f ? DevTools.Severity.Warning : null, "Loop iterations that didn't present a frame" );

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
		DevTools.GetScopes( _scopeCosts );
		_scopes.SetCosts( _scopeCosts, 30, s.CpuWorkAvg, null, "Nothing measured" );
	}
}

/// <summary>
/// GPU passes, by their own time, on a timeline, and as the full tree.
/// </summary>
internal sealed class DevToolsGpuTab : DevToolsTab
{
	public override string TabId => "gpu";
	public override string Title => "GPU";

	readonly DevToolsFacts _facts;
	readonly DevToolsTable _passes;
	readonly DevToolsTable _tree;
	readonly List<DevTools.GpuPass> _gpuPasses = new();

	public DevToolsGpuTab()
	{
		_facts = AddChild( new DevToolsFacts() );

		var passes = AddChild( new Panel( null, "section" ) );
		_passes = passes.AddChild( DevToolsTable.ForCosts( "Leaf passes" ) );
		_passes.Tooltip = "Passes with no measured children, so inclusive time is their own";

		AddChild( new DevToolsSection( "Timeline" ) ).Body.AddChild( new DevToolsTimelineView() );
		_tree = AddChild( new Panel( null, "section" ) ).AddChild( new DevToolsTable( "minmax(0, 1fr) 52px 52px 40px 56px", "Pass tree", "#avg", "#max", "#%", "" ).Banded() );
	}

	public override void Refresh( DevTools.Summary s )
	{
		_facts.Set( "GPU frame", s.GpuAvg > 0 ? $"{s.GpuAvg:0.00} ms" : "-", s.Limiter == DevTools.Limiter.Gpu && !s.MeetingTarget ? s.Rating : null );
		_facts.Set( "Of frame", s.FrameAvg > 0 && s.GpuAvg > 0 ? $"{s.GpuAvg / s.FrameAvg * 100:0}%" : "-" );
		_facts.Set( "GPU p99", s.GpuTime.P99 > 0 ? $"{s.GpuTime.P99:0.00} ms" : "-" );
		_facts.Set( "VRAM", GpuProfilerStats.VideoMemoryBudget > 0
			? DevToolsFormat.BytesOf( GpuProfilerStats.VideoMemoryUsed, GpuProfilerStats.VideoMemoryBudget )
			: DevToolsFormat.Bytes( GpuProfilerStats.VideoMemoryUsed ) );
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
		DevTools.GetGpuPasses( _gpuPasses );

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
			t.Row( $"{pass.Id}", [new( pass.Name, null, Indent: indent ), DevToolsTable.Number( DevToolsFormat.Ms( pass.AvgMs ) ),
				new( DevToolsFormat.Ms( pass.MaxMs ), "number dim" ), new( $"{share * 100:0}", "number dim" ), DevToolsTable.BarOf( share )] );
		}
		t.End( "Waiting for GPU timings" );
	}
}

/// <summary>
/// Managed heap and GC, process and video memory, streaming, what's allocating, and what the resource caches hold.
/// </summary>
internal sealed class DevToolsMemoryTab : DevToolsTab
{
	public override string TabId => "memory";
	public override string Title => "Memory";

	readonly DevToolsFacts _facts;
	readonly DevToolsFacts _heap;
	readonly DevToolsSection _allocSection;
	readonly DevToolsTable _allocs;
	readonly DevToolsTable _strong, _weak, _native;

	public DevToolsMemoryTab()
	{
		_facts = AddChild( new DevToolsFacts() );

		var heap = AddChild( new DevToolsSection( "Managed heap, at the last GC" ) );
		_heap = heap.Body.AddChild( new DevToolsFacts() );

		_allocSection = AddChild( new DevToolsSection( "Allocations by type" ) );
		_allocs = _allocSection.Body.AddChild( new DevToolsTable( "minmax(0, 1fr) 72px 56px 64px", "type", "#bytes", "#count", "" ) );

		var split = AddChild( new Panel( null, "split" ) );
		_strong = split.AddChild( new Panel( null, "section" ) ).AddChild( new DevToolsTable( "minmax(0, 1fr) 56px 56px", "Resource index", "#live", "#max" ).Banded() );
		_weak = split.AddChild( new Panel( null, "section" ) ).AddChild( new DevToolsTable( "minmax(0, 1fr) 56px 56px", "Weak index", "#live", "#max" ).Banded() );
		_native = split.AddChild( new Panel( null, "section span-all" ) ).AddChild( new DevToolsTable( "minmax(0, 1fr) 56px 56px", "Native resource cache", "#live", "#max" ).Banded() );
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
		_facts.Set( "VRAM", GpuProfilerStats.VideoMemoryBudget > 0
			? DevToolsFormat.BytesOf( GpuProfilerStats.VideoMemoryUsed, GpuProfilerStats.VideoMemoryBudget )
			: "-" );
		_facts.Set( "Texture pool", stats.TexturePoolLimitBytes > 0
			? DevToolsFormat.BytesOf( stats.TexturePoolUsedBytes, stats.TexturePoolLimitBytes )
			: DevToolsFormat.Bytes( stats.TexturePoolUsedBytes ) );
		_facts.Set( "Pinned textures", DevToolsFormat.Bytes( stats.TexturePoolNonEvictableBytes ) );
		_facts.Set( "Streaming", $"{stats.PendingStreamingRequests} pending", stats.PendingStreamingRequests > 32 ? DevTools.Severity.Warning : null );

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
			? $"{seconds:0} s: {DevToolsFormat.Bytes( allocated )}, GCs {g0}/{g1}/{g2}, pauses {DebugOverlay.Allocations.PauseSumMs:0.0} ms (max {DebugOverlay.Allocations.PauseMaxMs:0.0}), {DebugOverlay.Allocations.Stutters} over 2 ms"
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
	static void Counts( DevToolsTable t, Dictionary<string, int> current, Dictionary<string, int> max )
	{
		t.Begin();
		if ( current is not null )
		{
			foreach ( var (name, count) in current.OrderByDescending( x => x.Value ) )
			{
				var dead = name == "(dead)";
				var peak = max.GetValueOrDefault( name );
				t.Row( name, [new( name, dead ? "warning" : null ), DevToolsTable.Number( $"{count:N0}", dead && count > 0 ? DevTools.Severity.Warning : null ),
					new( dead ? "" : $"{peak:N0}", "number dim" )] );
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
