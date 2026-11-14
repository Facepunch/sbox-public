namespace Borealis;

/// <summary>
/// The shadow map debug view (<see cref="DebugViews.ShadowMaps"/>): each shadow map <see cref="ShadowSystem.Describe"/> listed,
/// drawn as a thumbnail into the final output, over everything else. The host labels them.
/// </summary>
internal sealed class DebugViewLayer : RenderLayer
{
	static readonly StringToken ForwardMode = new( "Forward" );
	static readonly StringToken CubeCombo = new( "D_CUBE" );
	static readonly StringToken IndexName = new( "ShadowMapIndex" );
	static readonly StringToken OrthographicName = new( "ShadowMapOrthographic" );
	static readonly StringToken FrameName = new( "ShadowMapFrame" );

	static GpuMaterial material;

	public DebugViewLayer() : base( "Debug Views" ) { }

	// Planned before setup lists the maps, so it may find none to draw
	public override bool IsNeeded( RenderFrame frame ) => (frame.DebugViews & DebugViews.ShadowMaps) != 0;

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
	{
		var maps = frame.System.DebugReport.ShadowMaps;
		if ( maps.Count == 0 ) return;

		material ??= RenderHost.Current.Device.MaterialFromShader( "shaders/debug_shadow_map.shader" );

		var target = frame.Target( rc, color: true, final: true ) with { Depth = null };
		foreach ( var map in maps )
		{
			// A static cache that didn't render this frame was last a copy's source
			if ( map.StaticCache ) rc.BarrierToPixelShaderRead( map.Texture );

			rc.Bind( target with { Viewport = map.Thumbnail } );

			var parameters = ParameterPool.Get();
			parameters.SetCombo( CubeCombo, map.Kind == ShadowMapKind.Point );
			parameters.Set( IndexName, map.Texture.BindlessIndex );
			parameters.Set( OrthographicName, map.Kind == ShadowMapKind.Cascade ? 1 : 0 );
			var color = DebugViewReport.ShadowFrameColor( map );
			parameters.Set( FrameName, new Vector4( color.r, color.g, color.b, 1 ) );
			rc.DrawFullscreen( material, parameters, ForwardMode );
			ParameterPool.Return( parameters );
		}

		rc.Bind( target );
	}
}
