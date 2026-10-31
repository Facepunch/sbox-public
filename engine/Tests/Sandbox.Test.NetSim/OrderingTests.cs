namespace NetSim;

/// <summary>
/// Unreliable snapshots and reliable messages race each other, whichever arrives first the result has to be the same.
/// </summary>
[TestClass]
public class OrderingTests
{
	[TestInitialize]
	public void Init() => ThreadSafe.MarkMainThread();

	/// <summary>
	/// A component's values can arrive in a snapshot before the refresh that creates it on the receiver.
	/// </summary>
	[TestMethod]
	public void ValuesThatOvertakeTheirComponentAreKept()
	{
		using var sim = new NetSimulation();
		var client = sim.Join();

		var id = sim.As( sim.Host, () =>
		{
			var go = new GameObject( true, "prop" );
			go.NetworkSpawn( null );
			return go.Id;
		} );

		sim.RunSeconds( 1 );

		// The refresh goes out while the link is down, so it's held back and the next snapshots get there first
		sim.Transport.DownUntil = c => c.Owner == sim.Host && c.RemotePeer == client ? sim.Now + 1 : 0;
		sim.As( sim.Host, () =>
		{
			var go = Game.ActiveScene.Directory.FindByGuid( id );
			var fire = go.Components.Create<SimFire>();
			fire.Intensity = 42;
			go.Network.Refresh( fire );
		} );
		sim.Transport.DownUntil = null;

		sim.RunSeconds( 3 );

		var intensity = sim.As( client, () => Game.ActiveScene.Directory.FindByGuid( id )?.Components.Get<SimFire>()?.Intensity );
		Assert.AreEqual( 42f, intensity );
	}

	/// <summary>
	/// A refresh can name a child only its owner has, like an item a game clones under its player on the owner
	/// alone. Nobody else can apply it, but the snapshots after it must still get through both ways.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void RefreshOfAChildOnlyTheOwnerHasKeepsSnapshotsFlowing( bool refreshDescendant )
	{
		using var sim = new NetSimulation();
		var owner = sim.Join();
		var observer = sim.Join();

		var id = sim.As( owner, () =>
		{
			var go = new GameObject( true, "player" );
			go.Components.Create<SimProp>();
			go.NetworkSpawn( Connection.Local );
			return go.Id;
		} );

		sim.RunSeconds( 1 );

		sim.As( owner, () =>
		{
			var go = Game.ActiveScene.Directory.FindByGuid( id );
			var item = new GameObject( go, true, "item" );

			if ( refreshDescendant )
				go.Network.Refresh( new GameObject( item, true, "part" ) );
			else
				go.Network.Refresh( item.Components.Create<SimFire>() );
		} );

		sim.RunSeconds( 1 );

		var position = new Vector3( 100, 200, 300 );
		sim.As( owner, () => Game.ActiveScene.Directory.FindByGuid( id ).WorldPosition = position );
		sim.As( sim.Host, () => Game.ActiveScene.Directory.FindByGuid( id ).Components.Get<SimProp>().Heat = 5 );

		sim.RunSeconds( 2 );

		foreach ( var peer in new[] { sim.Host, observer } )
		{
			var seen = sim.As( peer, () => Game.ActiveScene.Directory.FindByGuid( id ).WorldPosition );
			Assert.IsTrue( seen.Distance( position ) < 0.1f, $"{peer} sees the owner's object at {seen}" );
		}

		var heat = sim.As( owner, () => Game.ActiveScene.Directory.FindByGuid( id ).Components.Get<SimProp>().Heat );
		Assert.AreEqual( 5f, heat );
	}

	/// <summary>
	/// A refresh can name an object everyone has but only the owner put under its player, like something placed in
	/// the map. Nobody else finds it under the player, but the snapshots after it must still get through.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void RefreshOfASharedObjectOnlyTheOwnerMovedKeepsSnapshotsFlowing( bool refreshDescendant )
	{
		using var sim = new NetSimulation();
		var owner = sim.Join();
		var observer = sim.Join();

		var sharedId = Guid.NewGuid();
		foreach ( var peer in new[] { sim.Host, owner, observer } )
		{
			sim.As( peer, () =>
			{
				var shared = new GameObject( true, "shared" );
				shared.SetDeterministicId( sharedId );
			} );
		}

		var id = sim.As( owner, () =>
		{
			var go = new GameObject( true, "player" );
			go.Components.Create<SimProp>();
			go.NetworkSpawn( Connection.Local );
			return go.Id;
		} );

		sim.RunSeconds( 1 );

		sim.As( owner, () =>
		{
			var go = Game.ActiveScene.Directory.FindByGuid( id );
			var shared = Game.ActiveScene.Directory.FindByGuid( sharedId );
			shared.SetParent( go );

			if ( refreshDescendant )
				go.Network.Refresh( new GameObject( shared, true, "part" ) );
			else
				go.Network.Refresh( shared.Components.Create<SimFire>() );
		} );

		sim.RunSeconds( 1 );

		var position = new Vector3( 100, 200, 300 );
		sim.As( owner, () => Game.ActiveScene.Directory.FindByGuid( id ).WorldPosition = position );

		sim.RunSeconds( 2 );

		foreach ( var peer in new[] { sim.Host, observer } )
		{
			var seen = sim.As( peer, () => Game.ActiveScene.Directory.FindByGuid( id ).WorldPosition );
			Assert.IsTrue( seen.Distance( position ) < 0.1f, $"{peer} sees the owner's object at {seen}" );
		}
	}

	/// <summary>
	/// A component destroyed inside a callback batch stays valid until the batch ends. Refreshing it then, like
	/// <see cref="Prop"/> does after destroying its procedural components, must destroy it for everyone.
	/// </summary>
	[TestMethod]
	public void RefreshOfAComponentBeingDestroyedDestroysIt()
	{
		using var sim = new NetSimulation();
		var client = sim.Join();

		var id = sim.As( sim.Host, () =>
		{
			var go = new GameObject( true, "prop" );
			go.Components.Create<SimFire>();
			go.NetworkSpawn( null );
			return go.Id;
		} );

		sim.RunSeconds( 1 );

		sim.As( sim.Host, () =>
		{
			var go = Game.ActiveScene.Directory.FindByGuid( id );
			using var batch = CallbackBatch.Batch();
			var fire = go.Components.Get<SimFire>();
			fire.Destroy();
			go.Network.Refresh( fire );
		} );

		sim.RunSeconds( 1 );

		var hasFire = sim.As( client, () => Game.ActiveScene.Directory.FindByGuid( id ).Components.Get<SimFire>( true ) is not null );
		Assert.IsFalse( hasFire );
	}
}
