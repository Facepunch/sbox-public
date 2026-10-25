using Sandbox.Movement;
using System;

namespace SceneTests.Components;

public partial class LadderMoveModeTest
{
	[TestMethod]
	[DataRow( 0f, 0f, false )]
	[DataRow( 0f, 45f, false )]
	[DataRow( 20f, 25f, false )]
	[DataRow( -20f, -25f, false )]
	[DataRow( 0f, 25f, true )]
	[DataRow( 20f, 45f, true )]
	public void ClimbsAndDescendsAlongLadderThenHangs( float pitch, float yaw, bool backSide )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw ) * Rotation.FromPitch( pitch );
		var center = new Vector3( 16, 0, 128 );
		var trigger = CreateLadderTrigger( scene, center, new Vector3( 20, 64, 256 ) );
		trigger.WorldRotation = rotation;
		var pc = CreatePlayer( scene, center - rotation.Up * 48 + rotation.Forward * (backSide ? 16 : -16), out var mode );
		TickUntil( scene, () => pc.IsClimbing, 60 );
		Assert.IsTrue( pc.IsClimbing );
		var up = mode.ClimbingRotation.Up;
		pc.WishVelocity = mode.UpdateMove( Rotation.Identity, Vector3.Forward );
		Assert.IsTrue( pc.WishVelocity.Normal.Dot( up ) > 0.999f, $"climb input is not along ladder: {pc.WishVelocity}, {up}" );
		var start = pc.Body.PhysicsBody.Position;
		for ( int i = 0; i < 150 && (pc.Body.PhysicsBody.Position - start).Dot( up ) < 48; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.IsClimbing, $"fell off while climbing at {pc.Body.PhysicsBody.Position}" );
			Assert.IsFalse( pc.IsOnGround );
		}
		var top = pc.Body.PhysicsBody.Position;
		Assert.IsTrue( (top - start).Dot( up ) >= 48, $"could not climb: {top}" );
		Assert.AreEqual( 0, (top - start).Dot( rotation.Forward ), 0.5f, $"pushed away from ladder at {top}" );
		pc.WishVelocity = mode.UpdateMove( Rotation.FromPitch( 80 ), Vector3.Forward );
		for ( int i = 0; i < 150 && (top - pc.Body.PhysicsBody.Position).Dot( up ) < 48; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.IsClimbing, $"fell off while descending at {pc.Body.PhysicsBody.Position}" );
		}
		Assert.IsTrue( (top - pc.Body.PhysicsBody.Position).Dot( up ) >= 48 );
		var hang = pc.Body.PhysicsBody.Position;
		pc.WishVelocity = Vector3.Zero;
		for ( int i = 0; i < 30; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.IsClimbing );
		Assert.IsTrue( (pc.Body.PhysicsBody.Position - hang).Length < 5, $"did not hang on ladder: {pc.Body.PhysicsBody.Position}" );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 45f )]
	public void LadderClimbingCannotPassThroughCeiling( float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		CreateLadderTrigger( scene, rotation * new Vector3( 16, 0, 128 ), new Vector3( 32, 32, 256 ) ).WorldRotation = rotation;
		var roof = scene.CreateObject();
		roof.WorldPosition = new Vector3( 0, 0, 168 );
		var collider = roof.Components.Create<BoxCollider>();
		collider.Scale = new Vector3( 512, 512, 16 );
		collider.Static = true;
		var pc = CreatePlayer( scene, Vector3.Up * 10, out var mode );
		TickUntil( scene, () => pc.IsClimbing, 60 );
		Assert.IsTrue( pc.IsClimbing );
		pc.WishVelocity = mode.UpdateMove( Rotation.Identity, Vector3.Forward );
		for ( int i = 0; i < 120; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.Body.PhysicsBody.Position.z <= 88.25f, $"passed through ceiling at {pc.Body.PhysicsBody.Position}" );
			Assert.IsTrue( pc.IsClimbing );
			Assert.IsFalse( pc.IsOnGround );
		}
		Assert.IsTrue( pc.Body.PhysicsBody.Position.z > 86 );
		Assert.IsTrue( MathF.Abs( pc.Body.Velocity.z ) < 1 );
	}

	[TestMethod]
	[DataRow( 20f )]
	[DataRow( 80f )]
	public void JumpingOffLadderReturnsToWalking( float depth )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateLadderTrigger( scene, new Vector3( 16, 0, 128 ), new Vector3( depth, 64, 256 ) );
		var pc = CreatePlayer( scene, Vector3.Up * 80, out var mode );
		TickUntil( scene, () => pc.IsClimbing, 60 );
		Assert.IsTrue( pc.IsClimbing );
		pc.Jump( mode.ClimbingRotation.Backward * 200 );
		pc.WishVelocity = Vector3.Zero;
		TickUntil( scene, () => !pc.IsClimbing, 60 );
		Assert.IsFalse( pc.IsClimbing, $"jump could not leave ladder: {pc.Body.PhysicsBody.Position}" );
		Assert.IsTrue( pc.Mode is MoveModeWalk );
		TickUntil( scene, () => pc.IsOnGround, 150 );
		Assert.IsTrue( pc.IsOnGround );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 25f )]
	[DataRow( 90f )]
	public void LadderCenteringUsesOffsetColliderWithoutVerticalPush( float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		var trigger = CreateLadderTrigger( scene, new Vector3( 16, 0, 128 ), new Vector3( 20, 64, 256 ) );
		trigger.WorldRotation = rotation;
		trigger.GetComponent<BoxCollider>().Center = new Vector3( 0, 32, 0 );
		var center = trigger.WorldTransform.PointToWorld( new Vector3( 0, 32, 0 ) );
		var pc = CreatePlayer( scene, center - rotation.Up * 48 - rotation.Forward * 16 + rotation.Right * 8, out var mode );
		TickUntil( scene, () => pc.IsClimbing, 60 );
		Assert.IsTrue( pc.IsClimbing );
		var height = pc.Body.PhysicsBody.Position.z;
		for ( int i = 0; i < 200 && MathF.Abs( (pc.Body.PhysicsBody.Position - center).Dot( rotation.Right ) ) > 1; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.IsClimbing );
			Assert.AreEqual( height, pc.Body.PhysicsBody.Position.z, 0.2f );
		}
		Assert.IsTrue( MathF.Abs( (pc.Body.PhysicsBody.Position - center).Dot( rotation.Right ) ) <= 1 );
		Assert.IsTrue( mode.ClimbingRotation.Forward.Dot( rotation.Forward ) > 0.99f );
	}

	[TestMethod]
	public void ExitingLadderAtTopCanLandOnPlatform()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateLadderTrigger( scene, new Vector3( 16, 0, 96 ), new Vector3( 20, 64, 192 ) );
		var platform = scene.CreateObject();
		platform.WorldPosition = new Vector3( 80, 0, 184 );
		var collider = platform.Components.Create<BoxCollider>();
		collider.Scale = new Vector3( 128, 128, 16 );
		collider.Static = true;
		var pc = CreatePlayer( scene, Vector3.Up * 80, out var mode );
		TickUntil( scene, () => pc.IsClimbing, 60 );
		Assert.IsTrue( pc.IsClimbing );
		pc.WishVelocity = mode.UpdateMove( Rotation.Identity, Vector3.Forward );
		for ( int i = 0; i < 200 && pc.IsClimbing; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsFalse( pc.IsClimbing, $"did not exit ladder at {pc.Body.PhysicsBody.Position}" );
		Assert.IsTrue( pc.Mode is MoveModeWalk );
		pc.WishVelocity = Vector3.Forward * 160;
		for ( int i = 0; i < 120 && !pc.IsOnGround; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( platform, pc.GroundObject );
		Assert.AreEqual( 192f, pc.Body.PhysicsBody.Position.z, 1.0f );
	}

	[TestMethod]
	public void OverlappingLaddersRetainTheCurrentAttachment()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateLadderTrigger( scene, new Vector3( 16, -8, 128 ), new Vector3( 20, 64, 256 ) );
		CreateLadderTrigger( scene, new Vector3( 16, 8, 128 ), new Vector3( 20, 64, 256 ) );
		var pc = CreatePlayer( scene, Vector3.Up * 80, out var mode );
		TickUntil( scene, () => pc.IsClimbing, 60 );
		Assert.IsTrue( pc.IsClimbing );
		var attached = mode.ClimbingObject;
		for ( int i = 0; i < 20; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.AreEqual( attached, mode.ClimbingObject, "overlapping triggers changed the attachment while still touching it" );
		}
	}

	[TestMethod]
	public void JumpingDoesNotAttachToAnOverlappingLadder()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateLadderTrigger( scene, new Vector3( 16, -8, 128 ), new Vector3( 20, 64, 256 ) );
		CreateLadderTrigger( scene, new Vector3( 16, 8, 128 ), new Vector3( 20, 64, 256 ) );
		var pc = CreatePlayer( scene, Vector3.Up * 80, out var mode );
		TickUntil( scene, () => pc.IsClimbing, 60 );
		Assert.IsTrue( pc.IsClimbing );
		var start = pc.Body.PhysicsBody.Position;
		pc.Jump( mode.ClimbingRotation.Backward * 200 );
		for ( int i = 0; i < 10; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			if ( (pc.Body.PhysicsBody.Position - start).Length < 0.1f )
				continue;
			Assert.IsFalse( pc.IsClimbing, "jumping reattached to another overlapping ladder trigger" );
			return;
		}
		Assert.Fail( "jump did not move away from the ladder" );
	}

	[TestMethod]
	[DataRow( 20f, 0f )]
	[DataRow( 20f, 45f )]
	[DataRow( 80f, 0f )]
	[DataRow( 80f, 45f )]
	public void JumpedLadderCanBeClimbedAgainAfterLeavingItsTrigger( float depth, float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		var ladder = CreateLadderTrigger( scene, rotation * new Vector3( 16, 0, 128 ), new Vector3( depth, 64, 256 ) );
		ladder.WorldRotation = rotation;
		var start = Vector3.Up * 80;
		var pc = CreatePlayer( scene, start, out var mode );
		TickUntil( scene, () => pc.IsClimbing, 60 );
		Assert.IsTrue( pc.IsClimbing );
		pc.Jump( mode.ClimbingRotation.Backward * 200 );
		TickUntil( scene, () => pc.IsOnGround, 180 );
		Assert.IsTrue( pc.IsOnGround );
		Assert.IsFalse( pc.IsClimbing );
		Assert.IsFalse( pc.Body.Touching.Contains( ladder.GetComponent<BoxCollider>() ), "never left the ladder trigger" );
		pc.WorldPosition = start;
		pc.Body.Velocity = Vector3.Zero;
		TickUntil( scene, () => pc.IsClimbing, 60 );
		Assert.IsTrue( pc.IsClimbing, "jump suppression did not reset after leaving the ladder" );
		Assert.AreEqual( ladder, mode.ClimbingObject );
	}

	[TestMethod]
	[DataRow( "collider", 0f )]
	[DataRow( "collider", 45f )]
	[DataRow( "object", 0f )]
	[DataRow( "object", 45f )]
	[DataRow( "tag", 0f )]
	[DataRow( "tag", 45f )]
	public void RemovingClimbableTriggerReturnsToWalking( string removal, float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		var ladder = CreateLadderTrigger( scene, rotation * new Vector3( 16, 0, 128 ), new Vector3( 20, 64, 256 ) );
		ladder.WorldRotation = rotation;
		var pc = CreatePlayer( scene, Vector3.Up * 80, out var mode );
		TickUntil( scene, () => pc.IsClimbing, 60 );
		Assert.IsTrue( pc.IsClimbing );
		switch ( removal )
		{
			case "collider": ladder.GetComponent<BoxCollider>().Enabled = false; break;
			case "object": ladder.Enabled = false; break;
			case "tag": ladder.Tags.Remove( "ladder" ); break;
			default: Assert.Fail( $"unknown removal: {removal}" ); break;
		}
		TickUntil( scene, () => !pc.IsClimbing, 60 );
		Assert.IsFalse( pc.IsClimbing, $"retained removed {removal}" );
		Assert.IsTrue( pc.Mode is MoveModeWalk );
		Assert.IsNull( mode.ClimbingObject );
		TickUntil( scene, () => pc.Body.Gravity, 10 );
		Assert.IsTrue( pc.Body.Gravity, "walking did not restore gravity after losing the ladder" );
		TickUntil( scene, () => pc.IsOnGround, 150 );
		Assert.IsTrue( pc.IsOnGround );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ReplacingColliderOnAttachedLadderRefreshesClimbDirection( bool destroy )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var ladder = CreateLadderTrigger( scene, new Vector3( 16, 0, 128 ), new Vector3( 20, 64, 256 ) );
		var original = ladder.GetComponent<BoxCollider>();
		var pc = CreatePlayer( scene, Vector3.Up * 80, out var mode );
		TickUntil( scene, () => pc.IsClimbing, 60 );
		Assert.IsTrue( pc.IsClimbing );
		var replacement = ladder.Components.Create<BoxCollider>();
		replacement.Center = new Vector3( 0, 16, 0 );
		replacement.Scale = new Vector3( 64, 20, 256 );
		replacement.IsTrigger = true;
		replacement.Static = true;
		if ( destroy )
			original.Destroy();
		else
			original.Enabled = false;
		for ( int i = 0; i < 30; ++i )
			scene.GameTick( 1.0 / 50 );
		Assert.IsTrue( pc.IsClimbing );
		Assert.AreEqual( ladder, mode.ClimbingObject );
		Assert.IsTrue( mode.ClimbingRotation.Forward.Dot( Rotation.Identity.Left ) > 0.99f, $"retained removed collider's direction: {mode.ClimbingRotation}" );
		Assert.IsTrue( mode.UpdateMove( Rotation.Identity, Vector3.Forward ).Normal.Dot( Vector3.Up ) > 0.99f );
	}

	[TestMethod]
	[DataRow( 0.5f, false )]
	[DataRow( 1f, false )]
	[DataRow( 2f, false )]
	[DataRow( 0.5f, true )]
	[DataRow( 1f, true )]
	[DataRow( 2f, true )]
	public void ScaledLaddersClimbWithEitherThinHorizontalAxis( float scale, bool thinY )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( 25 );
		var forward = thinY ? rotation.Left : rotation.Forward;
		var center = new Vector3( 0, 0, 128 * scale ) + forward * (16 * scale);
		var ladder = CreateLadderTrigger( scene, center, (thinY ? new Vector3( 64, 20, 256 ) : new Vector3( 20, 64, 256 )) * scale );
		ladder.WorldRotation = rotation;
		var pc = CreatePlayer( scene, Vector3.Up * (80 * scale), out var mode );
		pc.WorldScale = new Vector3( scale );
		TickUntil( scene, () => pc.IsClimbing, 60 );
		Assert.IsTrue( pc.IsClimbing );
		Assert.IsTrue( mode.ClimbingRotation.Forward.Dot( forward ) > 0.99f );
		var start = pc.Body.PhysicsBody.Position;
		pc.WishVelocity = mode.UpdateMove( Rotation.Identity, Vector3.Forward );
		for ( int i = 0; i < 150 && pc.Body.PhysicsBody.Position.z < start.z + 24 * scale; ++i )
		{
			scene.GameTick( 1.0 / 50 );
			Assert.IsTrue( pc.IsClimbing );
		}
		var displacement = pc.Body.PhysicsBody.Position - start;
		Assert.IsTrue( displacement.z >= 24 * scale, $"failed to climb scaled ladder: {displacement}" );
		Assert.AreEqual( 0f, displacement.Dot( forward ), 0.2f );
	}
}
