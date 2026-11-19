using Sandbox;
using Sandbox.Navigation;
namespace NavigationTests;

[TestClass]
public class MeshBuildingTest
{
	[TestMethod]
	public void Generator_FromPhysicsShape_Simple()
	{
		// A physics world to generate the navmesh from, with a box that is inside one tile
		var world = new PhysicsWorld();
		var body = new PhysicsBody( world );
		body.AddBoxShape( BBox.FromPositionAndSize( new Vector3( 300, 300, 0 ), 200 ), Rotation.Identity );

		using var navMesh = new NavMesh { IsEnabled = true };
		navMesh.mesh.MarkDirty( 0, 0 );
		var jobs = new Nav.TileJob[1];
		Assert.AreEqual( 1, navMesh.mesh.TakeJobs( jobs ) );

		// The box as physics triangulates it
		navMesh.CollectGeometry( world, jobs[0], out _, out _ );
		Assert.AreEqual( 8, navMesh.geometryVertices.Count );
		Assert.AreEqual( 36, navMesh.geometryIndices.Count );

		// Of its twelve triangles the two on top face up and are ground
		new Nav.NavBuilder().Build( jobs[0] );
		Assert.AreEqual( Nav.TileJobStatus.Built, jobs[0].Status );
		navMesh.mesh.Commit( jobs );
		var stats = navMesh.mesh.GetStats();
		Assert.AreEqual( 2, stats.TrianglesUsed );
		Assert.AreEqual( 10, stats.TrianglesRejected );

		var tile = new Vector2Int( 0, 0 );
		var polyCount = navMesh.GetPolyCount( tile );
		Assert.IsTrue( polyCount > 0, "The top of the box should be walkable" );
		for ( int i = 0; i < polyCount; i++ )
		{
			foreach ( var vert in navMesh.GetPolyVerts( tile, i ) )
			{
				Assert.IsTrue( vert.z >= 100 && vert.z <= 108, $"The navmesh should be on top of the box, a vertex is at {vert}" );
			}
		}

		world.Delete();
	}

	[TestMethod]
	public async Task Generator_GenerateTile_HighLevel()
	{
		using var navMesh = new NavMesh();
		var world = new PhysicsWorld();

		var boxSize = BBox.FromPositionAndSize( 0, 100 );
		var body = new PhysicsBody( world );
		body.AddBoxShape( boxSize, Rotation.Identity );

		navMesh.IsEnabled = true;
		navMesh.Init();

		// Use the tile that contains our box geometry
		var testTilePosition = navMesh.WorldPositionToTilePosition( Vector3.Zero );
		var testTileWorldPosition = navMesh.TilePositionToWorldPosition( testTilePosition );

		var generateTask = navMesh.GenerateTile( world, testTileWorldPosition );
		await generateTask;

		world.Delete();

		boxSize = boxSize.Grow( 5 );

		// An empty tile would make the loop below pass without asserting anything
		var polyCount = navMesh.GetPolyCount( testTilePosition );
		Assert.IsTrue( polyCount > 0, "Generated tile should contain at least one poly" );

		for ( int i = 0; i < polyCount; i++ )
		{
			foreach ( var vert in navMesh.GetPolyVerts( testTilePosition, i ) )
			{
				Assert.IsTrue( boxSize.Contains( vert ) );
			}
		}
	}

	[TestMethod]
	public async Task Query_RandomPoint()
	{
		var navMesh = new NavMesh();
		var world = new PhysicsWorld();

		var boxSize = BBox.FromPositionAndSize( 0, 500 );
		var body = new PhysicsBody( world );
		body.AddBoxShape( boxSize, Rotation.Identity );

		var generatedTask = navMesh.Generate( world );
		var generated = await generatedTask;
		Assert.IsTrue( generated );

		world.Delete();

		var p = navMesh.GetRandomPoint();
		Assert.IsTrue( p.HasValue );

		navMesh.Dispose();
	}

	[TestMethod]
	public async Task Query_ClosestPoint()
	{
		var navMesh = new NavMesh();
		var world = new PhysicsWorld();

		var boxSize = BBox.FromPositionAndSize( 0, 200 );
		var body = new PhysicsBody( world );
		body.AddBoxShape( boxSize, Rotation.Identity );

		var generatedTask = navMesh.Generate( world );
		var generated = await generatedTask;
		Assert.IsTrue( generated );

		world.Delete();

		var p = navMesh.GetClosestPoint( new Vector3( 100, 100, 100 ) );
		Assert.IsTrue( p.HasValue );

		navMesh.Dispose();
	}


	[TestMethod]
	public async Task Query_ClosestEdge()
	{
		var navMesh = new NavMesh();
		var world = new PhysicsWorld();

		var boxSize = BBox.FromPositionAndSize( 0, 500 );
		var body = new PhysicsBody( world );
		body.AddBoxShape( boxSize, Rotation.Identity );

		var generatedTask = navMesh.Generate( world );
		var generated = await generatedTask;
		Assert.IsTrue( generated );

		world.Delete();

		var p = navMesh.GetClosestEdge( new Vector3( 100, 100, 100 ) );
		Assert.IsTrue( p.HasValue );

		navMesh.Dispose();
	}

	[TestMethod]
	public async Task Query_Path()
	{
		var navMesh = new NavMesh();
		var world = new PhysicsWorld();

		var boxSize = BBox.FromPositionAndSize( 0, 500 );
		var body = new PhysicsBody( world );
		body.AddBoxShape( boxSize, Rotation.Identity );

		var generatedTask = navMesh.Generate( world );
		var generated = await generatedTask;
		Assert.IsTrue( generated );

		world.Delete();


		var pathResult = navMesh.CalculatePath( new CalculatePathRequest { Start = new Vector3( 200, 200, 250 ), Target = new Vector3( -200, -200, 250 ) } );
		Assert.IsTrue( pathResult.IsValid() );
		Assert.AreNotEqual( 0, pathResult.Points.Count );

		navMesh.Dispose();
	}

	[TestMethod]
	public async Task Bake_Roundtrip_PreservesNavMesh()
	{
		// Generate a navmesh
		var sourceNavMesh = new NavMesh();
		var world = new PhysicsWorld();

		var boxSize = BBox.FromPositionAndSize( 0, 500 );
		var body = new PhysicsBody( world );
		body.AddBoxShape( boxSize, Rotation.Identity );

		var generated = await sourceNavMesh.Generate( world );
		Assert.IsTrue( generated, "Failed to generate source navmesh" );

		// Capture some data from source for comparison
		var sourceRandomPoint = sourceNavMesh.GetRandomPoint();
		Assert.IsTrue( sourceRandomPoint.HasValue, "Source navmesh should have valid points" );

		// Bake the heightfield data to bytes (in-memory, no file I/O)
		var bakedData = await sourceNavMesh.BakeDataToBytes();
		Assert.IsNotNull( bakedData, "Baked data should not be null" );
		Assert.IsTrue( bakedData.Length > 0, "Baked data should have content" );

		// Drop the source geometry NOW: regenerating against the live world would fall
		// back to collecting world geometry, hiding a completely broken bake/load path.
		// With an empty world, a working mesh can only come from the baked heightfields.
		world.Delete();
		var emptyWorld = new PhysicsWorld();

		// Create new navmesh and load from baked data directly
		var loadedNavMesh = new NavMesh();
		loadedNavMesh.IsEnabled = true;
		loadedNavMesh.Init();
		await loadedNavMesh.LoadFromBakedData( bakedData );

		// Generate() derives Bounds from the world - with the empty world that's a zero-size
		// box and no tile would be processed. Pin the source's generation bounds instead.
		loadedNavMesh.CustomBounds = true;
		loadedNavMesh.Bounds = sourceNavMesh.Bounds;

		// Generate always builds from live geometry, only a scene load reads the bake
		var loadedGenerated = await loadedNavMesh.Load( emptyWorld );
		Assert.IsTrue( loadedGenerated, "Failed to generate navmesh from loaded baked data" );

		// Verify the loaded navmesh is functional
		var loadedRandomPoint = loadedNavMesh.GetRandomPoint();
		Assert.IsTrue( loadedRandomPoint.HasValue, "Loaded navmesh should have valid points" );

		// Test path finding on loaded navmesh
		var pathResult = loadedNavMesh.CalculatePath( new CalculatePathRequest
		{
			Start = new Vector3( 200, 200, 250 ),
			Target = new Vector3( -200, -200, 250 )
		} );
		Assert.IsTrue( pathResult.IsValid(), "Path should be valid on loaded navmesh" );
		Assert.AreNotEqual( 0, pathResult.Points.Count, "Path should have points" );

		emptyWorld.Delete();

		sourceNavMesh.Dispose();
		loadedNavMesh.Dispose();
	}

	[TestMethod]
	public async Task BakeKeepsDeliberatelyEmptyTilesEmpty_11853()
	{
		// Tiles are 1024 units across, so these two boxes land in different tiles.
		var world = new PhysicsWorld();
		var keptCentre = Vector3.Zero;
		var droppedCentre = new Vector3( 3000, 0, 0 );
		var kept = new PhysicsBody( world );
		kept.AddBoxShape( BBox.FromPositionAndSize( keptCentre, 500 ), Rotation.Identity );
		var dropped = new PhysicsBody( world );
		dropped.AddBoxShape( BBox.FromPositionAndSize( droppedCentre, 500 ), Rotation.Identity );

		var source = new NavMesh { CustomBounds = true, Bounds = BBox.FromPositionAndSize( new Vector3( 1500, 0, 0 ), new Vector3( 5000, 2000, 2000 ) ) };
		Assert.IsTrue( await source.Generate( world ), "Failed to generate source navmesh" );
		Assert.IsNotNull( source.GetClosestPoint( droppedCentre, 400 ), "The region to exclude should start out walkable" );

		// Exclude a region the way a project baking around water would.
		source.UnloadTiles( BBox.FromPositionAndSize( droppedCentre, 600 ) );
		Assert.IsNull( source.GetClosestPoint( droppedCentre, 400 ), "Unloading should have removed the excluded region" );

		var baked = await source.BakeDataToBytes();
		Assert.IsNotNull( baked, "Baked data should not be null" );

		// Load into a navmesh facing the same world. The excluded geometry is still
		// there, so only baked coverage can keep it out of the result.
		var loaded = new NavMesh { CustomBounds = true, Bounds = source.Bounds, IsEnabled = true };
		loaded.Init();
		await loaded.LoadFromBakedData( baked );
		// Generate always builds from live geometry, only a scene load reads the bake
		Assert.IsTrue( await loaded.Load( world ), "Failed to generate navmesh from loaded baked data" );

		Assert.IsNotNull( loaded.GetClosestPoint( keptCentre, 400 ), "The baked region should be walkable" );
		Assert.IsNull( loaded.GetClosestPoint( droppedCentre, 400 ), "A tile the bake left empty must not be regenerated from live geometry" );

		source.Dispose();
		loaded.Dispose();
		world.Delete();
	}
}
