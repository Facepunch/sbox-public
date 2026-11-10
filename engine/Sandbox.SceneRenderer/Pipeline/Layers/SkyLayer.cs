namespace Sandbox.SceneRenderer;

/// <summary>
/// Draws the 2D sky at reverse-Z depth 0 (<c>Add2DSkyboxLayers</c>).
/// A 3D skybox supplies its own sky after translucency (<c>CRenderingPipeline3DSkybox::AddLayersToView</c>).
/// </summary>
internal sealed class SkyLayer : RenderLayer
{
	static readonly StringToken ForwardMode = new( "Forward" );

	public SkyLayer() : base( "Sky" ) { }

	public override bool IsNeeded( RenderFrame frame ) => frame.System.DrawSky && frame.Skybox is null && RenderSystem.ShowsSkies( frame.View )
		&& frame.World.Lighting.SkyMaterial is not null;

	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats )
	{
		// Match view constants to the far-plane depth range.
		frame.BeginPass( rc, frame.Target( rc, color: true, final: false ) with { MinZ = 0, MaxZ = 0 } );
		if ( rc.DrawSky( frame.World.Lighting, ForwardMode ) ) stats.Draws++;
	}
}
