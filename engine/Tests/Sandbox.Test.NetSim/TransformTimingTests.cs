namespace NetSim;

/// <summary>
/// Objects moved in fixed update reach other peers as evenly as objects moved every frame.
/// </summary>
[TestClass]
public class TransformTimingTests
{
	[TestInitialize]
	public void Init() => ThreadSafe.MarkMainThread();

	const float Speed = 300;

	public sealed class FixedMover : Component
	{
		public double StartAt;
		public double StopAt = double.MaxValue;

		// After stopping: one correction from a frame update, then spinning every frame until this time
		public float CorrectBy;
		public double SpinUntil;
		public bool SpinWhileMoving;
		bool _corrected;

		protected override void OnFixedUpdate()
		{
			if ( !IsProxy && Time.Now >= StartAt && Time.Now < StopAt )
				WorldPosition = new Vector3( (float)(Time.Now - StartAt) * Speed, 0, 0 );
		}

		protected override void OnUpdate()
		{
			if ( SpinWhileMoving && !IsProxy )
				WorldRotation = Rotation.FromYaw( (float)Time.Now * 90 );

			if ( IsProxy || Time.Now < StopAt ) return;

			if ( !_corrected && CorrectBy != 0 )
			{
				WorldPosition += new Vector3( CorrectBy, 0, 0 );
				_corrected = true;
			}

			if ( Time.Now < SpinUntil )
				WorldRotation = Rotation.FromYaw( (float)Time.Now * 90 );
		}
	}

	static Guid SpawnMover( NetSimulation sim, double startIn = 0, double stopIn = double.MaxValue, float correctBy = 0, double spinFor = 0, bool spinWhileMoving = false ) => sim.As( sim.Host, () =>
	{
		var go = new GameObject( true, "mover" );
		go.WorldPosition = new Vector3( 0, 0, 1 );
		var mover = go.Components.Create<FixedMover>();
		mover.StartAt = Time.Now + startIn;
		mover.StopAt = Time.Now + stopIn;
		mover.CorrectBy = correctBy;
		mover.SpinUntil = Time.Now + stopIn + spinFor;
		mover.SpinWhileMoving = spinWhileMoving;
		go.NetworkSpawn();
		return go.Id;
	} );

	static float SeenX( NetSimulation sim, SimPeer peer, Guid id ) => sim.As( peer, () => Game.ActiveScene.Directory.FindByGuid( id ).WorldPosition.x );
	static Transform Seen( NetSimulation sim, SimPeer peer, Guid id ) => sim.As( peer, () => Game.ActiveScene.Directory.FindByGuid( id ).WorldTransform );

	static void AssertSameTransform( NetSimulation sim, SimPeer client, Guid id )
	{
		var host = Seen( sim, sim.Host, id );
		var seen = Seen( sim, client, id );
		Assert.IsTrue( host.Position.Distance( seen.Position ) < 0.01f && host.Rotation.Distance( seen.Rotation ) < 0.01f, $"host {host}, client {seen}" );
	}

	/// <summary>
	/// The spread of the speed a client sees, as a fraction of it. Moving at a constant speed, it should be close to 0.
	/// </summary>
	static double SpeedSpread( NetSimulation sim, SimPeer client, Guid id )
	{
		var speeds = new List<double>();
		var last = SeenX( sim, client, id );

		for ( var i = 0; i < 6 * sim.Settings.FrameRate; i++ )
		{
			sim.Step();
			var x = SeenX( sim, client, id );
			speeds.Add( (x - last) / sim.FrameTime );
			last = x;
		}

		var mean = speeds.Average();
		return Math.Sqrt( speeds.Average( s => (s - mean) * (s - mean) ) ) / mean;
	}

	[TestMethod]
	[DataRow( 60f, 0f, false )]
	[DataRow( 144f, 0f, false )]
	[DataRow( 144f, 0.3f, false )]
	[DataRow( 60f, 0.3f, true )]
	public void FixedUpdateMoverIsSmoothForClients( float fps, float frameJitter, bool headless ) => Smooth( fps, frameJitter, headless, 30 );

	/// <summary>
	/// Network updates faster than physics: some updates carry no new fixed step.
	/// </summary>
	[TestMethod]
	public void FixedUpdateMoverIsSmoothWhenSendingFasterThanPhysics() => Smooth( 144, 0.3f, false, 60 );

	/// <summary>
	/// Rotation set every frame while position moves in fixed update, the rotation setter carries the interpolated position.
	/// </summary>
	[TestMethod]
	public void FixedUpdateMoverIsSmoothWhileRotatingEveryFrame() => Smooth( 144, 0.3f, false, 30, spinWhileMoving: true );

	static void Smooth( float fps, float frameJitter, bool headless, int updateRate, bool spinWhileMoving = false )
	{
		var rate = ProjectSettings.Networking.UpdateRate;
		ProjectSettings.Networking.UpdateRate = updateRate;
		try
		{
			using var sim = new NetSimulation( new NetSimSettings { FrameRate = fps, FrameTimeJitter = frameJitter, Link = new SimLinkSettings { LatencySeconds = 0.05f } } );
			var client = sim.Join();
			sim.Host.Headless = headless;

			var id = SpawnMover( sim, spinWhileMoving: spinWhileMoving );
			sim.RunSeconds( 3 );

			var spread = SpeedSpread( sim, client, id );
			Assert.IsTrue( spread < 0.05, $"client sees the speed vary by {spread:P1}" );
		}
		finally
		{
			ProjectSettings.Networking.UpdateRate = rate;
		}
	}

	public sealed class FixedSpawner : Component
	{
		public Guid SpawnedId;

		protected override void OnFixedUpdate()
		{
			if ( SpawnedId != Guid.Empty ) return;

			var go = new GameObject( true, "spawned" );
			go.NetworkSpawn();
			go.WorldPosition = new Vector3( 500, 0, 0 );
			SpawnedId = go.Id;
		}
	}

	/// <summary>
	/// An object spawned and placed in fixed update snaps there for the owner, so it's sent there too, not partway from where it was created.
	/// </summary>
	[TestMethod]
	public void ObjectPlacedInFixedUpdateIsSentWhereItSnapped()
	{
		using var sim = new NetSimulation( new NetSimSettings { FrameRate = 144, Link = new SimLinkSettings { LatencySeconds = 0.05f } } );
		sim.Join();
		sim.RunSeconds( 1 );

		var spawner = sim.As( sim.Host, () => new GameObject( true, "spawner" ).Components.Create<FixedSpawner>() );
		while ( sim.As( sim.Host, () => spawner.SpawnedId ) == Guid.Empty )
			sim.Step();

		var sent = sim.As( sim.Host, () => Game.ActiveScene.Directory.FindByGuid( spawner.SpawnedId )._net.SampleLocal( Time.NowDouble ).Position.x );
		Assert.AreEqual( 500, sent, 0.01f );
	}

	[TestMethod]
	public void ObjectSpawnedWhileMovingNeverMovesBack()
	{
		using var sim = new NetSimulation( new NetSimSettings { FrameRate = 144, FrameTimeJitter = 0.3f, Link = new SimLinkSettings { LatencySeconds = 0.05f } } );
		var id = SpawnMover( sim );
		sim.RunSeconds( 1 );

		var client = sim.Join();
		sim.RunSeconds( 0.5f );

		var last = SeenX( sim, client, id );
		for ( var i = 0; i < 2 * sim.Settings.FrameRate; i++ )
		{
			sim.Step();
			var x = SeenX( sim, client, id );
			Assert.IsTrue( x >= last - 0.01f, $"moved back from {last} to {x} on frame {i}" );
			last = x;
		}
	}

	/// <summary>
	/// A client that joined while the object sat still sees it move promptly once it does.
	/// </summary>
	[TestMethod]
	public void StillObjectMovesPromptlyAfterJoin()
	{
		using var sim = new NetSimulation( new NetSimSettings { Link = new SimLinkSettings { LatencySeconds = 0.05f } } );
		var id = SpawnMover( sim, startIn: 3 );
		sim.RunSeconds( 1 );

		var client = sim.Join();
		sim.RunSeconds( 2.5f );

		Assert.IsTrue( SeenX( sim, client, id ) > Speed * 0.2f, $"client sees {SeenX( sim, client, id )} half a second after it started moving" );
	}

	/// <summary>
	/// Over a lossy link the object still ends where the owner left it.
	/// </summary>
	[TestMethod]
	[DataRow( 1 )]
	[DataRow( 2 )]
	[DataRow( 3 )]
	[DataRow( 4 )]
	[DataRow( 5 )]
	public void LossyLinkEndsWhereTheOwnerStopped( int seed )
	{
		using var sim = new NetSimulation( new NetSimSettings { Seed = seed, FrameRate = 144, Link = new SimLinkSettings { LatencySeconds = 0.05f, Loss = 0.2f } } );
		var client = sim.Join();
		var id = SpawnMover( sim, startIn: 0.5, stopIn: 2.5, spinFor: 1 );
		sim.RunSeconds( 6 );

		AssertSameTransform( sim, client, id );
	}

	/// <summary>
	/// A frame update correction right after fixed update moves still ends where the owner left it.
	/// </summary>
	[TestMethod]
	[DataRow( 1 )]
	[DataRow( 2 )]
	[DataRow( 3 )]
	public void FrameCorrectionAfterFixedMovesArrives( int seed )
	{
		var rate = ProjectSettings.Networking.UpdateRate;
		ProjectSettings.Networking.UpdateRate = 60;
		try
		{
			using var sim = new NetSimulation( new NetSimSettings { Seed = seed, FrameRate = 144, FrameTimeJitter = 0.3f, Link = new SimLinkSettings { LatencySeconds = 0.05f } } );
			var client = sim.Join();
			var id = SpawnMover( sim, startIn: 0.5, stopIn: 1.5, correctBy: 50 );
			sim.RunSeconds( 3 );

			AssertSameTransform( sim, client, id );
		}
		finally
		{
			ProjectSettings.Networking.UpdateRate = rate;
		}
	}
}
