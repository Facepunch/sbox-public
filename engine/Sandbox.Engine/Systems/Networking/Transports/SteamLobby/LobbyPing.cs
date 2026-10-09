using System.Globalization;

namespace Sandbox.Network;

/// <summary>
/// Publishes Steam relay locations with their owner so discovery cannot use a previous host's location.
/// </summary>
internal static class LobbyPing
{
	/// <summary>
	/// Lobby metadata containing the host Steam ID followed by its opaque ping location.
	/// </summary>
	internal const string MetadataKey = "_ping_location";

	/// <summary>
	/// Wraps a Steam ping location with the host it describes, or returns null when unavailable.
	/// </summary>
	internal static string CreateMetadata( ulong ownerId, string location )
	{
		if ( ownerId == 0 || string.IsNullOrEmpty( location ) )
			return null;

		return $"{ownerId.ToString( CultureInfo.InvariantCulture )}|{location}";
	}

	/// <summary>
	/// Rejects missing, oversized or stale host metadata before passing the opaque marker to Steam.
	/// </summary>
	internal static bool TryGetLocation( ulong ownerId, string metadata, out string location )
	{
		location = null;

		// Steam's marker is at most 1023 characters; a ulong owner and separator need another 21.
		if ( ownerId == 0 || string.IsNullOrEmpty( metadata ) || metadata.Length > 1044 )
			return false;

		var separator = metadata.IndexOf( '|' );
		if ( separator <= 0 || separator == metadata.Length - 1 )
			return false;

		if ( !ulong.TryParse( metadata.AsSpan( 0, separator ), NumberStyles.None, CultureInfo.InvariantCulture, out var markerOwner ) || markerOwner != ownerId )
			return false;

		location = metadata[(separator + 1)..];
		return !location.Contains( '\0' ) && location.Length < 1024;
	}

	/// <summary>
	/// Estimates round-trip latency to the current host, or returns -1 until both peers have ping data.
	/// </summary>
	internal static int Estimate( ulong ownerId, string metadata )
	{
		if ( !TryGetLocation( ownerId, metadata, out var location ) )
			return -1;

		var utils = NativeEngine.Steam.SteamNetworkingUtils();
		return utils.IsValid ? utils.EstimatePingFromLocationString( location ) : -1;
	}
}
