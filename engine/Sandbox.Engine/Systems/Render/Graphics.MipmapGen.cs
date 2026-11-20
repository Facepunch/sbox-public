using NativeEngine;

namespace Sandbox;

public static partial class Graphics
{
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

		Backend.GenerateMipMaps( texture, downsampleMethod, initialMip );
	}
}
