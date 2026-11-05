using Sandbox.Audio;
using System;

namespace AudioTests;

[TestClass]
public class DopplerTest
{
	const float M = 39.37f;
	static readonly float C = Doppler.SpeedOfSound;

	[TestMethod]
	public void ApproachingRisesRecedingFalls()
	{
		var listener = Vector3.Zero;
		var source = new Vector3( 100, 0, 0 ) * M;
		var tenth = new Vector3( C * 0.1f, 0, 0 );

		// Source toward the listener at a tenth of the speed of sound: c / (c - v).
		Assert.AreEqual( 1f / 0.9f, Doppler.Factor( source, -tenth, listener, Vector3.Zero, 1f ), 1e-4f );
		Assert.AreEqual( 1f / 1.1f, Doppler.Factor( source, tenth, listener, Vector3.Zero, 1f ), 1e-4f );

		// Listener toward the source: (c + v) / c.
		Assert.AreEqual( 1.1f, Doppler.Factor( source, Vector3.Zero, listener, tenth, 1f ), 1e-4f );

		// Moving together, or across the line, doesn't shift.
		Assert.AreEqual( 1f, Doppler.Factor( source, tenth, listener, tenth, 1f ), 1e-3f );
		Assert.AreEqual( 1f, Doppler.Factor( source, new Vector3( 0, C * 0.1f, 0 ), listener, Vector3.Zero, 1f ), 1e-4f );

		// Scale 0 is off.
		Assert.AreEqual( 1f, Doppler.Factor( source, -tenth, listener, Vector3.Zero, 0f ) );
	}

	[TestMethod]
	public void TrackerFollowsMovementAndIgnoresTeleports()
	{
		var tracker = new VelocityTracker();
		var speed = 30f * M;
		for ( int i = 0; i <= 60; i++ ) tracker.Update( new Vector3( speed * i / 60f, 0, 0 ), i / 60.0 );
		Assert.AreEqual( speed, tracker.Velocity.x, speed * 0.01f );

		// Positions and timestamps that don't quite line up still read close to the steady speed.
		for ( int i = 61; i <= 120; i++ )
		{
			double t = i / 60.0 + (i % 2 == 0 ? 0.002 : -0.002);
			tracker.Update( new Vector3( speed * i / 60f, 0, 0 ), t );
		}
		Assert.AreEqual( speed, tracker.Velocity.x, speed * 0.1f );

		// A teleport doesn't blip, nor does the step back to normal motion after it.
		var before = tracker.Velocity;
		tracker.Update( new Vector3( 1_000_000, 0, 0 ), 121 / 60.0 );
		Assert.AreEqual( before, tracker.Velocity );
		tracker.Update( new Vector3( 1_000_000 + speed / 60f, 0, 0 ), 122 / 60.0 );
		Assert.AreEqual( before, tracker.Velocity );
	}

	[TestMethod]
	public void TrackerTakesTheFirstSpeedAsItIs()
	{
		// A sound that starts out already moving, say on a player in a car, is at its speed from the first measurement.
		var tracker = new VelocityTracker();
		var speed = 30f * M;
		tracker.Update( Vector3.Zero, 0.0 );
		Assert.IsFalse( tracker.Known );
		tracker.Update( new Vector3( speed / 60f, 0, 0 ), 1 / 60.0 );
		Assert.IsTrue( tracker.Known );
		Assert.AreEqual( speed, tracker.Velocity.x, speed * 0.001f );
	}

	[TestMethod]
	public void TrackerTakesASuddenSpeedThatHolds()
	{
		// A rocket launched from rest: the first update is ignored as a jump, the next ones are real.
		var tracker = new VelocityTracker();
		var speed = 100f * M;
		for ( int i = 0; i <= 10; i++ ) tracker.Update( Vector3.Zero, i / 60.0 );
		for ( int i = 1; i <= 30; i++ ) tracker.Update( new Vector3( speed * i / 60f, 0, 0 ), (10 + i) / 60.0 );
		Assert.AreEqual( speed, tracker.Velocity.x, speed * 0.01f );
	}
}
