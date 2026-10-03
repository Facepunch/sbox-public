using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ResourceTests;

[TestClass]
public class ModelBuilderTests
{
	[DataTestMethod]
	[DataRow( null, "sbox_procedural_model.vmdl" )]
	[DataRow( "", "sbox_procedural_model.vmdl" )]
	[DataRow( "model_builder_named", "model_builder_named.vmdl" )]
	[DataRow( "scenes/world/aggregate_0.vmdl", "scenes/world/aggregate_0.vmdl" )]
	[DataRow( "mounts/goldsrc/model.mdl", "mounts/goldsrc/model.vmdl" )]
	[DataRow( "mounts/quake/model.md5mesh", "mounts/quake/model.vmdl" )]
	[DataRow( "mount://goldsrc/models/barney.mdl.vmdl", "mount_/goldsrc/models/barney.mdl.vmdl" )]
	[DataRow( "mount://quake/id1/models/player.md5mesh.vmdl", "mount_/quake/id1/models/player.md5mesh.vmdl" )]
	[DataRow( "C:\\Models\\Builder\\Named.MDL", "c_/models/builder/named.vmdl" )]
	[DataRow( "vpk:models/named.vmdl", "vpk_models/named.vmdl" )]
	[DataRow( "/", "sbox_procedural_model.vmdl" )]
	[DataRow( "Models\\Builder\\MixedCase.MDL", "models/builder/mixedcase.vmdl" )]
	[DataRow( "/scenes/world/aggregate_0.vmdl_c", "scenes/world/aggregate_0.vmdl" )]
	public void WithNameNamesNativeModel( string name, string nativeName )
	{
		var model = Model.Builder.WithName( name ).AddMesh( CreateBoundsMesh() ).Create();

		Assert.IsNotNull( model );
		Assert.IsFalse( model.IsError );
		Assert.IsTrue( model.IsProcedural );
		Assert.AreEqual( name ?? nativeName, model.Name );
		Assert.AreEqual( Resource.FixPath( name ?? nativeName ), model.ResourcePath );
		Assert.AreEqual( nativeName, model.native.GetModelName() );
		Assert.AreEqual( nativeName, NativeGlue.Resources.GetModelResourceName( model.native ) );
	}

	[TestMethod]
	public void NamedBuildersRemainAnonymous()
	{
		var name = $"model_builder_{Guid.NewGuid():N}.vmdl";
		var first = Model.Builder.WithName( name ).AddMesh( CreateBoundsMesh() ).Create();
		var second = Model.Builder.WithName( name ).AddMesh( CreateBoundsMesh() ).Create();

		Assert.AreNotSame( first, second );
		Assert.AreNotEqual( first.native.GetBindingPtr(), second.native.GetBindingPtr() );
		Assert.AreEqual( Guid.Empty, first.Guid );
		Assert.AreEqual( Guid.Empty, second.Guid );
		Assert.AreEqual( name, first.native.GetModelName() );
		Assert.AreEqual( name, second.native.GetModelName() );
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct BoundsVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.BlendIndices] public Color32 BlendIndices;
		[VertexLayout.BlendWeight] public Color32 BlendWeights;
	}

	[TestMethod]
	public void RuntimeBoneBoundsDefaultToBoneOrigin()
	{
		var bounds = new BBox( new Vector3( -100, -60, -10 ), new Vector3( 110, 80, 120 ) );
		var world = new SceneWorld();
		try
		{
			foreach ( var skinned in new[] { false, true } )
			{
				var builder = Model.Builder.AddMesh( CreateBoundsMesh() ).WithViewBounds( bounds );
				if ( skinned )
					builder.AddBone( "root", new Vector3( 0, 0, 16 ), Rotation.Identity );
				var model = builder.Create();
				Assert.AreEqual( bounds, model.RenderBounds );
				var sceneModel = new SceneModel( world, model, Transform.Zero );
				foreach ( var transform in new[]
				{
					Transform.Zero,
					new Transform( new Vector3( 100, -200, 50 ), Rotation.From( 15, 70, 25 ), new Vector3( 2, 0.5f, 1.5f ) )
				} )
				{
					sceneModel.Transform = transform;
					var expected = skinned
						? new BBox( new Vector3( 0, 0, 16 ), new Vector3( 0, 0, 16 ) )
						: bounds;
					sceneModel.UpdateToBindPose();
					AssertSceneBounds( sceneModel, expected.Transform( transform ) );
					sceneModel.Update( 0.016f );
					AssertSceneBounds( sceneModel, expected.Transform( transform ) );
				}
				sceneModel.Delete();
			}
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void RuntimeBoneBoundsFollowPose()
	{
		var world = new SceneWorld();
		var modelBounds = new BBox( new Vector3( -100, -100, -100 ), new Vector3( 100, 100, 100 ) );
		try
		{
			foreach ( var bounds in new[] { new BBox( new Vector3( -8, -6, -4 ), new Vector3( 12, 10, 16 ) ), default } )
				for ( var api = 0; api < 3; ++api )
				{
					var builder = Model.Builder.AddMesh( CreateBoundsMesh() ).WithViewBounds( modelBounds );
					var bone = new ModelBuilder.Bone( "root", null, Vector3.Zero, Rotation.Identity ) { Bounds = bounds };
					if ( api == 0 ) builder.AddBone( bone );
					else if ( api == 1 ) builder.AddBones( [bone] );
					else builder.AddBone( bone.Name, bone.Position, bone.Rotation, bounds );
					var model = builder.Create();
					Assert.AreEqual( modelBounds, model.RenderBounds );
					var transform = new Transform( new Vector3( 100, -200, 50 ), Rotation.From( 15, 70, 25 ), 2 );
					var sceneModel = new SceneModel( world, model, transform );
					sceneModel.UpdateToBindPose();
					AssertSceneBounds( sceneModel, bounds.Transform( transform ) );
					var pose = new Transform( new Vector3( 200, 20, 30 ), Rotation.From( 20, 40, 60 ) );
					sceneModel.Update( 0.016f, () => sceneModel.SetParentSpaceBone( 0, pose ) );
					AssertSceneBounds( sceneModel, bounds.Transform( transform.ToWorld( pose ) ) );
					sceneModel.Delete();
				}
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void RuntimeBoneBoundsIncludeUnspecifiedBounds()
	{
		var bounds = new BBox( new Vector3( -100, -60, -10 ), new Vector3( 110, 80, 120 ) );
		var boneBounds = new BBox( new Vector3( -8, -6, -4 ), new Vector3( 12, 10, 16 ) );
		var builder = Model.Builder.AddMesh( CreateBoundsMesh() ).WithViewBounds( bounds );
		builder.AddBone( "bounded", new Vector3( 200, 0, 0 ), Rotation.Identity, boneBounds );
		builder.AddBone( "unspecified", Vector3.Zero, Rotation.Identity );
		var world = new SceneWorld();
		try
		{
			var sceneModel = new SceneModel( world, builder.Create(), Transform.Zero );
			var expected = boneBounds.Translate( new Vector3( 200, 0, 0 ) ).AddPoint( Vector3.Zero );
			sceneModel.UpdateToBindPose();
			AssertSceneBounds( sceneModel, expected );
			sceneModel.Update( 0.016f );
			AssertSceneBounds( sceneModel, expected );
		}
		finally
		{
			world.Delete();
		}
	}

	private static Mesh CreateBoundsMesh()
	{
		var mesh = new Mesh( Material.Load( "materials/default/white.vmat" ) );
		var vertices = new BoundsVertex[]
		{
			new() { Position = new Vector3( -16, -16, 0 ), BlendWeights = new Color32( 255, 0, 0, 0 ) },
			new() { Position = new Vector3( 16, -16, 0 ), BlendWeights = new Color32( 255, 0, 0, 0 ) },
			new() { Position = new Vector3( 0, 16, 32 ), BlendWeights = new Color32( 255, 0, 0, 0 ) }
		};
		mesh.CreateVertexBuffer( vertices.Length, vertices );
		mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );
		mesh.Bounds = new BBox( new Vector3( -16, -16, 0 ), new Vector3( 16, 16, 32 ) );
		return mesh;
	}

	private static void AssertSceneBounds( SceneModel sceneModel, BBox expected )
	{
		var actual = sceneModel.Bounds;
		Assert.AreEqual( expected.Mins.x, actual.Mins.x, 1.01f );
		Assert.AreEqual( expected.Mins.y, actual.Mins.y, 1.01f );
		Assert.AreEqual( expected.Mins.z, actual.Mins.z, 1.01f );
		Assert.AreEqual( expected.Maxs.x, actual.Maxs.x, 1.01f );
		Assert.AreEqual( expected.Maxs.y, actual.Maxs.y, 1.01f );
		Assert.AreEqual( expected.Maxs.z, actual.Maxs.z, 1.01f );
	}

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
