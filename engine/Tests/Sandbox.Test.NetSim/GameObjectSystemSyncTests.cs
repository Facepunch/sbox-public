namespace NetSim;

/// <summary>
/// A game object system with replicated state.
/// </summary>
public sealed class SyncedSimSystem : GameObjectSystem<SyncedSimSystem>
{
	[Sync] public int Value { get; set; }

	public SyncedSimSystem( Scene scene ) : base( scene ) { }
}

[TestClass]
public class GameObjectSystemSyncTests
{
	[TestInitialize]
	public void Init() => ThreadSafe.MarkMainThread();

	static int ValueOn( NetSimulation sim, SimPeer peer ) =>
		sim.As( peer, () => Game.ActiveScene.GetSystem<SyncedSimSystem>().Value );

	[TestMethod]
	public void SystemSyncPropertiesReachEarlyAndLateClients()
	{
		using var sim = new NetSimulation();
		var early = sim.Join();

		sim.As( sim.Host, () => Game.ActiveScene.GetSystem<SyncedSimSystem>().Value = 5 );
		sim.RunSeconds( 1 );
		Assert.AreEqual( 5, ValueOn( sim, early ) );

		var late = sim.Join();
		sim.RunSeconds( 1 );
		Assert.AreEqual( 5, ValueOn( sim, late ) );

		sim.As( sim.Host, () => Game.ActiveScene.GetSystem<SyncedSimSystem>().Value = 9 );
		sim.RunSeconds( 1 );
		Assert.AreEqual( 9, ValueOn( sim, early ) );
		Assert.AreEqual( 9, ValueOn( sim, late ) );
	}
}
