using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Sandbox.Mounting.Source1;

internal sealed partial class VtfFile
{
	internal Vector4[] ReadRgbaPixels( int frame, (int X, int Y, int Width, int Height) region )
	{
		if ( FaceCount != 1 || ZSliceCount != 1 )
			throw new NotSupportedException( "Decal textures must be two-dimensional." );
		if ( region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0
			|| (long)region.X + region.Width > Header.Width || (long)region.Y + region.Height > Header.Height )
			throw new ArgumentOutOfRangeException( nameof( region ), "Pixel region extends outside its texture." );
		var format = Header.HiResFormat;
		var source = GetHiResPixelData( 0, frame: frame );
		var pixels = new Vector4[checked(region.Width * region.Height)];
		byte[] converted = null;
		if ( format is VtfFormat.DXT1 or VtfFormat.DXT1_ONEBITALPHA or VtfFormat.DXT3 or VtfFormat.DXT5 )
		{
			converted = new byte[checked(pixels.Length * 4)];
			DecompressDxt( source, converted, format, Header.Width, Header.Height, region );
			format = VtfFormat.RGBA8888;
		}
		else if ( format is not (VtfFormat.RGBA8888 or VtfFormat.BGRA8888 or VtfFormat.RGBA16161616F
			or VtfFormat.RGBA16161616 or VtfFormat.RGB323232F or VtfFormat.RGBA32323232F) )
		{
			converted = new byte[checked(pixels.Length * 4)];
			int sourceStride = GetImageDataSize( 1, 1, 1, 1, format );
			for ( int y = 0; y < region.Height; y++ )
				ConvertPixels( source.Slice( ((region.Y + y) * Header.Width + region.X) * sourceStride, region.Width * sourceStride ),
					converted.AsSpan( y * region.Width * 4, region.Width * 4 ), format );
			format = VtfFormat.RGBA8888;
		}
		if ( converted is not null ) source = converted;
		var stride = format switch
		{
			VtfFormat.RGBA16161616F or VtfFormat.RGBA16161616 => 8,
			VtfFormat.RGB323232F => 12,
			VtfFormat.RGBA32323232F => 16,
			_ => 4
		};
		for ( var i = 0; i < pixels.Length; i++ )
		{
			int offset = converted is not null ? i : (region.Y + i / region.Width) * Header.Width + region.X + i % region.Width;
			var pixel = source.Slice( offset * stride, stride );
			if ( format == VtfFormat.RGBA16161616F )
			{
				var channels = MemoryMarshal.Cast<byte, Half>( pixel );
				pixels[i] = new( (float)channels[0], (float)channels[1], (float)channels[2], (float)channels[3] );
			}
			else if ( format == VtfFormat.RGBA16161616 )
			{
				var channels = MemoryMarshal.Cast<byte, ushort>( pixel );
				pixels[i] = new Vector4( channels[0], channels[1], channels[2], channels[3] ) / ushort.MaxValue;
			}
			else if ( format is VtfFormat.RGB323232F or VtfFormat.RGBA32323232F )
			{
				var channels = MemoryMarshal.Cast<byte, float>( pixel );
				pixels[i] = new( channels[0], channels[1], channels[2], channels.Length == 4 ? channels[3] : 1 );
			}
			else
			{
				pixels[i] = new Vector4( pixel[format == VtfFormat.BGRA8888 ? 2 : 0], pixel[1],
					pixel[format == VtfFormat.BGRA8888 ? 0 : 2], pixel[3] ) / 255f;
			}
		}
		return pixels;
	}

	public static void DecompressDxt( ReadOnlySpan<byte> source, Span<byte> destination,
		VtfFormat format, int width, int height )
		=> DecompressDxt( source, destination, format, width, height, (0, 0, width, height) );

	private static void DecompressDxt( ReadOnlySpan<byte> source, Span<byte> destination,
		VtfFormat format, int width, int height, (int X, int Y, int Width, int Height) region )
	{
		var blockSize = format switch
		{
			VtfFormat.DXT1 or VtfFormat.DXT1_ONEBITALPHA => 8,
			VtfFormat.DXT3 or VtfFormat.DXT5 => 16,
			_ => throw new NotSupportedException( $"VTF DXT conversion: {format}" )
		};
		if ( width <= 0 || height <= 0 )
			throw new ArgumentOutOfRangeException( nameof( width ), "DXT dimensions must be positive." );
		if ( region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0
			|| (long)region.X + region.Width > width || (long)region.Y + region.Height > height )
			throw new ArgumentOutOfRangeException( nameof( region ), "DXT region extends outside its texture." );
		var blocksX = checked((width + 3) / 4);
		var blocksY = checked((height + 3) / 4);
		if ( source.Length != checked(blocksX * blocksY * blockSize)
			|| destination.Length != checked(region.Width * region.Height * 4) )
			throw new ArgumentException( "VTF DXT conversion buffer sizes do not match." );

		Span<byte> colors = stackalloc byte[16];
		Span<byte> alphas = stackalloc byte[8];
		for ( var blockY = region.Y / 4; blockY <= (region.Y + region.Height - 1) / 4; blockY++ )
		{
			for ( var blockX = region.X / 4; blockX <= (region.X + region.Width - 1) / 4; blockX++ )
			{
				var block = source.Slice( (blockY * blocksX + blockX) * blockSize, blockSize );
				var colorBlock = block[(blockSize - 8)..];
				var color0 = BinaryPrimitives.ReadUInt16LittleEndian( colorBlock );
				var color1 = BinaryPrimitives.ReadUInt16LittleEndian( colorBlock[2..] );
				Decode565( color0, colors );
				Decode565( color1, colors[4..] );
				var fourColors = blockSize == 16 || color0 > color1;
				for ( var channel = 0; channel < 3; channel++ )
				{
					colors[8 + channel] = fourColors
						? (byte)((2 * colors[channel] + colors[4 + channel]) / 3)
						: (byte)((colors[channel] + colors[4 + channel]) / 2);
					colors[12 + channel] = fourColors
						? (byte)((colors[channel] + 2 * colors[4 + channel]) / 3)
						: (byte)0;
				}
				colors[11] = 255;
				colors[15] = !fourColors && format == VtfFormat.DXT1_ONEBITALPHA ? (byte)0 : (byte)255;
				var colorIndices = BinaryPrimitives.ReadUInt32LittleEndian( colorBlock[4..] );
				var alphaBits = blockSize == 16 ? BinaryPrimitives.ReadUInt64LittleEndian( block ) : 0UL;
				if ( format == VtfFormat.DXT5 )
				{
					alphas[0] = block[0];
					alphas[1] = block[1];
					var interpolated = alphas[0] > alphas[1] ? 6 : 4;
					for ( var i = 1; i <= interpolated; i++ )
						alphas[i + 1] = (byte)(((interpolated + 1 - i) * alphas[0] + i * alphas[1]) / (interpolated + 1));
					if ( interpolated == 4 )
					{
						alphas[6] = 0;
						alphas[7] = 255;
					}
					alphaBits >>= 16;
				}
				for ( var y = Math.Max( 0, region.Y - blockY * 4 ); y < 4 && blockY * 4 + y < region.Y + region.Height; y++ )
				{
					for ( var x = Math.Max( 0, region.X - blockX * 4 ); x < 4 && blockX * 4 + x < region.X + region.Width; x++ )
					{
						var pixel = y * 4 + x;
						var color = (int)((colorIndices >> (pixel * 2)) & 3);
						var output = destination.Slice( ((blockY * 4 + y - region.Y) * region.Width + blockX * 4 + x - region.X) * 4, 4 );
						colors.Slice( color * 4, 4 ).CopyTo( output );
						if ( format == VtfFormat.DXT3 )
							output[3] = (byte)(((alphaBits >> (pixel * 4)) & 15) * 17);
						else if ( format == VtfFormat.DXT5 )
							output[3] = alphas[(int)((alphaBits >> (pixel * 3)) & 7)];
					}
				}
			}
		}
	}

	private static void Decode565( ushort value, Span<byte> color )
	{
		var red = value >> 11;
		var green = (value >> 5) & 63;
		var blue = value & 31;
		color[0] = (byte)((red << 3) | (red >> 2));
		color[1] = (byte)((green << 2) | (green >> 4));
		color[2] = (byte)((blue << 3) | (blue >> 2));
		color[3] = 255;
	}

	internal static void ConvertPixels( ReadOnlySpan<byte> source, Span<byte> destination, VtfFormat format )
	{
		var stride = format switch
		{
			VtfFormat.BGR888 or VtfFormat.RGB888
				or VtfFormat.RGB888_BLUESCREEN or VtfFormat.BGR888_BLUESCREEN => 3,
			VtfFormat.ARGB8888 or VtfFormat.ABGR8888 or VtfFormat.BGRX8888 => 4,
			VtfFormat.IA88 or VtfFormat.UV88
				or VtfFormat.RGB565 or VtfFormat.BGR565 or VtfFormat.BGRA4444
				or VtfFormat.BGRX5551 or VtfFormat.BGRA5551 => 2,
			VtfFormat.I8 or VtfFormat.A8 => 1,
			VtfFormat.RGB323232F => 12,
			_ => throw new NotSupportedException( $"VTF conversion: {format}" )
		};
		var outputStride = format == VtfFormat.RGB323232F ? 16 : 4;
		if ( source.Length % stride != 0 || destination.Length != checked(source.Length / stride * outputStride) )
			throw new ArgumentException( "VTF pixel conversion buffer sizes do not match." );

		for ( int i = 0, j = 0; i < source.Length; i += stride, j += outputStride )
		{
			switch ( format )
			{
				case VtfFormat.RGB323232F:
					source.Slice( i, 12 ).CopyTo( destination.Slice( j, 12 ) );
					BinaryPrimitives.WriteSingleLittleEndian( destination.Slice( j + 12, 4 ), 1.0f );
					break;
				case VtfFormat.BGR888:
				case VtfFormat.BGR888_BLUESCREEN:
				case VtfFormat.BGRX8888:
					destination[j] = source[i + 2];
					destination[j + 1] = source[i + 1];
					destination[j + 2] = source[i];
					destination[j + 3] = 255;
					break;
				case VtfFormat.RGB888:
				case VtfFormat.RGB888_BLUESCREEN:
					source.Slice( i, 3 ).CopyTo( destination.Slice( j, 3 ) );
					destination[j + 3] = 255;
					break;
				case VtfFormat.ARGB8888:
					source.Slice( i + 1, 3 ).CopyTo( destination.Slice( j, 3 ) );
					destination[j + 3] = source[i];
					break;
				case VtfFormat.ABGR8888:
					destination[j] = source[i + 3];
					destination[j + 1] = source[i + 2];
					destination[j + 2] = source[i + 1];
					destination[j + 3] = source[i];
					break;
				case VtfFormat.RGB565:
				case VtfFormat.BGR565:
				case VtfFormat.BGRX5551:
				case VtfFormat.BGRA5551:
				case VtfFormat.BGRA4444:
					var packed = BinaryPrimitives.ReadUInt16LittleEndian( source.Slice( i, 2 ) );
					if ( format == VtfFormat.BGRA4444 )
					{
						destination[j] = (byte)(((packed >> 8) & 15) * 17);
						destination[j + 1] = (byte)(((packed >> 4) & 15) * 17);
						destination[j + 2] = (byte)((packed & 15) * 17);
						destination[j + 3] = (byte)((packed >> 12) * 17);
					}
					else
					{
						var is565 = format is VtfFormat.RGB565 or VtfFormat.BGR565;
						var low = packed & 31;
						var green = (packed >> 5) & (is565 ? 63 : 31);
						var high = (packed >> (is565 ? 11 : 10)) & 31;
						// VTF RGB565 has red in the low bits; BGR565 has blue there.
						var red = format == VtfFormat.RGB565 ? low : high;
						var blue = format == VtfFormat.RGB565 ? high : low;
						destination[j] = (byte)((red << 3) | (red >> 2));
						destination[j + 1] = is565
							? (byte)((green << 2) | (green >> 4))
							: (byte)((green << 3) | (green >> 2));
						destination[j + 2] = (byte)((blue << 3) | (blue >> 2));
						destination[j + 3] = format == VtfFormat.BGRA5551 && (packed & 0x8000) == 0
							? (byte)0 : (byte)255;
					}
					break;
				case VtfFormat.I8:
				case VtfFormat.IA88:
					destination[j] = destination[j + 1] = destination[j + 2] = source[i];
					destination[j + 3] = format == VtfFormat.IA88 ? source[i + 1] : (byte)255;
					break;
				case VtfFormat.A8:
					destination[j] = destination[j + 1] = destination[j + 2] = source[i];
					destination[j + 3] = source[i];
					break;
				case VtfFormat.UV88:
					destination[j] = source[i];
					destination[j + 1] = source[i + 1];
					destination[j + 2] = 0;
					destination[j + 3] = 0;
					break;
			}
			if ( (format is VtfFormat.RGB888_BLUESCREEN or VtfFormat.BGR888_BLUESCREEN)
				&& destination[j] == 0 && destination[j + 1] == 0 && destination[j + 2] == 255 )
				destination.Slice( j, 4 ).Clear();
		}
	}
}
