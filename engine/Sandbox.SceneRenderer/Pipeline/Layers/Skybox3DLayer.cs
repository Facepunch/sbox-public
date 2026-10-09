namespace Sandbox.SceneRenderer;

/// <summary>
/// Inserts the skybox frame's layers after opaque and its stage (<c>Add3DSkyboxLayers</c>).
/// </summary>
internal sealed class Skybox3DLayer : RenderLayer
{
	public Skybox3DLayer() : base( "3D Skybox" ) { }

	public override bool IsNeeded( RenderFrame frame ) => frame.Skybox is not null;

	public override void AddTo( FrameRecorder recorder, RenderFrame frame ) => recorder.AddFrame( frame.Skybox );
}
