using Sandbox.Movement;
using System;

namespace SceneTests.Components;

public partial class PlayerControllerTest
{
	static void StepMovementOptions( Scene scene, PlayerController player, int tick )
	{
		using var time = Time.Scope( tick * 0.02, 0.02f );
		SimulateControllerPhysics( scene, player, 0.02f );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void DisablingAirStrafingPreservesBallisticMotion( bool is2D )
	{
		var scene = new Scene { PhysicsMode = is2D ? ScenePhysicsMode.Physics2D : ScenePhysicsMode.Physics3D };
		using var scope = scene.Push();
		using var initialTime = Time.Scope( 0, 0.02f );
		Vector3 up = is2D ? Vector2.Up : Vector3.Up;
		scene.PhysicsWorld.Gravity = -up * 600;
		var player = CreatePlayer( scene, up * 1000 );
		var walk = (MoveModeWalk)player.Mode;
		walk.EnableAirStrafing = false;
		var velocity = Vector3.Forward * 120 + up * 200;
		player.Body.Velocity = velocity;
		player.WishVelocity = (is2D ? Vector3.Backward : Vector3.Left) * 320;
		StepMovementOptions( scene, player, 0 );
		Assert.IsTrue( (player.Body.Velocity - (velocity - up * 12)).Length < 0.01f );
		Assert.IsTrue( (player.WorldPosition - (up * 1000 + velocity * 0.02f - up * 0.12f)).Length < 0.01f );

		walk.EnableAirStrafing = true;
		StepMovementOptions( scene, player, 1 );
		Assert.IsTrue( (player.WithoutVertical( player.Body.Velocity ) - Vector3.Forward * 120).Length > 10 );
		Assert.AreEqual( 176f, player.Body.Velocity.Dot( up ), 0.01f );
	}

	[TestMethod]
	[DataRow( true, 1f, 1f, 675f, 775f )]
	[DataRow( false, 1f, 1f, 267f, 231f )]
	[DataRow( true, 0.5f, 1f, 375f, 375f )]
	[DataRow( false, 0.5f, 1f, 171f, 103f )]
	[DataRow( false, 1f, 0.2f, 195f, 135f )]
	[DataRow( false, 0f, 1f, 75f, -25f )]
	[DataRow( true, -1f, 1f, 75f, -25f )]
	[DataRow( true, 2f, 1f, 675f, 775f )]
	public void WalkingJumpMomentumTuningPreservesGroundVelocity( bool bunnyHopping, float retention, float speedScale, float expectedX, float expectedY )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		using var initialTime = Time.Scope( 0, 0.02f );
		CreateFloor( scene );
		var player = CreatePlayer( scene, Vector3.Up );
		StepMovementOptions( scene, player, 0 );
		Assert.IsTrue( player.IsOnGround );
		var walk = (MoveModeWalk)player.Mode;
		walk.EnableBunnyHopping = bunnyHopping;
		walk.BunnyHopRecoveryTime = 0;
		walk.JumpMomentumRetention = retention;
		player.GroundVelocity = new Vector3( 75, -25, 0 );
		player.Body.Velocity = player.GroundVelocity + new Vector3( 600, 800, 0 ) * speedScale;
		walk.RequestJump();
		StepMovementOptions( scene, player, 1 );
		Assert.IsFalse( player.IsOnGround );
		Assert.AreEqual( expectedX, player.Body.Velocity.x, 0.01f );
		Assert.AreEqual( expectedY, player.Body.Velocity.y, 0.01f );
		Assert.IsTrue( player.Body.Velocity.z > 100 );
		Assert.AreEqual( PhysicsBodyType.Dynamic, player.Body.PhysicsBody.BodyType );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void DisablingBunnyHoppingRequiresGroundRecoveryWithoutBuffering( bool is2D )
	{
		var scene = new Scene { PhysicsMode = is2D ? ScenePhysicsMode.Physics2D : ScenePhysicsMode.Physics3D };
		using var scope = scene.Push();
		using var initialTime = Time.Scope( 0, 0.02f );
		Vector3 up = is2D ? Vector2.Up : Vector3.Up;
		scene.PhysicsWorld.Gravity = -up * 800;
		CreateFloor( scene );
		var player = CreatePlayer( scene, up * 100 );
		var walk = (MoveModeWalk)player.Mode;
		walk.EnableBunnyHopping = false;
		int tick = 0;
		for ( ; tick < 100 && !player.IsOnGround; ++tick ) StepMovementOptions( scene, player, tick );
		Assert.IsTrue( player.IsOnGround );
		walk.RequestJump();
		StepMovementOptions( scene, player, tick++ );
		Assert.IsTrue( player.IsOnGround, "Immediate re-jump must be rejected" );
		for ( int i = 0; i < 8; ++i )
		{
			StepMovementOptions( scene, player, tick++ );
			Assert.IsTrue( player.IsOnGround, "Rejected jump must not fire later" );
		}
		walk.RequestJump();
		StepMovementOptions( scene, player, tick );
		Assert.IsFalse( player.IsOnGround );
		Assert.IsTrue( player.Body.Velocity.Dot( up ) > 100 );
	}

	[TestMethod]
	[DataRow( true, 1f )]
	[DataRow( false, 0f )]
	[DataRow( false, -1f )]
	public void BunnyHopRecoveryCanBeBypassedOrDisabled( bool bunnyHopping, float recovery )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		using var initialTime = Time.Scope( 0, 0.02f );
		CreateFloor( scene );
		var player = CreatePlayer( scene, Vector3.Up );
		StepMovementOptions( scene, player, 0 );
		Assert.IsTrue( player.IsOnGround );
		var walk = (MoveModeWalk)player.Mode;
		walk.EnableBunnyHopping = bunnyHopping;
		walk.BunnyHopRecoveryTime = recovery;
		walk.RequestJump();
		StepMovementOptions( scene, player, 1 );
		Assert.IsFalse( player.IsOnGround );
	}

	[TestMethod]
	public void BunnyHopRestrictionPreservesCoyoteJumps()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		using var initialTime = Time.Scope( 0, 0.02f );
		CreateFloor( scene );
		var player = CreatePlayer( scene, Vector3.Up );
		player.CoyoteTime = 0.2f;
		StepMovementOptions( scene, player, 0 );
		Assert.IsTrue( player.IsOnGround );
		var walk = (MoveModeWalk)player.Mode;
		walk.EnableBunnyHopping = false;
		walk.BunnyHopRecoveryTime = 1;
		player.PreventGrounding( 0 );
		player.Body.WorldPosition = Vector3.Up * 1000;
		player.Body.Velocity = Vector3.Forward * 600;
		walk.RequestJump();
		StepMovementOptions( scene, player, 1 );
		Assert.AreEqual( 320f, player.Body.Velocity.x, 0.01f );
		Assert.IsTrue( player.Body.Velocity.z > 100 );
	}

	[TestMethod]
	[DataRow( true, 0.1f, 320f, 320f, 0f, false, 352f )]
	[DataRow( false, 0.1f, 320f, 320f, 0f, false, 320f )]
	[DataRow( true, 0f, 320f, 320f, 0f, false, 320f )]
	[DataRow( true, 0.25f, 320f, 320f, 0f, false, 400f )]
	[DataRow( true, 0.1f, 352f, 320f, 0f, false, 352f )]
	[DataRow( true, 0.1f, 400f, 320f, 0f, false, 400f )]
	[DataRow( true, 0.1f, 110f, 320f, 0f, false, 110f )]
	[DataRow( true, 0.1f, 320f, 110f, 0f, false, 320f )]
	[DataRow( true, 0.1f, 320f, 320f, 0f, true, 320f )]
	[DataRow( true, 0.1f, 320f, 320f, 75f, false, 427f )]
	public void WalkingJumpRequestsApplySprintBoost( bool enabled, float boost, float speed, float wishSpeed, float groundSpeed, bool ducked, float expectedSpeed )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		using var initialTime = Time.Scope( 0, 0.02f );
		CreateFloor( scene );
		var player = CreatePlayer( scene, Vector3.Up );
		StepMovementOptions( scene, player, 0 );
		Assert.IsTrue( player.IsOnGround );
		player.EnableJumpBoost = enabled;
		player.JumpBoost = boost;
		player.UpdateDucking( ducked );
		player.GroundVelocity = Vector3.Forward * groundSpeed;
		player.Body.Velocity = Vector3.Forward * (speed + groundSpeed);
		player.WishVelocity = Vector3.Forward * wishSpeed;
		((MoveModeWalk)player.Mode).RequestJump();
		StepMovementOptions( scene, player, 1 );
		Assert.IsFalse( player.IsOnGround );
		Assert.IsTrue( player.Body.Velocity.z > 100 );
		Assert.AreEqual( expectedSpeed, player.Body.Velocity.x, 0.01f );
	}

	[TestMethod]
	public void CoyoteWalkingJumpDoesNotApplySprintBoost()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		using var initialTime = Time.Scope( 0, 0.02f );
		CreateFloor( scene );
		var player = CreatePlayer( scene, Vector3.Up );
		StepMovementOptions( scene, player, 0 );
		Assert.IsTrue( player.IsOnGround );
		player.EnableJumpBoost = true;
		player.CoyoteTime = 0.2f;
		player.PreventGrounding( 0 );
		player.Body.WorldPosition = Vector3.Up * 1000;
		player.Body.Velocity = Vector3.Forward * player.RunSpeed;
		player.WishVelocity = Vector3.Forward * player.RunSpeed;
		((MoveModeWalk)player.Mode).RequestJump();
		StepMovementOptions( scene, player, 1 );
		Assert.IsTrue( player.Body.Velocity.z > 100 );
		Assert.AreEqual( player.RunSpeed, player.Body.Velocity.x, 0.01f );
	}

	[TestMethod]
	public void ExplicitLaunchBypassesWalkingJumpOptions()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var player = CreatePlayer( scene, Vector3.Up * 1000 );
		var walk = (MoveModeWalk)player.Mode;
		walk.EnableBunnyHopping = false;
		walk.BunnyHopRecoveryTime = 1;
		walk.JumpMomentumRetention = 0;
		player.Body.Velocity = Vector3.Forward * 600;
		player.Jump( Vector3.Up * 160 );
		Assert.AreEqual( new Vector3( 600, 0, 160 ), player.Body.Velocity );
	}
}
