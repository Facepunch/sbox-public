using Sandbox.SteamApi;

namespace Sandbox.Network;

internal static partial class SteamNetwork
{
	private static readonly Dictionary<HSteamListenSocket, Socket> sockets = new();

	internal static void OnSocketConnection( HSteamListenSocket socketHandle, HSteamNetConnection connection )
	{
		if ( !sockets.TryGetValue( socketHandle, out var socket ) )
		{
			Log.Warning( $"Connection from unknown socket - {socketHandle} / {connection}" );
			return;
		}

		socket.OnConnected( connection );
	}

	internal static bool ShouldAcceptConnection( HSteamListenSocket socketHandle, HSteamNetConnection connection )
	{
		// Don't accept any connections to any socket if we have no networking system active.
		return Networking.System is not null;
	}

	internal static void OnSessionAccepted( ulong steamId )
	{
		var e = new Api.Events.EventRecord( "SteamNetwork.SessionEstablished" );
		e.SetValue( "LocalSteamId", new { IsValid = SteamClient.SteamId.ValueUnsigned != 0 } );
		e.SetValue( "RemoteSteamId", steamId );
		e.Submit();
	}

	internal static void OnSessionFailed( ulong steamId, SteamSessionStatus failure )
	{
		var system = Networking.System;
		if ( system is null ) return;

		var e = new Api.Events.EventRecord( "SteamNetwork.SessionFailed" );
		e.SetValue( "ReasonCode", failure.Reason );
		e.SetValue( "LocalSteamId", new { IsValid = SteamClient.SteamId.ValueUnsigned != 0 } );
		e.SetValue( "RemoteSteamId", steamId );
		e.Submit();

		foreach ( var socket in system.Sockets )
		{
			if ( socket is SteamLobbySocket lobby )
				lobby.OnSessionFailed( steamId, failure );
		}
	}

	internal static void OnSocketDisconnection( HSteamListenSocket socketHandle, HSteamNetConnection connection )
	{
		if ( !sockets.TryGetValue( socketHandle, out var socket ) )
		{
			Log.Warning( $"Disconnection from unknown socket - {socketHandle} / {connection}" );
			return;
		}

		socket.OnDisconnected( connection );
	}

	internal static void OnDisconnection( HSteamNetConnection connection, int reasonCode, string reasonString )
	{
		// Conna: this'll be called if we get disconnected from a server.
		Networking.System?.OnServerDisconnection( reasonCode, reasonString );
	}
}
