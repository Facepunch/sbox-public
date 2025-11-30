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

	internal bool IsDisconnecting { get; set; }

	public LobbyConfig Config { get; internal init; }
	public ConnectionInfoManager ConnectionInfo { get; }
	public HostStats HostStats { get; private set; }
	public string DebugName { get; }

	/// <summary>
	/// Whether the host is busy right now. This can be used to determine if
	/// the host can be changed.
	/// </summary>
	internal bool IsHostBusy
	{
		get
		{
			if ( IsHandshaking() )
				return false;

			return GameSystem?.IsHostBusy ?? true;
		}
	}

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

		AddHandler( InternalMessageType.TableSnapshot, TableMessage );
		AddHandler( InternalMessageType.TableUpdated, TableMessage );
		AddHandler<TargetedInternalMessage>( OnTargetedInternalMessage );
		AddHandler<TargetedMessage>( OnTargetedMessage );
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

		Networking.StartReconnecting( data );
	}

	/// <summary>
	/// We have received a message intended for a different connection.
	/// </summary>
	void OnTargetedInternalMessage( TargetedInternalMessage data, Connection source, Guid msgId )
	{
		// A targeted message is only trusted from the host or if the sender is saying he's the sender
		if ( data.SenderId != source.Id && !source.IsHost )
		{
			Log.Warning( $"Connection {source.Id} tried to send a TargetedMessage with invalid SenderId {data.SenderId}" );
			source.Kick( "Invalid TargetedMessage.SenderId" ); // If we're the host, kick them

			return;
		}

		// This targeted message is intended for us!
		if ( data.TargetId == Guid.Empty || data.TargetId == Connection.Local.Id )
		{
			var senderConnection = Connection.Find( data.SenderId );
			senderConnection ??= source;

			var msg = new NetworkMessage
			{
				Source = senderConnection,
				Data = ByteStream.CreateReader( data.Data )
			};

			try
			{
				HandleIncomingMessage( msg );
			}
			catch ( Exception e )
			{
				Log.Warning( e );
			}

			msg.Data.Dispose();
		}
		else
		{
			// It's not for us, let's have a look to see if we have a connection with this id and forward it to them.
			var target = FindConnection( data.TargetId );
			target?.SendMessage( data, (NetFlags)data.Flags );
		}
	}

	/// <summary>
	/// We have received a message intended for a different connection.
	/// </summary>
	void OnTargetedMessage( TargetedMessage data, Connection source, Guid msgId )
	{
		// A targeted message is only trusted from the host or if the sender is saying he's the sender
		if ( data.SenderId != source.Id && !source.IsHost )
		{
			Log.Warning( $"Connection {source.Id} tried to send a TargetedMessage with invalid SenderId {data.SenderId}" );
			source.Kick( "Invalid TargetedMessage.SenderId" ); // If we're the host, kick them

			return;
		}

		// This targeted message is intended for us!
		if ( data.TargetId == Guid.Empty || data.TargetId == Connection.Local.Id )
		{
			var senderConnection = Connection.Find( data.SenderId );
			senderConnection ??= source;

			object messageData = data.Message;

			if ( messageData is byte[] arr )
			{
				var stream = ByteStream.CreateReader( arr );
				var type = stream.Read<InternalMessageType>();

				if ( type == InternalMessageType.Packed )
					messageData = TypeLibrary.FromBytes<object>( ref stream );

				stream.Dispose();
			}

			if ( !typeMessageHandlers.TryGetValue( messageData.GetType(), out var h ) )
				return;

			try
			{
				// We wanna call the message handler for the contained type now, but with the sender's connection instead.
				h( messageData, senderConnection, msgId );
			}
			catch ( Exception e )
			{
				Log.Warning( e );
			}
		}
		else
		{
			// It's not for us, let's have a look to see if we have a connection with this id and forward it to them.
			var target = FindConnection( data.TargetId );
			target?.SendMessage( data, (NetFlags)data.Flags );
		}
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
		}
	}

	public void SendHeartbeat()
	{
		if ( !IsHost ) return;
		if ( timeSinceHeartbeat < 0.33f ) return;

		timeSinceHeartbeat = 0f;

		var targets = GetFilteredConnections( Connection.ChannelState.Welcome );
		foreach ( var c in targets )
		{
			var bs = ByteStream.Create( 32 );
			bs.Write( InternalMessageType.HeartbeatPing );
			bs.Write( RealTime.Now ); // Real time
			bs.Write( Time.Now ); // Game time
			c.SendRawMessage( bs );
			bs.Dispose();
		}
	}

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

		//
		// Trigger local join events, so that the new scene can do stuff like spawn a player prefab etc.
		//
		GameSystem?.OnJoined( Connection.Local );
	}
}

