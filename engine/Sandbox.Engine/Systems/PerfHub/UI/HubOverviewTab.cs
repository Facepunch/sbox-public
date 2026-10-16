using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// What's out of line, how the frames are going, and where the time goes. The one tab everyone gets.
/// </summary>
internal sealed class HubOverviewTab : HubTab
{
	public override string TabId => PerfHub.OverviewTabId;
	public override string Title => "Overview";
	public override bool Advanced => false;

	const int CostRows = 8;

	readonly HubFindings _findings;
	readonly HubSection _chart;
	readonly HubTable _frame;
	readonly HubFacts _pacing;
	readonly HubTable _cpuCosts, _gpuCosts;
	readonly HubFacts _resources;

	public HubOverviewTab()
	{
		_findings = AddChild( new HubFindings() );

		_chart = AddChild( new HubSection( "Frame time" ) );
		_chart.Body.AddChild( new HubFrameChart() );

		_frame = AddChild( new HubTable( "minmax(0, 1fr) 64px 64px 64px 64px", "Frame breakdown", "#avg", "#p50", "#p99", "#max" ).Banded() );
		_pacing = AddChild( new HubFacts() );

		var split = AddChild( new Panel( null, "split" ) );
		_cpuCosts = split.AddChild( new Panel( null, "section" ) ).AddChild( HubTable.ForCosts( "Main thread" ) );
		_gpuCosts = split.AddChild( new Panel( null, "section" ) ).AddChild( HubTable.ForCosts( "GPU passes" ) );

		_resources = AddChild( new HubFacts() );
	}

	public override void Refresh( PerfHub.Summary s )
	{
		var advanced = PerfHub.ShowAdvanced;
		var target = s.TargetMs;

		_findings.Set( s.Issues );

		_chart.Aside.Text = target > 0 ? $"target {target:0.00} ms ({s.TargetFps:0} fps)" : "";

		var t = _frame;
		t.Begin();
		if ( s.Frames > 0 )
		{
			var work = MathF.Max( 0, s.FrameAvg - s.HubAvg );
			Series( t, "frame", "Frame", s.FrameTime, s, work > target * 1.5f ? PerfHub.Severity.Bad : work > target * 1.05f ? PerfHub.Severity.Warning : null, null );
			Series( t, "cpu", "CPU work", s.CpuTime, s, s.Limiter == PerfHub.Limiter.Cpu && !s.MeetingTarget ? s.Rating : null, advanced ? "cpu" : null );
			if ( s.GpuAvg > 0 )
				Series( t, "gpu", "GPU", s.GpuTime, s, s.Limiter == PerfHub.Limiter.Gpu && !s.MeetingTarget ? s.Rating : null, advanced ? "gpu" : null );
			Series( t, "wait", "Cap wait", s.WaitTime, s, null, null, dim: true );
			Series( t, "hub", "Hub (this panel)", s.HubTime, s, null, null, dim: true );
		}
		t.End( "Waiting for frames" );

		_pacing.Set( "Hitches", s.Spikes > 0 ? $"{s.Spikes}, worst {s.WorstSpikeMs:0} ms" : "0",
			s.Spikes > 4 || s.WorstSpikeMs > 100 ? PerfHub.Severity.Bad : s.Spikes > 0 ? PerfHub.Severity.Warning : null,
			$"Frames over {s.HitchMs:0.0} ms - twice the median, and half again over the target" );
		_pacing.Set( "Window", $"{s.WindowMs / 1000:0.0} s", tooltip: $"{s.Frames} frames" );
		_pacing.Set( "Target", $"{s.TargetFps:0} fps", tooltip: $"From {s.TargetSource}" );

		_cpuCosts.SetCosts( s.TopCpu, CostRows, s.CpuWorkAvg, advanced ? "cpu" : null, "Nothing measured" );
		_gpuCosts.SetCosts( s.TopGpu, CostRows, s.GpuAvg, advanced ? "gpu" : null, "No GPU timings yet" );

		var second = PerformanceStats.LastSecond;
		var gcMs = TimeSpan.FromTicks( second.GcPause ).TotalMilliseconds;
		var f = FrameStats.Current;

		_resources.Set( "Process", HubFormat.Bytes( PerformanceStats.ApproximateProcessMemoryUsage ) );
		if ( GpuProfilerStats.VideoMemoryBudget > 0 )
		{
			var vram = GpuProfilerStats.VideoMemoryUsageFraction;
			_resources.Set( "VRAM", HubFormat.BytesOf( GpuProfilerStats.VideoMemoryUsed, GpuProfilerStats.VideoMemoryBudget ),
				vram > 0.95f ? PerfHub.Severity.Bad : vram > 0.8f ? PerfHub.Severity.Warning : null );
		}
		_resources.Set( "Alloc", $"{HubFormat.Bytes( second.ByteAlloc )}/s", second.ByteAlloc > 20 * 1024 * 1024 ? PerfHub.Severity.Warning : null, "Managed allocations in the last second" );
		_resources.Set( "GC", $"{gcMs:0.0} ms/s", gcMs > 2 || second.Gc2 > 0 ? PerfHub.Severity.Warning : null, $"Paused for collection in the last second. gen0 {second.Gc0}, gen1 {second.Gc1}, gen2 {second.Gc2}" );
		_resources.Set( "Draws", $"{(f.Managed.Renders > 0 ? f.Managed.TotalDraws : f.DrawCalls):N0}" );
		_resources.Set( "Triangles", HubFormat.Count( f.Managed.Renders > 0 ? f.Managed.Triangles : f.TrianglesRendered ) );
	}

	static void Series( HubTable t, string key, string name, PerfHub.Stat stat, PerfHub.Summary s, PerfHub.Severity? avg, string tab, bool dim = false )
	{
		var p99 = stat.P99 > s.HitchMs ? PerfHub.Severity.Bad : stat.P99 > s.TargetMs * 1.25f && !dim ? PerfHub.Severity.Warning : (PerfHub.Severity?)null;
		var max = stat.Max > s.HitchMs && !dim ? PerfHub.Severity.Warning : (PerfHub.Severity?)null;

		t.Row( key, [
			new HubTable.Cell( name, dim ? "dim" : null ),
			Ms( stat.Avg, dim ? null : avg, dim ),
			Ms( stat.P50, null, dim ),
			Ms( stat.P99, dim ? null : p99, dim ),
			Ms( stat.Max, max, dim ),
		], tab is null ? null : () => PerfHub.ActiveTab = tab );
	}

	static HubTable.Cell Ms( float ms, PerfHub.Severity? severity, bool dim ) =>
		dim && severity is null ? new( HubFormat.Ms( ms ), "number dim" ) : HubTable.Number( HubFormat.Ms( ms ), severity );
}
