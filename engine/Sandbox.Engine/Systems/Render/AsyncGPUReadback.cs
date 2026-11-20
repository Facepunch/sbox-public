using NativeEngine;

namespace Sandbox;

/// <summary>
/// Reads of textures and buffers whose callbacks run off the calling thread, as the GPU's were. What reads them is
/// <see cref="RenderBackend.Default"/>: Borealis.Gpu's device, or the headless one's copies.
/// </summary>
internal static class AsyncGPUReadback
{
	internal delegate void TextureReadDelegate( Span<byte> readData, ImageFormat readFormat, int readMipLevel, int readWidth, int readHeight, Action doneWithData );

	internal delegate void BufferReadDelegate( Span<byte> pData );

	/// <summary>
	/// Reads texture data, handing it to the callback on a worker; the data is valid until the callback returns.
	/// If srcRect is not specified, the entire texture will be read.
	/// </summary>
	internal static void ReadTextureAsync( Texture texture, TextureReadDelegate callback, int slice = 0, int mipLevel = 0, (int X, int Y, int Width, int Height) srcRect = default )
	{
		if ( texture is null )
			return;

		ReadFromDevice( texture, callback, slice, mipLevel, srcRect );
	}

	/// <summary>
	/// Read a texture on <see cref="RenderBackend.Default"/> now, in its own format where that's uncompressed (else RGBA8, or
	/// RGBA16F for HDR), and hand it to the callback off this thread.
	/// </summary>
	static unsafe void ReadFromDevice( Texture texture, TextureReadDelegate callback, int slice, int mip, (int X, int Y, int Width, int Height) rect )
	{
		if ( rect.Width <= 0 || rect.Height <= 0 )
			rect = (0, 0, Math.Max( texture.Width >> mip, 1 ), Math.Max( texture.Height >> mip, 1 ));

		var format = texture.ImageFormat switch
		{
			ImageFormat.DXT1 or ImageFormat.DXT3 or ImageFormat.DXT5 or ImageFormat.DXT1_ONEBITALPHA or ImageFormat.DXT5_NM or ImageFormat.ATI1N or ImageFormat.ATI2N or ImageFormat.BC7 => ImageFormat.RGBA8888,
			ImageFormat.BC6H => ImageFormat.RGBA16161616F,
			var own => own,
		};

		var data = new byte[rect.Width * rect.Height * Math.Max( ImageLoader.GetMemRequired( 1, 1, 1, 1, format ), 0 )];
		bool read = false;
		if ( data.Length > 0 )
		{
			fixed ( byte* pixels = data )
			{
				read = RenderBackend.Default.ReadPixels( texture, new NativeRect( rect.X, rect.Y, rect.Width, rect.Height ), slice, mip, new NativeRect( 0, 0, rect.Width, rect.Height ), (IntPtr)pixels, format, 0 );
			}
		}

		Task.Run( () => callback( read ? data : Span<byte>.Empty, format, mip, read ? rect.Width : 0, read ? rect.Height : 0, () => { } ) );
	}
}
