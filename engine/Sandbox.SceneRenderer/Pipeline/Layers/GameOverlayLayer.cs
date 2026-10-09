using Sandbox.Rendering;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Redraws unfaded opaque viewmodels at the front of the depth range (<c>CRenderingPipelineStandard::AddLayersToView</c>).
/// Runs before <c>AfterTransparent</c> so depth of field reads their remapped depth.
/// </summary>
internal class GameOverlayLayer : MeshLayer
{
	/// <summary>
	/// Overlay depth range: 0.99–1 in reverse-Z (<c>DEPTH_RANGE_GAME_OVERLAYS</c>).
	/// </summary>
	internal const float DepthRange = 0.01f;

	static readonly StringToken ForwardMode = new( "Forward" );

	public GameOverlayLayer() : this( "GameOverlay", MeshRuns.OverlayOpaque, SceneLayerType.Opaque ) { }

	protected GameOverlayLayer( string name, MeshRuns runs, SceneLayerType type ) : base( name )
	{
		ShaderMode = ForwardMode;
		Runs = runs;
		LayerType = type;
	}

	public override bool IsShared => false;

	public override bool IsNeeded( RenderFrame frame ) => frame.RunCount( this ) > 0;

	/// <summary>
	/// Bind overlay depth and matching view constants so shaders select the correct light clusters.
	/// </summary>
	public override void Begin( RenderFrame frame, RenderContext rc, ViewPass view, bool start )
	{
		frame.BeginPass( rc, frame.Target( rc, color: true, final: false ) with { MinZ = 1 - DepthRange, MaxZ = 1 } );
	}
}

/// <summary>
/// Redraws unfaded translucent viewmodels back to front (<c>GameOverlay Translucent</c>).
/// Runs after <c>AfterTransparent</c> and <c>AfterViewmodel</c> to avoid background depth effects blurring over them.
/// </summary>
internal sealed class GameOverlayTranslucentLayer : GameOverlayLayer
{
	public GameOverlayTranslucentLayer() : base( "GameOverlay Translucent", MeshRuns.OverlayTranslucent, SceneLayerType.Translucent ) { }
}
