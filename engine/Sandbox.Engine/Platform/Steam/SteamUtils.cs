using Steamworks.Data;

namespace Steamworks;

/// <summary>
/// Interface which provides access to a range of miscellaneous utility functions
/// </summary>
internal class SteamUtils : SteamSharedClass<SteamUtils>
{
	internal static ISteamUtils Internal => Interface as ISteamUtils;

	internal static bool IsInstalled => Internal?.IsValid ?? false;

	internal override void InitializeInterface( bool server )
	{
		SetInterface( server, new ISteamUtils( server ) );
		InstallEvents( server );
	}

	internal static void InstallEvents( bool server )
	{
		Dispatch.Install<SteamShutdown_t>( x => SteamClosed(), server );
	}

	private static void SteamClosed()
	{
		SteamClient.Cleanup();
	}

	/// <summary>
	/// returns true if the image exists, and the buffer was successfully filled out
	/// results are returned in RGBA format
	/// the destination buffer size should be 4 * height * width * sizeof(char)
	/// </summary>
	internal static bool GetImageSize( int image, out uint width, out uint height )
	{
		width = 0;
		height = 0;
		return IsInstalled && Internal.GetImageSize( image, ref width, ref height );
	}

	/// <summary>
	/// returns the image in RGBA format
	/// </summary>
	internal static Data.Image? GetImage( int image )
	{
		if ( image == -1 ) return null;
		if ( image == 0 ) return null;

		var i = new Data.Image();

		if ( !GetImageSize( image, out i.Width, out i.Height ) )
			return null;

		var size = i.Width * i.Height * 4;

		var buf = Helpers.TakeBuffer( (int)size );

		if ( !Internal.GetImageRGBA( image, buf, (int)size ) )
			return null;

		i.Data = new byte[size];
		Array.Copy( buf, 0, i.Data, 0, size );
		return i;
	}

	/// <summary>
	/// returns true if Steam itself is running on the Steam Deck
	/// </summary>
	internal static bool IsRunningOnSteamDeck => IsInstalled && Internal.IsSteamRunningOnSteamDeck();
}
