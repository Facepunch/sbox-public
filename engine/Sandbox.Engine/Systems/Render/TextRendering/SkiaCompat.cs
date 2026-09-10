using Sandbox.UI;
using SkiaSharp;
using System.Buffers;
using System.Numerics;
using System.Runtime.Intrinsics;

namespace Sandbox
{
	internal static class SkiaCompat
	{
		extension( SKBitmap bitmap )
		{
			/// <summary>
			/// Give every fully transparent texel a colour, so a bilinear tap or a mip average near an edge doesn't
			/// drag black in. Skia works premultiplied, so anything it didn't draw - and anything it drew with less
			/// than half a level of coverage - lands as a plain zero. Texels within four of the ink take the colour
			/// of the ink nearest them, ring by ring, which is what a mip level's average wants to see. Everything
			/// further out gets <paramref name="color"/>.
			/// </summary>
			public unsafe void RepairTransparentTexels( SKColorF color )
			{
				var pixels = (byte*)bitmap.GetPixels();
				if ( pixels == null ) return;

				int width = bitmap.Width, height = bitmap.Height, bpp = bitmap.BytesPerPixel;
				bool f16 = bitmap.ColorType == SKColorType.RgbaF16;

				if ( f16 )
				{
					var rgb = (ulong)BitConverter.HalfToUInt16Bits( (Half)color.Red )
						| ((ulong)BitConverter.HalfToUInt16Bits( (Half)color.Green ) << 16)
						| ((ulong)BitConverter.HalfToUInt16Bits( (Half)color.Blue ) << 32);

					new Span<ulong>( pixels, width * height ).Replace( 0ul, rgb );
				}
				else
				{
					var r = (uint)(color.Red.Clamp( 0, 1 ) * 255.0f + 0.5f);
					var g = (uint)(color.Green.Clamp( 0, 1 ) * 255.0f + 0.5f);
					var b = (uint)(color.Blue.Clamp( 0, 1 ) * 255.0f + 0.5f);

					new Span<uint>( pixels, width * height ).Replace( 0u, (r << 16) | (g << 8) | b );
				}

				// One bit per texel, rows padded to whole words: transparent and not yet coloured, this ring, the next ring
				int stride = (width + 63) >> 6, words = stride * height;
				var bits = ArrayPool<ulong>.Shared.Rent( words * 3 );

				fixed ( ulong* p = bits )
				{
					ulong* transparent = p, ring = p + words, next = p + words * 2;
					new Span<ulong>( p, words * 3 ).Clear();

					for ( int y = 0; y < height; y++ )
					{
						var row = pixels + y * width * bpp;
						var set = transparent + y * stride;
						int x = 0;

						if ( !f16 && Vector256.IsHardwareAccelerated )
						{
							var alpha = Vector256.Create( 0xff000000u );
							for ( ; x + 8 <= width; x += 8 )
							{
								var clear = Vector256.Equals( Vector256.Load( (uint*)row + x ) & alpha, Vector256<uint>.Zero ).ExtractMostSignificantBits();
								set[x >> 6] |= (ulong)clear << (x & 63);
							}
						}

						for ( ; x < width; x++ )
						{
							var texel = row + x * bpp;
							bool inked = f16 ? (*(ushort*)(texel + 6) & 0x7fff) != 0 : texel[3] != 0;
							if ( !inked ) set[x >> 6] |= 1ul << (x & 63);
						}
					}

					// The ink's edge - inked texels with a transparent 4-neighbour - is where colour grows out from
					ulong last = (width & 63) == 0 ? ~0ul : (1ul << (width & 63)) - 1;

					for ( int i = 0; i < words; i++ )
					{
						int k = i % stride;
						ulong t = transparent[i];
						ulong near = (t << 1) | (t >> 1)
							| (k > 0 ? transparent[i - 1] >> 63 : 0) | (k < stride - 1 ? transparent[i + 1] << 63 : 0)
							| (i >= stride ? transparent[i - stride] : 0) | (i + stride < words ? transparent[i + stride] : 0);

						ring[i] = ~t & near & (k == stride - 1 ? last : ~0ul);
					}

					for ( int r = 0; r < 4; r++ )
					{
						bool any = false;

						for ( int i = 0; i < words; i++ )
						{
							ulong w = ring[i];
							if ( w == 0 ) continue;

							any = true;
							int y = i / stride, x0 = (i % stride) << 6;

							while ( w != 0 )
							{
								int x = x0 + BitOperations.TrailingZeroCount( w );
								w &= w - 1;

								var source = pixels + (y * width + x) * bpp;

								for ( int ny = Math.Max( y - 1, 0 ); ny <= Math.Min( y + 1, height - 1 ); ny++ )
								{
									for ( int nx = Math.Max( x - 1, 0 ); nx <= Math.Min( x + 1, width - 1 ); nx++ )
									{
										int j = ny * stride + (nx >> 6);
										ulong bit = 1ul << (nx & 63);
										if ( (transparent[j] & bit) == 0 ) continue;

										transparent[j] &= ~bit;
										next[j] |= bit;

										var texel = pixels + (ny * width + nx) * bpp;
										if ( f16 ) *(ulong*)texel = *(ulong*)source & 0x0000ffffffffffffUL;
										else *(uint*)texel = *(uint*)source & 0x00ffffffu;
									}
								}
							}
						}

						if ( !any ) break;

						var done = ring; ring = next; next = done;
						new Span<ulong>( next, words ).Clear();
					}
				}

				ArrayPool<ulong>.Shared.Return( bits );
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
