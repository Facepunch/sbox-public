namespace Borealis;

/// <summary>
/// The visibility ids views (<see cref="ToolsViews.IdsViewOf"/>; docs/reference/borealis.md, "Visibility ids views"): the
/// main view's depth prepass ids as a colour per triangle, instance or draw, over the unlit frame after post processing and
/// before the tool overlays and UI. A pixel without an id shows the frame darkened. Where the view has no ids the frame is
/// left unlit and the host says why (<see cref="LastResult"/>): Borealis draws no text.
/// </summary>
internal sealed class IdsViewLayer : RenderLayer
{
	static readonly StringToken DefaultMode = new( "Default" );
	static readonly StringToken TextureName = new( "DebugTexture" );
	static readonly StringToken ShowModeCombo = new( "D_DEBUG_SHOW_TEXTURE_MODE" );
	static readonly StringToken SrgbCombo = new( "D_DEBUG_SHOW_TEXTURE_SRGB" );

	/// <summary><c>debug_show_texture</c>'s RGBA mode: blended over the frame by its alpha.</summary>
	const int ShowRgba = 3;

	// Per layer, as a render system is made for one device
	GpuMaterial material;
	static int lastResult;
	static long lastAt;

	public IdsViewLayer() : base( "Visibility Ids View" ) { }

	/// <summary>The colours the last frame drew, for the tests (a transient: read it before the next frame).</summary>
	internal GpuTexture LastDrawn { get; private set; }

	/// <summary>What the last ids view drawn came to, for the host's text.</summary>
	internal static IdsViewResult LastResult => (IdsViewResult)System.Threading.Volatile.Read( ref lastResult );

	/// <summary>When the last ids view was drawn, in <see cref="Environment.TickCount64"/> milliseconds; 0 for never.</summary>
	internal static long LastAt => System.Threading.Volatile.Read( ref lastAt );

	// A camera's own view: not reflections, captures, the 3D skybox, or a child view drawn inside another's frame without stages
	public override bool IsNeeded( RenderFrame frame ) => ToolsViews.IdsViewOf( frame.View ) != IdsView.None && !frame.IsSkybox && !frame.View.Reflection
		&& !frame.View.Capture && (frame.Stages is not null || frame.World.Rendering <= 1);

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
	{
		var viewport = frame.View.Viewport;
		var (width, height) = ((int)viewport.Width, (int)viewport.Height);
		if ( width <= 0 || height <= 0 ) return;

		var colors = frame.System.Transients.Get( TextureDesc.Target( width, height, TextureFormat.RGBA8888 ) with { Storage = true }, "Ids View" );
		var result = rc.ResolveIdsView( frame.MainPass, colors, viewport, ToolsViews.IdsViewOf( frame.View ) );
		System.Threading.Volatile.Write( ref lastResult, (int)result );
		System.Threading.Volatile.Write( ref lastAt, Environment.TickCount64 );
		LastDrawn = result == IdsViewResult.Drawn ? colors : null;
		if ( result != IdsViewResult.Drawn ) return;

		rc.BarrierComputeToPixelShaderRead( colors );
		material ??= RenderHost.Current.Device.MaterialFromShader( "shaders/debug_show_texture.shader" );
		rc.Bind( frame.Target( rc, color: true, final: true ) with { Depth = null } );

		var parameters = ParameterPool.Get();
		parameters.SetCombo( ShowModeCombo, ShowRgba );
		parameters.SetCombo( SrgbCombo, 0 );
		parameters.Set( TextureName, colors );
		rc.DrawFullscreen( material, parameters, DefaultMode );
		ParameterPool.Return( parameters );
	}
}
