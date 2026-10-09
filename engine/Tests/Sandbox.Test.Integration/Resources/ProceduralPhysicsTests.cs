using System;
using System.Linq;

namespace ResourceTests;

[TestClass]
public class ProceduralPhysicsTests
{
	static string NewPath() => $"procedural_physics_tests/{Guid.NewGuid():N}.vphys";

	sealed class TestSurface : Surface
	{
		public TestSurface()
		{
			RegisterWeakResourceId( $"surfaces/{System.Guid.NewGuid():N}.surface" );
			PostLoad();
		}
	}

	[TestMethod]
	public void StandalonePhysicsLoadsByName()
	{
		var path = NewPath();
		var physics = PhysicsGroupDescription.Create( path, [new PhysicsBodyBuilder().AddBox( new Vector3( 8 ) )] );
		var second = PhysicsGroupDescription.Create( NewPath(), [new PhysicsBodyBuilder().AddSphere( new Sphere( Vector3.Zero, 4 ) )] );

		Assert.IsTrue( physics.IsValid );
		Assert.AreEqual( path, physics.Name );
		Assert.AreEqual( path, physics.ResourcePath );
		Assert.AreEqual( 1, physics.Parts.Count );
		Assert.AreEqual( Transform.Zero, physics.Parts[0].Transform );
		Assert.AreSame( physics, PhysicsGroupDescription.Load( path ) );
		Assert.AreSame( physics, PhysicsGroupDescription.Load( path + "_c" ) );
		Assert.AreSame( physics, PhysicsGroupDescription.Load( path.ToUpperInvariant().Replace( '/', '\\' ) ) );
		Assert.AreSame( physics, PhysicsGroupDescription.Load( new ResourceId { Guid = Guid.NewGuid(), Path = path } ) );
		Assert.AreSame( physics, Json.Deserialize<PhysicsGroupDescription>( Json.Serialize( physics ) ) );
		Assert.AreSame( physics, Game.TypeLibrary.FromBytes<PhysicsGroupDescription>( Game.TypeLibrary.ToBytes( physics ) ) );
		Assert.AreSame( second, PhysicsGroupDescription.Load( second.ResourcePath ) );
	}

	[TestMethod]
	public void RecreatedPhysicsReplacesTheCachedReference()
	{
		var path = NewPath();
		var original = PhysicsGroupDescription.Create( path, [new PhysicsBodyBuilder().AddBox( new Vector3( 8 ) )] );
		var reference = Json.Serialize( original );
		var replacement = PhysicsGroupDescription.Create( path, [new PhysicsBodyBuilder().AddSphere( new Sphere( Vector3.Zero, 4 ) )] );

		Assert.AreNotSame( original, replacement );
		Game.Resources.Unregister( original );
		Assert.AreSame( replacement, PhysicsGroupDescription.Load( path ) );
		Assert.AreSame( replacement, Json.Deserialize<PhysicsGroupDescription>( reference ) );
	}

	[TestMethod]
	public void StandalonePhysicsCreatesColliderShapes()
	{
		var physics = PhysicsGroupDescription.Create( NewPath(), [new PhysicsBodyBuilder().AddBox( new Vector3( 8 ) )] );
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var collider = scene.CreateObject().AddComponent<PhysicsCollider>( false );
			collider.Static = true;
			collider.Physics = physics;
			collider.Enabled = true;

			var trace = scene.Trace.Ray( new Vector3( 0, 0, 32 ), new Vector3( 0, 0, -32 ) ).Run();
			Assert.IsTrue( trace.Hit );
			Assert.AreSame( collider, trace.Component );
			Assert.AreEqual( 8.0f, trace.EndPosition.z, 0.01f );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void StandaloneMeshPreservesItsSurfacePalette()
	{
		var first = new TestSurface();
		var second = new TestSurface();
		try
		{
			var body = new PhysicsBodyBuilder().AddMesh(
				[new Vector3( 0, 0, 0 ), new Vector3( 32, 0, 0 ), new Vector3( 32, 32, 0 ), new Vector3( 0, 32, 0 )],
				[0u, 1u, 2u, 0u, 2u, 3u], [0, 1] );
			var physics = PhysicsGroupDescription.Create( NewPath(), [body], [first, second] );
			var mesh = physics.Parts[0].Meshes.Single();

			CollectionAssert.AreEqual( new Surface[] { first, second }, mesh.GetTriangleSurfaces() );
			Assert.AreEqual( 4, mesh.GetVertices().Length );
			Assert.AreEqual( 6, mesh.GetIndices().Length );
			Assert.IsTrue( Sandbox.Resources.VPhysWriter.Write( [body], [first, second] ).Length > 0 );
		}
		finally
		{
			Surface.All.Remove( first.Index );
			Surface.All.Remove( second.Index );
			Game.Resources.Unregister( first );
			Game.Resources.Unregister( second );
		}
	}

	[TestMethod]
	public void StandalonePhysicsRejectsMissingNamesAndBodies()
	{
		Assert.ThrowsException<ArgumentNullException>( () => PhysicsGroupDescription.Create( NewPath(), null ) );
		Assert.ThrowsException<ArgumentException>( () => PhysicsGroupDescription.Create( NewPath(), [] ) );
		Assert.ThrowsException<ArgumentException>( () => PhysicsGroupDescription.Create( null, [new PhysicsBodyBuilder()] ) );
	}

	[TestMethod]
	public void ModelPhysicsKeepsItsEmbeddedName()
	{
		var model = Model.Load( "models/citizen/citizen.vmdl" );
		var physics = model.Physics;

		Assert.IsNotNull( physics );
		Assert.IsTrue( physics.IsValid );
		Assert.AreEqual( "embedded_physics.vphys", physics.Name );
		Assert.AreEqual( physics.Name, physics.ResourcePath );
		Assert.AreSame( physics, model.Physics );
	}
}
