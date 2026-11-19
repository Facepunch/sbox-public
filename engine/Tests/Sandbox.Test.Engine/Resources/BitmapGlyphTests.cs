using System;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace ResourceTests;

/// <summary>
/// <see cref="Bitmap.CreateFromGlyph"/> draws one glyph of font file data, white on transparent, cropped to its outline.
/// </summary>
[TestClass]
public class BitmapGlyphTests
{
	static byte[] Font( string file ) => File.ReadAllBytes( Path.Combine( Environment.CurrentDirectory, "core", "fonts", file ) );

	static readonly Lazy<byte[]> Roboto = new( () => Font( "Roboto-Regular.ttf" ) );

	static bool Draws( byte[] fontData, int character )
	{
		using var bitmap = Bitmap.CreateFromGlyph( fontData, character, 64 );
		return bitmap is not null;
	}

	static ushort GlyphId( byte[] fontData, int character )
	{
		using var data = SKData.CreateCopy( fontData );
		using var typeface = SKTypeface.FromData( data );
		using var font = new SKFont( typeface );
		return font.GetGlyph( character );
	}

	[TestMethod]
	public void EdgesAreAntialiasedAndEveryPixelIsWhite()
	{
		using var bitmap = Bitmap.CreateFromGlyph( Roboto.Value, 'O', 64 );
		Assert.IsNotNull( bitmap );

		var raw = bitmap.GetBuffer().ToArray();
		var pixels = bitmap.GetPixels32();
		Assert.AreEqual( bitmap.Width * bitmap.Height, pixels.Length );

		int partial = 0, clear = 0;
		for ( var i = 0; i < pixels.Length; i++ )
		{
			Assert.AreEqual( new Color32( 255, 255, 255, raw[i * 4 + 3] ), pixels[i], $"pixel {i}" );
			if ( pixels[i].a == 0 ) clear++;
			else if ( pixels[i].a < 255 ) partial++;
		}

		Assert.IsTrue( partial > 0, "curved edges have partial coverage" );
		Assert.IsTrue( clear > 0, "the corners and the hole are clear" );
	}

	/// <summary>
	/// Skia draws the same outline at the same scale into a bigger mask, with room on every side. All of its ink must
	/// land inside our crop, and match it, so a crop one pixel short on any side fails.
	/// </summary>
	[DataTestMethod]
	[DataRow( "Roboto-Regular.ttf", 'O' )]
	[DataRow( "Roboto-Regular.ttf", 'S' )]
	[DataRow( "Roboto-Regular.ttf", 'W' )]
	[DataRow( "Roboto-Regular.ttf", 'g' )]
	[DataRow( "Roboto-Italic.ttf", 'f' )]
	public void CropKeepsAllTheInk( string file, char character )
	{
		const int Height = 64;
		const int Room = 16;
		var fontData = Font( file );

		using var bitmap = Bitmap.CreateFromGlyph( fontData, character, Height );
		Assert.IsNotNull( bitmap );
		var pixels = bitmap.GetPixels32();

		using var data = SKData.CreateCopy( fontData );
		using var typeface = SKTypeface.FromData( data );
		using var font = new SKFont( typeface, 256 ) { Hinting = SKFontHinting.None };
		using var path = font.GetGlyphPath( font.GetGlyph( character ) );
		var bounds = path.TightBounds;

		var width = bitmap.Width + Room * 2;
		var height = bitmap.Height + Room * 2;
		using var reference = new SKBitmap( new SKImageInfo( width, height, SKColorType.Alpha8, SKAlphaType.Premul ) );
		using ( var canvas = new SKCanvas( reference ) )
		using ( var paint = new SKPaint { Color = SKColors.White, IsAntialias = true } )
		{
			canvas.Clear( SKColors.Transparent );
			canvas.Translate( Room, Room );
			canvas.Scale( Height / bounds.Height );
			canvas.Translate( -bounds.Left, -bounds.Top );
			canvas.DrawPath( path, paint );
		}

		var expected = reference.GetPixelSpan().ToArray();
		var rowBytes = reference.RowBytes;
		for ( var y = 0; y < height; y++ )
		{
			for ( var x = 0; x < width; x++ )
			{
				int cx = x - Room, cy = y - Room;
				var inside = cx >= 0 && cy >= 0 && cx < bitmap.Width && cy < bitmap.Height;
				int want = expected[y * rowBytes + x];

				if ( inside )
					Assert.AreEqual( want, (int)pixels[cy * bitmap.Width + cx].a, $"({cx}, {cy})" );
				else
					Assert.AreEqual( 0, want, $"ink outside the crop at ({cx}, {cy})" );
			}
		}
	}

	[TestMethod]
	public void SupplementaryPlaneCharactersDraw()
	{
		const int SquaredLatinCapitalA = 0x1F130;
		var inter = Font( "Inter-Regular.ttf" );
		Assert.AreNotEqual( 0, GlyphId( inter, SquaredLatinCapitalA ), "Inter maps U+1F130" );

		using var squared = Bitmap.CreateFromGlyph( inter, SquaredLatinCapitalA, 64 );
		Assert.IsNotNull( squared );

		var raw = squared.GetBuffer().ToArray();
		int Alpha( int x, int y ) => raw[(y * squared.Width + x) * 4 + 3];
		var middle = squared.Height / 2;

		Assert.AreEqual( 64, squared.Width, "the square's right edge falls on a pixel boundary and must not round up" );
		Assert.IsTrue( Alpha( 0, middle ) > 127 && Alpha( squared.Width - 1, middle ) > 127, "the square's sides, not a bare A" );
		Assert.AreEqual( 0, Alpha( squared.Width / 4, middle ), "clear between the square and the A" );
	}

	/// <summary>
	/// Material Icons maps U+F021 but not '!'. This is the code point fallback symbol fonts rely on; no font we ship has a symbol cmap.
	/// </summary>
	[TestMethod]
	public void ByteCharactersFallBackToTheF000Range()
	{
		var icons = Font( "MaterialIcons-Regular.ttf" );
		Assert.AreEqual( 0, GlyphId( icons, '!' ) );
		Assert.AreNotEqual( 0, GlyphId( icons, 0xF021 ) );

		using var byByte = Bitmap.CreateFromGlyph( icons, '!', 48 );
		using var byPrivateUse = Bitmap.CreateFromGlyph( icons, 0xF021, 48 );

		Assert.IsNotNull( byByte );
		Assert.IsNotNull( byPrivateUse );
		Assert.AreEqual( byPrivateUse.Size, byByte.Size );
		CollectionAssert.AreEqual( byPrivateUse.GetBuffer().ToArray(), byByte.GetBuffer().ToArray() );
	}

	[TestMethod]
	public void GlyphFillsTheHeightAndIsCroppedToItsOutline()
	{
		using var bitmap = Bitmap.CreateFromGlyph( Roboto.Value, 'H', 64 );

		Assert.IsNotNull( bitmap );
		Assert.AreEqual( 64, bitmap.Height );
		Assert.IsTrue( bitmap.Width is > 32 and < 64, $"an H is a little narrower than it is tall, got {bitmap.Width}" );

		var pixels = bitmap.GetBuffer().ToArray();
		int Alpha( int x, int y ) => pixels[(y * bitmap.Width + x) * 4 + 3];
		var columns = Enumerable.Range( 0, bitmap.Width );
		var rows = Enumerable.Range( 0, bitmap.Height );

		Assert.IsTrue( columns.Any( x => Alpha( x, 0 ) > 0 ), "ink on the top row" );
		Assert.IsTrue( columns.Any( x => Alpha( x, bitmap.Height - 1 ) > 0 ), "ink on the bottom row" );
		Assert.IsTrue( rows.Any( y => Alpha( 0, y ) > 0 ), "ink on the left column" );
		Assert.IsTrue( rows.Any( y => Alpha( bitmap.Width - 1, y ) > 0 ), "ink on the right column" );

		Assert.AreEqual( 255, Alpha( 1, bitmap.Height / 4 ), "the stems are solid" );
		Assert.AreEqual( 0, Alpha( bitmap.Width / 2, bitmap.Height / 4 ), "between the stems is clear" );

		for ( var i = 0; i < pixels.Length; i += 4 )
		{
			Assert.AreEqual( 255, pixels[i] );
			Assert.AreEqual( 255, pixels[i + 1] );
			Assert.AreEqual( 255, pixels[i + 2] );
		}
	}

	[TestMethod]
	public void IconFontGlyphsDraw()
	{
		using var home = Bitmap.CreateFromGlyph( Font( "MaterialIcons-Regular.ttf" ), 0xE88A, 32 );

		Assert.IsNotNull( home );
		Assert.AreEqual( 32, home.Height );
	}

	[TestMethod]
	public void WideGlyphsAreScaledDownToFit()
	{
		using var underscore = Bitmap.CreateFromGlyph( Roboto.Value, '_', 1024 );

		Assert.IsNotNull( underscore );
		Assert.AreEqual( 4096, underscore.Width );
		Assert.IsTrue( underscore.Height < 1024 );
	}

	[TestMethod]
	public void MissingOrEmptyGlyphsReturnNull()
	{
		Assert.IsFalse( Draws( Roboto.Value, ' ' ), "a space has no outline" );
		Assert.IsFalse( Draws( Roboto.Value, 0x10FFFD ), "not in the font" );
	}

	[TestMethod]
	public void UnreadableFontDataReturnsNull()
	{
		var garbage = new byte[4096];
		new Random( 1 ).NextBytes( garbage );

		Assert.IsFalse( Draws( [], 'H' ) );
		Assert.IsFalse( Draws( garbage, 'H' ) );
		Assert.IsFalse( Draws( Roboto.Value.AsSpan( 0, 1024 ).ToArray(), 'H' ) );
		Assert.IsFalse( Draws( new byte[32 * 1024 * 1024 + 1], 'H' ) );
	}

	[TestMethod]
	public void CorruptedFontDataDoesNotThrow()
	{
		var random = new Random( 2 );
		for ( var i = 0; i < 64; i++ )
		{
			var data = (byte[])Roboto.Value.Clone();
			for ( var j = 0; j < 64; j++ )
				data[random.Next( data.Length )] = (byte)random.Next( 256 );

			using var bitmap = Bitmap.CreateFromGlyph( data, 'H', 64 );
			if ( bitmap is not null ) Assert.IsTrue( bitmap.Width is >= 1 and <= 4096 && bitmap.Height is >= 1 and <= 64 );
		}
	}

	[TestMethod]
	public void ArgumentsAreChecked()
	{
		Assert.ThrowsException<ArgumentNullException>( () => Bitmap.CreateFromGlyph( null, 'H', 64 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Bitmap.CreateFromGlyph( Roboto.Value, 'H', 0 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Bitmap.CreateFromGlyph( Roboto.Value, 'H', 4097 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Bitmap.CreateFromGlyph( Roboto.Value, -1, 64 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Bitmap.CreateFromGlyph( Roboto.Value, 0x110000, 64 ) );
	}
}
