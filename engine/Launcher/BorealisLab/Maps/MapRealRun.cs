using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Sdl;
using Borealis.Source2;
using Borealis.Source2.Shaders;
using System.Diagnostics;
using System.IO;
using System.Text;
using Capture = BorealisGpuSample.Capture;
using GpuDevice = Borealis.Gpu.Device;

namespace Sandbox.BorealisLab.Maps;

/// <summary>
/// <c>-map ident -real</c>: the map in a <see cref="MapWorld"/>, drawn by Borealis's <see cref="RenderSystem"/> with the
/// map's materials through <see cref="BackendDevice"/>, timed per run of frames: Borealis's collect, prepare, setup,
/// record and submit, the copy into the window, and GPU time from timestamps; then, on warm frames, the draws, binds,
/// object changes (the same material with another object's lightmap, probe or attributes), multi-draws and descriptor
/// sets written. Prints and saves a table, and what the materials' draws bind. <c>-nobaked</c> leaves the map's
/// lightmaps and probes out; <c>-occlusion</c> turns on the GPU occlusion cull; <c>-surface-convert blendable,...</c>
/// draws those shaders' materials with the Slang surfaces beside them (<c>shaders/blendable.slang</c>).
/// <c>-drawcensus &lt;dir&gt;</c> also counts one warm frame of each run's every draw (<see cref="DrawCensus"/>) into that folder.
/// </summary>
internal static class MapRealRun
{
	public sealed record Options( string Ident, MapView[] Views, int Width, int Height, int Samples, int Frames, int Warmup, string Captures, string Output, string Unoptimised );

	/// <summary>
	/// A way of drawing it: recording on one thread or several, material objects or binding by reflection, whether static
	/// geometry casts sun shadows, and whether aggregates draw GPU-driven (<c>-gpudriven</c>).
	/// </summary>
	sealed record Variant( string Name, bool Parallel, bool MaterialObjects, bool Shadows, bool GpuDriven = false, bool MaterialBuckets = false, bool CpuCulling = false, bool NoCompaction = false, bool FullVertices = false, bool Visibility = false, bool FreezeFar = false, bool? FarCache = null, bool NoTiles = false, bool NoFrameBufferMips = false, bool NoContactShadows = false );

	static readonly Variant[] Variants =
	[
		new( "real-serial", false, true, false ),
		new( "real-parallel", true, true, false ),
		new( "real-parallel-nofbmips", true, true, false, NoFrameBufferMips: true ),
		new( "real-parallel-nocontact", true, true, false, NoContactShadows: true ),
		new( "real-reflection", false, false, false ),
		new( "real-serial-shadows", false, true, true ),
		new( "real-parallel-shadows", true, true, true ),
		new( "real-serial-gpudriven", false, true, false, true ),
		new( "real-parallel-gpudriven", true, true, false, true ),
		new( "real-serial-gpudriven-materials", false, true, false, true, true ),
		new( "real-parallel-gpudriven-materials", true, true, false, true, true ),
		new( "real-serial-gpudriven-cpucull", false, true, false, true, false, true ),
		new( "real-parallel-gpudriven-cpucull", true, true, false, true, false, true ),
		new( "real-serial-gpudriven-nocompact", false, true, false, true, false, false, true ),
		new( "real-parallel-gpudriven-nocompact", true, true, false, true, false, false, true ),
		new( "real-serial-gpudriven-fullvertices", false, true, false, true, FullVertices: true ),
		new( "real-serial-gpudriven-visibility", false, true, false, true, Visibility: true ),
		new( "real-parallel-gpudriven-visibility", true, true, false, true, Visibility: true ),
		new( "real-serial-gpudriven-shadows", false, true, true, true, Visibility: true ),
		new( "real-parallel-gpudriven-shadows", true, true, true, true, Visibility: true ),
		new( "real-parallel-gpudriven-shadows-frozen", true, true, true, true, Visibility: true, FreezeFar: true ),
		new( "real-parallel-gpudriven-shadows-farcache", true, true, true, true, Visibility: true, FarCache: true ),
		new( "real-parallel-gpudriven-shadows-nofarcache", true, true, true, true, Visibility: true, FarCache: false ),
		new( "real-parallel-gpudriven-shadows-notiles", true, true, true, true, Visibility: true, NoTiles: true ),
		new( "real-parallel-gpudriven-visibility-notiles", true, true, false, true, Visibility: true, NoTiles: true ),
		new( "real-parallel-gpudriven-fullvertices", true, true, false, true, FullVertices: true ),
		new( "real-serial-fullvertices", false, true, false, FullVertices: true ),
		new( "real-parallel-fullvertices", true, true, false, FullVertices: true ),
	];

	struct Frame
	{
		public double Wait, Render, Copy, Collect, Prepare, Setup, Record, WorkerRecord, Prepass, Stages, Submit, Gpu;
	}

	public static int Run( GpuDevice gpu, SdlWindow window, SwapChain swapChain, string folder, ContentFiles core, Options options )
	{
		var only = Args.String( "-variants" )?.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );
		var gpuDriven = Args.Has( "-gpudriven" ) || (only?.Any( v => v.Contains( "-gpudriven", StringComparison.OrdinalIgnoreCase ) ) ?? false);
		// In the order -variants names them, so runs can alternate which goes first
		var variants = only is null ? Variants.Where( v => gpuDriven || !v.GpuDriven ).ToArray()
			: only.Select( name => Variants.FirstOrDefault( v => v.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) ) ).Where( v => v is not null ).ToArray();
		if ( variants.Length == 0 )
		{
			Console.Error.WriteLine( $"-variants names none of {string.Join( ", ", Variants.Select( v => v.Name ) )}" );
			return 2;
		}

		var load = Stopwatch.StartNew();
		using var map = new MapWorld( gpu, folder, core, castShadows: false, aggregatesAsMeshes: Args.Has( "-aggregates-as-meshes" ), bakedLighting: !Args.Has( "-nobaked" ), entities: !Args.Has( "-noentities" ), gpuDriven: gpuDriven );
		map.SetTarget( options.Width, options.Height, options.Samples );
		map.System.DepthPrepass = !Args.Has( "-noprepass" );
		// -lod-ranges: what generated LODs make of each material's ranges (LodRanges)
		if ( Args.Has( "-lod-ranges" ) && map.Backend.LodStore is { } labelled ) labelled.Labelled = true;

		// -capture-visibility <dir>: the visibility pass's figure instead of timing (debug: needs -msaa 1)
		var figureFolder = Args.String( "-capture-visibility" );
		using var figure = figureFolder is not null && options.Samples == 1 ? new VisibilityFigure( gpu, options.Width, options.Height ) : null;
		if ( figureFolder is not null && figure is null ) Console.Error.WriteLine( "-capture-visibility needs -msaa 1: the ids are read back as they are" );
		map.Backend.Figure = figure;

		if ( Args.Has( "-occlusion" ) && RenderHost.Current is BackendRenderHost host ) host.Occlusion = true;

		// -debug-views shadowmaps: the shadow maps as thumbnails along the bottom of the frame, framed green where drawn this frame
		if ( Args.String( "-debug-views" ) is "shadowmaps" && RenderHost.Current is BackendRenderHost debugHost ) debugHost.Views = DebugViews.ShadowMaps;

		// Sun cascades: -no-texel-cascades culls and picks LODs by the main view, as native; -freeze-far-cascade (or the
		// -frozen variant) draws the last cascade once and copies it in after, an experiment for a camera that doesn't move;
		// -far-cache / -no-far-cache, -no-cascade-tiles (or the -farcache, -nofarcache and -notiles variants);
		// -cascade-resolution N; -cascade-cull-texels N
		global::Borealis.Shadows.ShadowMapper.TexelSizedCascades = !Args.Has( "-no-texel-cascades" );
		var farCache = Args.Has( "-far-cache" ) || (global::Borealis.Shadows.ShadowMapper.FarCascadeCache && !Args.Has( "-no-far-cache" ));
		var cascadeTiles = global::Borealis.Shadows.ShadowMapper.CascadeTiles && !Args.Has( "-no-cascade-tiles" );
		// The map's sun's contact shadows, which the -nocontact variants turn off
		var sunContactShadows = map.World.Lighting.SunContactShadows;
		if ( Args.Int( "-cascade-resolution", 0 ) is > 0 and var cascadeResolution ) RenderHost.Current.Shadows.MaxCascadeResolution = cascadeResolution;
		if ( Args.Int( "-cascade-cull-texels", 0 ) is > 0 and var cullTexels ) global::Borealis.Culling.SizeCull.CascadeTexels = cullTexels;
		if ( !Args.Has( "-noprobes" ) ) map.CaptureProbes();
		foreach ( var (name, ms) in map.Phases ) Console.WriteLine( $"  {name}: {ms:0} ms" );
		Console.WriteLine( $"Loaded in {load.Elapsed.TotalSeconds:0.0} s; {map.Problems.Count} problems" );
		if ( map.Backend.Surfaces.ConvertedMaterials > 0 ) Console.WriteLine( $"  {map.Backend.Surfaces.ConvertedMaterials} materials drawn as surfaces" );
		if ( Args.Has( "-surface-convert-list" ) )
		{
			foreach ( var path in map.Backend.Surfaces.ConvertedPaths ) Console.WriteLine( $"  as a surface: {path}" );
		}

		// -material-values a,b: the values of the materials whose path contains one of these, as their .vmat sets them
		if ( Args.String( "-material-values" ) is { } valuesOf )
		{
			var parts = valuesOf.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );
			foreach ( var path in map.Backend.Surfaces.ConvertedPaths.Where( p => parts.Any( q => p.Contains( q, StringComparison.OrdinalIgnoreCase ) ) ) )
			{
				if ( map.Backend.Content.TryRead( path ) is not { } data ) continue;
				var file = global::Borealis.Source2.Materials.MaterialFile.Read( data );
				Console.WriteLine( $"  values of {path} ({file.Shader}): {string.Join( ", ", file.Ints.Select( v => $"{v.Key}={v.Value}" ) )}; {string.Join( ", ", file.Floats.Select( v => $"{v.Key}={v.Value}" ) )}; {string.Join( ", ", file.Vectors.Select( v => $"{v.Key}=[{string.Join( " ", v.Value )}]" ) )}; {string.Join( ", ", file.Textures.Keys )}" );
			}
		}
		foreach ( var problem in map.Problems.Take( 10 ) ) Console.WriteLine( $"  {problem}" );

		using var timestamps = gpu.CreateTimestamps( 2 * 4 );
		var period = gpu.Capabilities.TimestampPeriod / 1e6;
		var ticks = new ulong[2];
		long frameNumber = 0;

		// -passes: GPU time per layer and list from timestamps around each; -pass-stats: and what each assembles and shades
		// (which closes rendering at every layer, so the times aren't the frame's)
		using var passTimer = Args.Has( "-passes" ) || Args.Has( "-pass-stats" ) ? new PassTimer( gpu, statistics: Args.Has( "-pass-stats" ) ) : null;
		map.Backend.PassTimer = passTimer;
		map.Backend.BatchSubmits = !Args.Has( "-no-batch" );

		// -full-barriers: every barrier waits for all GPU work, as before exact stages; -carry-scopes: worker lists splitting
		// one view's layer keep one rendering scope (BackendDevice.CarryScopes)
		map.Backend.ExactBarriers = !Args.Has( "-full-barriers" );
		map.Backend.CarryScopes = Args.Has( "-carry-scopes" );
		map.Backend.LayoutPlans = !Args.Has( "-no-layout-plans" );

		// -fixed-time S: shader time S seconds every frame, so captures from separate runs can be compared pixel for pixel
		if ( Args.String( "-fixed-time" ) is { } fixedTime && float.TryParse( fixedTime, System.Globalization.CultureInfo.InvariantCulture, out var seconds ) ) map.Time = seconds;
		map.Backend.Shaders.ShareVariants = !Args.Has( "-no-share-variants" );
		var passes = new PassTotals();
		var timings = new List<PassTimer.Timing>();

		// -orbit: the timed frames circle the view once around the point it looks at, so the camera moves every frame
		var orbit = Args.Has( "-orbit" );

		// -drawcensus <dir>: one warm frame of each run with every draw counted
		var censusFolder = Args.String( "-drawcensus" );
		var censusReport = new StringBuilder();

		// The window's title shows the frames' stats live, as the game window's does on Borealis.Gpu
		var title = new BorealisGpuTitle();
		var titleName = $"Borealis Lab: {options.Ident}";
		Action<string> setTitle = text => window.Title = text;

		Frame RenderFrame( MapView view )
		{
			using var frameZone = LabTracy.Frame();
			var f = default( Frame );
			var t0 = Stopwatch.GetTimestamp();
			map.Backend.BeginFrame();
			var image = default( TextureHandle );
			if ( !swapChain.Acquire( out image ) ) image = default;
			var t1 = Stopwatch.GetTimestamp();

			// GPU time: from the start of Borealis's frame list to after its last list, three frames back
			var slot = (int)(frameNumber % 4);
			map.Backend.FrameStartTimestamp = (timestamps, slot * 2);
			passTimer?.BeginFrame( frameNumber );

			map.Render( view );
			var t2 = Stopwatch.GetTimestamp();

			var copy = gpu.BeginCommandList( QueueType.Graphics, "Map to window" );
			var copyScope = passTimer?.Begin( copy, "Copy to window", PassTimer.ScopeKind.Own ) ?? -1;
			if ( image.IsValid ) map.CopyTo( copy, image );
			passTimer?.End( copy, copyScope );
			copy.WriteTimestamp( timestamps, slot * 2 + 1 );
			gpu.Submit( copy, swapChain: image.IsValid ? swapChain : null );
			if ( image.IsValid ) swapChain.Present();
			var t3 = Stopwatch.GetTimestamp();

			f.Gpu = double.NaN;
			var old = (int)((frameNumber + 1) % 4);
			if ( frameNumber >= 4 && timestamps.TryRead( old * 2, ticks ) ) f.Gpu = (ticks[1] - ticks[0]) * period;
			if ( passTimer is not null && frameNumber >= 3 && passTimer.TryRead( frameNumber - 3, timings ) ) passes.Add( timings );
			frameNumber++;

			var s = map.System.Stats;
			f.Wait = Ms( t0, t1 );
			f.Render = Ms( t1, t2 );
			f.Copy = Ms( t2, t3 );
			(f.Collect, f.Prepare, f.Setup, f.Record, f.WorkerRecord, f.Prepass, f.Stages, f.Submit) = (s.CollectMs, s.PrepareMs, s.SetupMs, s.RecordMs, s.WorkerRecordMs, s.PrepassRecordMs, s.StagesMs, s.SubmitMs);
			title.Frame( titleName, Sandbox.Rendering.ManagedFrameCounters.From( s ), f.Gpu, map.Backend, view.Position, setTitle );
			return f;
		}

		// The first frames after loading, each with the pipelines it made and the time that took: start-up hitches
		var (loadPipelines, loadPipelineMs) = gpu.PipelineCreation;
		Console.WriteLine( $"Pipeline cache: {(gpu.PipelineCachePath is null ? "off" : gpu.PipelineCacheLoaded ? $"loaded from {gpu.PipelineCachePath}" : "empty")}; {loadPipelines} pipelines made while loading, {loadPipelineMs:0.0} ms" );
		var firstFrames = new List<string>();
		for ( int i = 0; i < 10 && figure is null; i++ )
		{
			var (made, ms) = gpu.PipelineCreation;
			var started = Stopwatch.GetTimestamp();
			RenderFrame( options.Views[0] );
			var took = Ms( started, Stopwatch.GetTimestamp() );
			var (madeAfter, msAfter) = gpu.PipelineCreation;
			firstFrames.Add( FormattableString.Invariant( $"{took:0.0} ms ({madeAfter - made} pipelines, {msAfter - ms:0.0} ms)" ) );
		}

		if ( firstFrames.Count > 0 ) Console.WriteLine( $"First frames: {string.Join( ", ", firstFrames )}" );
		if ( gpu.Capabilities.PipelineLibraries ) Console.WriteLine( $"Pipeline libraries after the first frames: {gpu.PipelineLibraryCreation}" );
		Console.WriteLine( map.Backend.PipelineManifestReport );

		// Meshlets and generated LODs build on workers after load (and levels' meshlets after their levels), asked for as draws
		// find their GPU-driven variants, which package shaders compile on workers: the timed frames start once none has been
		// pending or asked for in two seconds, or after three minutes
		var building = Stopwatch.StartNew();
		var quiet = Stopwatch.StartNew();
		var asked = map.Backend.BuildsRequested;
		while ( quiet.Elapsed.TotalSeconds < 2 && building.Elapsed.TotalSeconds < 180 && figure is null && !window.CloseRequested )
		{
			MapRun.Pump( window );
			RenderFrame( options.Views[0] );
			// Optimised pipeline links replacing fast ones are background work too
			if ( map.Backend.BuildsPending > 0 || map.Backend.BuildsRequested != asked || gpu.PipelineLibraryCreation.Pending > 0 || gpu.DeferredPipelines.Pending > 0 ) quiet.Restart();
			asked = map.Backend.BuildsRequested;
		}

		Console.WriteLine( FormattableString.Invariant( $"Built in {building.Elapsed.TotalSeconds:0.0} s after the first frames" ) );
		Console.WriteLine( map.Backend.MeshletReport );
		Console.WriteLine( map.Backend.LodReport );
		if ( Args.Has( "-lod-ranges" ) && map.Backend.LodStore is { } lodStore ) LodRanges.Write( lodStore, map.Backend.LodPixels, options.Width, options.Views, options.Output );

		if ( figure is not null )
		{
			VisibilityFigureCapture.Save( gpu, map, figure, options.Views[0], view => RenderFrame( view ), figureFolder );
			return 0;
		}

		var lines = new List<string>();
		var csv = new StringBuilder( "view,variant,parallel,material_objects,shadows,frames,wait_ms,render_ms,collect_ms,prepare_ms,setup_ms,record_ms,worker_record_ms,stages_ms,submit_ms,copy_ms,cpu_ms,frame_ms,frame_p95_ms,gpu_ms,draws,depth_draws,shadow_draws,translucent_draws,instances,objects_visible,triangles,model_draws,binds,fragment_draws,ns_per_bind,ns_per_repeat,sets_written_per_frame,gcs,object_changes,ns_per_object_change,failed_draws,cpu_mean_ms,frame_mean_ms,gpu_mean_ms,prepass_record_ms,gpudriven,gpudriven_batches,gpudriven_buckets,gpudriven_multidraws,lists,scopes,resumed_scopes,barriers,full_barriers,layout_fixup_lists,scope_pairings\n" );
		var bindingReport = "";
		var passTables = new StringBuilder();
		var passCsv = new StringBuilder( "view,variant,scope,kind,ms,per_frame,primitives,vertex_invocations,fragment_invocations,draws,pipelines,sets,rendering_scopes,barriers\n" );

		foreach ( var view in options.Views )
		{
			foreach ( var variant in variants )
			{
				map.System.ParallelRecording = variant.Parallel;
				map.Backend.MaterialObjects = variant.MaterialObjects;
				map.Backend.GpuDriven = variant.GpuDriven;
				map.Backend.GpuDrivenMaterialBuckets = variant.MaterialBuckets;
				map.Backend.GpuDrivenCulling = !variant.CpuCulling;
				map.Backend.GpuDrivenCompaction = !variant.NoCompaction;
				map.Backend.PositionStreams = !variant.FullVertices;
				map.Backend.GpuDrivenVisibility = variant.Visibility;
				map.Backend.FrameBufferCopyMips = !variant.NoFrameBufferMips;
				map.World.Lighting.SunContactShadows = sunContactShadows && !variant.NoContactShadows;
				map.SetCastShadows( variant.Shadows );
				global::Borealis.Shadows.ShadowMapper.FreezeFarCascade = variant.FreezeFar || Args.Has( "-freeze-far-cascade" );
				global::Borealis.Shadows.ShadowMapper.ForgetFrozenFar();
				global::Borealis.Shadows.ShadowMapper.FarCascadeCache = variant.FarCache ?? farCache;
				global::Borealis.Shadows.ShadowMapper.CascadeTiles = cascadeTiles && !variant.NoTiles;
				var farDraws = global::Borealis.Shadows.ShadowMapper.FarCaches.Sum( f => f.Draws );

				var warm = Stopwatch.StartNew();
				for ( int i = 0; (i < options.Warmup || warm.Elapsed.TotalSeconds < 2) && !window.CloseRequested; i++ )
				{
					MapRun.Pump( window );
					RenderFrame( view );
				}

				var frames = new List<Frame>( options.Frames );
				var frameTimes = new List<double>( options.Frames );
				var sets = map.Backend.MaterialObjectStats.SetsWritten;
				var gcs = GC.CollectionCount( 0 );
				passes.Clear();
				var recordedBefore = gpu.Recorded;
				var (fixUpsBefore, pairingsBefore) = (BackendContext.LayoutFixUps, gpu.ScopePairings);
				var last = Stopwatch.GetTimestamp();
				for ( int i = 0; i < options.Frames && !window.CloseRequested; i++ )
				{
					MapRun.Pump( window );
					frames.Add( RenderFrame( orbit ? Orbit( view, i / (float)options.Frames ) : view ) );
					var now = Stopwatch.GetTimestamp();
					frameTimes.Add( Stopwatch.GetElapsedTime( last, now ).TotalMilliseconds );
					last = now;
				}

				// What the timed frames recorded, per frame: lists, rendering scopes (and of them resumed), barrier calls (and of them
				// ones waiting for every stage), layout fix-up lists and lists pairing suspended scopes
				var recorded = gpu.Recorded;
				double PerFrame( long count ) => count / (double)Math.Max( frames.Count, 1 );
				var counts = (Lists: PerFrame( recorded.Lists - recordedBefore.Lists ), Scopes: PerFrame( recorded.Scopes - recordedBefore.Scopes ), Resumed: PerFrame( recorded.Resumed - recordedBefore.Resumed ),
					Barriers: PerFrame( recorded.Barriers - recordedBefore.Barriers ), Full: PerFrame( recorded.FullBarriers - recordedBefore.FullBarriers ),
					FixUps: PerFrame( BackendContext.LayoutFixUps - fixUpsBefore ), Pairings: PerFrame( gpu.ScopePairings - pairingsBefore ));
				var countsLine = FormattableString.Invariant( $"{counts.Lists:0.0} lists, {counts.Scopes:0.0} rendering scopes ({counts.Resumed:0.0} resumed), {counts.Barriers:0.0} barriers ({counts.Full:0.0} on every stage), {counts.FixUps:0.0} layout fix-up lists, {counts.Pairings:0.0} scope pairings a frame" );
				Console.WriteLine( $"{view.Name} {variant.Name}: {countsLine}" );

				if ( global::Borealis.Shadows.ShadowMapper.FarCascadeCache )
					Console.WriteLine( $"{view.Name} {variant.Name}: far cascade cache drawn {(global::Borealis.Shadows.ShadowMapper.FarCaches.Sum( f => f.Draws ) - farDraws)} times over the warm-up and timed frames" );

				if ( passTimer is not null )
				{
					var table = passes.Describe( $"{view.Name} {variant.Name}", options.Width * options.Height, passTimer.Statistics );
					Console.WriteLine( table );
					passTables.AppendLine( table );
					passes.AppendCsv( passCsv, view.Name, variant.Name );
				}

				if ( censusFolder is not null ) censusReport.AppendLine( Census( gpu, map, $"{options.Ident.Split( '.' ).Last()}-{view.Name}-{variant.Name}", censusFolder, () => RenderFrame( view ) ) );

				gcs = GC.CollectionCount( 0 ) - gcs;
				var setsPerFrame = (map.Backend.MaterialObjectStats.SetsWritten - sets) / (double)Math.Max( frames.Count, 1 );
				var stats = map.System.Stats;

				// Counted on warm frames after the timed ones, with the draw timer on (it costs a little)
				map.Backend.TimeDraws = true;
				map.Backend.DrawTimes.Reset();
				var (planHits, planBuilds) = (map.Backend.GpuDrivenPlanHits, map.Backend.GpuDrivenPlanBuilds);
				var (cullCalls, cullDispatches, cullViews, cullEntries) = (map.Backend.ResidentCullCalls, map.Backend.ResidentCullDispatches, map.Backend.ResidentCullViews, map.Backend.ResidentCullEntries);
				var cullFromIds = map.Backend.ResidentCullListsFromIds;
				const int Counted = 10;
				for ( int i = 0; i < Counted; i++ ) RenderFrame( view );
				(planHits, planBuilds) = (map.Backend.GpuDrivenPlanHits - planHits, map.Backend.GpuDrivenPlanBuilds - planBuilds);
				(cullCalls, cullDispatches, cullViews, cullEntries) = (map.Backend.ResidentCullCalls - cullCalls, map.Backend.ResidentCullDispatches - cullDispatches, map.Backend.ResidentCullViews - cullViews, map.Backend.ResidentCullEntries - cullEntries);
				cullFromIds = map.Backend.ResidentCullListsFromIds - cullFromIds;
				var residentList = map.System.GetFeature<global::Borealis.Features.AggregateRenderFeature>().ResidentFor( map.System.MainPass );
				var residentBatches = residentList?.Batches.Count ?? -1;
				var residentMeshes = residentList?.MeshGroups.Sum( g => g.Owner.Count ) ?? 0;
				var residentGroups = residentList?.MeshGroups.Count ?? 0;
				map.Backend.TimeDraws = false;
				var t = map.Backend.DrawTimes;
				var (modelDraws, binds, fragmentDraws) = (t.Draws / Counted, t.Binds / Counted, t.IndirectDraws / Counted);
				var (nsBind, nsRepeat) = (t.NanosecondsPerBind, t.NanosecondsPerRepeat);
				var (objectChanges, nsObjectChange) = (t.ObjectChanges / Counted, t.NanosecondsPerObjectChange);
				var gpuDrivenLine = variant.GpuDriven ? $"GPU-driven: {t.GpuDrivenBatches / Counted} aggregate batches in {t.GpuDrivenBuckets / Counted} buckets, {t.GpuDrivenMultiDraws / Counted} multi-draws of {t.GpuDrivenFragments / Counted} fragments, {planHits / (double)Counted:0.#} resident calls from plans and {planBuilds / (double)Counted:0.#} plans made a frame, {cullCalls / (double)Counted:0.#} calls from the resident cull ({cullDispatches / (double)Counted:0.#} dispatches culling {cullViews / (double)Counted:0.#} view plans of {cullEntries / Counted} entries a frame, {cullFromIds / (double)Counted:0.#} forward lists from their prepass's ids), {residentBatches} resident batches and {residentMeshes} resident meshes in {residentGroups} groups in the main view; " : "";
				string failed;
				int failedDraws;
				lock ( t.Failed )
				{
					failedDraws = t.Failed.Values.Sum() / Counted;
					failed = t.Failed.Count == 0 ? "none" : $"{failedDraws} a frame: {string.Join( ", ", t.Failed.OrderByDescending( x => x.Value ).Take( 8 ).Select( x => $"{x.Key} {x.Value / Counted}" ) )}";
				}

				double M( Func<Frame, double> f ) => Percentile( frames.Select( f ).Where( x => !double.IsNaN( x ) ), 0.5 );

				// Means too, as SceneLab's benchmark reports them
				double Mean( Func<Frame, double> f ) => frames.Select( f ).Where( x => !double.IsNaN( x ) ).DefaultIfEmpty( double.NaN ).Average();
				var cpu = M( f => f.Render + f.Copy );
				var line = $"{view.Name,-9} {variant.Name,-22} collect {M( f => f.Collect ),6:0.000} prepare {M( f => f.Prepare ),6:0.000} setup {M( f => f.Setup ),6:0.000} record {M( f => f.Record ),6:0.000} (workers {M( f => f.WorkerRecord ):0.000}, prepass {M( f => f.Prepass ):0.000}) submit {M( f => f.Submit ),6:0.000} render {M( f => f.Render ),6:0.000} copy {M( f => f.Copy ),5:0.000} wait {M( f => f.Wait ),6:0.000} frame {Percentile( frameTimes, 0.5 ),6:0.000} gpu {M( f => f.Gpu ),6:0.000}  "
					+ $"{stats.Draws}+{stats.DepthDraws} depth+{stats.ShadowDraws} shadow draws ({stats.TranslucentDraws} translucent, {stats.Instances} instances), {stats.ObjectsVisible} objects visible, {stats.Triangles / 1e6:0.00}M tris; {modelDraws} model draws, {binds} binds, {objectChanges} object changes, {fragmentDraws} fragment draws ({nsBind:0} ns a bind, {nsObjectChange:0} ns an object change, {nsRepeat:0} ns a repeat draw, timed), {setsPerFrame:0.0} sets written/frame, {gcs} GCs; {countsLine}; {gpuDrivenLine}{(map.System.SkyboxStats is { } sky ? $"3D skybox {sky.Draws}+{sky.DepthDraws} depth draws of {sky.ObjectsVisible} visible objects of {sky.Objects} ({sky.ObjectsFrustumCulled} frustum culled, {sky.ObjectsSizeCulled} size culled, {sky.ObjectsTagCulled} tag culled, {sky.ObjectsOcclusionCulled} occlusion culled)" : "no 3D skybox drawn")}; draws that recorded nothing: {failed}";
				Console.WriteLine( line );
				Console.WriteLine( map.Backend.MeshletReport );
				Console.WriteLine( map.Backend.LodReport );
				lines.Add( line );

				// -lod-shimmer: generated levels' changes with the camera still, moving slowly and swaying (LodShimmer)
				if ( Args.Has( "-lod-shimmer" ) && variant.GpuDriven )
				{
					var shimmer = LodShimmer.Run( map.Backend, view, Args.Int( "-lod-shimmer-frames", 300 ), v => RenderFrame( v ) );
					Console.WriteLine( shimmer );
					File.AppendAllText( Path.Combine( options.Output, "lod-shimmer.md" ), $"## {view.Name} {variant.Name}\n\n{shimmer}\n\n" );
				}

				csv.AppendLine( FormattableString.Invariant( $"{view.Name},{variant.Name},{variant.Parallel},{variant.MaterialObjects},{variant.Shadows},{frames.Count},{M( f => f.Wait ):0.0000},{M( f => f.Render ):0.0000},{M( f => f.Collect ):0.0000},{M( f => f.Prepare ):0.0000},{M( f => f.Setup ):0.0000},{M( f => f.Record ):0.0000},{M( f => f.WorkerRecord ):0.0000},{M( f => f.Stages ):0.0000},{M( f => f.Submit ):0.0000},{M( f => f.Copy ):0.0000},{cpu:0.0000},{Percentile( frameTimes, 0.5 ):0.0000},{Percentile( frameTimes, 0.95 ):0.0000},{M( f => f.Gpu ):0.0000},{stats.Draws},{stats.DepthDraws},{stats.ShadowDraws},{stats.TranslucentDraws},{stats.Instances},{stats.ObjectsVisible},{stats.Triangles},{modelDraws},{binds},{fragmentDraws},{nsBind:0},{nsRepeat:0},{setsPerFrame:0.0},{gcs},{objectChanges},{nsObjectChange:0},{failedDraws},{Mean( f => f.Render + f.Copy ):0.0000},{frameTimes.DefaultIfEmpty( double.NaN ).Average():0.0000},{Mean( f => f.Gpu ):0.0000},{M( f => f.Prepass ):0.0000},{(variant.GpuDriven ? (variant.MaterialBuckets ? "materials" : "onebucket") : "classic")},{t.GpuDrivenBatches / Counted},{t.GpuDrivenBuckets / Counted},{t.GpuDrivenMultiDraws / Counted},{counts.Lists:0.00},{counts.Scopes:0.00},{counts.Resumed:0.00},{counts.Barriers:0.00},{counts.Full:0.00},{counts.FixUps:0.00},{counts.Pairings:0.00}" ) );
			}

			if ( bindingReport.Length == 0 ) bindingReport = DescribeBindings( map.Backend );

			// -meshlet-census folder: what culling meshlets would take out of the view's prepass (MeshletCensus)
			if ( Args.String( "-meshlet-census" ) is { } meshletCensus )
				MeshletCensus.Run( map, view, options.Width, options.Height, options.Samples, Path.Combine( Path.GetFullPath( meshletCensus ), view.Name ), () => RenderFrame( view ) );

			if ( options.Captures is not null )
			{
				// -capture-parallel: captured recording on workers, as the threaded runs are
				map.System.ParallelRecording = Args.Has( "-capture-parallel" );
				map.Backend.MaterialObjects = true;
				map.Backend.FrameBufferCopyMips = true;
				map.World.Lighting.SunContactShadows = sunContactShadows;
				// -capture-shadows: with static geometry casting sun shadows, so GPU-driven shadow views are compared too
				map.SetCastShadows( Args.Has( "-capture-shadows" ) );
				global::Borealis.Shadows.ShadowMapper.FarCascadeCache = farCache;
				global::Borealis.Shadows.ShadowMapper.CascadeTiles = cascadeTiles;
				global::Borealis.Shadows.ShadowMapper.FreezeFarCascade = false;
				var path = Path.Combine( Path.GetFullPath( options.Captures ), $"{options.Ident.Split( '.' ).Last()}-{view.Name}-real.png" );

				// -capture-page: the far cascade's cache at work, for the status page: kept (casters settled into it), a strip drawn
				// as the camera crosses a grid line, and all of it drawn again as the sun turns
				if ( Args.Has( "-capture-page" ) )
				{
					map.Backend.GpuDriven = true;
					map.Backend.GpuDrivenVisibility = true;
					var far = global::Borealis.Shadows.ShadowMapper.FarCaches;
					void Save( string name )
					{
						gpu.WaitIdle();
						var (texture, layout) = map.Output;
						var file = Path.Combine( Path.GetFullPath( options.Captures ), name + ".png" );
						Capture.Save( gpu, texture, layout, file );
						BackendDevice.SetLayout( map.Target.Samples > 1 ? map.Target.Resolved : map.Target.Color, ImageLayout.CopySource );
						var report = map.System.DebugReport;
						Console.WriteLine( $"Saved {file}; far cache drawn whole {far.Sum( f => f.Draws )} times, {far.Sum( f => f.CellsDrawn )} cells in parts; debug views {RenderHost.Current.DebugViews}, thumbnails {string.Join( ", ", report.ShadowMaps.Select( m => $"{m.Kind} {m.Index} {(m.Rendered ? "drawn" : "kept")} at {m.Thumbnail}" ) )}" );
					}

					// Long enough for the casters to settle and the cache to take them, then a frame that only copies it
					var settle = Stopwatch.StartNew();
					while ( settle.Elapsed.TotalSeconds < global::Borealis.Shadows.SettledCasters.Seconds * 3 ) RenderFrame( view );
					for ( int i = 0; i < 10; i++ )
					{
						var (draws, drawnCells) = (far.Sum( f => f.Draws ), far.Sum( f => f.CellsDrawn ));
						RenderFrame( view );
						if ( far.Sum( f => f.Draws ) == draws && far.Sum( f => f.CellsDrawn ) == drawnCells ) break;
					}

					Save( "a-kept" );

					// Walk forward until a frame draws cells, and keep that frame
					var look = view.LookAt - view.Position;
					var step = System.Numerics.Vector3.Normalize( new System.Numerics.Vector3( look.X, look.Y, 0 ) ) * 20;
					var walked = view;
					var cells = far.Sum( f => f.CellsDrawn );
					for ( int i = 0; i < 1000 && far.Sum( f => f.CellsDrawn ) == cells; i++ )
					{
						walked = walked with { Position = walked.Position + step, LookAt = walked.LookAt + step };
						RenderFrame( walked );
					}

					Save( "b-strip" );

					// The sun a degree round: the whole cache again
					var lighting = map.World.Lighting;
					lighting.SunDirection = Rotation.FromYaw( 1 ) * lighting.SunDirection;
					RenderFrame( walked );
					Save( "c-sun-turned" );
					continue;
				}

				// -hiz-sweep N: from the view, N frames turning 15 degrees a frame, then N frames stepping 150 units a frame to its
				// right, one frame each, without, with and again without two-phase Hi-Z occlusion: each frame is saved, to compare
				// (what a turn or a step shows has to draw that same frame; the two runs without show what moves anyway)
				if ( Args.Int( "-hiz-sweep", 0 ) is > 0 and var turns )
				{
					map.Backend.GpuDriven = true;
					map.Backend.GpuDrivenVisibility = true;
					var look = view.LookAt - view.Position;
					var right = System.Numerics.Vector3.Normalize( System.Numerics.Vector3.Cross( look, System.Numerics.Vector3.UnitZ ) ) * 150;
					foreach ( var (hiz, suffix) in new[] { (false, "off"), (true, "on"), (false, "off2") } )
					{
						map.Backend.HiZOcclusion = hiz;
						for ( int i = 0; i < 5; i++ ) RenderFrame( view );
						for ( int t = 1; t <= turns * 2; t++ )
						{
							var turned = t <= turns
								? view with { LookAt = view.Position + System.Numerics.Vector3.Transform( look, System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitZ, t * MathF.PI / 12 ) ) }
								: view with { Position = view.Position + right * (t - turns), LookAt = view.LookAt + right * (t - turns) };
							RenderFrame( turned );
							gpu.WaitIdle();
							var (swept, sweptLayout) = map.Output;
							Capture.Save( gpu, swept, sweptLayout, Path.ChangeExtension( path, null ) + $"-hizsweep{t:00}-{suffix}.png" );
							BackendDevice.SetLayout( map.Target.Samples > 1 ? map.Target.Resolved : map.Target.Color, ImageLayout.CopySource );
						}

						var (candidates, occluded) = map.Backend.HiZCounts();
						Console.WriteLine( $"Hi-Z sweep {suffix}: {turns * 2} frames saved; last frame's candidates {candidates}, occluded {occluded}" );
					}

					map.Backend.HiZOcclusion = false;
					continue;
				}

				// -capture-walk N: walk the camera N steps of a sun-cascade cell and a half along its view, 30 frames a step, and at
				// each step capture the frame as the far cascade's cache has it (strips drawn as it went) and drawn whole again
				if ( Args.Int( "-capture-walk", 0 ) is > 0 and var steps )
				{
					map.Backend.GpuDriven = true;
					map.Backend.GpuDrivenVisibility = true;
					var look = view.LookAt - view.Position;
					var forward = System.Numerics.Vector3.Normalize( new System.Numerics.Vector3( look.X, look.Y, 0 ) ) * 3800;
					var start = view;
					for ( int step = 0; step <= steps; step++ )
					{
						for ( int i = 0; i < 30; i++ )
						{
							var at = start.Position + forward * (step - 1 + (i + 1) / 30.0f);
							RenderFrame( step == 0 ? start : start with { Position = at, LookAt = at + (start.LookAt - start.Position) } );
						}

						var current = step == 0 ? start : start with { Position = start.Position + forward * step, LookAt = start.LookAt + forward * step };
						foreach ( var (whole, suffix) in new[] { (false, "cache"), (true, "whole") } )
						{
							global::Borealis.Shadows.ShadowMapper.FarCacheRedrawWhole = whole;
							for ( int i = 0; i < 3; i++ ) RenderFrame( current );
							gpu.WaitIdle();
							var (walked, walkedLayout) = map.Output;
							var walkPath = Path.ChangeExtension( path, null ) + $"-walk{step}-{suffix}.png";
							Capture.Save( gpu, walked, walkedLayout, walkPath );
							BackendDevice.SetLayout( map.Target.Samples > 1 ? map.Target.Resolved : map.Target.Color, ImageLayout.CopySource );
							var farCaches = global::Borealis.Shadows.ShadowMapper.FarCaches;
							Console.WriteLine( $"Saved {walkPath}; far cascade cache drawn whole {farCaches.Sum( f => f.Draws )} times, cells {farCaches.Sum( f => f.CellsDrawn )}" );
						}

						global::Borealis.Shadows.ShadowMapper.FarCacheRedrawWhole = false;
					}

					continue;
				}

				// The GPU-driven frames between two classic ones, to compare (wind and water move between captures)
				foreach ( var (drivenCapture, materialBuckets, visibility, suffix) in gpuDriven ? new[] { (false, false, false, "-classic-before"), (true, false, false, "-gpudriven"), (true, true, false, "-gpudriven-materials"), (true, false, true, "-gpudriven-visibility"), (false, false, false, "") } : [(false, false, false, "")] )
				{
					map.Backend.GpuDriven = drivenCapture;
					map.Backend.GpuDrivenMaterialBuckets = materialBuckets;
					map.Backend.GpuDrivenCulling = true;
					map.Backend.GpuDrivenCompaction = true;
					map.Backend.PositionStreams = true;
					map.Backend.GpuDrivenVisibility = visibility;
					for ( int i = 0; i < 3; i++ ) RenderFrame( view );
					gpu.WaitIdle();
					var (captured, capturedLayout) = map.Output;
					var capturePath = Path.ChangeExtension( path, null ) + suffix + ".png";
					Capture.Save( gpu, captured, capturedLayout, capturePath );
					BackendDevice.SetLayout( map.Target.Samples > 1 ? map.Target.Resolved : map.Target.Color, ImageLayout.CopySource );
					Console.WriteLine( $"Saved {capturePath}" );
				}

				// -capture-times from,to,step: the frame at each shader time in the range, to find the time another capture was taken at
				if ( Args.String( "-capture-times" )?.Split( ',' ).Select( v => float.Parse( v, System.Globalization.CultureInfo.InvariantCulture ) ).ToArray() is [var from, var to, var by] && by > 0 )
				{
					var kept = map.Time;
					for ( var at = from; at <= to + by * 0.5f; at += by )
					{
						map.Time = at;
						for ( int i = 0; i < 3; i++ ) RenderFrame( view );
						gpu.WaitIdle();
						var (timed, timedLayout) = map.Output;
						Capture.Save( gpu, timed, timedLayout, Path.ChangeExtension( path, null ) + FormattableString.Invariant( $"-t{at:0.00}.png" ) );
						BackendDevice.SetLayout( map.Target.Samples > 1 ? map.Target.Resolved : map.Target.Color, ImageLayout.CopySource );
					}

					map.Time = kept;
				}

				var (texture, layout) = map.Output;

				// -vis 10,2: the same frame in the shaders' debug views
				foreach ( var mode in (Args.String( "-vis" ) ?? "").Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ) )
				{
					if ( !int.TryParse( mode, out var vis ) ) continue;
					map.ToolsVisMode = vis;
					for ( int i = 0; i < 3; i++ ) RenderFrame( view );
					gpu.WaitIdle();
					(texture, layout) = map.Output;
					var visPath = Path.ChangeExtension( path, null ) + $"-vis{vis}.png";
					Capture.Save( gpu, texture, layout, visPath );
					BackendDevice.SetLayout( map.Target.Samples > 1 ? map.Target.Resolved : map.Target.Color, ImageLayout.CopySource );
					Console.WriteLine( $"Saved {visPath}" );
				}

				map.ToolsVisMode = 0;
			}
		}

		Console.WriteLine( bindingReport );
		var (allPipelines, allPipelineMs) = gpu.PipelineCreation;
		Console.WriteLine( $"Pipelines made in the whole run: {allPipelines}, {allPipelineMs:0.0} ms" );
		if ( gpu.Capabilities.PipelineLibraries ) Console.WriteLine( $"Pipeline libraries in the whole run: {gpu.PipelineLibraryCreation}; guessed parts {gpu.PartGuesses.Made}, {gpu.PartGuesses.Used} linked, {gpu.PartGuesses.Wasted} wasted" );
		Console.WriteLine( $"Pipelines made while their draws were skipped: {gpu.DeferredPipelines}" );
		gpu.WaitIdle();
		if ( censusFolder is not null ) File.WriteAllText( Path.Combine( censusFolder, "census.md" ), censusReport.ToString() );
		Directory.CreateDirectory( options.Output );
		var md = new StringBuilder();
		md.AppendLine( $"Borealis Lab -map {options.Ident} -real, {DateTime.Now:yyyy-MM-dd HH:mm}, {gpu.Adapter.Name}, {options.Width}x{options.Height} {options.Samples}x MSAA, vsync off, {options.Frames} measured frames per run (medians, ms)" );
		md.AppendLine();
		md.AppendLine( $"Baked lighting: {map.LightmapTextures} lightmaps for {map.LightmappedObjects} objects and aggregates, {map.ProbeVolumes} probe volumes for {map.ProbeObjects} objects; {map.EnvMapProbesCaptured} of {map.EnvMapProbes} envmap probes captured; {map.Lights} lights ({map.BakedLights} baked); 3D skybox {(map.SkyWorld is null ? "none" : "loaded")}; occlusion culling {(RenderHost.Current.OcclusionCulling ? "on" : "off")}" );
		md.AppendLine();
		md.AppendLine( $"GPU-driven tables: {map.Backend.GpuDrivenReport}" );
		md.AppendLine();
		md.AppendLine( $"Entities used, by class: {(map.EntityClasses.Count == 0 ? "none" : string.Join( ", ", map.EntityClasses.Select( x => $"{x.Key} {x.Value}" ) ))}" );
		if ( options.Unoptimised.Length > 0 ) md.AppendLine( $"**Debug build: {options.Unoptimised}**" );
		md.AppendLine();
		md.AppendLine( "```" );
		foreach ( var line in lines ) md.AppendLine( line );
		md.AppendLine( "```" );
		md.AppendLine();
		if ( passTimer is not null )
		{
			md.AppendLine( passTables.ToString() );
			File.WriteAllText( Path.Combine( options.Output, "map-passes.csv" ), passCsv.ToString() );
		}

		md.AppendLine( bindingReport );
		if ( gpu.ShaderStatistics.Count > 0 ) File.WriteAllText( Path.Combine( options.Output, "map-shader-stats.csv" ), ShaderStatisticsCsv( gpu.ShaderStatistics ) );
		File.WriteAllText( Path.Combine( options.Output, "map-real.md" ), md.ToString() );
		File.WriteAllText( Path.Combine( options.Output, "map-real.csv" ), csv.ToString() );
		if ( map.Backend.Surfaces.ConvertedMaterials > 0 ) Console.WriteLine( $"Surface batches drawn GPU-driven: {map.Backend.Surfaces.GpuDrivenBatches} over the run" );
		Console.WriteLine( $"Results in {options.Output}; validation errors {gpu.ValidationErrors}" );
		return 0;
	}

	/// <summary>
	/// -shader-stats: the driver's statistics for every pipeline the run made, a row per executable (pipeline, executable,
	/// stages, wave size, then each statistic the driver names).
	/// </summary>
	static string ShaderStatisticsCsv( IReadOnlyCollection<ShaderStatistics> statistics )
	{
		var names = statistics.SelectMany( s => s.Values.Keys ).Distinct().ToList();
		static string Quote( string text ) => $"\"{text.Replace( "\"", "\"\"" )}\"";
		var csv = new StringBuilder();
		csv.AppendLine( string.Join( ',', new[] { "pipeline", "executable", "stages", "wave" }.Concat( names ).Select( Quote ) ) );
		foreach ( var s in statistics )
			csv.AppendLine( string.Join( ',', new[] { Quote( s.Pipeline ?? "" ), Quote( s.Executable ), s.Stages.ToString(), s.WaveSize.ToString() }.Concat( names.Select( n => s.Values.TryGetValue( n, out var v ) ? v.ToString( System.Globalization.CultureInfo.InvariantCulture ) : "" ) ) ) );
		return csv.ToString();
	}

	/// <summary>
	/// One frame with every draw counted (<see cref="BackendDevice.Census"/>), after the GPU finished it: each draw into
	/// <c>&lt;name&gt;-draws.csv</c>, each pass's totals into <c>&lt;name&gt;-passes.csv</c>, and the report, which it returns.
	/// </summary>
	static string Census( GpuDevice gpu, MapWorld map, string name, string folder, Action renderFrame )
	{
		Directory.CreateDirectory( folder );
		gpu.WaitIdle();
		var census = new DrawCensus();
		map.Backend.Census = census;
		try
		{
			renderFrame();
		}
		finally
		{
			map.Backend.Census = null;
		}

		gpu.WaitIdle();
		census.Resolve( gpu );
		File.WriteAllText( Path.Combine( folder, $"{name}-draws.csv" ), census.Csv() );
		File.WriteAllText( Path.Combine( folder, $"{name}-passes.csv" ), census.PassCsv() );
		var orders = map.OverlayOrders.ToArray();
		var report = census.Report( name ) + $"{Environment.NewLine}Static overlay objects: {orders.Sum( o => o.Objects )} in {orders.Length} render orders, at most {(orders.Length == 0 ? 0 : orders.Max( o => o.Objects ))} in one{Environment.NewLine}";
		File.WriteAllText( Path.Combine( folder, $"{name}-overlay-orders.csv" ), "order,objects" + Environment.NewLine + string.Concat( orders.Select( o => $"{o.Order},{o.Objects}{Environment.NewLine}" ) ) );
		Console.WriteLine( report );
		return report;
	}

	/// <summary>
	/// What the material objects made so far bind, by shader and way of drawing: per object, how many material textures,
	/// pass textures, samplers, material and pass constant buffers, buffers and storage textures, and of those how many the
	/// backend fills with a placeholder because nothing standalone provides them.
	/// </summary>
	static string DescribeBindings( BackendDevice backend )
	{
		var objects = backend.MaterialCache.Describe();
		var sb = new StringBuilder();
		sb.AppendLine( $"What each material object binds ({objects.Length} objects, {objects.Select( o => o.Material ).Distinct().Count()} materials), mean per object [min-max]:" );
		sb.AppendLine();
		sb.AppendLine( "| Shader | Way | Objects | Material textures | Pass textures | Samplers | Material constants | Pass constant buffers | Buffers | Other | Pass resources by name |" );
		sb.AppendLine( "|---|---|---|---|---|---|---|---|---|---|---|" );

		foreach ( var group in objects.GroupBy( o => (Shader: o.Material?.Shader?.Name ?? "(none)", Way: o.DepthOnly ? "depth only" : "colour") ).OrderByDescending( g => g.Count() ) )
		{
			var counts = group.Select( o => Count( o.Layout.Variant ) ).ToArray();
			string Stat( Func<int[], int> f ) => $"{counts.Average( c => f( c ) ):0.0} [{counts.Min( c => f( c ) )}-{counts.Max( c => f( c ) )}]";
			var names = group.SelectMany( o => o.Layout.Variant.Resources )
				.Where( r => r.Kind is ShaderResourceKind.ConstantBuffer or ShaderResourceKind.Buffer || (r.Kind == ShaderResourceKind.Texture && r.Variable.Source != VariableSource.Material) )
				.Select( r => r.Name ).Distinct().OrderBy( x => x, StringComparer.Ordinal );
			sb.AppendLine( $"| {group.Key.Shader} | {group.Key.Way} | {group.Count()} | {Stat( c => c[0] )} | {Stat( c => c[1] )} | {Stat( c => c[2] )} | {Stat( c => c[3] )} | {Stat( c => c[4] )} | {Stat( c => c[5] )} | {Stat( c => c[6] )} | {string.Join( ", ", names )} |" );
		}

		return sb.ToString();
	}

	/// <summary>A variant's bindings: material textures, pass textures, samplers, material constants, pass constant buffers, buffers, other.</summary>
	static int[] Count( ShaderVariant variant )
	{
		var c = new int[7];
		foreach ( var r in variant.Resources )
		{
			var i = r.Kind switch
			{
				ShaderResourceKind.Texture => r.Variable.Source == VariableSource.Material ? 0 : 1,
				ShaderResourceKind.Sampler => 2,
				ShaderResourceKind.MaterialConstants => 3,
				ShaderResourceKind.ConstantBuffer => 4,
				ShaderResourceKind.Buffer => 5,
				_ => 6,
			};
			c[i] += Math.Max( r.Count, 1 );
		}

		return c;
	}

	static double Ms( long from, long to ) => Stopwatch.GetElapsedTime( from, to ).TotalMilliseconds;

	/// <summary>The view turned <paramref name="turns"/> of a full circle around the point it looks at, at the same height.</summary>
	static MapView Orbit( MapView view, float turns )
	{
		var (sin, cos) = MathF.SinCos( turns * MathF.Tau );
		var offset = view.Position - view.LookAt;
		var turned = new System.Numerics.Vector3( offset.X * cos - offset.Y * sin, offset.X * sin + offset.Y * cos, offset.Z );
		return view with { Position = view.LookAt + turned };
	}

	static double Percentile( IEnumerable<double> values, double p )
	{
		var sorted = values.OrderBy( x => x ).ToArray();
		return sorted.Length == 0 ? double.NaN : sorted[Math.Clamp( (int)(p * (sorted.Length - 1) + 0.5), 0, sorted.Length - 1 )];
	}
}
