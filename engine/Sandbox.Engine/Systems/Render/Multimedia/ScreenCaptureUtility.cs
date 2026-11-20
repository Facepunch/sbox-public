using NativeEngine;
using System.IO;

namespace Sandbox;

/// <summary>
/// Utility methods for screen recording and screenshot functionality
/// </summary>
internal static class ScreenCaptureUtility
{
	/// <summary>
	/// Captures the current scene layer for screenshots and video recording.
	/// Call this after rendering any overlays that should appear in the capture.
	/// </summary>
	internal static void CaptureFrame()
	{
		// A Borealis.Gpu window's frame: recorded from the bound target before the border is drawn; screenshots come from
		// the finished frame (BorealisGpuWindow.EndFrame)
		if ( Graphics.Backend is BorealisGpuRenderBackend gpu && ScreenRecorder.IsRecording() && BorealisGpuWindow.Drawing?.Target is { } frame )
			ScreenRecorder.RecordVideoFrame( gpu, frame );
	}

	internal static void DrawRecordingBorder()
	{
		if ( !ScreenRecorder.IsRecording() )
			return;

		var width = Graphics.Viewport.Width;
		var height = Graphics.Viewport.Height;

		const float rectSize = 4f;
		var color = new Color( 255, 0, 0, 10 );

		Graphics.DrawRoundedRectangle( new Rect( 0, 0, width, rectSize ), color );
		Graphics.DrawRoundedRectangle( new Rect( 0, height - rectSize, width, rectSize ), color );
		Graphics.DrawRoundedRectangle( new Rect( 0, rectSize, rectSize, height - (rectSize * 2) ), color );
		Graphics.DrawRoundedRectangle( new Rect( width - rectSize, rectSize, rectSize, height - (rectSize * 2) ), color );
	}

	/// <summary>
	/// Generates a suitable screenshot filename with timestamp
	/// </summary>
	public static string GenerateScreenshotFilename( string extension, string filePath = "screenshots" )
	{
		var fileName = SafePath.ToFileName( ScreenshotService.ScreenshotPrefix, "sbox_" );

		// Format extension with dot if needed
		string extensionSeparator = "";
		if ( !string.IsNullOrEmpty( extension ) && !extension.StartsWith( "." ) )
		{
			extensionSeparator = ".";
		}

		// Get timestamp
		string timestamp = DateTime.Now.ToString( "yyyy.MM.dd.HH.mm.ss" );

		// Generate final filename
		string screenshotFilename;
		if ( !string.IsNullOrEmpty( filePath ) )
		{
			screenshotFilename = Path.Combine( filePath, $"{fileName}.{timestamp}{extensionSeparator}{extension}" );

			if ( !SafePath.IsWithin( filePath, screenshotFilename ) )
				throw new ArgumentException( $"Screenshot path escapes '{filePath}'", nameof( extension ) );
		}
		else
		{
			screenshotFilename = $"{fileName}.{timestamp}{extensionSeparator}{extension}";
		}

		return screenshotFilename;
	}
}
