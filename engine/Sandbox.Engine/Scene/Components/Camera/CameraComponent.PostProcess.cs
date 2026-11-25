using Sandbox.Rendering;

namespace Sandbox;

public sealed partial class CameraComponent : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// The layers the game's update builds this frame's post processing into (<see cref="PostProcessSystem"/>).
	/// </summary>
	internal PostProcessLayers PostProcess { get; private set; } = new PostProcessLayers();

	PostProcessLayers _drawnPostProcess;

	/// <summary>
	/// The layers the camera's drawing runs: those built for the frame, taken at the frame's extract
	/// (<see cref="TakePostProcess"/>), so a render thread drawing them while the game builds the next frame's into the
	/// other set reads only its own. A camera never extracted (the editor's, one only rendered by hand) draws what's built.
	/// </summary>
	internal PostProcessLayers DrawnPostProcess => _drawnPostProcess ?? PostProcess;

	/// <summary>
	/// At the frame's extract, on the main thread with nothing drawing: the layers built since the last extract become the
	/// ones drawn, and the next frame builds into the others.
	/// </summary>
	internal void TakePostProcess()
	{
		if ( !PostProcess.Rebuilt ) return;
		PostProcess.Rebuilt = false;
		var drawn = PostProcess;
		PostProcess = _drawnPostProcess ?? new PostProcessLayers();
		_drawnPostProcess = drawn;
	}

	/// <summary>
	/// Whether this camera's command lists may read the depth-normals prepass's G-buffer, for both renderers.
	/// </summary>
	bool WantsDepthNormals() => Scene.RenderAttributes.GetBool( "NeedsDepthNormals" ) || DrawnPostProcess.HasLayers || HasCustomCommandLists();

	/// <summary>
	/// Whether independent post processing can run ahead of <paramref name="stage"/> on <paramref name="queue"/>.
	/// </summary>
	bool HasEarlyPostProcess( Stage stage, PostProcessQueue queue ) => PostProcessSystem.EnablePostProcess && DrawnPostProcess.HasEarlyPostProcess( stage, queue );

	void ScheduleEarlyPostProcess( Stage stage, bool enabled ) => DrawnPostProcess.ScheduleEarlyPostProcess( stage, enabled );

	/// <summary>
	/// Run it, under the conditions the stage would (<c>PostProcessSystem.OnRenderStage</c>). On the render thread.
	/// </summary>
	void RenderEarlyPostProcess( Stage stage, PostProcessQueue queue )
	{
		if ( !PostProcessSystem.EnablePostProcess || !Graphics.PostProcessEnabled ) return;
		DrawnPostProcess.RenderEarlyPostProcess( stage, queue );
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
