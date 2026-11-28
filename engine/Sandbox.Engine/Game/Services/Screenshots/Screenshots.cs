namespace Sandbox.Services;

/// <summary>
/// Implements Steamscreenshots
/// </summary>
public static class Screenshots
{
	/// <summary>
	/// Writes a screenshot to the user's Steam screenshot library
	/// </summary>
	internal static bool AddScreenshotToLibrary( ReadOnlySpan<byte> rgbData, int width, int height )
	{
		unsafe
		{
			fixed ( byte* p = rgbData )
			{
				return SteamNative.Screenshots.WriteScreenshot( (IntPtr)p, (uint)rgbData.Length, width, height );
			}
		}
	}
}
