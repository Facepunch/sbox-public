namespace Sandbox.Rendering.Bridge;

/// <summary>
/// A camera's stages inside Borealis's frame on a Borealis.Gpu device: the ones before the scene is finished
/// (<c>AfterDepthPrepass</c>, so ambient occlusion; <c>AfterOpaque</c>; <c>AfterSkybox</c>) and its screen-space
/// reflections, drawn into the frame's own list at their point of it (<see cref="BorealisGpuFrame.RunStage"/>). The rest
/// run over the finished frame (<see cref="SceneBridge"/>'s camera frame on Borealis.Gpu), so here they do nothing.
/// </summary>
internal sealed class GpuCameraStages : IRenderStages, IDisposable
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

	public bool RendersInsideFrame( RenderStage stage )
		=> stage is RenderStage.AfterDepthPrepass or RenderStage.AfterOpaque or RenderStage.AfterSkybox;

	public void Render( RenderStage stage, in StageTarget target )
	{
		if ( !RendersInsideFrame( stage ) ) return;
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

	/// <summary>
	/// The camera's effects that can run ahead of <paramref name="stage"/> on <paramref name="queue"/> (ambient occlusion on
	/// async compute, <see cref="BasePostProcess.EarlyQueue"/>), which Borealis's frame schedules when it overlaps compute.
	/// </summary>
	public bool HasEarlyPostProcess( RenderStage stage, StageQueue queue )
		=> Camera is not null && Frame is not null && View is not null && Camera.HasEarlyPostProcess( (Stage)stage, (PostProcessQueue)queue );

	/// <summary>
	/// The camera has command lists at <paramref name="stage"/> (<see cref="CameraComponent.AddCommandList"/>).
	/// </summary>
	public bool DrawsAt( RenderStage stage )
		=> Camera is not null && Frame is not null && View is not null && Camera.HasCommandLists( (Stage)stage );

	/// <summary>
	/// Run them in the frame's list for that queue, with no frame targets bound: the target's depth and depth chain readable,
	/// as the frame's compute handoff left them. The stage then skips what ran (<c>RanEarly</c>); a frame that doesn't
	/// overlap compute doesn't call this, and the stage runs them in its place.
	/// </summary>
	public void RenderEarlyPostProcess( RenderStage stage, StageQueue queue, in StageTarget target )
	{
		if ( Camera is null || Frame is null || View is null ) return;

		var layer = View.LayerType;
		(earlyStage, earlyQueue) = ((Stage)stage, (PostProcessQueue)queue);
		View.LayerType = SceneLayerType.DepthPrepass;
		try
		{
			Frame.RunStage( target, System.Frame.Constants, View, first: false, drawEarly ??= DrawEarly );
		}
		finally
		{
			View.LayerType = layer;
		}
	}

	Stage earlyStage;
	PostProcessQueue earlyQueue;
	Action drawEarly;

	bool earlyFailed;

	void DrawEarly()
	{
		try
		{
			Camera?.RenderEarlyPostProcess( earlyStage, earlyQueue );
		}
		catch ( Exception e )
		{
			if ( !earlyFailed ) Log.Warning( e, $"Borealis.Gpu: {Camera?.Name}'s early {earlyStage} effects: {e.Message}" );
			earlyFailed = true;
		}
	}

	// These stages belong to one CameraState (one view), so the pass's history does too.
	ScreenSpaceReflectionsPass reflections;
	Action trace, captureHistory;

	/// <summary>Make the pass and its shaders before the view's render job, or release it while SSR is disabled.</summary>
	internal void PrepareScreenSpaceReflections()
	{
		traced = false;
		if ( Camera?.TracesScreenSpaceReflections != true )
		{
			ReleaseScreenSpaceReflections();
			return;
		}

		if ( reflections is not null ) return;
		reflections = new();
		trace = TraceScreenSpaceReflections;
		captureHistory = reflections.CaptureHistory;
	}

	void TraceScreenSpaceReflections()
	{
		var frame = System.Frame;
		// The trace stage doesn't bind colour. Read the actual opaque target's format before using history, not
		// the default format of the depth-only block or the format of the final output.
		var (color, _) = Borealis.Backend.BackendDevice.Unwrap( frame.Output.Target.Color );
		var backend = (BorealisGpuRenderBackend)Graphics.Backend;
		var format = BorealisGpuRenderBackend.ImageFormatOf( backend.Device.InfoOf( color ).Format );
		reflections.Trace( frame.View.Position, frame.View.Rotation, frame.World, format );
	}

	public void Dispose() => ReleaseScreenSpaceReflections();

	void ReleaseScreenSpaceReflections()
	{
		reflections?.Dispose();
		reflections = null;
		trace = null;
		captureHistory = null;
	}

	/// <summary>
	/// A top-level camera's frame with screen-space reflections on (<c>r_ssr_downsample_ratio</c>): child views, reflections
	/// and cube faces render without stages here.
	/// </summary>
	public bool HasScreenSpaceReflections => reflections is not null;

	/// <summary>
	/// Trace the camera's reflections from the frame's depth chain and last frame's normals and roughness
	/// (<see cref="StageTarget.Normals"/>, which the trace's pass modules read as <c>NormalsGBuffer</c>), or keep the lit
	/// scene after opaque as the next frame's history, in the frame's list.
	/// </summary>
	public void RenderScreenSpaceReflections( bool history, in StageTarget target )
	{
		if ( reflections is null || Frame is null || View is null ) return;

		var layer = View.LayerType;
		View.LayerType = history ? SceneLayerType.Opaque : SceneLayerType.DepthPrepass;
		try
		{
			Frame.RunStage( target, System.Frame.Constants, View, first: !history, history ? captureHistory : trace );
		}
		finally
		{
			View.LayerType = layer;
			traced = !history;
		}
	}
}
