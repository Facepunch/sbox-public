using NativeEngine;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// Records the screen to a video file.
/// </summary>
internal static class ScreenRecorder
{
	private static bool _isRecording;
	private static bool _firstFrame;
	private static string _filename;
	private static VideoWriter _videoWriter;
	private static Stopwatch _recordingTimer;
	private static object _timerLockObj = new();

	/// <summary>
	/// Gets whether a recording is currently in progress.
	/// </summary>
	public static bool IsRecording() => _isRecording;

	[ConVar( "video_bitrate", Min = 1, Max = 100, Help = "Bit rate for video recorder (in Mbps)" )]
	public static int VideoBitRate { get; set; } = 14;
	[ConVar( "video_framerate", Min = 1, Max = 1000, Help = "Frame rate for screen recording" )]
	public static int VideoFrameRate { get; set; } = 60;
	[ConVar( "video_Scale", Min = 1, Max = 800, Help = "Scale percentage for video recorder" )]
	public static float VideoScale { get; set; } = 100f;

	/// <summary>
	/// Starts recording to the specified file.
	/// </summary>
	/// <returns>True if recording started successfully</returns>
	[ConCmd( "video" )]
	public static bool StartRecording()
	{
		if ( _isRecording )
		{
			StopRecording();
			return false;
		}

		_isRecording = true;
		_filename = ScreenCaptureUtility.GenerateScreenshotFilename( "mp4" );
		_firstFrame = true;

		Log.Info( $"Video recording started: {_filename}" );
		return true;
	}

	/// <summary>
	/// Stops the current recording.
	/// </summary>
	public static void StopRecording()
	{
		if ( !_isRecording ) return;

		_isRecording = false;

		if ( _videoWriter != null )
		{
			_videoWriter.Dispose();
			_videoWriter = null;
		}

		Log.Info( $"Video recording finished: <a href=\"{_filename}\">{_filename}</a>" );
	}

	/// <summary>
	/// Captures a video frame from the provided render context and view.
	/// </summary>
	public static void RecordVideoFrame( IRenderContext renderContext, ITexture nativeTexture )
	{
		if ( !_isRecording ) return;
		if ( nativeTexture.IsNull || !nativeTexture.IsStrongHandleValid() ) return;

		if ( _videoWriter == null )
		{
			var desc = g_pRenderDevice.GetOnDiskTextureDesc( nativeTexture );

			_videoWriter = new VideoWriter( _filename, new VideoWriter.Config
			{
				Width = desc.m_nWidth,
				Height = desc.m_nHeight,
				FrameRate = VideoFrameRate,
				Bitrate = VideoBitRate,
				Codec = VideoWriter.Codec.H264,
				Container = VideoWriter.Container.MP4
			} );
		}

		renderContext.ReadTextureAsync( nativeTexture, ( pData, format, mipLevel, width, height, _ ) =>
		{
			if ( !_isRecording || _videoWriter == null ) return;

			// Capture timestamp early to get an accurate timestamp
			// need to lock as multiple threads may call this in parallel
			TimeSpan timestamp;
			lock ( _timerLockObj )
			{
				if ( _firstFrame )
				{
					// First frame should be 0 for accurate timing
					timestamp = TimeSpan.Zero;
					_recordingTimer = Stopwatch.StartNew();
					_firstFrame = false;
				}
				else
				{
					timestamp = TimeSpan.FromTicks( _recordingTimer.ElapsedTicks );
				}
			}

#if WIN
			// Blit cursor directly onto the image data
			if ( Mouse.Active )
			{
				unsafe
				{
					fixed ( byte* dataPtr = pData )
					{
						BlitCursor( dataPtr, width, height, width * 4 );
					}
				}
			}
#endif

			// Due to thread races:
			// Writer may be null very briefly during shutdown, instead of adding complex locks just handle the exception.
			try
			{
				// Create a TimeSpan from microseconds
				_videoWriter.AddFrame( pData, timestamp );
			}
			catch ( NullReferenceException )
			{

			}
		} );
	}

	/// <summary>
	/// Captures an audio frame from the provided audio buffers.
	/// </summary>
	public static void RecordAudioSample( CAudioMixDeviceBuffers buffers )
	{
		// Drop until video is ready
		if ( !_isRecording || _videoWriter == null ) return;

		// Due to thread races:
		// Writer may be null very briefly during shutdown, instead of adding complex locks just handle the exception.
		try
		{
			_videoWriter.AddAudioSamples( buffers );
		}
		catch ( NullReferenceException )
		{

		}
	}

#if WIN
	// Windows-specific cursor blitting implementation
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

		public bool IsValid => Handle != IntPtr.Zero && BitmapData != null;
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

		// If cursor hasn't changed, we're done
		if ( cursorInfo.hCursor == _cachedCursor.Handle && _cachedCursor.IsValid )
			return true;

		// Cache the new cursor
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

				// Save cursor properties
				_cachedCursor.HotspotX = iconInfo.xHotspot;
				_cachedCursor.HotspotY = iconInfo.yHotspot;
				_cachedCursor.Width = bm.bmWidth;
				_cachedCursor.Height = cursorDisplayHeight;
				_cachedCursor.HasColor = iconInfo.hbmColor != IntPtr.Zero;

				// Create cursor cache bitmap (RGBA format)
				_cachedCursor.BitmapData = new byte[bm.bmWidth * cursorDisplayHeight * 4];

				// Create a byte buffer for GetDIBits
				int rgbQuadSize = Marshal.SizeOf<RGBQUAD>();
				int bufferSize = bm.bmWidth * bm.bmHeight * rgbQuadSize;

				// Process color data if available
				if ( iconInfo.hbmColor != IntPtr.Zero )
				{
					using PooledSpan<byte> colorBuffer = new( bufferSize );
					var colorBufferSpan = colorBuffer.Span;
					IntPtr colorsPtr = Marshal.AllocHGlobal( bufferSize );

					try
					{
						if ( !GetDIBits( hdc, iconInfo.hbmColor, 0, (uint)bm.bmHeight, colorsPtr, ref bmiHeader, DIB_RGB_COLORS ) )
							return false;

						// Copy raw bytes
						fixed ( byte* bufferPtr = colorBuffer.Span )
						{
							Buffer.MemoryCopy( colorsPtr.ToPointer(), bufferPtr, bufferSize, bufferSize );
						}

						// Process bytes into RGBA values in our cache
						for ( int y = 0; y < cursorDisplayHeight; y++ )
						{
							for ( int x = 0; x < bm.bmWidth; x++ )
							{
								int srcIndex = (y * bm.bmWidth + x) * rgbQuadSize;
								int dstIndex = (y * bm.bmWidth + x) * 4;

								_cachedCursor.BitmapData[dstIndex + 0] = colorBufferSpan[srcIndex + 2]; // R <- B
								_cachedCursor.BitmapData[dstIndex + 1] = colorBufferSpan[srcIndex + 1]; // G
								_cachedCursor.BitmapData[dstIndex + 2] = colorBufferSpan[srcIndex + 0]; // B <- R
								_cachedCursor.BitmapData[dstIndex + 3] = colorBufferSpan[srcIndex + 3]; // A
							}
						}
					}
					finally
					{
						Marshal.FreeHGlobal( colorsPtr );
					}
				}

				// Process mask data
				using PooledSpan<byte> maskBuffer = new( bufferSize );
				var maskBufferSpan = maskBuffer.Span;
				IntPtr maskPtr = Marshal.AllocHGlobal( bufferSize );

				try
				{
					if ( !GetDIBits( hdc, iconInfo.hbmMask, 0, (uint)bm.bmHeight, maskPtr, ref bmiHeader, DIB_RGB_COLORS ) )
						return false;

					// Copy raw bytes
					fixed ( byte* bufferPtr = maskBuffer.Span )
					{
						Buffer.MemoryCopy( maskPtr.ToPointer(), bufferPtr, bufferSize, bufferSize );
					}

					// If we don't have color data, process mask data for monochrome cursor
					if ( iconInfo.hbmColor == IntPtr.Zero )
					{
						for ( int y = 0; y < cursorDisplayHeight; y++ )
						{
							for ( int x = 0; x < bm.bmWidth; x++ )
							{
								int pixelIndex = y * bm.bmWidth + x;
								int srcIndex = pixelIndex * rgbQuadSize;
								int dstIndex = pixelIndex * 4;

								// Get AND mask (transparency mask)
								bool isTransparent = maskBufferSpan[srcIndex] == 0 && maskBufferSpan[srcIndex + 1] == 0 && maskBufferSpan[srcIndex + 2] == 0;

								// Get XOR mask (color mask)
								int xorIndex = (y + cursorDisplayHeight) * bm.bmWidth + x;
								int xorSrcIndex = xorIndex * rgbQuadSize;
								bool xorBitIsWhite = maskBufferSpan[xorSrcIndex] != 0 || maskBufferSpan[xorSrcIndex + 1] != 0 || maskBufferSpan[xorSrcIndex + 2] != 0;

								if ( isTransparent )
								{
									// Transparent pixel
									_cachedCursor.BitmapData[dstIndex + 0] = 0;
									_cachedCursor.BitmapData[dstIndex + 1] = 0;
									_cachedCursor.BitmapData[dstIndex + 2] = 0;
									_cachedCursor.BitmapData[dstIndex + 3] = 0; // Transparent
								}
								else
								{
									// XOR cursor pixel
									byte colorValue = xorBitIsWhite ? (byte)255 : (byte)0;
									_cachedCursor.BitmapData[dstIndex + 0] = colorValue;
									_cachedCursor.BitmapData[dstIndex + 1] = colorValue;
									_cachedCursor.BitmapData[dstIndex + 2] = colorValue;
									_cachedCursor.BitmapData[dstIndex + 3] = 255; // Opaque
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

		// Draw the cursor bitmap at the given position
		for ( int cursorY = 0; cursorY < _cachedCursor.Height; cursorY++ )
		{
			for ( int cursorX = 0; cursorX < _cachedCursor.Width; cursorX++ )
			{
				int targetX = x + cursorX;
				int targetY = y + cursorY;

				if ( targetX < 0 || targetY < 0 || targetX >= targetWidth || targetY >= targetHeight )
					continue;

				// Invert Y-coordinate in the cache to fix upside-down cursor
				int cacheOffset = ((_cachedCursor.Height - 1 - cursorY) * _cachedCursor.Width + cursorX) * 4;
				int targetOffset = targetY * targetStride + targetX * 4;

				// Skip fully transparent pixels
				if ( _cachedCursor.BitmapData[cacheOffset + 3] == 0 )
					continue;

				if ( _cachedCursor.HasColor )
				{
					// Color cursor - blend using alpha
					float alpha = _cachedCursor.BitmapData[cacheOffset + 3] / 255.0f;

					targetData[targetOffset + 0] = (byte)((_cachedCursor.BitmapData[cacheOffset + 0] * alpha) + (targetData[targetOffset + 0] * (1.0f - alpha)));
					targetData[targetOffset + 1] = (byte)((_cachedCursor.BitmapData[cacheOffset + 1] * alpha) + (targetData[targetOffset + 1] * (1.0f - alpha)));
					targetData[targetOffset + 2] = (byte)((_cachedCursor.BitmapData[cacheOffset + 2] * alpha) + (targetData[targetOffset + 2] * (1.0f - alpha)));
					targetData[targetOffset + 3] = 255;
				}
				else
				{
					// Monochrome cursor - XOR
					if ( _cachedCursor.BitmapData[cacheOffset + 0] != 0 ) // White XOR bit
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
