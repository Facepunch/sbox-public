using NativeEngine;

namespace Sandbox.Rendering;

/// <summary>
/// Borealis's settings from s&amp;box's convars, its profiler zones into Tracy, and its frame plans and counters into the dev tools.
/// </summary>
internal sealed class SboxRenderHost : RenderHost
{
	/// <summary>
	/// The process's Borealis.Gpu device under <c>-borealisgpu</c>: its renderer services, which the engine's drawing on that
	/// device shares. Null in a process drawing on Source 2's, where nothing renders through Borealis.
	/// </summary>
	public override GpuDevice Device => RenderBackend.Default is BorealisGpuRenderBackend backend ? backend.Services : null;

	// Async compute where the device has a compute queue (BackendDevice.SupportsAsyncCompute) and r_managed_async_compute is on
	public override bool AsyncCompute => ManagedSceneRendering.AsyncCompute && Device is { SupportsAsyncCompute: true };

	public override ulong FrameCount => Application.FrameCount;

	public override Borealis.Shadows.ShadowSettings Shadows { get; } = new SboxShadowSettings();

	public override bool ContactShadows => Sandbox.Rendering.ContactShadows.Enabled;

	public override bool OcclusionCulling => ManagedSceneRendering.Occlusion;

	public override FragmentOcclusion FragmentOcclusion => (FragmentOcclusion)ManagedSceneRendering.OcclusionAggregates;

	public override Borealis.OcclusionDebugView OcclusionDebugView => (Borealis.OcclusionDebugView)OcclusionDebug.View;

	public override Borealis.OcclusionEarlyCull OcclusionEarlyCull => (Borealis.OcclusionEarlyCull)OcclusionDebug.EarlyCull;

	public override bool OcclusionFrozen => OcclusionDebug.Freeze;

	public override int OcclusionFreezes => OcclusionDebug.Freezes;

	public override unsafe bool ProfilesZones => PerformanceTrace.__N.PerformanceTrace_BeginEvent != null;

	public override void BeginZone( IntPtr name, uint color ) => PerformanceTrace.BeginEvent( name, null, color, null, 0, null );

	public override void EndZone() => PerformanceTrace.EndEvent();

	public override Borealis.DebugViews DebugViews => ManagedDebugViews.Views;

	public override void PublishDebugViews( DebugViewReport report ) => ManagedDebugViews.Latest = report;

	public override bool WantsFramePlan => ManagedFramePlan.Due;

	public override long Submitting( long ticks, RenderView view ) => ManagedFramePlan.NoteHandedOver( ticks, view );

	public override void PublishFramePlan( FramePlan plan )
	{
		var published = new ManagedFramePlan
		{
			Camera = plan.Camera,
			View = plan.View,
			Parallel = plan.Parallel,
			Resources = plan.Resources,
			StartTicks = plan.StartTicks,
			SubmitTicks = plan.SubmitTicks,
			SubmitTag = plan.SubmitTag,
			MainThread = plan.MainThread,
			CollectMs = plan.CollectMs,
			PrepareMs = plan.PrepareMs,
			SetupMs = plan.SetupMs,
			RecordMs = plan.RecordMs,
			RecordWaitMs = plan.RecordWaitMs,
			SubmitMs = plan.SubmitMs,
			WorkerMs = plan.WorkerMs,
		};

		foreach ( var frame in plan.Frames )
		{
			var copy = new ManagedFramePlan.Frame { Name = frame.Name, Skybox = frame.Skybox };
			foreach ( var layer in frame.Layers )
			{
				copy.Layers.Add( new ManagedFramePlan.Layer
				{
					Name = layer.Name,
					State = (ManagedFramePlan.LayerState)layer.State,
					Makes = layer.Makes,
					Reads = layer.Reads,
					NotMade = layer.NotMade,
					Views = layer.Views,
					Runs = layer.Runs,
					Draws = layer.Draws,
					Segment = layer.Segment,
					Order = layer.Order,
					ChildFrame = layer.ChildFrame,
					CpuMs = layer.CpuMs,
					Pieces = layer.Pieces,
					Recorded = layer.Recorded,
					Binds = layer.Binds,
					SimilarBinds = layer.SimilarBinds,
					Triangles = layer.Triangles,
				} );
			}

			published.Frames.Add( copy );
		}

		foreach ( var segment in plan.Segments )
		{
			published.Segments.Add( new ManagedFramePlan.Segment
			{
				Worker = segment.Worker,
				Async = segment.Async,
				Draws = segment.Draws,
				Ms = segment.Ms,
				First = segment.First,
				StartTicks = segment.StartTicks,
				EndTicks = segment.EndTicks,
				Thread = segment.Thread,
			} );
		}

		published.Publish();
	}

	// Native's counters leave out Borealis's draws; these go into the frame stats beside them
	public override void Rendered( in RenderStats stats ) => ManagedSceneRendering.Report( ManagedFrameCounters.From( stats ) );
}
