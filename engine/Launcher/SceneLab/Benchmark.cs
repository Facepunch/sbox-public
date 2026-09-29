using System.Text;

namespace Sandbox.SceneLab;

/// <summary>
/// <c>-benchmark</c>: every scene rendered by the managed renderer, then by native, with vsync off -
/// a warm-up, then a measured run - and a table of what each cost. <c>-benchmark "10k Boxes;50k Mixed"</c> (semicolons, because scene names have commas)
/// picks the scenes.
/// </summary>
internal sealed class Benchmark
{
	static readonly string[] DefaultScenes = ["256 Lights", "10k Boxes", "50k Mixed", "10k Boxes, 256 Lights", "10k Boxes, 64 Shadowed Lights", "10k Boxes, 64 Still Shadowed Lights"];

	// Long enough for the JIT to have tiered the hot paths up - measuring tier 0 code made a
	// run look several times slower than the same scene measured later
	const int WarmupFrames = 120;
	const double WarmupSeconds = 3;

	// After loading a scene, longer, counted from its first ready frame: pipelines and streamed textures are still arriving
	// for a while, and a map's first run measured them - Construct's managed run had p99 frames of 15 ms that the same
	// renderer, measured after native, didn't
	const double LoadWarmupSeconds = 10;
	double warmupSeconds;
	readonly int measureFrames = Math.Max( 60, Args.Int( "-benchmark-frames", 600 ) );

	readonly record struct Run( SceneLabScene Scene, SceneLabWindow.RendererKind Renderer, bool Prepass = true, bool Target = false )
	{
		public string Name => Renderer != SceneLabWindow.RendererKind.Managed ? Renderer.ToString()
			: Target ? $"Managed, texture{(Args.Int( "-msaa", 1 ) > 1 ? $" {Args.Int( "-msaa", 1 )}x MSAA" : "")}"
			: Prepass ? "Managed" : "Managed, no prepass";
	}
	readonly record struct Result( Run Run, int Objects, FrameTimer.Summary Summary, int Gen0, int Gen1, int Gen2, Breakdown Managed, NativeCounts Native, List<(string Path, double Ms)> Gpu );

	// -gpuprofile: GPU time by layer, from the engine's GPU profiler - native's layers, and the managed renderer's layers
	// through its perf markers. Collecting it allocates, so the other columns of a profiled run aren't comparable.
	readonly bool gpuProfile = Args.Has( "-gpuprofile" );
	readonly Dictionary<string, double> gpuSums = new();

	void AccumulateGpu()
	{
		if ( !gpuProfile ) return;

		NativeEngine.CSceneSystem.RefreshGpuTimestampSnapshot();
		var count = NativeEngine.CSceneSystem.GetGpuTimestampCount();

		// Each scope's path through its parents (GpuProfilerStats reads the same rows)
		var paths = new string[count];
		string PathOf( int i )
		{
			if ( paths[i] is not null ) return paths[i];
			var name = NativeEngine.CSceneSystem.GetGpuTimestampName( i );
			var parent = NativeEngine.CSceneSystem.GetGpuTimestampParent( i );
			return paths[i] = parent >= 0 && parent < count && parent != i ? $"{PathOf( parent )}/{name}" : name;
		}

		for ( int i = 0; i < count; i++ )
		{
			if ( !NativeEngine.CSceneSystem.GetGpuTimestampMeasured( i ) ) continue;
			var path = PathOf( i );
			if ( string.IsNullOrEmpty( path ) ) continue;
			gpuSums[path] = gpuSums.GetValueOrDefault( path ) + NativeEngine.CSceneSystem.GetGpuTimestampDuration( i );
		}
	}

	List<(string, double)> AverageGpu( int n )
	{
		var list = new List<(string Path, double Ms)>();
		foreach ( var (path, sum) in gpuSums ) list.Add( (path, sum / n) );
		list.Sort( ( a, b ) => b.Ms.CompareTo( a.Ms ) );
		return list;
	}

	/// <summary>
	/// Native's scenesystem counters per frame (<c>SceneSystemPerFrameStats_t</c>), averaged over a native run: what its views drew.
	/// </summary>
	readonly record struct NativeCounts( double DrawCalls, double BatchDraws, double ObjectsTested, double ObjectsPassing, double MaterialChanges, double MaterialChangesShadow, double Views, double DisplayLists, double MaterialSets, double SimilarSets, double TextureOnlySets, double MaterialComputes );

	double[] nativeSums = new double[12];

	void AccumulateNative()
	{
		var s = NativeEngine.CSceneSystem.GetPerFrameStats();
		nativeSums[0] += s.m_nDrawCalls; nativeSums[1] += s.m_nRenderBatchDraws; nativeSums[2] += s.m_nNumObjectsTested; nativeSums[3] += s.m_nNumObjectsPassingCullCheck;
		nativeSums[4] += s.m_nMaterialChangesNonShadow; nativeSums[5] += s.m_nMaterialChangesShadow; nativeSums[6] += s.m_nNumViewsRendered; nativeSums[7] += s.m_nNumDisplayListsSubmitted;
		nativeSums[8] += s.m_nNumMaterialSet; nativeSums[9] += s.m_nNumSimilarMaterialSet; nativeSums[10] += s.m_nNumTextureOnlyMaterialSet; nativeSums[11] += s.m_nNumMaterialCompute;
	}

	NativeCounts AverageNative( int n ) => new( nativeSums[0] / n, nativeSums[1] / n, nativeSums[2] / n, nativeSums[3] / n, nativeSums[4] / n, nativeSums[5] / n, nativeSums[6] / n, nativeSums[7] / n, nativeSums[8] / n, nativeSums[9] / n, nativeSums[10] / n, nativeSums[11] / n );

	/// <summary>
	/// The managed renderer's main thread per frame, section by section (<c>RenderStats</c>), averaged over a run.
	/// </summary>
	readonly record struct Breakdown( double Collect, double Prepare, double Setup, double Record, double Workers, double Stages, double Submit, double Draws, double ShadowDraws, double Wait, double Sync );

	double[] sums = new double[11];

	void Accumulate( SceneRenderer.RenderStats s )
	{
		sums[0] += s.CollectMs; sums[1] += s.PrepareMs; sums[2] += s.SetupMs; sums[3] += s.RecordMs; sums[4] += s.WorkerRecordMs;
		sums[5] += s.StagesMs; sums[6] += s.SubmitMs; sums[7] += s.Draws + s.DepthDraws; sums[8] += s.ShadowDraws; sums[9] += s.RecordWaitMs; sums[10] += s.SyncMs;
	}

	Breakdown Average( int n ) => new( sums[0] / n, sums[1] / n, sums[2] / n, sums[3] / n, sums[4] / n, sums[5] / n, sums[6] / n, sums[7] / n, sums[8] / n, sums[9] / n, sums[10] / n );

	readonly List<Run> runs = new();
	readonly List<Result> results = new();
	int index = -1;
	int frames;
	int measured = -1;
	System.Diagnostics.Stopwatch runTime = new();
	int gen0, gen1, gen2;

	public Benchmark( string sceneNames )
	{
		var names = string.IsNullOrWhiteSpace( sceneNames ) || sceneNames.StartsWith( '-' )
			? DefaultScenes
			: sceneNames.Split( ';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );

		if ( gpuProfile ) Sandbox.Diagnostics.GpuProfilerStats.Enabled = true;

		foreach ( var name in names )
		{
			var scene = SceneLabScene.Presets.FirstOrDefault( x => x.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) );
			if ( scene is null )
			{
				Log.Warning( $"Benchmark: no scene called \"{name}\"" );
				continue;
			}

			runs.Add( new Run( scene, SceneLabWindow.RendererKind.Managed ) );
			// Without a prepass: the lab's own renderer only, since a GameObject scene renders through the bridge's
			if ( !scene.IsGame ) runs.Add( new Run( scene, SceneLabWindow.RendererKind.Managed, Prepass: false ) );

			// Into a ViewTarget, the way a camera with a render target draws, when -target asks for it
			if ( Args.Has( "-target" ) ) runs.Add( new Run( scene, SceneLabWindow.RendererKind.Managed, Target: true ) );
			runs.Add( new Run( scene, SceneLabWindow.RendererKind.Native ) );
		}
	}

	/// <summary>
	/// Called after every rendered frame. Returns true when every run is done.
	/// </summary>
	public bool Frame( SceneLabWindow window, FrameTimer timer )
	{
		if ( index < 0 ) return !Start( window, timer, 0 );

		// The warm-up counts from the scene's first ready frame: a game scene's frames only come here once it's loaded
		if ( ++frames == 1 ) runTime.Restart();

		if ( measured < 0 )
		{
			if ( frames < WarmupFrames || runTime.Elapsed.TotalSeconds < warmupSeconds ) return false;

			timer.Clear();
			gen0 = GC.CollectionCount( 0 );
			gen1 = GC.CollectionCount( 1 );
			gen2 = GC.CollectionCount( 2 );
			measured = 0;
			Array.Clear( sums );
			Array.Clear( nativeSums );
			gpuSums.Clear();
			return false;
		}

		if ( runs[index].Renderer == SceneLabWindow.RendererKind.Managed ) Accumulate( window.ManagedStats );
		else AccumulateNative();
		AccumulateGpu();
		if ( ++measured < measureFrames ) return false;

		var result = new Result( runs[index], window.ObjectCount, timer.Summarise(),
			GC.CollectionCount( 0 ) - gen0, GC.CollectionCount( 1 ) - gen1, GC.CollectionCount( 2 ) - gen2, Average( measured ), AverageNative( measured ), AverageGpu( measured ) );
		results.Add( result );
		Log.Info( $"Benchmark: {result.Run.Scene.Name} / {result.Run.Name}: {result.Summary}, GCs {result.Gen0}/{result.Gen1}/{result.Gen2}" );

		if ( Start( window, timer, index + 1 ) ) return false;

		Write();
		return true;
	}

	bool Start( SceneLabWindow window, FrameTimer timer, int next )
	{
		index = next;
		if ( index >= runs.Count ) return false;

		var run = runs[index];
		// The first run's scene may have been loaded by the window already
		warmupSeconds = next == 0 ? LoadWarmupSeconds : WarmupSeconds;
		if ( window.Scene != run.Scene )
		{
			window.Load( run.Scene );
			warmupSeconds = LoadWarmupSeconds;
		}

		window.Renderer = run.Renderer;
		window.DepthPrepass = run.Prepass;
		window.RenderToTexture = run.Target;

		frames = 0;
		measured = -1;
		runTime.Restart();
		timer.Clear();
		return true;
	}

	/// <summary>
	/// A markdown table and a CSV in screenshots/scenelab.
	/// </summary>
	void Write()
	{
		var md = new StringBuilder();
		md.AppendLine( $"Scene Lab benchmark, {DateTime.Now:yyyy-MM-dd HH:mm}, {measureFrames} measured frames per run, vsync off" );
		md.AppendLine();
		md.AppendLine( "| Scene | Objects | Renderer | FPS | Frame ms | p95 ms | p99 ms | Render ms (main thread) | GPU ms | Alloc B/frame | GCs (0/1/2) |" );
		md.AppendLine( "|---|---|---|---|---|---|---|---|---|---|---|" );

		var csv = new StringBuilder();
		csv.AppendLine( "scene,objects,renderer,fps,frame_ms,p95_ms,p99_ms,render_ms,gpu_ms,alloc_bytes,gen0,gen1,gen2" );

		foreach ( var r in results )
		{
			var s = r.Summary;
			md.AppendLine( $"| {r.Run.Scene.Name} | {r.Objects} | {r.Run.Name} | {s.Fps:0} | {s.FrameMs:0.000} | {s.FrameP95Ms:0.000} | {s.FrameP99Ms:0.000} | {s.RenderMs:0.000} | {s.GpuMs:0.000} | {s.AllocBytes:0} | {r.Gen0}/{r.Gen1}/{r.Gen2} |" );
			csv.AppendLine( FormattableString.Invariant( $"\"{r.Run.Scene.Name}\",{r.Objects},{r.Run.Name},{s.Fps:0.0},{s.FrameMs:0.000},{s.FrameP95Ms:0.000},{s.FrameP99Ms:0.000},{s.RenderMs:0.000},{s.GpuMs:0.000},{s.AllocBytes:0},{r.Gen0},{r.Gen1},{r.Gen2}" ) );
		}

		md.AppendLine();
		md.AppendLine( "Managed main thread by section, ms per frame:" );
		md.AppendLine();
		md.AppendLine( "| Scene | Renderer | Bridge sync | Collect | Prepare | Setup | Record (wall) | Record wait | Workers (sum) | Stages | Submit | Draws | Shadow draws |" );
		md.AppendLine( "|---|---|---|---|---|---|---|---|---|---|---|---|---|" );
		foreach ( var r in results )
		{
			if ( r.Run.Renderer != SceneLabWindow.RendererKind.Managed ) continue;
			var b = r.Managed;
			md.AppendLine( $"| {r.Run.Scene.Name} | {r.Run.Name} | {b.Sync:0.000} | {b.Collect:0.000} | {b.Prepare:0.000} | {b.Setup:0.000} | {b.Record:0.000} | {b.Wait:0.000} | {b.Workers:0.000} | {b.Stages:0.000} | {b.Submit:0.000} | {b.Draws:0} | {b.ShadowDraws:0} |" );
		}

		md.AppendLine();
		md.AppendLine( "Native scenesystem counters, per frame:" );
		md.AppendLine();
		md.AppendLine( "| Scene | Draw calls | Batch draws | Objects tested | Objects passing | Material changes | Shadow material changes | Views | Display lists | Material sets | Similar sets | Texture-only sets | Material computes |" );
		md.AppendLine( "|---|---|---|---|---|---|---|---|---|---|---|---|---|" );
		foreach ( var r in results )
		{
			if ( r.Run.Renderer != SceneLabWindow.RendererKind.Native ) continue;
			var n = r.Native;
			md.AppendLine( $"| {r.Run.Scene.Name} | {n.DrawCalls:0} | {n.BatchDraws:0} | {n.ObjectsTested:0} | {n.ObjectsPassing:0} | {n.MaterialChanges:0} | {n.MaterialChangesShadow:0} | {n.Views:0.#} | {n.DisplayLists:0.#} | {n.MaterialSets:0} | {n.SimilarSets:0} | {n.TextureOnlySets:0} | {n.MaterialComputes:0} |" );
		}

		foreach ( var r in results )
		{
			if ( r.Gpu.Count == 0 ) continue;

			md.AppendLine();
			md.AppendLine( $"GPU by layer, {r.Run.Scene.Name} / {r.Run.Name}, ms per frame:" );
			md.AppendLine();
			md.AppendLine( "| Layer | GPU ms |" );
			md.AppendLine( "|---|---|" );
			foreach ( var (path, ms) in r.Gpu )
			{
				if ( ms >= 0.005 ) md.AppendLine( $"| {path} | {ms:0.000} |" );
			}
		}

		var folder = SceneLabWindow.OutputFolder();
		System.IO.File.WriteAllText( System.IO.Path.Combine( folder, "benchmark.md" ), md.ToString() );
		System.IO.File.WriteAllText( System.IO.Path.Combine( folder, "benchmark.csv" ), csv.ToString() );
		Log.Info( $"Benchmark: results in {folder}\n{md}" );
	}
}
