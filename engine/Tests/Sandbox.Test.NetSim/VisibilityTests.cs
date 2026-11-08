using Sandbox.Network;

namespace NetSim;

/// <summary>
/// Only visible to connections that can see it from close by.
/// </summary>
public sealed class NearbyOnly : Component, Component.INetworkVisible
{
	[Sync] public int Value { get; set; }

	public bool IsVisibleToConnection( Connection connection, in BBox worldBounds ) => connection.DistanceSquared( worldBounds.Center ) < 500 * 500;
}

[TestClass]
public class VisibilityTests
{
	[TestInitialize]
	public void Init() => ThreadSafe.MarkMainThread();

	static int ValueOn( NetSimulation sim, SimPeer peer, Guid id ) =>
		sim.As( peer, () => Game.ActiveScene.Directory.FindByGuid( id )?.Components.Get<NearbyOnly>( FindMode.EverythingInSelf )?.Value ?? -1 );

	static bool ActiveOn( NetSimulation sim, SimPeer peer, Guid id ) =>
		sim.As( peer, () => Game.ActiveScene.Directory.FindByGuid( id )?.Active ?? false );

	static void Set( NetSimulation sim, SimPeer owner, Guid id, Action<NearbyOnly> change )
	{
		sim.As( owner, () => change( Game.ActiveScene.Directory.FindByGuid( id ).Components.Get<NearbyOnly>() ) );
		sim.RunSeconds( 1 );
	}

	/// <summary>
	/// A client doesn't know where peers behind the host look from, only the host does, so a custom culler on something
	/// it owns hides it from them. Hidden, not left frozen with stale values. The host culls by where it really looks.
	/// Every peer looks from the world origin here, there are no cameras.
	/// </summary>
	[TestMethod]
	public void ClientOwnedObjectsAreHiddenFromPeersBehindTheHost()
	{
		using var sim = new NetSimulation();
		var a = sim.Join();
		var b = sim.Join();

		var id = sim.As( a, () =>
		{
			var go = new GameObject( true, "nearby" );
			go.WorldPosition = new Vector3( 100, 0, 0 );
			go.Components.Create<NearbyOnly>();
			go.NetworkSpawn();
			go.Network.AlwaysTransmit = false;
			return go.Id;
		} );

		sim.RunSeconds( 1 );
		Set( sim, a, id, n => n.Value = 5 );

		Assert.AreEqual( 5, ValueOn( sim, sim.Host, id ) );

		// Past the grace period
		sim.RunSeconds( 3 );
		Assert.IsTrue( ActiveOn( sim, sim.Host, id ), "the host is close" );
		Assert.IsFalse( ActiveOn( sim, b, id ), "a can't tell where b looks from" );

		// Out of the host's sight, then changed
		Set( sim, a, id, n => n.WorldPosition = new Vector3( 10000, 0, 0 ) );
		sim.RunSeconds( 3 );
		Set( sim, a, id, n => n.Value = 7 );

		Assert.IsFalse( ActiveOn( sim, sim.Host, id ), "culled for the host" );
		Assert.AreEqual( 5, ValueOn( sim, sim.Host, id ) );

		// Back in sight
		Set( sim, a, id, n => n.WorldPosition = new Vector3( 100, 0, 0 ) );
		sim.RunSeconds( 1 );

		Assert.IsTrue( ActiveOn( sim, sim.Host, id ) );
		Assert.AreEqual( 7, ValueOn( sim, sim.Host, id ) );
	}
}
