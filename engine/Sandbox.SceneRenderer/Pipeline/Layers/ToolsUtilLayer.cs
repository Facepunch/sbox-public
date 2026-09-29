namespace Sandbox.SceneRenderer;

/// <summary>
/// Redraws tools geometry in <c>ToolsUtil</c> mode after opaque or translucent rendering
/// (<c>CRenderingPipelineStandard::AddLayersToView</c>). Skips frame-buffer readers and materials without that mode.
/// </summary>
internal sealed class ToolsUtilLayer : MeshLayer
{
	static readonly StringToken ToolsUtilMode = new( "ToolsUtil" );

	readonly bool translucent;

	public ToolsUtilLayer( bool translucent ) : base( translucent ? "ToolsUtil Translucent" : "ToolsUtil Opaque" )
	{
		this.translucent = translucent;
		ShaderMode = ToolsUtilMode;
		Runs = translucent ? MeshRuns.Translucent : MeshRuns.Opaque | MeshRuns.OpaqueNoPrepass;
		DrawsCustomObjects = false;
		if ( translucent ) LayerType = SceneLayerType.Translucent;
	}

	public override bool IsNeeded( RenderFrame frame ) => frame.View.ToolsView && frame.RunCount( this ) > 0;

	// Translucency must blend back to front on the main thread.
	public override bool IsShared => !translucent;

	public override void Begin( RenderFrame frame, RenderContext rc, ViewPass view, bool start ) => frame.BindFrame( rc );

	/// <summary>
	/// Record without a frame-buffer copy; this layer excludes glass.
	/// </summary>
	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats )
	{
		var view = frame.MainPass;
		Begin( frame, rc, view, start: true );
		frame.System.DrawRange( rc, view, this, 0, frame.System.RunCount( view, this ), ref stats );
	}
}
