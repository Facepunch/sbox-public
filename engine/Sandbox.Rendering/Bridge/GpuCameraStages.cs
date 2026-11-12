namespace Sandbox.Rendering.Bridge;

/// <summary>
/// A camera's stages inside Basalt's frame on a Basalt.Gpu device: the ones before the scene is finished
/// (<c>AfterDepthPrepass</c>, so ambient occlusion; <c>AfterOpaque</c>; <c>AfterSkybox</c>), drawn into the frame's own
/// list at their point of it (<see cref="BasaltGpuFrame.RunStage"/>). The rest run over the finished frame
/// (<see cref="SceneBridge"/>'s camera frame on Basalt.Gpu), so here they do nothing.
/// </summary>
internal sealed class GpuCameraStages : IRenderStages
{
	/// <summary>The camera whose stages these are, this frame.</summary>
	public SceneCamera Camera { get; set; }

	/// <summary>The render system drawing the frame, whose view constants the stages draw with.</summary>
	public RenderSystem System { get; set; }

	/// <summary>The frame's handover to the engine's drawing.</summary>
	public BasaltGpuFrame Frame { get; set; }

	/// <summary>What the stages' drawing asks about the frame.</summary>
	public BasaltGpuView View { get; set; }

	/// <summary>Run a stage of the camera's, logging what throws.</summary>
	public Action<SceneCamera, Stage> Run { get; set; }

	public void Render( RenderStage stage, in StageTarget target )
	{
		if ( stage is not (RenderStage.AfterDepthPrepass or RenderStage.AfterOpaque or RenderStage.AfterSkybox) ) return;
		if ( Camera is null || Frame is null || View is null ) return;

		var camera = Camera;
		var run = Run;
		var layer = View.LayerType;
		View.LayerType = stage == RenderStage.AfterDepthPrepass ? SceneLayerType.DepthPrepass : SceneLayerType.Opaque;
		try
		{
			Frame.RunStage( target, System.Frame.Constants, View, first: stage == RenderStage.AfterDepthPrepass, () => run( camera, (Stage)stage ) );
		}
		finally
		{
			View.LayerType = layer;
		}
	}
}
