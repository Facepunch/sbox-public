using NativeEngine;

namespace Sandbox.SceneRenderer.Gpu;

internal enum DepthResolveMode
{
	/// <summary>First sample only, as native does.</summary>
	Sample0,

	/// <summary>Min and max of every sample over each 4x4 pixels, so the chain starts at quarter resolution.</summary>
	ConservativeQuarter,
}

/// <summary>
/// Passes and targets this renderer and native's pipeline (<see cref="Native.NativeRenderPipeline"/>) share, so the two
/// build them the same way: the depth chain, quarter-resolution depth, and the bloom and refraction stencil targets.
/// </summary>
internal static class SharedPasses
{
	static readonly ComputeShader DepthResolveCompute = new( "shaders/depthresolve_cs.shader" );
	static readonly Material DepthResolve = Material.FromShader( "shaders/depthresolve.shader" );

	/// <summary>
	/// The depth chain's target for a viewport this size: a temporary RG32F texture with every mip, never multisampled.
	/// </summary>
	public static RenderTarget DepthChainTarget( int width, int height )
	{
		var numMips = (int)Math.Log2( Math.Min( width, height ) ) + 1;
		return RenderTarget.GetTemporary( width, height, ImageFormat.None, ImageFormat.RG3232F, MultisampleAmount.MultisampleNone, numMips );
	}

	/// <summary>
	/// Build the depth chain into <paramref name="destDepth"/>: the depth resolved into its first mip, then min and max
	/// downsampled into the rest. <paramref name="sourceDepth"/> is the depth to read, or null for what the native layer
	/// binds as <c>SourceDepth</c>.
	/// </summary>
	public static void BuildDepthChain( IRenderContext context, Texture sourceDepth, Texture destDepth, bool msaaInput, int width, int height, DepthResolveMode resolve = DepthResolveMode.Sample0 )
	{
		var attributes = RenderAttributes.Pool.Get();
		if ( sourceDepth is not null ) attributes.Set( "SourceDepth", sourceDepth );
		attributes.Set( "DestDepth", destDepth );
		attributes.SetCombo( "D_MSAA", msaaInput );
		attributes.SetCombo( "D_CONSERVATIVE_QUARTER", resolve == DepthResolveMode.ConservativeQuarter );

		if ( resolve == DepthResolveMode.ConservativeQuarter ) attributes.Set( "DepthResolveSourceSize", new Vector2( width, height ) );

		RenderTools.Compute( context, attributes.Get(), DepthResolveCompute.ComputeMaterial.native.GetMode(), width, height, 1 );

		RenderAttributes.Pool.Return( attributes );

		// Downsample using min max
		CSceneSystem.DownsampleTexture( context, destDepth.native, 5 ); /* DOWNSAMPLE_METHOD_MINMAX ( None of it is enumed properly? ) */
	}

	/// <summary>
	/// Write the depth at a quarter of the resolution into the bound depth target, from <c>SourceDepth</c> in
	/// <paramref name="attributes"/> (<paramref name="msaaInput"/> or not) - inside a render block.
	/// </summary>
	public static void DownsampleQuarterDepth( RenderAttributes attributes, bool msaaInput )
	{
		attributes.SetCombo( "D_MSAA", msaaInput );
		attributes.SetCombo( "D_UPSCALE", false );
		attributes.Set( "DownsampleFactor", 4 );
		Graphics.Blit( DepthResolve, attributes );
	}

	/// <summary>Write low-resolution scene depth into a full-resolution depth target.</summary>
	public static void UpscaleDepth( RenderAttributes attributes, bool msaaInput, Vector2 outputToSourceScale )
	{
		attributes.SetCombo( "D_MSAA", msaaInput );
		attributes.SetCombo( "D_UPSCALE", true );
		attributes.Set( "OutputToSourceScale", outputToSourceScale );
		Graphics.Blit( DepthResolve, attributes );
	}

	/// <summary>
	/// The bloom layer's target for a quarter viewport this size: a temporary RGBA1010102 colour and D32 depth. The bloom
	/// effect blurs it in its own downsample chain.
	/// </summary>
	public static RenderTarget BloomTarget( int width, int height )
	{
		return RenderTarget.GetTemporary( width, height, colorFormat: ImageFormat.RGBA1010102, depthFormat: ImageFormat.D32 );
	}

	/// <summary>
	/// The refraction stencil's target for a quarter viewport this size: a temporary D16 depth.
	/// </summary>
	public static RenderTarget RefractionStencilTarget( int width, int height )
	{
		return RenderTarget.GetTemporary( width, height, colorFormat: ImageFormat.None, depthFormat: ImageFormat.D16 );
	}
}
