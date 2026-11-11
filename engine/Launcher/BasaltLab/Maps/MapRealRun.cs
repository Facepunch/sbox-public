using Basalt;
using Basalt.Backend;
using Basalt.Gpu;
using Basalt.Source2;
using Basalt.Source2.Shaders;
using System.Diagnostics;
using System.IO;
using System.Text;
using Capture = BasaltGpuSample.Capture;
using GpuDevice = Basalt.Gpu.Device;
using Win32Window = BasaltGpuSample.Win32Window;

namespace Sandbox.BasaltLab.Maps;

/// <summary>
/// <c>-map ident -real</c>: the map in a <see cref="MapWorld"/>, drawn by Basalt's <see cref="RenderSystem"/> with the
/// map's materials through <see cref="BackendDevice"/>, timed per run of frames: Basalt's collect, prepare, setup,
/// record and submit, the copy into the window, and GPU time from timestamps; then, on warm frames, the draws, binds,
/// object changes (the same material with another object's lightmap, probe or attributes), multi-draws and descriptor
/// sets written. Prints and saves a table, and what the materials' draws bind. <c>-nobaked</c> leaves the map's
/// lightmaps and probes out; <c>-occlusion</c> turns on the GPU occlusion cull.
/// </summary>
internal static class MapRealRun
{
	public sealed record Options( string Ident, MapView[] Views, int Width, int Height, int Samples, int Frames, int Warmup, string Captures, string Output, string Unoptimised );

	/// <summary>
	/// A way of drawing it: recording on one thread or several, material objects or binding by reflection, whether static
	/// geometry casts sun shadows, and whether aggregates draw GPU-driven (<c>-gpudriven</c>).
	/// </summary>
	sealed record Variant( string Name, bool Parallel, bool MaterialObjects, bool Shadows, bool GpuDriven = false, bool MaterialBuckets = false );

	static readonly Variant[] Variants =
	[
		new( "real-serial", false, true, false ),
		new( "real-parallel", true, true, false ),
		new( "real-reflection", false, false, false ),
		new( "real-serial-shadows", false, true, true ),
		new( "real-parallel-shadows", true, true, true ),
		new( "real-serial-gpudriven", false, true, false, true ),
		new( "real-parallel-gpudriven", true, true, false, true ),
		new( "real-serial-gpudriven-materials", false, true, false, true, true ),
		new( "real-parallel-gpudriven-materials", true, true, false, true, true ),
	];

	struct Frame
	{
		public double Wait, Render, Copy, Collect, Prepare, Setup, Record, WorkerRecord, Prepass, Stages, Submit, Gpu;
	}

	public static int Run( GpuDevice gpu, Win32Window window, SwapChain swapChain, string folder, ContentFiles core, Options options )
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
		if ( Args.Has( "-occlusion" ) && RenderHost.Current is BackendRenderHost host ) host.Occlusion = true;
		foreach ( var (name, ms) in map.Phases ) Console.WriteLine( $"  {name}: {ms:0} ms" );
		Console.WriteLine( $"Loaded in {load.Elapsed.TotalSeconds:0.0} s; {map.Problems.Count} problems" );
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
		map.Backend.Shaders.ShareVariants = !Args.Has( "-no-share-variants" );
		var passes = new PassTotals();
		var timings = new List<PassTimer.Timing>();

		Frame RenderFrame( MapView view )
		{
			var f = default( Frame );
			var t0 = Stopwatch.GetTimestamp();
			map.Backend.BeginFrame();
			var image = default( TextureHandle );
			if ( !swapChain.Acquire( out image ) ) image = default;
			var t1 = Stopwatch.GetTimestamp();

			// GPU time: from the start of Basalt's frame list to after its last list, three frames back
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
			return f;
		}

		var lines = new List<string>();
		var csv = new StringBuilder( "view,variant,parallel,material_objects,shadows,frames,wait_ms,render_ms,collect_ms,prepare_ms,setup_ms,record_ms,worker_record_ms,stages_ms,submit_ms,copy_ms,cpu_ms,frame_ms,frame_p95_ms,gpu_ms,draws,depth_draws,shadow_draws,translucent_draws,instances,objects_visible,triangles,model_draws,binds,fragment_draws,ns_per_bind,ns_per_repeat,sets_written_per_frame,gcs,object_changes,ns_per_object_change,failed_draws,cpu_mean_ms,frame_mean_ms,gpu_mean_ms,prepass_record_ms,gpudriven,gpudriven_batches,gpudriven_buckets,gpudriven_multidraws\n" );
		var bindingReport = "";
		var passTables = new StringBuilder();
		var passCsv = new StringBuilder( "view,variant,scope,kind,ms,per_frame,primitives,vertex_invocations,fragment_invocations,draws,pipelines,sets,rendering_scopes\n" );

		foreach ( var view in options.Views )
		{
			foreach ( var variant in variants )
			{
				map.System.ParallelRecording = variant.Parallel;
				map.Backend.MaterialObjects = variant.MaterialObjects;
				map.Backend.GpuDriven = variant.GpuDriven;
				map.Backend.GpuDrivenMaterialBuckets = variant.MaterialBuckets;
				map.SetCastShadows( variant.Shadows );

				var warm = Stopwatch.StartNew();
				for ( int i = 0; (i < options.Warmup || warm.Elapsed.TotalSeconds < 2) && !window.Closed; i++ )
				{
					window.Pump();
					RenderFrame( view );
				}

				var frames = new List<Frame>( options.Frames );
				var frameTimes = new List<double>( options.Frames );
				var sets = map.Backend.MaterialObjectStats.SetsWritten;
				var gcs = GC.CollectionCount( 0 );
				passes.Clear();
				var last = Stopwatch.GetTimestamp();
				for ( int i = 0; i < options.Frames && !window.Closed; i++ )
				{
					window.Pump();
					frames.Add( RenderFrame( view ) );
					var now = Stopwatch.GetTimestamp();
					frameTimes.Add( Stopwatch.GetElapsedTime( last, now ).TotalMilliseconds );
					last = now;
				}

				if ( passTimer is not null )
				{
					var table = passes.Describe( $"{view.Name} {variant.Name}", options.Width * options.Height, passTimer.Statistics );
					Console.WriteLine( table );
					passTables.AppendLine( table );
					passes.AppendCsv( passCsv, view.Name, variant.Name );
				}

				gcs = GC.CollectionCount( 0 ) - gcs;
				var setsPerFrame = (map.Backend.MaterialObjectStats.SetsWritten - sets) / (double)Math.Max( frames.Count, 1 );
				var stats = map.System.Stats;

				// Counted on warm frames after the timed ones, with the draw timer on (it costs a little)
				map.Backend.TimeDraws = true;
				map.Backend.DrawTimes.Reset();
				const int Counted = 10;
				for ( int i = 0; i < Counted; i++ ) RenderFrame( view );
				map.Backend.TimeDraws = false;
				var t = map.Backend.DrawTimes;
				var (modelDraws, binds, fragmentDraws) = (t.Draws / Counted, t.Binds / Counted, t.IndirectDraws / Counted);
				var (nsBind, nsRepeat) = (t.NanosecondsPerBind, t.NanosecondsPerRepeat);
				var (objectChanges, nsObjectChange) = (t.ObjectChanges / Counted, t.NanosecondsPerObjectChange);
				var gpuDrivenLine = variant.GpuDriven ? $"GPU-driven: {t.GpuDrivenBatches / Counted} aggregate batches in {t.GpuDrivenBuckets / Counted} buckets, {t.GpuDrivenMultiDraws / Counted} multi-draws of {t.GpuDrivenFragments / Counted} fragments; " : "";
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
					+ $"{stats.Draws}+{stats.DepthDraws} depth+{stats.ShadowDraws} shadow draws ({stats.TranslucentDraws} translucent, {stats.Instances} instances), {stats.ObjectsVisible} objects visible, {stats.Triangles / 1e6:0.00}M tris; {modelDraws} model draws, {binds} binds, {objectChanges} object changes, {fragmentDraws} fragment draws ({nsBind:0} ns a bind, {nsObjectChange:0} ns an object change, {nsRepeat:0} ns a repeat draw, timed), {setsPerFrame:0.0} sets written/frame, {gcs} GCs; {gpuDrivenLine}{(map.System.SkyboxStats is { } sky ? $"3D skybox {sky.Draws}+{sky.DepthDraws} depth draws of {sky.ObjectsVisible} visible objects of {sky.Objects} ({sky.ObjectsFrustumCulled} frustum culled, {sky.ObjectsSizeCulled} size culled, {sky.ObjectsTagCulled} tag culled, {sky.ObjectsOcclusionCulled} occlusion culled)" : "no 3D skybox drawn")}; draws that recorded nothing: {failed}";
				Console.WriteLine( line );
				lines.Add( line );
				csv.AppendLine( FormattableString.Invariant( $"{view.Name},{variant.Name},{variant.Parallel},{variant.MaterialObjects},{variant.Shadows},{frames.Count},{M( f => f.Wait ):0.0000},{M( f => f.Render ):0.0000},{M( f => f.Collect ):0.0000},{M( f => f.Prepare ):0.0000},{M( f => f.Setup ):0.0000},{M( f => f.Record ):0.0000},{M( f => f.WorkerRecord ):0.0000},{M( f => f.Stages ):0.0000},{M( f => f.Submit ):0.0000},{M( f => f.Copy ):0.0000},{cpu:0.0000},{Percentile( frameTimes, 0.5 ):0.0000},{Percentile( frameTimes, 0.95 ):0.0000},{M( f => f.Gpu ):0.0000},{stats.Draws},{stats.DepthDraws},{stats.ShadowDraws},{stats.TranslucentDraws},{stats.Instances},{stats.ObjectsVisible},{stats.Triangles},{modelDraws},{binds},{fragmentDraws},{nsBind:0},{nsRepeat:0},{setsPerFrame:0.0},{gcs},{objectChanges},{nsObjectChange:0},{failedDraws},{Mean( f => f.Render + f.Copy ):0.0000},{frameTimes.DefaultIfEmpty( double.NaN ).Average():0.0000},{Mean( f => f.Gpu ):0.0000},{M( f => f.Prepass ):0.0000},{(variant.GpuDriven ? (variant.MaterialBuckets ? "materials" : "onebucket") : "classic")},{t.GpuDrivenBatches / Counted},{t.GpuDrivenBuckets / Counted},{t.GpuDrivenMultiDraws / Counted}" ) );
			}

			if ( bindingReport.Length == 0 ) bindingReport = DescribeBindings( map.Backend );

			if ( options.Captures is not null )
			{
				map.System.ParallelRecording = false;
				map.Backend.MaterialObjects = true;
				map.SetCastShadows( false );
				var path = Path.Combine( Path.GetFullPath( options.Captures ), $"{options.Ident.Split( '.' ).Last()}-{view.Name}-real.png" );

				// The GPU-driven frames between two classic ones, to compare (wind and water move between captures)
				foreach ( var (drivenCapture, materialBuckets, suffix) in gpuDriven ? new[] { (false, false, "-classic-before"), (true, false, "-gpudriven"), (true, true, "-gpudriven-materials"), (false, false, "") } : [(false, false, "")] )
				{
					map.Backend.GpuDriven = drivenCapture;
					map.Backend.GpuDrivenMaterialBuckets = materialBuckets;
					for ( int i = 0; i < 3; i++ ) RenderFrame( view );
					gpu.WaitIdle();
					var (captured, capturedLayout) = map.Output;
					var capturePath = Path.ChangeExtension( path, null ) + suffix + ".png";
					Capture.Save( gpu, captured, capturedLayout, capturePath );
					BackendDevice.SetLayout( map.Target.Samples > 1 ? map.Target.Resolved : map.Target.Color, ImageLayout.CopySource );
					Console.WriteLine( $"Saved {capturePath}" );
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
		gpu.WaitIdle();
		Directory.CreateDirectory( options.Output );
		var md = new StringBuilder();
		md.AppendLine( $"Basalt Lab -map {options.Ident} -real, {DateTime.Now:yyyy-MM-dd HH:mm}, {gpu.Adapter.Name}, {options.Width}x{options.Height} {options.Samples}x MSAA, vsync off, {options.Frames} measured frames per run (medians, ms)" );
		md.AppendLine();
		md.AppendLine( $"Baked lighting: {map.LightmapTextures} lightmaps for {map.LightmappedObjects} objects and aggregates, {map.ProbeVolumes} probe volumes for {map.ProbeObjects} objects; {map.Lights} lights ({map.BakedLights} baked); 3D skybox {(map.SkyWorld is null ? "none" : "loaded")}; occlusion culling {(RenderHost.Current.OcclusionCulling ? "on" : "off")}" );
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
		File.WriteAllText( Path.Combine( options.Output, "map-real.md" ), md.ToString() );
		File.WriteAllText( Path.Combine( options.Output, "map-real.csv" ), csv.ToString() );
		Console.WriteLine( $"Results in {options.Output}; validation errors {gpu.ValidationErrors}" );
		return 0;
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

	static double Percentile( IEnumerable<double> values, double p )
	{
		var sorted = values.OrderBy( x => x ).ToArray();
		return sorted.Length == 0 ? double.NaN : sorted[Math.Clamp( (int)(p * (sorted.Length - 1) + 0.5), 0, sorted.Length - 1 )];
	}
}
