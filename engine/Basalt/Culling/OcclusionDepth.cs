namespace Basalt.Culling;

/// <summary>
/// A depth chain an occlusion cull tests against, and the view it was drawn from. Viewport is occlusion_cull_cs.shader's: the
/// view's width and height in pixels, the chain's mip count, and log2 of a mip 0 texel's width in pixels.
/// </summary>
internal readonly record struct OcclusionDepth( GpuTexture Chain, Vector4 Viewport, Vector2 DepthRange, Matrix WorldToProjection )
{
	/// <summary>
	/// A chain as big as its view, as the history keeps: a mip 0 texel is a pixel.
	/// </summary>
	public static OcclusionDepth FullResolution( GpuTexture chain, Vector2 depthRange, Matrix worldToProjection )
		=> new( chain, new Vector4( chain.Width, chain.Height, chain.Mips, 0 ), depthRange, worldToProjection );

	/// <summary>
	/// A conservative chain for the late cull to build into <paramref name="chain"/> (<see cref="QuarterChain"/>), a mip 0
	/// texel to every 4x4 pixels. The effects' chain isn't conservative: it only keeps MSAA sample 0, which misses
	/// alpha-to-coverage gaps.
	/// </summary>
	public static OcclusionDepth Quarter( GpuTexture chain, Rect viewport, Vector2 depthRange, Matrix worldToProjection )
	{
		// A mip 0 texel is 4 pixels wide, so w is 2
		return new( chain, new Vector4( (int)viewport.Width, (int)viewport.Height, chain.Mips, 2 ), depthRange, worldToProjection );
	}

	/// <summary>
	/// The size of a <see cref="Quarter"/> chain's first mip.
	/// </summary>
	public static Vector2Int QuarterSize( Rect viewport ) => new( ((int)viewport.Width + 3) / 4, ((int)viewport.Height + 3) / 4 );

	/// <summary>
	/// What a <see cref="Quarter"/> chain for <paramref name="viewport"/> is.
	/// </summary>
	public static TextureDesc QuarterChain( Rect viewport )
	{
		var size = QuarterSize( viewport );
		return SharedPasses.DepthChainDesc( size.x, size.y );
	}

	/// <summary>
	/// Binds what the cull tests against. Without <paramref name="previous"/>, everything counts as drawn by the early prepass.
	/// </summary>
	public static void Bind( ShaderParameters attributes, in OcclusionDepth current, in OcclusionDepth previous )
	{
		attributes.Set( "OcclusionDepthChain", current.Chain );
		attributes.Set( "OcclusionViewport", current.Viewport );
		attributes.Set( "OcclusionDepthRange", current.DepthRange );
		attributes.Set( "OcclusionWorldToProjection", current.WorldToProjection );

		// A zero mip count disables history, but something has to be bound
		var hasPrevious = previous.Chain is not null;
		attributes.Set( "OcclusionPreviousChain", hasPrevious ? previous.Chain : current.Chain );
		attributes.Set( "OcclusionPreviousViewport", hasPrevious ? previous.Viewport : Vector4.Zero );
		attributes.Set( "OcclusionPreviousDepthRange", previous.DepthRange );
		attributes.Set( "OcclusionPreviousWorldToProjection", previous.WorldToProjection );
	}
}
