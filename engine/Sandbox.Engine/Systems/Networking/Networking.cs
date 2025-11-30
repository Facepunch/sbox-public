using Sandbox.Compression;
using Sandbox.Network;
using Sandbox.Utility;
using Sentry;
using Steamworks;
using Steamworks.Data;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Steam = NativeEngine.Steam;

namespace Sandbox;

/// <summary>
/// Global manager to hold and tick the singleton instance of NetworkSystem.
/// </summary>
public static partial class Networking
{
	internal const int MaxIncomingMessages = 32;
	internal static NetworkSystem System;

	internal static Dictionary<string, string> ServerData { get; set; } = new();

	private const byte FlagUncompressed = 0;
	private const byte FlagLz4 = 1;

	/// <summary>
	/// The minimum byte count required to compress using LZ4 encoding. This number
	/// was chosen because the overhead is often not worth it otherwise.
	/// </summary>
	private const int MinimumCompressionByteCount = 128;

	/// <summary>
	/// Try to encode the data from the specified <see cref="ByteStream"/> using LZ4 encoding.
	/// If the data is less than the required byte count, the data will not be compressed.
	/// </summary>
	internal static byte[] EncodeStream( ByteStream stream )
	{
		var src = stream.ToSpan();

		// Compress only if it’s large enough
		if ( src.Length > MinimumCompressionByteCount )
		{
			var compressed = LZ4.CompressBlock( src );

			// Only keep compression if it actually helped
			if ( compressed.Length < src.Length )
			{
				var output = new byte[1 + sizeof( int ) + compressed.Length];
				output[0] = FlagLz4;

				BinaryPrimitives.WriteInt32LittleEndian( output.AsSpan( 1 ), src.Length );
				compressed.CopyTo( output.AsSpan( 1 + sizeof( int ) ) );

				return output;
			}
		}

		var result = new byte[1 + sizeof( int ) + src.Length];
		result[0] = FlagUncompressed;

		BinaryPrimitives.WriteInt32LittleEndian( result.AsSpan( 1 ), src.Length );
		src.CopyTo( result.AsSpan( 1 + sizeof( int ) ) );

		return result;
	}

	private static readonly byte[] ReceiveBuffer = new byte[1024 * 1024 * 4];

	/// <summary>
	/// Try to decode the supplied data using LZ4. If the data cannot be decompressed, then the
	/// original data will be returned.
	/// </summary>
	internal static Span<byte> DecodeStream( byte[] data )
	{
		if ( data.Length < 1 + sizeof( int ) )
			return data;

		var flag = data[0];
		var originalLen = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 1, sizeof( int ) ) );
		ReadOnlySpan<byte> payload = data.AsSpan( 1 + sizeof( int ) );

		switch ( flag )
		{
			case FlagUncompressed:
				return MemoryMarshal.CreateSpan( ref MemoryMarshal.GetArrayDataReference( data ), data.Length )
					.Slice( 1 + sizeof( int ), originalLen );
			case FlagLz4:
				{
					int written = LZ4.DecompressBlock( payload.ToArray(), ReceiveBuffer );
					return ReceiveBuffer.AsSpan( 0, written );
				}
			default:
				return data;
		}
	}

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

	[ConVar( "net_shared_query_port", ConVarFlags.Protected )]
	internal static bool SharedQueryPort { get; set; } = true;

	[ConVar( "net_use_fake_ip", ConVarFlags.Protected )]
	internal static bool UseFakeIP { get; set; } = true;

	[ConVar( "net_game_server_token", ConVarFlags.Protected )]
	internal static string GameServerToken { get; set; } = string.Empty;

	[ConVar( "net_interp_time", ConVarFlags.Protected, Help = "Interpolation time in seconds" )]
	internal static float InterpolationTime { get; set; } = 0.1f;

	[ConVar( "net_fakepacketloss", ConVarFlags.Protected | ConVarFlags.Cheat, Help = "Simulate packet loss in %" )]
	internal static int FakePacketLoss { get; set; } = 0;

	[ConVar( "net_fakelag", ConVarFlags.Protected | ConVarFlags.Cheat, Help = "Simulate latency in ms" )]
	internal static int FakeLag { get; set; } = 0;

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

		utils.InitializeRelayNetwork();

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

	private static CancellationTokenSource createLobbyCts;

	/// <summary>
	/// Will create a new lobby with the specified <see cref="LobbyConfig"/> to
	/// customize the lobby further.
	/// </summary>
	public static void CreateLobby( LobbyConfig config )
	{
		// Let's not make a lobby if we're in the editor and we're not playing a game.
		// Editor tools could call this, and we don't want a lingering lobby.
		if ( Application.IsEditor && !Game.IsPlaying )
			throw new UnauthorizedAccessException( "Unable to create a lobby outside of a game" );

		if ( IsActive )
			return;

		createLobbyCts?.Cancel();
		createLobbyCts = new();

		//
		// Did the menu want to override the lobby's max players?
		//
		if ( LaunchArguments.MaxPlayers > 1 )
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

		_ = CreateLobbyAsync( config, createLobbyCts );
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

	static async Task<bool> CreateDedicatedServer( LobbyConfig config, CancellationTokenSource cts = null )
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

			return !(cts?.IsCancellationRequested ?? false);
		}
	}

	static async Task<bool> CreateLobbyAsync( LobbyConfig config, CancellationTokenSource cts = null )
	{
		if ( IsActive )
			return false;

		if ( Application.IsEditor )
		{
			config.Privacy = EditorLobbyPrivacy;
		}

		if ( Application.IsDedicatedServer )
		{
			return await CreateDedicatedServer( config, cts );
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

		if ( cts?.IsCancellationRequested ?? false )
			return false;

		var socket = await SteamLobbySocket.Create( config );
		if ( socket is null )
		{
			if ( cts?.IsCancellationRequested ?? false )
				return false;

			Disconnect();
			return false;
		}

		if ( cts?.IsCancellationRequested ?? false )
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
		if ( System is null ) return;

		lock ( NetworkThreadLock )
		{
			// Send any remaining messages
			System.ProcessMessagesInThread();

			SentrySdk.AddBreadcrumb( $"Disconnected from {System}", "network.disconnect" );

			System.Disconnect();
			System = null;

			createLobbyCts?.Cancel();
			createLobbyCts = null;

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
		Disconnect();
		_ = TryConnect( target );
	}

	internal static async Task<bool> TryConnect( string target, int retries = 30 )
	{
		if ( string.IsNullOrWhiteSpace( target ) )
		{
			Log.Warning( "Couldn't connect - target is null!" );
			return false;
		}

		SentrySdk.AddBreadcrumb( $"Connect to '{target}'", "network.connect" );
		Assert.IsNull( System );

		//
		// SteamID
		//
		if ( ulong.TryParse( target, out var steamId ) )
		{
			return await TryConnectSteamId( steamId );
		}

		var count = 0;

		while ( count < retries )
		{
			lock ( NetworkThreadLock )
			{
				if ( target == "local" )
				{
					Assert.NotNull( Engine.IGameInstanceDll.Current );
					Assert.NotNull( Engine.IGameInstanceDll.Current.TypeLibrary );

					Log.Info( $"Connecting to local client.." );

					System = new( "localclient", Engine.IGameInstanceDll.Current.TypeLibrary );
					System.Connect( new TcpChannel( "127.0.0.1", 55333 ) );
					System.UpdateLoading( "Connecting" );

					LastConnectionString = target;
				}
				else
				{
					Log.Info( $"Connecting to {target}.." );
					System = new( "client", Engine.IGameInstanceDll.Current.TypeLibrary );
					System.Connect( new SteamNetwork.IpConnection( target ) );
					System.UpdateLoading( "Connecting" );

					LastConnectionString = target;
				}
			}

			var success = await AwaitSuccessfulConnection();
			if ( success ) return true;

			if ( System is null )
				return false;

			Log.Info( $"Couldn't connect, trying again ({count} out of {retries})" );
			count++;

			Disconnect();
		}

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

	/// <summary>
	/// Will try to connect to a server. Will return false if failed to connect.
	/// </summary>
	public static async Task<bool> TryConnectSteamId( SteamId steamId )
	{
		Disconnect();

		if ( steamId.AccountType == SteamId.AccountTypes.Lobby )
		{
			return await JoinSteamLobbyServer( steamId );
		}

		// Don't load no weird maps
		LaunchArguments.Reset();

		lock ( NetworkThreadLock )
		{
			System = new( "steamclient", Engine.IGameInstanceDll.Current.TypeLibrary );
			System.Connect( new SteamNetwork.IdConnection( steamId, 77 ) );
			System.UpdateLoading( "Connecting" );
		}

		LastConnectionString = $"{steamId}";

		var success = await AwaitSuccessfulConnection();
		if ( success ) return true;

		Disconnect();
		return false;
	}

	static async Task<bool> JoinSteamLobbyServer( ulong steamid )
	{
		LoadingScreen.IsVisible = true;
		LoadingScreen.Title = "Connecting";

		var lobbySocket = await SteamLobbySocket.Join( steamid );
		if ( lobbySocket is null )
		{
			LoadingScreen.IsVisible = false;

			// Try another one?
			return false;
		}

		Log.Trace( $"Joined Lobby {steamid}" );
		LoadingScreen.Title = "Connected";

		if ( System is not null )
		{
			LoadingScreen.IsVisible = false;
			Log.Warning( "Network is already active - leaving lobby" );
			lobbySocket?.Dispose();

			return false;
		}

		// Don't load no weird maps
		LaunchArguments.Reset();

		// This lobby should tell us what to do
		lock ( NetworkThreadLock )
		{
			System = new( "lobbyclient", Engine.IGameInstanceDll.Current.TypeLibrary );
			System.AddSocket( lobbySocket );

			LastConnectionString = $"{steamid}";
		}

		var success = await AwaitSuccessfulConnection();
		if ( success ) return true;

		Disconnect();
		return false;
	}
}
