using Sandbox.Navigation.Generation;

namespace Navigation;

[TestClass]
public class HeightFieldPhysicsIntegrationTests
{
	private static Config MakeConfig( BBox bounds )
	{
		return Config.CreateValidatedConfig(
			new Vector2Int( 0, 0 ),
			bounds,
			cellSize: 4.0f,
			cellHeight: 2.0f,
			agentHeight: 64.0f,
			agentRadius: 16.0f,
			agentStepSize: 18.0f,
			agentMaxSlope: 40.0f
		);
	}

	[TestMethod]
	public void HeightFieldGenerator_Generate_WithPhysicsShape()
	{
		var world = new PhysicsWorld();
		var body = new PhysicsBody( world );
		var shape = body.AddBoxShape( BBox.FromPositionAndSize( 0, 200 ), Rotation.Identity );

		using var gen = new HeightFieldGenerator();
		var cfg = MakeConfig( BBox.FromPositionAndSize( Vector3.Zero, 400 ) );
		gen.Init( cfg );

		gen.AddGeometryFromPhysicsShape( shape );

		Assert.AreEqual( 8, gen.inputGeoVerticesCount, "Expected box triangulation vertex count." );
		Assert.AreEqual( 36, gen.inputGeoIndicesCount, "Expected box triangulation index count (12 triangles)." );

		using var chf = gen.Generate();
		Assert.IsNotNull( chf, "Compact heightfield should be generated." );
		Assert.IsTrue( chf.SpanCount > 0, "Span count should be > 0." );

		bool anyWalkable = false;
		for ( int i = 0; i < chf.SpanCount; i++ )
		{
			if ( chf.Areas[i] == Constants.WALKABLE_AREA )
			{
				anyWalkable = true;
				break;
			}
		}

		Assert.IsTrue( anyWalkable, "Expected at least one remaining walkable area." );
		world.Delete();
	}
}
