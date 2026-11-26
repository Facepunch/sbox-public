namespace Borealis;

/// <summary>
/// A frame step, equivalent to native's <c>ISceneLayer</c>. Ordered in <c>RenderSystem.Layers.cs</c>.
/// Mesh layers can record in parallel; other layers record on the main thread unless <see cref="RecordsOffMainThread"/>. Empty layers are skipped.
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
	/// thread before recording. Unused output producers are culled unless they have <see cref="HasSideEffects"/>.
	/// </summary>
	public virtual bool IsNeeded( RenderFrame frame ) => true;

	/// <summary>
	/// What it makes for later layers of the same frame. None for a layer that draws into the frame, which is kept whenever
	/// it's needed; otherwise it's kept only while a later kept layer <see cref="Reads"/> something it makes,
	/// or it declares <see cref="HasSideEffects"/>.
	/// </summary>
	public virtual FrameResources Writes => FrameResources.None;

	/// <summary>
	/// What it reads this frame of what earlier layers make. Untracked access is a scheduling boundary:
	/// override with the complete set of inputs to allow independent compute work to overlap this layer.
	/// </summary>
	public virtual FrameResources Reads( RenderFrame frame ) => FrameResources.Untracked;

	/// <summary>Whether this layer has externally visible work in addition to its declared outputs.</summary>
	public virtual bool HasSideEffects => Writes == FrameResources.None;

	/// <summary>
	/// Whether runs can be split across recording threads.
	/// </summary>
	public virtual bool IsShared => false;

	/// <summary>
	/// At most how many segments a shared layer's draws split into (<see cref="IsShared"/>); its runs that must record on the
	/// main thread still split off theirs.
	/// </summary>
	public virtual int MaxSegments => int.MaxValue;

	/// <summary>
	/// Whether this layer can execute entirely on compute. The scheduler chooses async execution only when
	/// declared resource dependencies leave independent graphics work to overlap. Draw layers stay on graphics.
	/// </summary>
	public virtual bool SupportsAsyncCompute => false;

	/// <summary>
	/// Whether <see cref="Record"/> may run on a worker beside the main thread's segments: it runs no engine callback or
	/// custom object, and reads only what <see cref="BeforeSegments"/> made, the frame's setup left and its own context.
	/// A segment only joins layers that agree.
	/// </summary>
	public virtual bool RecordsOffMainThread => false;

	/// <summary>
	/// A compute layer's graphics preparation: what it reads made readable by compute, what it writes cleared.
	/// Recorded on the main thread into a graphics context submitted before the batch. Only external inputs may be
	/// transitioned here; inputs made by earlier compute layers are made readable by those layers when recorded.
	/// </summary>
	public virtual void HandOff( RenderFrame frame, GpuContext rc ) { }

	/// <summary>
	/// A compute layer's graphics work after it, recorded by the first segment that waits for it, maybe on a
	/// worker thread: barriers through <paramref name="rc"/> only.
	/// </summary>
	public virtual void TakeBack( RenderFrame frame, GpuContext rc ) { }

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
	public virtual void BeforeSegments( RenderFrame frame, GpuContext frameContext ) { }

	/// <summary>
	/// Record with the frame's view constants, on the main thread unless <see cref="RecordsOffMainThread"/>. Bind targets with
	/// <see cref="RenderFrame.BeginPass"/>; pass combos are reset afterward.
	/// </summary>
	public virtual void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats ) { }

	/// <summary>
	/// Release owned native resources.
	/// </summary>
	public virtual void Dispose() { }

	public override string ToString() => Name;
}
