using Sandbox.Movement;
using System;

namespace SceneTests.Components;

/// <summary>
/// Pins the PlayerController contract that's reachable without live input: the
/// rigidbody/collider rig it builds on awake, body dimensions, grounding against a
/// static floor, WishVelocity driven movement, jumping, ducking, the move-mode
/// state machine and serialization. Anything needing real Input is out of scope.
/// </summary>
[TestClass]
public class PlayerControllerTest
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
	/// Creates a PlayerController at the given position, configured for headless
	/// ticking: input controls off (there's no input to read) and footstep sounds
	/// off (no audio assets mounted).
	/// </summary>
	static PlayerController CreatePlayer( Scene scene, Vector3 position )
	{
		var go = scene.CreateObject();
		go.Name = "Player";
		go.WorldPosition = position;

		var pc = go.Components.Create<PlayerController>();
		pc.UseInputControls = false;
		pc.EnableFootstepSounds = false;

		return pc;
	}

	/// <summary>
	/// Ticks the scene until the controller reports ground, bounded so a regression
	/// fails fast instead of hanging.
	/// </summary>
	static void TickUntilGrounded( Scene scene, PlayerController pc, int maxTicks = 60 )
	{
		for ( int i = 0; i < maxTicks && !pc.IsOnGround; i++ )
		{
			scene.GameTick();
		}
	}

	/// <summary>
	/// Creating the component builds the whole rig: a hidden Rigidbody on the same
	/// object, a hidden "Colliders" child holding the capsule and feet colliders,
	/// a walk move mode, and a body locked against rotation with the mass override.
	/// </summary>
	[TestMethod]
	public void CreationBuildsBodyAndColliders()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var pc = go.Components.Create<PlayerController>();

		// rigidbody auto-created on the same object and hidden by default
		Assert.IsTrue( pc.Body.IsValid() );
		Assert.AreEqual( go, pc.Body.GameObject );
		Assert.IsTrue( pc.Body.Flags.HasFlag( ComponentFlags.Hidden ) );

		// colliders live on a hidden "Colliders" child object
		Assert.IsTrue( pc.ColliderObject.IsValid() );
		Assert.AreEqual( "Colliders", pc.ColliderObject.Name );
		Assert.AreEqual( go, pc.ColliderObject.Parent );
		Assert.IsTrue( pc.ColliderObject.Flags.HasFlag( GameObjectFlags.Hidden ) );

		Assert.IsTrue( pc.BodyCollider.IsValid() );
		Assert.AreEqual( pc.ColliderObject, pc.BodyCollider.GameObject );
		Assert.IsTrue( pc.FeetCollider.IsValid() );
		Assert.AreEqual( pc.ColliderObject, pc.FeetCollider.GameObject );

		// the walk move mode is installed and selected on awake
		Assert.IsTrue( pc.Mode is MoveModeWalk );

		// the body is set up for character use
		Assert.IsTrue( pc.Body.Locking.Pitch );
		Assert.IsTrue( pc.Body.Locking.Yaw );
		Assert.IsTrue( pc.Body.Locking.Roll );
		Assert.AreEqual( 500f, pc.Body.MassOverride );
		Assert.IsTrue( pc.Body.OverrideMassCenter );
		Assert.AreEqual( 36f, pc.Body.MassCenterOverride.z, 0.01f );
		Assert.AreEqual( RigidbodyFlags.DisableCollisionSounds, pc.Body.RigidbodyFlags );
		Assert.IsTrue( pc.Body.CollisionEventsEnabled );
		Assert.IsTrue( pc.Body.CollisionUpdateEventsEnabled );
	}

	/// <summary>
	/// Pins the documented property defaults on a freshly created controller.
	/// </summary>
	[TestMethod]
	public void DefaultPropertySurface()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var pc = go.Components.Create<PlayerController>();

		Assert.AreEqual( 16f, pc.BodyRadius );
		Assert.AreEqual( 72f, pc.BodyHeight );
		Assert.AreEqual( 500f, pc.BodyMass );
		Assert.AreEqual( 1f, pc.BrakePower );
		Assert.AreEqual( 0.1f, pc.AirFriction );

		Assert.IsTrue( pc.UseInputControls );
		Assert.AreEqual( 110f, pc.WalkSpeed );
		Assert.AreEqual( 320f, pc.RunSpeed );
		Assert.AreEqual( 70f, pc.DuckedSpeed );
		Assert.AreEqual( 300f, pc.JumpSpeed );
		Assert.AreEqual( 36f, pc.DuckedHeight );
		Assert.AreEqual( "run", pc.AltMoveButton );
		Assert.IsFalse( pc.RunByDefault );
		Assert.IsTrue( pc.EnablePressing );
		Assert.AreEqual( "use", pc.UseButton );
		Assert.AreEqual( 130f, pc.ReachLength );
		Assert.IsTrue( pc.UseLookControls );
		Assert.IsTrue( pc.RotateWithGround );
		Assert.AreEqual( 90f, pc.PitchClamp );

		Assert.IsTrue( pc.UseCameraControls );
		Assert.AreEqual( 8f, pc.EyeDistanceFromTop );
		Assert.IsTrue( pc.ThirdPerson );
		Assert.IsTrue( pc.HideBodyInFirstPerson );
		Assert.AreEqual( "view", pc.ToggleCameraModeButton );

		Assert.IsTrue( pc.UseAnimatorControls );
		Assert.IsTrue( pc.EnableFootstepSounds );

		Assert.IsFalse( pc.IsDucking );
		Assert.IsFalse( pc.IsClimbing );
		Assert.IsFalse( pc.IsSwimming );
		Assert.IsFalse( pc.IsOnGround );
		Assert.IsTrue( pc.IsAirborne );
		Assert.AreEqual( Vector3.Zero, pc.WishVelocity );
		Assert.AreEqual( 72f, pc.CurrentHeight, "standing height comes from BodyHeight" );
	}

	/// <summary>
	/// The collider rig is sized from BodyRadius/BodyHeight: the capsule covers the
	/// upper half, the feet box the lower half - and resizing reapplies on tick.
	/// </summary>
	[TestMethod]
	public void BodyDimensionsFollowProperties()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var pc = go.Components.Create<PlayerController>();

		var capsuleRadius = (16f * MathF.Sqrt( 2f )) / 2f;
		Assert.AreEqual( capsuleRadius, pc.BodyCollider.Radius, 0.001f );
		Assert.AreEqual( 72f - capsuleRadius, pc.BodyCollider.Start.z, 0.001f );
		Assert.AreEqual( 36f, pc.BodyCollider.End.z, 0.001f );
		Assert.IsTrue( pc.BodyCollider.Enabled );

		Assert.AreEqual( new Vector3( 16, 16, 36 ), pc.FeetCollider.Scale );
		Assert.AreEqual( new Vector3( 0, 0, 18 ), pc.FeetCollider.Center );
		Assert.IsTrue( pc.FeetCollider.Enabled );

		// BodyBox is the trace hull: half-radius footprint, full height
		var hull = pc.BodyBox();
		Assert.AreEqual( new Vector3( -8, -8, 0 ), hull.Mins );
		Assert.AreEqual( new Vector3( 8, 8, 72 ), hull.Maxs );

		// resizing takes effect when UpdateBody runs on the next tick
		pc.BodyHeight = 100;
		scene.GameTick();

		Assert.AreEqual( 100f - capsuleRadius, pc.BodyCollider.Start.z, 0.001f );
		Assert.AreEqual( 50f, pc.BodyCollider.End.z, 0.001f );
		Assert.AreEqual( 50f, pc.FeetCollider.Scale.z, 0.001f );
	}

	/// <summary>
	/// A controller spawned above a static floor falls under its rigidbody's gravity
	/// and lands: IsOnGround flips on, the ground object is reported as static, and
	/// the headroom probe reports the full 100 units of open sky.
	/// </summary>
	[TestMethod]
	public void FallsAndLandsOnFloor()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var floor = CreateFloor( scene );
		var floorCollider = floor.Components.Get<BoxCollider>();
		var pc = CreatePlayer( scene, new Vector3( 0, 0, 10 ) );
		var go = pc.GameObject;

		Assert.IsFalse( pc.IsOnGround );
		Assert.IsTrue( pc.IsAirborne );

		TickUntilGrounded( scene, pc );

		Assert.IsTrue( pc.IsOnGround, $"should have landed: {go.WorldPosition}" );
		Assert.IsFalse( pc.IsAirborne );
		Assert.AreEqual( floor, pc.GroundObject );
		Assert.AreEqual( floorCollider, pc.GroundComponent );
		Assert.IsFalse( pc.GroundIsDynamic, "a static collider is not dynamic ground" );
		Assert.IsTrue( go.WorldPosition.z < 2f, $"{go.WorldPosition}" );
		Assert.IsTrue( go.WorldPosition.z > -1f, $"{go.WorldPosition}" );

		Assert.AreEqual( 100f, pc.Headroom, 1f, "open sky above - the probe reports its full length" );
	}

	/// <summary>
	/// WishVelocity drives the body across the floor through the walk mode, and
	/// clearing it engages the brakes - the velocity bleeds off quickly.
	/// </summary>
	[TestMethod]
	public void WishVelocityWalksAcrossFloor()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );
		var pc = CreatePlayer( scene, new Vector3( 0, 0, 1 ) );
		var go = pc.GameObject;

		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );

		pc.WishVelocity = new Vector3( 100, 0, 0 );

		for ( int i = 0; i < 20; i++ )
		{
			scene.GameTick();
		}

		Assert.IsTrue( go.WorldPosition.x > 50f, $"should have walked: {go.WorldPosition}" );
		Assert.IsTrue( MathF.Abs( go.WorldPosition.y ) < 5f, $"no sideways drift: {go.WorldPosition}" );
		Assert.IsTrue( pc.Velocity.x > 10f, $"{pc.Velocity}" );
		Assert.IsTrue( pc.IsOnGround, "walking on a flat floor stays grounded" );

		// no wish velocity = brakes on, the body stops quickly
		pc.WishVelocity = Vector3.Zero;

		for ( int i = 0; i < 20; i++ )
		{
			scene.GameTick();
		}

		Assert.IsTrue( pc.Velocity.Length < 10f, $"the brakes should have stopped it: {pc.Velocity}" );
	}

	/// <summary>
	/// Jump adds an upwards velocity to the body and ungrounds immediately via
	/// PreventGrounding - the controller rises, falls back and lands again.
	/// </summary>
	[TestMethod]
	public void JumpLaunchesAndRelands()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );
		var pc = CreatePlayer( scene, new Vector3( 0, 0, 1 ) );
		var go = pc.GameObject;

		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );

		pc.Jump( Vector3.Up * 200f );

		Assert.IsFalse( pc.IsOnGround, "PreventGrounding ungrounds the moment we jump" );
		Assert.IsTrue( pc.Body.Velocity.z > 150f, $"{pc.Body.Velocity}" );

		// rises off the floor...
		for ( int i = 0; i < 3; i++ )
		{
			scene.GameTick();
		}

		Assert.IsTrue( go.WorldPosition.z > 5f, $"should be in the air: {go.WorldPosition}" );

		// ...and comes back down
		TickUntilGrounded( scene, pc );

		Assert.IsTrue( pc.IsOnGround, $"should have landed again: {go.WorldPosition}" );
		Assert.IsTrue( go.WorldPosition.z < 2f, $"{go.WorldPosition}" );
	}

	/// <summary>
	/// UpdateDucking swaps CurrentHeight between BodyHeight and DuckedHeight, and
	/// standing back up is refused while there isn't enough headroom.
	/// </summary>
	[TestMethod]
	public void DuckingNeedsHeadroomToStand()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );
		var pc = CreatePlayer( scene, new Vector3( 0, 0, 1 ) );

		TickUntilGrounded( scene, pc );
		scene.GameTick(); // refresh Headroom with open sky above

		pc.UpdateDucking( true );
		Assert.IsTrue( pc.IsDucking );
		Assert.AreEqual( 36f, pc.CurrentHeight, "ducked height comes from DuckedHeight" );

		// plenty of headroom - standing up works
		pc.UpdateDucking( false );
		Assert.IsFalse( pc.IsDucking );
		Assert.AreEqual( 72f, pc.CurrentHeight );

		// duck again and slide a ceiling in at z=40 - too low to stand under
		pc.UpdateDucking( true );

		var ceiling = scene.CreateObject();
		ceiling.Name = "Ceiling";
		ceiling.WorldPosition = new Vector3( 0, 0, 90 );
		var ceilingBox = ceiling.Components.Create<BoxCollider>();
		ceilingBox.Scale = new Vector3( 400, 400, 100 );
		ceilingBox.Static = true;

		scene.GameTick(); // refresh Headroom against the new ceiling

		pc.UpdateDucking( false );
		Assert.IsTrue( pc.IsDucking, "must stay ducked without the headroom to stand" );
	}

	/// <summary>
	/// The move mode state machine picks the enabled mode with the highest score
	/// each tick, firing OnModeBegin/OnModeEnd on the way - and a mode that forbids
	/// grounding ungrounds the controller.
	/// </summary>
	[TestMethod]
	public void ModeSelectionPicksHighestScore()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );
		var pc = CreatePlayer( scene, new Vector3( 0, 0, 1 ) );
		var go = pc.GameObject;

		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.Mode is MoveModeWalk );

		var probe = go.Components.Create<ProbeMoveMode>();
		probe.Controller = pc; // codegen-free test type: wire the [RequireComponent] by hand
		probe.ScoreValue = 10;

		scene.GameTick();
		Assert.AreEqual( probe, pc.Mode, "the higher scoring mode wins" );
		Assert.AreEqual( 1, probe.BeginCount );

		scene.GameTick();
		Assert.IsFalse( pc.IsOnGround, "the probe mode doesn't allow grounding" );

		// drop the score below the walk mode - control hands back
		probe.ScoreValue = -5;
		scene.GameTick();

		Assert.IsTrue( pc.Mode is MoveModeWalk, "the walk mode should win again" );
		Assert.AreEqual( pc.Mode, probe.EndedWith, "OnModeEnd is told which mode takes over" );
		Assert.AreEqual( 1, probe.BeginCount, "the probe was only begun once" );

		// grounding works again under the walk mode
		TickUntilGrounded( scene, pc, 30 );
		Assert.IsTrue( pc.IsOnGround );
	}

	/// <summary>
	/// A controller round-trips through JSON: tuned properties survive, and the
	/// deserialized copy rebuilds its body and reuses the serialized colliders child
	/// instead of creating a duplicate.
	/// </summary>
	[TestMethod]
	public void SerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var pc = go.Components.Create<PlayerController>();
		pc.WalkSpeed = 123;
		pc.RunSpeed = 456;
		pc.BodyHeight = 80;
		pc.RunByDefault = true;
		pc.UseInputControls = false;

		var json = go.Serialize().ToJsonString();
		var jsonObject = Json.ParseToJsonObject( json );
		Assert.IsNotNull( jsonObject );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var copy = new GameObject( false );
		copy.Deserialize( jsonObject );
		copy.Enabled = true;

		var pc2 = copy.GetComponent<PlayerController>();
		Assert.IsNotNull( pc2 );
		Assert.AreEqual( 123f, pc2.WalkSpeed );
		Assert.AreEqual( 456f, pc2.RunSpeed );
		Assert.AreEqual( 80f, pc2.BodyHeight );
		Assert.IsTrue( pc2.RunByDefault );
		Assert.IsFalse( pc2.UseInputControls );

		Assert.IsTrue( pc2.Body.IsValid() );
		Assert.AreNotEqual( pc.Body, pc2.Body );
		Assert.IsTrue( pc2.ColliderObject.IsValid() );
		Assert.IsTrue( pc2.BodyCollider.IsValid() );
		Assert.IsTrue( pc2.FeetCollider.IsValid() );
		Assert.IsTrue( pc2.Mode is MoveModeWalk );

		Assert.AreEqual( 1, copy.Children.Count( x => x.Name == "Colliders" ),
			"awake must reuse the serialized colliders child, not duplicate it" );
	}
}

/// <summary>
/// A move mode with a test-controlled score that records the state machine
/// callbacks. The [RequireComponent] controller wiring is done by the tests since
/// test assemblies don't run codegen.
/// </summary>
public class ProbeMoveMode : MoveMode
{
	/// <summary>
	/// The score this mode reports to the mode selection.
	/// </summary>
	public int ScoreValue { get; set; }

	/// <summary>
	/// How many times OnModeBegin fired.
	/// </summary>
	public int BeginCount { get; private set; }

	/// <summary>
	/// The mode passed to OnModeEnd when this mode lost control.
	/// </summary>
	public MoveMode EndedWith { get; private set; }

	/// <summary>
	/// Reports the test-controlled score to the controller's mode selection.
	/// </summary>
	public override int Score( PlayerController controller ) => ScoreValue;

	/// <summary>
	/// Records that this mode took control.
	/// </summary>
	public override void OnModeBegin() => BeginCount++;

	/// <summary>
	/// Records which mode took over from this one.
	/// </summary>
	public override void OnModeEnd( MoveMode next ) => EndedWith = next;
}
