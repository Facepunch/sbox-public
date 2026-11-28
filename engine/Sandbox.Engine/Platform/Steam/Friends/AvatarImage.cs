namespace Sandbox.SteamApi;

internal enum AvatarSize
{
	Small,
	Medium,
	Large
}

internal readonly record struct AvatarImage( int Width, int Height, byte[] Pixels )
{
	/// <summary>
	/// Reads the RGBA pixels for a Steam image handle.
	/// </summary>
	internal static unsafe AvatarImage? FromHandle( int handle )
	{
		if ( handle <= 0 || !SteamNative.Utils.GetImageSize( handle, out var width, out var height ) )
			return null;

		var pixels = new byte[checked((int)width * (int)height * 4)];
		fixed ( byte* data = pixels )
		{
			if ( !SteamNative.Utils.GetImageRGBA( handle, (nint)data, pixels.Length ) )
				return null;
		}

		return new( (int)width, (int)height, pixels );
	}
}
