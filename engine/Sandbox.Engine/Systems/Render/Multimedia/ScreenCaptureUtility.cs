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
		var colorTarget = Graphics.SceneLayer.GetColorTarget();

		if ( colorTarget.IsNull )
			return;

		try
		{
			if ( !colorTarget.IsStrongHandleValid() )
				return;

			if ( ScreenRecorder.IsRecording() )
			{
				var width = (int)Graphics.Viewport.Width;
				var height = (int)Graphics.Viewport.Height;

				// Preserve the frame before the recording border is drawn.
				using var tempRT = RenderTarget.GetTemporary( width, height, Graphics.IdealColorFormat, ImageFormat.None );
				RenderTools.CopyTexture( Graphics.Context, colorTarget, tempRT.ColorTarget.native, default, 0, 0, 0, 0, 0, 0 );

				ScreenshotService.ProcessFrame( Graphics.Context, tempRT.ColorTarget.native );
				ScreenRecorder.RecordVideoFrame( Graphics.Context, tempRT.ColorTarget.native );
			}
			else
			{
				ScreenshotService.ProcessFrame( Graphics.Context, colorTarget );
			}
		}
		finally
		{
			colorTarget.DestroyStrongHandle();
		}
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
		var fileName = ConsoleSystem.GetValue( "screenshot_prefix", "sbox_" );
		if ( string.IsNullOrEmpty( fileName ) )
		{
			fileName = "sbox_";
		}

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
		}
		else
		{
			screenshotFilename = $"{fileName}.{timestamp}{extensionSeparator}{extension}";
		}

		return screenshotFilename;
	}
}
