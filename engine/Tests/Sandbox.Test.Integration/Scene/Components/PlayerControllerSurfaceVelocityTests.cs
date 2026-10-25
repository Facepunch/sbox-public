using System;

namespace SceneTests.Components;

public partial class PlayerControllerTest
{
	static BoxCollider CreatePlayerConveyor( Scene scene, bool rigidbody, float yaw, bool scaled )
	{
		var belt = scene.CreateObject();
		belt.WorldPosition = Vector3.Down * 25;
		belt.WorldRotation = Rotation.FromYaw( yaw );
		belt.WorldScale = scaled ? new Vector3( 3, 20, 1 ) : Vector3.One;
		if ( rigidbody )
		{
			var body = belt.Components.Create<Rigidbody>();
			body.MotionEnabled = false;
			body.Gravity = false;
		}
		var collider = belt.Components.Create<BoxCollider>();
		collider.Static = true;
		collider.Scale = scaled ? new Vector3( 50 ) : new Vector3( 512, 1024, 50 );
		collider.SurfaceVelocity = new Vector3( 0, -100, 0 );
		return collider;
	}

	[TestMethod]
	[DataRow( false, 0f, false, 0f )]
	[DataRow( true, 0f, false, 0f )]
	[DataRow( true, -26f, true, 0f )]
	[DataRow( false, 90f, true, 0f )]
	[DataRow( true, -26f, true, 20f )]
	public void ConveyorSurfaceVelocityCarriesIdlePlayersThroughPhysics( bool rigidbody, float yaw, bool scaled, float platformSpeed )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var belt = CreatePlayerConveyor( scene, rigidbody, yaw, scaled );
		var surfaceVelocity = belt.WorldRotation * belt.SurfaceVelocity;
		var direction = surfaceVelocity.Normal;
		if ( platformSpeed != 0 )
			belt.Rigidbody.Velocity = direction * platformSpeed;
		var pc = CreatePlayer( scene, Vector3.Up );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		var start = pc.Body.PhysicsBody.Position;
		for ( int i = 0; i < 60; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.IsOnGround );
		}
		var groundVelocity = direction * platformSpeed;
		var expected = surfaceVelocity + groundVelocity;
		Assert.IsTrue( (pc.GroundVelocity - groundVelocity).Length < 0.5f, $"incorrect platform velocity: {pc.GroundVelocity}, expected {groundVelocity}" );
		Assert.IsTrue( (pc.Body.Velocity - expected).Length < 0.5f, $"conveyor movement {pc.Body.Velocity}, expected {expected}" );
		var displacement = pc.Body.PhysicsBody.Position - start;
		Assert.IsTrue( MathF.Abs( displacement.Dot( Vector3.Up.Cross( direction ) ) ) < 0.5f, $"conveyor deflected player: {displacement}" );
		Assert.IsTrue( displacement.Dot( direction ) >= expected.Length * 0.75f - 0.5f, $"conveyor did not transport player: {displacement}" );
	}

	[TestMethod]
	public void ConveyorUsesTheContactedShapeAndItsPhysicsBodyFrame()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var belt = CreatePlayerConveyor( scene, true, -26, false );
		var body = belt.Rigidbody;
		belt.Enabled = false;
		var child = new GameObject( belt.GameObject, true );
		child.LocalRotation = Rotation.FromYaw( 35 );
		var contact = child.Components.Create<BoxCollider>();
		contact.Scale = new Vector3( 512, 1024, 50 );
		contact.SurfaceVelocity = new Vector3( 0, -100, 0 );
		var other = belt.GameObject.Components.Create<BoxCollider>();
		other.Center = Vector3.Down * 128;
		other.Scale = new Vector3( 512, 1024, 50 );
		other.SurfaceVelocity = new Vector3( 200, 0, 0 );
		var pc = CreatePlayer( scene, Vector3.Up );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		for ( int i = 0; i < 60; ++i )
			scene.GameTick( 1.0 / 50 );
		var expected = body.WorldRotation * contact.SurfaceVelocity;
		Assert.AreEqual( Vector3.Zero, pc.GroundVelocity );
		Assert.IsTrue( (pc.Body.Velocity - expected).Length < 0.5f );
	}

	[TestMethod]
	public void ConveyorIgnoresSurfaceVelocityIntoTheGround()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var belt = CreatePlayerConveyor( scene, true, -26, true );
		belt.SurfaceVelocity += Vector3.Up * 500;
		var pc = CreatePlayer( scene, Vector3.Up );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		for ( int i = 0; i < 60; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( 0f, pc.GroundVelocity.z, 0.01f );
		Assert.AreEqual( 0f, pc.Body.Velocity.z, 0.5f );
		Assert.AreEqual( 0f, pc.Body.PhysicsBody.Position.z, 0.2f );
		Assert.AreEqual( 100f, pc.Body.Velocity.Length, 0.5f );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( -1f )]
	public void ConveyorVelocityChangesAreAppliedWhileStandingOnIt( float multiplier )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var belt = CreatePlayerConveyor( scene, true, -26, true );
		var pc = CreatePlayer( scene, Vector3.Up );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		for ( int i = 0; i < 30; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.AreEqual( 100f, pc.Body.Velocity.Length, 0.5f );
		belt.SurfaceVelocity *= multiplier;
		for ( int i = 0; i < 60; ++i )
			scene.GameTick( 1.0 / 50 );
		var expected = belt.WorldRotation * belt.SurfaceVelocity;
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( Vector3.Zero, pc.GroundVelocity );
		Assert.IsTrue( (pc.Body.Velocity - expected).Length < 0.5f );
	}

	[TestMethod]
	public void JumpingFromAConveyorRetainsMomentumWithoutFurtherSurfaceTransport()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var belt = CreatePlayerConveyor( scene, true, -26, true );
		var pc = CreatePlayer( scene, Vector3.Up );
		TickUntilGrounded( scene, pc );
		for ( int i = 0; i < 30; ++i )
			scene.GameTick( 1.0 / 50 );
		var expected = belt.WorldRotation * belt.SurfaceVelocity;
		Assert.IsTrue( (pc.Body.Velocity - expected).Length < 0.5f );
		pc.Jump( Vector3.Up * 300 );
		belt.SurfaceVelocity *= 10;
		for ( int i = 0; i < 3; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsFalse( pc.IsOnGround );
		Assert.AreEqual( Vector3.Zero, pc.GroundVelocity );
		Assert.IsTrue( (pc.WithoutVertical( pc.Body.Velocity ) - expected).Length < 2, $"incorrect conveyor takeoff velocity: {pc.Body.Velocity}" );
	}

	[TestMethod]
	public void ConveyorsDoNotPushPlayersThroughWalls()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreatePlayerConveyor( scene, true, 0, false );
		AddMovementBox( scene, new Vector3( 0, -64, 64 ), new Vector3( 512, 16, 128 ), Rotation.Identity );
		var pc = CreatePlayer( scene, Vector3.Up );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		for ( int i = 0; i < 100; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.IsOnGround );
			Assert.IsTrue( pc.Body.PhysicsBody.Position.y > -56, $"belt bypassed a wall: {pc.Body.PhysicsBody.Position}" );
		}
		Assert.IsTrue( pc.Body.PhysicsBody.Position.y < -20, "belt did not carry the player into the wall" );
	}

	[TestMethod]
	public void FrictionlessConveyorsDoNotAccelerateIdlePlayers()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var belt = CreatePlayerConveyor( scene, true, 0, false );
		belt.Friction = 0;
		var pc = CreatePlayer( scene, Vector3.Up );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		for ( int i = 0; i < 60; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.IsOnGround );
		Assert.IsTrue( pc.WithoutVertical( pc.Body.PhysicsBody.Position ).Length < 0.5f );
		Assert.IsTrue( pc.WithoutVertical( pc.Body.Velocity ).Length < 0.5f );
	}

	[TestMethod]
	public void ConveyorTransportMatchesPhysicsObjectsIn2D()
	{
		var scene = new Scene { PhysicsMode = ScenePhysicsMode.Physics2D };
		using var scope = scene.Push();
		var floor = CreateFloor( scene );
		floor.GetComponent<BoxCollider>().SurfaceVelocity = Vector3.Forward * 100;
		var payload = scene.CreateObject();
		payload.WorldPosition = new Vector3( 128, -8, 0 );
		var body = payload.Components.Create<Rigidbody>();
		payload.Components.Create<BoxCollider>().Scale = new Vector3( 16 );
		var pc = CreatePlayer( scene, new Vector3( -128, -1, 0 ) );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		for ( int i = 0; i < 60; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( MathF.Abs( body.Velocity.x ) > 50, "control physics object was not transported" );
		Assert.IsTrue( MathF.Abs( pc.Body.Velocity.x - body.Velocity.x ) < 0.5f,
			$"player and physics conveyor motion disagree: player {pc.Body.Velocity}, object {body.Velocity}" );
	}

	[TestMethod]
	[DataRow( 500f, 0f, 100f, 0f )]
	[DataRow( 1000f, -26f, 100f, 40f )]
	[DataRow( 10000f, 90f, -100f, -20f )]
	[DataRow( 1000000f, -26f, 100f, 0f )]
	public void DynamicConveyorsCarryStandingPlayersWhileReactingToContact( float mass, float yaw, float surfaceSpeed, float platformSpeed )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var floor = CreateFloor( scene );
		floor.WorldPosition = Vector3.Down * 100;
		floor.GetComponent<BoxCollider>().Friction = 0;
		var belt = CreatePlayerConveyor( scene, true, yaw, false );
		belt.Static = false;
		belt.SurfaceVelocity = new Vector3( 0, -surfaceSpeed, 0 );
		var body = belt.Rigidbody;
		body.MassOverride = mass;
		body.Gravity = true;
		body.MotionEnabled = true;
		var direction = body.WorldRotation * belt.SurfaceVelocity.Normal;
		body.Velocity = direction * platformSpeed;
		var pc = CreatePlayer( scene, Vector3.Up );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		Assert.IsTrue( pc.GroundIsDynamic );
		Assert.AreEqual( PhysicsBodyType.Dynamic, body.PhysicsBody.BodyType );
		var start = body.PhysicsBody.Transform.PointToLocal( pc.Body.PhysicsBody.Position );
		for ( int i = 0; i < 60; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.IsOnGround, $"lost dynamic conveyor: {DescribeGround( pc )}" );
			Assert.AreEqual( body.GameObject, pc.GroundObject );
		}
		var relativeVelocity = pc.Body.Velocity - body.GetVelocityAtPoint( pc.Body.PhysicsBody.Position );
		var expected = body.PhysicsBody.Rotation * belt.SurfaceVelocity;
		Assert.IsTrue( (relativeVelocity - expected).Length < 1,
			$"dynamic conveyor transport {relativeVelocity}, expected {expected}, body {body.Velocity}, player {pc.Body.Velocity}" );
		var end = body.PhysicsBody.Transform.PointToLocal( pc.Body.PhysicsBody.Position );
		Assert.IsTrue( (end - start).Dot( belt.SurfaceVelocity.Normal ) > 50, $"player did not travel along dynamic conveyor: {end - start}" );
		if ( mass <= 10000 )
			Assert.IsTrue( body.Velocity.Dot( direction ) < platformSpeed - 1, "dynamic conveyor did not recoil from contact" );
	}
}
