using Sandbox.Network;

namespace NetSim;

/// <summary>
/// Messages between clients that only reach each other through the host.
/// </summary>
[TestClass]
public class RelayTests
{
	[TestInitialize]
	public void Init() => ThreadSafe.MarkMainThread();

	// Raw and only obeyed from the object's owner, so it shows who a relayed message reached and as whom
	static byte[] Cull( Guid objectId )
	{
		var bs = ByteStream.Create( 32 );
		bs.Write( InternalMessageType.SetCullState );
		bs.Write( objectId );
		bs.Write( true );
		var bytes = bs.ToArray();
		bs.Dispose();
		return bytes;
	}

	static Guid Spawn( NetSimulation sim, SimPeer owner )
	{
		var id = sim.As( owner, () =>
		{
			var go = new GameObject( true, "relayed" );
			go.NetworkSpawn();
			return go.Id;
		} );

		sim.RunSeconds( 1 );
		return id;
	}

	static bool CulledOn( NetSimulation sim, SimPeer viewer, Guid id ) =>
		sim.As( viewer, () => Game.ActiveScene.Directory.FindByGuid( id ).IsNetworkCulled );

	static void Multicast( NetSimulation sim, SimPeer from, Guid claimedSender, byte[] message, params SimPeer[] targets )
	{
		sim.As( from, () =>
		{
			var ids = targets.Select( t => t.Local.Id ).ToArray();
			var encoded = NetworkSystem.EncodeMulticast( claimedSender, NetFlags.Reliable, ids, message );
			from.ToHost.Send( encoded, NetFlags.Reliable );
			encoded.Release();
		} );

		sim.RunSeconds( 0.5f );
	}

	[TestMethod]
	public void MulticastReachesItsTargetsOnly()
	{
		using var sim = new NetSimulation();
		var a = sim.Join();
		var b = sim.Join();
		var c = sim.Join();
		var d = sim.Join();

		var id = Spawn( sim, a );
		Multicast( sim, a, a.Local.Id, Cull( id ), b, c );

		Assert.IsTrue( CulledOn( sim, b, id ) );
		Assert.IsTrue( CulledOn( sim, c, id ) );
		Assert.IsFalse( CulledOn( sim, d, id ) );
	}

	[TestMethod]
	public void ForgedSenderIsNotPassedOn()
	{
		using var sim = new NetSimulation();
		var a = sim.Join();
		var b = sim.Join();
		var c = sim.Join();

		// Only b's to cull, a claims to be b
		var id = Spawn( sim, b );
		Multicast( sim, a, b.Local.Id, Cull( id ), c );

		Assert.IsFalse( CulledOn( sim, c, id ) );
	}
}
