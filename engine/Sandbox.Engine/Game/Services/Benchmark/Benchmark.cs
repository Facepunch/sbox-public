using Sandbox.Engine;
using Sandbox.Modals;
using System.Threading;

namespace Sandbox.Services;

/// <summary>
/// Allows access to stats for the current game. Stats are defined by the game's author
/// and can be used to track anything from player actions to performance metrics. They are
/// how you submit data to leaderboards.
/// </summary>
public partial class BenchmarkSystem
{
	private Dictionary<string, BenchmarkRecord> results { get; } = new();
	private List<Sampler> samplers;
	private Dictionary<string, double> metrics = new();
	private Allocations.Scope allocations = new Allocations.Scope();

	string testName;
	FastTimer timer;

	public BenchmarkSystem()
	{

	}

	/// <summary>
	/// Called to start a benchmark
	/// </summary>
	public void Start( string name )
	{
		BenchmarkOrchestrator.EnsureTracyCaptureStarted();

		testName = name;

		metrics.Clear();
		allocations.Clear();

		samplers = new()
		{
			new ("Fps", () => 1.0f / Time.Delta ),

			// PerformanceStats
			new ("ApproximateProcessMemoryUsage",       () => PerformanceStats.ApproximateProcessMemoryUsage / (1024.0f * 1024.0f * 1024.0f) ),
			new ("BytesAllocated",                      () => PerformanceStats.BytesAllocated),
			new ("FrameTimeMs",                         () => PerformanceStats.FrameTime * 1000.0f ),
			new ("GpuFrametime",                        () => PerformanceStats.GpuFrametime, () => PerformanceStats.GpuFrameNumber ),
			new ("Gen0Collections",                     () => PerformanceStats.Gen0Collections ),
			new ("Gen1Collections",                     () => PerformanceStats.Gen1Collections ),
			new ("Gen2Collections",                     () => PerformanceStats.Gen2Collections ),
			new ("GcPauseMs",                           () => TimeSpan.FromTicks( PerformanceStats.GcPause ).Milliseconds ), // Convert to ms so it matches the other timings
			new ("Exceptions",                          () => PerformanceStats.Exceptions ),

			// SceneStats
			new( "ObjectsRendered",             () => FrameStats._current.ObjectsRendered ),
			new( "TrianglesRendered",           () => FrameStats._current.TrianglesRendered ),
			new( "DrawCalls",                   () => FrameStats._current.DrawCalls ),
			new( "MaterialChanges",             () => FrameStats._current.MaterialChanges ),
			new( "DisplayLists",                () => FrameStats._current.DisplayLists ),
			new( "SceneViewsRendered",          () => FrameStats._current.SceneViewsRendered ),
			new( "RenderTargetResolves",        () => FrameStats._current.RenderTargetResolves ),
			new( "ObjectsCulledByVis",          () => FrameStats._current.ObjectsCulledByVis ),
			new( "ObjectsCulledByScreenSize",   () => FrameStats._current.ObjectsCulledByScreenSize ),
			new( "ObjectsCulledByFade",         () => FrameStats._current.ObjectsCulledByFade ),
			new( "ObjectsFading",               () => FrameStats._current.ObjectsFading ),
			new( "ShadowedLightsInView",        () => FrameStats._current.ShadowedLightsInView ),
			new( "UnshadowedLightsInView",      () => FrameStats._current.UnshadowedLightsInView ),
			new( "ShadowMaps",                  () => FrameStats._current.ShadowMaps ),
		};

		foreach ( var e in Sandbox.Diagnostics.PerformanceStats.Timings.GetMain() )
		{
			samplers.Add( new( e.Name, () => e.AverageMs( 1 ) ) );
		}

		timer.Start();
		allocations.Start();
		IGameInstanceDll.Current.ResetSceneListenerMetrics();
	}

	/// <summary>
	/// Set a custom metric, like load time, shutdown time etc
	/// </summary>
	/// <param name="name"></param>
	/// <param name="metric"></param>
	public void SetMetric( string name, double metric )
	{
		metrics[name] = metric;
	}

	/// <summary>
	/// Called to close a benchmark off
	/// </summary>
	public void Finish()
	{
		var elapsedSeconds = timer.ElapsedSeconds;
		allocations.Stop();

		var benchmarkResult = new BenchmarkRecord();
		benchmarkResult.Name = testName;
		benchmarkResult.Duration = elapsedSeconds;
		benchmarkResult.Data = samplers.ToDictionary( x => x.Name, x => (object)x.GetResults() );

		benchmarkResult.Data["Alloc"] = allocations.Entries.OrderByDescending( x => x.TotalBytes ).Take( 100 ).ToDictionary( x => x.Name, x => new { x.Count, Size = x.TotalBytes } );
		benchmarkResult.Data["Listeners"] = IGameInstanceDll.Current.GetSceneListenerMetrics();

		foreach ( var m in metrics )
		{
			benchmarkResult.Data[m.Key] = m.Value;
		}

		results[testName] = benchmarkResult;
	}

	/// <summary>
	/// Should be called in update every frame
	/// </summary>
	public void Sample()
	{
		if ( samplers is null )
			return;

		foreach ( var sampler in samplers )
		{
			sampler.Update();
		}
	}

	/// <summary>
	/// Finish this benchmark session and send it off to the backend
	/// </summary>
	public async Task<Guid> SendAsync( CancellationToken token = default )
	{
		var value = results.Values.ToArray();
		var summaries = value.Select( BuildSummary ).ToArray();
		results.Clear();
		allocations?.Stop();

		var batchId = await Api.Benchmarks.Post( value, token );
		BenchmarkOrchestrator.LastBatchId = batchId;
		BenchmarkOrchestrator.Summaries.AddRange( summaries );
		return batchId;
	}

	private static BenchmarkTestSummary BuildSummary( BenchmarkRecord r )
	{
		static double Get( BenchmarkRecord rec, string key, Func<Sampler.Result, double> sel )
			=> rec.Data.TryGetValue( key, out var v ) && v is Sampler.Result sr ? sel( sr ) : 0;

		return new BenchmarkTestSummary
		{
			Name = r.Name,
			DurationSeconds = r.Duration,
			AvgFps = Get( r, "Fps", x => x.Avg ),
			AvgFrameTimeMs = Get( r, "FrameTimeMs", x => x.Avg ),
			OnePercentLowMs = Get( r, "FrameTimeMs", x => x.P99 ),
			AvgGpuFrametimeMs = Get( r, "GpuFrametime", x => x.Avg ),
			Stuttering = Get( r, "FrameTimeMs", x => x.Stuttering ),
		};
	}
}

