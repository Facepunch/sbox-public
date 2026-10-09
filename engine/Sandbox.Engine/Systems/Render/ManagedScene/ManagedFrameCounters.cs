namespace Sandbox.Rendering;

/// <summary>
/// What the managed scene renderer did in a frame, added up over every camera it rendered - native's scenesystem counters
/// (<c>SceneSystemPerFrameStats_t</c>) don't see its draws, so the frame stats overlay (<c>overlay_frame</c>) reads these.
/// </summary>
internal struct ManagedFrameCounters
{
	/// <summary>Camera frames the managed renderer rendered.</summary>
	public int Renders;

	/// <summary>Objects in the worlds rendered, those the main views drew, and those too small on screen to draw.</summary>
	public int Objects, ObjectsVisible, ObjectsSizeCulled;

	/// <summary>Instanced draws: forward (translucent included), depth prepass, translucent alone, shadow views, and custom objects drawing themselves.</summary>
	public int Draws, DepthDraws, TranslucentDraws, ShadowDraws, CustomDraws;

	/// <summary>Objects drawn, counting every instance of an instanced draw.</summary>
	public int Instances;

	/// <summary>Triangles the forward passes drew.</summary>
	public long Triangles;

	/// <summary>Lights binned for the main views, and shadow views rendered.</summary>
	public int Lights, ShadowViews;

	/// <summary>Main thread time in each phase, in milliseconds, and the worker threads' recording time added up.</summary>
	public double SyncMs, CollectMs, PrepareMs, SetupMs, RecordMs, RecordWaitMs, SubmitMs, WorkerRecordMs;

	/// <summary>Draw calls in every pass.</summary>
	public readonly int TotalDraws => Draws + DepthDraws + ShadowDraws + CustomDraws;

	/// <summary>Main thread time, sync to submit.</summary>
	public readonly double MainThreadMs => SyncMs + CollectMs + PrepareMs + SetupMs + RecordMs + SubmitMs;

	internal void Add( in ManagedFrameCounters other )
	{
		Renders += other.Renders;
		Objects += other.Objects;
		ObjectsVisible += other.ObjectsVisible;
		ObjectsSizeCulled += other.ObjectsSizeCulled;
		Draws += other.Draws;
		DepthDraws += other.DepthDraws;
		TranslucentDraws += other.TranslucentDraws;
		ShadowDraws += other.ShadowDraws;
		CustomDraws += other.CustomDraws;
		Instances += other.Instances;
		Triangles += other.Triangles;
		Lights += other.Lights;
		ShadowViews += other.ShadowViews;
		SyncMs += other.SyncMs;
		CollectMs += other.CollectMs;
		PrepareMs += other.PrepareMs;
		SetupMs += other.SetupMs;
		RecordMs += other.RecordMs;
		RecordWaitMs += other.RecordWaitMs;
		SubmitMs += other.SubmitMs;
		WorkerRecordMs += other.WorkerRecordMs;
	}
}

internal static partial class ManagedSceneRendering
{
	static ManagedFrameCounters thisFrame;

	/// <summary>
	/// What the managed renderer did last frame. <see cref="ManagedFrameCounters.Renders"/> is 0 when every camera rendered
	/// natively.
	/// </summary>
	public static ManagedFrameCounters LastFrame { get; private set; }

	/// <summary>
	/// Add a camera's frame - called by the managed renderer on the main thread, after each frame it renders.
	/// </summary>
	internal static void Report( in ManagedFrameCounters counters ) => thisFrame.Add( counters );

	/// <summary>
	/// Called once a frame, before the overlays draw: what was reported since the last call becomes <see cref="LastFrame"/>.
	/// </summary>
	internal static void EndFrame()
	{
		LastFrame = thisFrame;
		thisFrame = default;
	}
}
