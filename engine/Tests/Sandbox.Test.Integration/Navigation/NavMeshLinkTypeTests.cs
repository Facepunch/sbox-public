using Sandbox.Engine.Resources;
using Sandbox.Navigation;
using System;
using System.Collections.Generic;

namespace NavigationTests;

/// <summary>
/// Link types on links and agents: which types an agent takes, what a type costs, what the
/// traversal reports, the limit of types, and exclusive links that take one agent at a time.
/// </summary>
[TestClass]
public class NavMeshLinkTypeTests
{
	// Two floors, one above the other, joined by a typed link at y 0 and a plain one at y 150
	static async Task<(Scene Scene, NavMeshLink Typed, NavMeshLink Plain)> TwoLinks( NavMeshLinkType type )
	{
		var scene = new Scene();
		scene.Floor( Vector3.Zero );
		scene.Floor( new Vector3( 0, 0, 200 ) );
		var typed = scene.Link<NavMeshLink>( new Vector3( 100, 0, 0 ), new Vector3( 100, 0, 200 ) );
		typed.Type = type;
		var plain = scene.Link<NavMeshLink>( new Vector3( 100, 150, 0 ), new Vector3( 100, 150, 200 ) );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		return (scene, typed, plain);
	}

	// Where the path reaches the upper floor
	static float LinkY( NavMeshPath path ) => path.Points.First( point => point.Position.z > 100 ).Position.y;

	[TestMethod]
	public void TheEngineShipsItsLinkTypes()
	{
		var core = new LocalFileSystem( System.IO.Path.Combine( Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" ), "core" ) );
		var shipped = new (string Name, Func<NavMeshLinkType> Get)[]
		{
			("jump", () => NavMeshLinkType.Jump), ("drop", () => NavMeshLinkType.Drop), ("ladder", () => NavMeshLinkType.Ladder), ("climb", () => NavMeshLinkType.Climb),
			("crawl", () => NavMeshLinkType.Crawl), ("interact", () => NavMeshLinkType.Interact), ("teleport", () => NavMeshLinkType.Teleport)
		};
		var loaded = new List<NavMeshLinkType>();
		try
		{
			foreach ( var (name, get) in shipped )
			{
				var type = Game.Resources.LoadGameResource<NavMeshLinkType>( $"navigation/{name}.navlink", core );
				Assert.IsNotNull( type, $"{name} is not in core" );
				loaded.Add( type );
				Assert.AreSame( type, get(), name );
			}
			Assert.AreEqual( shipped.Length, loaded.Distinct().Count() );
		}
		finally
		{
			foreach ( var type in loaded ) Game.Resources.Unregister( type );
		}
	}

	[TestMethod]
	public async Task ALinkTypeTheAgentMayNotTakeIsWalkedAround()
	{
		var ladder = new NavMeshLinkType();
		var (scene, typed, plain) = await TwoLinks( ladder );
		using var scope = scene.Push();
		var agent = scene.Agent();
		var types = new List<NavMeshLinkType>();
		agent.LinkEnter = () => types.Add( agent.CurrentLinkTraversal.Value.Type );
		var up = new CalculatePathRequest { Start = Vector3.Zero, Target = new Vector3( -100, 0, 200 ), Agent = agent };
		Assert.IsTrue( LinkY( scene.NavMesh.CalculatePath( up ) ) < 10, "Every type is allowed by default" );

		agent.AllowAllLinkTypes = false;
		Assert.IsTrue( LinkY( scene.NavMesh.CalculatePath( up ) ) > 140, "An empty list still takes plain links" );
		agent.AllowedLinkTypes.Add( ladder );
		Assert.IsTrue( LinkY( scene.NavMesh.CalculatePath( up ) ) < 10 );
		agent.AllowedLinkTypes.Clear();

		// The crowd picks up the set edited in place
		agent.MoveTo( up.Target );
		for ( int i = 0; i < 1000 && agent.IsNavigating | types.Count == 0; i++ ) scene.GameTick();
		CollectionAssert.AreEqual( new NavMeshLinkType[] { null }, types, "Only the plain link should have been taken" );
		Assert.IsTrue( agent.AgentPosition.z > 150 );

		agent.AllowedLinkTypes.Add( ladder );
		agent.MoveTo( new Vector3( 120, 0, 0 ) );
		for ( int i = 0; i < 1000 && agent.IsNavigating | types.Count == 1; i++ ) scene.GameTick();
		CollectionAssert.AreEqual( new[] { null, ladder }, types );

		// Restricted to a type the navmesh has not seen yet: the ladder is closed again, and only the plain link is left
		plain.Enabled = false;
		agent.AllowedLinkTypes = new() { new NavMeshLinkType() };
		scene.NavUpdate();
		Assert.AreEqual( NavMeshPathStatus.Partial, scene.NavMesh.CalculatePath( up with { Start = new Vector3( 120, 0, 0 ) } ).Status );
	}

	[TestMethod]
	public async Task ATypesPenaltyAndCostSetAtRuntimeChangeTheRoute()
	{
		var jump = new NavMeshLinkType();
		var (scene, typed, _) = await TwoLinks( jump );
		using var scope = scene.Push();
		var agent = scene.Agent();
		var up = new CalculatePathRequest { Start = Vector3.Zero, Target = new Vector3( -100, 0, 200 ), Agent = agent };
		Assert.IsTrue( LinkY( scene.NavMesh.CalculatePath( up ) ) < 10 );

		jump.Penalty = 1000;
		Assert.IsTrue( LinkY( scene.NavMesh.CalculatePath( up ) ) > 140, "Walking round is shorter than the penalty" );
		Assert.IsTrue( LinkY( scene.NavMesh.CalculatePath( up with { Agent = null } ) ) > 140, "A path without an agent is priced the same" );
		jump.Penalty = 50;
		Assert.IsTrue( LinkY( scene.NavMesh.CalculatePath( up ) ) < 10, "The way round is longer than a small penalty" );
		jump.Penalty = float.PositiveInfinity;
		Assert.AreEqual( 0, jump.Penalty, "A penalty that is not finite counts as none" );
		jump.CostMultiplier = 100;
		Assert.IsTrue( LinkY( scene.NavMesh.CalculatePath( up ) ) > 140, "A slow link costs more than the way round" );

		// The crowd follows the changed type in the next fixed update
		int id = scene.NavMesh.LinkTypeToId( jump );
		scene.GameTick();
		Assert.AreEqual( 100, agent.agentInternal.Options.Filter.LinkCosts[id] );
		jump.CostMultiplier = 1;
		scene.GameTick();
		Assert.AreEqual( 1, agent.agentInternal.Options.Filter.LinkCosts[id] );
	}

	[TestMethod]
	public void TheSixtyFourthTypeIsAPlainLink()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var types = Enumerable.Range( 0, 64 ).Select( i => new NavMeshLinkType() ).ToArray();
		var ids = types.Take( 63 ).Select( scene.NavMesh.LinkTypeToId ).ToArray();
		CollectionAssert.AreEqual( Enumerable.Range( 1, 63 ).ToArray(), ids );
		Assert.AreEqual( 5, scene.NavMesh.LinkTypeToId( types[4] ), "Ids are kept" );
		Assert.AreEqual( 0, scene.NavMesh.LinkTypeToId( null ) );

		var link = scene.CreateObject().Components.Create<NavMeshLink>();
		Assert.IsNull( link.Type );
		link.Type = types[7];
		Assert.AreEqual( 8, link.CreateLink().Type );
		link.Type = types[63];
		Assert.AreEqual( 0, link.CreateLink().Type );
	}

	static async Task<(Scene Scene, NavMeshLink Link, NavMeshAgent[] Agents)> ExclusiveLink( bool autoTraverse )
	{
		var scene = new Scene();
		scene.Floor( Vector3.Zero );
		scene.Floor( new Vector3( 0, 0, 200 ) );
		var link = scene.Link<NavMeshLink>( new Vector3( 100, 0, 0 ), new Vector3( 100, 0, 200 ) );
		link.Occupancy = NavMeshLinkOccupancy.OneAtATime;
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );

		// Both reach the link at about the same time
		var agents = new[] { scene.Agent( new Vector3( 40, 40, 0 ) ), scene.Agent( new Vector3( 40, -40, 0 ) ) };
		foreach ( var agent in agents )
		{
			agent.AutoTraverseLinks = autoTraverse;
			agent.MoveTo( new Vector3( -100, agent.AgentPosition.y, 200 ) );
		}
		return (scene, link, agents);
	}

	static bool Waits( NavMeshAgent agent ) => !agent.IsTraversingLink && agent.agentInternal.State.Link >= 0;

	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public async Task AnExclusiveLinkTakesOneAgentAtATime( bool autoTraverse )
	{
		var (scene, link, agents) = await ExclusiveLink( autoTraverse );
		using var scope = scene.Push();
		var events = new List<string>();
		var held = new Dictionary<NavMeshAgent, int>();
		foreach ( var agent in agents )
		{
			string name = agent == agents[0] ? "a" : "b";
			agent.LinkEnter = () => events.Add( name + "+" );
			agent.LinkExit = () => events.Add( name + "-" );
		}

		bool waited = false;
		for ( int i = 0; i < 3000 && events.Count < 4; i++ )
		{
			scene.NavUpdate();
			var crossing = agents.Where( agent => agent.IsTraversingLink ).ToArray();
			Assert.IsTrue( crossing.Length <= 1, "Two agents on an exclusive link" );
			CollectionAssert.AreEqual( crossing, link.Occupants.ToArray() );
			Assert.AreEqual( crossing.Length == 1, link.IsOccupied );
			waited |= agents.Any( Waits );
			if ( autoTraverse ) continue;

			// Game code takes its time crossing
			foreach ( var agent in crossing )
			{
				held[agent] = held.GetValueOrDefault( agent ) + 1;
				if ( held[agent] < 20 ) continue;
				agent.SetAgentPosition( agent.CurrentLinkTraversal.Value.LinkExitPosition );
				agent.CompleteLinkTraversal();
			}
		}

		Assert.IsTrue( waited, "The second agent should have waited at the link" );
		Assert.AreEqual( 4, events.Count, string.Join( " ", events ) );
		Assert.AreEqual( events[0][0], events[1][0], $"The first agent leaves before the second enters: {string.Join( " ", events )}" );
		Assert.AreEqual( '+', events[2][1] );
		Assert.IsFalse( link.IsOccupied );

		for ( int i = 0; i < 300 && agents.Any( agent => agent.IsNavigating ); i++ ) scene.NavUpdate();
		foreach ( var agent in agents ) Assert.IsTrue( agent.AgentPosition.z > 150, $"An agent stopped at {agent.AgentPosition}" );
	}

	[TestMethod]
	public async Task AReservationEndsWithItsOccupantAndAWaitEndsWithTheLink()
	{
		var (scene, link, agents) = await ExclusiveLink( false );
		using var scope = scene.Push();
		for ( int i = 0; i < 1000 && !agents.Any( Waits ); i++ ) scene.NavUpdate();
		var first = agents.Single( agent => agent.IsTraversingLink );
		var second = agents.Single( Waits );
		Assert.AreSame( first, link.Occupants.Single() );

		first.Enabled = false;
		Assert.IsFalse( link.IsOccupied );
		scene.NavUpdate();
		Assert.IsTrue( second.IsTraversingLink, "The link goes to the waiting agent" );
		Assert.AreSame( second, link.Occupants.Single() );

		// Back again, the first agent waits for the second
		first.Enabled = true;
		first.MoveTo( new Vector3( -100, 0, 200 ) );
		for ( int i = 0; i < 1000 && !Waits( first ); i++ ) scene.NavUpdate();
		Assert.IsTrue( Waits( first ) );

		// A changed setting does not let a held agent go
		first.MaxSpeed = 100;
		first.AutoTraverseLinks = true;
		Assert.IsTrue( first.agentInternal.Options.External );
		Assert.IsFalse( first.agentInternal.Options.AutoLinks );
		first.AutoTraverseLinks = false;

		int exits = 0;
		second.LinkExit = () => exits++;
		link.Enabled = false;
		Assert.IsFalse( link.IsOccupied );
		Assert.AreEqual( 1, exits, "The occupant's crossing ends with the link" );
		Assert.IsFalse( second.IsTraversingLink );
		scene.NavUpdate();
		Assert.IsTrue( first.agentInternal.State.Link < 0, "A waiting agent leaves a link that is gone" );
		Assert.IsTrue( second.agentInternal.State.Link < 0, "So does the agent that crossed it" );
		Assert.IsFalse( first.IsTraversingLink );
		Assert.IsFalse( first.agentInternal.Options.External, "It is moved by the crowd again" );
		Assert.AreEqual( 1, exits );

		// Back again, the link still takes one agent at a time
		link.Enabled = true;
		var held = new Dictionary<NavMeshAgent, int>();
		for ( int i = 0; i < 3000 && agents.Any( agent => agent.AgentPosition.z < 150 ); i++ )
		{
			scene.NavUpdate();
			var crossing = agents.Where( agent => agent.IsTraversingLink ).ToArray();
			Assert.IsTrue( crossing.Length <= 1, "Two agents on an exclusive link after it came back" );
			CollectionAssert.AreEqual( crossing, link.Occupants.ToArray() );
			foreach ( var agent in crossing )
			{
				held[agent] = held.GetValueOrDefault( agent ) + 1;
				if ( held[agent] < 20 ) continue;
				agent.SetAgentPosition( agent.CurrentLinkTraversal.Value.LinkExitPosition );
				agent.CompleteLinkTraversal();
				held[agent] = 0;
			}
		}
		foreach ( var agent in agents ) Assert.IsTrue( agent.AgentPosition.z > 150, $"An agent stopped at {agent.AgentPosition}" );
	}
}
