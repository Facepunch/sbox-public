namespace Borealis;

/// <summary>
/// Draws the 2D sky at reverse-Z depth 0 (<c>Add2DSkyboxLayers</c>).
/// A 3D skybox supplies its own sky after translucency (<c>CRenderingPipeline3DSkybox::AddLayersToView</c>).
/// </summary>
internal sealed class SkyLayer : RenderLayer
{
	static readonly StringToken ForwardMode = new( "Forward" );

	public SkyLayer() : base( "Sky" ) { }

	public override bool IsNeeded( RenderFrame frame ) => frame.System.DrawSky && frame.Skybox is null && RenderSystem.ShowsSkies( frame.View ) && !ToolsViews.Counts( frame.View )
		&& (frame.View.OverridesSky ? frame.View.SkyMaterial : frame.World.Lighting.SkyMaterial) is not null;

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
	{
		// Match view constants to the far-plane depth range.
		frame.BeginPass( rc, frame.Target( rc, color: true, final: false ) with { MinZ = 0, MaxZ = 0 } );
		var view = frame.View;
		var lighting = frame.World.Lighting;
		var drawn = view.OverridesSky
			? rc.DrawSky( view.SkyMaterial, view.SkyTint, view.SkyRotation, view.SkyFog, ForwardMode )
			: rc.DrawSky( lighting.SkyMaterial, lighting.SkyTint, lighting.SkyRotation, lighting.SkyFog, ForwardMode );
		if ( drawn ) stats.Draws++;
	}
}
