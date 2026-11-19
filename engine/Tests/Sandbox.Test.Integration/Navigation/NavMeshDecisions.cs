using Sandbox.Navigation;
using Sandbox.Volumes;
using System;
using System.Collections.Generic;

namespace NavigationTests;

/// <summary>
/// Navigation behaviour that is easy to get wrong: blockers keep an agent radius away, one snap rule for
/// path ends and agents, what a bake holds and when it is ignored, areas that rebuild from what was kept,
/// and link callbacks.
/// </summary>
[TestClass]
public class NavMeshDecisions
{
	[TestMethod]
	public async Task BlockersKeepAgentsOneAgentRadiusAway()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 700 );
		scene.Area( Vector3.Zero, new Vector3( 100, 100, 200 ) );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );

		// The blocker ends at x = 50. The navmesh starts a radius of 16 further out, as it does at a wall.
		var edge = scene.NavMesh.GetClosestPoint( new Vector3( 50, 0, 0 ), 60 );
		Assert.IsTrue( edge.HasValue );
		Assert.IsTrue( edge.Value.x >= 62 && edge.Value.x <= 74, $"The navmesh starts at x = {edge.Value.x}" );
	}

	[TestMethod]
	public async Task AreaChangesRebuildFromTheKeptFieldNotFromGeometry()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var floor = scene.Floor( Vector3.Zero, 700 );
		var area = scene.Area( new Vector3( -250, -250, 0 ), new Vector3( 100, 100, 200 ) );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		Assert.IsFalse( scene.NavMesh.OnMesh( new Vector3( -250, -250, 0 ) ) );
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( -120, -120, 0 ) ) );

		// With the geometry gone, the hole can only move if the tile is built from what was kept of it
		scene.Remove( floor );
		area.WorldPosition = new Vector3( -120, -120, 0 );
		await scene.Settle();

		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( -250, -250, 0 ) ), "The old hole should have closed" );
		Assert.IsFalse( scene.NavMesh.OnMesh( new Vector3( -120, -120, 0 ) ), "The blocker should have cut a hole where it is now" );
	}

	[TestMethod]
	public async Task AnAreaChangeDoesNotReadNewGeometry()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.NavMesh.CustomBounds = true;
		scene.NavMesh.Bounds = new BBox( new Vector3( 0, 0, -100 ), new Vector3( 900, 900, 500 ) );
		scene.Floor( new Vector3( 250, 250, 0 ) );
		var area = scene.Area( new Vector3( 650, 650, 200 ), new Vector3( 450, 450, 100 ), blocker: false );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );

		var added = new Vector3( 650, 650, 200 );
		scene.Floor( added );

		area.WorldPosition = added + new Vector3( 10, 0, 0 );
		await scene.Settle();
		Assert.IsFalse( scene.NavMesh.OnMesh( added, 40 ), "An area change alone must not read new geometry" );

		scene.NavMesh.RequestTilesGeneration( BBox.FromPositionAndSize( added, 450 ) );
		await scene.Settle();
		Assert.IsTrue( scene.NavMesh.OnMesh( added, 40 ), "Asking for the tiles builds them from the geometry of the moment" );
	}

	[TestMethod]
	public async Task AnInfiniteBlockerLeavesNoNavMesh()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		var area = scene.Area( Vector3.Zero, new SceneVolume { Type = SceneVolume.VolumeTypes.Infinite }, blocker: false );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		Assert.IsTrue( scene.NavMesh.OnMesh( Vector3.Zero ) );

		area.IsBlocker = true;
		await scene.Settle();
		Assert.IsNull( scene.NavMesh.GetRandomPoint() );
	}

	[TestMethod]
	public async Task PathEndsSnapFromUpTo256UnitsAway()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );

		// The navmesh ends near x = 184
		var near = new Vector3( 400, 0, 0 );
		var far = new Vector3( 460, 0, 0 );
		Assert.AreEqual( NavMeshPathStatus.Complete, scene.NavMesh.CalculatePath( new() { Start = near, Target = Vector3.Zero } ).Status );
		Assert.AreEqual( NavMeshPathStatus.Complete, scene.NavMesh.CalculatePath( new() { Start = Vector3.Zero, Target = near } ).Status );
		Assert.AreEqual( NavMeshPathStatus.StartNotFound, scene.NavMesh.CalculatePath( new() { Start = far, Target = Vector3.Zero } ).Status );
		Assert.AreEqual( NavMeshPathStatus.TargetNotFound, scene.NavMesh.CalculatePath( new() { Start = Vector3.Zero, Target = far } ).Status );

		// Up and down it is 1.51 agent heights, as placement has always been
		Assert.AreEqual( NavMeshPathStatus.Complete, scene.NavMesh.CalculatePath( new() { Start = new Vector3( 0, 0, 90 ), Target = Vector3.Zero } ).Status );
		Assert.AreEqual( NavMeshPathStatus.StartNotFound, scene.NavMesh.CalculatePath( new() { Start = new Vector3( 0, 0, 120 ), Target = Vector3.Zero } ).Status );
	}

	[TestMethod]
	public async Task AgentsArePlacedFromUpTo256UnitsAway()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );

		var near = scene.Agent( new Vector3( 400, 0, 0 ) );
		var far = scene.Agent( new Vector3( 460, 0, 0 ) );
		near.MoveTo( new Vector3( -100, 0, 0 ) );
		far.MoveTo( new Vector3( -100, 0, 0 ) );
		scene.GameTick();

		Assert.IsTrue( near.AgentPosition.x < 190, $"An agent 216 units off the navmesh should be put on it, was at {near.AgentPosition}" );
		Assert.IsTrue( near.IsNavigating );
		Assert.AreEqual( new Vector3( 460, 0, 0 ), far.AgentPosition, "An agent further away than 256 units stays where it is" );
		Assert.IsFalse( far.IsNavigating );

		for ( int i = 0; i < 100; i++ ) scene.GameTick();
		Assert.IsTrue( near.AgentPosition.Distance( new Vector3( -100, 0, 4 ) ) < 10 );
	}

	[TestMethod]
	public async Task AgentTargetsSnapByTheSameRule()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var agent = scene.Agent();

		// 1.51 agent heights up or down, 256 units sideways
		foreach ( var (target, placed) in new[] { (new Vector3( 100, 0, 90 ), true), (new Vector3( 100, 0, 120 ), false), (new Vector3( 400, 0, 0 ), true), (new Vector3( 460, 0, 0 ), false) } )
		{
			agent.Stop();
			agent.SetAgentPosition( Vector3.Zero );
			agent.MoveTo( target );
			scene.GameTick();
			Assert.AreEqual( placed, agent.IsNavigating, $"Target {target}" );
			Assert.AreEqual( (Vector3?)target, agent.TargetPosition, "A target that cannot be placed is kept" );
		}
	}

	[TestMethod]
	public async Task ATallAgentsTargetSnapsAsFarAsItsPathEnds()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var agent = scene.Agent();
		var tall = scene.Agent( new Vector3( 0, 50, 0 ) );
		tall.Height = 128;
		scene.GameTick();

		// 150 above the floor: more than 1.51 heights of the navmesh's agent, less than 1.51 of the tall one's
		var target = new Vector3( 100, 0, 150 );
		Assert.AreEqual( NavMeshPathStatus.TargetNotFound, scene.NavMesh.CalculatePath( new() { Start = Vector3.Zero, Target = target, Agent = agent } ).Status );
		Assert.AreEqual( NavMeshPathStatus.Complete, scene.NavMesh.CalculatePath( new() { Start = Vector3.Zero, Target = target, Agent = tall } ).Status );
		agent.MoveTo( target );
		tall.MoveTo( target );
		scene.GameTick();
		Assert.IsFalse( agent.IsNavigating );
		Assert.IsTrue( tall.IsNavigating );
	}

	[TestMethod]
	public async Task AnOwnerMovedAgentIsWhereItWasPutUntilTheStepAfterItsTileWasRebuilt()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var driven = scene.Agent( new Vector3( -100, 0, 0 ) );
		driven.MaxSpeed = 0;

		// Navigation updates only: in a game tick the ground trace may move an agent that has no polygon yet
		scene.NavUpdate();
		var put = new Vector3( -50, 0, 30 );
		driven.SetAgentPosition( put );
		scene.NavUpdate();
		Assert.IsTrue( driven.AgentPosition.Distance( new Vector3( -50, 0, 4 ) ) < 1, $"On its polygon the agent is at {driven.AgentPosition}" );

		// The polygon the agent was on is gone with the old tile, and only a step finds the new one
		await scene.NavMesh.GenerateTile( scene.PhysicsWorld, put );
		Assert.AreEqual( put, driven.AgentPosition );
		scene.NavUpdate();
		Assert.IsTrue( driven.AgentPosition.Distance( new Vector3( -50, 0, 4 ) ) < 1, $"After a step the agent is at {driven.AgentPosition}" );
	}

	[TestMethod]
	public async Task AnAgentHeldAtZeroSpeedKeepsItsRoute()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var agent = scene.Agent( new Vector3( -150, 0, 0 ) );
		var target = new Vector3( 150, 0, 0 );
		agent.MoveTo( target );
		for ( int i = 0; i < 25; i++ ) scene.NavUpdate();
		Assert.IsTrue( agent.IsNavigating );

		agent.MaxSpeed = 0;
		scene.NavUpdate();
		var held = agent.AgentPosition;
		for ( int i = 0; i < 50; i++ ) scene.NavUpdate();
		Assert.IsTrue( agent.AgentPosition.Distance( held ) < 0.01f, $"Held at {held}, now at {agent.AgentPosition}" );
		Assert.IsTrue( agent.IsNavigating );
		Assert.AreEqual( target, agent.TargetPosition );

		agent.MaxSpeed = 120;
		for ( int i = 0; i < 500 && agent.IsNavigating; i++ ) scene.NavUpdate();
		Assert.IsTrue( agent.AgentPosition.Distance( target ) < 8, $"The agent stopped at {agent.AgentPosition}" );
	}

	[TestMethod]
	public async Task ABakeHoldsFinishedTiles()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 700 );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var baked = await scene.NavMesh.BakeDataToBytes();
		Assert.IsNotNull( baked );

		// Nothing is generated: the tiles are in the data
		using var loaded = new NavMesh { IsEnabled = true };
		await loaded.LoadFromBakedData( baked );
		Assert.AreEqual( 0, loaded.mesh.PendingTiles );
		Assert.AreEqual( scene.NavMesh.mesh.GetStats().Polygons, loaded.mesh.GetStats().Polygons );
		Assert.AreEqual( 0, loaded.mesh.GetStats().CacheBytes );
		Assert.AreEqual( NavMeshPathStatus.Complete, loaded.CalculatePath( new() { Start = new Vector3( -250, -250, 0 ), Target = new Vector3( 250, 250, 0 ) } ).Status );
	}

	[TestMethod]
	public async Task ALoadedBakeGetsTheLinksOfTheScene()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		scene.Floor( new Vector3( 0, 0, 200 ) );
		var link = scene.Link<NavMeshLink>( new Vector3( 100, 0, 0 ), new Vector3( 100, 0, 200 ) );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var baked = await scene.NavMesh.BakeDataToBytes();
		Assert.IsNotNull( baked );

		// A bake holds no links. The loaded mesh is given the scene's in the next update, without building a tile
		await scene.NavMesh.LoadFromBakedData( baked );
		scene.NavUpdate();
		scene.NavUpdate();
		Assert.AreEqual( 0, scene.NavMesh.mesh.PendingTiles );
		Assert.AreEqual( 0, scene.NavMesh.mesh.GetStats().CacheBytes );
		Assert.IsTrue( link.WorldStartPositionOnNavmesh.HasValue && link.WorldEndPositionOnNavmesh.HasValue );
		var path = scene.NavMesh.CalculatePath( new() { Start = new Vector3( -100, 0, 0 ), Target = new Vector3( -100, 0, 200 ) } );
		Assert.AreEqual( NavMeshPathStatus.Complete, path.Status );
	}

	[TestMethod]
	public async Task ABakeBuildsWhatIsStillPendingFirst()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 2600 );
		scene.NavMesh.IsEnabled = true;
		scene.NavMesh.SetDirty();

		// Nothing is built yet. A tile the bake lacked would stay empty at load
		var baked = await scene.NavMesh.BakeDataToBytes( world: scene.PhysicsWorld );
		Assert.IsNotNull( baked );
		Assert.IsFalse( scene.NavMesh.IsDirty );

		using var loaded = new NavMesh { IsEnabled = true };
		await loaded.LoadFromBakedData( baked );
		foreach ( var corner in new[] { new Vector3( 1200, 1200, 0 ), new Vector3( -1200, 1200, 0 ), new Vector3( 1200, -1200, 0 ), new Vector3( -1200, -1200, 0 ) } )
		{
			Assert.IsTrue( loaded.OnMesh( corner ), $"The bake has no tile at {corner}" );
		}
	}

	[TestMethod]
	public async Task ABakeAfterChangedAgentSettingsSavesTheMeshBuiltForThem()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 700 );
		await scene.NavMesh.Generate( scene.PhysicsWorld );

		scene.NavMesh.AgentRadius = 48;
		scene.NavMesh.SetDirty();
		var baked = await scene.NavMesh.BakeDataToBytes( world: scene.PhysicsWorld );
		Assert.IsNotNull( baked );

		using var loaded = new NavMesh { AgentRadius = 48, IsEnabled = true };
		await loaded.LoadFromBakedData( baked );
		Assert.IsTrue( loaded.OnMesh( Vector3.Zero ), "The bake should hold the mesh of the new radius" );
		Assert.IsFalse( loaded.OnMesh( new Vector3( 330, 0, 0 ), 4 ) );
	}

	[TestMethod]
	public async Task ABakeThatWaitsCanBeCancelled()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		scene.NavMesh.IsEnabled = true;
		scene.NavMesh.EditorAutoUpdate = true;
		scene.NavMesh.SetDirty();

		// Nobody builds the tiles, so only the cancellation ends the wait
		using var cancel = new System.Threading.CancellationTokenSource( 100 );
		Assert.IsNull( await scene.NavMesh.BakeDataToBytes( cancelToken: cancel.Token ) );
		Assert.IsTrue( scene.NavMesh.EditorAutoUpdate, "Auto update is only suspended while baking" );
	}

	[TestMethod]
	public async Task BakesOfTheOldFormatAreIgnoredAndTheSceneGenerates()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );

		// One chunk of four bytes, as files of the old format start
		byte[] old = [1, 0, 0, 0, 4, 0, 0, 0, 31, 139, 8, 0];
		scene.NavMesh.IsEnabled = true;
		await scene.NavMesh.LoadFromBakedData( old );
		Assert.IsNull( scene.NavMesh.GetRandomPoint() );

		Assert.IsTrue( await scene.NavMesh.Load( scene.PhysicsWorld ) );
		Assert.IsTrue( scene.NavMesh.OnMesh( Vector3.Zero ) );
	}

	[TestMethod]
	public async Task ABakeMadeWithOtherAgentSettingsIsIgnored()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var baked = await scene.NavMesh.BakeDataToBytes();
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( 180, 0, 0 ), 4 ) );

		using var wider = new NavMesh { AgentRadius = 32, IsEnabled = true };
		await wider.LoadFromBakedData( baked );
		Assert.IsNull( wider.GetRandomPoint(), "Tiles eroded for another agent must not be loaded" );

		Assert.IsTrue( await wider.Load( scene.PhysicsWorld ) );
		Assert.IsTrue( wider.OnMesh( Vector3.Zero ) );
		Assert.IsFalse( wider.OnMesh( new Vector3( 180, 0, 0 ), 4 ), "The scene should have generated with its own radius" );
	}

	// What the editor's scene folder was given to write, in order
	sealed class WrittenFiles : Editor.SceneFolder
	{
		public readonly List<byte[]> Written = new();

		public override string WriteFile( string filename, byte[] data )
		{
			Written.Add( data );
			return "scene_data" + filename;
		}
	}

	[TestMethod]
	public async Task AnEditorBakeIsEmptyUntilTheNavMeshIsBuiltFromTheWorldAsItIs()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		Assert.IsTrue( scene.NavMesh.OnMesh( Vector3.Zero ) );

		// A crate the navmesh has not seen yet, too low to stand in
		scene.Box( new Vector3( 0, 0, 20 ), new Vector3( 60, 60, 40 ) );

		var folder = new WrittenFiles();
		scene.NavMesh.DrawMesh = true;
		scene.NavMesh.EditorAutoUpdate = true;
		var bake = scene.NavMesh.BakeInEditor( scene.PhysicsWorld, folder, () => true, regenerate: true );
		Assert.AreEqual( 1, folder.Written.Count );
		Assert.AreEqual( 0, folder.Written[0].Length, "Until the bake is done the file makes the scene generate" );
		Assert.IsTrue( scene.NavMesh.Serialize().ContainsKey( "BakedDataPath" ) );

		// The scene is written to disk right now: the bake holds auto update back without touching the setting
		Assert.IsTrue( (bool)scene.NavMesh.Serialize()["EditorAutoUpdate"] );
		Assert.IsFalse( scene.NavMesh.AutoUpdates );

		await bake;
		Assert.IsTrue( scene.NavMesh.AutoUpdates );
		Assert.AreEqual( 2, folder.Written.Count );
		using var loaded = new NavMesh { IsEnabled = true };
		await loaded.LoadFromBakedData( folder.Written[1] );
		Assert.IsTrue( loaded.OnMesh( new Vector3( 150, 150, 0 ) ) );
		Assert.IsFalse( loaded.OnMesh( Vector3.Zero ), "The bake should be of the world as it was when it was asked for" );
	}

	[TestMethod]
	public async Task AnEditorBakeIsNotWrittenForASceneEditedMeanwhileOrBakedAgain()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );

		var folder = new WrittenFiles();
		bool unedited = true;
		var edited = scene.NavMesh.BakeInEditor( scene.PhysicsWorld, folder, () => unedited, regenerate: true );
		unedited = false;
		await edited;
		Assert.AreEqual( 1, folder.Written.Count, "What was built is no longer the scene as it was saved" );

		// Saved twice in a row: only the later bake is written, and auto update comes back once both are over
		unedited = true;
		scene.NavMesh.DrawMesh = true;
		scene.NavMesh.EditorAutoUpdate = true;
		var first = scene.NavMesh.BakeInEditor( scene.PhysicsWorld, folder, () => unedited, regenerate: true );
		var second = scene.NavMesh.BakeInEditor( scene.PhysicsWorld, folder, () => unedited, regenerate: true );
		await first;
		Assert.IsFalse( scene.NavMesh.AutoUpdates );
		Assert.IsTrue( scene.NavMesh.EditorAutoUpdate );
		await second;
		Assert.IsTrue( scene.NavMesh.AutoUpdates );
		Assert.AreEqual( 4, folder.Written.Count );
		Assert.AreEqual( 0, folder.Written[1].Length + folder.Written[2].Length );
		Assert.IsTrue( folder.Written[3].Length > 0 );

		// A scene without a navmesh refers to no bake
		scene.NavMesh.IsEnabled = false;
		await scene.NavMesh.BakeInEditor( scene.PhysicsWorld, folder, () => unedited, regenerate: true );
		Assert.AreEqual( 4, folder.Written.Count );
		Assert.IsFalse( scene.NavMesh.Serialize().ContainsKey( "BakedDataPath" ) );
	}

	[TestMethod]
	public async Task ASceneLoadedWithoutAUsableBakeIsMissingOne()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var baked = await scene.NavMesh.BakeDataToBytes();

		// Never baked: nothing is missing
		scene.NavMesh.Reset();
		Assert.IsTrue( await scene.NavMesh.Load( scene.PhysicsWorld ) );
		Assert.IsFalse( scene.NavMesh.TakeMissingBake() );

		// The scene refers to a baked file, which is in the old format: the scene generates and misses its bake
		scene.NavMesh.Reset();
		ReferToABake( scene.NavMesh );
		await scene.NavMesh.LoadFromBakedData( [1, 0, 0, 0, 4, 0, 0, 0, 31, 139, 8, 0] );
		Assert.IsTrue( await scene.NavMesh.Load( scene.PhysicsWorld ) );
		Assert.IsTrue( scene.NavMesh.OnMesh( Vector3.Zero ), "The scene should have generated" );
		Assert.IsTrue( scene.NavMesh.TakeMissingBake() );
		Assert.IsFalse( scene.NavMesh.TakeMissingBake() );

		scene.NavMesh.Reset();
		ReferToABake( scene.NavMesh );
		await scene.NavMesh.LoadFromBakedData( baked );
		Assert.IsTrue( await scene.NavMesh.Load( scene.PhysicsWorld ) );
		Assert.IsFalse( scene.NavMesh.TakeMissingBake() );
	}

	// As a scene file that refers to a baked file leaves the navmesh
	static void ReferToABake( NavMesh navMesh )
	{
		var json = navMesh.Serialize();
		json["Enabled"] = true;
		json["BakedDataPath"] = new System.Text.Json.Nodes.JsonObject { ["path"] = "navmesh/baked.navdata" };
		navMesh.Deserialize( json );
	}

	[TestMethod]
	public async Task FailedPathsHaveAnEmptyListOfPoints()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var agent = scene.Agent();

		var path = scene.NavMesh.CalculatePath( new() { Start = Vector3.Zero, Target = Vector3.One } );
		Assert.AreEqual( NavMeshPathStatus.StartNotFound, path.Status );
		Assert.IsNotNull( path.Points );
		Assert.AreEqual( 0, path.Points.Count );
		Assert.AreEqual( 0, default( NavMeshPath ).Points.Count );
		Assert.AreEqual( 0, agent.GetPath().Points.Count );
#pragma warning disable CS0618
		Assert.AreEqual( 0, scene.NavMesh.GetSimplePath( Vector3.Zero, Vector3.One ).Count );
#pragma warning restore CS0618

		// A navmesh that is not there answers the same way with and without a task
		Assert.AreEqual( NavMeshPathStatus.StartNotFound, (await scene.NavMesh.CalculatePathAsync( new() { Start = Vector3.Zero, Target = Vector3.One } )).Status );

		scene.Floor( Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		path = scene.NavMesh.CalculatePath( new() { Start = Vector3.Zero, Target = new Vector3( 5000, 0, 0 ) } );
		Assert.AreEqual( NavMeshPathStatus.TargetNotFound, path.Status );
		Assert.AreEqual( 0, path.Points.Count );
	}

	[TestMethod]
	public async Task AnAreaVolumeAssignedFromCodeApplies()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 700 );

		// Enabled with the default shape, a box of 100 around the game object
		var area = scene.CreateObject().Components.Create<NavMeshArea>();
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		Assert.IsFalse( scene.NavMesh.OnMesh( Vector3.Zero ) );

		area.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Box, Box = BBox.FromPositionAndSize( new Vector3( 0, 200, 0 ), 100 ) };
		await scene.Settle();

		Assert.IsTrue( scene.NavMesh.OnMesh( Vector3.Zero ), "The hole of the old shape should have closed" );
		Assert.IsFalse( scene.NavMesh.OnMesh( new Vector3( 0, 200, 0 ) ), "The new shape should block" );
	}

	[TestMethod]
	public async Task LinkCallbacksFireInTheUpdateOfTheTransition()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		scene.Floor( new Vector3( 0, 0, 200 ) );
		var link = scene.CreateObject().Components.Create<CountingNavMeshLink>( false );
		link.LocalStartPosition = new Vector3( 100, 0, 0 );
		link.LocalEndPosition = new Vector3( 100, 0, 200 );
		int linkEntered = 0, linkExited = 0;
		link.LinkEntered = _ => linkEntered++;
		link.LinkExited = _ => linkExited++;
		link.Enabled = true;
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );

		var agent = scene.Agent();
		int entries = 0, exits = 0;
		agent.LinkEnter = () => entries++;
		agent.LinkExit = () => exits++;
		agent.MoveTo( new Vector3( -100, 0, 200 ) );

		bool wasOnLink = false;
		for ( int i = 0; i < 1000 && (!wasOnLink || agent.IsTraversingLink); i++ )
		{
			scene.NavUpdate();

			// What the crowd says and what the component was told agree after every update
			bool onLink = agent.agentInternal.State.Link >= 0;
			Assert.AreEqual( onLink, agent.IsTraversingLink );
			Assert.AreEqual( onLink || wasOnLink ? 1 : 0, entries );
			Assert.AreEqual( wasOnLink && !onLink ? 1 : 0, exits );
			if ( onLink ) Assert.AreSame( link, agent.CurrentLinkTraversal.Value.LinkComponent );
			wasOnLink |= onLink;
		}

		Assert.IsTrue( wasOnLink, "The route should have gone over the link" );
		Assert.AreEqual( (1, 1), (entries, exits) );
		Assert.AreEqual( (1, 1), (linkEntered, linkExited) );
		Assert.AreEqual( (1, 1), (link.Entered, link.Exited) );
	}

	[TestMethod]
	public async Task AThrowingLinkCallbackDoesNotLoseOtherLinkEvents()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		scene.Floor( new Vector3( 0, 0, 200 ) );
		scene.Link<NavMeshLink>( new Vector3( 100, 0, 0 ), new Vector3( 100, 0, 200 ) );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );

		// Both reach the link in the same update, the one that throws first
		var thrower = scene.Agent( new Vector3( 40, 40, 0 ) );
		int throwerExits = 0;
		thrower.LinkEnter = () => throw new InvalidOperationException( "LinkEnter of the test" );
		thrower.LinkExit = () => { throwerExits++; throw new InvalidOperationException( "LinkExit of the test" ); };
		var other = scene.Agent( new Vector3( 40, -40, 0 ) );
		int entries = 0, exits = 0;
		other.LinkEnter = () => entries++;
		other.LinkExit = () => exits++;
		thrower.MoveTo( new Vector3( -100, 40, 200 ) );
		other.MoveTo( new Vector3( -100, -40, 200 ) );

		for ( int i = 0; i < 2000 && (exits == 0 || throwerExits == 0); i++ ) scene.NavUpdate();

		Assert.AreEqual( 1, throwerExits, "An agent whose enter handler threw still leaves its link" );
		Assert.AreEqual( (1, 1), (entries, exits) );
		Assert.IsFalse( thrower.IsTraversingLink );
		Assert.IsFalse( other.IsTraversingLink );
	}

	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public async Task LinksBackToBackEachRaiseEnterAndExit( bool autoTraverse )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		scene.Floor( new Vector3( 0, 0, 200 ) );
		scene.Floor( new Vector3( 0, 0, 400 ) );
		// The second link starts where the first one ends
		var first = scene.Link<NavMeshLink>( new Vector3( 100, 0, 0 ), new Vector3( 100, 0, 200 ) );
		var second = scene.Link<NavMeshLink>( new Vector3( 100, 0, 200 ), new Vector3( 100, 0, 400 ) );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );

		var agent = scene.Agent();
		agent.AutoTraverseLinks = autoTraverse;
		var entered = new List<NavMeshLink>();
		int exits = 0;
		agent.LinkEnter = () => entered.Add( agent.CurrentLinkTraversal.Value.LinkComponent );
		agent.LinkExit = () => exits++;
		var target = new Vector3( -100, 0, 400 );
		agent.MoveTo( target );

		bool Arrived() => agent.AgentPosition.z > 300 && agent.AgentPosition.WithZ( 0 ).Distance( target.WithZ( 0 ) ) < 8;
		for ( int i = 0; i < 3000 && !Arrived(); i++ )
		{
			scene.NavUpdate();
			if ( autoTraverse || !agent.IsTraversingLink ) continue;
			agent.SetAgentPosition( agent.CurrentLinkTraversal.Value.LinkExitPosition );
			agent.CompleteLinkTraversal();
		}

		Assert.IsTrue( Arrived(), $"The agent stopped at {agent.AgentPosition}" );
		Assert.AreEqual( 2, entered.Count );
		Assert.AreSame( first, entered[0] );
		Assert.AreSame( second, entered[1] );
		Assert.AreEqual( 2, exits );
	}

	[TestMethod]
	public async Task ALinkTakenAgainRightAfterItWasCompletedRaisesEnterAgain()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		scene.Floor( new Vector3( 0, 0, 200 ) );
		scene.Link<NavMeshLink>( new Vector3( 100, 0, 0 ), new Vector3( 100, 0, 200 ) );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );

		var agent = scene.Agent();
		agent.AutoTraverseLinks = false;
		int entries = 0, exits = 0;
		agent.LinkEnter = () => entries++;
		agent.LinkExit = () => exits++;
		agent.MoveTo( new Vector3( -100, 0, 200 ) );
		for ( int i = 0; i < 1000 && !agent.IsTraversingLink; i++ ) scene.NavUpdate();
		Assert.IsTrue( agent.IsTraversingLink );
		var top = agent.CurrentLinkTraversal.Value.LinkExitPosition;

		// Sent back down while it stands on the upper end: the way back starts with the same link
		agent.SetAgentPosition( top );
		agent.MoveTo( new Vector3( -100, 0, 0 ) );
		agent.CompleteLinkTraversal();
		for ( int i = 0; i < 10 && entries < 2; i++ ) scene.NavUpdate();

		Assert.AreEqual( (2, 1), (entries, exits) );
		Assert.IsTrue( agent.IsTraversingLink );
		Assert.AreEqual( top, agent.CurrentLinkTraversal.Value.LinkEnterPosition );
	}

	[TestMethod]
	public async Task AnAgentAtTheEndOfARouteThatStopsShortIsNotNavigating()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		scene.Floor( new Vector3( 0, 0, 200 ) );
		await scene.NavMesh.Generate( scene.PhysicsWorld );

		var agent = scene.Agent();
		var target = new Vector3( 100, 0, 200 );
		agent.MoveTo( target );
		scene.GameTick();
		Assert.IsTrue( agent.IsNavigating );

		for ( int i = 0; i < 100; i++ ) scene.GameTick();
		Assert.IsFalse( agent.IsNavigating, "Nothing more to walk: the agent is as close as it gets" );
		Assert.AreEqual( (Vector3?)target, agent.TargetPosition, "It still wants to go there" );
		Assert.IsTrue( agent.AgentPosition.Distance( new Vector3( 100, 0, 4 ) ) < 10, $"It should wait under the target, was at {agent.AgentPosition}" );
		Assert.AreEqual( NavMeshPathStatus.Partial, agent.GetPath().Status );
	}

	[TestMethod]
	public async Task AnAgentJoinsAPathThatStartsElsewhere()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 2600 );
		await scene.NavMesh.Generate( scene.PhysicsWorld );

		// From one corner tile to the opposite one, while the agent stands in a third
		var target = new Vector3( -1100, -1100, 0 );
		var path = scene.NavMesh.CalculatePath( new() { Start = new Vector3( 1100, 1100, 0 ), Target = target } );
		Assert.AreEqual( NavMeshPathStatus.Complete, path.Status );

		var agent = scene.Agent( new Vector3( -1100, 1100, 0 ) );
		agent.SetPath( path );
		Assert.IsTrue( agent.IsNavigating, "The agent should have joined the path" );
		Assert.AreEqual( (Vector3?)target, agent.TargetPosition );
	}

	[TestMethod]
	public async Task ClosestEdgeIsNullWhenNoEdgeIsInRange()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 700 );
		await scene.NavMesh.Generate( scene.PhysicsWorld );

		Assert.IsNull( scene.NavMesh.GetClosestEdge( Vector3.Zero, 100 ) );
		var edge = scene.NavMesh.GetClosestEdge( new Vector3( 300, 0, 0 ), 100 );
		Assert.IsTrue( edge.HasValue );
		Assert.IsTrue( edge.Value.Distance( new Vector3( 334, 0, 4 ) ) < 6, $"The edge was {edge.Value}" );
	}

	[TestMethod]
	public async Task BoundsGrowByTwoCellsAndDoNotClampBeforeTheyAreKnown()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( new Vector3( 3000, 0, 0 ) );

		// No bounds yet: a request far from the origin is not cut down to the origin tile
		scene.NavMesh.IsEnabled = true;
		scene.NavMesh.RequestTilesGeneration( BBox.FromPositionAndSize( new Vector3( 3000, 0, 0 ), 600 ) );
		await scene.Settle();
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( 3000, 100, 0 ) ) );
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( 3000, -100, 0 ) ) );

		// The floor is 400 by 400 by 32 with its top at 0
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		Assert.IsTrue( scene.NavMesh.Bounds.Mins.AlmostEqual( new Vector3( 2792, -208, -40 ), 1 ), $"The bounds start at {scene.NavMesh.Bounds.Mins}" );
		Assert.IsTrue( scene.NavMesh.Bounds.Maxs.AlmostEqual( new Vector3( 3208, 208, 8 ), 1 ), $"The bounds end at {scene.NavMesh.Bounds.Maxs}" );
	}

	[TestMethod]
	public async Task LinkFieldsChangedAfterEnablingApply()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		scene.Floor( new Vector3( 0, 0, 200 ) );
		var link = scene.Link<NavMeshLink>( Vector3.Zero, new Vector3( 0, 0, 200 ) );
		await scene.NavMesh.Generate( scene.PhysicsWorld );

		var up = new CalculatePathRequest { Start = new Vector3( 100, 0, 0 ), Target = new Vector3( 100, 0, 200 ) };
		var down = new CalculatePathRequest { Start = up.Target, Target = up.Start };
		Assert.AreEqual( NavMeshPathStatus.Complete, scene.NavMesh.CalculatePath( up ).Status );
		Assert.AreEqual( NavMeshPathStatus.Complete, scene.NavMesh.CalculatePath( down ).Status );

		link.IsBiDirectional = false;
		scene.NavUpdate();
		Assert.AreEqual( NavMeshPathStatus.Complete, scene.NavMesh.CalculatePath( up ).Status );
		Assert.AreEqual( NavMeshPathStatus.Partial, scene.NavMesh.CalculatePath( down ).Status, "A one way link only leads from its start to its end" );

		// Out of reach of the navmesh: the link lets go
		link.LocalStartPosition = new Vector3( 0, 0, 60 );
		scene.NavUpdate();
		Assert.IsNull( link.WorldStartPositionOnNavmesh );
		Assert.AreEqual( NavMeshPathStatus.Partial, scene.NavMesh.CalculatePath( up ).Status );

		link.ConnectionRadius = 80;
		scene.NavUpdate();
		Assert.IsNotNull( link.WorldStartPositionOnNavmesh );
		Assert.AreEqual( NavMeshPathStatus.Complete, scene.NavMesh.CalculatePath( up ).Status );
	}
}

public sealed class CountingNavMeshLink : NavMeshLink
{
	public int Entered, Exited;

	protected override void OnLinkEntered( NavMeshAgent agent ) => Entered++;
	protected override void OnLinkExited( NavMeshAgent agent ) => Exited++;
}
