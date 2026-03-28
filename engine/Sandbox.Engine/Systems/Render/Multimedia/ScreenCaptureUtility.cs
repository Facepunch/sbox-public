using System.IO;

namespace Sandbox;

/// <summary>
/// Utility methods for screen recording and screenshot functionality
/// </summary>
internal static class ScreenCaptureUtility
{
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
