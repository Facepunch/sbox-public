namespace NetSim;

/// <summary>
/// The same seed has to give the same run, or before/after comparisons measure luck.
/// </summary>
[TestClass]
public class DeterminismTests
{
	[TestInitialize]
	public void Init() => ThreadSafe.MarkMainThread();

	static List<string> Record( bool mesh )
	{
		var settings = new NetSimSettings { Mesh = mesh, Link = new SimLinkSettings { LatencySeconds = 0.05f, Loss = 0.03f, JitterSeconds = 0.01f } };
		using var sim = new NetSimulation( settings );
		var log = new List<string>();
		sim.Transport.SendLog = log;

		var clients = Enumerable.Range( 0, 3 ).Select( _ => sim.Join() ).ToList();
		using var workload = new Workload( sim, new WorkloadSettings { Props = 50, Interpolation = true } );
		foreach ( var c in clients ) workload.AddPlayer( c );

		GameplayTests.RunGame( sim, workload, 4 );
		var late = sim.Join();
		workload.AddPlayer( late );
		GameplayTests.RunGame( sim, workload, 3 );

		return log;
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void SameSeedSameTraffic( bool mesh )
	{
		// The first simulation in a process leaves statics behind that change the next handshake
		Record( mesh );
		var a = Record( mesh );
		var b = Record( mesh );

		var first = Enumerable.Range( 0, Math.Min( a.Count, b.Count ) ).FirstOrDefault( i => a[i] != b[i], -1 );
		if ( first < 0 && a.Count == b.Count )
			return;

		if ( first < 0 ) first = Math.Min( a.Count, b.Count );
		var context = Enumerable.Range( Math.Max( 0, first - 5 ), 12 )
			.Select( i => $"{i}: {(i < a.Count ? a[i] : "-"),-50} | {(i < b.Count ? b[i] : "-")}" );

		var differing = Enumerable.Range( 0, Math.Min( a.Count, b.Count ) ).Count( i => a[i] != b[i] );
		Assert.Fail( $"{a.Count} vs {b.Count} sends, {differing} differ, first at {first}:\n{string.Join( "\n", context )}" );
	}
}
