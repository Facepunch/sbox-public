using NativeEngine;
using System.Collections.Concurrent;
using System.IO;

namespace Sandbox;

/// <summary>
/// Provides functionality to capture and save screenshots in various formats.
/// </summary>
public static class ScreenshotService
{
	[ConVar( "screenshot_prefix", Help = "Prefix for auto-generated screenshot filenames" )]
	public static string ScreenshotPrefix { get; set; } = "sbox";





	[ConVar( "screenshot_steam", Help = "Add screenshots to Steam library" )]
	public static bool AddToSteamLibrary { get; set; } = true;



	public static event EventHandler<ScreenshotCapturedEventArgs> OnScreenshotCaptured;

	private sealed record ScreenshotRequest(
		string FilePath,

		bool AddToSteam,
		bool IsHighRes,
		Action<ScreenshotCapturedEventArgs> Callback
	);
	private static readonly ConcurrentQueue<ScreenshotRequest> _pendingRequests = new();

	/// <summary>
	/// Captures the screen and saves it as a PNG file.
	/// </summary>
	public static string RequestCapture()
	{
		return RequestCapture( AddToSteamLibrary, null );
	}

	public static string RequestCapture(
		bool addToSteam,
		Action<ScreenshotCapturedEventArgs> callback )
	{
		string filePath = ScreenCaptureUtility.GenerateScreenshotFilename( "png" );

		_pendingRequests.Enqueue( new ScreenshotRequest( filePath, addToSteam, false, callback ) );

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
			Bitmap bitmap = null;

			context.ReadTextureAsync( nativeTexture, ( pData, format, mipLevel, width, height, _ ) =>
			{
				try
				{
					bitmap = new Bitmap( width, height );
					pData.CopyTo( bitmap.GetBuffer() );

					if ( request.AddToSteam )
					{
						var rgbData = bitmap.ToFormat( ImageFormat.RGB888 );
						Services.Screenshots.AddScreenshotToLibrary( rgbData, width, height );
					}

					SaveBitmap( bitmap, request.FilePath );

					var fileSize = new FileInfo( request.FilePath ).Length;
					var args = new ScreenshotCapturedEventArgs( request.FilePath, width, height, fileSize, request.IsHighRes );

					OnScreenshotCaptured?.Invoke( null, args );
					request.Callback?.Invoke( args );

					Log.Info( $"Screenshot saved to: {request.FilePath}" );
				}
				catch ( Exception ex )
				{
					Log.Error( $"Error processing screenshot: {ex.Message}" );
				}
				finally
				{
					bitmap?.Dispose();
				}
			} );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Error capturing screenshot: {ex.Message}" );
		}
	}

	private static void SaveBitmap( Bitmap bitmap, string filePath )
	{
		var dir = Path.GetDirectoryName( filePath );
		if ( !string.IsNullOrEmpty( dir ) )
			Directory.CreateDirectory( dir );

		byte[] data = bitmap.ToPng();

		File.WriteAllBytes( filePath, data );
	}

	public static void TakeHighResScreenshot( Scene scene, int width, int height )
	{
		TakeHighResScreenshot( scene, width, height, null );
	}

	public static void TakeHighResScreenshot(
		Scene scene,
		int width,
		int height,
		Action<ScreenshotCapturedEventArgs> callback )
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

			SaveBitmap( captureBitmap, filePath );

			Log.Info( $"High-res screenshot saved to: {filePath} ({width}x{height})" );

			var fileSize = new FileInfo( filePath ).Length;
			var args = new ScreenshotCapturedEventArgs( filePath, width, height, fileSize, true );
			OnScreenshotCaptured?.Invoke( null, args );
			callback?.Invoke( args );
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

public class ScreenshotCapturedEventArgs : EventArgs
{
	public string FilePath { get; }
	public int Width { get; }
	public int Height { get; }
	public long FileSize { get; }
	public bool IsHighRes { get; }

	public ScreenshotCapturedEventArgs( string filePath, int width, int height, long fileSize, bool isHighRes )
	{
		FilePath = filePath;
		Width = width;
		Height = height;
		FileSize = fileSize;
		IsHighRes = isHighRes;
	}
}
