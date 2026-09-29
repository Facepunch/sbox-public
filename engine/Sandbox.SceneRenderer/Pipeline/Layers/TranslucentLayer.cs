using Sandbox.Rendering;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Back-to-front alpha-tinted geometry on the main thread (<c>Translucent Forward</c>).
/// The main world enables fading; the 3D skybox does not. Materials control blending and depth writes.
/// </summary>
internal sealed class TranslucentLayer : MeshLayer
{
	static readonly StringToken ForwardMode = new( "Forward" );
	static readonly StringToken EnableAlphaTint = new( "EnableAlphaTint" );

	/// <summary>
	/// Enable <c>D_OPAQUE_FADE</c>, matching native's main translucent layer.
	/// </summary>
	public bool Fade { get; }

	public TranslucentLayer( string name, bool fade ) : base( name )
	{
		ShaderMode = ForwardMode;
		Runs = MeshRuns.Translucent;
		LayerType = SceneLayerType.Translucent;
		Fade = fade;
	}

	public override bool IsShared => false;

	/// <summary>
	/// Bind the frame with alpha tint and optional fading.
	/// </summary>
	public override void Begin( RenderFrame frame, RenderContext rc, ViewPass view, bool start )
	{
		frame.BindFrame( rc );
		if ( Fade ) rc.SetPassCombo( MeshRenderFeature.OpaqueFade, 1 );
		rc.SetPassAttribute( EnableAlphaTint, 1 );
	}
}
