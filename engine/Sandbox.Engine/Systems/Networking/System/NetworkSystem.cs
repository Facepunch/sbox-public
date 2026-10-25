using Sandbox.Internal;

namespace Sandbox.Network;

/// <summary>
/// A network system is a bunch of connections that people can send messages 
/// over. Right now it can be a dedicated server, a listen server, a pure client,
/// or a p2p system.
/// </summary>
internal partial class NetworkSystem
{
	readonly Logger log;

	/// <summary>
	/// Are we the owner of this network system? True if we're hosting
	/// the server, or we're the current owner of a p2p system.
	/// </summary>
	public bool IsHost { get; private set; }

	/// <summary>
	/// Has this network system been disconnected?
	/// </summary>
	public bool IsDisconnected { get; private set; }

	/// <summary>The peer's actual failure, retained after teardown for the join UI.</summary>
	internal string FailureReason { get; set; }

	/// <summary>
	/// Are we currently disconnecting from networking?
	/// </summary>
	internal bool IsDisconnecting { get; set; }

	public LobbyConfig Config { get; internal init; }

	/// <summary>
	/// Who the host lets in. Only means anything while we're the host.
	/// </summary>
	internal JoinAccess Access { get; } = new();
	public ConnectionInfoManager ConnectionInfo { get; }
	public HostStats HostStats { get; private set; }
	public string DebugName { get; }

	public override string ToString() => DebugName;

	public NetworkSystem( string debugName, TypeLibrary library )
	{
		DebugName = debugName;
		TypeLibrary = library;
		IsDeveloperHost = Application.IsEditor;
		ConnectionInfo = new( this );

		log = new( $"NetworkSystem/{debugName}" );
		log.Trace( "Initialized" );

		InstallHandshakeMessages();
		InstallHostMigrationMessages();
		InstallJoinAccessMessages();

		AddHandler( InternalMessageType.TableSnapshot, TableMessage );
		AddHandler( InternalMessageType.TableUpdated, TableMessage );
		AddHandler<ServerCommand>( OnServerCommand );
		AddHandler<UserInfoUpdate>( OnUserInfoUpdate );
		AddHandler<HostStats>( OnReceiveHostStats );
		AddHandler<ReconnectMsg>( OnReconnectMsg );
		AddHandler<ServerDataMsg>( OnReceiveServerData );
		AddHandler<ServerNameMsg>( OnReceiveServerName );
		AddHandler<MapNameMsg>( OnReceiveMapName );
		AddHandler<LogMsg>( OnLogMsg );
	}

	/// <summary>
	/// We have received a log message from another client.
	/// </summary>
	void OnLogMsg( LogMsg msg, Connection source, Guid msgId )
	{
		// Only the host can put shit in our console.
		if ( !source.IsHost )
			return;

		switch ( (LogLevel)msg.Level )
		{
			case LogLevel.Error:
				Log.Error( msg.Message );
				break;
			case LogLevel.Warn:
				Log.Warning( msg.Message );
				break;
			case LogLevel.Info:
				Log.Info( msg.Message );
				break;
		}
	}

	/// <summary>
	/// We have received a UserInfo ConVar value update from a client.
	/// </summary>
	void OnUserInfoUpdate( UserInfoUpdate msg, Connection source, Guid msgId )
	{
		if ( !Networking.IsHost )
			return;

		var command = ConVarSystem.Find( msg.Command );
		if ( command is null ) return;
		if ( !command.IsUserInfo ) return;

		source.SetUserData( msg.Command, msg.Value );
	}

	/// <summary>
	/// We have received a console command from a client that should be run on the server.
	/// </summary>
	void OnServerCommand( ServerCommand msg, Connection source, Guid msgId )
	{
		// It's not meant for us if we're not the host.
		if ( !Networking.IsHost )
			return;

		var command = ConVarSystem.Find( msg.Command );
		if ( command is null ) return;
		if ( !command.IsConCommand ) return;
		if ( !command.IsServer && !command.IsAdmin ) return;
		if ( command.IsCheat && !Game.CheatsEnabled ) return;

		var oldCaller = Command.Caller;
		Command.Caller = source;

		try
		{
			command.Run( msg.Args );
		}
		finally
		{
			Command.Caller = oldCaller;
		}
	}

	/// <summary>
	/// We have received network / performance stats from the server.
	/// </summary>
	void OnReceiveHostStats( HostStats data, Connection source, Guid msgId )
	{
		// We should only receive host stats from the host, obviously.
		if ( !source.IsHost )
			return;

		HostStats = data;
	}

	/// <summary>
	/// We have received a changed server name.
	/// </summary>
	void OnReceiveServerName( ServerNameMsg data, Connection source, Guid msgId )
	{
		if ( !source.IsHost )
		{
			Log.Warning( "Got ServerNameMsg - but not from host!" );
			return;
		}

		Networking.ServerName = data.Name;
	}

	/// <summary>
	/// We have received a changed map name.
	/// </summary>
	void OnReceiveMapName( MapNameMsg data, Connection source, Guid msgId )
	{
		if ( !source.IsHost )
		{
			Log.Warning( "Got MapNameMsg - but not from host!" );
			return;
		}

		Networking.MapName = data.Name;
	}

	/// <summary>
	/// We have received changed data from the server.
	/// </summary>
	void OnReceiveServerData( ServerDataMsg data, Connection source, Guid msgId )
	{
		if ( !source.IsHost )
		{
			Log.Warning( "Got ServerDataMsg - but not from host!" );
			return;
		}

		Networking.SetData( data.Name, data.Value );
	}

	/// <summary>
	/// The server has told us to reconnect
	/// </summary>
	void OnReconnectMsg( ReconnectMsg data, Connection source, Guid msgId )
	{
		if ( !source.IsHost )
		{
			Log.Warning( "Got ReconnectMsg - but not from host!" );
			return;
		}

		_ = Networking.ClientReconnect( data );
	}

	/// <summary>
	/// A message for several peers. The host passes it on to the others it's for, encoded once.
	/// </summary>
	void OnMulticastMessage( ByteStream data, Connection source, int depth )
	{
		var senderId = data.Read<Guid>();
		var flags = (NetFlags)data.Read<byte>();
		var targets = data.ReadArraySpan<Guid>( 4096 );
		var message = data.GetRemainingBytes();

		// Only trusted from the host or if the sender is saying he's the sender
		if ( senderId != source.Id && !source.IsHost )
		{
			Log.Warning( $"Connection {source.Id} tried to send a Multicast with invalid SenderId {senderId}" );
			source.Kick( "Invalid Multicast sender" );

			return;
		}

		if ( IsHost )
		{
			NetPacket encoded = null;
			byte[][] packets = null;
			_multicastTargets.Clear();

			foreach ( var id in targets )
			{
				// Each peer once, however often the sender listed it
				if ( id == Connection.Local.Id || id == senderId || !_multicastTargets.Add( id ) )
					continue;

				var target = FindConnection( id );
				if ( target is null )
					continue;

				// Passed on without the targets, the host only sends it to who it's for
				encoded ??= EncodeMulticast( senderId, flags, [], message );
				target.Send( encoded, flags, ref packets );
			}

			encoded?.Release();
		}

		var forUs = targets.IndexOf( Connection.Local.Id ) >= 0 || (targets.Length == 0 && source.IsHost);

		if ( forUs )
		{
			var senderConnection = Connection.Find( senderId ) ?? source;
			DispatchNested( message, senderConnection, depth );
		}
	}

	readonly HashSet<Guid> _multicastTargets = new();

	internal static NetPacket EncodeMulticast( Guid senderId, NetFlags flags, ReadOnlySpan<Guid> targets, ReadOnlySpan<byte> message )
	{
		var bs = ByteStream.Create( message.Length + 32 + targets.Length * 16 );
		bs.Write( InternalMessageType.Multicast );
		bs.Write( senderId );
		bs.Write( (byte)flags );
		bs.WriteArray( targets );
		bs.Write( message );

		var encoded = Connection.EncodePacket( bs );
		bs.Dispose();
		return encoded;
	}

	RealTimeSince timeSinceTick = 100f;
	RealTimeSince timeSinceHeartbeat = 100f;
	RealTimeSince timeSinceSentStats = 100f;

	/// <summary>
	/// Called to read and process incoming messages.
	/// </summary>
	public void Tick()
	{
		HandleIncomingMessages();

		GameSystem?.TickInternal();

		TickHostMigration();

		if ( timeSinceTick >= 1f )
		{
			timeSinceTick = 0f;

			foreach ( var socket in sockets )
			{
				socket.Tick( this );
			}

			foreach ( var connect in _connections )
			{
				connect.Tick( this );
			}

			Connection?.Tick( this );

			ResendPendingInvites();
		}
	}

	public void SendHeartbeat()
	{
		if ( !IsHost ) return;
		if ( timeSinceHeartbeat < HeartbeatInterval ) return;

		timeSinceHeartbeat = 0f;

		var bs = ByteStream.Create( 32 );
		bs.Write( InternalMessageType.HeartbeatPing );
		bs.Write( RealTime.Now ); // Real time
		bs.Write( Time.NowDouble ); // Game time
		var packet = Connection.EncodePacket( bs );
		bs.Dispose();

		foreach ( var c in GetFilteredConnections( Connection.ChannelState.Welcome ) )
			c.Send( packet, NetFlags.Reliable | NetFlags.SendImmediate );

		packet.Release();
	}

	// Measures ping and corrects clients' game time, which they advance themselves in between
	const float HeartbeatInterval = 1.0f;

	public void SendHostStats()
	{
		if ( !IsHost ) return;
		if ( timeSinceSentStats < 1.0f ) return;

		timeSinceSentStats = 0;

		var totalBytesIn = 0f;
		var totalBytesOut = 0f;
		var connections = Connection.All.Where( c => c != Connection.Local ).ToArray();

		foreach ( var c in connections )
		{
			var s = c.Stats;
			totalBytesIn += s.InBytesPerSecond;
			totalBytesOut += s.OutBytesPerSecond;
		}

		var stats = new HostStats
		{
			InBytesPerSecond = totalBytesIn,
			OutBytesPerSecond = totalBytesOut,
			Fps = (ushort)(1f / Time.Delta).CeilToInt()
		};

		var bs = ByteStream.Create( 32 );

		bs.Write( InternalMessageType.Packed );
		TypeLibrary.ToBytes( stats, ref bs );

		Broadcast( bs, Connection.ChannelState.Welcome );

		bs.Dispose();
	}

	public void Disconnect()
	{
		if ( IsDisconnected )
		{
			Log.Warning( "Tried to disconnect an already disconnected NetworkSystem!" );
			return;
		}

		Connection.Local.State = Connection.ChannelState.Unconnected;
		Sandbox.Diagnostics.DevTools.NetworkLog.Session( "Ended: left", Connection.Local, ends: true );

		log.Trace( "Disconnect" );
		IsDisconnected = true;

		GameSystem?.Dispose();
		GameSystem = null;

		Connection?.Close( 0, "Disconnect" );
		Connection = null;

		CloseSockets();

		foreach ( var c in _connections )
		{
			c.Close( 0, "Disconnect" );
		}

		_connectionLookup.Clear();
		_connections.Clear();
	}

	internal void OnSceneLoaded()
	{
		if ( !Networking.IsHost )
			return;

		// Conna: if we're a dedicated server, we don't "join" the game.
		if ( Application.IsDedicatedServer )
			return;

		// We already have a player in the scene we're taking over
		if ( _isBecomingHost )
			return;

		//
		// Trigger local join events, so that the new scene can do stuff like spawn a player prefab etc.
		//
		GameSystem?.OnJoined( Connection.Local );
	}
}

