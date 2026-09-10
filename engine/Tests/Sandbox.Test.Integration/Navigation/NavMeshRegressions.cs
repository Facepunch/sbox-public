using Sandbox.Navigation;
using Sandbox.Volumes;
using System;

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
		var path = agent.agentInternal.Path.ToArray();
		parent.WorldPosition += new Vector3( 50, 100, 200 );
		parent.WorldRotation = Rotation.FromYaw( 90 );
		parent.WorldScale = new Vector3( 2 );
		Assert.AreEqual( position, agent.AgentPosition );
		Assert.IsTrue( agent.IsNavigating );
		CollectionAssert.AreEqual( path, agent.agentInternal.Path.ToArray() );
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
		var path = agent.agentInternal.Path.ToArray();
		go.WorldPosition = new Vector3( 0, 100, 500 );
		go.WorldRotation = Rotation.FromYaw( 90 );
		go.WorldScale = new Vector3( 2 );
		Assert.AreEqual( position, agent.AgentPosition );
		Assert.IsTrue( agent.IsNavigating );
		CollectionAssert.AreEqual( path, agent.agentInternal.Path.ToArray() );
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
			var path = agent.agentInternal.Path.ToArray();
			go.WorldRotation = Rotation.FromYaw( tick * 31 );
			go.WorldScale = new Vector3( tick % 2 == 0 ? 1.1f : 1 );
			Assert.AreEqual( position, agent.AgentPosition, "Visual transform changes must not pull the simulated position backwards" );
			Assert.IsTrue( agent.IsNavigating, "Rotation/scale changes must not drop the route" );
			CollectionAssert.AreEqual( path, agent.agentInternal.Path.ToArray() );
		}
		Assert.IsTrue( agent.AgentPosition.x > 90, "Facing updates must not slow the agent's progress" );
	}

	[TestMethod]
	public void CachedHeightfieldReadsSurviveConcurrentRetirement()
	{
		using var field = new Sandbox.Navigation.Generation.Heightfield( 8, 8, Vector3.Zero, new Vector3( 8, 64, 8 ), 1, 1 );
		for ( int z = 0; z < 8; z++ )
			for ( int x = 0; x < 8; x++ ) field.AddOrMergeSpan( x, z, 0, 1, 1, 0 );
		using var compact = field.BuildCompactHeightfield( 8, 4 );
		using var tile = new NavMeshTile();
		tile.SetCachedHeightField( compact );
		byte[] payload = tile.CopyCompressedHeightField();
		System.Threading.Tasks.Parallel.Invoke(
			() => { for ( int i = 0; i < 500; i++ ) { tile.Dispose(); tile.SetCompressedHeightField( payload ); } },
			() => { for ( int i = 0; i < 500; i++ ) { using var copy = tile.DecompressCachedHeightField(); if ( copy is not null ) Assert.AreEqual( compact.SpanCount, copy.SpanCount ); } },
			() => { for ( int i = 0; i < 500; i++ ) { var copy = tile.CopyCompressedHeightField(); Assert.IsTrue( copy.Length == 0 || copy.SequenceEqual( payload ) ); } } );
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

	[TestMethod]
	public void ConcurrentTileBuildsKeepTheirOwnLinks()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		using var field = new Sandbox.Navigation.Generation.Heightfield( 64, 64, Vector3.Zero, new Vector3( 640, 512, 640 ), 10, 1 );
		for ( int z = 0; z < 64; z++ )
			for ( int x = 0; x < 64; x++ )
			{
				field.AddOrMergeSpan( x, z, 0, 1, 1, 0 );
				field.AddOrMergeSpan( x, z, 200, 201, 1, 0 );
			}
		using var compact = field.BuildCompactHeightfield( 32, 8 );
		using var tile = new NavMeshTile();
		for ( int i = 0; i < 8; i++ )
			tile.AddSpatialData( new NavMeshLinkData { StartPosition = new Vector3( 100 + i * 50, 320, 1 ), EndPosition = new Vector3( 100 + i * 50, 320, 201 ), ConnectionRadius = 24, UserData = i } );
		var config = new Sandbox.Navigation.Generation.Config { CellSize = 10, CellHeight = 1, WalkableHeight = 32, WalkableRadius = 8, WalkableClimb = 8, MaxSimplificationError = 1, MaxEdgeLen = 8, MaxVertsPerPoly = 6 };
		Parallel.For( 0, 32, _ =>
		{
			var data = tile.BuildNavmesh( compact, config, scene.NavMesh );
			Assert.AreEqual( 8, data.offMeshCons.Length );
			Assert.AreEqual( 8, data.offMeshCons.Select( link => link.userData ).Distinct().Count() );
		} );
	}

	[TestMethod]
	public async Task IncrementalBuildRequestsWaitForTheCurrentBuild()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.NavMesh.IsEnabled = true;
		using var field = new Sandbox.Navigation.Generation.Heightfield( 8, 8, Vector3.Zero, new Vector3( 8, 64, 8 ), 1, 1 );
		field.AddOrMergeSpan( 0, 0, 0, 1, 1, 0 );
		using var compact = field.BuildCompactHeightfield( 8, 4 );
		using var cache = new NavMeshTileCache();
		var tile = cache.GetOrAddTile( Vector2Int.Zero );
		tile.SetCachedHeightField( compact );
		tile.DispatchNavmeshBuild( scene.NavMesh );
		tile.RequestNavmeshBuild();
		cache.Update( scene.NavMesh, scene.PhysicsWorld );
		Assert.IsTrue( tile.IsNavmeshBuildRequested, "The next request must remain queued while this tile is busy" );
		for ( int i = 0; i < 200 && tile.IsNavmeshBuildInProgress; i++ ) { await Task.Delay( 10 ); MainThread.RunQueues(); }
		Assert.IsFalse( tile.IsNavmeshBuildInProgress );
		Assert.IsFalse( tile.DispatchHeightFieldBuild( scene.NavMesh, scene.PhysicsWorld ) );
		Assert.IsFalse( tile.IsHeightFieldValid, "An empty rebuild must discard the previous cached geometry" );
	}

	[TestMethod]
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
		long before = GC.GetAllocatedBytesForCurrentThread();
		for ( int i = 0; i < 64; i++ ) agent.SetPath( path );
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.IsTrue( allocated <= 256, $"Repeated SetPath allocated {allocated} bytes after warmup" );
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

	[TestMethod]
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
			Assert.AreNotEqual( Sandbox.Navigation.Generation.Constants.WALKABLE_AREA, id );
			var agent = scene.CreateObject().Components.Create<NavMeshAgent>();
			var original = scene.NavMesh.CreateFilter( agent );
			area.CostMultiplier = 7;
			agent.ForbiddenAreas.Add( area );
			var restricted = scene.NavMesh.CreateFilter( agent );
			Assert.IsFalse( restricted.Allows( id ) );
			Assert.AreEqual( 7f, restricted.CostMultiplier( id ) );
			Assert.IsTrue( original.Allows( id ) );
			Assert.AreEqual( 1f, original.CostMultiplier( id ) );
			var unrestricted = scene.NavMesh.CreateFilter();
			Assert.AreEqual( 7f, unrestricted.CostMultiplier( id ) );
			for ( int i = 0; i < 8; i++ ) { scene.NavMesh.CreateFilter( agent ); scene.NavMesh.CreateFilter(); }
			long before = GC.GetAllocatedBytesForCurrentThread();
			for ( int i = 0; i < 64; i++ ) { scene.NavMesh.CreateFilter( agent ); scene.NavMesh.CreateFilter(); }
			long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
			Assert.IsTrue( allocated <= 256, $"Unchanged filters allocated {allocated} bytes after warmup" );
			agent.ForbiddenAreas.Clear();
			area.CostMultiplier = 3;
			Assert.IsTrue( scene.NavMesh.CreateFilter( agent ).Allows( id ) );
			Assert.AreEqual( 3f, scene.NavMesh.CreateFilter().CostMultiplier( id ) );
			Assert.AreEqual( 7f, unrestricted.CostMultiplier( id ) );
			Assert.IsFalse( restricted.Allows( id ) );
		}
		finally { Game.Resources.Unregister( area ); }
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
	public async Task NonBlockingAreaRegeneratesNewFloorGeometry_10587()
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
		for ( int i = 0; i < 200; i++ )
		{
			MainThread.RunQueues();
			scene.NavMesh.UpdateCache( scene.PhysicsWorld );
			if ( scene.NavMesh.GetClosestPoint( new Vector3( 650, 650, 200 ), 40 ).HasValue ) return;
			await Task.Delay( 10 );
		}
		Assert.Fail( "Adding a nonblocking area must update the cached geometry without SetDirty" );
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
}
