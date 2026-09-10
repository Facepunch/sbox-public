using Sandbox.Engine;

namespace Sandbox.Network;

/// <summary>
/// Host migration: a leaving host hands a snapshot to a successor, who re-syncs everyone else.
/// Nothing happens on a crash, nobody else has the authoritative state.
/// </summary>
internal partial class NetworkSystem
{
	/// <summary>
	/// The connection we treat as the host. Null when we are the host, or haven't found one yet.
	/// </summary>
	internal Connection HostConnection { get; private set; }

	bool _hostMigrationFromServerInfo;

	/// <summary>
	/// True if the host hands the game to another peer when it leaves.
	/// </summary>
	internal bool IsHostMigrationEnabled
	{
		get => IsHost ? CanHandoffHost() : _hostMigrationFromServerInfo;
		set => _hostMigrationFromServerInfo = value;
	}

	enum MigrationPhase
	{
		None,
		Announcing,
		HandingOff,
		HandedOff,
		BecomingHost,
		WaitingForHost,
		WaitingForLocalHost,
		ApplyingResync
	}

	MigrationPhase _migrationPhase;
	bool _isHandingOff => _migrationPhase is MigrationPhase.Announcing or MigrationPhase.HandingOff or MigrationPhase.HandedOff;
	bool _isBecomingHost => _migrationPhase == MigrationPhase.BecomingHost;

	// Leaving host
	Connection _handoffSuccessor;
	readonly HashSet<Guid> _pendingLeaveAcks = new();

	// Successor
	readonly HashSet<Guid> _pendingResyncs = new();

	// Everyone else
	Guid _pendingHostId;
	RealTimeSince _timeSincePendingHost;
	Connection _leavingHost;
	bool _hostLost;
	RealTimeSince _timeSinceHostLost;

	void InstallHostMigrationMessages()
	{
		AddHandler<HostLeavingMsg>( OnHostLeaving );
		AddHandler<HostLeavingAckMsg>( OnHostLeavingAck );
		AddHandler<HostHandoffMsg>( OnHostHandoff );
		AddHandler<HostHandoffAckMsg>( OnHostHandoffAck );
		AddHandler<HostResyncMsg>( OnHostResync );
		AddHandler<HostResyncDoneMsg>( OnHostResyncDone );
	}

	internal void SetHostConnection( Connection connection )
	{
		if ( !IsHost )
			HostConnection = connection;
	}

	bool CanHandoffHost()
	{
		if ( !IsHost || IsDisconnected ) return false;
		if ( Application.IsDedicatedServer ) return false;
		if ( Config.DestroyWhenHostLeaves ) return false;

		return sockets.All( s => s.SupportsHostMigration );
	}

	/// <summary>
	/// Earliest fully connected peer takes over.
	/// </summary>
	Connection PickSuccessor()
	{
		return _connections
			.Where( c => c.State == Connection.ChannelState.Connected && c.Id != Guid.Empty )
			.OrderBy( c => c.ConnectionTime )
			.ThenBy( c => c.Id )
			.FirstOrDefault();
	}

	/// <summary>
	/// We're leaving. Returns true if the caller should pump until <see cref="PumpHostHandoff"/> says everyone acknowledged.
	/// </summary>
	internal bool BeginHostHandoff()
	{
		if ( !IsHost || IsDisconnected || _isHandingOff )
			return false;

		var peers = GetFilteredConnections( Connection.ChannelState.Welcome ).ToArray();
		if ( peers.Length == 0 )
			return false;

		// What arrived so far belongs in the snapshot; anything later is dropped
		HandleIncomingMessages();
		_migrationPhase = MigrationPhase.Announcing;

		var successor = CanHandoffHost() && (GameSystem?.CanSnapshot ?? false) ? PickSuccessor() : null;
		_handoffSuccessor = successor;

		foreach ( var peer in peers )
		{
			_pendingLeaveAcks.Add( peer.Id );
		}

		// Same reliable channel, so everything queued before this arrives first
		Broadcast( new HostLeavingMsg { SuccessorId = successor?.Id ?? Guid.Empty }, Connection.ChannelState.Welcome, flags: NetFlags.Reliable | NetFlags.SendImmediate );

		return true;
	}

	void SendHostHandoff()
	{
		var successor = _handoffSuccessor;
		if ( successor is null )
		{
			_migrationPhase = MigrationPhase.HandedOff;
			return;
		}

		Log.Info( $"Handing off host to {successor}" );

		_migrationPhase = MigrationPhase.HandingOff;
		GameSystem.SendSnapshot( successor, snapshot => new HostHandoffMsg { Snapshot = snapshot }, handoff: true, flags: NetFlags.Reliable | NetFlags.SendImmediate );

		foreach ( var socket in sockets )
		{
			socket.OnHostChanged( successor );
		}
	}

	/// <summary>
	/// True once the successor has the snapshot and every peer has acknowledged.
	/// </summary>
	internal bool PumpHostHandoff()
	{
		HandleIncomingMessages();

		// A resync travels over a different connection from the announcement. Wait for every
		// peer to receive the announcement before the successor can send any resyncs.
		if ( _migrationPhase == MigrationPhase.Announcing && _pendingLeaveAcks.Count == 0 )
			SendHostHandoff();

		return _migrationPhase == MigrationPhase.HandedOff;
	}

	void OnHostHandoffAck( HostHandoffAckMsg msg, Connection source, Guid msgId )
	{
		if ( _migrationPhase == MigrationPhase.HandingOff && source == _handoffSuccessor )
			_migrationPhase = MigrationPhase.HandedOff;
	}

	void OnHostLeavingAck( HostLeavingAckMsg msg, Connection source, Guid msgId ) => _pendingLeaveAcks.Remove( source.Id );

	void OnHostLeaving( HostLeavingMsg msg, Connection source, Guid msgId )
	{
		if ( IsHost || !source.IsHost )
			return;

		source.SendMessage( new HostLeavingAckMsg(), NetFlags.Reliable | NetFlags.SendImmediate );

		if ( msg.SuccessorId == Guid.Empty )
		{
			LeaveBecauseHostLeft();
			return;
		}

		_hostLost = false;
		_leavingHost = source;

		// The handoff follows
		if ( msg.SuccessorId == Connection.Local.Id )
			return;

		ExpectNewHost( msg.SuccessorId );
	}

	void ExpectNewHost( Guid successorId )
	{
		_pendingHostId = successorId;
		_timeSincePendingHost = 0;
		var successor = FindConnection( successorId );

		if ( successor is null )
		{
			// Star topology, we only had a channel to the old host. Local instances can rejoin over loopback.
			if ( Application.IsJoinLocal )
			{
				// Keep this channel alive until our announcement ack has reached the old host.
				// Reconnecting now would cancel the TCP send queue and could rejoin the old listener.
				_migrationPhase = MigrationPhase.WaitingForLocalHost;
				return;
			}

			LeaveBecauseHostLeft();
			return;
		}

		Log.Info( $"Host is changing to {successor}" );

		_migrationPhase = MigrationPhase.WaitingForHost;
		SetHostConnection( successor );
	}

	async Task OnHostHandoff( HostHandoffMsg msg, Connection source, Guid msgId )
	{
		if ( IsHost || !source.IsHost )
			return;

		source.SendMessage( new HostHandoffAckMsg(), NetFlags.Reliable | NetFlags.SendImmediate );
		await BecomeHostAsync( source, msg.Snapshot );
	}

	/// <summary>
	/// Load the previous host's snapshot as the host, then re-sync everyone else.
	/// </summary>
	internal async Task BecomeHostAsync( Connection previousHost, SnapshotMsg snapshot )
	{
		if ( IsHost || _isBecomingHost )
			return;

		_migrationPhase = MigrationPhase.BecomingHost;
		_leavingHost = previousHost;

		Log.Info( $"Becoming the host (previous host was {previousHost})" );

		try
		{
			HostConnection = null;
			IsHost = true;
			_pendingHostId = Guid.Empty;
			Connection.Local.State = Connection.ChannelState.Connected;

			IGameInstanceDll.Current?.OnBecameHost();

			// Nobody gets live updates until they've loaded our snapshot
			foreach ( var c in _connections )
			{
				if ( c != previousHost && c.State == Connection.ChannelState.Connected )
					c.State = Connection.ChannelState.MountVPKs;
			}

			if ( GameSystem is not null )
			{
				await GameSystem.BecomeHostAsync( previousHost, snapshot );
			}

			if ( IsDisconnected )
				return;

			RemovePeer( previousHost );

			var peers = _connections.ToArray();
			foreach ( var c in peers )
			{
				if ( c.State < Connection.ChannelState.Welcome )
					StartHandshake( c );
			}

			if ( GameSystem is not null )
			{
				SnapshotCapture shared = null;
				foreach ( var connection in peers.Where( c => c.State >= Connection.ChannelState.Welcome ) )
				{
					shared = StartResync( connection, shared );
				}
			}

			foreach ( var socket in sockets )
			{
				socket.OnHostChanged( Connection.Local );
			}

			Networking.AddLocalListenSocket( this );
		}
		catch ( Exception e )
		{
			Log.Error( e, "Error becoming the new host" );
			// Never hand a partially applied snapshot to another successor.
			Networking.Disconnect( handoffHost: false );
			IGameInstanceDll.Current?.Disconnect( "Error loading the host handoff." );
		}
		finally
		{
			_migrationPhase = MigrationPhase.None;
			_leavingHost = null;
		}
	}

	SnapshotCapture StartResync( Connection connection, SnapshotCapture shared )
	{
		_pendingResyncs.Add( connection.Id );
		connection.State = Connection.ChannelState.Snapshot;
		var previousHostId = _leavingHost?.Id ?? Guid.Empty;
		return GameSystem.SendSnapshot( connection, snapshot => new HostResyncMsg { PreviousHostId = previousHostId, Snapshot = snapshot }, shared: shared );
	}

	async Task OnHostResync( HostResyncMsg msg, Connection source, Guid msgId )
	{
		if ( IsHost )
			return;

		if ( _migrationPhase != MigrationPhase.WaitingForHost || source.Id != _pendingHostId )
		{
			Log.Warning( $"Ignoring host resync from {source}, we were expecting {_pendingHostId}" );
			return;
		}

		_migrationPhase = MigrationPhase.ApplyingResync;
		SetHostConnection( source );

		var previousHost = _leavingHost;
		_leavingHost = null;

		if ( previousHost is not null )
			RemovePeer( previousHost );

		if ( GameSystem is not null )
		{
			try
			{
				await GameSystem.ResyncFromHostAsync( previousHost, source, msg.Snapshot );
			}
			catch ( Exception e )
			{
				Log.Error( e );
				IGameInstanceDll.Current?.Disconnect( "Error loading snapshot from the new host." );
				return;
			}
		}

		if ( IsDisconnected )
			return;

		_pendingHostId = Guid.Empty;
		_migrationPhase = MigrationPhase.None;
		source.SendMessage( new HostResyncDoneMsg() );
	}

	void OnHostResyncDone( HostResyncDoneMsg msg, Connection source, Guid msgId )
	{
		if ( !IsHost || !_pendingResyncs.Remove( source.Id ) )
			return;

		source.State = Connection.ChannelState.Connected;
		Log.Info( $"{source.Name} [{source.SteamId}] is connected to the new host" );
	}

	/// <summary>
	/// The host is gone. The game ends unless a handoff from them is still queued; give it a moment.
	/// </summary>
	internal void OnHostLost( Connection host )
	{
		if ( IsHost || IsDisconnected || _isBecomingHost || _hostLost )
			return;

		if ( _pendingHostId != Guid.Empty && host.Id != _pendingHostId )
			return;

		_hostLost = true;
		_timeSinceHostLost = 0;
	}

	void LeaveBecauseHostLeft()
	{
		if ( IsDisconnected )
			return;

		Log.Info( "The host has left the game" );
		IGameInstanceDll.Current?.Disconnect( "The host has left the game." );
	}

	void TickHostMigration()
	{
		if ( IsHost || IsDisconnected || _isBecomingHost )
			return;

		if ( _migrationPhase == MigrationPhase.WaitingForLocalHost && (Connection is null || Connection.IsConnectionLost) )
		{
			Log.Info( "Host changed - rejoining local host" );
			Networking.Connect( "local" );
			return;
		}

		if ( Connection is not null && Connection.IsConnectionLost )
			OnHostLost( Connection );

		if ( _hostLost && _pendingHostId == Guid.Empty && _timeSinceHostLost > 2f )
		{
			_hostLost = false;
			LeaveBecauseHostLeft();
			return;
		}

		if ( _pendingHostId != Guid.Empty && _timeSincePendingHost > Networking.HostMigrationTimeout )
		{
			Log.Warning( "Timed out waiting for the new host" );
			IGameInstanceDll.Current?.Disconnect( "Timed out waiting for the new host." );
		}
	}
}
