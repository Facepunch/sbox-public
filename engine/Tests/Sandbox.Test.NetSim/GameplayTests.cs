namespace NetSim;

[TestClass]
public class GameplayTests
{
	[TestInitialize]
	public void Init() => ThreadSafe.MarkMainThread();

	internal static void RunGame( NetSimulation sim, Workload workload, float seconds )
	{
		var frames = (int)(seconds * sim.Settings.FrameRate);
		for ( var i = 0; i < frames; i++ )
		{
			workload.Tick();
			sim.Step();
		}
	}

	internal static List<string> SettleAndCheck( NetSimulation sim, Workload workload, float settleSeconds = 3 )
	{
		workload.Stop();
		sim.RunSeconds( settleSeconds );

		var problems = WorldCheck.Differences( sim );
		problems.AddRange( WorldCheck.RpcProblems( sim, nameof( SimPlayer.Shoot ) ) );
		problems.AddRange( sim.GameInstance.Disconnects );
		return problems;
	}

	static void AssertConverges( NetSimulation sim, Workload workload, float settleSeconds = 3 )
	{
		var problems = SettleAndCheck( sim, workload, settleSeconds );
		Assert.AreEqual( 0, problems.Count, string.Join( "\n", problems ) );
	}

	static (NetSimulation Sim, Workload Workload) Start( int clients, WorkloadSettings workload = null, NetSimSettings settings = null )
	{
		var sim = new NetSimulation( settings );
		var joined = Enumerable.Range( 0, clients ).Select( _ => sim.Join() ).ToList();

		var w = new Workload( sim, workload ?? new WorkloadSettings { Props = 100 } );
		foreach ( var c in joined ) w.AddPlayer( c );

		return (sim, w);
	}

	[TestMethod]
	public void GameConverges()
	{
		var (sim, workload) = Start( 4 );
		using var _ = sim;
		using var __ = workload;

		RunGame( sim, workload, 10 );
		AssertConverges( sim, workload );
	}

	[TestMethod]
	public void GameConvergesInALobby()
	{
		var (sim, workload) = Start( 4, settings: new NetSimSettings { Mesh = true, Link = new SimLinkSettings { LatencySeconds = 0.05f, Loss = 0.03f } } );
		using var _ = sim;
		using var __ = workload;

		RunGame( sim, workload, 10 );
		AssertConverges( sim, workload, 5 );
	}

	[TestMethod]
	public void GameConvergesWithLossAndLatency()
	{
		var (sim, workload) = Start( 4, settings: new NetSimSettings { Link = new SimLinkSettings { LatencySeconds = 0.08f, Loss = 0.05f } } );
		using var _ = sim;
		using var __ = workload;

		RunGame( sim, workload, 10 );
		AssertConverges( sim, workload, 5 );
	}

	[TestMethod]
	public void GameConvergesWithInterpolation()
	{
		var (sim, workload) = Start( 3, new WorkloadSettings { Props = 60, Interpolation = true } );
		using var _ = sim;
		using var __ = workload;

		RunGame( sim, workload, 6 );
		AssertConverges( sim, workload );
	}

	[TestMethod]
	public void LateJoinersGetTheWorldAsItIs()
	{
		var (sim, workload) = Start( 2 );
		using var _ = sim;
		using var __ = workload;

		RunGame( sim, workload, 5 );

		// Join while props are held, moved and changing
		var late = sim.Join();
		workload.AddPlayer( late );
		RunGame( sim, workload, 3 );

		var later = sim.Join();
		workload.AddPlayer( later );
		RunGame( sim, workload, 3 );

		AssertConverges( sim, workload );
	}

	[TestMethod]
	public void SixteenPlayers()
	{
		var (sim, workload) = Start( 16, new WorkloadSettings { Props = 300 } );
		using var _ = sim;
		using var __ = workload;

		RunGame( sim, workload, 6 );
		AssertConverges( sim, workload );
	}

	/// <summary>
	/// Random mixes of latency, loss, player count and activity.
	/// </summary>
	[TestMethod]
	public void GameConvergesAcrossSeeds()
	{
		var failures = new List<string>();

		for ( var seed = 1; seed <= 8; seed++ )
		{
			var random = new Random( seed );
			var settings = new NetSimSettings
			{
				Seed = seed,
				Mesh = seed % 2 == 0,
				Link = new SimLinkSettings { LatencySeconds = 0.01f + (float)random.NextDouble() * 0.15f, Loss = (float)random.NextDouble() * 0.1f }
			};

			var workloadSettings = new WorkloadSettings
			{
				Seed = seed,
				Props = random.Next( 20, 150 ),
				GrabsPerSecond = (float)random.NextDouble(),
				ChurnPerSecond = (float)random.NextDouble() * 5,
				PropChangesPerSecond = (float)random.NextDouble() * 2
			};

			var (sim, workload) = Start( random.Next( 1, 6 ), workloadSettings, settings );
			using ( sim )
			using ( workload )
			{
				RunGame( sim, workload, 6 );
				var problems = SettleAndCheck( sim, workload, 5 );
				if ( problems.Count > 0 )
					failures.Add( $"seed {seed}: {problems.Count} problems, first: {problems[0]}" );
			}
		}

		Assert.AreEqual( 0, failures.Count, string.Join( "\n", failures ) );
	}
}
