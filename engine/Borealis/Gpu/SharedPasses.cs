namespace Borealis.Gpu;

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
	static GpuMaterial DepthResolveCompute => RenderHost.Current.Device.PassMaterial( GpuPassMaterial.DepthResolveCompute );
	static GpuMaterial DepthResolve => RenderHost.Current.Device.PassMaterial( GpuPassMaterial.DepthResolve );
	static GpuMaterial Spd => RenderHost.Current.Device.PassMaterial( GpuPassMaterial.Spd );

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
	static readonly StringToken QuarterDepthSourceViewport = new( "QuarterDepthSourceViewport" );
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
	internal const int MinMax = 5;

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

		// The first six mips can come directly from the depth on the graphics queue.
		if ( resolve == DepthResolveMode.Sample0 && Math.Min( width, height ) >= 2 && destDepth.Mips > 1 )
		{
			DownsampleSpd( context, destDepth, fuseDepth: true, depth: sourceDepth, multisampled: msaaInput );
			return;
		}

		ResolveDepth( context, sourceDepth, destDepth, msaaInput, width, height, resolve );
		StorageBarrier( context, destDepth );

		// Min/max into the rest of the mips
		DownsampleSpd( context, destDepth );
	}

	static void ResolveDepth( GpuContext context, GpuTexture source, GpuTexture destination, bool msaaInput, int width, int height, DepthResolveMode resolve )
	{
		using var lease = ParameterPool.Borrow( out var parameters );
		if ( source is not null ) parameters.Set( SourceDepth, source );
		parameters.Set( DestDepth, destination );
		parameters.SetCombo( Msaa, msaaInput );
		var quarter = resolve == DepthResolveMode.ConservativeQuarter;
		parameters.SetCombo( ConservativeQuarter, quarter );
		if ( quarter )
		{
			parameters.Set( DepthResolveSourceSize, new Vector2( width, height ) );
			context.Dispatch( DepthResolveCompute, parameters, width, height, 1 );
		}
		else
		{
			// Four texels a thread.
			context.Dispatch( DepthResolveCompute, parameters, (width + 1) / 2, (height + 1) / 2, 1 );
		}
	}

	// Both graphics contexts use the same dispatch shape and mip batches for downsample_spd.
	internal const int MipsPerSpdBatch = 6;
	internal static int LastSpdMip( int width, int height, int mips )
		=> Math.Min( mips - 1, System.Numerics.BitOperations.Log2( (uint)Math.Min( width, height ) ) );

	internal static (int Width, int Height, int Count) SpdBatch( int width, int height, int source, int lastMip )
		=> (width >> source, height >> source, Math.Min( lastMip - source, MipsPerSpdBatch ));

	internal static (int X, int Y) SpdDispatch( int width, int height ) => ((width + 63) / 64 * 256, (height + 63) / 64);

	/// <summary>
	/// Downsample a depth chain with <c>downsample_spd</c>, up to six mips a dispatch. Fused,
	/// the first dispatch also resolves <paramref name="depth"/> into the first mip (the engine's <c>Graphics.DownsampleSpd</c>).
	/// </summary>
	static void DownsampleSpd( GpuContext context, GpuTexture texture, bool fuseDepth = false, GpuTexture depth = null, bool multisampled = false )
	{
		using var lease = ParameterPool.Borrow( out var parameters );
		parameters.SetCombo( DownsampleMethod, MinMax );
		if ( fuseDepth )
		{
			parameters.SetCombo( Msaa, multisampled );
			if ( depth is not null ) parameters.Set( SourceDepth, depth );
		}

		var material = Spd;
		var lastMip = LastSpdMip( texture.Width, texture.Height, texture.Mips );
		for ( int source = 0; source < lastMip; )
		{
			var (width, height, mipCount) = SpdBatch( texture.Width, texture.Height, source, lastMip );

			// Mips past the count are never written, bind the last one so every slot is valid
			for ( int i = fuseDepth ? 1 : 0; i <= MipsPerSpdBatch; i++ )
				parameters.Set( SpdMips[i], texture, source + Math.Min( i, mipCount ) );

			if ( fuseDepth )
				parameters.Set( Mip0Out, texture, source );
			parameters.SetCombo( FusedDepth, fuseDepth );

			parameters.Set( SourceSize, new Vector2Int( width, height ) );
			parameters.Set( MipCount, mipCount );

			var (x, y) = SpdDispatch( width, height );
			context.Dispatch( material, parameters, x, y, 1 );
			StorageBarrier( context, texture );

			source += mipCount;
			fuseDepth = false;
		}
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
	/// Write <paramref name="depth"/> at a quarter of the resolution into the bound depth target. It draws into what's bound,
	/// so it keeps the binding (and the depth clear pending on it).
	/// </summary>
	public static void DownsampleQuarterDepth( GpuContext context, GpuTexture depth, bool msaaInput, Rect sourceViewport )
	{
		using var lease = ParameterPool.Borrow( out var parameters );
		parameters.Set( SourceDepth, depth );
		parameters.SetCombo( Msaa, msaaInput );
		parameters.SetCombo( Upscale, false );
		parameters.Set( QuarterDepthSourceViewport, new Vector4( sourceViewport.Left, sourceViewport.Top, sourceViewport.Width, sourceViewport.Height ) );
		context.DrawFullscreen( DepthResolve, parameters, ForwardMode );
	}
}
