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

		var output = new StringBuilder();

		void Section( string title )
		{
			if ( output.Length > 0 ) output.AppendLine();
			output.AppendLine( $"{title}:" );
		}

		void Field( string label, object value )
		{
			output.AppendLine( $"\t{label + ":",-22}\t{value}" );
		}

		var gameLobby = Networking.System.Sockets.OfType<SteamLobbySocket>().FirstOrDefault();
		var lobbyPrivacy = gameLobby is not null
			? gameLobby.SteamLobby.GetData( "access_level" )
			: null;
		var privacyLabel = lobbyPrivacy switch
		{
			nameof( LobbyPrivacy.FriendsOnly ) => "friends only",
			nameof( LobbyPrivacy.Private ) => "private",
			nameof( LobbyPrivacy.Public ) => "public",
			_ => "unknown privacy"
		};
		var playerCount = Networking.System.ConnectionInfo.All.Values.Count( x => x.State == Connection.ChannelState.Connected );
		output.AppendLine( $"{Networking.ServerName} ({privacyLabel})" );
		output.AppendLine( $"'{Application.GameIdent}' on '{Networking.MapName}'" );
		output.AppendLine( $"{playerCount}/{Networking.MaxPlayers} players" );

		var status = Networking.GetSteamRelayStatus( out var debugMsg );
		Section( "Steam Relay" );
		Field( "Availability", status );
		Field( "Details", debugMsg );

		Section( "Socket Information" );
		Field( "Network Id", Connection.Local.Id );
		Field( "Role", Networking.System.IsHost ? "Host" : "Client" );

		int s = 0;
		foreach ( var socket in Networking.System.Sockets )
		{
			output.AppendLine( $"\tSocket {++s}" );
			Field( "Transport", socket );

			if ( socket is SteamLobbySocket lobbySocket )
			{
				var lobby = lobbySocket.SteamLobby;
				var privacy = lobby.GetData( "access_level" );
				Field( "Lobby Id", lobbySocket.LobbySteamId );
				Field( "Owner Steam Id", lobbySocket.HostSteamId );
				Field( "Members", $"{lobby.MemberCount}/{lobby.MaxMembers}" );
				Field( "Advertised Privacy", string.IsNullOrEmpty( privacy ) ? "Unknown" : privacy );
			}
		}

		if ( Networking.System.Connection is Connection connect )
		{
			output.AppendLine( "\tPrimary Connection" );
			Field( "Name", connect.Name );
			Field( "Id", connect.Id );
			Field( "State", connect.State );
			Field( "Address", connect.Address );
			Field( "Time", connect.Time );
			Field( "Latency", connect.Latency );
			Field( "Messages", $"{connect.MessagesSent} sent / {connect.MessagesRecieved} received" );
		}

		Section( "Player Info" );
		var players = Networking.System.ConnectionInfo.All.Values.ToArray();
		var nameWidth = Math.Max( 24, players.Select( x => x.Name?.Length ?? 0 ).DefaultIfEmpty( 0 ).Max() );
		output.AppendLine( $"\t{"Name".PadRight( nameWidth )}  {"Steam Id",-17}  {"State",-24}  {"Connected For",-13}  Connection Id" );
		foreach ( var info in players )
		{
			var connectedMinutes = (long)Math.Max( 0, (DateTimeOffset.UtcNow - info.ConnectionTime).TotalMinutes );
			output.AppendLine( $"\t{(info.Name ?? "").PadRight( nameWidth )}  {info.SteamId,-17}  {info.State,-24}  {connectedMinutes + "m",-13}  {info.ConnectionId}" );
		}
		if ( players.Length == 0 ) output.AppendLine( "\tNone" );

		Log.Info( output.ToString() );
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
