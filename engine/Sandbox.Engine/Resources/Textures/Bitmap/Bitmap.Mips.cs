using System.Runtime.InteropServices;

namespace Sandbox;

public sealed partial class Bitmap
{
	/// <summary>
	/// The next mip of this image, as a texture made from it gets on the GPU (a linear blit per level): half the size
	/// (at least 1), each pixel filtered bilinearly from the ones under its center, on straight, not premultiplied, values.
	/// An 8-bit texture is sRGB, so its colour is filtered in linear light and its alpha as it is; half floats are linear.
	/// </summary>
	internal Bitmap NextMip()
	{
		int sw = Width, sh = Height;
		int dw = Math.Max( sw / 2, 1 ), dh = Math.Max( sh / 2, 1 );
		var half = IsFloatingPoint;

		var source = GetPixels();
		if ( !half )
		{
			for ( int i = 0; i < source.Length; i++ ) source[i] = source[i].ToLinear();
		}

		var mip = new Bitmap( dw, dh, half );
		var target = mip.GetBuffer();

		for ( int y = 0; y < dh; y++ )
		{
			var (y0, y1, fy) = Taps( y, sh, dh );
			for ( int x = 0; x < dw; x++ )
			{
				var (x0, x1, fx) = Taps( x, sw, dw );
				var top = Color.Lerp( source[y0 * sw + x0], source[y0 * sw + x1], fx, false );
				var bottom = Color.Lerp( source[y1 * sw + x0], source[y1 * sw + x1], fx, false );
				var value = Color.Lerp( top, bottom, fy, false );

				var i = y * dw + x;
				if ( half )
				{
					MemoryMarshal.Cast<byte, Color.Rgba16>( target )[i] = new Color.Rgba16( value );
					continue;
				}

				var srgb = value.ToSrgb();
				target[i * 4] = Byte( srgb.r );
				target[i * 4 + 1] = Byte( srgb.g );
				target[i * 4 + 2] = Byte( srgb.b );
				target[i * 4 + 3] = Byte( srgb.a );
			}
		}

		return mip;
	}

	static byte Byte( float value ) => (byte)Math.Clamp( MathF.Round( value * 255 ), 0, 255 );

	/// <summary>
	/// The two source texels a target texel's center falls between, and how far toward the second, clamped to the edge.
	/// </summary>
	static (int First, int Second, float Weight) Taps( int target, int sourceSize, int targetSize )
	{
		var center = (target + 0.5f) * sourceSize / targetSize - 0.5f;
		var first = (int)MathF.Floor( center );
		var weight = center - first;
		return (Math.Clamp( first, 0, sourceSize - 1 ), Math.Clamp( first + 1, 0, sourceSize - 1 ), weight);
	}
}
