using Sandbox.Navigation;
using Sandbox.Volumes;
using System;

namespace NavigationTests;

/// <summary>
/// Covers the navigation scene components - NavMeshAgent, NavMeshArea and NavMeshLink:
/// default property state, serialization round-trips, registration with the scene's
/// navmesh/crowd, and agent locomotion across a navmesh generated from a flat floor.
/// </summary>
[TestClass]
public class NavMeshComponentTest
{
	/// <summary>
	/// Creates a large flat keyframed floor collider whose top surface sits at z = 0,
	/// spanning roughly [-350, 350] on x and y. The floor straddles the navmesh tile
	/// origin so tile generation covers more than a single tile.
	/// </summary>
	static GameObject CreateFloor( Scene scene )
	{
		var go = scene.CreateObject();
		go.Name = "floor";
		go.WorldPosition = new Vector3( 0, 0, -32 );

		var collider = go.Components.Create<BoxCollider>();
		collider.Scale = new Vector3( 700, 700, 64 );

		return go;
	}

	/// <summary>
	/// Serializes a GameObject to JSON, gives it fresh ids and deserializes it into a
	/// new enabled GameObject in the active scene - the standard round-trip idiom.
	/// </summary>
	static GameObject RoundTrip( GameObject go )
	{
		var jsonObject = Json.ParseToJsonObject( go.Serialize().ToJsonString() );
		Assert.IsNotNull( jsonObject );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var copy = new GameObject( false );
		copy.Deserialize( jsonObject );
		copy.Enabled = true;

		return copy;
	}

	/// <summary>
	/// Without an initialized navmesh there is no crowd, so a freshly created agent has
	/// no internal crowd agent. It exposes its documented defaults, falls back to the
	/// transform for positional queries and treats every navigation call as a safe no-op.
	/// </summary>
	[TestMethod]
	public void AgentDefaultsWithoutNavmeshAreSafe()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 10, 20, 30 );
		var agent = go.Components.Create<NavMeshAgent>();

		Assert.IsNull( agent.agentInternal, "no crowd exists, so no crowd agent should have been created" );

		// Default property surface
		Assert.AreEqual( 64f, agent.Height );
		Assert.AreEqual( 16f, agent.Radius );
		Assert.AreEqual( 120f, agent.MaxSpeed );
		Assert.AreEqual( 120f, agent.Acceleration );
		Assert.AreEqual( 0.25f, agent.Separation );
		Assert.IsTrue( agent.UpdatePosition );
		Assert.IsFalse( agent.UpdateRotation );
		Assert.IsTrue( agent.AllowDefaultArea );
		Assert.IsTrue( agent.AutoTraverseLinks );
		Assert.AreEqual( 0, agent.AllowedAreas.Count );
		Assert.AreEqual( 0, agent.ForbiddenAreas.Count );

		// Without an internal agent, positional state falls back to the transform
		Assert.AreEqual( go.WorldPosition, agent.AgentPosition );
		Assert.AreEqual( Vector3.Zero, agent.Velocity );
		Assert.AreEqual( Vector3.Zero, agent.WishVelocity );
		Assert.IsFalse( agent.IsNavigating );
		Assert.IsNull( agent.TargetPosition );
		Assert.IsFalse( agent.IsTraversingLink );
		Assert.AreEqual( go.WorldPosition, agent.GetLookAhead( 100f ) );

		// All of these are no-ops without a crowd agent
		agent.MoveTo( new Vector3( 500, 0, 0 ) );
		agent.SetAgentPosition( new Vector3( 1, 2, 3 ) );
		agent.Stop();
		agent.CompleteLinkTraversal();

		Assert.IsFalse( agent.IsNavigating );

		var path = agent.GetPath();
		Assert.AreEqual( NavMeshPathStatus.PathNotFound, path.Status );
		Assert.IsFalse( path.IsValid );
	}

	/// <summary>
	/// All [Property] members of NavMeshAgent survive a JSON serialize/deserialize
	/// round-trip of the owning GameObject.
	/// </summary>
	[TestMethod]
	public void AgentSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var agent = go.Components.Create<NavMeshAgent>();
		agent.Height = 72f;
		agent.Radius = 24f;
		agent.MaxSpeed = 250f;
		agent.Acceleration = 500f;
		agent.Separation = 0.75f;
		agent.UpdatePosition = false;
		agent.UpdateRotation = true;
		agent.AllowDefaultArea = false;
		agent.AutoTraverseLinks = false;

		var copy = RoundTrip( go );

		var restored = copy.GetComponent<NavMeshAgent>();
		Assert.IsNotNull( restored, "round-tripped GameObject should have a NavMeshAgent" );
		Assert.AreEqual( 72f, restored.Height );
		Assert.AreEqual( 24f, restored.Radius );
		Assert.AreEqual( 250f, restored.MaxSpeed );
		Assert.AreEqual( 500f, restored.Acceleration );
		Assert.AreEqual( 0.75f, restored.Separation );
		Assert.IsFalse( restored.UpdatePosition );
		Assert.IsTrue( restored.UpdateRotation );
		Assert.IsFalse( restored.AllowDefaultArea );
		Assert.IsFalse( restored.AutoTraverseLinks );
	}

	/// <summary>
	/// Agents register with the crowd when the scene navmesh initializes (via OnInit for
	/// agents that already exist, immediately for agents created afterwards), unregister
	/// on disable/destroy, and property setters push updated parameters into the live
	/// crowd agent.
	/// </summary>
	[TestMethod]
	public void AgentCrowdRegistrationAndParameterSync()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var agent = go.Components.Create<NavMeshAgent>();

		Assert.IsNull( scene.NavMesh.crowd, "crowd should not exist before the navmesh is enabled" );
		Assert.IsNull( agent.agentInternal );

		// Enabling the navmesh initializes the crowd and registers existing agents via OnInit
		scene.NavMesh.IsEnabled = true;

		Assert.IsNotNull( scene.NavMesh.crowd );
		Assert.IsNotNull( agent.agentInternal, "existing agent should register when the navmesh initializes" );
		Assert.AreEqual( 1, scene.NavMesh.crowd.GetActiveAgents().Count );

		// Agents created after init register immediately
		var go2 = scene.CreateObject();
		var agent2 = go2.Components.Create<NavMeshAgent>();
		Assert.IsNotNull( agent2.agentInternal );
		Assert.AreEqual( 2, scene.NavMesh.crowd.GetActiveAgents().Count );

		// Property setters push the new parameters into the live crowd agent
		agent.MaxSpeed = 240f;
		agent.Radius = 32f;
		agent.Height = 80f;
		agent.Acceleration = 480f;
		agent.Separation = 0.5f;
		agent.AutoTraverseLinks = false;

		var option = agent.agentInternal.option;
		Assert.AreEqual( 240f, option.maxSpeed );
		Assert.AreEqual( 32f, option.radius );
		Assert.AreEqual( 32f * 16f, option.collisionQueryRange );
		Assert.AreEqual( 80f, option.height );
		Assert.AreEqual( 480f, option.maxAcceleration );
		Assert.AreEqual( 0.5f * 12f, option.separationWeight, 0.001f );
		Assert.IsFalse( option.autoTraverseOffMeshLink );

		// Disabling removes the agent from the crowd, re-enabling adds it back
		agent.Enabled = false;
		Assert.IsNull( agent.agentInternal );
		Assert.AreEqual( 1, scene.NavMesh.crowd.GetActiveAgents().Count );

		agent.Enabled = true;
		Assert.IsNotNull( agent.agentInternal );
		Assert.AreEqual( 2, scene.NavMesh.crowd.GetActiveAgents().Count );

		// Destroying the GameObject also unregisters its agent
		go2.Destroy();
		scene.ProcessDeletes();
		Assert.AreEqual( 1, scene.NavMesh.crowd.GetActiveAgents().Count );
	}

	/// <summary>
	/// On a navmesh generated over a flat floor, MoveTo makes the agent walk towards the
	/// target over game ticks: it reports IsNavigating, picks up a nonzero velocity, the
	/// component drives the GameObject transform along, and after arriving it settles
	/// near the target and bleeds its velocity back off.
	/// </summary>
	[TestMethod]
	public async Task AgentMoveToWalksAcrossFloor()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );

		var generated = await scene.NavMesh.Generate( scene.PhysicsWorld );
		Assert.IsTrue( generated, "navmesh should generate from the floor collider" );

		var startOnMesh = scene.NavMesh.GetClosestPoint( new Vector3( -200, 0, 0 ) );
		var targetOnMesh = scene.NavMesh.GetClosestPoint( new Vector3( 200, 0, 0 ) );
		Assert.IsTrue( startOnMesh.HasValue, "start position should be on the navmesh" );
		Assert.IsTrue( targetOnMesh.HasValue, "target position should be on the navmesh" );

		var go = scene.CreateObject();
		go.Name = "agent";
		go.WorldPosition = startOnMesh.Value;
		var agent = go.Components.Create<NavMeshAgent>();
		Assert.IsNotNull( agent.agentInternal, "agent should register with the initialized crowd" );

		var target = targetOnMesh.Value;
		agent.MoveTo( target );

		var startDistance = agent.AgentPosition.Distance( target );
		var maxObservedSpeed = 0f;
		var wasNavigating = false;

		for ( int i = 0; i < 150; i++ )
		{
			scene.GameTick();

			maxObservedSpeed = MathF.Max( maxObservedSpeed, agent.Velocity.Length );
			wasNavigating |= agent.IsNavigating;

			if ( agent.AgentPosition.Distance( target ) < 25f )
				break;
		}

		Assert.IsTrue( wasNavigating, "agent should report IsNavigating while moving" );
		Assert.IsTrue( maxObservedSpeed > 10f, $"agent should have picked up speed, max was {maxObservedSpeed}" );
		Assert.IsTrue( agent.AgentPosition.Distance( target ) < 25f, $"agent should have arrived, was {agent.AgentPosition.Distance( target )} away" );
		Assert.IsTrue( agent.AgentPosition.Distance( target ) < startDistance, "agent should be closer to the target than where it started" );

		// UpdatePosition drives the GameObject transform towards the agent position
		Assert.IsTrue( go.WorldPosition.Distance( target ) < 100f, $"GameObject should follow the agent, was {go.WorldPosition.Distance( target )} away" );

		// Once arrived the agent settles - it stays at the target and slows back down
		for ( int i = 0; i < 40; i++ )
		{
			scene.GameTick();
		}

		Assert.IsTrue( agent.AgentPosition.Distance( target ) < 60f, "agent should stay near the target after arrival" );
		Assert.IsTrue( agent.Velocity.Length < 30f, $"agent should have slowed down after arrival, velocity was {agent.Velocity.Length}" );
	}

	/// <summary>
	/// Path plumbing between the navmesh and the agent is deterministic without any
	/// ticking: CalculatePath produces a complete path across the floor, SetPath makes
	/// the agent navigate it immediately, GetPath reports the corridor back out, and
	/// Stop clears the move target again.
	/// </summary>
	[TestMethod]
	public async Task AgentPathQueriesAndStop()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );

		var generated = await scene.NavMesh.Generate( scene.PhysicsWorld );
		Assert.IsTrue( generated, "navmesh should generate from the floor collider" );

		var startOnMesh = scene.NavMesh.GetClosestPoint( new Vector3( -250, -250, 0 ) );
		var targetOnMesh = scene.NavMesh.GetClosestPoint( new Vector3( 250, 250, 0 ) );
		Assert.IsTrue( startOnMesh.HasValue );
		Assert.IsTrue( targetOnMesh.HasValue );

		var start = startOnMesh.Value;
		var target = targetOnMesh.Value;

		var go = scene.CreateObject();
		go.WorldPosition = start;
		var agent = go.Components.Create<NavMeshAgent>();
		Assert.IsNotNull( agent.agentInternal );

		// Calculate a path using the agent's filter and feed it back via SetPath
		var path = scene.NavMesh.CalculatePath( new CalculatePathRequest { Start = start, Target = target, Agent = agent } );
		Assert.IsTrue( path.IsValid );
		Assert.AreEqual( NavMeshPathStatus.Complete, path.Status );
		Assert.IsTrue( path.Points.Count >= 2, "path across the floor should have at least start and end points" );
		Assert.IsTrue( path.Points[0].Position.Distance( start ) < 64f );
		Assert.IsTrue( path.Points[path.Points.Count - 1].Position.Distance( target ) < 64f );

		agent.SetPath( path );

		Assert.IsTrue( agent.IsNavigating, "SetPath should immediately make the agent navigate" );
		Assert.IsTrue( agent.TargetPosition.HasValue );
		Assert.IsTrue( agent.TargetPosition.Value.Distance( target ) < 64f );

		// The agent reports its current corridor back out via GetPath
		var agentPath = agent.GetPath();
		Assert.IsTrue( agentPath.IsValid );
		Assert.AreEqual( NavMeshPathStatus.Complete, agentPath.Status );
		Assert.IsTrue( agentPath.Points.Count >= 2 );
		Assert.IsTrue( agentPath.Points[0].Position.Distance( start ) < 64f );
		Assert.IsTrue( agentPath.Points[agentPath.Points.Count - 1].Position.Distance( target ) < 64f );

		// Stop clears the move target
		agent.Stop();
		Assert.IsFalse( agent.IsNavigating );
		Assert.IsNull( agent.TargetPosition );
		Assert.AreEqual( NavMeshPathStatus.PathNotFound, agent.GetPath().Status );
	}

	/// <summary>
	/// NavMeshLink defaults and property state: local positions map to world space
	/// through the owning transform and back, the navmesh-snapped positions are null
	/// while nothing is generated, and all properties survive a serialization round-trip.
	/// </summary>
	[TestMethod]
	public void LinkPropertyStateAndSerialization()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );
		var link = go.Components.Create<NavMeshLink>();

		// Defaults
		Assert.IsTrue( link.IsBiDirectional );
		Assert.AreEqual( 16f, link.ConnectionRadius );
		Assert.IsNull( link.Area );

		link.LocalStartPosition = new Vector3( 10, 20, 30 );
		link.LocalEndPosition = new Vector3( 40, 50, 60 );

		// World accessors are the local positions transformed by the GameObject
		Assert.IsTrue( link.WorldStartPosition.AlmostEqual( new Vector3( 110, 20, 30 ) ) );
		Assert.IsTrue( link.WorldEndPosition.AlmostEqual( new Vector3( 140, 50, 60 ) ) );

		// Setting a world position maps back into local space
		link.WorldStartPosition = new Vector3( 100, 0, 50 );
		Assert.IsTrue( link.LocalStartPosition.AlmostEqual( new Vector3( 0, 0, 50 ) ) );
		link.LocalStartPosition = new Vector3( 10, 20, 30 );

		// No navmesh has been generated, so the link is not connected to anything
		Assert.IsFalse( link.WorldStartPositionOnNavmesh.HasValue );
		Assert.IsFalse( link.WorldEndPositionOnNavmesh.HasValue );

		link.IsBiDirectional = false;
		link.ConnectionRadius = 48f;

		var copy = RoundTrip( go );

		var restored = copy.GetComponent<NavMeshLink>();
		Assert.IsNotNull( restored, "round-tripped GameObject should have a NavMeshLink" );
		Assert.IsTrue( restored.LocalStartPosition.AlmostEqual( new Vector3( 10, 20, 30 ) ) );
		Assert.IsTrue( restored.LocalEndPosition.AlmostEqual( new Vector3( 40, 50, 60 ) ) );
		Assert.IsFalse( restored.IsBiDirectional );
		Assert.AreEqual( 48f, restored.ConnectionRadius );
	}

	/// <summary>
	/// A NavMeshLink registered before generation gets baked into the generated tile as
	/// an off-mesh connection: both endpoints connect to the navmesh and report their
	/// snapped world positions. Disabling and re-enabling the link resets it to an
	/// unconnected state until the navmesh rebuilds.
	/// </summary>
	[TestMethod]
	public async Task LinkConnectsToGeneratedNavmesh()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );

		var linkGo = scene.CreateObject();
		linkGo.WorldPosition = new Vector3( 50, 50, 0 );
		var link = linkGo.Components.Create<NavMeshLink>( false );
		link.LocalStartPosition = Vector3.Zero;
		link.LocalEndPosition = new Vector3( 200, 0, 0 );
		link.Enabled = true;

		Assert.IsFalse( link.WorldStartPositionOnNavmesh.HasValue, "link should be unconnected before generation" );

		// Give the navmesh fixed bounds so the link's overlapping tiles can be resolved
		// before any tiles exist, then generate - the new tiles pick the link data up.
		scene.NavMesh.CustomBounds = true;
		scene.NavMesh.Bounds = BBox.FromPositionAndSize( 0, new Vector3( 800, 800, 256 ) );
		scene.NavMesh.UpdateCache( scene.PhysicsWorld );

		var generated = await scene.NavMesh.Generate( scene.PhysicsWorld );
		Assert.IsTrue( generated );

		// The off-mesh connection was baked into the tile and both ends connected
		Assert.IsTrue( link.WorldStartPositionOnNavmesh.HasValue, "link start should connect to the navmesh" );
		Assert.IsTrue( link.WorldEndPositionOnNavmesh.HasValue, "link end should connect to the navmesh" );
		Assert.IsTrue( link.WorldStartPositionOnNavmesh.Value.Distance( new Vector3( 50, 50, 0 ) ) < 32f,
			$"snapped start was {link.WorldStartPositionOnNavmesh.Value}" );
		Assert.IsTrue( link.WorldEndPositionOnNavmesh.Value.Distance( new Vector3( 250, 50, 0 ) ) < 32f,
			$"snapped end was {link.WorldEndPositionOnNavmesh.Value}" );

		// Disabling clears the registered link data; re-enabling starts a fresh,
		// unconnected registration until the affected tiles rebuild.
		link.Enabled = false;
		link.Enabled = true;
		Assert.IsFalse( link.WorldStartPositionOnNavmesh.HasValue, "fresh link data should not be connected yet" );
		Assert.IsFalse( link.WorldEndPositionOnNavmesh.HasValue );
	}

	/// <summary>
	/// NavMeshArea defaults to a blocker with no area definition, and its volume and
	/// blocker flag survive a serialization round-trip. Toggling the component with no
	/// navmesh around is safe.
	/// </summary>
	[TestMethod]
	public void AreaPropertyStateAndSerialization()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var area = go.Components.Create<NavMeshArea>();

		// Defaults: a box-shaped blocker with no area definition
		Assert.IsTrue( area.IsBlocker );
		Assert.IsNull( area.Area );
		Assert.AreEqual( SceneVolume.VolumeTypes.Box, area.SceneVolume.Type );

		area.SceneVolume = new SceneVolume
		{
			Type = SceneVolume.VolumeTypes.Box,
			Box = BBox.FromPositionAndSize( new Vector3( 1, 2, 3 ), new Vector3( 10, 20, 30 ) )
		};
		area.IsBlocker = false;

		var copy = RoundTrip( go );

		var restored = copy.GetComponent<NavMeshArea>();
		Assert.IsNotNull( restored, "round-tripped GameObject should have a NavMeshArea" );
		Assert.IsFalse( restored.IsBlocker );
		Assert.IsNull( restored.Area );
		Assert.AreEqual( SceneVolume.VolumeTypes.Box, restored.SceneVolume.Type );
		Assert.IsTrue( restored.SceneVolume.Box.Mins.AlmostEqual( new Vector3( -4, -8, -12 ) ) );
		Assert.IsTrue( restored.SceneVolume.Box.Maxs.AlmostEqual( new Vector3( 6, 12, 18 ) ) );

		// Toggling enabled with no navmesh generated must be safe
		restored.Enabled = false;
		restored.Enabled = true;
	}

	/// <summary>
	/// A blocker NavMeshArea registered before generation punches a hole into the
	/// generated navmesh: queries inside the blocked region find no mesh while the rest
	/// of the floor stays walkable, and a full regeneration keeps both properties.
	/// </summary>
	[TestMethod]
	public async Task AreaBlocksNavmeshGeneration()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );

		// A blocker volume over the (+x, +y) corner of the floor
		var areaGo = scene.CreateObject();
		areaGo.WorldPosition = new Vector3( 200, 200, 0 );
		var area = areaGo.Components.Create<NavMeshArea>( false );
		area.SceneVolume = new SceneVolume
		{
			Type = SceneVolume.VolumeTypes.Box,
			Box = BBox.FromPositionAndSize( 0, new Vector3( 240, 240, 200 ) )
		};
		area.Enabled = true;
		Assert.IsTrue( area.IsBlocker, "areas should block by default" );

		// Give the navmesh fixed bounds so the area's overlapping tiles can be resolved
		// before any tiles exist, then generate - the new tiles pick the area data up.
		scene.NavMesh.CustomBounds = true;
		scene.NavMesh.Bounds = BBox.FromPositionAndSize( 0, new Vector3( 800, 800, 256 ) );
		scene.NavMesh.UpdateCache( scene.PhysicsWorld );

		var generated = await scene.NavMesh.Generate( scene.PhysicsWorld );
		Assert.IsTrue( generated );

		// The floor outside the area is walkable, the blocked region has no navmesh
		Assert.IsTrue( scene.NavMesh.GetClosestPoint( new Vector3( -200, -200, 0 ), 48f ).HasValue,
			"floor outside the blocker should be on the navmesh" );
		Assert.IsFalse( scene.NavMesh.GetClosestPoint( new Vector3( 200, 200, 0 ), 48f ).HasValue,
			"blocked region should have no navmesh" );

		// A full regeneration with the area still active keeps the hole
		var regenerated = await scene.NavMesh.Generate( scene.PhysicsWorld );
		Assert.IsTrue( regenerated );

		Assert.IsTrue( scene.NavMesh.GetClosestPoint( new Vector3( -200, -200, 0 ), 48f ).HasValue,
			"floor outside the blocker should survive regeneration" );
		Assert.IsFalse( scene.NavMesh.GetClosestPoint( new Vector3( 200, 200, 0 ), 48f ).HasValue,
			"blocked region should stay blocked after regeneration" );
	}
}
