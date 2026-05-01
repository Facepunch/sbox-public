using Sandbox.Engine;
using Sandbox.Network;
using Sandbox.Utility;
using Sentry;
using Steamworks;
using Steamworks.Data;
using System.Text;
using System.Threading;
using Steam = NativeEngine.Steam;

namespace Sandbox;

/// <summary>
/// Global manager to hold and tick the singleton instance of NetworkSystem.
/// </summary>
public static partial class Networking
{
	internal const int ReceiveBatchSize = 32;
	internal static NetworkSystem System;

	[ConVar( "net_max_outgoing", ConVarFlags.Protected, Help = "Maximum outgoing messages to send per tick. 0 = unlimited." )]
	internal static int MaxOutgoingMessagesPerTick { get; set; } = 1024;

	[ConVar( "net_max_incoming", ConVarFlags.Protected, Help = "Maximum incoming messages to receive per tick. 0 = unlimited." )]
	internal static int ReceiveBatchSizePerTick { get; set; } = 1024;

	internal static Dictionary<string, string> ServerData { get; set; } = new();

	/// <summary>
	/// Set data about the current server or lobby. Other players can query this
	/// when searching for a game. Note: for now, try to keep the key and value as short
	/// as possible, Steam enforce a character limit on server tags, so it could be possible
	/// to reach that limit when running a Dedicated Server. In the future we'll store this
	/// stuff on our backend, so that won't be a problem.
	/// </summary>
	public static void SetData( string key, string value )
	{
		ServerData[key] = value;

		if ( !IsHost || System is null )
			return;

		foreach ( var s in System.Sockets )
		{
			s.SetData( key, value );
		}

		var msg = new ServerDataMsg { Name = key, Value = value };
		System.Broadcast( msg, Connection.ChannelState.Welcome );
	}

	/// <summary>
	/// Get data about the current server or lobby. This data can be used for filtering
	/// when querying lobbies.
	/// </summary>
	public static string GetData( string key, string defaultValue = "" )
	{
		return ServerData.GetValueOrDefault( key, defaultValue );
	}

	private static string _serverName;
	private static string _mapName;

	/// <summary>
	/// The name of the server you are currently connected to.
	/// </summary>
	public static string ServerName
	{
		get => _serverName;
		set
		{
			if ( _serverName == value )
				return;

			_serverName = value;

			if ( !IsHost || System is null )
				return;

			foreach ( var s in System.Sockets )
			{
				s.SetServerName( value );
			}

			var msg = new ServerNameMsg { Name = value };
			System.Broadcast( msg, Connection.ChannelState.Welcome );
		}
	}

	/// <summary>
	/// The name of the map being used on the server you're connected to.
	/// </summary>
	public static string MapName
	{
		get => _mapName;
		internal set
		{
			if ( _mapName == value )
				return;

			_mapName = value;

			if ( !IsHost || System is null )
				return;

			foreach ( var s in System.Sockets )
			{
				s.SetMapName( value );
			}

			var msg = new MapNameMsg { Name = value };
			System.Broadcast( msg, Connection.ChannelState.Welcome );
		}
	}

	/// <summary>
	/// The maximum number of players allowed on the server you're connected to.
	/// </summary>
	public static int MaxPlayers { get; internal set; }

	/// <summary>
	/// The last connection string used to connect to a server.
	/// </summary>
	internal static string LastConnectionString { get; set; }

	[ConVar( "net_debug", ConVarFlags.Protected )]
	internal static bool Debug { get; set; }

	[ConVar( "net_hide_address", ConVarFlags.Protected )]
	internal static bool HideAddress { get; set; } = true;

	[ConVar( "net_game_server_token", ConVarFlags.Protected )]
	internal static string GameServerToken { get; set; } = string.Empty;

	[ConVar( "net_interp_time", ConVarFlags.Protected, Help = "Interpolation time in seconds" )]
	internal static float InterpolationTime { get; set; } = 0.1f;

	[ConVar( "net_fakepacketloss", ConVarFlags.Protected | ConVarFlags.Cheat, Help = "Simulate packet loss in %" )]
	internal static int FakePacketLoss { get; set; } = 0;

	[ConVar( "net_fakelag", ConVarFlags.Protected | ConVarFlags.Cheat, Help = "Simulate latency in ms" )]
	internal static int FakeLag { get; set; } = 0;

	[ConVar( "net_query_port", ConVarFlags.Protected )]
	internal static int QueryPort { get; set; } = 27016;

	[ConCmd( "hostname", ConVarFlags.Protected | ConVarFlags.Admin )]
	private static void SetHostname( string name )
	{
		ServerName = name;
	}

	[ConVar( "port", ConVarFlags.Protected )]
	internal static int Port { get; set; } = 27015;

	[ConCmd( "kick", ConVarFlags.Protected )]
	private static void Kick( string id, string reason = "" )
	{
		if ( !IsHost )
		{
			Log.Warning( "You need to be the host to kick other players!" );
			return;
		}

		var connection = Connection.All.FirstOrDefault( c => c.SteamId.ToString() == id || c.DisplayName.Contains( id ) );

		if ( connection is null )
		{
			Log.Warning( "Unable to find a matching connection with that Steam Id or Display Name!" );
			return;
		}

		connection.Kick( reason );
	}

	/// <summary>
	/// Get the latest host stats such as bandwidth used and the current frame rate.
	/// </summary>
	public static HostStats HostStats => System?.HostStats ?? default;

	// Wire-level network stats for this machine, aggregated across all non-local connections.
	// Post-compression, post-framing bytes as reported by the transport layer.
	internal static ConnectionStats LocalStats { get; private set; }

	/// <summary>
	/// True if we can be considered the host of this session. Either we're not connected to a server, or we are host of a server.
	/// </summary>
	public static bool IsHost => System is null || System.IsHost;

	/// <summary>
	/// True if we're currently connected to a server, and we are not the host
	/// </summary>
	public static bool IsClient => System is not null && System.IsClient;

	/// <summary>
	/// True if we're currently connecting to the server
	/// </summary>
	public static bool IsConnecting => System?.IsConnecting ?? false;

	/// <summary>
	/// True if we're currently connecting to the server
	/// </summary>
	public static bool IsActive => System is not null;

	/// <summary>
	/// True if we're currently disconnecting from the server
	/// </summary>
	internal static bool IsDisconnecting => System is not null && System.IsDisconnecting;

	/// <summary>
	/// The connection of the current network host.
	/// </summary>
	[Obsolete( "Moved to Connection.Host" )]
	public static Connection HostConnection => Connection.Host;

	/// <summary>
	/// Whether the host is busy right now. This can be used to determine if
	/// the host can be changed.
	/// </summary>
	internal static bool IsHostBusy
	{
		get
		{
			return System?.IsHostBusy ?? true;
		}
	}

	/// <summary>
	/// A list of connections that are currently on this server. If you're not on a server
	/// this will return only one connection (Connection.Local). Some games restrict the 
	/// connection list - in which case you will get an empty list.
	/// </summary>
	[Obsolete( "Moved to Connection.All" )]
	public static IReadOnlyList<Connection> Connections => Connection.All;

	internal static void Bootstrap()
	{
		var utils = NativeEngine.Steam.SteamNetworkingUtils();
		if ( !utils.IsValid ) return;

		var sockets = NativeEngine.Steam.SteamNetworkingSockets();
		if ( !sockets.IsValid ) return;

		Log.Info( "Bootstrap Networking..." );

		// conna: fuck it, let's set these to insane values.
		var maxBufferSize = 1024 * 1024 * 64;
		utils.SetConfig( NetConfig.SendBufferSize, maxBufferSize );
		utils.SetConfig( NetConfig.RecvBufferSize, maxBufferSize );
		utils.SetConfig( NetConfig.RecvMaxMessageSize, maxBufferSize );
		utils.SetConfig( NetConfig.RecvBufferMessages, 256 * 256 );

		// conna: allow 120s before a client will disconnect from a timeout.
		utils.SetConfig( NetConfig.TimeoutConnected, 120 * 1000 );

		// conna: when these two values are not the same, there seems to be a bug that causes the send buffer
		// to often become clogged up and not clear properly. Ultimately resulting in heavier load and backlog.
		// These values are ridiculous because there's no way to remove this limit. So let's just make it 1gbps.
		utils.SetConfig( NetConfig.SendRateMin, 1024 * 1024 * 1024 );
		utils.SetConfig( NetConfig.SendRateMax, 1024 * 1024 * 1024 );

		utils.SetConfig( NetConfig.P2P_Transport_ICE_Enable, Defines.k_nSteamNetworkingConfig_P2P_Transport_ICE_Enable_All );
		utils.SetConfig( NetConfig.P2P_STUN_ServerList, "stun.l.google.com:19302,stun1.l.google.com:19302,stun2.l.google.com:19302,stun3.l.google.com:19302,stun4.l.google.com:19302" );

		sockets.StartAuthentication();
	}

	/// <summary>
	/// Internally update the server name without propagating to sockets.
	/// </summary>
	/// <param name="name"></param>
	internal static void UpdateServerName( string name )
	{
		_serverName = name;
	}

	/// <summary>
	/// Get the status of our connection to the Steam Datagram Relay service.
	/// </summary>
	/// <returns></returns>
	internal static unsafe SteamNetworkingAvailability GetSteamRelayStatus( out string debugMsg )
	{
		var utils = Steam.SteamNetworkingUtils();
		if ( !utils.IsValid )
		{
			debugMsg = "SteamNetworkingUtils is not initialized";
			return SteamNetworkingAvailability.Unknown;
		}

		var buffer = new byte[256];

		fixed ( byte* ptr = buffer )
		{

			var availability = Glue.Networking.GetRelayNetworkStatus( new( ptr ) );
			debugMsg = Encoding.UTF8.GetString( buffer ).TrimEnd( '\0' );
			return availability;
		}
	}

	/// <summary>
	/// Reset any static members to their defaults or clear them.
	/// </summary>
	internal static void Reset()
	{
		MaxPlayers = 0;
		ServerData.Clear();
		LocalStats = default;
	}

	private static int? OldFakePacketLoss { get; set; }
	private static int? OldFakeLag { get; set; }
	private static void UpdateFakeLag()
	{
		var utils = NativeEngine.Steam.SteamNetworkingUtils();
		if ( !utils.IsValid ) return;


		if ( OldFakePacketLoss != FakePacketLoss )
		{
			var clampedPacketLoss = FakePacketLoss.Clamp( 0, 100 );
			utils.SetConfig( NetConfig.FakePacketLoss_Send, clampedPacketLoss );
			utils.SetConfig( NetConfig.FakePacketLoss_Recv, clampedPacketLoss );
			OldFakePacketLoss = FakePacketLoss;
		}

		if ( OldFakeLag == FakeLag )
			return;

		utils.SetConfig( NetConfig.FakePacketLag_Send, FakeLag );
		utils.SetConfig( NetConfig.FakePacketLag_Recv, FakeLag );
		OldFakeLag = FakeLag;
	}

	/// <summary>
	/// Aggregate wire stats across all active non-local connections, expose them via
	/// <see cref="LocalStats"/>, and feed them into the performance telemetry pipeline
	/// so they appear in activity updates alongside frametime and render stats.
	/// </summary>
	private static void UpdateLocalStats()
	{
		if ( System is null )
		{
			LocalStats = default;
			return;
		}

		var totalIn = 0f;
		var totalOut = 0f;
		var totalPing = 0;
		var connectionCount = 0;

		// Iterate System.Connections (real wire connections in the NetworkSystem) rather than
		// Connection.All, which allocates and includes mock ConnectionInfo entries with zero stats.
		foreach ( var c in System.Connections )
		{
			var s = c.Stats;
			totalIn += s.InBytesPerSecond;
			totalOut += s.OutBytesPerSecond;
			totalPing += s.Ping;
			connectionCount++;
		}

		LocalStats = new ConnectionStats( "local" )
		{
			InBytesPerSecond = totalIn,
			OutBytesPerSecond = totalOut,
			// Average ping across real wire connections; on a client this is just the host ping
			Ping = connectionCount > 0 ? totalPing / connectionCount : 0,
		};
	}

	internal static void PreFrameTick()
	{
		UpdateFakeLag();

		try
		{
			SteamNetwork.RunCallbacks();
			System?.Tick();
			System?.SendTableUpdates();
			System?.SendHeartbeat();
			System?.SendHostStats();
			UpdateLocalStats();
		}
		catch ( Exception e )
		{
			Log.Error( e );
		}
	}

	internal static void PostFrameTick()
	{
		try
		{
			System?.SendTableUpdates();
		}
		catch ( Exception e )
		{
			Log.Error( e );
		}
	}

	[Obsolete( "Moved to Connection.Find" )]
	public static Connection FindConnection( Guid id ) => Connection.Find( id );

	/// <summary>
	/// Try to join the best lobby. Return true on success.
	/// </summary>
	public static async Task<bool> JoinBestLobby( string ident )
	{
		// get all lobbies
		var lobbies = await QueryLobbies( ident );

		//
		// try to join most populated with the fewest historic hosts
		//
		foreach ( var lobby in lobbies.OrderByDescending( x => x.Members - x.Get( "hostcount" ).ToInt( 0 ) ) )
		{
			Log.Info( $"Trying to connect to {lobby.LobbyId} ({lobby.Name}).." );
			if ( await TryConnectSteamId( lobby.LobbyId ) )
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// When creating a lobby from the editor, we'll use this override for the lobby privacy.
	/// </summary>
	internal static LobbyPrivacy EditorLobbyPrivacy { get; set; } = LobbyPrivacy.Private;

	private static CancellationTokenSource lobbyCts;

	/// <summary>
	/// Will create a new lobby with the specified <see cref="LobbyConfig"/> to
	/// customize the lobby further.
	/// </summary>
	public static void CreateLobby( LobbyConfig config )
	{
		// Let's not make a lobby if we're in the editor, and we're not playing a game.
		// Editor tools could call this, and we don't want a lingering lobby.
		if ( Application.IsEditor && !Game.IsPlaying )
			throw new UnauthorizedAccessException( "Unable to create a lobby outside of a game" );

		// If this game can only be hosted on a Dedicated Server, ensure that we are one. We'll allow
		// the Editor to host a lobby as well, though, for testing and development purposes.
		var launchMode = Application.GamePackage?.GetCachedMeta( "LaunchMode", "default" ).ToLower();

		if ( launchMode == "dedicatedserveronly" && !Application.IsDedicatedServer && !Application.IsEditor )
			throw new UnauthorizedAccessException( "This game can only be hosted on a Dedicated Server" );

		if ( IsActive )
			return;

		lobbyCts?.Cancel();
		lobbyCts = new();

		//
		// Did the menu want to override the lobby's max players?
		//
		if ( LaunchArguments.MaxPlayers > 0 )
		{
			config.MaxPlayers = LaunchArguments.MaxPlayers;
		}

		//
		// Did the menu want to override the lobby's name?
		//
		if ( !string.IsNullOrEmpty( LaunchArguments.ServerName ) )
		{
			config.Name = LaunchArguments.ServerName;
		}

		//
		// Did the menu want to override the lobby's privacy mode?
		//
		if ( LaunchArguments.Privacy != config.Privacy )
		{
			config.Privacy = LaunchArguments.Privacy;
		}

		_ = CreateLobbyAsync( config, lobbyCts.Token );
	}

	/// <summary>
	/// Will create a new lobby.
	/// </summary>
	[Obsolete( "Use CreateLobby( LobbyConfig )" )]
	public static void CreateLobby()
	{
		var config = new LobbyConfig
		{
			MaxPlayers = Application.GamePackage?.GetCachedMeta( "MaxPlayers", 32 ) ?? 32
		};

		CreateLobby( config );
	}

	static async Task<bool> CreateDedicatedServer( LobbyConfig config, CancellationToken token = default )
	{
		var success = await DedicatedServer.Start( config );
		if ( !success ) return false;

		lock ( NetworkThreadLock )
		{
			var net = new NetworkSystem( "server", Engine.IGameInstanceDll.Current.TypeLibrary )
			{
				Config = config
			};

			System = net;

			net.InitializeHost();
			net.AddSocket( DedicatedServer.IpSocket );
			net.AddSocket( DedicatedServer.IdSocket );

			return !token.IsCancellationRequested;
		}
	}

	static async Task<bool> CreateLobbyAsync( LobbyConfig config, CancellationToken token = default )
	{
		if ( IsActive )
			return false;

		if ( Application.IsEditor )
		{
			config.Privacy = EditorLobbyPrivacy;
		}

		if ( Application.IsDedicatedServer )
		{
			return await CreateDedicatedServer( config, token );
		}

		var net = new NetworkSystem( "lobbyhost", Engine.IGameInstanceDll.Current.TypeLibrary )
		{
			Config = config
		};

		lock ( NetworkThreadLock )
		{
			System = net;
			net.InitializeHost();
		}

		if ( Engine.IToolsDll.Current is not null )
		{
			await Engine.IToolsDll.Current.OnInitializeHost();
		}

		if ( token.IsCancellationRequested )
			return false;

		var socket = await SteamLobbySocket.Create( config );
		if ( socket is null )
		{
			if ( token.IsCancellationRequested )
				return false;

			Disconnect();
			return false;
		}

		if ( token.IsCancellationRequested )
			return false;

		net.AddSocket( socket );

		//
		// If runnning in editor, we create a named socket that we can join locally
		//
		if ( Application.IsEditor || Application.IsStandalone )
		{
			net.AddSocket( new TcpSocket( "127.0.0.1", 55333 ) );
		}

		return true;
	}

	/// <summary>
	/// Disconnect from current multiplayer session.
	/// </summary>
	public static void Disconnect()
	{
		lobbyCts?.Cancel();
		lobbyCts = null;

		if ( System is null ) return;

		lock ( NetworkThreadLock )
		{
			// Send any remaining messages
			System.ProcessMessagesInThread();

			SentrySdk.AddBreadcrumb( $"Disconnected from {System}", "network.disconnect" );

			System.Disconnect();
			System = null;

			DedicatedServer.Hide();
		}
	}

	internal static IDisposable DisconnectScope()
	{
		if ( System is null ) return default;

		System.IsDisconnecting = true;

		return new DisposeAction( () =>
		{
			System.IsDisconnecting = false;

			Disconnect();
		} );
	}

	public static void Connect( ulong steamid ) => Connect( steamid.ToString() );

	/// <summary>
	/// Will try to determine the right method for connection, and then try to connect.
	/// </summary>
	public static void Connect( string target )
	{
		_ = TryConnect( target );
	}

	static async Task<bool> TryConnect( string target, int retries = 30 )
	{
		Disconnect();

		if ( string.IsNullOrWhiteSpace( target ) )
		{
			Log.Warning( "Couldn't connect - target is null!" );
			return false;
		}

		//
		// SteamID
		//
		if ( ulong.TryParse( target, out var steamId ) )
		{
			return await TryConnectSteamId( steamId, retries );
		}

		SentrySdk.AddBreadcrumb( $"Connect to '{target}'", "network.connect" );
		Assert.IsNull( System );

		LoadingScreen.IsVisible = true;
		LoadingScreen.Media = null;
		LoadingScreen.Title = "Connecting";

		OnTryConnect( target );

		var count = 0;
		while ( count < retries )
		{
			lock ( NetworkThreadLock )
			{
				if ( target == "local" )
				{
					Log.Info( $"Connecting to local client.." );

					System = new( "localclient", IGameInstanceDll.Current.TypeLibrary );
					System.Connect( new TcpChannel( "127.0.0.1", 55333 ) );
				}
				else
				{
					// replace localhost
					target = target.Replace( "localhost", "127.0.0.1", StringComparison.OrdinalIgnoreCase );

					// append port if needed
					if ( !target.Contains( ':' ) )
						target = $"{target}:{Port}";

					Log.Info( $"Connecting to {target}.." );
					System = new( "client", IGameInstanceDll.Current.TypeLibrary );
					System.Connect( new SteamNetwork.IpConnection( target ) );
				}

				LastConnectionString = target;
			}

			var success = await AwaitSuccessfulConnection();
			if ( success ) return true;

			if ( System is null )
				return false;

			Log.Info( $"Couldn't connect, retrying ({count}/{retries})" );
			count++;

			Disconnect();
		}

		IGameInstanceDll.Current.Disconnect( $"Connection failed after {retries} retries." );
		return false;
	}

	static async Task<bool> AwaitSuccessfulConnection()
	{
		for ( var i = 0; i < 30; i++ )
		{
			await Task.Delay( 100 );

			if ( System is null )
				return false;

			if ( Connection.Local?.State > Connection.ChannelState.Unconnected )
				return true;
		}

		return false;
	}

	public static async Task<bool> TryConnectSteamId( SteamId steamId, int retries = 30 )
	{
		Disconnect();

		SentrySdk.AddBreadcrumb( $"Connect to '{steamId}'", "network.connect" );
		Assert.IsNull( System );

		LoadingScreen.IsVisible = true;
		LoadingScreen.Media = null;
		LoadingScreen.Title = "Connecting";

		LastConnectionString = steamId.ToString();
		OnTryConnect( LastConnectionString );

		if ( steamId.AccountType == SteamId.AccountTypes.Lobby )
		{
			lobbyCts?.Cancel();
			lobbyCts = new();

			return await JoinSteamLobbyServer( steamId, retries, lobbyCts.Token );
		}

		var count = 0;
		while ( count < retries )
		{
			Log.Info( $"Connecting to {steamId}.." );
			lock ( NetworkThreadLock )
			{
				System = new( "steamclient", IGameInstanceDll.Current.TypeLibrary );
				System.Connect( new SteamNetwork.IdConnection( steamId, 77 ) );
			}

			var success = await AwaitSuccessfulConnection();
			if ( success ) return true;

			if ( System is null )
				return false;

			Log.Info( $"Couldn't connect, retrying ({count}/{retries})" );
			count++;

			Disconnect();
		}

		IGameInstanceDll.Current.Disconnect( $"Connection failed after {retries} retries." );
		return false;
	}

	static async Task<bool> JoinSteamLobbyServer( ulong steamid, int retries, CancellationToken token = default )
	{
		SteamLobbySocket lobbySocket = null;

		// attempt to join the lobby, allowing for the possibility that the lobby doesn't exist yet because the host is still setting up.
		// in future when lobbies persist thru map changes etc, we should be able to remove this retry logic and just attempt to join once.
		var count = 0;
		while ( count < retries )
		{
			var result = await SteamLobbySocket.Join( steamid );

			if ( token.IsCancellationRequested )
				return false;

			if ( result.Response == RoomEnter.Success )
			{
				// ok!
				lobbySocket = result.Socket;
				break;
			}

			if ( result.Response != RoomEnter.DoesntExist )
			{
				// the lobby exists, but we failed to join for some reason. no point in retrying.
				IGameInstanceDll.Current.Disconnect( $"Failed to join lobby: {result.Response}" );
				return false;
			}

			Log.Info( $"Couldn't join lobby ({result.Response}), retrying ({count}/{retries})" );
			count++;

			// the lobby doesn't exist, it might be because the host is still setting up.
			// let's wait a bit and retry.

			await Task.Delay( 2000 );

			if ( token.IsCancellationRequested )
				return false;
		}

		if ( lobbySocket is null )
		{
			IGameInstanceDll.Current.Disconnect( $"Joining lobby failed after {retries} retries." );
			return false;
		}

		Log.Trace( $"Joined Lobby {steamid}" );
		LoadingScreen.Title = "Joined lobby";

		if ( System is not null )
		{
			Log.Warning( "Network is already active - leaving lobby" );
			lobbySocket?.Dispose();
			return false;
		}

		// Don't load no weird maps
		LaunchArguments.Reset();

		// This lobby should tell us what to do
		lock ( NetworkThreadLock )
		{
			System = new( "lobbyclient", IGameInstanceDll.Current.TypeLibrary );
			System.AddSocket( lobbySocket );
		}

		var success = await AwaitSuccessfulConnection();
		if ( success ) return true;

		Disconnect();
		IGameInstanceDll.Current.Disconnect( "Connection timed out." );
		return false;
	}

	static void OnTryConnect( string address )
	{
		// if we're a non-leader in a party and we're connecting to a server that isn't what the leader is on, leave the party.
		if ( PartyRoom.Current is { } party && !party.Owner.IsMe )
		{
			string partyAddress = party.GameAddress;
			if ( string.IsNullOrEmpty( partyAddress ) || partyAddress != address )
			{
				party.Leave();
			}
		}
	}

	/// <summary>
	/// The client has been told to reconnect to the server. Pause while the server restarts, then attempt to reconnect.
	/// </summary>
	internal static async Task<bool> ClientReconnect( ReconnectMsg data )
	{
		IGameInstanceDll.Current?.CloseGame();

		string address = LastConnectionString;
		if ( string.IsNullOrWhiteSpace( address ) )
		{
			IGameInstanceDll.Current.Disconnect( "Reconnect failed, missing target address." );
			return false;
		}

		Disconnect();

		Log.Info( $"Reconnecting to {address}" );

		LoadingScreen.IsVisible = true;
		LoadingScreen.Media = null;
		LoadingScreen.Title = "Server Restarting";
		await Task.Delay( 4000 ); // pause to allow server to restart

		return await TryConnect( address );
	}

	/// <summary>
	/// Are we currently matchmaking?
	/// We want to suppress user-facing join errors in this case, and silently keep trying lobbies until we find one that works.
	/// </summary>
	internal static bool IsMatchmaking { get; private set; }

	internal static IDisposable MatchmakingScope()
	{
		IsMatchmaking = true;

		return new DisposeAction( () =>
		{
			IsMatchmaking = false;
		} );
	}
}
