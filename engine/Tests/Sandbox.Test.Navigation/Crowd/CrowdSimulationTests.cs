using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Nav.Lib.Tests.Sim;

namespace Nav.Lib.Tests;

/// <summary>
/// Agents in a crowd on hand-made meshes: wall steering, doorways, avoidance, arrival, links, tiles
/// that go and come back.
/// </summary>
[TestClass]
public class CrowdSimulationTests
{
	static readonly AgentParams Runner = AgentParams.Create( 16, 64, 180, 1200 );

	[TestMethod]
	public void WallSteeringDoesNotAvoidWallsBeyondTheDestination_11822()
	{
		var sim = new Sim( new() { Doorway = true } );
		int agent = sim.Add( At( 250, 100 ), Runner );
		sim.Step();
		Assert.IsFalse( sim[agent].Poly.IsNone );
		var desired = new Vector2( 180, 0 );
		var goal = At( 280, 100 );
		var steered = sim.Crowd.SteerWalls( agent, sim.Mesh.State, sim.Query, desired, goal, Vector3.Distance( sim.Position( agent ), goal ) );
		Assert.AreEqual( desired, steered, "A wall beyond the destination must not divert a clear approach" );
	}

	[TestMethod]
	public void WallSteeringDoesNotPredictPastTheNextRouteCorner_11822()
	{
		var sim = new Sim( new() { Doorway = true } );
		int agent = sim.Add( At( 280, 270 ), Runner );
		sim.Step();
		Assert.IsFalse( sim[agent].Poly.IsNone );
		var direction = At( 300, 280 ) - sim.Position( agent );
		var desired = Vector2.Normalize( new Vector2( direction.X, direction.Y ) ) * 180;
		var steered = sim.Crowd.SteerWalls( agent, sim.Mesh.State, sim.Query, desired, At( 540, 100 ), direction.Length() );
		Assert.IsTrue( Vector2.Distance( steered, desired ) < 0.01f, $"A clear approach to a route corner was diverted: {steered} versus {desired}" );
	}

	[TestMethod]
	public void WallSteeringTurnsAVelocityThatWouldHitAWall()
	{
		var sim = new Sim( new() { Doorway = true } );
		int agent = sim.Add( At( 280, 100 ), Runner );
		sim.Step();
		var desired = new Vector2( 180, 0 );
		var steered = sim.Crowd.SteerWalls( agent, sim.Mesh.State, sim.Query, desired, At( 540, 100 ), 400 );
		Assert.AreNotEqual( desired, steered, "Walking straight at a wall 20 units ahead must be steered" );
		Assert.IsTrue( steered.X < desired.X );
	}

	[TestMethod]
	[DataRow( 100, false )]
	[DataRow( 540, false )]
	[DataRow( 100, true )]
	[DataRow( 540, true )]
	public void RoomEntryFollowsTheRouteWithoutWideDetours_11822( int y, bool reverse )
	{
		var sim = new Sim( new() { Doorway = true } );
		var start = At( reverse ? 540 : 100, y );
		var target = At( reverse ? 100 : 540, y );
		int agent = sim.Add( start, target, Runner );
		sim.Step();

		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, sim.Query.FindPath( start, target, new Vector3( 8 ), default, path ) );
		var corners = path.Corners.ToArray();
		float shortest = path.Length, traveled = Vector3.Distance( sim.Position( agent ), start ), deviation = 0;
		for ( int tick = 0; tick < 1000 && sim.HasTarget( agent ); tick++ )
		{
			var before = sim.Position( agent );
			sim.Step();
			traveled += Vector3.Distance( before, sim.Position( agent ) );
			float nearest = float.MaxValue;
			for ( int i = 1; i < corners.Length; i++ ) nearest = MathF.Min( nearest, Geo.DistanceToSegmentSquared( sim.Position( agent ), corners[i - 1], corners[i], out _ ) );
			deviation = MathF.Max( deviation, MathF.Sqrt( nearest ) );
		}
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );
		Assert.IsTrue( deviation <= Runner.Radius, $"An unobstructed agent took a wide detour: {deviation:F2}" );
		Assert.IsTrue( traveled < shortest * 1.1f, $"Route length {traveled:F2} versus {shortest:F2}" );
	}

	[TestMethod]
	public void AvoidingANeighbourDoesNotDriveTheAgentIntoAWall_11811()
	{
		var sim = new Sim( new() { Doorway = true } );
		int agent = sim.Add( At( 294, 100 ), At( 294, 250 ) );
		int neighbour = sim.Add( At( 280, 100 ), At( 280, 230 ) );
		float clearance = 6;
		for ( int tick = 0; tick < 500; tick++ )
		{
			sim.Step();
			clearance = MathF.Min( clearance, 300 - sim.Position( agent ).X );
		}
		Assert.IsTrue( clearance >= 1, $"Steering drove the agent against the wall: clearance {clearance}" );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), At( 294, 250 ) ) < 2, sim.Position( agent ).ToString() );
		Assert.IsTrue( Vector3.Distance( sim.Position( neighbour ), At( 280, 230 ) ) < 2, sim.Position( neighbour ).ToString() );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ), "Nearby agents must not prevent arrival from being reported" );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( neighbour ) );
	}

	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void WallAdjacentLinkEntrancesRemainReachable_10146( bool automatic )
	{
		var mesh = CrowdMesh.Create( new() { UpperFloor = true } );
		CrowdMesh.AddLink( mesh, x: 639.9f );
		var sim = new Sim( mesh );
		var target = At( 100, 320, 201 );
		int agent = sim.Add( At( 100, 320 ), target, Walker( automatic ) );
		bool entered = false;
		for ( int tick = 0; tick < 1600; tick++ )
		{
			sim.Step();
			if ( !sim.OnLink( agent ) ) continue;
			entered = true;
			if ( automatic ) continue;
			var position = sim.Position( agent );
			sim.Step( 0.1f );
			Assert.AreEqual( position, sim.Position( agent ) );
			sim.Crowd.SetPosition( agent, sim[agent].LinkEnd );
			sim.Crowd.CompleteLink( agent );
		}
		Assert.IsTrue( entered, "Wall steering must not prevent link entry" );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), target ) < 1, sim.Position( agent ).ToString() );
	}

	[TestMethod]
	[DataRow( 1, false )]
	[DataRow( 8, false )]
	[DataRow( 32, false )]
	[DataRow( 64, false )]
	[DataRow( 8, true )]
	[DataRow( 32, true )]
	[DataRow( 64, true )]
	public void AgentsReachTargetsThroughDoorway_11811( int count, bool opposing )
	{
		var sim = new Sim( new() { Doorway = true } );
		var targets = new List<Vector3>();
		for ( int i = 0; i < count; i++ )
		{
			bool reverse = opposing && i % 2 == 1;
			int slot = opposing ? i / 2 : i;
			float x = 60 + slot % 8 * 25, y = 100 + slot / 8 * 25;
			targets.Add( At( reverse ? x : 640 - x, y ) );
			sim.Add( At( reverse ? 640 - x : x, y ), targets[i] );
		}

		// Once every agent has arrived, give them two seconds to settle instead of running out the clock.
		int arrivedTick = -1;
		for ( int tick = 0; tick < 2250; tick++ )
		{
			sim.Step();
			bool arrived = true;
			for ( int i = 0; i < count; i++ )
			{
				if ( !sim.HasTarget( i ) ) continue;
				arrived = false;
				Assert.IsTrue( sim.Navigating( i ), "Corridor repair must not publish a lost route" );
			}
			if ( arrived && arrivedTick < 0 ) arrivedTick = tick;
			if ( arrivedTick >= 0 && tick - arrivedTick >= 100 ) break;
		}
		for ( int i = 0; i < count; i++ )
			Assert.IsTrue( Vector3.Distance( sim.Position( i ), targets[i] ) < 8, $"Agent {i} stalled at {sim.Position( i )}, {sim.Status( i )}" );
	}

	[TestMethod]
	public void RetargetingKeepsTheActiveRoute_11811()
	{
		var sim = new Sim();
		int agent = sim.Add( At( 100, 100 ), At( 500, 100 ) );
		Assert.AreEqual( AgentStatus.Pending, sim.Status( agent ), "An initial request has no route yet" );
		for ( int tick = 0; tick < 100; tick++ )
		{
			sim.Step();
			sim.Crowd.MoveTo( agent, At( 500, 100 + tick * 2 ) );
			Assert.AreEqual( AgentStatus.Moving, sim.Status( agent ), "Requesting an updated route must not drop the active route" );
		}
		sim.Crowd.MoveTo( agent, At( 10000, 10000 ) );
		sim.Step();
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( agent ), "An unplaceable destination must not report a valid route" );
	}

	[TestMethod]
	public void ArrivedAgentsYieldAndSettleBackAtTheirDestination()
	{
		var sim = new Sim();
		var goal = At( 200, 320 );
		int resting = sim.Add( At( 100, 320 ), goal );
		sim.Run( 300 );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( resting ) );
		int passing = sim.Add( sim.Position( resting ), At( 500, 320 ) );
		float displaced = 0;
		for ( int tick = 0; tick < 500; tick++ )
		{
			sim.Step();
			displaced = MathF.Max( displaced, Vector3.Distance( sim.Position( resting ), goal ) );
			Assert.AreEqual( AgentStatus.Arrived, sim.Status( resting ) );
		}
		Assert.IsTrue( displaced > 2, "The arrived agent must yield to an overlapping neighbour" );
		Assert.IsTrue( Vector3.Distance( sim.Position( resting ), goal ) <= 2 );
		Assert.IsTrue( Vector3.Distance( sim.Position( passing ), At( 500, 320 ) ) < 1 );
	}

	[TestMethod]
	public void OverlappingSpawnsReachTheirTargets_11811()
	{
		var sim = new Sim( new() { Doorway = true } );
		for ( int i = 0; i < 16; i++ ) sim.Add( At( 100, 320 ), At( 450 + i % 4 * 35, 250 + i / 4 * 35 ) );
		sim.Run( 2250 );
		for ( int i = 0; i < 16; i++ )
			Assert.IsTrue( Vector3.Distance( sim.Position( i ), At( 450 + i % 4 * 35, 250 + i / 4 * 35 ) ) < 8, $"Agent {i} stalled at {sim.Position( i )}, {sim.Status( i )}" );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 0.1f )]
	public void AgentReachesWallAdjacentTargets( float clearance )
	{
		var sim = new Sim( new() { Obstacles = true } );
		var target = At( 180 - clearance, 500 );
		int agent = sim.Add( At( 100, 100 ), target );
		sim.Run( 2000 );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), target ) < 1, sim.Position( agent ).ToString() );
	}

	[TestMethod]
	public void SpawnedAgentNavigatesAroundObstacles_11811()
	{
		var sim = new Sim( new() { Obstacles = true } );
		var target = At( 500, 500 );
		int agent = sim.Add( At( 100, 100 ), target );
		sim.Run( 2000 );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), target ) < 2, $"Agent stalled at {sim.Position( agent )}" );
	}

	[TestMethod]
	public void PartialRouteResumesWhenAConnectionAppears()
	{
		var sim = new Sim( new() { UpperFloor = true } );
		var target = At( 500, 320, 201 );
		int agent = sim.Add( At( 100, 320 ), target );
		sim.Run( 500 );
		Assert.AreEqual( AgentStatus.Blocked, sim.Status( agent ) );
		Assert.AreEqual( target, sim[agent].Target );
		CrowdMesh.AddLink( sim.Mesh );
		sim.Run( 700 );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), target ) < 1 );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );
	}

	[TestMethod]
	public void MovingAnAgentAlongItsRouteRepairsInsteadOfReplanning()
	{
		var sim = new Sim( new() { Obstacles = true } );
		var target = At( 540, 320 );
		int agent = sim.Add( At( 100, 320 ), target );
		sim.Step();
		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, sim.Crowd.GetPath( agent, path ) );
		var end = path.Polys[^1];
		foreach ( var position in new[] { At( 160, 320 ), At( 200, 360 ) } )
		{
			sim.Crowd.SetPosition( agent, position );
			Assert.AreEqual( AgentStatus.Moving, sim.Status( agent ), "Moving an agent on its route dropped the route" );
			sim.Step();
			Assert.AreEqual( AgentStatus.Moving, sim.Status( agent ) );
			Assert.AreEqual( PathStatus.Complete, sim.Crowd.GetPath( agent, path ) );
			Assert.AreEqual( sim.PolyAt( position ), path.Polys[0] );
			Assert.AreEqual( end, path.Polys[^1] );
		}
		sim.Run( 500 );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), target ) < 1 );
	}

	[TestMethod]
	public void AgentWaitsWhenItsTileDisappearsAndResumesWhenRestored()
	{
		var sim = new Sim();
		var target = At( 500, 100 );
		int agent = sim.Add( At( 100, 100 ), target );
		sim.Step();
		sim.Mesh.RemoveTile( 0, 0 );
		sim.Mesh.Commit();
		var position = sim.Position( agent );
		sim.Step();
		Assert.IsTrue( sim[agent].Poly.IsNone );
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( agent ) );
		Assert.AreEqual( position, sim.Position( agent ) );
		Assert.AreEqual( target, sim[agent].Target );
		sim.Mesh.CommitTiles( [CrowdMesh.Tile( default )] );
		sim.Run( 400 );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), target ) < 1 );
	}

	[TestMethod]
	public void StoppingOnALinkReprojectsOntoTheCurrentFloor()
	{
		var sim = new Sim( new() { UpperFloor = true, Link = true } );
		int agent = sim.Add( At( 320, 320 ), At( 500, 320, 201 ), Walker( autoLinks: false ) );
		sim.Step();
		Assert.IsTrue( sim.OnLink( agent ) );
		sim.Crowd.Stop( agent );
		sim.Step();
		Assert.AreEqual( AgentStatus.Idle, sim.Status( agent ) );
		Assert.IsFalse( sim[agent].Poly.IsNone );
		Assert.AreEqual( 1f, sim.Position( agent ).Z );
		sim.Crowd.MoveTo( agent, At( 100, 320 ) );
		sim.Run( 300 );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), At( 100, 320 ) ) < 1 );
	}

	[TestMethod]
	public void AirborneAgentRecoversAndKeepsTarget_10230()
	{
		var sim = new Sim();
		int agent = sim.Add( At( 100, 100, 500 ), At( 500, 100 ) );
		sim.Step();
		Assert.AreEqual( 500f, sim.Position( agent ).Z, "An unsuccessful placement must not move the agent" );
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( agent ) );
		sim.Crowd.SetPosition( agent, At( 100, 100 ) );
		sim.Run( 300 );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), At( 500, 100 ) ) < 1 );
	}

	[TestMethod]
	public void VerticalLinkTraversesInBothDirections_10146()
	{
		var sim = new Sim( new() { UpperFloor = true, Link = true } );
		int agent = sim.Add( At( 100, 320 ) );
		foreach ( float height in new[] { 201f, 1f } )
		{
			sim.Crowd.MoveTo( agent, At( 500, 320, height ) );
			bool entered = false;
			for ( int i = 0; i < 500; i++ )
			{
				sim.Step();
				entered |= sim.OnLink( agent );
			}
			Assert.IsTrue( entered );
			Assert.IsTrue( Vector3.Distance( sim.Position( agent ), At( 500, 320, height ) ) < 1, sim.Position( agent ).ToString() );
		}
	}

	[TestMethod]
	public void ManualLinkWaitsForCompletion_10146()
	{
		var sim = new Sim( new() { UpperFloor = true, Link = true } );
		int agent = sim.Add( At( 100, 320 ), At( 500, 320, 201 ), Walker( autoLinks: false ) );
		sim.Run( 300 );
		Assert.IsTrue( sim.OnLink( agent ) );
		Assert.AreEqual( 1f, sim.Position( agent ).Z, 0.01f );
		sim.Crowd.SetPosition( agent, sim[agent].LinkEnd );
		sim.Crowd.CompleteLink( agent );
		sim.Run( 300 );
		Assert.IsFalse( sim.OnLink( agent ) );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), At( 500, 320, 201 ) ) < 1 );
	}

	[TestMethod]
	[DataRow( 120, true )]
	[DataRow( -120, true )]
	[DataRow( 120, false )]
	[DataRow( -120, false )]
	public void ManualLinkResumesFromActualLanding( int offset, bool ascending )
	{
		var sim = new Sim( new() { UpperFloor = true, Link = true, Obstacles = true } );
		float startHeight = ascending ? 1 : 201, endHeight = ascending ? 201 : 1;
		var target = At( 500, 320, endHeight );
		int agent = sim.Add( At( 320, 320, startHeight ), target, Walker( autoLinks: false ) );
		for ( int i = 0; i < 500 && !sim.OnLink( agent ); i++ ) sim.Step();
		Assert.IsTrue( sim.OnLink( agent ) );

		var landing = sim[agent].LinkEnd + new Vector3( offset, 0, 0 );
		Assert.AreNotEqual( sim.PolyAt( sim[agent].LinkEnd ), sim.PolyAt( landing ), "Exercise a landing outside the original exit polygon" );
		sim.Crowd.SetPosition( agent, landing );
		sim.Crowd.CompleteLink( agent );
		Assert.AreEqual( landing, sim.Position( agent ), "Custom traversal must not snap back to the link endpoint" );
		Assert.IsFalse( sim.OnLink( agent ) );
		Assert.AreEqual( target, sim[agent].Target );
		Assert.IsTrue( sim.HasTarget( agent ) );
		sim.Crowd.CompleteLink( agent );
		Assert.AreEqual( landing, sim.Position( agent ), "Completing twice must be harmless" );
		sim.Step();
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), landing ) < 1, "The first walking update must start from the landing polygon" );
		Assert.AreEqual( AgentStatus.Moving, sim.Status( agent ) );
		sim.Run( 2000 );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), target ) < 1, sim.Position( agent ).ToString() );
	}

	[TestMethod]
	public void ManualLinkCompletedOffMeshWaitsForPlacement()
	{
		var sim = new Sim( new() { UpperFloor = true, Link = true } );
		var target = At( 500, 320, 201 );
		int agent = sim.Add( At( 320, 320 ), target, Walker( autoLinks: false ) );
		sim.Step();
		Assert.IsTrue( sim.OnLink( agent ) );
		var outside = At( 800, 320, 201 );
		sim.Crowd.SetPosition( agent, outside );
		sim.Crowd.CompleteLink( agent );
		sim.Run( 10 );
		Assert.AreEqual( outside, sim.Position( agent ) );
		Assert.AreEqual( target, sim[agent].Target );
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( agent ) );
		sim.Crowd.SetPosition( agent, At( 600, 320, 201 ) );
		sim.Run( 300 );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), target ) < 1 );
	}

	[TestMethod]
	public void ShortLinkEntryIsPublishedBeforeCompletion()
	{
		var sim = new Sim( new() { UpperFloor = true, Link = true } );
		var fast = Walker();
		fast.MaxSpeed = 100000;
		int agent = sim.Add( At( 320, 320 ), At( 500, 320, 201 ), fast );
		sim.Step( 0.4f );
		Assert.IsTrue( sim.OnLink( agent ), "Substeps must not consume the entry before the caller can observe it" );
		sim.Step();
		Assert.IsFalse( sim.OnLink( agent ) );
	}

	[TestMethod]
	public void PassingABlockingNeighbourDoesNotOrbitIt()
	{
		var sim = new Sim();
		var target = At( 350, 320 );
		int agent = sim.Add( At( 240, 320 ), target );
		sim.Add( At( 330, 320 ) );
		float travelled = 0;
		int sidestep = 0;
		var previous = sim.Position( agent );
		for ( int tick = 0; tick < 2000 && sim.HasTarget( agent ); tick++ )
		{
			sim.Step();
			travelled += Vector3.Distance( sim.Position( agent ), previous );
			previous = sim.Position( agent );
			var velocity = sim[agent].Velocity;
			if ( Vector3.Distance( previous, target ) > 20 && velocity.Length() > 1 && Vector3.Normalize( velocity ).X < 0.5f ) sidestep++;
		}
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), target ) < 10, sim.Position( agent ).ToString() );
		Assert.AreEqual( 0, sidestep, $"Passing a blocker turned the agent away from its goal for {sidestep} ticks" );
		Assert.IsTrue( travelled < 170, $"Route around one blocker was {travelled:0} units instead of 110" );
	}

	[TestMethod]
	public void NeighbourPassedWideDoesNotDivertTheAgent()
	{
		var sim = new Sim();
		var target = At( 440, 320 );
		int agent = sim.Add( At( 200, 320 ), target );
		sim.Add( At( 320, 360 ) );
		float lateral = 0;
		for ( int tick = 0; tick < 2000; tick++ )
		{
			sim.Step();
			lateral = MathF.Max( lateral, MathF.Abs( sim.Position( agent ).Y - 320 ) );
		}
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), target ) < 1, sim.Position( agent ).ToString() );
		Assert.IsTrue( lateral < 1, $"A neighbour cleared by 40 units diverted the agent by {lateral:0.0}" );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 0.25f )]
	[DataRow( 1f )]
	public void SeparationDoesNotPreventReachingAGoalBesideANeighbour( float separation )
	{
		var sim = new Sim();
		var settings = Walker();
		settings.Separation = separation;
		sim.Add( At( 330, 320 ), settings );
		var target = At( 350, 320 );
		int agent = sim.Add( At( 240, 320 ), target, settings );
		int stalled = 0, worst = 0;
		for ( int tick = 0; tick < 1500; tick++ )
		{
			sim.Step();
			if ( sim.Navigating( agent ) && sim[agent].Velocity.Length() < 6 ) worst = Math.Max( worst, ++stalled );
			else stalled = 0;
		}
		Assert.IsTrue( worst < 75, $"Separation {separation} stalled the agent for {worst * 0.02f:0.0}s" );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), target ) < 12, $"Separation {separation} left the agent {Vector3.Distance( sim.Position( agent ), target ):0} units short" );
	}

	[TestMethod]
	public void CrossingAgentsPassWithoutStalling()
	{
		var sim = new Sim();
		Vector3 firstTarget = At( 440, 320 ), secondTarget = At( 320, 440 );
		int first = sim.Add( At( 200, 320 ), firstTarget );
		int second = sim.Add( At( 320, 200 ), secondTarget );
		float travelled = 0, closest = float.MaxValue;
		var previous = sim.Position( first );
		for ( int tick = 0; tick < 1000; tick++ )
		{
			sim.Step();
			travelled += Vector3.Distance( sim.Position( first ), previous );
			previous = sim.Position( first );
			closest = MathF.Min( closest, Vector3.Distance( sim.Position( first ), sim.Position( second ) ) );
		}
		Assert.IsTrue( closest >= 16, $"Crossing agents overlapped: {closest:0.0}" );
		Assert.IsTrue( Vector3.Distance( sim.Position( first ), firstTarget ) < 2, sim.Position( first ).ToString() );
		Assert.IsTrue( Vector3.Distance( sim.Position( second ), secondTarget ) < 2, sim.Position( second ).ToString() );
		Assert.IsTrue( travelled < 300, $"Crossing detour was {travelled:0} units on a 240 unit route" );
	}

	[TestMethod]
	public void AgentsOrderedToOnePointPackAroundItWithoutOverlapping()
	{
		var sim = new Sim();
		var target = At( 420, 420 );
		for ( int i = 0; i < 8; i++ ) sim.Add( At( 120 + i % 4 * 30, 200 + i / 4 * 30 ), target );
		float closest = float.MaxValue;
		for ( int tick = 0; tick < 1500; tick++ )
		{
			sim.Step();
			if ( tick < 60 ) continue;
			for ( int i = 0; i < 8; i++ )
				for ( int j = i + 1; j < 8; j++ )
					closest = MathF.Min( closest, Flat( sim.Position( i ), sim.Position( j ) ) );
		}
		Assert.IsTrue( closest > 12.8f, $"A pile-up compressed agents to {closest:0.0} apart, radii sum 16" );
		for ( int i = 0; i < 8; i++ )
			Assert.IsTrue( Vector3.Distance( sim.Position( i ), target ) < 32, $"An agent settled {Vector3.Distance( sim.Position( i ), target ):0} units from the shared destination" );
	}

	[TestMethod]
	public void HeadOnAgentsPassAndReachTheirTargets()
	{
		var sim = new Sim();
		Vector3 start = At( 100, 320 ), end = At( 500, 320 );
		int left = sim.Add( start, end );
		int right = sim.Add( end, start );
		float minimumDistance = float.MaxValue;
		for ( int i = 0; i < 500; i++ )
		{
			sim.Step();
			minimumDistance = MathF.Min( minimumDistance, Vector3.Distance( sim.Position( left ), sim.Position( right ) ) );
		}
		Assert.IsTrue( minimumDistance >= 16, $"Agents overlapped: distance {minimumDistance}" );
		Assert.IsTrue( Vector3.Distance( sim.Position( left ), end ) < 1 );
		Assert.IsTrue( Vector3.Distance( sim.Position( right ), start ) < 1 );
	}

	// Two agents meet head-on along the cut-off corner of a wall, already touching or about to: one
	// of them has the wall on its passing side. While touching agents were only pushed apart, the pair
	// starting 6 back, 33 apart at speed 85 stood there for good.
	[TestMethod]
	public void HeadOnAgentsTouchingAtAWallCornerPass()
	{
		var walker = AgentParams.Create( 16, 64, 120, 120 );
		var along = Vector3.Normalize( new Vector3( 1, 1, 0 ) );
		foreach ( float back in new[] { 0.4f, 2f, 6f, 12f } )
			foreach ( float apart in new[] { 31.9f, 32f, 33f, 40f, 60f } )
				foreach ( float speed in new[] { 0f, 85f } )
				{
					var sim = new Sim( CrowdMesh.ChamferedCorner() );
					var from = At( 280, 348 ) - along * back;
					int first = sim.Add( from, At( 540, 500 ), walker );
					int second = sim.Add( from + along * apart, At( 140, 100 ), walker );
					sim.Crowd.SetVelocity( first, along * speed );
					sim.Crowd.SetVelocity( second, -along * speed );
					sim.Run( 1500 );
					Assert.IsTrue( sim.Status( first ) == AgentStatus.Arrived && sim.Status( second ) == AgentStatus.Arrived,
						$"Back {back}, apart {apart}, speed {speed}: {sim.Status( first )} at {sim.Position( first )}, {sim.Status( second )} at {sim.Position( second )}" );
				}
	}
}
