using System.Diagnostics;

namespace NetSim;

/// <summary>
/// One explosion in a big pile breaks and ignites hundreds of props in a single host frame.
/// </summary>
[TestClass]
public class ExplosionTests
{
	[TestInitialize]
	public void Init() => ThreadSafe.MarkMainThread();

	static int Env( string name, int fallback ) => int.TryParse( Environment.GetEnvironmentVariable( name ), out var v ) ? v : fallback;

	/// <summary>
	/// Break <paramref name="breaks"/> props with a gib broadcast each, and ignite <paramref name="ignites"/> with a refresh each.
	/// </summary>
	static void Explode( NetSimulation sim, List<Guid> props, int breaks, int ignites, int seq )
	{
		sim.As( sim.Host, () =>
		{
			var scene = Game.ActiveScene;
			for ( var i = 0; i < breaks + ignites && i < props.Count; i++ )
			{
				var go = scene.Directory.FindByGuid( props[i] );
				if ( !go.IsValid() ) continue;

				if ( i < breaks )
				{
					go.Components.Get<SimProp>().Hit( 100, seq + i );
					go.Destroy();
				}
				else
				{
					// Like Prop.Ignite: a fire child, refreshed on its own
					var fire = new GameObject( go, true, "fire" );
					fire.Components.Create<SimFire>();
					fire.Network.Refresh( fire );
				}
			}
		} );

		props.RemoveRange( 0, Math.Min( breaks, props.Count ) );
	}

	static List<Guid> HostProps( NetSimulation sim ) => sim.As( sim.Host, () => Game.ActiveScene.GetAllComponents<SimProp>().Select( p => p.GameObject.Id ).ToList() );

	[TestMethod]
	public void WorldAgreesAfterAnExplosion()
	{
		using var sim = new NetSimulation( new NetSimSettings { Mesh = true, Link = new SimLinkSettings { LatencySeconds = 0.05f, Loss = 0.02f } } );
		var clients = Enumerable.Range( 0, 3 ).Select( _ => sim.Join() ).ToList();
		using var workload = new Workload( sim, new WorkloadSettings { Props = 300, ChurnPerSecond = 0, GrabsPerSecond = 0 } );
		foreach ( var c in clients ) workload.AddPlayer( c );

		GameplayTests.RunGame( sim, workload, 2 );
		Explode( sim, HostProps( sim ), 100, 100, 1_000_000 );
		GameplayTests.RunGame( sim, workload, 2 );

		var problems = GameplayTests.SettleAndCheck( sim, workload );
		Assert.AreEqual( 0, problems.Count, string.Join( "\n", problems ) );
	}

	/// <summary>
	/// Set SBOX_NETSIM_BENCH=1 to run; _CLIENTS, _PROPS, _BREAKS, _IGNITES size it.
	/// </summary>
	[TestMethod]
	public void ExplosionBenchmark()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_NETSIM_BENCH" ) != "1" )
			Assert.Inconclusive( "set SBOX_NETSIM_BENCH=1" );

		var clientCount = Env( "SBOX_NETSIM_CLIENTS", 8 );
		var props = Env( "SBOX_NETSIM_PROPS", 2000 );
		var breaks = Env( "SBOX_NETSIM_BREAKS", 300 );
		var ignites = Env( "SBOX_NETSIM_IGNITES", 300 );
		var mesh = Environment.GetEnvironmentVariable( "SBOX_NETSIM_MESH" ) == "1";

		using var sim = new NetSimulation( new NetSimSettings { Mesh = mesh, Link = new SimLinkSettings { LatencySeconds = 0.05f, Loss = 0.01f } } );
		var clients = Enumerable.Range( 0, clientCount ).Select( _ => sim.Join() ).ToList();
		using var workload = new Workload( sim, new WorkloadSettings { Props = props, ChurnPerSecond = 0, GrabsPerSecond = 0, HitsPerSecond = 0 } );
		RpcLog.Enabled = false;
		foreach ( var c in clients ) workload.AddPlayer( c );

		GameplayTests.RunGame( sim, workload, 3 );

		// A quiet second for the baseline, then the explosion
		sim.ResetCounters();
		GameplayTests.RunGame( sim, workload, 1 );
		var frames = sim.Settings.FrameRate;
		var quietMs = sim.Host.FrameTicks * 1000.0 / Stopwatch.Frequency / frames;
		var quietBytes = sim.Host.Sent.Bytes;

		sim.ResetCounters();
		var hostProps = HostProps( sim );
		var t0 = Stopwatch.GetTimestamp();
		Explode( sim, hostProps, breaks, ignites, 1_000_000 );
		var explodeMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;

		sim.Step();
		var burstMs = sim.Host.FrameTicks * 1000.0 / Stopwatch.Frequency;
		var burstBytes = sim.Host.Sent.Bytes;
		var burstMessages = sim.Host.Sent.Messages;

		GameplayTests.RunGame( sim, workload, 1 );
		var secondBytes = sim.Host.Sent.Bytes;

		Console.WriteLine( $"clients {clientCount} props {props} breaks {breaks} ignites {ignites}{(mesh ? " mesh" : "")}" );
		Console.WriteLine( $"  host quiet {quietMs:F3} ms/frame, {quietBytes / 1024.0:F1} KB/s" );
		Console.WriteLine( $"  explode call {explodeMs:F2} ms, next host frame {burstMs:F2} ms, sent {burstBytes / 1024.0:F1} KB in {burstMessages} msgs" );
		Console.WriteLine( $"  host sent {secondBytes / 1024.0:F1} KB in the second after (incl. burst)" );

		foreach ( var (type, t) in sim.Host.Sent.ByType.OrderByDescending( x => x.Value.Bytes ) )
			Console.WriteLine( $"    host {type,-28} {t.Bytes / 1024.0,9:F1} KB {t.Messages,8} msgs" );

		RpcLog.Enabled = true;
	}
}
