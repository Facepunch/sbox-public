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

	/// <summary>
	/// A dedicated server has no ConnectionInfo of its own, so clients only know its Steam Id from the handshake.
	/// </summary>
	[TestMethod]
	public void ClientsSeeDedicatedServerSteamId()
	{
		var dedicated = typeof( Application ).GetProperty( nameof( Application.IsDedicatedServer ) );
		var steamId = typeof( Sandbox.Utility.Steam ).GetProperty( nameof( Sandbox.Utility.Steam.SteamId ) );
		var previousSteamId = Sandbox.Utility.Steam.SteamId;
		var serverSteamId = new SteamId( 0x0140000000000001UL );

		try
		{
			dedicated.SetValue( null, true );
			steamId.SetValue( null, serverSteamId );

			using var sim = new NetSimulation();
			var client = sim.Join();

			Assert.AreEqual( serverSteamId, sim.As( sim.Host, () => Connection.Local.SteamId ) );
			Assert.AreEqual( serverSteamId, sim.As( client, () => Connection.Host.SteamId ) );
		}
		finally
		{
			dedicated.SetValue( null, false );
			steamId.SetValue( null, previousSteamId );
		}
	}
}
