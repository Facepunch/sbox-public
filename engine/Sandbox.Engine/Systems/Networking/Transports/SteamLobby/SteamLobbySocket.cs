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
/// A fake socket that wraps around a Steam lobby.
/// </summary>
internal class SteamLobbySocket : NetworkSocket, ILobby
{
	public ConcurrentDictionary<ulong, SteamLobbyConnection> Connections { get; } = new();

	internal int NetworkChannel => (int)(Id % int.MaxValue);

	/// <summary>
	/// The time until we should try to find a new host.
	/// </summary>
	TimeUntil nextTryFindHost;

	Connection hostConnection;
	ulong Id => SteamLobby.Id;
	bool wasHost;

	/// <summary>
	/// The SteamId of the host of this lobby.
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

	public SteamLobbySocket( Lobby lobby )
	{
		SteamLobby = lobby;

		UpdateConnections();
		UpdateOwnerFromLobby();

		hostConnection = Connections.Values.FirstOrDefault( x => x.IsHost );
		wasHost = Owner.IsMe;

		LobbyManager.Register( this );
		((ILobby)this).OnLobbyUpdated();
	}

	private void LoadConfig( LobbyConfig config )
	{
		this.config = config;

		SteamLobby.SetData( "destroy_when_host_leaves", config.DestroyWhenHostLeaves.ToString() );
		SteamLobby.SetData( "auto_switch_host", config.AutoSwitchToBestHost.ToString() );

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

	private void UpdateConfig()
	{
		if ( bool.TryParse( SteamLobby.GetData( "destroy_when_host_leaves" ), out var value ) )
		{
			config.DestroyWhenHostLeaves = value;
		}

		if ( bool.TryParse( SteamLobby.GetData( "auto_switch_host" ), out value ) )
		{
			config.AutoSwitchToBestHost = value;
		}
	}

	public static async Task<SteamLobbySocket> Create( LobbyConfig config )
	{
		var steamlobby = await SteamMatchmaking.CreateLobbyAsync( config.MaxPlayers );

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

		switch ( config.Privacy )
		{
			case LobbyPrivacy.Public:
				steamlobby.Value.SetPublic();
				break;
			case LobbyPrivacy.Private:
				steamlobby.Value.SetPrivate();
				break;
			case LobbyPrivacy.FriendsOnly:
				steamlobby.Value.SetFriendsOnly();
				break;
		}

		return lobby;
	}

	public static async Task<SteamLobbySocket> Join( ulong lobbyId )
	{
		var steamlobby = await SteamMatchmaking.JoinLobbyAsync( lobbyId );

		if ( !steamlobby.HasValue )
		{
			Log.Warning( "Error joining lobby!" );
			return null;
		}

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
		var timeout = Stopwatch.StartNew();

		// Wait 2000ms for a host or time out
		while ( timeout.Elapsed.TotalSeconds < 5f )
		{
			SteamNetwork.RunCallbacks();

			if ( lobby.hostConnection is not null )
				return lobby;

			await Task.Delay( 100 );
		}

		Log.Warning( $"Timed out connecting to lobby {steamlobby.Value}" );
		return null;
	}

	internal override void Initialize( NetworkSystem networkSystem )
	{
		foreach ( var c in Connections.Values )
		{
			OnClientConnect?.Invoke( c );
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

	private void ChangeLobbyHost( HostCandidate candidate )
	{
		SteamLobby.SetData( "_ownerid", $"{candidate.Friend.Id}" );
		SteamLobby.SetOwner( candidate.Friend.Id );
		Owner = new( candidate.Friend.Id );
	}

	internal override void Dispose()
	{
		// If we're the current owner of the lobby, we should try to find another
		// candidate to pass ownership to.
		if ( Owner.IsMe )
		{
			if ( config.DestroyWhenHostLeaves )
			{
				SteamLobby.SetData( "disbanded", "1" );
			}
			else if ( TryFindBestHost( out var candidate ) )
			{
				Log.Info( $"Disconnected - New Host: {candidate.Friend.Name}" );
				ChangeLobbyHost( candidate );
			}
		}

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
		var ptr = stackalloc IntPtr[Networking.MaxIncomingMessages];

		while ( true )
		{
			var count = net.ReceiveMessagesOnChannel( channel, (IntPtr)ptr, Networking.MaxIncomingMessages );
			if ( count == 0 ) return;

			for ( var i = 0; i < count; i++ )
			{
				var msg = Unsafe.Read<SteamNetworkMessage>( (void*)ptr[i] );

				var data = new byte[msg.Size];
				Marshal.Copy( (IntPtr)msg.Data, data, 0, data.Length );

				var m = new IncomingMessage
				{
					SteamId = msg.IdentitySteamId,
					Data = data
				};

				IncomingMessages.Writer.TryWrite( m );
				net.ReleaseMessage( ptr[i] );
			}
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

		while ( OutgoingMessages.Reader.TryRead( out var msg ) )
		{
			ProcessOutgoingMessage( net, msg );
		}

		ProcessIncomingMessages( net, NetworkChannel );
	}

	internal override void GetIncomingMessages( NetworkSystem.MessageHandler handler )
	{
		while ( IncomingMessages.Reader.TryRead( out var msg ) )
		{
			if ( !Connections.TryGetValue( msg.SteamId, out var connection ) )
				continue;

			Span<byte> data = Networking.DecodeStream( msg.Data );

			using var stream = ByteStream.CreateReader( data );

			var nwm = new NetworkSystem.NetworkMessage
			{
				Data = stream,
				Source = connection
			};

			connection.MessagesRecieved++;
			handler( nwm );
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

	struct HostCandidate
	{
		public Friend Friend { get; set; }
		public float AveragePing { get; set; }
		public float AverageQuality { get; set; }
		public double ScoreDelta { get; set; }
		public double Score { get; set; }
	}

	private bool TryFindBestHost( out HostCandidate candidate )
	{
		double maxScore = double.MinValue;
		HostCandidate? bestOption = null;

		const double pingWeight = 0.6f;
		const double qualityWeight = 0.4f;

		var candidates = new List<HostCandidate>();
		var ourScore = 0d;

		foreach ( var member in SteamLobby.Members )
		{
			if ( !IsValidConnectionState( member.Id ) )
				continue;

			if ( !float.TryParse( SteamLobby.GetMemberData( member, "average_peer_ping" ), out var averagePing ) )
				continue;

			if ( !float.TryParse( SteamLobby.GetMemberData( member, "average_peer_quality" ), out var averageQuality ) )
				continue;

			if ( averageQuality < 0f )
			{
				// If our average quality is less than zero, then it probably hasn't
				// stabilized yet. Assume that it's good for now.
				averageQuality = 1f;
			}

			candidates.Add( new()
			{
				Friend = new( member ),
				AveragePing = averagePing,
				AverageQuality = averageQuality
			} );
		}

		if ( candidates.Count > 0 )
		{
			var minPing = 0f;
			var maxPing = 500f;

			foreach ( var c in candidates )
			{
				float normalizedPing;

				if ( minPing == maxPing )
					normalizedPing = 1f;
				else
					normalizedPing = 1.0f - (c.AveragePing - minPing) / (maxPing - minPing);

				var score = (pingWeight * normalizedPing) + (qualityWeight * c.AverageQuality);
				score = score.Clamp( 0f, 1f );

				if ( c.Friend.IsMe )
				{
					ourScore = score;
					continue;
				}

				if ( score > maxScore )
				{
					bestOption = c with { Score = score };
					maxScore = score;
				}
			}
		}

		if ( bestOption.HasValue )
		{
			var c = bestOption.Value;
			c.ScoreDelta = c.Score - ourScore;
			candidate = c;

			return true;
		}

		candidate = default;
		return false;
	}

	private bool IsValidConnectionState( ulong steamId )
	{
		var state = SteamLobby.GetMemberData( new( steamId ), "connection_state" );

		if ( int.TryParse( state, out var result ) )
		{
			return (Connection.ChannelState)result == Connection.ChannelState.Connected;
		}

		return false;
	}

	private void UpdateAveragePeerQuality()
	{
		var totalPing = 0f;
		var totalQuality = 0f;
		var validConnections = Connections
			.Where( c => IsValidConnectionState( c.Key ) )
			.Select( c => c.Value );

		var numberOfConnections = validConnections.Count();

		foreach ( var c in validConnections )
		{
			var stats = c.Stats;
			totalPing += stats.Ping;
			totalQuality += stats.ConnectionQuality;
		}

		if ( numberOfConnections > 0 )
		{
			var averagePing = totalPing / numberOfConnections;
			var averageQuality = totalQuality / numberOfConnections;

			SteamLobby.SetMemberData( "average_peer_ping", averagePing.ToString() );
			SteamLobby.SetMemberData( "average_peer_quality", averageQuality.ToString() );
		}

		SteamLobby.SetMemberData( "connection_state", ((int)Connection.Local.State).ToString() );
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
		UpdateAveragePeerQuality();

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

		if ( config.AutoSwitchToBestHost && nextTryFindHost )
		{
			// Conna: don't auto-switch to best host if we should destroy when the host leaves.
			if ( config.DestroyWhenHostLeaves )
				return;

			if ( !Networking.IsHostBusy )
				return;

			if ( Application.IsEditor )
				return;

			if ( TryFindBestHost( out var candidate ) && candidate.ScoreDelta > 0.02f )
			{
				ChangeLobbyHost( candidate );
				nextTryFindHost = 5f;
			}
		}
	}

	internal override void OnSessionFailed( SteamId steamId )
	{
		/*
		IGameInstanceDll.Current.Disconnect();
		IMenuSystem.ShowServerError( "Disconnected", "Invalid Session" );
		Log.Warning( $"Disconnecting - Invalid Session" );
		*/

		if ( Connections.TryGetValue( steamId, out var connection ) )
		{
			Log.Warning( $"SteamLobbySocket - Invalid Network Session (Recipient: {connection.DisplayName})" );
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

		Connection targetConnection = Connections.Values.FirstOrDefault( x => x.IsHost );
		UpdateConfig();

		if ( Owner.IsMe )
		{
			targetConnection = Connection.Local;

			if ( !wasHost )
			{
				if ( !config.DestroyWhenHostLeaves )
				{
					// The lobby should keep a count of how many times it changed hosts.
					var hostCount = (SteamLobby.GetData( "hostcount" )?.ToInt() ?? 0) + 1;
					SteamLobby.SetData( "hostcount", hostCount.ToString() );

					//
					// If we're still connecting, try and find another host candidate. If we can't
					// find one, then mark the lobby as toxic.
					//
					if ( Connection.Local.IsConnecting )
					{
						if ( TryFindBestHost( out var candidate ) )
						{
							Log.Info( $"We were made the host, but we're still connecting. We found a better candidate: {candidate.Friend.Name}" );
							ChangeLobbyHost( candidate );
							return;
						}

						// Marking it as toxic tells everyone else to disconnect and to avoid it.
						SteamLobby.SetData( "toxic", "1" );
					}
				}
				else
				{
					SteamLobby.SetData( "disbanded", "1" );
				}

				nextTryFindHost = 5f;
				wasHost = true;
			}
		}
		else
		{
			wasHost = false;
		}

		//
		// If the lobby is toxic, we need to disconnect from it. It will usually be toxic if someone
		// became the owner while they were connecting and no other suitable host candidate could be
		// found.
		//
		if ( SteamLobby.GetData( "toxic" ) == "1" )
		{
			IGameInstanceDll.Current.Disconnect();
			IMenuSystem.ShowServerError( "Disconnected", "Inoperable Server State" );
			Log.Warning( "Disconnecting - Inoperable Server State" );
			return;
		}

		if ( SteamLobby.GetData( "disbanded" ) == "1" )
		{
			IGameInstanceDll.Current.Disconnect();
			IMenuSystem.ShowServerError( "Disconnected", "Lobby Disbanded" );
			Log.Warning( "Disconnecting - Lobby Disbanded" );
			return;
		}

		// Conna: let's not call host changed if we have no target connection.
		if ( targetConnection is null )
			return;

		if ( hostConnection == targetConnection )
			return;

		var previousHost = hostConnection;
		hostConnection = targetConnection;

		OnHostChanged?.Invoke( new( previousHost, hostConnection ) );

		if ( Connection.Local.State == Connection.ChannelState.Connected )
			return;

		if ( Owner.IsMe )
			return;

		Log.Info( "Restarting Handshake" );

		// Restart the handshake process if the host changed while we're
		// still connecting.
		Networking.System?.RestartHandshake();
	}

	void ILobby.OnMemberMessage( Friend friend, ByteStream stream )
	{
		throw new NotImplementedException();
	}
}
