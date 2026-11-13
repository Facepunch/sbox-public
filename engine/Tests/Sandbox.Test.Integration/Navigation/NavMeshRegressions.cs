using Sandbox.Navigation;
using Sandbox.Volumes;
using System;
using System.Collections.Generic;

namespace NavigationTests;

[TestClass]
public class NavMeshRegressions
{
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task CustomLinkCompletionKeepsTheSuppliedAgentPosition( bool handBackPositionControl )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		Floor( scene, Vector3.Zero );
		Floor( scene, new Vector3( 0, 0, 200 ) );
		var link = scene.CreateObject().Components.Create<NavMeshLink>( false );
		link.LocalStartPosition = Vector3.Zero;
		link.LocalEndPosition = new Vector3( 0, 0, 200 );
		link.Enabled = true;
		scene.NavMesh.CustomBounds = true;
		scene.NavMesh.Bounds = BBox.FromPositionAndSize( new Vector3( 0, 0, 100 ), new Vector3( 800, 800, 400 ) );
		scene.NavMesh.UpdateCache( scene.PhysicsWorld );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var go = scene.CreateObject();
		var agent = go.Components.Create<NavMeshAgent>();
		agent.AutoTraverseLinks = false;
		agent.UpdatePosition = false;
		int entries = 0, exits = 0;
		agent.LinkEnter = () => entries++;
		agent.LinkExit = () => exits++;
		agent.MoveTo( new Vector3( 150, 0, 200 ) );
		for ( int i = 0; i < 100 && !agent.IsTraversingLink; i++ ) scene.GameTick();
		Assert.IsTrue( agent.IsTraversingLink );
		Assert.AreSame( link, agent.CurrentLinkTraversal.Value.LinkComponent );
		var landing = agent.CurrentLinkTraversal.Value.LinkExitPosition + new Vector3( 80, 0, 0 );
		go.WorldPosition = handBackPositionControl ? landing : landing + new Vector3( 0, 25, 0 );
		var bodyPosition = go.WorldPosition;
		agent.SetAgentPosition( landing );
		agent.UpdatePosition = handBackPositionControl;
		agent.CompleteLinkTraversal();
		Assert.AreEqual( landing, agent.AgentPosition );
		Assert.AreEqual( bodyPosition, go.WorldPosition );
		scene.GameTick();
		Assert.IsTrue( agent.AgentPosition.x >= landing.x, "Resuming walking must not pull the agent back to the endpoint" );
		Assert.IsFalse( agent.IsTraversingLink );
		Assert.AreEqual( 1, entries );
		Assert.AreEqual( 1, exits );
		for ( int i = 0; i < 100; i++ )
		{
			scene.GameTick();
			Assert.IsTrue( go.WorldPosition.x >= landing.x, "Handing position control back must not pull the body to the endpoint" );
		}
		Assert.IsTrue( agent.AgentPosition.x > 145 );
		if ( handBackPositionControl ) Assert.IsTrue( go.WorldPosition.x > 145 );
		else Assert.AreEqual( bodyPosition, go.WorldPosition );
		Assert.AreEqual( 1, entries );
		Assert.AreEqual( 1, exits );
	}

	[TestMethod]
	public async Task PendingRetargetKeepsTheActivePathSnapshotConsistent()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		Floor( scene, Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( -100, 0, 0 );
		var agent = go.Components.Create<NavMeshAgent>();
		var firstTarget = new Vector3( 100, 0, 0 );
		agent.MoveTo( firstTarget );
		scene.GameTick();
		var active = agent.GetPath();
		agent.MoveTo( new Vector3( 0, 100, 0 ) );
		var pending = agent.GetPath();
		Assert.IsTrue( pending.IsValid );
		Assert.AreEqual( firstTarget, pending.RequestedTarget );
		Assert.AreEqual( active.Points[^1].Position, pending.Points[^1].Position );
		agent.SetPath( pending );
		Assert.AreEqual( (Vector3?)firstTarget, agent.TargetPosition );
	}

	[TestMethod]
	[DataRow( false, false )]
	[DataRow( true, false )]
	[DataRow( false, true )]
	[DataRow( true, true )]
	public async Task RuntimeParentTransformsKeepTheAgentPath_11811( bool updatePosition, bool updateRotation )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		Floor( scene, Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var parent = scene.CreateObject();
		var go = scene.CreateObject();
		go.Parent = parent;
		go.WorldPosition = new Vector3( -100, 0, 0 );
		var agent = go.Components.Create<NavMeshAgent>();
		agent.UpdatePosition = updatePosition;
		agent.UpdateRotation = updateRotation;
		agent.MoveTo( new Vector3( 100, 0, 0 ) );
		scene.GameTick();
		var position = agent.AgentPosition;
		var path = agent.GetPath().Polygons;
		parent.WorldPosition += new Vector3( 50, 100, 200 );
		parent.WorldRotation = Rotation.FromYaw( 90 );
		parent.WorldScale = new Vector3( 2 );
		Assert.AreEqual( position, agent.AgentPosition );
		Assert.IsTrue( agent.IsNavigating );
		CollectionAssert.AreEqual( path, agent.GetPath().Polygons );
	}

	[TestMethod]
	public async Task EditorTransformsStillRepositionTheAgent()
	{
		var scene = Scene.CreateEditorScene();
		using var scope = scene.Push();
		Floor( scene, Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var go = scene.CreateObject();
		var agent = go.Components.Create<NavMeshAgent>();
		go.WorldPosition = new Vector3( 80, 40, 0 );
		Assert.AreEqual( go.WorldPosition, agent.AgentPosition );
	}

	[TestMethod]
	public async Task RuntimeBodyTransformsAreIndependentOfSimulation_11811()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		Floor( scene, Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( -100, 0, 0 );
		var agent = go.Components.Create<NavMeshAgent>();
		agent.UpdatePosition = agent.UpdateRotation = false;
		agent.MoveTo( new Vector3( 100, 0, 0 ) );
		scene.GameTick();
		var position = agent.AgentPosition;
		var path = agent.GetPath().Polygons;
		go.WorldPosition = new Vector3( 0, 100, 500 );
		go.WorldRotation = Rotation.FromYaw( 90 );
		go.WorldScale = new Vector3( 2 );
		Assert.AreEqual( position, agent.AgentPosition );
		Assert.IsTrue( agent.IsNavigating );
		CollectionAssert.AreEqual( path, agent.GetPath().Polygons );
		var body = go.WorldTransform;
		for ( int tick = 0; tick < 80; tick++ ) scene.GameTick();
		Assert.IsTrue( agent.AgentPosition.x > 90 );
		Assert.AreEqual( body, go.WorldTransform );
		var teleport = new Vector3( -80, 40, 0 );
		agent.SetAgentPosition( teleport );
		Assert.AreEqual( teleport, agent.AgentPosition );
		Assert.AreEqual( body, go.WorldTransform );
	}

	[TestMethod]
	public async Task RotatingAndScalingBodyKeepsAgentPath_11811()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		Floor( scene, Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( -100, 0, 0 );
		var agent = go.Components.Create<NavMeshAgent>();
		agent.UpdateRotation = false;
		agent.MoveTo( new Vector3( 100, 0, 0 ) );
		for ( int tick = 0; tick < 80; tick++ )
		{
			scene.GameTick();
			if ( agent.AgentPosition.x > 90 ) break;
			Assert.IsTrue( agent.IsNavigating );
			var position = agent.AgentPosition;
			var path = agent.GetPath().Polygons;
			go.WorldRotation = Rotation.FromYaw( tick * 31 );
			go.WorldScale = new Vector3( tick % 2 == 0 ? 1.1f : 1 );
			Assert.AreEqual( position, agent.AgentPosition, "Visual transform changes must not pull the simulated position backwards" );
			Assert.IsTrue( agent.IsNavigating, "Rotation/scale changes must not drop the route" );
			CollectionAssert.AreEqual( path, agent.GetPath().Polygons );
		}
		Assert.IsTrue( agent.AgentPosition.x > 90, "Facing updates must not slow the agent's progress" );
	}

	[TestMethod]
	public async Task AssigningAPartialPathPreservesItsRequestedTarget()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		Floor( scene, Vector3.Zero );
		Floor( scene, new Vector3( 0, 0, 200 ) );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var agent = scene.CreateObject().Components.Create<NavMeshAgent>();
		var target = new Vector3( 100, 0, 200 );
		var path = scene.NavMesh.CalculatePath( new() { Start = Vector3.Zero, Target = target, Agent = agent } );
		Assert.AreEqual( NavMeshPathStatus.Partial, path.Status );
		agent.SetPath( path );
		Assert.AreEqual( (Vector3?)target, agent.TargetPosition );
		agent.SetPath( agent.GetPath() );
		Assert.AreEqual( (Vector3?)target, agent.TargetPosition );
	}

	[TestMethod, TestCategory( Allocations.Category )]
	public async Task PathResultsRemainIndependentOfReusedBuffers_11074()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		Floor( scene, Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var agent = scene.CreateObject().Components.Create<NavMeshAgent>();
		var request = new CalculatePathRequest { Start = Vector3.Zero, Target = new Vector3( 100, 100, 0 ), Agent = agent };
		var path = scene.NavMesh.CalculatePath( request );
		Assert.IsTrue( path.IsValid );
		var points = path.Points.ToArray();
		var polygons = path.Polygons.ToArray();
		for ( int i = 0; i < 8; i++ ) agent.SetPath( path );
		var saved = agent.GetPath();
		var savedPoints = saved.Points.ToArray();
		Allocations.AssertSettles( "64 repeated SetPath calls", () =>
		{
			for ( int i = 0; i < 64; i++ ) agent.SetPath( path );
		}, allowedPerPass: 256 );
		request.Target = new Vector3( -100, -100, 0 );
		for ( int i = 0; i < 16; i++ ) Assert.IsTrue( scene.NavMesh.CalculatePath( request ).IsValid );
		Assert.IsTrue( (await scene.NavMesh.CalculatePathAsync( request )).IsValid );
		agent.MoveTo( request.Target );
		for ( int i = 0; i < 20; i++ ) scene.GameTick();
		CollectionAssert.AreEqual( points, path.Points.ToArray() );
		CollectionAssert.AreEqual( polygons, path.Polygons );
		CollectionAssert.AreEqual( savedPoints, saved.Points.ToArray() );
		CollectionAssert.AreEqual( polygons, saved.Polygons );
	}

	[TestMethod, TestCategory( Allocations.Category )]
	public void ReusedFiltersStillCapturePermissionAndCostChanges_11074()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var area = new Sandbox.Engine.Resources.NavMeshAreaDefinition();
		area.RegisterWeakResourceId( $"tests/navmesh-filter-{Guid.NewGuid():N}.navarea" );
		Game.Resources.Register( area );
		try
		{
			scene.NavMesh.UpdateAreaIds();
			int id = scene.NavMesh.AreaDefinitionToId( area );
			Assert.AreNotEqual( NavMesh.DefaultArea, id );
			var agent = scene.CreateObject().Components.Create<NavMeshAgent>();
			var original = scene.NavMesh.CreateFilter( agent );
			area.CostMultiplier = 7;
			agent.ForbiddenAreas.Add( area );
			var restricted = scene.NavMesh.CreateFilter( agent );
			Assert.IsFalse( Allows( restricted, id ) );
			Assert.AreEqual( 7f, restricted.Costs[id] );
			Assert.IsTrue( Allows( original, id ) );
			Assert.AreEqual( 1f, original.Costs[id] );
			var unrestricted = scene.NavMesh.CreateFilter();
			Assert.AreEqual( 7f, unrestricted.Costs[id] );
			Allocations.AssertSettles( "64 pairs of unchanged filters", () =>
			{
				for ( int i = 0; i < 64; i++ ) { scene.NavMesh.CreateFilter( agent ); scene.NavMesh.CreateFilter(); }
			}, allowedPerPass: 256 );
			agent.ForbiddenAreas.Clear();
			area.CostMultiplier = 3;
			Assert.IsTrue( Allows( scene.NavMesh.CreateFilter( agent ), id ) );
			Assert.AreEqual( 3f, scene.NavMesh.CreateFilter().Costs[id] );
			Assert.AreEqual( 7f, unrestricted.Costs[id] );
			Assert.IsFalse( Allows( restricted, id ) );
		}
		finally { Game.Resources.Unregister( area ); }

		static bool Allows( Nav.NavFilter filter, int area ) => (filter.Forbidden >> area & 1) == 0;
	}

	sealed class NavigationEditorScene : Scene
	{
		public NavigationEditorScene() : base( true ) { }
	}

	static GameObject Floor( Scene scene, Vector3 center )
	{
		var go = scene.CreateObject();
		go.WorldPosition = center - Vector3.Up * 16;
		var collider = go.Components.Create<BoxCollider>();
		collider.Scale = new Vector3( 400, 400, 32 );
		return go;
	}

	[TestMethod]
	public async Task TerrainGeneratesInEditorAfterRebuild_11802()
	{
		var scene = new NavigationEditorScene();
		using var scope = scene.Push();
		var storage = new TerrainStorage();
		storage.SetResolution( 64 );
		storage.TerrainSize = 640;
		storage.TerrainHeight = 256;
		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 200, 30 );
		var terrain = go.Components.Create<Terrain>( false );
		terrain.Storage = storage;
		terrain.Enabled = true;
		var transform = go.WorldTransform;
		terrain.EnableCollision = false;
		terrain.EnableCollision = true;
		scene.GameTick();
		Assert.IsTrue( go.WorldPosition.AlmostEqual( transform.Position ) );
		Assert.IsTrue( terrain.PhysicsBody.Transform.Rotation.AlmostEqual( terrain.GetTargetTransform().Rotation ) );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		Assert.IsNotNull( scene.NavMesh.GetClosestPoint( new Vector3( 400, 500, 30 ), 40 ) );
	}

	[TestMethod]
	public async Task SceneReplacementDropsOldTiles_11025()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		Floor( scene, Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var oldPath = scene.NavMesh.CalculatePath( new() { Start = new Vector3( -100, 0, 0 ), Target = new Vector3( 100, 0, 0 ) } );
		Assert.IsTrue( oldPath.IsValid );
		var replacement = new Scene();
		using ( replacement.Push() ) Floor( replacement, new Vector3( 3000, 0, 0 ) );
		var file = replacement.CreateSceneFile();
		Assert.IsTrue( scene.Load( file ) );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		Assert.IsNull( scene.NavMesh.GetClosestPoint( Vector3.Zero, 100 ) );
		Assert.IsNotNull( scene.NavMesh.GetClosestPoint( new Vector3( 3000, 0, 0 ), 100 ) );
	}

	[TestMethod]
	public async Task ResetWithoutNavigationInvalidatesSurvivingAgents_11025()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		Floor( scene, Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var agent = scene.CreateObject().Components.Create<NavMeshAgent>();
		agent.MoveTo( new Vector3( 100, 0, 0 ) );
		scene.GameTick();
		Assert.IsTrue( agent.IsNavigating );
		scene.NavMesh.Reset();
		scene.NavMesh.Deserialize( null );
		Assert.IsFalse( scene.NavMesh.IsEnabled );
		Assert.IsFalse( agent.IsNavigating );
		Assert.IsNull( scene.NavMesh.GetClosestPoint( Vector3.Zero ) );
	}

	[TestMethod]
	public async Task EmptyRegenerationRemovesPreviouslyWalkableTile_11025()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var floor = Floor( scene, new Vector3( 300, 300, 0 ) );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		Assert.IsNotNull( scene.NavMesh.GetClosestPoint( new Vector3( 300, 300, 0 ), 40 ) );
		floor.Destroy();
		scene.ProcessDeletes();
		await scene.NavMesh.GenerateTile( scene.PhysicsWorld, new Vector3( 300, 300, 0 ) );
		Assert.IsNull( scene.NavMesh.GetClosestPoint( new Vector3( 300, 300, 0 ), 40 ) );
	}

	[TestMethod]
	public async Task NewFloorUnderANonBlockingAreaNeedsATileRequest_10587()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.NavMesh.CustomBounds = true;
		scene.NavMesh.Bounds = new BBox( new Vector3( 0, 0, -100 ), new Vector3( 900, 900, 500 ) );
		Floor( scene, new Vector3( 250, 250, 0 ) );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		Floor( scene, new Vector3( 650, 650, 200 ) );
		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 650, 650, 200 );
		var area = go.Components.Create<NavMeshArea>( false );
		area.IsBlocker = false;
		area.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Box, Box = BBox.FromPositionAndSize( Vector3.Zero, new Vector3( 450, 450, 100 ) ) };
		area.Enabled = true;
		// An area marks the navmesh that is there. Geometry that is new to it has to be asked for
		scene.NavMesh.RequestTilesGeneration( BBox.FromPositionAndSize( new Vector3( 650, 650, 200 ), 450 ) );
		for ( int i = 0; i < 200; i++ )
		{
			MainThread.RunQueues();
			scene.NavMesh.UpdateCache( scene.PhysicsWorld );
			if ( scene.NavMesh.GetClosestPoint( new Vector3( 650, 650, 200 ), 40 ).HasValue ) return;
			await Task.Delay( 10 );
		}
		Assert.Fail( "A tile request must build the new floor under the area" );
	}

	[TestMethod]
	public async Task AsyncQueriesHonorCancellation()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		Floor( scene, Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var request = new CalculatePathRequest
		{
			Start = new Vector3( -100, -100, 0 ),
			Target = new Vector3( 100, 100, 0 ),
		};
		var paths = await Task.WhenAll( Enumerable.Range( 0, 16 ).Select( _ => scene.NavMesh.CalculatePathAsync( request ) ) );
		Assert.IsTrue( paths.All( path => path.Status == NavMeshPathStatus.Complete ) );
		using var cancellation = new System.Threading.CancellationTokenSource();
		cancellation.Cancel();
		try
		{
			await scene.NavMesh.CalculatePathAsync( request, cancellation.Token );
			Assert.Fail( "A cancelled request must not return a path" );
		}
		catch ( OperationCanceledException ) { }
	}

	[TestMethod]
	public async Task AirborneComponentRecoversAfterLanding_10230()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		Floor( scene, Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( -100, 0, 1000 );
		var agent = go.Components.Create<NavMeshAgent>();
		agent.MoveTo( new Vector3( 100, 0, 0 ) );
		scene.GameTick();
		Assert.IsTrue( agent.AgentPosition.z > 900 );
		// An external controller explicitly places the simulated agent after landing.
		go.WorldPosition = new Vector3( -100, 0, 0 );
		agent.SetAgentPosition( go.WorldPosition );
		for ( int i = 0; i < 100; i++ ) scene.GameTick();
		Assert.IsTrue( agent.AgentPosition.Distance( new Vector3( 100, 0, 4 ) ) < 10 );
	}

	[TestMethod]
	public async Task CalculatedPathUsesMeshPlacementRadius_10594()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		Floor( scene, Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 196, 0, 0 );
		var agent = go.Components.Create<NavMeshAgent>();
		agent.Radius = 6;
		var path = scene.NavMesh.CalculatePath( new() { Start = agent.AgentPosition, Target = Vector3.Zero, Agent = agent } );
		Assert.IsTrue( path.IsValid );
		agent.SetPath( path );
		Assert.IsTrue( agent.IsNavigating );
		Assert.IsTrue( agent.GetPath().IsValid );
		var asyncPath = await scene.NavMesh.CalculatePathAsync( new() { Start = agent.AgentPosition, Target = Vector3.Zero, Agent = agent } );
		Assert.AreEqual( path.Status, asyncPath.Status );
	}

	/// <summary>
	/// Neighbour avoidance and overlap separation must not shove an agent through a wall.
	/// The navmesh is eroded by AgentRadius, so an agent clamped to the boundary ends up
	/// flush with the wall; anything past that is body inside geometry.
	/// </summary>
	[TestMethod]
	public async Task CrowdingAgainstAWallNeverPushesAgentsIntoIt()
	{
		const float wallFace = 180;

		var scene = new Scene();
		using var sceneScope = scene.Push();

		var floor = scene.CreateObject();
		floor.WorldPosition = new Vector3( 0, 0, -32 );
		floor.Components.Create<BoxCollider>().Scale = new Vector3( 1400, 1400, 64 );

		var wall = scene.CreateObject();
		wall.WorldPosition = new Vector3( 200, 0, 64 );
		wall.Components.Create<BoxCollider>().Scale = new Vector3( 40, 800, 128 );

		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ), "navmesh should generate" );

		var agents = new List<NavMeshAgent>();
		for ( int i = 0; i < 8; i++ )
		{
			var go = scene.CreateObject();
			go.WorldPosition = new Vector3( 100 + i % 2 * 30, -300 + i / 2 * 40, 0 );
			agents.Add( go.Components.Create<NavMeshAgent>() );
		}
		// One agent walks the other way so the group is squeezed against the wall.
		var opposing = scene.CreateObject();
		opposing.WorldPosition = new Vector3( 150, 300, 0 );
		agents.Add( opposing.Components.Create<NavMeshAgent>() );

		for ( int i = 0; i < agents.Count - 1; i++ ) agents[i].MoveTo( new Vector3( 150, 300, 0 ) );
		agents[^1].MoveTo( new Vector3( 150, -300, 0 ) );

		float deepest = float.MinValue;
		for ( int tick = 0; tick < 400; tick++ )
		{
			scene.GameTick();
			foreach ( var agent in agents ) deepest = MathF.Max( deepest, agent.AgentPosition.x );
		}

		float flush = wallFace - agents[0].Radius;
		Assert.IsTrue( deepest <= flush + 0.5f, $"An agent was pushed {deepest - flush:0.0} units into the wall (reached x={deepest:0.0}, flush at {flush:0})" );
	}
}
