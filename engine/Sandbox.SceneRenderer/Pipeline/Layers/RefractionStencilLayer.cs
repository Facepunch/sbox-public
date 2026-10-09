using Sandbox.Rendering;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Quarter-resolution glass depth for <c>depthaware_mask_cs</c> (<c>RenderPipeline.AddLayersToView</c>).
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
	RenderTarget target;
	Rect viewport;

	public RefractionStencilLayer() : base( "Refraction Stencil Layer" )
	{
		ShaderMode = DepthMode;
		Depth = true;
		ForceDepthFastPath = true;
		Runs = MeshRuns.Refraction;
		DrawsCustomObjects = false;
	}

	public override bool IsShared => false;

	public override FrameResources Writes => FrameResources.RefractionStencil;

	/// <summary>
	/// Acquire and bind refraction depth before recording.
	/// </summary>
	public override void BeforeSegments( RenderFrame frame, RenderContext frameContext )
	{
		var full = frame.View.Viewport;
		viewport = new Rect( (int)(full.Left / 4), (int)(full.Top / 4), (int)(full.Width / 4), (int)(full.Height / 4) );
		target = Rendering.RefractionStencilLayer.GetTarget( (int)viewport.Width, (int)viewport.Height );
		frameContext.Attributes.Set( RefractionDepth, target.DepthTarget );
	}

	/// <summary>
	/// Clear depth and set quarter-resolution constants and refraction combos.
	/// </summary>
	public override void Begin( RenderFrame frame, RenderContext rc, ViewPass view, bool start )
	{
		frame.BeginPass( rc, new StageTarget
		{
			Context = rc.Native,
			Attributes = rc.Attributes,
			Depth = target.DepthTarget.native,
			Size = new Vector2( target.Width, target.Height ),
			Viewport = viewport,
			MinZ = frame.DepthMin,
			MaxZ = frame.DepthMax,
			ColorFormat = ImageFormat.None,
			Samples = 1,
		} );
		if ( start ) rc.Clear( Color.Transparent, clearColor: false, clearDepth: true );

		rc.SetPassCombo( RefractionTest, 1 );
		rc.SetPassCombo( RenderBackfaces, 1 );
	}

	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats )
	{
		base.Record( frame, rc, ref stats );
		target = null;
	}
}
