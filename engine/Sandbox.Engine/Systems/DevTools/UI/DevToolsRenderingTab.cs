using Sandbox.Engine.Settings;
using Sandbox.Rendering;
using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// The renderer's frame: GPU time frame by frame, every pass in the order it ran with what it cost on the GPU and - through
/// the managed renderer - to record, the picked pass in detail, the frame on one clock from the CPU starting it to the
/// GPU finishing it, and the frame's counters. The way GPU profilers lay it out: history, tree, detail, timeline.
/// </summary>
internal sealed class DevToolsRenderingTab : DevToolsTab
{
	public override string TabId => "rendering";
	public override string Title => "Rendering";

	readonly DevToolsFacts _context;
	readonly DevToolsTable _managedTree, _nativeTree, _skippedTable;
	readonly Label _orderButton, _skippedToggle, _treeAside;
	readonly DevToolsSection _detail;
	readonly DevToolsFacts _frameFacts, _passFacts;
	readonly Panel _detailChart;
	readonly Label _detailHint;
	readonly DevToolsFacts _counters;
	readonly Panel _deviceLines;
	readonly List<DevTools.RenderPass> _passes = new();
	readonly List<DevTools.SkippedLayer> _skipped = new();
	readonly List<DevTools.GpuPass> _profilerPasses = new();
	readonly List<(string View, string Pass)> _views = new();
	string _deviceKey;
	DevTools.PassOrder _order;
	bool _showSkipped;
	string _filter = "";
	string _chartFor;

	public DevToolsRenderingTab()
	{
		AddClass( "rendering" );

		_context = AddChild( new DevToolsFacts() );
		_context.AddClass( "span-all" );

		// GPU time frame by frame, scaled to itself rather than the budget so its ups and downs show; picking a frame
		// pauses on it
		var history = AddChild( new Panel( null, "render-history span-all" ) );
		history.AddChild( new Label( "gpu ms", "render-history-key" ) );
		history.AddChild( new DevToolsSparkline
		{
			Sample = age => DevTools.ShownFrames.GpuMs( age ),
			Floor = 1,
			Color = DevToolsStyles.Gpu,
			Selectable = true,
		} );

		// The whole frame on one clock, over the passes it's made of
		var timeline = AddChild( new DevToolsSection( "Timeline", "span-all" ) );
		var chart = timeline.Body.AddChild( new DevToolsFrameTimeline { Status = timeline.Aside } );
		var fitGpu = timeline.Head.AddChild( new Label( "fit gpu", "head-button" ) );
		fitGpu.Tooltip = "The GPU's work. Double click empty space to go back a zoom";
		fitGpu.AddEventListener( "onclick", () => chart.Fit( false ) );
		var fitAll = timeline.Head.AddChild( new Label( "fit frame", "head-button" ) );
		fitAll.Tooltip = "The whole frame, from the CPU starting on it to the GPU finishing it";
		fitAll.AddEventListener( "onclick", () => chart.Fit( true ) );

		var split = AddChild( new Panel( null, "pass-split span-all" ) );
		var tree = split.AddChild( new Panel( null, "section pass-tree" ) );

		var tools = tree.AddChild( new Panel( null, "section-head" ) );
		tools.AddChild( new Label( "Passes", "section-title" ) );
		_treeAside = tools.AddChild( new Label( "", "section-aside" ) );
		// Find a pass by name: the matches, and what they're in
		var filter = tools.AddChild( new TextEntry() );
		filter.AddClass( "pass-filter" );
		filter.Placeholder = "find";
		filter.AddEventListener( "onchange", () => { _filter = filter.Text ?? ""; Refresh( DevTools.Current ); } );

		_orderButton = tools.AddChild( new Label( "", "head-button" ) );
		_orderButton.AddEventListener( "onclick", () =>
		{
			// Recording time only means something through the managed renderer
			var managed = FrameStats.Current.Managed.Renders > 0;
			_order = _order switch
			{
				DevTools.PassOrder.Started => DevTools.PassOrder.Gpu,
				DevTools.PassOrder.Gpu when managed => DevTools.PassOrder.Recording,
				_ => DevTools.PassOrder.Started,
			};
			Refresh( DevTools.Current );
		} );

		const string managedColumns = "minmax(140px, 1fr) 40px 52px 52px minmax(40px, 110px) 52px 52px 52px";
		_managedTree = tree.AddChild( new DevToolsTable( managedColumns, "pass", "", "#gpu ms", "#max", "", "#rec ms", "#draws", "#binds" ) );
		_nativeTree = tree.AddChild( new DevToolsTable( "minmax(140px, 1fr) 40px 52px 52px minmax(40px, 160px)", "pass", "", "#gpu ms", "#max", "" ) );

		_skippedToggle = tree.AddChild( new Label( "", "pass-skipped" ) );
		_skippedToggle.AddEventListener( "onclick", () => { _showSkipped = !_showSkipped; Refresh( DevTools.Current ); } );
		_skippedTable = tree.AddChild( new DevToolsTable( "minmax(140px, 1fr) 80px minmax(0, 1fr)" ) );

		_detail = split.AddChild( new DevToolsSection( "Frame", "pass-detail" ) );
		_frameFacts = _detail.Body.AddChild( new DevToolsFacts() );
		_passFacts = _detail.Body.AddChild( new DevToolsFacts() );
		_detailChart = _detail.Body.AddChild( new Panel( null, "pass-chart" ) );
		_detailChart.AddChild( new DevToolsSparkline
		{
			Sample = age => _chartFor is null ? 0 : DevTools.GpuSpanSample( _chartFor, age ),
			Count = () => _chartFor is null ? 0 : DevTools.GpuSpanSamples( _chartFor ),
			Capacity = DevTools.GpuSpanHistory,
			Floor = 0.05f,
			Color = DevToolsStyles.Gpu,
		} );
		_detailHint = _detail.Body.AddChild( new Label( "", "hint" ) );

		_counters = AddChild( new DevToolsFacts() );
		_counters.AddClass( "span-all" );

		var device = AddChild( new DevToolsSection( "Device report", "span-all" ) );
		_deviceLines = new Panel( null, "device-lines" );
		device.Collapsible( body => body.AddChild( _deviceLines ) );
	}

	/// <summary>
	/// While this is on screen, ask the managed renderer to keep publishing its plan.
	/// </summary>
	public override void Tick()
	{
		base.Tick();
		if ( DevTools.IsShowing( TabId ) ) ManagedFramePlan.Want();
	}

	public override void Refresh( DevTools.Summary s )
	{
		var f = FrameStats.Current;
		var m = f.Managed;
		var isManaged = m.Renders > 0;
		var timeline = DevTools.ShownGpuTimeline;
		var plan = isManaged ? timeline.Plan ?? ManagedFramePlan.Latest : null;

		// Native's passes go under the scene views the GPU profiler has them in
		_views.Clear();
		if ( plan is null )
		{
			DevTools.GetGpuPasses( _profilerPasses );
			string view = null;
			foreach ( var pass in _profilerPasses )
			{
				if ( pass.Depth == 1 ) view = pass.Name;
				else if ( pass.Depth == 2 && view is not null ) _views.Add( (view, pass.Name) );
			}
		}

		DevTools.BuildPasses( timeline, plan, _views, plan is null && _order == DevTools.PassOrder.Recording ? DevTools.PassOrder.Gpu : _order, _passes, _skipped );

		RefreshContext( s, f, m, isManaged, timeline );
		RefreshTree( s, plan is not null );
		RefreshDetail( s, plan, timeline );
		RefreshCounters( f, m, isManaged );
		RefreshDevice( f );
	}

	void RefreshContext( DevTools.Summary s, FrameStats f, ManagedFrameCounters m, bool isManaged, DevTools.GpuLayout timeline )
	{
		var rs = RenderSettings.Instance;
		var samples = rs.AntiAliasQuality.ToString();
		var msaa = rs.AntiAliasQuality == MultisampleAmount.MultisampleNone ? null : $"{samples["Multisample".Length..]} MSAA";
		var aa = rs.AntiAliasMode == AntiAliasMode.Cmaa2
			? msaa is null ? "CMAA2" : $"CMAA2 + {msaa}"
			: msaa ?? "off";

		_context.Set( "Showing", DevToolsFrameTimeline.Showing( timeline ), tooltip: timeline.CapturedOn > 0
			? "A frame is picked: the GPU's passes are one captured frame, the soonest after it - GPU results arrive a few frames late. Counters are the last second's"
			: "GPU times are averaged over the frames captured in the last two seconds; max is the most any of them took" );
		_context.Set( "Renderer", isManaged ? "managed" : "native", tooltip: isManaged
			? $"{m.Renders} camera {(m.Renders == 1 ? "frame" : "frames")} through Sandbox.SceneRenderer last frame"
			: ManagedSceneRendering.Enabled ? "r_managed_scene is on, but no camera rendered through it" : "The Source 2 scene system" );
		_context.Set( "Resolution", $"{Screen.Width:0}x{Screen.Height:0}" );
		_context.Set( "AA", aa );
		_context.Set( "Upscaling", rs.UpscalerMode.ToString().ToLowerInvariant() );
		_context.Set( "GPU frame", s.GpuAvg > 0 ? $"{s.GpuAvg:0.00} ms" : "-", s.Limiter == DevTools.Limiter.Gpu && !s.MeetingTarget ? s.Rating : null,
			s.GpuAvg > 0 ? $"p99 {s.GpuTime.P99:0.00} ms, max {s.GpuTime.Max:0.0} ms\n{s.GpuAvg / MathF.Max( 0.01f, s.TargetMs ) * 100:0}% of the {s.TargetMs:0.0} ms budget" : null );
		_context.Set( "GPU busy", timeline.ActiveMs > 0 ? $"{timeline.BusyShare * 100:0}%" : "-",
			tooltip: timeline.ActiveMs > 0 ? $"The graphics queue had a pass running {timeline.BusyMs:0.00} ms of the {timeline.ActiveMs:0.00} ms from its first pass to its last{(timeline.HasAsync ? $"\nAsync compute busy {timeline.AsyncBusyMs:0.00} ms beside it" : "")}" : null );
	}

	void RefreshTree( DevTools.Summary s, bool managed )
	{
		_managedTree.SetClass( "hidden", !managed );
		_nativeTree.SetClass( "hidden", managed );
		var table = managed ? _managedTree : _nativeTree;
		var gpuFrame = s.GpuAvg > 0 ? s.GpuAvg : DevTools.ShownGpuTimeline.BusyMs;
		var selected = DevTools.SelectedPass;

		(_orderButton.Text, _orderButton.Tooltip) = _order switch
		{
			DevTools.PassOrder.Gpu => ("by gpu ms", "Most GPU time first among siblings. Click to sort by recording time"),
			DevTools.PassOrder.Recording when managed => ("by rec ms", "Most recording time first among siblings, what's under them included. Click for the order they ran in"),
			_ => ("in order", "In the order they started on the GPU. Click to sort by GPU time"),
		};

		// What the filter finds, and what each match is in, so it stays in its place in the tree
		var shown = Filtered();

		// Passes that took next to nothing fold away with the layers that didn't run
		var idle = 0;
		var idleMs = 0f;
		table.Begin();
		for ( int index = 0; index < _passes.Count; index++ )
		{
			var pass = _passes[index];
			if ( shown is not null && !shown[index] ) continue;
			if ( shown is null && IsIdle( pass ) && pass.Key != selected && !_showSkipped )
			{
				idle++;
				if ( pass.Parent < 0 || !IsIdle( _passes[pass.Parent] ) ) idleMs += pass.GpuMs;
				continue;
			}

			var gpu = pass.GpuMs;
			var layer = pass.Layer;
			var key = pass.Key;
			Action pick = () => DevTools.SelectedPass = DevTools.SelectedPass == key ? null : key;

			var none = new DevToolsTable.Cell( "", "number" );
			// The kind's colour, as the timeline draws it
			var kind = pass.Span?.Kind ?? DevTools.KindOf( pass.Name, DevTools.PassKind.Other );
			var name = new DevToolsTable.Cell( pass.Name, null, DevToolsStyles.PassColor( kind ), Indent: 10 + pass.Depth * 14 );
			var queue = new DevToolsTable.Cell( pass.Async ? "async" : "", "dim" );
			var avg = gpu > 0 ? DevToolsTable.Number( DevToolsFormat.Ms( gpu ) ) : none;
			var max = pass.Span is { MaxMs: > 0 } span ? new DevToolsTable.Cell( DevToolsFormat.Ms( span.MaxMs ), "number dim" ) : none;
			var bar = gpu > 0 && gpuFrame > 0 ? DevToolsTable.BarOf( gpu / gpuFrame ) : "";
			var rowClass = key == selected ? "selected" : pass.Heading ? "heading" : null;

			if ( !managed )
			{
				table.Row( key, [name, queue, avg, max, bar], pick, PassTooltip( pass ), rowClass );
				continue;
			}

			var l = layer ?? default;
			var cpu = l.CpuMs > 0 ? DevToolsTable.Number( DevToolsFormat.Ms( l.CpuMs ) ) : none;
			var draws = l.Recorded > 0 ? DevToolsTable.Number( DevToolsFormat.Count( l.Recorded ) ) : none;
			var binds = l.Binds + l.SimilarBinds > 0 ? DevToolsTable.Number( DevToolsFormat.Count( l.Binds + l.SimilarBinds ) ) : none;
			table.Row( key, [name, queue, avg, max, bar, cpu, draws, binds], pick, PassTooltip( pass ), rowClass );
		}
		table.End( shown is not null ? "Nothing by that name" : "Waiting for GPU timings" );

		_treeAside.Text = _passes.Count > 0 ? $"{_passes.Count( x => x.Depth == 0 )} at the top, {_passes.Count} in all" : "";

		// The managed frame's layers that didn't run, folded away
		if ( _showSkipped ) idle = _passes.Count( IsIdle );
		var folded = new List<string>();
		if ( idle > 0 ) folded.Add( $"{idle} {(idle == 1 ? "pass" : "passes")} under 0.02 ms{(idleMs > 0 ? $" ({DevToolsFormat.Ms( idleMs )} ms between them)" : "")}" );
		if ( _skipped.Count > 0 ) folded.Add( $"{_skipped.Count} {(_skipped.Count == 1 ? "layer" : "layers")} that didn't run" );
		_skippedToggle.SetClass( "hidden", folded.Count == 0 );
		_skippedToggle.Text = _showSkipped ? $"hide the {string.Join( " and ", folded )}" : $"show the {string.Join( " and ", folded )}";
		_skippedTable.SetClass( "hidden", !_showSkipped || _skipped.Count == 0 );
		if ( _showSkipped )
		{
			_skippedTable.Begin();
			foreach ( var skip in _skipped )
				_skippedTable.Row( $"{skip.Frame}:{skip.Name}", [new( skip.Name, "dim", Indent: 10 ), new( skip.Frame, "dim" ), new( skip.Why, "note" )] );
			_skippedTable.End();
		}
	}

	/// <summary>
	/// Which rows the filter keeps - each pass whose name has it in, and what that pass is in - or null with no filter.
	/// </summary>
	bool[] Filtered()
	{
		if ( string.IsNullOrWhiteSpace( _filter ) ) return null;

		var keep = new bool[_passes.Count];
		for ( int i = 0; i < _passes.Count; i++ )
		{
			if ( !_passes[i].Name.Contains( _filter.Trim(), StringComparison.OrdinalIgnoreCase ) ) continue;
			for ( int p = i; p >= 0 && !keep[p]; p = _passes[p].Parent ) keep[p] = true;
		}

		return keep;
	}

	/// <summary>
	/// Took next to nothing on either side: under 0.02 ms on the GPU on average, never 0.05, and nothing much to record.
	/// </summary>
	static bool IsIdle( DevTools.RenderPass pass ) =>
		pass.GpuMs < 0.02f && (pass.Span?.MaxMs ?? 0) < 0.05f && (pass.Layer?.CpuMs ?? 0) < 0.02 && (pass.Layer?.Recorded ?? 0) == 0;

	static string PassTooltip( DevTools.RenderPass pass )
	{
		var lines = new List<string> { pass.Name };
		if ( pass.Span is { } span ) lines.Add( span.MaxMs > 0 ? $"GPU {span.AvgMs:0.000} ms avg, {span.MaxMs:0.000} ms max, from {span.StartMs:0.00} ms" : $"GPU {span.AvgMs:0.000} ms avg, from {span.StartMs:0.00} ms" );
		if ( pass.Layer is { } layer && layer.Pieces > 0 ) lines.Add( $"Recorded in {layer.CpuMs:0.000} ms over {layer.Pieces} {(layer.Pieces == 1 ? "piece" : "pieces")}, added across threads" );

		// How it was placed, where that's inferred rather than recorded
		if ( pass.Key.StartsWith( "view:" ) ) lines.Add( "Grouped by pass names: the GPU timeline doesn't record which view drew what" );
		else if ( pass.Layer is not null && pass.Span is not null ) lines.Add( "Matched to its layer by name and the order the layers ran" );
		return string.Join( "\n", lines );
	}

	void RefreshDetail( DevTools.Summary s, ManagedFramePlan plan, DevTools.GpuLayout timeline )
	{
		var selected = DevTools.SelectedPass is { } key ? _passes.FirstOrDefault( x => x.Key == key ) : null;
		var facts = selected is null ? _frameFacts : _passFacts;
		_frameFacts.SetClass( "hidden", selected is not null );
		_passFacts.SetClass( "hidden", selected is null );

		// The facts a pass has, the rest hidden
		var shown = new HashSet<string>();
		void Set( string name, string value, string tooltip = null )
		{
			facts.Set( name, value, tooltip: tooltip );
			shown.Add( name );
		}

		if ( selected is null )
		{
			_detail.TitleLabel.Text = "Frame";
			_detail.Aside.Text = "";
			_chartFor = null;

			if ( timeline.ElapsedMs > 0 )
			{
				Set( "GPU span", $"{timeline.ActiveMs:0.00} ms", "From the GPU's first pass on the frame to its last" );
				Set( "GPU busy", $"{timeline.BusyMs:0.00} ms", $"Time the graphics queue had a pass running, {timeline.BusyShare * 100:0}% of the span" );
				Set( "Submit to done", $"{timeline.ElapsedMs:0.00} ms", "From the frame's first submit to its last pass ending, the wait before the GPU starts included" );
				if ( timeline.HasAsync ) Set( "Async busy", $"{timeline.AsyncBusyMs:0.00} ms", "Time the async compute queue had a pass running, beside the graphics queue" );
				Set( "Submits", $"{timeline.Submits.Count}" );
			}

			if ( plan is not null )
			{
				var main = plan.CollectMs + plan.PrepareMs + plan.SetupMs + plan.RecordMs + plan.SubmitMs;
				var workers = plan.Segments.Where( x => x.Thread != plan.MainThread ).Select( x => x.Thread ).Distinct().Count();
				Set( "CPU", $"{main:0.00} ms", $"The main thread making the frame: collect {plan.CollectMs:0.00}, prepare {plan.PrepareMs:0.00}, setup {plan.SetupMs:0.00}, record {plan.RecordMs:0.00}, submit {plan.SubmitMs:0.00} ms" );
				Set( "Workers", $"{plan.WorkerMs:0.00} ms", $"Recording on {workers} worker {(workers == 1 ? "thread" : "threads")}, added up. They run beside the main thread, which waited {plan.RecordWaitMs:0.00} ms on them" );
				Set( "Segments", $"{plan.Segments.Count}", $"{plan.Segments.Count( x => x.Async )} on the async compute queue" );
			}

			_detailHint.Text = "Pick a pass in the tree or on the timeline";
		}
		else
		{
			_detail.TitleLabel.Text = selected.Name;
			_detail.Aside.Text = selected.Heading ? "frame" : selected.Async ? "async compute" : "";
			_chartFor = selected.Heading ? null : selected.Span?.Path;

			if ( selected.Span is { } span )
			{
				var gpuFrame = s.GpuAvg > 0 ? s.GpuAvg : timeline.BusyMs;
				Set( "GPU", $"{span.AvgMs:0.000} ms", "Average over the frames captured in the last two seconds" );
				if ( span.MaxMs > 0 ) Set( "GPU max", $"{span.MaxMs:0.000} ms" );
				if ( gpuFrame > 0 ) Set( "Of GPU frame", $"{span.AvgMs / gpuFrame * 100:0}%" );
				Set( "Runs", $"{span.StartMs:0.00} to {span.EndMs:0.00} ms", "From the frame's first submit" );
			}

			if ( selected.Layer is { } layer && plan is not null )
			{
				if ( layer.Pieces > 0 )
					Set( "Recording", $"{layer.CpuMs:0.000} ms", $"CPU time recording it, over {layer.Pieces} {(layer.Pieces == 1 ? "piece" : "pieces")}, added up across threads - not how long it took end to end" );
				if ( layer.Recorded > 0 ) Set( "Draws", $"{layer.Recorded:N0}", layer.Views > 0 ? $"{layer.Views:N0} views, {layer.Runs:N0} runs" : null );
				if ( layer.Binds + layer.SimilarBinds > 0 ) Set( "Binds", $"{layer.Binds + layer.SimilarBinds:N0}", $"{layer.Binds:N0} full material binds, {layer.SimilarBinds:N0} similar materials rebinding only what differs" );
				if ( layer.Triangles > 0 ) Set( "Triangles", DevToolsFormat.Count( layer.Triangles ) );
				if ( layer.Makes != 0 ) Set( "Makes", Names( plan, layer.Makes, 0 ) );
				if ( layer.Reads != 0 ) Set( "Reads", Names( plan, layer.Reads, layer.NotMade ), layer.NotMade != 0 ? "? read without anything making it this frame" : null );
			}

			if ( Effect( selected.Name ) is { } effect )
				Set( "Post-process", $"{Words( effect.Stage )}, order {effect.Layer.Order}", string.Join( ", ", Flags( effect.Layer ) ) );

			var children = _passes.Count( x => x.Parent >= 0 && _passes[x.Parent] == selected );
			if ( children > 0 ) Set( "Passes in it", $"{children}" );

			_detailHint.Text = "";
		}

		foreach ( var name in DetailFacts )
			if ( !shown.Contains( name ) ) facts.Hide( name );

		_detailChart.SetClass( "hidden", _chartFor is null );
		_detailHint.SetClass( "hidden", string.IsNullOrEmpty( _detailHint.Text ) );
	}

	static readonly string[] DetailFacts = ["GPU span", "GPU busy", "Submit to done", "Async busy", "Submits", "CPU", "Workers", "Segments", "GPU", "GPU max", "Of GPU frame", "Runs", "Recording", "Draws", "Binds", "Triangles", "Makes", "Reads", "Post-process", "Passes in it"];

	/// <summary>
	/// The active camera's post-processing effect of this name, which the GPU timeline shows as a pass.
	/// </summary>
	static (PostProcessLayer Layer, string Stage)? Effect( string name )
	{
		var layers = Application.GetActiveScene()?.Camera?.PostProcess.Layers;
		if ( layers is null ) return null;

		foreach ( var (stage, list) in layers )
			foreach ( var layer in list )
				if ( layer.Name == name ) return (layer, stage.ToString());

		return null;
	}

	static IEnumerable<string> Flags( PostProcessLayer layer )
	{
		if ( layer.RanEarly ) yield return "ran early";
		if ( layer.EarlyQueue is { } queue )
			yield return queue == PostProcessQueue.AsyncCompute ? "early compute capable" : "early graphics capable";
		if ( layer.NeedsDepthNormals ) yield return "depth normals";
	}

	void RefreshCounters( FrameStats f, ManagedFrameCounters m, bool isManaged )
	{
		var c = _counters;
		var drawCalls = isManaged ? m.TotalDraws : f.DrawCalls;
		var triangles = isManaged ? m.Triangles : f.TrianglesRendered;

		c.Set( "Draws", $"{drawCalls:N0}", drawCalls >= 5000 ? DevTools.Severity.Bad : drawCalls >= 3000 ? DevTools.Severity.Warning : null,
			isManaged ? $"{m.Draws:N0} forward ({m.TranslucentDraws:N0} translucent), {m.DepthDraws:N0} depth, {m.ShadowDraws:N0} shadow, {m.CustomDraws:N0} custom"
				: $"{f.RenderBatchDraws:N0} batches, {f.AggregateObjectDraws:N0} aggregated\n{f.ConstantBufferUpdates:N0} constant buffer updates, {DevToolsFormat.Bytes( f.ConstantBufferBytes )}\n{f.PrimaryContexts + f.SecondaryContexts:N0} contexts, {f.DisplayLists:N0} display lists" );
		c.Set( "Triangles", DevToolsFormat.Count( triangles ), tooltip: drawCalls > 0 ? $"{triangles / drawCalls:N0} a draw{(isManaged ? ", forward only" : "")}" : null );

		if ( isManaged )
		{
			var plan = ManagedFramePlan.Latest;
			var binds = 0; var similar = 0;
			if ( plan is not null )
				foreach ( var frame in plan.Frames )
					foreach ( var layer in frame.Layers ) { binds += layer.Binds; similar += layer.SimilarBinds; }

			c.Set( "Binds", binds + similar > 0 ? $"{binds + similar:N0}" : "-", tooltip: $"{binds:N0} full material binds, {similar:N0} similar materials rebinding only what differs" );
			c.Set( "Objects", $"{m.ObjectsVisible:N0}", tooltip: $"Visible, of {m.Objects:N0}\n{m.ObjectsSizeCulled:N0} too small{(ManagedSceneRendering.Occlusion ? $", {m.ObjectsOcclusionCulled:N0} hidden (occlusion, read back late)" : "")}\n{m.Instances:N0} instances" );
			c.Set( "Lights", $"{m.Lights:N0}", tooltip: $"Lights binned for the view" );
			c.Set( "Shadow views", $"{m.ShadowViews:N0}", tooltip: $"{m.ShadowDraws:N0} shadow draws" );
		}
		else
		{
			var matChanges = f.MaterialChanges + f.ShadowMaterialChanges;
			c.Set( "Binds", $"{matChanges:N0}", tooltip: $"Material changes, {f.ShadowMaterialChanges:N0} in shadows\n{f.FullMaterialSets:N0} full sets, {f.SimilarMaterialSets:N0} similar" );

			var tested = f.ObjectsTested;
			var occlusion = RenderPipeline.OcclusionCulling ? $"\n{f.ObjectsCulledByOcclusion:N0} hidden instances (occlusion, read back late)" : "";
			c.Set( "Objects", $"{f.ObjectsRendered:N0}", tooltip: $"Drawn, of {tested:N0} tested\n{f.ObjectsCulledByVis:N0} vis culled, {f.ObjectsCulledByScreenSize:N0} too small, {f.ObjectsCulledByFade:N0} faded out{occlusion}" );
			c.Set( "Lights", $"{f.ShadowedLightsInView + f.UnshadowedLightsInView:N0}", tooltip: $"{f.ShadowedLightsInView:N0} shadowed, {f.UnshadowedLightsInView:N0} unshadowed" );
			c.Set( "Shadow views", $"{f.ShadowMaps:N0}", tooltip: "Shadow maps rendered" );
		}

		c.Set( "Scene views", $"{f.SceneViewsRendered:N0}", tooltip: $"{f.RenderTargetResolves:N0} resolves" );
	}

	void RefreshDevice( FrameStats f )
	{
		if ( _deviceLines.Parent is null ) return;

		var summary = f.GpuStatsSummary ?? "";
		if ( summary == _deviceKey ) return;
		_deviceKey = summary;

		_deviceLines.DeleteChildren( true );
		foreach ( var line in summary.Split( '\n', StringSplitOptions.RemoveEmptyEntries ) )
			_deviceLines.AddChild( new Label( line.TrimEnd( '\r' ) ) );
	}

	/// <summary>
	/// The names of the resources in a mask, those in <paramref name="missing"/> marked as read without being made.
	/// </summary>
	static string Names( ManagedFramePlan plan, uint mask, uint missing )
	{
		var names = new List<string>();
		for ( int bit = 0; bit < 32 && bit < plan.Resources.Length; bit++ )
		{
			if ( (mask & (1u << bit)) == 0 ) continue;
			names.Add( (missing & (1u << bit)) != 0 ? $"{plan.Resources[bit]}?" : plan.Resources[bit] );
		}

		return string.Join( ", ", names );
	}

	/// <summary>
	/// "BeforePostProcess" to "before post process".
	/// </summary>
	static string Words( string name )
	{
		var words = new System.Text.StringBuilder( name.Length + 8 );
		for ( int i = 0; i < name.Length; i++ )
		{
			var c = name[i];
			if ( i > 0 && char.IsUpper( c ) && !char.IsUpper( name[i - 1] ) ) words.Append( ' ' );
			words.Append( char.ToLowerInvariant( c ) );
		}

		return words.ToString();
	}
}
