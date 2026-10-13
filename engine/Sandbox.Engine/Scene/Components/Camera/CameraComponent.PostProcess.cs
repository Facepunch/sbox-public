using Sandbox.Rendering;

namespace Sandbox;

public sealed partial class CameraComponent : Component, Component.ExecuteInEditor
{
	internal PostProcessLayers PostProcess { get; private set; } = new PostProcessLayers();

	/// <summary>
	/// Whether this camera's post processing reads the depth-normals prepass's G-buffer (AO, SSR), so the managed scene
	/// renderer draws it.
	/// </summary>
	bool WantsDepthNormals() => PostProcess.NeedsDepthNormals;

	/// <summary>
	/// Whether post processing at <paramref name="stage"/> can run on the async compute queue (<see cref="BasePostProcess.AsyncCompute"/>).
	/// </summary>
	bool HasAsyncCompute( Stage stage ) => PostProcessSystem.EnablePostProcess && PostProcess.HasAsyncCompute( stage );

	/// <summary>
	/// Run it, under the conditions the stage would (<c>PostProcessSystem.OnRenderStage</c>). On the render thread.
	/// </summary>
	void RenderAsyncCompute( Stage stage )
	{
		if ( !PostProcessSystem.EnablePostProcess || !Graphics.PostProcessEnabled ) return;
		PostProcess.RenderAsyncCompute( stage );
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
