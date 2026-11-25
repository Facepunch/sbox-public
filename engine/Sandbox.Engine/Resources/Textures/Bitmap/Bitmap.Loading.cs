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
		Bitmap bitmap;
		using ( var stream = new SKMemoryStream( data ) )
		using ( var codec = SKCodec.Create( stream ) )
		{
			bitmap = DecodeSkia( codec );
		}

		return bitmap ?? DecodeOtherFormats( data );
	}

	internal static unsafe Bitmap CreateFromFile( BaseFileSystem files, string path )
	{
		// ReadAllBytes returns a fresh buffer. Skia only borrows it until the pixels are decoded.
		var bytes = files.ReadAllBytes( path );
		Bitmap bitmap;
		fixed ( byte* encoded = bytes )
		{
			using var data = SKData.Create( (IntPtr)encoded, bytes.Length );
			using var codec = SKCodec.Create( data );
			bitmap = DecodeSkia( codec );
		}

		return bitmap ?? DecodeOtherFormats( bytes.ToArray() );
	}

	static Bitmap DecodeSkia( SKCodec codec )
	{
		if ( codec is null ) return null;
		var source = codec.Info;
		var info = new SKImageInfo( source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul );
		var pixels = SKBitmap.Decode( codec, info );
		return pixels is null ? null : new Bitmap( pixels );
	}

	static Bitmap DecodeOtherFormats( byte[] data )
	{
		return CreateFromTgaBytes( data )
			?? CreateFromPsdBytes( data )
			?? CreateFromTifBytes( data )
			?? CreateFromIesBytes( data );
	}
}
