using System;
using Sandbox.Mounting;

namespace Sandbox.Mounting.Source1;

internal sealed partial class TextureLoader
{
	private static (ImageFormat Format, byte[] Pixels, int MipCount, int SkippedTopMips) ReadTextureData( VtfFile vtf, int? frame )
	{
		var width = vtf.Header.Width;
		var height = vtf.Header.Height;
		var sourceFormat = vtf.Header.HiResFormat;
		var (format, needsConversion) = GetImageFormat( sourceFormat );
		if ( format == ImageFormat.None )
			return (format, null, 0, 0);

		var mipCount = vtf.GetTextureMipmapCount();

		// Shim: the native renderer downsamples large textures (observed: a 2048-wide
		// image allocated at 1024 on male07 in group3 dir for hl2 content) 
		// but still uploads every source mip at full size,
		// overflowing the GPU image and losing the device. Pre-drop the top
		// mips so the declared size matches the native allocation.
		var skippedTopMips = TopMipsToSkip( width, height, mipCount );
		if ( skippedTopMips > 0 )
			Log.Trace( $"Texture exceeds the native size budget, dropping {skippedTopMips} top mip(s) ({width}x{height})." );

		var isCube = vtf.FaceCount > 1;
		var compressed = sourceFormat is VtfFormat.DXT1 or VtfFormat.DXT1_ONEBITALPHA
			or VtfFormat.DXT3 or VtfFormat.DXT5;
		// FindOrCreateTexture2 incorrectly adds render-target flags to multi-mip cubes.
		// DXT cannot be a render target; decoding also preserves sub-4x4 authored mips.
		var decompress = isCube && mipCount > 1 && compressed;
		if ( decompress )
		{
			format = ImageFormat.RGBA8888;
			needsConversion = true;
		}

		var faceCount = isCube ? 6 : 1;
		var imageCount = faceCount * (frame.HasValue ? 1 : vtf.FrameCount);
		if ( skippedTopMips == 0 && !needsConversion && vtf.GetTexturePixelData( mipCount, faceCount ) is { } pixels )
			return (format, pixels, mipCount, 0);

		var convertedPixelSize = format == ImageFormat.RGBA32323232F ? 16 : 4;
		var totalSize = 0;
		for ( var mip = skippedTopMips; mip < mipCount; mip++ )
		{
			var length = needsConversion
				? checked(Math.Max( 1, width >> mip ) * Math.Max( 1, height >> mip ) * convertedPixelSize)
				: vtf.GetHiResPixelDataLength( mip );
			totalSize = checked(totalSize + checked(imageCount * length));
		}

		var data = new byte[totalSize];
		var offset = 0;
		for ( var mip = mipCount - 1; mip >= skippedTopMips; mip-- )
		{
			var mipWidth = Math.Max( 1, width >> mip );
			var mipHeight = Math.Max( 1, height >> mip );
			var length = needsConversion ? checked(mipWidth * mipHeight * convertedPixelSize)
				: vtf.GetHiResPixelDataLength( mip );
			for ( var image = 0; image < imageCount; image++ )
			{
				var source = vtf.GetHiResPixelData( mip, frame: frame ?? image, face: isCube ? image : 0 );
				var destination = data.AsSpan( offset, length );
				if ( decompress )
					VtfFile.DecompressDxt( source, destination, sourceFormat, mipWidth, mipHeight );
				else if ( needsConversion )
					VtfFile.ConvertPixels( source, destination, sourceFormat );
				else
					source.CopyTo( destination );
				offset += length;
			}
		}
		return (format, data, mipCount - skippedTopMips, skippedTopMips);
	}

	/// <summary>
	/// How many top mip levels to drop so the largest dimension fits the native
	/// texture budget. Override the budget with SBOX_SOURCE1_TEXTURE_MAXSIZE.
	/// </summary>
	private static int TopMipsToSkip( int width, int height, int mipCount )
	{
		var maxSize = 1024;
		if ( int.TryParse( Environment.GetEnvironmentVariable( "SBOX_SOURCE1_TEXTURE_MAXSIZE" ), out var configured ) && configured > 0 )
			maxSize = configured;
		var skip = 0;
		while ( Math.Max( width >> skip, height >> skip ) > maxSize && skip < mipCount - 1 )
			skip++;
		return skip;
	}

	internal static (ImageFormat Format, bool NeedsConversion) GetImageFormat( VtfFormat format ) => format switch
	{
		VtfFormat.DXT1 => (ImageFormat.DXT1, false),
		VtfFormat.DXT1_ONEBITALPHA => (ImageFormat.DXT1_ONEBITALPHA, false),
		VtfFormat.DXT3 => (ImageFormat.DXT3, false),
		VtfFormat.DXT5 => (ImageFormat.DXT5, false),
		VtfFormat.RGBA8888 or VtfFormat.UVWQ8888
			or VtfFormat.UVLX8888 => (ImageFormat.RGBA8888, false),
		VtfFormat.BGRA8888 => (ImageFormat.BGRA8888, false),
		VtfFormat.RGBA16161616F => (ImageFormat.RGBA16161616F, false),
		VtfFormat.RGBA16161616 => (ImageFormat.RGBA16161616, false),
		VtfFormat.R32F => (ImageFormat.R32F, false),
		VtfFormat.RGB323232F => (ImageFormat.RGBA32323232F, true),
		VtfFormat.RGBA32323232F => (ImageFormat.RGBA32323232F, false),
		VtfFormat.BGR888 or VtfFormat.RGB888 or VtfFormat.ARGB8888
			or VtfFormat.ABGR8888 or VtfFormat.BGRX8888
			or VtfFormat.RGB565 or VtfFormat.BGR565 or VtfFormat.BGRA4444
			or VtfFormat.BGRX5551 or VtfFormat.BGRA5551
			or VtfFormat.RGB888_BLUESCREEN or VtfFormat.BGR888_BLUESCREEN
			or VtfFormat.I8 or VtfFormat.IA88 or VtfFormat.A8
			or VtfFormat.UV88 => (ImageFormat.RGBA8888, true),
		_ => (ImageFormat.None, false)
	};
}
