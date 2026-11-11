namespace Basalt;

/// <summary>
/// Depth-only pass for unfaded opaque meshes (<c>DepthOnlyPrepassLayer</c>), with conservative alpha testing.
/// Tests the overlay stencil to preserve viewmodel depth inside walls (<c>ISceneLayer::SetOverlayStencil</c>).
/// Depth only for every view: Basalt draws no normals G-buffer (native's <c>DepthNormalPrepassLayer</c>); ambient occlusion
/// rebuilds its normals from depth.
/// </summary>
internal class DepthPrepassLayer : MeshLayer
{
	static readonly StringToken DepthMode = new( "Depth" );
	static readonly StringToken AlphaTestConservative = new( "D_ALPHA_TEST_CONSERVATIVE" );

	/// <summary>
	/// Earlier overlay prepass, which owns the depth clear when active. Null for the overlay pass itself.
	/// </summary>
	readonly OverlayDepthPrepassLayer overlays;

	public DepthPrepassLayer( OverlayDepthPrepassLayer overlays ) : this( "Depth Prepass", MeshRuns.Opaque, overlays )
	{
		OcclusionPhase = OcclusionPhase.Early;
	}

	protected DepthPrepassLayer( string name, MeshRuns runs, OverlayDepthPrepassLayer overlays = null ) : base( name )
	{
		ShaderMode = DepthMode;
		Depth = true;
		Runs = runs;
		this.overlays = overlays;

		// Opaque custom objects - terrain - prepass too, as native's do: AO and contact shadows read the prepass before the
		// opaque pass would draw them
		LayerType = MeshLayerType.DepthPrepass;
	}

	public override bool IsNeeded( RenderFrame frame ) => frame.System.DepthPrepass;
	public override FrameResources Writes => FrameResources.Depth;
	public override bool HasSideEffects => true;

	/// <summary>
	/// Whether this is the first active prepass and must clear depth.
	/// </summary>
	protected virtual bool Clears( RenderFrame frame ) => overlays is null || !overlays.IsNeeded( frame );

	/// <summary>
	/// Test the stencil when overlays have written it.
	/// </summary>
	protected virtual OverlayStencil Stencil( RenderFrame frame ) => overlays is not null && overlays.IsNeeded( frame ) ? OverlayStencil.Test : OverlayStencil.None;

	public override void Begin( RenderFrame frame, GpuContext rc, ViewPass view, bool start )
	{
		frame.BindFrame( rc, color: false );

		// The main frame already cleared skybox depth. Clearing depth also clears stencil
		if ( start && !frame.IsSkybox && Clears( frame ) ) rc.Clear( frame.Clear, clearColor: false );

		// Depth mode lacks forward's alpha-to-coverage smoothing.
		rc.SetPassCombo( AlphaTestConservative, 1 );
		rc.OverlayStencil = Stencil( frame );
	}
}

/// <summary>
/// Prepasses viewmodels at real depth and claims their stencil pixels (<c>Depth Normal Prepass (Overlay)</c>).
/// Clears depth before the world prepass when active.
/// </summary>
internal sealed class OverlayDepthPrepassLayer : DepthPrepassLayer
{
	public OverlayDepthPrepassLayer() : base( "Depth Prepass (Overlay)", MeshRuns.OverlayPrepass ) { }

	public override bool IsNeeded( RenderFrame frame ) => frame.System.DepthPrepass && frame.RunCount( this ) > 0;

	protected override bool Clears( RenderFrame frame ) => true;

	protected override OverlayStencil Stencil( RenderFrame frame ) => OverlayStencil.Write;
}


/// <summary>
/// The world prepass again, for anything the early occlusion cull rejected that's visible now. Only draws culled runs,
/// into the same depth.
/// </summary>
internal sealed class LateDepthPrepassLayer : DepthPrepassLayer
{
	public LateDepthPrepassLayer( OverlayDepthPrepassLayer overlays ) : base( "Depth Prepass (Late)", MeshRuns.Opaque, overlays )
	{
		OcclusionPhase = OcclusionPhase.Late;
	}

	// Without last frame's chain the early prepass drew everything
	public override bool IsNeeded( RenderFrame frame ) => frame.System.Occlusion.CullsEarly;

	protected override bool Clears( RenderFrame frame ) => false;
}
