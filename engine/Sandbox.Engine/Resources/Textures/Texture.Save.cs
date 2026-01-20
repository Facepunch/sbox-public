using Sandbox.Resources;

namespace Sandbox;

public partial class Texture
{
	/// <summary>
	/// Creates a VTEX file from this texture.
	/// </summary>
	/// <remarks>
	/// For cubemaps, this assumes the texture was rendered for use with inverted scale sampling
	/// (like EnvmapProbe's dynamic rendering). Face pairs are swapped and flipped to compensate
	/// so the saved texture can be sampled normally.
	/// </remarks>
	public byte[] SaveToVtex()
	{
		var desc = Desc;

		if ( desc.m_nFlags.HasFlag( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_TEXTURE_ARRAY ) )
		{
			throw new System.NotSupportedException( "SaveToVtex does not yet support texture arrays" );
		}

		if ( desc.m_nFlags.HasFlag( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_VOLUME_TEXTURE ) )
		{
			throw new System.NotSupportedException( "SaveToVtex does not yet support volume textures" );
		}

		bool isCubemap = desc.m_nFlags.HasFlag( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_CUBE_TEXTURE );
		int numFaces = isCubemap ? 6 : 1;

		int width = Desc.m_nWidth;
		int height = Desc.m_nHeight;
		int depth = 1; // For vtex format, depth is 1 for cubemaps (face count is implied by flag)
		int mipCount = Desc.m_nNumMipLevels;

		var writer = new VTexWriter();

		// For cubemaps rendered with inverted scale sampling, we need to:
		// 1. Swap face pairs (+X/-X, +Y/-Y, +Z/-Z) because the lookup direction is negated
		// 2. Apply per-axis flips because UV coordinates change when direction is negated
		int[] cubemapFaceRemap = { 1, 0, 3, 2, 5, 4 };

		for ( var mip = 0; mip < mipCount; mip++ )
		{
			if ( isCubemap )
			{
				for ( int face = 0; face < 6; face++ )
				{
					var faceBitmap = GetFaceBitmap( mip, cubemapFaceRemap[face] );
					writer.SetTexture( faceBitmap, mip, face );
				}
			}
			else
			{
				var bitmap = GetBitmap( mip );
				writer.SetTexture( bitmap, mip );
			}
		}

		writer.Header.Width = (ushort)width;
		writer.Header.Height = (ushort)height;
		writer.Header.Depth = (ushort)depth;
		writer.Header.MipCount = (byte)mipCount;

		var flags = VTexWriter.VTEX_Flags_t.NONE;

		if ( isCubemap )
			flags |= VTexWriter.VTEX_Flags_t.VTEX_FLAG_CUBE_TEXTURE;

		if ( desc.m_nFlags.HasFlag( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_NO_LOD ) )
			flags |= VTexWriter.VTEX_Flags_t.VTEX_FLAG_NO_LOD;

		writer.Header.Flags = flags;

		writer.CalculateFormat();

		var vtexData = writer.GetData();
		var streamingData = writer.GetStreamingData();

		// Calculate expected size for validation (cubemaps need size * 6)
		var outputFormat = VTexWriter.VTEX_FormatToRuntime( writer.Header.Format );
		var expectedSize = NativeEngine.ImageLoader.GetMemRequired( width, height, depth, mipCount, outputFormat ) * numFaces;

		if ( streamingData.Length != expectedSize )
		{
			Log.Warning( $"SaveToVtex: Size mismatch for {width}x{height} {writer.Header.Format}! Got {streamingData.Length} bytes but expected {expectedSize}" );
		}

		// Write Source 2 resource container format
		var resource = new ResourceWriter();
		resource.ResourceVersion = 1; // TEXTURE_RESOURCE_VERSION_NUMBER
		resource.SetDataBlock( vtexData );
		resource.SetStreamingData( streamingData );

		return resource.ToArray();
	}

	/// <summary>
	/// Gets a single face from a cubemap texture as a bitmap, applying the necessary
	/// flip transformation for cubemaps rendered with inverted scale sampling.
	/// </summary>
	private unsafe Bitmap GetFaceBitmap( int mip, int sourceFace )
	{
		mip = Math.Clamp( mip, 0, Mips - 1 );
		var d = 1 << mip;

		bool floatingPoint = ImageFormat == ImageFormat.RGBA16161616F;

		var desc = Desc;
		var width = desc.m_nWidth / d;
		var height = desc.m_nHeight / d;
		var outputFormat = floatingPoint ? ImageFormat.RGBA16161616F : ImageFormat.RGBA8888;

		var bitmap = new Bitmap( width, height, floatingPoint );
		var data = bitmap.GetBuffer();

		GetPixels( (0, 0, width, height), sourceFace, mip, data, outputFormat );

		// Cubemaps rendered for inverted scale sampling need per-axis flips.
		// When the lookup direction is negated, UV coordinates change:
		// - X axis faces: V coordinate inverts
		// - Y axis faces: U coordinate inverts
		// - Z axis faces: V coordinate inverts
		return sourceFace switch
		{
			0 or 1 => bitmap.FlipVertical(),   // X faces: flip V
			2 or 3 => bitmap.FlipHorizontal(), // Y faces: flip U
			4 or 5 => bitmap.FlipVertical(),   // Z faces: flip V
			_ => bitmap
		};
	}

	/// <summary>
	/// Asynchronously saves the current data to the VTEX platform and returns the resulting byte array.
	/// </summary>
	public async Task<byte[]> SaveToVtexAsync()
	{
		return await Task.Run( () => SaveToVtex() );
	}
}
