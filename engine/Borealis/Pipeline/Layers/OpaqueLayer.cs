namespace Borealis;

/// <summary>
/// Draws opaque then faded geometry (<c>AddOpaqueLayers</c>).
/// The first segment clears colour, plus depth if no prepass ran. While the frame traces screen-space reflections, its
/// surfaces also write their normal and roughness into <see cref="RenderFrame.Normals"/>, cleared with colour.
/// </summary>
internal sealed class OpaqueLayer : MeshLayer
{
	static readonly StringToken ForwardMode = new( "Forward" );

	public OpaqueLayer() : base( "Opaque Forward" )
	{
		ShaderMode = ForwardMode;
		Runs = MeshRuns.Opaque | MeshRuns.OpaqueNoPrepass | MeshRuns.Faded | MeshRuns.OverlayPrepass;
		OcclusionPhase = OcclusionPhase.Final;
		TakesToolsPass = true;
	}

	public override void Begin( RenderFrame frame, GpuContext rc, ViewPass view, bool start )
	{
		frame.BindFrame( rc, normals: true );

		// The main frame already cleared the skybox target.
		if ( start && !frame.IsSkybox ) rc.Clear( frame.Clear, clearDepth: !frame.System.DepthPrepass );
	}
}
