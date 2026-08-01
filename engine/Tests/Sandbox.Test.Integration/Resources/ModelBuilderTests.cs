using System;
using System.Collections.Generic;

namespace ResourceTests;

[TestClass]
public class ModelBuilderTests
{
	[TestMethod]
	public void CollisionShapesRoundTrip()
	{
		var model = Model.Builder
			.WithName( "builder_shapes" )
			.WithMass( 250 )
			.AddCollisionSphere( 16, new Vector3( 0, 0, 8 ) )
			.AddCollisionCapsule( new Vector3( 0, 0, -8 ), new Vector3( 0, 0, 8 ), 4 )
			.AddCollisionBox( new Vector3( 8, 8, 8 ) )
			.Create();

		Assert.IsNotNull( model );
		Assert.AreEqual( "builder_shapes", model.Name );

		var physics = model.Physics;
		Assert.IsNotNull( physics );

		var body = physics.Parts.Single();
		Assert.AreEqual( 250f, body.Mass );

		var sphere = body.Spheres.Single().Sphere;
		Assert.AreEqual( 16f, sphere.Radius );
		Assert.AreEqual( new Vector3( 0, 0, 8 ), sphere.Center );

		Assert.AreEqual( 4f, body.Capsules.Single().Capsule.Radius );

		// Boxes are built into convex hulls
		Assert.AreEqual( 1, body.Hulls.Count );
	}

	[TestMethod]
	public void PhysicsBoundsComputedFromCollision()
	{
		var model = Model.Builder
			.AddCollisionBox( new Vector3( 16, 16, 16 ) )
			.Create();

		var bounds = model.PhysicsBounds;
		Assert.AreEqual( -16f, bounds.Mins.x, 0.5f );
		Assert.AreEqual( -16f, bounds.Mins.y, 0.5f );
		Assert.AreEqual( -16f, bounds.Mins.z, 0.5f );
		Assert.AreEqual( 16f, bounds.Maxs.x, 0.5f );
		Assert.AreEqual( 16f, bounds.Maxs.y, 0.5f );
		Assert.AreEqual( 16f, bounds.Maxs.z, 0.5f );
	}

	[TestMethod]
	public void CollisionMeshRoundTrip()
	{
		List<Vector3> vertices = [new( 0, 0, 0 ), new( 32, 0, 0 ), new( 32, 32, 0 ), new( 0, 32, 0 )];
		List<int> indices = [0, 1, 2, 0, 2, 3];

		var model = Model.Builder
			.AddCollisionMesh( vertices, indices )
			.Create();

		var body = model.Physics.Parts.Single();
		Assert.AreEqual( 1, body.Meshes.Count );
	}

	[TestMethod]
	public void BodiesAndJointsRoundTrip()
	{
		var builder = Model.Builder;
		builder.AddBody( 10 ).AddSphere( new Sphere( Vector3.Zero, 8 ) );
		builder.AddBody( 20 ).AddBox( new Vector3( 4, 4, 4 ) );
		builder.AddHingeJoint( 0, 1 );

		var physics = builder.Create().Physics;

		Assert.AreEqual( 2, physics.Parts.Count );
		Assert.AreEqual( 10f, physics.Parts[0].Mass );
		Assert.AreEqual( 8f, physics.Parts[0].Spheres.Single().Sphere.Radius );
		Assert.AreEqual( 20f, physics.Parts[1].Mass );
		Assert.AreEqual( 1, physics.Parts[1].Hulls.Count );

		var joint = physics.Joints.Single();
		Assert.AreEqual( PhysicsGroupDescription.JointType.Hinge, joint.Type );
		Assert.AreEqual( 0, joint.Body1 );
		Assert.AreEqual( 1, joint.Body2 );
	}

	[TestMethod]
	public void HitboxesRoundTrip()
	{
		var builder = Model.Builder;
		builder.AddBone( "head", new Vector3( 0, 0, 64 ), Rotation.Identity );

		var set = builder.AddHitboxSet();
		set.AddBox( "head_box", "head", new BBox( new Vector3( -4, -4, -4 ), new Vector3( 4, 4, 4 ) ) )
			.WithSurface( "flesh" )
			.AddTag( "head" );
		set.AddSphere( "head_sphere", "head", new Sphere( Vector3.Zero, 4 ) );
		set.AddCapsule( "neck", "head", new Capsule( new Vector3( 0, 0, -8 ), Vector3.Zero, 2 ) );

		var hitboxes = builder.Create().HitboxSet.All;
		Assert.AreEqual( 3, hitboxes.Count );

		var box = hitboxes.Single( h => h.Name == "head_box" );
		Assert.IsInstanceOfType<BBox>( box.Shape );
		Assert.AreEqual( "flesh", box.SurfaceName );
		Assert.IsTrue( box.Tags.Has( "head" ) );
		Assert.AreEqual( "head", box.Bone?.Name );

		var sphere = hitboxes.Single( h => h.Name == "head_sphere" );
		Assert.AreEqual( 4f, ((Sphere)sphere.Shape).Radius );

		var capsule = hitboxes.Single( h => h.Name == "neck" );
		Assert.AreEqual( 2f, ((Capsule)capsule.Shape).Radius );
	}

	[TestMethod]
	public void LodLevelOutOfRangeThrows()
	{
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Model.Builder.WithLodDistance( -1, 100 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Model.Builder.WithLodDistance( 8, 100 ) );
	}

	[TestMethod]
	public void CollisionMeshValidation()
	{
		List<Vector3> vertices = [new( 0, 0, 0 ), new( 32, 0, 0 ), new( 32, 32, 0 ), new( 0, 32, 0 )];

		// Indices must form triangles
		Assert.ThrowsException<ArgumentException>( () =>
			Model.Builder.AddCollisionMesh( vertices, [0, 1, 2, 3] ) );

		// Indices must be in range
		Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
			Model.Builder.AddCollisionMesh( vertices, [0, 1, 4] ) );

		// Materials are per triangle, not per vertex
		Assert.ThrowsException<ArgumentException>( () =>
			Model.Builder.AddCollisionMesh( vertices, [0, 1, 2, 0, 2, 3], [0, 0, 0, 0] ) );
	}
}
