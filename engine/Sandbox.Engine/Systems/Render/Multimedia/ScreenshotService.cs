using NativeEngine;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// Specifies the image format for screenshot output.
/// </summary>
public enum ScreenshotFormat
{
	PNG,
	JPEG,
	WebP
}

/// <summary>
/// Event arguments for screenshot capture completion.
/// </summary>
public sealed class ScreenshotCapturedEventArgs : EventArgs
{
	public string FilePath { get; init; }
	public int Width { get; init; }
	public int Height { get; init; }
	public ScreenshotFormat Format { get; init; }
	public long FileSize { get; init; }
	public bool IsHighRes { get; init; }
}

/// <summary>
/// Provides functionality to capture and save screenshots in various formats.
/// </summary>
internal static class ScreenshotService
{
	public const int MaxHighResDimension = 16384;
	public const int MinQuality = 1;
	public const int MaxQuality = 100;

	[ConVar( "screenshot_prefix", Help = "Prefix for auto-generated screenshot filenames" )]
	public static string ScreenshotPrefix { get; set; } = "sbox";

	[ConVar( "screenshot_format", Help = "Screenshot format: png, jpeg, or webp" )]
	public static ScreenshotFormat DefaultFormat { get; set; } = ScreenshotFormat.PNG;

	[ConVar( "screenshot_quality", Min = MinQuality, Max = MaxQuality, Help = "Quality for JPEG/WebP screenshots (1-100)" )]
	public static int ScreenshotQuality { get; set; } = 90;

	[ConVar( "screenshot_cursor", Help = "Include mouse cursor in screenshots" )]
	public static bool IncludeCursor { get; set; } = false;

	[ConVar( "screenshot_steam", Help = "Add screenshots to Steam library" )]
	public static bool AddToSteamLibrary { get; set; } = true;

	[ConVar( "screenshot_highres_msaa", Help = "MSAA level for high-res screenshots (1, 2, 4, 8, 16)" )]
	public static int HighResMSAA { get; set; } = 16;

	public static event EventHandler<ScreenshotCapturedEventArgs> OnScreenshotCaptured;

	private sealed record ScreenshotRequest(
		string FilePath,
		ScreenshotFormat Format,
		int Quality,
		bool IncludeCursor,
		bool AddToSteam,
		bool IsHighRes,
		bool HideUI,
		Action<ScreenshotCapturedEventArgs> Callback
	);

	private static readonly ConcurrentQueue<ScreenshotRequest> _pendingRequests = new();

	public static int PendingRequestCount => _pendingRequests.Count;

	internal static string RequestCapture()
	{
		return RequestCapture( DefaultFormat, ScreenshotQuality, IncludeCursor, AddToSteamLibrary, hideUI: false, null );
	}

	internal static string RequestCleanCapture()
	{
		return RequestCapture( DefaultFormat, ScreenshotQuality, IncludeCursor, AddToSteamLibrary, hideUI: true, null );
	}

	internal static string RequestCapture(
		ScreenshotFormat format,
		int quality,
		bool includeCursor,
		bool addToSteam,
		bool hideUI,
		Action<ScreenshotCapturedEventArgs> callback )
	{
		quality = Math.Clamp( quality, MinQuality, MaxQuality );

		string extension = GetFileExtension( format );
		string filePath = ScreenCaptureUtility.GenerateScreenshotFilename( extension );

		var request = new ScreenshotRequest(
			FilePath: filePath,
			Format: format,
			Quality: quality,
			IncludeCursor: includeCursor,
			AddToSteam: addToSteam,
			IsHighRes: false,
			HideUI: hideUI,
			Callback: callback
		);

		if ( hideUI )
		{
			Graphics.SuppressUI = true;
		}

		_pendingRequests.Enqueue( request );

		Log.Info( $"Screenshot request queued: {filePath}" );
		return filePath;
	}

	internal static void ProcessFrame( IRenderContext context, ITexture nativeTexture )
	{
		if ( nativeTexture.IsNull || !nativeTexture.IsStrongHandleValid() )
			return;

		while ( _pendingRequests.TryDequeue( out var request ) )
		{
			CaptureRenderTexture( context, nativeTexture, request );
		}
	}

	private static void CaptureRenderTexture( IRenderContext context, ITexture nativeTexture, ScreenshotRequest request )
	{
		try
		{
			context.ReadTextureAsync( nativeTexture, ( pData, format, mipLevel, width, height, _ ) =>
			{
				ProcessCapturedData( pData, width, height, request );
			} );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to initiate screenshot capture: {ex.Message}" );
		}
	}

	private static void ProcessCapturedData( Span<byte> pData, int width, int height, ScreenshotRequest request )
	{
		Bitmap bitmap = null;

		try
		{
			bitmap = new Bitmap( width, height );
			pData.CopyTo( bitmap.GetBuffer() );

#if WIN
			if ( request.IncludeCursor && Mouse.Active )
			{
				unsafe
				{
					fixed ( byte* dataPtr = bitmap.GetBuffer() )
					{
						BlitCursor( dataPtr, width, height, width * 4 );
					}
				}
			}
#endif

			if ( request.AddToSteam )
			{
				try
				{
					var rgbData = bitmap.ToFormat( ImageFormat.RGB888 );
					Services.Screenshots.AddScreenshotToLibrary( rgbData, width, height );
				}
				catch ( Exception ex )
				{
					Log.Warning( $"Failed to add screenshot to Steam library: {ex.Message}" );
				}
			}

			var directory = Path.GetDirectoryName( request.FilePath );
			if ( !string.IsNullOrEmpty( directory ) )
			{
				Directory.CreateDirectory( directory );
			}

			byte[] encodedBytes = EncodeBitmap( bitmap, request.Format, request.Quality );
			File.WriteAllBytes( request.FilePath, encodedBytes );

			var fileInfo = new FileInfo( request.FilePath );
			var eventArgs = new ScreenshotCapturedEventArgs
			{
				FilePath = request.FilePath,
				Width = width,
				Height = height,
				Format = request.Format,
				FileSize = fileInfo.Length,
				IsHighRes = request.IsHighRes
			};

			Log.Info( $"Screenshot saved: {request.FilePath} ({width}x{height}, {FormatFileSize( fileInfo.Length )})" );

			request.Callback?.Invoke( eventArgs );
			OnScreenshotCaptured?.Invoke( null, eventArgs );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to save screenshot '{request.FilePath}': {ex.Message}" );
		}
		finally
		{
			bitmap?.Dispose();

			if ( request.HideUI )
			{
				Graphics.SuppressUI = false;
			}
		}
	}

	public static void TakeHighResScreenshot( Scene scene, int width, int height )
	{
		TakeHighResScreenshot( scene, width, height, DefaultFormat, ScreenshotQuality, hideUI: false, null );
	}

	public static void TakeHighResScreenshot( Scene scene, int width, int height, bool hideUI )
	{
		TakeHighResScreenshot( scene, width, height, DefaultFormat, ScreenshotQuality, hideUI, null );
	}

	public static void TakeHighResScreenshot(
		Scene scene,
		int width,
		int height,
		ScreenshotFormat format,
		int quality,
		Action<ScreenshotCapturedEventArgs> callback )
	{
		TakeHighResScreenshot( scene, width, height, format, quality, hideUI: false, callback );
	}

	public static void TakeHighResScreenshot(
		Scene scene,
		int width,
		int height,
		ScreenshotFormat format,
		int quality,
		bool hideUI,
		Action<ScreenshotCapturedEventArgs> callback )
	{
		if ( !scene.IsValid() )
		{
			Log.Warning( "Cannot take high-res screenshot: No valid scene provided." );
			return;
		}

		if ( width <= 0 || height <= 0 )
		{
			Log.Warning( $"Cannot take high-res screenshot: Dimensions must be positive (got {width}x{height})." );
			return;
		}

		if ( width > MaxHighResDimension || height > MaxHighResDimension )
		{
			Log.Warning( $"Cannot take high-res screenshot: Maximum dimension is {MaxHighResDimension}px (got {width}x{height})." );
			return;
		}

		if ( scene.Camera is not { } camera || !camera.IsValid() )
		{
			Log.Warning( "Cannot take high-res screenshot: Scene does not have a valid camera." );
			return;
		}

		quality = Math.Clamp( quality, MinQuality, MaxQuality );
		string extension = GetFileExtension( format );

		Bitmap captureBitmap = null;
		RenderTarget renderTarget = null;
		var previousCustomSize = camera.CustomSize;

		try
		{
			if ( hideUI )
			{
				Graphics.SuppressUI = true;
			}

			camera.CustomSize = new Vector2( width, height );

			var msaa = HighResMSAA switch
			{
				>= 16 => MultisampleAmount.Multisample16x,
				>= 8 => MultisampleAmount.Multisample8x,
				>= 4 => MultisampleAmount.Multisample4x,
				>= 2 => MultisampleAmount.Multisample2x,
				_ => MultisampleAmount.MultisampleNone
			};

			renderTarget = RenderTarget.GetTemporary( width, height, ImageFormat.Default, ImageFormat.Default, msaa, 1, "HighResScreenshot" );
			if ( renderTarget is null )
			{
				Log.Warning( "Cannot take high-res screenshot: Failed to create render target." );
				return;
			}

			if ( !camera.RenderToTexture( renderTarget.ColorTarget ) )
			{
				Log.Warning( "Cannot take high-res screenshot: Camera failed to render to texture." );
				return;
			}

			captureBitmap = renderTarget.ColorTarget.GetBitmap( 0 );
			if ( captureBitmap is null || !captureBitmap.IsValid )
			{
				Log.Warning( "Cannot take high-res screenshot: Failed to read pixels from render target." );
				return;
			}

			var filePath = ScreenCaptureUtility.GenerateScreenshotFilename( extension );
			var directory = Path.GetDirectoryName( filePath );
			if ( !string.IsNullOrEmpty( directory ) )
			{
				Directory.CreateDirectory( directory );
			}

			byte[] encodedBytes = EncodeBitmap( captureBitmap, format, quality );
			File.WriteAllBytes( filePath, encodedBytes );

			var fileInfo = new FileInfo( filePath );
			var eventArgs = new ScreenshotCapturedEventArgs
			{
				FilePath = filePath,
				Width = width,
				Height = height,
				Format = format,
				FileSize = fileInfo.Length,
				IsHighRes = true
			};

			Log.Info( $"High-res screenshot saved: {filePath} ({width}x{height}, {FormatFileSize( fileInfo.Length )})" );

			callback?.Invoke( eventArgs );
			OnScreenshotCaptured?.Invoke( null, eventArgs );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to capture high-res screenshot: {ex.Message}" );
		}
		finally
		{
			if ( hideUI )
			{
				Graphics.SuppressUI = false;
			}

			camera.CustomSize = previousCustomSize;
			renderTarget?.Dispose();
			captureBitmap?.Dispose();

			if ( camera.IsValid() )
			{
				camera.InitializeRendering();
			}
		}
	}

	private static byte[] EncodeBitmap( Bitmap bitmap, ScreenshotFormat format, int quality )
	{
		return format switch
		{
			ScreenshotFormat.JPEG => bitmap.ToJpg( quality ),
			ScreenshotFormat.WebP => bitmap.ToWebP( quality ),
			_ => bitmap.ToPng()
		};
	}

	private static string GetFileExtension( ScreenshotFormat format )
	{
		return format switch
		{
			ScreenshotFormat.JPEG => "jpg",
			ScreenshotFormat.WebP => "webp",
			_ => "png"
		};
	}

	private static string FormatFileSize( long bytes )
	{
		const long KB = 1024;
		const long MB = KB * 1024;

		return bytes switch
		{
			>= MB => $"{bytes / (double)MB:F2} MB",
			>= KB => $"{bytes / (double)KB:F1} KB",
			_ => $"{bytes} bytes"
		};
	}

#if WIN
	[DllImport( "user32.dll" )]
	private static extern bool GetCursorInfo( ref CURSORINFO pci );

	[DllImport( "user32.dll" )]
	private static extern bool GetIconInfo( IntPtr hIcon, ref ICONINFO piconinfo );

	[DllImport( "gdi32.dll" )]
	private static extern bool GetObjectA( IntPtr hObject, int nCount, ref BITMAP lpObject );

	[DllImport( "user32.dll" )]
	private static extern IntPtr GetDC( IntPtr hWnd );

	[DllImport( "user32.dll" )]
	private static extern int ReleaseDC( IntPtr hWnd, IntPtr hDC );

	[DllImport( "gdi32.dll" )]
	private static extern bool GetDIBits( IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, IntPtr lpvBits, ref BITMAPINFOHEADER lpbmi, uint uUsage );

	[DllImport( "gdi32.dll" )]
	private static extern bool DeleteObject( IntPtr hObject );

	[StructLayout( LayoutKind.Sequential )]
	private struct CURSORINFO
	{
		public int cbSize;
		public int flags;
		public IntPtr hCursor;
		public POINT ptScreenPos;
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct ICONINFO
	{
		public bool fIcon;
		public int xHotspot;
		public int yHotspot;
		public IntPtr hbmMask;
		public IntPtr hbmColor;
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct BITMAP
	{
		public int bmType;
		public int bmWidth;
		public int bmHeight;
		public int bmWidthBytes;
		public short bmPlanes;
		public short bmBitsPixel;
		public IntPtr bmBits;
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct POINT
	{
		public int x;
		public int y;
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct BITMAPINFOHEADER
	{
		public uint biSize;
		public int biWidth;
		public int biHeight;
		public ushort biPlanes;
		public ushort biBitCount;
		public uint biCompression;
		public uint biSizeImage;
		public int biXPelsPerMeter;
		public int biYPelsPerMeter;
		public uint biClrUsed;
		public uint biClrImportant;
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct RGBQUAD
	{
		public byte rgbBlue;
		public byte rgbGreen;
		public byte rgbRed;
		public byte rgbReserved;
	}

	private const int CURSOR_SHOWING = 0x00000001;
	private const uint BI_RGB = 0;
	private const uint DIB_RGB_COLORS = 0;

	private struct CachedCursor
	{
		public IntPtr Handle;
		public byte[] BitmapData;
		public int Width;
		public int Height;
		public int HotspotX;
		public int HotspotY;
		public bool HasColor;

		public readonly bool IsValid => Handle != IntPtr.Zero && BitmapData != null;
	}

	private static CachedCursor _cachedCursor;

	private static unsafe void BlitCursor( byte* targetData, int targetWidth, int targetHeight, int targetStride )
	{
		if ( !EnsureCachedCursor() )
			return;

		var mousePos = Mouse.Position;
		int x = (int)mousePos.x - _cachedCursor.HotspotX;
		int y = (int)mousePos.y - _cachedCursor.HotspotY;

		DrawCachedCursor( targetData, targetWidth, targetHeight, targetStride, x, y );
	}

	private static unsafe bool EnsureCachedCursor()
	{
		CURSORINFO cursorInfo = new() { cbSize = Marshal.SizeOf<CURSORINFO>() };
		if ( !GetCursorInfo( ref cursorInfo ) || cursorInfo.hCursor == IntPtr.Zero || cursorInfo.flags != CURSOR_SHOWING )
			return false;

		if ( cursorInfo.hCursor == _cachedCursor.Handle && _cachedCursor.IsValid )
			return true;

		_cachedCursor.Handle = cursorInfo.hCursor;

		ICONINFO iconInfo = new();
		if ( !GetIconInfo( cursorInfo.hCursor, ref iconInfo ) )
			return false;

		IntPtr hbmColorOriginal = iconInfo.hbmColor;
		IntPtr hbmMaskOriginal = iconInfo.hbmMask;

		try
		{
			BITMAP bm = new();
			if ( !GetObjectA( iconInfo.hbmMask, Marshal.SizeOf<BITMAP>(), ref bm ) )
				return false;

			BITMAPINFOHEADER bmiHeader = new()
			{
				biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
				biWidth = bm.bmWidth,
				biHeight = bm.bmHeight,
				biPlanes = 1,
				biBitCount = 32,
				biCompression = BI_RGB
			};

			IntPtr hdc = GetDC( IntPtr.Zero );
			if ( hdc == IntPtr.Zero )
				return false;

			try
			{
				int cursorDisplayHeight = bm.bmHeight;
				if ( iconInfo.hbmColor == IntPtr.Zero && bm.bmHeight == bm.bmWidth * 2 )
					cursorDisplayHeight /= 2;

				_cachedCursor.HotspotX = iconInfo.xHotspot;
				_cachedCursor.HotspotY = iconInfo.yHotspot;
				_cachedCursor.Width = bm.bmWidth;
				_cachedCursor.Height = cursorDisplayHeight;
				_cachedCursor.HasColor = iconInfo.hbmColor != IntPtr.Zero;

				_cachedCursor.BitmapData = new byte[bm.bmWidth * cursorDisplayHeight * 4];

				int rgbQuadSize = Marshal.SizeOf<RGBQUAD>();
				int bufferSize = bm.bmWidth * bm.bmHeight * rgbQuadSize;

				if ( iconInfo.hbmColor != IntPtr.Zero )
				{
					using PooledSpan<byte> colorBuffer = new( bufferSize );
					var colorBufferSpan = colorBuffer.Span;
					IntPtr colorsPtr = Marshal.AllocHGlobal( bufferSize );

					try
					{
						if ( !GetDIBits( hdc, iconInfo.hbmColor, 0, (uint)bm.bmHeight, colorsPtr, ref bmiHeader, DIB_RGB_COLORS ) )
							return false;

						fixed ( byte* bufferPtr = colorBuffer.Span )
						{
							Buffer.MemoryCopy( colorsPtr.ToPointer(), bufferPtr, bufferSize, bufferSize );
						}

						for ( int cy = 0; cy < cursorDisplayHeight; cy++ )
						{
							for ( int cx = 0; cx < bm.bmWidth; cx++ )
							{
								int srcIndex = (cy * bm.bmWidth + cx) * rgbQuadSize;
								int dstIndex = (cy * bm.bmWidth + cx) * 4;

								_cachedCursor.BitmapData[dstIndex + 0] = colorBufferSpan[srcIndex + 2];
								_cachedCursor.BitmapData[dstIndex + 1] = colorBufferSpan[srcIndex + 1];
								_cachedCursor.BitmapData[dstIndex + 2] = colorBufferSpan[srcIndex + 0];
								_cachedCursor.BitmapData[dstIndex + 3] = colorBufferSpan[srcIndex + 3];
							}
						}
					}
					finally
					{
						Marshal.FreeHGlobal( colorsPtr );
					}
				}

				using PooledSpan<byte> maskBuffer = new( bufferSize );
				var maskBufferSpan = maskBuffer.Span;
				IntPtr maskPtr = Marshal.AllocHGlobal( bufferSize );

				try
				{
					if ( !GetDIBits( hdc, iconInfo.hbmMask, 0, (uint)bm.bmHeight, maskPtr, ref bmiHeader, DIB_RGB_COLORS ) )
						return false;

					fixed ( byte* bufferPtr = maskBuffer.Span )
					{
						Buffer.MemoryCopy( maskPtr.ToPointer(), bufferPtr, bufferSize, bufferSize );
					}

					if ( iconInfo.hbmColor == IntPtr.Zero )
					{
						for ( int cy = 0; cy < cursorDisplayHeight; cy++ )
						{
							for ( int cx = 0; cx < bm.bmWidth; cx++ )
							{
								int pixelIndex = cy * bm.bmWidth + cx;
								int srcIndex = pixelIndex * rgbQuadSize;
								int dstIndex = pixelIndex * 4;

								bool isTransparent = maskBufferSpan[srcIndex] == 0 && maskBufferSpan[srcIndex + 1] == 0 && maskBufferSpan[srcIndex + 2] == 0;
								int xorIndex = (cy + cursorDisplayHeight) * bm.bmWidth + cx;
								int xorSrcIndex = xorIndex * rgbQuadSize;
								bool xorBitIsWhite = maskBufferSpan[xorSrcIndex] != 0 || maskBufferSpan[xorSrcIndex + 1] != 0 || maskBufferSpan[xorSrcIndex + 2] != 0;

								if ( isTransparent )
								{
									_cachedCursor.BitmapData[dstIndex + 0] = 0;
									_cachedCursor.BitmapData[dstIndex + 1] = 0;
									_cachedCursor.BitmapData[dstIndex + 2] = 0;
									_cachedCursor.BitmapData[dstIndex + 3] = 0;
								}
								else
								{
									byte colorValue = xorBitIsWhite ? (byte)255 : (byte)0;
									_cachedCursor.BitmapData[dstIndex + 0] = colorValue;
									_cachedCursor.BitmapData[dstIndex + 1] = colorValue;
									_cachedCursor.BitmapData[dstIndex + 2] = colorValue;
									_cachedCursor.BitmapData[dstIndex + 3] = 255;
								}
							}
						}
					}
				}
				finally
				{
					Marshal.FreeHGlobal( maskPtr );
				}

				return true;
			}
			finally
			{
				ReleaseDC( IntPtr.Zero, hdc );
			}
		}
		finally
		{
			if ( hbmColorOriginal != IntPtr.Zero )
				DeleteObject( hbmColorOriginal );
			if ( hbmMaskOriginal != IntPtr.Zero )
				DeleteObject( hbmMaskOriginal );
		}
	}

	private static unsafe void DrawCachedCursor( byte* targetData, int targetWidth, int targetHeight, int targetStride, int x, int y )
	{
		if ( !_cachedCursor.IsValid )
			return;

		for ( int cursorY = 0; cursorY < _cachedCursor.Height; cursorY++ )
		{
			for ( int cursorX = 0; cursorX < _cachedCursor.Width; cursorX++ )
			{
				int targetX = x + cursorX;
				int targetY = y + cursorY;

				if ( targetX < 0 || targetY < 0 || targetX >= targetWidth || targetY >= targetHeight )
					continue;

				int cacheOffset = ((_cachedCursor.Height - 1 - cursorY) * _cachedCursor.Width + cursorX) * 4;
				int targetOffset = targetY * targetStride + targetX * 4;

				if ( _cachedCursor.BitmapData[cacheOffset + 3] == 0 )
					continue;

				if ( _cachedCursor.HasColor )
				{
					float alpha = _cachedCursor.BitmapData[cacheOffset + 3] / 255.0f;

					targetData[targetOffset + 0] = (byte)((_cachedCursor.BitmapData[cacheOffset + 0] * alpha) + (targetData[targetOffset + 0] * (1.0f - alpha)));
					targetData[targetOffset + 1] = (byte)((_cachedCursor.BitmapData[cacheOffset + 1] * alpha) + (targetData[targetOffset + 1] * (1.0f - alpha)));
					targetData[targetOffset + 2] = (byte)((_cachedCursor.BitmapData[cacheOffset + 2] * alpha) + (targetData[targetOffset + 2] * (1.0f - alpha)));
					targetData[targetOffset + 3] = 255;
				}
				else
				{
					if ( _cachedCursor.BitmapData[cacheOffset + 0] != 0 )
					{
						targetData[targetOffset + 0] ^= 255;
						targetData[targetOffset + 1] ^= 255;
						targetData[targetOffset + 2] ^= 255;
					}
					targetData[targetOffset + 3] = 255;
				}
			}
		}
	}
#endif
}
