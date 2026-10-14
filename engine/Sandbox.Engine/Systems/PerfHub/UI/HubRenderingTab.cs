using Sandbox.Engine.Settings;
using Sandbox.Rendering;
using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// What the renderer submitted last frame, what it threw away, and the resources and effects it leaned on. Numbers in
/// tables, coloured where they're out of line, with what's out of line listed first.
/// </summary>
internal sealed class HubRenderingTab : HubTab
{
	public override string TabId => "rendering";
	public override string Title => "Rendering";

	// Where a number starts to be worth a look. Rules of thumb carried over from the old frame stats overlay.
	const double DrawCallsWarn = 3000, DrawCallsBad = 5000;
	const double MatChangesPerDrawWarn = 0.5;
	const double TrisPerDrawWarn = 1000;
	const double BatchDensityWarn = 2;
	const double TexturePoolWarn = 85, TexturePoolBad = 95;
	const int StreamingWarn = 32;

	/// <summary>
	/// Batching ratios only mean something once there are enough draws for them to add up.
	/// </summary>
	const double BatchingMattersAbove = 300;

	const string Columns = "minmax(0, 1fr) 72px minmax(0, 1fr)";

	readonly HubFacts _context;
	readonly HubFindings _findings;
	readonly HubTable _submission;
	readonly HubTable _visibility;
	readonly HubTable _lights;
	readonly HubTable _textures;
	readonly HubTable _postProcess;
	readonly Panel _managedPane;
	readonly HubTable _managedPhases;
	readonly HubTable _managedDraws;
	readonly Panel _deviceLines;
	readonly List<PerfHub.Issue> _issues = new();
	string _deviceKey;

	public HubRenderingTab()
	{
		_context = AddChild( new HubFacts() );
		_findings = AddChild( new HubFindings() );

		var split = AddChild( new Panel( null, "split" ) );

		_submission = Pane( split, "render-submission" ).AddChild( new HubTable( Columns, "Submission", "#count", "" ).Banded() );
		_visibility = Pane( split ).AddChild( new HubTable( "minmax(0, 1fr) 64px 40px 64px", "Visibility", "#objects", "#%", "" ).Banded() );
		_lights = Pane( split ).AddChild( new HubTable( "minmax(0, 1fr) 64px", "Lights and shadows", "#count" ).Banded() );
		_textures = Pane( split ).AddChild( new HubTable( "minmax(0, 1fr) minmax(0, 130px) 64px", "Texture pool", "#", "" ).Banded() );

		_postProcess = Pane( split, "span-all" ).AddChild( new HubTable( "minmax(0, 1fr) minmax(0, 1fr) 48px minmax(0, 1fr)", "Post-processing", "stage", "#order", "flags" ).Banded() );

		_managedPane = Pane( split, "span-all" );
		var managed = _managedPane.AddChild( new Panel( null, "split" ) );
		_managedPhases = Pane( managed ).AddChild( new HubTable( "minmax(0, 1fr) 56px 64px", "Managed renderer", "#ms", "" ).Banded() );
		_managedDraws = Pane( managed ).AddChild( new HubTable( "minmax(0, 1fr) 64px 64px", "Draws by pass", "#draws", "" ).Banded() );
		_managedPane.AddChild( new HubSection( "Frame plan" ).Collapsible( body =>
			body.AddChild( new HubOverlayView( ( Painter p, ref Vector2 pos ) => DebugOverlay.ScenePlan.Draw( p, ref pos, 1 ) ) ) ) );

		var device = split.AddChild( new HubSection( "Device report", "span-all" ) );
		_deviceLines = new Panel( null, "device-lines" );
		device.Collapsible( body => body.AddChild( _deviceLines ) );
	}

	static Panel Pane( Panel split, string classes = null )
	{
		var pane = split.AddChild( new Panel( null, "section" ) );
		if ( classes is not null ) pane.AddClass( classes );
		return pane;
	}

	public override void Refresh( PerfHub.Summary s )
	{
		var f = FrameStats.Current;
		var m = f.Managed;
		var isManaged = m.Renders > 0;

		var drawCalls = isManaged ? m.TotalDraws : f.DrawCalls;
		var triangles = isManaged ? m.Triangles : f.TrianglesRendered;
		var trisPerDraw = Ratio( triangles, drawCalls );
		var batchingMatters = drawCalls > BatchingMattersAbove;

		RefreshContext( f, m, isManaged );
		RefreshFindings( f );
		RefreshSubmission( f, m, isManaged, drawCalls, triangles, trisPerDraw, batchingMatters );
		RefreshVisibility( f, m, isManaged );
		RefreshLights( f, m, isManaged );
		RefreshTextures( f );
		RefreshPostProcess();
		RefreshManaged( m, isManaged );
		RefreshDevice( f );
	}

	void RefreshContext( FrameStats f, ManagedFrameCounters m, bool isManaged )
	{
		var rs = RenderSettings.Instance;
		var aa = rs.AntiAliasQuality.ToString();
		aa = aa.StartsWith( "Multisample" ) ? $"{aa["Multisample".Length..]} MSAA" : aa == "None" ? "off" : aa;

		_context.Set( "Renderer", isManaged ? "managed" : "native", tooltip: isManaged
			? $"{m.Renders} camera {(m.Renders == 1 ? "frame" : "frames")} through Sandbox.SceneRenderer last frame"
			: ManagedSceneRendering.Enabled ? "r_managed_scene is on, but no camera rendered through it" : "The Source 2 scene system" );
		_context.Set( "Resolution", $"{Screen.Width:0}x{Screen.Height:0}" );
		_context.Set( "AA", aa );
		_context.Set( "Upscaling", rs.UpscalerMode.ToString().ToLowerInvariant() );
		_context.Set( "Scene views", $"{f.SceneViewsRendered:N0}" );
		_context.Set( "Resolves", $"{f.RenderTargetResolves:N0}" );
	}

	void RefreshFindings( FrameStats f )
	{
		FindIssues( f, _issues );
		_findings.Set( _issues );
	}

	/// <summary>
	/// What's out of line in last frame's rendering numbers, worst first.
	/// </summary>
	internal static void FindIssues( FrameStats f, List<PerfHub.Issue> into )
	{
		var isManaged = f.Managed.Renders > 0;
		var drawCalls = isManaged ? f.Managed.TotalDraws : f.DrawCalls;
		var trisPerDraw = Ratio( isManaged ? f.Managed.Triangles : f.TrianglesRendered, drawCalls );
		var batchingMatters = drawCalls > BatchingMattersAbove;

		into.Clear();

		if ( drawCalls >= DrawCallsWarn )
			into.Add( new( drawCalls >= DrawCallsBad ? PerfHub.Severity.Bad : PerfHub.Severity.Warning, "Draw calls",
				$"{drawCalls:N0}, over {DrawCallsWarn:N0}. Merge meshes, instance repeats, use LODs", null ) );

		if ( batchingMatters && trisPerDraw < TrisPerDrawWarn )
			into.Add( new( PerfHub.Severity.Warning, "Small draws",
				$"{trisPerDraw:N0} triangles per draw, under {TrisPerDrawWarn:N0}", null ) );

		if ( !isManaged )
		{
			var matPerDraw = Ratio( f.MaterialChanges + f.ShadowMaterialChanges, f.DrawCalls );
			if ( batchingMatters && matPerDraw > MatChangesPerDrawWarn )
				into.Add( new( PerfHub.Severity.Info, "Material changes",
					$"{matPerDraw:0.00} per draw, over {MatChangesPerDrawWarn}. Share materials so draws batch", null ) );
		}

		var pool = Percent( f.TexturePoolUsedBytes, f.TexturePoolLimitBytes );
		if ( pool >= TexturePoolWarn )
			into.Add( new( pool >= TexturePoolBad ? PerfHub.Severity.Bad : PerfHub.Severity.Warning, "Texture pool",
				$"{pool:0}% used, {HubFormat.BytesOf( f.TexturePoolUsedBytes, f.TexturePoolLimitBytes )}", null ) );

		if ( f.PendingStreamingRequests > StreamingWarn )
			into.Add( new( PerfHub.Severity.Info, "Streaming backlog", $"{f.PendingStreamingRequests:N0} pending", null ) );

		into.Sort( static ( a, b ) => b.Severity.CompareTo( a.Severity ) );
	}

	void RefreshSubmission( FrameStats f, ManagedFrameCounters m, bool isManaged, double drawCalls, double triangles, double trisPerDraw, bool batchingMatters )
	{
		var t = _submission;
		t.Begin();

		t.Row( "draws", ["Draw calls", N( $"{drawCalls:N0}", Grade( drawCalls, DrawCallsWarn, DrawCallsBad ) ),
			HubTable.Note( isManaged ? $"{m.Draws:N0} forward, {m.DepthDraws:N0} depth, {m.ShadowDraws:N0} shadow" : "" )] );
		t.Row( "tris", ["Triangles", N( HubFormat.Count( triangles ) ), HubTable.Note( $"{trisPerDraw:N0} per draw" )] );

		if ( isManaged )
		{
			t.Row( "instances", ["Instances", N( $"{m.Instances:N0}" ), HubTable.Note( $"{m.Objects:N0} objects, {m.ObjectsVisible:N0} visible" )] );
			t.End();
			return;
		}

		var batchDensity = Ratio( f.ObjectsRendered, f.RenderBatchDraws );
		t.Row( "batches", ["Batches", N( $"{f.RenderBatchDraws:N0}", batchingMatters && batchDensity < BatchDensityWarn ? PerfHub.Severity.Warning : null ),
			HubTable.Note( $"{batchDensity:N1} objects each" )] );

		var matChanges = f.MaterialChanges + f.ShadowMaterialChanges;
		var matPerDraw = Ratio( matChanges, f.DrawCalls );
		t.Row( "matchanges", ["Material changes", N( $"{matChanges:N0}", batchingMatters && matPerDraw > MatChangesPerDrawWarn ? PerfHub.Severity.Warning : null ),
			HubTable.Note( $"{matPerDraw:0.00} per draw, {f.ShadowMaterialChanges:N0} in shadows" )] );

		var sets = f.FullMaterialSets + f.SimilarMaterialSets;
		t.Row( "sets", ["Material sets", N( $"{sets:N0}" ), HubTable.Note( $"{f.SimilarMaterialSets:N0} similar ({Percent( f.SimilarMaterialSets, sets ):0}%)" )] );

		var prims = f.BaseObjectDraws + f.AnimatableObjectDraws + f.AggregateObjectDraws;
		t.Row( "aggregated", ["Aggregated", N( $"{f.AggregateObjectDraws:N0}" ), HubTable.Note( $"of {prims:N0} primitives ({Percent( f.AggregateObjectDraws, prims ):0}%)" )] );
		if ( f.UnbatchableMaterialDraws > 0 )
			t.Row( "unbatchable", ["Unbatchable", N( $"{f.UnbatchableMaterialDraws:N0}" ), HubTable.Note( "materials that can't batch" )] );

		t.Row( "cbuffers", ["Constant buffers", N( $"{f.ConstantBufferUpdates:N0}" ), HubTable.Note( $"{HubFormat.Bytes( f.ConstantBufferBytes )} uploaded" )] );
		t.Row( "contexts", ["Contexts", N( $"{f.PrimaryContexts + f.SecondaryContexts:N0}" ), HubTable.Note( $"{f.PrimaryContexts:N0} primary, {f.SecondaryContexts:N0} secondary" )] );
		t.Row( "displaylists", ["Display lists", N( $"{f.DisplayLists:N0}" ), HubTable.Note( "" )] );
		if ( f.UniqueMaterials > 0 )
			t.Row( "materials", ["Unique materials", N( $"{f.UniqueMaterials:N0}" ), HubTable.Note( "" )] );

		t.End();
	}

	void RefreshVisibility( FrameStats f, ManagedFrameCounters m, bool isManaged )
	{
		var t = _visibility;
		t.Begin();

		if ( isManaged )
		{
			var other = Math.Max( 0, m.Objects - m.ObjectsVisible - m.ObjectsSizeCulled );
			Part( t, "Drawn", m.ObjectsVisible, m.Objects, always: true );
			Part( t, "Too small", m.ObjectsSizeCulled, m.Objects );
			Part( t, "Unaccounted", other, m.Objects );
			t.End();
			return;
		}

		// The scene system counts what visibility, screen size and fading removed. The rest of what it tested and
		// didn't draw isn't attributed to anything, so it's shown as such rather than guessed at.
		var tested = f.ObjectsTested;
		var drawn = Math.Min( f.ObjectsRendered, tested );
		var counted = f.ObjectsCulledByVis + f.ObjectsCulledByScreenSize + f.ObjectsCulledByFade;
		var unaccounted = Math.Max( 0, tested - drawn - counted );

		Part( t, "Drawn", drawn, tested, always: true );
		Part( t, "Vis culled", f.ObjectsCulledByVis, tested );
		Part( t, "Too small", f.ObjectsCulledByScreenSize, tested );
		Part( t, "Faded out", f.ObjectsCulledByFade, tested );
		Part( t, "Unaccounted", unaccounted, tested, tooltip: "Tested and not drawn, without a reason the scene system counts" );
		t.Row( "tested", [new( "Tested", "dim" ), N( $"{tested:N0}" ), "", ""], rowClass: "total" );
		t.Row( "precull", [new( "Before culling", "dim" ), new( $"{f.ObjectsPreCull:N0}", "number dim" ), "", ""],
			tooltip: "Counted at an earlier stage - doesn't have to add up with the rest" );
		t.End();
	}

	static void Part( HubTable t, string name, double count, double total, bool always = false, string tooltip = null )
	{
		if ( count <= 0 && !always ) return;
		var share = Ratio( count, total );
		t.Row( name, [name, N( $"{count:N0}" ), new( $"{share * 100:0}", "number dim" ), HubTable.BarOf( share )], tooltip: tooltip );
	}

	void RefreshLights( FrameStats f, ManagedFrameCounters m, bool isManaged )
	{
		var t = _lights;
		t.Begin();
		t.Row( "shadowed", ["Shadowed lights", N( $"{f.ShadowedLightsInView:N0}" )] );
		t.Row( "unshadowed", ["Unshadowed lights", N( $"{f.UnshadowedLightsInView:N0}" )] );
		t.Row( "maps", ["Shadow maps", N( $"{f.ShadowMaps:N0}" )], tooltip: f.ShadowedLightsInView > 0 ? $"{Ratio( f.ShadowMaps, f.ShadowedLightsInView ):N1} per shadowed light" : null );

		if ( isManaged )
		{
			t.Row( "views", ["Shadow views", N( $"{m.ShadowViews:N0}" )] );
			t.Row( "shadowdraws", ["Shadow draws", N( $"{m.ShadowDraws:N0}" )] );
			t.Row( "binned", ["Lights binned", N( $"{m.Lights:N0}" )] );
		}
		else
		{
			t.Row( "shadowmats", ["Shadow material changes", N( $"{f.ShadowMaterialChanges:N0}" )], tooltip: $"{f.ShadowMaterialChangesAlphaTested:N0} alpha tested" );
		}

		t.End();
	}

	void RefreshTextures( FrameStats f )
	{
		var t = _textures;
		t.Begin();

		var limit = (double)f.TexturePoolLimitBytes;
		var used = (double)f.TexturePoolUsedBytes;
		var pool = Percent( used, limit );
		var severity = Grade( pool, TexturePoolWarn, TexturePoolBad );

		t.Row( "used", ["Used", N( limit > 0 ? HubFormat.BytesOf( used, limit ) : HubFormat.Bytes( used ), severity ), HubTable.BarOf( Ratio( used, limit ), severity )] );

		// What the used part is made of - more telling than how much of a big pool is free
		var pinned = (double)f.TexturePoolNonEvictableBytes;
		t.Row( "pinned", ["Pinned", N( HubFormat.Bytes( pinned ) ), HubTable.BarOf( Ratio( pinned, limit ) )], tooltip: "Can't be evicted to make room" );
		t.Row( "evictable", ["Evictable", N( HubFormat.Bytes( Math.Max( 0, used - pinned ) ) ), HubTable.BarOf( Ratio( Math.Max( 0, used - pinned ), limit ) )] );
		t.Row( "pending", ["Streaming", N( $"{f.PendingStreamingRequests:N0}", f.PendingStreamingRequests > StreamingWarn ? PerfHub.Severity.Warning : null ), ""] );

		if ( GpuProfilerStats.VideoMemoryBudget > 0 )
		{
			var vram = GpuProfilerStats.VideoMemoryUsageFraction;
			var vramSeverity = vram > 0.95f ? PerfHub.Severity.Bad : vram > 0.8f ? PerfHub.Severity.Warning : (PerfHub.Severity?)null;
			t.Row( "vram", ["Video memory", N( HubFormat.BytesOf( GpuProfilerStats.VideoMemoryUsed, GpuProfilerStats.VideoMemoryBudget ), vramSeverity ), HubTable.BarOf( vram, vramSeverity )], rowClass: "total" );
		}

		t.End();
	}

	void RefreshPostProcess()
	{
		var camera = Application.GetActiveScene()?.Camera;
		var layers = camera?.PostProcess.Layers;

		var t = _postProcess;
		t.Begin();

		if ( layers is not null )
		{
			foreach ( var (stage, list) in layers.OrderBy( x => x.Key ) )
			{
				foreach ( var layer in list.OrderBy( x => x.Order ) )
				{
					t.Row( $"{stage}/{layer.Name}/{layer.Order}", [layer.Name, new( Words( stage.ToString() ), "dim" ), N( $"{layer.Order}" ), new( string.Join( ", ", Flags( layer ) ), "dim" )] );
				}
			}
		}

		t.End( camera is null ? "No active camera"
			: !camera.EnablePostProcessing ? "Post-processing is off on this camera"
			: "No effects on this camera" );
	}

	static IEnumerable<string> Flags( PostProcessLayer layer )
	{
		if ( layer.RanAsync ) yield return "async";
		else if ( layer.AsyncCompute ) yield return "async capable";
		if ( layer.NeedsDepthNormals ) yield return "depth normals";
	}

	void RefreshManaged( ManagedFrameCounters m, bool isManaged )
	{
		_managedPane.SetClass( "hidden", !isManaged );
		if ( !isManaged ) return;

		// Worker recording and waiting overlap the main thread, so they're shown beside it rather than added in
		var total = m.MainThreadMs;
		var t = _managedPhases;
		t.Begin();
		Phase( t, "Collect", m.CollectMs, total );
		Phase( t, "Prepare", m.PrepareMs, total );
		Phase( t, "Record", m.RecordMs, total );
		Phase( t, "Sync", m.SyncMs, total );
		Phase( t, "Setup", m.SetupMs, total );
		Phase( t, "Submit", m.SubmitMs, total );
		t.Row( "total", ["Main thread", N( HubFormat.Ms( total ) ), ""], rowClass: "total" );
		t.Row( "workers", [new( "Workers recording", "dim" ), new( HubFormat.Ms( m.WorkerRecordMs ), "number dim" ), ""], tooltip: "Overlaps the main thread" );
		t.Row( "wait", [new( "Waiting on workers", "dim" ), new( HubFormat.Ms( m.RecordWaitMs ), "number dim" ), ""] );
		t.End();

		var d = _managedDraws;
		var opaque = Math.Max( 0, m.Draws - m.TranslucentDraws );
		d.Begin();
		Pass( d, "Opaque", opaque, m.TotalDraws );
		Pass( d, "Translucent", m.TranslucentDraws, m.TotalDraws );
		Pass( d, "Depth", m.DepthDraws, m.TotalDraws );
		Pass( d, "Shadow", m.ShadowDraws, m.TotalDraws );
		Pass( d, "Custom", m.CustomDraws, m.TotalDraws );
		d.End();
	}

	static void Phase( HubTable t, string name, double ms, double total ) =>
		t.Row( name, [name, N( HubFormat.Ms( ms ) ), HubTable.BarOf( Ratio( ms, total ) )] );

	static void Pass( HubTable t, string name, double draws, double total )
	{
		if ( draws <= 0 ) return;
		t.Row( name, [name, N( $"{draws:N0}" ), HubTable.BarOf( Ratio( draws, total ) )] );
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

	static HubTable.Cell N( string text, PerfHub.Severity? severity = null ) => HubTable.Number( text, severity );

	static PerfHub.Severity? Grade( double value, double warn, double bad ) =>
		value >= bad ? PerfHub.Severity.Bad : value >= warn ? PerfHub.Severity.Warning : null;

	static double Ratio( double a, double b ) => b > 0 ? a / b : 0;

	static double Percent( double a, double b ) => b > 0 ? a / b * 100 : 0;

	/// <summary>
	/// "BeforePostProcess" to "Before post process".
	/// </summary>
	static string Words( string name )
	{
		var words = new System.Text.StringBuilder( name.Length + 8 );
		for ( int i = 0; i < name.Length; i++ )
		{
			var c = name[i];
			if ( i > 0 && char.IsUpper( c ) && !char.IsUpper( name[i - 1] ) )
			{
				words.Append( ' ' );
				c = char.ToLowerInvariant( c );
			}

			words.Append( c );
		}

		return words.ToString();
	}
}
