namespace NetSim;

[TestClass]
public class SmokeTests
{
	[TestInitialize]
	public void Init() => ThreadSafe.MarkMainThread();

	[TestMethod]
	public void ClientsJoinAndSeeHostObjects()
	{
		using var sim = new NetSimulation();

		var a = sim.Join();
		var b = sim.Join();

		var id = sim.As( sim.Host, () =>
		{
			var go = new GameObject( true, "crate" );
			go.WorldPosition = new Vector3( 10, 20, 30 );
			go.Network.Interpolation = false;
			go.NetworkSpawn();
			return go.Id;
		} );

		sim.RunSeconds( 0.5f );

		foreach ( var client in new[] { a, b } )
		{
			var go = sim.As( client, () => client.Scene.Directory.FindByGuid( id ) );
			Assert.IsNotNull( go, $"{client} doesn't have the object" );
			Assert.AreEqual( new Vector3( 10, 20, 30 ), sim.As( client, () => go.WorldPosition ) );
		}

		sim.As( sim.Host, () => sim.Host.Scene.Directory.FindByGuid( id ).WorldPosition = new Vector3( 1, 2, 3 ) );
		sim.RunSeconds( 0.5f );

		foreach ( var client in new[] { a, b } )
		{
			var position = sim.As( client, () => client.Scene.Directory.FindByGuid( id ).WorldPosition );
			Assert.AreEqual( new Vector3( 1, 2, 3 ), position, $"{client} has a stale position" );
		}
	}
}
