using System;

namespace SceneTests.Components;

public partial class PlayerControllerTest
{
	static Rigidbody CreateOverlappingHullPlayerSupport( Scene scene, bool seesaw, float pitch, float yaw, float overlap, float rise )
	{
		CreateFloor( scene );
		var heading = Rotation.FromYaw( yaw );
		if ( seesaw )
			AddMovementBox( scene, Vector3.Up * 96, new Vector3( 32, 256, 64 ), heading );
		var platform = scene.CreateObject();
		platform.WorldPosition = Vector3.Up * 136;
		platform.WorldRotation = heading * Rotation.FromPitch( pitch );
		var body = platform.Components.Create<Rigidbody>();
		body.Gravity = seesaw;
		body.PhysicsBody.AutoSleep = false;
		if ( !seesaw )
		{
			var locking = body.Locking;
			locking.X = locking.Y = locking.Z = true;
			locking.Pitch = locking.Yaw = locking.Roll = true;
			body.Locking = locking;
		}
		var halfLength = 128 + overlap * 0.25f;
		for ( int side = -1; side <= 1; side += 2 )
		{
			var child = new GameObject( platform, true );
			child.LocalPosition = new Vector3( side * (256 - halfLength), 0, side > 0 ? rise : 0 );
			var hull = child.Components.Create<HullCollider>( false );
			hull.Type = HullCollider.PrimitiveType.Points;
			hull.Points = [
				new( -halfLength, -64, -8 ), new( halfLength, -64, -8 ),
				new( halfLength, 64, -8 ), new( -halfLength, 64, -8 ),
				new( -halfLength, -64, 8 ), new( halfLength, -64, 8 ),
				new( halfLength, 64, 8 ), new( -halfLength, 64, 8 )
			];
			hull.Enabled = true;
		}
		body.MassOverride = 2000;
		Assert.AreEqual( PhysicsBodyType.Dynamic, body.PhysicsBody.BodyType );
		return body;
	}

	[TestMethod]
	[DataRow( false, 0f, 0f, 64f, 1f )]
	[DataRow( false, 0f, 25f, 2f, -1f )]
	[DataRow( false, 20f, 25f, 64f, 1f )]
	[DataRow( false, -20f, 25f, 64f, -1f )]
	[DataRow( true, 0f, 0f, 64f, 1f )]
	[DataRow( true, 0f, 25f, 64f, -1f )]
	[DataRow( true, 0f, 25f, 2f, 1f )]
	public void OverlappingDynamicHullSeamsDoNotBlockOrLiftPlayers( bool seesaw, float pitch, float yaw, float overlap, float direction )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = CreateOverlappingHullPlayerSupport( scene, seesaw, pitch, yaw, overlap, 0 );
		var heading = Rotation.FromYaw( yaw ).Forward * direction;
		var start = body.PhysicsBody.Transform.PointToWorld( new Vector3( -180 * direction, 0, 8 ) );
		var pc = CreatePlayer( scene, start + Vector3.Up * 8 );
		var decisions = ObserveStepDecisions( pc );
		for ( int i = 0; i < 30; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = heading * 110;
		var reachedEnd = false;
		for ( int i = 0; i < 400; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.IsOnGround, $"lost compound support at {pc.Body.PhysicsBody.Position}: {DescribeGround( pc )}" );
			Assert.AreEqual( body.GameObject, pc.GroundObject );
			var local = body.PhysicsBody.Transform.PointToLocal( pc.Body.PhysicsBody.Position );
			Assert.IsTrue( MathF.Abs( local.y ) < 2, $"deflected at overlapping hull seam: {local}" );
			if ( local.x * direction < 160 )
				continue;
			reachedEnd = true;
			break;
		}
		Assert.IsTrue( reachedEnd, $"blocked by an overlapping hull seam at {body.PhysicsBody.Transform.PointToLocal( pc.Body.PhysicsBody.Position )}, velocity {pc.Body.Velocity}, lift {decisions.MaxLift}" );
		AssertNoStepLift( decisions );
	}

	[TestMethod]
	[DataRow( 0.001f, 0f, 0f, false )]
	[DataRow( 0.01f, 25f, 0f, false )]
	[DataRow( 0.1f, 0f, 0f, false )]
	[DataRow( 1f, 0f, 0f, false )]
	[DataRow( 8f, 0f, 0f, false )]
	[DataRow( 18f, 25f, 0f, false )]
	[DataRow( 8f, 25f, 20f, false )]
	[DataRow( 8f, 25f, -20f, false )]
	[DataRow( 8f, 0f, 0f, true )]
	[DataRow( 8f, 25f, 0f, true )]
	public void RealStepsOnOverlappingDynamicHullsRemainClimbable( float rise, float yaw, float pitch, bool seesaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = CreateOverlappingHullPlayerSupport( scene, seesaw, pitch, yaw, 64, rise );
		var pc = CreatePlayer( scene, body.PhysicsBody.Transform.PointToWorld( new Vector3( -180, 0, 8 ) ) + Vector3.Up * 8 );
		var decisions = ObserveStepDecisions( pc );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = Rotation.FromYaw( yaw ).Forward * 110;
		for ( int i = 0; i < 400 && body.PhysicsBody.Transform.PointToLocal( pc.Body.PhysicsBody.Position ).x < 160; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.IsOnGround, $"lost support on a real dynamic step: {DescribeGround( pc )}" );
			Assert.AreEqual( body.GameObject, pc.GroundObject );
		}
		var local = body.PhysicsBody.Transform.PointToLocal( pc.Body.PhysicsBody.Position );
		Assert.IsTrue( local.x >= 160, $"blocked by a real compound step at {local}, velocity {pc.Body.Velocity}, lift {decisions.MaxLift}" );

		float HeightError()
		{
			var position = body.PhysicsBody.Transform.PointToLocal( pc.Body.PhysicsBody.Position );
			var normal = body.PhysicsBody.Rotation.Up;
			var feetSupport = 8 * (MathF.Abs( normal.x ) + MathF.Abs( normal.y ));
			return position.z - (8 + rise + feetSupport);
		}

		if ( seesaw )
		{
			pc.WishVelocity = Vector3.Zero;
			var stableTicks = 0;
			for ( int i = 0; i < 50 && stableTicks < 5; ++i )
			{
				scene.GameTick( 1.0 / 50 );
				Assert.IsTrue( pc.IsOnGround, $"lost support while settling on a dynamic step: {DescribeGround( pc )}" );
				Assert.AreEqual( body.GameObject, pc.GroundObject );
				stableTicks = MathF.Abs( HeightError() ) <= 0.5f ? stableTicks + 1 : 0;
			}
			Assert.AreEqual( 5, stableTicks, $"feet did not settle on a dynamic step: height error {HeightError()}" );
		}

		Assert.AreEqual( 0, HeightError(), 0.5f );
		Assert.IsTrue( pc.IsOnGround );
	}

	[TestMethod]
	[DataRow( 19f, false )]
	[DataRow( 8f, true )]
	public void DynamicHullStepsRespectHeightAndHeadroom( float rise, bool ceiling )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = CreateOverlappingHullPlayerSupport( scene, false, 0, 0, 64, rise );
		if ( ceiling )
			AddMovementBox( scene, new Vector3( 0, 0, 228 ), new Vector3( 512, 128, 16 ), Rotation.Identity );
		var pc = CreatePlayer( scene, new Vector3( -180, 0, 145 ) );
		var decisions = ObserveStepDecisions( pc );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = Vector3.Forward * 110;
		for ( int i = 0; i < 200; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.IsOnGround );
			Assert.IsTrue( pc.Body.PhysicsBody.Position.x < -39, $"entered an obstructed dynamic step: {pc.Body.PhysicsBody.Position}" );
		}
		AssertNoStepLift( decisions );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 25f )]
	public void DynamicHullStepsRemainClimbableWhileSlidingAlongAWall( float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = CreateOverlappingHullPlayerSupport( scene, false, 0, yaw, 64, 8 );
		var heading = Rotation.FromYaw( yaw );
		AddMovementBox( scene, new Vector3( 0, 24, 192 ), new Vector3( 512, 16, 256 ), heading );
		var pc = CreatePlayer( scene, heading * new Vector3( -180, 4.5f, 145 ) );
		var decisions = ObserveStepDecisions( pc );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = heading * new Vector3( 110, 55, 0 );
		for ( int i = 0; i < 400 && body.PhysicsBody.Transform.PointToLocal( pc.Body.PhysicsBody.Position ).x < 160; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			var localPosition = heading.Inverse * pc.Body.PhysicsBody.Position;
			Assert.IsTrue( localPosition.y < 5, $"entered the side wall at {localPosition}" );
			Assert.IsTrue( pc.IsOnGround, $"lost dynamic wall-step support at {i}: {localPosition}, lift {decisions.MaxLift}: {DescribeGround( pc )}" );
		}
		var local = body.PhysicsBody.Transform.PointToLocal( pc.Body.PhysicsBody.Position );
		Assert.IsTrue( local.x >= 160, $"stalled at a dynamic step beside a wall: {local}, velocity {pc.Body.Velocity}" );
		Assert.AreEqual( 16f, local.z, 0.5f );
	}

	[TestMethod]
	[DataRow( false, -1f, false )]
	[DataRow( false, 1f, false )]
	[DataRow( true, -1f, false )]
	[DataRow( true, 1f, false )]
	[DataRow( false, -1f, true )]
	[DataRow( false, 1f, true )]
	[DataRow( true, -1f, true )]
	[DataRow( true, 1f, true )]
	public void CrossingTheSceneCompoundSeesawDoesNotCatchItsNarrowOverlap( bool seesaw, float direction, bool acrossBeam )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var origin = new Vector3( 1161.026f, 320.253906f, -1588.95801f );
		CreateFloor( scene ).WorldPosition += new Vector3( origin.x, origin.y, -1635.552f );
		if ( seesaw )
			AddMovementBox( scene, origin - Vector3.Up * 40, new Vector3( 32, 128, 64 ), Rotation.Identity );
		var board = scene.CreateObject();
		board.WorldPosition = origin;
		board.WorldRotation = new Rotation( -3.60088812e-8f, 0.0590275712f, -1.9211841e-9f, 0.99825573f );
		board.WorldScale = new Vector3( 5, 2, 0.2f );
		var body = board.Components.Create<Rigidbody>();
		body.Gravity = seesaw;
		if ( !seesaw )
		{
			var locking = body.Locking;
			locking.X = locking.Y = locking.Z = true;
			locking.Pitch = locking.Yaw = locking.Roll = true;
			body.Locking = locking;
		}
		board.Components.Create<BoxCollider>();
		var beam = new GameObject( board, true );
		beam.LocalPosition = new Vector3( -12.4371901f, 3.03390503f, 0.00569343613f );
		beam.LocalScale = new Vector3( 1, 0.2f, 1 );
		beam.Components.Create<BoxCollider>();
		body.MassOverride = 8192;
		var localStart = acrossBeam
			? new Vector3( -40, -35 * direction, 5 )
			: new Vector3( direction > 0 ? -160 : 90, 6.06781f, 5 );
		var start = body.PhysicsBody.Transform.PointToWorld( localStart );
		var hit = scene.Trace.Ray( start + Vector3.Up * 128, start - Vector3.Up * 128 ).Run();
		Assert.AreEqual( board, hit.Body.GameObject );
		var pc = CreatePlayer( scene, hit.EndPosition + Vector3.Up );
		pc.BodyHeight = 64;
		var decisions = ObserveStepDecisions( pc );
		for ( int i = 0; i < 30; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = (acrossBeam ? Vector3.Left : Vector3.Forward) * (110 * direction);
		var reachedEnd = false;
		for ( int i = 0; i < 400; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.IsOnGround, $"lost scene compound support at {body.PhysicsBody.Transform.PointToLocal( pc.Body.PhysicsBody.Position )}, tilt {Vector3.GetAngle( body.PhysicsBody.Rotation.Up, Vector3.Up )}, lift {decisions.MaxLift}: {DescribeGround( pc )}" );
			Assert.AreEqual( board, pc.GroundObject );
			var local = body.PhysicsBody.Transform.PointToLocal( pc.Body.PhysicsBody.Position );
			var reached = acrossBeam ? local.y * direction >= 35 : direction > 0 ? local.x >= 90 : local.x <= -160;
			if ( !reached )
				continue;
			reachedEnd = true;
			break;
		}
		Assert.IsTrue( reachedEnd, $"caught the scene compound overlap at {body.PhysicsBody.Transform.PointToLocal( pc.Body.PhysicsBody.Position )}, velocity {pc.Body.Velocity}, lift {decisions.MaxLift}" );
		Assert.IsTrue( decisions.MaxLift < 0.5f, $"false step on a nearly coplanar overlap: {decisions.MaxLift}" );
	}

	static Rigidbody CreatePlayerSeesaw( Scene scene, float mass, float yaw )
	{
		CreateFloor( scene );
		var heading = Rotation.FromYaw( yaw );
		AddMovementBox( scene, Vector3.Up * 96, new Vector3( 32, 256, 64 ), heading );
		var platform = scene.CreateObject();
		platform.WorldPosition = Vector3.Up * 136;
		platform.WorldRotation = heading;
		var body = platform.Components.Create<Rigidbody>();
		body.MassOverride = mass;
		body.PhysicsBody.AutoSleep = false;
		platform.Components.Create<BoxCollider>().Scale = new Vector3( 1024, 256, 16 );
		Assert.AreEqual( PhysicsBodyType.Dynamic, body.PhysicsBody.BodyType );
		Assert.AreEqual( mass, body.Mass, 0.01f );
		return body;
	}

	[TestMethod]
	[DataRow( 20f, 0f, 100f )]
	[DataRow( -20f, 0f, 100f )]
	[DataRow( 35f, 25f, 100f )]
	[DataRow( -35f, 25f, 100f )]
	[DataRow( 20f, 0f, 320f )]
	[DataRow( -20f, 0f, 320f )]
	[DataRow( 35f, 0f, 40f )]
	[DataRow( -35f, 0f, 40f )]
	public void WalkingOnDynamicSlopesDoesNotAccumulateSpeed( float pitch, float yaw, float speed )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var platform = scene.CreateObject();
		platform.WorldPosition = Vector3.Up * 128;
		var heading = Rotation.FromYaw( yaw );
		platform.WorldRotation = heading * Rotation.FromPitch( pitch );
		var body = platform.Components.Create<Rigidbody>();
		body.Gravity = false;
		body.MassOverride = 1000;
		var locking = body.Locking;
		locking.X = true;
		locking.Y = true;
		locking.Z = true;
		locking.Pitch = true;
		locking.Yaw = true;
		locking.Roll = true;
		body.Locking = locking;
		platform.Components.Create<BoxCollider>().Scale = new Vector3( 2048, 512, 16 );
		Assert.AreEqual( PhysicsBodyType.Dynamic, body.PhysicsBody.BodyType );
		var start = heading.Forward * -256;
		var surface = scene.Trace.Ray( start + Vector3.Up * 1024, start - Vector3.Up * 1024 ).Run();
		Assert.IsTrue( surface.Hit );
		var pc = CreatePlayer( scene, surface.EndPosition + Vector3.Up * 8 );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		Assert.IsTrue( pc.GroundIsDynamic );
		var movement = pc.Components.Create<PhysicsMovementRecorder>();
		movement.Controller = pc;
		pc.WishVelocity = heading.Forward * speed;
		for ( int i = 0; i < 120; ++i )
		{
			movement.Steps.Clear();
			scene.GameTick( 1.0 / 50 );
			foreach ( var step in movement.Steps )
			{
				Assert.IsTrue( step.Grounded, $"lost dynamic slope at {step.After}" );
				var normal = pc.GroundNormal;
				var integrated = step.InitialVelocity + scene.PhysicsWorld.Gravity * (pc.Body.GravityScale * step.Delta);
				var before = integrated - normal * integrated.Dot( normal );
				Assert.IsTrue( before.Length <= speed * 1.01f,
					$"dynamic slope accelerated before physics: {step.InitialVelocity}, tangent speed {before.Length}, wish {speed}" );
				if ( i < 40 )
					continue;
				var after = step.Velocity - normal * step.Velocity.Dot( normal );
				Assert.IsTrue( after.Dot( heading.Forward ) > 0.5f, $"walk input moved backwards on a dynamic slope: {after}" );
				Assert.IsTrue( after.Length >= (speed < 60 ? 1 : speed * 0.8f) && after.Length <= speed * 1.2f,
					$"inconsistent dynamic slope speed: {after.Length}, wish {speed}, velocity {step.Velocity}" );
			}
		}
	}

	[TestMethod]
	[DataRow( 1000f, 0f, 100f, 1f )]
	[DataRow( 2000f, 25f, 160f, 1f )]
	[DataRow( 1000f, 0f, 100f, -1f )]
	public void WalkingAcrossASeesawKeepsSpeedRelativeToItsMovingSurface( float mass, float yaw, float speed, float direction )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var heading = Rotation.FromYaw( yaw );
		var body = CreatePlayerSeesaw( scene, mass, yaw );
		var platform = body.GameObject;
		var start = heading.Forward * (-128 * direction);
		var surface = scene.Trace.Ray( start + Vector3.Up * 1024, start - Vector3.Up * 1024 ).Run();
		Assert.IsTrue( surface.Hit );
		Assert.AreEqual( platform, surface.GameObject );
		var pc = CreatePlayer( scene, surface.EndPosition + Vector3.Up * 8 );
		var decisions = ObserveStepDecisions( pc );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		start = body.PhysicsBody.Transform.PointToLocal( pc.Body.PhysicsBody.Position );
		pc.WishVelocity = heading.Forward * (speed * direction);
		var maxTilt = 0f;
		for ( int i = 0; i < 140; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.IsOnGround, $"lost seesaw support: {DescribeGround( pc )}" );
			Assert.AreEqual( platform, pc.GroundObject );
			var normal = body.PhysicsBody.Rotation.Up;
			maxTilt = MathF.Max( maxTilt, Vector3.GetAngle( normal, Vector3.Up ) );
			var supportVelocity = body.GetVelocityAtPoint( pc.Body.PhysicsBody.Position );
			Assert.IsTrue( (pc.GroundVelocity - supportVelocity).Length < 0.001f,
				$"incorrect moving ground reference: {pc.GroundVelocity}, actual {supportVelocity}" );
			var relative = pc.Body.Velocity - supportVelocity;
			var tangent = relative - normal * relative.Dot( normal );
			Assert.IsTrue( tangent.Length <= speed * 1.2f,
				$"seesaw accelerated walking to {tangent.Length}, wish {speed}, player {pc.Body.Velocity}, platform {body.Velocity}" );
			if ( i >= 40 )
				Assert.IsTrue( tangent.Length >= speed * 0.7f, $"seesaw slowed walking to {tangent.Length}, wish {speed}" );
		}
		var end = body.PhysicsBody.Transform.PointToLocal( pc.Body.PhysicsBody.Position );
		Assert.IsTrue( (end - start).x * direction > 160, $"did not walk across seesaw: {end - start}" );
		Assert.IsTrue( maxTilt > 3, $"seesaw did not tip: {maxTilt}" );
		pc.WishVelocity = Vector3.Zero;
		for ( int i = 0; i < 200; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.IsOnGround, $"lost seesaw support after stopping: {DescribeGround( pc )}" );
			Assert.AreEqual( platform, pc.GroundObject );
		}
		Assert.IsTrue( body.PhysicsBody.Rotation.Up.Dot( heading.Forward ) * direction > 0.05f,
			$"standing player did not tip the seesaw toward their new side: {body.PhysicsBody.Rotation}" );
		AssertNoStepLift( decisions );
	}

	static float[] MeasureSeesawLoading( bool player, float mass, float offset, float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = CreatePlayerSeesaw( scene, mass, yaw );
		body.MotionEnabled = false;
		var position = Rotation.FromYaw( yaw ).Forward * offset + Vector3.Up * 145;
		Rigidbody loadBody;
		if ( player )
		{
			loadBody = CreatePlayer( scene, position ).Body;
		}
		else
		{
			var load = scene.CreateObject();
			load.WorldPosition = position;
			var passive = load.Components.Create<Rigidbody>();
			loadBody = passive;
			passive.MassOverride = 500;
			passive.PhysicsBody.AutoSleep = false;
			passive.LinearDamping = 0.1f;
			passive.OverrideMassCenter = true;
			passive.MassCenterOverride = Vector3.Zero;
			var loadLocking = passive.Locking;
			loadLocking.Pitch = true;
			loadLocking.Yaw = true;
			loadLocking.Roll = true;
			passive.Locking = loadLocking;
			var collider = load.Components.Create<BoxCollider>();
			collider.Scale = new Vector3( 16, 16, 72 );
			collider.Center = Vector3.Up * 36;
			collider.Friction = 101;
		}
		for ( int i = 0; i < 30; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.AreEqual( 500f, loadBody.Mass, 0.01f );
		body.MotionEnabled = true;
		body.Sleeping = false;
		Assert.AreEqual( mass, body.Mass, 0.01f );
		var speeds = new float[20];
		for ( int i = 0; i < speeds.Length; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			speeds[i] = body.PhysicsBody.AngularVelocity.Length;
		}
		return speeds;
	}

	[TestMethod]
	[DataRow( 500f, 128f, 0f )]
	[DataRow( 1000f, 128f, 0f )]
	[DataRow( 1000f, -128f, 0f )]
	[DataRow( 5000f, 256f, 25f )]
	public void StandingPlayersLoadASeesawLikeAnEqualMassPhysicsBody( float mass, float offset, float yaw )
	{
		var reference = MeasureSeesawLoading( false, mass, offset, yaw );
		var actual = MeasureSeesawLoading( true, mass, offset, yaw );
		for ( int i = 5; i < actual.Length; ++i )
		{
			var expectedAcceleration = reference[i] - reference[i - 1];
			var actualAcceleration = actual[i] - actual[i - 1];
			Assert.IsTrue( expectedAcceleration > 0.0001f, $"reference weight did not accelerate the seesaw at {i}: {expectedAcceleration}" );
			Assert.IsTrue( actualAcceleration >= expectedAcceleration * 0.85f && actualAcceleration <= expectedAcceleration * 1.15f,
				$"inconsistent player weight at sample {i}: seesaw acceleration {actualAcceleration}, equal-mass reference {expectedAcceleration}" );
		}
	}

	static Rigidbody CreateSceneSizedPlayerSeesaw( Scene scene, float yaw )
	{
		CreateFloor( scene );
		var parent = scene.CreateObject();
		parent.WorldPosition = new Vector3( -0.001969594f, 276.310486f, -5.195557f );
		parent.WorldRotation = Rotation.FromYaw( yaw );
		parent.WorldScale = new Vector3( 2, 1, 2 );
		var rotation = new Rotation( 0.5f, 0.5f, 0.5f, 0.5f );
		var pivot = new GameObject( parent, true );
		pivot.LocalPosition = new Vector3( 0.001575289f, 130.028305f, 14.9571505f );
		pivot.LocalRotation = rotation;
		pivot.LocalScale = new Vector3( 0.1f, 0.2f, 1.333333f );
		pivot.Components.Create<BoxCollider>();
		var board = new GameObject( parent, true );
		board.LocalPosition = new Vector3( 0.000201998f, 130.029099f, 19.748051f );
		board.LocalRotation = rotation;
		board.LocalScale = new Vector3( 3, 0.2f, 1 );
		var body = board.Components.Create<Rigidbody>();
		board.Components.Create<BoxCollider>();
		return body;
	}

	[TestMethod]
	[DataRow( 1f, 0f )]
	[DataRow( -1f, 0f )]
	[DataRow( 1f, 25f )]
	[DataRow( -1f, 25f )]
	public void IdlePlayerLoadsAndStaysOnTheSceneSizedSeesaw( float side, float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = CreateSceneSizedPlayerSeesaw( scene, yaw );
		var direction = Rotation.FromYaw( yaw ).Left * side;
		var start = body.PhysicsBody.Position + direction * 124;
		var hit = scene.Trace.Ray( start + Vector3.Up * 128, start - Vector3.Up * 128 ).Run();
		Assert.IsTrue( hit.Hit );
		Assert.AreEqual( body.GameObject, hit.GameObject );
		var pc = CreatePlayer( scene, hit.EndPosition + Vector3.Up );
		pc.BodyHeight = 64;
		for ( int i = 0; i < 30; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.IsOnGround );
		Assert.IsTrue( body.Mass > 9000 && body.Mass < 11000, $"unexpected scene plank mass {body.Mass}" );
		var relative = body.PhysicsBody.Transform.PointToLocal( pc.Body.PhysicsBody.Position );
		var tilt = 0f;
		for ( int i = 0; i < 300; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.IsOnGround, $"lost scene seesaw at {i}: {DescribeGround( pc )}" );
			Assert.AreEqual( body.GameObject, pc.GroundObject );
			var local = body.PhysicsBody.Transform.PointToLocal( pc.Body.PhysicsBody.Position );
			Assert.IsTrue( MathF.Abs( local.x - relative.x ) < 2, $"slid along scene seesaw at {i}: {local - relative}" );
			var normal = body.PhysicsBody.Rotation * Vector3.Left;
			tilt = MathF.Max( tilt, normal.Dot( direction ) );
		}
		Assert.IsTrue( tilt > 0.05f, $"player did not load scene seesaw: tilt {tilt}" );
	}

	[TestMethod]
	[DataRow( 1f, 0f, 110f )]
	[DataRow( -1f, 0f, 110f )]
	[DataRow( 1f, 25f, 110f )]
	[DataRow( -1f, 25f, 110f )]
	[DataRow( 1f, 25f, 320f )]
	[DataRow( -1f, 25f, 320f )]
	public void CrossingStoppingAndReversingOnTheSceneSizedSeesaw( float direction, float yaw, float speed )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = CreateSceneSizedPlayerSeesaw( scene, yaw );
		var heading = Rotation.FromYaw( yaw ).Left;
		var start = body.PhysicsBody.Position - heading * (124 * direction);
		var hit = scene.Trace.Ray( start + Vector3.Up * 128, start - Vector3.Up * 128 ).Run();
		Assert.AreEqual( body.GameObject, hit.GameObject );
		var pc = CreatePlayer( scene, hit.EndPosition + Vector3.Up );
		pc.BodyHeight = 64;
		var decisions = ObserveStepDecisions( pc );
		for ( int i = 0; i < 60; ++i )
			scene.GameTick( 1.0 / 50 );
		for ( int crossing = 0; crossing < 3; ++crossing )
		{
			pc.WishVelocity = heading * (speed * direction);
			for ( int i = 0; i < 300; ++i )
			{
				scene.GameTick( 1.0 / 50 );
				Assert.IsTrue( pc.IsOnGround, $"lost scene seesaw on crossing {crossing} at {i}: {DescribeGround( pc )}" );
				Assert.AreEqual( body.GameObject, pc.GroundObject );
				var local = body.PhysicsBody.Transform.PointToLocal( pc.Body.PhysicsBody.Position );
				if ( local.x * direction >= 120 )
					break;
				if ( i == 299 )
					Assert.Fail( $"stalled crossing scene seesaw: {local}, velocity {pc.Body.Velocity}" );
			}
			pc.WishVelocity = Vector3.Zero;
			for ( int i = 0; i < 300; ++i )
			{
				scene.GameTick( 1.0 / 50 );
				Assert.IsTrue( pc.IsOnGround, $"lost scene seesaw after crossing {crossing}: {DescribeGround( pc )}" );
				Assert.AreEqual( body.GameObject, pc.GroundObject );
			}
			var normal = body.PhysicsBody.Rotation * Vector3.Left;
			Assert.IsTrue( normal.Dot( heading ) * direction > 0.05f,
				$"player did not load their side after crossing {crossing}: normal {normal}, relative position {body.PhysicsBody.Transform.PointToLocal( pc.Body.PhysicsBody.Position )}" );
			direction = -direction;
		}
		AssertNoStepLift( decisions );
	}
}
