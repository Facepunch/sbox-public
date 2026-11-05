using System;

namespace PhysicsTests;

[TestClass]
public class ContactTest
{
	[TestMethod]
	public void RollingSphereDoesNotLaunchAtCoplanarSeam()
	{
		var world = new PhysicsWorld();
		try
		{
			world.Gravity = new Vector3( 0, 0, -850 );
			var ground = new PhysicsBody( world ) { BodyType = PhysicsBodyType.Static };
			Vector3[] vertices = [new( -4, -4, 0 ), new( 0, -4, 0 ), new( -4, 0, 0 ), new( 0, 0, 0 ), new( 4, 0, -2 ), new( 0, 4, -2 )];
			int[] indices = [0, 1, 2, 1, 3, 2, 3, 4, 5];
			ground.AddMeshShape( vertices, indices );
			var body = new PhysicsBody( world )
			{
				BodyType = PhysicsBodyType.Dynamic,
				Position = new Vector3( -2.5f, -2.5f, 16 ),
				Mass = 100000,
				GravityEnabled = true,
				Locking = new PhysicsLock { Y = true, Yaw = true, Roll = true }
			};
			body.AddSphereShape( Vector3.Zero, 16 );
			body.Velocity = new Vector3( 1000, 0, 0 );
			body.AngularVelocity = new Vector3( 0, 62.5f, 0 );

			world.Step( 1f / 120 );

			Assert.IsTrue( body.Velocity.z <= 1,
				$"Sphere launched from a coplanar seam: position {body.Position}, velocity {body.Velocity}" );
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	[DataRow( 117170.09f )]
	[DataRow( 500000f )]
	public void HeavySphereRestsOnTranslationLockedBox( float mass )
	{
		const float radius = 17.79f;
		const float halfThickness = 2.5f;
		var world = new PhysicsWorld();
		try
		{
			world.Gravity = new Vector3( 0, 0, -800 );
			world.SleepingEnabled = false;
			var ground = new PhysicsBody( world )
			{
				BodyType = PhysicsBodyType.Dynamic,
				Mass = 50000,
				GravityEnabled = true,
				Locking = new PhysicsLock { X = true, Y = true, Z = true, Yaw = true, Roll = true }
			};
			ground.AddBoxShape( Vector3.Zero, Rotation.Identity, new Vector3( 321.5f, 12.5f, halfThickness ) );
			var body = new PhysicsBody( world )
			{
				BodyType = PhysicsBodyType.Dynamic,
				Mass = mass,
				GravityEnabled = true,
				Position = Vector3.Up * (radius + halfThickness + 4),
				Locking = new PhysicsLock { Y = true, Yaw = true, Roll = true }
			};
			body.AddSphereShape( Vector3.Zero, radius );

			var settledSteps = 0;
			for ( var step = 0; step < 120; ++step )
			{
				world.Step( 1f / 50 );
				var separation = body.Position.z - radius - halfThickness;
				Assert.IsTrue( separation >= -halfThickness,
					$"Sphere sank past the translation-locked box's midplane at step {step}: separation {separation}, velocity {body.Velocity}" );
				settledSteps = MathF.Abs( separation ) < 0.5f && body.Velocity.Length < 1 ? settledSteps + 1 : 0;
				if ( settledSteps >= 12 )
					return;
			}

			Assert.Fail( $"Sphere did not settle on the translation-locked box: position {body.Position}, velocity {body.Velocity}" );
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	[DataRow( true, 8f, 1f )]
	[DataRow( true, 8f, 100000f )]
	[DataRow( false, 8f, 100000f )]
	[DataRow( true, 40f, 100000f )]
	public void LargeCapsuleRestsOnTessellatedGround( bool heightfield, float cellSize, float mass )
	{
		const int resolution = 64;
		const float radius = 160;
		var world = new PhysicsWorld();
		try
		{
			world.Gravity = new Vector3( 0, -800, 0 );
			world.SleepingEnabled = false;
			var ground = new PhysicsBody( world ) { BodyType = PhysicsBodyType.Static };
			if ( heightfield )
			{
				ground.AddHeightFieldShape( new ushort[resolution * resolution], new byte[resolution * resolution],
					resolution, resolution, cellSize, 1 );
			}
			else
			{
				var vertices = new Vector3[resolution * resolution];
				var indices = new int[(resolution - 1) * (resolution - 1) * 6];
				var index = 0;
				for ( var row = 0; row < resolution; row++ )
				{
					for ( var column = 0; column < resolution; column++ )
					{
						var vertex = row * resolution + column;
						vertices[vertex] = new Vector3( column * cellSize, 0, row * cellSize );
						if ( row == resolution - 1 || column == resolution - 1 )
							continue;

						indices[index++] = vertex;
						indices[index++] = vertex + resolution;
						indices[index++] = vertex + 1;
						indices[index++] = vertex + 1;
						indices[index++] = vertex + resolution;
						indices[index++] = vertex + resolution + 1;
					}
				}
				ground.AddMeshShape( vertices, indices );
			}

			var center = (resolution - 1) * cellSize * 0.5f;
			var body = new PhysicsBody( world )
			{
				BodyType = PhysicsBodyType.Dynamic,
				Position = new Vector3( center, radius + 16, center ),
				Mass = mass,
				GravityEnabled = true,
				Locking = new PhysicsLock { Pitch = true, Yaw = true, Roll = true }
			};
			body.AddCapsuleShape( Vector3.Zero, new Vector3( 0, 100, 0 ), radius );

			var settledSteps = 0;
			for ( var step = 0; step < 120; step++ )
			{
				world.Step( 1f / 60 );
				Assert.IsTrue( body.Position.y >= radius - 2,
					$"Capsule lost ground support at step {step}: position {body.Position}, velocity {body.Velocity}" );
				settledSteps = MathF.Abs( body.Position.y - radius ) < 2 && body.Velocity.Length < 1 ? settledSteps + 1 : 0;
				if ( settledSteps >= 12 )
					return;
			}

			Assert.Fail( $"Capsule did not settle on the ground: position {body.Position}, velocity {body.Velocity}" );
		}
		finally
		{
			world.Delete();
		}
	}
}
