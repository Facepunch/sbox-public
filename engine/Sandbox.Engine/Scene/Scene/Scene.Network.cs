using Sandbox.Network;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Sandbox;

public partial class Scene : GameObject
{
	[Obsolete( "Moved to ProjectSettings.Networking.UpdateRate" )]
	public float NetworkFrequency { get; set; }

	/// <summary>
	/// One divided by ProjectSettings.Networking.UpdateRate.
	/// </summary>
	public float NetworkRate => 1.0f / ProjectSettings.Networking.UpdateRate.Clamp( 1, 500 );

	internal readonly HashSet<NetworkObject> networkedObjects = new();
	internal readonly List<NetworkObject> _dirtyNetworkObjects = new();
	internal readonly List<NetworkObject> _awakeNetworkObjects = new();
	private readonly List<NetworkObject> _networkProbeBuffer = new();
	private readonly ConcurrentQueue<NetworkObject> _workerThreadNetworkWakes = new();

	private readonly HashSet<NetworkObject> _cullNetworkObjects = new();

	/// <summary>
	/// Cumulative counts of the work done to find visibility changes on sleeping network objects.
	/// </summary>
	internal struct NetworkProbeStats
	{
		public long Ticks;
		public long VisibilityProbes;
		public long ProbeWakes;
	}

	internal NetworkProbeStats NetworkProbeTotals;

	internal void MarkNetworkObjectDirty( NetworkObject obj )
	{
		if ( obj.IsDirty ) return;
		obj.IsDirty = true;
		(obj.KeepsAwake ? _awakeNetworkObjects : _dirtyNetworkObjects).Add( obj );
	}

	internal void MoveNetworkObjectPendingList( NetworkObject obj )
	{
		var from = obj.KeepsAwake ? _dirtyNetworkObjects : _awakeNetworkObjects;
		var to = obj.KeepsAwake ? _awakeNetworkObjects : _dirtyNetworkObjects;
		if ( from.Remove( obj ) )
			to.Add( obj );
	}

	/// <summary>
	/// Wake an object from a worker thread. The pending set belongs to the main thread, so this is applied
	/// at the start of the next network update and before end-of-frame snapshot capture.
	/// </summary>
	internal void QueueWorkerThreadNetworkWake( NetworkObject obj ) => _workerThreadNetworkWakes.Enqueue( obj );

	private void DrainWorkerThreadNetworkWakes()
	{
		while ( _workerThreadNetworkWakes.TryDequeue( out var woken ) )
			woken.MarkDirty();
	}

	internal void RegisterNetworkedObject( NetworkObject obj )
	{
		networkedObjects.Add( obj );
		UpdateNetworkCullRegistration( obj );
		MarkNetworkObjectDirty( obj );
	}

	internal void UpdateNetworkCullRegistration( NetworkObject obj )
	{
		if ( networkedObjects.Contains( obj ) && !obj.GameObject.Network.AlwaysTransmit )
			_cullNetworkObjects.Add( obj );
		else
			_cullNetworkObjects.Remove( obj );
	}

	internal void UnregisterNetworkObject( NetworkObject obj )
	{
		// When a network object is removed, we can clean up any snapshot data.
		var system = SceneNetworkSystem.Instance;
		system?.DeltaSnapshots?.ClearNetworkObject( obj );

		networkedObjects.Remove( obj );
		_cullNetworkObjects.Remove( obj );
		obj.IsDirty = false;
	}

	internal double _nextNetworkUpdate;
	bool _sendSnapshots;

	/// <summary>
	/// Send our user command at our desired <see cref="NetworkRate"/>, and snapshots at the end of the frame.
	/// Retire destroyed pending objects even when no network system is active.
	/// </summary>
	internal void SceneNetworkUpdate()
	{
		DrainWorkerThreadNetworkWakes();

		_networkMapInstanceCache.Clear();
		GetAll( _networkMapInstanceCache );

		if ( SceneNetworkSystem.Instance is not { } system )
		{
			_dirtyNetworkObjects.RemoveAll( static obj => !obj.IsValid );
			_awakeNetworkObjects.RemoveAll( static obj => !obj.IsValid );
			return;
		}

		var now = RealTime.NowDouble;
		if ( now < _nextNetworkUpdate )
			return;

		// Kept on a schedule, waiting a full interval from whichever frame we got to would send less often than asked
		_nextNetworkUpdate += NetworkRate;
		if ( _nextNetworkUpdate <= now )
			_nextNetworkUpdate = now + NetworkRate;

		SendClientTick( system );

		_sendSnapshots = true;
	}

	/// <summary>
	/// Send snapshots once this frame's fixed update and update have run, so they carry this frame's state.
	/// </summary>
	internal void SceneNetworkSnapshots()
	{
		if ( !_sendSnapshots )
			return;

		_sendSnapshots = false;

		if ( SceneNetworkSystem.Instance is not { } system )
			return;

		// Animation workers can wake sleeping objects after SceneNetworkUpdate has run.
		DrainWorkerThreadNetworkWakes();

		system.DeltaSnapshots.UpdateTime();

		// The array is only rebuilt when the connections change
		system.CollectConnections( _sendConnectionList, Connection.ChannelState.Connected );
		if ( !CollectionsMarshal.AsSpan( _sendConnectionList ).SequenceEqual( _sendConnections ) )
		{
			var hasNewConnection = false;
			foreach ( var connection in _sendConnectionList )
			{
				if ( Array.IndexOf( _sendConnections, connection ) >= 0 )
					continue;

				hasNewConnection = true;
				break;
			}

			_sendConnections = _sendConnectionList.ToArray();

			// New peers need a baseline and may change who controls polled entries.
			if ( hasNewConnection )
			{
				foreach ( var obj in networkedObjects )
				{
					obj.RefreshKeepsAwake();
					obj.MarkDirty();
				}
			}
		}

		ProbeCleanNetworkObjects( _sendConnections );

		// Partition objects into dirty (pending changes) and clean (fully ACK'd).
		// Dirty objects are processed first so they end up in earlier clusters,
		// which are less likely to be delayed under network congestion.
		_snapshotObjects.Clear();
		_cleanSnapshotObjects.Clear();

		PartitionSnapshotObjects( _dirtyNetworkObjects );
		PartitionSnapshotObjects( _awakeNetworkObjects );

		_snapshotObjects.AddRange( _cleanSnapshotObjects );

		// If we're the host, include any GameObjectSystems.
		if ( Networking.IsHost )
		{
			foreach ( var gameObjectSystem in systems.Values )
			{
				if ( gameObjectSystem.HasSyncProperties )
					_snapshotObjects.Add( gameObjectSystem );
			}
		}

		system.DeltaSnapshots.Send( CollectionsMarshal.AsSpan( _snapshotObjects ), _sendConnections );

		RetireSettledNetworkObjects( Time.NowDouble );

		system.DeltaSnapshots.Tick();
	}

	private void PartitionSnapshotObjects( List<NetworkObject> objects )
	{
		// Always-awake objects skip post-send cleanup, so prune destroyed objects here.
		var kept = 0;
		for ( var i = 0; i < objects.Count; i++ )
		{
			var obj = objects[i];
			if ( !obj.IsValid )
			{
				obj.IsDirty = false;
				continue;
			}

			if ( kept != i )
				objects[kept] = obj;
			kept++;

			if ( obj.LocalSnapshotState.AckedByConnections < 0 )
				_snapshotObjects.Add( obj );
			else
				_cleanSnapshotObjects.Add( obj );
		}

		objects.RemoveRange( kept, objects.Count - kept );
	}

	/// <summary>
	/// Drop destroyed, dormant and fully acknowledged objects from the pending set, keeping send order.
	/// </summary>
	private void RetireSettledNetworkObjects( double now )
	{
		var kept = 0;
		for ( var i = 0; i < _dirtyNetworkObjects.Count; i++ )
		{
			var obj = _dirtyNetworkObjects[i];
			if ( obj.IsValid && !obj.IsDeltaDormant && (!obj.IsFullyUpdated || !obj.CanSleepAt( now )) )
			{
				_dirtyNetworkObjects[kept++] = obj;
				continue;
			}

			obj.IsDirty = false;
		}

		_dirtyNetworkObjects.RemoveRange( kept, _dirtyNetworkObjects.Count - kept );
	}

	private void ProbeCleanNetworkObjects( Connection[] connections )
	{
		// Cull state callbacks can spawn or destroy objects, so never enumerate the live registration set here.
		_networkProbeBuffer.Clear();
		foreach ( var obj in _cullNetworkObjects )
		{
			if ( !obj.IsDirty )
				_networkProbeBuffer.Add( obj );
		}

		ref var stats = ref NetworkProbeTotals;
		stats.Ticks++;

		foreach ( var obj in _networkProbeBuffer )
		{
			if ( !obj.IsValid || !obj.CanProbeVisibility )
				continue;

			stats.VisibilityProbes++;
			if ( obj.ProbeVisibility( connections ) )
				stats.ProbeWakes++;
		}

		_networkProbeBuffer.Clear();
	}

	/// <summary>
	/// Create messages for what <paramref name="source"/> should see; null source means everything.
	/// </summary>
	internal void SerializeNetworkObjects( Connection source, List<object> collection, bool includeLocalObjects = false, SnapshotCapture capture = null )
	{
		var included = new HashSet<NetworkObject>();

		foreach ( var target in networkedObjects )
		{
			if ( target.GameObject?.IsDestroyed ?? true )
				continue;

			// Filter on the root's real visibility, not ShouldTransmit which is stale for a fresh join.
			var root = target.RootNetworkObject;
			if ( source is null || target.ShouldIncludeInSnapshot( source )
				|| (root != target && (root?.ShouldIncludeInSnapshot( source ) ?? false)) )
			{
				AddWithAncestors( source, target, included, collection, includeLocalObjects, capture );
			}
		}
	}

	/// <summary>
	/// Emit the create message for <paramref name="target"/> and every networked ancestor above it,
	/// each at most once. A networked child must never be sent without its parent chain.
	/// </summary>
	internal void AddWithAncestors( Connection source, NetworkObject target, HashSet<NetworkObject> included, List<object> collection, bool includeLocalObjects, SnapshotCapture capture = null )
	{
		var current = target;
		while ( current is not null )
		{
			if ( current.GameObject?.IsDestroyed ?? true )
				break;

			if ( !included.Add( current ) )
				break;

			collection.Add( current.GetCreateMessage( includeLocalObjects, capture ) );

			if ( source is not null )
				current.MarkCreateMessageSent( source );

			current = current.GameObject.Parent?._net;
		}
	}

	/// <summary>
	/// Do appropriate actions based on the <see cref="NetworkOrphaned"/> mode for all networked objects owned by a specific connection.
	/// </summary>
	internal void DoOrphanedActions( Connection connection )
	{
		var objects = networkedObjects.Where( n => n.Owner == connection.Id ).ToArray();

		foreach ( var o in objects )
		{
			o.DoOrphanedAction();
		}
	}

	/// <summary>
	/// Send all of our visibility origins to other clients. These are points we can observe from, which helps
	/// to determine the visibility of network objects and sends a user command.
	/// </summary>
	internal void SendClientTick( SceneNetworkSystem system )
	{
		var localConnection = Connection.Local;

		// Conna: in the future we might support multiple visibility origins. For now though, we'll just
		// use the main camera position as our primary viewing source.
		if ( localConnection.VisibilityOrigins.Length == 0 )
			localConnection.VisibilityOrigins = new Vector3[1];

		localConnection.VisibilityOrigins[0] = Camera?.WorldPosition ?? default;

		var userCommand = UserCommand.Create();
		localConnection.BuildUserCommand( ref userCommand );

		// Everyone we're connected to gets our visibility origins, the host also gets our user command. The message for
		// everyone else is the same, so it's encoded once.
		NetPacket shared = null;

		system.CollectConnections( _clientTickConnections, Connection.ChannelState.Snapshot );

		foreach ( var connection in _clientTickConnections )
		{
			// Not to peers behind the host: only the host gets to know where everyone looks from
			if ( connection is MockConnection )
				continue;

			if ( connection.IsHost )
			{
				var msg = WriteClientTick( localConnection );
				userCommand.Serialize( ref msg );
				connection.SendStream( msg, NetFlags.UnreliableNoDelay );
				msg.Dispose();
				continue;
			}

			if ( shared is null )
			{
				var msg = WriteClientTick( localConnection );
				shared = Connection.EncodePacket( msg );
				msg.Dispose();
			}

			connection.Send( shared, NetFlags.UnreliableNoDelay );
		}

		shared?.Release();
	}

	static ByteStream WriteClientTick( Connection localConnection )
	{
		var msg = ByteStream.Create( 256 );
		msg.Write( InternalMessageType.ClientTick );
		msg.Write( (char)localConnection.VisibilityOrigins.Length );

		for ( var i = 0; i < localConnection.VisibilityOrigins.Length; i++ )
		{
			var source = localConnection.VisibilityOrigins[i];
			msg.Write( source.x );
			msg.Write( source.y );
			msg.Write( source.z );
		}

		return msg;
	}

	/// <summary>
	/// A cache of all the MapInstances that gets updated every frame
	/// </summary>
	readonly List<MapInstance> _networkMapInstanceCache = new();
	readonly List<IDeltaSnapshot> _snapshotObjects = new();
	readonly List<IDeltaSnapshot> _cleanSnapshotObjects = new();
	readonly List<Connection> _sendConnectionList = new();
	Connection[] _sendConnections = [];
	readonly List<Connection> _clientTickConnections = new();

	/// <summary>
	/// Are these bounds visible to the specified <see cref="Connection"/>?
	/// </summary>
	public unsafe bool IsBBoxVisibleToConnection( Connection target, BBox box )
	{
		var sources = target.VisibilityOrigins;

		for ( var i = 0; i < _networkMapInstanceCache.Count; i++ )
		{
			var pvs = _networkMapInstanceCache[i].GetNetworkPvs();
			if ( !pvs.IsValid || pvs.IsEmptyPVS() )
				continue;

			fixed ( Vector3* sourcePtr = sources )
			{
				if ( !pvs.IsAbsBoxInPVS( sources.Length, (IntPtr)sourcePtr, box.Mins, box.Maxs ) )
					return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Is a position visible to the specified <see cref="Connection"/>?
	/// </summary>
	public unsafe bool IsPointVisibleToConnection( Connection target, Vector3 position )
	{
		var sources = target.VisibilityOrigins;

		for ( var i = 0; i < _networkMapInstanceCache.Count; i++ )
		{
			var pvs = _networkMapInstanceCache[i].GetNetworkPvs();
			if ( !pvs.IsValid || pvs.IsEmptyPVS() )
				continue;

			fixed ( Vector3* sourcePtr = sources )
			{
				if ( !pvs.IsInPVS( sources.Length, (IntPtr)sourcePtr, position ) )
					return false;
			}
		}

		return true;
	}
}
