using System;
using System.Diagnostics;
using System.Linq;

namespace NetworkTests;

/// <summary>
/// End to end tests of the delta snapshot system: a host and several clients exchange real snapshot and ack
/// messages through a simulated transport, and every client must end up with the host's values.
/// </summary>
[TestClass]
[DoNotParallelize]
public class DeltaSnapshotTests
{
	static void RunAndSettle( DeltaSnapshotSim sim, int ticks )
	{
		for ( var i = 0; i < ticks; i++ )
			sim.Tick();

		var settled = sim.Settle();
		var mismatches = sim.Mismatches();

		Assert.AreEqual( 0, mismatches.Count, $"{mismatches.Count} stale slots, first: {mismatches.FirstOrDefault()}" );
		Assert.AreNotEqual( -1, settled, "host kept sending after values stopped changing" );
	}

	[TestMethod]
	public void ClientsConverge()
	{
		RunAndSettle( new DeltaSnapshotSim( new SimSettings() ), 60 );
	}

	[TestMethod]
	public void ClientsConvergeWithLossAndLatency()
	{
		RunAndSettle( new DeltaSnapshotSim( new SimSettings { Loss = 0.1f, LatencyTicks = 5 } ), 120 );
	}

	[TestMethod]
	public void ClientsConvergeWhenLatencyOutlivesPredictions()
	{
		// Predictions expire after 0.25 s, 12 ticks of 1/30 s is longer so values get resent
		RunAndSettle( new DeltaSnapshotSim( new SimSettings { LatencyTicks = 12, Loss = 0.05f } ), 120 );
	}

	[TestMethod]
	public void ClientsConvergeWithCullingAndRemovedSlots()
	{
		RunAndSettle( new DeltaSnapshotSim( new SimSettings { CullToggleRate = 0.05f, SlotToggleRate = 0.05f, Loss = 0.05f } ), 150 );
	}

	[TestMethod]
	public void ClientsConvergeWithManyClients()
	{
		RunAndSettle( new DeltaSnapshotSim( new SimSettings { Clients = 70, Objects = 40, Loss = 0.02f } ), 60 );
	}

	[TestMethod]
	public void ClientsConvergeWhenObjectsComeAndGo()
	{
		var sim = new DeltaSnapshotSim( new SimSettings { Loss = 0.05f } );
		var random = new Random( 7 );

		for ( var i = 0; i < 120; i++ )
		{
			if ( i % 5 == 0 )
				sim.AddObject();

			if ( i % 7 == 0 && sim.HostObjects.Count > 10 )
				sim.RemoveObject( sim.HostObjects[random.Next( sim.HostObjects.Count )] );

			sim.Tick();
		}

		RunAndSettle( sim, 0 );
	}

	/// <summary>
	/// Many random lossy runs. Each seed gets its own latency, loss, culling and slot churn.
	/// </summary>
	[TestMethod]
	public void ClientsConvergeAcrossSeeds()
	{
		var failures = new System.Collections.Generic.List<string>();

		for ( var seed = 1; seed <= 25; seed++ )
		{
			var random = new Random( seed );
			var sim = new DeltaSnapshotSim( new SimSettings
			{
				Seed = seed,
				Objects = 60,
				Clients = 1 + random.Next( 6 ),
				LatencyTicks = random.Next( 1, 15 ),
				Loss = (float)random.NextDouble() * 0.25f,
				ChangeRate = 0.2f + (float)random.NextDouble() * 0.6f,
				CullToggleRate = (float)random.NextDouble() * 0.05f,
				SlotToggleRate = (float)random.NextDouble() * 0.05f
			} );

			for ( var i = 0; i < 150; i++ )
				sim.Tick();

			var settled = sim.Settle( 600 );
			var mismatches = sim.Mismatches();

			if ( mismatches.Count > 0 ) failures.Add( $"seed {seed}: {mismatches.Count} stale slots" );
			if ( settled == -1 ) failures.Add( $"seed {seed}: host kept sending ({sim.BytesSentThisTick} bytes last tick)" );
		}

		Assert.AreEqual( 0, failures.Count, string.Join( "; ", failures ) );
	}

	[TestMethod]
	public void ReconnectedClientGetsEverythingAgain()
	{
		var sim = new DeltaSnapshotSim( new SimSettings { Loss = 0.02f } );

		for ( var i = 0; i < 60; i++ )
		{
			if ( i == 30 )
				sim.ReconnectClient( 1 );

			sim.Tick();
		}

		RunAndSettle( sim, 0 );

		// A quiet scene after a reconnect still converges
		sim.ReconnectClient( 2 );
		RunAndSettle( sim, 0 );
	}

	[TestMethod]
	public void NothingIsResentOnceEveryoneHasIt()
	{
		var sim = new DeltaSnapshotSim( new SimSettings() );
		RunAndSettle( sim, 30 );

		var sent = sim.HostBytesSent;
		for ( var i = 0; i < 30; i++ )
			sim.Tick( mutate: false );

		Assert.AreEqual( sent, sim.HostBytesSent );
	}

	[TestMethod]
	public void LostAcknowledgementsAreNotKeptForever()
	{
		var sim = new DeltaSnapshotSim( new SimSettings { Loss = 0.2f } );

		for ( var i = 0; i < 200; i++ )
			sim.Tick();

		var early = sim.Host.AwaitingAckCount;

		for ( var i = 0; i < 600; i++ )
			sim.Tick();

		// 30 ticks a second and sends are kept 5 s, so the count stays around one window's worth
		Assert.IsTrue( sim.Host.AwaitingAckCount <= early * 1.5f, $"{early} after 200 ticks, {sim.Host.AwaitingAckCount} after 800" );
	}

	/// <summary>
	/// The bytes the host puts on the wire for a fixed scenario. A refactor of the snapshot internals must not
	/// change what gets sent; update the constant only for an intended protocol or behaviour change.
	/// </summary>
	[TestMethod]
	public void WireOutputIsUnchanged()
	{
		var sim = new DeltaSnapshotSim( new SimSettings
		{
			Objects = 120,
			Clients = 5,
			Loss = 0.05f,
			LatencyTicks = 4,
			CullToggleRate = 0.03f,
			SlotToggleRate = 0.03f,
			Seed = 99
		} );

		for ( var i = 0; i < 150; i++ )
		{
			if ( i == 70 )
				sim.ReconnectClient( 3 );

			sim.Tick();
		}

		Console.WriteLine( $"wire hash {sim.WireHash:X16}, {sim.HostMessagesSent} messages, {sim.HostBytesSent} bytes" );
		Assert.AreEqual( ExpectedWireHash, sim.WireHash, $"wire output changed: {sim.WireHash:X16}" );
	}

	const ulong ExpectedWireHash = 0xCEDE0E000318F7E6UL;

	/// <summary>
	/// Host cost per network tick for a busy server. Set SBOX_NET_BENCH=1 to run.
	/// </summary>
	[TestMethod]
	public void Benchmark()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_NET_BENCH" ) != "1" )
			Assert.Inconclusive( "set SBOX_NET_BENCH=1" );

		var objects = int.TryParse( Environment.GetEnvironmentVariable( "SBOX_NET_BENCH_OBJECTS" ), out var o ) ? o : 2000;
		var clients = int.TryParse( Environment.GetEnvironmentVariable( "SBOX_NET_BENCH_CLIENTS" ), out var c ) ? c : 8;
		var slots = int.TryParse( Environment.GetEnvironmentVariable( "SBOX_NET_BENCH_SLOTS" ), out var s ) ? s : 8;

		var sim = new DeltaSnapshotSim( new SimSettings
		{
			Objects = objects,
			Clients = clients,
			SlotsPerObject = slots,
			ChangeRate = 0.3f,
			LatencyTicks = 3,
			Loss = 0.01f
		} );

		// Past the 5 s that unacknowledged sends are kept, so retention is at its steady state
		for ( var i = 0; i < 200; i++ )
			sim.Tick();

		const int ticks = 300;
		double send = 0, receive = 0, clientReceive = 0;
		long alloc = 0, bytes = 0, clientAlloc = 0;

		for ( var i = 0; i < ticks; i++ )
		{
			sim.Tick();
			send += sim.LastSendTicks;
			receive += sim.LastHostReceiveTicks;
			alloc += sim.LastHostAllocatedBytes;
			bytes += sim.BytesSentThisTick;
			clientReceive += sim.LastClientReceiveTicks;
			clientAlloc += sim.LastClientAllocatedBytes;
		}

		var toMs = 1000.0 / Stopwatch.Frequency / ticks;
		Console.WriteLine( $"objects {objects} clients {clients} slots {slots}: send {send * toMs:F3} ms, acks+tick {receive * toMs:F3} ms, host total {(send + receive) * toMs:F3} ms, alloc {alloc / ticks / 1024.0:F1} KB, wire {bytes / ticks / 1024.0:F1} KB per tick" );
		Console.WriteLine( $"  per client: receive {clientReceive * toMs / clients:F3} ms, alloc {clientAlloc / ticks / clients / 1024.0:F1} KB per tick" );
	}
}
