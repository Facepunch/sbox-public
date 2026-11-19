using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Nav.Lib.Tests.Sim;

namespace Nav.Lib.Tests;

/// <summary>
/// How an agent turns corners: turn rate, speed kept, deviation from the route and time, within limits.
/// </summary>
[TestClass]
public class CrowdCorneringTests
{
	const float Delta = 0.02f;
	static readonly AgentParams Settings = AgentParams.Create( 16, 64, 180, 1200 );

	[TestMethod]
	[DataRow( "straight", false )]
	[DataRow( "doorway", false )]
	[DataRow( "doorway", true )]
	[DataRow( "corner", false )]
	[DataRow( "corner", true )]
	public void AgentTurnsSmoothlyWithoutExcessiveSlowdownOrDetours( string layout, bool reverse )
	{
		var sim = new Sim( new() { Doorway = layout == "doorway", Corner = layout == "corner" } );
		var start = layout == "corner" ? At( 140, 100 ) : At( 100, 100 );
		var target = layout == "corner" ? At( 540, 500 ) : At( 540, 100 );
		if ( reverse ) (start, target) = (target, start);

		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, sim.Query.FindPath( start, target, new Vector3( 8 ), default, path ) );
		var route = path.Corners.ToArray();
		float shortest = path.Length;

		int agent = sim.Add( start, target, Settings );
		var samples = new List<Vector3> { sim.Position( agent ) };
		for ( int tick = 0; tick < 1000 && Vector3.Distance( samples[^1], target ) > Settings.Radius / 4; tick++ )
		{
			sim.Step( Delta );
			var point = sim.Position( agent );
			Assert.IsTrue( float.IsFinite( point.X ) && float.IsFinite( point.Y ) && float.IsFinite( point.Z ) );
			samples.Add( point );
		}
		Assert.IsTrue( Vector3.Distance( samples[^1], target ) <= Settings.Radius / 4, "The agent did not arrive within 20 seconds" );

		var measured = Measure( samples, route );
		// Heading changes are degrees per 20 ms tick; speed is units per second.
		var (maxTurn, maxRmsTurn, minSpeed, maxDeviation) = layout switch
		{
			"straight" => (0.5f, 0.1f, 178f, 1f),
			"doorway" => (10f, 3f, 150f, 8f),
			_ => (9f, 2f, 170f, 6f)
		};
		Assert.IsTrue( measured.PeakTurn <= maxTurn, $"Abrupt turn: {measured}" );
		Assert.IsTrue( measured.RmsTurn <= maxRmsTurn, $"Excessive heading variation: {measured}" );
		Assert.IsTrue( measured.MinimumSpeed >= minSpeed, $"Excessive slowdown: {measured}" );
		Assert.IsTrue( measured.Deviation <= maxDeviation, $"Wide turn: {measured}" );
		// Half a radius of extra travel and 0.35 seconds for acceleration and turns.
		Assert.IsTrue( measured.Length <= shortest + Settings.Radius / 2, $"Detour: {measured}" );
		Assert.IsTrue( measured.Seconds <= shortest / Settings.MaxSpeed + 0.35f, $"Slow arrival: {measured}" );
	}

	[TestMethod]
	public void CornerMetricsDistinguishSmoothTurnsFromSnapsAndSlowdowns()
	{
		Vector3[] route = [At( 0, 0 ), At( 200, 0 ), At( 200, 200 )];
		var sharp = new List<Vector3>();
		for ( int i = 0; i <= 50; i++ ) sharp.Add( At( i * 4, 0 ) );
		for ( int i = 1; i <= 50; i++ ) sharp.Add( At( 200, i * 4 ) );
		var smooth = new List<Vector3>();
		for ( int i = 0; i <= 40; i++ ) smooth.Add( At( i * 4, 0 ) );
		for ( int i = 1; i <= 16; i++ )
		{
			float angle = -MathF.PI / 2 + i * MathF.PI / 32;
			smooth.Add( At( 160 + MathF.Cos( angle ) * 40, 40 + MathF.Sin( angle ) * 40 ) );
		}
		for ( int i = 1; i <= 40; i++ ) smooth.Add( At( 200, 40 + i * 4 ) );

		var sharpMetrics = Measure( sharp, route );
		var smoothMetrics = Measure( smooth, route );
		Assert.AreEqual( 90f, sharpMetrics.PeakTurn, 0.01f );
		Assert.IsTrue( smoothMetrics.PeakTurn < 6 && smoothMetrics.RmsTurn < sharpMetrics.RmsTurn );
		var stopped = new List<Vector3>( smooth );
		stopped.Insert( 45, stopped[44] );
		var stoppedMetrics = Measure( stopped, route );
		Assert.AreEqual( 0f, stoppedMetrics.MinimumSpeed );
		Assert.IsTrue( stoppedMetrics.Seconds > smoothMetrics.Seconds, "A stop must not hide behind a smooth heading score" );
	}

	readonly record struct Metrics( float PeakTurn, float RmsTurn, float MinimumSpeed, float Length, float Deviation, float Seconds );

	static Metrics Measure( IReadOnlyList<Vector3> samples, Vector3[] route )
	{
		float peak = 0, squaredTurns = 0, minimumSpeed = float.MaxValue, length = 0, deviation = 0;
		int turns = 0;
		for ( int i = 1; i < samples.Count; i++ )
		{
			float moved = Flat( samples[i], samples[i - 1] );
			length += moved;
			float nearest = float.MaxValue;
			for ( int edge = 1; edge < route.Length; edge++ ) nearest = MathF.Min( nearest, Geo.DistanceToSegmentSquared( samples[i], route[edge - 1], route[edge], out _ ) );
			deviation = MathF.Max( deviation, MathF.Sqrt( nearest ) );

			// Turns count within ten radii of a route corner, not during launch and arrival.
			if ( Vector3.Distance( samples[i], route[0] ) < Settings.Radius * 2 || Vector3.Distance( samples[i], route[^1] ) < Settings.Radius * 2 ) continue;
			bool atCorner = route.Length == 2;
			for ( int corner = 1; corner < route.Length - 1; corner++ ) atCorner |= Vector3.Distance( samples[i], route[corner] ) <= Settings.Radius * 10;
			if ( !atCorner ) continue;
			float speed = moved / Delta;
			minimumSpeed = MathF.Min( minimumSpeed, speed );
			if ( i < 2 || speed < Settings.MaxSpeed * 0.1f ) continue;
			var movement = samples[i] - samples[i - 1];
			var previous = samples[i - 1] - samples[i - 2];
			if ( Flat( samples[i - 1], samples[i - 2] ) / Delta < Settings.MaxSpeed * 0.1f ) continue;
			float angle = MathF.Abs( MathF.Atan2( previous.X * movement.Y - previous.Y * movement.X, previous.X * movement.X + previous.Y * movement.Y ) ) * (180 / MathF.PI);
			peak = MathF.Max( peak, angle );
			squaredTurns += angle * angle;
			turns++;
		}
		Assert.IsTrue( turns > 0, "No moving samples were measured" );
		return new( peak, MathF.Sqrt( squaredTurns / turns ), minimumSpeed, length, deviation, (samples.Count - 1) * Delta );
	}
}
