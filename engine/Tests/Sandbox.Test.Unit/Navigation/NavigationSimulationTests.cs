using System;
using System.Collections.Generic;
using Sandbox.Navigation;
using Sandbox.Navigation.Pathfinding;

namespace NavigationTests;

[TestClass]
public class NavigationSimulationTests
{
	[TestMethod]
	public void WallSteeringDoesNotAvoidWallsBeyondTheDestination_11822()
	{
		var mesh = SyntheticNavMesh.Create( new() { Doorway = true } );
		var simulation = new NavigationSimulation( mesh, new object(), 16, 64 );
		var agent = simulation.Add( new Vector3( 250, 1, 100 ), new( 16, 64, 180, 1200, 0.25f, true, TraversalFilter.Unrestricted ) );
		agent.Query.FindNearestPoly( agent.Position, new Vector3( 8 ), TraversalFilter.Unrestricted, out var polygon, out _, out _ );
		Assert.AreNotEqual( 0L, polygon );
		agent.Path.Add( polygon );
		var desired = new Vector3( 180, 0, 0 );
		var goal = new Vector3( 280, 1, 100 );
		Assert.AreEqual( desired, WallSteering.Steer( agent, desired, goal, agent.Position.Distance( goal ) ), "A wall beyond the destination must not divert a clear approach" );
	}

	[TestMethod]
	public void WallSteeringDoesNotPredictPastTheNextRouteCorner_11822()
	{
		var mesh = SyntheticNavMesh.Create( new() { Doorway = true } );
		var simulation = new NavigationSimulation( mesh, new object(), 16, 64 );
		var agent = simulation.Add( new Vector3( 280, 1, 270 ), new( 16, 64, 180, 1200, 0.25f, true, TraversalFilter.Unrestricted ) );
		agent.Query.FindNearestPoly( agent.Position, new Vector3( 8 ), TraversalFilter.Unrestricted, out var polygon, out _, out _ );
		Assert.AreNotEqual( 0L, polygon );
		agent.Path.Add( polygon );
		var corner = new Vector3( 300, 1, 280 );
		var direction = corner - agent.Position;
		var desired = direction.Normal * 180;
		var result = WallSteering.Steer( agent, desired, new Vector3( 540, 1, 100 ), direction.Length );
		Assert.IsTrue( result.Distance( desired ) < 0.01f, $"A clear approach to a route corner was diverted: {result} versus {desired}" );
	}

	[TestMethod]
	[DataRow( 100, false )]
	[DataRow( 540, false )]
	[DataRow( 100, true )]
	[DataRow( 540, true )]
	public void RoomEntryFollowsTheRouteWithoutWideDetours_11822( int z, bool reverse )
	{
		var mesh = SyntheticNavMesh.Create( new() { Doorway = true } );
		var simulation = new NavigationSimulation( mesh, new object(), 16, 64 );
		var start = new Vector3( reverse ? 540 : 100, 1, z );
		var target = new Vector3( reverse ? 100 : 540, 1, z );
		var agent = simulation.Add( start, new( 16, 64, 180, 1200, 0.25f, true, TraversalFilter.Unrestricted ) );
		agent.MoveTo( target );
		simulation.Update( 0.02f );
		var corners = new StraightPath[16];
		Assert.IsTrue( agent.Query.FindStraightPath( start, target, agent.Path, agent.Path.Count, corners, out int count, corners.Length, 0 ).Succeeded() );
		float shortest = 0;
		for ( int i = 1; i < count; i++ ) shortest += corners[i].pos.Distance( corners[i - 1].pos );
		float traveled = agent.Position.Distance( start ), deviation = 0;
		int ticks = 1;
		for ( int tick = 0; tick < 1000 && agent.State.Target.HasValue; tick++ )
		{
			var before = agent.Position;
			simulation.Update( 0.02f );
			ticks++;
			traveled += before.Distance( agent.Position );
			float nearest = float.MaxValue;
			for ( int i = 1; i < count; i++ ) nearest = MathF.Min( nearest, Geometry.DistancePtSegSqr2D( agent.Position, corners[i - 1].pos, corners[i].pos, out _ ) );
			deviation = MathF.Max( deviation, MathF.Sqrt( nearest ) );
		}
		Console.WriteLine( $"Room entry: length {traveled:F2}, shortest {shortest:F2}, deviation {deviation:F2}, seconds {ticks * 0.02f:F2}, arrived {!agent.State.Target.HasValue}" );
		Assert.IsNull( agent.State.Target );
		Assert.IsTrue( deviation <= agent.Options.Radius, $"An unobstructed agent took a wide detour: {deviation:F2}" );
		Assert.IsTrue( traveled < shortest * 1.1f, $"Route length {traveled:F2} versus {shortest:F2}" );
	}

	[TestMethod]
	public void ExternallyDrivenAgentKeepsItsReportedVelocity()
	{
		var mesh = SyntheticNavMesh.Create();
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var position = new Vector3( 100, 1, 100 );
		var agent = simulation.Add( position, Settings( mesh ) with { MaxSpeed = 0 } );
		agent.Velocity = new Vector3( 20, 0, 0 );
		simulation.Update( 0.02f );
		Assert.AreEqual( position, agent.State.Position );
		Assert.AreEqual( new Vector3( 20, 0, 0 ), agent.State.Velocity );
		agent.Stop();
		Assert.AreEqual( Vector3.Zero, agent.State.Velocity );
	}

	[TestMethod]
	public void AvoidingANeighbourDoesNotDriveTheAgentIntoAWall_11811()
	{
		var mesh = SyntheticNavMesh.Create( new() { Doorway = true } );
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var agent = simulation.Add( new Vector3( 294, 1, 100 ), Settings( mesh ) );
		var neighbour = simulation.Add( new Vector3( 280, 1, 100 ), Settings( mesh ) );
		agent.MoveTo( new Vector3( 294, 1, 250 ) );
		neighbour.MoveTo( new Vector3( 280, 1, 230 ) );
		float clearance = 6;
		for ( int tick = 0; tick < 500; tick++ )
		{
			simulation.Update( 0.02f );
			clearance = MathF.Min( clearance, 300 - agent.Position.x );
		}
		Assert.IsTrue( clearance >= 1, $"Steering drove the agent against the wall: clearance {clearance}" );
		Assert.IsTrue( agent.Position.Distance( new Vector3( 294, 1, 250 ) ) < 2, agent.State.ToString() );
		Assert.IsTrue( neighbour.Position.Distance( new Vector3( 280, 1, 230 ) ) < 2, neighbour.State.ToString() );
		Assert.IsNull( agent.State.Target, "Nearby agents must not prevent arrival from being reported" );
		Assert.IsNull( neighbour.State.Target );
	}

	[TestMethod]
	public void WallQueryKeepsPartialTilePortalsOpen()
	{
		var mesh = SyntheticNavMesh.Create( new() { TileBorders = true } );
		var neighbour = SyntheticNavMesh.Create( new() { MinZ = 32, TileBorders = true } ).GetTile( 0 ).data;
		neighbour.header.x = 1;
		neighbour.header.bmin.x += 640;
		neighbour.header.bmax.x += 640;
		for ( int i = 0; i < neighbour.verts.Length; i++ ) neighbour.verts[i].x += 640;
		Assert.IsTrue( mesh.AddTile( neighbour, 0, 0, out _ ).Succeeded() );
		var query = new MeshQuery( mesh );
		Span<MeshQuery.BoundarySegment> walls = stackalloc MeshQuery.BoundarySegment[16];
		foreach ( float z in new[] { 280f, 400f } )
		{
			var position = new Vector3( 630, 1, z );
			query.FindNearestPoly( position, new Vector3( 8 ), TraversalFilter.Unrestricted, out var poly, out _, out _ );
			int count = query.FindLocalWalls( poly, position, 64, 32, TraversalFilter.Unrestricted, walls );
			bool borderWall = false;
			foreach ( var wall in walls[..count] )
			{
				if ( MathF.Abs( wall.Start.x - 640 ) > 0.01f || MathF.Abs( wall.End.x - 640 ) > 0.01f ) continue;
				borderWall = true;
				Assert.IsTrue( MathF.Max( wall.Start.z, wall.End.z ) <= 321, "The open part of a tile portal must not be treated as a wall" );
			}
			Assert.AreEqual( z < 320, borderWall );
		}
		for ( int i = 0; i < neighbour.polys.Length; i++ ) neighbour.polys[i].area = 2;
		var filter = new TraversalFilter( uint.MaxValue & ~(1u << 2), null );
		var blockedPosition = new Vector3( 630, 1, 400 );
		query.FindNearestPoly( blockedPosition, new Vector3( 8 ), filter, out var start, out _, out _ );
		Assert.AreNotEqual( 0L, start, "The starting tile must remain traversable" );
		int blockedCount = query.FindLocalWalls( start, blockedPosition, 64, 32, filter, walls );
		Assert.IsTrue( blockedCount > 0, "A portal into an excluded area must become a steering boundary" );
		Assert.AreEqual( 640f, walls[0].Start.x, 0.01f );
		Assert.AreEqual( 640f, walls[0].End.x, 0.01f );
	}

	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void WallAdjacentLinkEntrancesRemainReachable_10146( bool automatic )
	{
		var mesh = SyntheticNavMesh.Create( new() { UpperFloor = true, Link = true } );
		var data = mesh.GetTile( 0 ).data;
		data.offMeshCons[0].startPos.x = 639.9f;
		data.offMeshCons[0].endPos.x = 639.9f;
		Assert.IsTrue( mesh.UpdateTile( data, 0 ).Succeeded() );
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var agent = simulation.Add( new Vector3( 100, 1, 320 ), Settings( mesh, automatic ) );
		var target = new Vector3( 100, 201, 320 );
		agent.MoveTo( target );
		bool entered = false;
		for ( int tick = 0; tick < 1600; tick++ )
		{
			simulation.Update( 0.02f );
			if ( !agent.State.Link.HasValue ) continue;
			entered = true;
			if ( automatic ) continue;
			var position = agent.Position;
			simulation.Update( 0.1f );
			Assert.AreEqual( position, agent.Position );
			agent.SetPosition( agent.State.Link.Value.End );
			agent.CompleteLink();
		}
		Assert.IsTrue( entered, "Wall steering must not prevent link entry" );
		Assert.IsTrue( agent.Position.Distance( target ) < 1, agent.State.ToString() );
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
		var mesh = SyntheticNavMesh.Create( new() { Doorway = true } );
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var agents = new List<SimulationAgent>();
		var targets = new List<Vector3>();
		for ( int i = 0; i < count; i++ )
		{
			bool reverse = opposing && i % 2 == 1;
			int slot = opposing ? i / 2 : i;
			float x = 60 + slot % 8 * 25, z = 100 + slot / 8 * 25;
			var target = new Vector3( reverse ? x : 640 - x, 1, z );
			var agent = simulation.Add( new Vector3( reverse ? 640 - x : x, 1, z ), Settings( mesh ) );
			agent.MoveTo( target );
			agents.Add( agent );
			targets.Add( target );
		}
		for ( int tick = 0; tick < 2250; tick++ )
		{
			simulation.Update( 0.02f );
			foreach ( var agent in agents )
				if ( agent.State.Target.HasValue ) Assert.IsTrue( agent.State.Navigating, "Corridor repair must not publish a lost route" );
		}
		for ( int i = 0; i < agents.Count; i++ )
			Assert.IsTrue( agents[i].Position.Distance( targets[i] ) < 8, $"Agent {i} stalled: {agents[i].State}" );
	}

	[TestMethod]
	public void RetargetingKeepsTheActiveRoute_11811()
	{
		var mesh = SyntheticNavMesh.Create();
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var agent = simulation.Add( new Vector3( 100, 1, 100 ), Settings( mesh ) );
		agent.MoveTo( new Vector3( 500, 1, 100 ) );
		Assert.IsFalse( agent.State.Navigating, "An initial request has no route yet" );
		for ( int tick = 0; tick < 100; tick++ )
		{
			simulation.Update( 0.02f );
			agent.MoveTo( new Vector3( 500, 1, 100 + tick * 2 ) );
			Assert.IsTrue( agent.State.Navigating, "Requesting an updated route must not drop the active route" );
		}
		agent.MoveTo( new Vector3( 10000, 1, 10000 ) );
		simulation.Update( 0.02f );
		Assert.IsFalse( agent.State.Navigating, "An unplaceable destination must not report a valid route" );
	}

	[TestMethod]
	public void ArrivedAgentsYieldAndSettleBackAtTheirDestination()
	{
		var mesh = SyntheticNavMesh.Create();
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var goal = new Vector3( 200, 1, 320 );
		var resting = simulation.Add( new Vector3( 100, 1, 320 ), Settings( mesh ) );
		resting.MoveTo( goal );
		for ( int tick = 0; tick < 300; tick++ ) simulation.Update( 0.02f );
		Assert.IsNull( resting.State.Target );
		var passing = simulation.Add( resting.Position, Settings( mesh ) );
		passing.MoveTo( new Vector3( 500, 1, 320 ) );
		float displaced = 0;
		for ( int tick = 0; tick < 500; tick++ )
		{
			simulation.Update( 0.02f );
			displaced = MathF.Max( displaced, resting.Position.Distance( goal ) );
			Assert.IsFalse( resting.State.Navigating );
			Assert.IsNull( resting.State.Target );
		}
		Assert.IsTrue( displaced > 2, "The arrived agent must yield to an overlapping neighbour" );
		Assert.IsTrue( resting.Position.Distance( goal ) <= 2 );
		Assert.IsTrue( passing.Position.Distance( new Vector3( 500, 1, 320 ) ) < 1 );
	}

	[TestMethod]
	public void OverlappingSpawnsReachTheirTargets_11811()
	{
		var mesh = SyntheticNavMesh.Create( new() { Doorway = true } );
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var agents = new List<SimulationAgent>();
		for ( int i = 0; i < 16; i++ )
		{
			var agent = simulation.Add( new Vector3( 100, 1, 320 ), Settings( mesh ) );
			agent.MoveTo( new Vector3( 450 + i % 4 * 35, 1, 250 + i / 4 * 35 ) );
			agents.Add( agent );
		}
		for ( int tick = 0; tick < 2250; tick++ ) simulation.Update( 0.02f );
		for ( int i = 0; i < agents.Count; i++ )
			Assert.IsTrue( agents[i].Position.Distance( new Vector3( 450 + i % 4 * 35, 1, 250 + i / 4 * 35 ) ) < 8, $"Agent {i} stalled: {agents[i].State}" );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 0.1f )]
	public void AgentReachesWallAdjacentTargets( float clearance )
	{
		var mesh = SyntheticNavMesh.Create( new() { Obstacles = true } );
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var agent = simulation.Add( new Vector3( 100, 1, 100 ), Settings( mesh ) );
		var target = new Vector3( 180 - clearance, 1, 500 );
		agent.MoveTo( target );
		for ( int tick = 0; tick < 2000; tick++ ) simulation.Update( 0.02f );
		Assert.IsTrue( agent.Position.Distance( target ) < 1, agent.State.ToString() );
	}

	[TestMethod]
	public void SpawnedAgentNavigatesAroundObstacles_11811()
	{
		var mesh = SyntheticNavMesh.Create( new() { Obstacles = true } );
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var agent = simulation.Add( new Vector3( 100, 1, 100 ), Settings( mesh ) );
		var target = new Vector3( 500, 1, 500 );
		agent.MoveTo( target );
		for ( int tick = 0; tick < 2000; tick++ ) simulation.Update( 0.02f );
		Assert.IsTrue( agent.Position.Distance( target ) < 2, $"Agent stalled: {agent.State}" );
	}

	[TestMethod]
	public void SpanConnectivityAndSearchQueueMatchReference() => NavigationAlgorithmChecks.Run();

	[TestMethod]
	public void BoundsSearchAndTileReuse() => NavigationAlgorithmChecks.BoundsAndSearch();

	[TestMethod]
	public void LayerRegionsSupportMoreRowSpansThanColumns()
	{
		using var field = new Sandbox.Navigation.Generation.Heightfield( 8, 8, Vector3.Zero, new Vector3( 8, 512, 8 ), 1, 1 );
		for ( int z = 0; z < 8; z++ )
			for ( int x = 0; x < 8; x += 2 )
				for ( int y = 0; y < 8; y++ ) field.AddOrMergeSpan( x, z, (ushort)(y * 32), (ushort)(y * 32 + 1), 1, 0 );
		using var compact = field.BuildCompactHeightfield( 8, 4 );
		Assert.IsTrue( Sandbox.Navigation.Generation.RegionBuilder.BuildLayerRegions( compact, 0, 0, new(), new() ) );
		Assert.IsTrue( compact.MaxRegions >= 32 );
		using var copy = compact.Copy();
		Assert.AreEqual( compact.BMax, copy.BMax, "Copying a cached field must not expand its bounds" );
	}
	[TestMethod]
	[DataRow( 1 )]
	[DataRow( 32 )]
	public void WarmSerialSimulationDoesNotAllocate( int count )
	{
		var mesh = SyntheticNavMesh.Create();
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		SimulationAgent agent = null;
		for ( int i = 0; i < count; i++ )
		{
			agent = simulation.Add( new Vector3( 100 + i % 8 * 24, 1, 100 + i / 8 * 24 ), Settings( mesh ) );
			agent.MoveTo( new Vector3( 500, 1, 100 + i / 8 * 24 ) );
		}
		for ( int i = 0; i < 32; i++ ) simulation.Update( 0.001f );
		long before = GC.GetAllocatedBytesForCurrentThread();
		for ( int i = 0; i < 128; i++ ) simulation.Update( 0.001f );
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.IsTrue( allocated <= 256, $"Warmed serial updates allocated {allocated} bytes" );
		Assert.IsTrue( agent.State.Position.x > 100 );
	}

	[TestMethod]
	public void PartialRouteResumesWhenAConnectionAppears()
	{
		var mesh = SyntheticNavMesh.Create( new() { UpperFloor = true } );
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var agent = simulation.Add( new Vector3( 100, 1, 320 ), Settings( mesh ) );
		var target = new Vector3( 500, 201, 320 );
		agent.MoveTo( target );
		for ( int i = 0; i < 500; i++ ) simulation.Update( 0.02f );
		Assert.IsTrue( agent.Partial );
		Assert.AreEqual( (Vector3?)target, agent.State.Target );
		var connected = SyntheticNavMesh.Create( new() { UpperFloor = true, Link = true } );
		Assert.IsTrue( mesh.UpdateTile( connected.GetTile( 0 ).data, 0 ).Succeeded() );
		simulation.Revision++;
		for ( int i = 0; i < 700; i++ ) simulation.Update( 0.02f );
		Assert.IsTrue( agent.State.Position.Distance( target ) < 1 );
		Assert.IsNull( agent.State.Target );
	}

	[TestMethod]
	public void AgentWaitsWhenItsTileDisappearsAndResumesWhenRestored()
	{
		var mesh = SyntheticNavMesh.Create();
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var agent = simulation.Add( new Vector3( 100, 1, 100 ), Settings( mesh ) );
		var target = new Vector3( 500, 1, 100 );
		agent.MoveTo( target );
		simulation.Update( 0.02f );
		var data = mesh.GetTile( 0 ).data;
		mesh.RemoveTile( mesh.GetTileRef( mesh.GetTile( 0 ) ) );
		simulation.Revision++;
		var position = agent.State.Position;
		simulation.Update( 0.02f );
		Assert.AreEqual( 0, agent.Path.Count );
		Assert.IsFalse( agent.State.Navigating );
		Assert.AreEqual( position, agent.State.Position );
		Assert.AreEqual( (Vector3?)target, agent.State.Target );
		mesh.AddTile( data, 0, 0, out _ );
		simulation.Revision++;
		for ( int i = 0; i < 400; i++ ) simulation.Update( 0.02f );
		Assert.IsTrue( agent.State.Position.Distance( target ) < 1 );
	}

	[TestMethod]
	public void StoppingOnALinkReprojectsOntoTheCurrentFloor()
	{
		var mesh = SyntheticNavMesh.Create( new() { UpperFloor = true, Link = true } );
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var agent = simulation.Add( new Vector3( 320, 1, 320 ), Settings( mesh, false ) );
		agent.MoveTo( new Vector3( 500, 201, 320 ) );
		simulation.Update( 0.02f );
		Assert.IsTrue( agent.State.Link.HasValue );
		agent.Stop();
		simulation.Update( 0.02f );
		Assert.IsNull( agent.State.Link );
		Assert.IsTrue( agent.Query.GetPolyHeight( agent.Path[0], agent.Position, out float height ).Succeeded() );
		Assert.AreEqual( 1f, height );
		agent.MoveTo( new Vector3( 100, 1, 320 ) );
		for ( int i = 0; i < 300; i++ ) simulation.Update( 0.02f );
		Assert.IsTrue( agent.State.Position.Distance( new Vector3( 100, 1, 320 ) ) < 1 );
	}

	static SimulationSettings Settings( NavMeshGraph mesh, bool automatic = true ) => new( 8, 32, 120, 240, 0.25f, automatic, TraversalFilter.Unrestricted );

	[TestMethod]
	public void AirborneAgentRecoversAndKeepsTarget_10230()
	{
		var mesh = SyntheticNavMesh.Create();
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var agent = simulation.Add( new Vector3( 100, 500, 100 ), Settings( mesh ) );
		agent.MoveTo( new Vector3( 500, 1, 100 ) );
		simulation.Update( 0.02f );
		Assert.AreEqual( 500f, agent.State.Position.y, "An unsuccessful placement must not reset position to the origin" );
		agent.SetPosition( new Vector3( 100, 1, 100 ) );
		for ( int i = 0; i < 300; i++ ) simulation.Update( 0.02f );
		Assert.IsTrue( agent.State.Position.Distance( new Vector3( 500, 1, 100 ) ) < 1 );
	}

	[TestMethod]
	public void VerticalLinkTraversesInBothDirections_10146()
	{
		var mesh = SyntheticNavMesh.Create( new() { UpperFloor = true, Link = true } );
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var agent = simulation.Add( new Vector3( 100, 1, 320 ), Settings( mesh ) );
		foreach ( float height in new[] { 201f, 1f } )
		{
			agent.MoveTo( new Vector3( 500, height, 320 ) );
			bool entered = false;
			for ( int i = 0; i < 500; i++ ) { simulation.Update( 0.02f ); entered |= agent.State.Link.HasValue; }
			Assert.IsTrue( entered );
			Assert.IsTrue( agent.State.Position.Distance( new Vector3( 500, height, 320 ) ) < 1, agent.State.ToString() );
		}
	}

	[TestMethod]
	public void ManualLinkWaitsForCompletion_10146()
	{
		var mesh = SyntheticNavMesh.Create( new() { UpperFloor = true, Link = true } );
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var agent = simulation.Add( new Vector3( 100, 1, 320 ), Settings( mesh, false ) );
		agent.MoveTo( new Vector3( 500, 201, 320 ) );
		for ( int i = 0; i < 300; i++ ) simulation.Update( 0.02f );
		Assert.IsTrue( agent.State.Link.HasValue );
		Assert.AreEqual( 1f, agent.State.Position.y, 0.01f );
		agent.SetPosition( agent.State.Link.Value.End );
		agent.CompleteLink();
		for ( int i = 0; i < 300; i++ ) simulation.Update( 0.02f );
		Assert.IsFalse( agent.State.Link.HasValue );
		Assert.IsTrue( agent.State.Position.Distance( new Vector3( 500, 201, 320 ) ) < 1 );
	}

	[TestMethod]
	[DataRow( 120, true )]
	[DataRow( -120, true )]
	[DataRow( 120, false )]
	[DataRow( -120, false )]
	public void ManualLinkResumesFromActualLanding( int offset, bool ascending )
	{
		var mesh = SyntheticNavMesh.Create( new() { UpperFloor = true, Link = true, Obstacles = true } );
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		float startHeight = ascending ? 1 : 201, endHeight = ascending ? 201 : 1;
		var agent = simulation.Add( new Vector3( 320, startHeight, 320 ), Settings( mesh, false ) );
		var target = new Vector3( 500, endHeight, 320 );
		agent.MoveTo( target );
		for ( int i = 0; i < 500 && !agent.State.Link.HasValue; i++ ) simulation.Update( 0.02f );
		Assert.IsTrue( agent.State.Link.HasValue );
		long exitPolygon = agent.Path[0];
		var landing = agent.State.Link.Value.End + new Vector3( offset, 0, 0 );
		agent.Query.FindNearestPoly( landing, new Vector3( 8 ), TraversalFilter.Unrestricted, out var landingPolygon, out _, out _ );
		Assert.AreNotEqual( 0L, landingPolygon );
		Assert.AreNotEqual( exitPolygon, landingPolygon, "Exercise a landing outside the original exit polygon" );
		agent.SetPosition( landing );
		agent.CompleteLink();
		Assert.AreEqual( landing, agent.State.Position, "Custom traversal must not snap back to the link endpoint" );
		Assert.IsNull( agent.State.Link );
		Assert.AreEqual( (Vector3?)target, agent.State.Target );
		agent.CompleteLink();
		Assert.AreEqual( landing, agent.State.Position, "Completing twice must be harmless" );
		simulation.Update( 0.02f );
		Assert.IsTrue( agent.State.Position.Distance( landing ) < 1, "The first walking update must start from the landing polygon" );
		Assert.IsTrue( agent.State.Navigating );
		for ( int i = 0; i < 2000; i++ ) simulation.Update( 0.02f );
		Assert.IsNull( agent.State.Link );
		Assert.IsNull( agent.State.Target );
		Assert.IsTrue( agent.State.Position.Distance( target ) < 1, agent.State.ToString() );
	}

	[TestMethod]
	public void ManualLinkCompletedOffMeshWaitsForPlacement()
	{
		var mesh = SyntheticNavMesh.Create( new() { UpperFloor = true, Link = true } );
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var agent = simulation.Add( new Vector3( 320, 1, 320 ), Settings( mesh, false ) );
		var target = new Vector3( 500, 201, 320 );
		agent.MoveTo( target );
		simulation.Update( 0.02f );
		Assert.IsTrue( agent.State.Link.HasValue );
		var outside = new Vector3( 800, 201, 320 );
		agent.SetPosition( outside );
		agent.CompleteLink();
		for ( int i = 0; i < 10; i++ ) simulation.Update( 0.02f );
		Assert.AreEqual( outside, agent.State.Position );
		Assert.AreEqual( (Vector3?)target, agent.State.Target );
		Assert.IsFalse( agent.State.Navigating );
		agent.SetPosition( new Vector3( 600, 201, 320 ) );
		for ( int i = 0; i < 300; i++ ) simulation.Update( 0.02f );
		Assert.IsTrue( agent.State.Position.Distance( target ) < 1 );
	}

	[TestMethod]
	public void ShortLinkEntryIsPublishedBeforeCompletion()
	{
		var mesh = SyntheticNavMesh.Create( new() { UpperFloor = true, Link = true } );
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var agent = simulation.Add( new Vector3( 320, 1, 320 ), Settings( mesh ) with { MaxSpeed = 100000 } );
		agent.MoveTo( new Vector3( 500, 201, 320 ) );
		simulation.Update( 0.4f );
		Assert.IsTrue( agent.State.Link.HasValue, "Substeps must not consume the entry before component callbacks can observe it" );
		simulation.Update( 0.02f );
		Assert.IsFalse( agent.State.Link.HasValue );
	}

	[TestMethod]
	public void PassingABlockingNeighbourDoesNotOrbitIt()
	{
		var mesh = SyntheticNavMesh.Create();
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var agent = simulation.Add( new Vector3( 240, 1, 320 ), Settings( mesh ) );
		simulation.Add( new Vector3( 330, 1, 320 ), Settings( mesh ) );
		var target = new Vector3( 350, 1, 320 );
		agent.MoveTo( target );
		float travelled = 0;
		int sidestep = 0;
		var previous = agent.Position;
		for ( int tick = 0; tick < 2000 && agent.State.Target.HasValue; tick++ )
		{
			simulation.Update( 0.02f );
			travelled += agent.Position.Distance( previous );
			previous = agent.Position;
			if ( agent.Position.Distance( target ) > 20 && agent.Velocity.Length > 1 && agent.Velocity.Normal.x < 0.5f ) sidestep++;
		}
		Assert.IsTrue( agent.Position.Distance( target ) < 10, agent.State.ToString() );
		Assert.AreEqual( 0, sidestep, $"Passing a blocker turned the agent away from its goal for {sidestep} ticks" );
		Assert.IsTrue( travelled < 170, $"Route around one blocker was {travelled:0} units instead of 110" );
	}

	[TestMethod]
	public void NeighbourPassedWideDoesNotDivertTheAgent()
	{
		var mesh = SyntheticNavMesh.Create();
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var agent = simulation.Add( new Vector3( 200, 1, 320 ), Settings( mesh ) );
		simulation.Add( new Vector3( 320, 1, 360 ), Settings( mesh ) );
		var target = new Vector3( 440, 1, 320 );
		agent.MoveTo( target );
		float lateral = 0;
		for ( int tick = 0; tick < 2000; tick++ )
		{
			simulation.Update( 0.02f );
			lateral = MathF.Max( lateral, MathF.Abs( agent.Position.z - 320 ) );
		}
		Assert.IsTrue( agent.Position.Distance( target ) < 1, agent.State.ToString() );
		Assert.IsTrue( lateral < 1, $"A neighbour cleared by 40 units diverted the agent by {lateral:0.0}" );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 0.25f )]
	[DataRow( 1f )]
	public void SeparationDoesNotPreventReachingAGoalBesideANeighbour( float separation )
	{
		var mesh = SyntheticNavMesh.Create();
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var settings = Settings( mesh ) with { Separation = separation };
		simulation.Add( new Vector3( 330, 1, 320 ), settings );
		var agent = simulation.Add( new Vector3( 240, 1, 320 ), settings );
		var target = new Vector3( 350, 1, 320 );
		agent.MoveTo( target );
		int stalled = 0, worst = 0;
		for ( int tick = 0; tick < 1500; tick++ )
		{
			simulation.Update( 0.02f );
			if ( agent.State.Navigating && agent.Velocity.Length < 6 ) worst = Math.Max( worst, ++stalled );
			else stalled = 0;
		}
		Assert.IsTrue( worst < 75, $"Separation {separation} stalled the agent for {worst * 0.02f:0.0}s" );
		Assert.IsTrue( agent.Position.Distance( target ) < 12, $"Separation {separation} left the agent {agent.Position.Distance( target ):0} units short: {agent.State}" );
	}

	[TestMethod]
	public void CrossingAgentsPassWithoutStalling()
	{
		var mesh = SyntheticNavMesh.Create();
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var first = simulation.Add( new Vector3( 200, 1, 320 ), Settings( mesh ) );
		var second = simulation.Add( new Vector3( 320, 1, 200 ), Settings( mesh ) );
		var firstTarget = new Vector3( 440, 1, 320 );
		var secondTarget = new Vector3( 320, 1, 440 );
		first.MoveTo( firstTarget );
		second.MoveTo( secondTarget );
		float travelled = 0, closest = float.MaxValue;
		var previous = first.Position;
		for ( int tick = 0; tick < 1000; tick++ )
		{
			simulation.Update( 0.02f );
			travelled += first.Position.Distance( previous );
			previous = first.Position;
			closest = MathF.Min( closest, first.Position.Distance( second.Position ) );
		}
		Assert.IsTrue( closest >= 16, $"Crossing agents overlapped: {closest:0.0}" );
		Assert.IsTrue( first.Position.Distance( firstTarget ) < 2, first.State.ToString() );
		Assert.IsTrue( second.Position.Distance( secondTarget ) < 2, second.State.ToString() );
		Assert.IsTrue( travelled < 300, $"Crossing detour was {travelled:0} units on a 240 unit route" );
	}

	[TestMethod]
	public void AgentsOrderedToOnePointPackAroundItWithoutOverlapping()
	{
		var mesh = SyntheticNavMesh.Create();
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var target = new Vector3( 420, 1, 420 );
		var agents = new List<SimulationAgent>();
		for ( int i = 0; i < 8; i++ )
		{
			var agent = simulation.Add( new Vector3( 120 + i % 4 * 30, 1, 200 + i / 4 * 30 ), Settings( mesh ) );
			agent.MoveTo( target );
			agents.Add( agent );
		}
		float closest = float.MaxValue;
		for ( int tick = 0; tick < 1500; tick++ )
		{
			simulation.Update( 0.02f );
			if ( tick < 60 ) continue;
			for ( int i = 0; i < agents.Count; i++ )
				for ( int j = i + 1; j < agents.Count; j++ )
					closest = MathF.Min( closest, agents[i].Position.WithY( 0 ).Distance( agents[j].Position.WithY( 0 ) ) );
		}
		Assert.IsTrue( closest > 12.8f, $"A pile-up compressed agents to {closest:0.0} apart, radii sum 16" );
		foreach ( var agent in agents )
			Assert.IsTrue( agent.Position.Distance( target ) < 32, $"An agent settled {agent.Position.Distance( target ):0} units from the shared destination: {agent.State}" );
	}

	[TestMethod]
	public void HeadOnAgentsPassAndReachTheirTargets()
	{
		var mesh = SyntheticNavMesh.Create();
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var start = new Vector3( 100, 1, 320 );
		var end = new Vector3( 500, 1, 320 );
		var left = simulation.Add( start, Settings( mesh ) );
		var right = simulation.Add( end, Settings( mesh ) );
		left.MoveTo( end );
		right.MoveTo( start );
		float minimumDistance = float.MaxValue;
		for ( int i = 0; i < 500; i++ )
		{
			simulation.Update( 0.02f );
			minimumDistance = MathF.Min( minimumDistance, left.State.Position.Distance( right.State.Position ) );
		}
		Assert.IsTrue( minimumDistance >= 16, $"Agents overlapped: distance {minimumDistance}" );
		Assert.IsTrue( left.State.Position.Distance( end ) < 1 );
		Assert.IsTrue( right.State.Position.Distance( start ) < 1 );
	}

	[TestMethod]
	public void CommandsAndSnapshotsAreSafeDuringParallelSimulation()
	{
		var mesh = SyntheticNavMesh.Create();
		var simulation = new NavigationSimulation( mesh, new object(), 8, 32 );
		var agents = Enumerable.Range( 0, 100 ).Select( i => simulation.Add( new Vector3( 50 + i % 10 * 50, 1, 50 + i / 10 * 50 ), Settings( mesh ) ) ).ToArray();
		Parallel.Invoke(
			() => { for ( int i = 0; i < 100; i++ ) simulation.Update( 0.02f ); },
			() => { foreach ( var agent in agents ) { agent.MoveTo( new Vector3( 320, 1, 320 ) ); Assert.IsTrue( float.IsFinite( agent.State.Position.x ) ); agent.Stop(); } } );
		Assert.AreEqual( 100, simulation.Count );
	}
}
