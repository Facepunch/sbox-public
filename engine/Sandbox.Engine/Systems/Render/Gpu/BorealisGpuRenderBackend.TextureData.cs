using Borealis.Gpu;
using GpuFormat = Borealis.Gpu.Format;

namespace Sandbox;

/// <summary>
/// Textures made with data of more than one plain image: arrays, cubes, volumes, mips of their own, compressed and float formats.
/// </summary>
internal sealed partial class BorealisGpuRenderBackend
{
	/// <summary>
	/// A texture made with data that isn't one plain image (an array, a cube, a volume, mips of its own, a compressed format),
	/// as Source 2 takes it: every mip from the smallest, each with every slice, or only the first mip with the rest filled
	/// from it. Null for a plain image (made below from its first mip), data shorter than the first mip, or a format with no
	/// Borealis.Gpu form.
	/// </summary>
	TextureHandle? FromData( Texture texture )
	{
		if ( texture.InitialPixels is not { } data ) return null;

		var format = UploadFormatOf( texture.ImageFormat );
		if ( format == GpuFormat.Unknown ) return null;

		var desc = texture.Desc;
		var volume = desc.m_nFlags.Contains( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_VOLUME_TEXTURE );
		var info = new TextureInfo
		{
			Width = texture.Width,
			Height = texture.Height,
			Depth = volume ? Math.Max( (int)desc.m_nDepth, 1 ) : 1,
			Layers = volume ? 1 : desc.IsCube ? Math.Max( desc.ArrayCount / 6, 1 ) : Math.Max( desc.ArrayCount, 1 ),
			Mips = Math.Max( texture.Mips, 1 ),
			Samples = 1,
			Dimension = volume ? TextureDimension.Texture3D
				: desc.IsCube ? (desc.IsArray ? TextureDimension.TextureCubeArray : TextureDimension.TextureCube)
				: desc.IsArray ? TextureDimension.Texture2DArray : TextureDimension.Texture2D,
			Format = format,
			Usage = TextureUsage.Sampled | TextureUsage.CopyDestination | TextureUsage.CopySource,
			Name = $"Engine image {texture.Width}x{texture.Height}x{texture.Depth}",
		};

		var whole = HoldsEveryMip( info, data );
		if ( whole is null ) return null;

		// A plain image, or one with only its first mip: made from it below, as before
		var shaped = volume || desc.IsArray || desc.IsCube;
		if ( !shaped && !format.IsCompressed() && !(whole.Value && info.Mips > 1) ) return null;

		// Mips past the first are made by halving it, which a compressed format can't take
		if ( !whole.Value && format.IsCompressed() ) info.Mips = 1;
		if ( desc.m_nFlags.Contains( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_UAV ) && !format.IsCompressed() ) info.Usage |= TextureUsage.Storage;

		var handle = gpu.CreateTexture( info );
		Write( handle, info, data, whole.Value );
		return handle;
	}

	/// <summary>
	/// An array or cube made with no pixels (an atlas a game fills a layer at a time with <see cref="Texture.Update3D"/>):
	/// every layer in its own format, storage when made for compute, cleared to the colour it was made with. Null for
	/// another texture, a format with no Borealis.Gpu form, or a cube made for compute (a realtime probe's, which cube
	/// captures draw into instead).
	/// </summary>
	TextureHandle? LayeredOf( Texture texture )
	{
		var desc = texture.Desc;
		var storage = desc.m_nFlags.Contains( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_UAV );
		if ( !(desc.IsArray || desc.IsCube) || texture.InitialPixels is not null || (desc.IsCube && storage) ) return null;

		var format = DataFormatOf( texture.ImageFormat );
		if ( format == GpuFormat.Unknown ) return null;

		var info = new TextureInfo
		{
			Width = texture.Width,
			Height = texture.Height,
			Depth = 1,
			Layers = desc.IsCube ? Math.Max( desc.ArrayCount / 6, 1 ) : Math.Max( desc.ArrayCount, 1 ),
			Mips = Math.Max( texture.Mips, 1 ),
			Samples = 1,
			Dimension = desc.IsCube ? (desc.IsArray ? TextureDimension.TextureCubeArray : TextureDimension.TextureCube) : TextureDimension.Texture2DArray,
			Format = format,
			Usage = TextureUsage.Sampled | TextureUsage.CopyDestination | TextureUsage.CopySource | (storage ? TextureUsage.Storage : 0),
			Name = $"Engine layers {texture.Width}x{texture.Height}x{desc.ArrayCount}",
		};

		var handle = gpu.CreateTexture( info );
		ClearNew( handle, texture.InitialColor ?? Color.Transparent );
		return handle;
	}

	/// <summary>
	/// Whether <paramref name="data"/> holds every mip of a texture (true) or only its first (false), or null when it's
	/// shorter than that.
	/// </summary>
	static bool? HoldsEveryMip( in TextureInfo info, byte[] data )
	{
		long chain = 0;
		for ( int mip = 0; mip < info.Mips; mip++ ) chain += MipBytes( info, mip );
		if ( data.Length >= chain ) return true;
		return data.Length >= MipBytes( info, 0 ) ? false : null;
	}

	/// <summary>
	/// Write a new texture's data in Source 2's order - the smallest mip first, each one's slices in turn (a volume's mip is
	/// all its slices at once) - or only its first mip, the rest filled from it; recorded where uploads go. It ends in
	/// <see cref="ImageLayout.ShaderRead"/>.
	/// </summary>
	void Write( TextureHandle handle, in TextureInfo info, byte[] data, bool everyMip )
	{
		var target = UploadList();
		target.Barrier( TextureBarrier.Transition( handle, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.AllCommands, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );

		var layers = ArrayLayersOf( info );
		var position = 0;
		for ( int mip = everyMip ? info.Mips - 1 : 0; mip >= 0; mip-- )
		{
			int width = Math.Max( info.Width >> mip, 1 ), height = Math.Max( info.Height >> mip, 1 );
			var slice = (int)(MipBytes( info, mip ) / layers);
			for ( int layer = 0; layer < layers; layer++, position += slice )
			{
				target.UpdateTexture( handle, data.AsSpan( position, slice ), mip, layer, width, height );
			}
		}

		if ( everyMip || info.Mips == 1 )
		{
			target.Barrier( TextureBarrier.Transition( handle, ImageLayout.CopyDestination, ImageLayout.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead ) );
		}
		else
		{
			target.Barrier( MipBarrier( handle, 0, ImageLayout.CopyDestination, ImageLayout.ShaderRead ) );
			FillMips( target, handle, info, ImageLayout.CopyDestination );
		}

		Track( handle, ImageLayout.ShaderRead );
		TextureUploads++;
	}

	/// <summary>Bytes of one mip of a texture, every layer (or a volume's every slice), tightly packed.</summary>
	static long MipBytes( in TextureInfo info, int mip )
	{
		int width = Math.Max( info.Width >> mip, 1 ), height = Math.Max( info.Height >> mip, 1 );
		var slices = info.Dimension == TextureDimension.Texture3D ? Math.Max( info.Depth >> mip, 1 ) : ArrayLayersOf( info );
		return (long)info.Format.RowPitch( width ) * info.Format.Rows( height ) * slices;
	}

	/// <summary>A texture's array layers as Vulkan counts them: six a cube, one a volume.</summary>
	static int ArrayLayersOf( in TextureInfo info ) => info.Dimension switch
	{
		TextureDimension.Texture3D => 1,
		TextureDimension.TextureCube or TextureDimension.TextureCubeArray => 6 * Math.Max( info.Layers, 1 ),
		_ => Math.Max( info.Layers, 1 ),
	};

	/// <summary>The Borealis.Gpu form of an engine format data can be uploaded in as it is, compressed ones too, or Unknown.</summary>
	static GpuFormat UploadFormatOf( ImageFormat format ) => format switch
	{
		ImageFormat.DXT1 or ImageFormat.DXT1_ONEBITALPHA => GpuFormat.BC1Unorm,
		ImageFormat.DXT5 => GpuFormat.BC3Unorm,
		ImageFormat.ATI1N => GpuFormat.BC4Unorm,
		ImageFormat.ATI2N => GpuFormat.BC5Unorm,
		ImageFormat.BC6H => GpuFormat.BC6HUfloat,
		ImageFormat.BC7 => GpuFormat.BC7Unorm,
		_ => DataFormatOf( format ),
	};
}
