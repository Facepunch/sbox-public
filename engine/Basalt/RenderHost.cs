namespace Basalt;

/// <summary>
/// What the application running Basalt supplies: its settings, and where frame diagnostics go. The defaults suit an
/// application with no settings UI or profiler. s&amp;box installs one backed by its convars and dev tools (Sandbox.Rendering).
/// </summary>
public class RenderHost
{
	/// <summary>
	/// The host every render system reads. Set it before rendering.
	/// </summary>
	public static RenderHost Current { get; set; } = new();

	/// <summary>
	/// The graphics device to render with. A host that renders has to supply one.
	/// </summary>
	public virtual GpuDevice Device => throw new InvalidOperationException( "This RenderHost has no graphics device: set RenderHost.Current to one that does" );

	/// <summary>
	/// Run the depth chain, contact shadows and AO on the async compute queue, beside SSR and shadow rendering.
	/// </summary>
	public virtual bool AsyncCompute => true;

	/// <summary>
	/// The application's frame. Views rendered in one frame share shadow maps, which re-render at most once a frame. By
	/// default, how many times <see cref="NextFrame"/> has been called.
	/// </summary>
	public virtual ulong FrameCount => frameCount;

	ulong frameCount;

	/// <summary>
	/// Start the next frame, for a host that doesn't count its own (<see cref="FrameCount"/>).
	/// </summary>
	public void NextFrame() => frameCount++;

	/// <summary>
	/// How shadows are budgeted and filtered.
	/// </summary>
	public virtual ShadowSettings Shadows { get; } = new();

	/// <summary>
	/// Screen-space shadows from the sun, on top of its shadow maps.
	/// </summary>
	public virtual bool ContactShadows => true;

	/// <summary>
	/// Skip opaque meshes hidden by the depth prepass.
	/// </summary>
	public virtual bool OcclusionCulling => true;

	/// <summary>
	/// How aggregate fragments are occlusion culled.
	/// </summary>
	public virtual FragmentOcclusion FragmentOcclusion => FragmentOcclusion.Compact;

	/// <summary>
	/// What occlusion culling's debug view draws.
	/// </summary>
	public virtual OcclusionDebugView OcclusionDebugView => OcclusionDebugView.Off;

	/// <summary>
	/// Somewhere for occlusion culling's debug view to go, while <see cref="OcclusionDebugView"/> asks for one. Without one,
	/// there's no debug view.
	/// </summary>
	internal virtual OcclusionDebugOutput CreateOcclusionDebug() => null;

	/// <summary>
	/// How the early occlusion cull runs.
	/// </summary>
	public virtual OcclusionEarlyCull OcclusionEarlyCull => OcclusionEarlyCull.On;

	/// <summary>
	/// Keep culling against the depth and view from when the freeze started.
	/// </summary>
	public virtual bool OcclusionFrozen => false;

	/// <summary>
	/// How many times a freeze has started. A new freeze drops the last one's depth.
	/// </summary>
	public virtual int OcclusionFreezes => 0;

	/// <summary>
	/// Time named GPU scopes for a profiler.
	/// </summary>
	public virtual bool GpuTimings => false;

	/// <summary>
	/// Whether a profiler is recording CPU zones (<see cref="BeginZone"/>).
	/// </summary>
	public virtual bool ProfilesZones => false;

	/// <summary>
	/// Start a CPU profiler zone: a UTF-8 name that lives as long as the process, and an RGBA colour.
	/// </summary>
	public virtual void BeginZone( IntPtr name, uint color ) { }

	/// <summary>
	/// End the zone <see cref="BeginZone"/> started on this thread.
	/// </summary>
	public virtual void EndZone() { }

	/// <summary>
	/// Whether the next frame drawn into a window should describe itself (<see cref="PublishFramePlan"/>).
	/// </summary>
	public virtual bool WantsFramePlan => false;

	/// <summary>
	/// A frame's plan, with its timings, once it's submitted.
	/// </summary>
	public virtual void PublishFramePlan( FramePlan plan ) { }

	/// <summary>
	/// A frame of <paramref name="view"/> is being submitted at <paramref name="ticks"/>. Returns a tag for its submission
	/// to carry to the GPU's timings, or 0.
	/// </summary>
	public virtual long Submitting( long ticks, RenderView view ) => 0;

	/// <summary>
	/// A view has been rendered, with these counts and timings.
	/// </summary>
	public virtual void Rendered( in RenderStats stats ) { }
}
