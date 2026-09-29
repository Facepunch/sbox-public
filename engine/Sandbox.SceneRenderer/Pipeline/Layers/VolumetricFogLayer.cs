namespace Sandbox.SceneRenderer;

/// <summary>
/// Computes next frame's fog from depth, lights and shadows, after all other layers
/// (<c>CRenderingPipelineStandard::AddLayersToView</c>).
/// </summary>
internal sealed class VolumetricFogLayer : RenderLayer
{
	public VolumetricFogLayer() : base( "VolumetricFog" ) { }

	public override bool IsNeeded( RenderFrame frame ) => frame.System.Fog.Active;

	// The froxels are culled and lit against scene depth
	public override FrameResources Reads( RenderFrame frame ) => FrameResources.DepthChain;

	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats )
	{
		frame.System.Fog.Render( rc, frame.View, frame.World.FogVolumes );
	}
}
