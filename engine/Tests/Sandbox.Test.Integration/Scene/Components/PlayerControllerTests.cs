using Sandbox.Movement;
using System;
using System.Collections.Generic;

namespace SceneTests.Components;

/// <summary>
/// PlayerController integration coverage without live input.
/// </summary>
[TestClass]
public partial class PlayerControllerTest
{
	public sealed class PhysicsMovementRecorder : Component, IScenePhysicsEvents
	{
		public PlayerController Controller { get; set; }
		public List<(Vector3 Before, Vector3 After, Vector3 Velocity, float Delta, bool Grounded, Vector3 InitialVelocity)> Steps { get; } = [];
		Vector3 _before;
		Vector3 _velocity;

		public void PrePhysicsStep()
		{
			_before = Controller.Body.PhysicsBody.Position;
			_velocity = Controller.Body.Velocity;
		}

		public void PostPhysicsStep() => Steps.Add( (_before, Controller.Body.PhysicsBody.Position,
			Controller.Body.Velocity, Time.Delta, Controller.IsOnGround, _velocity) );
	}

	/// <summary>
	/// Creates a large static floor whose top surface sits at z = 0.
	/// </summary>
	static GameObject CreateFloor( Scene scene )
	{
		var go = scene.CreateObject();
		go.Name = "Floor";
		var is2D = scene.PhysicsMode == ScenePhysicsMode.Physics2D;
		go.WorldPosition = is2D ? new Vector3( 0, 50, 0 ) : new Vector3( 0, 0, -50 );

		var box = go.Components.Create<BoxCollider>();
		box.Scale = is2D ? new Vector3( 4000, 100, 100 ) : new Vector3( 4000, 4000, 100 );
		box.Static = true;

		return go;
	}

	/// <summary>
	/// Creates a headless player with a Source-sized feet footprint.
	/// </summary>
	static PlayerController CreatePlayer( Scene scene, Vector3 position )
	{
		var go = scene.CreateObject( false );
		go.Name = "Player";
		go.WorldPosition = position;

		var pc = go.Components.Create<PlayerController>();
		pc.BodyRadius = 32;
		pc.UseInputControls = false;
		pc.EnableFootstepSounds = false;
		go.Enabled = true;

		return pc;
	}

	internal static void SimulateControllerPhysics( Scene scene, PlayerController player, float delta )
	{
		((IScenePhysicsEvents)player.Body).PrePhysicsStep();
		((IScenePhysicsEvents)player).PrePhysicsStep();
		scene.PhysicsWorld.Step( delta );
		player.Body.UpdateTransformFromBody();
		((IScenePhysicsEvents)player).PostPhysicsStep();
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

	static GameObject CreateStep( Scene scene, float start, float end, float height, Rotation rotation )
	{
		var go = scene.CreateObject();
		var is2D = scene.PhysicsMode == ScenePhysicsMode.Physics2D;
		var center = is2D ? new Vector3( (start + end) * 0.5f, (100 - height) * 0.5f, 0 )
			: new Vector3( (start + end) * 0.5f, 0, (height - 100) * 0.5f );
		go.WorldPosition = rotation * center;
		go.WorldRotation = rotation;
		var collider = go.Components.Create<BoxCollider>();
		collider.Scale = is2D ? new Vector3( end - start, height + 100, 100 ) : new Vector3( end - start, 256, height + 100 );
		collider.Static = true;
		return go;
	}

	static string DescribeGround( PlayerController pc )
	{
		var up = pc.Scene.PhysicsMode == ScenePhysicsMode.Physics2D ? (Vector3)Vector2.Up : Vector3.Up;
		var tr = pc.TraceBody( pc.WorldPosition + up * 4, pc.WorldPosition - up * 2, 1, 0.5f );
		return $"velocity {pc.Body.Velocity}, ground normal {pc.GroundNormal}, box {tr.Hit}/{tr.StartedSolid}/{tr.Normal}, object {tr.GameObject}, body {tr.Body?.GameObject}, collider {tr.Collider?.GameObject}";
	}

	static void CreateStairs( Scene scene, float height, Rotation rotation, bool mesh, float scale = 1, float tread = 24, float gap = 0 )
	{
		var vertices = new List<Vector3>();
		var indices = new List<int>();
		for ( int i = 0; i < 4; ++i )
		{
			var start = 48 + i * tread;
			var end = start + tread - gap;
			var top = (i + 1) * height;
			if ( !mesh )
			{
				var step = CreateStep( scene, start, end, top, rotation );
				step.WorldPosition *= scale;
				step.WorldScale = new Vector3( scale );
				continue;
			}

			var index = vertices.Count;
			vertices.AddRange( [
				new Vector3( start, -128, top ), new Vector3( end, -128, top ),
				new Vector3( end, 128, top ), new Vector3( start, 128, top ),
				new Vector3( start, -128, i * height ), new Vector3( start, 128, i * height )
			] );
			indices.AddRange( [index, index + 1, index + 2, index, index + 2, index + 3,
				index + 4, index + 3, index + 5, index + 4, index, index + 3] );
		}
		if ( mesh )
		{
			var stairs = scene.CreateObject();
			stairs.WorldRotation = rotation;
			stairs.WorldScale = new Vector3( scale );
			var collider = stairs.Components.Create<ModelCollider>();
			collider.Model = Model.Builder.AddCollisionMesh( vertices.ToArray(), indices.ToArray() ).Create();
			collider.Static = true;
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
		Assert.AreEqual( 0f, pc.AirFriction );
		var walk = (MoveModeWalk)pc.Mode;
		Assert.IsTrue( walk.EnableAirStrafing );
		Assert.IsTrue( walk.EnableBunnyHopping );
		Assert.AreEqual( 0.1f, walk.BunnyHopRecoveryTime );
		Assert.AreEqual( 1f, walk.JumpMomentumRetention );

		Assert.IsTrue( pc.UseInputControls );
		Assert.AreEqual( 110f, pc.WalkSpeed );
		Assert.AreEqual( 320f, pc.RunSpeed );
		Assert.AreEqual( 70f, pc.DuckedSpeed );
		Assert.AreEqual( MovementMath.JumpSpeed, pc.JumpSpeed );
		Assert.AreEqual( 0f, pc.CoyoteTime );
		Assert.IsFalse( pc.HoldToJump );
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
	/// The capsule diameter matches the feet width; BodyRadius retains its width convention.
	/// </summary>
	[TestMethod]
	public void BodyDimensionsFollowProperties()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var pc = go.Components.Create<PlayerController>();

		var capsuleRadius = 8f;
		Assert.AreEqual( capsuleRadius, pc.BodyCollider.Radius, 0.001f );
		Assert.AreEqual( 72f - capsuleRadius, pc.BodyCollider.Start.z, 0.001f );
		Assert.AreEqual( 36f, pc.BodyCollider.End.z, 0.001f );
		Assert.IsTrue( pc.BodyCollider.Enabled );

		Assert.AreEqual( new Vector3( 16, 16, 36 ), pc.FeetCollider.Scale );
		Assert.AreEqual( new Vector3( 0, 0, 18 ), pc.FeetCollider.Center );
		Assert.IsTrue( pc.FeetCollider.Enabled );

		// BodyBox uses the feet footprint and full standing height.
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
	/// Source's fixed stop speed deliberately makes low-speed braking independent of wish speed.
	/// </summary>
	[TestMethod]
	[DataRow( 10f, 0.02 )]
	[DataRow( 35f, 0.02 )]
	[DataRow( 70f, 0.02 )]
	[DataRow( 140f, 0.02 )]
	[DataRow( 640f, 0.02 )]
	[DataRow( 10f, 0.01 )]
	[DataRow( 140f, 0.01 )]
	public void GroundMovementUsesSourceStopSpeed( float speed, double delta )
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		CreateFloor( scene );
		var scaled = CreatePlayer( scene, new Vector3( 0, 100, 1 ) );
		TickUntilGrounded( scene, scaled );
		Assert.IsTrue( scaled.IsOnGround );

		scaled.GroundFriction = 0.8f;
		using var timeScope = Time.Scope( 1.0, delta );
		scaled.Body.Velocity = Vector3.Forward * speed;
		scaled.WishVelocity = Vector3.Zero;
		scaled.Mode.AddVelocity();
		var expected = MathF.Max( 0, speed - MathF.Max( speed, 100 ) * 4 * (float)delta );
		if ( expected < 1 ) expected = 0;
		Assert.AreEqual( expected, scaled.Body.Velocity.x, 0.001f );
	}

	[TestMethod]
	[DataRow( 0.01, 16f )]
	[DataRow( 0.02, 30f )]
	public void AirAccelerationCapsProjectionNotTotalSpeed( double delta, float expectedSideSpeed )
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var pc = CreatePlayer( scene, Vector3.Up * 1000 );
		using var timeScope = Time.Scope( 1.0, delta );
		pc.Body.Velocity = new Vector3( 500, 0, -200 );
		pc.WishVelocity = new Vector3( 0, 160, 400 );
		pc.GroundFriction = 0; // previous ground must not affect air acceleration
		pc.Mode.AddVelocity();

		Assert.AreEqual( 500f, pc.Body.Velocity.x, 0.001f );
		Assert.AreEqual( expectedSideSpeed, pc.Body.Velocity.y, 0.001f );
		Assert.AreEqual( -200f, pc.Body.Velocity.z, 0.001f );
		for ( int i = 0; i < 10; i++ ) pc.Mode.AddVelocity();
		Assert.AreEqual( 30f, pc.Body.Velocity.y, 0.001f );

		pc.WishVelocity = Vector3.Forward * 320;
		pc.Mode.AddVelocity();
		Assert.AreEqual( 500f, pc.Body.Velocity.x, 0.001f, "holding forward must not clamp existing speed" );
		pc.WishVelocity = Vector3.Zero;
		var coasting = pc.Body.Velocity;
		pc.Mode.AddVelocity();
		Assert.AreEqual( coasting, pc.Body.Velocity );
	}

	[TestMethod]
	public void AirStrafingBuildsSpeedBeyondRunSpeed()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var pc = CreatePlayer( scene, Vector3.Up * 1000 );
		using var timeScope = Time.Scope( 1.0, 0.015 );
		pc.Body.Velocity = Vector3.Forward * 320;
		for ( int i = 0; i < 100; i++ )
		{
			var velocity = pc.Body.Velocity;
			pc.WishVelocity = new Vector3( -velocity.y, velocity.x, 0 ).Normal * 320;
			pc.Mode.AddVelocity();
		}

		Assert.AreEqual( MathF.Sqrt( 320 * 320 + 100 * 30 * 30 ), pc.Body.Velocity.Length, 0.1f );
	}

	[TestMethod]
	public void AirAccelerationRespectsAnalogInputAndTuning()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var pc = CreatePlayer( scene, Vector3.Up * 1000 );
		var walk = (MoveModeWalk)pc.Mode;
		using var timeScope = Time.Scope( 1.0, 0.02 );
		pc.WishVelocity = Vector3.Forward * 10;
		pc.Mode.AddVelocity();
		Assert.AreEqual( 2f, pc.Body.Velocity.x, 0.001f );
		for ( int i = 0; i < 10; i++ ) pc.Mode.AddVelocity();
		Assert.AreEqual( 10f, pc.Body.Velocity.x, 0.001f );

		walk.AirAcceleration = 100;
		walk.AirSpeedCap = 60;
		pc.WishVelocity = Vector3.Forward * 320;
		pc.Mode.AddVelocity();
		Assert.AreEqual( 60f, pc.Body.Velocity.x, 0.001f );
		walk.AirAcceleration = 0;
		pc.WishVelocity = Vector3.Left * 320;
		pc.Mode.AddVelocity();
		Assert.AreEqual( 0f, pc.Body.Velocity.y, 0.001f );
	}

	[TestMethod]
	[DataRow( 60f )]
	[DataRow( -60f )]
	public void SteepRampsSurfWithoutGroundingOrLosingLateralMomentum( float pitch )
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var ramp = CreateFloor( scene );
		ramp.Components.Get<BoxCollider>().Scale = new Vector3( 16000, 16000, 100 );
		ramp.WorldRotation = Rotation.FromPitch( pitch );
		var normal = ramp.WorldRotation * Vector3.Up;
		ramp.WorldPosition = normal * -50;
		var pc = CreatePlayer( scene, Vector3.Up * 40 );
		pc.Body.Velocity = new Vector3( 0, 300, -100 );
		bool touchedRamp = false;
		for ( int i = 0; i < 30; i++ )
		{
			scene.GameTick();
			Assert.IsFalse( pc.IsOnGround, "a surf ramp must not engage ground braking or snapping" );
			Assert.IsFalse( pc.Body.Gravity, "walk mode integrates gravity in two half-steps" );
			Assert.AreEqual( 0f, pc.Body.LinearDamping );
			Assert.AreEqual( 0f, pc.FeetCollider.Friction );
			Assert.AreEqual( 300f, pc.Body.Velocity.y, 1f, "ramp contact must preserve tangential momentum" );
			touchedRamp |= MathF.Abs( pc.Body.Velocity.x ) > 20;
		}

		Assert.IsTrue( touchedRamp, "gravity and ramp contact should generate downhill velocity" );
		Assert.IsTrue( pc.WorldPosition.Dot( normal ) > 0, "the player must remain above the ramp plane" );
		// The final half-gravity step follows contact resolution.
		Assert.IsTrue( MathF.Abs( pc.Body.Velocity.Dot( normal ) ) < 10, $"the rigidbody must slide along the ramp: {pc.Body.Velocity.Dot( normal )}" );
	}

	[TestMethod]
	[DataRow( 0f, 0f )]
	[DataRow( 0.4f, 0.5f )]
	[DataRow( 0.8f, 1f )]
	[DataRow( 1f, 1f )]
	public void GroundTractionIsNormalizedToDefaultSurface( float friction, float traction )
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		CreateFloor( scene );
		var pc = CreatePlayer( scene, new Vector3( 0, 0, 1 ) );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.GroundFriction = friction;
		using var timeScope = Time.Scope( 1.0, 0.02 );

		pc.Body.Velocity = Vector3.Zero;
		pc.WishVelocity = Vector3.Forward * 100;
		pc.Mode.AddVelocity();
		Assert.AreEqual( 20 * traction, pc.Body.Velocity.x, 0.001f );

		pc.Body.Velocity = Vector3.Forward * 100;
		pc.WishVelocity = Vector3.Zero;
		pc.Mode.AddVelocity();
		Assert.AreEqual( 100 - 8 * traction, pc.Body.Velocity.x, 0.001f );

		pc.BrakePower = 0;
		pc.Body.Velocity = Vector3.Forward * 100;
		pc.Mode.AddVelocity();
		Assert.AreEqual( 100f, pc.Body.Velocity.x, 0.001f, "zero brake power must preserve momentum" );
	}

	[TestMethod]
	[DataRow( 20f )]
	[DataRow( -20f )]
	public void SlopeFixStopsAfterWalkingAndHoldsPosition( float pitch )
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var floor = CreateFloor( scene );
		floor.WorldRotation = Rotation.FromPitch( pitch );
		var pc = CreatePlayer( scene, new Vector3( 0, 0, 40 ) );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = new Vector3( 100, 0, 0 );
		for ( int i = 0; i < 20; i++ ) scene.GameTick();
		pc.WishVelocity = Vector3.Zero;
		for ( int i = 0; i < 20; i++ ) scene.GameTick();
		var stopped = pc.WorldPosition;
		for ( int i = 0; i < 100; i++ ) scene.GameTick();

		Assert.IsTrue( pc.IsOnGround );
		Assert.IsTrue( (pc.WorldPosition - stopped).Length < 1, $"should not drift after stopping on a slope: {pc.WorldPosition - stopped}" );
		Assert.IsTrue( pc.Body.Velocity.Length < 1, $"should remain stopped: {pc.Body.Velocity}" );
	}

	[TestMethod]
	[DataRow( 1f, 0f )]
	[DataRow( 8f, 0f )]
	[DataRow( 18f, 0f )]
	[DataRow( 18f, 45f )]
	public void WalksUpSuccessiveStepsWithoutLosingGround( float height, float yaw )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		var direction = rotation.Forward;
		for ( int i = 0; i < 4; ++i )
		{
			CreateStep( scene, 48 + i * 48, 96 + i * 48, (i + 1) * height, rotation );
		}

		var pc = CreatePlayer( scene, Vector3.Up );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = direction * 160;
		for ( int i = 0; i < 180 && pc.WorldPosition.Dot( direction ) < 216; ++i )
		{
			scene.GameTick( 1.0 / 60 );
			Assert.IsTrue( pc.IsOnGround, $"lost ground at {pc.WorldPosition}" );
		}

		Assert.IsTrue( pc.WorldPosition.Dot( direction ) >= 216, $"stuck at {pc.WorldPosition}" );
		Assert.AreEqual( 4 * height, pc.WorldPosition.z, 1.0f );
		Assert.IsTrue( MathF.Abs( pc.Body.Velocity.z ) < 1, $"launched off a step: {pc.Body.Velocity}" );
	}

	[TestMethod]
	[DataRow( 1f, 160f, 0f, false, 1f )]
	[DataRow( 8f, 80f, 0f, false, 1f )]
	[DataRow( 8f, 110f, 0f, false, 1f )]
	[DataRow( 18f, 160f, 0f, false, 1f )]
	[DataRow( 18f, 320f, 0f, false, 1f )]
	[DataRow( 18f, 160f, 45f, false, 1f )]
	[DataRow( 18f, 320f, 45f, false, 1f )]
	[DataRow( 8f, 110f, 0f, true, 1f )]
	[DataRow( 16f, 110f, 0f, true, 1f )]
	[DataRow( 18f, 160f, 0f, true, 1f )]
	[DataRow( 18f, 320f, 0f, true, 1f )]
	[DataRow( 18f, 160f, 25f, true, 1f )]
	[DataRow( 18f, 320f, 45f, true, 1f )]
	[DataRow( 1f, 160f, 0f, true, 0.5f )]
	[DataRow( 18f, 160f, 0f, true, 0.5f )]
	[DataRow( 18f, 320f, 0f, true, 0.5f )]
	[DataRow( 18f, 320f, 45f, true, 2f )]
	public void StairsPreserveForwardSpeed( float height, float speed, float yaw, bool mesh, float scale )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		var direction = rotation.Forward;
		CreateStairs( scene, height, rotation, mesh, scale );

		var pc = CreatePlayer( scene, Vector3.Up );
		pc.WorldScale = new Vector3( scale );
		var movement = pc.Components.Create<PhysicsMovementRecorder>();
		movement.Controller = pc;
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = direction * speed;
		pc.Body.Velocity = pc.WishVelocity;
		const double delta = 1.0 / 50;
		var measuredSteps = 0;
		var ticks = (int)MathF.Ceiling( 144 * scale / (speed * (float)delta) ) + 20;
		for ( int i = 0; i < ticks && pc.Body.PhysicsBody.Position.Dot( direction ) < 132 * scale; ++i )
		{
			movement.Steps.Clear();
			scene.GameTick( delta );
			// Check actual physics steps, not render ticks or interpolated object transforms.
			foreach ( var step in movement.Steps )
			{
				++measuredSteps;
				Assert.AreEqual( speed, step.Velocity.Dot( direction ), 0.5f, $"slowed on stairs from {step.Before} to {step.After}: {step.Velocity}" );
				var distance = (step.After - step.Before).Dot( direction );
				Assert.IsTrue( distance >= speed * step.Delta - 0.05f, $"lost forward movement from {step.Before} to {step.After}: {distance}" );
				Assert.AreEqual( 0, (step.After - step.Before).Dot( rotation.Left ), 0.05f, $"deflected on stairs from {step.Before} to {step.After}" );
				Assert.IsTrue( step.Grounded, $"lost ground at {step.After}" );
				Assert.IsTrue( MathF.Abs( step.Velocity.z ) < 1, $"launched at {step.After}: {step.Velocity}" );
			}
		}

		Assert.IsTrue( measuredSteps > 0 );
		Assert.IsTrue( pc.Body.PhysicsBody.Position.Dot( direction ) >= 132 * scale );
		Assert.AreEqual( 4 * height * scale, pc.Body.PhysicsBody.Position.z, 1.0f );
	}

	[TestMethod]
	[DataRow( -10f )]
	[DataRow( 10f )]
	public void StepUpFromSlopedGround( float pitch )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var floor = CreateFloor( scene );
		floor.WorldRotation = Rotation.FromPitch( pitch );
		var surface = scene.Trace.Ray( new Vector3( 48, 0, 128 ), new Vector3( 48, 0, -128 ) ).Run();
		Assert.IsTrue( surface.Hit );
		var height = surface.EndPosition.z + 8;
		var step = CreateStep( scene, 48, 256, height, Rotation.Identity );
		var pc = CreatePlayer( scene, Vector3.Up * 40 );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = Vector3.Forward * 160;
		// Stop before the rising floor overtakes the step.
		for ( int i = 0; i < 120 && pc.WorldPosition.x < 64; ++i )
			scene.GameTick( 1.0 / 60 );

		Assert.IsTrue( pc.WorldPosition.x >= 64, $"stuck on a sloped approach: {pc.WorldPosition}" );
		Assert.AreEqual( height, pc.WorldPosition.z, 1.0f );
		Assert.AreEqual( step, pc.GroundObject );
		Assert.IsTrue( pc.IsOnGround, $"position {pc.WorldPosition}: {DescribeGround( pc )}" );
		Assert.IsTrue( MathF.Abs( pc.Body.Velocity.z ) < 1 );
	}

	[TestMethod]
	[DataRow( 8f )]
	[DataRow( 18f )]
	public void SteppingUsesThe2DUpDirection( float height )
	{
		var scene = new Scene { PhysicsMode = ScenePhysicsMode.Physics2D };
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateStep( scene, 48, 256, height, Rotation.Identity );
		var pc = CreatePlayer( scene, Vector2.Up );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround, $"2D grounding failed at {pc.WorldPosition}: {DescribeGround( pc )}" );
		pc.WishVelocity = Vector3.Forward * 160;
		for ( int i = 0; i < 120 && pc.WorldPosition.x < 96; ++i )
			scene.GameTick( 1.0 / 60 );

		Assert.IsTrue( pc.WorldPosition.x >= 96, $"2D step blocked movement: {pc.WorldPosition}" );
		Assert.AreEqual( -height, pc.WorldPosition.y, 1.0f );
		Assert.IsTrue( pc.IsOnGround );
		Assert.IsTrue( MathF.Abs( pc.Body.Velocity.y ) < 1 );
	}

	[TestMethod]
	public void StepUpOntoMovingPlatform()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var platform = scene.CreateObject();
		platform.WorldPosition = new Vector3( 152, 0, -46 );
		var body = platform.Components.Create<Rigidbody>();
		body.MotionEnabled = false;
		var collider = platform.Components.Create<BoxCollider>();
		collider.Scale = new Vector3( 208, 256, 108 );
		body.Velocity = Vector3.Forward * 20;
		var pc = CreatePlayer( scene, Vector3.Up );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = Vector3.Forward * 160;
		for ( int i = 0; i < 150 && pc.WorldPosition.x < 96; ++i )
			scene.GameTick( 1.0 / 60 );

		Assert.IsTrue( pc.WorldPosition.x >= 96, $"moving step blocked movement: {pc.WorldPosition}" );
		Assert.AreEqual( platform, pc.GroundObject );
		Assert.IsTrue( pc.GroundVelocity.x > 0, $"lost the platform's velocity: {pc.GroundVelocity}, platform {body.Velocity}" );
		Assert.IsTrue( MathF.Abs( pc.Body.Velocity.z ) < 1 );
	}

	[TestMethod]
	[DataRow( 10f )]
	[DataRow( 40f )]
	public void SlowMovementCanStepWithoutAddingVerticalSpeed( float speed )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateStep( scene, 48, 256, 8, Rotation.Identity );
		var pc = CreatePlayer( scene, Vector3.Up );
		pc.WorldPosition = new Vector3( 48 - pc.BodyRadius * 0.5f - 0.05f, 0, 1 );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = Vector3.Forward * speed;
		for ( int i = 0; i < 90 && pc.WorldPosition.z < 7; ++i )
		{
			scene.GameTick( 1.0 / 60 );
			Assert.IsTrue( MathF.Abs( pc.Body.Velocity.z ) < 1, $"step injected vertical speed: {pc.Body.Velocity}" );
			Assert.IsTrue( pc.IsOnGround );
		}

		Assert.AreEqual( 8f, pc.WorldPosition.z, 0.2f, $"did not lift by the exact step height: {pc.WorldPosition}" );
	}

	[TestMethod]
	[DataRow( 19f, 0f )]
	[DataRow( 40f, 0f )]
	[DataRow( 8f, 76f )]
	public void StepUpRespectsHeightAndHeadroom( float height, float ceiling )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateStep( scene, 48, 256, height, Rotation.Identity );
		if ( ceiling > 0 )
		{
			var roof = scene.CreateObject();
			roof.WorldPosition = new Vector3( 96, 0, ceiling + 8 );
			var collider = roof.Components.Create<BoxCollider>();
			collider.Scale = new Vector3( 320, 256, 16 );
			collider.Static = true;
		}

		var pc = CreatePlayer( scene, Vector3.Up );
		TickUntilGrounded( scene, pc );
		pc.WishVelocity = Vector3.Forward * 160;
		for ( int i = 0; i < 90; ++i )
			scene.GameTick( 1.0 / 60 );

		Assert.IsTrue( pc.WorldPosition.x < 44, $"passed an obstructed step: {pc.WorldPosition}" );
		Assert.IsTrue( pc.WorldPosition.z < 1, $"climbed an obstructed step: {pc.WorldPosition}" );
		Assert.IsTrue( pc.IsOnGround );
	}

	[TestMethod]
	[DataRow( 8f )]
	[DataRow( 17.5f )]
	[DataRow( 18f )]
	public void WalkingDownStepsRespectsSnapLimit( float height )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateStep( scene, -256, 48, height * 3, Rotation.Identity );
		CreateStep( scene, 48, 96, height * 2, Rotation.Identity );
		CreateStep( scene, 96, 144, height, Rotation.Identity );
		var pc = CreatePlayer( scene, Vector3.Up * (height * 3 + 1) );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = Vector3.Forward * 160;
		var fell = false;
		for ( int i = 0; i < 150 && (pc.WorldPosition.x < 176 || !pc.IsOnGround); ++i )
		{
			scene.GameTick( 1.0 / 60 );
			fell |= !pc.IsOnGround;
			if ( height < 18 ) Assert.IsTrue( pc.IsOnGround, $"fell between steps at {pc.WorldPosition}: {DescribeGround( pc )}" );
		}

		Assert.AreEqual( height >= 18, fell );
		scene.GameTick( 1.0 / 60 );
		Assert.IsTrue( pc.WorldPosition.x >= 176, $"stuck at {pc.WorldPosition}" );
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( 0f, pc.WorldPosition.z, 1.0f );
	}

	[TestMethod]
	public void DeepLedgesDoNotSnapDown()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateStep( scene, -256, 48, 48, Rotation.Identity );
		var pc = CreatePlayer( scene, Vector3.Up * 49 );
		TickUntilGrounded( scene, pc );
		pc.WishVelocity = Vector3.Forward * 160;
		for ( int i = 0; i < 90 && pc.IsOnGround; ++i )
			scene.GameTick( 1.0 / 60 );

		Assert.IsFalse( pc.IsOnGround, $"did not leave the ledge: {pc.WorldPosition}" );
		Assert.IsTrue( pc.WorldPosition.z > 44, $"snapped down a deep ledge: {pc.WorldPosition}" );
	}

	[TestMethod]
	public void JumpingDoesNotStepOrSnapBackToGround()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		CreateStep( scene, 48, 256, 8, Rotation.Identity );
		var pc = CreatePlayer( scene, new Vector3( 24, 0, 1 ) );
		TickUntilGrounded( scene, pc );
		pc.WishVelocity = Vector3.Forward * 160;
		pc.Jump( Vector3.Up * 200 );
		var previous = pc.WorldPosition.z;
		for ( int i = 0; i < 6; ++i )
		{
			scene.GameTick( 1.0 / 30 );
			Assert.IsFalse( pc.IsOnGround, $"regrounded during a jump at {pc.WorldPosition}" );
			Assert.IsTrue( pc.WorldPosition.z > previous, $"jump was stopped at {pc.WorldPosition}" );
			previous = pc.WorldPosition.z;
		}
	}

	[TestMethod]
	public void RealMeshWallsStillBlockMovement()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		List<Vector3> vertices =
		[
			new( -256, -256, 0 ), new( 256, -256, 0 ), new( 256, 256, 0 ), new( -256, 256, 0 ),
			new( 64, -256, 0 ), new( 64, 256, 0 ), new( 64, 256, 128 ), new( 64, -256, 128 )
		];
		var mesh = scene.CreateObject();
		var collider = mesh.Components.Create<ModelCollider>();
		collider.Model = Model.Builder.AddCollisionMesh( vertices, [0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6] ).Create();
		collider.Static = true;
		var pc = CreatePlayer( scene, Vector3.Up * 20 );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = Vector3.Forward * 160;
		for ( int i = 0; i < 90; ++i )
			scene.GameTick( 1.0 / 60 );

		Assert.IsTrue( pc.WorldPosition.x < 56, $"ignored a real mesh wall: {pc.WorldPosition}" );
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( 0f, pc.WorldPosition.y, 0.05f );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 0.1f )]
	[DataRow( 0.5f )]
	public void MeshTJunctionsAndSmallGapsDoNotDeflectWalking( float gap )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var vertices = new List<Vector3>();
		var indices = new List<int>();
		void AddPatch( float x1, float x2, float y1, float y2 )
		{
			var index = vertices.Count;
			vertices.AddRange( [new( x1, y1, 0 ), new( x2, y1, 0 ), new( x2, y2, 0 ), new( x1, y2, 0 )] );
			indices.AddRange( [index, index + 1, index + 2, index, index + 2, index + 3] );
		}

		for ( int i = -4; i < 20; ++i )
		{
			var x = i * 32;
			AddPatch( x + gap, x + 32, -512, 0 );
			AddPatch( x + gap, x + 16, gap, 512 );
			AddPatch( x + 16 + gap, x + 32, gap, 512 );
		}

		var mesh = scene.CreateObject();
		mesh.WorldRotation = Rotation.FromYaw( 25 );
		var collider = mesh.Components.Create<ModelCollider>();
		collider.Model = Model.Builder.AddCollisionMesh( vertices, indices ).Create();
		collider.Static = true;
		var pc = CreatePlayer( scene, new Vector3( 0, 3, 20 ) );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		pc.WishVelocity = Vector3.Forward * 160;
		for ( int i = 0; i < 150 && pc.WorldPosition.x < 256; ++i )
		{
			scene.GameTick( 1.0 / 60 );
			Assert.IsTrue( pc.IsOnGround, $"lost mesh support at {pc.WorldPosition}: {DescribeGround( pc )}" );
			Assert.AreEqual( 3f, pc.WorldPosition.y, 0.05f, $"mesh seam deflected the character: {pc.WorldPosition}: {DescribeGround( pc )}" );
		}

		Assert.IsTrue( pc.WorldPosition.x >= 256, $"mesh seam stopped the character at {pc.WorldPosition}" );
	}

	[TestMethod]
	[DataRow( 0.01f, 1f )]
	[DataRow( 0.015f, 1f )]
	[DataRow( 0.02f, 1f )]
	[DataRow( 0.01f, 0f )]
	[DataRow( 0.015f, 0f )]
	[DataRow( 0.02f, 0f )]
	[DataRow( 0.02f, 0.5f )]
	public void PhysicalFreeFlightUsesScaledSplitGravity( float delta, float gravityScale )
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		scene.PhysicsWorld.Gravity = Vector3.Down * 800;
		var pc = CreatePlayer( scene, Vector3.Up * 1000 );
		pc.Body.GravityScale = gravityScale;
		pc.Body.Velocity = new Vector3( 320, 0, 200 );
		var expectedPosition = pc.WorldPosition;
		var expectedVelocity = pc.Body.Velocity;
		for ( int tick = 0; tick < 30; tick++ )
		{
			using var timeScope = Time.Scope( tick * delta, delta );
			// Check physical displacement as well as split-gravity velocity.
			expectedVelocity.z -= 800 * gravityScale * delta * 0.5f;
			expectedPosition += expectedVelocity * delta;
			expectedVelocity.z -= 800 * gravityScale * delta * 0.5f;
			((IScenePhysicsEvents)pc).PrePhysicsStep();
			scene.PhysicsWorld.Step( delta );
			pc.Body.UpdateTransformFromBody();
			((IScenePhysicsEvents)pc).PostPhysicsStep();
			Assert.IsTrue( (pc.WorldPosition - expectedPosition).Length < 0.01f, $"tick {tick}: {pc.WorldPosition} vs {expectedPosition}" );
			Assert.IsTrue( (pc.Body.Velocity - expectedVelocity).Length < 0.01f, $"tick {tick}: {pc.Body.Velocity} vs {expectedVelocity}" );
		}
	}

	[TestMethod]
	[DataRow( false, 0f )]
	[DataRow( true, 0f )]
	[DataRow( false, 0.1f )]
	[DataRow( true, 0.1f )]
	public void AirborneCollisionTransfersRealMomentum( bool besideStaticWall, float airFriction )
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		scene.PhysicsWorld.Gravity = 0;
		if ( besideStaticWall )
		{
			var wall = scene.CreateObject();
			wall.WorldPosition = new Vector3( 128, 24.03125f, 36 );
			var collider = wall.Components.Create<BoxCollider>();
			collider.Scale = new Vector3( 512, 16, 256 );
			collider.Static = true;
			collider.Friction = 0;
		}
		var pc = CreatePlayer( scene, Vector3.Zero );
		pc.BodyMass = 100;
		pc.AirFriction = airFriction;
		var prop = scene.CreateObject();
		prop.WorldPosition = new Vector3( 80, 0, 36 );
		var box = prop.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 32, besideStaticWall ? 32 : 100, 100 );
		box.Friction = 0;
		box.Elasticity = 0;
		var body = prop.Components.Create<Rigidbody>();
		body.MassOverride = 100;
		body.LinearDamping = 0;
		body.Gravity = false;
		pc.Body.Velocity = Vector3.Forward * 300;
		var expectedMomentum = 30000f;
		for ( int tick = 0; tick < 25; tick++ )
		{
			using var time = Time.Scope( tick * 0.02, 0.02f );
			expectedMomentum -= pc.BodyMass * pc.Body.Velocity.x * (1 - 1 / (1 + airFriction * 0.02f));
			scene.InternalFixedUpdate();
		}
		Assert.IsTrue( body.Velocity.x > 100, $"the actual prop should receive momentum: {body.Velocity}" );
		Assert.IsTrue( pc.Body.Velocity.x < 200, $"the player must receive the reaction impulse: {pc.Body.Velocity}" );
		Assert.AreEqual( expectedMomentum, pc.Body.Mass * pc.Body.Velocity.x + body.Mass * body.Velocity.x, 300f );
	}

	[TestMethod]
	public void DynamicPlatformCarriesPlayerWeight()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		scene.PhysicsWorld.Gravity = Vector3.Down * 800;
		var platform = scene.CreateObject();
		platform.WorldPosition = Vector3.Down * 8;
		var box = platform.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 256, 256, 16 );
		box.Elasticity = 0;
		var body = platform.Components.Create<Rigidbody>();
		body.MassOverride = 500;
		body.Gravity = false;
		body.LinearDamping = 0;
		var pc = CreatePlayer( scene, Vector3.Up );
		pc.BodyMass = 500;
		for ( int tick = 0; tick < 20; tick++ ) scene.GameTick( 0.02 );
		Assert.IsTrue( body.Velocity.z < -50, $"a gravity-disabled platform must still accelerate under player weight: {body.Velocity}" );
		Assert.IsTrue( MathF.Abs( pc.Body.Velocity.z - body.Velocity.z ) < 20, "player and support must move together through contact impulses" );
		Assert.IsTrue( pc.GroundIsDynamic );
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
		pc.HoldToJump = true;
		pc.UseInputControls = false;
		var walk = (MoveModeWalk)pc.Mode;
		walk.EnableAirStrafing = false;
		walk.EnableBunnyHopping = false;
		walk.BunnyHopRecoveryTime = 0.25f;
		walk.JumpMomentumRetention = 0.4f;

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
		Assert.IsTrue( pc2.HoldToJump );
		Assert.IsFalse( pc2.UseInputControls );

		Assert.IsTrue( pc2.Body.IsValid() );
		Assert.AreNotEqual( pc.Body, pc2.Body );
		Assert.IsTrue( pc2.ColliderObject.IsValid() );
		Assert.IsTrue( pc2.BodyCollider.IsValid() );
		Assert.IsTrue( pc2.FeetCollider.IsValid() );
		Assert.IsTrue( pc2.Mode is MoveModeWalk );
		var walk2 = (MoveModeWalk)pc2.Mode;
		Assert.IsFalse( walk2.EnableAirStrafing );
		Assert.IsFalse( walk2.EnableBunnyHopping );
		Assert.AreEqual( 0.25f, walk2.BunnyHopRecoveryTime );
		Assert.AreEqual( 0.4f, walk2.JumpMomentumRetention );

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
