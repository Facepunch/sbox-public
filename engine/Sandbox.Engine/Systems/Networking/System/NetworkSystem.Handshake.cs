using Sandbox.Engine;
using Sandbox.Internal;

namespace Sandbox.Network;

/// <summary>
/// A network system is a bunch of connections that people can send messages 
/// over. Right now it can be a dedicated server, a listen server, a pure client,
/// or a p2p system.
/// </summary>
internal partial class NetworkSystem
{
	void InstallHandshakeMessages()
	{
		AddHandler<ServerInfo>( On_Handshake_ServerInfo );
		AddHandler<UserInfo>( On_Handshake_ClientInfo );
		AddHandler<Welcome>( On_Handshake_Welcome );
		AddHandler<RequestInitialSnapshot>( On_Handshake_RequestSnapshot );
		AddHandler<InitialSnapshotResponse>( On_Handshake_Snapshot );
		AddHandler<MountedVPKsResponse>( On_Handshake_MountedVPKs );
		AddHandler<RequestMountedVPKs>( On_Handshake_RequestMountedVPKs );
		AddHandler<ClientReady>( On_Handshake_ClientReady );
		AddHandler<Activate>( On_Handshake_Activate );
		AddHandler<RestartHandshakeMsg>( On_Handshake_Restart );
		AddHandler<KickMsg>( On_Kick );
	}

	/// <summary>
	/// Server says hello to the client. It tells the client some basic information about itself.
	/// The client can determine here whether they still want to join or not.
	/// </summary>
	async Task On_Handshake_ServerInfo( ServerInfo msg, Connection source, Guid msgId )
	{
		if ( !source.IsHost )
			return;

		IsDeveloperHost = msg.IsDeveloperHost;

		source.UpdateFrom( msg.Host );
		source.State = Connection.ChannelState.LoadingServerInformation;

		Connection.Local = new LocalConnection( msg.Assigned.Id )
		{
			HandshakeId = msg.HandshakeId,
			State = Connection.ChannelState.LoadingServerInformation
		};

		//
		// Tell the server all about ourselves
		//
		var output = UserInfo.Local;

		if ( !source.OnReceiveServerInfo( ref output, msg ) )
		{
			IGameInstanceDll.Current.Disconnect();
			return;
		}

		log.Trace( $"Server Id is {source.Id}" );
		log.Trace( $"Map Name is {msg.MapName}" );
		log.Trace( $"Server Name is {msg.ServerName}" );
		log.Trace( $"Engine version is {msg.EngineVersion}" );
		log.Trace( $"Game Package is {msg.GamePackage}" );
		log.Trace( $"My Client ID is {Connection.Local.Id}" );

		// This is a bit of a mess, it needs a good cleaning up. If they have a menu package, then load it first.
		if ( !string.IsNullOrEmpty( msg.GamePackage ) )
		{
			UpdateLoading( $"Loading {msg.GamePackage}" );

			log.Trace( $"Loading menu package.. {msg.GamePackage}" );

			var flags = GameLoadingFlags.Remote | GameLoadingFlags.Reload;
			if ( IsDeveloperHost ) flags |= GameLoadingFlags.Developer;

			if ( !Application.IsStandalone )
			{
				LaunchArguments.Map = msg.MapPackage;
				await IGameInstanceDll.Current.LoadGamePackageAsync( msg.GamePackage, flags, default );
			}
		}
		else
		{
			log.Trace( $"No game package - must be a developer" );
		}

		if ( IGameInstanceDll.Current is not null )
		{
			// TypeLibrary was probably rebuilt, keep it up to date
			TypeLibrary = IGameInstanceDll.Current.TypeLibrary;
		}

		foreach ( var (k, v) in msg.ServerData )
		{
			Networking.SetData( k, v );
		}

		Networking.MaxPlayers = msg.MaxPlayers;
		Networking.ServerName = msg.ServerName;
		Networking.MapName = msg.MapName;

		InstallStringTables();
		log.Trace( $"Fetching Server Data.." );

		//
		// Tell me what I need
		//
		UpdateLoading( $"Fetching Server Data" );

		source.SendMessage( output with
		{
			HandshakeId = msg.HandshakeId
		} );
	}

	Task On_Handshake_ClientInfo( UserInfo msg, Connection source, Guid msgId )
	{
		if ( source.IsHost )
			return Task.CompletedTask;

		if ( msg.HandshakeId != source.HandshakeId )
			return Task.CompletedTask;

		if ( source.State != Connection.ChannelState.LoadingServerInformation )
		{
			source.Kick( $"Invalid Handshake State {source.State}" );
			Log.Info( $"Kicking {source.DisplayName} [{source.SteamId}] Invalid Handshake State {source.State}" );
			return Task.CompletedTask;
		}

		if ( !source.OnReceiveUserInfo( msg ) )
			return Task.CompletedTask;

		//
		// Lobbies and steam network connections are trusted, so we can take the display name and Steam Id from them,
		// we shouldn't trust any other type of connection... but local TCP we can let slide.
		//
		if ( source is SteamLobbyConnection slob )
		{
			var friend = new Friend( slob.Friend.Id );
			msg.SteamId = slob.Friend.Id;
			msg.DisplayName = friend.Name;
		}

		Log.Info( $"{msg.DisplayName} [{msg.SteamId}] is connecting" );

		var denialReason = "";

		source.PreInfo = new ConnectionInfo( null )
		{
			ConnectionId = source.Id,
			State = source.State
		};

		source.PreInfo.Update( msg );

		if ( GameSystem is not null && !GameSystem.AcceptConnection( source, ref denialReason ) )
		{
			Log.Info( $"Kicking {msg.DisplayName} [{msg.SteamId}] - {denialReason}" );
			source.Kick( denialReason );
			return Task.CompletedTask;
		}

		source.PreInfo = null;
		source.State = Connection.ChannelState.Welcome;

		//log.Info( $"Client Name is {data.DisplayName}" );
		//log.Info( $"Client SteamId is {data.SteamId}" );
		//log.Info( $"Client EngineVersion is {data.EngineVersion}" );

		var output = new Welcome();
		output.HandshakeId = msg.HandshakeId;

		foreach ( var table in tables )
		{
			table.SendSnapshot( source );
		}

		//
		// They're connected now dummy
		//
		msg.ConnectionTime = DateTime.UtcNow;

		//
		// Make their name unique
		//
		var displayName = msg.DisplayName;
		var index = 2;
		while ( ConnectionInfo.All.Values.Any( x => string.Equals( x.DisplayName, displayName, StringComparison.OrdinalIgnoreCase ) ) )
		{
			displayName = $"{msg.DisplayName} ({index})";
			index++;
		}
		msg.DisplayName = displayName;

		//
		// Add player info to the manager. This will get sent to all the other players, so this
		// player is part of the game now.
		//
		{
			AddConnection( source, msg );
		}

		//
		// Tell game this guy has connected. This happens after ConnectionInfo so that
		// everyone can look up their name etc.
		//
		GameSystem?.OnConnected( source );

		source.SendMessage( output );
		return Task.CompletedTask;
	}

	async Task On_Handshake_Welcome( Welcome msg, Connection source, Guid msgId )
	{
		if ( !source.IsHost )
			return;

		if ( Connection.Local is null )
			throw new UnauthorizedAccessException();

		if ( msg.HandshakeId != Connection.Local.HandshakeId )
			return;

		if ( Connection.Local.State != Connection.ChannelState.LoadingServerInformation )
			throw new UnauthorizedAccessException();

		Connection.Local.State = Connection.ChannelState.Welcome;

		log.Trace( $"Welcome!" );

		UpdateLoading( $"Loading Network Tables" );

		await IGameInstanceDll.Current?.LoadNetworkTables( this );

		UpdateLoading( $"Init Game System" );

		InitializeGameSystem();

		log.Trace( $"Game Network System: {GameSystem}" );

		//
		// Here would be a goodish place to send a bunch of CRC's of the loaded state, so
		// the server can compare and reject if we're loading assemblies wrong (cheater)
		//
		UpdateLoading( "Fetching Snapshot" );

		var output = new RequestMountedVPKs { HandshakeId = msg.HandshakeId };
		source.SendMessage( output );
	}

	Task On_Handshake_RequestMountedVPKs( RequestMountedVPKs msg, Connection source, Guid msgId )
	{
		if ( source.IsHost )
			return Task.CompletedTask;

		if ( msg.HandshakeId != source.HandshakeId )
			return Task.CompletedTask;

		if ( source.State != Connection.ChannelState.Welcome )
		{
			source.Kick( $"Invalid Handshake State {source.State}" );
			Log.Info( $"Kicking {source.DisplayName} [{source.SteamId}] Invalid Handshake State {source.State}" );
			return Task.CompletedTask;
		}

		var output = new MountedVPKsResponse { HandshakeId = msg.HandshakeId };

		GameSystem?.GetMountedVPKs( source, ref output );

		source.State = Connection.ChannelState.MountVPKs;
		source.SendMessage( output );

		return Task.CompletedTask;
	}

	async Task On_Handshake_MountedVPKs( MountedVPKsResponse msg, Connection source, Guid msgId )
	{
		if ( !source.IsHost )
			return;

		if ( Connection.Local is null )
			throw new UnauthorizedAccessException();

		if ( msg.HandshakeId != Connection.Local.HandshakeId )
			return;

		if ( Connection.Local.State != Connection.ChannelState.Welcome )
			throw new UnauthorizedAccessException();

		Connection.Local.State = Connection.ChannelState.MountVPKs;

		await GameSystem?.MountVPKs( source, msg );

		var userInfo = UserInfo.Local;
		var output = new RequestInitialSnapshot();

		output.HandshakeId = msg.HandshakeId;
		output.UserData = userInfo.UserData;

		source.SendMessage( output );
	}

	Task On_Handshake_RequestSnapshot( RequestInitialSnapshot msg, Connection source, Guid msgId )
	{
		if ( source.IsHost )
			return Task.CompletedTask;

		if ( msg.HandshakeId != source.HandshakeId )
			return Task.CompletedTask;

		if ( source.State != Connection.ChannelState.MountVPKs )
		{
			source.Kick( $"Invalid Handshake State {source.State}" );
			Log.Info( $"Kicking {source.DisplayName} [{source.SteamId}] Invalid Handshake State {source.State}" );
			return Task.CompletedTask;
		}

		if ( msg.UserData is not null )
		{
			source.UpdateUserData( msg.UserData );
		}

		source.State = Connection.ChannelState.Snapshot;

		log.Trace( $"[{this}] Requesting a snapshot" );

		var snapshot = new SnapshotMsg
		{
			GameObjectSystems = [],
			NetworkObjects = new( 64 )
		};

		GameSystem?.GetSnapshot( source, ref snapshot );

		var output = new InitialSnapshotResponse
		{
			HandshakeId = source.HandshakeId,
			Snapshot = snapshot
		};

		source.SendMessage( output );
		return Task.CompletedTask;
	}

	async Task On_Handshake_Snapshot( InitialSnapshotResponse msg, Connection source, Guid msgId )
	{
		NetworkDebugSystem.Current?.Record( NetworkDebugSystem.MessageType.Snapshot, msg );

		if ( !source.IsHost )
			return;

		if ( Connection.Local is null )
			throw new UnauthorizedAccessException();

		if ( msg.HandshakeId != Connection.Local.HandshakeId )
			return;

		if ( Connection.Local.State != Connection.ChannelState.MountVPKs )
			throw new UnauthorizedAccessException();

		Connection.Local.State = Connection.ChannelState.Snapshot;

		UpdateLoading( "Loading Snapshot" );
		Log.Trace( $"[{this}] Got a snapshot" );

		//
		// Spawn the scene, which could also lead to calling OnStart, OnEnable on components
		// which might create new network instances. So from this point we should be considered
		// live in the game.
		//
		if ( GameSystem is not null )
		{
			try
			{
				await GameSystem.SetSnapshotAsync( msg.Snapshot );
			}
			catch ( Exception e )
			{
				IGameInstanceDll.Current.Disconnect();
				IMenuSystem.ShowServerError( "Disconnected", "Error Deserializing Snapshot" );
				Log.Error( e );

				return;
			}
		}

		Log.Trace( $"[{this}] Finished loading snapshot" );

		var output = new ClientReady
		{
			HandshakeId = msg.HandshakeId
		};

		source.SendMessage( output );
	}

	Task On_Handshake_ClientReady( ClientReady msg, Connection source, Guid msgId )
	{
		if ( source.IsHost )
			return Task.CompletedTask;

		if ( msg.HandshakeId != source.HandshakeId )
			return Task.CompletedTask;

		Log.Trace( $"[{this}] Client is ready" );

		if ( source.State != Connection.ChannelState.Snapshot )
		{
			source.Kick( $"Invalid Handshake State {source.State}" );
			Log.Info( $"Kicking {source.DisplayName} [{source.SteamId}] Invalid Handshake State {source.State}" );
			return Task.CompletedTask;
		}

		source.State = Connection.ChannelState.Connected;

		//
		// Tell game this guy is fully active.
		//
		GameSystem?.OnJoined( source );

		var output = new Activate();
		output.HandshakeId = msg.HandshakeId;

		source.SendMessage( output );

		Log.Info( $"{source.DisplayName} [{source.SteamId}] is connected" );

		return Task.CompletedTask;
	}

	Task On_Handshake_Restart( RestartHandshakeMsg msg, Connection source, Guid msgId )
	{
		if ( source.IsHost )
			return Task.CompletedTask;

		StartHandshake( source );

		return Task.CompletedTask;
	}

	Task On_Kick( KickMsg msg, Connection source, Guid msgId )
	{
		if ( !source.IsHost )
		{
			// Conna: only the host can kick us.
			return Task.CompletedTask;
		}

		IGameInstanceDll.Current.Disconnect();
		IMenuSystem.ShowServerError( "Disconnected", msg.Reason );
		Log.Warning( $"Disconnecting - {msg.Reason}" );

		return Task.CompletedTask;
	}

	Task On_Handshake_Activate( Activate msg, Connection source, Guid msgId )
	{
		if ( !source.IsHost )
			return Task.CompletedTask;

		if ( Connection.Local is null )
			throw new UnauthorizedAccessException();

		if ( msg.HandshakeId != Connection.Local.HandshakeId )
			return Task.CompletedTask;

		Log.Trace( $"[{this}] I am spawning into the game!" );
		LoadingScreen.IsVisible = false;

		Connection.Local.State = Connection.ChannelState.Connected;
		source.State = Connection.ChannelState.Connected;

		return Task.CompletedTask;
	}
}
