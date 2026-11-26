namespace Borealis;

/// <summary>
/// Quarter-resolution glass depth for <c>depthaware_mask_cs</c> (native: <c>StandardPipeline</c>).
/// The mask uses reprojected history to keep foreground objects out of refraction.
/// Draws unfaded translucent readers with forced depth material and both faces; runs only when a frame copy is needed.
/// Records on a worker as one segment (<see cref="MaxSegments"/>): a view full of glass has hundreds of draws, which split
/// into many small lists cost more in submits and plan checks than they save; custom objects among them stay on the main thread.
/// </summary>
internal sealed class RefractionStencilLayer : MeshLayer
{
	static readonly StringToken DepthMode = new( "Depth" );
	static readonly StringToken RefractionDepth = new( "RefractionDepthTexture" );
	static readonly StringToken RefractionTest = new( "D_REFRACTION_TEST" );
	static readonly StringToken RenderBackfaces = new( "D_RENDER_BACKFACES" );

	// Pooled target for this frame, made before its segments record
	GpuTexture depth;
	Rect viewport;

	public RefractionStencilLayer() : base( "Refraction Stencil Layer" )
	{
		ShaderMode = DepthMode;
		Depth = true;
		ForceDepthFastPath = true;
		Runs = MeshRuns.Refraction;
	}

	public override int MaxSegments => 1;

	public override FrameResources Writes => FrameResources.RefractionStencil;

	/// <summary>
	/// Acquire and bind refraction depth before recording.
	/// </summary>
	public override void BeforeSegments( RenderFrame frame, GpuContext frameContext )
	{
		var full = frame.View.Viewport;
		viewport = new Rect( 0, 0, Math.Max( 1, (int)full.Width / 4 ), Math.Max( 1, (int)full.Height / 4 ) );
		depth = frame.System.Transients.Get( TransientTextureRole.RefractionStencil, TextureDesc.Target( (int)viewport.Width, (int)viewport.Height, TextureFormat.D16 ) );
		frameContext.Attributes.Set( RefractionDepth, depth );
	}

	/// <summary>
	/// Clear depth and set quarter-resolution constants and refraction combos.
	/// </summary>
	public override void Begin( RenderFrame frame, GpuContext rc, ViewPass view, bool start )
	{
		frame.BeginPass( rc, new StageTarget
		{
			Context = rc,
			Attributes = rc.Attributes,
			Depth = depth,
			Size = new Vector2( depth.Width, depth.Height ),
			Viewport = viewport,
			MinZ = frame.DepthMin,
			MaxZ = frame.DepthMax,
			ColorFormat = TextureFormat.None,
			Samples = 1,
		} );
		if ( start ) rc.Clear( Color.Transparent, clearColor: false, clearDepth: true );

		rc.SetPassCombo( RefractionTest, 1 );
		rc.SetPassCombo( RenderBackfaces, 1 );
	}
}
