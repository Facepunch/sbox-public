using SkiaSharp;

namespace Sandbox.UI
{
	internal static class SkiaCompat
	{
		public static SKColor ToSk( this Color c )
		{
			var c32 = c.ToColor32();

			return new SKColor( c32.r, c32.g, c32.b, c32.a );
		}

		/// <summary>
		/// Float colour, unclamped — components above 1 survive so text can be rasterized HDR.
		/// </summary>
		public static SKColorF ToSkF( this Color c )
		{
			return new SKColorF( c.r, c.g, c.b, c.a );
		}
	}

}
