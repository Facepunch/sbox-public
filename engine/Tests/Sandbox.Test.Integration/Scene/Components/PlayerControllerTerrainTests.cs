using Sandbox.Movement;
using System;

namespace SceneTests.Components;

public partial class PlayerControllerTest
{
	static Terrain CreateWalkingTerrain( Scene scene, float roughness )
	{
		var storage = new TerrainStorage();
		storage.SetResolution( 64 );
		storage.TerrainSize = 1250;
		storage.TerrainHeight = 512;
		var cellSize = storage.TerrainSize / storage.Resolution;
		for ( int y = 0; y < storage.Resolution; ++y )
		{
			for ( int x = 0; x < storage.Resolution; ++x )
			{
				var height = 64 + x * cellSize * 0.15f + y * cellSize * 0.1f
					+ ((x + y) % 2 == 0 ? roughness : -roughness);
				storage.HeightMap[y * storage.Resolution + x] = (ushort)MathF.Round( height / storage.TerrainHeight * ushort.MaxValue );
			}
		}
		var terrain = scene.CreateObject().Components.Create<Terrain>( false );
		terrain.Storage = storage;
		terrain.Enabled = true;
		Assert.AreEqual( 1, terrain.Shapes.Count );
		Assert.IsTrue( terrain.Shapes[0].IsHeightfieldShape );
		return terrain;
	}

	[TestMethod]
	[DataRow( 0f, 0f, 160f, 1f )]
	[DataRow( 0f, 25f, 160f, 1f )]
	[DataRow( 2f, 0f, 160f, 1f )]
	[DataRow( 2f, 25f, 160f, 1f )]
	[DataRow( 2f, 90f, 160f, 1f )]
	[DataRow( 2f, 135f, 160f, 1f )]
	[DataRow( 4f, 0f, 160f, 1f )]
	[DataRow( 4f, 90f, 160f, 1f )]
	[DataRow( 4f, 135f, 160f, 1f )]
	[DataRow( 4f, 180f, 160f, 1f )]
	[DataRow( 2f, 225f, 160f, 1f )]
	[DataRow( 2f, 315f, 160f, 1f )]
	[DataRow( 2f, 0f, 110f, 1f )]
	[DataRow( 2f, 0f, 320f, 1f )]
	[DataRow( 2f, 25f, 320f, 1f )]
	[DataRow( 2f, 0f, 160f, 0.5f )]
	[DataRow( 2f, 25f, 160f, 2f )]
	public void WalkingOnHeightfieldsPreservesTheRequestedHorizontalHeading( float roughness, float yaw, float speed, float scale )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var terrain = CreateWalkingTerrain( scene, roughness );
		var start = new Vector3( 512, 512, 0 );
		var surface = scene.Trace.Ray( start + Vector3.Up * 1024, start - Vector3.Up * 1024 ).Run();
		Assert.IsTrue( surface.Hit );
		Assert.AreEqual( terrain, surface.Collider );
		var pc = CreatePlayer( scene, surface.EndPosition + Vector3.Up * 4 );
		pc.BodyHeight = 64;
		pc.WorldScale = new Vector3( scale );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		var direction = Rotation.FromYaw( yaw ).Forward;
		var sideways = Rotation.FromYaw( yaw ).Left;
		start = pc.Body.PhysicsBody.Position;
		var movement = pc.Components.Create<PhysicsMovementRecorder>();
		movement.Controller = pc;
		pc.WishVelocity = direction * speed;
		var measured = 0;
		for ( int i = 0; i < 300 && (pc.Body.PhysicsBody.Position - start).Dot( direction ) < 320; ++i )
		{
			movement.Steps.Clear();
			scene.GameTick( 1.0 / 50 );
			foreach ( var step in movement.Steps )
			{
				++measured;
				Assert.IsTrue( step.Grounded, $"lost heightfield support at {step.After}" );
				Assert.IsTrue( MathF.Abs( step.Velocity.Dot( sideways ) ) < 0.5f,
					$"terrain redirected input at {step.Before} -> {step.After}: before physics {step.InitialVelocity}, after physics {step.Velocity}, ground {pc.GroundNormal}" );
				Assert.IsTrue( MathF.Abs( (step.After - start).Dot( sideways ) ) < 0.5f,
					$"heightfield deflected the path at {step.After}" );
			}
		}
		Assert.IsTrue( measured > 0 );
		Assert.IsTrue( (pc.Body.PhysicsBody.Position - start).Dot( direction ) >= 320, $"stuck on heightfield at {pc.Body.PhysicsBody.Position}" );
	}
}
