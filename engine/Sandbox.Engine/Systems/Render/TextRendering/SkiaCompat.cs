using Sandbox.UI;
using SkiaSharp;

namespace Sandbox
{
	internal static class SkiaCompat
	{
		extension( SKBitmap bitmap )
		{
			/// <summary>
			/// Put a colour into every fully transparent texel, so nothing outside the ink is black.
			/// Skia works premultiplied, so anything it didn't draw - and anything it drew with less than
			/// half a level of coverage - lands as a plain zero, which a bilinear tap near an edge drags in.
			/// </summary>
			public unsafe void RepairTransparentTexels( SKColorF color )
			{
				var pixels = bitmap.GetPixels();
				if ( pixels == IntPtr.Zero ) return;

				var count = bitmap.Width * bitmap.Height;

				if ( bitmap.ColorType == SKColorType.RgbaF16 )
				{
					var rgb = (ulong)BitConverter.HalfToUInt16Bits( (Half)color.Red )
						| ((ulong)BitConverter.HalfToUInt16Bits( (Half)color.Green ) << 16)
						| ((ulong)BitConverter.HalfToUInt16Bits( (Half)color.Blue ) << 32);

					new Span<ulong>( (void*)pixels, count ).Replace( 0ul, rgb );
					return;
				}

				var r = (uint)(color.Red.Clamp( 0, 1 ) * 255.0f + 0.5f);
				var g = (uint)(color.Green.Clamp( 0, 1 ) * 255.0f + 0.5f);
				var b = (uint)(color.Blue.Clamp( 0, 1 ) * 255.0f + 0.5f);

				new Span<uint>( (void*)pixels, count ).Replace( 0u, (r << 16) | (g << 8) | b );
			}
		}

		public static SKColor ToSk( this in Color c )
		{
			var c32 = c.ToColor32();

			return new SKColor( c32.r, c32.g, c32.b, c32.a );
		}

		public static SKColorF ToSkF( this in Color c )
		{
			return new SKColorF( c.r, c.g, c.b, c.a );
		}

		public static Color FromSk( this in SKColor c )
		{
			return new Color( c.Red / 255.0f, c.Green / 255.0f, c.Blue / 255.0f, c.Alpha / 255.0f );
		}

		public static SKRect ToSk( this in Rect c )
		{
			return new SKRect( c.Left, c.Top, c.Right, c.Bottom );
		}

		public static SKPoint ToSk( this in Vector2 c )
		{
			return new SKPoint( c.x, c.y );
		}

		public static SKTextAlign ToSk( this TextAlign c )
		{
			if ( c == TextAlign.Left ) return SKTextAlign.Left;
			else if ( c == TextAlign.Right ) return SKTextAlign.Right;
			else if ( c == TextAlign.Center ) return SKTextAlign.Center;

			return SKTextAlign.Left;
		}
	}

}
