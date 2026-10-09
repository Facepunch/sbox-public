namespace Sandbox.SceneRenderer;

/// <summary>
/// A frame step, equivalent to native's <c>ISceneLayer</c>. Ordered in <c>RenderSystem.Layers.cs</c>.
/// Mesh layers can record in parallel; other layers record on the main thread. Empty layers are skipped.
/// </summary>
internal abstract class RenderLayer
{
	/// <summary>
	/// Native layer name, where applicable.
	/// </summary>
	public string Name { get; }

	protected RenderLayer( string name )
	{
		Name = name;
	}

	/// <summary>
	/// Whether this layer can run this frame: it has something to draw, or what it needs exists. Checked on the main
	/// thread before recording. A layer that <see cref="Writes"/> something is then only kept if a later one reads it.
	/// </summary>
	public virtual bool IsNeeded( RenderFrame frame ) => true;

	/// <summary>
	/// What it makes for later layers of the same frame. None for a layer that draws into the frame, which is kept whenever
	/// it's needed; otherwise it's kept only while a later kept layer <see cref="Reads"/> something it makes.
	/// </summary>
	public virtual FrameResources Writes => FrameResources.None;

	/// <summary>
	/// What it reads this frame of what earlier layers make.
	/// </summary>
	public virtual FrameResources Reads( RenderFrame frame ) => FrameResources.None;

	/// <summary>
	/// Whether runs can be split across recording threads.
	/// </summary>
	public virtual bool IsShared => false;

	/// <summary>
	/// Compute only, over what earlier layers made: it may record on the async compute queue (<see cref="RenderContext.AsyncCompute"/>),
	/// running while the <see cref="BesideAsyncCompute"/> layers after it draw. What it needs changed on the graphics queue first
	/// goes in <see cref="HandOff"/>, and back afterwards in <see cref="TakeBack"/>. Recorded on the main thread.
	/// </summary>
	public virtual bool AsyncCompute => false;

	/// <summary>
	/// It touches nothing an <see cref="AsyncCompute"/> layer reads or writes (shadow maps only), so it can draw while they run.
	/// The first layer after them that isn't waits for them.
	/// </summary>
	public virtual bool BesideAsyncCompute => false;

	/// <summary>
	/// An <see cref="AsyncCompute"/> layer's graphics work before it: what it reads made readable by compute, what it writes cleared.
	/// Recorded on the main thread into a graphics context submitted just before it.
	/// </summary>
	public virtual void HandOff( RenderFrame frame, RenderContext rc ) { }

	/// <summary>
	/// An <see cref="AsyncCompute"/> layer's graphics work after it, recorded by the first segment that waits for it, maybe on a
	/// worker thread: barriers through <paramref name="rc"/> only.
	/// </summary>
	public virtual void TakeBack( RenderFrame frame, RenderContext rc ) { }

	/// <summary>
	/// Add this layer or its child frame's layers before setup. Called only when needed.
	/// </summary>
	public virtual void AddTo( FrameRecorder recorder, RenderFrame frame ) => recorder.AddLayer( frame, this );

	/// <summary>
	/// Add views to split into recording work for a shared layer.
	/// </summary>
	public virtual void AddWork( FrameRecorder recorder, RenderFrame frame ) { }

	/// <summary>
	/// Record frame-wide prerequisites after setup, before any segment.
	/// </summary>
	public virtual void BeforeSegments( RenderFrame frame, RenderContext frameContext ) { }

	/// <summary>
	/// Record on the main thread with the frame's view constants. Bind targets with <see cref="RenderFrame.BeginPass"/>;
	/// pass combos are reset afterward.
	/// </summary>
	public virtual void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats ) { }

	/// <summary>
	/// Release owned native resources.
	/// </summary>
	public virtual void Dispose() { }

	public override string ToString() => Name;
}
