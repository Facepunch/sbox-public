#pragma warning disable CS0618 // Tests intentionally exercise legacy SyncFlags.Query polling.

namespace NetSim;

/// <summary>
/// Scalar-only state keeps these probes independent of collection polling and gameplay updates.
/// </summary>
public sealed class DormancyProbe : Component
{
	[Sync( SyncFlags.FromHost )] public int HostValue { get; set; } = 23;
	[Sync] public int OwnerValue { get; set; } = 11;
}

/// <summary>
/// Network containers report their own changes, so an object holding only these can sleep.
/// </summary>
public sealed class DormancyContainers : Component
{
	[Sync] public NetList<int> List { get; set; } = new();
	[Sync] public NetDictionary<string, int> Dictionary { get; set; } = new();
}

/// <summary>
/// Synced values that change without going through a sync setter, so they keep the sender polling.
/// </summary>
public sealed class DormancyPolled : Component
{
	[Sync] public List<int> Plain { get; set; } = new();

	public int QueriedField;
	[Sync( SyncFlags.Query )] public int Queried { get => QueriedField; set => QueriedField = value; }
}

/// <summary>
/// Sleeping network objects must still replicate every change, whichever peer owns them.
/// </summary>
[TestClass]
public class DormancyTests
{
	[TestInitialize]
	public void Init() => ThreadSafe.MarkMainThread();

	static readonly Vector3 InitialPosition = new( 10, 20, 30 );

	static GameObject Find( NetSimulation sim, SimPeer peer, Guid id ) =>
		sim.As( peer, () => Game.ActiveScene.Directory.FindByGuid( id ) );

	static Guid Spawn( NetSimulation sim, SimPeer owner, bool containers = false, bool polled = false )
	{
		return sim.As( sim.Host, () =>
		{
			var go = new GameObject( true, "dormancy-probe" );
			go.Components.Create<DormancyProbe>();
			if ( containers ) go.Components.Create<DormancyContainers>();
			if ( polled ) go.Components.Create<DormancyPolled>();
			go.WorldPosition = InitialPosition;
			go.Network.Interpolation = false;
			go.Network.SetOwnerTransfer( OwnerTransfer.Takeover );
			if ( owner == sim.Host )
				go.NetworkSpawn();
			else
				go.NetworkSpawn( Networking.System.FindConnection( owner.Local.Id ) );
			return go.Id;
		} );
	}

	static void AssertReplicas( NetSimulation sim, Guid id, SimPeer owner, int hostValue, int ownerValue, Vector3 position )
	{
		foreach ( var peer in sim.Peers )
		{
			sim.As( peer, () =>
			{
				var go = Find( sim, peer, id );
				Assert.IsNotNull( go, $"{peer} doesn't have the object" );
				var probe = go.Components.Get<DormancyProbe>();
				Assert.IsNotNull( probe, $"{peer} doesn't have the probe" );
				Assert.AreEqual( owner.Local.Id, go.Network.OwnerId, $"{peer} has a stale owner" );
				Assert.AreEqual( hostValue, probe.HostValue, $"{peer} has a stale FromHost value" );
				Assert.AreEqual( ownerValue, probe.OwnerValue, $"{peer} has a stale owner-authored value" );
				Assert.AreEqual( position, go.WorldPosition, $"{peer} has a stale position" );
			} );
		}
	}

	static void Mutate<T>( NetSimulation sim, SimPeer peer, Guid id, Action<T> mutate ) where T : Component =>
		sim.As( peer, () => mutate( Find( sim, peer, id ).Components.Get<T>() ) );

	static void AssertAsleep( NetSimulation sim, Guid id, string when )
	{
		foreach ( var peer in sim.Peers )
			Assert.IsFalse( sim.As( peer, () => Find( sim, peer, id )._net.IsDirty ), $"Precondition: {peer} has settled {when}" );
	}

	static void AssertContainers( NetSimulation sim, Guid id, int[] list, int? dictionaryValue )
	{
		foreach ( var peer in sim.Peers )
		{
			sim.As( peer, () =>
			{
				var containers = Find( sim, peer, id )?.Components.Get<DormancyContainers>();
				Assert.IsNotNull( containers, $"{peer} doesn't have the containers" );
				CollectionAssert.AreEqual( list, containers.List.ToArray(), $"{peer} has a stale NetList" );
				Assert.AreEqual( dictionaryValue, containers.Dictionary.TryGetValue( "key", out var value ) ? value : null, $"{peer} has a stale NetDictionary" );
			} );
		}
	}

	static void AssertPolled( NetSimulation sim, Guid id, int[] plain, int queried )
	{
		foreach ( var peer in sim.Peers )
		{
			sim.As( peer, () =>
			{
				var polled = Find( sim, peer, id )?.Components.Get<DormancyPolled>();
				Assert.IsNotNull( polled, $"{peer} doesn't have the polled values" );
				CollectionAssert.AreEqual( plain, polled.Plain.ToArray(), $"{peer} has a stale plain list" );
				Assert.AreEqual( queried, polled.Queried, $"{peer} has a stale query value" );
			} );
		}
	}

	[TestMethod]
	[DataRow( false, 0f )]
	[DataRow( true, 0.15f )]
	public void HostScalarWakesIdleClientOwnedObject( bool ownerMoves, float loss )
	{
		using var sim = new NetSimulation( new NetSimSettings
		{
			Seed = 7,
			Link = new SimLinkSettings { LatencySeconds = 0.05f, Loss = loss }
		} );
		var owner = sim.Join();
		sim.Join();
		var id = Spawn( sim, owner );

		sim.RunSeconds( 2 );
		AssertReplicas( sim, id, owner, 23, 11, InitialPosition );

		// The host is a proxy, but remains authoritative for this one scalar.
		sim.As( sim.Host, () => Find( sim, sim.Host, id ).Components.Get<DormancyProbe>().HostValue = 91 );

		var finalPosition = InitialPosition;
		if ( ownerMoves )
		{
			sim.Run( 60, frame =>
			{
				finalPosition = InitialPosition + new Vector3( frame + 1, 0, 0 );
				sim.As( owner, () => Find( sim, owner, id ).WorldPosition = finalPosition );
			} );
		}

		sim.RunSeconds( 2 );
		AssertReplicas( sim, id, owner, 91, 11, finalPosition );
	}

	[TestMethod]
	public void OwnerMoveReplicatesWhileHostIsAsleep()
	{
		using var sim = new NetSimulation();
		var owner = sim.Join();
		sim.Join();
		var id = Spawn( sim, owner );

		sim.RunSeconds( 2 );
		Assert.IsFalse( sim.As( sim.Host, () => Find( sim, sim.Host, id )._net.IsDirty ), "Precondition: the host has settled the object" );

		var position = InitialPosition + new Vector3( 64, 0, 0 );
		sim.As( owner, () => Find( sim, owner, id ).WorldPosition = position );

		sim.RunSeconds( 2 );
		AssertReplicas( sim, id, owner, 23, 11, position );
	}

	[TestMethod]
	public void IdleProxyCanTakeOwnershipAndAuthorState()
	{
		using var sim = new NetSimulation();
		var previousOwner = sim.Join();
		var newOwner = sim.Join();
		var id = Spawn( sim, previousOwner );

		sim.RunSeconds( 2 );
		AssertReplicas( sim, id, previousOwner, 23, 11, InitialPosition );

		var firstPosition = new Vector3( 100, 200, 300 );
		sim.As( newOwner, () =>
		{
			var go = Find( sim, newOwner, id );
			Assert.IsTrue( go.Network.TakeOwnership() );
			go.WorldPosition = firstPosition;
			go.Components.Get<DormancyProbe>().OwnerValue = 47;
		} );

		sim.RunSeconds( 2 );
		AssertReplicas( sim, id, newOwner, 23, 47, firstPosition );

		var secondPosition = new Vector3( -40, 50, 60 );
		sim.As( newOwner, () =>
		{
			var go = Find( sim, newOwner, id );
			go.WorldPosition = secondPosition;
			go.Components.Get<DormancyProbe>().OwnerValue = 83;
		} );
		sim.RunSeconds( 2 );
		AssertReplicas( sim, id, newOwner, 23, 83, secondPosition );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void LocallyAuthoredFastOrExactTransformWakesIdleObject( bool exact )
	{
		using var sim = new NetSimulation();
		sim.Join();
		sim.Join();
		var id = Spawn( sim, sim.Host );

		sim.RunSeconds( 2 );
		AssertReplicas( sim, id, sim.Host, 23, 11, InitialPosition );

		var position = new Vector3( 120, -240, 360 );
		sim.As( sim.Host, () =>
		{
			var go = Find( sim, sim.Host, id );
			var transform = new Transform( position );
			if ( exact )
			{
				go.Transform.SetLocalTransformExact( transform );
			}
			else
			{
				// Match animation's batched setter plus its final cache/callback update.
				go.Transform.SetLocalTransformFast( transform );
				go.Transform.TransformChanged( true );
			}
		} );

		sim.RunSeconds( 2 );
		AssertReplicas( sim, id, sim.Host, 23, 11, position );
	}

	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void IdleObjectReplicatesContainerChanges( bool hostOwned )
	{
		using var sim = new NetSimulation();
		var client = sim.Join();
		sim.Join();
		var owner = hostOwned ? sim.Host : client;
		var id = Spawn( sim, owner, containers: true );

		sim.RunSeconds( 2 );
		AssertContainers( sim, id, [], null );
		AssertAsleep( sim, id, "before the NetList change" );

		Mutate<DormancyContainers>( sim, owner, id, c => c.List.Add( 5 ) );
		sim.RunSeconds( 2 );
		AssertContainers( sim, id, [5], null );
		AssertAsleep( sim, id, "before the NetDictionary change" );

		Mutate<DormancyContainers>( sim, owner, id, c => c.Dictionary["key"] = 9 );
		sim.RunSeconds( 2 );
		AssertContainers( sim, id, [5], 9 );
		AssertAsleep( sim, id, "before clearing" );

		Mutate<DormancyContainers>( sim, owner, id, c =>
		{
			c.List.Clear();
			c.Dictionary.Remove( "key" );
		} );
		sim.RunSeconds( 2 );
		AssertContainers( sim, id, [], null );
	}

	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void IdleObjectReplicatesPolledChanges( bool hostOwned )
	{
		using var sim = new NetSimulation();
		var client = sim.Join();
		sim.Join();
		var owner = hostOwned ? sim.Host : client;
		var id = Spawn( sim, owner, polled: true );

		sim.RunSeconds( 2 );
		AssertPolled( sim, id, [], 0 );
		Assert.IsTrue( sim.As( owner, () => Find( sim, owner, id )._net.IsDirty ), "The owner must keep polling" );

		Mutate<DormancyPolled>( sim, owner, id, c => c.Plain.Add( 3 ) );
		sim.RunSeconds( 2 );
		AssertPolled( sim, id, [3], 0 );

		Mutate<DormancyPolled>( sim, owner, id, c => c.QueriedField = 12 );
		sim.RunSeconds( 2 );
		AssertPolled( sim, id, [3], 12 );
	}

	[TestMethod]
	public void IdleObjectReplicatesReassignedNetList()
	{
		using var sim = new NetSimulation();
		sim.Join();
		var id = Spawn( sim, sim.Host, containers: true );

		sim.RunSeconds( 2 );
		AssertAsleep( sim, id, "before reassigning the list" );
		Mutate<DormancyContainers>( sim, sim.Host, id, c => c.List = new NetList<int> { 1 } );
		sim.RunSeconds( 2 );
		AssertContainers( sim, id, [1], null );

		// The replacement list must wake the sleeping object by itself.
		AssertAsleep( sim, id, "before changing the replacement list" );
		Mutate<DormancyContainers>( sim, sim.Host, id, c => c.List.Add( 2 ) );
		sim.RunSeconds( 2 );
		AssertContainers( sim, id, [1, 2], null );
	}

	[TestMethod]
	public void IdleObjectPollsComponentAddedAfterSpawn()
	{
		using var sim = new NetSimulation();
		sim.Join();
		var id = Spawn( sim, sim.Host );

		sim.RunSeconds( 2 );
		AssertAsleep( sim, id, "before adding the component" );
		sim.As( sim.Host, () =>
		{
			var go = Find( sim, sim.Host, id );
			go.Components.Create<DormancyPolled>();
			go.Network.Refresh();
		} );
		sim.RunSeconds( 2 );
		AssertPolled( sim, id, [], 0 );

		Mutate<DormancyPolled>( sim, sim.Host, id, c => c.Plain.Add( 4 ) );
		sim.RunSeconds( 2 );
		AssertPolled( sim, id, [4], 0 );
	}

	[TestMethod]
	public void IdleProxyPollsAfterTakingOwnership()
	{
		using var sim = new NetSimulation();
		var previousOwner = sim.Join();
		var newOwner = sim.Join();
		var id = Spawn( sim, previousOwner, polled: true );

		sim.RunSeconds( 2 );
		Assert.IsFalse( sim.As( newOwner, () => Find( sim, newOwner, id )._net.IsDirty ), "Precondition: the proxy has settled before taking ownership" );
		sim.As( newOwner, () => Assert.IsTrue( Find( sim, newOwner, id ).Network.TakeOwnership() ) );
		sim.RunSeconds( 2 );

		Mutate<DormancyPolled>( sim, newOwner, id, c => c.Plain.Add( 6 ) );
		sim.RunSeconds( 2 );
		AssertPolled( sim, id, [6], 0 );
	}

	[TestMethod]
	public void IdleProxyNotifiesAfterTakingOwnership()
	{
		using var sim = new NetSimulation();
		var previousOwner = sim.Join();
		var newOwner = sim.Join();
		var id = Spawn( sim, previousOwner, containers: true );

		sim.RunSeconds( 2 );
		sim.As( newOwner, () => Assert.IsTrue( Find( sim, newOwner, id ).Network.TakeOwnership() ) );
		sim.RunSeconds( 2 );

		AssertAsleep( sim, id, "after the ownership change" );
		Mutate<DormancyContainers>( sim, newOwner, id, c => c.List.Add( 8 ) );
		sim.RunSeconds( 2 );
		AssertContainers( sim, id, [8], null );
	}

	/// <summary>
	/// Transform changes on a disabled channel don't wake the owner, so the host re-enabling
	/// the channel must wake it to send the current value without any further owner change.
	/// </summary>
	[TestMethod]
	[DataRow( NetworkFlags.NoPositionSync )]
	[DataRow( NetworkFlags.NoRotationSync )]
	[DataRow( NetworkFlags.NoScaleSync )]
	public void HostReenablingTransformChannelWakesIdleClientOwner( NetworkFlags channel )
	{
		using var sim = new NetSimulation();
		var owner = sim.Join();
		var observer = sim.Join();
		var id = Spawn( sim, owner );

		sim.RunSeconds( 2 );
		AssertAsleep( sim, id, "before disabling the channel" );

		sim.As( sim.Host, () => Find( sim, sim.Host, id ).Network.Flags |= channel );
		sim.RunSeconds( 2 );
		Assert.IsTrue( sim.As( owner, () => (Find( sim, owner, id ).Network.Flags & channel) != 0 ), "Precondition: the owner received the disabled channel" );
		AssertAsleep( sim, id, "after disabling the channel" );

		var position = InitialPosition + new Vector3( 512, 0, 0 );
		var rotation = Rotation.FromYaw( 90 );
		var scale = new Vector3( 2, 3, 4 );
		sim.As( owner, () =>
		{
			var go = Find( sim, owner, id );
			if ( channel == NetworkFlags.NoPositionSync ) go.WorldPosition = position;
			if ( channel == NetworkFlags.NoRotationSync ) go.WorldRotation = rotation;
			if ( channel == NetworkFlags.NoScaleSync ) go.WorldScale = scale;
		} );
		sim.RunSeconds( 2 );
		AssertAsleep( sim, id, "after the disabled-channel change" );
		sim.As( observer, () =>
		{
			var go = Find( sim, observer, id );
			Assert.AreEqual( InitialPosition, go.WorldPosition, "Precondition: the disabled position wasn't sent" );
			Assert.IsTrue( go.WorldRotation.Distance( Rotation.Identity ) < 0.01f, "Precondition: the disabled rotation wasn't sent" );
			Assert.AreEqual( Vector3.One, go.WorldScale, "Precondition: the disabled scale wasn't sent" );
		} );

		sim.As( sim.Host, () => Find( sim, sim.Host, id ).Network.Flags &= ~channel );
		sim.RunSeconds( 2 );

		var expectedPosition = channel == NetworkFlags.NoPositionSync ? position : InitialPosition;
		var expectedRotation = channel == NetworkFlags.NoRotationSync ? rotation : Rotation.Identity;
		var expectedScale = channel == NetworkFlags.NoScaleSync ? scale : Vector3.One;
		foreach ( var peer in sim.Peers )
		{
			sim.As( peer, () =>
			{
				var go = Find( sim, peer, id );
				Assert.AreEqual( 0, (int)(go.Network.Flags & channel), $"{peer} has a stale disabled channel" );
				Assert.AreEqual( expectedPosition, go.WorldPosition, $"{peer} has a stale position" );
				Assert.IsTrue( go.WorldRotation.Distance( expectedRotation ) < 0.01f, $"{peer} has a stale rotation" );
				Assert.AreEqual( expectedScale, go.WorldScale, $"{peer} has a stale scale" );
			} );
		}
	}
}
