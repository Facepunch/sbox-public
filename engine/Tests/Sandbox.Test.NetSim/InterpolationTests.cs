namespace NetSim;

/// <summary>
/// How far behind other peers see something moving, and that it keeps moving smoothly.
/// </summary>
[TestClass]
public class InterpolationTests
{
	[TestInitialize]
	public void Init() => ThreadSafe.MarkMainThread();

	const float Speed = 100;

	internal record struct Seen( double DelaySeconds, double Stalls );

	/// <summary>
	/// The owner moves an object at a constant speed. Returns how far behind the viewer sees it on average, in seconds,
	/// and the share of frames it didn't move there.
	/// </summary>
	internal static Seen Measure( NetSimulation sim, SimPeer owner, SimPeer viewer, float seconds = 10 )
	{
		var id = sim.As( owner, () =>
		{
			var go = new GameObject( true, "mover" );
			go.NetworkSpawn();
			return go.Id;
		} );

		var x = 0f;
		float SeenBy( SimPeer peer ) => sim.As( peer, () => Game.ActiveScene.Directory.FindByGuid( id ).WorldPosition.x );
		void Move() => sim.As( owner, () => Game.ActiveScene.Directory.FindByGuid( id ).WorldPosition = new Vector3( x += Speed * sim.FrameTime, 0, 0 ) );

		sim.Run( (int)(2 * sim.Settings.FrameRate), _ => Move() );

		var delay = 0.0;
		var stalls = 0;
		var frames = (int)(seconds * sim.Settings.FrameRate);
		var last = float.NaN;

		for ( var i = 0; i < frames; i++ )
		{
			Move();
			sim.Step();

			var seen = SeenBy( viewer );
			delay += (x - seen) / Speed;

			if ( seen == last )
				stalls++;

			last = seen;
		}

		return new Seen( delay / frames, stalls / (double)frames );
	}

	static NetSimulation Start( float loss, float jitter ) =>
		new( new NetSimSettings { Link = new SimLinkSettings { LatencySeconds = 0.05f, Loss = loss, JitterSeconds = jitter } } );

	const double Frame = 1.0 / 60;

	/// <summary>
	/// About one update of buffer on a clean link. A lossy or jittery one grows it, never past 100 ms, and rarely stalls.
	/// </summary>
	[TestMethod]
	[DataRow( 0f, 0f )]
	[DataRow( 0.01f, 0.02f )]
	[DataRow( 0.05f, 0.01f )]
	public void HostOwnedObjectBuffersWhatTheLinkNeeds( float loss, float jitter )
	{
		using var sim = Start( loss, jitter );
		var a = sim.Join();

		var seen = Measure( sim, sim.Host, a );
		Console.WriteLine( $"{loss:P0} loss, {jitter * 1000} ms jitter: {seen.DelaySeconds * 1000:F0} ms behind, {seen.Stalls:P1} frames stalled" );

		var most = loss == 0 && jitter == 0 ? 0.05 + 1.0 / 30 + 0.002 : 0.05 + 0.1 + Frame + 0.002;
		Assert.IsTrue( seen.DelaySeconds < most, $"{seen.DelaySeconds * 1000:F0} ms behind" );
		Assert.IsTrue( seen.Stalls < 0.01, $"{seen.Stalls:P1} frames stalled" );
	}

	/// <summary>
	/// Through the host, so one more link and a host frame.
	/// </summary>
	[TestMethod]
	public void ClientOwnedObjectThroughTheHost()
	{
		using var sim = Start( 0.01f, 0.01f );
		var a = sim.Join();
		var b = sim.Join();

		var seen = Measure( sim, a, b );
		Console.WriteLine( $"through the host: {seen.DelaySeconds * 1000:F0} ms behind, {seen.Stalls:P1} frames stalled" );

		Assert.IsTrue( seen.DelaySeconds < 0.1 + 0.1 + 2 * Frame + 0.002, $"{seen.DelaySeconds * 1000:F0} ms behind" );
		Assert.IsTrue( seen.Stalls < 0.01, $"{seen.Stalls:P1} frames stalled" );
	}

	/// <summary>
	/// Jitter on both links through the host is more than two updates of buffer covered. The buffer grows for it, and
	/// shrinks back once the link is clean.
	/// </summary>
	[TestMethod]
	public void BufferFollowsTheLink()
	{
		using var sim = Start( 0, 0.04f );
		var a = sim.Join();
		var b = sim.Join();

		var jittery = Measure( sim, a, b );
		Console.WriteLine( $"40 ms jitter: {jittery.DelaySeconds * 1000:F0} ms behind, {jittery.Stalls:P1} frames stalled" );
		Assert.IsTrue( jittery.Stalls < 0.005, $"{jittery.Stalls:P1} frames stalled" );

		sim.Transport.Settings = new SimLinkSettings { LatencySeconds = 0.05f };
		sim.RunSeconds( 10 );

		var clean = Measure( sim, a, b );
		Console.WriteLine( $"then clean: {clean.DelaySeconds * 1000:F0} ms behind, {clean.Stalls:P1} frames stalled" );
		Assert.IsTrue( clean.DelaySeconds < 0.1 + 1.0 / 30 + Frame + 0.002, $"{clean.DelaySeconds * 1000:F0} ms behind" );
	}

	/// <summary>
	/// After standing still, movement starts smoothly instead of jumping to the first new update and waiting there.
	/// </summary>
	[TestMethod]
	public void ResumesWithoutJumping()
	{
		using var sim = Start( 0, 0 );
		var a = sim.Join();

		var id = sim.As( sim.Host, () =>
		{
			var go = new GameObject( true, "mover" );
			go.NetworkSpawn();
			return go.Id;
		} );

		var x = 0f;
		var step = Speed * sim.FrameTime;
		float Seen() => sim.As( a, () => Game.ActiveScene.Directory.FindByGuid( id ).WorldPosition.x );
		void Move() => sim.As( sim.Host, () => Game.ActiveScene.Directory.FindByGuid( id ).WorldPosition = new Vector3( x += step, 0, 0 ) );

		sim.Run( 60, _ => Move() );
		sim.RunSeconds( 1 );

		var last = Seen();
		var moved = false;

		for ( var i = 0; i < 30; i++ )
		{
			Move();
			sim.Step();

			var seen = Seen();
			Assert.IsTrue( seen - last < step * 1.5f, $"jumped {seen - last} in frame {i}" );
			Assert.IsFalse( moved && seen == last, $"stopped in frame {i}" );

			moved |= seen != last;
			last = seen;
		}

		Assert.IsTrue( moved );
	}

	/// <summary>
	/// net_interp_time still fixes the buffer.
	/// </summary>
	[TestMethod]
	public void FixedInterpolationTime()
	{
		Networking.InterpolationTimeOverride = 0.1f;

		try
		{
			using var sim = Start( 0, 0 );
			var a = sim.Join();

			var seen = Measure( sim, sim.Host, a );
			Assert.AreEqual( 0.05 + 0.1 - Frame, seen.DelaySeconds, 0.002 );
		}
		finally
		{
			Networking.InterpolationTimeOverride = 0;
		}
	}

	/// <summary>
	/// Updates go out at the update rate whatever the frame rate, not at the first frame after each interval.
	/// </summary>
	[TestMethod]
	[DataRow( 45f )]
	[DataRow( 60f )]
	[DataRow( 144f )]
	public void SendsAtTheUpdateRate( float fps )
	{
		using var sim = new NetSimulation( new NetSimSettings { FrameRate = fps } );
		sim.Join();

		var id = sim.As( sim.Host, () =>
		{
			var go = new GameObject( true, "mover" );
			go.NetworkSpawn();
			return go.Id;
		} );

		var x = 0f;
		void Move() => sim.As( sim.Host, () => Game.ActiveScene.Directory.FindByGuid( id ).WorldPosition = new Vector3( x += 1, 0, 0 ) );

		sim.Run( (int)fps, _ => Move() );
		sim.ResetCounters();
		sim.Run( (int)(fps * 10), _ => Move() );

		var perSecond = sim.Host.Sent.ByType[Sandbox.Network.InternalMessageType.DeltaSnapshotCluster].Messages / 10.0;
		Assert.AreEqual( ProjectSettings.Networking.UpdateRate, perSecond, 0.5, $"{fps} fps" );
	}
}
