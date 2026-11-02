using System;
using System.IO;
using System.Runtime.InteropServices;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	private static readonly float[] SkySrgb = CreateSkySrgbTable();
	private static float[] CreateSkySrgbTable()
	{
		var table = new float[256];
		for ( var i = 0; i < table.Length; i++ )
		{
			var value = i / 255.0f;
			table[i] = value <= 0.04045f ? value / 12.92f : MathF.Pow( (value + 0.055f) / 1.055f, 2.4f );
		}

		return table;
	}

	private static Vector3[] ReadSkyPixels( VtfFile vtf, int frame, bool compressedHdr, Vector3 tint )
	{
		var width = vtf.Header.Width;
		var height = vtf.Header.Height;
		var format = vtf.Header.HiResFormat;
		var source = vtf.GetHiResPixelData( 0, frame: frame );
		var output = new Vector3[checked(width * height)];
		byte[] converted = null;
		if ( format is VtfFormat.DXT1 or VtfFormat.DXT1_ONEBITALPHA or VtfFormat.DXT3 or VtfFormat.DXT5 )
		{
			converted = new byte[checked(output.Length * 4)];
			VtfFile.DecompressDxt( source, converted, format, width, height );
			format = VtfFormat.RGBA8888;
		}
		else if ( format is not (VtfFormat.RGBA8888 or VtfFormat.BGRA8888 or VtfFormat.RGBA16161616F or VtfFormat.RGBA16161616 or VtfFormat.RGBA32323232F or VtfFormat.RGB323232F) )
		{
			converted = new byte[checked(output.Length * 4)];
			VtfFile.ConvertPixels( source, converted, format );
			format = VtfFormat.RGBA8888;
		}

		if ( converted is not null )
			source = converted;
		var stride = format switch
		{
			VtfFormat.RGBA16161616F or VtfFormat.RGBA16161616 => 8,
			VtfFormat.RGB323232F => 12,
			VtfFormat.RGBA32323232F => 16,
			_ => 4
		};
		for ( var i = 0; i < output.Length; i++ )
		{
			var pixel = source.Slice( i * stride, stride );
			Vector3 color;
			if ( format == VtfFormat.RGBA16161616F )
			{
				var channels = MemoryMarshal.Cast<byte, Half>( pixel );
				color = new( (float)channels[0], (float)channels[1], (float)channels[2] );
			}
			else if ( format == VtfFormat.RGBA16161616 )
			{
				var channels = MemoryMarshal.Cast<byte, ushort>( pixel );
				color = new Vector3( channels[0], channels[1], channels[2] ) * (16.0f / ushort.MaxValue);
			}
			else if ( format is VtfFormat.RGB323232F or VtfFormat.RGBA32323232F )
			{
				var channels = MemoryMarshal.Cast<byte, float>( pixel );
				color = new( channels[0], channels[1], channels[2] );
			}
			else
			{
				var red = format == VtfFormat.BGRA8888 ? pixel[2] : pixel[0];
				var blue = format == VtfFormat.BGRA8888 ? pixel[0] : pixel[2];
				color = compressedHdr ? new Vector3( red, pixel[1], blue ) * (8.0f * pixel[3] / (255.0f * 255.0f)) : new Vector3( SkySrgb[red], SkySrgb[pixel[1]], SkySrgb[blue] );
			}

			color *= tint;
			if ( !float.IsFinite( color.x ) || !float.IsFinite( color.y ) || !float.IsFinite( color.z ) || color.x > 65504 || color.y > 65504 || color.z > 65504 )
				throw new InvalidDataException( "Sky pixels exceed the finite half-float range." );
			output[i] = Vector3.Max( color, Vector3.Zero );
		}

		return output;
	}

	private static void WriteSkyFace( Span<Half> output, int size, Vector3[] pixels, SkyFace face, int index )
	{
		var width = face.Header.Width;
		var height = face.Header.Height;
		var clampS = (face.Header.Flags & VtfFlags.CLAMPS) != 0;
		var clampT = (face.Header.Flags & VtfFlags.CLAMPT) != 0;
		var point = (face.Header.Flags & VtfFlags.POINTSAMPLE) != 0 && !face.CompressedHdr;
		for ( var y = 0; y < size; y++ )
		{
			for ( var x = 0; x < size; x++ )
			{
				var u = (x + 0.5f) / size;
				var v = (y + 0.5f) / size;
				var uv = index switch
				{
					0 or 4 or 5 => new Vector2( v, u ),
					1 => new Vector2( 1 - v, 1 - u ),
					2 => new Vector2( u, 1 - v ),
					_ => new Vector2( 1 - u, v )
				};
				uv = Vector2.Clamp( uv, new( 1.0f / 512 ), new( 511.0f / 512 ) );
				uv = face.Transform.Transform( uv );
				if ( !float.IsFinite( uv.x ) || !float.IsFinite( uv.y ) )
					throw new InvalidDataException( $"{face.Path}: sky texture coordinates overflow." );
				var sx = (clampS ? Math.Clamp( uv.x, 0, 1 ) : uv.x - MathF.Floor( uv.x )) * width - 0.5f;
				var sy = (clampT ? Math.Clamp( uv.y, 0, 1 ) : uv.y - MathF.Floor( uv.y )) * height - 0.5f;
				var ix = (int)MathF.Floor( sx );
				var iy = (int)MathF.Floor( sy );
				Vector3 color;
				if ( point )
				{
					color = pixels[SkyPixelIndex( (int)MathF.Floor( sx + 0.5f ), width, clampS ) + SkyPixelIndex( (int)MathF.Floor( sy + 0.5f ), height, clampT ) * width];
				}
				else
				{
					var x0 = SkyPixelIndex( ix, width, clampS );
					var x1 = SkyPixelIndex( ix + 1, width, clampS );
					var y0 = SkyPixelIndex( iy, height, clampT ) * width;
					var y1 = SkyPixelIndex( iy + 1, height, clampT ) * width;
					color = Vector3.Lerp( Vector3.Lerp( pixels[y0 + x0], pixels[y0 + x1], sx - ix ), Vector3.Lerp( pixels[y1 + x0], pixels[y1 + x1], sx - ix ), sy - iy );
				}

				var offset = (y * size + x) * 4;
				output[offset] = (Half)color.x;
				output[offset + 1] = (Half)color.y;
				output[offset + 2] = (Half)color.z;
				output[offset + 3] = (Half)1;
			}
		}
	}

	private static int SkyPixelIndex( int value, int size, bool clamp ) => clamp ? Math.Clamp( value, 0, size - 1 ) : (value % size + size) % size;
}
