using Sandbox.Movement;
using System;
using System.Collections.Generic;

namespace SceneTests.Components;

public partial class PlayerControllerTest
{
	public sealed class StepDecisionRecorder : MoveModeWalk
	{
		public int Decisions { get; private set; }
		public float MaxLift { get; private set; }

		public override void PrePhysicsStep()
		{
			var before = Controller.Body.PhysicsBody.Position;
			base.PrePhysicsStep();
			MaxLift = MathF.Max( MaxLift, (Controller.Body.PhysicsBody.Position - before).Dot( Controller.UpDirection ) );
			++Decisions;
		}
	}

	static StepDecisionRecorder ObserveStepDecisions( PlayerController pc )
	{
		var recorder = pc.Components.Create<StepDecisionRecorder>();
		recorder.Controller = pc;
		recorder.Priority = 1;
		return recorder;
	}

	static void AssertNoStepLift( StepDecisionRecorder recorder )
	{
		Assert.IsTrue( recorder.Decisions > 0, "step planner was not exercised" );
		Assert.AreEqual( 0f, recorder.MaxLift, 0.001f, "step planner applied a lift without a valid step" );
	}

	static GameObject AddMovementBox( Scene scene, Vector3 center, Vector3 size, Rotation rotation )
	{
		var go = scene.CreateObject();
		go.WorldPosition = rotation * center;
		go.WorldRotation = rotation;
		var collider = go.Components.Create<BoxCollider>();
		collider.Scale = size;
		collider.Static = true;
		return go;
	}

	public static IEnumerable<object[]> WallStepCases()
	{
		foreach ( var mesh in new[] { false, true } )
			foreach ( var descending in new[] { false, true } )
				foreach ( var yaw in new[] { 0f, 25f } )
					foreach ( var stairYaw in new[] { 0f, 25f } )
						yield return [18f, descending, mesh, yaw, yaw == 0 ? 1f : -1f, stairYaw, 160f, 1f, 0.5f];

		foreach ( var scale in new[] { 0.25f, 0.5f, 2f } )
			foreach ( var pressure in new[] { 0.05f, 2f } )
				foreach ( var descending in new[] { false, true } )
					yield return [18f, descending, true, 45f, -1f, -25f, 160f * scale, scale, pressure];

		foreach ( var speed in new[] { 40f, 320f, 640f } )
			foreach ( var descending in new[] { false, true } )
				yield return [8f, descending, true, 25f, 1f, -25f, speed, 1f, 0.5f];
	}

	[TestMethod]
	[DynamicData( nameof( WallStepCases ), DynamicDataSourceType.Method )]
	public void StairsWhileSlidingAgainstWall( float height, bool descending, bool mesh, float yaw, float side, float stairYaw, float speed, float scale, float pressure )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		var stairsRotation = rotation * Rotation.FromYaw( stairYaw );
		CreateStairs( scene, height, stairsRotation, mesh, scale );
		AddMovementBox( scene, new Vector3( 64, side * 48, 128 ) * scale, new Vector3( 512, 32, 512 ) * scale, rotation );
		var pc = CreatePlayer( scene, Vector3.Up * ((descending ? 4 * height * scale : 0) + 1) );
		pc.WorldScale = new Vector3( scale );
		var wallLimit = 32 * scale - pc.BodyCollider.Radius * scale;
		var towardWall = rotation * new Vector3( 0, side, 0 );
		var start = new Vector3( 0, side * (wallLimit - 0.2f), (descending ? 4 * height * scale : 0) + 1 );
		if ( descending )
		{
			var stairDirection = Rotation.FromYaw( stairYaw ).Forward;
			start.x = (132 * scale - start.y * stairDirection.y) / stairDirection.x;
		}
		pc.WorldPosition = rotation * start;
		var movement = pc.Components.Create<PhysicsMovementRecorder>();
		movement.Controller = pc;
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		var direction = rotation.Forward * (descending ? -1 : 1);
		pc.WishVelocity = (direction + towardWall * pressure) * speed;
		pc.Body.Velocity = direction * speed;
		var reachedEnd = false;
		for ( int i = 0; i < (speed < 60 ? 3000 : 600); ++i )
		{
			movement.Steps.Clear();
			scene.GameTick( 1.0 / 50 );
			foreach ( var step in movement.Steps )
			{
				var distance = (step.After - step.Before).Dot( direction );
				var expected = step.InitialVelocity.Dot( direction ) * step.Delta;
				Assert.IsTrue( distance >= expected - 0.05f, $"wall clipped progress from {step.Before} to {step.After}: {distance} < {expected}" );
				Assert.IsTrue( step.Grounded, $"lost support at {step.After}: {DescribeGround( pc )}" );
				Assert.IsTrue( MathF.Abs( step.Velocity.z ) < 1, $"launched at {step.After}: {step.Velocity}" );
				Assert.IsTrue( step.After.Dot( towardWall ) <= wallLimit + 0.25f * MathF.Max( 1, scale ), $"entered wall at {step.After}" );
				Assert.IsTrue( step.After.Dot( towardWall ) >= wallLimit - 1, $"deflected from wall at {step.After}" );
			}
			var along = pc.Body.PhysicsBody.Position.Dot( stairsRotation.Forward );
			if ( descending ? along <= 12 * scale : along >= 132 * scale )
			{
				reachedEnd = true;
				break;
			}
		}
		Assert.IsTrue( reachedEnd, $"stuck beside wall at {pc.Body.PhysicsBody.Position}" );
		Assert.AreEqual( descending ? 0 : 4 * height * scale, pc.Body.PhysicsBody.Position.z, 1.0f );
	}

	[TestMethod]
	[DataRow( 0.1f, 24f, 110f, 0f, true, 1f, 0f )]
	[DataRow( 0.5f, 24f, 320f, 25f, true, 1f, 0f )]
	[DataRow( 1f, 12f, 320f, -25f, true, 1f, 0f )]
	[DataRow( 8f, 8f, 110f, 0f, true, 1f, 0f )]
	[DataRow( 18f, 8f, 160f, 0f, true, 1f, 0f )]
	[DataRow( 18f, 12f, 160f, 25f, true, 1f, 0f )]
	[DataRow( 18f, 24f, 320f, 40f, true, 1f, 0f )]
	[DataRow( 18f, 24f, 320f, -40f, false, 1f, 0f )]
	[DataRow( 18f, 24f, 640f, 0f, true, 1f, 0f )]
	[DataRow( 8f, 24f, 10f, 0f, true, 1f, 0f )]
	[DataRow( 8f, 24f, 10f, 25f, true, 1f, 0f )]
	[DataRow( 8f, 24f, 10f, -25f, false, 1f, 0f )]
	[DataRow( 18f, 24f, 40f, 0f, true, 0.5f, 0f )]
	[DataRow( 1f, 24f, 10f, 0f, true, 1f, 0f )]
	[DataRow( 18f, 24f, 160f, 25f, true, 0.25f, 0f )]
	[DataRow( 18f, 24f, 320f, 0f, true, 0.5f, 0f )]
	[DataRow( 18f, 24f, 640f, 25f, true, 2f, 0f )]
	[DataRow( 18f, 24f, 160f, 0f, true, 1f, 0.1f )]
	[DataRow( 18f, 24f, 160f, -25f, true, 1f, 0.5f )]
	[DataRow( 8f, 12f, 320f, 0f, false, 1f, 0.5f )]
	public void ObliqueAndNarrowStairsPreserveMovement( float height, float tread, float speed, float angle, bool mesh, float scale, float gap )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateStairs( scene, height, Rotation.Identity, mesh, scale, tread, gap );
		var pc = CreatePlayer( scene, Vector3.Up );
		pc.WorldScale = new Vector3( scale );
		var movement = pc.Components.Create<PhysicsMovementRecorder>();
		movement.Controller = pc;
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		var direction = Rotation.FromYaw( angle ).Forward;
		pc.WishVelocity = direction * speed;
		pc.Body.Velocity = pc.WishVelocity;
		var goal = (48 + tread * 3.5f) * scale;
		var ticks = (int)MathF.Ceiling( goal / (direction.x * speed / 50) ) * (speed < 60 ? 8 : 1) + 30;
		for ( int i = 0; i < ticks && pc.Body.PhysicsBody.Position.x < goal; ++i )
		{
			movement.Steps.Clear();
			scene.GameTick( 1.0 / 50 );
			foreach ( var step in movement.Steps )
			{
				var distance = (step.After - step.Before).Dot( direction );
				var expectedSpeed = step.InitialVelocity.Dot( direction );
				Assert.IsTrue( distance >= expectedSpeed * step.Delta - 0.05f, $"clipped at {step.Before} -> {step.After}: {distance}, {step.Velocity}" );
				Assert.AreEqual( expectedSpeed, step.Velocity.Dot( direction ), 0.5f, $"slowed at {step.Before} -> {step.After}: {step.Velocity}" );
				Assert.AreEqual( 0, (step.After - step.Before).Dot( Rotation.FromYaw( angle ).Left ), 0.05f, $"deflected at {step.After}" );
				Assert.IsTrue( step.Grounded, $"lost support at {step.After}: {DescribeGround( pc )}" );
				Assert.IsTrue( MathF.Abs( step.Velocity.z ) < 1, $"launched at {step.After}" );
			}
		}
		Assert.IsTrue( pc.Body.PhysicsBody.Position.x >= goal, $"failed to reach stair top: {pc.Body.PhysicsBody.Position}" );
		Assert.AreEqual( 4 * height * scale, pc.Body.PhysicsBody.Position.z, 1.0f );
	}

	[TestMethod]
	[DataRow( false, 0f )]
	[DataRow( false, 25f )]
	[DataRow( true, 0f )]
	[DataRow( true, 25f )]
	public void WallSlidingStepsRespectCornersAndHeadroom( bool lowCeiling, float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		CreateStairs( scene, 18, rotation, true );
		AddMovementBox( scene, new Vector3( 64, 48, 128 ), new Vector3( 512, 32, 512 ), rotation );
		AddMovementBox( scene, lowCeiling ? new Vector3( 64, 0, 96 ) : new Vector3( 112, 0, 128 ),
			lowCeiling ? new Vector3( 512, 256, 32 ) : new Vector3( 32, 512, 512 ), rotation );
		var pc = CreatePlayer( scene, Vector3.Up );
		var towardWall = rotation * new Vector3( 0, 1, 0 );
		var wallLimit = 32 - pc.BodyCollider.Radius;
		pc.WorldPosition += towardWall * (wallLimit - 0.2f);
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = (rotation.Forward + towardWall * 0.5f) * 160;
		for ( int i = 0; i < 80; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			var position = pc.Body.PhysicsBody.Position;
			Assert.IsTrue( position.Dot( towardWall ) <= wallLimit + 0.25f, $"entered wall at {position}" );
			Assert.IsTrue( position.Dot( rotation.Forward ) <= (lowCeiling ? 44 : 96 - pc.BodyCollider.Radius + 0.25f), $"bypassed obstruction at {position}" );
			Assert.IsTrue( pc.IsOnGround, $"lost ground at {position}" );
			Assert.IsTrue( MathF.Abs( pc.Body.Velocity.z ) < 1, $"launched at {position}" );
		}
		Assert.AreEqual( lowCeiling ? 0 : 36, pc.Body.PhysicsBody.Position.z, 1.0f );
		if ( !lowCeiling )
			Assert.IsTrue( pc.Body.PhysicsBody.Position.Dot( rotation.Forward ) > 80 );
	}

	[TestMethod]
	[DataRow( -10f, 0f )]
	[DataRow( 10f, 25f )]
	[DataRow( -10f, 25f )]
	[DataRow( 10f, 0f )]
	public void StepFromSlopeWhileSlidingAgainstWall( float pitch, float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var rotation = Rotation.FromYaw( yaw );
		CreateFloor( scene ).WorldRotation = rotation * Rotation.FromPitch( pitch );
		var stepPosition = rotation * new Vector3( 48, 0, 0 );
		var surface = scene.Trace.Ray( stepPosition + Vector3.Up * 128, stepPosition - Vector3.Up * 128 ).Run();
		Assert.IsTrue( surface.Hit );
		var height = surface.EndPosition.z + 8;
		CreateStep( scene, 48, 256, height, rotation );
		AddMovementBox( scene, new Vector3( 64, 48, 128 ), new Vector3( 512, 32, 512 ), rotation );
		var pc = CreatePlayer( scene, Vector3.Up * 40 );
		var towardWall = rotation * new Vector3( 0, 1, 0 );
		pc.WorldPosition += towardWall * (32 - pc.BodyCollider.Radius - 0.2f);
		var movement = pc.Components.Create<PhysicsMovementRecorder>();
		movement.Controller = pc;
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = (rotation.Forward + towardWall * 0.5f) * 160;
		for ( int i = 0; i < 100 && pc.Body.PhysicsBody.Position.Dot( rotation.Forward ) < 72; ++i )
		{
			movement.Steps.Clear();
			scene.GameTick( 1.0 / 50 );
			foreach ( var step in movement.Steps )
			{
				var distance = (step.After - step.Before).Dot( rotation.Forward );
				var expected = step.InitialVelocity.Dot( rotation.Forward ) * step.Delta;
				Assert.IsTrue( distance >= expected - 0.05f, $"slope clipped progress from {step.Before} to {step.After}: {distance} < {expected}" );
				Assert.IsTrue( step.Grounded, $"lost support at {step.After}" );
			}
		}
		Assert.IsTrue( pc.Body.PhysicsBody.Position.Dot( rotation.Forward ) >= 72 );
		Assert.AreEqual( height, pc.Body.PhysicsBody.Position.z, 1.0f );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 25f )]
	public void WallSlidingUnderLowCeilingDoesNotInventSteps( float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		AddMovementBox( scene, new Vector3( 64, 48, 128 ), new Vector3( 512, 32, 512 ), rotation );
		AddMovementBox( scene, new Vector3( 0, 0, 94 ), new Vector3( 1024, 1024, 32 ), Rotation.Identity );
		var pc = CreatePlayer( scene, Vector3.Up );
		var towardWall = rotation * new Vector3( 0, 1, 0 );
		pc.WorldPosition += towardWall * (32 - pc.BodyCollider.Radius - 0.2f);
		var movement = pc.Components.Create<PhysicsMovementRecorder>();
		movement.Controller = pc;
		TickUntilGrounded( scene, pc );
		pc.WishVelocity = (rotation.Forward + towardWall * 0.5f) * 160;
		for ( int i = 0; i < 100 && pc.Body.PhysicsBody.Position.Dot( rotation.Forward ) < 128; ++i )
		{
			movement.Steps.Clear();
			scene.GameTick( 1.0 / 50 );
			foreach ( var step in movement.Steps )
			{
				Assert.IsTrue( (step.After - step.Before).Dot( rotation.Forward ) >= step.InitialVelocity.Dot( rotation.Forward ) * step.Delta - 0.05f );
				Assert.IsTrue( step.Grounded );
				Assert.AreEqual( 0, step.After.z, 0.2f, $"invented step at {step.After}" );
				Assert.IsTrue( MathF.Abs( step.Velocity.z ) < 1 );
			}
		}
		Assert.IsTrue( pc.Body.PhysicsBody.Position.Dot( rotation.Forward ) >= 128 );
	}

	[TestMethod]
	[DataRow( 8f, 80.5f, false, true )]
	[DataRow( 8f, 79.5f, false, false )]
	[DataRow( 8f, 44.5f, true, true )]
	[DataRow( 8f, 43.5f, true, false )]
	[DataRow( 18f, 90.5f, false, true )]
	[DataRow( 18.5f, 128f, false, false )]
	public void StepsRespectExactHeightAndBodyHeadroom( float height, float ceiling, bool ducked, bool allowed )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateStep( scene, 48, 256, height, Rotation.Identity );
		AddMovementBox( scene, new Vector3( 96, 0, ceiling + 8 ), new Vector3( 512, 256, 16 ), Rotation.Identity );
		var pc = CreatePlayer( scene, Vector3.Up );
		pc.IsDucking = ducked;
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = Vector3.Forward * 110;
		for ( int i = 0; i < 100 && pc.Body.PhysicsBody.Position.x < 80; ++i )
			scene.GameTick( 1.0 / 50 );
		var position = pc.Body.PhysicsBody.Position;
		if ( allowed )
		{
			Assert.IsTrue( position.x >= 80, $"rejected reachable landing at {position}" );
			Assert.AreEqual( height, position.z, 0.2f );
		}
		else
		{
			Assert.IsTrue( position.x < 44, $"passed blocked step at {position}" );
			Assert.IsTrue( position.z < 1, $"lifted onto blocked step at {position}" );
		}
	}

	[TestMethod]
	[DataRow( 0.5f, 110f )]
	[DataRow( 1f, 320f )]
	[DataRow( 8f, 40f )]
	public void SmallStepsPreserve2DMovement( float height, float speed )
	{
		var scene = new Scene { PhysicsMode = ScenePhysicsMode.Physics2D };
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateStep( scene, 48, 256, height, Rotation.Identity );
		var pc = CreatePlayer( scene, Vector2.Up );
		var movement = pc.Components.Create<PhysicsMovementRecorder>();
		movement.Controller = pc;
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = Vector3.Forward * speed;
		pc.Body.Velocity = pc.WishVelocity;
		for ( int i = 0; i < 2000 && pc.Body.PhysicsBody.Position.x < 80; ++i )
		{
			movement.Steps.Clear();
			scene.GameTick( 1.0 / 50 );
			foreach ( var step in movement.Steps )
			{
				Assert.IsTrue( step.After.x - step.Before.x >= step.InitialVelocity.x * step.Delta - 0.05f, $"2D movement clipped at {step.After}" );
				Assert.IsTrue( step.Grounded, $"2D support lost at {step.After}" );
				Assert.IsTrue( MathF.Abs( step.Velocity.y ) < 1, $"2D step launched at {step.After}" );
			}
		}
		Assert.IsTrue( pc.Body.PhysicsBody.Position.x >= 80 );
		Assert.AreEqual( -height, pc.Body.PhysicsBody.Position.y, 0.2f );
	}

	[TestMethod]
	[DataRow( 0f, false )]
	[DataRow( 25f, false )]
	[DataRow( 0f, true )]
	[DataRow( 25f, true )]
	public void AirborneWallSlidingDoesNotStepOrSnap( float yaw, bool prevented )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		CreateStairs( scene, 18, rotation, true );
		AddMovementBox( scene, new Vector3( 64, 48, 128 ), new Vector3( 512, 32, 512 ), rotation );
		var pc = CreatePlayer( scene, rotation * new Vector3( 24, 0, 1 ) );
		var towardWall = rotation * new Vector3( 0, 1, 0 );
		pc.WorldPosition += towardWall * (32 - pc.BodyCollider.Radius - 0.2f);
		TickUntilGrounded( scene, pc );
		var movement = pc.Components.Create<PhysicsMovementRecorder>();
		movement.Controller = pc;
		pc.WishVelocity = (rotation.Forward + towardWall * 0.5f) * 160;
		pc.Jump( Vector3.Up * 200 );
		if ( prevented )
			pc.PreventGrounding( 1 );
		var measured = 0;
		for ( int i = 0; i < 20 && measured < 5; ++i )
		{
			movement.Steps.Clear();
			scene.GameTick( 1.0 / 50 );
			foreach ( var step in movement.Steps )
			{
				++measured;
				Assert.IsFalse( step.Grounded );
				Assert.IsTrue( step.After.z > step.Before.z, $"step interrupted jump at {step.After}" );
			}
		}
		Assert.IsTrue( measured >= 5 );
	}

	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void ReversingAfterSlowStepReturnsToTheFloor( bool mesh )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateStairs( scene, 8, Rotation.Identity, mesh );
		var pc = CreatePlayer( scene, new Vector3( 39, 0, 1 ) );
		TickUntilGrounded( scene, pc );
		pc.WishVelocity = Vector3.Forward * 10;
		for ( int i = 0; i < 1000 && pc.Body.PhysicsBody.Position.z < 7; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.AreEqual( 8f, pc.Body.PhysicsBody.Position.z, 0.2f );
		pc.WishVelocity = Vector3.Backward * 10;
		for ( int i = 0; i < 1000 && pc.Body.PhysicsBody.Position.x > 36; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.IsOnGround );
			Assert.IsTrue( MathF.Abs( pc.Body.Velocity.z ) < 1 );
		}
		Assert.IsTrue( pc.Body.PhysicsBody.Position.x <= 36 );
		Assert.AreEqual( 0f, pc.Body.PhysicsBody.Position.z, 0.2f, "clearance lookahead retained unsupported step height" );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 25f )]
	public void DisabledSteppingDoesNotClimbRisers( float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		CreateStep( scene, 48, 256, 18, rotation );
		var pc = CreatePlayer( scene, Vector3.Up );
		pc.GetComponent<MoveModeWalk>().StepUpHeight = 0;
		TickUntilGrounded( scene, pc );
		pc.WishVelocity = rotation.Forward * 160;
		for ( int i = 0; i < 70; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.Body.PhysicsBody.Position.Dot( rotation.Forward ) < 44 );
		Assert.IsTrue( pc.Body.PhysicsBody.Position.z < 1 );
	}

	[TestMethod]
	[DataRow( false, 0f )]
	[DataRow( false, 25f )]
	[DataRow( true, 0f )]
	[DataRow( true, 25f )]
	public void StepLandingCannotPassUpperBodyObstructions( bool overhang, float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		CreateStep( scene, 48, 256, 8, rotation );
		AddMovementBox( scene, overhang ? new Vector3( 96, 0, 84 ) : new Vector3( 64, 0, 64 ),
			overhang ? new Vector3( 80, 256, 16 ) : new Vector3( 16, 256, 8 ), rotation );
		var pc = CreatePlayer( scene, Vector3.Up );
		TickUntilGrounded( scene, pc );
		pc.WishVelocity = rotation.Forward * 160;
		for ( int i = 0; i < 70; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.Body.PhysicsBody.Position.Dot( rotation.Forward ) < 56, $"passed upper-body obstruction at {pc.Body.PhysicsBody.Position}" );
			Assert.IsTrue( pc.Body.PhysicsBody.Position.z < 8.3f, $"lifted through upper-body obstruction at {pc.Body.PhysicsBody.Position}" );
		}
		Assert.IsTrue( pc.IsOnGround );
	}

	[TestMethod]
	[DataRow( 12f )]
	[DataRow( 18f )]
	public void ShortBodyStepsWithOnlyItsFeetCollider( float bodyHeight )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateStep( scene, 48, 256, 8, Rotation.Identity );
		AddMovementBox( scene, new Vector3( 96, 0, bodyHeight + 16.5f ), new Vector3( 512, 256, 16 ), Rotation.Identity );
		var pc = CreatePlayer( scene, Vector3.Up );
		pc.BodyHeight = bodyHeight;
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		Assert.IsFalse( pc.BodyCollider.Enabled );
		pc.WishVelocity = Vector3.Forward * 110;
		for ( int i = 0; i < 100 && pc.Body.PhysicsBody.Position.x < 80; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.Body.PhysicsBody.Position.x >= 80, $"short body stuck at {pc.Body.PhysicsBody.Position}" );
		Assert.AreEqual( 8f, pc.Body.PhysicsBody.Position.z, 0.2f );
	}

	[TestMethod]
	[DataRow( false, 30 )]
	[DataRow( false, 60 )]
	[DataRow( false, 144 )]
	[DataRow( false, 0 )]
	[DataRow( true, 30 )]
	[DataRow( true, 60 )]
	[DataRow( true, 144 )]
	[DataRow( true, 0 )]
	public void StairsPreserveMovementAcrossRenderCadences( bool mesh, int framesPerSecond )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateStairs( scene, 18, Rotation.Identity, mesh );
		var pc = CreatePlayer( scene, Vector3.Up );
		var movement = pc.Components.Create<PhysicsMovementRecorder>();
		movement.Controller = pc;
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		double[] jitter = [1.0 / 144, 1.0 / 30, 1.0 / 90, 1.0 / 20];
		foreach ( var descending in new[] { false, true } )
		{
			var direction = descending ? Vector3.Backward : Vector3.Forward;
			pc.WishVelocity = direction * 160;
			pc.Body.Velocity = pc.WishVelocity;
			var reachedEnd = false;
			var recordedSteps = 0;
			for ( int i = 0; i < 600; ++i )
			{
				movement.Steps.Clear();
				scene.GameTick( framesPerSecond == 0 ? jitter[i % jitter.Length] : 1.0 / framesPerSecond );
				foreach ( var step in movement.Steps )
				{
					++recordedSteps;
					Assert.IsTrue( step.Grounded, $"lost support at {step.After}" );
					Assert.IsTrue( (step.After - step.Before).Dot( direction ) >= step.InitialVelocity.Dot( direction ) * step.Delta - 0.05f,
						$"clipped progress at {step.Before} -> {step.After}" );
					Assert.IsTrue( MathF.Abs( step.Velocity.z ) < 1, $"launched at {step.After}" );
				}
				var position = pc.Body.PhysicsBody.Position;
				if ( descending ? position.x <= 12 : position.x >= 132 )
				{
					reachedEnd = true;
					break;
				}
			}
			Assert.IsTrue( recordedSteps > 0 );
			Assert.IsTrue( reachedEnd, $"did not finish {(descending ? "descent" : "ascent")} at {pc.Body.PhysicsBody.Position}" );
			Assert.AreEqual( descending ? 0f : 72f, pc.Body.PhysicsBody.Position.z, 0.2f );
		}
	}

	public static IEnumerable<object[]> ScaledStepLimitCases()
	{
		foreach ( var scale in new[] { 0.25f, 0.5f, 1f, 2f } )
			foreach ( var height in new[] { 7.5f, 8f, 9f } )
				yield return [scale, height];
	}

	[TestMethod]
	[DynamicData( nameof( ScaledStepLimitCases ), DynamicDataSourceType.Method )]
	public void CustomStepHeightIsRespectedAcrossScales( float scale, float height )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var step = CreateStep( scene, 48, 256, height, Rotation.Identity );
		step.WorldPosition *= scale;
		step.WorldScale = new Vector3( scale );
		var pc = CreatePlayer( scene, Vector3.Up );
		pc.WorldScale = new Vector3( scale );
		pc.GetComponent<MoveModeWalk>().StepUpHeight = 8;
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = Vector3.Forward * (160 * scale);
		for ( int i = 0; i < 600 && pc.Body.PhysicsBody.Position.x < 80 * scale; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.IsOnGround );
			Assert.IsTrue( MathF.Abs( pc.Body.Velocity.z ) < 1 );
		}
		var position = pc.Body.PhysicsBody.Position;
		if ( height <= 8 )
		{
			Assert.IsTrue( position.x >= 80 * scale, $"rejected configured step at {position}" );
			Assert.AreEqual( height * scale, position.z, 0.2f );
		}
		else
		{
			Assert.IsTrue( position.x < 44 * scale, $"climbed above configured step height at {position}" );
			Assert.AreEqual( 0f, position.z, 0.2f );
		}
	}

	[TestMethod]
	[DataRow( false, false, 110f )]
	[DataRow( true, false, 110f )]
	[DataRow( false, true, 110f )]
	[DataRow( true, true, 110f )]
	[DataRow( false, false, 320f )]
	[DataRow( true, false, 320f )]
	[DataRow( false, true, 320f )]
	[DataRow( true, true, 320f )]
	public void SceneSizedStairsSupportStationaryKeyframedColliders( bool keyframedSteps, bool keyframedFloor, float speed )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var origin = new Vector3( 995.606018f, 2597.80103f, -1635.552f );
		var floor = CreateFloor( scene );
		floor.WorldPosition += origin + new Vector3( 235.605499f, 501.801514f, 0 );
		floor.GetComponent<BoxCollider>().Static = !keyframedFloor;
		var stairs = scene.CreateObject();
		stairs.WorldPosition = origin + new Vector3( 235.605499f, 501.801514f, 279.713715f );
		stairs.WorldRotation = new Rotation( 0, 0, -0.707106709f, 0.707106829f );
		stairs.WorldScale = new Vector3( 2, 3, 0.2f );
		GameObject bottom = null;
		for ( int i = 0; i < 36; ++i )
		{
			var step = new GameObject( stairs, true );
			step.Name = $"Step {i}";
			var y = i < 13 ? -125.3332f + i * (16f / 3) : -58.66667f + (i - 13) * (8f / 3);
			step.LocalPosition = new Vector3( 0, y, -1400 + i * 40 );
			var collider = step.Components.Create<BoxCollider>();
			collider.Scale = new Vector3( 50 );
			collider.Static = !keyframedSteps;
			bottom ??= step;
		}
		var pc = CreatePlayer( scene, new Vector3( bottom.WorldPosition.x - 110, bottom.WorldPosition.y, origin.z + 1 ) );
		pc.BodyHeight = 64;
		var decisions = ObserveStepDecisions( pc );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = Vector3.Forward * speed;
		for ( int i = 0; i < 500 && pc.Body.PhysicsBody.Position.x < stairs.WorldPosition.x; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.Body.PhysicsBody.Position.x >= stairs.WorldPosition.x,
			$"stuck on scene stairs at {pc.Body.PhysicsBody.Position}, lift {decisions.MaxLift}: {DescribeGround( pc )}" );
		Assert.AreEqual( stairs.WorldPosition.z + 5, pc.Body.PhysicsBody.Position.z, 1f );
		Assert.IsTrue( pc.IsOnGround );
	}

	[TestMethod]
	[DataRow( false, 0f )]
	[DataRow( true, 0f )]
	[DataRow( false, 0.002f )]
	[DataRow( true, 0.002f )]
	public void ReleasingInputPreservesMomentumOnIce( bool keyframed, float friction )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var floor = CreateFloor( scene ).GetComponent<BoxCollider>();
		floor.Static = !keyframed;
		floor.Friction = friction;
		var pc = CreatePlayer( scene, Vector3.Up );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( friction, pc.GroundFriction, 0.0001f );
		pc.Body.Velocity = Vector3.Forward * 220;
		pc.WishVelocity = Vector3.Zero;
		for ( int i = 0; i < 30; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.IsOnGround );
		}
		Assert.IsTrue( pc.Body.Velocity.x > 176,
			$"ice stopped the player: velocity {pc.Body.Velocity}, damping {pc.Body.LinearDamping}, friction {pc.GroundFriction}" );
	}

	[TestMethod]
	[DataRow( false, 10f )]
	[DataRow( false, 40f )]
	[DataRow( true, 10f )]
	[DataRow( true, 40f )]
	public void IdlePlayerStaysAwakeAndResumesStepping( bool is2D, float speed )
	{
		var scene = new Scene { PhysicsMode = is2D ? ScenePhysicsMode.Physics2D : ScenePhysicsMode.Physics3D };
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateStep( scene, 48, 256, 8, Rotation.Identity );
		var up = is2D ? (Vector3)Vector2.Up : Vector3.Up;
		var pc = CreatePlayer( scene, Vector3.Forward * 39 + up );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		for ( int i = 0; i < 300; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsFalse( pc.Body.Sleeping, "idle player went to sleep" );
			Assert.IsTrue( pc.IsOnGround );
		}
		Assert.IsFalse( pc.Body.EnhancedCcd );
		pc.WishVelocity = Vector3.Forward * speed;
		for ( int i = 0; i < 2000 && pc.Body.PhysicsBody.Position.x < 80; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.Body.PhysicsBody.Position.x >= 80, $"could not resume after idling at {pc.Body.PhysicsBody.Position}" );
		Assert.AreEqual( 8f, pc.Body.PhysicsBody.Position.Dot( up ), 0.2f );
	}

	[TestMethod]
	[DataRow( 0.25f )]
	[DataRow( 0.5f )]
	[DataRow( 1f )]
	[DataRow( 2f )]
	public void DisabledStepDownFallsInsteadOfSnapping( float scale )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var floor = CreateFloor( scene );
		var ledge = CreateStep( scene, -256, 48, 18, Rotation.Identity );
		ledge.WorldPosition *= scale;
		ledge.WorldScale = new Vector3( scale );
		var pc = CreatePlayer( scene, Vector3.Up * (18 * scale + 1) );
		pc.WorldScale = new Vector3( scale );
		pc.GetComponent<MoveModeWalk>().StepDownHeight = 0;
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = Vector3.Forward * (160 * scale);
		var movement = pc.Components.Create<PhysicsMovementRecorder>();
		movement.Controller = pc;
		var fell = false;
		for ( int i = 0; i < 150 && !fell; ++i )
		{
			movement.Steps.Clear();
			scene.GameTick( 1.0 / 50 );
			foreach ( var step in movement.Steps )
			{
				if ( step.Grounded )
					continue;
				fell = true;
				Assert.IsTrue( step.After.z > 18 * scale - 1, $"snapped off ledge at {step.After}" );
				break;
			}
		}
		Assert.IsTrue( fell, "did not become airborne after leaving support" );
		TickUntilGrounded( scene, pc, 150 );
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( floor, pc.GroundObject );
	}

	[TestMethod]
	[DataRow( 0f, 0f, 0f )]
	[DataRow( 25f, 0f, 0f )]
	[DataRow( 0f, -1f, 0f )]
	[DataRow( 25f, -1f, 0f )]
	[DataRow( 0f, 0f, -1f )]
	[DataRow( 25f, 0f, -1f )]
	[DataRow( 0f, 0f, 1f )]
	[DataRow( 25f, 0f, 1f )]
	public void NearbyRiserDoesNotLiftStationaryOrDepartingPlayers( float yaw, float forward, float side )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		CreateStep( scene, 48, 256, 18, rotation );
		var pc = CreatePlayer( scene, rotation * new Vector3( 39, 0, 1 ) );
		var decisions = ObserveStepDecisions( pc );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = rotation * new Vector3( forward, side, 0 ) * 40;
		pc.Body.Velocity = pc.WishVelocity;
		for ( int i = 0; i < 120; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.AreEqual( 0f, pc.Body.PhysicsBody.Position.z, 0.2f, "nearby step lifted a player not moving into it" );
			Assert.IsTrue( MathF.Abs( pc.Body.Velocity.z ) < 1 );
			Assert.IsTrue( pc.IsOnGround );
		}
		AssertNoStepLift( decisions );
	}

	[TestMethod]
	[DataRow( false, 0f )]
	[DataRow( false, 25f )]
	[DataRow( true, 0f )]
	public void TriggerRisersNeverProvideStepSupport( bool is2D, float yaw )
	{
		var scene = new Scene { PhysicsMode = is2D ? ScenePhysicsMode.Physics2D : ScenePhysicsMode.Physics3D };
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		CreateStep( scene, 48, 256, 18, rotation ).GetComponent<BoxCollider>().IsTrigger = true;
		var up = is2D ? (Vector3)Vector2.Up : Vector3.Up;
		var pc = CreatePlayer( scene, up );
		var decisions = ObserveStepDecisions( pc );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = rotation.Forward * 110;
		for ( int i = 0; i < 100 && pc.Body.PhysicsBody.Position.Dot( rotation.Forward ) < 96; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.AreEqual( 0f, pc.Body.PhysicsBody.Position.Dot( up ), 0.2f, "trigger was used as a solid step" );
			Assert.IsTrue( pc.IsOnGround );
		}
		Assert.IsTrue( pc.Body.PhysicsBody.Position.Dot( rotation.Forward ) >= 96 );
		AssertNoStepLift( decisions );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 25f )]
	public void UpperBodyContactAloneDoesNotInventAStep( float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		AddMovementBox( scene, new Vector3( 64, 0, 64 ), new Vector3( 16, 256, 8 ), rotation );
		var pc = CreatePlayer( scene, Vector3.Up );
		var decisions = ObserveStepDecisions( pc );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = rotation.Forward * 160;
		for ( int i = 0; i < 100; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.AreEqual( 0f, pc.Body.PhysicsBody.Position.z, 0.2f, "upper-body contact caused a vertical lift" );
			Assert.IsTrue( pc.Body.PhysicsBody.Position.Dot( rotation.Forward ) < 56 );
			Assert.IsTrue( MathF.Abs( pc.Body.Velocity.z ) < 1 );
		}
		Assert.IsTrue( pc.IsOnGround );
		AssertNoStepLift( decisions );
	}

	[TestMethod]
	[DataRow( 36f, 0f )]
	[DataRow( 36f, 25f )]
	[DataRow( 128f, 0f )]
	[DataRow( 128f, 25f )]
	public void WallSlideFallbackCannotClimbTallRisers( float height, float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		CreateStep( scene, 48, 256, height, rotation );
		AddMovementBox( scene, new Vector3( 64, 48, 128 ), new Vector3( 512, 32, 512 ), rotation );
		var pc = CreatePlayer( scene, Vector3.Up );
		var decisions = ObserveStepDecisions( pc );
		pc.WorldPosition += rotation.Left * (32 - pc.BodyCollider.Radius - 0.2f);
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = (rotation.Forward + rotation.Left * 0.5f) * 160;
		for ( int i = 0; i < 120; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			var position = pc.Body.PhysicsBody.Position;
			Assert.AreEqual( 0f, position.z, 0.2f, "wall-slide fallback lifted onto a tall obstruction" );
			Assert.IsTrue( position.Dot( rotation.Forward ) < 44 );
			Assert.IsTrue( position.Dot( rotation.Left ) <= 32 - pc.BodyCollider.Radius + 0.25f );
		}
		AssertNoStepLift( decisions );
	}

	[TestMethod]
	[DataRow( false, 0f )]
	[DataRow( false, 25f )]
	[DataRow( true, 0f )]
	[DataRow( true, 25f )]
	public void WallSlideFallbackRequiresHeadroomForTheWholeBody( bool ducked, float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		CreateStep( scene, 48, 256, 8, rotation );
		AddMovementBox( scene, new Vector3( 64, 48, 128 ), new Vector3( 512, 32, 512 ), rotation );
		var pc = CreatePlayer( scene, Vector3.Up );
		var decisions = ObserveStepDecisions( pc );
		pc.IsDucking = ducked;
		var ceiling = pc.CurrentHeight + 7;
		AddMovementBox( scene, new Vector3( 96, 0, ceiling + 8 ), new Vector3( 512, 256, 16 ), rotation );
		pc.WorldPosition += rotation.Left * (32 - pc.BodyCollider.Radius - 0.2f);
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = (rotation.Forward + rotation.Left * 0.5f) * 160;
		for ( int i = 0; i < 100; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.AreEqual( 0f, pc.Body.PhysicsBody.Position.z, 0.2f, "lifted into insufficient headroom" );
			Assert.IsTrue( pc.Body.PhysicsBody.Position.Dot( rotation.Forward ) < 44 );
			Assert.IsTrue( pc.IsOnGround );
		}
		AssertNoStepLift( decisions );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 25f )]
	public void GroundingPreventionRejectsStepsEvenWhenTouchingTheFloor( float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		CreateStep( scene, 48, 256, 18, rotation );
		var pc = CreatePlayer( scene, Vector3.Up );
		var decisions = ObserveStepDecisions( pc );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.PreventGrounding( 5 );
		pc.WishVelocity = rotation.Forward * 110;
		for ( int i = 0; i < 80; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.Body.PhysicsBody.Position.z < 1, "stepped while grounding was explicitly prevented" );
			Assert.IsTrue( pc.Body.PhysicsBody.Position.Dot( rotation.Forward ) < 44 );
			Assert.IsFalse( pc.IsOnGround );
		}
		AssertNoStepLift( decisions );
	}

	[TestMethod]
	[DataRow( 60f, 45f, 0f )]
	[DataRow( 60f, 45f, 25f )]
	[DataRow( 45f, 30f, 0f )]
	[DataRow( 45f, 30f, 25f )]
	public void StepCannotLandOnASurfaceAboveTheGroundAngleLimit( float slope, float groundAngle, float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		var top = 8 + 64 * MathF.Tan( slope * MathF.PI / 180 );
		Vector3[] vertices = [
			new( 48, -128, 8 ), new( 112, -128, top ), new( 112, 128, top ), new( 48, 128, 8 ),
			new( 48, -128, 0 ), new( 48, 128, 0 )
		];
		int[] indices = [0, 1, 2, 0, 2, 3, 4, 3, 5, 4, 0, 3];
		var obstacle = scene.CreateObject();
		obstacle.WorldRotation = rotation;
		var collider = obstacle.Components.Create<ModelCollider>();
		collider.Model = Model.Builder.AddCollisionMesh( vertices, indices ).Create();
		collider.Static = true;
		var pc = CreatePlayer( scene, Vector3.Up );
		var decisions = ObserveStepDecisions( pc );
		decisions.GroundAngle = groundAngle;
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = rotation.Forward * 110;
		for ( int i = 0; i < 100; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.Body.PhysicsBody.Position.z < 1, "lifted onto an unstandable landing" );
			Assert.IsTrue( pc.Body.PhysicsBody.Position.Dot( rotation.Forward ) < 44 );
		}
		Assert.IsTrue( pc.IsOnGround );
		AssertNoStepLift( decisions );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 25f )]
	public void StepDecisionRecorderObservesAValidLift( float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		CreateStep( scene, 48, 256, 8, rotation );
		var pc = CreatePlayer( scene, Vector3.Up );
		var decisions = ObserveStepDecisions( pc );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = rotation.Forward * 110;
		for ( int i = 0; i < 100 && pc.Body.PhysicsBody.Position.Dot( rotation.Forward ) < 80; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.Body.PhysicsBody.Position.Dot( rotation.Forward ) >= 80 );
		Assert.IsTrue( decisions.Decisions > 0 );
		Assert.IsTrue( decisions.MaxLift > 7, $"probe did not observe the valid lift: {decisions.MaxLift}" );
	}
}
