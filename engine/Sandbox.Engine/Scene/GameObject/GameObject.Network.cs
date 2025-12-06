using Sandbox.Network;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace Sandbox;

public partial class GameObject
{
	internal NetworkObject _net { get; private set; }

	/// <summary>
	/// True if this is a networked object and is owned by another client. This means that we're
	/// not controlling this object, so shouldn't try to move it or anything.
	/// </summary>
	public bool IsProxy => Network.IsProxy;

	/// <summary>
	/// If true then this object is the root of a networked object.
	/// </summary>
	public bool IsNetworkRoot => _net is not null;

	/// <summary>
	/// OBSOLETE: Use NetworkMode instead.
	/// </summary>
	[Obsolete( "Use GameObject.NetworkMode" )]
	public bool Networked
	{
		get => NetworkMode == NetworkMode.Object;
		set
		{
			NetworkMode = value ? NetworkMode.Object : NetworkMode.Snapshot;
		}
	}

	/// <summary>
	/// How should this object be networked to other clients? By default, a <see cref="GameObject"/> will be
	/// networked as part of the <see cref="Scene"/> snapshot.
	/// </summary>
	public NetworkMode NetworkMode
	{
		get => _networkMode;
		set
		{
			if ( _net is not null )
			{
				// We must always be `NetworkMode.Object` if we're a networked object.
				_networkMode = NetworkMode.Object;
				return;
			}

			_networkMode = value;
		}
	}

	private NetworkMode _networkMode = NetworkMode.Snapshot;

	/// <summary>
	/// A component that can control our network visibility to a specific <see cref="Connection"/>.
	/// </summary>
	internal Component.INetworkVisible NetworkVisibility;

	/// <summary>
	/// If this object is networked, who can control ownership of it? This property will only
	/// be synchronized for a root network object.
	/// </summary>
	[Sync, Expose] private OwnerTransfer OwnerTransfer { get; set; } = OwnerTransfer.Fixed;

	/// <summary>
	/// Determines what happens when the owner disconnects. This property will only
	/// be synchronized for a root network object.
	/// </summary>
	[Sync, Expose] private NetworkOrphaned NetworkOrphaned { get; set; } = NetworkOrphaned.Destroy;

	/// <summary>
	/// Determines whether updates for this networked object are always transmitted to clients. Otherwise,
	/// they are only transmitted when the object is determined as visible to each client.
	/// </summary>
	[Property, Expose] private bool AlwaysTransmit { get; set; } = true;

	/// <summary>
	/// Whether our networked transform will be interpolated. This property will only
	/// be synchronized for a root network object.
	/// </summary>
	[Property, Sync, Expose] public bool NetworkInterpolation { get; set; } = true;

	/// <summary>
	/// Spawn on the network. If you have permission to spawn entities, this will spawn on
	/// everyone else's clients, and you will be the owner.
	/// </summary>
	public bool NetworkSpawn() => NetworkSpawn( Connection.Local );

	/// <summary>
	/// Spawn on the network with the specified options. If you have permission to spawn
	/// entities, this will spawn on everyone else's clients.
	/// </summary>
	public bool NetworkSpawn( NetworkSpawnOptions options )
	{
		// We don't network spawn prefab scene files
		if ( Scene is PrefabScene )
			return false;

		if ( Scene.IsEditor )
			return false;

		// Already is spawned
		if ( _net is not null )
			return false;

		var connection = Connection.Local;

		if ( !connection.CanSpawnObjects )
		{
			Log.Warning( $"{this} is trying to spawn - but we don't have permission!" );
			return false;
		}

		if ( IsPrefabInstanceRoot )
		{
			PrefabInstanceData.BreakAllPrefabInstanceInHierarchy( this );
		}

		// We may contain other networked children. In which case we want to send
		// them all in a singular message to keep any references
		using ( SceneNetworkSystem.Instance?.NetworkSpawnBatch() )
		{
			NetworkMode = NetworkMode.Object;

			if ( options.OwnerTransfer.HasValue )
				OwnerTransfer = options.OwnerTransfer.Value;

			if ( options.OrphanedMode.HasValue )
				NetworkOrphaned = options.OrphanedMode.Value;

			if ( options.AlwaysTransmit.HasValue )
				AlwaysTransmit = options.AlwaysTransmit.Value;

			// Give us a network object
			_net = new NetworkObject( this );

			// Tell all children that we're the network root
			UpdateNetworkRoot();

			// Make this connection the owner
			_net.InitializeForConnection( options.Owner, options.StartEnabled );
		}

		return true;
	}

	/// <summary>
	/// Spawn on the network. If you have permission to spawn entities, this will spawn on
	/// everyone else's clients and the owner will be the connection provided.
	/// </summary>
	public bool NetworkSpawn( bool enabled, Connection owner )
	{
		var options = new NetworkSpawnOptions
		{
			StartEnabled = enabled,
			Owner = owner
		};

		return NetworkSpawn( options );
	}

	/// <summary>
	/// Spawn on the network. If you have permission to spawn entities, this will spawn on
	/// everyone else's clients and the owner will be the connection provided.
	/// </summary>
	public bool NetworkSpawn( Connection owner ) => NetworkSpawn( true, owner );

	/// <summary>
	/// Initialize this object from the network
	/// </summary>
	internal void NetworkSpawnRemote( ObjectCreateMsg msg )
	{
		if ( _net is not null )
		{
			_net.OnCreateMessage( msg );
			return;
		}

		_net = new NetworkObject( this );
		_net.Initialize( msg );

		UpdateNetworkRoot();
	}

	/// <summary>
	/// Always transmit has been changed by the owner. We can't use Sync Vars for this, because
	/// they don't get sent if they shouldn't be transmitted.
	/// </summary>
	[Rpc.Broadcast( NetFlags.OwnerOnly ), Expose]
	void Msg_UpdateAlwaysTransmit( bool alwaysTransmit )
	{
		AlwaysTransmit = alwaysTransmit;
		_net?.TransmitStateChanged();
	}

	internal bool IsNetworkCulled { get; set; }

	void ClearNetworking()
	{
		if ( _net is null ) return;

		_net.Dispose();
		_net = null;
	}

	/// <summary>
	/// Make a request from the host to stop being the network owner of this game object.
	/// </summary>
	[Rpc.Broadcast]
	void Msg_RequestDropOwnership( ushort snapshotVersion )
	{
		if ( _net is null ) return;
		if ( !Networking.IsHost ) return;
		if ( OwnerTransfer != OwnerTransfer.Request ) return;

		var caller = Rpc.Caller;

		// Does the source connection even own this object?
		if ( _net.Owner != caller.Id )
			return;

		// Can this caller drop ownership?
		if ( _net.CanDropOwnership( caller ) )
		{
			Msg_DropOwnership( snapshotVersion );
		}
	}

	/// <summary>
	/// Stop being the network owner of this game object, or clear ownership if you're the host.
	/// </summary>
	[Rpc.Broadcast]
	void Msg_DropOwnership( ushort snapshotVersion )
	{
		if ( _net is null ) return;

		var caller = Rpc.Caller;

		// Is it the host telling us to drop ownership of this object?
		if ( caller.IsHost )
		{
			_net.Owner = Guid.Empty;
			return;
		}

		if ( OwnerTransfer == OwnerTransfer.Request )
			return;

		// Does the source connection even own this object?
		if ( _net.Owner != caller.Id )
			return;

		_net.LocalSnapshotState.Version = (ushort)(snapshotVersion + 1);
		_net.Owner = Guid.Empty;
	}

	/// <summary>
	/// Make a request from the host to become the network owner of this game object.
	/// </summary>
	[Rpc.Broadcast]
	void Msg_RequestTakeOwnership( ushort snapshotVersion )
	{
		if ( _net is null ) return;
		if ( !Networking.IsHost ) return;
		if ( OwnerTransfer != OwnerTransfer.Request ) return;

		// Can this caller take ownership?
		if ( _net.CanTakeOwnership( Rpc.Caller ) )
		{
			Msg_AssignOwnership( Rpc.CallerId, snapshotVersion );
		}
	}

	/// <summary>
	/// Set the parent of this networked object.
	/// </summary>
	[Rpc.Broadcast]
	void Msg_SetParent( Guid id, bool keepWorldPosition )
	{
		if ( _net is null ) return;

		var caller = Rpc.Caller;
		if ( caller == Connection.Local )
			return;

		// Can this caller set the parent?
		if ( !caller.IsHost && !_net.HasControl( caller ) )
			return;

		var parentObject = Scene.Directory.FindByGuid( id );

		if ( !parentObject.IsValid() )
			parentObject = Scene;

		if ( parentObject._net is not null )
		{
			// Does the caller own the parent object too?
			if ( !caller.IsHost && !parentObject._net.HasControl( caller ) )
				return;
		}

		SetParentFromNetwork( parentObject, keepWorldPosition );
	}

	/// <summary>
	/// Become the network owner of this game object.
	/// </summary>
	[Rpc.Broadcast]
	void Msg_TakeOwnership( ushort snapshotVersion )
	{
		if ( _net is null ) return;

		var caller = Rpc.Caller;

		// Can only the host control ownership?
		if ( OwnerTransfer == OwnerTransfer.Fixed && !caller.IsHost )
			return;

		// Only the host can give ownership if we have to request it.
		if ( OwnerTransfer == OwnerTransfer.Request && !caller.IsHost )
			return;

		_net.LocalSnapshotState.Version = (ushort)(snapshotVersion + 1);
		_net.Owner = Rpc.CallerId;
	}

	/// <summary>
	/// Make a request from the host to assign ownership of this game object to the specified connection <see cref="Guid"/>.
	/// </summary>
	[Rpc.Broadcast]
	void Msg_RequestAssignOwnership( Guid guid, ushort snapshotVersion )
	{
		if ( _net is null ) return;
		if ( !Networking.IsHost ) return;
		if ( OwnerTransfer != OwnerTransfer.Request ) return;

		// Can this caller assign ownership?
		if ( _net.CanAssignOwnership( Rpc.Caller, guid ) )
		{
			Msg_AssignOwnership( guid, snapshotVersion );
		}
	}

	/// <summary>
	/// Assign ownership of this game object to the specified connection <see cref="Guid"/>.
	/// </summary>
	/// <param name="guid"></param>
	/// <param name="snapshotVersion"></param>
	[Rpc.Broadcast]
	void Msg_AssignOwnership( Guid guid, ushort snapshotVersion )
	{
		if ( _net is null ) return;

		var caller = Rpc.Caller;

		// Can only the host control ownership?
		if ( OwnerTransfer == OwnerTransfer.Fixed && !caller.IsHost )
			return;

		// Only the host can assign ownership if we have to request it.
		if ( OwnerTransfer == OwnerTransfer.Request && !caller.IsHost )
			return;

		_net.LocalSnapshotState.Version = (ushort)(snapshotVersion + 1);
		_net.Owner = guid;
	}

	[EditorBrowsable( EditorBrowsableState.Never )]
	protected void __sync_SetValue<T>( in WrappedPropertySet<T> p )
	{
		var root = FindNetworkRoot();
		var slot = NetworkObject.GetPropertySlot( p.MemberIdent, Id );

		if ( root is null )
		{
			p.Setter( p.Value );
			return;
		}

		var net = root._net;

		if ( !net.dataTable.IsRegistered( slot ) )
		{
			p.Setter( p.Value );
			return;
		}

		if ( !net.dataTable.HasControl( slot ) )
		{
			if ( NetworkTable.IsReadingChanges )
				p.Setter( p.Value );

			return;
		}

		net.dataTable.UpdateSlotHash( slot, p.Value );
		p.Setter( p.Value );
	}

	[EditorBrowsable( EditorBrowsableState.Never )]
	protected T __sync_GetValue<T>( WrappedPropertyGet<T> p )
	{
		// We might want to implement lerp or something later on
		// so keeping this open in case.

		return p.Value;
	}

	[EditorBrowsable( EditorBrowsableState.Never )]
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	protected void __rpc_Wrapper( in WrappedMethod m, params object[] argumentList )
	{
		Rpc.OnCallInstanceRpc( this, default, m, argumentList );
	}

	/// <summary>
	/// The network root is the first networked GameObject above this.
	/// This gets set from the parent's NetworkSpawn and invalidated when the parent changes.
	/// </summary>
	internal GameObject NetworkRoot { get; private set; }

	internal void UpdateNetworkRoot()
	{
		NetworkRoot = FindNetworkRoot();
		ForEachChild( "UpdateNetworkRoot", true, go => go.UpdateNetworkRoot() );
	}

	internal GameObject FindNetworkRoot()
	{
		if ( _net is not null ) return this;
		if ( Parent is null || Parent is Scene ) return null;

		return Parent.FindNetworkRoot();
	}

	private static readonly DeserializeOptions _networkRefreshFromHostOptions = new() { IsRefreshing = true, IsNetworkRefresh = true, IsRefreshFromHost = true };
	private static readonly DeserializeOptions _networkRefreshFromClientOptions = new() { IsRefreshing = true, IsNetworkRefresh = true, IsRefreshFromHost = false };

	/// <summary>
	/// Update hierarchy from a network refresh.
	/// </summary>
	internal void NetworkRefresh( JsonObject jso, bool sourceIsHost )
	{
		Deserialize( jso, sourceIsHost ? _networkRefreshFromHostOptions : _networkRefreshFromClientOptions );
	}

	/// <summary>
	/// Loop all of our children, and any with networking enabled, we should spawn them
	/// with the same creator + owner as this.
	/// </summary>
	internal void NetworkSpawnRecursive( Connection connection )
	{
		if ( Scene is PrefabScene )
			return;

		foreach ( var go in Children )
		{
			// not this child, maybe one of its children
			if ( go.NetworkMode != NetworkMode.Object )
			{
				go.NetworkSpawnRecursive( connection );
				continue;
			}

			go.NetworkSpawn( go.Enabled, connection );
		}
	}

	NetworkAccessor __networkAccess;

	/// <summary>
	/// Access network information for this GameObject.
	/// </summary>
	[ActionGraphInclude, Icon( "wifi" )]
	public NetworkAccessor Network
	{
		get
		{
			var root = NetworkRoot;

			if ( root is not null && root != this )
				return root.Network;

			__networkAccess ??= new( this );
			return __networkAccess;

		}
	}

	public NetworkAccessor RootNetwork
	{
		get
		{
			var accessor = Network;

			if ( IsRoot )
				return accessor;

			var parentAccessor = Parent?.RootNetwork;
			return (parentAccessor?.Active ?? false) ? parentAccessor : accessor;
		}
	}

	[Expose, ActionGraphIgnore]
	public class NetworkAccessor
	{
		readonly GameObject go;

		public NetworkAccessor( GameObject o )
		{
			go = o;
		}

		/// <summary>
		/// Is this object networked
		/// </summary>
		public bool Active => go._net is not null;

		/// <summary>
		/// Get the GameObject that is the root of this network object
		/// </summary>
		public GameObject RootGameObject => go;

		/// <summary>
		/// Are we the owner of this network object
		/// </summary>
		[ActionGraphInclude]
		public bool IsOwner => OwnerId == Connection.Local.Id;

		/// <summary>
		/// The Id of the owner of this object
		/// </summary>
		public Guid OwnerId => go._net?.Owner ?? Guid.Empty;

		/// <summary>
		/// Are we the creator of this network object
		/// </summary>
		[ActionGraphInclude]
		public bool IsCreator => CreatorId == Connection.Local.Id;

		/// <summary>
		/// The Id of the create of this object
		/// </summary>
		public Guid CreatorId => go._net?.Creator ?? Guid.Empty;

		/// <summary>
		/// Is this object a network proxy. A network proxy is a network object that is not being simulated on the local pc.
		/// This means it's either owned by no-one and is being simulated by the host, or owned by another client.
		/// </summary>
		[ActionGraphInclude]
		public bool IsProxy => go._net?.IsProxy ?? false;

		/// <summary>
		/// Try to get the connection that owns this object. This can and will return null
		/// if we don't have information for this connection.
		/// </summary>
		[ActionGraphInclude, Obsolete( "Moved to Owner" )]
		public Connection OwnerConnection => Owner;

		/// <summary>
		/// Try to get the connection that owns this object. This can and will return null
		/// if we don't have information for this connection.
		/// </summary>
		[ActionGraphInclude]
		public Connection Owner => Connection.Find( OwnerId );

		/// <summary>
		/// Who can control ownership of this networked object?
		/// </summary>
		public OwnerTransfer OwnerTransfer => go.OwnerTransfer;

		/// <summary>
		/// Determines what happens when the owner disconnects.
		/// </summary>
		public NetworkOrphaned NetworkOrphaned => go.NetworkOrphaned;

		/// <summary>
		/// Current snapshot version. This usually changes when the owner changes.
		/// </summary>
		internal ushort SnapshotVersion => go._net?.SnapshotVersion ?? 0;

		/// <summary>
		/// Determines whether updates for this networked object are always transmitted to clients. Otherwise,
		/// they are only transmitted when the object is determined as visible to each client.
		/// </summary>
		public bool AlwaysTransmit
		{
			get => go.AlwaysTransmit;
			set
			{
				if ( IsProxy )
					return;

				if ( go.AlwaysTransmit == value )
					return;

				go.Msg_UpdateAlwaysTransmit( value );
			}
		}

		/// <summary>
		/// Whether the networked object's transform is interpolated.
		/// </summary>
		public bool Interpolation => go.NetworkInterpolation;

		/// <summary>
		/// Enable interpolation for the networked object's transform.
		/// </summary>
		public bool EnableInterpolation()
		{
			if ( IsProxy ) return false;
			go.NetworkInterpolation = true;
			return true;
		}

		/// <summary>
		/// Disable interpolation for the networked object's transform.
		/// </summary>
		public bool DisableInterpolation()
		{
			if ( IsProxy ) return false;
			go.NetworkInterpolation = false;
			return true;
		}

		/// <summary>
		/// <inheritdoc cref="GameTransform.ClearInterpolation()"/>
		/// </summary>
		public bool ClearInterpolation()
		{
			go.Transform.ClearInterpolation();
			return true;
		}

		/// <summary>
		/// Set what happens to this networked object when the owner disconnects.
		/// </summary>
		public bool SetOrphanedMode( NetworkOrphaned action )
		{
			if ( IsProxy ) return false;
			go.NetworkOrphaned = action;
			return true;
		}

		/// <summary>
		/// Set who can control ownership of this networked object. Only the current owner can change this.
		/// </summary>
		public bool SetOwnerTransfer( OwnerTransfer option )
		{
			if ( IsProxy ) return false;
			go.OwnerTransfer = option;
			return true;
		}

		/// <summary>
		/// Send a complete refresh snapshot of this networked object to other clients. This is useful if you have
		/// made vast changes to components or children.
		/// </summary>
		public void Refresh()
		{
			if ( IsProxy && !Networking.IsHost ) return;

			var connection = Connection.Local;
			if ( !connection.CanRefreshObjects )
			{
				Log.Warning( $"{go} is trying to refresh - but we don't have permission!" );
				return;
			}

			go._net?.RegisterPropertiesRecursive();
			go._net?.SendNetworkRefresh();
		}

		/// <summary>
		/// Send a refresh for a specific <see cref="GameObject"/> in the hierarchy of this networked object to other clients.
		/// This is useful if you've destroyed or added a new <see cref="GameObject"/> descendent and don't want to refresh
		/// the entire networked object.
		/// </summary>
		public void Refresh( GameObject descendent )
		{
			if ( IsProxy && !Networking.IsHost ) return;

			var connection = Connection.Local;
			if ( !connection.CanRefreshObjects )
			{
				Log.Warning( $"{go} is trying to refresh - but we don't have permission!" );
				return;
			}

			go._net?.RegisterPropertiesRecursive();
			go._net?.SendNetworkRefresh( descendent );
		}

		/// <summary>
		/// Send a refresh for a specific <see cref="Component"/> in the hierarchy of this networked object to other clients.
		/// This is useful if you've destroyed or added a new <see cref="Component"/> and don't want to refresh the entire object.
		/// </summary>
		public void Refresh( Component component )
		{
			if ( IsProxy && !Networking.IsHost ) return;

			var connection = Connection.Local;
			if ( !connection.CanRefreshObjects )
			{
				Log.Warning( $"{go} is trying to refresh - but we don't have permission!" );
				return;
			}

			go._net?.RegisterPropertiesRecursive();
			go._net?.SendNetworkRefresh( component );
		}

		/// <summary>
		/// Become the network owner of this object.
		/// <br/>
		/// <br/>
		/// Note: whether you can take ownership of this object depends on the
		/// <see cref="OwnerTransfer"/> of this networked object.
		/// </summary>
		[ActionGraphInclude]
		public bool TakeOwnership()
		{
			if ( !Active ) return false;
			if ( IsOwner ) return false;

			var snapshotVersion = go.Network.SnapshotVersion;

			if ( !Networking.IsHost && go.OwnerTransfer == OwnerTransfer.Request )
			{
				go.Msg_RequestTakeOwnership( snapshotVersion );
				return true;
			}

			go.Msg_TakeOwnership( snapshotVersion );
			return true;
		}

		/// <summary>
		/// Set the owner of this object to the specified <see cref="Connection"/>.
		/// <br/>
		/// <br/>
		/// Note: whether you can assign ownership of this object depends on the
		/// <see cref="OwnerTransfer"/> of this networked object.
		/// </summary>
		/// <exception cref="ArgumentNullException"><paramref name="channel"/> cannot be null. To clear owner, use <see cref="DropOwnership"/> instead.</exception>
		[ActionGraphInclude]
		public bool AssignOwnership( Connection channel )
		{
			ArgumentNullException.ThrowIfNull( channel );

			if ( !Active ) return false;

			var snapshotVersion = go.Network.SnapshotVersion;

			if ( !IsProxy )
			{
				// Clear interpolation and set that flag here.
				go.Transform.ClearInterpolation();

				// Force a delta snapshot for this object since we changed owner.
				var system = SceneNetworkSystem.Instance;
				system?.DeltaSnapshots?.Send( go._net, NetFlags.Reliable, true );
			}

			if ( !Networking.IsHost && go.OwnerTransfer == OwnerTransfer.Request )
			{
				go.Msg_RequestAssignOwnership( channel.Id, snapshotVersion );
				return true;
			}

			go.Msg_AssignOwnership( channel.Id, snapshotVersion );
			return true;
		}

		/// <summary>
		/// Assign ownership to the specific connection id. This should only be used internally
		/// when we want to force an ownership change, such as for a <see cref="NetworkOrphaned"/> action.
		/// </summary>
		/// <param name="connectionId"></param>
		/// <returns></returns>
		internal bool AssignOwnership( Guid connectionId )
		{
			if ( !Active ) return false;

			if ( !IsProxy )
			{
				// Clear interpolation and set that flag here.
				go.Transform.ClearInterpolation();

				// Force a delta snapshot for this object since we changed owner.
				var system = SceneNetworkSystem.Instance;
				system?.DeltaSnapshots?.Send( go._net, NetFlags.Reliable, true );
			}

			go.Msg_AssignOwnership( connectionId, go.Network.SnapshotVersion );
			return true;
		}

		/// <summary>
		/// Change the cull state of this GameObject on the network for the specified <see cref="Connection"/>.
		/// This is for internal use only.
		/// </summary>
		internal void SetCullState( Connection target, bool isCulled )
		{
			if ( IsProxy )
				return;

			using var bs = ByteStream.Create( 32 );

			bs.Write( InternalMessageType.SetCullState );
			bs.Write( RootGameObject.Id );
			bs.Write( isCulled );

			target.SendRawMessage( bs );
		}

		/// <summary>
		/// Stop being the owner of this object. Will clear the owner so the object becomes
		/// controlled by the server, and owned by no-one.
		/// <br/>
		/// <br/>
		/// Note: whether you can drop ownership of this object depends on the
		/// <see cref="OwnerTransfer"/> of this networked object.
		/// </summary>
		[ActionGraphInclude]
		public bool DropOwnership()
		{
			if ( !Active ) return false;

			var snapshotVersion = go.Network.SnapshotVersion;

			if ( !IsProxy )
			{
				// Clear interpolation and set that flag here.
				go.Transform.ClearInterpolation();

				// Force a delta snapshot for this object since we changed owner.
				var system = SceneNetworkSystem.Instance;
				system?.DeltaSnapshots?.Send( go._net, NetFlags.Reliable, true );
			}

			if ( Networking.IsHost )
			{
				// The host can always drop ownership of a networked object.
				go.Msg_DropOwnership( snapshotVersion );
				return true;
			}

			if ( !IsOwner ) return false;

			if ( go.OwnerTransfer == OwnerTransfer.Request )
			{
				go.Msg_RequestDropOwnership( snapshotVersion );
				return true;
			}

			go.Msg_DropOwnership( snapshotVersion );
			return true;
		}

		/// <summary>
		/// <inheritdoc cref="GameObject.NetworkSpawn()"/>
		/// </summary>
		[Obsolete( "Use GameObject.NetworkSpawn" )]
		public bool Spawn()
		{
			return go.NetworkSpawn();
		}

		/// <summary>
		/// <inheritdoc cref="GameObject.NetworkSpawn( Connection )"/>
		/// </summary>
		[Obsolete( "Use GameObject.NetworkSpawn" )]
		public bool Spawn( Connection owner )
		{
			return go.NetworkSpawn( owner );
		}
	}
}
