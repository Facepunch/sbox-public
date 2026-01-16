using Sandbox.UI;
using SkiaSharp;

namespace Sandbox;

public partial class Bitmap
{
	/// <summary>
	/// Applies a Gaussian blur effect to the current bitmap.
	/// </summary>
	/// <param name="radius">The radius of the blur, controlling its intensity. Must be non-negative.</param>
	/// <param name="tileClamp">Determines the behavior at the edges of the bitmap:
	/// <c>true</c> to clamp the edges (default), or <c>false</c> to repeat the edges.</param>
	public void Blur( float radius, bool tileClamp = true )
	{
		var tileMode = tileClamp ? SKShaderTileMode.Clamp : SKShaderTileMode.Repeat;

		using var clampPaint = new SKPaint
		{
			Shader = SKShader.CreateBitmap( _bitmap, tileMode, tileMode ),
			ImageFilter = SKImageFilter.CreateBlur( radius, radius, SKShaderTileMode.Clamp )
		};

		_canvas.Clear( SKColors.Transparent );
		_canvas.DrawRect( new SKRect( -_bitmap.Width, -_bitmap.Height, _bitmap.Width * 2, _bitmap.Height * 2 ), clampPaint );
	}

	/// <summary>
	/// Applies a Gaussian blur effect to the current bitmap.
	/// </summary>
	public void Sharpen( float amount, bool tileClamp = true )
	{
		var tileMode = tileClamp ? SKShaderTileMode.Clamp : SKShaderTileMode.Repeat;

		float[] kernel = new float[]
		{
			0,     -amount,  0,
			-amount, 1 + 4 * amount, -amount,
			0,     -amount,  0
		};

		using var clampPaint = new SKPaint
		{
			Shader = SKShader.CreateBitmap( _bitmap, tileMode, tileMode ),
			ImageFilter = SKImageFilter.CreateMatrixConvolution( new SKSizeI( 3, 3 ), kernel, 1.0f, 0.0f, new SKPointI( 1, 1 ), tileClamp ? SKShaderTileMode.Clamp : SKShaderTileMode.Repeat, false )
		};

		_canvas.Clear( SKColors.Transparent );
		_canvas.DrawRect( new SKRect( -_bitmap.Width, -_bitmap.Height, _bitmap.Width * 2, _bitmap.Height * 2 ), clampPaint );
	}

	/// <summary>
	/// Adjusts brightness, contrast, and saturation in one pass.
	/// </summary>
	public void Adjust( float brightness = 1, float contrast = 1, float saturation = 1, float hueDegrees = 0 )
	{
		// Build your B/C/S matrix
		float newBrightness = brightness - 1f;
		float scale = contrast;
		float translate = (1f - contrast) * 0.5f + newBrightness;

		float rWeight = 0.2126f;
		float gWeight = 0.7152f;
		float bWeight = 0.0722f;
		float invSat = 1f - saturation;

		float[] bcsMatrix =
		{
			scale*(invSat*rWeight + saturation), scale*invSat*gWeight,            scale*invSat*bWeight,            0, translate,
			scale*invSat*rWeight,               scale*(invSat*gWeight + saturation), scale*invSat*bWeight,        0, translate,
			scale*invSat*rWeight,               scale*invSat*gWeight,            scale*(invSat*bWeight + saturation), 0, translate,
			0, 0, 0, 1, 0
		};

		// Hue rotation matrix
		float hue = hueDegrees.DegreeToRadian();
		float cosA = MathF.Cos( hue );
		float sinA = MathF.Sin( hue );

		// You can tweak these rotation weights, but they're typical
		float[] hueMatrix =
		{
			rWeight + (cosA*(1-rWeight)) - rWeight*sinA,   gWeight - gWeight*cosA   - gWeight*sinA,        bWeight - bWeight*cosA   + (1-bWeight)*sinA, 0, 0,
			rWeight - rWeight*cosA     + 0.143f*sinA,      gWeight + cosA*(1-gWeight) + 0.140f*sinA,       bWeight - bWeight*cosA   - 0.283f*sinA,      0, 0,
			rWeight - rWeight*cosA     - (1-rWeight)*sinA, gWeight - gWeight*cosA   + gWeight*sinA,       bWeight + cosA*(1-bWeight) + bWeight*sinA,    0, 0,
			0, 0, 0, 1, 0
		};

		using var bcsFilter = SKColorFilter.CreateColorMatrix( bcsMatrix );
		using var hueFilter = SKColorFilter.CreateColorMatrix( hueMatrix );
		using var composedFilter = SKColorFilter.CreateCompose( bcsFilter, hueFilter );

		// Compose both filters so we only do one paint pass
		using var paint = new SKPaint
		{
			Shader = SKShader.CreateBitmap( _bitmap ),
			ColorFilter = composedFilter
		};

		_canvas.DrawRect( _bitmap.Info.Rect, paint );
	}


	/// <summary>
	/// Adjusts the hue of the bitmap.
	/// </summary>
	/// <param name="angle">The angle to rotate the hue, in degrees (0 to 360).</param>
	public void AdjustHue( float angle )
	{
		angle = angle % 360; // Normalize to 0-360
		float radians = angle * (float)Math.PI / 180f;

		float cos = (float)Math.Cos( radians );
		float sin = (float)Math.Sin( radians );

		float[] hueMatrix = new float[]
		{
		0.213f + cos * 0.787f - sin * 0.213f, 0.213f - cos * 0.213f + sin * 0.143f, 0.213f - cos * 0.213f - sin * 0.787f, 0, 0,
		0.715f - cos * 0.715f - sin * 0.715f, 0.715f + cos * 0.285f + sin * 0.140f, 0.715f - cos * 0.715f + sin * 0.715f, 0, 0,
		0.072f - cos * 0.072f + sin * 0.928f, 0.072f - cos * 0.072f - sin * 0.283f, 0.072f + cos * 0.928f + sin * 0.072f, 0, 0,
		0, 0, 0, 1, 0
		};

		using var hueFilter = SKColorFilter.CreateColorMatrix( hueMatrix );
		using var paint = new SKPaint
		{
			Shader = SKShader.CreateBitmap( _bitmap, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp ),
			ColorFilter = hueFilter,
		};

		_canvas.DrawRect( new SKRect( 0, 0, _bitmap.Width, _bitmap.Height ), paint );
	}

	/// <summary>
	/// Color the bitmap using this color, respect alpha
	/// </summary>
	public void Colorize( Color color )
	{
		using var paint = new SKPaint
		{
			BlendMode = SKBlendMode.SrcATop, // Blends source RGB based on source alpha
			ColorF = color.ToSkF()           // Input color with alpha intact
		};

		// Draw over the entire canvas
		_canvas.DrawRect( new SKRect( 0, 0, _bitmap.Width, _bitmap.Height ), paint );
	}

	/// <summary>
	/// Tint the bitmap using this color, respect alpha
	/// </summary>
	/// <param name="color"></param>
	public void Tint( Color color )
	{
		using var paint = new SKPaint
		{
			BlendMode = SKBlendMode.Modulate,
			ColorF = color.ToSkF() // Use the color as a tint
		};

		// Draw over the entire canvas
		_canvas.DrawRect( new SKRect( 0, 0, _bitmap.Width, _bitmap.Height ), paint );
	}

	/// <summary>
	/// Shrink the image by adding padding all around - without resizing the bitmap
	/// </summary>
	public void InsertPadding( Margin margin )
	{
		var rect = Rect.Shrink( margin );
		var targetRect = rect.ToSk();

		using var copy = _bitmap.Copy();
		_canvas.Clear( SKColors.Transparent );
		_canvas.DrawBitmap( copy, targetRect );
	}

	/// <summary>
	/// Applies a posterize effect to the bitmap.
	/// </summary>
	/// <param name="levels">The number of color levels to reduce to (e.g., 2, 4, 8).</param>
	internal void Posterize( int levels ) // internalling this because I doubt the usefulness
	{
		if ( levels < 2 ) levels = 2;

		// Create lookup tables for each channel
		byte[] rTable = new byte[256];
		byte[] gTable = new byte[256];
		byte[] bTable = new byte[256];
		byte[] aTable = new byte[256]; // Keep alpha as-is (or also posterize if you want)

		for ( int i = 0; i < 256; i++ )
			aTable[i] = (byte)i;

		float step = 255f / (levels - 1);
		for ( int i = 0; i < 256; i++ )
		{
			int index = (int)MathF.Round( i / step );
			byte val = (byte)MathF.Round( index * step );
			rTable[i] = val;
			gTable[i] = val;
			bTable[i] = val;
		}

		using var paint = new SKPaint
		{
			Shader = SKShader.CreateBitmap( _bitmap, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp ),
			ColorFilter = SKColorFilter.CreateTable( rTable, gTable, bTable, aTable )
		};

		_canvas.DrawRect( new SKRect( 0, 0, _bitmap.Width, _bitmap.Height ), paint );
	}

	/// <summary>
	/// Converts a heightmap to a normal map using parallel processing.
	/// </summary>
	/// <param name="strength">The strength of the normal map effect (default is 1.0).</param>
	/// <returns>The generated normal map as an SKBitmap.</returns>
	public Bitmap HeightmapToNormalMap( float strength = 1.0f )
	{
		if ( IsFloatingPoint )
			return default;

		int width = _bitmap.Width;
		int height = _bitmap.Height;
		var normalMap = Clone();

		// Get raw pixel buffers
		var heightmapPixels = _bitmap.Pixels;
		var normalMapPixels = normalMap._bitmap.Pixels;

		// Parallelize the pixel processing
		Parallel.For( 0, height, y =>
		{
			for ( int x = 0; x < width; x++ )
			{
				// Sample neighboring height values
				float heightL = GetHeight( heightmapPixels, width, height, x - 1, y ); // Left
				float heightR = GetHeight( heightmapPixels, width, height, x + 1, y ); // Right
				float heightT = GetHeight( heightmapPixels, width, height, x, y - 1 ); // Top
				float heightB = GetHeight( heightmapPixels, width, height, x, y + 1 ); // Bottom

				// Compute gradients
				float dx = (heightR - heightL) * strength;
				float dy = (heightB - heightT) * strength;

				// Normalize the normal vector
				var normal = new Vector3( -dx, -dy, 1.0f ).Normal;

				// Convert the normal to color
				byte r = (byte)((normal.x * 0.5f + 0.5f) * 255);
				byte g = (byte)((normal.y * 0.5f + 0.5f) * 255);
				byte b = (byte)((normal.z * 0.5f + 0.5f) * 255);

				// Set the pixel in the normal map buffer
				normalMapPixels[y * width + x] = new SKColor( r, g, b, 255 );
			}
		} );

		// Write the normal map buffer back to the bitmap
		normalMap._bitmap.Pixels = normalMapPixels;

		return normalMap;
	}

	/// <summary>
	/// Gets the height value from the pixel array, clamped to the edges.
	/// </summary>
	private static float GetHeight( SKColor[] pixels, int width, int height, int x, int y )
	{
		x = Math.Clamp( x, 0, width - 1 );
		y = Math.Clamp( y, 0, height - 1 );

		SKColor color = pixels[y * width + x];
		return color.Red / 255.0f; // Assuming grayscale input (Red channel as height)
	}

	/// <summary>
	/// Inverts the colors of the bitmap while preserving alpha.
	/// </summary>
	public void InvertColor()
	{
		using var paint = new SKPaint
		{
			Shader = SKShader.CreateBitmap( _bitmap, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp ),
			ColorFilter = SKColorFilter.CreateColorMatrix(
			[
				-1,  0,  0, 0, 1,
				 0, -1,  0, 0, 1,
				 0,  0, -1, 0, 1,
				 0,  0,  0, 1, 0,
			] )
		};

		_canvas.DrawRect( new SKRect( 0, 0, _bitmap.Width, _bitmap.Height ), paint );
	}
}
