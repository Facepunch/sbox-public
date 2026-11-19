using Sandbox.Engine.Resources;
using Sandbox.Navigation;
using Sandbox.Volumes;
using System;
using System.Collections.Generic;

namespace NavigationTests;

/// <summary>
/// Area definitions on areas, links and agents: costs, permissions, priorities where areas overlap,
/// and how many definitions get an area of their own.
/// </summary>
[TestClass]
public class NavMeshAreaDefinitions
{
	readonly List<NavMeshAreaDefinition> registered = new();
	readonly string folder = $"tests/navmesh-area-{Guid.NewGuid():N}";

	// Equal priorities are ordered by resource path, so a definition made later sorts after the earlier
	// ones and leaves their area ids alone.
	NavMeshAreaDefinition Define( float cost = 1, int priority = 0 )
	{
		var definition = new NavMeshAreaDefinition { CostMultiplier = cost, Priority = priority };
		definition.RegisterWeakResourceId( $"{folder}/{registered.Count:000}.navarea" );
		Game.Resources.Register( definition );
		registered.Add( definition );
		return definition;
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var definition in registered ) Game.Resources.Unregister( definition );
	}

	static NavMeshArea Area( Scene scene, BBox box, NavMeshAreaDefinition definition )
	{
		var area = scene.CreateObject().Components.Create<NavMeshArea>( false );
		area.IsBlocker = false;
		area.Area = definition;
		area.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Box, Box = box };
		area.Enabled = true;
		return area;
	}

	static float Reach( NavMeshPath path ) => path.Points.Max( point => MathF.Abs( point.Position.y ) );

	[TestMethod]
	public async Task AreaCostsAndPermissionsSteerPaths()
	{
		var mud = Define( cost: 10 );
		var scene = new Scene();
		using var scope = scene.Push();
		scene.NavMesh.UpdateAreaIds();
		scene.Floor( Vector3.Zero, 700 );

		// A strip of mud across the floor that leaves a way round at the far side
		Area( scene, new BBox( new Vector3( -50, -400, -50 ), new Vector3( 50, 200, 50 ) ), mud );
		await scene.NavMesh.Generate( scene.PhysicsWorld );

		var request = new CalculatePathRequest { Start = new Vector3( -250, 0, 0 ), Target = new Vector3( 250, 0, 0 ) };
		var around = scene.NavMesh.CalculatePath( request );
		Assert.AreEqual( NavMeshPathStatus.Complete, around.Status );
		Assert.IsTrue( Reach( around ) > 190, "Ten times the cost: the way round is cheaper" );

		// A cost is picked up by the next query, nothing is built again
		mud.CostMultiplier = 1;
		Assert.IsTrue( Reach( scene.NavMesh.CalculatePath( request ) ) < 10, "At the cost of plain ground the path goes straight through" );

		var agent = scene.Agent( request.Start );
		request.Agent = agent;
		Assert.IsTrue( Reach( scene.NavMesh.CalculatePath( request ) ) < 10 );

		// The set is edited in place, as game code does
		agent.ForbiddenAreas.Add( mud );
		Assert.IsTrue( Reach( scene.NavMesh.CalculatePath( request ) ) > 190, "A forbidden area is walked around" );
		agent.MoveTo( request.Target );
		for ( int i = 0; i < 300 && agent.TargetPosition.HasValue; i++ )
		{
			scene.GameTick();
			Assert.IsFalse( MathF.Abs( agent.AgentPosition.x ) < 40 && agent.AgentPosition.y < 190, $"The agent walked into the mud at {agent.AgentPosition}" );
		}
		Assert.IsTrue( agent.AgentPosition.Distance( new Vector3( 250, 0, 4 ) ) < 6 );

		agent.ForbiddenAreas.Clear();
		var other = Define();
		scene.NavMesh.UpdateAreaIds();
		agent.AllowedAreas.Add( other );
		Assert.IsTrue( Reach( scene.NavMesh.CalculatePath( request ) ) > 190, "Mud is not among the allowed areas" );
		agent.AllowedAreas.Add( mud );
		Assert.IsTrue( Reach( scene.NavMesh.CalculatePath( request ) ) < 10 );
	}

	[TestMethod]
	public async Task TheHigherPriorityWinsWhereAreasOverlap()
	{
		var low = Define( priority: 1 );
		var high = Define( priority: 5 );
		var scene = new Scene();
		using var scope = scene.Push();
		scene.NavMesh.UpdateAreaIds();
		Assert.IsTrue( scene.NavMesh.AreaDefinitionToId( high ) > scene.NavMesh.AreaDefinitionToId( low ) );
		Assert.AreSame( high, scene.NavMesh.AreaIdToDefinition( scene.NavMesh.AreaDefinitionToId( high ) ) );

		scene.Floor( Vector3.Zero, 700 );
		Area( scene, BBox.FromPositionAndSize( new Vector3( -50, 0, 0 ), new Vector3( 200, 200, 100 ) ), low );
		Area( scene, BBox.FromPositionAndSize( new Vector3( 50, 0, 0 ), new Vector3( 200, 200, 100 ) ), high );
		await scene.NavMesh.Generate( scene.PhysicsWorld );

		// x from -50 to 50 is under both areas
		var noLow = scene.Agent();
		noLow.ForbiddenAreas.Add( low );
		var noHigh = scene.Agent();
		noHigh.ForbiddenAreas.Add( high );
		var overlap = new Vector3( 20, 0, 0 );
		var start = new Vector3( 0, 250, 0 );

		var intoHigh = scene.NavMesh.CalculatePath( new() { Start = start, Target = overlap, Agent = noLow } );
		Assert.IsTrue( intoHigh.Points[^1].Position.Distance( overlap.WithZ( 4 ) ) < 1, "The overlap belongs to the area of higher priority" );
		var keptOut = scene.NavMesh.CalculatePath( new() { Start = start, Target = overlap, Agent = noHigh } );
		Assert.IsTrue( keptOut.Points[^1].Position.x < -45, $"An agent kept out of the higher area ends beside the overlap, was {keptOut.Points[^1].Position}" );

		// Priorities swapped: ids are given out again in every scene and the tiles follow in the next updates
		low.Priority = 9;
		Assert.IsTrue( scene.NavMesh.AreaDefinitionToId( low ) > scene.NavMesh.AreaDefinitionToId( high ) );
		await scene.Settle();
		keptOut = scene.NavMesh.CalculatePath( new() { Start = start, Target = overlap, Agent = noHigh } );
		Assert.IsTrue( keptOut.Points[^1].Position.Distance( overlap.WithZ( 4 ) ) < 1 );
	}

	[TestMethod]
	public async Task LinksCarryAnAreaAndTheDefaultAreaIsAboutTheGround()
	{
		var ladder = Define();
		var scene = new Scene();
		using var scope = scene.Push();
		scene.NavMesh.UpdateAreaIds();
		scene.Floor( Vector3.Zero );
		scene.Floor( new Vector3( 0, 0, 200 ) );
		var link = scene.Link<NavMeshLink>( Vector3.Zero, new Vector3( 0, 0, 200 ) );
		await scene.NavMesh.Generate( scene.PhysicsWorld );

		var agent = scene.Agent( new Vector3( 100, 0, 0 ) );
		var up = new CalculatePathRequest { Start = new Vector3( 100, 0, 0 ), Target = new Vector3( 100, 0, 200 ), Agent = agent };
		agent.ForbiddenAreas.Add( ladder );
		Assert.AreEqual( NavMeshPathStatus.Complete, scene.NavMesh.CalculatePath( up ).Status );

		link.Area = ladder;
		scene.NavUpdate();
		Assert.AreEqual( NavMeshPathStatus.Partial, scene.NavMesh.CalculatePath( up ).Status, "The link is in an area the agent may not cross" );
		agent.ForbiddenAreas.Clear();
		Assert.AreEqual( NavMeshPathStatus.Complete, scene.NavMesh.CalculatePath( up ).Status );

		// Links without a definition stay open, AllowDefaultArea closes plain ground only
		link.Area = null;
		scene.NavUpdate();
		agent.AllowDefaultArea = false;
		var filter = scene.NavMesh.CreateFilter( agent );
		Assert.IsFalse( filter.Allows( NavMesh.DefaultArea ) );
		Assert.IsTrue( filter.Allows( NavMesh.LinkArea ) );
		Assert.AreEqual( NavMeshPathStatus.StartNotFound, scene.NavMesh.CalculatePath( up ).Status );
	}

	[TestMethod]
	public void SixtyOneDefinitionsGetAnAreaOfTheirOwn()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.NavMesh.UpdateAreaIds();
		int taken = Enumerable.Range( NavMesh.FirstDefinitionArea, NavMesh.AreaCount - NavMesh.FirstDefinitionArea ).Count( id => scene.NavMesh.AreaIdToDefinition( id ) is not null );

		// Above every definition that is there already, in the order of their priorities
		var definitions = Enumerable.Range( 0, 62 - taken ).Select( i => Define( priority: 1000 + i ) ).ToArray();
		scene.NavMesh.UpdateAreaIds();

		var ids = definitions.Select( scene.NavMesh.AreaDefinitionToId ).ToArray();
		CollectionAssert.AreEqual( Enumerable.Range( NavMesh.FirstDefinitionArea + taken, 61 - taken ).ToArray(), ids[..^1] );
		Assert.AreEqual( NavMesh.DefaultArea, ids[^1], "One more than fit is treated as no definition" );
	}
}
