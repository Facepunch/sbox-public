using Sandbox.Movement;
using System;

namespace SceneTests.Components;

public partial class PlayerControllerTest
{
	[TestMethod]
	[DataRow( "collider", 24f )]
	[DataRow( "collider", 96f )]
	[DataRow( "object", 24f )]
	[DataRow( "object", 96f )]
	[DataRow( "destroy", 24f )]
	[DataRow( "destroy", 96f )]
	public void RemovingSupportUngroundsWithoutSnappingToTheFloor( string removal, float height )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var floor = CreateFloor( scene );
		var support = CreateStep( scene, -256, 256, height, Rotation.Identity );
		var pc = CreatePlayer( scene, Vector3.Up * (height + 1) );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( support, pc.GroundObject );
		var movement = pc.Components.Create<PhysicsMovementRecorder>();
		movement.Controller = pc;
		switch ( removal )
		{
			case "collider": support.GetComponent<BoxCollider>().Enabled = false; break;
			case "object": support.Enabled = false; break;
			case "destroy": support.Destroy(); break;
			default: Assert.Fail( $"unknown removal: {removal}" ); break;
		}
		if ( removal == "destroy" )
		{
			for ( int i = 0; i < 10 && support.IsValid(); ++i )
				scene.GameTick( 1.0 / 50 );
			Assert.IsFalse( support.IsValid(), "support destruction was not processed" );
			movement.Steps.Clear();
		}
		var departureHeight = pc.Body.PhysicsBody.Position.z;
		for ( int i = 0; i < 10 && movement.Steps.Count == 0; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( movement.Steps.Count > 0 );
		Assert.IsFalse( movement.Steps[0].Grounded, "retained a support that no longer participates in physics" );
		Assert.IsTrue( movement.Steps[0].After.z >= departureHeight - 1, $"snapped to a distant floor: {movement.Steps[0].After}" );
		Assert.IsTrue( movement.Steps[0].After.z <= height + 1, "removing support launched the player" );
		TickUntilGrounded( scene, pc, 150 );
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( floor, pc.GroundObject );
		Assert.AreEqual( Vector3.Zero, pc.GroundVelocity );
	}

	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	public void TeleportingAwayFromSupportDoesNotReuseItsGrounding( bool is2D, bool sideways )
	{
		var scene = new Scene { PhysicsMode = is2D ? ScenePhysicsMode.Physics2D : ScenePhysicsMode.Physics3D };
		using var scope = scene.Push();
		var floor = CreateFloor( scene );
		var support = CreateStep( scene, -256, 256, 64, Rotation.Identity );
		var up = is2D ? (Vector3)Vector2.Up : Vector3.Up;
		var pc = CreatePlayer( scene, up * 65 );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( support, pc.GroundObject );
		var target = up * 100 + (sideways ? Vector3.Left : Vector3.Forward) * 320;
		pc.WorldPosition = target;
		pc.Body.Velocity = Vector3.Zero;
		var decisions = ObserveStepDecisions( pc );
		var movement = pc.Components.Create<PhysicsMovementRecorder>();
		movement.Controller = pc;
		for ( int i = 0; i < 10 && movement.Steps.Count == 0; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( movement.Steps.Count > 0 );
		Assert.IsFalse( movement.Steps[0].Grounded );
		Assert.IsTrue( (movement.Steps[0].After - target).Length < 1, "teleport was moved back toward stale support" );
		TickUntilGrounded( scene, pc, 180 );
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( floor, pc.GroundObject );
		AssertNoStepLift( decisions );
	}

	[TestMethod]
	[DataRow( false, 0f )]
	[DataRow( false, 25f )]
	[DataRow( true, 0f )]
	[DataRow( true, 25f )]
	public void ReversingPartwayUpStairsKeepsGroundAndReturnsToTheFloor( bool mesh, float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		CreateStairs( scene, 18, rotation, mesh );
		var pc = CreatePlayer( scene, Vector3.Up );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = rotation.Forward * 160;
		for ( int i = 0; i < 150 && pc.Body.PhysicsBody.Position.Dot( rotation.Forward ) < 90; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.Body.PhysicsBody.Position.Dot( rotation.Forward ) >= 90 );
		Assert.IsTrue( pc.Body.PhysicsBody.Position.z >= 36 );
		var movement = pc.Components.Create<PhysicsMovementRecorder>();
		movement.Controller = pc;
		pc.WishVelocity = rotation.Backward * 160;
		for ( int i = 0; i < 200 && pc.Body.PhysicsBody.Position.Dot( rotation.Forward ) > 12; ++i )
		{
			movement.Steps.Clear();
			scene.GameTick( 1.0 / 50 );
			foreach ( var step in movement.Steps )
			{
				Assert.IsTrue( step.Grounded, $"lost support after reversal at {step.After}" );
				Assert.IsTrue( MathF.Abs( step.Velocity.z ) < 1, $"launched after reversal at {step.After}" );
				Assert.IsTrue( (step.After - step.Before).Dot( rotation.Backward ) >= step.InitialVelocity.Dot( rotation.Backward ) * step.Delta - 0.05f,
					$"clipped the return path at {step.Before} -> {step.After}" );
			}
		}
		Assert.IsTrue( pc.Body.PhysicsBody.Position.Dot( rotation.Forward ) <= 12 );
		Assert.AreEqual( 0f, pc.Body.PhysicsBody.Position.z, 0.2f );
	}

	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void JumpingIntoACeilingDoesNotTunnelOrRemainGrounded( bool is2D, bool ducked )
	{
		var scene = new Scene { PhysicsMode = is2D ? ScenePhysicsMode.Physics2D : ScenePhysicsMode.Physics3D };
		using var scope = scene.Push();
		var floor = CreateFloor( scene );
		var up = is2D ? (Vector3)Vector2.Up : Vector3.Up;
		var pc = CreatePlayer( scene, up );
		pc.IsDucking = ducked;
		AddMovementBox( scene, up * (pc.CurrentHeight + 24),
			is2D ? new Vector3( 512, 16, 100 ) : new Vector3( 512, 512, 16 ), Rotation.Identity );
		var decisions = ObserveStepDecisions( pc );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		var movement = pc.Components.Create<PhysicsMovementRecorder>();
		movement.Controller = pc;
		pc.Jump( up * 300 );
		Assert.IsFalse( pc.IsOnGround );
		for ( int i = 0; i < 100; ++i )
			scene.GameTick( 1.0 / 50 );
		var rose = false;
		var fell = false;
		foreach ( var step in movement.Steps )
		{
			var height = step.After.Dot( up );
			Assert.IsTrue( height <= 16.5f, $"head passed through ceiling at {step.After}" );
			rose |= height > 5;
			fell |= step.Velocity.Dot( up ) < -1;
		}
		Assert.IsTrue( rose, "jump never reached the ceiling" );
		Assert.IsTrue( fell, "ceiling contact did not allow falling back down" );
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( floor, pc.GroundObject );
		AssertNoStepLift( decisions );
	}

	[TestMethod]
	[DataRow( 8f, 110f )]
	[DataRow( 8f, 320f )]
	[DataRow( 18f, 110f )]
	[DataRow( 18f, 320f )]
	public void JumpingOntoAStepLandsWithoutAnAdditionalStepLift( float height, float speed )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var landing = CreateStep( scene, 48, 512, height, Rotation.Identity );
		var pc = CreatePlayer( scene, Vector3.Forward * 16 + Vector3.Up );
		var decisions = ObserveStepDecisions( pc );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = Vector3.Forward * speed;
		pc.Body.Velocity = pc.WishVelocity;
		pc.Jump( Vector3.Up * 200 );
		var landed = false;
		for ( int i = 0; i < 150; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			if ( pc.GroundObject != landing )
				continue;
			landed = true;
			break;
		}
		Assert.IsTrue( landed, $"did not land on the step: {pc.Body.PhysicsBody.Position}" );
		AssertNoStepLift( decisions );
		pc.WishVelocity = Vector3.Zero;
		for ( int i = 0; i < 30; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( landing, pc.GroundObject );
		Assert.AreEqual( height, pc.Body.PhysicsBody.Position.z, 0.2f );
	}

	[TestMethod]
	[DataRow( false, 8f, 110f, 1f, 1f, 0f )]
	[DataRow( true, 8f, 110f, 1f, 1f, 0f )]
	[DataRow( true, 8f, 320f, 1f, 1f, 0f )]
	[DataRow( true, 18f, 110f, 1f, 1f, 0f )]
	[DataRow( true, 8f, 110f, 0.5f, 1f, 0f )]
	[DataRow( true, 8f, 110f, 2f, 1f, 0f )]
	[DataRow( true, 8f, 110f, 1f, -1f, 0f )]
	[DataRow( true, 8f, 110f, 1f, 1f, 12f )]
	public void RestoringGroundingAllowsPreviouslyRejectedSteps( bool is2D, float height, float speed, float scale, float direction, float headroom )
	{
		var scene = new Scene { PhysicsMode = is2D ? ScenePhysicsMode.Physics2D : ScenePhysicsMode.Physics3D };
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateStep( scene, MathF.Min( 48 * direction, 256 * direction ) * scale,
			MathF.Max( 48 * direction, 256 * direction ) * scale, height * scale, Rotation.Identity );
		var up = is2D ? (Vector3)Vector2.Up : Vector3.Up;
		var pc = CreatePlayer( scene, (Vector3.Forward * (24 * direction) + up) * scale );
		pc.WorldScale = new Vector3( scale );
		var decisions = ObserveStepDecisions( pc );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.PreventGrounding( 5 );
		pc.WishVelocity = Vector3.Forward * (speed * direction);
		for ( int i = 0; i < 30; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsFalse( pc.IsOnGround );
		AssertNoStepLift( decisions );
		if ( headroom > 0 )
			AddMovementBox( scene, Vector3.Forward * (48 * direction * scale) + up * ((pc.CurrentHeight + headroom + 4) * scale),
				new Vector3( 256 * scale, 8 * scale, 100 ), Rotation.Identity );
		pc.RestoreGrounding();
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		for ( int i = 0; i < 100 && pc.Body.PhysicsBody.Position.x * direction < 80 * scale; ++i )
			scene.GameTick( 1.0 / 50 );
		if ( pc.Body.PhysicsBody.Position.x * direction < 80 * scale )
		{
			var feet = pc.FeetCollider;
			var center = feet.WorldTransform.PointToWorld( feet.Center );
			var extents = feet.Scale * feet.WorldScale.Abs() * 0.5f - new Vector3( 0.05f );
			var probe = scene.Trace.Box( new BBox( -extents, extents ), center, center + Vector3.Forward * (4 * direction) )
				.Rotated( feet.WorldRotation )
				.IgnoreGameObjectHierarchy( pc.GameObject )
				.Run();
			Assert.Fail( $"step stayed disabled after restoring grounding at {pc.Body.PhysicsBody.Position}, velocity {pc.Body.Velocity}, "
				+ $"grounded {pc.IsOnGround}, mode {pc.Mode}, planner lift {decisions.MaxLift}, "
				+ $"forward probe hit {probe.Hit}, started solid {probe.StartedSolid}, normal {probe.Normal}, collider {probe.Collider}" );
		}
		Assert.AreEqual( height * scale, pc.Body.PhysicsBody.Position.Dot( up ), 0.2f );
		Assert.IsTrue( decisions.MaxLift > height * scale - 1 );
	}

	[TestMethod]
	[DataRow( "ceiling" )]
	[DataRow( "compound-ceiling" )]
	[DataRow( "tall" )]
	[DataRow( "too-high" )]
	[DataRow( "disabled" )]
	[DataRow( "departing" )]
	public void Restoring2DGroundingDoesNotBypassStepRejections( string rejection )
	{
		var scene = new Scene { PhysicsMode = ScenePhysicsMode.Physics2D };
		using var scope = scene.Push();
		CreateFloor( scene );
		var height = rejection switch { "tall" => 36f, "too-high" => 18.5f, _ => 8f };
		var riser = CreateStep( scene, 48, 256, height, Rotation.Identity );
		var up = (Vector3)Vector2.Up;
		var pc = CreatePlayer( scene, Vector3.Forward * 24 + up );
		var decisions = ObserveStepDecisions( pc );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.PreventGrounding( 5 );
		pc.WishVelocity = Vector3.Forward * 110;
		for ( int i = 0; i < 30; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsFalse( pc.IsOnGround );
		AssertNoStepLift( decisions );

		switch ( rejection )
		{
			case "ceiling":
				AddMovementBox( scene, Vector3.Forward * 48 + up * (pc.CurrentHeight + 8),
					new Vector3( 256, 8, 100 ), Rotation.Identity );
				break;
			case "compound-ceiling":
				var ceiling = riser.Components.Create<BoxCollider>();
				ceiling.Center = riser.WorldTransform.PointToLocal( Vector3.Forward * 48 + up * (pc.CurrentHeight + 8) );
				ceiling.Scale = new Vector3( 256, 8, 100 );
				ceiling.Static = true;
				break;
			case "disabled":
				decisions.StepUpHeight = 0;
				break;
			case "departing":
				pc.WishVelocity = Vector3.Backward * 110;
				pc.Body.Velocity = pc.WishVelocity;
				break;
		}

		pc.RestoreGrounding();
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		for ( int i = 0; i < 60; ++i )
			scene.GameTick( 1.0 / 50 );
		AssertNoStepLift( decisions );
		Assert.IsTrue( pc.Body.PhysicsBody.Position.x < 48, "passed through a rejected step" );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void DuckingThroughATunnelCanStepAndStandAfterTheExit( bool is2D )
	{
		var scene = new Scene { PhysicsMode = is2D ? ScenePhysicsMode.Physics2D : ScenePhysicsMode.Physics3D };
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateStep( scene, 48, 512, 8, Rotation.Identity );
		var up = is2D ? (Vector3)Vector2.Up : Vector3.Up;
		AddMovementBox( scene, Vector3.Forward * 48 + up * 56,
			is2D ? new Vector3( 96, 16, 100 ) : new Vector3( 96, 256, 16 ), Rotation.Identity );
		var pc = CreatePlayer( scene, up );
		pc.IsDucking = true;
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = Vector3.Forward * 110;
		for ( int i = 0; i < 150 && pc.Body.PhysicsBody.Position.x < 80; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.Body.PhysicsBody.Position.x >= 80 );
		pc.UpdateDucking( false );
		Assert.IsTrue( pc.IsDucking, "stood up inside the tunnel" );
		for ( int i = 0; i < 150 && pc.Body.PhysicsBody.Position.x < 128; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.Body.PhysicsBody.Position.x >= 128 );
		pc.UpdateDucking( false );
		Assert.IsFalse( pc.IsDucking, $"could not stand after the exit, headroom {pc.Headroom}" );
		for ( int i = 0; i < 10; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( 8f, pc.Body.PhysicsBody.Position.Dot( up ), 0.2f );
		Assert.IsTrue( pc.BodyCollider.Enabled );
	}

	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void DuckingInTheAirPreservesHeadPositionAndCanReland( bool is2D, bool ceiling )
	{
		var scene = new Scene { PhysicsMode = is2D ? ScenePhysicsMode.Physics2D : ScenePhysicsMode.Physics3D };
		using var scope = scene.Push();
		var floor = CreateFloor( scene );
		var up = is2D ? (Vector3)Vector2.Up : Vector3.Up;
		if ( ceiling )
			AddMovementBox( scene, up * 108, is2D ? new Vector3( 512, 16, 100 ) : new Vector3( 512, 512, 16 ), Rotation.Identity );
		var pc = CreatePlayer( scene, up );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.Jump( up * 200 );
		for ( int i = 0; i < 10 && pc.Body.PhysicsBody.Position.Dot( up ) < 5; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.Body.PhysicsBody.Position.Dot( up ) >= 5 );
		Assert.IsTrue( pc.IsAirborne );
		var head = pc.WorldPosition + up * pc.CurrentHeight;
		pc.UpdateDucking( true );
		Assert.IsTrue( pc.IsDucking );
		Assert.IsTrue( (pc.WorldPosition + up * pc.CurrentHeight - head).Length < 0.01f, "ducking moved the head instead of lifting the feet" );
		pc.UpdateDucking( false );
		Assert.IsTrue( pc.IsDucking, "unducked while airborne" );
		TickUntilGrounded( scene, pc, 150 );
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( floor, pc.GroundObject );
		for ( int i = 0; i < 10; ++i )
			scene.GameTick( 1.0 / 50 );
		pc.UpdateDucking( false );
		Assert.IsFalse( pc.IsDucking );
		Assert.IsTrue( pc.IsOnGround );
	}

	[TestMethod]
	[DataRow( false, 0.5f )]
	[DataRow( false, 1f )]
	[DataRow( true, 0.5f )]
	[DataRow( true, 1f )]
	public void RecreatingThePlayerBodyDisablesEnhancedCcdAndSleeping( bool is2D, float scale )
	{
		var scene = new Scene { PhysicsMode = is2D ? ScenePhysicsMode.Physics2D : ScenePhysicsMode.Physics3D };
		using var scope = scene.Push();
		CreateFloor( scene );
		var up = is2D ? (Vector3)Vector2.Up : Vector3.Up;
		var pc = CreatePlayer( scene, up );
		pc.WorldScale = new Vector3( scale );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		var previous = pc.Body.PhysicsBody;
		pc.Body.EnhancedCcd = true;
		pc.Body.Enabled = false;
		scene.GameTick( 1.0 / 50 );
		pc.Body.Enabled = true;
		for ( int i = 0; i < 300; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.Body.PhysicsBody.IsValid() );
			Assert.IsFalse( pc.Body.Sleeping, "recreated player body fell asleep" );
			Assert.IsFalse( pc.Body.EnhancedCcd );
		}
		Assert.IsFalse( previous.IsValid(), "test did not recreate the native body" );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = Vector3.Forward * 40;
		var start = pc.Body.PhysicsBody.Position;
		for ( int i = 0; i < 150; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.Body.PhysicsBody.Position.x > start.x + 8, "recreated body could not move" );
	}

	[TestMethod]
	[DataRow( 40f, 0f, 0f )]
	[DataRow( 0f, 40f, 0f )]
	[DataRow( 0f, 0f, 40f )]
	[DataRow( 0f, 0f, -40f )]
	public void IdlePlayerFollowsAPlatformWithoutRelativeDrift( float x, float y, float z )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var platform = scene.CreateObject();
		platform.WorldPosition = Vector3.Up * 120;
		var body = platform.Components.Create<Rigidbody>();
		body.MotionEnabled = false;
		var collider = platform.Components.Create<BoxCollider>();
		collider.Scale = new Vector3( 256, 256, 16 );
		var pc = CreatePlayer( scene, Vector3.Up * 129 );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( platform, pc.GroundObject );
		var velocity = new Vector3( x, y, z );
		body.Velocity = velocity;
		for ( int i = 0; i < 30; ++i )
			scene.GameTick( 1.0 / 50 );
		var relative = pc.Body.PhysicsBody.Position - body.PhysicsBody.Position;
		for ( int i = 0; i < 60; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.IsOnGround, $"lost moving platform at {pc.Body.PhysicsBody.Position}" );
			Assert.AreEqual( platform, pc.GroundObject );
			Assert.IsTrue( (pc.Body.PhysicsBody.Position - body.PhysicsBody.Position - relative).Length < 0.5f,
				$"drifted relative to platform: {pc.Body.PhysicsBody.Position - body.PhysicsBody.Position}, expected {relative}" );
			Assert.IsTrue( (pc.GroundVelocity - velocity).Length < 0.5f, $"incorrect platform velocity: {pc.GroundVelocity}" );
			Assert.IsTrue( pc.Velocity.Length < 0.5f, $"idle player has relative velocity: {pc.Velocity}" );
		}
	}

	public sealed class AcceleratingPlayerPlatform : Component, IScenePhysicsEvents
	{
		public bool Running { get; set; }
		public bool Rotate { get; set; }
		public Rigidbody PlatformBody { get; set; }
		float elapsed;

		void IScenePhysicsEvents.PrePhysicsStep()
		{
			if ( !Running )
				return;

			elapsed += Time.Delta;
			var phase = elapsed * 1.8f;
			var travel = 1 - MathF.Cos( phase );
			var position = new Vector3( 120 * travel, 80 * travel, 128 + 20 * travel );
			var rotation = Rotation.FromYaw( Rotate ? 35 * travel : 0 );
			if ( PlatformBody.IsValid() )
			{
				PlatformBody.PhysicsBody.Move( new Transform( position, rotation ), Time.Delta );
			}
			else
			{
				WorldPosition = position;
				WorldRotation = rotation;
			}
		}
	}

	[TestMethod]
	[DataRow( false, false, 0.5f, true, false )]
	[DataRow( false, true, 0.5f, true, false )]
	[DataRow( true, false, 0.5f, true, false )]
	[DataRow( true, true, 0.5f, true, false )]
	[DataRow( false, false, 0f, false, false )]
	[DataRow( true, false, 0f, false, false )]
	[DataRow( false, false, 0.001f, false, false )]
	[DataRow( true, false, 0.001f, false, false )]
	[DataRow( false, false, 0.5f, true, true )]
	[DataRow( false, true, 0.5f, true, true )]
	public void IdlePlayersStayOnAcceleratingAndTurningPlatformsWhenFrictionAllows( bool rigidbody, bool rotate, float friction, bool grips, bool passive )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var platform = scene.CreateObject();
		platform.WorldPosition = Vector3.Up * 128;
		var motion = platform.Components.Create<AcceleratingPlayerPlatform>();
		motion.Rotate = rotate;
		if ( rigidbody )
		{
			motion.PlatformBody = platform.Components.Create<Rigidbody>();
			motion.PlatformBody.MotionEnabled = false;
		}
		var child = new GameObject( platform, true );
		child.LocalRotation = Rotation.FromYaw( 15 );
		child.LocalScale = new Vector3( 2, 2, 0.1f );
		var collider = child.Components.Create<BoxCollider>();
		collider.Friction = friction;
		var hit = scene.Trace.Ray( Vector3.Up * 256, Vector3.Zero ).Run();
		Assert.IsTrue( hit.Hit );
		var support = hit.Body;
		Assert.AreEqual( PhysicsBodyType.Keyframed, support.BodyType );
		var pc = CreatePlayer( scene, hit.EndPosition + Vector3.Up + Vector3.Forward * 16 );
		pc.BodyHeight = 64;
		var decisions = ObserveStepDecisions( pc );
		for ( int i = 0; i < 30; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( friction, pc.GroundFriction, 0.001f );
		Assert.IsFalse( pc.Body.EnhancedCcd );
		motion.Running = true;
		if ( passive )
		{
			for ( int i = 0; i < 10; ++i )
				scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.Body.Gravity, "passive control must retain native support contacts" );
			pc.Enabled = false;
			pc.Body.LinearDamping = 0;
		}
		var relative = support.Transform.PointToLocal( pc.Body.PhysicsBody.Position );
		var maxDrift = 0f;
		for ( int i = 0; i < 400; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			var drift = (support.Transform.PointToLocal( pc.Body.PhysicsBody.Position ) - relative).Length;
			maxDrift = MathF.Max( maxDrift, drift );
			if ( !grips )
				continue;
			if ( !passive )
			{
				Assert.IsTrue( pc.IsOnGround, $"lost animated platform at {i}, drift {drift}, velocity {pc.Body.Velocity}, ground {pc.GroundVelocity}" );
				Assert.AreEqual( platform, rigidbody ? pc.GroundObject : pc.GroundObject.Parent );
			}
			Assert.IsTrue( drift < 2, $"slid on an animated platform at {i}: {drift}, relative {support.Transform.PointToLocal( pc.Body.PhysicsBody.Position ) - relative}, lift {decisions.MaxLift}, velocity {pc.Body.Velocity}, ground {pc.GroundVelocity}, gravity {pc.Body.Gravity}, damping {pc.Body.LinearDamping}, friction {pc.FeetCollider.Friction}" );
		}
		if ( !grips )
			Assert.IsTrue( maxDrift > 16, "low-friction platform incorrectly attached the player" );
		else
			AssertNoStepLift( decisions );
	}
}
