using Sandbox.Network;
using Sandbox.Utility;
using System.IO;
using System.Text.Json.Nodes;

namespace Sandbox;

/// <summary>
/// This is created and referenced by the network system, as a way to route.
/// </summary>
[Expose]
public partial class SceneNetworkSystem : GameNetworkSystem
{
	internal static SceneNetworkSystem Instance { get; set; }
	internal DeltaSnapshotSystem DeltaSnapshots { get; private set; }

	private List<NetworkObject> BatchSpawnList { get; set; } = [];
	private static bool IsSuppressingDestroyMessages { get; set; }
	private static bool IsSuppressingSpawnMessages { get; set; }
	private bool IsBatchNetworkSpawning { get; set; }
	private int BatchNetworkSpawnCount { get; set; }

	internal override bool IsHostBusy => !Game.ActiveScene?.IsLoading ?? true;

	internal SceneNetworkSystem( Internal.TypeLibrary typeLibrary, NetworkSystem system )
	{
		Instance = this;
		DeltaSnapshots = new( this );

		Library = typeLibrary;
		NetworkSystem = system;

		AddHandler<ObjectCreateBatchMsg>( OnObjectCreateBatch );
		AddHandler<ObjectCreateMsg>( OnObjectCreate );
		AddHandler<ObjectRefreshMsg>( OnObjectRefresh );
		AddHandler<ObjectDestroyComponentMsg>( OnObjectDestroyComponent );
		AddHandler<ObjectDestroyDescendantMsg>( OnObjectDestroyDescendant );
		AddHandler<ObjectRefreshComponentMsg>( OnObjectRefreshComponent );
		AddHandler<ObjectRefreshDescendantMsg>( OnObjectRefreshDescendant );
		AddHandler<ObjectRefreshMsgAck>( OnObjectRefreshAck );
		AddHandler<ObjectDestroyMsg>( OnObjectDestroy );
		AddHandler<ObjectDetachMsg>( OnObjectDetach );
		AddHandler<ObjectRpcMsg>( OnObjectMessage );
		AddHandler<ObjectNetworkTableMsg>( OnNetworkTableChanges );
		AddHandler<SceneNetworkTableMsg>( OnNetworkTableChanges );
		AddHandler<SceneRpcMsg>( OnSceneRpc );
		AddHandler<StaticRpcMsg>( OnStaticRpc );
		AddHandler<LoadSceneBeginMsg>( OnLoadSceneMsg );
		AddHandler<LoadSceneSnapshotMsg>( OnLoadSceneSnapshotMsg );
		AddHandler<LoadSceneRequestSnapshotMsg>( OnLoadSceneRequestSnapshotMsg );
		AddHandler<SceneLoadedMsg>( OnSceneLoadedMsg );
	}

	internal void OnHotload()
	{
		DeltaSnapshots.Reset();
	}

	/// <summary>
	/// Any <see cref="GameObject">GameObjects</see> created within this scope will not send spawn messages to other clients.
	/// </summary>
	internal static IDisposable SuppressSpawnMessages()
	{
		IsSuppressingSpawnMessages = true;

		return new DisposeAction( () =>
		{
			IsSuppressingSpawnMessages = false;
		} );
	}

	/// <summary>
	/// Any <see cref="GameObject">GameObjects</see> destroyed within this scope will not send destroy messages to other clients.
	/// </summary>
	internal static IDisposable SuppressDestroyMessages()
	{
		IsSuppressingDestroyMessages = true;

		return new DisposeAction( () =>
		{
			IsSuppressingDestroyMessages = false;
		} );
	}

	private readonly Dictionary<Guid, Guid> PendingSceneLoads = new();

	/// <summary>
	/// Load a scene for all other clients. This can only be called by the host.
	/// </summary>
	internal void LoadSceneBroadcast( SceneLoadOptions options )
	{
		if ( !Networking.IsActive || !Networking.IsHost )
			return;

		// Clear all pending scene loads before we load a new scene
		PendingSceneLoads.Clear();

		var mounedVpks = Game.ActiveScene.GetAllComponents<MapInstance>()
			.Select( x => x.MapName )
			.ToList();

		var loadMsg = new LoadSceneBeginMsg
		{
			ShowLoadingScreen = options.ShowLoadingScreen,
			MountedVPKs = mounedVpks,
			SceneId = Game.ActiveScene.Id,
			Id = Guid.NewGuid()
		};

		var msg = ByteStream.Create( 256 );
		msg.Write( InternalMessageType.Packed );

		Networking.System.Serialize( loadMsg, ref msg );

		foreach ( var c in Connection.All )
		{
			if ( c == Connection.Local )
				continue;

			if ( c.State < Connection.ChannelState.Snapshot )
				continue;

			PendingSceneLoads[c.Id] = loadMsg.Id;
			c.SendRawMessage( msg );
			c.State = Connection.ChannelState.MountVPKs;
		}

		msg.Dispose();
	}

	/// <summary>
	/// Start a network spawn batch. Any networked objects created within this scope
	/// will be sent with one spawn message. This makes sure that any references are
	/// kept to child networked objects when the objects are spawned on the other side.
	/// </summary>
	/// <returns></returns>
	internal IDisposable NetworkSpawnBatch()
	{
		IsBatchNetworkSpawning = true;
		BatchNetworkSpawnCount++;

		return new DisposeAction( () =>
		{
			BatchNetworkSpawnCount--;

			if ( BatchNetworkSpawnCount > 0 )
				return;

			IsBatchNetworkSpawning = false;
			SendNetworkSpawnBatch();
		} );
	}

	private void SendNetworkSpawnBatch()
	{
		// If we only have one, just send a normal message.
		if ( BatchSpawnList.Count == 1 )
		{
			var networkObject = BatchSpawnList.FirstOrDefault();

			if ( !(networkObject.GameObject?.IsDestroyed ?? true) )
				Broadcast( networkObject.GetCreateMessage() );

			BatchSpawnList.Clear();
			return;
		}

		var msg = new ObjectCreateBatchMsg();
		var list = new List<ObjectCreateMsg>();

		foreach ( var networkObject in BatchSpawnList )
		{
			if ( networkObject.GameObject?.IsDestroyed ?? true )
				continue;

			list.Add( networkObject.GetCreateMessage() );
		}

		msg.CreateMsgs = list.ToArray();
		BatchSpawnList.Clear();

		Broadcast( msg );
	}

	/// <summary>
	/// Broadcast the spawning of a networked object. This will add the networked object
	/// to the batch list if we're spawning as part of a batch and will ignore the spawn message
	/// entirely if we're supposed to be suppressing spawn messages.
	/// </summary>
	/// <param name="networkObject"></param>
	internal void NetworkSpawnBroadcast( NetworkObject networkObject )
	{
		// We're not supposed to send spawn messages right now.
		if ( IsSuppressingSpawnMessages )
			return;

		if ( IsBatchNetworkSpawning )
		{
			BatchSpawnList.Add( networkObject );
			return;
		}

		Broadcast( networkObject.GetCreateMessage() );
	}

	/// <summary>
	/// Broadcast the detachment of a networked object.
	/// </summary>
	/// <param name="networkObject"></param>
	internal void NetworkDetachBroadcast( NetworkObject networkObject )
	{
		var msg = new ObjectDetachMsg
		{
			Mode = networkObject.GameObject.NetworkMode,
			Guid = networkObject.GameObject.Id
		};

		Broadcast( msg );
	}

	/// <summary>
	/// Broadcast the destruction of a networked object. The message will be ignored if we're
	/// supposed to be suppressing destroy messages.
	/// </summary>
	/// <param name="networkObject"></param>
	internal void NetworkDestroyBroadcast( NetworkObject networkObject )
	{
		if ( IsSuppressingDestroyMessages )
			return;

		var msg = new ObjectDestroyMsg { Guid = networkObject.GameObject.Id };
		Broadcast( msg );
	}

	/// <summary>
	/// Called when the host has provided us with a snapshot for a newly loaded scene.
	/// </summary>
	private async Task OnLoadSceneSnapshotMsg( LoadSceneSnapshotMsg msg, Connection connection, Guid msgId )
	{
		NetworkDebugSystem.Current?.Record( NetworkDebugSystem.MessageType.Snapshot, msg );

		await SetSnapshotAsync( msg.Snapshot );

		// Let them know we have now loaded this scene.
		var loadedMsg = new SceneLoadedMsg { SceneId = msg.SceneId, Id = msg.Id };
		connection.SendMessage( loadedMsg, NetFlags.Reliable );

		LoadingScreen.IsVisible = false;
	}

	/// <summary>
	/// Called when a client has requested the snapshot for a newly loaded scene. This is usually
	/// once they've done any preloading that they need to do.
	/// </summary>
	private async Task OnLoadSceneRequestSnapshotMsg( LoadSceneRequestSnapshotMsg msg, Connection connection, Guid msgId )
	{
		// If this connection doesn't have a pending scene load with this id then bail.
		if ( !PendingSceneLoads.TryGetValue( connection.Id, out var id ) || id != msg.Id )
			return;

		var activeScene = Game.ActiveScene;

		if ( !activeScene.IsValid() || activeScene.Id != msg.SceneId )
		{
			PendingSceneLoads.Remove( connection.Id );
			return;
		}

		// Don't send anything while the scene is still loading
		while ( activeScene.IsValid() && activeScene.IsLoading )
		{
			if ( Game.ActiveScene != activeScene )
				break;

			await GameTask.Yield();
		}

		if ( Game.ActiveScene != activeScene || !activeScene.IsValid() )
		{
			PendingSceneLoads.Remove( connection.Id );
			return;
		}

		connection.State = Connection.ChannelState.Snapshot;

		var output = new LoadSceneSnapshotMsg { SceneId = msg.SceneId, Id = msg.Id };
		var snapshot = new SnapshotMsg
		{
			GameObjectSystems = [],
			NetworkObjects = new List<object>( 64 )
		};

		GetSnapshot( default, ref snapshot );
		output.Snapshot = snapshot;

		var bs = ByteStream.Create( 256 );
		bs.Write( InternalMessageType.Packed );

		Networking.System.Serialize( output, ref bs );
		connection.SendRawMessage( bs );

		bs.Dispose();
	}

	/// <summary>
	/// Called when the host has told us to load a new scene.
	/// </summary>
	private async Task OnLoadSceneMsg( LoadSceneBeginMsg msg, Connection connection, Guid msgId )
	{
		if ( !Game.IsEditor && msg.ShowLoadingScreen )
		{
			LoadingScreen.IsVisible = true;
			LoadingScreen.Title = "Loading Scene";
		}

		// Go ahead and destroy the scene
		if ( Game.ActiveScene is not null )
		{
			Game.ActiveScene.Destroy();
			Game.ActiveScene = null;
		}

		MountedVPKs?.Dispose();
		MountedVPKs = await MountMaps( msg.MountedVPKs );

		// Let them know we would like a snapshot now.
		var loadedMsg = new LoadSceneRequestSnapshotMsg { SceneId = msg.SceneId, Id = msg.Id };
		connection.SendMessage( loadedMsg, NetFlags.Reliable );
	}

	/// <summary>
	/// Called by clients to confirm they have finished loading the new scene.
	/// </summary>
	private void OnSceneLoadedMsg( SceneLoadedMsg msg, Connection connection, Guid msgId )
	{
		// If this connection doesn't have a pending scene load with this id then bail.
		if ( !PendingSceneLoads.TryGetValue( connection.Id, out var id ) || id != msg.Id )
			return;

		var activeScene = Game.ActiveScene;

		if ( !activeScene.IsValid() || activeScene.Id != msg.SceneId )
		{
			PendingSceneLoads.Remove( connection.Id );
			return;
		}

		connection.State = Connection.ChannelState.Connected;

		PendingSceneLoads.Remove( connection.Id );
		Instance?.OnJoined( connection );
	}

	/// <summary>
	/// A client has joined and wants to know what VPKs to preload.
	/// </summary>
	public override void GetMountedVPKs( Connection source, ref MountedVPKsResponse msg )
	{
		msg.MountedVPKs = Game.ActiveScene.GetAllComponents<MapInstance>().Select( x => x.MapName ).ToList();
	}

	/// <summary>
	/// Asynchronously load and mount any VPKs from the provided server response.
	/// </summary>
	public override async Task MountVPKs( Connection source, MountedVPKsResponse msg )
	{
		// Mount any vpks early because snapshotted or networked objects can use resources within
		// This removes it's refcount at the end because the MapInstance should take over
		MountedVPKs?.Dispose();
		MountedVPKs = await MountMaps( msg.MountedVPKs );
	}

	private static readonly GameObject.SerializeOptions _snapshotSerializeOptions = new() { SceneForNetwork = true };

	/// <summary>
	/// A client has joined and wants a snapshot of the world.
	/// </summary>
	public override void GetSnapshot( Connection source, ref SnapshotMsg msg )
	{
		ThreadSafe.AssertIsMainThread();
		using var _ = PerformanceStats.Timings.Network.Scope();

		msg.Time = Time.Now;

		var analytic = new Api.Events.EventRecord( "SceneNetworkSystem.GetSnapshot" );

		using ( analytic.ScopeTimer( "SceneTime" ) )
		{
			using var blobs = BlobDataSerializer.Capture();
			msg.SceneData = Game.ActiveScene.Serialize( _snapshotSerializeOptions ).ToJsonString();
			msg.BlobData = blobs.ToByteArray();
		}

		using ( analytic.ScopeTimer( "NetworkObjectTime" ) )
		{
			Game.ActiveScene.SerializeNetworkObjects( msg.NetworkObjects );
		}

		var systems = Game.ActiveScene.GetSystems();

		foreach ( var system in systems )
		{
			var snapshotData = WriteGameObjectSystemSnapshot( system );

			var type = new SnapshotMsg.GameObjectSystemData
			{
				SnapshotData = snapshotData,
				TableData = system.WriteDataTable( true ),
				Type = Game.TypeLibrary.GetType( system.GetType() ).Identity,
				Id = system.Id
			};

			msg.GameObjectSystems.Add( type );
		}

		analytic.SetValue( "SceneDataLength", msg.SceneData?.Length ?? 0 );
		analytic.SetValue( "NetworkObjectCount", msg.NetworkObjects?.Count ?? 0 );
		analytic.SetValue( "GameObjectCount", Game.ActiveScene.Directory.GameObjectCount );
		analytic.SetValue( "ComponentCount", Game.ActiveScene.Directory.ComponentCount );
		analytic.SetValue( "Machine", Environment.MachineName );

		analytic.Submit();
	}

	/// <summary>
	/// Write any snapshot data from <see cref="Component.INetworkSnapshot"/> for a <see cref="GameObjectSystem"/>.
	/// </summary>
	private static byte[] WriteGameObjectSystemSnapshot( GameObjectSystem system )
	{
		if ( system is not Component.INetworkSnapshot snapshot )
			return null;

		var bs = ByteStream.Create( 512 );

		try
		{
			snapshot.WriteSnapshot( ref bs );
		}
		catch ( Exception e )
		{
			Log.Warning( e );
		}

		var snapshotData = bs.ToArray();
		bs.Dispose();

		return snapshotData;
	}

	public override void Dispose()
	{
		base.Dispose();

		MountedVPKs?.Dispose();
		MountedVPKs = null;

		if ( Instance == this )
			Instance = null;
	}

	protected string WorkoutMapName()
	{
		if ( Game.ActiveScene is null ) return "<empty>";

		foreach ( var map in Game.ActiveScene.GetAllComponents<MapInstance>() )
		{
			if ( !map.Active ) continue;
			if ( !map.IsLoaded ) continue;

			return map.MapName;
		}

		return Game.ActiveScene.Name;
	}

	protected override void Tick()
	{
		if ( !Networking.IsHost )
			return;

		Networking.MapName = WorkoutMapName();
	}

	private IDisposable MountedVPKs { get; set; }
	private async Task<IDisposable> MountMaps( List<string> maps )
	{
		List<string> vpks = [];

		// Some safety here in-case the input list if null
		if ( maps is not null )
		{
			// Let's see if any are cloud maps and mount those first
			foreach ( var map in maps )
			{
				// Ignore any entries that are null or empty
				if ( string.IsNullOrEmpty( map ) )
					continue;

				if ( map.EndsWith( ".vpk" ) )
				{
					vpks.Add( map );
					continue;
				}

				if ( !Package.TryParseIdent( map, out var parts ) )
					continue;

				var package = await Package.Fetch( map, false );
				if ( package is null )
					continue;

				var fs = await package.MountAsync();
				if ( fs is null ) continue;

				var mapFileName = package.PrimaryAsset;
				vpks.Add( mapFileName );
			}
		}

		foreach ( var vpk in vpks )
			g_pWorldRendererMgr.MountWorldVPK( Path.GetFileNameWithoutExtension( vpk ), Path.ChangeExtension( vpk, ".vpk" ) );

		return new DisposeAction( () =>
		{
			foreach ( var vpk in vpks )
				g_pWorldRendererMgr.UnmountWorldVPK( Path.GetFileNameWithoutExtension( vpk ) );
		} );
	}

	/// <summary>
	/// We have received a snapshot of the world.
	/// </summary>
	public override async Task SetSnapshotAsync( SnapshotMsg msg )
	{
		ThreadSafe.AssertIsMainThread();

		if ( Game.ActiveScene is not null )
		{
			Game.ActiveScene?.Destroy();
			Game.ActiveScene = null;
		}

		Game.ActiveScene = new();
		Game.ActiveScene.StartLoading();

		Time.Now = (float)msg.Time;
		Game.ActiveScene.UpdateTimeFromHost( msg.Time );

		{
			using var blobs = BlobDataSerializer.LoadFromMemory( msg.BlobData );
			using var batchGroup = CallbackBatch.Batch();

			if ( !string.IsNullOrWhiteSpace( msg.SceneData ) )
			{
				var sceneData = JsonNode.Parse( msg.SceneData ).AsObject();
				Game.ActiveScene.Deserialize( sceneData );
			}

			var createdNetworkObjects = new List<Tuple<GameObject, ObjectCreateMsg>>();

			foreach ( var nwo in msg.NetworkObjects )
			{
				if ( nwo is not ObjectCreateMsg oc )
					continue;

				var go = new GameObject();
				go.Deserialize( JsonNode.Parse( oc.JsonData ).AsObject() );
				createdNetworkObjects.Add( new( go, oc ) );
			}

			foreach ( var (go, oc) in createdNetworkObjects )
			{
				go.NetworkSpawnRemote( oc );
			}
		}

		foreach ( var s in msg.GameObjectSystems )
		{
			var type = Game.TypeLibrary.GetTypeByIdent( s.Type );
			var system = Game.ActiveScene.GetSystemByType( type );

			if ( system is null )
				continue;

			system.Id = s.Id;
			system.ReadDataTable( s.TableData );

			ReadGameObjectSystemSnapshot( system, s );
		}

		MountedVPKs?.Dispose();
		MountedVPKs = null;

		// Wait for loading to finish
		if ( Game.ActiveScene is not null )
		{
			await Game.ActiveScene.WaitForLoading();
		}

		if ( Game.ActiveScene.IsValid() )
		{
			Game.ActiveScene.Signal( GameObjectSystem.Stage.SceneLoaded );

			Game.ActiveScene.RunEvent<ISceneStartup>( x => x.OnClientInitialize() );
		}

		Game.IsPlaying = true;
	}

	/// <summary>
	/// Read any snapshot data from <see cref="Component.INetworkSnapshot"/> for a <see cref="GameObjectSystem"/>.
	/// </summary>
	private static void ReadGameObjectSystemSnapshot( GameObjectSystem system, SnapshotMsg.GameObjectSystemData s )
	{
		if ( system is not Component.INetworkSnapshot snapshot )
			return;

		if ( s.SnapshotData is null )
			return;

		var bs = ByteStream.CreateReader( s.SnapshotData );

		try
		{
			snapshot.ReadSnapshot( ref bs );
		}
		catch ( Exception e )
		{
			Log.Warning( e );
		}

		bs.Dispose();
	}

	/// <summary>
	/// Called on the host to decide whether to accept a <see cref="Connection"/>. If any <see cref="Component"/>
	/// that implements this returns false, the connection will be denied.
	/// </summary>
	/// <param name="channel"></param>
	/// <param name="reason">The reason to display to the client.</param>
	public override bool AcceptConnection( Connection channel, ref string reason )
	{
		foreach ( var c in Game.ActiveScene.GetAll<Component.INetworkListener>() )
		{
			if ( !c.AcceptConnection( channel, ref reason ) )
				return false;
		}

		return true;
	}

	public override void OnConnected( Connection client )
	{
		Action queue = default;

		foreach ( var c in Game.ActiveScene.GetAll<Component.INetworkListener>() )
		{
			queue += () => c.OnConnected( client );
		}

		try
		{
			queue?.Invoke();
		}
		catch ( Exception e )
		{
			Log.Error( e, "Exception when calling INetworkListener.OnConnected" );
		}
	}

	public override void OnInitialize()
	{
		if ( !Networking.IsHost )
			return;

		var scene = Game.ActiveScene;

		if ( !scene.IsValid() || scene.IsLoading )
			return;

		var sceneInformation = scene.Components.Get<SceneInformation>();
		OnLoadedScene( sceneInformation?.Title );
	}

	public override void OnJoined( Connection client )
	{
		Action queue = default;

		foreach ( var c in Game.ActiveScene.GetAll<Component.INetworkListener>() )
		{
			queue += () => c.OnActive( client );
		}

		try
		{
			queue?.Invoke();
		}
		catch ( Exception e )
		{
			Log.Error( e, "Exception when calling INetworkListener.OnActive" );
		}
	}

	public override void OnLeave( Connection client )
	{
		DeltaSnapshots.RemoveConnection( client );

		if ( Game.ActiveScene is not null )
		{
			foreach ( var no in Game.ActiveScene.networkedObjects )
			{
				no.RemoveConnection( client.Id );
			}

			foreach ( var system in Game.ActiveScene.GetSystems() )
			{
				system.LocalSnapshotState.RemoveConnection( client.Id );
			}

			if ( Networking.IsHost )
			{
				Action queue = default;

				foreach ( var c in Game.ActiveScene.GetAll<Component.INetworkListener>() )
				{
					queue += () => c.OnDisconnected( client );
				}

				try
				{
					queue?.Invoke();
				}
				catch ( Exception e )
				{
					Log.Error( e, "Exception when calling INetworkListener.OnDisconnected" );
				}
			}
		}

		if ( client.Id == Guid.Empty )
			return;

		DoOrphanedActions( client );
	}

	public override void OnHostChanged( Connection previousHost, Connection newHost )
	{
		var scene = Game.ActiveScene;

		if ( scene.IsValid() )
		{
			foreach ( var system in scene.GetSystems() )
			{
				system.LocalSnapshotState.ClearConnections();
			}

			foreach ( var no in scene.networkedObjects )
			{
				no.OnHostChanged( previousHost, newHost );
			}
		}

		foreach ( var connection in Connection.All )
		{
			connection.Input.Clear();
		}

		DeltaSnapshots?.Reset();
		UserCommand.Reset();
	}

	public override void OnBecameHost( Connection previousHost )
	{
		// Was the host at startup, so this call isn't needed
		if ( previousHost is null || previousHost.Id == Guid.Empty )
			return;

		Log.Info( $"Became the host (previous host was {previousHost})" );
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;

		Action queue = default;
		foreach ( var c in scene.GetAll<Component.INetworkListener>() )
		{
			queue += () => c.OnBecameHost( previousHost );
		}

		try
		{
			queue?.Invoke();
		}
		catch ( Exception e )
		{
			Log.Error( e, "Exception when calling INetworkListener.OnBecameHost" );
		}

		// Don't run orphaned actions if the previous host is still connected.
		if ( previousHost.IsActive )
			return;

		DoOrphanedActions( previousHost );
	}

	internal void DoOrphanedActions( Connection connection )
	{
		Game.ActiveScene?.DoOrphanedActions( connection );
	}

	public override IDisposable Push()
	{
		return Game.ActiveScene is null ? null : Game.ActiveScene.Push();
	}

	private void OnObjectDestroyDescendant( ObjectDestroyDescendantMsg message, Connection source )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() )
			return;

		var go = scene.Directory.FindByGuid( message.Guid );
		if ( !go.IsValid() ) return;

		var root = go.Network.RootGameObject;
		if ( !root.IsValid() ) return;

		if ( root._net is null )
		{
			Log.Warning( $"ObjectDestroyDescendant: Object {root} is not networked" );
			return;
		}

		// Only the owner or the host can do this.
		if ( !root._net.HasControl( source ) && !source.IsHost )
			return;

		go.Destroy();
	}

	private void OnObjectDestroyComponent( ObjectDestroyComponentMsg message, Connection source )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() )
			return;

		var component = scene.Directory.FindComponentByGuid( message.Guid );
		if ( component == null ) return;

		var root = component.Network.RootGameObject;
		if ( !root.IsValid() ) return;

		if ( root._net is null )
		{
			Log.Warning( $"ObjectDestroyComponent: Object {root} is not networked" );
			return;
		}

		// Only the owner or the host can do this.
		if ( !root._net.HasControl( source ) && !source.IsHost )
			return;

		component.Destroy();
	}

	private void OnObjectRefreshAck( ObjectRefreshMsgAck message, Connection source )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() )
			return;

		var obj = scene.Directory.FindByGuid( message.Guid );
		if ( obj is null ) return;

		if ( obj._net is null )
		{
			Log.Warning( $"ObjectRefreshAck: Object {obj} is not networked" );
			return;
		}

		DeltaSnapshots.ClearNetworkObject( obj._net );
	}

	private void OnObjectRefreshDescendant( ObjectRefreshDescendantMsg message, Connection source )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() )
			return;

		var parentObject = scene.Directory.FindByGuid( message.ParentId );
		if ( !parentObject.IsValid() )
			return;

		var root = parentObject.Network.RootGameObject;
		if ( !root.IsValid() ) return;

		if ( root._net is null )
		{
			Log.Warning( $"ObjectRefreshDescendant: Object {root} is not networked" );
			return;
		}

		// Only the owner or the host can do this.
		if ( !root._net.HasControl( source ) && !source.IsHost )
			return;

		var gameObjectJson = JsonNode.Parse( message.JsonData ).AsObject();

		if ( !gameObjectJson.TryGetPropertyValue( GameObject.JsonKeys.Id, out var childId ) )
			return;

		var gameObject = scene.Directory.FindByGuid( childId.GetValue<Guid>() );

		if ( !gameObject.IsValid() )
		{
			gameObject = new GameObject( parentObject, false );
		}
		else if ( gameObject != parentObject )
		{
			gameObject.SetParentFromNetwork( parentObject );
		}

		using ( var _ = CallbackBatch.Batch() )
		using ( BlobDataSerializer.LoadFromMemory( message.BlobData ) )
		{
			gameObject?.Deserialize( gameObjectJson, new GameObject.DeserializeOptions
			{
				IsNetworkRefresh = true,
				IsRefreshing = true
			} );
		}

		root._net.UpdateFromRefresh( source, message.TableData, message.Snapshot );
	}

	private void OnObjectRefreshComponent( ObjectRefreshComponentMsg message, Connection source )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() )
			return;

		var gameObject = scene.Directory.FindByGuid( message.GameObjectId );

		if ( !gameObject.IsValid() )
			return;

		var root = gameObject.Network.RootGameObject;
		if ( !root.IsValid() ) return;

		if ( root._net is null )
		{
			Log.Warning( $"ObjectRefreshComponent: Object {root} is not networked" );
			return;
		}

		// Only the owner or the host can do this.
		if ( !root._net.HasControl( source ) && !source.IsHost )
			return;

		var componentJson = JsonNode.Parse( message.JsonData ).AsObject();

		if ( !componentJson.TryGetPropertyValue( Component.JsonKeys.Id, out var componentId ) )
			return;

		var component = scene.Directory.FindComponentByGuid( componentId.GetValue<Guid>() );

		if ( !component.IsValid() )
		{
			var componentTypeName = componentJson.GetPropertyValue( Component.JsonKeys.Type, "" );
			var componentType = Game.TypeLibrary.GetType<Component>( componentTypeName, true );

			if ( componentType is null || componentType.TargetType.IsAbstract )
			{
				Log.Warning( $"TypeLibrary couldn't find {nameof( Component )} type {componentTypeName}" );
				return;
			}

			try
			{
				component = gameObject.Components.Create( componentType, false );
			}
			catch ( Exception e )
			{
				Log.Error( e );
			}
		}
		else if ( component.GameObject != gameObject )
		{
			return;
		}

		using ( var _ = CallbackBatch.Batch() )
		using ( BlobDataSerializer.LoadFromMemory( message.BlobData ) )
		{
			component?.Deserialize( componentJson );
		}

		root._net.UpdateFromRefresh( source, message.TableData, message.Snapshot );
	}


	private void OnObjectRefresh( ObjectRefreshMsg message, Connection source )
	{
		NetworkDebugSystem.Current?.Record( NetworkDebugSystem.MessageType.Refresh, message );
		NetworkDebugSystem.Current?.Track( "OnObjectRefresh", message );

		// Is this a request from someone?
		if ( source is not null && !source.CanRefreshObjects )
			return;

		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;

		var obj = scene.Directory.FindByGuid( message.Guid );
		if ( obj is null ) return;

		if ( obj._net is null )
		{
			Log.Warning( $"ObjectRefresh: Object {obj} is not networked" );
			return;
		}

		if ( obj._net.IsUnowned )
		{
			// If we're unowned and the source is not the host, we can't refresh.
			if ( !source.IsHost )
				return;
		}
		else
		{
			// If the source is not the owner and not the host, we can't refresh.
			if ( !source.IsHost && obj._net.Owner != source.Id )
				return;
		}

		obj._net.OnRefreshMessage( source, message );
	}

	private void OnObjectCreateBatch( ObjectCreateBatchMsg message, Connection source, Guid msgId )
	{
		NetworkDebugSystem.Current?.Record( NetworkDebugSystem.MessageType.Spawn, message );
		NetworkDebugSystem.Current?.Track( "OnObjectCreateBatch", message );

		// If we haven't even loaded a scene yet, this message was not sent in order (we don't even have the snapshot yet.)
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() )
			return;

		// Is this a request from someone?
		if ( source is not null && !source.CanSpawnObjects )
			return;

		using ( CallbackBatch.Batch() )
		{
			foreach ( var msg in message.CreateMsgs )
			{
				using ( BlobDataSerializer.LoadFromMemory( msg.BlobData ) )
				{
					var go = new GameObject();
					go.Deserialize( JsonNode.Parse( msg.JsonData ).AsObject() );
					go.NetworkSpawnRemote( msg );
				}
			}
		}
	}

	private void OnObjectCreate( ObjectCreateMsg message, Connection source, Guid msgId )
	{
		NetworkDebugSystem.Current?.Record( NetworkDebugSystem.MessageType.Spawn, message );
		NetworkDebugSystem.Current?.Track( "OnObjectCreate", message );

		// If we haven't even loaded a scene yet, this message was not sent in order (we don't even have the snapshot yet.)
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() )
			return;

		// Is this a request from someone?
		if ( source is not null && !source.CanSpawnObjects )
			return;

		var go = new GameObject();

		using ( CallbackBatch.Batch() )
		using ( BlobDataSerializer.LoadFromMemory( message.BlobData ) )
		{
			go.Deserialize( JsonNode.Parse( message.JsonData ).AsObject() );
			go.NetworkSpawnRemote( message );
		}
	}

	private void OnNetworkTableChanges( SceneNetworkTableMsg message, Connection source, Guid msgId )
	{
		NetworkDebugSystem.Current?.Record( NetworkDebugSystem.MessageType.SyncVars, message );

		var scene = Game.ActiveScene;
		if ( !scene.IsValid() )
			return;

		var system = scene.Directory.FindSystemByGuid( message.Guid );
		if ( system is null )
			return;

		// Can we receive network table changes from this source?
		if ( !source.IsHost )
			return;

		system.ReadDataTable( message.TableData );
	}

	private void OnNetworkTableChanges( ObjectNetworkTableMsg message, Connection source, Guid msgId )
	{
		NetworkDebugSystem.Current?.Record( NetworkDebugSystem.MessageType.SyncVars, message );

		var scene = Game.ActiveScene;
		if ( !scene.IsValid() )
			return;

		var obj = scene.Directory.FindByGuid( message.Guid );
		if ( obj is null )
			return;

		if ( obj._net is null )
		{
			Log.Warning( $"ObjectNetworkTable: Object {obj} is not networked" );
			return;
		}

		// Can we receive network table changes from this source?
		if ( !source.IsHost && source.Id != obj._net.Owner )
			return;

		obj._net.OnNetworkTableMessage( message );
	}

	private void OnObjectDetach( ObjectDetachMsg message, Connection source, Guid msgId )
	{
		NetworkDebugSystem.Current?.Track( "OnObjectDetach", message );

		var scene = Game.ActiveScene;
		if ( !scene.IsValid() )
			return;

		var obj = scene.Directory.FindByGuid( message.Guid );
		if ( obj is null )
			return;

		if ( obj._net is null )
		{
			// We can't just destroy arbitrary game objects.
			Log.Warning( $"OnObjectDetach: Object {obj} is not networked" );
			return;
		}

		if ( !source.IsHost )
		{
			Log.Warning( $"OnObjectDetach: Only the host can detach networked objects. {source.DisplayName} attempted to detach {obj.Name}." );
			return;
		}

		obj.DetachFromNetwork();
		obj.NetworkMode = message.Mode;
	}

	private void OnObjectDestroy( ObjectDestroyMsg message, Connection source, Guid msgId )
	{
		NetworkDebugSystem.Current?.Track( "OnObjectDestroy", message );

		var scene = Game.ActiveScene;
		if ( !scene.IsValid() )
			return;

		var obj = scene.Directory.FindByGuid( message.Guid );
		if ( obj is null )
			return;

		if ( obj._net is null )
		{
			// We can't just destroy arbitrary game objects.
			Log.Warning( $"ObjectDestroy: Object {obj} is not networked" );
			return;
		}

		if ( obj._net.IsUnowned )
		{
			// If we're unowned and the source is not the host, we can't destroy.
			if ( !source.IsHost )
			{
				Log.Warning( $"ObjectDestroy: Only the host can destroy unowned networked objects. {source.DisplayName} attempted to destroy {obj.Name}." );
				return;
			}
		}
		else
		{
			// If the source is not the owner and not the host, we can't destroy.
			if ( !source.IsHost && obj._net.Owner != source.Id )
			{
				Log.Warning( $"ObjectDestroy: {source.DisplayName} attempted to destroy {obj.Name} but is not the owner. Owner is {obj._net.Owner}." );
				return;
			}

			// If the source is the owner but not the host, check if they have permission to destroy.
			if ( !source.IsHost && !source.CanDestroyObjects )
			{
				Log.Warning( $"ObjectDestroy: {source.DisplayName} attempted to destroy {obj.Name} but does not have CanDestroyObjects permission enabled." );
				return;
			}
		}

		obj._net.OnNetworkDestroy();
	}

	private void OnObjectMessage( ObjectRpcMsg rpc, Connection source, Guid msgId )
	{
		Rpc.IncomingInstanceRpcMsg( rpc, source );
	}

	private void OnSceneRpc( SceneRpcMsg message, Connection source, Guid msgId )
	{
		Rpc.IncomingInstanceRpcMsg( message, source );
	}

	private void OnStaticRpc( StaticRpcMsg message, Connection source, Guid msgId )
	{
		Rpc.IncomingStaticRpcMsg( message, source );
	}

	/// <summary>
	/// A heartbeat has been received from the host. We should make sure our times are in sync.
	/// </summary>
	internal override void OnHeartbeat( float serverGameTime )
	{
		Game.ActiveScene?.UpdateTimeFromHost( serverGameTime );
	}

	/// <summary>
	/// We've received a cull state change for a networked object.
	/// </summary>
	internal override void OnCullStateChangeMessage( ByteStream bs, Connection source )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;

		NetworkDebugSystem.Current?.Record( NetworkDebugSystem.MessageType.Culling, bs.Length );

		var objectId = bs.Read<Guid>();
		var isCulled = bs.Read<bool>();

		var go = scene.Directory.FindByGuid( objectId );
		if ( !go.IsValid() ) return;

		if ( go.IsNetworkCulled == isCulled )
			return;

		var ownerId = go.Network.OwnerId;
		var isOwner = (source.Id == ownerId) || (ownerId == Guid.Empty && source.IsHost);

		if ( !isOwner )
			return;

		go.IsNetworkCulled = isCulled;
		go.UpdateNetworkCulledState();
	}

	/// <summary>
	/// A delta snapshot message has been received from another connection.
	/// </summary>
	internal override void OnDeltaSnapshotMessage( InternalMessageType type, ByteStream bs, Connection source )
	{
		if ( type == InternalMessageType.DeltaSnapshot )
		{
			NetworkDebugSystem.Current?.Record( NetworkDebugSystem.MessageType.SyncVars, bs.Length );
			DeltaSnapshots.OnDeltaSnapshot( source, bs );
		}
		else if ( type == InternalMessageType.DeltaSnapshotAck )
		{
			DeltaSnapshots.OnDeltaSnapshotAck( source, bs );
		}
		else if ( type == InternalMessageType.DeltaSnapshotCluster )
		{
			NetworkDebugSystem.Current?.Record( NetworkDebugSystem.MessageType.SyncVars, bs.Length );
			DeltaSnapshots.OnDeltaSnapshotCluster( source, bs );
		}
		else if ( type == InternalMessageType.DeltaSnapshotClusterAck )
		{
			DeltaSnapshots.OnDeltaSnapshotClusterAck( source, bs );
		}
	}
}

/// <summary>
/// When a client has sent an acknowledgement that they've received a refresh message for
/// a networked object.
/// </summary>
[Expose]
struct ObjectRefreshMsgAck
{
	public Guid Guid { get; set; }
}

/// <summary>
/// When a <see cref="Component"/> in the hierarchy of a networked object has
/// been destroyed.
/// </summary>
[Expose]
struct ObjectDestroyComponentMsg
{
	public Guid Guid { get; set; }
}

/// <summary>
/// When a <see cref="GameObject"/> in the hierarchy of a networked object has
/// been destroyed.
/// </summary>
[Expose]
struct ObjectDestroyDescendantMsg
{
	public Guid Guid { get; set; }
}

/// <summary>
/// When a <see cref="GameObject"/> in the hierarchy of a networked object has
/// been added or changed.
/// </summary>
[Expose]
struct ObjectRefreshDescendantMsg
{
	public string JsonData { get; set; }
	public byte[] BlobData { get; set; }
	public byte[] TableData { get; set; }
	public byte[] Snapshot { get; set; }
	public Guid ParentId { get; set; }
	public Guid GameObjectId { get; set; }
}

/// <summary>
/// When a <see cref="Component"/> in the hierarchy of a networked object has
/// been added or changed.
/// </summary>
[Expose]
struct ObjectRefreshComponentMsg
{
	public string JsonData { get; set; }
	public byte[] BlobData { get; set; }
	public byte[] TableData { get; set; }
	public byte[] Snapshot { get; set; }
	public Guid GameObjectId { get; set; }
}

/// <summary>
/// When a networked object has been refreshed. This is a full update message for that
/// networked object. Any new GameObjects or Components in the hierarchy will be
/// created and existing ones will be updated.
/// </summary>
[Expose]
struct ObjectRefreshMsg
{
	public string JsonData { get; set; }
	public byte[] BlobData { get; set; }
	public byte[] TableData { get; set; }
	public byte[] Snapshot { get; set; }
	public Guid Parent { get; set; }
	public Guid Guid { get; set; }
}

[Expose]
struct LoadSceneBeginMsg
{
	public List<string> MountedVPKs { get; set; }
	public bool ShowLoadingScreen { get; set; }
	public Guid SceneId { get; set; }
	public Guid Id { get; set; }
}

[Expose]
struct LoadSceneRequestSnapshotMsg
{
	public Guid SceneId { get; set; }
	public Guid Id { get; set; }
}

[Expose]
struct LoadSceneSnapshotMsg
{
	public SnapshotMsg Snapshot { get; set; }
	public Guid SceneId { get; set; }
	public Guid Id { get; set; }
}

[Expose]
struct SceneLoadedMsg
{
	public Guid SceneId { get; set; }
	public Guid Id { get; set; }
}

[Expose]
struct ObjectCreateBatchMsg
{
	public ObjectCreateMsg[] CreateMsgs { get; set; }
}

[Expose]
struct ObjectCreateMsg
{
	public ushort SnapshotVersion { get; set; }
	public string JsonData { get; set; }
	public byte[] BlobData { get; set; }
	public Transform Transform { get; set; }
	public Guid Guid { get; set; }
	public Guid Creator { get; set; }
	public Guid Parent { get; set; }
	public Guid Owner { get; set; }
	public byte[] TableData { get; set; }
	public bool Enabled { get; set; }
}

[Expose]
struct ObjectNetworkTableMsg
{
	public Guid Guid { get; set; }
	public byte[] TableData { get; set; }
}

[Expose]
struct SceneNetworkTableMsg
{
	public Guid Guid { get; set; }
	public byte[] TableData { get; set; }
}

[Expose]
struct ObjectDetachMsg
{
	public NetworkMode Mode { get; set; }
	public Guid Guid { get; set; }
}

[Expose]
struct ObjectDestroyMsg
{
	public Guid Guid { get; set; }
}

[Expose]
struct SceneRpcMsg
{
	public Guid Guid { get; set; }
	public int MethodIdentity { get; set; }
	public object[] Arguments { get; set; }
	public int[] GenericArguments { get; set; }
}

[Expose]
struct ObjectRpcMsg
{
	public Guid Guid { get; set; }
	public Guid ComponentId { get; set; }
	public int MethodIdentity { get; set; }
	public object[] Arguments { get; set; }
	public int[] GenericArguments { get; set; }
}

[Expose]
struct StaticRpcMsg
{
	public int MethodIdentity { get; set; }
	public object[] Arguments { get; set; }
	public int[] GenericArguments { get; set; }
}
