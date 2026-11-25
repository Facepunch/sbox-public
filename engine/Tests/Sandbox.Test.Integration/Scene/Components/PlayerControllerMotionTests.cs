using Sandbox.Movement;

namespace SceneTests.Components;

public partial class PlayerControllerTest
{
	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void MotionDisabledWalkingPreservesAssignedPosition( bool is2D, bool nearFloor )
	{
		var scene = new Scene { PhysicsMode = is2D ? ScenePhysicsMode.Physics2D : ScenePhysicsMode.Physics3D };
		using var scope = scene.Push();
		using var initialTime = Time.Scope( 0, 0.02f );
		try
		{
			Vector3 up = is2D ? Vector2.Up : Vector3.Up;
			scene.PhysicsWorld.Gravity = -up * 600;
			if ( nearFloor ) CreateFloor( scene );
			var seat = up * (nearFloor ? 1 : 1000);
			var player = CreatePlayer( scene, seat );
			player.Body.Velocity = 0;
			player.Body.MotionEnabled = false;

			for ( int tick = 0; tick < 100; ++tick )
			{
				player.WorldPosition = seat;
				player.WishVelocity = tick < 50 ? Vector3.Zero : Vector3.Forward * 300;
				StepMovementOptions( scene, player, tick );
				Assert.IsTrue( player.WorldPosition.Distance( seat ) < 0.001f,
					$"Position drift on tick {tick}: {player.WorldPosition}" );
				Assert.IsTrue( player.Body.Velocity.Length < 0.001f,
					$"Velocity accumulated on tick {tick}: {player.Body.Velocity}" );
			}

			player.WishVelocity = 0;
			player.WorldPosition = up * 1000;
			player.Body.MotionEnabled = true;
			player.PreventGrounding( 0.1f );
			StepMovementOptions( scene, player, 100 );
			Assert.AreEqual( -12f, player.Body.Velocity.Dot( up ), 0.01f,
				"Re-enabling motion must resume gravity without accumulated frozen-time velocity" );
			Assert.IsTrue( player.WorldPosition.Dot( up ) < 1000, "The unfrozen player must fall" );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void MotionDisabledWalkingDiscardsPendingJump()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		using var initialTime = Time.Scope( 0, 0.02f );
		try
		{
			CreateFloor( scene );
			var player = CreatePlayer( scene, Vector3.Up );
			StepMovementOptions( scene, player, 0 );
			Assert.IsTrue( player.IsOnGround );

			((MoveModeWalk)player.Mode).RequestJump();
			player.Body.MotionEnabled = false;
			player.Body.Velocity = 0;
			StepMovementOptions( scene, player, 1 );
			Assert.IsTrue( player.Body.Velocity.Length < 0.001f, "The pending jump must not move the frozen player" );

			player.Body.MotionEnabled = true;
			StepMovementOptions( scene, player, 2 );
			Assert.IsTrue( player.IsOnGround, "The pending jump must not fire after unfreezing" );
			Assert.IsTrue( player.Body.Velocity.z < 1 );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void MotionDisabledMidStepPreservesCallerPositionAndVelocity()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		using var initialTime = Time.Scope( 0, 0.02f );
		try
		{
			CreateFloor( scene );
			var player = CreatePlayer( scene, Vector3.Up );
			player.Body.Velocity = new Vector3( 100, 0, -10 );
			((IScenePhysicsEvents)player.Body).PrePhysicsStep();
			((IScenePhysicsEvents)player).PrePhysicsStep();

			player.Body.MotionEnabled = false;
			var seat = new Vector3( 40, 0, 1 );
			var velocity = new Vector3( 3, 4, 5 );
			player.Body.WorldPosition = seat;
			player.Body.Velocity = velocity;
			((IScenePhysicsEvents)player).PostPhysicsStep();
			Assert.IsTrue( player.Body.WorldPosition.Distance( seat ) < 0.001f, "A cached static move must not overwrite the assigned position" );
			Assert.IsTrue( player.Body.Velocity.Distance( velocity ) < 0.001f, "Finishing gravity must not change caller velocity" );

			((IScenePhysicsEvents)player).PrePhysicsStep();
			((IScenePhysicsEvents)player).PostPhysicsStep();
			Assert.IsTrue( player.Body.WorldPosition.Distance( seat ) < 0.001f );
			Assert.IsTrue( player.Body.Velocity.Distance( velocity ) < 0.001f, "Frozen walking must preserve, not zero, caller velocity" );
		}
		finally
		{
			scene.Destroy();
		}
	}
}
