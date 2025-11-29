using Sandbox.Engine;
using Sandbox.Network;
using Sandbox.Services;
using System.Threading;

namespace Sandbox;

public static partial class Networking
{
	/// <summary>
	/// Get all lobbies for the current game.
	/// </summary>
	public static Task<List<LobbyInformation>> QueryLobbies( CancellationToken ct = default ) => QueryLobbies( Application.GameIdent, ct );

	/// <summary>
	/// Get all lobbies for a specific game.
	/// </summary>
	public static Task<List<LobbyInformation>> QueryLobbies( string gameIdent, CancellationToken ct = default ) =>
		QueryLobbies( new Dictionary<string, string> { { "game", gameIdent } }, true, ct );

	/// <summary>
	/// Get all lobbies for a specific game and map.
	/// </summary>
	public static Task<List<LobbyInformation>> QueryLobbies( string gameIdent, string mapIdent, CancellationToken ct = default ) =>
		QueryLobbies( new Dictionary<string, string> { { "game", gameIdent }, { "map", mapIdent } }, true, ct );

	private static async Task<List<LobbyInformation>> QueryServers( string gameIdent, string mapIdent, IReadOnlyDictionary<string, string> filters, CancellationToken ct )
	{
		var list = new List<LobbyInformation>();

		try
		{
			using var serverList = new ServerList();

			if ( !string.IsNullOrEmpty( mapIdent ) )
				serverList.AddFilter( "gametagsand", $"mapident:{mapIdent}" );

			if ( !string.IsNullOrEmpty( gameIdent ) )
				serverList.AddFilter( "gametagsand", $"gameident:{gameIdent}" );

			if ( filters is not null )
			{
				foreach ( var (k, v) in filters )
				{
					if ( k == "hidden" || k == "hdn" ) continue;

					serverList.AddFilter( "gametagsand", $"{k}:{v}" );
				}

				if ( filters.TryGetValue( "hidden", out var hdn ) && hdn.ToBool() == true )
				{
					// include hidden servers
				}
				else
				{
					// Hide hidden servers by default
					serverList.AddFilter( "gametagsand", "hdn:0" );
				}
			}

			serverList.Query();

			while ( serverList.IsQuerying )
			{
				if ( ct.IsCancellationRequested )
				{
					// Stop waiting on the server list if the caller timed out/cancelled, return anything collected so far
					return list;
				}

				await Task.Yield();
			}

			foreach ( var e in serverList )
			{
				var lobby = new LobbyInformation
				{
					LobbyId = e.SteamId,
					OwnerId = e.SteamId,
					Game = e.Game,
					Name = e.Name,
					Map = e.Map,
					Members = e.Players,
					MaxMembers = e.MaxPlayers,
					Data = new()
				};

				foreach ( var t in e.Tags )
				{
					if ( string.IsNullOrEmpty( t ) )
						continue;

					var split = t.Split( ':' );
					if ( split.Length != 2 )
						continue;

					var key = split[0];
					var value = split[1];

					switch ( key )
					{
						case "mapident":
							lobby.Map = value;
							break;
						case "gameident":
							lobby.Game = value;
							break;
					}

					lobby.Data.Add( key, value );
				}

				list.Add( lobby );
			}
		}
		catch ( OperationCanceledException )
		{
			// Caller cancelled, return whatever we gathered instead of bubbling the exception
			return list;
		}
		catch ( Exception e )
		{
			Log.Error( e );
		}

		return list;
	}

	/// <summary>
	/// Get all lobbies that match the specified filters.
	/// </summary>
	public static async Task<List<LobbyInformation>> QueryLobbies( Dictionary<string, string> filters, bool includeServers = true, CancellationToken ct = default )
	{
		if ( Application.IsDedicatedServer )
		{
			Log.Warning( "Networking.QueryLobbies: unable to query lobbies on a dedicated server." );
			return [];
		}

		using var cts = CancellationTokenSource.CreateLinkedTokenSource( ct );
		cts.CancelAfter( TimeSpan.FromSeconds( 30 ) );

		ct = cts.Token;

		Task<List<LobbyInformation>> serverListTask = Task.FromResult( new List<LobbyInformation>() );

		if ( includeServers )
		{
			serverListTask = QueryServers( filters.GetValueOrDefault( "game" ), filters.GetValueOrDefault( "map" ), filters.Without( "game" ).Without( "map" ), ct );
		}

		var q = Steamworks.SteamMatchmaking.LobbyList;
		q = q.FilterDistanceWorldwide();
		q = q.WithKeyValue( "lobby_type", "scene" );
		q = q.WithKeyValue( "protocol", $"{Protocol.Network}" );
		q = q.WithKeyValue( "api", $"{Protocol.Api}" );
		q = q.WithNotEqual( "toxic", 1 );

		foreach ( var filter in filters )
		{
			if ( filter.Value is null ) continue;
			if ( filter.Key == "hidden" || filter.Key == "hdn" ) continue;

			q = q.WithKeyValue( filter.Key, filter.Value );
		}

		if ( filters.TryGetValue( "hidden", out var hdn ) && hdn.ToBool() == true )
		{
			// include hidden servers
		}
		else
		{
			// Hide hidden servers by default
			q = q.WithKeyValue( "hdn", "0" );
		}

		// by key name
		q = q.WithMaxResults( 1000 );

		var lobbies = await q.RequestAsync( ct );

		var found = new List<LobbyInformation>();

		try
		{
			var servers = await serverListTask;
			if ( servers is not null && servers.Any() )
			{
				found.AddRange( servers );
			}
		}
		catch ( OperationCanceledException )
		{
			return found;
		}

		if ( lobbies == null || lobbies.Length == 0 )
			return found;

		foreach ( var l in lobbies )
		{
			var item = new LobbyInformation();
			item.LobbyId = l.Id;
			item.OwnerId = l.Owner.Id;

			item.MaxMembers = l.MaxMembers;
			item.Members = l.MemberCount;
			item.Data = l.Data.ToDictionary( x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase );

			item.Data.Remove( "name", out item.Name );
			item.Data.Remove( "map", out item.Map );
			item.Data.Remove( "game", out item.Game );

			if ( string.IsNullOrEmpty( item.Name ) ) item.Name = $"{item.LobbyId}";
			if ( string.IsNullOrEmpty( item.Map ) ) item.Map = "";
			if ( string.IsNullOrEmpty( item.Game ) ) item.Game = "";

			found.Add( item );
		}

		return found;
	}

	/// <summary>
	/// The client has been told to reconnect to the server. So disconnect and keep trying to connect.
	/// </summary>
	internal static void StartReconnecting( ReconnectMsg data )
	{
		IGameInstanceDll.Current?.CloseGame();

		LoadingScreen.IsVisible = true;
		LoadingScreen.Title = "Server Loading..";

		var lastConnection = LastConnectionString;

		Disconnect();

		if ( string.IsNullOrWhiteSpace( lastConnection ) )
		{
			Log.Warning( "Tried to reconnect but no connection string" );
			LoadingScreen.IsVisible = false;
			return;
		}

		_ = Reconnect( data, lastConnection );
	}

	/// <summary>
	/// The client has been told to reconnect to the server. Keep trying for 30 seconds.
	/// </summary>
	static async Task Reconnect( ReconnectMsg data, string address )
	{
		await Task.Delay( 1000 );

		RealTimeSince timeSinceStarted = 0;

		Log.Info( $"Reconnecting to {address}" );

		while ( timeSinceStarted < 60f )
		{
			if ( !await TryConnect( address, 1 ) )
				continue;

			Log.Info( "Reconnect succeeded." );
			return;
		}

		LoadingScreen.IsVisible = false;
		Log.Info( "Reconnect failed." );
	}

}
