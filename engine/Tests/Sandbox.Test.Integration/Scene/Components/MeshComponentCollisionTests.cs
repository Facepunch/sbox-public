using System.Linq;

namespace SceneTests.Components;

/// <summary>
/// A <see cref="MeshComponent"/> model only carries the physics representation its <see cref="MeshComponent.Collision"/>
/// uses - building the other one costs native memory per mesh for nothing.
/// </summary>
[TestClass]
public class MeshComponentCollisionTest
{
	const int DefaultSurfaceKey = int.MinValue;

	/// <summary>
	/// Headless tests mount no surfaces, but building a mesh model resolves the "default" surface for
	/// untextured faces. Name one for the duration of each test, on the physics system's built-in index 0.
	/// </summary>
	[TestInitialize]
	public void NameDefaultSurface()
	{
		if ( Surface.FindByName( "default" ) is not null ) return;

		var surface = new Surface();
		surface.RegisterWeakResourceId( "surfaces/default.surface" );
		Surface.All[DefaultSurfaceKey] = surface;
	}

	[TestCleanup]
	public void RemoveDefaultSurface() => Surface.All.Remove( DefaultSurfaceKey );

	static PolygonMesh CreateBox()
	{
		var mesh = new PolygonMesh();
		var v = new[]
		{
			mesh.AddVertex( new Vector3( 0, 0, 0 ) ), mesh.AddVertex( new Vector3( 64, 0, 0 ) ),
			mesh.AddVertex( new Vector3( 64, 64, 0 ) ), mesh.AddVertex( new Vector3( 0, 64, 0 ) ),
			mesh.AddVertex( new Vector3( 0, 0, 64 ) ), mesh.AddVertex( new Vector3( 64, 0, 64 ) ),
			mesh.AddVertex( new Vector3( 64, 64, 64 ) ), mesh.AddVertex( new Vector3( 0, 64, 64 ) ),
		};

		mesh.AddFace( v[3], v[2], v[1], v[0] ); // bottom
		mesh.AddFace( v[4], v[5], v[6], v[7] ); // top
		mesh.AddFace( v[0], v[1], v[5], v[4] );
		mesh.AddFace( v[1], v[2], v[6], v[5] );
		mesh.AddFace( v[2], v[3], v[7], v[6] );
		mesh.AddFace( v[3], v[0], v[4], v[7] );
		return mesh;
	}

	static (int Meshes, int Hulls) PhysicsParts( MeshComponent component )
	{
		var parts = component.Model.Physics?.Parts ?? [];
		return (parts.Sum( p => p.Meshes.Count ), parts.Sum( p => p.Hulls.Count ));
	}

	static void AssertCollision( MeshComponent component, MeshComponent.CollisionType collision )
	{
		Assert.IsTrue( component.Model.MeshCount > 0, "The render mesh is always built" );

		var (meshes, hulls) = PhysicsParts( component );
		Assert.AreEqual( collision == MeshComponent.CollisionType.Mesh, meshes > 0, $"{collision}: collision mesh parts" );
		Assert.AreEqual( collision == MeshComponent.CollisionType.Hull, hulls > 0, $"{collision}: collision hull parts" );

		Assert.AreEqual( collision == MeshComponent.CollisionType.None ? 0 : 1, component.Shapes.Count, $"{collision}: physics shapes" );
		Assert.IsTrue( component.Shapes.All( s => collision == MeshComponent.CollisionType.Mesh ? s.IsMeshShape : s.IsHullShape ) );
	}

	[TestMethod]
	[DataRow( MeshComponent.CollisionType.Mesh )]
	[DataRow( MeshComponent.CollisionType.Hull )]
	[DataRow( MeshComponent.CollisionType.None )]
	public void ModelOnlyBuildsTheCollisionItUses( MeshComponent.CollisionType collision )
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		// Game scenes only build the mesh on enable, so configure it first
		var go = new GameObject( true, "box" );
		var component = go.Components.Create<MeshComponent>( false );
		component.Collision = collision;
		component.Mesh = CreateBox();
		component.Enabled = true;

		AssertCollision( component, collision );
	}

	[TestMethod]
	public void ChangingCollisionRebuildsThePhysicsRepresentation()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = new GameObject( true, "box" );
		var component = go.Components.Create<MeshComponent>( false );
		component.Mesh = CreateBox();
		component.Enabled = true;
		AssertCollision( component, MeshComponent.CollisionType.Mesh );

		component.Collision = MeshComponent.CollisionType.Hull;
		AssertCollision( component, MeshComponent.CollisionType.Hull );

		component.Collision = MeshComponent.CollisionType.None;
		AssertCollision( component, MeshComponent.CollisionType.None );

		component.Collision = MeshComponent.CollisionType.Mesh;
		AssertCollision( component, MeshComponent.CollisionType.Mesh );
	}
}
