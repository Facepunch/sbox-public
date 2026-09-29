namespace Sandbox.SceneRenderer;

/// <summary>
/// Counts and timings from the last render.
/// </summary>
public struct RenderStats
{
	/// <summary>
	/// Objects in the world.
	/// </summary>
	public int Objects;
	/// <summary>
	/// Objects the main view drew - in its frustum and big enough on screen. Lights and probes count.
	/// </summary>
	public int ObjectsVisible;

	/// <summary>
	/// In the frustum, but too small on screen to draw.
	/// </summary>
	public int ObjectsSizeCulled;
	/// <summary>
	/// Forward draws, including instanced runs.
	/// </summary>
	public int Draws;

	/// <summary>
	/// Draws in the depth prepass, counted apart from the forward ones.
	/// </summary>
	public int DepthDraws;

	/// <summary>
	/// Draws in the translucent pass. They're counted in <see cref="Draws"/> too.
	/// </summary>
	public int TranslucentDraws;

	/// <summary>
	/// Custom-object draws.
	/// </summary>
	public int CustomDraws;

	/// <summary>
	/// Objects drawn, counting every instance of an instanced draw.
	/// </summary>
	public int Instances;

	/// <summary>
	/// Triangles the forward pass drew, after LODs.
	/// </summary>
	public long Triangles;

	/// <summary>
	/// Lights binned for the view.
	/// </summary>
	public int Lights;

	/// <summary>
	/// Rendered shadow views and their draw counts.
	/// </summary>
	public int ShadowViews, ShadowDraws;

	/// <summary>
	/// Managed memory each phase allocated on the render thread. Steady state should be zero.
	/// </summary>
	public long CollectAllocBytes, PrepareAllocBytes, DrawAllocBytes;

	/// <summary>
	/// Main thread time in each phase: culling, preparing (shadows included), and recording and submitting.
	/// </summary>
	public double CollectMs, PrepareMs, DrawMs;

	/// <summary>
	/// Draw-phase timings. Recording includes worker waits and camera stages.
	/// </summary>
	public double SetupMs, RecordMs, RecordWaitMs, StagesMs, SubmitMs;

	/// <summary>
	/// Main-thread scene-mirror sync time; zero outside the GameObject bridge.
	/// </summary>
	public double SyncMs;

	/// <summary>
	/// Sum of worker-segment recording times.
	/// </summary>
	public double WorkerRecordMs;

	/// <summary>
	/// Accumulate segment or skybox draw counts.
	/// </summary>
	internal static void AddCounts( ref RenderStats into, in RenderStats from )
	{
		into.Draws += from.Draws;
		into.DepthDraws += from.DepthDraws;
		into.TranslucentDraws += from.TranslucentDraws;
		into.CustomDraws += from.CustomDraws;
		into.Instances += from.Instances;
		into.Triangles += from.Triangles;
		into.ShadowViews += from.ShadowViews;
		into.ShadowDraws += from.ShadowDraws;
		into.Lights = Math.Max( into.Lights, from.Lights );
	}

	/// <summary>
	/// Everything on one line, for logs.
	/// </summary>
	public override readonly string ToString()
		=> $"{ObjectsVisible}/{Objects} visible ({ObjectsSizeCulled} too small), {Draws} draws ({TranslucentDraws} translucent, {Instances} instances, {CustomDraws} custom, {Triangles / 1000000.0:0.0}M tris) + {DepthDraws} depth, {Lights} lights, {ShadowViews} shadow views ({ShadowDraws} draws), collect {CollectMs:0.000}ms, prepare {PrepareMs:0.000}ms, draw {DrawMs:0.000}ms, alloc {CollectAllocBytes}/{PrepareAllocBytes}/{DrawAllocBytes} B";
}
