namespace Sandbox.SceneRenderer;

/// <summary>
/// Depth-only pass for unfaded opaque meshes (<c>DepthOnlyPrepassLayer</c>), with conservative alpha testing.
/// Tests the overlay stencil to preserve viewmodel depth inside walls (<c>ISceneLayer::SetOverlayStencil</c>).
/// For a view with <see cref="RenderView.DepthNormals"/> it's native's depth-normals prepass instead
/// (<c>DepthNormalPrepassLayer</c>): the same draws with their materials' depth pixel shaders, writing normals and
/// roughness into a G-buffer that AO and SSR read (<c>NormalsGBuffer</c>, <c>NormalsTextureIndex</c>).
/// </summary>
internal class DepthPrepassLayer : MeshLayer
{
	static readonly StringToken DepthMode = new( "Depth" );
	static readonly StringToken AlphaTestConservative = new( "D_ALPHA_TEST_CONSERVATIVE" );
	static readonly StringToken NormalsTextureIndex = new( "NormalsTextureIndex" );
	static readonly StringToken NormalsGBuffer = new( "NormalsGBuffer" );

	// The world prepass makes the frame's G-buffer; the overlay prepass, which draws first, only draws into it
	readonly bool ownsGBuffer;

	/// <summary>
	/// Earlier overlay prepass, which owns the depth clear when active. Null for the overlay pass itself.
	/// </summary>
	readonly OverlayDepthPrepassLayer overlays;

	public DepthPrepassLayer( OverlayDepthPrepassLayer overlays ) : this( "Depth Prepass", MeshRuns.Opaque )
	{
		this.overlays = overlays;
		ownsGBuffer = true;
	}

	protected DepthPrepassLayer( string name, MeshRuns runs ) : base( name )
	{
		ShaderMode = DepthMode;
		Depth = true;
		WritesNormals = true;
		Runs = runs;

		// Opaque custom objects - terrain - prepass too, as native's do, for views with depth normals (DrawsCustomObjectsIn):
		// AO runs after the prepass, before they'd draw
		LayerType = SceneLayerType.DepthPrepass;
	}

	/// <summary>
	/// Take the frame's G-buffer and name it on the frame's attributes, before any segment records - or say there's none
	/// (<c>NormalsTextureIndex</c> -1, which <c>Normals::Sample</c> answers from depth), since the frame's attributes outlive
	/// the frame. Native's is the full target's size at its sample count (<c>RenderPipeline.AddLayersToView</c>).
	/// </summary>
	public override void BeforeSegments( RenderFrame frame, RenderContext frameContext )
	{
		if ( !ownsGBuffer ) return;

		if ( !frame.View.DepthNormals || frame.IsSkybox )
		{
			frame.NormalsGBuffer = null;
			frameContext.Attributes.Set( NormalsTextureIndex, -1 );
			return;
		}

		var target = frame.Output.Target;
		using var gbuffer = RenderTarget.GetTemporary( (int)target.Size.x, (int)target.Size.y, ImageFormat.RGBA16161616F, ImageFormat.None, target.Color.MultisampleType.FromEngine(), 1, "NormalsGBuffer" );
		frame.NormalsGBuffer = gbuffer.ColorTarget;
		frameContext.Attributes.Set( NormalsTextureIndex, gbuffer.ColorTarget.Index );
		frameContext.Attributes.Set( NormalsGBuffer, gbuffer.ColorTarget );
	}

	public override bool IsNeeded( RenderFrame frame ) => frame.System.DepthPrepass;

	/// <summary>
	/// Whether this is the first active prepass and must clear depth.
	/// </summary>
	protected virtual bool Clears( RenderFrame frame ) => overlays is null || !overlays.IsNeeded( frame );

	/// <summary>
	/// Test the stencil when overlays have written it.
	/// </summary>
	protected virtual OverlayStencil Stencil( RenderFrame frame ) => overlays is not null && overlays.IsNeeded( frame ) ? OverlayStencil.Test : OverlayStencil.None;

	public override void Begin( RenderFrame frame, RenderContext rc, ViewPass view, bool start )
	{
		var gbuffer = frame.NormalsGBuffer;
		if ( gbuffer is null ) frame.BindFrame( rc, color: false );
		else frame.BeginPass( rc, frame.Target( rc, color: false, final: false ) with { Color = gbuffer.native, SrgbWrite = false } );

		// The main frame already cleared skybox depth. Clearing depth also clears stencil, and the first prepass clears the
		// G-buffer: nothing drawn reads as no normal
		if ( start && !frame.IsSkybox && Clears( frame ) ) rc.Clear( gbuffer is null ? frame.Clear : Color.Transparent, clearColor: gbuffer is not null );

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
