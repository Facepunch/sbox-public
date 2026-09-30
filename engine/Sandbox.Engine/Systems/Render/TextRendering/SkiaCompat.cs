using SkiaSharp;

namespace Sandbox
{
	internal static class SkiaCompat
	{
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
	}

}
