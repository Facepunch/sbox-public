
namespace PhysicsTests;

[TestClass]
public class QueryTest
{
	/// <summary>
	/// Does sphere overlap test work against meshes?
	/// </summary>
	[TestMethod]
	public void QueryMesh()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var world = scene.PhysicsWorld;
		var body = new PhysicsBody( world );
		var vertices = new[] { new Vector3( 0, 0, 0 ), new Vector3( 100, 0, 0 ), new Vector3( 100, 100, 0 ) };
		var indices = new[] { 0, 1, 2 };
		body.AddMeshShape( vertices, indices );

		var go = scene.CreateObject();
		body.GameObject = go;

		var gameObjects = scene.FindInPhysics( new Sphere( 0, 200 ) ).ToList();

		Assert.IsTrue( gameObjects != null && gameObjects.Count == 1 );
		Assert.IsTrue( gameObjects.FirstOrDefault() == go );
	}
}
