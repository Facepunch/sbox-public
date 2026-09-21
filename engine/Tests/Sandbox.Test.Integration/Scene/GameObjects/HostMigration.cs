using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Sandbox.Internal;
using Sandbox.Network;
using SceneTests;

namespace SceneTests.GameObjects;

using static GlobalGameNamespace;

/// <summary>
/// The host hands the game to a peer when it leaves. The peer loads the host's snapshot as the
/// new host, everyone else reloads from the new host. These run the pieces in one process with
/// the ClientAndHost harness.
/// </summary>
[TestClass]
public class HostMigrationTest
{
	static void DrainSnapshot( Connection connection )
	{
		var deadline = DateTime.UtcNow.AddSeconds( 10 );
		while ( connection.HasPendingSends && DateTime.UtcNow < deadline )
		{
			connection.FlushPendingSends();
			System.Threading.Thread.Sleep( 1 );
		}
		Assert.IsFalse( connection.HasPendingSends, "Snapshot encoding timed out" );
	}
	private TypeLibrary _oldTypeLibrary;

	[TestMethod]
	public void CapturedSnapshotKeepsStateAfterSceneChanges()
	{
		using var scope = new Scene().Push();
		using var clientAndHost = new ClientAndHost( TypeLibrary );
		clientAndHost.BecomeHost();
		var go = new GameObject( "Before capture" );
		go.Components.Create<CounterComponent>().Value = 7;
		go.NetworkSpawn( null );
		var expected = SnapshotMsg.Create();
		SceneNetworkSystem.Instance.GetSnapshot( clientAndHost.Client, ref expected );
		var captured = SceneNetworkSystem.Instance.CaptureSnapshot( clientAndHost.Client );
		go.Name = "After capture";
		go.Components.Get<CounterComponent>().Value = 99;
		go.Destroy();
		var actual = Task.Run( captured.Materialize ).GetAwaiter().GetResult();
		Assert.AreEqual( expected.SceneData, actual.SceneData );
		var before = expected.NetworkObjects.OfType<ObjectCreateMsg>().Single( x => x.Guid == go.Id );
		var after = actual.NetworkObjects.OfType<ObjectCreateMsg>().Single( x => x.Guid == go.Id );
		Assert.AreEqual( before.JsonData, after.JsonData );
		CollectionAssert.AreEqual( before.TableData, after.TableData );
	}

	[TestInitialize]
	public void TestInitialize()
	{
		_oldTypeLibrary = Game.TypeLibrary;

		Game.TypeLibrary = new Sandbox.Internal.TypeLibrary();
		Game.TypeLibrary.AddAssembly( typeof( PrefabFile ).Assembly, false );
		Game.TypeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		Game.TypeLibrary.AddAssembly( typeof( HostListener ).Assembly, false );

		JsonUpgrader.UpdateUpgraders( Game.TypeLibrary );

		HostListener.ResetCounters();
	}

	[TestCleanup]
	public void TestCleanup()
	{
		Game.TypeLibrary = _oldTypeLibrary;
	}

	/// <summary>
	/// The leaving host tells everyone who takes over, then gives the successor a snapshot.
	/// </summary>
	[TestMethod]
	public void LeavingHostAnnouncesSuccessorAndSendsSnapshot()
	{
		using var scope = new Scene().Push();
		using var clientAndHost = new ClientAndHost( TypeLibrary );

		clientAndHost.BecomeHost();

		var go = new GameObject();
		go.Components.Create<CounterComponent>().Value = 7;
		go.NetworkSpawn( null );

		// Host-only state that a late joiner never sees
		var hostOnly = new GameObject( "host only" ) { NetworkMode = NetworkMode.Never };
		hostOnly.Components.Create<CounterComponent>().Value = 3;

		var started = Networking.System.BeginHostHandoff();
		Assert.IsTrue( started, "The host should hand off when a connected peer exists" );

		var leaving = clientAndHost.Client.Messages.Select( m => m.Payload ).OfType<HostLeavingMsg>().ToArray();
		Assert.AreEqual( 1, leaving.Length, "The successor should be told the host is leaving" );
		Assert.AreEqual( clientAndHost.Client.Id, leaving[0].SuccessorId );
		Assert.IsFalse( clientAndHost.Client.Messages.Any( m => m.Payload is HostHandoffMsg ), "Wait for the announcement acknowledgement" );
		Receive( clientAndHost.Client, new HostLeavingAckMsg() );
		Assert.IsFalse( Networking.System.PumpHostHandoff() );

		DrainSnapshot( clientAndHost.Client );
		var handoff = clientAndHost.Client.Messages.Select( m => m.Payload ).OfType<HostHandoffMsg>().ToArray();
		Assert.AreEqual( 1, handoff.Length, "The successor should receive exactly one handoff" );
		Assert.IsTrue( handoff[0].Snapshot.NetworkObjects.OfType<ObjectCreateMsg>().Any( x => x.Guid == go.Id ), "The handoff snapshot should carry the networked objects" );
		Assert.IsTrue( handoff[0].Snapshot.SceneData.Contains( hostOnly.Id.ToString() ), "The handoff snapshot should carry objects that are never networked" );

		var joinSnapshot = SnapshotMsg.Create();
		SceneNetworkSystem.Instance.GetSnapshot( clientAndHost.Client, ref joinSnapshot );
		Assert.IsFalse( joinSnapshot.SceneData.Contains( hostOnly.Id.ToString() ), "A join snapshot still leaves them out" );
	}

	/// <summary>
	/// With nobody to hand to, the host just leaves.
	/// </summary>
	[TestMethod]
	public void HostWithNoPeersDoesNotHandOff()
	{
		using var scope = new Scene().Push();
		using var clientAndHost = new ClientAndHost( TypeLibrary );

		clientAndHost.BecomeHost();
		clientAndHost.Client.State = Connection.ChannelState.LoadingServerInformation;

		Assert.IsFalse( Networking.System.BeginHostHandoff() );
		Assert.IsFalse( clientAndHost.Client.Messages.Select( m => m.Payload ).OfType<HostHandoffMsg>().Any() );
	}

	/// <summary>
	/// The successor becomes the host and its scene is the previous host's snapshot: unowned objects
	/// are now its own, other players keep theirs, and the previous host's objects get their
	/// orphaned action, with the usual callbacks in the usual order.
	/// </summary>
	[TestMethod]
	public async Task SuccessorLoadsSnapshotAsHost()
	{
		using var scope = new Scene().Push();
		using var clientAndHost = new ClientAndHost( TypeLibrary );

		clientAndHost.BecomeHost();

		var listener = new GameObject( "listener" );
		listener.Components.Create<HostListener>();

		var unowned = new GameObject( "unowned" );
		unowned.Components.Create<CounterComponent>().Value = 5;
		unowned.NetworkSpawn( null );

		var clientOwned = new GameObject( "client" );
		clientOwned.NetworkSpawn( clientAndHost.Client );

		var hostOwned = new GameObject( "host" );
		hostOwned.NetworkSpawn( clientAndHost.Host );

		var snapshot = SnapshotMsg.Create();
		SceneNetworkSystem.Instance.GetSnapshot( null, ref snapshot );

		clientAndHost.BecomeClient();
		Assert.IsFalse( Networking.IsHost );

		await Networking.System.BecomeHostAsync( clientAndHost.Host, snapshot );

		Assert.IsTrue( Networking.IsActive );
		Assert.IsTrue( Networking.IsHost, "The successor should be the host" );
		Assert.AreEqual( Connection.Local, Connection.Host );

		var scene = Game.ActiveScene;

		var unownedAfter = scene.Directory.FindByGuid( unowned.Id );
		Assert.IsNotNull( unownedAfter, "Unowned objects survive the handoff" );
		Assert.AreEqual( 5, unownedAfter.Components.Get<CounterComponent>().Value, "Synced state comes from the snapshot" );
		Assert.IsFalse( unownedAfter.Network.IsProxy, "The new host owns unowned objects" );

		var clientOwnedAfter = scene.Directory.FindByGuid( clientOwned.Id );
		Assert.IsNotNull( clientOwnedAfter, "Other players keep their objects" );
		Assert.AreEqual( clientAndHost.Client.Id, clientOwnedAfter.Network.Owner.Id );
		Assert.IsTrue( clientOwnedAfter.Network.IsOwner );

		var hostOwnedAfter = scene.Directory.FindByGuid( hostOwned.Id );
		Assert.IsTrue( hostOwnedAfter is null || hostOwnedAfter.IsDestroyed, "The previous host's objects get the orphaned action" );

		Assert.AreEqual( 1, HostListener.BecameHostCalls, "OnBecameHost fires once on the successor" );
		Assert.AreEqual( 0, HostListener.HostChangedCalls, "OnHostChanged is for the other peers" );
		Assert.IsTrue( HostListener.Disconnected.Contains( clientAndHost.Host.Id ), "The previous host disconnects after we became host" );
		Assert.IsTrue( HostListener.BecameHostSequence < HostListener.DisconnectedSequence, "OnBecameHost comes before OnDisconnected for the previous host" );
	}

	/// <summary>
	/// A peer reloads its scene from the new host's snapshot and is told who the host is now.
	/// </summary>
	[TestMethod]
	public async Task PeerResyncsFromNewHost()
	{
		using var scope = new Scene().Push();
		using var clientAndHost = new ClientAndHost( TypeLibrary );

		clientAndHost.BecomeHost();

		var listener = new GameObject( "listener" );
		listener.Components.Create<HostListener>();

		var unowned = new GameObject( "unowned" );
		unowned.Components.Create<CounterComponent>().Value = 9;
		unowned.NetworkSpawn( null );

		var snapshot = SnapshotMsg.Create();
		SceneNetworkSystem.Instance.GetSnapshot( clientAndHost.Client, ref snapshot );

		clientAndHost.BecomeClient();

		var previousHost = new TestConnection( Guid.NewGuid() );
		await SceneNetworkSystem.Instance.ResyncFromHostAsync( previousHost, clientAndHost.Host, snapshot );

		Assert.IsFalse( Networking.IsHost );

		var unownedAfter = Game.ActiveScene.Directory.FindByGuid( unowned.Id );
		Assert.IsNotNull( unownedAfter );
		Assert.AreEqual( 9, unownedAfter.Components.Get<CounterComponent>().Value );
		Assert.IsTrue( unownedAfter.Network.IsProxy, "Peers don't own unowned objects" );

		Assert.AreEqual( 1, HostListener.HostChangedCalls );
		Assert.AreEqual( clientAndHost.Host.Id, HostListener.LastNewHost );
		Assert.AreEqual( 0, HostListener.BecameHostCalls );
	}

	/// <summary>
	/// A connection goes over the wire as its id and comes back as the receiver's object for it, which
	/// is what lets [Sync] and RPC arguments carry connections across a migration.
	/// </summary>
	[TestMethod]
	public void ConnectionRoundTripsAsItsId()
	{
		using var scope = new Scene().Push();
		using var clientAndHost = new ClientAndHost( TypeLibrary );

		clientAndHost.BecomeHost();

		var bytes = TypeLibrary.ToBytes<Connection>( clientAndHost.Client );
		Assert.AreSame( clientAndHost.Client, TypeLibrary.FromBytes<Connection>( bytes ) );

		Assert.IsNull( TypeLibrary.FromBytes<Connection>( TypeLibrary.ToBytes<Connection>( null ) ) );
	}

	/// <summary>
	/// A component that initialises a synced property in OnStart used to overwrite what the host
	/// sent, on every receiver. The snapshot value is applied again after the lifecycle callbacks.
	/// </summary>
	[TestMethod]
	public async Task SnapshotValueWinsOverOnStart()
	{
		using var scope = new Scene().Push();
		using var clientAndHost = new ClientAndHost( TypeLibrary );

		clientAndHost.BecomeHost();

		var charger = new GameObject( "charger" );
		var component = charger.Components.Create<ChargerComponent>();
		component.Power = 25f;
		charger.NetworkSpawn( null );

		var snapshot = SnapshotMsg.Create();
		SceneNetworkSystem.Instance.GetSnapshot( null, ref snapshot );

		clientAndHost.BecomeClient();
		await SceneNetworkSystem.Instance.SetSnapshotAsync( snapshot );

		var after = Game.ActiveScene.Directory.FindByGuid( charger.Id ).Components.Get<ChargerComponent>();
		Assert.IsTrue( after.StartCalls > 0, "OnStart ran on the receiver" );
		Assert.AreEqual( 25f, after.Power, "The host's value wins over OnStart" );
	}

	/// <summary>
	/// A resync updates the scene in place. Networked objects are matched by id, so pending code on their
	/// components keeps running; local objects and anything we own are untouched.
	/// </summary>
	[TestMethod]
	public async Task ResyncKeepsTheSceneInPlace()
	{
		using var scope = new Scene().Push();
		using var clientAndHost = new ClientAndHost( TypeLibrary );

		clientAndHost.BecomeHost();

		var unowned = new GameObject( "unowned" );
		var counter = unowned.Components.Create<CounterComponent>();
		counter.Value = 3;
		unowned.NetworkSpawn( null );

		var owned = new GameObject( "owned" );
		var ownedCounter = owned.Components.Create<CounterComponent>();
		ownedCounter.Value = 1;
		owned.NetworkSpawn( clientAndHost.Client );

		var missing = new GameObject( "missing" );
		missing.NetworkSpawn( null );

		var snapshot = SnapshotMsg.Create();
		SceneNetworkSystem.Instance.GetSnapshot( clientAndHost.Client, ref snapshot );

		// Diverge from the snapshot: we lack one object, have one the host doesn't, and moved on with ours
		clientAndHost.BecomeClient();
		missing.DestroyImmediate();
		var stale = new GameObject( "stale" );
		stale.NetworkSpawn( null );
		var local = new GameObject( "local" ) { NetworkMode = NetworkMode.Never };
		ownedCounter.Value = 2;

		var scene = Game.ActiveScene;
		await SceneNetworkSystem.Instance.ResyncFromHostAsync( new TestConnection( Guid.NewGuid() ), clientAndHost.Host, snapshot );

		Assert.AreSame( scene, Game.ActiveScene, "The scene is not replaced" );
		Assert.AreSame( counter, Game.ActiveScene.Directory.FindByGuid( unowned.Id ).Components.Get<CounterComponent>(), "Components keep their identity" );
		Assert.AreEqual( 3, counter.Value );
		Assert.IsTrue( local.IsValid(), "Local objects are left alone" );
		Assert.AreEqual( 2, ownedCounter.Value, "Our own objects are not rewound to the host's copy" );
		Assert.IsTrue( Game.ActiveScene.Directory.FindByGuid( missing.Id ).IsValid(), "Objects we lack are created" );
		Assert.IsFalse( stale.IsValid(), "Objects the host doesn't have are destroyed" );
	}

	/// <summary>
	/// The handoff snapshot carries the host's NetworkMode.Never objects. The successor creates the ones it
	/// doesn't have and takes the host's values for the ones it shares.
	/// </summary>
	[TestMethod]
	public async Task SuccessorReceivesHostOnlyObjects()
	{
		using var scope = new Scene().Push();
		using var clientAndHost = new ClientAndHost( TypeLibrary );

		clientAndHost.BecomeHost();

		var shared = new GameObject( "shared" ) { NetworkMode = NetworkMode.Never };
		var sharedState = shared.Components.Create<LocalStateComponent>();
		sharedState.Level = 40;

		var hostOnly = new GameObject( "hostonly" ) { NetworkMode = NetworkMode.Never };
		hostOnly.Components.Create<CounterComponent>();

		var snapshot = SnapshotMsg.Create();
		SceneNetworkSystem.Instance.GetHandoffSnapshot( ref snapshot );

		clientAndHost.BecomeClient();
		hostOnly.DestroyImmediate();
		sharedState.Level = 5;

		await Networking.System.BecomeHostAsync( clientAndHost.Host, snapshot );

		Assert.IsTrue( Game.ActiveScene.Directory.FindByGuid( hostOnly.Id ).IsValid(), "Host-only objects are created on the successor" );
		Assert.AreSame( sharedState, shared.Components.Get<LocalStateComponent>() );
		Assert.AreEqual( 40, sharedState.Level, "Shared local objects take the host's values" );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void HandoffWaitsForEveryAnnouncementAck( bool successorFirst )
	{
		using var scope = new Scene().Push();
		using var peers = new ClientAndHost( TypeLibrary );
		peers.BecomeHost();
		var third = new TestConnection();
		Networking.System.OnConnected( third );
		third.State = Connection.ChannelState.Connected;

		Assert.IsTrue( Networking.System.BeginHostHandoff() );
		var leaving = peers.Client.Messages.Select( m => m.Payload ).OfType<HostLeavingMsg>().Single();
		var successor = leaving.SuccessorId == peers.Client.Id ? peers.Client : third;
		var other = successor == peers.Client ? third : peers.Client;

		Receive( successorFirst ? successor : other, new HostLeavingAckMsg() );
		Assert.IsFalse( Networking.System.PumpHostHandoff() );
		Assert.IsFalse( successor.Messages.Any( m => m.Payload is HostHandoffMsg ), "The slower peer must know who to expect before the successor can resync it" );

		Receive( successorFirst ? other : successor, new HostLeavingAckMsg() );
		Assert.IsFalse( Networking.System.PumpHostHandoff() );
		DrainSnapshot( successor );
		Assert.AreEqual( 1, successor.Messages.Count( m => m.Payload is HostHandoffMsg ) );

		Receive( other, new HostHandoffAckMsg() );
		Assert.IsFalse( Networking.System.PumpHostHandoff(), "Only the chosen successor can acknowledge the snapshot" );
		Receive( successor, new HostHandoffAckMsg() );
		Assert.IsTrue( Networking.System.PumpHostHandoff() );
		DrainSnapshot( successor );
		Assert.AreEqual( 1, successor.Messages.Count( m => m.Payload is HostHandoffMsg ) );
	}

	// Exercise the same packed-message dispatch used by the transports, with explicit delivery order.
	static void Receive<T>( Connection source, T message )
	{
		var handler = typeof( NetworkSystem ).GetMethod( "HandleIncomingMessage", BindingFlags.Instance | BindingFlags.NonPublic )
			.CreateDelegate<Action<NetworkSystem.NetworkMessage>>( Networking.System );
		var stream = ByteStream.Create( 32 );
		try
		{
			stream.Write( InternalMessageType.Packed );
			TypeLibrary.ToBytes( message, ref stream );
			using var reader = ByteStream.CreateReader( stream.ToArray() );
			handler( new NetworkSystem.NetworkMessage { Source = source, Data = reader } );
		}
		finally
		{
			stream.Dispose();
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task SuccessorReceivesLocalChildrenOfExistingNetworkObjects( bool locallyOwned )
	{
		using var scope = new Scene().Push();
		using var peers = new ClientAndHost( TypeLibrary );
		peers.BecomeHost();
		var root = new GameObject( "networked root" );
		root.NetworkSpawn( locallyOwned ? peers.Client : null );
		var shared = new GameObject( root, name: "shared local child" ) { NetworkMode = NetworkMode.Never };
		var state = shared.Components.Create<LocalStateComponent>();
		state.Level = 40;
		var missing = new GameObject( root, name: "missing local child" ) { NetworkMode = NetworkMode.Never };
		missing.Components.Create<LocalStateComponent>().Level = 12;
		var missingId = missing.Id;
		var snapshot = SnapshotMsg.Create();
		SceneNetworkSystem.Instance.GetHandoffSnapshot( ref snapshot );

		peers.BecomeClient();
		state.Level = 5;
		missing.DestroyImmediate();
		await Networking.System.BecomeHostAsync( peers.Host, snapshot );

		Assert.IsTrue( Networking.IsActive );
		Assert.AreSame( root, Game.ActiveScene.Directory.FindByGuid( root.Id ) );
		Assert.AreSame( state, shared.Components.Get<LocalStateComponent>() );
		Assert.AreEqual( 40, state.Level );
		var restored = Game.ActiveScene.Directory.FindByGuid( missingId );
		Assert.IsTrue( restored.IsValid() );
		Assert.AreSame( root, restored.Parent );
		Assert.AreEqual( 12, restored.Components.Get<LocalStateComponent>().Level );
	}

	[TestMethod]
	public async Task MapsLoadedAfterMigrationCanSpawnNetworkContent()
	{
		using var scope = new Scene().Push();
		using var peers = new ClientAndHost( TypeLibrary );
		peers.BecomeHost();
		var snapshot = SnapshotMsg.Create();
		SceneNetworkSystem.Instance.GetHandoffSnapshot( ref snapshot );
		peers.BecomeClient();
		await SceneNetworkSystem.Instance.SetSnapshotAsync( snapshot );
		await Networking.System.BecomeHostAsync( peers.Host, snapshot );

		Assert.IsTrue( Networking.IsActive );
		var map = new GameObject( "new map" ).Components.Create<MapInstance>();
		Assert.IsFalse( map.ContentComesFromSnapshot, "A new map on the successor has no snapshot supplying its networked content" );
	}

	[TestMethod]
	public void LocalPeerKeepsItsChannelUntilTheOldHostLeaves()
	{
		using var scope = new Scene().Push();
		using var peers = new ClientAndHost( TypeLibrary );
		peers.BecomeClient();
		var network = Networking.System;
		var joinLocal = typeof( Application ).GetProperty( "IsJoinLocal", BindingFlags.Static | BindingFlags.NonPublic );
		var previous = Application.IsJoinLocal;
		try
		{
			joinLocal.SetValue( null, true );
			Receive( peers.Host, new HostLeavingMsg { SuccessorId = Guid.NewGuid() } );
			typeof( NetworkSystem ).GetMethod( "TickHostMigration", BindingFlags.Instance | BindingFlags.NonPublic ).Invoke( network, null );
			Assert.AreSame( network, Networking.System, "Do not reconnect while the old host is still listening" );
			Assert.IsFalse( network.IsDisconnected, "Keep the TCP send queue alive so the host can receive our acknowledgement" );
			Assert.IsTrue( peers.Host.Messages.Any( m => m.Payload is HostLeavingAckMsg ) );
		}
		finally
		{
			joinLocal.SetValue( null, previous );
		}
	}

	[TestMethod]
	public void ListenerFailureDoesNotSkipOtherListeners()
	{
		using var scope = new Scene().Push();
		using var peers = new ClientAndHost( TypeLibrary );
		peers.BecomeHost();
		new GameObject().Components.Create<ThrowingListener>();
		new GameObject().Components.Create<HostListener>();
		SceneNetworkSystem.Instance.OnLeave( peers.Client );
		Assert.IsTrue( HostListener.Disconnected.Contains( peers.Client.Id ) );
	}

	[TestMethod]
	public void ResyncSharesSystemStateButKeepsPerPeerVisibility()
	{
		using var scope = new Scene().Push();
		using var peers = new ClientAndHost( TypeLibrary );
		peers.BecomeHost();
		var third = new TestConnection();
		Networking.System.OnConnected( third );
		third.State = Connection.ChannelState.Connected;
		var system = Game.ActiveScene.GetSystem<SnapshotCounterSystem>();
		var go = new GameObject();
		go.Components.Create<PeerVisibility>().VisibleTo = peers.Client.Id;
		go.NetworkSpawn();
		go.Network.AlwaysTransmit = false;

		var first = SceneNetworkSystem.Instance.CaptureSnapshot( peers.Client );
		var second = SceneNetworkSystem.Instance.CaptureSnapshot( third, shared: first );
		var snapshots = Task.WhenAll( Task.Run( first.Materialize ), Task.Run( second.Materialize ) ).GetAwaiter().GetResult();
		Assert.AreEqual( 1, system.Writes, "Capture shared system state once for the entire migration" );
		Assert.AreSame( snapshots[0].SceneData, snapshots[1].SceneData );
		Assert.AreSame( snapshots[0].GameObjectSystems, snapshots[1].GameObjectSystems );
		Assert.IsTrue( snapshots[0].NetworkObjects.OfType<ObjectCreateMsg>().Any( m => m.Guid == go.Id ) );
		Assert.IsFalse( snapshots[1].NetworkObjects.OfType<ObjectCreateMsg>().Any( m => m.Guid == go.Id ), "Visibility must still be evaluated for each recipient" );
	}

	private class PeerVisibility : Component, Component.INetworkVisible
	{
		public Guid VisibleTo;
		public bool IsVisibleToConnection( Connection connection, in BBox bounds ) => connection.Id == VisibleTo;
	}

	private class SnapshotCounterSystem( Scene scene ) : GameObjectSystem( scene ), Component.INetworkSnapshot
	{
		public int Writes;
		public void WriteSnapshot( ref ByteStream writer ) => writer.Write( ++Writes );
		public void ReadSnapshot( ref ByteStream reader ) => reader.Read<int>();
	}

	private class ThrowingListener : Component, Component.INetworkListener
	{
		public void OnDisconnected( Connection connection ) => throw new InvalidOperationException( "Test listener failure" );
	}

	private class LocalStateComponent : Component
	{
		[Property] public int Level { get; set; }
	}

	private class ChargerComponent : Component
	{
		[Sync] public float Power { get; set; }
		public int StartCalls;

		protected override void OnStart()
		{
			StartCalls++;
			Power = 100f;
		}
	}

	private class CounterComponent : Component
	{
		[Sync] public int Value { get; set; }
	}

	private class HostListener : Component, Component.INetworkListener
	{
		public static int BecameHostCalls;
		public static int HostChangedCalls;
		public static Guid LastNewHost;
		public static List<Guid> Disconnected = new();
		public static int BecameHostSequence = -1;
		public static int DisconnectedSequence = -1;
		static int sequence;

		public static void ResetCounters()
		{
			BecameHostCalls = 0;
			HostChangedCalls = 0;
			LastNewHost = Guid.Empty;
			Disconnected.Clear();
			BecameHostSequence = -1;
			DisconnectedSequence = -1;
			sequence = 0;
		}

		public void OnBecameHost( Connection previousHost )
		{
			BecameHostCalls++;
			BecameHostSequence = sequence++;
		}

		public void OnHostChanged( Connection previousHost, Connection newHost )
		{
			HostChangedCalls++;
			LastNewHost = newHost.Id;
		}

		public void OnDisconnected( Connection channel )
		{
			Disconnected.Add( channel.Id );
			DisconnectedSequence = sequence++;
		}
	}
}
