using Steamworks;

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

	internal static void OnSessionEstablished( ulong steamId )
	{
		var ourSteamId = SteamClient.SteamId;
		var e = new Api.Events.EventRecord( "SteamNetwork.SessionEstablished" );
		e.SetValue( "LocalSteamId", ourSteamId );
		e.SetValue( "RemoteSteamId", steamId );
		e.Submit();
	}

	internal static void OnSessionFailed( HSteamListenSocket socketHandle, ulong steamId, int reasonCode, string reason )
	{
		var system = Networking.System;
		if ( system is null ) return;

		var ourSteamId = SteamClient.SteamId;
		var e = new Api.Events.EventRecord( "SteamNetwork.SessionFailed" );
		e.SetValue( "ReasonCode", reasonCode );
		e.SetValue( "LocalSteamId", ourSteamId );
		e.SetValue( "RemoteSteamId", steamId );
		e.Submit();

		foreach ( var socket in system.Sockets )
		{
			socket.OnSessionFailed( steamId, reasonCode, reason );
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
