using System;
using System.Collections.Generic;
using Sandbox.Navigation;
using Sandbox.Navigation.Pathfinding;

namespace NavigationTests;

[TestClass]
public class NavigationCorneringTests
{
	[TestMethod]
	[DataRow( "straight", false )]
	[DataRow( "doorway", false )]
	[DataRow( "doorway", true )]
	[DataRow( "corner", false )]
	[DataRow( "corner", true )]
	public void AgentTurnsSmoothlyWithoutExcessiveSlowdownOrDetours( string layout, bool reverse )
	{
		var (mesh, start, target) = Scenario( layout, reverse );
		var route = Route( mesh, start, target );
		float shortest = 0;
		for ( int i = 1; i < route.Length; i++ ) shortest += route[i].Distance( route[i - 1] );
		var simulation = new NavigationSimulation( mesh, new object(), Settings.Radius, Settings.Height );
		var agent = simulation.Add( start, Settings );
		agent.MoveTo( target );
		var samples = Capture( simulation, agent, target );
		var measured = Measure( samples, route );
		// Heading changes are degrees per 20 ms tick; speed is units per second.
		var (maxTurn, maxRmsTurn, minSpeed, maxDeviation) = layout switch
		{
			"straight" => (0.5f, 0.1f, 178f, 1f),
			"doorway" => (10f, 3f, 150f, 8f),
			"corner" => (9f, 2f, 170f, 6f),
			_ => throw new ArgumentOutOfRangeException( nameof( layout ) )
		};
		Assert.IsTrue( measured.PeakTurn <= maxTurn, $"Abrupt turn: {measured}" );
		Assert.IsTrue( measured.RmsTurn <= maxRmsTurn, $"Excessive heading variation: {measured}" );
		Assert.IsTrue( measured.MinimumSpeed >= minSpeed, $"Excessive slowdown: {measured}" );
		Assert.IsTrue( measured.Deviation <= maxDeviation, $"Wide turn: {measured}" );
		// Allow half a radius of extra travel and 0.35 seconds for acceleration and turns.
		Assert.IsTrue( measured.Length <= shortest + Settings.Radius / 2, $"Detour: {measured}" );
		Assert.IsTrue( measured.Seconds <= shortest / Settings.MaxSpeed + 0.35f, $"Slow arrival: {measured}" );
	}

	[TestMethod]
	public void CornerMetricsDistinguishSmoothTurnsFromSnapsAndSlowdowns()
	{
		Vector3[] route = [new( 0, 1, 0 ), new( 200, 1, 0 ), new( 200, 1, 200 )];
		var sharp = new List<Vector3>();
		for ( int i = 0; i <= 50; i++ ) sharp.Add( new Vector3( i * 4, 1, 0 ) );
		for ( int i = 1; i <= 50; i++ ) sharp.Add( new Vector3( 200, 1, i * 4 ) );
		var smooth = new List<Vector3>();
		for ( int i = 0; i <= 40; i++ ) smooth.Add( new Vector3( i * 4, 1, 0 ) );
		for ( int i = 1; i <= 16; i++ )
		{
			float angle = -MathF.PI / 2 + i * MathF.PI / 32;
			smooth.Add( new Vector3( 160 + MathF.Cos( angle ) * 40, 1, 40 + MathF.Sin( angle ) * 40 ) );
		}
		for ( int i = 1; i <= 40; i++ ) smooth.Add( new Vector3( 200, 1, 40 + i * 4 ) );
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

	private const float Delta = 0.02f;
	private static readonly SimulationSettings Settings = new( 16, 64, 180, 1200, 0.25f, true, TraversalFilter.Unrestricted );

	private static (NavMeshGraph Mesh, Vector3 Start, Vector3 Target) Scenario( string layout, bool reverse )
	{
		var mesh = SyntheticNavMesh.Create( new() { Doorway = layout == "doorway", Corner = layout == "corner" } );
		var start = layout == "corner" ? new Vector3( 140, 1, 100 ) : new Vector3( 100, 1, 100 );
		var target = layout == "corner" ? new Vector3( 540, 1, 500 ) : new Vector3( 540, 1, 100 );
		return reverse ? (mesh, target, start) : (mesh, start, target);
	}

	private static List<Vector3> Capture( NavigationSimulation simulation, SimulationAgent agent, Vector3 target )
	{
		var samples = new List<Vector3> { agent.Position };
		for ( int tick = 0; tick < 1000; tick++ )
		{
			simulation.Update( Delta );
			var point = agent.Position;
			Assert.IsTrue( float.IsFinite( point.x ) && float.IsFinite( point.y ) && float.IsFinite( point.z ) );
			samples.Add( point );
			if ( point.Distance( target ) <= Settings.Radius / 4 ) return samples;
		}
		Assert.Fail( "The agent did not arrive within 20 seconds" );
		return samples;
	}

	private static Vector3[] Route( NavMeshGraph mesh, Vector3 start, Vector3 target )
	{
		var query = new MeshQuery( mesh );
		query.FindNearestPoly( start, new Vector3( 8 ), Settings.Filter, out var first, out _, out _ );
		query.FindNearestPoly( target, new Vector3( 8 ), Settings.Filter, out var last, out _, out _ );
		var path = new List<long>();
		Assert.IsTrue( query.FindPath( first, last, start, target, Settings.Filter, path ).Succeeded() );
		var corners = new StraightPath[16];
		Assert.IsTrue( query.FindStraightPath( start, target, path, path.Count, corners, out int count, corners.Length, 0 ).Succeeded() );
		var result = new Vector3[count];
		for ( int i = 0; i < count; i++ ) result[i] = corners[i].pos;
		return result;
	}

	private readonly record struct Metrics( float PeakTurn, float RmsTurn, float MinimumSpeed, float Length, float Deviation, float Seconds );

	private static Metrics Measure( IReadOnlyList<Vector3> samples, Vector3[] route )
	{
		float peak = 0, squaredTurns = 0, minimumSpeed = float.MaxValue, length = 0, deviation = 0;
		int turns = 0;
		for ( int i = 1; i < samples.Count; i++ )
		{
			var movement = (samples[i] - samples[i - 1]).WithY( 0 );
			length += movement.Length;
			float nearest = float.MaxValue;
			for ( int edge = 1; edge < route.Length; edge++ )
				nearest = MathF.Min( nearest, Geometry.DistancePtSegSqr2D( samples[i], route[edge - 1], route[edge], out _ ) );
			deviation = MathF.Max( deviation, MathF.Sqrt( nearest ) );

			// Measure turns within ten radii of a route corner, excluding launch and arrival.
			if ( samples[i].Distance( route[0] ) < Settings.Radius * 2 || samples[i].Distance( route[^1] ) < Settings.Radius * 2 ) continue;
			bool atCorner = route.Length == 2;
			for ( int corner = 1; corner < route.Length - 1; corner++ )
				atCorner |= samples[i].Distance( route[corner] ) <= Settings.Radius * 10;
			if ( !atCorner ) continue;
			float speed = movement.Length / Delta;
			minimumSpeed = MathF.Min( minimumSpeed, speed );
			if ( i < 2 || speed < Settings.MaxSpeed * 0.1f ) continue;
			var previous = (samples[i - 1] - samples[i - 2]).WithY( 0 );
			if ( previous.Length / Delta < Settings.MaxSpeed * 0.1f ) continue;
			float angle = MathF.Abs( MathF.Atan2( previous.x * movement.z - previous.z * movement.x, Vector3.Dot( previous, movement ) ) ) * (180 / MathF.PI);
			peak = MathF.Max( peak, angle );
			squaredTurns += angle * angle;
			turns++;
		}
		Assert.IsTrue( turns > 0, "No moving samples were measured" );
		return new( peak, MathF.Sqrt( squaredTurns / turns ), minimumSpeed, length, deviation, (samples.Count - 1) * Delta );
	}
}
