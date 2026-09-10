using Sandbox.Movement;

namespace SceneTests.Components;

/// <summary>
/// Pins MoveModeLadder: it scores -100 until its fixed-update scan finds a touched
/// trigger tagged with a climbable tag, at which point its Priority outranks the
/// walk mode - the controller starts climbing (gravity off, velocity zeroed),
/// ladders below the feet are ignored, the climb rotation faces the ladder from
/// either side, and removing the ladder hands control back to walking. Climb
/// movement itself reads Input.AnalogMove, which is silent headless, so actual
/// up/down motion is out of scope.
/// </summary>
[TestClass]
public class LadderMoveModeTest
{
	/// <summary>
	/// Creates a large static floor whose top surface sits at z = 0.
	/// </summary>
	static GameObject CreateFloor( Scene scene )
	{
		var go = scene.CreateObject();
		go.Name = "Floor";
		go.WorldPosition = new Vector3( 0, 0, -50 );

		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 4000, 4000, 100 );
		box.Static = true;

		return go;
	}

	/// <summary>
	/// Creates a PlayerController with a MoveModeLadder at the given position,
	/// configured for headless ticking: no input controls and no footstep sounds.
	/// </summary>
	static PlayerController CreatePlayer( Scene scene, Vector3 position, out MoveModeLadder ladderMode )
	{
		var go = scene.CreateObject();
		go.Name = "Player";
		go.WorldPosition = position;

		var pc = go.Components.Create<PlayerController>();
		pc.UseInputControls = false;
		pc.EnableFootstepSounds = false;

		ladderMode = go.Components.Create<MoveModeLadder>();

		return pc;
	}

	/// <summary>
	/// Creates a static trigger box tagged "ladder" - the shape MoveModeLadder's
	/// scan looks for in the body's touching triggers.
	/// </summary>
	static GameObject CreateLadderTrigger( Scene scene, Vector3 position, Vector3 scale )
	{
		var go = scene.CreateObject();
		go.Name = "Ladder";
		go.WorldPosition = position;
		go.Tags.Add( "ladder" );

		var box = go.Components.Create<BoxCollider>();
		box.Scale = scale;
		box.IsTrigger = true;
		box.Static = true;

		return go;
	}

	/// <summary>
	/// Ticks the scene until the condition holds, bounded so a regression fails
	/// fast instead of hanging. The caller asserts the condition afterwards.
	/// </summary>
	static void TickUntil( Scene scene, System.Func<bool> condition, int maxTicks )
	{
		for ( int i = 0; i < maxTicks && !condition(); i++ )
		{
			scene.GameTick();
		}
	}

	/// <summary>
	/// Pins the defaults (priority 5, speed 1, the built-in "ladder" climbable tag)
	/// and the Score contract: -100 without a climbing object, the configured
	/// Priority with one.
	/// </summary>
	[TestMethod]
	public void DefaultsAndScore()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var pc = CreatePlayer( scene, Vector3.Zero, out var ladderMode );

		Assert.AreEqual( 5, ladderMode.Priority );
		Assert.AreEqual( 1f, ladderMode.Speed );
		Assert.IsTrue( ladderMode.ClimbableTags.Has( "ladder" ), "the ladder tag is climbable out of the box" );
		Assert.IsNull( ladderMode.ClimbingObject );

		Assert.AreEqual( -100, ladderMode.Score( pc ), "without a ladder the mode rules itself out" );

		ladderMode.ClimbingObject = scene.CreateObject();
		Assert.AreEqual( 5, ladderMode.Score( pc ), "with a ladder the mode scores its priority" );

		ladderMode.Priority = 9;
		Assert.AreEqual( 9, ladderMode.Score( pc ) );
	}

	/// <summary>
	/// A tall "ladder" trigger overlapping the player makes the scan latch onto it:
	/// the ladder mode outranks walking, the controller reports climbing, the
	/// body's gravity is turned off and the climb rotation faces the ladder.
	/// </summary>
	[TestMethod]
	public void TaggedTriggerActivatesClimbing()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );
		var ladder = CreateLadderTrigger( scene, new Vector3( 16, 0, 128 ), new Vector3( 20, 20, 256 ) );
		var pc = CreatePlayer( scene, new Vector3( 0, 0, 10 ), out var ladderMode );

		TickUntil( scene, () => pc.IsClimbing, 60 );

		Assert.IsTrue( pc.IsClimbing, "the player should have latched onto the ladder" );
		Assert.AreEqual( ladderMode, pc.Mode, "the ladder mode outranks the walk mode" );
		Assert.AreEqual( ladder, ladderMode.ClimbingObject );
		Assert.IsFalse( pc.IsAirborne, "climbing is not airborne" );

		// the ladder is ahead of the player (+x) so the climb rotation is the
		// ladder's own rotation, not the flipped one
		Assert.IsTrue( ladderMode.ClimbingRotation.Distance( Rotation.Identity ) < 5f,
			$"facing the ladder front should keep its rotation: {ladderMode.ClimbingRotation}" );

		// climbing turns gravity off and brakes hard - the player hangs there
		scene.GameTick();
		Assert.IsFalse( pc.Body.Gravity, "the ladder mode disables body gravity" );

		var heightWhenClimbing = pc.WorldPosition.z;

		for ( int i = 0; i < 10; i++ )
		{
			scene.GameTick();
		}

		Assert.AreEqual( heightWhenClimbing, pc.WorldPosition.z, 5f,
			$"without input the climber should hang in place: {pc.WorldPosition}" );
	}

	/// <summary>
	/// Approaching the ladder from its back side flips the climb rotation 180
	/// degrees of yaw, so the climber always faces the ladder.
	/// </summary>
	[TestMethod]
	public void ClimbRotationFlipsOnBackSide()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );
		CreateLadderTrigger( scene, new Vector3( 16, 0, 128 ), new Vector3( 20, 20, 256 ) );

		// the player stands at +x of the ladder, behind its forward direction
		var pc = CreatePlayer( scene, new Vector3( 32, 0, 10 ), out var ladderMode );

		TickUntil( scene, () => pc.IsClimbing, 60 );

		Assert.IsTrue( pc.IsClimbing );
		Assert.IsTrue( ladderMode.ClimbingRotation.Distance( Rotation.FromYaw( 180 ) ) < 5f,
			$"approaching from behind should flip the climb rotation: {ladderMode.ClimbingRotation}" );
	}

	/// <summary>
	/// A climbable trigger below the player's midpoint is never latched onto - the
	/// scan refuses ladders beneath the character so walking over a low ladder
	/// trigger doesn't yank the player into climb mode.
	/// </summary>
	[TestMethod]
	public void LadderBelowFeetIsIgnored()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );
		CreateLadderTrigger( scene, new Vector3( 12, 0, 10 ), new Vector3( 20, 20, 20 ) );
		var pc = CreatePlayer( scene, new Vector3( 0, 0, 10 ), out var ladderMode );

		for ( int i = 0; i < 30; i++ )
		{
			scene.GameTick();
			Assert.IsFalse( pc.IsClimbing, "a ladder below the feet must never be climbed" );
		}

		Assert.IsNull( ladderMode.ClimbingObject );
		Assert.IsTrue( pc.Mode is MoveModeWalk, $"walking should stay in control: {pc.Mode}" );
	}

	/// <summary>
	/// When the ladder trigger goes away the scan clears the climbing object, the
	/// mode scores itself out, walking takes back over and the player falls back
	/// onto the floor.
	/// </summary>
	[TestMethod]
	public void RemovingLadderStopsClimbing()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );
		var ladder = CreateLadderTrigger( scene, new Vector3( 16, 0, 128 ), new Vector3( 20, 20, 256 ) );
		var pc = CreatePlayer( scene, new Vector3( 0, 0, 10 ), out var ladderMode );

		TickUntil( scene, () => pc.IsClimbing, 60 );
		Assert.IsTrue( pc.IsClimbing );

		ladder.Enabled = false;

		TickUntil( scene, () => !pc.IsClimbing, 20 );

		Assert.IsFalse( pc.IsClimbing, "disabling the ladder should end the climb" );
		Assert.IsTrue( pc.Mode is MoveModeWalk, $"walking should take back over: {pc.Mode}" );
		Assert.IsFalse( ladderMode.ClimbingObject.IsValid(), "the climbing object should be cleared" );

		TickUntil( scene, () => pc.IsOnGround, 60 );
		Assert.IsTrue( pc.IsOnGround, $"gravity should return and land the player: {pc.WorldPosition}" );
	}
}
