using Sandbox.Rendering;

namespace Basalt;

/// <summary>
/// Quarter-resolution glass depth for <c>depthaware_mask_cs</c> (native: <c>StandardPipeline</c>).
/// The mask uses reprojected history to keep foreground objects out of refraction.
/// Draws unfaded translucent readers with forced depth material and both faces; runs only when a frame copy is needed.
/// </summary>
internal sealed class RefractionStencilLayer : MeshLayer
{
	static readonly StringToken DepthMode = new( "Depth" );
	static readonly StringToken RefractionDepth = new( "RefractionDepthTexture" );
	static readonly StringToken RefractionTest = new( "D_REFRACTION_TEST" );
	static readonly StringToken RenderBackfaces = new( "D_RENDER_BACKFACES" );

	// Pooled target for this frame.
	GpuTexture depth;
	Rect viewport;

	public RefractionStencilLayer() : base( "Refraction Stencil Layer" )
	{
		ShaderMode = DepthMode;
		Depth = true;
		ForceDepthFastPath = true;
		Runs = MeshRuns.Refraction;
	}

	public override bool IsShared => false;

	public override FrameResources Writes => FrameResources.RefractionStencil;

	/// <summary>
	/// Acquire and bind refraction depth before recording.
	/// </summary>
	public override void BeforeSegments( RenderFrame frame, GpuContext frameContext )
	{
		var full = frame.View.Viewport;
		viewport = new Rect( (int)(full.Left / 4), (int)(full.Top / 4), (int)(full.Width / 4), (int)(full.Height / 4) );
		depth = frame.System.Transients.Get( TextureDesc.Target( (int)viewport.Width, (int)viewport.Height, TextureFormat.D16 ), "Refraction Stencil" );
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

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
	{
		base.Record( frame, rc, ref stats );
		depth = null;
	}
}
