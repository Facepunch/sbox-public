namespace Sandbox.SceneRenderer;

/// <summary>
/// Draws decal meshes before translucency (<c>CRenderingPipelineStandard::AddLayersToView</c>).
/// Reads <c>SceneDepth</c> without binding depth, so view constants use one sample even with MSAA.
/// Includes faded decals in nearest-first order.
/// </summary>
internal sealed class DecalLayer : MeshLayer
{
	static readonly StringToken ForwardMode = new( "Forward" );
	static readonly StringToken SceneDepth = new( "SceneDepth" );
	static readonly StringToken MsaaDepthBuffer = new( "D_MSAA_DEPTH_BUFFER" );

	public DecalLayer() : base( "Decals (Static and Dynamic)" )
	{
		ShaderMode = ForwardMode;
		Runs = MeshRuns.Decal;
		DrawsCustomObjects = false;
	}

	public override bool IsShared => false;

	public override bool IsNeeded( RenderFrame frame ) => frame.RunCount( this ) > 0;

	/// <summary>
	/// Bind colour and expose depth for sampling.
	/// </summary>
	public override void Begin( RenderFrame frame, RenderContext rc, ViewPass view, bool start )
	{
		var target = frame.Output.Target;
		frame.BeginPass( rc, frame.Target( rc, color: true, final: false ) with { Depth = default } );

		rc.Set( SceneDepth, target.Depth );
		rc.SetPassCombo( MsaaDepthBuffer, target.Samples > 1 ? 1 : 0 );
	}
}
