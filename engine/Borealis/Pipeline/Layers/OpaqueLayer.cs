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
		// Not the overlays the prepass drew in the world (OverlayPrepass): they're shaded once, in GameOverlay
		Runs = MeshRuns.Opaque | MeshRuns.OpaqueNoPrepass | MeshRuns.Faded;
		OcclusionPhase = OcclusionPhase.Final;
		TakesToolsPass = true;
	}

	public override void Begin( RenderFrame frame, GpuContext rc, ViewPass view, bool start )
	{
		frame.BindFrame( rc, normals: true );

		// The main frame already cleared the skybox target, and the stage after the prepass cleared it when it draws colour
		if ( start && !frame.IsSkybox && !frame.ColorBeforeOpaque ) rc.Clear( frame.Clear, clearDepth: !frame.System.DepthPrepass );
	}
}
