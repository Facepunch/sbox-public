using NativeEngine;

namespace Sandbox;

public static partial class Graphics
{
	static ComputeShader _mipMapGeneratorShader;
	static ComputeShader _spdShader;
	static readonly StringToken[] _spdMips = Enumerable.Range( 0, 7 ).Select( i => new StringToken( $"Mip{i}" ) ).ToArray();

	/// <summary>
	/// Which method to use when downsampling a texture. The gaussian and GGX methods take a dispatch per mip, the others one per six.
	/// </summary>
	public enum DownsampleMethod
	{
		/// <summary>
		/// Uses a box filter to downsample the texture
		/// </summary>
		Box,
		/// <summary>
		/// Uses a gaussian filter to downsample the texture
		/// </summary>
		GaussianBlur,
		/// <summary>
		/// Uses a gaussian filter to downsample the texture, applies border to not oversample edges
		/// </summary>
		GaussianBorder,
		/// <summary>
		/// Downsamples the texture using a max operator filter ( brightest pixel )
		/// </summary>
		Max,
		/// <summary>
		/// Downsamples the texture using a min operator filter ( darkest pixel )
		/// </summary>
		Min,
		/// <summary>
		/// Downsamples the texture in red and green channels using a Min/Max filter ( darkest and brightest pixel )
		/// </summary>
		MinMax,
		/// <summary>
		/// Downsamples with a long-tailed GGX-like kernel, for reflection mip chains
		/// </summary>
		GGX,
		Default = 0,
		None = -1
	};

	/// <summary>
	/// Generate the mips of a UAV texture after <paramref name="initialMip"/> with downsample_spd: one dispatch per 6 mips
	/// instead of one per mip. Box, Max, Min and MinMax only.
	/// With <paramref name="fuseDepth"/> the first mip isn't read from <paramref name="texture"/> but resolved into it from depth
	/// (<paramref name="depth"/>, or what's bound as <c>SourceDepth</c> when null) as the first dispatch reads it: a RG32F min/max
	/// chain, MinMax only, needing at least two mips.
	/// </summary>
	internal static void DownsampleSpd( IRenderContext context, Texture texture, DownsampleMethod method, int initialMip = 0, bool fuseDepth = false, Texture depth = null, bool multisampled = false )
	{
		var mode = _spdShader.ComputeMaterial.native.GetMode();
		var attributes = RenderAttributes.Pool.Get();
		attributes.SetCombo( "D_DOWNSAMPLE_METHOD", (int)method );
		attributes.SetCombo( "D_FUSED_DEPTH", false );
		if ( fuseDepth )
		{
			attributes.SetCombo( "D_MSAA", multisampled );
			if ( depth is not null ) attributes.Set( "SourceDepth", depth );
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
				attributes.Set( _spdMips[i], texture, source + Math.Min( i, mipCount ) );

			if ( fuseDepth )
				attributes.Set( "Mip0Out", texture, source );
			attributes.SetCombo( "D_FUSED_DEPTH", fuseDepth );

			attributes.Set( "SourceSize", new Vector2Int( width, height ) );
			attributes.Set( "MipCount", mipCount );

			RenderTools.Compute( context, attributes.Get(), mode, (width + 63) / 64 * 256, (height + 63) / 64, 1 );

			ComputeUavBarrier( context, texture );

			source += mipCount;
			fuseDepth = false;
		}

		RenderAttributes.Pool.Return( attributes );
	}

	/// <summary>
	/// Generate the mip maps for this texture. Obviously the texture needs to support mip maps.
	/// </summary>
	public static void GenerateMipMaps( Texture texture, DownsampleMethod downsampleMethod = DownsampleMethod.Default, int initialMip = 0, int numMips = -1 )
	{
		ArgumentNullException.ThrowIfNull( texture );

		AssertRenderBlock();
		using var perfScope = Performance.Scope( "GenerateMips" );

		if ( !texture.Desc.m_nFlags.Contains( RuntimeTextureSpecificationFlags.TSPEC_UAV ) )
		{
			throw new Exception( "Texture must be created with UAV flag to generate mipmaps" );
		}

		if ( numMips == -1 )
		{
			numMips = texture.Desc.m_nNumMipLevels - initialMip;
		}

		if ( numMips <= initialMip )
		{
			return;
		}

		if ( downsampleMethod is not (DownsampleMethod.GaussianBlur or DownsampleMethod.GaussianBorder or DownsampleMethod.GGX) )
		{
			DownsampleSpd( Context, texture, downsampleMethod, initialMip );
			return;
		}

		// Set the attributes
		for ( int i = initialMip; i < texture.Mips - 1; i++ )
		{
			// We want the width and height of the next mip level
			int width = texture.Width >> (i + 1);
			int height = texture.Height >> (i + 1);

			// Don't attempt to generate mips smaller than 1x1
			if ( width < 1 || height < 1 )
				break;

			var attributes = RenderAttributes.Pool.Get();
			attributes.SetCombo( "D_DOWNSAMPLE_METHOD", (int)downsampleMethod );
			attributes.Set( $"MipLevel0", texture, i );
			attributes.Set( $"MipLevel1", texture, i + 1 );
			attributes.Set( "TextureSize", new Vector2( width, height ) );
			attributes.Set( "InvTextureSize", new Vector2( 1.0f / width, 1.0f / height ) );

			// And send to the GPU
			// GGX threads write 2x2 texels each
			if ( downsampleMethod == DownsampleMethod.GGX )
				_mipMapGeneratorShader.DispatchWithAttributes( attributes, (width + 1) / 2, (height + 1) / 2, 1 );
			else
				_mipMapGeneratorShader.DispatchWithAttributes( attributes, width, height, 1 );

			RenderAttributes.Pool.Return( attributes );

			// Add a UAV barrier for the specific mip level that was just written to
			// This ensures the compute shader dispatch completes before the next iteration
			// that will read from this mip level
			// matt: ... why not use the high level api
			Context.TextureBarrierTransition(
				texture.native,
				i + 1, // Specific mip level we just wrote to
				RenderBarrierPipelineStageFlags_t.ComputeShaderBit,
				RenderBarrierPipelineStageFlags_t.ComputeShaderBit,
				RenderImageLayout_t.RENDER_IMAGE_LAYOUT_GENERAL,
				RenderBarrierAccessFlags_t.ShaderWriteBit | RenderBarrierAccessFlags_t.ShaderReadBit,
				RenderBarrierAccessFlags_t.ShaderWriteBit | RenderBarrierAccessFlags_t.ShaderReadBit );
		}
	}
}
