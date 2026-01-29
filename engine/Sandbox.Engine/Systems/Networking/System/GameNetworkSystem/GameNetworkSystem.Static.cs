using NativeEngine;
using Sandbox.Engine;
using Steamworks;
using Steamworks.Data;
using System.Threading;

namespace Sandbox.Network;

public abstract partial class GameNetworkSystem
{
	//
	// The local network Id
	//
	// 1. Server Network Id should exist and persist before the network system
	//    - We want people to be able to play, and then turn multiplayer on
	//    - The local network ids associated with created objects should still be valid
	// 2. Client network id should be assigned by the host
	//    - If they're re-joining the host might want to give them the same id
	//    - Should be assigned as part of the handshake
	// 3. Possibly when loading a save in the future, the local Ids should restore
	//

	/// <summary>
	/// True if we can be considered the host of this session. Either we're not connected to a server, or we are and we are the host.
	/// </summary>
	[System.Obsolete( "Moved to Networking.IsHost" )]
	public static bool IsHost => Networking.IsHost;

	/// <summary>
	/// True if we're connected to a server and not the host.
	/// </summary>
	[System.Obsolete( "Moved to Networking.IsClient" )]
	public static bool IsClient => Networking.IsClient;

	/// <summary>
	/// True if we're currently connecting to the server
	/// </summary>
	[System.Obsolete( "Moved to Networking.IsConnecting" )]
	public static bool IsConnecting => Networking.IsConnecting;

	/// <summary>
	/// True if we're currently connected etc
	/// </summary>
	[System.Obsolete( "Moved to Networking.IsActive" )]
	public static bool IsActive => Networking.IsActive;

	[System.Obsolete( "Moved to Networking.CreateLobby" )]
	public static void CreateLobby() => Networking.CreateLobby( new() );

	[System.Obsolete( "Moved to Networking.QueryLobbies" )]
	public static Task<List<LobbyInformation>> QueryLobbies() => QueryLobbies( Application.GameIdent );

	[System.Obsolete( "Moved to Networking.QueryLobbies" )]
	internal async static Task<List<LobbyInformation>> QueryLobbies( string gameIdent )
	{
		return await Networking.QueryLobbies( gameIdent );
	}

	[Obsolete( "Moved to Networking.Disconnect" )]
	public static void Disconnect() => Networking.Disconnect();

	[Obsolete( "Moved to Networking.Connect" )]
	public static void Connect( ulong steamid ) => Networking.Connect( steamid );

	[Obsolete( "Moved to Networking.Connect" )]
	public static void Connect( string target ) => Networking.Connect( target );

	[Obsolete( "Moved to Networking.Connect" )]
	public static Task<bool> TryConnectSteamId( ulong steamId ) => Networking.TryConnectSteamId( steamId );

	internal static string CurrentSceneName { get; set; }

	/// <summary>
	/// Called after we've loaded a new scene.
	/// </summary>
	internal static void OnLoadedScene( string sceneName )
	{
		if ( Networking.System is null )
			return;

		if ( Networking.IsConnecting )
			return;

		DedicatedServer.MapName = sceneName;
		Networking.System.OnSceneLoaded();
		CurrentSceneName = sceneName;
	}
}

public struct LobbyInformation
{
	public ulong LobbyId;
	public ulong OwnerId;

	public int Members;
	public int MaxMembers;

	public string Name;
	public string Map;
	public string Game;

	public Dictionary<string, string> Data;

	/// <summary>
	/// True if this lobby is full (Members >= MaxMembers).
	/// </summary>
	public bool IsFull => Members >= MaxMembers;

	/// <summary>
	/// True if this lobby should be hidden from server lists.
	/// </summary>
	public bool IsHidden => Data.TryGetValue( "hdn", out var value ) && value == "1";

	/// <summary>
	/// Return true if this lobby contains a friend.
	/// </summary>
	internal bool ContainsFriends => false;

	public string Get( string key, string defaultValue = "" )
	{
		return Data.GetValueOrDefault( key, defaultValue );
	}
}

internal static class DedicatedServer
{
	internal static SteamNetwork.IpListenSocket IpSocket { get; private set; }
	internal static SteamNetwork.IdListenSocket IdSocket { get; private set; }

	private static bool IsGameServerActive { get; set; }
	private static bool Initialized { get; set; }

	private static Dictionary<string, string> _data = new();
	private static string _mapName;
	private static string _name;

	/// <summary>
	/// All metadata for this dedicated server.
	/// </summary>
	public static IReadOnlyDictionary<string, string> Data => _data;

	/// <summary>
	/// The current name of the dedicated server.
	/// </summary>
	public static string Name
	{
		get => _name;
		set
		{
			var sgs = Steam.SteamGameServer();
			if ( !sgs.IsValid || !Networking.IsHost )
			{
				_name = value;
				return;
			}

			if ( _name == value )
				return;

			sgs.SetServerName( value );
			sgs.SetAdvertiseServerActive( true );

			_name = value;
		}
	}

	/// <summary>
	/// The current map name of the dedicated server.
	/// </summary>
	public static string MapName
	{
		get => _mapName;
		set
		{
			var sgs = Steam.SteamGameServer();
			if ( !sgs.IsValid || !Networking.IsHost )
			{
				_mapName = value;
				return;
			}

			if ( _mapName == value )
				return;

			sgs.SetMapName( value );
			sgs.SetAdvertiseServerActive( true );

			_mapName = value;
		}
	}

	/// <summary>
	/// Log a warning if the tags string is too long.
	/// </summary>
	private static void LogTagsLengthWarning( string tagsString )
	{
		if ( tagsString.Length <= 128 )
			return;

		var lost = tagsString[128..];
		Log.Warning( $"Steam game tags exceed 128 char limit ({tagsString.Length} chars). Data dropped: \"{lost}\"" );
	}

	/// <summary>
	/// Set data for this dedicated server. This data is used when querying or filtering servers. Uses game tags
	/// internally, which have a hardcoded character limit enforced by Steam.
	/// </summary>
	public static void SetData( string key, string value )
	{
		var sgs = Steam.SteamGameServer();
		if ( !sgs.IsValid || !Networking.IsHost )
		{
			_data[key] = value;
			return;
		}

		_data[key] = value;

		var tagsString = string.Join( ",", _data.Select( kv => $"{kv.Key}:{kv.Value}" ) );

		if ( tagsString.Length > 128 )
			LogTagsLengthWarning( tagsString );

		sgs.SetGameTags( tagsString[..Math.Min( 128, tagsString.Length )] );
	}

	/// <summary>
	/// Get data for this dedicated server. This data is used when querying or filtering servers.
	/// </summary>
	public static string GetData( string key )
	{
		return _data.TryGetValue( key, out var value ) ? value : string.Empty;
	}

	public static async Task<bool> Start( LobbyConfig config )
	{
		if ( !Initialized )
		{
			Dispatch.Install<SteamServersConnected_t>( OnConnected, true );
			Dispatch.Install<SteamServerConnectFailure_t>( OnConnectionFailed, true );
			Dispatch.Install<SteamServersDisconnected_t>( OnDisconnected, true );

			Initialized = true;
		}

		if ( !IsGameServerActive )
		{
			Steam.SteamGameServer_Init( Networking.Port, Networking.QueryPort, "1.0.0.0" );
			Networking.Bootstrap();

			// Conna: we can abstract this stuff out later, but for now we only use Steam networking for dedicated servers.
			if ( Networking.HideAddress )
			{
				var sns = Steam.SteamNetworkingSockets();
				sns.BeginRequestFakeIP();
			}
		}

		await Task.Delay( 1000 );

		var gameTitle = Application.GamePackage?.Title ?? "s&box";
		var mapName = Application.MapPackage?.Title ?? GameNetworkSystem.CurrentSceneName ?? gameTitle;

		try
		{
			var hostname = Networking.ServerName;

			if ( string.IsNullOrWhiteSpace( hostname ) )
				hostname = config.Name;

			if ( string.IsNullOrWhiteSpace( hostname ) )
				hostname = $"{gameTitle} Dedicated Server";

			_data = new()
			{
				{ "revision", Application.GamePackage?.Revision?.VersionId.ToString() ?? string.Empty },
				{ "protocol", Protocol.Network.ToString() },
				{ "gameident", Application.GameIdent },
				{ "mapident", Application.MapPackage?.Ident ?? string.Empty },
				{ "buildid", Application.Version },
				{ "hdn", config.Hidden ? "1" : "0" },
				{ "api", Protocol.Api.ToString() }
			};

			// Add any user-set data
			foreach ( var kvp in Networking.ServerData )
			{
				_data[kvp.Key] = kvp.Value;
			}

			var tagsString = string.Join( ",", _data.Select( kv => $"{kv.Key}:{kv.Value}" ) );

			if ( tagsString.Length > 128 )
				LogTagsLengthWarning( tagsString );

			var sgs = Steam.SteamGameServer();
			sgs.SetGameDescription( gameTitle );
			sgs.SetGameTags( tagsString[..Math.Min( 128, tagsString.Length )] );
			sgs.SetMaxPlayerCount( config.MaxPlayers );
			sgs.SetProduct( "sbox" );
			sgs.SetModDir( "sbox" );
			sgs.SetDedicatedServer( true );
			sgs.SetServerName( hostname );
			sgs.SetMapName( mapName );

			Networking.UpdateServerName( hostname );
			Networking.MaxPlayers = config.MaxPlayers;

			_mapName = mapName;
			_name = hostname;

			if ( !IsGameServerActive && !sgs.BLoggedOn() )
			{
				if ( !string.IsNullOrEmpty( Networking.GameServerToken ) )
					sgs.LogOn( Networking.GameServerToken );
				else
					sgs.LogOnAnonymous();
			}

			var timeout = TimeSpan.FromSeconds( 10f );
			using var timeoutSource = new CancellationTokenSource( timeout );
			var delay = TimeSpan.FromMilliseconds( 100 );

			while ( !sgs.BLoggedOn() )
			{
				if ( timeoutSource.IsCancellationRequested )
					return false;

				await Task.Delay( delay, timeoutSource.Token );
			}

			sgs.SetAdvertiseServerActive( true );

			IpSocket ??= new SteamNetwork.IpListenSocket
			{
				AutoDispose = false
			};

			IdSocket ??= new SteamNetwork.IdListenSocket( 77 )
			{
				AutoDispose = false
			};

			IsGameServerActive = true;

			return true;
		}
		catch ( Exception e )
		{
			if ( e is TaskCanceledException )
				Log.Warning( "Unable to connect to Steam in a reasonable time!" );
			else
				Log.Error( e );

			Shutdown();

			return false;
		}
	}

	private static void OnConnected( SteamServersConnected_t cb )
	{
		Log.Warning( $"Connected to Steam" );
	}

	private static void OnDisconnected( SteamServersDisconnected_t cb )
	{
		Log.Warning( $"Connection to Steam Disconnected: {cb.Result}" );
	}

	private static void OnConnectionFailed( SteamServerConnectFailure_t cb )
	{
		Log.Warning( $"Connection to Steam Failed: {cb.Result}" );
	}

	public static void Hide()
	{
		if ( !IsGameServerActive )
			return;

		var sgs = Steam.SteamGameServer();

		if ( sgs.IsValid )
		{
			sgs.SetAdvertiseServerActive( false );
		}
	}

	public static void Shutdown()
	{
		if ( !IsGameServerActive )
			return;

		IdSocket?.Dispose();
		IdSocket = null;

		IpSocket?.Dispose();
		IpSocket = null;

		try
		{
			var sgs = Steam.SteamGameServer();

			if ( sgs.IsValid )
			{
				sgs.LogOff();
			}
		}
		catch ( Exception e )
		{
			Log.Error( e );
		}

		Steam.SteamGameServer_Shutdown();
		IsGameServerActive = false;
	}
}
