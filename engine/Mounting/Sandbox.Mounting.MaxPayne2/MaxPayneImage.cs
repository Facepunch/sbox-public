using Sandbox;
using Sandbox.Mounting;
using System;

static class MaxPayneImage
{
	public static Texture Load( byte[] data )
	{
		if ( data is null || data.Length < 18 )
			return null;

		if ( data[0] == 'D' && data[1] == 'D' && data[2] == 'S' && data[3] == ' ' )
			return TextureLoader.FromDds( data );

		if ( data[0] == 0xFF && data[1] == 0xD8 )
		{
			using var bitmap = Bitmap.CreateFromBytes( data );
			return bitmap?.ToTexture();
		}

		if ( data[0] == 0x0A && data[2] == 0x01 )
			return FromPcx( data );

		return FromTga( data );
	}

	static Texture FromTga( byte[] data )
	{
		var idLength = data[0];
		var colorMapType = data[1];
		var imageType = data[2];
		var colorMapLength = BitConverter.ToUInt16( data, 5 );
		var colorMapBits = data[7];
		var width = BitConverter.ToUInt16( data, 12 );
		var height = BitConverter.ToUInt16( data, 14 );
		var bpp = data[16];
		var topOrigin = (data[17] & 0x20) != 0;

		if ( width <= 0 || height <= 0 )
			return null;

		var pos = 18 + idLength;

		byte[] colorMap = null;
		if ( colorMapType == 1 )
		{
			var colorMapBytes = colorMapLength * (colorMapBits / 8);
			colorMap = new byte[colorMapBytes];
			Array.Copy( data, pos, colorMap, 0, colorMapBytes );
			pos += colorMapBytes;
		}

		var bytesPerPixel = bpp / 8;
		var pixels = new byte[width * height * bytesPerPixel];

		switch ( imageType )
		{
			case 1:
			case 2:
			case 3:
				Array.Copy( data, pos, pixels, 0, pixels.Length );
				break;

			case 9:
			case 10:
			case 11:
				DecodeTgaRle( data, pos, pixels, bytesPerPixel );
				break;

			default:
				return null;
		}

		var rgba = new byte[width * height * 4];
		for ( var i = 0; i < width * height; i++ )
		{
			var src = i * bytesPerPixel;
			var dst = i * 4;

			switch ( bpp )
			{
				case 8 when colorMap is not null:
					var entry = pixels[src] * (colorMapBits / 8);
					rgba[dst + 0] = colorMap[entry + 2];
					rgba[dst + 1] = colorMap[entry + 1];
					rgba[dst + 2] = colorMap[entry + 0];
					rgba[dst + 3] = 255;
					break;

				case 8:
					rgba[dst + 0] = pixels[src];
					rgba[dst + 1] = pixels[src];
					rgba[dst + 2] = pixels[src];
					rgba[dst + 3] = 255;
					break;

				case 24:
					rgba[dst + 0] = pixels[src + 2];
					rgba[dst + 1] = pixels[src + 1];
					rgba[dst + 2] = pixels[src + 0];
					rgba[dst + 3] = 255;
					break;

				case 32:
					rgba[dst + 0] = pixels[src + 2];
					rgba[dst + 1] = pixels[src + 1];
					rgba[dst + 2] = pixels[src + 0];
					rgba[dst + 3] = pixels[src + 3];
					break;

				default:
					return null;
			}
		}

		if ( !topOrigin )
			FlipVertical( rgba, width, height );

		return Texture.Create( width, height )
			.WithData( rgba )
			.WithMips()
			.Finish();
	}

	static void DecodeTgaRle( byte[] data, int pos, byte[] pixels, int bytesPerPixel )
	{
		var op = 0;
		while ( op < pixels.Length && pos < data.Length )
		{
			var packet = data[pos++];
			var count = (packet & 0x7F) + 1;

			if ( (packet & 0x80) != 0 )
			{
				for ( var i = 0; i < count && op < pixels.Length; i++ )
				{
					Array.Copy( data, pos, pixels, op, bytesPerPixel );
					op += bytesPerPixel;
				}

				pos += bytesPerPixel;
			}
			else
			{
				var bytes = count * bytesPerPixel;
				Array.Copy( data, pos, pixels, op, Math.Min( bytes, pixels.Length - op ) );
				op += bytes;
				pos += bytes;
			}
		}
	}

	static Texture FromPcx( byte[] data )
	{
		var bitsPerPixel = data[3];
		var xMin = BitConverter.ToUInt16( data, 4 );
		var yMin = BitConverter.ToUInt16( data, 6 );
		var xMax = BitConverter.ToUInt16( data, 8 );
		var yMax = BitConverter.ToUInt16( data, 10 );
		var planes = data[65];
		var bytesPerLine = BitConverter.ToUInt16( data, 66 );

		var width = xMax - xMin + 1;
		var height = yMax - yMin + 1;

		if ( width <= 0 || height <= 0 || bitsPerPixel != 8 )
			return null;

		var decoded = new byte[bytesPerLine * planes * height];
		var pos = 128;
		var op = 0;

		while ( op < decoded.Length && pos < data.Length )
		{
			var b = data[pos++];
			if ( (b & 0xC0) == 0xC0 )
			{
				var count = b & 0x3F;
				var value = data[pos++];
				for ( var i = 0; i < count && op < decoded.Length; i++ )
					decoded[op++] = value;
			}
			else
			{
				decoded[op++] = b;
			}
		}

		var rgba = new byte[width * height * 4];

		if ( planes == 1 )
		{
			if ( data.Length < 769 || data[data.Length - 769] != 0x0C )
				return null;

			var palette = data.Length - 768;
			for ( var y = 0; y < height; y++ )
			{
				for ( var x = 0; x < width; x++ )
				{
					var index = decoded[y * bytesPerLine + x] * 3;
					var dst = (y * width + x) * 4;
					rgba[dst + 0] = data[palette + index + 0];
					rgba[dst + 1] = data[palette + index + 1];
					rgba[dst + 2] = data[palette + index + 2];
					rgba[dst + 3] = 255;
				}
			}
		}
		else if ( planes == 3 )
		{
			for ( var y = 0; y < height; y++ )
			{
				var row = y * bytesPerLine * 3;
				for ( var x = 0; x < width; x++ )
				{
					var dst = (y * width + x) * 4;
					rgba[dst + 0] = decoded[row + x];
					rgba[dst + 1] = decoded[row + bytesPerLine + x];
					rgba[dst + 2] = decoded[row + bytesPerLine * 2 + x];
					rgba[dst + 3] = 255;
				}
			}
		}
		else
		{
			return null;
		}

		return Texture.Create( width, height )
			.WithData( rgba )
			.WithMips()
			.Finish();
	}

	static void FlipVertical( byte[] rgba, int width, int height )
	{
		var stride = width * 4;
		var row = new byte[stride];

		for ( var y = 0; y < height / 2; y++ )
		{
			var top = y * stride;
			var bottom = (height - 1 - y) * stride;

			Array.Copy( rgba, top, row, 0, stride );
			Array.Copy( rgba, bottom, rgba, top, stride );
			Array.Copy( row, 0, rgba, bottom, stride );
		}
	}
}
