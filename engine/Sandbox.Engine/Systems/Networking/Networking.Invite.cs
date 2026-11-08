using Sandbox.Network;

namespace Sandbox;

public static partial class Networking
{
	/// <summary>
	/// What to send a friend so they can join the game we're in. Unlike our rich presence, this is
	/// there for private games too - just call <see cref="AllowJoin"/> before sending it.
	/// </summary>
	internal static string GetInviteConnectString()
	{
		var system = System;
		if ( system is null || system.IsDisconnected )
			return null;

		if ( system.Sockets.OfType<SteamLobbySocket>().FirstOrDefault() is { } lobby )
			return $"+connect {lobby.LobbySteamId}";

		if ( system.Connection is SteamNetwork.IpConnection or SteamNetwork.IdConnection )
			return $"+connect {system.Connection.Address}";

		return null;
	}

	/// <summary>
	/// Let this player into the game we're in, even if it's private or friends only.
	/// </summary>
	internal static void AllowJoin( SteamId steamId )
	{
		System?.AllowJoin( steamId );
	}
}
