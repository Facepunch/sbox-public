using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Sandbox.Network;

internal class DeltaSnapshotSystem
{
	private static readonly GuidUlongComparer GuidComparer = new();

	private GameNetworkSystem System { get; set; }

	public DeltaSnapshotSystem( GameNetworkSystem system )
	{
		System = system;
	}

	internal class GuidUlongComparer : IEqualityComparer<Guid>
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		public bool Equals( Guid x, Guid y ) => x.Equals( y );

		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		public int GetHashCode( Guid guid )
		{
			var lowBits = MemoryMarshal.Read<ulong>( MemoryMarshal.AsBytes( MemoryMarshal.CreateReadOnlySpan( in guid, 1 ) ) );
			return (int)(lowBits ^ (lowBits >> 32));
		}
	}

	internal class ConnectionData
	{
		public List<DeltaSnapshotCluster> SentClusters { get; set; } = new( 128 );
		public Dictionary<Guid, List<DeltaSnapshot>> SentSnapshots { get; set; } = new( GuidComparer );
		public Dictionary<Guid, RemoteSnapshotState> ReceivedSnapshotStates { get; set; } = new( GuidComparer );
		public Dictionary<Guid, RemoteSnapshotState> RemoteSnapshotStates { get; set; } = new( GuidComparer );
		public Connection Connection { get; private set; }

		public ConnectionData( Connection connection )
		{
			Connection = connection;
		}

		/// <summary>
		/// Clean up any data about a removed networked object.
		/// </summary>
		/// <param name="nwo"></param>
		public void RemoveNetworkObject( NetworkObject nwo )
		{
			RemoteSnapshotStates.Remove( nwo.Id );
			ReceivedSnapshotStates.Remove( nwo.Id );

			if ( !SentSnapshots.Remove( nwo.Id, out var snapshots ) )
				return;

			foreach ( var snapshot in snapshots )
			{
				snapshot.Release();
			}
		}

		private TimeUntil NextPruneData { get; set; }

		/// <summary>
		/// Clear this connection and clean up.
		/// </summary>
		public void Clear()
		{
			RemoteSnapshotStates.Clear();
			ReceivedSnapshotStates.Clear();

			foreach ( var snapshots in SentSnapshots.Values )
			{
				foreach ( var snapshot in snapshots )
				{
					snapshot.Release();
				}
			}

			SentSnapshots.Clear();

			foreach ( var cluster in SentClusters )
			{
				cluster.Release();
			}

			SentClusters.Clear();
		}

		/// <summary>
		/// Tick the connection and clear any out-of-date data.
		/// </summary>
		public void Tick()
		{
			if ( !NextPruneData )
				return;

			for ( var i = SentClusters.Count - 1; i >= 0; i-- )
			{
				var cluster = SentClusters[i];
				if ( cluster.TimeSinceCreated <= 5f )
					continue;

				cluster.Release();
				SentClusters.RemoveAt( i );
			}

			foreach ( var list in SentSnapshots.Values )
			{
				for ( var i = list.Count - 1; i >= 0; i-- )
				{
					var snapshot = list[i];
					if ( snapshot.TimeSinceCreated <= 5f )
						continue;

					snapshot.Release();
					list.RemoveAt( i );
				}
			}

			NextPruneData = 1f;
		}
	}

	private Dictionary<Guid, ConnectionData> Connections { get; set; } = new( GuidComparer );
	private Dictionary<Guid, ushort> LastSentSnapshotIds { get; set; } = new( GuidComparer );
	private float Time { get; set; }

	/// <summary>
	/// Remove a connection from the snapshot system.
	/// </summary>
	/// <param name="target"></param>
	public void RemoveConnection( Connection target )
	{
		if ( Connections.Remove( target.Id, out var data ) )
		{
			data.Clear();
		}
	}

	/// <summary>
	/// Reset all connection data for the snapshot system. This might happen when a hotload
	/// occurs, or the host changes.
	/// </summary>
	internal void Reset()
	{
		LastSentSnapshotIds.Clear();

		foreach ( var connection in Connections.Values )
		{
			connection.Clear();
		}

		Connections.Clear();
	}

	/// <summary>
	/// Locally clear any stored snapshot information about a networked object.
	/// </summary>
	/// <param name="nwo"></param>
	internal void ClearNetworkObject( NetworkObject nwo )
	{
		foreach ( var c in Connections.Values )
		{
			c.RemoveNetworkObject( nwo );
		}

		LastSentSnapshotIds.Remove( nwo.Id );
	}

	internal ConnectionData GetConnection( Connection connection )
	{
		if ( !Connections.TryGetValue( connection.Id, out var data ) )
		{
			data = Connections[connection.Id] = new( connection );
		}

		return data;
	}

	private Dictionary<DeltaSnapshot, SnapshotData> ClusterBuffer { get; set; } = new();

	private void SendCluster( ConnectionData target, DeltaSnapshotCluster cluster,
		NetFlags flags = NetFlags.UnreliableNoDelay )
	{
		if ( cluster.Snapshots.Count == 0 )
			return;

		var receivedSnapshotStates = target.ReceivedSnapshotStates;
		var connectionId = target.Connection.Id;
		var connection = target.Connection;

		for ( var i = 0; i < cluster.Snapshots.Count; i++ )
		{
			var snapshot = cluster.Snapshots[i];

			if ( snapshot.LocalState?.UpdatedConnections.Contains( connectionId ) ?? false )
				continue;

			if ( !(snapshot.Source?.ShouldTransmit( connection ) ?? true) )
				continue;

			SnapshotData dataToSend = null;

			if ( receivedSnapshotStates.TryGetValue( snapshot.ObjectId, out var state ) )
			{
				for ( var j = 0; j < snapshot.Entries.Count; j++ )
				{
					var entry = snapshot.Entries[j];

					if ( entry.LocalState?.Connections?.Contains( connectionId ) ?? false )
						continue;

					var slot = entry.Slot;
					var value = entry.Value;

					if ( state.TryGetHash( slot, out var oldHash, Time ) )
					{
						// Nothing to be done here, we have this value...
						if ( entry.Hash == oldHash )
							continue;
					}

					dataToSend ??= SnapshotData.Pool.Rent();
					dataToSend[slot] = value;

					state.AddPredicted( slot, value, Time );

					entry.Connections?.Add( connectionId );
				}
			}
			else
			{
				dataToSend = SnapshotData.Pool.Rent();
				state = new RemoteSnapshotState
				{
					ObjectId = snapshot.ObjectId
				};

				foreach ( var entry in snapshot.Entries )
				{
					var slot = entry.Slot;
					var value = entry.Value;

					state.AddPredicted( slot, value, Time );
					dataToSend[slot] = value;

					entry.Connections?.Add( connectionId );
				}

				receivedSnapshotStates[snapshot.ObjectId] = state;
			}

			if ( dataToSend == null )
				continue;

			if ( dataToSend.Count == 0 )
			{
				dataToSend.Release();
				continue;
			}

			ClusterBuffer.Add( snapshot, dataToSend );
		}

		if ( ClusterBuffer.Count == 0 )
			return;

		using var writer = new ByteStream( DeltaSnapshotCluster.MaxSize * 4 );

		writer.Write( cluster.Id );
		writer.Write( (ushort)ClusterBuffer.Count );

		foreach ( var (snapshot, dataToSend) in ClusterBuffer )
		{
			writer.Write( snapshot.Version );
			writer.Write( snapshot.SnapshotId );
			writer.Write( snapshot.ObjectId );
			writer.Write( (ushort)dataToSend.Count );

			foreach ( var (slot, value) in dataToSend )
			{
				writer.Write( slot );
				writer.WriteArray( value );
			}

			dataToSend.Release();
		}

		ClusterBuffer.Clear();

		System.Send( target.Connection, InternalMessageType.DeltaSnapshotCluster, writer.ToSpan(), flags );

		target.SentClusters.Add( cluster );
		cluster.AddReference();

		// For empty connections, we still want to "receive" acknowledgements for benchmarking purposes
		if ( target.Connection is EmptyConnection )
		{
			_ = ReceiveAckAfterDelay( 50, target, cluster.Id );
		}
	}

	private async Task ReceiveAckAfterDelay( int delayMs, ConnectionData target, ushort clusterId )
	{
		try
		{
			await GameTask.Delay( delayMs );

			var bs = ByteStream.Create( 32 );
			bs.Write( clusterId );
			bs.Write( (ushort)0 );
			bs.Position = 0;

			OnDeltaSnapshotClusterAck( target.Connection, bs );

			bs.Dispose();
		}
		catch ( TaskCanceledException )
		{
			// Who cares.
		}
	}

	private void SendSnapshot( ConnectionData target, DeltaSnapshot snapshot, NetFlags flags = NetFlags.UnreliableNoDelay, bool sendFullUpdate = false )
	{
		var connectionId = target.Connection.Id;
		SnapshotData snapshotData = null;

		if ( target.ReceivedSnapshotStates.TryGetValue( snapshot.ObjectId, out var state ) )
		{
			foreach ( var entry in snapshot.Entries )
			{
				var slot = entry.Slot;
				var value = entry.Value;

				if ( !sendFullUpdate && state.TryGetHash( slot, out var oldHash, Time ) )
				{
					// Nothing to be done here, we have this value...
					if ( entry.Hash == oldHash )
						continue;
				}

				snapshotData ??= SnapshotData.Pool.Rent();
				snapshotData[slot] = value;

				state.AddPredicted( slot, value, Time );

				entry.Connections?.Add( connectionId );
			}
		}
		else
		{
			snapshotData = SnapshotData.Pool.Rent();
			state = new RemoteSnapshotState { ObjectId = snapshot.ObjectId };

			foreach ( var entry in snapshot.Entries )
			{
				var slot = entry.Slot;
				var value = entry.Value;

				state.AddPredicted( slot, value, Time );
				snapshotData[slot] = value;

				entry.Connections?.Add( connectionId );
			}

			target.ReceivedSnapshotStates[snapshot.ObjectId] = state;
		}

		if ( snapshotData == null )
			return;

		if ( snapshotData.Count == 0 )
		{
			snapshotData.Release();
			return;
		}

		using var writer = new ByteStream( DeltaSnapshotCluster.MaxSize * 4 );

		writer.Write( snapshot.ObjectId );
		writer.Write( snapshot.Version );
		writer.Write( snapshot.SnapshotId );
		writer.Write( (ushort)snapshotData.Count );

		foreach ( var (slot, value) in snapshotData )
		{
			writer.Write( slot );
			writer.WriteArray( value );
		}

		snapshotData.Release();

		System.Send( target.Connection, InternalMessageType.DeltaSnapshot, writer.ToSpan(), flags );

		if ( !target.SentSnapshots.TryGetValue( snapshot.ObjectId, out var sentSnapshots ) )
		{
			sentSnapshots = target.SentSnapshots[snapshot.ObjectId] = [];
		}

		sentSnapshots.Add( snapshot );
		snapshot.AddReference();
	}

	public void OnDeltaSnapshotClusterAck( Connection source, ByteStream message )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;

		var connectionData = GetConnection( source );

		var clusterId = message.Read<ushort>();
		var cluster = connectionData.SentClusters.FirstOrDefault( c => c.Id == clusterId );
		if ( cluster is null ) return;

		var invalidSnapshotCount = message.Read<ushort>();
		var invalidSnapshotIds = new HashSet<ushort>();
		var connectionId = source.Id;

		for ( var i = 0; i < invalidSnapshotCount; i++ )
		{
			invalidSnapshotIds.Add( message.Read<ushort>() );
		}

		foreach ( var snapshot in cluster.Snapshots )
		{
			// Did the client reject this particular snapshot? Maybe the game object didn't exist yet.
			if ( invalidSnapshotIds.Contains( snapshot.SnapshotId ) )
				continue;

			IDeltaSnapshot snapshotter = scene.Directory.FindSystemByGuid( snapshot.ObjectId );

			if ( snapshotter is null )
			{
				var go = scene.Directory.FindByGuid( snapshot.ObjectId );
				if ( go.IsValid() )
					snapshotter = go._net;
			}

			if ( snapshotter is null || snapshotter.SnapshotVersion != snapshot.Version )
				continue;

			if ( connectionData.ReceivedSnapshotStates.TryGetValue( snapshot.ObjectId, out var state ) )
			{
				foreach ( var entry in snapshot.Entries )
				{
					if ( (!entry.Connections?.Contains( connectionId ) ?? false) )
						continue;

					var slot = entry.Slot;
					var value = entry.Value;

					state.Update( slot, snapshot.SnapshotId, value );
				}
			}
			else
			{
				state = connectionData.ReceivedSnapshotStates[snapshot.ObjectId] = RemoteSnapshotState.From( connectionId, snapshot );
			}

			snapshotter.OnSnapshotAck( source, snapshot, state );
		}

		connectionData.SentClusters.Remove( cluster );
		cluster.Release();
	}

	public void OnDeltaSnapshotCluster( Connection source, ByteStream reader )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;

		var clusterId = reader.Read<ushort>();
		var connectionData = GetConnection( source );

		var count = (int)reader.Read<ushort>();
		var invalidSnapshotIds = new HashSet<ushort>();  // allocation
		var currentData = new Dictionary<int, byte[]>();  // allocation

		for ( var i = 0; i < count; i++ )
		{
			var version = reader.Read<ushort>();
			var snapshotId = reader.Read<ushort>();
			var objectId = reader.Read<Guid>();
			var dataCount = reader.Read<ushort>();

			currentData.Clear();

			for ( var j = 0; j < dataCount; j++ )
			{
				var slot = reader.Read<int>();
				currentData[slot] = reader.ReadArraySpan<byte>( 1024 * 1024 * 16 ).ToArray(); // allocation
			}

			IDeltaSnapshot snapshotter = scene.Directory.FindSystemByGuid( objectId );

			if ( snapshotter is null )
			{
				var go = scene.Directory.FindByGuid( objectId );
				if ( go.IsValid() ) snapshotter = go._net;
			}

			if ( snapshotter is null || snapshotter.SnapshotVersion != version )
			{
				invalidSnapshotIds.Add( snapshotId );
				continue;
			}

			var snapshot = DeltaSnapshot.From( currentData );
			snapshot.SnapshotId = snapshotId;
			snapshot.Version = version;
			snapshot.ObjectId = objectId;

			if ( connectionData.RemoteSnapshotStates.TryGetValue( objectId, out var state ) )
			{
				foreach ( var entry in snapshot.Entries )
				{
					var slot = entry.Slot;
					var value = entry.Value;

					state.Update( slot, snapshotId, value );
				}
			}
			else
			{
				state = connectionData.RemoteSnapshotStates[objectId] = RemoteSnapshotState.From( source.Id, snapshot );
			}

			var finalSnapshot = state.ToDeltaSnapshot( snapshotId, version, snapshot.Keys, Time );

			if ( !snapshotter.OnSnapshot( source, finalSnapshot ) )
			{
				invalidSnapshotIds.Add( snapshotId );
			}
		}

		var ackBs = ByteStream.Create( 1024 );
		ackBs.Write( clusterId );
		ackBs.Write( (ushort)invalidSnapshotIds.Count );

		foreach ( var invalidSnapshotId in invalidSnapshotIds )
		{
			ackBs.Write( invalidSnapshotId );
		}

		System.Send( source, InternalMessageType.DeltaSnapshotClusterAck, ackBs.ToArray(),
			NetFlags.Unreliable | NetFlags.DiscardOnDelay );

		ackBs.Dispose();
	}

	public void OnDeltaSnapshotAck( Connection source, ByteStream message )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;

		var objectId = message.Read<Guid>();
		var snapshotId = message.Read<ushort>();
		var connectionData = GetConnection( source );
		var connectionId = source.Id;

		if ( !connectionData.SentSnapshots.TryGetValue( objectId, out var sentSnapshots ) )
			sentSnapshots = connectionData.SentSnapshots[objectId] = new();

		var snapshot = sentSnapshots.FirstOrDefault( s => s.SnapshotId == snapshotId );
		if ( snapshot is null ) return;

		IDeltaSnapshot snapshotter = scene.Directory.FindSystemByGuid( snapshot.ObjectId );

		if ( snapshotter is null )
		{
			var go = scene.Directory.FindByGuid( snapshot.ObjectId );
			if ( go.IsValid() )
				snapshotter = go._net;
		}

		if ( snapshotter is not null && snapshotter.SnapshotVersion == snapshot.Version )
		{
			if ( connectionData.ReceivedSnapshotStates.TryGetValue( objectId, out var state ) )
			{
				foreach ( var entry in snapshot.Entries )
				{
					if ( (!entry.Connections?.Contains( connectionId ) ?? false) )
						continue;

					var slot = entry.Slot;
					var value = entry.Value;

					state.Update( slot, snapshot.SnapshotId, value );
				}
			}
			else
			{
				connectionData.ReceivedSnapshotStates[objectId] = RemoteSnapshotState.From( connectionId, snapshot );
			}

			snapshotter.OnSnapshotAck( source, snapshot, state );
		}

		sentSnapshots.Remove( snapshot );
		snapshot.Release();
	}

	public void OnDeltaSnapshot( Connection source, ByteStream reader )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;

		var connectionData = GetConnection( source );

		var objectId = reader.Read<Guid>();
		var version = reader.Read<ushort>();
		var snapshotId = reader.Read<ushort>();
		var currentData = new Dictionary<int, byte[]>();
		var dataCount = reader.Read<ushort>();

		for ( var i = 0; i < dataCount; i++ )
		{
			var slot = reader.Read<int>();
			currentData[slot] = reader.ReadArraySpan<byte>( 1024 * 1024 * 16 ).ToArray();
		}

		IDeltaSnapshot snapshotter = scene.Directory.FindSystemByGuid( objectId );

		if ( snapshotter is null )
		{
			var go = scene.Directory.FindByGuid( objectId );
			if ( go.IsValid() ) snapshotter = go._net;
		}

		if ( snapshotter is null || snapshotter.SnapshotVersion != version )
			return;

		var snapshot = DeltaSnapshot.From( currentData );
		snapshot.SnapshotId = snapshotId;
		snapshot.ObjectId = objectId;

		if ( connectionData.RemoteSnapshotStates.TryGetValue( objectId, out var state ) )
		{
			foreach ( var entry in snapshot.Entries )
			{
				var slot = entry.Slot;
				var value = entry.Value;

				state.Update( slot, snapshot.SnapshotId, value );
			}
		}
		else
		{
			state = connectionData.RemoteSnapshotStates[objectId] = RemoteSnapshotState.From( source.Id, snapshot );
		}

		var finalSnapshot = state.ToDeltaSnapshot( snapshot.SnapshotId, version, snapshot.Keys, Time );

		if ( !snapshotter.OnSnapshot( source, finalSnapshot ) )
			return;

		var ackBs = ByteStream.Create( 1024 );
		ackBs.Write( objectId );
		ackBs.Write( snapshotId );

		System.Send( source, InternalMessageType.DeltaSnapshotAck, ackBs.ToArray(),
			NetFlags.Unreliable | NetFlags.DiscardOnDelay );

		ackBs.Dispose();
	}

	/// <summary>
	/// Create a new snapshot id for the provided <see cref="NetworkObject"/>.
	/// </summary>
	public ushort CreateSnapshotId( Guid objectId )
	{
		ushort snapshotId = 0;

		if ( LastSentSnapshotIds.TryGetValue( objectId, out var id ) )
			snapshotId = (ushort)(id + 1);

		LastSentSnapshotIds[objectId] = snapshotId;
		return snapshotId;
	}

	/// <summary>
	/// Update the cached real time value that is used internally in the Delta Snapshot System.
	/// </summary>
	internal void UpdateTime()
	{
		// We cache the real time here for use within the Delta Snapshot System. This is because
		// it can be expensive when called a lot of times.
		Time = RealTime.Now;
	}

	/// <summary>
	/// Tick the snapshot system and clear any out-of-date data.
	/// </summary>
	public void Tick()
	{
		foreach ( var c in Connections.Values )
		{
			c.Tick();
		}
	}

	private readonly List<DeltaSnapshotCluster> _clusters = new( 256 );

	/// <summary>
	/// Send a delta snapshot for a set of networked objects to the specified connections.
	/// </summary>
	/// <param name="objects"></param>
	/// <param name="connections"></param>
	public void Send( IEnumerable<IDeltaSnapshot> objects, Connection[] connections )
	{
		var currentCluster = DeltaSnapshotCluster.Pool.Rent();

		foreach ( var nwo in objects )
		{
			// Don't send updates about objects we don't own. The host can always send updates though
			// because there may be FromHost sync vars.
			if ( nwo.IsProxy && !Networking.IsHost )
				continue;

			var isAnyVisible = nwo.UpdateTransmitState( connections );

			// No point doing anything else if no connections can even see this object.
			if ( !isAnyVisible )
			{
				nwo.SendNetworkUpdate( true );
				continue;
			}

			var localSnapshotState = nwo.WriteSnapshotState();
			nwo.SendNetworkUpdate();

			var allConnectionsAreUpdated = true;

			foreach ( var connection in connections )
			{
				if ( localSnapshotState.UpdatedConnections.Contains( connection.Id ) )
					continue;

				allConnectionsAreUpdated = false;
				break;
			}

			// Do we even need to send this to anybody?
			if ( allConnectionsAreUpdated )
				continue;

			if ( localSnapshotState.Size == 0 )
				continue;

			var clonedSnapshot = DeltaSnapshot.Pool.Rent();
			clonedSnapshot.CopyFrom( nwo, localSnapshotState, connections.Length );

			if ( currentCluster.Size + clonedSnapshot.Size >= DeltaSnapshotCluster.MaxSize )
			{
				_clusters.Add( currentCluster );
				currentCluster = DeltaSnapshotCluster.Pool.Rent();
			}

			currentCluster.Add( clonedSnapshot );
			clonedSnapshot.Release();
		}

		if ( currentCluster.Size > 0 )
			_clusters.Add( currentCluster );
		else
			currentCluster.Release();

		if ( _clusters.Count == 0 )
			return;

		foreach ( var c in connections )
		{
			var connectionData = GetConnection( c );

			foreach ( var cluster in _clusters )
			{
				SendCluster( connectionData, cluster );
			}
		}

		foreach ( var cluster in _clusters )
		{
			cluster.Release();
		}

		_clusters.Clear();
	}

	/// <summary>
	/// Send a delta snapshot for a single networked object.
	/// </summary>
	public void Send( IDeltaSnapshot snapshotter, NetFlags flags = NetFlags.UnreliableNoDelay,
		bool sendFullUpdate = false )
	{
		if ( snapshotter is null ) return;

		var localSnapshotState = snapshotter.WriteSnapshotState();
		snapshotter.SendNetworkUpdate();

		if ( localSnapshotState.Size == 0 )
			return;

		var filteredConnections = System.GetFilteredConnections();
		var connections = filteredConnections as Connection[] ?? filteredConnections.ToArray();
		var clonedSnapshot = DeltaSnapshot.Pool.Rent();

		clonedSnapshot.CopyFrom( snapshotter, localSnapshotState, connections.Length );

		foreach ( var target in connections )
		{
			var connection = GetConnection( target );
			SendSnapshot( connection, clonedSnapshot, flags, sendFullUpdate );
		}

		clonedSnapshot.Release();
	}

	/// <summary>
	/// Get a full serialized data update for a local snapshot state.
	/// </summary>
	public byte[] GetFullSnapshotData( LocalSnapshotState state )
	{
		var bs = ByteStream.Create( 4096 );
		bs.Write( state.ObjectId );
		bs.Write( state.Version );
		bs.Write( state.SnapshotId );
		bs.Write( (ushort)state.Entries.Count );

		foreach ( var entry in state.Entries )
		{
			var slot = entry.Slot;
			var value = entry.Value;

			bs.Write( slot );
			bs.WriteArray( value );
		}

		var output = bs.ToArray();
		bs.Dispose();
		return output;
	}
}
