using SkiaSharp;

namespace Sandbox;

public partial class Bitmap
{
	public unsafe static Bitmap CreateFromTgaBytes( byte[] data )
	{
		if ( !IsTga( data ) || !IsTgaPayloadValid( data ) )
			return default;

		FloatBitMap_t fbm = FloatBitMap_t.Create();

		try
		{
			fixed ( byte* ptr = data )
			{
				var success = fbm.LoadFromInMemoryTGA( (IntPtr)ptr, data.Length );
				if ( !success ) return null;

				var bitmap = new SKBitmap( fbm.Width(), fbm.Height(), SKColorType.Rgba8888, SKAlphaType.Unpremul );

				fbm.WriteToBuffer( bitmap.GetPixels(), bitmap.ByteCount, ImageFormat.RGBA8888, false, false, 0 );

				return new Bitmap( bitmap );
			}
		}
		finally
		{
			fbm.Delete();
		}
	}

	/// <summary>
	/// Return true if this data is a Tga file
	/// </summary>
	public static bool IsTga( byte[] data )
	{
		if ( data is null || data.Length < 18 ) return false;
		int imageType = data[2];

		if ( imageType == 2 || imageType == 3 || imageType == 10 )
		{
			int width = data[12] | (data[13] << 8);
			int height = data[14] | (data[15] << 8);
			return width > 0 && height > 0;
		}

		return false;
	}

	internal const int MaxTgaPixels = 8192 * 8192;

	internal static bool IsTgaPayloadValid( byte[] data )
	{
		if ( !IsTga( data ) ) return false;

		int idLength = data[0];
		int colormapLength = data[5] | (data[6] << 8);
		int imageType = data[2];
		int width = data[12] | (data[13] << 8);
		int height = data[14] | (data[15] << 8);
		int pixelSize = data[16];

		if ( colormapLength != 0 ) return false;
		if ( (long)width * height > MaxTgaPixels ) return false;

		int bytesPerPixel = imageType == 3 ? 1 : pixelSize >> 3;
		if ( imageType == 3 && pixelSize != 8 ) return false;
		if ( imageType != 3 && pixelSize != 24 && pixelSize != 32 ) return false;

		long offset = 18 + idLength;
		long pixels = (long)width * height;

		if ( imageType != 10 )
			return offset + pixels * bytesPerPixel <= data.Length;

		while ( pixels > 0 )
		{
			if ( offset >= data.Length ) return false;

			int packet = data[offset++];
			int count = 1 + (packet & 0x7f);
			offset += (packet & 0x80) != 0 ? bytesPerPixel : (long)count * bytesPerPixel;
			if ( offset > data.Length ) return false;

			pixels -= count;
		}

		return true;
	}
}
