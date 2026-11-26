namespace Borealis;

/// <summary>
/// The overlays (viewmodels and the game overlay layer), opaque and translucent, drawn again in depth mode into a depth of
/// their own for a temporal upscaler's reactive mask, as native's <c>Reactive Depth (Viewmodel)</c> and
/// <c>Reactive Depth (Overlay)</c> layers draw them (<c>StandardPipeline</c>). Translucent overlays write no depth in the
/// frame's own depth, so this is where they reach the mask. Runs only when the view asks (<see cref="RenderView.ReactiveDepth"/>)
/// and has overlays; the depth is <see cref="RenderSystem.ReactiveDepth"/> after the frame.
/// </summary>
internal sealed class ReactiveDepthLayer : MeshLayer
{
	static readonly StringToken DepthMode = new( "Depth" );

	GpuTexture depth;

	public ReactiveDepthLayer() : base( "Reactive Depth" )
	{
		ShaderMode = DepthMode;
		Depth = true;
		Runs = MeshRuns.OverlayOpaque | MeshRuns.OverlayTranslucent;

		// Native's are depth prepass layers: custom objects in the overlays draw there too
		LayerType = MeshLayerType.DepthPrepass;
	}

	public override bool IsShared => false;

	public override bool IsNeeded( RenderFrame frame ) => frame.View.ReactiveDepth && !frame.IsSkybox && frame.RunCount( this ) > 0;

	/// <summary>
	/// The depth, the size of the frame's (depth alone: nothing tests stencil there), before recording.
	/// </summary>
	public override void BeforeSegments( RenderFrame frame, GpuContext frameContext )
	{
		var size = frame.Output.Size;
		depth = frame.System.Transients.Get( TransientTextureRole.ReactiveDepth, TextureDesc.Target( (int)size.x, (int)size.y, TextureFormat.D32 ) );
		frame.System.ReactiveDepth = depth;
	}

	/// <summary>
	/// Cleared to the far plane: anything nearer is an overlay.
	/// </summary>
	public override void Begin( RenderFrame frame, GpuContext rc, ViewPass view, bool start )
	{
		frame.BeginPass( rc, new StageTarget
		{
			Context = rc,
			Attributes = rc.Attributes,
			Depth = depth,
			Size = new Vector2( depth.Width, depth.Height ),
			Viewport = frame.View.Viewport,
			MinZ = frame.DepthMin,
			MaxZ = frame.DepthMax,
			ColorFormat = TextureFormat.None,
			Samples = 1,
		} );
		if ( start ) rc.Clear( Color.Transparent, clearColor: false, clearDepth: true );
	}

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
	{
		base.Record( frame, rc, ref stats );
		depth = null;
	}
}
