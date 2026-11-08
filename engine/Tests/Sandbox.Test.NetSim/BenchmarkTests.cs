using System.Diagnostics;

namespace NetSim;

/// <summary>
/// Cost of a whole game's networking. Set SBOX_NETSIM_BENCH=1 to run; _CLIENTS, _PROPS, _SECONDS size it.
/// </summary>
[TestClass]
public class BenchmarkTests
{
	[TestInitialize]
	public void Init() => ThreadSafe.MarkMainThread();

	static int Env( string name, int fallback ) => int.TryParse( Environment.GetEnvironmentVariable( name ), out var v ) ? v : fallback;

	[TestMethod]
	public void Benchmark()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_NETSIM_BENCH" ) != "1" )
			Assert.Inconclusive( "set SBOX_NETSIM_BENCH=1" );

		var clients = Env( "SBOX_NETSIM_CLIENTS", 8 );
		var props = Env( "SBOX_NETSIM_PROPS", 1000 );
		var seconds = Env( "SBOX_NETSIM_SECONDS", 10 );

		var mesh = Environment.GetEnvironmentVariable( "SBOX_NETSIM_MESH" ) == "1";
		using var sim = new NetSimulation( new NetSimSettings { Mesh = mesh, Link = new SimLinkSettings { LatencySeconds = 0.05f, Loss = 0.01f } } );
		var joined = Enumerable.Range( 0, clients ).Select( _ => sim.Join() ).ToList();

		// SBOX_NETSIM_RPCS: reliable and unreliable broadcasts per second per client
		var rpcs = Env( "SBOX_NETSIM_RPCS", 3 );

		using var workload = new Workload( sim, new WorkloadSettings { Props = props, ShotsPerSecond = rpcs, FootstepsPerSecond = rpcs } );
		RpcLog.Enabled = false;
		foreach ( var c in joined ) workload.AddPlayer( c );

		GameplayTests.RunGame( sim, workload, 3 );

		sim.ResetCounters();
		var workloadTicks = 0L;
		var frames = (int)(seconds * sim.Settings.FrameRate);

		for ( var i = 0; i < frames; i++ )
		{
			var t0 = Stopwatch.GetTimestamp();
			workload.Tick();
			workloadTicks += Stopwatch.GetTimestamp() - t0;
			sim.Step();
		}

		var toMs = 1000.0 / Stopwatch.Frequency / frames;
		var host = sim.Host;
		var clientMs = sim.Clients.Average( c => c.FrameTicks * toMs );
		var clientAlloc = sim.Clients.Average( c => c.FrameAllocatedBytes / (double)frames );

		Console.WriteLine( $"clients {clients} props {props}{(mesh ? " mesh" : "")}: host {host.FrameTicks * toMs:F3} ms/frame, alloc {host.FrameAllocatedBytes / (double)frames / 1024:F1} KB/frame; client avg {clientMs:F3} ms/frame, alloc {clientAlloc / 1024:F1} KB/frame (workload {workloadTicks * toMs:F3} ms/frame, not counted)" );

		Console.WriteLine( $"  host sends {host.Sent.Bytes / seconds / 1024.0:F1} KB/s in {host.Sent.Messages / seconds:F0} msg/s ({host.Sent.Repeats * 100.0 / Math.Max( 1, host.Sent.Messages ):F0}% repeats within a frame); each client {sim.Clients.Average( c => c.Sent.Bytes ) / seconds / 1024.0:F1} KB/s in {sim.Clients.Average( c => c.Sent.Messages ) / seconds:F0} msg/s" );

		foreach ( var (type, t) in host.Sent.ByType.OrderByDescending( x => x.Value.Bytes ) )
			Console.WriteLine( $"    host {type,-28} {t.Bytes / seconds / 1024.0,9:F1} KB/s {t.Messages / seconds,8:F0} msg/s, {t.Bytes * 100.0 / Math.Max( 1, t.Uncompressed ),3:F0}% of uncompressed" );

		var clientTypes = sim.Clients.SelectMany( c => c.Sent.ByType ).GroupBy( x => x.Key );
		foreach ( var g in clientTypes.OrderByDescending( g => g.Sum( x => x.Value.Bytes ) ) )
			Console.WriteLine( $"    client {g.Key,-26} {g.Sum( x => x.Value.Bytes ) / (double)clients / seconds / 1024.0,9:F1} KB/s {g.Sum( x => x.Value.Messages ) / (double)clients / seconds,8:F0} msg/s" );

		RpcLog.Enabled = true;
	}

	/// <summary>
	/// Host owned ragdolls, 16 keyed body transforms each moving every frame. _CLIENTS and _RAGDOLLS size it.
	/// </summary>
	[TestMethod]
	public void RagdollBenchmark()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_NETSIM_BENCH" ) != "1" )
			Assert.Inconclusive( "set SBOX_NETSIM_BENCH=1" );

		var clients = Env( "SBOX_NETSIM_CLIENTS", 8 );
		var count = Env( "SBOX_NETSIM_RAGDOLLS", 100 );
		const int bodies = 16;

		using var sim = new NetSimulation( new NetSimSettings { Link = new SimLinkSettings { LatencySeconds = 0.05f, Loss = 0.01f } } );
		for ( var i = 0; i < clients; i++ ) sim.Join();

		var ragdolls = sim.As( sim.Host, () => Enumerable.Range( 0, count ).Select( i =>
		{
			var go = new GameObject( true, $"ragdoll {i}" );
			go.WorldPosition = new Vector3( i * 100, 0, 0 );
			var b = go.Components.Create<SimBodies>();
			for ( var k = 0; k < bodies; k++ ) b.Transforms.Set( k, new Transform( new Vector3( i * 100, k * 10, 0 ) ) );
			go.NetworkSpawn( null );
			return b;
		} ).ToList() );

		void Move( int frame ) => sim.As( sim.Host, () =>
		{
			for ( var i = 0; i < ragdolls.Count; i++ )
				for ( var k = 0; k < bodies; k++ )
					ragdolls[i].Transforms.Set( k, new Transform( new Vector3( i * 100, k * 10, MathF.Sin( frame * 0.1f + k ) * 20 ), Rotation.FromYaw( frame + k ) ) );
		} );

		sim.Run( (int)(2 * sim.Settings.FrameRate), Move );
		sim.ResetCounters();
		var frames = (int)(5 * sim.Settings.FrameRate);
		var moveTicks = 0L;
		for ( var f = 0; f < frames; f++ )
		{
			var t0 = Stopwatch.GetTimestamp();
			Move( f );
			moveTicks += Stopwatch.GetTimestamp() - t0;
			sim.Step();
		}

		var toMs = 1000.0 / Stopwatch.Frequency / frames;
		Console.WriteLine( $"ragdolls {count} x {bodies} bodies, clients {clients}: host {sim.Host.FrameTicks * toMs:F3} ms/frame, alloc {sim.Host.FrameAllocatedBytes / (double)frames / 1024:F1} KB/frame, Set() {moveTicks * toMs:F3} ms/frame; host sends {sim.Host.Sent.Bytes / 5.0 / 1024.0:F1} KB/s, client avg {sim.Clients.Average( c => c.FrameTicks ) * toMs:F3} ms/frame" );
		foreach ( var (type, t) in sim.Host.Sent.ByType.OrderByDescending( x => x.Value.Bytes ) )
			Console.WriteLine( $"    host {type,-28} {t.Bytes / 5.0 / 1024.0,9:F1} KB/s {t.Messages / 5.0,8:F0} msg/s" );
	}
}
