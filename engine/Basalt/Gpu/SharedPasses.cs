namespace Basalt.Gpu;

internal enum DepthResolveMode
{
	/// <summary>First sample only, as native does.</summary>
	Sample0,

	/// <summary>Min and max of every sample over each 4x4 pixels, so the chain starts at quarter resolution.</summary>
	ConservativeQuarter,
}

/// <summary>
/// Depth passes several layers share, built the same way native's pipeline builds them and from the same shaders: the
/// depth chain, its min/max downsample, and quarter-resolution depth.
/// </summary>
internal static class SharedPasses
{
	static GpuMaterial depthResolveCompute, depthResolve, spd;

	static GpuMaterial DepthResolveCompute => depthResolveCompute ??= RenderHost.Current.Device.MaterialFromShader( "shaders/depthresolve_cs.shader" );
	static GpuMaterial DepthResolve => depthResolve ??= RenderHost.Current.Device.MaterialFromShader( "shaders/depthresolve.shader" );
	static GpuMaterial Spd => spd ??= RenderHost.Current.Device.MaterialFromShader( "downsample_spd" );

	/// <summary>
	/// Make the shaders on the main thread, before anything records with them. Layers call it before their segments.
	/// </summary>
	public static void WarmUp()
	{
		_ = DepthResolveCompute;
		_ = DepthResolve;
		_ = Spd;
	}

	static readonly StringToken SourceDepth = new( "SourceDepth" );
	static readonly StringToken DestDepth = new( "DestDepth" );
	static readonly StringToken Msaa = new( "D_MSAA" );
	static readonly StringToken ConservativeQuarter = new( "D_CONSERVATIVE_QUARTER" );
	static readonly StringToken DepthResolveSourceSize = new( "DepthResolveSourceSize" );
	static readonly StringToken Upscale = new( "D_UPSCALE" );
	static readonly StringToken DownsampleFactor = new( "DownsampleFactor" );
	static readonly StringToken DownsampleMethod = new( "D_DOWNSAMPLE_METHOD" );
	static readonly StringToken FusedDepth = new( "D_FUSED_DEPTH" );
	static readonly StringToken Mip0Out = new( "Mip0Out" );
	static readonly StringToken SourceSize = new( "SourceSize" );
	static readonly StringToken MipCount = new( "MipCount" );
	static readonly StringToken[] SpdMips = [new( "Mip0" ), new( "Mip1" ), new( "Mip2" ), new( "Mip3" ), new( "Mip4" ), new( "Mip5" ), new( "Mip6" )];
	static readonly StringToken ForwardMode = new( "Forward" );

	/// <summary>
	/// <c>downsample_spd</c>'s min/max method (the engine's <c>Graphics.DownsampleMethod.MinMax</c>).
	/// </summary>
	const int MinMax = 5;

	/// <summary>
	/// The depth chain's target for a viewport this size: RG32F with every mip, never multisampled.
	/// </summary>
	public static TextureDesc DepthChainDesc( int width, int height ) => TextureDesc.Target( width, height, TextureFormat.RG3232F, mips: (int)Math.Log2( Math.Min( width, height ) ) + 1 ) with { Storage = true };

	/// <summary>
	/// Build the depth chain into <paramref name="destDepth"/>: the depth resolved into its first mip, then min and max
	/// downsampled into the rest.
	/// </summary>
	public static void BuildDepthChain( GpuContext context, GpuTexture sourceDepth, GpuTexture destDepth, bool msaaInput, int width, int height, DepthResolveMode resolve = DepthResolveMode.Sample0 )
	{
		context.Invalidate();

		// The first six mips come from the depth itself in one dispatch, no pass resolving it first. Not on the async compute
		// queue, where the one heavy dispatch left graphics slower than the resolve and downsample as two lighter ones
		if ( resolve == DepthResolveMode.Sample0 && Math.Min( width, height ) >= 2 && destDepth.Mips > 1 && !context.AsyncCompute )
		{
			DownsampleSpd( context, destDepth, MinMax, fuseDepth: true, depth: sourceDepth, multisampled: msaaInput );
			return;
		}

		var parameters = ParameterPool.Get();
		if ( sourceDepth is not null ) parameters.Set( SourceDepth, sourceDepth );
		parameters.Set( DestDepth, destDepth );
		parameters.SetCombo( Msaa, msaaInput );
		parameters.SetCombo( ConservativeQuarter, resolve == DepthResolveMode.ConservativeQuarter );

		if ( resolve == DepthResolveMode.ConservativeQuarter )
		{
			parameters.Set( DepthResolveSourceSize, new Vector2( width, height ) );
			context.Dispatch( DepthResolveCompute, parameters, width, height, 1 );
		}
		else
		{
			// Four texels a thread
			context.Dispatch( DepthResolveCompute, parameters, (width + 1) / 2, (height + 1) / 2, 1 );
		}

		ParameterPool.Return( parameters );
		StorageBarrier( context, destDepth );

		// Min/max into the rest of the mips
		DownsampleSpd( context, destDepth, MinMax );

		// Finish the producer's layout transition before graphics and compute sample the chain concurrently
		if ( context.AsyncCompute )
			context.TextureBarrier( destDepth, GpuStage.ComputeShader, GpuAccess.ShaderWrite, GpuStage.ComputeShader, GpuAccess.ShaderRead, TextureLayout.ShaderRead );
	}

	/// <summary>
	/// Downsample a texture's mips from <paramref name="initialMip"/> with <c>downsample_spd</c>, up to six a dispatch. Fused,
	/// the first dispatch also resolves <paramref name="depth"/> into the first mip (the engine's <c>Graphics.DownsampleSpd</c>).
	/// </summary>
	static void DownsampleSpd( GpuContext context, GpuTexture texture, int method, int initialMip = 0, bool fuseDepth = false, GpuTexture depth = null, bool multisampled = false )
	{
		var parameters = ParameterPool.Get();
		parameters.SetCombo( DownsampleMethod, method );
		parameters.SetCombo( FusedDepth, false );
		if ( fuseDepth )
		{
			parameters.SetCombo( Msaa, multisampled );
			if ( depth is not null ) parameters.Set( SourceDepth, depth );
		}

		// Down to the smaller side's 1 texel mip
		var lastMip = Math.Min( texture.Mips - 1, System.Numerics.BitOperations.Log2( (uint)Math.Min( texture.Width, texture.Height ) ) );
		for ( int source = initialMip; source < lastMip; )
		{
			int width = texture.Width >> source;
			int height = texture.Height >> source;
			int mipCount = Math.Min( lastMip - source, 6 );

			// Mips past the count are never written, bind the last one so every slot is valid
			for ( int i = fuseDepth ? 1 : 0; i <= 6; i++ )
				parameters.Set( SpdMips[i], texture, source + Math.Min( i, mipCount ) );

			if ( fuseDepth )
				parameters.Set( Mip0Out, texture, source );
			parameters.SetCombo( FusedDepth, fuseDepth );

			parameters.Set( SourceSize, new Vector2Int( width, height ) );
			parameters.Set( MipCount, mipCount );

			context.Dispatch( Spd, parameters, (width + 63) / 64 * 256, (height + 63) / 64, 1 );
			StorageBarrier( context, texture );

			source += mipCount;
			fuseDepth = false;
		}

		ParameterPool.Return( parameters );
	}

	/// <summary>
	/// Compute writes to a texture finish before later compute reads or writes it (the engine's <c>Graphics.ComputeUavBarrier</c>).
	/// </summary>
	static void StorageBarrier( GpuContext context, GpuTexture texture )
	{
		const GpuAccess access = GpuAccess.ShaderRead | GpuAccess.ShaderWrite;
		context.TextureBarrier( texture, GpuStage.ComputeShader, access, GpuStage.ComputeShader, access, TextureLayout.General );
	}

	/// <summary>
	/// Write <paramref name="depth"/> at a quarter of the resolution into the bound depth target.
	/// </summary>
	public static void DownsampleQuarterDepth( GpuContext context, GpuTexture depth, bool msaaInput )
	{
		context.Invalidate();

		var parameters = ParameterPool.Get();
		parameters.Set( SourceDepth, depth );
		parameters.SetCombo( Msaa, msaaInput );
		parameters.SetCombo( Upscale, false );
		parameters.Set( DownsampleFactor, 4 );
		context.DrawFullscreen( DepthResolve, parameters, ForwardMode );
		ParameterPool.Return( parameters );
	}
}
