using Sandbox.Utility;
using SkiaSharp;

namespace Sandbox;

public partial class Bitmap
{
	/// <summary>
	/// Exports the bitmap as a JPEG byte array with the specified quality.
	/// </summary>
	/// <param name="quality">The quality of the JPEG, between 0 and 100.</param>
	/// <returns>A byte array containing the JPEG image data.</returns>
	public byte[] ToJpg( int quality = 100 )
	{
		return Encode( SKEncodedImageFormat.Jpeg, quality );
	}

	/// <summary>
	/// Exports the bitmap as a PNG byte array.
	/// </summary>
	/// <returns>A byte array containing the PNG image data.</returns>
	public byte[] ToPng()
	{
		return Encode( SKEncodedImageFormat.Png, 100 );
	}

	/// <summary>
	/// Exports the bitmap as a BMP byte array.
	/// </summary>
	/// <returns>A byte array containing the BMP image data.</returns>
	public byte[] ToBmp()
	{
		return Encode( SKEncodedImageFormat.Bmp, 100 );
	}

	/// <summary>
	/// Exports the bitmap as an HDR WebP byte array with the specified quality.
	/// </summary>
	/// <param name="quality">The quality of the WebP image, between 0 and 100.</param>
	/// <returns>A byte array containing the WebP HDR image data.</returns>
	public byte[] ToWebP( int quality = 100 )
	{
		return Encode( SKEncodedImageFormat.Webp, quality );
	}

	/// <summary>
	/// Exports the bitmap to the specified image format with optional quality.
	/// </summary>
	/// <param name="format">The image format (e.g., PNG, JPEG, BMP).</param>
	/// <param name="quality">The quality of the image, used for formats like JPEG.</param>
	/// <returns>A byte array containing the image data.</returns>
	private byte[] Encode( SKEncodedImageFormat format, int quality )
	{
		using var image = SKImage.FromBitmap( _bitmap );
		using var data = image.Encode( format, quality );
		return data.ToArray();
	}

	/// <summary>
	/// Exports the bitmap to the specified engine format
	/// </summary>
	/// <param name="format">The target image format to encode to.</param>
	public byte[] ToFormat( ImageFormat format )
	{
		var data = _bitmap.GetPixels();

		using var fbm = new FloatBitmap( Width, Height, ImageFormat, data, ByteCount, srgb: false );
		return fbm.EncodeTo( format );
	}

}
