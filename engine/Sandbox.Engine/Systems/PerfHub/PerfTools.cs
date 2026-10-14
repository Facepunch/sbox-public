using Sandbox.Diagnostics;
using Sandbox.Engine;
using Sandbox.Engine.Settings;
using Sandbox.Rendering;
using Sandbox.UI;

namespace Editor.Mcp;

/// <summary>
/// The performance hub's numbers as data, so an agent can read them instead of opening the hub and screenshotting it.
/// Each tool is one of the hub's tabs; they read what the tabs read (<see cref="PerfHub"/>), whether or not the hub is
/// open. Times are milliseconds, memory is MiB.
/// </summary>
[McpToolset( "perf", "Performance numbers from the performance hub (perf_hub): frame times, what limits the frame rate, CPU scopes, GPU passes, memory and GC, rendering counters, network and UI costs" )]
internal static class PerfTools
{
	/// <summary>
	/// How long collectors a tool switched on keep running after it returns, so follow-up calls don't wait to warm up.
	/// </summary>
	const float Linger = 30;

	[McpTool.ReadOnly( "perf_overview" )]
	[Description( "Start here. Frame rate and frame time stats over the last ~2 seconds (avg/p50/p99/max for the frame, CPU work, GPU and cap wait), what's limiting the frame rate, hitches, the issues the performance hub flags, the top main thread and GPU costs, and memory/GC headlines. Each issue names the perf_* tool with the detail. Takes about a second the first time, while GPU timings warm up." )]
	internal static async Task<object> Overview()
	{
		await WarmGpu();

		var s = PerfHub.Summarise();
		var second = PerformanceStats.LastSecond;
		var f = FrameStats.Current;

		return new
		{
			Window = Window( s ),
			Fps = Round( s.Fps, 1 ),
			TargetFps = Round( s.TargetFps, 1 ),
			TargetSource = s.TargetSource,
			LimitedBy = s.LimitText,
			s.MeetingTarget,
			s.Rating,
			Frame = Stat( s.FrameTime ),
			CpuWork = Stat( s.CpuTime ),
			Gpu = s.GpuAvg > 0 ? Stat( s.GpuTime ) : null,
			CapWait = Stat( s.WaitTime ),
			PerfHubWindow = PerfHub.IsOpen ? Stat( s.HubTime ) : null,
			Hitches = Hitches( s ),
			Issues = Issues( s.Issues ),
			MainThread = Costs( s.TopCpu, 8 ),
			GpuPasses = GpuCosts( s.TopGpu, 8 ),
			Memory = new
			{
				ProcessMiB = MiB( PerformanceStats.ApproximateProcessMemoryUsage ),
				VideoMemory = VideoMemory(),
				AllocatedMiBPerSecond = MiB( second.ByteAlloc ),
				GcPauseMsLastSecond = Round( TimeSpan.FromTicks( second.GcPause ).TotalMilliseconds ),
				GcLastSecond = new { Gen0 = second.Gc0, Gen1 = second.Gc1, Gen2 = second.Gc2 },
			},
			DrawCalls = f.Managed.Renders > 0 ? f.Managed.TotalDraws : f.DrawCalls,
			Triangles = f.Managed.Renders > 0 ? f.Managed.Triangles : f.TrianglesRendered,
		};
	}

	[McpTool.ReadOnly( "perf_frames" )]
	[Description( "Per-frame timings, newest first: the whole frame, the engine's work on it (frame minus cap wait and the performance hub's own cost), the GPU frame time, and the cap wait. Use it to find hitches and pacing problems the averages in perf_overview hide. GPU times arrive a few frames late." )]
	internal static object Frames( [Description( "How many frames, newest first. Default 120, max 512." )] int count = 120 )
	{
		var frames = PerfHub.Frames;
		count = Math.Clamp( count, 1, Math.Max( 1, frames.Count ) );
		if ( frames.Count == 0 ) return new { Count = 0 };

		var frame = new double[count];
		var cpu = new double[count];
		var gpu = new double[count];
		var wait = new double[count];
		for ( int i = 0; i < count; i++ )
		{
			frame[i] = Round( frames.FrameMs( i ) );
			cpu[i] = Round( frames.BusyMs( i ) );
			gpu[i] = Round( frames.GpuMs( i ) );
			wait[i] = Round( frames.IdleMs( i ) );
		}

		return new { Count = count, FrameMs = frame, CpuMs = cpu, GpuMs = gpu, WaitMs = wait };
	}

	[McpTool.ReadOnly( "perf_cpu" )]
	[Description( "Main thread detail: frame pacing, what caps the frame rate (fps_max, vsync, display mode), time by category, and every named timing scope. Scopes nest, so they overlap - a category includes the scopes inside it." )]
	internal static object Cpu( [Description( "How many scopes to return, biggest first. Default 30, max 200." )] int limit = 30 )
	{
		var s = PerfHub.Summarise();
		var rs = RenderSettings.Instance;
		var surface = GameSurface.Current;
		var cap = EngineLoop.FrameRateLimit;

		var scopes = new List<PerfHub.Cost>();
		PerfHub.GetScopes( scopes );

		return new
		{
			Window = Window( s ),
			CpuWork = Stat( s.CpuTime ),
			CapWait = Stat( s.WaitTime ),
			Frame = Stat( s.FrameTime ),
			FrameStdDevMs = Round( s.FrameStdDev ),
			Hitches = Hitches( s ),
			NotPresentedPercent = Round( s.NotRendered * 100, 1 ),
			Cap = new
			{
				CappedBy = cap.FramesPerSecond > 0 ? cap.Source : "nothing",
				CapFps = cap.FramesPerSecond > 0 ? Round( cap.FramesPerSecond, 1 ) : 0,
				Display = rs.Fullscreen ? "exclusive" : rs.Borderless ? "borderless" : "windowed",
				VSync = surface?.VSync ?? rs.VSync,
				RefreshRate = surface?.RefreshRate ?? 0,
				FpsMax = rs.MaxFrameRate,
				FpsMaxMenu = rs.MaxFrameRateMenu,
				FpsMaxInactive = rs.MaxFrameRateInactive,
			},
			Categories = Costs( s.TopCpu, int.MaxValue ),
			TotalScopes = scopes.Count,
			Scopes = Costs( scopes, Math.Clamp( limit, 1, 200 ) ),
		};
	}

	[McpTool.ReadOnly( "perf_gpu" )]
	[Description( "GPU detail: GPU frame time, video memory, the leaf passes by their own time, and the full pass tree in submission order (Depth is nesting under the parent; grouping scopes that aren't timed themselves have null times). Scopes overlap, so a parent's time isn't the sum of its children's. Takes about a second the first time, while GPU timings warm up." )]
	internal static async Task<object> Gpu( [Description( "How many tree rows to return, in submission order. Passes under 0.01 ms are left out. Default 80, max 500." )] int limit = 80 )
	{
		await WarmGpu();

		var s = PerfHub.Summarise();
		var passes = new List<PerfHub.GpuPass>();
		PerfHub.GetGpuPasses( passes );

		var tree = passes.Where( x => x.BranchMs >= 0.01f ).ToList();

		return new
		{
			Gpu = s.GpuAvg > 0 ? Stat( s.GpuTime ) : null,
			OfFramePercent = s.FrameAvg > 0 && s.GpuAvg > 0 ? Round( s.GpuAvg / s.FrameAvg * 100, 1 ) : 0,
			VideoMemory = VideoMemory(),
			Renderer = GpuProfilerStats.Renderer,
			LeafPasses = GpuCosts( s.TopGpu, 40 ),
			TotalTreeRows = tree.Count,
			Tree = tree.Take( Math.Clamp( limit, 1, 500 ) ).Select( x => new
			{
				x.Name,
				x.Depth,
				AvgMs = x.Measured ? Round( x.AvgMs ) : (double?)null,
				MaxMs = x.Measured ? Round( x.MaxMs ) : (double?)null,
			} ),
		};
	}

	[McpTool.ReadOnly( "perf_memory" )]
	[Description( "Memory detail: process and managed heap, allocation rate, GC pauses and collections, video memory, the texture pool and streaming, and what the resource caches hold by type. Pass allocationSeconds to also track which types allocate the most over that many seconds - the call waits that long." )]
	internal static async Task<object> Memory(
		[Description( "Seconds to track managed allocations by type before returning, 2 to 60. 0 (the default) skips it." )] float allocationSeconds = 0,
		[Description( "How many rows each list returns, biggest first. Default 20, max 100." )] int limit = 20 )
	{
		limit = Math.Clamp( limit, 1, 100 );

		object allocations = null;
		if ( allocationSeconds > 0 )
		{
			allocationSeconds = Math.Clamp( allocationSeconds, 2, 60 );

			// A window of exactly what was asked for, unless the hub is already tracking for its own tab
			if ( !PerfHub.IsCollecting( "memory" ) ) DebugOverlay.Allocations.Disabled();
			await CollectFor( "memory", allocationSeconds, linger: 5 );

			var (g0, g1, g2) = DebugOverlay.Allocations.Collections;
			allocations = new
			{
				Seconds = Round( DebugOverlay.Allocations.WindowSeconds, 1 ),
				AllocatedMiB = MiB( DebugOverlay.Allocations.AllocatedBytes ),
				Collections = new { Gen0 = g0, Gen1 = g1, Gen2 = g2 },
				PauseTotalMs = Round( DebugOverlay.Allocations.PauseSumMs ),
				PauseMaxMs = Round( DebugOverlay.Allocations.PauseMaxMs ),
				FramesWithPauseOver2Ms = DebugOverlay.Allocations.Stutters,
				ByType = DebugOverlay.Allocations.Top.Take( limit ).Select( x => new { Type = x.Name, KiB = KiB( x.Bytes ), x.Count } ),
			};
		}

		var second = PerformanceStats.LastSecond;
		var f = FrameStats.Current;
		var info = GC.GetGCMemoryInfo();
		var gens = info.GenerationInfo;

		DebugOverlay.Resources.Update();
		var resources = DebugOverlay.Resources.Stats;
		var native = DebugOverlay.Resources.NativeStats;

		return new
		{
			ProcessMiB = MiB( PerformanceStats.ApproximateProcessMemoryUsage ),
			ManagedHeapMiB = MiB( GC.GetTotalMemory( false ) ),
			AllocatedMiBPerSecond = MiB( second.ByteAlloc ),
			GcPauseMsLastSecond = Round( TimeSpan.FromTicks( second.GcPause ).TotalMilliseconds ),
			GcLastSecond = new { Gen0 = second.Gc0, Gen1 = second.Gc1, Gen2 = second.Gc2 },
			HeapAtLastGc = new
			{
				HeapMiB = MiB( info.HeapSizeBytes ),
				FragmentedPercent = info.HeapSizeBytes > 0 ? Round( (double)info.FragmentedBytes / info.HeapSizeBytes * 100, 1 ) : 0,
				CommittedMiB = MiB( info.TotalCommittedBytes ),
				Gen0MiB = gens.Length > 0 ? MiB( gens[0].SizeAfterBytes ) : 0,
				Gen1MiB = gens.Length > 1 ? MiB( gens[1].SizeAfterBytes ) : 0,
				Gen2MiB = gens.Length > 2 ? MiB( gens[2].SizeAfterBytes ) : 0,
				LargeObjectsMiB = gens.Length > 3 ? MiB( gens[3].SizeAfterBytes ) : 0,
				PinnedObjects = info.PinnedObjectsCount,
				PendingFinalizers = info.FinalizationPendingCount,
			},
			VideoMemory = VideoMemory(),
			TexturePool = new
			{
				UsedMiB = MiB( f.TexturePoolUsedBytes ),
				LimitMiB = MiB( f.TexturePoolLimitBytes ),
				PinnedMiB = MiB( f.TexturePoolNonEvictableBytes ),
				PendingStreamingRequests = f.PendingStreamingRequests,
			},
			Allocations = allocations,
			ResourceIndex = Counts( resources.StrongIndex, DebugOverlay.Resources.MaxStrong, limit ),
			WeakIndex = Counts( resources.WeakIndexEntries, DebugOverlay.Resources.MaxWeak, limit ),
			NativeResourceCache = Counts( native.Entries, DebugOverlay.Resources.MaxNative, limit ),
		};
	}

	[McpTool.ReadOnly( "perf_rendering" )]
	[Description( "Rendering detail for the last frame: which renderer drew it, resolution and AA, the rendering issues the performance hub flags (draw calls, batching, texture pool), every scene system counter (draws, triangles, batches, material changes, culling, lights and shadows, texture pool, device report), the managed renderer's phases and draws when it rendered, and the active camera's post-processing layers." )]
	internal static object Rendering()
	{
		var f = FrameStats.Current;
		var m = f.Managed;
		var isManaged = m.Renders > 0;
		var rs = RenderSettings.Instance;

		var issues = new List<PerfHub.Issue>();
		HubRenderingTab.FindIssues( f, issues );

		var camera = Application.GetActiveScene()?.Camera;
		var layers = camera?.PostProcess.Layers;

		return new
		{
			Renderer = isManaged ? "managed" : "native",
			Resolution = $"{Screen.Width:0}x{Screen.Height:0}",
			AntiAliasing = rs.AntiAliasQuality,
			Upscaling = rs.UpscalerMode,
			Issues = Issues( issues ),
			Counters = f,
			ManagedRenderer = isManaged ? new
			{
				CameraFrames = m.Renders,
				m.Objects,
				m.ObjectsVisible,
				m.ObjectsSizeCulled,
				m.Draws,
				m.DepthDraws,
				m.TranslucentDraws,
				m.ShadowDraws,
				m.CustomDraws,
				m.TotalDraws,
				m.Instances,
				m.Triangles,
				m.Lights,
				m.ShadowViews,
				MainThreadMs = Round( m.MainThreadMs ),
				CollectMs = Round( m.CollectMs ),
				PrepareMs = Round( m.PrepareMs ),
				RecordMs = Round( m.RecordMs ),
				SyncMs = Round( m.SyncMs ),
				SetupMs = Round( m.SetupMs ),
				SubmitMs = Round( m.SubmitMs ),
				WorkerRecordMs = Round( m.WorkerRecordMs ),
				RecordWaitMs = Round( m.RecordWaitMs ),
			} : null,
			PostProcessing = camera is null ? "no active camera"
				: !camera.EnablePostProcessing ? "off on this camera"
				: (object)(layers ?? []).OrderBy( x => x.Key ).SelectMany( x => x.Value.OrderBy( l => l.Order ).Select( l => new
				{
					l.Name,
					Stage = x.Key,
					l.Order,
					Async = l.RanAsync,
					l.AsyncCompute,
					l.NeedsDepthNormals,
				} ) ).ToList(),
		};
	}

	[McpTool.ReadOnly( "perf_network" )]
	[Description( "Network detail: whether this is a host or client, connections with ping, quality and bandwidth, and what the host reports. Pass seconds to also measure which RPCs, sync vars and message types use the bandwidth over that many seconds - the call waits that long." )]
	internal static async Task<object> Network(
		[Description( "Seconds to record network messages before returning, 1 to 60. 0 (the default) skips it." )] float seconds = 0,
		[Description( "How many rows each message list returns, biggest first. Default 20, max 100." )] int limit = 20 )
	{
		if ( !Networking.IsActive )
			return new { Session = "not connected" };

		limit = Math.Clamp( limit, 1, 100 );

		object messages = null;
		if ( seconds > 0 )
		{
			seconds = Math.Clamp( seconds, 1, 60 );

			if ( NetworkDebugSystem.Current is { } before && !PerfHub.IsCollecting( "network" ) ) before.Reset();
			await CollectFor( "network", seconds, linger: 5 );

			messages = Messages( NetworkDebugSystem.Current, seconds, limit );
		}

		var hostStats = Networking.HostStats;
		return new
		{
			Session = Networking.IsHost ? "host" : "client",
			Connections = Connection.All.Where( c => c != Connection.Local ).Select( c => new
			{
				Name = c.DisplayName,
				c.Id,
				IsHost = c == Connection.Host,
				c.Stats.Ping,
				QualityPercent = Round( c.Stats.ConnectionQuality * 100, 0 ),
				InKiBPerSecond = KiB( c.Stats.InBytesPerSecond ),
				OutKiBPerSecond = KiB( c.Stats.OutBytesPerSecond ),
			} ).ToList(),
			Host = Networking.IsHost ? null : new
			{
				hostStats.Fps,
				InKiBPerSecond = KiB( hostStats.InBytesPerSecond ),
				OutKiBPerSecond = KiB( hostStats.OutBytesPerSecond ),
			},
			Messages = messages,
		};
	}

	[McpTool.ReadOnly( "perf_ui" )]
	[Description( "UI cost for the last frame: every root panel that rendered (game and menu, not the performance hub's own) with its panel count, layout, command build and submit time, and draw calls, plus the totals." )]
	internal static object Ui()
	{
		var total = new RootPanel.RenderStatistics();
		var gpuBuffers = 0;
		var roots = new List<object>();

		foreach ( var (name, root) in HubUiTab.RenderedRoots() )
		{
			var r = root.Stats;
			total = HubUiTab.Sum( total, r );
			gpuBuffers += HubUiTab.GpuBuffers( root );
			roots.Add( UiStats( name, r ) );
		}

		return new
		{
			Total = UiStats( "all roots", total ),
			GpuBuffers = gpuBuffers,
			Roots = roots,
		};
	}

	static object UiStats( string name, RootPanel.RenderStatistics r ) => new
	{
		Name = name,
		r.PanelsRendered,
		SubtreesCulled = r.PanelsCulled,
		CpuMs = Round( r.CpuTime.TotalMilliseconds ),
		LayoutMs = Round( r.LayoutTime.TotalMilliseconds ),
		BuildMs = Round( r.CommandBuildTime.TotalMilliseconds ),
		SubmitMs = Round( r.SubmissionTime.TotalMilliseconds ),
		r.DrawCalls,
		r.Instances,
		r.Layers,
		r.FrameGrabs,
	};

	static object Messages( NetworkDebugSystem system, float seconds, int limit )
	{
		if ( system is null ) return "no active scene to record in";

		// The graph's samples are taken 30 times a second; the newest ones cover the recording
		var samples = system.Samples.Reverse().Take( (int)MathF.Ceiling( seconds / NetworkDebugSystem.SampleRate ) ).ToList();
		var byType = new Dictionary<NetworkDebugSystem.MessageType, long>();
		foreach ( var sample in samples )
		{
			foreach ( var (type, bytes) in sample.BytesPerType )
				byType[type] = byType.GetValueOrDefault( type ) + bytes;
		}

		var window = samples.Count * NetworkDebugSystem.SampleRate;

		return new
		{
			Seconds = Round( system.TrackingElapsed.TotalSeconds, 1 ),
			KiBPerSecondByType = byType.OrderByDescending( x => x.Value ).ToDictionary( x => x.Key.ToString(), x => KiB( window > 0 ? x.Value / window : 0 ) ),
			InboundCalls = MessageStats( system.InboundStats, limit ),
			OutboundCalls = MessageStats( system.OutboundStats, limit ),
			InboundSyncVars = MessageStats( system.SyncVarInboundStats, limit ),
			OutboundSyncVars = MessageStats( system.SyncVarOutboundStats, limit ),
		};
	}

	static object MessageStats( Dictionary<string, NetworkDebugSystem.MessageStats> stats, int limit ) =>
		stats?.OrderByDescending( x => x.Value.TotalBytes ).Take( limit ).Select( x => new
		{
			Name = x.Key,
			Calls = x.Value.TotalCalls,
			x.Value.TotalBytes,
			RecentBytesPerMessage = x.Value.BytesPerMessage,
			x.Value.PeakBytes,
		} ).ToList();

	/// <summary>
	/// GPU pass timings only run while something wants them. Switch them on and give them a second to arrive and settle -
	/// the smoothed durations ease in over about that long.
	/// </summary>
	static async Task WarmGpu()
	{
		if ( PerfHub.WantsGpuTimings && GpuProfilerStats.RowCount > 0 )
		{
			PerfHub.CollectFor( "gpu", Linger );
			return;
		}

		await CollectFor( "gpu", 1, Linger );
	}

	/// <summary>
	/// Run a tab's collectors for <paramref name="seconds"/>, then come back to the main thread.
	/// </summary>
	static async Task CollectFor( string tab, float seconds, float linger )
	{
		PerfHub.CollectFor( tab, seconds + linger );
		await Task.Delay( TimeSpan.FromSeconds( seconds ), McpSession.Current?.Cancellation ?? default );
		await MainThread.Wait();
	}

	static object Window( PerfHub.Summary s ) => new { s.Frames, Seconds = Round( s.WindowMs / 1000, 2 ) };

	static object Stat( PerfHub.Stat stat ) => new { AvgMs = Round( stat.Avg ), P50Ms = Round( stat.P50 ), P99Ms = Round( stat.P99 ), MaxMs = Round( stat.Max ) };

	static object Hitches( PerfHub.Summary s ) => new { Count = s.Spikes, OverMs = Round( s.HitchMs, 1 ), WorstMs = Round( s.WorstSpikeMs, 1 ) };

	static object Issues( List<PerfHub.Issue> issues ) => issues.Select( x => new
	{
		x.Severity,
		x.Title,
		x.Detail,
		MoreIn = x.Tab is null ? null : $"perf_{x.Tab}",
	} ).ToList();

	static object Costs( List<PerfHub.Cost> costs, int limit ) => costs.Take( limit ).Select( x => new
	{
		x.Name,
		AvgMs = Round( x.AvgMs ),
		MaxMs = Round( x.MaxMs ),
	} ).ToList();

	/// <summary>
	/// GPU costs carry the pass's parent, since leaf names like "Bloom" or "Clear" repeat under different parents.
	/// </summary>
	static object GpuCosts( List<PerfHub.Cost> costs, int limit ) => costs.Take( limit ).Select( x => new
	{
		x.Name,
		AvgMs = Round( x.AvgMs ),
		MaxMs = Round( x.MaxMs ),
		Path = x.Tooltip,
	} ).ToList();

	static object VideoMemory() => new
	{
		UsedMiB = MiB( GpuProfilerStats.VideoMemoryUsed ),
		BudgetMiB = GpuProfilerStats.VideoMemoryBudget > 0 ? MiB( GpuProfilerStats.VideoMemoryBudget ) : (double?)null,
	};

	/// <summary>
	/// Live count by type and the most there have been. Dead entries are references whose target has gone.
	/// </summary>
	static object Counts( Dictionary<string, int> current, Dictionary<string, int> max, int limit ) =>
		current?.OrderByDescending( x => x.Value ).Take( limit ).Select( x => new { Type = x.Key, Live = x.Value, Max = max.GetValueOrDefault( x.Key ) } ).ToList();

	static double Round( double value, int digits = 2 ) => double.IsFinite( value ) ? Math.Round( value, digits ) : 0;

	static double MiB( double bytes ) => Round( bytes / (1024 * 1024), 1 );

	static double KiB( double bytes ) => Round( bytes / 1024, 1 );
}
