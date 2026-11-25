using SkiaSharp;

namespace Sandbox;

public partial class Bitmap
{
	const int MaxFontDataSize = 32 * 1024 * 1024;
	const int MaxGlyphSize = 4096;

	/// <summary>
	/// Draws one glyph of a TrueType or OpenType font, white with its coverage in alpha, scaled so its outline is
	/// <paramref name="height"/> pixels tall and cropped tightly to it. Useful for icon fonts. The font is only read
	/// for this call: it isn't added to the fonts that text rendering and UI can use.
	/// </summary>
	/// <param name="fontData">The contents of a .ttf or .otf file, at most 32 MB.</param>
	/// <param name="character">The Unicode code point to draw. A symbol font that keeps its characters at U+F020 to U+F0FF
	/// is also searched for there, so its glyphs can be asked for by their single byte character.</param>
	/// <param name="height">Height of the bitmap in pixels, from 1 to 4096. A glyph that would be wider than 4096 pixels is scaled down to fit.</param>
	/// <returns>The glyph, or null if the data is empty, over 32 MB or not a font we can read, or the font has no outline for this character.</returns>
	public static Bitmap CreateFromGlyph( byte[] fontData, int character, int height )
	{
		ArgumentNullException.ThrowIfNull( fontData );
		ArgumentOutOfRangeException.ThrowIfNegative( character );
		ArgumentOutOfRangeException.ThrowIfGreaterThan( character, 0x10FFFF );
		ArgumentOutOfRangeException.ThrowIfLessThan( height, 1 );
		ArgumentOutOfRangeException.ThrowIfGreaterThan( height, MaxGlyphSize );

		if ( fontData.Length == 0 || fontData.Length > MaxFontDataSize )
			return null;

		using var data = SKData.CreateCopy( fontData );
		using var typeface = SKTypeface.FromData( data );
		if ( typeface is null )
			return null;

		using var font = new SKFont( typeface, 256 ) { Hinting = SKFontHinting.None };
		var glyph = font.GetGlyph( character );
		if ( glyph == 0 && character is >= 0x20 and <= 0xFF )
			glyph = font.GetGlyph( 0xF000 | character );
		if ( glyph == 0 )
			return null;

		using var path = font.GetGlyphPath( glyph );
		if ( path is null )
			return null;

		var bounds = path.TightBounds;
		if ( !float.IsFinite( bounds.Left ) || !float.IsFinite( bounds.Top ) || !(bounds.Width > 0) || !(bounds.Height > 0) )
			return null;

		var scale = MathF.Min( height / bounds.Height, MaxGlyphSize / bounds.Width );
		if ( !float.IsFinite( scale ) || !(scale > 0) )
			return null;

		// An edge that lands on a pixel boundary must not round up to an extra, empty pixel. FreeType (Linux) gives Skia its
		// outlines in fixed point, so such an edge can come out a hundredth of a pixel over
		const float Slack = 1.0f / 16;
		var width = Math.Clamp( (int)MathF.Ceiling( bounds.Width * scale - Slack ), 1, MaxGlyphSize );
		height = Math.Clamp( (int)MathF.Ceiling( bounds.Height * scale - Slack ), 1, height );

		// A pixel of room on every side: Skia shades outlines that touch the canvas edge differently
		using var mask = new SKBitmap( new SKImageInfo( width + 2, height + 2, SKColorType.Alpha8, SKAlphaType.Premul ) );
		if ( mask.GetPixels() == IntPtr.Zero )
			return null;

		using ( var canvas = new SKCanvas( mask ) )
		using ( var paint = new SKPaint { Color = SKColors.White, IsAntialias = true } )
		{
			canvas.Clear( SKColors.Transparent );
			canvas.Translate( 1, 1 );
			canvas.Scale( scale );
			canvas.Translate( -bounds.Left, -bounds.Top );
			canvas.DrawPath( path, paint );
		}

		var bitmap = new Bitmap( width, height );
		var pixels = bitmap.GetBuffer();
		var coverage = mask.GetPixelSpan();
		var rowBytes = mask.RowBytes;
		for ( var y = 0; y < height; y++ )
		{
			for ( var x = 0; x < width; x++ )
			{
				var i = (y * width + x) * 4;
				pixels[i] = 255;
				pixels[i + 1] = 255;
				pixels[i + 2] = 255;
				pixels[i + 3] = coverage[(y + 1) * rowBytes + x + 1];
			}
		}

		return bitmap;
	}
}
