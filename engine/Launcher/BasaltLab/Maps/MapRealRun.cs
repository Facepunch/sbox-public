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
/// multi-draws and descriptor sets written. Prints and saves a table, and what the materials' draws bind.
/// </summary>
internal static class MapRealRun
{
	public sealed record Options( string Ident, MapView[] Views, int Width, int Height, int Samples, int Frames, int Warmup, string Captures, string Output, string Unoptimised );

	/// <summary>A way of drawing it: recording on one thread or several, material objects or binding by reflection, and whether static geometry casts sun shadows.</summary>
	sealed record Variant( string Name, bool Parallel, bool MaterialObjects, bool Shadows );

	static readonly Variant[] Variants =
	[
		new( "real-serial", false, true, false ),
		new( "real-parallel", true, true, false ),
		new( "real-reflection", false, false, false ),
		new( "real-serial-shadows", false, true, true ),
		new( "real-parallel-shadows", true, true, true ),
	];

	struct Frame
	{
		public double Wait, Render, Copy, Collect, Prepare, Setup, Record, WorkerRecord, Stages, Submit, Gpu;
	}

	public static int Run( GpuDevice gpu, Win32Window window, SwapChain swapChain, string folder, ContentFiles core, Options options )
	{
		var only = Args.String( "-variants" )?.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );
		var variants = only is null ? Variants : Variants.Where( v => only.Contains( v.Name, StringComparer.OrdinalIgnoreCase ) ).ToArray();
		if ( variants.Length == 0 )
		{
			Console.Error.WriteLine( $"-variants names none of {string.Join( ", ", Variants.Select( v => v.Name ) )}" );
			return 2;
		}

		var load = Stopwatch.StartNew();
		using var map = new MapWorld( gpu, folder, core, castShadows: false, aggregatesAsMeshes: Args.Has( "-aggregates-as-meshes" ) );
		map.SetTarget( options.Width, options.Height, options.Samples );
		map.System.DepthPrepass = !Args.Has( "-noprepass" );
		foreach ( var (name, ms) in map.Phases ) Console.WriteLine( $"  {name}: {ms:0} ms" );
		Console.WriteLine( $"Loaded in {load.Elapsed.TotalSeconds:0.0} s; {map.Problems.Count} problems" );
		foreach ( var problem in map.Problems.Take( 10 ) ) Console.WriteLine( $"  {problem}" );

		using var timestamps = gpu.CreateTimestamps( 2 * 4 );
		var period = gpu.Capabilities.TimestampPeriod / 1e6;
		var ticks = new ulong[2];
		long frameNumber = 0;

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

			map.Render( view );
			var t2 = Stopwatch.GetTimestamp();

			var copy = gpu.BeginCommandList( QueueType.Graphics, "Map to window" );
			if ( image.IsValid ) map.CopyTo( copy, image );
			copy.WriteTimestamp( timestamps, slot * 2 + 1 );
			gpu.Submit( copy, swapChain: image.IsValid ? swapChain : null );
			if ( image.IsValid ) swapChain.Present();
			var t3 = Stopwatch.GetTimestamp();

			f.Gpu = double.NaN;
			var old = (int)((frameNumber + 1) % 4);
			if ( frameNumber >= 4 && timestamps.TryRead( old * 2, ticks ) ) f.Gpu = (ticks[1] - ticks[0]) * period;
			frameNumber++;

			var s = map.System.Stats;
			f.Wait = Ms( t0, t1 );
			f.Render = Ms( t1, t2 );
			f.Copy = Ms( t2, t3 );
			(f.Collect, f.Prepare, f.Setup, f.Record, f.WorkerRecord, f.Stages, f.Submit) = (s.CollectMs, s.PrepareMs, s.SetupMs, s.RecordMs, s.WorkerRecordMs, s.StagesMs, s.SubmitMs);
			return f;
		}

		var lines = new List<string>();
		var csv = new StringBuilder( "view,variant,parallel,material_objects,shadows,frames,wait_ms,render_ms,collect_ms,prepare_ms,setup_ms,record_ms,worker_record_ms,stages_ms,submit_ms,copy_ms,cpu_ms,frame_ms,frame_p95_ms,gpu_ms,draws,depth_draws,shadow_draws,translucent_draws,instances,objects_visible,triangles,model_draws,binds,fragment_draws,ns_per_bind,ns_per_repeat,sets_written_per_frame,gcs\n" );
		var bindingReport = "";

		foreach ( var view in options.Views )
		{
			foreach ( var variant in variants )
			{
				map.System.ParallelRecording = variant.Parallel;
				map.Backend.MaterialObjects = variant.MaterialObjects;
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
				var last = Stopwatch.GetTimestamp();
				for ( int i = 0; i < options.Frames && !window.Closed; i++ )
				{
					window.Pump();
					frames.Add( RenderFrame( view ) );
					var now = Stopwatch.GetTimestamp();
					frameTimes.Add( Stopwatch.GetElapsedTime( last, now ).TotalMilliseconds );
					last = now;
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

				double M( Func<Frame, double> f ) => Percentile( frames.Select( f ).Where( x => !double.IsNaN( x ) ), 0.5 );
				var cpu = M( f => f.Render + f.Copy );
				var line = $"{view.Name,-9} {variant.Name,-22} collect {M( f => f.Collect ),6:0.000} prepare {M( f => f.Prepare ),6:0.000} setup {M( f => f.Setup ),6:0.000} record {M( f => f.Record ),6:0.000} (workers {M( f => f.WorkerRecord ):0.000}) submit {M( f => f.Submit ),6:0.000} render {M( f => f.Render ),6:0.000} copy {M( f => f.Copy ),5:0.000} wait {M( f => f.Wait ),6:0.000} frame {Percentile( frameTimes, 0.5 ),6:0.000} gpu {M( f => f.Gpu ),6:0.000}  "
					+ $"{stats.Draws}+{stats.DepthDraws} depth+{stats.ShadowDraws} shadow draws ({stats.TranslucentDraws} translucent, {stats.Instances} instances), {stats.ObjectsVisible} objects visible, {stats.Triangles / 1e6:0.00}M tris; {modelDraws} model draws, {binds} binds, {fragmentDraws} fragment draws ({nsBind:0} ns a bind, {nsRepeat:0} ns a repeat draw, timed), {setsPerFrame:0.0} sets written/frame, {gcs} GCs";
				Console.WriteLine( line );
				lines.Add( line );
				csv.AppendLine( FormattableString.Invariant( $"{view.Name},{variant.Name},{variant.Parallel},{variant.MaterialObjects},{variant.Shadows},{frames.Count},{M( f => f.Wait ):0.0000},{M( f => f.Render ):0.0000},{M( f => f.Collect ):0.0000},{M( f => f.Prepare ):0.0000},{M( f => f.Setup ):0.0000},{M( f => f.Record ):0.0000},{M( f => f.WorkerRecord ):0.0000},{M( f => f.Stages ):0.0000},{M( f => f.Submit ):0.0000},{M( f => f.Copy ):0.0000},{cpu:0.0000},{Percentile( frameTimes, 0.5 ):0.0000},{Percentile( frameTimes, 0.95 ):0.0000},{M( f => f.Gpu ):0.0000},{stats.Draws},{stats.DepthDraws},{stats.ShadowDraws},{stats.TranslucentDraws},{stats.Instances},{stats.ObjectsVisible},{stats.Triangles},{modelDraws},{binds},{fragmentDraws},{nsBind:0},{nsRepeat:0},{setsPerFrame:0.0},{gcs}" ) );
			}

			if ( bindingReport.Length == 0 ) bindingReport = DescribeBindings( map.Backend );

			if ( options.Captures is not null )
			{
				map.System.ParallelRecording = false;
				map.Backend.MaterialObjects = true;
				map.SetCastShadows( false );
				for ( int i = 0; i < 3; i++ ) RenderFrame( view );
				gpu.WaitIdle();
				var (texture, layout) = map.Output;
				var path = Path.Combine( Path.GetFullPath( options.Captures ), $"{options.Ident.Split( '.' ).Last()}-{view.Name}-real.png" );
				Capture.Save( gpu, texture, layout, path );
				BackendDevice.SetLayout( map.Target.Samples > 1 ? map.Target.Resolved : map.Target.Color, ImageLayout.CopySource );
				Console.WriteLine( $"Saved {path}" );
			}
		}

		Console.WriteLine( bindingReport );
		gpu.WaitIdle();
		Directory.CreateDirectory( options.Output );
		var md = new StringBuilder();
		md.AppendLine( $"Basalt Lab -map {options.Ident} -real, {DateTime.Now:yyyy-MM-dd HH:mm}, {gpu.Adapter.Name}, {options.Width}x{options.Height} {options.Samples}x MSAA, vsync off, {options.Frames} measured frames per run (medians, ms)" );
		if ( options.Unoptimised.Length > 0 ) md.AppendLine( $"**Debug build: {options.Unoptimised}**" );
		md.AppendLine();
		md.AppendLine( "```" );
		foreach ( var line in lines ) md.AppendLine( line );
		md.AppendLine( "```" );
		md.AppendLine();
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
