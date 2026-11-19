namespace Sandbox.Rendering.Bridge;

/// <summary>
/// A camera's stages inside Borealis's frame on a Borealis.Gpu device: the ones before the scene is finished
/// (<c>AfterDepthPrepass</c>, so ambient occlusion; <c>AfterOpaque</c>; <c>AfterSkybox</c>) and its screen-space
/// reflections, drawn into the frame's own list at their point of it (<see cref="BorealisGpuFrame.RunStage"/>). The rest
/// run over the finished frame (<see cref="SceneBridge"/>'s camera frame on Borealis.Gpu), so here they do nothing.
/// </summary>
internal sealed class GpuCameraStages : IRenderStages
{
	/// <summary>The camera whose stages these are, this frame.</summary>
	public SceneCamera Camera { get; set; }

	/// <summary>The render system drawing the frame, whose view constants the stages draw with.</summary>
	public RenderSystem System { get; set; }

	/// <summary>The frame's handover to the engine's drawing.</summary>
	public BorealisGpuFrame Frame { get; set; }

	/// <summary>What the stages' drawing asks about the frame.</summary>
	public BorealisGpuView View { get; set; }

	/// <summary>Run a stage of the camera's, logging what throws.</summary>
	public Action<SceneCamera, Stage> Run { get; set; }

	// The frame's pipeline textures start empty at its first stage: the reflections' trace when it runs, before the
	// AfterDepthPrepass stage, which starts them otherwise. The history after opaque ends a frame that traced
	bool traced;

	public void Render( RenderStage stage, in StageTarget target )
	{
		if ( stage is not (RenderStage.AfterDepthPrepass or RenderStage.AfterOpaque or RenderStage.AfterSkybox) ) return;
		if ( Camera is null || Frame is null || View is null ) return;

		var layer = View.LayerType;
		var outer = (drawing, drawingStage);
		(drawing, drawingStage) = (Camera, (Stage)stage);
		View.LayerType = stage == RenderStage.AfterDepthPrepass ? SceneLayerType.DepthPrepass : SceneLayerType.Opaque;
		try
		{
			Frame.RunStage( target, System.Frame.Constants, View, first: stage == RenderStage.AfterDepthPrepass && !traced, drawStage ??= DrawStage );
		}
		finally
		{
			View.LayerType = layer;
			(drawing, drawingStage) = outer;
		}
	}

	// The stage being drawn, which the one delegate the frame runs draws, so a stage allocates nothing
	SceneCamera drawing;
	Stage drawingStage;
	Action drawStage;

	void DrawStage() => Run( drawing, drawingStage );

	// The reflections' trace and history capture as delegates, made once per reflections pass
	ScreenSpaceReflectionsPass reflectionsDrawn;
	Action trace, captureHistory;

	/// <summary>
	/// A top-level camera's frame with screen-space reflections on (<c>r_ssr_downsample_ratio</c>): child views, reflections
	/// and cube faces render without stages here.
	/// </summary>
	public bool HasScreenSpaceReflections => Camera?.ScreenSpaceReflections is not null;

	/// <summary>
	/// Trace the camera's reflections from the frame's depth chain and last frame's normals and roughness
	/// (<see cref="StageTarget.Normals"/>, which the trace's pass modules read as <c>NormalsGBuffer</c>), or keep the lit
	/// scene after opaque as the next frame's history, in the frame's list.
	/// </summary>
	public void RenderScreenSpaceReflections( bool history, in StageTarget target )
	{
		if ( Camera?.ScreenSpaceReflections is not { } reflections || Frame is null || View is null ) return;

		var layer = View.LayerType;
		View.LayerType = history ? SceneLayerType.Opaque : SceneLayerType.DepthPrepass;
		try
		{
			if ( !ReferenceEquals( reflectionsDrawn, reflections ) ) (reflectionsDrawn, trace, captureHistory) = (reflections, reflections.Trace, reflections.CaptureHistory);
			Frame.RunStage( target, System.Frame.Constants, View, first: !history, history ? captureHistory : trace );
		}
		finally
		{
			View.LayerType = layer;
			traced = !history;
		}
	}
}
