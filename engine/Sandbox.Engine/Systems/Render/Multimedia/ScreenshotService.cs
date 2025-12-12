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

	public static void TakeHighResScreenshot( Scene scene, int width, int height )
	{
		if ( !scene.IsValid() )
		{
			Log.Warning( "No valid scene available for high-res screenshot." );
			return;
		}

		const int MaxDimension = 16384;

		if ( width <= 0 || height <= 0 )
		{
			Log.Warning( "screenshot_highres requires width and height greater than zero." );
			return;
		}

		if ( width > MaxDimension || height > MaxDimension )
		{
			Log.Warning( $"screenshot_highres maximum dimension is {MaxDimension}px." );
			return;
		}

		if ( scene.Camera is not { } camera || !camera.IsValid() )
		{
			Log.Warning( "Active scene does not have a main camera to capture from." );
			return;
		}

		Bitmap captureBitmap = null;
		RenderTarget renderTarget = null;
		var previousCustomSize = camera.CustomSize;

		try
		{
			camera.CustomSize = new Vector2( width, height );

			renderTarget = RenderTarget.GetTemporary( width, height, ImageFormat.Default, ImageFormat.Default, MultisampleAmount.Multisample16x, 1, "HighResScreenshot" );
			if ( renderTarget is null )
			{
				Log.Warning( "Failed to create render target for high-res screenshot." );
				return;
			}

			if ( !camera.RenderToTexture( renderTarget.ColorTarget ) )
			{
				Log.Warning( "Camera failed to render to texture for high-res screenshot." );
				return;
			}

			captureBitmap = renderTarget.ColorTarget.GetBitmap( 0 );
			if ( captureBitmap is null || !captureBitmap.IsValid )
			{
				Log.Warning( "Failed to read pixels from render target for high-res screenshot." );
				return;
			}

			var filePath = ScreenCaptureUtility.GenerateScreenshotFilename( "png" );
			var directory = Path.GetDirectoryName( filePath );
			if ( !string.IsNullOrEmpty( directory ) )
			{
				Directory.CreateDirectory( directory );
			}

			var pngData = captureBitmap.ToPng();

			File.WriteAllBytes( filePath, pngData );

			Log.Info( $"High-res screenshot saved to: {filePath} ({width}x{height})" );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to capture high-res screenshot: {ex.Message}" );
		}
		finally
		{
			camera.CustomSize = previousCustomSize;

			renderTarget?.Dispose();

			captureBitmap?.Dispose();

			if ( camera.IsValid() )
			{
				camera.InitializeRendering();
			}
		}
	}
}
