namespace Sandbox.SceneRenderer;

/// <summary>
/// Static overlays in render order, including faded ones (<c>CRenderingPipelineStandard::AddLayersToView</c>).
/// Runs after the combined opaque pass; overlays have no prepass or shadows.
/// </summary>
internal sealed class StaticOverlayLayer : MeshLayer
{
	static readonly StringToken ForwardMode = new( "Forward" );

	public StaticOverlayLayer() : base( "Static World Overlays" )
	{
		ShaderMode = ForwardMode;
		Runs = MeshRuns.StaticOverlay;
		DrawsCustomObjects = false;
	}

	public override bool IsNeeded( RenderFrame frame ) => frame.RunCount( this ) > 0;

	public override void Begin( RenderFrame frame, RenderContext rc, ViewPass view, bool start ) => frame.BindFrame( rc );
}
