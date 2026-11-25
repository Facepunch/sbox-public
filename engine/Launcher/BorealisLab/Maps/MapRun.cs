using Borealis.Gpu;
using Borealis.Sdl;
using Borealis.Source2;
using Capture = BorealisGpuSample.Capture;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using GpuDevice = Borealis.Gpu.Device;
using V3 = System.Numerics.Vector3;

namespace Sandbox.BorealisLab.Maps;

/// <summary>
/// <c>-map ident</c> (or <c>-construct</c> for <c>facepunch.construct</c>): load a compiled map with Borealis.Source2 and
/// draw it on a Borealis.Gpu device of its own, with no Source 2 device and none of the engine started, from fixed cameras
/// (SceneLab's for Construct), timing each way of issuing the draws. Prints and saves a table; exits 1 on validation errors.
/// <list type="bullet">
/// <item><c>-map-folder path</c> - the map package's folder instead of finding the installed workshop item (in Steam's libraries, or
/// <c>-workshop-content dir</c>, a folder of items by id): the map in it is
/// <c>maps/name.vpk</c> or <c>maps/folder/name.vpk</c>.</item>
/// <item><c>-view name</c> - one camera (<c>skyline</c>, <c>overview</c>); all by default.</item>
/// <item><c>-camera-pos x,y,z</c>, <c>-camera-lookat x,y,z</c> ((0, 0, 30) by default) - one camera of any map instead, as SceneLab's
/// switches move its map presets' cameras; <c>-view</c> names it; <c>-camera-fov</c> its horizontal field of view (80).</item>
/// <item><c>-variants a,b</c> - which of the runs below; all by default.</item>
/// <item><c>-frames N</c> (600), <c>-warmup N</c> (120 frames and 2 s) - measured and warm-up frames per run.</item>
/// <item><c>-width</c> / <c>-height</c> (1280x720, SceneLab's frame), <c>-msaa N</c> (4), <c>-vsync</c> (off by default).</item>
/// <item><c>-fullscreen</c> - borderless fullscreen on the desktop's mode; <c>-fullscreen exclusive</c> exclusive in it. The
/// swap chain takes the display's size; <c>-width</c> / <c>-height</c> still set the frame drawn into it.</item>
/// <item><c>-capture folder</c> - save each view's frame (material objects, shadows) as <c>map-view.png</c>.</item>
/// <item><c>-out folder</c> - where <c>map.md</c> and <c>map.csv</c> go (default <c>screenshots/borealislab/map</c>).</item>
/// <item><c>-texture-size N</c> (1024) - mips above this are left out, to load faster.</item>
/// <item><c>-validation</c> - the Khronos validation layer; <c>-sync-validation</c> with synchronisation checked too. Each message is counted by id and the counts printed at the end.</item>
/// <item><c>-real</c> - draw it with Borealis's renderer and the map's own materials and shaders instead (<see cref="MapRealRun"/>).</item>
/// <item><c>-borealis-driver-names</c> - introduce the device to the driver as Borealis instead of as native's names.</item>
/// </list>
/// </summary>
internal static class MapRun
{
	public static bool Requested => Args.Has( "-construct" ) || Args.String( "-map" ) is not null;

	sealed record Variant( string Name, string Step, int Cascades, IssueMode Mode, bool Parallel, bool Merge = false );

	static readonly Variant[] Variants =
	[
		new( "a-draw", "a", 0, IssueMode.PerDraw, false ),
		new( "a-material", "a", 0, IssueMode.MaterialObjects, false ),
		new( "a-indirect", "a", 0, IssueMode.Indirect, false ),
		new( "a-material-merged", "a", 0, IssueMode.MaterialObjects, false, true ),
		new( "b-draw", "b", 3, IssueMode.PerDraw, false ),
		new( "b-material", "b", 3, IssueMode.MaterialObjects, false ),
		new( "b-indirect", "b", 3, IssueMode.Indirect, false ),
		new( "b-material-merged", "b", 3, IssueMode.MaterialObjects, false, true ),
		new( "d-material-parallel", "d", 3, IssueMode.MaterialObjects, true ),
		new( "d-draw-parallel", "d", 3, IssueMode.PerDraw, true ),
		new( "d-merged-parallel", "d", 3, IssueMode.MaterialObjects, true, true ),
	];

	/// <summary>
	/// SceneLab's views of Construct (MapScenes.ConstructSkyline and ConstructOverview) as SceneLab draws them: its orbit
	/// camera keeps each preset's position but looks at (0, 0, 30) unless <c>-camera-lookat</c> moves it.
	/// </summary>
	internal static readonly MapView[] ConstructViews =
	[
		new( "skyline", new V3( 864, -608, 67 ), new V3( 0, 0, 30 ) ),
		new( "overview", new V3( 3000, 2500, 1200 ), new V3( 0, 0, 30 ) ),
	];

	public static int Run()
	{
		LabTracy.Start();
		global::Borealis.Features.AggregateRenderFeature.QuietRefresh = !Args.Has( "-no-quiet-resident" );
		global::Borealis.Features.AggregateRenderFeature.ShadowsLeaveResidentOut = !Args.Has( "-shadows-cull-resident" );
		global::Borealis.RenderSystem.CollectSkipsResident = !Args.Has( "-collect-resident" );
		global::Borealis.Shadows.ShadowSystem.MarksUnsettled = !Args.Has( "-no-unsettled-marks" );
		var ident = Args.String( "-map" ) ?? "facepunch.construct";
		var folder = Args.String( "-map-folder" ) ?? FindPackage( ident );
		if ( folder is null )
		{
			Console.Error.WriteLine( $"{ident} isn't installed: no workshop item with that package ident in any Steam library. Nothing is downloaded." );
			return 2;
		}

		var width = Args.Int( "-width", 1280 );
		var height = Args.Int( "-height", 720 );
		var samples = Args.Int( "-msaa", 4 );
		var frames = Math.Max( Args.Int( "-frames", 600 ), 10 );
		var warmup = Math.Max( Args.Int( "-warmup", 120 ), 1 );
		var only = Args.String( "-variants" )?.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );
		var variants = only is null ? Variants : Variants.Where( v => only.Contains( v.Name, StringComparer.OrdinalIgnoreCase ) ).ToArray();
		var views = Args.String( "-camera-pos" ) is { } cameraPos ? [new MapView( Args.String( "-view" ) ?? "camera", Vector( cameraPos ), Args.String( "-camera-lookat" ) is { } at ? Vector( at ) : new V3( 0, 0, 30 ), Args.Float( "-camera-fov", 80 ) )]
			: Args.String( "-view" ) is { } viewName ? ConstructViews.Where( v => v.Name.Equals( viewName, StringComparison.OrdinalIgnoreCase ) ).ToArray() : ConstructViews;
		var captures = Args.String( "-capture" );
		var output = Path.GetFullPath( Args.String( "-out" ) ?? Path.Combine( "screenshots", "borealislab", "map" ) );

		var unoptimised = Unoptimised();
		if ( unoptimised.Length > 0 ) Console.WriteLine( $"WARNING: {unoptimised} built without optimisation (Debug). These numbers aren't the renderer's." );

		var errors = 0;
		var messageCounts = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
		var driverNames = !Args.Has( "-borealis-driver-names" );
		using var window = new SdlWindow( new SdlWindowOptions { Title = $"Borealis Lab: {ident} on Borealis.Gpu", Width = width, Height = height, Fullscreen = Args.Has( "-fullscreen" ), Exclusive = Args.String( "-fullscreen" ) is "exclusive" } );
		// -markers: progress markers in every list, so a lost device's report (printed when it's lost) names how far each got
		GpuDevice lostWatch = null;
		using var gpu = GpuDevice.Create( new DeviceOptions
		{
			ProgressMarkers = Args.Has( "-markers" ),
			ApplicationName = driverNames ? "Sbox" : "Borealis Lab map",
			EngineName = driverNames ? "Source2" : "Borealis",
			Validation = Args.Has( "-validation" ) || Args.Has( "-sync-validation" ),
			SynchronizationValidation = Args.Has( "-sync-validation" ),
			ShaderStatistics = Args.Has( "-shader-stats" ),
			InstanceExtensions = Sdl.VulkanInstanceExtensions,
			// game/.source2/borealispipelines, as the game keeps it; -no-pipeline-cache starts cold and saves nothing
			PipelineCacheFolder = Args.Has( "-no-pipeline-cache" ) ? null : Path.GetFullPath( Path.Combine( AppContext.BaseDirectory, "..", "..", ".source2", "borealispipelines" ) ),
			Message = m =>
			{
				if ( m.Severity < MessageSeverity.Warning ) return;
				if ( m.Severity == MessageSeverity.Error ) Interlocked.Increment( ref errors );
				messageCounts.AddOrUpdate( $"[{m.Severity}] {m.Id}", 1, ( _, n ) => n + 1 );
				if ( errors < 20 ) Console.WriteLine( $"[{m.Severity}] {m.Id}: {m.Text}" );
				if ( m.Id == "Borealis.Gpu.DeviceLost" ) Console.WriteLine( lostWatch?.LostReport );
			},
		} );
		lostWatch = gpu;

		var (clientWidth, clientHeight) = window.PixelSize;
		using var swapChain = gpu.CreateSwapChain( new SwapChainInfo
		{
			Surface = SurfaceSource.Custom( window.CreateVulkanSurface ),
			Width = clientWidth,
			Height = clientHeight,
			PresentMode = Args.Has( "-vsync" ) ? PresentMode.Fifo : PresentMode.Immediate,
		} );

		Console.WriteLine( $"{gpu.Adapter}, validation {(gpu.ValidationEnabled ? "on" : "off")}, swap chain {swapChain.Width}x{swapChain.Height} {swapChain.PresentMode}, target {width}x{height} {samples}x MSAA" );
		Console.WriteLine( $"{ident}: {folder}" );

		var core = ContentFiles.FindGame();
		if ( core.Roots.Count == 0 ) core = ContentFiles.FindGame( Environment.CurrentDirectory );

		// The real frame: Borealis's renderer with s&box's shaders and the map's own materials
		if ( Args.Has( "-real" ) )
		{
			var code = MapRealRun.Run( gpu, window, swapChain, folder, core, new MapRealRun.Options( ident, views, width, height, samples, frames, warmup, captures, output, unoptimised ) );
			foreach ( var (id, count) in messageCounts.OrderByDescending( x => x.Value ) ) Console.WriteLine( $"{count,8} {id}" );
			return code != 0 || gpu.ValidationErrors > 0 || errors > 0 ? Math.Max( code, 1 ) : 0;
		}

		var load = Stopwatch.StartNew();
		using var map = new MapContent( gpu, folder, core, Args.Int( "-texture-size", 1024 ) );
		foreach ( var (name, ms) in map.Phases ) Console.WriteLine( $"  {name}: {ms:0} ms" );
		var opaqueMaterials = map.Materials.Count( m => m.Kind == SurfaceKind.Opaque );
		Console.WriteLine( $"Loaded in {load.Elapsed.TotalSeconds:0.0} s: {map.Objects.Count} objects ({map.Objects.Count( x => x.Aggregate )} aggregate draws), {map.Models.Count} models, "
			+ $"{map.Materials.Count} materials ({opaqueMaterials} opaque, {map.Materials.Count( m => m.Kind == SurfaceKind.AlphaTest )} alpha tested, {map.Materials.Count( m => m.Kind == SurfaceKind.Translucent )} translucent, "
			+ $"{map.Materials.Count( m => m.Textured )} textured, {map.Materials.Count( m => m.Unreadable )} unreadable), {map.Problems.Count} problems" );
		foreach ( var problem in map.Problems.Take( 10 ) ) Console.WriteLine( $"  {problem}" );
		if ( Args.Has( "-list-materials" ) )
		{
			foreach ( var m in map.Materials ) Console.WriteLine( $"  {m.Index,4} {m.Kind,-11} {(m.TwoSided ? "two-sided" : ""),-9} {(m.Unreadable ? "unreadable" : m.Textured ? "" : "untextured"),-10} {m.Shader} {m.Path} {m.ColorTexture}" );
		}

		using var renderer = new MapRenderer( gpu, map, width, height, samples, 2048 );
		if ( Args.String( "-sun" ) is { } sun )
		{
			var v = sun.Split( ',' ).Select( x => float.Parse( x, System.Globalization.CultureInfo.InvariantCulture ) ).ToArray();
			renderer.SunDirection = V3.Normalize( new V3( v[0], v[1], v[2] ) );
		}
		var results = new List<string>();
		var csv = new StringBuilder( "view,variant,step,mode,parallel,cascades,frames,wait_ms,prepare_ms,record_ms,submit_ms,cpu_ms,frame_ms,frame_p95_ms,record_p95_ms,gpu_ms,gpu_shadows_ms,gpu_prepass_ms,gpu_forward_ms,draws,triangles,materials,pipelines,set_binds,indirect_calls,gcs\n" );
		var passTable = new StringBuilder();

		// The window's title shows frames a second, the GPU's time and the camera live
		var title = new BorealisGpuTitle();
		var titleName = $"Borealis Lab: {ident}";
		Action<string> setTitle = text => window.Title = text;
		FrameTimes Render( MapView view )
		{
			var drawn = renderer.Render( view, swapChain );
			title.Frame( titleName, default, drawn.Gpu, null, view.Position, setTitle );
			return drawn;
		}

		foreach ( var view in views )
		{
			foreach ( var variant in variants )
			{
				renderer.Cascades = variant.Cascades;
				renderer.Mode = variant.Mode;
				renderer.Parallel = variant.Parallel;
				renderer.MergeRuns = variant.Merge;

				// Warm up for frames and time, then measure
				var warm = Stopwatch.StartNew();
				for ( int i = 0; (i < warmup || warm.Elapsed.TotalSeconds < 2) && !window.CloseRequested; i++ )
				{
					Pump( window );
					Render( view );
				}

				var times = new List<FrameTimes>( frames );
				var frameTimes = new List<double>( frames );
				var gcs = GC.CollectionCount( 0 );
				var last = Stopwatch.GetTimestamp();
				for ( int i = 0; i < frames && !window.CloseRequested; i++ )
				{
					Pump( window );
					times.Add( Render( view ) );
					var now = Stopwatch.GetTimestamp();
					frameTimes.Add( Stopwatch.GetElapsedTime( last, now ).TotalMilliseconds );
					last = now;
				}

				gcs = GC.CollectionCount( 0 ) - gcs;
				var total = default( PassStats );
				foreach ( var pass in renderer.Passes ) total.Add( pass );

				double Median( Func<FrameTimes, double> f ) => Percentile( times.Select( f ).Where( x => !double.IsNaN( x ) ), 0.5 );
				var cpu = Median( t => t.Prepare + t.Record + t.Submit );
				var line = $"{view.Name,-9} {variant.Name,-20} wait {Median( t => t.Wait ),6:0.000}  prepare {Median( t => t.Prepare ),6:0.000}  record {Median( t => t.Record ),6:0.000}  submit {Median( t => t.Submit ),6:0.000}  cpu {cpu,6:0.000}  frame {Percentile( frameTimes, 0.5 ),6:0.000} (p95 {Percentile( frameTimes, 0.95 ):0.000})  gpu {Median( t => t.Gpu ):0.000} (shadows {Median( t => t.GpuShadows ):0.000}, prepass {Median( t => t.GpuPrepass ):0.000}, forward {Median( t => t.GpuForward ):0.000})  "
					+ $"{total.Draws} draws, {total.Triangles / 1e6:0.00}M tris, {total.Pipelines} pipelines, {total.SetBinds} set binds, {total.IndirectCalls} indirect, {gcs} GCs";
				Console.WriteLine( line );
				results.Add( line );
				csv.AppendLine( FormattableString.Invariant( $"{view.Name},{variant.Name},{variant.Step},{variant.Mode},{variant.Parallel},{variant.Cascades},{times.Count},{Median( t => t.Wait ):0.0000},{Median( t => t.Prepare ):0.0000},{Median( t => t.Record ):0.0000},{Median( t => t.Submit ):0.0000},{cpu:0.0000},{Percentile( frameTimes, 0.5 ):0.0000},{Percentile( frameTimes, 0.95 ):0.0000},{Percentile( times.Select( t => t.Record ), 0.95 ):0.0000},{Median( t => t.Gpu ):0.0000},{Median( t => t.GpuShadows ):0.0000},{Median( t => t.GpuPrepass ):0.0000},{Median( t => t.GpuForward ):0.0000},{total.Draws},{total.Triangles},{total.Materials},{total.Pipelines},{total.SetBinds},{total.IndirectCalls},{gcs}" ) );

				passTable.AppendLine( $"| {view.Name} | {variant.Name} | {string.Join( " | ", renderer.Passes.Select( p => $"{p.Draws} / {p.Triangles / 1000}k / {p.Materials} / {p.Pipelines} / {p.SetBinds}{(p.IndirectCalls > 0 ? $" / {p.IndirectCalls} MDI" : "")}" ) )} |" );
			}

			if ( captures is not null )
			{
				renderer.Cascades = MapRenderer.MaxCascades;
				renderer.Mode = IssueMode.MaterialObjects;
				renderer.Parallel = false;
				renderer.MergeRuns = false;
				for ( int i = 0; i < 3; i++ ) renderer.Render( view, swapChain );
				gpu.WaitIdle();
				var path = Path.Combine( Path.GetFullPath( captures ), $"{ident.Split( '.' ).Last()}-{view.Name}.png" );
				Capture.Save( gpu, renderer.Resolved, ImageLayout.CopySource, path );
				Console.WriteLine( $"Saved {path}" );
			}
		}

		gpu.WaitIdle();
		Directory.CreateDirectory( output );
		var md = new StringBuilder();
		md.AppendLine( $"Borealis Lab -map {ident}, {DateTime.Now:yyyy-MM-dd HH:mm}, {gpu.Adapter.Name}, {width}x{height} {samples}x MSAA, vsync {(Args.Has( "-vsync" ) ? "on" : "off")}, {frames} measured frames per run (medians, ms)" );
		if ( unoptimised.Length > 0 ) md.AppendLine( $"**Debug build: {unoptimised}**" );
		md.AppendLine();
		md.AppendLine( "```" );
		foreach ( var line in results ) md.AppendLine( line );
		md.AppendLine( "```" );
		md.AppendLine();
		md.AppendLine( "Per pass: draws / triangles / materials / pipeline binds / set binds" );
		md.AppendLine();
		md.AppendLine( $"| View | Variant | {string.Join( " | ", MapRenderer.PassNames )} |" );
		md.AppendLine( $"|---|---|{string.Concat( MapRenderer.PassNames.Select( _ => "---|" ) )}" );
		md.Append( passTable );
		File.WriteAllText( Path.Combine( output, "map.md" ), md.ToString() );
		File.WriteAllText( Path.Combine( output, "map.csv" ), csv.ToString() );
		Console.WriteLine( $"Results in {output}; validation errors {gpu.ValidationErrors}" );
		return gpu.ValidationErrors > 0 || errors > 0 ? 1 : 0;
	}

	/// <summary>Handle the window's events: closing it, or Escape, ends the run.</summary>
	internal static void Pump( SdlWindow window )
	{
		while ( Sdl.PollEvent( out var e ) )
		{
			if ( e is { Kind: SdlEventKind.KeyDown, Key: SdlKey.Escape } ) window.RequestClose();
		}
	}

	static double Percentile( IEnumerable<double> values, double p )
	{
		var sorted = values.OrderBy( x => x ).ToArray();
		return sorted.Length == 0 ? 0 : sorted[Math.Clamp( (int)(p * (sorted.Length - 1) + 0.5), 0, sorted.Length - 1 )];
	}

	/// <summary>"x,y,z" as a vector.</summary>
	static V3 Vector( string text )
	{
		var v = text.Split( ',' ).Select( x => float.Parse( x, System.Globalization.CultureInfo.InvariantCulture ) ).ToArray();
		return new V3( v[0], v[1], v[2] );
	}

	/// <summary>Our assemblies in this process that were built without optimisation, or empty.</summary>
	static string Unoptimised()
	{
		var names = new List<string>();
		foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies().Append( typeof( GpuDevice ).Assembly ).Append( typeof( ContentFiles ).Assembly ).Distinct() )
		{
			var name = assembly.GetName().Name ?? "";
			if ( !name.StartsWith( "Borealis", StringComparison.Ordinal ) && name != "borealislab" ) continue;
			if ( assembly.GetCustomAttributes( typeof( DebuggableAttribute ), false ) is [DebuggableAttribute { IsJITOptimizerDisabled: true }, ..] ) names.Add( name );
		}

		return string.Join( ", ", names.Order() );
	}

	/// <summary>The folder of an installed s&amp;box workshop item whose metadata names <paramref name="ident"/>, or null.</summary>
	internal static string FindPackage( string ident )
	{
		foreach ( var content in WorkshopContent.Folders() )
		{
			if ( !Directory.Exists( content ) ) continue;
			foreach ( var item in Directory.GetDirectories( content ) )
			{
				var metadata = Path.Combine( item, "metadata.json" );
				if ( !File.Exists( metadata ) ) continue;
				if ( Regex.IsMatch( File.ReadAllText( metadata ), $@"""PackageIdent""\s*:\s*""{Regex.Escape( ident )}""", RegexOptions.IgnoreCase ) ) return item;
			}
		}

		return null;
	}
}
