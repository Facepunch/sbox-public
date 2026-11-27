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

	/// <summary>
	/// Whether the post processing drawn this frame reads its motion (<see cref="PostProcessLayers.ReadsMotionVectors"/>).
	/// </summary>
	bool ReadsMotionVectors() => PostProcessSystem.EnablePostProcess && DrawnPostProcess.ReadsMotionVectors;

	/// <summary>
	/// Run it, under the conditions the stage would (<c>PostProcessSystem.OnRenderStage</c>). On the render thread.
	/// </summary>
	void RenderEarlyPostProcess( Stage stage, PostProcessQueue queue )
	{
		if ( !PostProcessSystem.EnablePostProcess || !Graphics.PostProcessEnabled ) return;
		DrawnPostProcess.RenderEarlyPostProcess( stage, queue );
	}

	/// <summary>
	/// Whether the tonemap is drawn by the output copy this frame (<see cref="PostProcessLayers.DeferTonemap"/>): when its
	/// stage would draw it and nothing else draws in its stage after it (no command lists there). On the render thread, in the
	/// frame's block, before the Tonemapping stage.
	/// </summary>
	bool DeferTonemap( bool allowed )
		=> DrawnPostProcess.DeferTonemap( allowed && PostProcessSystem.UberOutput && PostProcessSystem.DrawsStage( Stage.Tonemapping ) && !HasCommandLists( Stage.Tonemapping ) );

	/// <summary>
	/// What the output copy draws with the uber post shader (<see cref="PostProcessLayers.OutputPostProcess"/>), the first run
	/// after post processing with it when its stage would draw it. On the render thread, in the output's block.
	/// </summary>
	UberPostEffect OutputPostProcess( bool withRun, RenderAttributes into )
		=> DrawnPostProcess.OutputPostProcess( withRun && PostProcessSystem.UberOutput && PostProcessSystem.DrawsStage( Stage.AfterPostProcess ), into );

	void EndOutputPostProcess() => DrawnPostProcess.EndOutputPostProcess();

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
