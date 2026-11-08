namespace NetSim;

/// <summary>
/// Ownership changes that race each other over a slow link must still leave every peer agreeing on the owner.
/// </summary>
[TestClass]
public class OwnershipTests
{
	[TestInitialize]
	public void Init() => ThreadSafe.MarkMainThread();

	static Guid SpawnProp( NetSimulation sim )
	{
		var id = sim.As( sim.Host, () =>
		{
			var go = new GameObject( true, "prop" );
			go.Components.Create<SimProp>();
			go.Network.SetOwnerTransfer( OwnerTransfer.Takeover );
			go.NetworkSpawn( null );
			return go.Id;
		} );

		sim.RunSeconds( 1 );
		return id;
	}

	static GameObject Find( NetSimulation sim, SimPeer peer, Guid id ) => sim.As( peer, () => Game.ActiveScene.Directory.FindByGuid( id ) );

	static string Owners( NetSimulation sim, Guid id )
	{
		return string.Join( ", ", sim.Peers.Select( p => $"{p}: {sim.As( p, () => Find( sim, p, id ).Network.OwnerId )} v{sim.As( p, () => Find( sim, p, id ).Network.SnapshotVersion )}" ) );
	}

	static void AssertAgree( NetSimulation sim, Guid id )
	{
		var owners = sim.Peers.Select( p => sim.As( p, () => Find( sim, p, id ).Network.OwnerId ) ).Distinct().Count();
		var versions = sim.Peers.Select( p => sim.As( p, () => Find( sim, p, id ).Network.SnapshotVersion ) ).Distinct().Count();
		Assert.IsTrue( owners == 1 && versions == 1, Owners( sim, id ) );
	}

	static NetSimulation Slow() => new( new NetSimSettings { Link = new SimLinkSettings { LatencySeconds = 0.1f } } );

	[TestMethod]
	public void TwoClientsTakeAtOnce()
	{
		using var sim = Slow();
		var a = sim.Join();
		var b = sim.Join();
		var id = SpawnProp( sim );

		sim.As( a, () => Find( sim, a, id ).Network.TakeOwnership() );
		sim.As( b, () => Find( sim, b, id ).Network.TakeOwnership() );
		sim.RunSeconds( 1 );

		AssertAgree( sim, id );
	}

	[TestMethod]
	public void ClientAndHostTakeAtOnce()
	{
		using var sim = Slow();
		var a = sim.Join();
		sim.Join();
		var id = SpawnProp( sim );

		sim.As( a, () => Find( sim, a, id ).Network.TakeOwnership() );
		sim.As( sim.Host, () => Find( sim, sim.Host, id ).Network.TakeOwnership() );
		sim.RunSeconds( 1 );

		AssertAgree( sim, id );
	}

	[TestMethod]
	public void TakeWhileAnotherDrops()
	{
		using var sim = Slow();
		var a = sim.Join();
		var b = sim.Join();
		var id = SpawnProp( sim );

		sim.As( a, () => Find( sim, a, id ).Network.TakeOwnership() );
		sim.RunSeconds( 1 );

		sim.As( a, () => Find( sim, a, id ).Network.DropOwnership() );
		sim.As( b, () => Find( sim, b, id ).Network.TakeOwnership() );
		sim.RunSeconds( 1 );

		AssertAgree( sim, id );
	}

	[TestMethod]
	public void TakeWhileHostDrops()
	{
		using var sim = Slow();
		var a = sim.Join();
		var b = sim.Join();
		var id = SpawnProp( sim );

		sim.As( a, () => Find( sim, a, id ).Network.TakeOwnership() );
		sim.RunSeconds( 1 );

		// Same version on both sides, the take wins the tie over the host's drop
		sim.As( sim.Host, () => Find( sim, sim.Host, id ).Network.DropOwnership() );
		sim.As( b, () => Find( sim, b, id ).Network.TakeOwnership() );
		sim.RunSeconds( 1 );

		AssertAgree( sim, id );
		Assert.AreEqual( b.Local.Id, sim.As( b, () => Find( sim, b, id ).Network.OwnerId ), Owners( sim, id ) );
	}

	[TestMethod]
	public void ManyClientsFightOverOneProp()
	{
		using var sim = Slow();
		var clients = Enumerable.Range( 0, 6 ).Select( _ => sim.Join() ).ToList();
		var id = SpawnProp( sim );
		var random = new Random( 3 );

		for ( var frame = 0; frame < 300; frame++ )
		{
			foreach ( var c in clients )
			{
				if ( random.NextDouble() < 0.02 )
					sim.As( c, () => Find( sim, c, id ).Network.TakeOwnership() );
				else if ( random.NextDouble() < 0.01 )
					sim.As( c, () => Find( sim, c, id ).Network.DropOwnership() );
			}

			sim.Step();
		}

		sim.RunSeconds( 1 );
		AssertAgree( sim, id );
	}
}
