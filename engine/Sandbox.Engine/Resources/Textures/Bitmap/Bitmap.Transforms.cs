using SkiaSharp;

namespace Sandbox;

public partial class Bitmap
{
	/// <summary>
	/// Rotates the bitmap by the specified angle.
	/// </summary>
	/// <param name="degrees">The angle in degrees to rotate the bitmap.</param>
	/// <returns>A new <see cref="Bitmap"/> instance with the rotated image.</returns>
	public Bitmap Rotate( float degrees )
	{
		float sin = (float)Math.Abs( Math.Sin( degrees.DegreeToRadian() ) );
		float cos = (float)Math.Abs( Math.Cos( degrees.DegreeToRadian() ) );

		var rw = (int)(cos * Width + sin * Height);
		var rh = (int)(cos * Height + sin * Width);

		var newBitmap = new SKBitmap( rw, rh, _bitmap.ColorType, _bitmap.AlphaType );

		using ( var canvas = new SKCanvas( newBitmap ) )
		{
			canvas.Clear( SKColors.Transparent );
			canvas.Translate( rw / 2f, rh / 2f );
			canvas.RotateDegrees( degrees );
			canvas.Translate( -Width / 2f, -Height / 2f );
			canvas.DrawBitmap( _bitmap, 0, 0, SKSamplingOptions.Default );
		}

		return new Bitmap( newBitmap );
	}

	/// <summary>
	/// Resizes the bitmap to the specified dimensions and returns a new bitmap.
	/// </summary>
	/// <param name="newWidth">The new width of the bitmap.</param>
	/// <param name="newHeight">The new height of the bitmap.</param>
	/// <param name="smooth">Resample smoothly. If false this will be nearest neighbour.</param>
	/// <returns>A new <see cref="Bitmap"/> instance with the specified dimensions.</returns>
	public Bitmap Resize( int newWidth, int newHeight, bool smooth = true )
	{
		if ( newWidth <= 0 || newHeight <= 0 )
			throw new ArgumentOutOfRangeException( "Width and height must be greater than zero." );

		var info = new SKImageInfo( newWidth, newHeight, _bitmap.ColorType, _bitmap.AlphaType );

		var sampling = smooth ? new SKSamplingOptions( SKFilterMode.Linear, SKMipmapMode.Linear ) : SKSamplingOptions.Default;

		var newBitmap = _bitmap.Resize( info, sampling );
		return new Bitmap( newBitmap );
	}

	/// <summary>
	/// Flips the bitmap vertically.
	/// </summary>
	/// <returns>A new <see cref="Bitmap"/> instance with the flipped image.</returns>
	public Bitmap FlipVertical()
	{
		var newBitmap = new SKBitmap( _bitmap.Width, _bitmap.Height, _bitmap.ColorType, _bitmap.AlphaType );

		using ( var canvas = new SKCanvas( newBitmap ) )
		{
			canvas.Scale( 1, -1 ); // Flip vertically
			canvas.Translate( 0, -_bitmap.Height ); // Adjust position
			canvas.DrawBitmap( _bitmap, 0, 0, SKSamplingOptions.Default );
		}

		return new Bitmap( newBitmap );
	}

	/// <summary>
	/// Flips the bitmap horizontally.
	/// </summary>
	/// <returns>A new <see cref="Bitmap"/> instance with the flipped image.</returns>
	public Bitmap FlipHorizontal()
	{
		var newBitmap = new SKBitmap( _bitmap.Width, _bitmap.Height, _bitmap.ColorType, _bitmap.AlphaType );

		using ( var canvas = new SKCanvas( newBitmap ) )
		{
			canvas.Scale( -1, 1 ); // Flip horizontally
			canvas.Translate( -_bitmap.Width, 0 ); // Adjust position
			canvas.DrawBitmap( _bitmap, 0, 0, SKSamplingOptions.Default );
		}

		return new Bitmap( newBitmap );
	}

	/// <summary>
	/// Crops the bitmap to the specified rectangle.
	/// </summary>
	/// <param name="rect">The rectangle to crop to.</param>
	/// <returns>A new <see cref="Bitmap"/> instance with the cropped image.</returns>
	public Bitmap Crop( Rect rect )
	{
		var newBitmap = new SKBitmap( (int)rect.Width, (int)rect.Height, _bitmap.ColorType, _bitmap.AlphaType );

		using ( var canvas = new SKCanvas( newBitmap ) )
		{
			var sourceRect = rect.ToSk();
			var destRect = new SKRect( 0, 0, rect.Width, rect.Height );
			canvas.DrawBitmap( _bitmap, sourceRect, destRect, SKSamplingOptions.Default );
		}

		return new Bitmap( newBitmap );
	}
}
