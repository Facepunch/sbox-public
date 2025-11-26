using NativeEngine;
using System.Collections.Concurrent;
using System.IO;

namespace Sandbox;

/// <summary>
/// Provides functionality to capture and save screenshots in various formats.
/// </summary>
internal static class ScreenshotService
{
	[ConVar( "screenshot_prefix", Help = "Prefix for auto-generated screenshot filenames" )]
	public static string ScreenshotPrefix { get; set; } = "sbox";

	private record ScreenshotRequest( string FilePath );

	private static readonly ConcurrentQueue<ScreenshotRequest> _pendingRequests = new();

	/// <summary>
	/// Captures the screen and saves it as a PNG file.
	/// </summary>
	internal static string RequestCapture()
	{
		string filePath = ScreenCaptureUtility.GenerateScreenshotFilename( "png" );

		_pendingRequests.Enqueue( new ScreenshotRequest( filePath ) );

		return filePath;
	}

	internal static void ProcessFrame( IRenderContext context, ITexture nativeTexture )
	{
		if ( nativeTexture.IsNull || !nativeTexture.IsStrongHandleValid() )
			return;

		while ( _pendingRequests.TryDequeue( out var request ) )
		{
			CaptureRenderTexture( context, nativeTexture, request.FilePath );
		}
	}

	/// <summary>
	/// Captures the current render target and saves it to the specified file.
	/// </summary>
	private static void CaptureRenderTexture( IRenderContext context, ITexture nativeTexture, string filePath )
	{
		try
		{
			Bitmap bitmap = null;

			context.ReadTextureAsync( nativeTexture, ( pData, format, mipLevel, width, height, _ ) =>
			{
				try
				{
					bitmap = new Bitmap( width, height );

					pData.CopyTo( bitmap.GetBuffer() );

					var rgbData = bitmap.ToFormat( ImageFormat.RGB888 );
					Services.Screenshots.AddScreenshotToLibrary( rgbData, width, height );

					var dir = Path.GetDirectoryName( filePath );
					if ( dir != null )
					{
						Directory.CreateDirectory( dir );
					}
					var encodedBytes = bitmap.ToPng();
					File.WriteAllBytes( filePath, encodedBytes );
				}
				catch ( Exception ex )
				{
					Log.Error( $"Error creating bitmap from texture: {ex.Message}" );
				}
			} );

			Log.Info( $"Screenshot saved to: {filePath}" );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Error capturing screenshot: {ex.Message}" );
		}
	}
}
