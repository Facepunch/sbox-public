namespace Borealis;

/// <summary>
/// Draws unfaded opaque viewmodels at the front of the depth range (<c>CRenderingPipelineStandard::AddLayersToView</c>), where
/// they're shaded: the world's forward pass leaves them out (<see cref="OpaqueLayer"/>), and <see cref="GameOverlayDepthLayer"/>
/// gives the range their depth first, so each pixel shades once. Runs before <c>AfterTransparent</c> so depth of field reads
/// their remapped depth.
/// </summary>
internal class GameOverlayLayer : MeshLayer
{
	/// <summary>
	/// Overlay depth range: 0.99–1 in reverse-Z (<c>DEPTH_RANGE_GAME_OVERLAYS</c>).
	/// </summary>
	internal const float DepthRange = 0.01f;

	static readonly StringToken ForwardMode = new( "Forward" );

	public GameOverlayLayer() : this( "GameOverlay", MeshRuns.OverlayOpaque, MeshLayerType.Opaque ) => DepthDrawnFirst = true;

	protected GameOverlayLayer( string name, MeshRuns runs, MeshLayerType type ) : base( name )
	{
		ShaderMode = ForwardMode;
		Runs = runs;
		LayerType = type;
		TakesToolsPass = true;
	}

	public override bool IsShared => false;

	public override bool IsNeeded( RenderFrame frame ) => frame.RunCount( this ) > 0;

	/// <summary>
	/// Bind overlay depth and matching view constants so shaders select the correct light clusters.
	/// </summary>
	public override void Begin( RenderFrame frame, GpuContext rc, ViewPass view, bool start )
	{
		frame.BeginPass( rc, frame.Target( rc, color: true, final: false ) with { MinZ = 1 - DepthRange, MaxZ = 1 } );
	}
}

/// <summary>
/// The opaque overlays' depth alone, in the overlay's range, before <see cref="GameOverlayLayer"/> draws them in colour
/// (depth tested, not written again: <see cref="MeshLayer.DepthDrawnFirst"/>), so its colour shades each pixel once, with
/// early depth testing: a viewmodel overlaps itself, and nothing else gives that range depth first.
/// </summary>
internal sealed class GameOverlayDepthLayer : GameOverlayLayer
{
	static readonly StringToken DepthMode = new( "Depth" );

	public GameOverlayDepthLayer() : base( "GameOverlay Depth", MeshRuns.OverlayOpaque, MeshLayerType.DepthPrepass )
	{
		ShaderMode = DepthMode;
		Depth = true;
		TakesToolsPass = false;
	}

	public override void Begin( RenderFrame frame, GpuContext rc, ViewPass view, bool start )
	{
		frame.BeginPass( rc, frame.Target( rc, color: false, final: false ) with { MinZ = 1 - DepthRange, MaxZ = 1 } );
	}
}

/// <summary>
/// Redraws unfaded translucent viewmodels back to front (<c>GameOverlay Translucent</c>).
/// Runs after <c>AfterTransparent</c> and <c>AfterViewmodel</c> to avoid background depth effects blurring over them.
/// </summary>
internal sealed class GameOverlayTranslucentLayer : GameOverlayLayer
{
	public GameOverlayTranslucentLayer() : base( "GameOverlay Translucent", MeshRuns.OverlayTranslucent, MeshLayerType.Translucent ) { }
}
