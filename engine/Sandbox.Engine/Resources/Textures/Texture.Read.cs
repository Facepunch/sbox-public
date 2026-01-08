using NativeEngine;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sandbox;

public partial class Texture
{
	/// <summary>
	/// Reads pixel colors from the texture at the specified mip level
	/// </summary>
	[Pure]
	public Color32[] GetPixels( int mip = 0 )
	{
		// BUG: Width/Height is on disk and not the currently streamed mip
		//      I don't know what would break if I made Width/Height represent that instead
		var desc = g_pRenderDevice.GetTextureDesc( native );

		mip = Math.Clamp( mip, 0, Mips - 1 );
		var d = 1 << mip;

		if ( Depth == 1 )
		{
			var rect = (X: 0, Y: 0, W: desc.m_nWidth / d, H: desc.m_nHeight / d);
			var data = new Color32[rect.W * rect.H];

			GetPixels( rect, 0, mip, data.AsSpan(), ImageFormat.RGBA8888 );

			return data;
		}
		else
		{
			var box = (X: 0, Y: 0, Z: 0, Width: desc.m_nWidth / d, Height: desc.m_nHeight / d, Depth: Depth / d);
			var data = new Color32[box.Width * box.Height * box.Depth];

			GetPixels3D( box, mip, data.AsSpan(), ImageFormat.RGBA8888 );

			return data;
		}
	}

	public unsafe Bitmap GetBitmap( int mip )
	{
		mip = Math.Clamp( mip, 0, Mips - 1 );
		var d = 1 << mip;

		bool floatingPoint = ImageFormat == ImageFormat.RGBA16161616F;

		var desc = g_pRenderDevice.GetTextureDesc( native );
		var width = desc.m_nWidth / d;
		var height = desc.m_nHeight / d;
		var depth = desc.m_nDepth;
		var outputFormat = floatingPoint ? ImageFormat.RGBA16161616F : ImageFormat.RGBA8888;
		var targetMemoryRequired = NativeEngine.ImageLoader.GetMemRequired( width, height, depth, 1, outputFormat );

		if ( targetMemoryRequired <= 0 )
		{
			//
			// If desc.m_nWidth and height are 0, this is probably the rendersystemempty, which is obviously a disaster
			//

			throw new System.Exception( $"targetMemoryRequired <= 0 ({width}x{height}x{depth} {outputFormat})" );
		}

		var bitmap = new Bitmap( width, height * depth, floatingPoint );
		var data = bitmap.GetBuffer();

		if ( data.Length != targetMemoryRequired )
		{
			throw new System.Exception( $"Buffer isn't big enough {data.Length} != {targetMemoryRequired}" );
		}

		fixed ( byte* pData = data )
		{
			if ( depth > 1 )
			{
				GetPixels3D( (0, 0, 0, width, height, depth), mip, data, outputFormat );
			}
			else
			{
				var rect = new NativeRect( 0, 0, width, height );

				if ( !g_pRenderDevice.ReadTexturePixels( native, ref rect, 0, mip, ref rect, (IntPtr)pData, outputFormat, 0 ) )
					return null;
			}

		}

		return bitmap;
	}

	private static int GetImageFormatSize( ImageFormat format )
	{
		switch ( format )
		{
			case ImageFormat.RGBA8888:
			case ImageFormat.BGRA8888:
			case ImageFormat.ARGB8888:
			case ImageFormat.ABGR8888:
			case ImageFormat.R32F:
			case ImageFormat.R32_UINT:
				return 4;

			case ImageFormat.RGB888:
			case ImageFormat.BGR888:
				return 3;

			case ImageFormat.R16:
			case ImageFormat.R16F:
				return 2;

			case ImageFormat.I8:
				return 1;

			default:
				throw new NotImplementedException( $"Reading pixels with format {format} not yet implemented." );
		}
	}

	private static int GetImageFormatSize( ImageFormat format, int width, int height )
	{
		return NativeEngine.ImageLoader.GetMemRequired( width, height, 1, 1, format );
	}

	/// <summary>
	/// Reads a 2D range of pixel values from the texture at the specified mip level, writing to <paramref name="dstData"/>.
	/// This reads one slice from a 2D texture array or 3D texture volume.
	/// </summary>
	/// <typeparam name="T">Pixel value type (e.g., <see cref="Color32"/>, <see cref="float"/>, <see cref="uint"/> or <see cref="byte"/>)</typeparam>
	/// <param name="srcRect">Pixel region to read.</param>
	/// <param name="slice">For 2D texture arrays or 3D texture volumes, which slice to read from.</param>
	/// <param name="mip">Mip level to read from.</param>
	/// <param name="dstData">Array to write to, starting at index 0 for the first read pixel.</param>
	/// <param name="dstFormat">Pixel format to use when writing to <paramref name="dstData"/>. We only support some common formats for now.</param>
	/// <param name="dstSize">Dimensions of destination pixel array. Matches <paramref name="srcRect"/> by default.</param>
	[Pure]
	public unsafe void GetPixels<T>( (int X, int Y, int Width, int Height) srcRect, int slice, int mip, Span<T> dstData, ImageFormat dstFormat, (int X, int Y) dstSize = default )
		where T : unmanaged
	{
		var pixelSize = Unsafe.SizeOf<T>();

		if ( dstSize.X < 0 || dstSize.Y < 0 )
			throw new ArgumentException( $"{nameof( dstSize )} can't be negative" );

		if ( dstSize.X == 0 )
			dstSize.X = srcRect.Width;

		if ( dstSize.Y == 0 )
			dstSize.Y = srcRect.Height;

		if ( srcRect.X < 0 || srcRect.Y < 0 ||
			((long)srcRect.X + srcRect.Width) > Width ||
			((long)srcRect.Y + srcRect.Height) > Height )
		{
			throw new ArgumentException( $"{nameof( srcRect )} out of range" );
		}

		var dstStrideBytes = GetImageFormatSize( dstFormat, dstSize.X, 1 );
		if ( dstStrideBytes <= 0 )
			throw new ArgumentException( $"{nameof( dstSize )} invalid" );
		if ( dstStrideBytes > int.MaxValue )
			throw new ArgumentException( $"{nameof( dstSize )} too large" );

		var sliceSize = GetImageFormatSize( dstFormat, dstSize.X, dstSize.Y );
		if ( sliceSize <= 0 )
			throw new ArgumentException( $"{nameof( dstSize )} invalid" );

		if ( !SandboxedUnsafe.IsAcceptablePod<T>() )
			throw new ArgumentException( $"{nameof( T )} must be a POD type" );

		var dataSize = (long)dstData.Length * pixelSize;

		if ( dataSize < sliceSize )
		{
			throw new ArgumentException( "Output array is too small to fit the given pixel range.",
				nameof( dstData ) );
		}

		var nativeSrcRect = new NativeRect { x = srcRect.X, y = srcRect.Y, w = srcRect.Width, h = srcRect.Height };
		var nativeDstRect = new NativeRect { x = 0, y = 0, w = dstSize.X, h = dstSize.Y };

		fixed ( T* dataPtr = dstData )
		{
			if ( !g_pRenderDevice.ReadTexturePixels( native, ref nativeSrcRect, slice, mip, ref nativeDstRect, (IntPtr)dataPtr, dstFormat, dstStrideBytes ) )
				throw new Exception( "Unable to read texture pixels" );
		}
	}

	/// <summary>
	/// Reads a 2D range of pixel values from the texture at the specified mip level, writing to <paramref name="dstData"/>.
	/// This reads one slice from a 2D texture array or 3D texture volume.
	/// </summary>
	/// <typeparam name="T">Pixel value type (e.g., <see cref="Color32"/>, <see cref="float"/>, <see cref="uint"/> or <see cref="byte"/>)</typeparam>
	/// <param name="srcRect">Pixel region to read.</param>
	/// <param name="slice">For 2D texture arrays or 3D texture volumes, which slice to read from.</param>
	/// <param name="mip">Mip level to read from.</param>
	/// <param name="dstData">Array to write to, starting at index 0 for the first read pixel.</param>
	/// <param name="dstFormat">Pixel format to use when writing to <paramref name="dstData"/>. We only support some common formats for now.</param>
	/// <param name="dstRect">Dimensions of destination pixel array. Matches <paramref name="srcRect"/> by default.</param>
	/// <param name="dstStride">Stride of the destination array, this is likely your width in pixels.</param>
	[Pure]
	public unsafe void GetPixels<T>( (int X, int Y, int Width, int Height) srcRect, int slice, int mip, Span<T> dstData, ImageFormat dstFormat, (int X, int Y, int W, int H) dstRect, int dstStride )
		where T : unmanaged
	{
		var pixelByteSize = Unsafe.SizeOf<T>();

		if ( dstRect.W < 0 || dstRect.H < 0 )
			throw new ArgumentException( $"{nameof( dstRect )} can't be negative" );

		if ( dstRect.W == 0 )
			dstRect.W = srcRect.Width;

		if ( dstRect.H == 0 )
			dstRect.H = srcRect.Height;

		if ( srcRect.X < 0 || srcRect.Y < 0 ||
			((long)srcRect.X + srcRect.Width) > Width ||
			((long)srcRect.Y + srcRect.Height) > Height )
		{
			throw new ArgumentException( $"{nameof( srcRect )} out of range" );
		}

		var maxLength = (dstRect.Y + dstRect.H - 1) * dstStride + dstRect.X + dstRect.W;
		if ( maxLength >= dstData.Length )
			throw new ArgumentException( $"Output rect size ({maxLength}) exceeds destination array size {dstData.Length}" );

		if ( maxLength <= 0 )
			throw new ArgumentException( $"Output rect size ({maxLength}) below zero or equal to zero" );

		if ( maxLength > int.MaxValue )
			throw new ArgumentException( $"Output rect size ({maxLength}) too large" );

		var sliceSize = GetImageFormatSize( dstFormat ) * maxLength;
		if ( sliceSize <= 0 )
			throw new ArgumentException( $"{nameof( dstRect )} invalid" );

		if ( !SandboxedUnsafe.IsAcceptablePod<T>() )
			throw new ArgumentException( $"{nameof( T )} must be a POD type" );

		var dataSize = (long)dstData.Length * pixelByteSize;

		if ( dataSize < sliceSize )
		{
			throw new ArgumentException( "Output array is too small to fit the given pixel range.",
				nameof( dstData ) );
		}

		var nativeSrcRect = new NativeRect { x = srcRect.X, y = srcRect.Y, w = srcRect.Width, h = srcRect.Height };
		var nativeDstRect = new NativeRect { x = dstRect.X, y = dstRect.Y, w = dstRect.W, h = dstRect.H };

		fixed ( T* dataPtr = dstData )
		{
			if ( !g_pRenderDevice.ReadTexturePixels( native, ref nativeSrcRect, slice, mip, ref nativeDstRect, (IntPtr)dataPtr, dstFormat, (int)dstStride * pixelByteSize ) )
				throw new Exception( "Unable to read texture pixels" );
		}
	}

	/// <summary>
	/// Reads a 3D range of pixel values from the texture at the specified mip level, writing to <paramref name="dstData"/>.
	/// This can be used with a 2D texture array, or a 3D volume texture.
	/// </summary>
	/// <typeparam name="T">Pixel value type (e.g., <see cref="Color32"/>, <see cref="float"/>, <see cref="uint"/> or <see cref="byte"/>)</typeparam>
	/// <param name="srcBox">Pixel region to read.</param>
	/// <param name="mip">Mip level to read from.</param>
	/// <param name="dstData">Array to write to, starting at index 0 for the first read pixel.</param>
	/// <param name="dstFormat">Pixel format to use when writing to <paramref name="dstData"/>. We only support some common formats for now.</param>
	/// <param name="dstSize">Dimensions of destination pixel array. Matches <paramref name="srcBox"/> by default.</param>
	[Pure]
	public void GetPixels3D<T>( (int X, int Y, int Z, int Width, int Height, int Depth) srcBox, int mip, Span<T> dstData, ImageFormat dstFormat, (int X, int Y, int Z) dstSize = default ) where T : unmanaged
	{
		if ( dstSize.X < 0 || dstSize.Y < 0 || dstSize.Z < 0 )
			throw new ArgumentException( $"{nameof( dstSize )} can't be negative" );

		if ( dstSize.X == 0 )
			dstSize.X = srcBox.Width;

		if ( dstSize.Y == 0 )
			dstSize.Y = srcBox.Height;

		if ( dstSize.Z == 0 )
			dstSize.Z = srcBox.Depth;

		if ( srcBox.X < 0 || srcBox.Y < 0 || srcBox.Z < 0 ||
			((long)srcBox.X + srcBox.Width) > Width ||
			((long)srcBox.Y + srcBox.Height) > Height ||
			((long)srcBox.Z + srcBox.Depth) > Depth )
		{
			throw new ArgumentException( $"{nameof( srcBox )} out of range" );
		}

		if ( !SandboxedUnsafe.IsAcceptablePod<T>() )
			throw new ArgumentException( $"{nameof( T )} must be a POD type" );

		var sliceSize = GetImageFormatSize( dstFormat, dstSize.X, dstSize.Y );
		if ( sliceSize <= 0 )
			throw new ArgumentException( $"{nameof( dstSize )} invalid" );
		var sliceStride = sliceSize / Unsafe.SizeOf<T>();

		for ( var z = 0; z < srcBox.Depth; ++z )
		{
			GetPixels(
				(srcBox.X, srcBox.Y, srcBox.Width, srcBox.Height),
				srcBox.Z + z, mip,
				dstData.Slice( z * sliceStride, sliceStride ),
				dstFormat, (dstSize.X, dstSize.Y) );
		}
	}

	/// <summary>
	/// Reads a single pixel color.
	/// </summary>
	[Pure]
	public unsafe Color32 GetPixel( float x, float y, int mip = 0 )
	{
		x = x.FloorToInt();
		y = y.FloorToInt();

		if ( x < 0 ) return Color.Green;
		if ( y < 0 ) return Color.Green;
		if ( x > Width - 1 ) return Color.Green;
		if ( y > Height - 1 ) return Color.Green;

		mip = Math.Clamp( mip, 0, Mips - 1 );

		int d = (int)Math.Pow( 2, mip );

		var data = new Color32[1];
		var source = new NativeRect { x = (int)x, y = (int)y, w = 1, h = 1 };
		var dest = new NativeRect { x = 0, y = 0, w = 1, h = 1 };

		fixed ( Color32* dataPtr = data )
		{
			if ( !g_pRenderDevice.ReadTexturePixels( native, ref source, 0, mip, ref dest, (IntPtr)dataPtr, ImageFormat.RGBA8888, 0 ) )
				return Color.Red;
		}

		return data[0];
	}

	/// <summary>
	/// Reads a single pixel color from a volume or array texture.
	/// </summary>
	[Pure]
	public unsafe Color32 GetPixel3D( float x, float y, float z, int mip = 0 )
	{
		x = x.FloorToInt();
		y = y.FloorToInt();
		z = z.FloorToInt();

		if ( x < 0 ) return Color.Green;
		if ( y < 0 ) return Color.Green;
		if ( z < 0 ) return Color.Green;
		if ( x > Width - 1 ) return Color.Green;
		if ( y > Height - 1 ) return Color.Green;
		if ( z > Depth - 1 ) return Color.Green;

		mip = Math.Clamp( mip, 0, Mips - 1 );

		var data = new Color32[1];
		var source = new NativeRect { x = (int)x, y = (int)y, w = 1, h = 1 };
		var dest = new NativeRect { x = 0, y = 0, w = 1, h = 1 };

		fixed ( Color32* dataPtr = data )
		{
			if ( !g_pRenderDevice.ReadTexturePixels( native, ref source, (int)z, mip, ref dest, (IntPtr)dataPtr, ImageFormat.RGBA8888, 0 ) )
				return Color.Red;
		}

		return data[0];
	}

	/// <summary>
	/// Asynchronously reads all pixel colors from the texture at the specified mip level.
	/// </summary>
	/// <param name="callback">Callback function that receives the pixel data when ready.</param>
	/// <param name="mip">Mip level to read from.</param>
	/// <remarks>
	/// This operation is asynchronous and won't block the calling thread while data is downloaded from the GPU.
	/// The data provided to the callback is only valid for the duration of the callback execution.
	/// Storing references to the Span beyond the callback's scope will result in undefined behavior.
	/// </remarks>
	public void GetPixelsAsync( Action<ReadOnlySpan<Color32>> callback, int mip = 0 )
	{
		mip = Math.Clamp( mip, 0, Mips - 1 );
		var d = 1 << mip;

		var desc = g_pRenderDevice.GetTextureDesc( native );

		if ( Depth == 1 )
		{
			var width = desc.m_nWidth / d;
			var height = desc.m_nHeight / d;
			var rect = (X: 0, Y: 0, Width: width, Height: height);

			GetPixelsAsync( callback, ImageFormat.RGBA8888, rect, 0, mip );
		}
		else
		{
			var box = (X: 0, Y: 0, Z: 0, Width: desc.m_nWidth / d, Height: desc.m_nHeight / d, Depth: Depth / d);

			GetPixelsAsync3D( callback, ImageFormat.RGBA8888, box, mip );
		}
	}

	/// <summary>
	/// Asynchronously reads a 2D range of pixel values from the texture at the specified mip level.
	/// </summary>
	/// <typeparam name="T">Pixel value type (e.g., <see cref="Color32"/>, <see cref="float"/>, <see cref="uint"/> or <see cref="byte"/>)</typeparam>
	/// <param name="callback">Callback function that receives the pixel data when ready.</param>
	/// <param name="dstFormat">Pixel format to use when writing to the destination buffer.</param>
	/// <param name="srcRect">Pixel region to read. If omitted full texture will be read.</param>
	/// <param name="slice">For 2D texture arrays or 3D texture volumes, which slice to read from.</param>
	/// <param name="mip">Mip level to read from.</param>
	/// <remarks>
	/// This operation is asynchronous and won't block the calling thread while data is downloaded from the GPU.
	/// The data provided to the callback is only valid for the duration of the callback execution.
	/// Storing references to the Span beyond the callback's scope will result in undefined behavior.
	/// </remarks>
	public void GetPixelsAsync<T>( Action<ReadOnlySpan<T>> callback, ImageFormat dstFormat = ImageFormat.Default, (int X, int Y, int Width, int Height) srcRect = default, int slice = 0, int mip = 0 ) where T : unmanaged
	{
		if ( srcRect.X < 0 || srcRect.Y < 0 ||
			((long)srcRect.X + srcRect.Width) > Width ||
			((long)srcRect.Y + srcRect.Height) > Height )
		{
			throw new ArgumentException( $"{nameof( srcRect )} out of range" );
		}

		if ( !SandboxedUnsafe.IsAcceptablePod<T>() )
			throw new ArgumentException( $"{nameof( T )} must be a POD type" );

		if ( dstFormat == ImageFormat.Default ) dstFormat = ImageFormat;

		var context = g_pRenderDevice.CreateRenderContext( 0 );

		context.ReadTextureAsync( this, ( readData, readFormat, readMip, readWidth, readHeight, doneWithData ) =>
		{
			if ( dstFormat != readFormat )
			{
				int dstSize = GetImageFormatSize( dstFormat, readWidth, readHeight );
				byte[] dstBuffer = ArrayPool<byte>.Shared.Rent( dstSize );
				try
				{
					ConvertImageDataTo( readData, readFormat, dstBuffer.AsSpan(), dstFormat, readWidth, readHeight );
					doneWithData();
					var typedResult = MemoryMarshal.Cast<byte, T>( dstBuffer );
					callback( typedResult );

				}
				finally
				{
					ArrayPool<byte>.Shared.Return( dstBuffer );
				}
			}
			else
			{
				var typedResult = MemoryMarshal.Cast<byte, T>( readData );
				callback( typedResult );
			}
		}, slice, mip, srcRect );

		context.Submit();

		g_pRenderDevice.ReleaseRenderContext( context );
	}

	/// <summary>
	/// Asynchronously reads a 3D range of pixel values from the texture at the specified mip level.
	/// </summary>
	/// <typeparam name="T">Pixel value type (e.g., <see cref="Color32"/>, <see cref="float"/>, <see cref="uint"/> or <see cref="byte"/>)</typeparam>
	/// <param name="callback">Callback function that receives the pixel data when ready.</param>
	/// <param name="dstFormat">Pixel format to use when writing to the destination buffer.</param>
	/// <param name="srcBox">Pixel region to read. If omitted full texture will be read.</param>
	/// <param name="mip">Mip level to read from.</param>
	/// <remarks>
	/// This operation is asynchronous and won't block the calling thread while data is downloaded from the GPU.
	/// The data provided to the callback is only valid for the duration of the callback execution.
	/// Storing references to the Span beyond the callback's scope will result in undefined behavior.
	/// </remarks>
	public void GetPixelsAsync3D<T>( Action<ReadOnlySpan<T>> callback, ImageFormat dstFormat = ImageFormat.Default, (int X, int Y, int Z, int Width, int Height, int Depth) srcBox = default, int mip = 0 ) where T : unmanaged
	{
		// Default to full texture if not specified
		if ( srcBox.Depth == 0 )
			srcBox.Depth = Depth;

		if ( srcBox.X < 0 || srcBox.Y < 0 || srcBox.Z < 0 ||
			((long)srcBox.X + srcBox.Width) > Width ||
			((long)srcBox.Y + srcBox.Height) > Height ||
			((long)srcBox.Z + srcBox.Depth) > Depth )
		{
			throw new ArgumentException( $"{nameof( srcBox )} out of range" );
		}

		if ( !SandboxedUnsafe.IsAcceptablePod<T>() )
			throw new ArgumentException( $"{nameof( T )} must be a POD type" );

		var sliceSize = GetImageFormatSize( dstFormat, srcBox.Width, srcBox.Height );
		if ( sliceSize <= 0 )
			throw new ArgumentException( $"{nameof( srcBox )} invalid size" );

		if ( dstFormat == ImageFormat.Default ) dstFormat = ImageFormat;

		var totalBytes = sliceSize * srcBox.Depth;

		// Create final buffer to hold all slices
		byte[] resultBuffer = ArrayPool<byte>.Shared.Rent( totalBytes );

		var completedSlices = 0;
		var context = g_pRenderDevice.CreateRenderContext( 0 );

		// Process each slice asynchronously
		for ( var z = 0; z < srcBox.Depth; z++ )
		{
			var currentZ = z;
			var srcRect = (srcBox.X, srcBox.Y, srcBox.Width, srcBox.Height);
			var sliceIndex = srcBox.Z + currentZ;

			context.ReadTextureAsync( this, ( readData, readFormat, readMip, readWidth, readHeight, doneWithData ) =>
			{
				var sliceOffset = currentZ * sliceSize;

				if ( dstFormat != readFormat )
				{
					ConvertImageDataTo( readData, readFormat, resultBuffer.AsSpan( sliceOffset ), dstFormat, readWidth, readHeight );
				}
				else
				{
					readData.CopyTo( resultBuffer.AsSpan( sliceOffset, sliceSize ) );
				}
				doneWithData();

				// Check if this was the last slice
				if ( Interlocked.Increment( ref completedSlices ) == srcBox.Depth )
				{
					try
					{
						var typedResult = MemoryMarshal.Cast<byte, T>( resultBuffer );
						// All slices are ready, call the callback with the final buffer
						callback( typedResult );
					}
					finally
					{
						ArrayPool<byte>.Shared.Return( resultBuffer );
					}
				}
			}, sliceIndex, mip, srcRect );
		}

		context.Submit();

		g_pRenderDevice.ReleaseRenderContext( context );
	}

	/// <summary>
	/// Asynchronously reads the texture into a bitmap at the specified mip level.
	/// </summary>
	/// <param name="callback">Callback function that receives the bitmap when ready.</param>
	/// <param name="mip">Mip level to read from.</param>
	/// <remarks>
	/// This operation is asynchronous and won't block the calling thread while data is downloaded from the GPU.
	/// Unlike the other async methods, the Bitmap provided to the callback is valid beyond the callback's scope
	/// as it owns its memory.
	/// </remarks>
	public void GetBitmapAsync( Action<Bitmap> callback, int mip = 0 )
	{
		mip = Math.Clamp( mip, 0, Mips - 1 );
		var d = 1 << mip;

		bool floatingPoint = ImageFormat == ImageFormat.RGBA16161616F;
		var desc = g_pRenderDevice.GetTextureDesc( native );
		var width = desc.m_nWidth / d;
		var height = desc.m_nHeight / d;
		var dstFormat = floatingPoint ? ImageFormat.RGBA16161616F : ImageFormat.RGBA8888;

		var context = g_pRenderDevice.CreateRenderContext( 0 );

		context.ReadTextureAsync( this, ( readData, readFormat, readMip, readWidth, readHeight, doneWithData ) =>
		{
			var bitmap = new Bitmap( width, height, floatingPoint );
			var bitmapData = bitmap.GetBuffer();

			if ( dstFormat != readFormat )
			{
				var byteSpan = MemoryMarshal.Cast<byte, byte>( bitmapData );
				ConvertImageDataTo( readData, readFormat, byteSpan, dstFormat, width, height );
			}
			else
			{
				readData.CopyTo( bitmapData );
			}
			doneWithData();

			callback( bitmap );
		}, 0, mip, (0, 0, width, height) );

		context.Submit();

		g_pRenderDevice.ReleaseRenderContext( context );
	}

	/// <summary>
	/// Converts image data from one format to another and writes to an existing buffer.
	/// </summary>
	private static unsafe void ConvertImageDataTo(
		ReadOnlySpan<byte> srcData,
		ImageFormat srcFormat,
		Span<byte> dstBuffer,
		ImageFormat dstFormat,
		int width,
		int height )
	{
		fixed ( byte* srcPtr = srcData )
		fixed ( byte* dstPtr = dstBuffer )
		{
			ImageLoader.ConvertImageFormat(
				(IntPtr)srcPtr, srcFormat,
				(IntPtr)dstPtr, dstFormat,
				width, height,
				0, width * GetImageFormatSize( dstFormat ) );
		}
	}
}
