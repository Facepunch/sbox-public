using System.Collections.Concurrent;
using NativeEngine;
using Sandbox.Engine;
using Sandbox.Internal;
using Steamworks;
using Steamworks.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace Sandbox.Network;

/// <summary>
/// Full mesh over a Steam lobby. The owner is only who a joiner handshakes with; afterwards it follows the host.
/// </summary>
internal class SteamLobbySocket : NetworkSocket, ILobby
{
	public ConcurrentDictionary<ulong, SteamLobbyConnection> Connections { get; } = new();

	internal int NetworkChannel => (int)(Id % int.MaxValue);

	/// <summary>
	/// The connection to the lobby owner. This is who we handshake with when joining.
	/// </summary>
	Connection ownerConnection;

	ulong Id => SteamLobby.Id;

	NetworkSystem System;

	/// <summary>
	/// The SteamId of the owner of this lobby.
	/// </summary>
	public ulong HostSteamId => Owner.Id;

	/// <summary>
	/// The SteamId of the host of this lobby.
	/// </summary>
	public ulong LobbySteamId => SteamLobby.Id;

	/// <summary>
	/// How many cunts are in this lobby.
	/// </summary>
	public int LobbyMemberCount => SteamLobby.MemberCount;

	/// <summary>
	/// Current owner of this lobby.
	/// </summary>
	internal Steamworks.Friend Owner;

	/// <summary>
	/// The underlying Steam lobby.
	/// </summary>
	internal Lobby SteamLobby;

	/// <summary>
	/// Current config of this lobby.
	/// </summary>
	LobbyConfig config;

	internal override bool SupportsHostMigration => true;

	public SteamLobbySocket( Lobby lobby )
	{
		SteamLobby = lobby;

		UpdateConnections();
		UpdateOwnerFromLobby();

		LobbyManager.Register( this );
		((ILobby)this).OnLobbyUpdated();
	}

	private void LoadConfig( LobbyConfig config )
	{
		this.config = config;

		SteamLobby.SetData( "destroy_when_host_leaves", config.DestroyWhenHostLeaves.ToString() );

		if ( !string.IsNullOrEmpty( config.Name ) )
		{
			Networking.UpdateServerName( config.Name );
			SteamLobby.SetData( "name", config.Name );
		}
		else
		{
			Networking.UpdateServerName( $"{Utility.Steam.PersonaName}" );
			SteamLobby.SetData( "auto_name", "1" );
			SteamLobby.SetData( "name", $"{Utility.Steam.PersonaName}" );
		}

		SteamLobby.SetData( "hdn", config.Hidden ? "1" : "0" );
	}

	public static async Task<SteamLobbySocket> Create( LobbyConfig config )
	{
		var lobbyType = config.Privacy switch
		{
			LobbyPrivacy.Public => LobbyType.Public,
			LobbyPrivacy.FriendsOnly => LobbyType.FriendsOnly,
			_ => LobbyType.Private
		};

		var steamlobby = await SteamMatchmaking.CreateLobbyAsync( lobbyType, config.MaxPlayers );
		if ( !steamlobby.HasValue )
		{
			Log.Warning( "An error occured when creating the lobby!" );
			return null;
		}

		// Wait for the lobby to exist
		for ( int i = 0; i < 200; i++ )
		{
			if ( LobbyManager.ActiveLobbies.Contains( steamlobby.Value.Id ) )
				break;

			await Task.Delay( 10 );
		}

		if ( !LobbyManager.ActiveLobbies.Contains( steamlobby.Value.Id ) )
		{
			Log.Warning( "Didn't enter lobby in a reasonable time!" );
			steamlobby.Value.Leave();
			return null;
		}

		var lobby = new SteamLobbySocket( steamlobby.Value );
		lobby.LoadConfig( config );

		foreach ( var (k, v) in Networking.ServerData )
		{
			steamlobby.Value.SetData( k, v );
		}

		Networking.MaxPlayers = config.MaxPlayers;

		steamlobby.Value.SetData( "lobby_type", "scene" );
		steamlobby.Value.SetData( "dev", Application.IsEditor ? "1" : "0" );
		steamlobby.Value.SetData( "game", Application.GameIdent );
		steamlobby.Value.SetData( "revision", $"{Application.GamePackage?.Revision?.VersionId}" );
		steamlobby.Value.SetData( "api", Protocol.Api.ToString() );
		steamlobby.Value.SetData( "protocol", Protocol.Network.ToString() );
		steamlobby.Value.SetData( "buildid", $"{Application.Version}" );
		steamlobby.Value.SetData( "access_level", $"{config.Privacy}" );

		return lobby;
	}

	public static async Task<(RoomEnter Response, SteamLobbySocket Socket)> Join( ulong lobbyId )
	{
		var result = await SteamMatchmaking.JoinLobbyAsync( lobbyId );
		if ( result.Response != RoomEnter.Success || result.Lobby is not { } lobby )
		{
			return (result.Response, null);
		}

		for ( int i = 0; i < 200; i++ )
		{
			if ( LobbyManager.ActiveLobbies.Contains( lobby.Id ) )
				break;

			await Task.Delay( 10 );
		}

		if ( !LobbyManager.ActiveLobbies.Contains( lobby.Id ) )
		{
			Log.Warning( "Didn't enter lobby in a reasonable time!" );
			lobby.Leave();
			return default;
		}

		var socket = new SteamLobbySocket( lobby );
		var timeout = Stopwatch.StartNew();

		// Wait for the owner to show up or time out
		while ( timeout.Elapsed.TotalSeconds < 5f )
		{
			SteamNetwork.RunCallbacks();

			if ( socket.ownerConnection is not null )
				return (result.Response, socket);

			await Task.Delay( 100 );
		}

		Log.Warning( $"Timed out connecting to lobby." );
		return default;
	}

	internal override void Initialize( NetworkSystem networkSystem )
	{
		System = networkSystem;

		foreach ( var c in Connections.Values )
		{
			OnClientConnect?.Invoke( c );
		}

		// Joining: the owner is who we handshake with
		if ( !networkSystem.IsHost && ownerConnection is not null )
		{
			networkSystem.SetHostConnection( ownerConnection );
		}
	}

	internal override void SetData( string key, string value )
	{
		SteamLobby.SetData( key, value );
	}

	internal override void SetServerName( string name )
	{
		SteamLobby.SetData( "auto_name", "0" );
		SteamLobby.SetData( "name", name );
	}

	internal override void SetMapName( string name )
	{
		SteamLobby.SetData( "map", name );
	}

	internal override void OnHostChanged( Connection newHost )
	{
		if ( !Owner.IsMe )
			return;

		if ( newHost == Connection.Local )
		{
			var hostCount = (SteamLobby.GetData( "hostcount" )?.ToInt() ?? 0) + 1;
			SteamLobby.SetData( "hostcount", hostCount.ToString() );
			return;
		}

		if ( newHost is SteamLobbyConnection lobbyConnection )
		{
			SetOwner( lobbyConnection.Friend.Id );
		}
	}

	private void SetOwner( ulong steamId )
	{
		SteamLobby.SetData( "_ownerid", $"{steamId}" );
		SteamLobby.SetOwner( steamId );
		Owner = new( steamId );
	}

	internal override void Dispose()
	{
		LobbyManager.Unregister( this );
		SteamLobby.Leave();
	}

	private struct IncomingMessage
	{
		public ulong SteamId { get; set; }
		public byte[] Data { get; set; }
	}

	private struct OutgoingMessage
	{
		public ulong SteamId { get; set; }
		public int Channel { get; set; }
		public byte[] Data { get; set; }
		public int Flags { get; set; }
	}

	private Channel<OutgoingMessage> OutgoingMessages { get; } = Channel.CreateUnbounded<OutgoingMessage>();
	private Channel<IncomingMessage> IncomingMessages { get; } = Channel.CreateUnbounded<IncomingMessage>();

	/// <summary>
	/// Enqueue a message to be sent to a user on a different thread.
	/// </summary>
	/// <param name="steamId"></param>
	/// <param name="data"></param>
	/// <param name="flags"></param>
	internal void SendMessage( ulong steamId, in byte[] data, int flags )
	{
		var message = new OutgoingMessage
		{
			Channel = NetworkChannel,
			SteamId = steamId,
			Data = data,
			Flags = flags
		};

		OutgoingMessages.Writer.TryWrite( message );
	}

	/// <summary>
	/// Process any incoming messages from Steam networking and enqueue them to be
	/// handled by the main thread.
	/// </summary>
	/// <param name="net"></param>
	/// <param name="channel"></param>
	private unsafe void ProcessIncomingMessages( in ISteamNetworkingMessages net, int channel )
	{
		var ptr = stackalloc IntPtr[Networking.ReceiveBatchSize];
		var maxIncoming = Networking.ReceiveBatchSizePerTick;
		var totalReceived = 0;

		while ( true )
		{
			var count = net.ReceiveMessagesOnChannel( channel, (IntPtr)ptr, Networking.ReceiveBatchSize );
			if ( count == 0 ) return;

			for ( var i = 0; i < count; i++ )
			{
				var msg = Unsafe.Read<SteamNetworkMessage>( (void*)ptr[i] );

				var data = GC.AllocateUninitializedArray<byte>( msg.Size );
				Marshal.Copy( (IntPtr)msg.Data, data, 0, data.Length );

				var m = new IncomingMessage
				{
					SteamId = msg.IdentitySteamId,
					Data = data
				};

				IncomingMessages.Writer.TryWrite( m );
				net.ReleaseMessage( ptr[i] );
			}

			totalReceived += count;

			if ( maxIncoming > 0 && totalReceived >= maxIncoming )
				return;
		}
	}

	/// <summary>
	/// Send any queued outgoing messages via Steam Networking API.
	/// </summary>
	private unsafe void ProcessOutgoingMessage( ISteamNetworkingMessages net, in OutgoingMessage msg )
	{
		fixed ( byte* d = msg.Data )
		{
			var result = net.SendMessageToUser( msg.SteamId, (IntPtr)d, msg.Data.Length, msg.Flags, NetworkChannel );
			if ( result == 1 )
			{
				if ( Connections.TryGetValue( msg.SteamId, out var target ) )
					target.MessagesSent++;

				return;
			}

			if ( !Networking.Debug ) return;
			Log.Warning( $"ISteamNetworkingMessages.SendMessageToUser Failed ({result})" );
		}
	}

	/// <summary>
	/// Send any queued outgoing messages and process any incoming messages to be queued for handling
	/// on the main thread.
	/// </summary>
	internal override void ProcessMessagesInThread()
	{
		var net = Steam.SteamNetworkingMessages();
		if ( !net.IsValid ) return;

		var maxOutgoing = Networking.MaxOutgoingMessagesPerTick;
		var outgoingCount = 0;

		while ( OutgoingMessages.Reader.TryRead( out var msg ) )
		{
			ProcessOutgoingMessage( net, msg );

			if ( maxOutgoing > 0 && ++outgoingCount >= maxOutgoing )
				break;
		}

		ProcessIncomingMessages( net, NetworkChannel );
	}

	internal override void GetIncomingMessages( NetworkSystem.MessageHandler handler )
	{
		while ( IncomingMessages.Reader.TryRead( out var msg ) )
		{
			if ( !Connections.TryGetValue( msg.SteamId, out var connection ) )
				continue;

			connection.OnRawPacketReceived( msg.Data, handler );
		}
	}

	void UpdateConnections()
	{
		if ( Id == 0 || !LobbyManager.ActiveLobbies.Contains( Id ) )
			return;

		UpdateOwnerFromLobby();

		var sw = Steam.SteamMatchmaking();
		var cc = sw.GetNumLobbyMembers( Id );
		var list = new List<ulong>();

		for ( var i = 0; i < cc; i++ )
		{
			var member = sw.GetLobbyMemberByIndex( Id, i );
			list.Add( member );
			AddConnection( member );
		}

		var toRemove = Connections.Values.Where( x => !list.Contains( x.Friend.Id ) ).ToArray();

		foreach ( var c in toRemove )
		{
			OnClientDisconnect?.Invoke( c );
			Connections.Remove( c.Friend.Id, out _ );
			c.Dispose();
		}
	}

	private void AddConnection( ulong v )
	{
		if ( Connections.ContainsKey( v ) )
			return;

		var friend = new Friend( (long)v );
		if ( friend.IsMe )
			return;

		Log.Trace( $"Lobby connection from {friend.Name} / {friend.Id}!" );

		var c = new SteamLobbyConnection( this, friend );
		Connections.TryAdd( v, c );

		OnClientConnect?.Invoke( c );
	}

	internal override void Tick( NetworkSystem networkSystem )
	{
		FollowHost();

		if ( !Owner.IsMe )
			return;

		// Should we automatically update the name of this lobby? Should be the case
		// if a lobby name was not provided during creation.
		if ( SteamLobby.GetData( "auto_name" ) == "1" )
		{
			Networking.UpdateServerName( $"{Utility.Steam.PersonaName}" );
			SteamLobby.SetData( "name", $"{Utility.Steam.PersonaName}" );
		}

		SteamLobby.SetData( "_ownerid", $"{Utility.Steam.SteamId}" );
		SteamLobby.SetData( "map", Networking.MapName );
	}

	/// <summary>
	/// Steam made us owner but we're not the host: pass ownership to the host.
	/// </summary>
	private void FollowHost()
	{
		if ( System is null || !Owner.IsMe || System.IsHost )
			return;

		if ( System.HostConnection is SteamLobbyConnection host )
		{
			SetOwner( host.Friend.Id );
		}
	}

	internal override void OnSessionFailed( SteamId steamId )
	{
		if ( Connections.TryGetValue( steamId, out var connection ) )
		{
			Log.Warning( $"SteamLobbySocket - Invalid Network Session (Recipient: {connection.Name})" );
			return;
		}

		Log.Warning( $"SteamLobbySocket - Invalid Network Session (Recipient: {steamId})" );
	}

	internal override void OnConnectionInfoUpdated( NetworkSystem networkSystem )
	{
		foreach ( var info in networkSystem.ConnectionInfo.All )
		{
			SetConnectionId( info.Value.SteamId, info.Value );
		}
	}

	/// <summary>
	/// Make sure this steamid has this connection id
	/// </summary>
	private void SetConnectionId( ulong steamId, ConnectionInfo info )
	{
		if ( Connections.TryGetValue( steamId, out var connection ) )
		{
			connection.UpdateFromInfo( info );
		}
	}

	void UpdateOwnerFromLobby()
	{
		Owner = SteamLobby.Owner;
		ownerConnection = Owner.IsMe ? Connection.Local : Connections.GetValueOrDefault( Owner.Id );
	}

	ulong ILobby.Id => SteamLobby.Id;

	void ILobby.OnMemberEnter( Friend friend )
	{
		Log.Trace( $"OnLobbyMemberEnter {friend}!" );
		UpdateConnections();
	}

	void ILobby.OnMemberLeave( Friend friend )
	{
		Log.Trace( $"OnLobbyMemberLeave {friend}!" );
		UpdateConnections();
	}

	void ILobby.OnMemberUpdated( Friend friend )
	{
		// nothing to do
	}

	void ILobby.OnLobbyUpdated()
	{
		UpdateOwnerFromLobby();

		if ( System is null )
			return;

		FollowHost();

		if ( System.IsHost )
			return;

		// Until we're in the game the owner is the host; if they change mid-connect we start over
		if ( Connection.Local.State == Connection.ChannelState.Connected )
			return;

		if ( ownerConnection is null || ownerConnection == Connection.Local )
			return;

		if ( System.HostConnection == ownerConnection )
			return;

		var hadHost = System.HostConnection is not null;
		System.SetHostConnection( ownerConnection );

		if ( !hadHost )
			return;

		Log.Info( "Lobby owner changed while connecting - restarting handshake" );
		System.RestartHandshake();
	}

	void ILobby.OnMemberMessage( Friend friend, ByteStream stream )
	{
		throw new NotImplementedException();
	}
}
