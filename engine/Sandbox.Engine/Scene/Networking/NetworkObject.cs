using Sandbox.Network;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace Sandbox;

internal sealed partial class NetworkObject : IValid, IDeltaSnapshot
{
	internal NetworkObject RootNetworkObject => GameObject.RootNetwork.RootGameObject._net;
	internal GameObject GameObject { get; set; }

	Guid IDeltaSnapshot.Id => Id;

	/// <summary>
	/// The unique <see cref="Guid"/> of the underlying <see cref="GameObject"/>.
	/// </summary>
	internal Guid Id => GameObject.Id;

	/// <summary>
	/// The <see cref="Guid"/> of the connection that created this.
	/// </summary>
	public Guid Creator { get; set; }

	public bool IsValid => GameObject.IsValid();

	/// <summary>
	/// If true then this object is spawning on the host, on behalf of another client. While it's
	/// doing this we're going to act like the host is the owner.. so that anything that is called in
	/// OnAwake will think we're not a proxy - until we've fully handed it off.
	/// </summary>
	bool _isNetworkSpawning;

	/// <summary>
	/// The <see cref="Guid"/> of the connection that owns this.
	/// </summary>
	public Guid Owner
	{
		get;
		set
		{
			if ( field == value )
				return;

			var oldOwner = field;
			field = value;

			OnOwnerChanged( field, oldOwner );
		}
	}

	/// <summary>
	/// Are we the owner of this networked object?
	/// </summary>
	public bool IsOwner => Owner == Connection.Local.Id;

	/// <summary>
	/// Is this networked object unowned?
	/// </summary>
	public bool IsUnowned => Owner == Guid.Empty;

	/// <summary>
	/// This is this a proxy if we don't own this networked object.
	/// </summary>
	public bool IsProxy
	{
		get
		{
			if ( _isNetworkSpawning ) return false;
			if ( IsOwner ) return false;
			if ( IsUnowned && Networking.IsHost ) return false;

			return true;
		}
	}

	/// <summary>
	/// Current snapshot version for this networked object.
	/// </summary>
	public ushort SnapshotVersion => LocalSnapshotState.Version;

	bool _clearInterpolationFlag;
	bool _hasNetworkDestroyed;
	bool _initialized;

	internal void OnHotload()
	{
		// Build the network table again as properties may have changed.
		CreateDataTable();
	}

	internal NetworkObject( GameObject source )
	{
		GameObject = source;
	}

	/// <summary>
	/// Initialize and spawn this networked object with the specified owner <see cref="Connection"/>.
	/// </summary>
	internal void InitializeForConnection( Connection owner, bool enable )
	{
		if ( _initialized )
			throw new( "NetworkObject already initialized" );

		using var _ = PerformanceStats.Timings.Network.Scope();

		_initialized = true;

		if ( owner is not null )
		{
			Creator = owner.Id;
			Owner = owner.Id;
		}
		else
		{
			Creator = Guid.Empty;
			Owner = Guid.Empty;
		}

		CreateDataTable();

		// Keep track of us
		GameObject.Scene.RegisterNetworkedObject( this );

		// Call OnAwake on everything. We allow you to enable here because if you're the
		// host spawning this object for other connections, then you probably want to act
		// like the owner of it while OnAwake/OnEnabled is being called.
		_isNetworkSpawning = true;
		GameObject.Enabled = enable;
		CallNetworkSpawn( owner );
		_isNetworkSpawning = false;

		// Tell the world that we're here
		BroadcastNetworkSpawn( owner );
	}

	/// <summary>
	/// Call INetworkSpawn hooks
	/// </summary>
	private void CallNetworkSpawn( Connection owner )
	{
		foreach ( var target in GameObject.Components.GetAll<Component.INetworkSpawn>( FindMode.EverythingInSelfAndDescendants ).ToArray() )
		{
			try
			{
				target.OnNetworkSpawn( owner );
			}
			catch ( Exception e )
			{
				Log.Error( e );
			}
		}
	}

	/// <summary>
	/// Tell everyone that we exist, and spawn any child network objects with the same owner.
	/// </summary>
	void BroadcastNetworkSpawn( Connection owner )
	{
		// Tell everyone that we exist
		SceneNetworkSystem.Instance?.NetworkSpawnBroadcast( this );

		// If we have any child network objects, then spawn them with the same owner
		GameObject.NetworkSpawnRecursive( owner );
	}

	/// <summary>
	/// Initialize this networked object from a create message.
	/// </summary>
	internal void Initialize( ObjectCreateMsg msg )
	{
		if ( _initialized )
			throw new( "NetworkObject already initialized" );

		using var _ = PerformanceStats.Timings.Network.Scope();

		_initialized = true;

		Creator = msg.Creator;
		Owner = msg.Owner;

		CreateDataTable();
		OnCreateMessage( msg );

		GameObject.Scene.RegisterNetworkedObject( this );
	}

	internal void Dispose()
	{
		GameObject.Scene.UnregisterNetworkObject( this );
		GameObject = default;
	}

	internal void ClearInterpolation()
	{
		if ( IsProxy ) return;
		_clearInterpolationFlag = true;
	}

	internal bool CanDropOwnership( Connection source )
	{
		// Conna: accept all requests for now. In future, maybe we can do something with INetworkListener?
		return true;
	}

	internal bool CanAssignOwnership( Connection source, Guid target )
	{
		// Conna: accept all requests for now. In future, maybe we can do something with INetworkListener?
		return true;
	}

	internal bool CanTakeOwnership( Connection source )
	{
		// Conna: accept all requests for now. In future, maybe we can do something with INetworkListener?
		return true;
	}

	internal void OnNetworkDestroy()
	{
		_hasNetworkDestroyed = true;
		GameObject.Destroy();
	}

	internal void SendNetworkDestroy()
	{
		if ( SceneNetworkSystem.Instance is null ) return;
		if ( Networking.IsDisconnecting ) return;
		if ( _hasNetworkDestroyed ) return;
		if ( IsProxy && !Networking.IsHost ) return;

		var msg = new ObjectDestroyMsg { Guid = GameObject.Id };
		SceneNetworkSystem.Instance.Broadcast( msg );
	}

	private static readonly GameObject.SerializeOptions _refreshSerializeOptions = new() { SingleNetworkObject = true };

	internal ObjectRefreshMsg GetRefreshMessage()
	{
		var system = SceneNetworkSystem.Instance;
		if ( system is null )
			throw new Exception( "SceneNetworkSystem is null" );

		var snapshot = ((IDeltaSnapshot)this).WriteSnapshotState();

		var msg = new ObjectRefreshMsg
		{
			Guid = GameObject.Id,
			Parent = GameObject.Parent.Id,
			JsonData = GameObject.Serialize( _refreshSerializeOptions ).ToJsonString(),
			TableData = WriteReliableData(),
			Snapshot = system.DeltaSnapshots.GetFullSnapshotData( snapshot )
		};

		return msg;
	}

	private static readonly GameObject.SerializeOptions _refreshDescendantSerializeOptions = new() { IgnoreChildren = true };

	internal void SendNetworkRefresh( GameObject go )
	{
		var system = SceneNetworkSystem.Instance;
		if ( system is null ) return;
		if ( go is null ) return;

		if ( go == GameObject )
		{
			// If the object passed isn't actually a descendant, then refresh
			// the entire tree.
			SendNetworkRefresh();
			return;
		}

		if ( go.IsDestroyed )
		{
			// We want to tell clients that this component has been destroyed...
			var msg = new ObjectDestroyDescendantMsg
			{
				Guid = go.Id
			};

			system.Broadcast( msg );
			return;
		}

		if ( !go.IsAncestor( GameObject ) )
			return;

		{
			var snapshot = ((IDeltaSnapshot)this).WriteSnapshotState();

			var msg = new ObjectRefreshDescendantMsg
			{
				GameObjectId = GameObject.Id,
				ParentId = go.Parent.Id,
				JsonData = go.Serialize( _refreshDescendantSerializeOptions ).ToJsonString(),
				TableData = WriteReliableData(),
				Snapshot = system.DeltaSnapshots.GetFullSnapshotData( snapshot )
			};

			system.Broadcast( msg );
		}
	}

	internal void SendNetworkRefresh( Component component )
	{
		var system = SceneNetworkSystem.Instance;
		if ( system is null ) return;

		if ( component is null ) return;

		if ( !component.IsValid() )
		{
			// We want to tell clients that this component has been destroyed...
			var msg = new ObjectDestroyComponentMsg
			{
				Guid = component.Id
			};

			system.Broadcast( msg );
			return;
		}

		{
			var snapshot = ((IDeltaSnapshot)this).WriteSnapshotState();

			var msg = new ObjectRefreshComponentMsg
			{
				JsonData = component.Serialize().ToJsonString(),
				GameObjectId = component.GameObject.Id,
				TableData = WriteReliableData(),
				Snapshot = system.DeltaSnapshots.GetFullSnapshotData( snapshot )
			};

			system.Broadcast( msg );
		}
	}

	internal void SendNetworkRefresh()
	{
		var system = SceneNetworkSystem.Instance;
		if ( system is null ) return;

		var msg = GetRefreshMessage();
		system.Broadcast( msg );
	}

	private struct CullState
	{
		public bool Culled;
		public float LastVisibleAt;
	}

	internal readonly LocalSnapshotState LocalSnapshotState = new();

	private readonly HashSet<Guid> _culledConnections = [];
	private readonly Dictionary<Guid, CullState> _cullStates = new();
	private readonly SnapshotValueCache _snapshotCache = new();
	private TimeUntil _nextUpdateCachedBounds;
	private BBox _cachedLocalBounds;

	/// <summary>
	/// Only cull this object if we've been invisible for this long.
	/// </summary>
	private const float CullDelay = 2f;

	/// <summary>
	/// Remove a connection id from any internal data structures.
	/// </summary>
	/// <param name="id"></param>
	internal void RemoveConnection( Guid id )
	{
		LocalSnapshotState.RemoveConnection( id );
		_culledConnections.Remove( id );
		_cullStates.Remove( id );
	}

	/// <summary>
	/// Clear all connections associated with the local snapshot state.
	/// </summary>
	internal void ClearConnections()
	{
		LocalSnapshotState.ClearConnections();
	}

	bool IDeltaSnapshot.ShouldTransmit( Connection target )
	{
		return GameObject.Network.AlwaysTransmit || !_culledConnections.Contains( target.Id );
	}

	bool IDeltaSnapshot.UpdateTransmitState( Connection[] targets )
	{
		if ( GameObject.Network.AlwaysTransmit )
		{
			for ( var i = 0; i < targets.Length; i++ )
			{
				var target = targets[i];

				if ( !_culledConnections.Remove( target.Id ) )
					continue;

				GameObject.Network.SetCullState( target, false );
			}

			return true;
		}

		if ( _nextUpdateCachedBounds )
		{
			// Let's update the cached local bounds every half a second.
			_nextUpdateCachedBounds = 0.5f;
			_cachedLocalBounds = GameObject.GetLocalBounds();
		}

		var shouldTransmitToAny = false;
		var worldBounds = _cachedLocalBounds + GameObject.WorldPosition;
		var timeNow = Time.Now;

		var rootNetworkObject = RootNetworkObject;
		IDeltaSnapshot root = rootNetworkObject != this ? rootNetworkObject : null;

		for ( var i = 0; i < targets.Length; i++ )
		{
			var target = targets[i];

			ref var state = ref CollectionsMarshal.GetValueRefOrAddDefault( _cullStates, target.Id, out var exists );

			if ( !exists )
			{
				state = new CullState
				{
					Culled = false,
					LastVisibleAt = timeNow
				};
			}

			if ( !state.Culled )
				shouldTransmitToAny = true;

			if ( (root?.ShouldTransmit( target ) ?? false) || IsVisible( target, worldBounds ) )
			{
				state.LastVisibleAt = timeNow;

				if ( !state.Culled )
					continue;

				if ( !_culledConnections.Remove( target.Id ) )
					continue;

				LocalSnapshotState.RemoveConnection( target.Id );
				GameObject.Network.SetCullState( target, false );

				shouldTransmitToAny = true;
				state.Culled = false;
			}
			else
			{
				var timeSinceVisible = timeNow - state.LastVisibleAt;

				if ( state.Culled || timeSinceVisible < CullDelay )
					continue;

				if ( !_culledConnections.Add( target.Id ) )
					continue;

				GameObject.Network.SetCullState( target, true );
				state.Culled = true;
			}
		}

		return shouldTransmitToAny;
	}

	/// <summary>
	/// Is this network object visible to the provided <see cref="Connection"/>. We'll check if we
	/// have a culler component and use that, but we'll also use our bounds to determine if we're
	/// visible.
	/// </summary>
	private bool IsVisible( Connection target, BBox worldBounds )
	{
		// Do we have a INetworkVisible? We're going to let that take priority.
		var go = GameObject;
		if ( go.IsValid() && go.Enabled && go.NetworkVisibility is not null )
		{
			return go.NetworkVisibility.IsVisibleToConnection( target, worldBounds );
		}

		// Global culling
		return GameObject.Scene.IsBBoxVisibleToConnection( target, worldBounds );
	}

	void IDeltaSnapshot.OnSnapshotAck( Connection source, DeltaSnapshot snapshot, RemoteSnapshotState state )
	{
		IDeltaSnapshot snapshotter = this;

		if ( !snapshotter.ShouldTransmit( source ) )
			return;

		var hasFullSnapshotState = true;

		foreach ( var entry in LocalSnapshotState.Entries )
		{
			if ( state.IsValueHashEqual( entry.Slot, entry.Hash, snapshot.SnapshotId ) )
			{
				entry.Connections.Add( source.Id );
			}
			else
			{
				entry.Connections.Remove( source.Id );
				hasFullSnapshotState = false;
			}
		}

		if ( hasFullSnapshotState )
			LocalSnapshotState.UpdatedConnections.Add( source.Id );
		else
			LocalSnapshotState.UpdatedConnections.Remove( source.Id );
	}

	private const int SnapshotPositionSlot = 1;
	private const int SnapshotRotationSlot = 2;
	private const int SnapshotScaleSlot = 3;
	private const int SnapshotInterpolationSlot = 4;
	private const int SnapshotEnabledSlot = 5;

	LocalSnapshotState IDeltaSnapshot.WriteSnapshotState()
	{
		var system = SceneNetworkSystem.Instance;
		if ( system is null ) return null;

		LocalSnapshotState.SnapshotId = system.DeltaSnapshots.CreateSnapshotId( Id );
		LocalSnapshotState.ParentId = GameObject.Parent is Scene ? Guid.Empty : GameObject.Parent.Id;
		LocalSnapshotState.ObjectId = Id;

		if ( !IsProxy )
		{
			var tx = GameObject.Transform.TargetLocal;
			LocalSnapshotState.AddCached( _snapshotCache, SnapshotPositionSlot, tx.Position, true );
			LocalSnapshotState.AddCached( _snapshotCache, SnapshotRotationSlot, tx.Rotation, true );
			LocalSnapshotState.AddCached( _snapshotCache, SnapshotScaleSlot, tx.Scale, true );
			LocalSnapshotState.AddCached( _snapshotCache, SnapshotInterpolationSlot, _clearInterpolationFlag );
			LocalSnapshotState.AddCached( _snapshotCache, SnapshotEnabledSlot, GameObject.Enabled );
		}

		dataTable.QueryValues();
		dataTable.WriteSnapshotState( LocalSnapshotState );

		_clearInterpolationFlag = false;

		return LocalSnapshotState;
	}

	void IDeltaSnapshot.SendNetworkUpdate( bool queryValues )
	{
		if ( queryValues )
			dataTable.QueryValues( true );

		if ( !dataTable.HasReliableChanges() )
			return;

		var msg = new ObjectNetworkTableMsg { Guid = GameObject.Id };
		var data = ByteStream.Create( 4096 );

		dataTable.WriteReliableChanged( ref data );
		msg.TableData = data.ToArray();

		data.Dispose();

		SceneNetworkSystem.Instance.Broadcast( msg );
	}

	internal void TransmitStateChanged()
	{
		LocalSnapshotState.ClearConnections();
	}

	private static readonly GameObject.SerializeOptions _createSerializeOptions = new() { SingleNetworkObject = true };

	internal ObjectCreateMsg GetCreateMessage()
	{
		if ( GameObject.Parent is null )
		{
			throw new( $"GameObject {GameObject.Id} ({GameObject.Name} has invalid parent" );
		}

		var jsonData = GameObject.Serialize( _createSerializeOptions );
		if ( jsonData is null )
		{
			throw new( $"Unable to serialize {GameObject.Id} ({GameObject.Name})" );
		}

		var create = new ObjectCreateMsg
		{
			Guid = GameObject.Id,
			SnapshotVersion = GameObject._net.LocalSnapshotState.Version,
			Transform = GameObject.Transform.TargetLocal,
			JsonData = jsonData.ToJsonString(),
			Creator = Creator,
			Parent = GameObject.Parent.Id,
			Owner = Owner,
			TableData = WriteDataTable( true ),
			Enabled = GameObject.Enabled
		};

		return create;
	}

	internal void DoOrphanedAction()
	{
		var action = GameObject.Network.NetworkOrphaned;

		if ( action == NetworkOrphaned.Destroy )
		{
			GameObject.Destroy();
		}
		else if ( action == NetworkOrphaned.ClearOwner )
		{
			if ( Networking.IsHost )
				GameObject.Network.AssignOwnership( Guid.Empty );
			else
				Owner = Guid.Empty;
		}
		else if ( action == NetworkOrphaned.Random )
		{
			// Only the host can assign ownership to a random connection. Because they'll need to broadcast
			// the random selection to everyone else.
			if ( Networking.IsHost )
			{
				var connections = Connection.All.ToArray();
				var randomIndex = Game.Random.Int( 0, connections.Length - 1 );
				var connection = connections[randomIndex];
				GameObject.Network.AssignOwnership( connection );
			}
			else
			{
				// We're not the host so let's just clear the owner until we get the new randomly
				// selected owner from the host.
				Owner = Guid.Empty;
			}
		}
		else if ( action == NetworkOrphaned.Host )
		{
			if ( Networking.IsHost )
				GameObject.Network.AssignOwnership( Connection.Host?.Id ?? Guid.Empty );
			else
				Owner = Guid.Empty;
		}
	}

	internal void OnNetworkTableMessage( ObjectNetworkTableMsg message )
	{
		ReadDataTable( message.TableData );
	}

	internal void OnRefreshMessage( Connection source, ObjectRefreshMsg message )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;

		var jsonObj = JsonNode.Parse( message.JsonData ).AsObject();

		GameObject.SetParentFromNetwork( scene.Directory.FindByGuid( message.Parent ) );
		GameObject.NetworkRefresh( jsonObj, source.IsHost );

		UpdateFromRefresh( source, message.TableData, message.Snapshot );
	}

	internal void UpdateFromRefresh( Connection source, byte[] tableData, byte[] snapshotData )
	{
		RegisterPropertiesRecursive();

		var system = SceneNetworkSystem.Instance;
		if ( system is null ) return;

		ReadDataTable( tableData, ( _, entry ) => source.IsHost || entry.HasControl( source ) );

		var bs = ByteStream.CreateReader( snapshotData );
		system.DeltaSnapshots.OnDeltaSnapshot( source, bs );
		bs.Dispose();
	}

	internal void OnCreateMessage( ObjectCreateMsg msg )
	{
		LocalSnapshotState.Version = msg.SnapshotVersion;

		var parent = GameObject.Scene.Directory.FindByGuid( msg.Parent );
		GameObject.Transform.SetLocalTransformFast( msg.Transform );
		GameObject.SetParentFromNetwork( parent );
		GameObject.Enabled = msg.Enabled;
		ReadDataTable( msg.TableData );
	}

	bool IDeltaSnapshot.OnSnapshot( Connection source, DeltaSnapshot snapshot )
	{
		// Don't process this if the source connection does not have control, and they
		// are not the host.
		if ( !HasControl( source ) && !source.IsHost )
			return false;

		// Conna: only what we regard as the owner can modify this shit.
		if ( HasControl( source ) )
		{
			snapshot.TryGetValue<bool>( SnapshotInterpolationSlot, out var clearInterpolation );

			var didTransformChange = false;
			var transform = GameObject.Transform.TargetLocal;

			if ( snapshot.TryGetValue<Vector3>( SnapshotPositionSlot, out var position ) )
			{
				didTransformChange = true;
				transform.Position = position;
			}

			if ( snapshot.TryGetValue<Rotation>( SnapshotRotationSlot, out var rotation ) )
			{
				didTransformChange = true;
				transform.Rotation = rotation;
			}

			if ( snapshot.TryGetValue<Vector3>( SnapshotScaleSlot, out var scale ) )
			{
				didTransformChange = true;
				transform.Scale = scale;
			}

			if ( didTransformChange )
			{
				GameObject.Transform.FromNetwork( transform, clearInterpolation );
			}
			else if ( clearInterpolation )
			{
				GameObject.Transform.ClearLocalInterpolation();
			}

			if ( snapshot.TryGetValue<bool>( SnapshotEnabledSlot, out var enabled ) )
			{
				GameObject.Enabled = enabled;
			}
		}

		dataTable.ReadSnapshot( source, snapshot );

		return true;
	}

	/// <summary>
	/// Whether the specified <see cref="Connection"/> has control over this networked object. A connection
	/// has control if the object is unowned and they are the host, or if they own it directly.
	/// </summary>
	internal bool HasControl( Connection c )
	{
		if ( IsUnowned )
			return c.IsHost;

		return c.Id == Owner;
	}

	void OnOwnerChanged( Guid newOwner, Guid prevOwner )
	{
		var wasOwner = (prevOwner == Connection.Local.Id) || (prevOwner == Guid.Empty && Networking.IsHost);
		var isOwner = (newOwner == Connection.Local.Id) || (newOwner == Guid.Empty && Networking.IsHost);

		var newConnection = Connection.Find( newOwner );
		var oldConnection = Connection.Find( prevOwner );

		// Conna: clear interpolation when ownership changes.
		GameObject.Transform.ClearLocalInterpolation();

		IGameObjectNetworkEvents.PostToGameObject( GameObject, x => x.NetworkOwnerChanged( newConnection, oldConnection ) );

		if ( wasOwner && !isOwner )
		{
			IGameObjectNetworkEvents.PostToGameObject( GameObject, x => x.StopControl() );
		}

		if ( isOwner && !wasOwner )
		{
			IGameObjectNetworkEvents.PostToGameObject( GameObject, x => x.StartControl() );
		}

		var system = SceneNetworkSystem.Instance;
		system?.DeltaSnapshots.ClearNetworkObject( this );

		LocalSnapshotState.ClearConnections();

		if ( !isOwner )
			return;

		GameObject.IsNetworkCulled = false;
		GameObject.UpdateNetworkCulledState();
	}
}
