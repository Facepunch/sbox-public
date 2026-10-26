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
}
