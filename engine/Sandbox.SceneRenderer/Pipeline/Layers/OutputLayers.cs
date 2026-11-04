using Sandbox.Rendering;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Post-HDR overlay base (<c>CRenderingPipelineStandard::AddLayersToView</c>).
/// Records on the main thread with per-layer constants; sorts opaque draws front to back and translucent draws back to front.
/// Includes faded objects.
/// </summary>
internal abstract class OutputOverlayLayer : MeshLayer
{
	static readonly StringToken ForwardMode = new( "Forward" );

	/// <summary>
	/// Bind HDR depth when its sample count matches the output.
	/// </summary>
	protected bool WithDepth { get; init; }

	/// <summary>
	/// Exposure override; negative uses the view's (<c>SetTonemapOverrideScaleValue</c>).
	/// </summary>
	protected float ToneMapScalar { get; init; } = -1;

	protected OutputOverlayLayer( string name ) : base( name )
	{
		ShaderMode = ForwardMode;
	}

	public override bool IsShared => false;

	public override bool IsNeeded( RenderFrame frame ) => frame.RunCount( this ) > 0;

	/// <summary>
	/// Bind output colour and optional HDR depth, including the custom-object restore target.
	/// </summary>
	public override void Begin( RenderFrame frame, RenderContext rc, ViewPass view, bool start )
	{
		var target = frame.Target( rc, color: true, final: true );
		if ( !WithDepth ) target = target with { Depth = default };
		frame.BeginPass( rc, target, ToneMapScalar );
	}
}

/// <summary>
/// Debug overlays before screen UI, with optional depth testing (<c>OverlayWithDepth</c>/<c>OverlayWithoutDepth</c>).
/// Uses a tonemap scale of 1.
/// </summary>
internal sealed class ScreenOverlayLayer : OutputOverlayLayer
{
	public ScreenOverlayLayer( bool withDepth ) : base( withDepth ? "OverlayWithDepth" : "OverlayWithoutDepth" )
	{
		WithDepth = withDepth;
		Runs = withDepth ? MeshRuns.OverlayWithDepth : MeshRuns.OverlayWithoutDepth;
		LayerType = SceneLayerType.Opaque;
		ToneMapScalar = 1;
	}
}

/// <summary>
/// Redraws after-UI objects without depth (<c>UI Overlay</c>), using fade and alpha tint.
/// Mixed objects contribute only opaque draws.
/// </summary>
internal sealed class AfterUILayer : OutputOverlayLayer
{
	static readonly StringToken EnableAlphaTint = new( "EnableAlphaTint" );

	public AfterUILayer() : base( "UI Overlay" )
	{
		Runs = MeshRuns.AfterUI;
		LayerType = SceneLayerType.Translucent;
	}

	public override void Begin( RenderFrame frame, RenderContext rc, ViewPass view, bool start )
	{
		base.Begin( frame, rc, view, start );
		rc.SetPassCombo( MeshRenderFeature.OpaqueFade, 1 );
		rc.SetPassAttribute( EnableAlphaTint, 1 );
	}
}
