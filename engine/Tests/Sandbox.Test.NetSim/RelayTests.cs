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

	static Connection Route( SimPeer to ) => Networking.System.FindConnection( to.Local.Id );

	[TestMethod]
	[DataRow( false, false )]
	[DataRow( true, false )]
	[DataRow( false, true )]
	public void ClosingDirectRoutePreservesQueuedReliableMessages( bool encodingPending, bool largeMessage )
	{
		using var sim = new NetSimulation( new NetSimSettings { Mesh = true, Link = new SimLinkSettings { LatencySeconds = 0.05f } } );
		var a = sim.Join();
		var b = sim.Join();
		RpcProbe.Calls.Clear();
		var gate = new TaskCompletionSource<byte[][]>();
		var random = new Random( 123 );
		var text = largeMessage ? new string( Enumerable.Range( 0, 200_000 ).Select( _ => (char)('!' + random.Next( 90 )) ).ToArray() ) : "ordered";
		sim.As( a, () =>
		{
			var route = Route( b );
			if ( encodingPending )
				route.QueueSend( gate.Task, NetFlags.Reliable );
			else
			{
				route.CloseDirectRoute();
				route.OpenDirectRoute();
			}
			RpcProbe.Static( 1, text );
			route.CloseDirectRoute();
			RpcProbe.Static( 2, "ordered" );
		} );
		sim.Transport.DownUntil = c => c.Owner == a && c.RemotePeer == b ? double.PositiveInfinity : 0;
		gate.SetResult( [] );
		sim.RunSeconds( 2 );
		var received = RpcProbe.Calls.Where( c => c.StartsWith( $"{b}: Static " ) ).Select( c => int.Parse( c.Split( ' ' )[2] ) ).ToArray();
		CollectionAssert.AreEqual( new[] { 1, 2 }, received );
	}

	[TestMethod]
	public void PreparedSnapshotUsesWholeMessageWhenRouteFallsBackToHost()
	{
		using var sim = new NetSimulation( new NetSimSettings { Mesh = true } );
		var a = sim.Join();
		var b = sim.Join();
		var id = Spawn( sim, a );
		var message = new byte[200_000];
		new Random( 42 ).NextBytes( message );
		Cull( id ).CopyTo( message, 0 );
		var encoded = Connection.EncodePacket( message );
		var chunks = Connection.CreatePackets( encoded.Span, NetFlags.Reliable );
		Assert.IsTrue( chunks.Length > 1 );
		var prepared = new byte[chunks.Length + 1][];
		prepared[0] = encoded.Span.ToArray();
		chunks.CopyTo( prepared, 1 );
		encoded.Release();

		sim.As( a, () =>
		{
			var route = Route( b );
			route.QueueSend( Task.FromResult( prepared ), NetFlags.Reliable, preparedSnapshot: true );
			route.CloseDirectRoute();
		} );
		sim.RunSeconds( 1 );
		Assert.IsTrue( CulledOn( sim, b, id ) );
	}

	[TestMethod]
	public void SimultaneousDirectRoutesDoNotBlockTheirAcknowledgements()
	{
		using var sim = new NetSimulation( new NetSimSettings { Mesh = true, Link = new SimLinkSettings { LatencySeconds = 0.05f } } );
		var a = sim.Join();
		var b = sim.Join();
		RpcProbe.Calls.Clear();
		sim.As( a, () => Route( b ).CloseDirectRoute() );
		sim.As( b, () => Route( a ).CloseDirectRoute() );

		sim.As( a, () =>
		{
			Route( b ).OpenDirectRoute();
			RpcProbe.Static( 1, "from-a" );
		} );
		sim.As( b, () =>
		{
			Route( a ).OpenDirectRoute();
			RpcProbe.Static( 2, "from-b" );
		} );

		// Far less than the five-second timeout: both acknowledgements must unblock each other.
		sim.RunSeconds( 1 );
		Assert.IsFalse( sim.As( a, () => Route( b ).HasPendingSends || Route( b ).RoutesThroughHost ) );
		Assert.IsFalse( sim.As( b, () => Route( a ).HasPendingSends || Route( a ).RoutesThroughHost ) );
		Assert.IsTrue( RpcProbe.Calls.Any( c => c.StartsWith( $"{a}: Static 2 from-b " ) ) );
		Assert.IsTrue( RpcProbe.Calls.Any( c => c.StartsWith( $"{b}: Static 1 from-a " ) ) );
	}

	[TestMethod]
	public void UnreliableMessagesFlowWhileWaitingForDirectRouteAck()
	{
		using var sim = new NetSimulation( new NetSimSettings { Mesh = true, Link = new SimLinkSettings { LatencySeconds = 0.05f } } );
		var a = sim.Join();
		var b = sim.Join();
		RpcProbe.Calls.Clear();
		sim.Transport.DownUntil = c => c.Owner == b && c.RemotePeer == sim.Host ? double.PositiveInfinity : 0;
		sim.As( a, () =>
		{
			Route( b ).CloseDirectRoute();
			Route( b ).OpenDirectRoute();
			Route( b ).OnDirectRouteAck( Guid.Empty ); // A stale acknowledgement cannot release this barrier
			Assert.IsTrue( Route( b ).HasPendingSends );
			RpcProbe.UnreliableStatic( 42 );
		} );
		sim.RunSeconds( 1 );
		Assert.IsTrue( sim.As( a, () => Route( b ).HasPendingSends ) );
		Assert.IsTrue( RpcProbe.Calls.Any( c => c.StartsWith( $"{b}: Unreliable 42 " ) ) );
	}

	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void HostChangeStopsRelayingAndReleasesPendingBarrier( bool localIsHost )
	{
		using var sim = new NetSimulation( new NetSimSettings { Mesh = true } );
		var a = sim.Join();
		var b = sim.Join();
		sim.As( a, () =>
		{
			var route = Route( b );
			route.CloseDirectRoute();
			route.OpenDirectRoute();
			Assert.IsTrue( route.HasPendingSends );

			// Simulate the host changing before the old host can acknowledge the barrier.
			var previousHost = a.System.HostConnection;
			var isHostProperty = typeof( NetworkSystem ).GetProperty( nameof( NetworkSystem.IsHost ) );
			if ( localIsHost )
				isHostProperty.SetValue( a.System, true );
			else
				a.System.SetHostConnection( route );

			Assert.IsFalse( route.RelayFor( NetFlags.Unreliable ) );
			route.FlushPendingSends();
			Assert.IsFalse( route.HasPendingSends );

			if ( localIsHost )
				isHostProperty.SetValue( a.System, false );
			else
				a.System.SetHostConnection( previousHost );
		} );
	}

	[TestMethod]
	public void MissingAckKeepsReliableMessagesOnTheHostRoute()
	{
		using var sim = new NetSimulation( new NetSimSettings { Mesh = true, Link = new SimLinkSettings { LatencySeconds = 0.05f } } );
		var a = sim.Join();
		var b = sim.Join();
		RpcProbe.Calls.Clear();
		sim.As( a, () =>
		{
			Route( b ).CloseDirectRoute();
			RpcProbe.Static( 0, "ordered" );
			Route( b ).OpenDirectRoute();
			RpcProbe.Static( 1, "ordered" );
		} );
		sim.Host.Paused = true;
		sim.RunSeconds( 6 );
		sim.As( a, () =>
		{
			Assert.IsTrue( Route( b ).RoutesThroughHost );
			Route( b ).OpenDirectRoute(); // Do not immediately start another barrier.
			Assert.IsTrue( Route( b ).RoutesThroughHost );
			RpcProbe.Static( 2, "ordered" );
		} );
		sim.Host.Paused = false;
		sim.RunSeconds( 2 );
		var received = RpcProbe.Calls.Where( c => c.StartsWith( $"{b}: Static " ) ).Select( c => int.Parse( c.Split( ' ' )[2] ) ).ToArray();
		CollectionAssert.AreEqual( new[] { 0, 1, 2 }, received );
	}

	/// <summary>
	/// The direct link is faster than going through the host, so messages sent right after switching to it would
	/// overtake the ones still on their way through the host.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void SwitchingToDirectKeepsOrder( bool hostHitches )
	{
		using var sim = new NetSimulation( new NetSimSettings { Mesh = true, Link = new SimLinkSettings { LatencySeconds = 0.05f } } );
		var a = sim.Join();
		var b = sim.Join();
		RpcProbe.Calls.Clear();

		sim.As( a, () => Route( b ).CloseDirectRoute() );
		sim.As( b, () => Route( a ).CloseDirectRoute() );

		for ( var i = 0; i < 120; i++ )
		{
			sim.As( a, () => RpcProbe.Static( i, "ordered" ) );

			if ( i == 60 )
			{
				sim.As( a, () => Route( b ).OpenDirectRoute() );
				sim.Host.Paused = hostHitches;
			}

			if ( i == 90 )
				sim.Host.Paused = false;

			sim.Step();
		}

		sim.RunSeconds( 3 );

		var received = RpcProbe.Calls.Where( c => c.StartsWith( $"{b}: Static " ) ).Select( c => int.Parse( c.Split( ' ' )[2] ) ).ToArray();
		CollectionAssert.AreEqual( Enumerable.Range( 0, 120 ).ToArray(), received, string.Join( ",", received ) );
		Assert.IsFalse( sim.As( a, () => Route( b ).RoutesThroughHost || Route( b ).HasPendingSends ) );
	}
}
