using NativeEngine;

namespace Sandbox.Utility;

public class FloatBitmap : IDisposable
{
	FloatBitMap_t native;

	internal FloatBitmap( FloatBitMap_t fbm )
	{
		native = fbm;
	}

	internal unsafe FloatBitmap( int width, int height, ImageFormat format, IntPtr data, int dataSize, bool srgb = false )
	{
		native = FloatBitMap_t.Create( width, height );

		if ( data != default && dataSize > 0 )
		{
			native.LoadFromBuffer( data, dataSize, format, srgb ? FBMGammaType_t.FBM_GAMMA_SRGB : FBMGammaType_t.FBM_GAMMA_LINEAR );
		}
	}

	~FloatBitmap()
	{
		Dispose();
	}

	public void Dispose()
	{
		if ( native.IsNull )
			return;

		GC.SuppressFinalize( this );

		native.Shutdown();
		native.Delete();
		native = IntPtr.Zero;
	}

	public int Width => native.Width();
	public int Height => native.Height();
	public int Depth => native.Depth();

	public void Resize( int width, int height, bool clamp = true )
	{
		if ( native.IsNull )
			return;

		native.Resize2D( width, height, clamp );
	}

	public unsafe byte[] EncodeTo( ImageFormat format )
	{
		if ( native.IsNull )
			return null;

		var dataSize = ImageLoader.GetMemRequired( Width, Height, 1, 1, format );
		if ( dataSize <= 0 )
			return null;

		var data = new byte[dataSize];

		fixed ( byte* pData = data )
		{
			//	uint FLOAT_BITMAP_PREFER_RUNTIME_FRIENDLY_DXT_ENCODER = 1;
			if ( !native.WriteToBuffer( (IntPtr)pData, data.Length, format, false, false, 0 ) )
			{
				return default;
			}
		}

		return data;
	}

	public unsafe byte[] EncodeTo( ImageFormat format, bool srgb )
	{
		if ( native.IsNull )
			return null;

		var dataSize = ImageLoader.GetMemRequired( Width, Height, 1, 1, format );
		if ( dataSize <= 0 )
			return null;

		var data = new byte[dataSize];

		fixed ( byte* pData = data )
		{
			//	uint FLOAT_BITMAP_PREFER_RUNTIME_FRIENDLY_DXT_ENCODER = 1;
			if ( !native.WriteToBuffer( (IntPtr)pData, data.Length, format, false, srgb, 0 ) )
			{
				return default;
			}
		}

		return data;
	}
}
