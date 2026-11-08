using Sandbox.Movement;
using System;

namespace SceneTests.Components;

public partial class PlayerControllerTest
{
	[TestMethod]
	[DataRow( 80.5f )]
	[DataRow( 81f )]
	[DataRow( 84f )]
	public void DescendingLowStepsKeepsGroundUnderLowCeilings( float ceilingHeight )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateStep( scene, -64, 0, 8, Rotation.Identity );
		AddMovementBox( scene, Vector3.Up * (ceilingHeight + 16), new Vector3( 256, 256, 32 ), Rotation.Identity );
		var pc = CreatePlayer( scene, new Vector3( -40, 0, 8.2f ) );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = Vector3.Forward * 160;
		for ( int tick = 0; tick < 90 && pc.WorldPosition.x < 48; ++tick )
		{
			scene.GameTick( 0.015 );
			Assert.IsTrue( pc.IsOnGround, $"Lost support at {pc.WorldPosition}" );
		}
		Assert.IsTrue( pc.WorldPosition.x >= 48 );
		Assert.AreEqual( 0f, pc.WorldPosition.z, 1f );
	}

	[TestMethod]
	[DataRow( 1.99f, true, false )]
	[DataRow( 2f, false, false )]
	[DataRow( 2.01f, false, false )]
	[DataRow( 1.99f, true, true )]
	[DataRow( 2f, false, true )]
	[DataRow( 2.01f, false, true )]
	public void StaticGroundProbeHasStrictTwoUnitReach( float height, bool expectedGrounded, bool previouslyGrounded )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var pc = CreatePlayer( scene, Vector3.Up * 0.5f );
		if ( previouslyGrounded )
		{
			((IScenePhysicsEvents)pc.Body).PrePhysicsStep();
			((IScenePhysicsEvents)pc).PostPhysicsStep();
			Assert.IsTrue( pc.IsOnGround );
		}
		pc.Body.WorldPosition = Vector3.Up * height;
		((IScenePhysicsEvents)pc.Body).PrePhysicsStep();
		((IScenePhysicsEvents)pc).PostPhysicsStep();
		Assert.AreEqual( expectedGrounded, pc.IsOnGround );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 25f )]
	[DataRow( 90f )]
	public void RepeatedAirborneWallContactsDoNotAccumulateSeparation( float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.PhysicsWorld.Gravity = Vector3.Zero;
		var rotation = Rotation.FromYaw( yaw );
		CreateStep( scene, 0, 64, 128, rotation );
		var pc = CreatePlayer( scene, Vector3.Up * 32 );
		var start = Vector3.Up * 32 - rotation.Forward * (HullSupport( pc, rotation.Forward ) + 0.03125f);
		pc.Body.WorldPosition = start;
		pc.WishVelocity = rotation.Forward * 190;
		for ( int tick = 0; tick < 100; ++tick )
		{
			using var time = Time.Scope( tick * 0.015, 0.015f );
			SimulateControllerPhysics( scene, pc, 0.015f );
			Assert.IsFalse( pc.IsOnGround );
			Assert.AreEqual( 0f, (pc.Body.PhysicsBody.Position - start).Dot( rotation.Forward ), 0.01f );
		}
	}

	[TestMethod]
	[DataRow( -20f )]
	[DataRow( 20f )]
	[DataRow( -35f )]
	[DataRow( 35f )]
	public void StaticRampsPreserveHorizontalWalkSpeed( float pitch )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene ).WorldRotation = Rotation.FromPitch( pitch );
		var pc = CreatePlayer( scene, Vector3.Up * 40 );
		TickUntilGrounded( scene, pc );
		pc.WishVelocity = Vector3.Forward * 160;
		for ( int tick = 0; tick < 100; ++tick ) scene.GameTick( 0.015 );
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( 160f, pc.Body.Velocity.x, 0.1f, "Static ramp movement must not repeatedly project away horizontal speed" );
	}

	[TestMethod]
	public void FallingBesideAHurdleDoesNotAcquireStepLookaheadSupport()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateStep( scene, 0, 64, 16, Rotation.Identity );
		var pc = CreatePlayer( scene, new Vector3( -16.03125f, 0, 15 ) );
		pc.Body.Velocity = Vector3.Down * 80;
		using var time = Time.Scope( 0, 0.015f );
		SimulateControllerPhysics( scene, pc, 0.015f );
		Assert.IsFalse( pc.IsOnGround, "A hull beside the step must fall, not snap onto its top" );
		Assert.IsTrue( pc.Body.Velocity.z < -80 );
	}

	[TestMethod]
	public void WalkingDuckInputUsesTimedGroundTransitions()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var pc = CreatePlayer( scene, Vector3.Up );
		TickUntilGrounded( scene, pc );
		for ( int tick = 0; tick <= 27; ++tick )
		{
			using var time = Time.Scope( tick * 0.015, 0.015f );
			pc.UpdateDuckInput( true );
			Assert.AreEqual( tick == 27, pc.IsDucking, $"duck tick {tick}" );
			if ( tick == 13 )
			{
				var eyeHeight = pc.Mode.CalculateEyeTransform().Position.z - pc.WorldPosition.z + pc.EyeDistanceFromTop;
				Assert.IsTrue( eyeHeight > pc.DuckedHeight && eyeHeight < pc.BodyHeight, "Eye transition should precede the hull change" );
				Assert.AreEqual( pc.BodyHeight, pc.CurrentHeight );
			}
		}
		for ( int tick = 0; tick <= 14; ++tick )
		{
			using var time = Time.Scope( 1 + tick * 0.015, 0.015f );
			pc.UpdateDuckInput( false );
			Assert.AreEqual( tick < 14, pc.IsDucking, $"unduck tick {tick}" );
		}
	}

	static void CreateCurvedSurfBank( Scene scene )
	{
		const int segments = 96;
		var half = Math.PI / segments;
		var height = 256 * Math.Cos( half ) * Math.Sqrt( 3 );
		static Vector3 Point( double radius, double angle, double z ) => new( (float)(radius * Math.Cos( angle )), (float)(radius * Math.Sin( angle )), (float)z );
		for ( int i = 0; i < segments; ++i )
		{
			var a = -Math.PI / 2 - half + i * 2 * half - Math.PI / 1800;
			var b = a + 2 * half + Math.PI / 900;
			var hull = scene.CreateObject().Components.Create<HullCollider>( false );
			hull.Type = HullCollider.PrimitiveType.Points;
			hull.Points = [Point( 768, a, 0 ), Point( 768, b, 0 ), Point( 512, b, 0 ), Point( 512, a, 0 ), Point( 768, a, height ), Point( 768, b, height )];
			hull.Static = true;
			hull.Enabled = true;
		}
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 0.1f )]
	public void CurvedSurfRetainsSpeedAcrossConvexSeams( float friction )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.PhysicsWorld.Gravity = Vector3.Down * 600;
		CreateCurvedSurfBank( scene );
		var pc = CreatePlayer( scene, new Vector3( 0, -640, 286.177307f ) );
		pc.AirFriction = friction;
		pc.Body.Velocity = new Vector3( 800, 0, -60 );
		for ( int tick = 0; tick < 120; ++tick )
		{
			using var time = Time.Scope( tick * 0.015, 0.015f );
			var yaw = (float)(tick * (800.0 / 640) * (180 / Math.PI) * 0.015 * 0.95);
			pc.WishVelocity = Rotation.FromYaw( yaw ).Right * 190;
			SimulateControllerPhysics( scene, pc, 0.015f );
			var minimumSpeed = 700 * MathF.Exp( -friction * (tick + 1) * 0.015f );
			Assert.IsTrue( pc.Body.Velocity.WithZ( 0 ).Length > minimumSpeed, $"Lost surf speed at tick {tick + 1}: {pc.Body.Velocity}" );
			Assert.IsFalse( pc.IsOnGround );
			Assert.AreEqual( PhysicsBodyType.Dynamic, pc.Body.PhysicsBody.BodyType );
		}
		Assert.IsTrue( pc.Body.PhysicsBody.Position.y > 200, $"Failed to follow bank: {pc.Body.PhysicsBody.Position}" );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 0.1f )]
	[DataRow( 0.5f )]
	public void AirFrictionDoesNotBlockGroundedSurfExit( float friction )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.PhysicsWorld.Gravity = Vector3.Down * 600;
		CreateFloor( scene );
		CreateCurvedSurfBank( scene );
		var player = CreatePlayer( scene, new Vector3( 255.306107f, -418.81015f, 1.04142785f ) );
		player.AirFriction = friction;
		((IScenePhysicsEvents)player.Body).PrePhysicsStep();
		((IScenePhysicsEvents)player).PostPhysicsStep();
		Assert.IsTrue( player.IsOnGround );
		player.Body.Velocity = new Vector3( 179.585449f, 107.674149f, 0 );
		for ( int tick = 0; tick < 80; ++tick )
		{
			using var time = Time.Scope( (780 + tick) * 0.015, 0.015f );
			player.WishVelocity = Rotation.FromYaw( (float)((780 + tick) * 1.02058107257678) ).Right * 190;
			scene.InternalFixedUpdate();
			Assert.IsTrue( player.Body.Velocity.WithZ( 0 ).Length > 80, $"Stopped at the bank rim: tick {tick}, {player.WorldPosition}, {player.Body.Velocity}" );
		}
		Assert.IsTrue( player.WorldPosition.x > 400, $"Failed to leave the bank rim: {player.WorldPosition}" );
	}

	[TestMethod]
	public void ControllerGrazingBankSweepDoesNotInventAnInvertedPlane()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateCurvedSurfBank( scene );
		var from = new Vector3( 517.5109f, -408.7042f, 294.7213f );
		var to = new Vector3( 524.7289f, -400.1807f, 295.3468f );
		var player = CreatePlayer( scene, from );
		var hit = player.TraceStaticMovement( from, to, null, Vector3.Zero );
		Assert.IsFalse( hit.StartedSolid );
		if ( hit.Hit ) Assert.AreEqual( 0.5f, hit.Normal.z, 0.01f, $"Spurious bank face: {hit.Normal}" );
	}

	[TestMethod]
	[DataRow( false, 0f, 0.02f, 0f )]
	[DataRow( false, 0.1f, 0.015f, 0f )]
	[DataRow( false, 0.1f, 0.02f, 25f )]
	[DataRow( true, 0.1f, 0.015f, 0f )]
	[DataRow( true, 0.1f, 0.02f, 25f )]
	[DataRow( true, 0.5f, 0.02f, 0f )]
	public void AirFrictionSlowsSurfExitsWithoutSnagging( bool crest, float friction, float delta, float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.PhysicsWorld.Gravity = Vector3.Down * 600;
		var rotation = Rotation.FromYaw( yaw );
		var tangent = MathF.Sqrt( 3 );
		var end = crest ? 1024f : 128f;
		var ramp = scene.CreateObject();
		ramp.WorldRotation = rotation;
		var hull = ramp.Components.Create<HullCollider>( false );
		hull.Type = HullCollider.PrimitiveType.Points;
		hull.Points = [new( -1024, -128, 0 ), new( end, -128, 0 ), new( end, 0, 0 ), new( -1024, 0, 0 ),
			new( -1024, 0, 128 * tangent ), new( end, 0, 128 * tangent )];
		hull.Static = true;
		hull.Enabled = true;
		var normal = rotation * new Vector3( 0, -MathF.Sqrt( 0.75f ), 0.5f );
		var support = 16 * (MathF.Abs( normal.x ) + MathF.Abs( normal.y )) / normal.z;
		var player = CreatePlayer( scene, rotation * new Vector3( 0, -64, 64 * tangent + support + 0.0625f ) );
		player.AirFriction = friction;
		player.Body.Velocity = rotation * new Vector3( 400, crest ? 240 : 0, crest ? 240 * tangent : 0 );

		// An unobstructed native body measures the expected drag along the ramp's length.
		var referenceObject = scene.CreateObject();
		referenceObject.WorldPosition = new Vector3( 0, 2000, 2000 );
		referenceObject.Components.Create<BoxCollider>().Scale = Vector3.One;
		var reference = referenceObject.Components.Create<Rigidbody>();
		reference.Gravity = false;
		reference.LinearDamping = friction;
		reference.Velocity = rotation.Forward * 400;
		for ( int tick = 0; tick < 60; ++tick )
		{
			using var time = Time.Scope( tick * (double)delta, delta );
			scene.InternalFixedUpdate();
			Assert.IsFalse( player.IsOnGround );
			Assert.AreEqual( reference.Velocity.Dot( rotation.Forward ), player.Body.Velocity.Dot( rotation.Forward ), 0.5f,
				$"Surf contact changed tangent speed at tick {tick}: {player.WorldPosition}, {player.Body.Velocity}" );
		}
		var position = rotation.Inverse * player.WorldPosition;
		Assert.IsTrue( crest ? position.y > 16 : position.x > end + 24, $"Did not leave the surf ramp: {position}" );
		if ( friction > 0 ) Assert.IsTrue( player.Body.Velocity.Dot( rotation.Forward ) < 390, "Contact solving must not cancel air drag" );
	}

	[TestMethod]
	public void StaticSweepKeepsTheCurrentFaceBeforeTheNextFacet()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateCurvedSurfBank( scene );
		var hull = new BBox( new Vector3( -16, -16, 0 ), new Vector3( 16, 16, 72 ) );
		var from = new Vector3( 446.500214f, -480.431366f, 288.446198f );
		var movement = new Vector3( 583.403015f, 480.069031f, 32.1034698f ) * 0.015f;
		var contact = scene.Trace.Box( hull, from + Vector3.Up * 0.25f, from - Vector3.Up * 0.25f ).Run();
		var player = CreatePlayer( scene, from );
		var hit = player.TraceStaticMovement( from, from + movement, contact.Shape, contact.Normal );
		Assert.IsTrue( hit.Hit );
		Assert.IsFalse( hit.StartedSolid );
		Assert.IsTrue( hit.Fraction < 0.02f, $"Skipped the current face: {hit.Fraction}" );
		Assert.IsTrue( (hit.Normal - new Vector3( -0.5710186f, 0.6511219f, 0.499978f )).Length < 0.001f );
		Assert.IsFalse( player.TraceStaticMovement( from, from + hit.Normal * 0.125f, contact.Shape, contact.Normal ).Hit, "Receding casts must remain clear" );
		var tangent = hit.Normal.Cross( Vector3.Up ).Normal;
		Assert.IsFalse( player.TraceStaticMovement( from, from + tangent * 0.125f, contact.Shape, contact.Normal ).Hit, "Tangential casts must remain clear" );
	}
}
