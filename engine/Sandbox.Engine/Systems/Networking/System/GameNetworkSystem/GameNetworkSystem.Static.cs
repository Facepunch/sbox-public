using NativeEngine;
using Sandbox.Engine;
using Sandbox.SteamApi;
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

	/// <summary>
	/// Round-trip latency in milliseconds, or -1 if unknown. Steam lobbies use a relay-route estimate.
	/// </summary>
	public int Ping;

	/// <summary>
	/// True when Ping is estimated from Steam relay locations rather than measured from a server query.
	/// </summary>
	public bool IsPingEstimated;

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
	/// True if you need a password to join this lobby or server.
	/// </summary>
	public bool IsPasswordProtected => Data.TryGetValue( "pw", out var value ) && value == "1";

	/// <summary>
	/// The engine API version it's running - 0 if it doesn't say (a dedicated server's tags can be cut short).
	/// </summary>
	public int ApiVersion => Data is not null && Data.TryGetValue( "api", out var value ) && int.TryParse( value, out var api ) ? api : 0;

	/// <summary>
	/// The network protocol it talks - 0 if it doesn't say.
	/// </summary>
	public int NetworkProtocol => Data is not null && Data.TryGetValue( "protocol", out var value ) && int.TryParse( value, out var protocol ) ? protocol : 0;

	/// <summary>
	/// True if it can't be joined from this version of the game - it runs a different API version or network protocol.
	/// Lobbies like this are only listed when asked for (Networking.QueryLobbies with includeOutdated). What it doesn't say
	/// isn't held against it.
	/// </summary>
	public bool IsOutdated => (ApiVersion != 0 && ApiVersion != Protocol.Api) || (NetworkProtocol != 0 && NetworkProtocol != Protocol.Network);

	/// <summary>
	/// True if it's <see cref="IsOutdated"/> the other way - on a newer version than this one, which is what needs updating.
	/// </summary>
	public bool IsNewerVersion => ApiVersion > Protocol.Api || ((ApiVersion == 0 || ApiVersion == Protocol.Api) && NetworkProtocol > Protocol.Network);

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
	private static TaskCompletionSource<bool> _steamLogOn;

	private static Dictionary<string, string> _data = new();
	private static string _mapName;
	private static string _name;
	private static bool _passwordProtected;

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
			if ( !SteamNative.GameServer.IsAvailable() || !Networking.IsHost )
			{
				_name = value;
				return;
			}

			if ( _name == value )
				return;

			SteamNative.GameServer.SetServerName( value );
			SteamNative.GameServer.SetAdvertiseServerActive( true );

			_name = value;
		}
	}

	/// <summary>
	/// Whether the server browser shows this server needs a password (sv_password).
	/// </summary>
	public static bool PasswordProtected
	{
		get => _passwordProtected;
		set
		{
			if ( !SteamNative.GameServer.IsAvailable() || !Networking.IsHost )
			{
				_passwordProtected = value;
				return;
			}

			if ( _passwordProtected == value )
				return;

			SteamNative.GameServer.SetPasswordProtected( value );
			SteamNative.GameServer.SetAdvertiseServerActive( true );

			_passwordProtected = value;
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
			if ( !SteamNative.GameServer.IsAvailable() || !Networking.IsHost )
			{
				_mapName = value;
				return;
			}

			if ( _mapName == value )
				return;

			SteamNative.GameServer.SetMapName( value );
			SteamNative.GameServer.SetAdvertiseServerActive( true );

			_mapName = value;
		}
	}

	static string GameTags => string.Join( ",", _data.Select( kv => $"{kv.Key}:{kv.Value}" ) );

	/// <summary>
	/// Set data for this dedicated server. This data is used when querying or filtering servers. Uses game tags
	/// internally, which the native wrapper limits to the Steam UTF-8 buffer size.
	/// </summary>
	public static void SetData( string key, string value )
	{
		_data[key] = value;
		if ( Networking.IsHost && SteamNative.GameServer.IsAvailable() )
			SteamNative.GameServer.SetGameTags( GameTags );
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
		if ( !IsGameServerActive )
		{
			await LogOnToSteamAsync();
			Networking.Bootstrap();

			// Conna: we can abstract this stuff out later, but for now we only use Steam networking for dedicated servers.
			if ( Networking.HideAddress )
			{
				SteamNative.NetworkingSockets.BeginAsyncRequestFakeIP();
			}
		}

		await Task.Delay( 1000 );

		var gameTitle = Application.GamePackage?.Title ?? "s&box";
		var mapName = Application.Map?.Title ?? GameNetworkSystem.CurrentSceneName ?? gameTitle;

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
				{ "mapident", Application.Map?.ServerTag ?? string.Empty },
				{ "buildid", Application.Version },
				{ "hdn", config.Hidden ? "1" : "0" },
				{ "api", Protocol.Api.ToString() }
			};

			// Add any user-set data
			foreach ( var kvp in Networking.ServerData )
			{
				_data[kvp.Key] = kvp.Value;
			}

			var passwordProtected = Networking.System?.Access.HasPassword ?? false;
			SteamNative.GameServer.SetAdvertisement( gameTitle, hostname, mapName, config.MaxPlayers, passwordProtected, GameTags );

			Networking.UpdateServerName( hostname );
			Networking.MaxPlayers = config.MaxPlayers;
			Networking.IsDedicatedServer = Application.IsDedicatedServer;

			_mapName = mapName;
			_name = hostname;
			_passwordProtected = passwordProtected;

			if ( !await LogOnToSteamAsync() ) return false;

			SteamNative.GameServer.SetAdvertiseServerActive( true );

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

	/// <summary>
	/// Bring up the Steam game server and log on. The server's packages install from the workshop
	/// before the server is started, so they call this too. Returns false if we couldn't log on.
	/// </summary>
	internal static async Task<bool> LogOnToSteamAsync()
	{
		if ( !ThreadSafe.IsMainThread ) await MainThread.Wait();
		if ( _steamLogOn is { } pending ) return await pending.Task;

		if ( !SteamNative.GameServer.IsAvailable() )
		{
			SteamNative.GameServer.Initialize( Networking.Port, Networking.QueryPort, "1.0.0.0" );
			if ( !SteamNative.GameServer.IsAvailable() ) return false;

			EngineFileSystem.DownloadedFiles.CreateDirectory( "/workshop" );
			if ( !SteamNative.Workshop.InitWorkshopForGameServer( EngineFileSystem.DownloadedFiles.GetFullPath( "/workshop" ) ) )
				Log.Warning( "Couldn't initialize the workshop for the game server" );

			SteamNative.GameServer.ConfigureDedicatedServer( Application.GamePackage?.Title ?? "s&box" );
		}

		if ( SteamNative.GameServer.IsLoggedOn() ) return true;

		var completion = new TaskCompletionSource<bool>( TaskCreationOptions.RunContinuationsAsynchronously );
		_steamLogOn = completion;

		try
		{
			SteamNative.GameServer.LogOn( Networking.GameServerToken );
			return await completion.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );
		}
		catch ( TimeoutException )
		{
			Log.Warning( "Timed out waiting for Steam game server logon." );
			return false;
		}
		finally
		{
			completion.TrySetResult( false );
			if ( _steamLogOn == completion ) _steamLogOn = null;
		}
	}

	internal static void OnConnected()
	{
		_steamLogOn?.TrySetResult( true );
		Log.Info( "Connected to Steam" );

		Utility.Steam.InitializeClient();
	}

	internal static void OnDisconnected( Result result )
	{
		_steamLogOn?.TrySetResult( false );
		Log.Warning( $"Connection to Steam Disconnected: {result}" );
	}

	internal static void OnConnectionFailed( Result result, bool stillRetrying )
	{
		if ( !stillRetrying ) _steamLogOn?.TrySetResult( false );
		Log.Warning( $"Connection to Steam Failed: {result} (retrying: {stillRetrying})" );
	}

	public static void Hide()
	{
		if ( !IsGameServerActive )
			return;


		if ( SteamNative.GameServer.IsAvailable() )
		{
			SteamNative.GameServer.SetAdvertiseServerActive( false );
		}
	}

	public static void Shutdown()
	{
		_steamLogOn?.TrySetResult( false );
		_steamLogOn = null;

		if ( !IsGameServerActive && !SteamNative.GameServer.IsAvailable() )
			return;

		IdSocket?.Dispose();
		IdSocket = null;

		IpSocket?.Dispose();
		IpSocket = null;

		try
		{


			if ( SteamNative.GameServer.IsAvailable() )
			{
				SteamNative.GameServer.LogOff();
			}
		}
		catch ( Exception e )
		{
			Log.Error( e );
		}

		if ( Application.IsDedicatedServer )
			Services.Ugc.CancelDownloads();

		SteamNative.GameServer.Shutdown();
		IsGameServerActive = false;
	}
}
