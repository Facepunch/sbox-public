using System.Text;
using NativeEngine;
using Sandbox.Engine;
using Sandbox.Services;

namespace Sandbox.Network;


internal static class NetworkConsoleCommands
{
	[ConCmd( "host", ConVarFlags.Protected )]
	public static void StartServer()
	{
		if ( Networking.IsActive )
		{
			Log.Warning( "You are already connected to a server." );
			return;
		}

		Networking.CreateLobby( new() );
	}

	[ConCmd( "joinlobby", ConVarFlags.Protected )]
	public static async Task FindAndJoinLobby()
	{
		if ( Networking.IsActive )
		{
			Log.Warning( "You are already connected to a server." );
			return;
		}

		var q = Steamworks.SteamMatchmaking.LobbyList
			.FilterDistanceWorldwide()
			.WithKeyValue( "lobby_type", "scene" )
			.WithMaxResults( 2000 );

		Log.Info( "Finding best lobby..." );
		var lobbies = await q.RequestAsync( default );

		if ( Networking.IsActive )
			return;

		if ( !lobbies.Any() )
		{
			Log.Info( "No lobbies found" );
			return;
		}

		foreach ( var l in lobbies )
		{
			Log.Info( $"{l.Id} / {l.Owner}" );
		}

		var chosen = lobbies.First();
		Networking.Connect( chosen.Id );
	}

	[ConCmd( "connect", ConVarFlags.Protected )]
	public static void ConnectToServer( string target )
	{
		if ( Networking.IsActive )
		{
			Log.Warning( "You are already connected to a server." );
			return;
		}

		Networking.Connect( target );
	}

	[ConCmd( "servers", ConVarFlags.Protected )]
	public static void Servers()
	{
		QueryServers();
	}

	private static async void QueryServers()
	{
		try
		{
			Log.Info( "Querying servers..." );

			using var ServerList = new ServerList();
			ServerList.Query();

			while ( ServerList.IsQuerying )
			{
				await Task.Yield();
			}

			foreach ( var e in ServerList )
			{
				Log.Info( e.IPAddressAndPort + " SteamId=" + e.SteamId + " Game=" + e.Game + " Map=" + e.Map + " Players=" + e.Players + " MaxPlayers=" + e.MaxPlayers + " Ping=" + e.Ping );
			}
		}
		catch ( Exception e )
		{
			Log.Error( e );
		}
	}

	[ConCmd( "status", ConVarFlags.Protected )]
	public static unsafe void Status()
	{
		if ( Networking.System is null )
		{
			Log.Warning( "Not connected" );
			return;
		}

		var status = Networking.GetSteamRelayStatus( out var debugMsg );
		Log.Info( $"Steam Relay Access [Availability: {status}] {new string( debugMsg )}" );
		Log.Info( $"Network Id: {Connection.Local.Id}" );
		Log.Info( $"IsClient: {Networking.System.IsClient}" );
		Log.Info( $"IsHost: {Networking.System.IsHost}" );

		int s = 0;
		foreach ( var socket in Networking.System.Sockets )
		{
			Log.Info( $" Socket {++s}: {socket}" );
		}

		if ( Networking.System.Connection is Connection connect )
		{
			Log.Info( $"Primary Connection:" );
			Log.Info( $"	 Name: {connect.Name}" );
			Log.Info( $"	 Id: {connect.Id}" );
			Log.Info( $"	 State: {connect.State}" );
			Log.Info( $"	 Address: {connect.Address}" );
			Log.Info( $"	 Time: {connect.Time}" );
			Log.Info( $"	 Latency: {connect.Latency}" );
			Log.Info( $"	 Messages: {connect.MessagesSent} sent, {connect.MessagesRecieved} recv" );
		}

		int i = 0;
		foreach ( var channel in Networking.System.Connections )
		{
			Log.Info( $" {++i}: {channel.State} {channel.Id} {channel.Name} {channel.Address} [{channel.MessagesSent}/{channel.MessagesRecieved}]" );
		}

		Log.Info( $"PLAYERS ----------" );

		foreach ( var info in Networking.System.ConnectionInfo.All.Values )
		{
			Log.Info( $"{info.ConnectionId}	{info.SteamId}	{info.State}		{info.DisplayName}		{info.ConnectionTime}" );
		}
	}

	[ConCmd( "disconnect", ConVarFlags.Protected )]
	public static void Disconnect()
	{
		IGameInstanceDll.Current.Disconnect();
	}

	[ConCmd( "reconnect", ConVarFlags.Protected )]
	public static void Reconnect()
	{
		if ( string.IsNullOrWhiteSpace( Networking.LastConnectionString ) )
		{
			Log.Warning( "You were never or are not currently connected to a server." );
			return;
		}

		Networking.Connect( Networking.LastConnectionString );
	}
}
