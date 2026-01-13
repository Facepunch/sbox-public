using SkiaSharp;

namespace Sandbox;

public partial class Bitmap
{
	/// <summary>
	/// Loads a bitmap from the specified byte array.
	/// </summary>
	/// <param name="data">The byte array containing the image data.</param>
	/// <returns>A new <see cref="Bitmap"/> instance.</returns>
	public static Bitmap CreateFromBytes( byte[] data )
	{
		// load via skia
		{
			using var stream = new SKMemoryStream( data );
			using var codec = SKCodec.Create( stream );

			if ( codec is not null )
			{
				var originalInfo = codec.Info;

				var info = new SKImageInfo( codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul );

				var skBitmap = SKBitmap.Decode( codec, info );

				if ( skBitmap is not null )
				{
					return new Bitmap( skBitmap );
				}
			}
		}

		return CreateFromTgaBytes( data )
			?? CreateFromPsdBytes( data )
			?? CreateFromTifBytes( data )
			?? CreateFromIesBytes( data );
	}
}
