using Sandbox.Rendering;

namespace Sandbox;

public sealed partial class CameraComponent : Component, Component.ExecuteInEditor
{
	internal PostProcessLayers PostProcess { get; private set; } = new PostProcessLayers();

	/// <summary>
	/// Whether this camera's command lists may read the depth-normals prepass's G-buffer, for both renderers.
	/// </summary>
	bool WantsDepthNormals() => Scene.RenderAttributes.GetBool( "NeedsDepthNormals" ) || PostProcess.Layers.Count != 0 || HasCustomCommandLists();

	/// <summary>
	/// Whether independent post processing can run ahead of <paramref name="stage"/> on <paramref name="queue"/>.
	/// </summary>
	bool HasEarlyPostProcess( Stage stage, PostProcessQueue queue ) => PostProcessSystem.EnablePostProcess && PostProcess.HasEarlyPostProcess( stage, queue );

	void ScheduleEarlyPostProcess( Stage stage, bool enabled ) => PostProcess.ScheduleEarlyPostProcess( stage, enabled );

	/// <summary>
	/// Run it, under the conditions the stage would (<c>PostProcessSystem.OnRenderStage</c>). On the render thread.
	/// </summary>
	void RenderEarlyPostProcess( Stage stage, PostProcessQueue queue )
	{
		if ( !PostProcessSystem.EnablePostProcess || !Graphics.PostProcessEnabled ) return;
		PostProcess.RenderEarlyPostProcess( stage, queue );
	}

	/// <summary>
	/// Enable or disable post processing for this camera.
	/// </summary>
	[Header( "Post Processing" )]
	[Property]
	public bool EnablePostProcessing { get; set; } = true;

	/// <summary>
	/// If set then we'll trigger post process volumes from this position, instead of the camera's position.
	/// </summary>
	[Property]
	public GameObject PostProcessAnchor { get; set; }
}
