namespace Sandbox.SceneRenderer;

/// <summary>
/// Draws opaque then faded geometry (<c>AddOpaqueLayers</c>).
/// The first segment clears colour, plus depth if no prepass ran.
/// </summary>
internal sealed class OpaqueLayer : MeshLayer
{
	static readonly StringToken ForwardMode = new( "Forward" );

	public OpaqueLayer() : base( "Opaque Forward" )
	{
		ShaderMode = ForwardMode;
		Runs = MeshRuns.Opaque | MeshRuns.OpaqueNoPrepass | MeshRuns.Faded | MeshRuns.OverlayPrepass;
	}

	public override void Begin( RenderFrame frame, RenderContext rc, ViewPass view, bool start )
	{
		frame.BindFrame( rc );

		// The main frame already cleared the skybox target.
		if ( start && !frame.IsSkybox ) rc.Clear( frame.Clear, clearDepth: !frame.System.DepthPrepass );
	}
}
