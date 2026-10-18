namespace NetSim;

/// <summary>
/// Values for a new key arrive before the key itself when the packet that added it is lost.
/// </summary>
[TestClass]
public class KeyedTransformTests
{
	[TestInitialize]
	public void Init() => ThreadSafe.MarkMainThread();

	[TestMethod]
	[DataRow( 0.0f )]
	[DataRow( 0.05f )]
	[DataRow( 0.1f )]
	[DataRow( 0.2f )]
	[DataRow( 0.4f )]
	public void NewKeySettlesWhenItsFirstPacketIsLost( float movingSeconds )
	{
		using var sim = new NetSimulation( new NetSimSettings { Link = new SimLinkSettings { LatencySeconds = 0.05f } } );

		var id = sim.As( sim.Host, () =>
		{
			var go = new GameObject( true, "ragdoll" );
			go.Components.Create<SimBodies>().Transforms.Set( 0, new Transform( Vector3.Zero ) );
			go.NetworkSpawn( null );
			return go.Id;
		} );

		var client = sim.Join();
		sim.RunSeconds( 1 );

		SimBodies Find( SimPeer peer ) => sim.As( peer, () => Game.ActiveScene.Directory.FindByGuid( id ).Components.Get<SimBodies>() );
		var host = Find( sim.Host );

		// Lose what the host sends the client for the next two frames
		var until = sim.Now + 2.5 * sim.FrameTime;
		sim.Transport.DownUntil = c => c.Owner == sim.Host && c.RemotePeer == client ? until : 0;

		var position = new Vector3( 100, 0, 0 );
		var frames = Math.Max( 1, (int)(movingSeconds * sim.Settings.FrameRate) );
		sim.Run( frames, i =>
		{
			position += new Vector3( 0, 10, 0 );
			sim.As( sim.Host, () => host.Transforms.Set( 1, new Transform( position ) ) );
		} );

		sim.RunSeconds( 3 );

		var seen = sim.As( client, () => Find( client ).Transforms.Entries.TryGetValue( 1, out var t ) ? t.Position : (Vector3?)null );
		Assert.AreEqual( position, seen, $"moved for {movingSeconds}s" );
	}
}
