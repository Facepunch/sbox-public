using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sandbox.Internal;
using Sandbox.Network;

namespace SceneTests.GameObjects;

using static GlobalGameNamespace;

[TestClass]
public class NetworkDormancyTest
{
	private const float StartTime = 1000f;
	private TypeLibrary _oldTypeLibrary;
	private float _oldTimeNow;
	private double _oldTimeNowDouble;
	private double? _oldRealTime;
	private Scene _scene;
	private IDisposable _scope;
	private ClientAndHost _peers;

	[TestInitialize]
	public void TestInitialize()
	{
		_oldTypeLibrary = Game.TypeLibrary;
		Game.TypeLibrary = SharedTypeLibrary.EngineAndTests;
		JsonUpgrader.UpdateUpgraders( Game.TypeLibrary );

		_oldTimeNow = Time.Now;
		_oldTimeNowDouble = Time.NowDouble;
		_oldRealTime = RealTime.SimulatedNow;
		SetTime( StartTime );

		_scene = new Scene();
		_scope = _scene.Push();

		_peers = new ClientAndHost( TypeLibrary );
		_peers.BecomeHost();
	}

	[TestCleanup]
	public void TestCleanup()
	{
		_peers.Dispose();
		_scope.Dispose();

		Game.TypeLibrary = _oldTypeLibrary;
		Time.Now = _oldTimeNow;
		Time.NowDouble = _oldTimeNowDouble;
		RealTime.SimulatedNow = _oldRealTime;
	}

	[TestMethod]
	[DataRow( "Transform" )]
	[DataRow( "Nested" )]
	[DataRow( "Enabled" )]
	[DataRow( "Parent" )]
	[DataRow( "NetList" )]
	[DataRow( "NetDictionary" )]
	public void ReplicatedChangesWakeImmediatelyAndRestartGrace( string change )
	{
		var parent = new GameObject();
		parent.NetworkSpawn();

		var go = change == "Nested" ? new GameObject( parent ) : new GameObject();
		var containers = go.Components.Create<DormancyTestComponent>();
		containers.SyncList = new NetList<int>();
		containers.SyncDictionary = new NetDictionary<int, int>();
		go.NetworkSpawn();

		Settle();
		Assert.IsFalse( go._net.IsDirty );

		var now = Time.NowDouble;

		for ( var step = 1; step <= 2; step++ )
		{
			if ( step == 2 )
			{
				SetGameTime( now + 0.75 );
				UpdateScene();

				Assert.IsTrue( go._net.IsDirty, "Acknowledgement must not end the awake grace" );
			}

			if ( change is "Transform" or "Nested" )
				go.LocalPosition = Vector3.Up * 128f * step;
			else if ( change == "Enabled" )
				go.Enabled = step == 2;
			else if ( change == "Parent" )
				go.Parent = step == 1 ? parent : null;
			else if ( change == "NetList" )
				containers.SyncList.Add( step );
			else if ( change == "NetDictionary" )
				containers.SyncDictionary[step] = step;

			_peers.Client.Messages.Clear();
			UpdateScene();

			Assert.IsTrue( change is "NetList" or "NetDictionary"
				? _peers.Client.Messages.Any( x => x.Payload is ObjectNetworkTableMsg )
				: ClusterCount( _peers.Client ) > 0, $"{change} change {step} must send immediately" );
			Assert.IsTrue( go._net.IsDirty, "A change must stay scheduled until acknowledged" );

			if ( change == "Nested" )
				Assert.IsFalse( parent._net.IsDirty, "The child must own its snapshot" );

			Acknowledge( _peers.Client );
			UpdateScene();

			Assert.IsTrue( go._net.IsFullyUpdated );
		}

		SetGameTime( now + 1.25 );
		UpdateScene();

		Assert.IsTrue( go._net.IsDirty, "The latest change must restart the grace period" );

		RealTime.SimulatedNow = RealTime.NowDouble + 10;
		UpdateScene();

		Assert.IsTrue( go._net.IsDirty, "The grace period runs on game time, so frozen game time keeps it awake" );

		SetGameTime( now + 1.8 );
		UpdateScene();

		Assert.IsFalse( go._net.IsDirty, "Must sleep after a second of game time without changes" );

		for ( var i = 0; i < 3; i++ )
			UpdateScene();

		Assert.AreEqual( 0, ClusterCount( _peers.Client ), "Idle objects must not resend acknowledged state" );
	}

	[TestMethod]
	[DataRow( typeof( int ) )]
	[DataRow( typeof( List<int> ) )]
	[DataRow( typeof( Dictionary<int, int> ) )]
	[DataRow( typeof( int[] ) )]
	public void PolledEntriesNeverSleep( Type type )
	{
		var go = new GameObject();
		go.NetworkSpawn();

		// Only the scalar is an explicit query; collections must be classified as polled on their own.
		object value = type.IsArray ? Array.CreateInstance( type.GetElementType(), 1 ) : Activator.CreateInstance( type );
		RegisterQuery( go, type, () => value, needsQuery: type == typeof( int ) );

		for ( var i = 0; i < 3; i++ )
		{
			SetGameTime( Time.NowDouble + 2 );
			UpdateScene();
			Acknowledge( _peers.Client );
		}

		Assert.IsTrue( go._net.IsDirty, "Entries that can't report their own changes must keep the object awake" );
		Assert.IsFalse( go._net.IsDeltaDormant );

		if ( value is List<int> list )
			list.Add( 1 );
		else if ( value is Dictionary<int, int> dictionary )
			dictionary[1] = 1;
		else if ( value is int[] array )
			array[0] = 1;
		else
			value = 1;

		_peers.Client.Messages.Clear();
		UpdateScene();

		Assert.IsTrue( ClusterCount( _peers.Client ) > 0, "An unreported change must still be sent" );
	}

	[TestMethod]
	public void ClientProxyWithPolledEntriesLeavesTheDirtyList()
	{
		_peers.BecomeClient();

		var go = new GameObject();
		go.Components.Create<PolledTestComponent>();
		go.NetworkSpawn( _peers.Host );
		Assert.IsTrue( go._net.IsProxy );
		Assert.IsTrue( go._net.IsDirty, "Precondition: spawning schedules the proxy" );

		for ( var i = 0; i < 3; i++ )
			UpdateScene();

		Assert.IsFalse( go._net.IsDirty, "A proxy we never send must not stay scheduled because of polled entries" );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void HostPollsOnlyEntriesItControls( bool fromHost )
	{
		var go = new GameObject();
		if ( fromHost )
			go.Components.Create<HostPolledTestComponent>();
		else
			go.Components.Create<PolledTestComponent>();

		go.NetworkSpawn( _peers.Client );
		Assert.IsTrue( go._net.IsProxy );

		Settle();

		Assert.AreEqual( fromHost, go._net.IsDirty, "Only polled entries the host controls should keep it polling" );
	}

	[TestMethod]
	public void ObjectStaysScheduledUntilLatestStateIsAcknowledged()
	{
		var go = new GameObject();
		go.NetworkSpawn();
		Settle();

		go.WorldPosition = Vector3.Up * 128f;
		UpdateScene();

		go.WorldPosition = Vector3.Up * 256f;
		Acknowledge( _peers.Client );
		UpdateScene();

		Assert.IsFalse( go._net.IsFullyUpdated, "An ACK for the old position must not acknowledge the new position" );
		Assert.IsTrue( ClusterCount( _peers.Client ) > 0 );

		_peers.Client.Messages.Clear();
		SetTime( Time.Now + 1.1f );
		UpdateScene();

		Assert.IsTrue( ClusterCount( _peers.Client ) > 0, "The lost final update must be retransmitted" );
		Assert.IsTrue( go._net.IsDirty );

		Acknowledge( _peers.Client );
		UpdateScene();

		Assert.IsTrue( go._net.IsFullyUpdated );
		Assert.IsFalse( go._net.IsDirty );
	}

	[TestMethod]
	public void VisibilityChecksWakeHiddenObjectWhileGameTimeIsPaused()
	{
		var (go, visibility) = CreateDormantHiddenObject();

		_peers.Client.Messages.Clear();
		visibility.Visible = true;
		var now = RealTime.NowDouble;

		for ( var i = 1; i <= 4; i++ )
		{
			RealTime.SimulatedNow = now + i * 0.25;
			UpdateScene();
		}

		Assert.IsFalse( go._net.IsDeltaDormant, "Visibility must wake in real time even while paused" );
		Assert.IsTrue( ClusterCount( _peers.Client ) > 0 );
	}

	[TestMethod]
	public void SleepingObjectBecomesVisibleOnTheNextTick()
	{
		var (go, visibility) = CreateDormantHiddenObject();

		SetTime( Time.Now + 0.01f );
		UpdateScene();

		_peers.Client.Messages.Clear();
		visibility.Visible = true;
		SetTime( Time.Now + 0.01f );
		UpdateScene();

		Assert.IsFalse( go._net.IsDeltaDormant, "Visibility must be re-checked every tick" );
		Assert.IsTrue( ClusterCount( _peers.Client ) > 0 );
	}

	[TestMethod]
	public void RefreshThatEnablesCullingKeepsVisibilityChecked()
	{
		var go = new GameObject();
		go.Components.Create<VisibilityController>();
		go.NetworkSpawn();
		Assert.IsTrue( go.Network.AlwaysTransmit );

		var json = go.Serialize();
		json[GameObject.JsonKeys.AlwaysTransmit] = false;
		go.NetworkRefresh( json );
		Assert.IsFalse( go.Network.AlwaysTransmit );

		for ( var i = 0; i < 60 && !go._net.IsDeltaDormant; i++ )
		{
			SetTime( StartTime + i * 0.1f );
			UpdateScene();
			Acknowledge( _peers.Client );
		}

		Assert.IsTrue( go._net.IsDeltaDormant, "A settled object must still be culled after a refresh turns culling on" );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void EnablingAlwaysTransmitWakesHiddenObject( bool refresh )
	{
		var (go, _) = CreateDormantHiddenObject();
		Assert.IsFalse( go._net.IsDirty, "Precondition: the hidden object has settled" );
		var clearGeneration = go._net.LocalSnapshotState.ClearGeneration;
		_peers.Client.Messages.Clear();

		if ( refresh )
		{
			var json = go.Serialize();
			json[GameObject.JsonKeys.AlwaysTransmit] = true;
			go.NetworkRefresh( json );
		}
		else
		{
			go.Network.AlwaysTransmit = true;
		}

		Assert.IsTrue( go._net.IsDirty, "Changing transmit policy must schedule the hidden object" );
		Assert.IsTrue( go._net.LocalSnapshotState.ClearGeneration > clearGeneration, "Old acknowledgements must not suppress transmission to previously culled peers" );
		UpdateScene();

		Assert.IsFalse( go._net.IsDeltaDormant );
		var uncullReceived = false;
		_peers.Client.ProcessMessages( InternalMessageType.SetCullState, reader =>
		{
			Assert.AreEqual( go.Id, reader.Read<Guid>() );
			Assert.IsFalse( reader.Read<bool>() );
			uncullReceived = true;
		} );
		Assert.IsTrue( uncullReceived, "The client must be told the object is visible again" );
		Assert.IsTrue( ClusterCount( _peers.Client ) > 0, "The previously hidden state must be sent immediately" );
	}

	[TestMethod]
	public void RefreshWakesSettledObject()
	{
		var go = new GameObject();
		go.NetworkSpawn();

		Settle();
		Assert.IsFalse( go._net.IsDirty );

		go.Network.Refresh();

		Assert.IsTrue( go._net.IsDirty, "A refresh must schedule the object so rejected values are sent again" );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ChangesWhileAloneAreNotReplayedAfterJoinBaseline( bool dictionary )
	{
		var go = new GameObject();
		go.NetworkSpawn();

		using IDisposable source = dictionary ? new NetDictionary<int, int>() : new NetList<int>();
		using IDisposable replica = dictionary ? new NetDictionary<int, int>() : new NetList<int>();

		go._net.dataTable.Register( 100, new NetworkTable.Entry
		{
			TargetType = source.GetType(),
			GetValue = () => source
		} );

		using var receiver = new NetworkTable();
		receiver.Register( 100, new NetworkTable.Entry
		{
			TargetType = replica.GetType(),
			GetValue = () => replica
		} );

		void Apply( byte[] data )
		{
			var reader = ByteStream.CreateReader( data );
			receiver.Read( ref reader );
			reader.Dispose();
		}

		Settle();
		_peers.Client.State = Connection.ChannelState.Unconnected;
		UpdateScene();

		if ( source is NetList<int> sourceList )
			sourceList.Add( 10 );
		else if ( source is NetDictionary<int, int> sourceDictionary )
			sourceDictionary[1] = 10;

		UpdateScene();

		Assert.IsTrue( source is INetworkSerializer { HasChanges: false }, "No-recipient changes must be retired before a peer joins" );

		var baseline = ByteStream.Create( 256 );
		go._net.dataTable.WriteAllReliable( ref baseline );
		Apply( baseline.ToArray() );
		baseline.Dispose();

		_peers.Client.Messages.Clear();
		_peers.Client.State = Connection.ChannelState.Connected;
		UpdateScene();

		foreach ( var message in _peers.Client.Messages.Select( x => x.Payload ).OfType<ObjectNetworkTableMsg>() )
			Apply( message.TableData );

		if ( source is not IEnumerable sourceItems || replica is not IEnumerable replicaItems )
		{
			Assert.Fail( "Source and replica must both be collections" );
			return;
		}

		CollectionAssert.AreEqual( sourceItems.Cast<object>().ToArray(), replicaItems.Cast<object>().ToArray() );
	}

	[TestMethod]
	public void NewConnectionAndReconnectionReceiveSleepingObject()
	{
		var go = new GameObject();
		go.NetworkSpawn();
		Settle();

		var latecomer = new TestConnection( Guid.NewGuid() );
		var network = SceneNetworkSystem.Instance.NetworkSystem;
		network.OnConnected( latecomer );
		latecomer.State = Connection.ChannelState.Connected;

		UpdateScene();
		Assert.IsTrue( ClusterCount( latecomer ) > 0, "A new connection must receive an already sleeping object" );
		Assert.IsFalse( go._net.IsFullyUpdated );

		Settle( latecomer );
		Assert.IsFalse( go._net.IsDirty );

		go._net.RemoveConnection( latecomer.Id );
		SceneNetworkSystem.Instance.DeltaSnapshots.RemoveConnection( latecomer );
		network.OnDisconnected( latecomer );
		UpdateScene();

		var reconnected = new TestConnection( latecomer.Id );
		network.OnConnected( reconnected );
		reconnected.State = Connection.ChannelState.Connected;
		UpdateScene();

		Assert.IsTrue( ClusterCount( reconnected ) > 0, "A reconnected peer must not inherit old acknowledgements" );
		Assert.IsFalse( go._net.IsFullyUpdated );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void DestroyedPendingObjectsAreRetired( bool offline )
	{
		var survivor = new GameObject();
		survivor.NetworkSpawn();
		Settle();

		var pending = _scene._dirtyNetworkObjects;
		var system = SceneNetworkSystem.Instance;

		try
		{
			if ( offline )
				SceneNetworkSystem.Instance = null;

			for ( var i = 0; i < 4; i++ )
			{
				var go = new GameObject();
				go.Components.Create<DormancyTestComponent>();
				go.NetworkSpawn();
				go.DestroyImmediate();
			}

			survivor.WorldPosition = Vector3.Up * 128f;
			_peers.Client.Messages.Clear();
			UpdateScene();

			CollectionAssert.AreEqual( new List<NetworkObject> { survivor._net }, pending, "Cleanup must retain only live pending state" );
		}
		finally
		{
			SceneNetworkSystem.Instance = system;
		}

		UpdateScene();

		Assert.AreEqual( 1, _scene.networkedObjects.Count );
		Assert.IsTrue( ClusterCount( _peers.Client ) > 0, "Surviving pending state must replicate" );

		Settle();

		Assert.AreEqual( 0, pending.Count, "Acknowledged survivors and destroyed entries must leave the pending list" );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void VisibilityCallbackCanSpawnOrDestroyNetworkObjects( bool spawn )
	{
		GameObject[] objects = [new GameObject(), new GameObject()];
		var armed = false;
		var changed = false;

		foreach ( var go in objects )
		{
			var visibility = go.Components.Create<VisibilityController>();
			visibility.OnCheck = () =>
			{
				if ( !armed || changed ) return;

				changed = true;
				if ( spawn )
				{
					var spawned = new GameObject();
					spawned.NetworkSpawn();
					spawned.Network.AlwaysTransmit = false;
				}
				else
				{
					objects.First( x => x != go ).DestroyImmediate();
				}
			};

			go.NetworkSpawn();
			go.Network.AlwaysTransmit = false;
		}

		Settle();
		Assert.IsTrue( objects.All( x => !x._net.IsDirty ), "Precondition: both objects are only visibility probed" );

		armed = true;
		UpdateScene();

		Assert.IsTrue( changed );
		Assert.AreEqual( spawn ? 3 : 1, _scene.networkedObjects.Count );
	}

	[TestMethod]
	public void RegisteringPolledEntriesWakesSettledObject()
	{
		var go = new GameObject();
		go.NetworkSpawn();
		Settle();
		Assert.IsFalse( go._net.IsDirty, "Precondition: the object has settled" );

		go.Components.Create<PolledTestComponent>();
		go._net.RegisterPropertiesRecursive();

		Assert.IsTrue( go._net.IsDirty, "Entries that can't report changes must put the object back in the capture set" );
	}

	[TestMethod]
	public void MutationAfterCaptureCannotBeDroppedByAcknowledgements()
	{
		var subject = new GameObject();
		var later = new GameObject();
		subject.NetworkSpawn();
		later.NetworkSpawn();

		var armed = false;
		RegisterQuery( later, typeof( int ), () =>
		{
			if ( armed )
			{
				armed = false;
				subject.WorldPosition = Vector3.Up * 256f;
			}

			return 0;
		} );

		Settle();

		subject.WorldPosition = Vector3.Up * 128f;
		Connection[] targets = [_peers.Client];
		SceneNetworkSystem.Instance.DeltaSnapshots.Send( [subject._net], targets );
		Acknowledge( _peers.Client );

		armed = true;
		SceneNetworkSystem.Instance.DeltaSnapshots.Send( [subject._net, later._net], targets );

		Assert.IsFalse( subject._net.IsFullyUpdated, "An earlier capture cannot acknowledge a later mutation" );

		_peers.Client.Messages.Clear();
		UpdateScene();

		Assert.IsTrue( ClusterCount( _peers.Client ) > 0, "The later mutation must still be transmitted" );

		Settle();

		Assert.IsFalse( subject._net.IsDirty );
	}

	[TestMethod]
	public void WorkerTransformAfterNetworkUpdateIsSentInTheSameFrame()
	{
		var go = new GameObject();
		go.NetworkSpawn();
		Settle();
		Assert.IsFalse( go._net.IsDirty, "Precondition: the object has settled" );

		_scene._nextNetworkUpdate = double.NegativeInfinity;
		_scene.SceneNetworkUpdate();
		_peers.Client.Messages.Clear();

		// Animation workers finish after the start-of-frame network update, before snapshots are captured.
		Task.Run( () => go.Transform.SetLocalTransformFast( new Transform( Vector3.Up * 128f ) ) ).GetAwaiter().GetResult();
		_scene.SceneNetworkSnapshots();

		Assert.IsTrue( go._net.IsDirty, "The worker wake must be applied before this frame's capture" );
		Assert.IsTrue( ClusterCount( _peers.Client ) > 0, "The moved object must replicate without waiting for another network tick" );
	}

	[TestMethod]
	public void TransformsSetOffTheMainThreadAreEachScheduledOnce()
	{
		var objects = new GameObject[1024];
		for ( var i = 0; i < objects.Length; i++ )
		{
			objects[i] = new GameObject();
			objects[i].NetworkSpawn();
		}

		Settle();
		Assert.AreEqual( 0, _scene._dirtyNetworkObjects.Count, "Precondition: every object has settled" );

		// Animation sets bone and attachment transforms from worker threads like this.
		Parallel.For( 0, objects.Length, i => objects[i].Transform.SetLocalTransformFast( new Transform( Vector3.Up * (i + 1) ) ) );

		_peers.Client.Messages.Clear();
		UpdateScene();

		CollectionAssert.AreEquivalent( objects.Select( x => x._net ).ToList(), _scene._dirtyNetworkObjects );
		Assert.IsTrue( ClusterCount( _peers.Client ) > 0, "Every moved object must replicate" );
	}

	private (GameObject Object, VisibilityController Visibility) CreateDormantHiddenObject()
	{
		var go = new GameObject();
		var visibility = go.Components.Create<VisibilityController>();
		go.NetworkSpawn();
		go.Network.AlwaysTransmit = false;

		for ( var i = 0; i < 45; i++ )
		{
			SetTime( StartTime + i * 0.1f );
			UpdateScene();
			Acknowledge( _peers.Client );
		}

		go.WorldPosition = Vector3.Up * 50f;
		for ( var i = 0; i < 15 && !go._net.IsDeltaDormant; i++ )
		{
			SetTime( Time.Now + 0.1f );
			UpdateScene();
		}

		Assert.IsTrue( go._net.IsDeltaDormant );
		return (go, visibility);
	}

	private static void RegisterQuery( GameObject go, Type type, Func<object> getter, bool needsQuery = true )
	{
		go._net.dataTable.Register( 100, new NetworkTable.Entry
		{
			TargetType = type,
			NeedsQuery = needsQuery,
			GetValue = getter
		} );
		go._net.MarkDirty();
	}

	private static void SetTime( float now )
	{
		Time.Now = now;
		Time.NowDouble = now;
		RealTime.SimulatedNow = now;
	}

	private static void SetGameTime( double now )
	{
		Time.Now = (float)now;
		Time.NowDouble = now;
	}

	private void UpdateScene()
	{
		// Bypass only the real-time rate limiter, not the scene scheduler or sender.
		_scene._nextNetworkUpdate = double.NegativeInfinity;

		_scene.SceneNetworkUpdate();
		_scene.SceneNetworkSnapshots();
	}

	private static int ClusterCount( TestConnection connection )
		=> connection.Messages.Count( x => x.Type == InternalMessageType.DeltaSnapshotCluster );

	private void Settle( params TestConnection[] extra )
	{
		for ( var i = 0; i < 3; i++ )
		{
			UpdateScene();
			Acknowledge( _peers.Client );

			foreach ( var connection in extra )
				Acknowledge( connection );
		}

		SetTime( (float)RealTime.NowDouble + 1.1f );
		UpdateScene();
	}

	private static void Acknowledge( TestConnection connection )
	{
		connection.ProcessMessages( InternalMessageType.DeltaSnapshotCluster, reader =>
		{
			reader.Read<int>();

			// Same layout as DeltaSnapshotSystem.FlushAcks: cluster count, then each cluster id and its invalid snapshot count.
			using var ack = ByteStream.Create( 6 );
			ack.Write( (ushort)1 );
			ack.Write( reader.Read<ushort>() );
			ack.Write( (ushort)0 );

			using var incoming = ByteStream.CreateReader( ack.ToSpan() );
			SceneNetworkSystem.Instance.DeltaSnapshots.OnDeltaSnapshotClusterAck( connection, incoming );
		} );

		connection.Messages.Clear();
	}

	private class VisibilityController : Component, Component.INetworkVisible
	{
		public bool Visible;
		public Action OnCheck;

		public bool IsVisibleToConnection( Connection connection, in BBox worldBounds )
		{
			OnCheck?.Invoke();
			return Visible;
		}
	}

	private class PolledTestComponent : Component
	{
		[Sync] public List<int> Plain { get; set; } = new();
	}

	private class HostPolledTestComponent : Component
	{
		[Sync( SyncFlags.FromHost )] public List<int> Plain { get; set; } = new();
	}

	private class DormancyTestComponent : Component
	{
		[Sync] public NetList<int> SyncList { get; set; }
		[Sync] public NetDictionary<int, int> SyncDictionary { get; set; }
	}
}
