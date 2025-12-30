using NativeEngine;

namespace Sandbox;

public static partial class Graphics
{
	internal static ComputeShader MipMapGeneratorShader;

	internal static void InitStatic()
	{
		MipMapGeneratorShader = new ComputeShader( "downsample_cs" );
	}

	internal static void DisposeStatic()
	{
		MipMapGeneratorShader?.Dispose();
		MipMapGeneratorShader = null;
	}

	/// <summary>
	/// Which method to use when downsampling a texture
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
		Default = 0,
		None = -1
	};

	/// <summary>
	/// Generate the mip maps for this texture. Obviously the texture needs to support mip maps.
	/// </summary>
	public static void GenerateMipMaps( Texture texture, DownsampleMethod downsampleMethod = DownsampleMethod.Default, int initialMip = 0, int numMips = -1 )
	{
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
			MipMapGeneratorShader.DispatchWithAttributes( attributes, width, height, 1 );

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
