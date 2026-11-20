using System.Collections.Generic;

namespace PhysicsTests;

[TestClass]
public class QueryTest
{
	[TestMethod]
	[DataRow( 0f, 0f, 0f, 50f, true )]
	[DataRow( 0f, 0f, 0f, 0f, true )]
	[DataRow( 9f, 0f, 0f, 1f, true )]
	[DataRow( 11f, 0f, 0f, 1f, true )]
	[DataRow( 11.5f, 0f, 0f, 1f, false )]
	[DataRow( 11f, 11f, 11f, 1f, false )]
	[DataRow( 100f, 0f, 0f, 1f, false )]
	public void SphereHullOverlapPreservesInteriorBoundaryAndMisses( float x, float y, float z, float radius, bool expected )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var vertices = new List<Vector3>();
		for ( var ix = -1; ix <= 1; ix += 2 )
			for ( var iy = -1; iy <= 1; iy += 2 )
				for ( var iz = -1; iz <= 1; iz += 2 )
					vertices.Add( new Vector3( ix, iy, iz ) * 10 );

		var localCenter = new Vector3( 32, -12, 5 );
		foreach ( var rotated in new[] { false, true } )
		{
			var rotation = rotated ? Rotation.From( 23, 47, 11 ) : Rotation.Identity;
			var position = rotated ? new Vector3( 20000, -15000, 5000 ) : Vector3.Zero;
			var transform = new Transform( position, rotation );
			var body = new PhysicsBody( scene.PhysicsWorld );
			try
			{
				body.Transform = transform;
				body.AddHullShape( localCenter, Rotation.Identity, vertices );
				var go = scene.CreateObject();
				body.GameObject = go;
				var sphere = new Sphere( transform.PointToWorld( localCenter + new Vector3( x, y, z ) ), radius );
				var found = scene.FindInPhysics( sphere ).Contains( go );
				Assert.AreEqual( expected, found, $"rotated={rotated}, center={sphere.Center}, radius={radius}" );
			}
			finally
			{
				body.Remove();
			}
		}
	}

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
