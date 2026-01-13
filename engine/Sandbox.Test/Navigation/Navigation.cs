using Sandbox;
using Sandbox.Navigation;
using Sandbox.Navigation.Generation;
namespace Navigation;

[TestClass]
public class MeshBuilding
{
	private static readonly Config testConfig = Config.CreateValidatedConfig(
		new Vector2Int( 0, 0 ),
		BBox.FromPositionAndSize( 0, 400 ),
		4.0f,
		2.0f,
		64.0f,
		16.0f,
		18.0f,
		40.0f
	);

	[TestMethod]
	public void Generator_FromPhysicsShape_Simple()
	{
		// A physics world to generate the navmesh from
		var world = new PhysicsWorld();
		var body = new PhysicsBody( world );
		var shape = body.AddBoxShape( BBox.FromPositionAndSize( 0, 200 ), Rotation.Identity );


		// generate the navmesh using CNavMeshHeightFieldGenerator
		{
			using HeightFieldGenerator hfGenerator = new();
			hfGenerator.Init( testConfig );

			hfGenerator.AddGeometryFromPhysicsShape( shape );
			Assert.AreEqual( 8, hfGenerator.inputGeoVerticesCount );
			Assert.AreEqual( 36, hfGenerator.inputGeoIndicesCount );

			using var heightField = hfGenerator.Generate();

			Assert.IsTrue( heightField != default );

			using NavMeshGenerator nmGenerator = new();
			nmGenerator.Init( testConfig, heightField );

			using var meshData = nmGenerator.Generate();

			Assert.IsNotNull( meshData );

			world.Delete();
		}
	}

	[TestMethod]
	public async Task Generator_GenerateTile_HighLevel()
	{
		using var navMesh = new NavMesh();
		var world = new PhysicsWorld();

		var boxSize = BBox.FromPositionAndSize( 0, 100 );
		var body = new PhysicsBody( world );
		body.AddBoxShape( boxSize, Rotation.Identity );

		navMesh.Init();
		Assert.AreNotEqual( navMesh.TileCount.Length, 0 );

		var testTilePosition = new Vector2Int( 0, 0 );
		var testTileWorldPosition = navMesh.TilePositionToWorldPosition( testTilePosition );

		var generateTask = navMesh.GenerateTile( world, testTileWorldPosition );
		await generateTask;

		world.Delete();

		boxSize = boxSize.Grow( 5 );

		for ( int i = 0; i < navMesh.GetPolyCount( testTilePosition ); i++ )
		{
			var pod = navMesh.GetPoly( testTilePosition, i );

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
}
