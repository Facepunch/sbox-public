using System;

namespace SceneTests.GameObjects;

/// <summary>
/// Pins the Rigidbody contract: body lifecycle through enable/disable, simulation
/// (gravity, velocity, impulses), motion and axis locking, mass overrides, and the
/// hand-off of collider shapes when the rigidbody goes away.
/// </summary>
[TestClass]
public class RigidbodyTest
{
	static (GameObject go, Rigidbody rb) CreateBody( Scene scene, Vector3 position, bool gravity = false )
	{
		var go = scene.CreateObject();
		go.WorldPosition = position;

		var rb = go.Components.Create<Rigidbody>();
		rb.Gravity = gravity;

		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 10 );

		return (go, rb);
	}

	/// <summary>
	/// The physics body exists while enabled, goes away when the component is
	/// disabled, and comes back with its shapes when re-enabled.
	/// </summary>
	[TestMethod]
	public void BodyLifecycle()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go, rb) = CreateBody( scene, Vector3.Zero );

		Assert.IsTrue( rb.PhysicsBody.IsValid() );
		Assert.AreEqual( 1, rb.PhysicsBody.Shapes.Count() );

		rb.Enabled = false;
		Assert.IsNull( rb.PhysicsBody );

		rb.Enabled = true;
		Assert.IsTrue( rb.PhysicsBody.IsValid() );
		Assert.AreEqual( 1, rb.PhysicsBody.Shapes.Count() );
	}

	/// <summary>
	/// When the rigidbody is disabled the collider re-homes to its own keyframed
	/// body - the geometry must still be traceable.
	/// </summary>
	[TestMethod]
	public void ColliderSurvivesRigidbodyDisable()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );
		var rb = go.Components.Create<Rigidbody>();
		rb.Gravity = false;
		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 50 );

		Assert.IsTrue( scene.Trace.Ray( Vector3.Zero, new Vector3( 400, 0, 0 ) ).Run().Hit );

		rb.Enabled = false;

		Assert.IsNull( box.Rigidbody );
		Assert.IsTrue( box.PhysicsBody.IsValid(), "the collider should own a keyframed body again" );

		var hit = scene.Trace.Ray( Vector3.Zero, new Vector3( 400, 0, 0 ) ).Run();
		Assert.IsTrue( hit.Hit, "the collider should still be traceable" );
		Assert.AreEqual( 75f, hit.EndPosition.x, 0.5f );
	}

	/// <summary>
	/// Gravity pulls a dynamic body down over ticks; with Gravity off it stays put.
	/// </summary>
	[TestMethod]
	public void GravityFall()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (falling, fallingRb) = CreateBody( scene, new Vector3( 0, 0, 1000 ), gravity: true );
		var (floating, _) = CreateBody( scene, new Vector3( 100, 0, 1000 ), gravity: false );

		for ( int i = 0; i < 10; i++ ) scene.GameTick();

		Assert.IsTrue( falling.WorldPosition.z < 1000f, $"gravity body should have fallen: {falling.WorldPosition}" );
		Assert.AreEqual( 1000f, floating.WorldPosition.z, 0.5f );

		// The body reports a downward velocity while falling
		Assert.IsTrue( fallingRb.Velocity.z < 0f );
	}

	/// <summary>
	/// A velocity moves the body between ticks and the GameObject transform follows
	/// the physics body.
	/// </summary>
	[TestMethod]
	public void VelocityMovesBodyAndTransform()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go, rb) = CreateBody( scene, Vector3.Zero );
		rb.Velocity = new Vector3( 100, 0, 0 );

		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		Assert.IsTrue( go.WorldPosition.x > 10f, $"body should have moved: {go.WorldPosition}" );
		Assert.AreEqual( 0f, go.WorldPosition.y, 0.5f );
	}

	/// <summary>
	/// MotionEnabled false disables dynamics - gravity no longer pulls the body -
	/// but an explicitly set velocity still carries it kinematically.
	/// </summary>
	[TestMethod]
	public void MotionDisabledIgnoresGravityButKeepsKinematicVelocity()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go, rb) = CreateBody( scene, new Vector3( 0, 0, 500 ), gravity: true );
		rb.MotionEnabled = false;
		rb.Velocity = new Vector3( 100, 0, 0 );

		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		Assert.AreEqual( 500f, go.WorldPosition.z, 0.5f, $"gravity must not apply: {go.WorldPosition}" );
		Assert.IsTrue( go.WorldPosition.x > 10f, $"explicit velocity still moves the body kinematically: {go.WorldPosition}" );
	}

	/// <summary>
	/// An impulse changes the velocity in its direction.
	/// </summary>
	[TestMethod]
	public void ImpulseChangesVelocity()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go, rb) = CreateBody( scene, Vector3.Zero );

		Assert.IsTrue( rb.Velocity.AlmostEqual( Vector3.Zero ) );

		rb.ApplyImpulse( new Vector3( 0, 5000, 0 ) );

		Assert.IsTrue( rb.Velocity.y > 0f, $"impulse should have set velocity: {rb.Velocity}" );
	}

	/// <summary>
	/// Locked axes don't move under gravity or velocity, unlocked axes do.
	/// </summary>
	[TestMethod]
	public void AxisLocking()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go, rb) = CreateBody( scene, new Vector3( 0, 0, 500 ), gravity: true );
		rb.Locking = new PhysicsLock { Z = true };
		rb.Velocity = new Vector3( 100, 0, 0 );

		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		Assert.AreEqual( 500f, go.WorldPosition.z, 0.5f, $"z is locked: {go.WorldPosition}" );
		Assert.IsTrue( go.WorldPosition.x > 10f, $"x is free: {go.WorldPosition}" );
	}

	/// <summary>
	/// MassOverride takes effect on the physics body.
	/// </summary>
	[TestMethod]
	public void MassOverride()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go, rb) = CreateBody( scene, Vector3.Zero );

		var autoMass = rb.Mass;
		Assert.IsTrue( autoMass > 0f, "auto mass should be positive" );

		rb.MassOverride = 123f;
		Assert.AreEqual( 123f, rb.Mass, 0.1f );

		rb.MassOverride = 0f;
		Assert.AreEqual( autoMass, rb.Mass, autoMass * 0.01f );
	}

	/// <summary>
	/// A sleeping body wakes when an impulse is applied.
	/// </summary>
	[TestMethod]
	public void SleepAndWake()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go, rb) = CreateBody( scene, Vector3.Zero );

		rb.Sleeping = true;
		Assert.IsTrue( rb.Sleeping );

		rb.ApplyImpulse( new Vector3( 0, 0, 5000 ) );
		scene.GameTick();

		Assert.IsFalse( rb.Sleeping, "an impulse should wake the body" );
	}

	/// <summary>
	/// Two stacked dynamic boxes collide instead of falling through each other:
	/// the upper body comes to rest above the lower one.
	/// </summary>
	[TestMethod]
	public void BodiesCollide()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		// A static floor
		var floor = scene.CreateObject();
		floor.WorldPosition = new Vector3( 0, 0, -50 );
		var floorBox = floor.Components.Create<BoxCollider>();
		floorBox.Scale = new Vector3( 1000, 1000, 100 );
		floorBox.Static = true;

		var (faller, _) = CreateBody( scene, new Vector3( 0, 0, 50 ), gravity: true );

		for ( int i = 0; i < 60; i++ ) scene.GameTick();

		// Resting on the floor: floor top is at z=0, box half-height 5
		Assert.IsTrue( faller.WorldPosition.z > 0f, $"body fell through the floor: {faller.WorldPosition}" );
		Assert.IsTrue( faller.WorldPosition.z < 20f, $"body should have come to rest near the floor: {faller.WorldPosition}" );
	}

	/// <summary>
	/// GravityScale multiplies the gravity applied to the body.
	/// </summary>
	[TestMethod]
	public void GravityScaleMultipliesGravity()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (normal, _) = CreateBody( scene, new Vector3( 0, 0, 1000 ), gravity: true );
		var (scaled, scaledRb) = CreateBody( scene, new Vector3( 100, 0, 1000 ), gravity: true );
		scaledRb.GravityScale = 3f;

		for ( int i = 0; i < 10; i++ ) scene.GameTick();

		var normalDrop = 1000f - normal.WorldPosition.z;
		var scaledDrop = 1000f - scaled.WorldPosition.z;

		Assert.IsTrue( normalDrop > 0f );
		Assert.IsTrue( scaledDrop > normalDrop * 2f, $"scaled gravity should fall much faster: {scaledDrop} vs {normalDrop}" );
	}

	/// <summary>
	/// Linear and angular damping bleed off velocity over time compared to an
	/// undamped twin.
	/// </summary>
	[TestMethod]
	public void DampingSlowsTheBody()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (_, freeRb) = CreateBody( scene, Vector3.Zero );
		var (_, dampedRb) = CreateBody( scene, new Vector3( 0, 100, 0 ) );
		dampedRb.LinearDamping = 5f;
		dampedRb.AngularDamping = 5f;

		freeRb.Velocity = new Vector3( 100, 0, 0 );
		dampedRb.Velocity = new Vector3( 100, 0, 0 );
		freeRb.AngularVelocity = new Vector3( 0, 0, 10 );
		dampedRb.AngularVelocity = new Vector3( 0, 0, 10 );

		for ( int i = 0; i < 10; i++ ) scene.GameTick();

		Assert.IsTrue( dampedRb.Velocity.Length < freeRb.Velocity.Length * 0.5f,
			$"linear damping should slow the body: {dampedRb.Velocity.Length} vs {freeRb.Velocity.Length}" );
		Assert.IsTrue( dampedRb.AngularVelocity.Length < freeRb.AngularVelocity.Length * 0.5f,
			$"angular damping should slow the spin: {dampedRb.AngularVelocity.Length} vs {freeRb.AngularVelocity.Length}" );
	}

	/// <summary>
	/// An angular velocity spins the body and the GameObject rotation follows.
	/// </summary>
	[TestMethod]
	public void AngularVelocitySpinsBody()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go, rb) = CreateBody( scene, Vector3.Zero );
		rb.AngularVelocity = new Vector3( 0, 0, 5 );
		Assert.IsTrue( rb.AngularVelocity.AlmostEqual( new Vector3( 0, 0, 5 ), 0.1f ) );

		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		var yaw = go.WorldRotation.Angles().yaw;
		Assert.IsTrue( MathF.Abs( yaw ) > 1f, $"the body should have yawed: {go.WorldRotation.Angles()}" );
	}

	/// <summary>
	/// ApplyForce and ApplyTorque accumulate and take effect on the next physics
	/// step; ClearForces cancels pending linear forces before they're applied.
	/// </summary>
	[TestMethod]
	public void ForcesApplyOnTickAndCanBeCleared()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (_, rb) = CreateBody( scene, Vector3.Zero );

		rb.ApplyForce( new Vector3( 1_000_000, 0, 0 ) );
		rb.ApplyTorque( new Vector3( 0, 0, 1_000_000 ) );
		scene.GameTick();

		Assert.IsTrue( rb.Velocity.x > 1f, $"the force should have accelerated the body: {rb.Velocity}" );
		Assert.IsTrue( rb.AngularVelocity.z > 0.1f, $"the torque should have spun the body: {rb.AngularVelocity}" );

		// Stop it and try again, clearing before the step runs
		rb.Velocity = Vector3.Zero;
		rb.AngularVelocity = Vector3.Zero;

		rb.ApplyForce( new Vector3( 1_000_000, 0, 0 ) );
		rb.ClearForces();
		scene.GameTick();

		Assert.AreEqual( 0f, rb.Velocity.x, 0.5f, "cleared forces must not be applied" );
	}

	/// <summary>
	/// Forces and impulses applied off-center induce spin as well as motion.
	/// </summary>
	[TestMethod]
	public void OffCenterForcesInduceSpin()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (_, rb) = CreateBody( scene, Vector3.Zero );

		rb.ApplyImpulseAt( new Vector3( 0, 5, 0 ), new Vector3( 5000, 0, 0 ) );
		Assert.IsTrue( rb.Velocity.x > 0f, $"{rb.Velocity}" );
		Assert.IsTrue( rb.AngularVelocity.Length > 0.01f, $"an off-center impulse should spin: {rb.AngularVelocity}" );

		rb.Velocity = Vector3.Zero;
		rb.AngularVelocity = Vector3.Zero;

		rb.ApplyForceAt( new Vector3( 0, 5, 0 ), new Vector3( 1_000_000, 0, 0 ) );
		scene.GameTick();

		Assert.IsTrue( rb.Velocity.x > 0f, $"{rb.Velocity}" );
		Assert.IsTrue( rb.AngularVelocity.Length > 0.01f, $"an off-center force should spin: {rb.AngularVelocity}" );
	}

	/// <summary>
	/// OverrideMassCenter moves the body's local center of mass to the override
	/// and back to the calculated one when turned off.
	/// </summary>
	[TestMethod]
	public void MassCenterOverrideMovesMassCenter()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (_, rb) = CreateBody( scene, Vector3.Zero );
		Assert.IsTrue( rb.MassCenter.AlmostEqual( Vector3.Zero, 0.5f ), $"{rb.MassCenter}" );

		rb.OverrideMassCenter = true;
		rb.MassCenterOverride = new Vector3( 0, 0, 10 );
		Assert.IsTrue( rb.MassCenter.AlmostEqual( new Vector3( 0, 0, 10 ), 0.5f ), $"{rb.MassCenter}" );

		rb.OverrideMassCenter = false;
		Assert.IsTrue( rb.MassCenter.AlmostEqual( Vector3.Zero, 0.5f ), $"{rb.MassCenter}" );
	}

	/// <summary>
	/// The inertia tensor can be overridden and reset back to the auto-calculated
	/// values, and its rotation can be overridden independently.
	/// </summary>
	[TestMethod]
	public void InertiaTensorOverrideAndReset()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (_, rb) = CreateBody( scene, Vector3.Zero );

		var auto = rb.InertiaTensor;
		Assert.IsTrue( auto.x > 0f && auto.y > 0f && auto.z > 0f, $"{auto}" );

		rb.InertiaTensor = auto * 5f;
		Assert.IsTrue( rb.InertiaTensor.AlmostEqual( auto * 5f, auto.x * 0.05f ), $"{rb.InertiaTensor}" );

		// A cube's tensor is symmetric, so the rotation degenerates back to
		// identity - only pin that the override round-trip doesn't disturb the tensor
		rb.InertiaTensorRotation = Rotation.FromYaw( 45 );
		_ = rb.InertiaTensorRotation;
		Assert.IsTrue( rb.InertiaTensor.AlmostEqual( auto * 5f, auto.x * 0.05f ), $"{rb.InertiaTensor}" );

		rb.ResetInertiaTensor();
		Assert.IsTrue( rb.InertiaTensor.AlmostEqual( auto, auto.x * 0.05f ), $"{rb.InertiaTensor}" );
	}

	/// <summary>
	/// StartAsleep makes the body begin sleeping when the component is enabled.
	/// </summary>
	[TestMethod]
	public void StartAsleepStartsSleeping()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject( false );
		var rb = go.Components.Create<Rigidbody>( false );
		rb.StartAsleep = true;
		var box = go.Components.Create<BoxCollider>( false );
		box.Scale = new Vector3( 10 );

		// Before the first enable there's no collision event system either
		Assert.IsFalse( rb.Touching.Any() );

		rb.Enabled = true;
		box.Enabled = true;
		go.Enabled = true;

		Assert.IsTrue( rb.Sleeping, "the body should start asleep" );

		scene.GameTick();
		Assert.IsTrue( rb.Sleeping, "nothing has woken the body" );
	}

	/// <summary>
	/// Tuning properties propagate down to the live physics body.
	/// </summary>
	[TestMethod]
	public void PropertiesPropagateToBody()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (_, rb) = CreateBody( scene, Vector3.Zero );

		rb.SleepThreshold = 50f;
		Assert.AreEqual( 50f, rb.PhysicsBody.SleepThreshold, 0.1f );

		// EnhancedCcd is write-only on the body - setting it must stick on the component
		rb.EnhancedCcd = true;
		Assert.IsTrue( rb.EnhancedCcd );

		rb.CollisionEventsEnabled = false;
		Assert.IsFalse( rb.PhysicsBody.EnableTouch );
		rb.CollisionEventsEnabled = true;
		Assert.IsTrue( rb.PhysicsBody.EnableTouch );

		rb.CollisionUpdateEventsEnabled = true;
		Assert.IsTrue( rb.PhysicsBody.EnableTouchPersists );

		rb.RigidbodyFlags = RigidbodyFlags.DisableCollisionSounds;
		Assert.AreEqual( RigidbodyFlags.DisableCollisionSounds, rb.RigidbodyFlags );
	}

	/// <summary>
	/// Every accessor copes with the physics body being gone: getters return inert
	/// defaults and setters and calls don't throw.
	/// </summary>
	[TestMethod]
	public void AccessorsAreInertWithoutBody()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (_, rb) = CreateBody( scene, Vector3.Zero );
		rb.Enabled = false;

		Assert.IsNull( rb.PhysicsBody );
		Assert.AreEqual( 0f, rb.Mass );
		Assert.IsTrue( rb.Velocity.AlmostEqual( Vector3.Zero ) );
		Assert.IsTrue( rb.MassCenter.AlmostEqual( Vector3.Zero ) );
		Assert.IsTrue( rb.InertiaTensor.AlmostEqual( Vector3.Zero ) );
		Assert.IsFalse( rb.Sleeping );
		Assert.AreEqual( 0, rb.Joints.Count );
		Assert.IsFalse( rb.Touching.Any() );
		_ = rb.InertiaTensorRotation;

		rb.Sleeping = true;
		rb.InertiaTensor = Vector3.One;
		rb.InertiaTensorRotation = Rotation.FromYaw( 10 );
		rb.ResetInertiaTensor();
		rb.ApplyForce( Vector3.One );
		rb.ApplyForceAt( Vector3.Zero, Vector3.One );
		rb.ApplyTorque( Vector3.One );
		rb.ApplyImpulse( Vector3.One );
		rb.ApplyImpulseAt( Vector3.Zero, Vector3.One );
		rb.ClearForces();
		rb.SmoothMove( Vector3.Zero, 1f, 0.02f );
		rb.SmoothMove( global::Transform.Zero, 1f, 0.02f );
		rb.SmoothRotate( Rotation.Identity, 1f, 0.02f );
		rb.ApplyBuoyancy( new Plane( Vector3.Zero, Vector3.Up ), 0.02f );

		Assert.IsTrue( rb.FindClosestPoint( new Vector3( 1, 2, 3 ) ).AlmostEqual( new Vector3( 1, 2, 3 ) ) );
		Assert.IsTrue( rb.GetVelocityAtPoint( Vector3.One ).AlmostEqual( Vector3.Zero ) );
		Assert.AreEqual( 0.1f, rb.GetWorldBounds().Size.x, 0.01f );
	}

	/// <summary>
	/// Point queries answer from the body: closest point lands on the surface, the
	/// velocity at an offset point includes the spin contribution, and the world
	/// bounds wrap the collider.
	/// </summary>
	[TestMethod]
	public void PointQueries()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (_, rb) = CreateBody( scene, Vector3.Zero );

		Assert.AreEqual( 5f, rb.FindClosestPoint( new Vector3( 100, 0, 0 ) ).x, 0.5f );

		rb.AngularVelocity = new Vector3( 0, 0, 10 );
		var v = rb.GetVelocityAtPoint( new Vector3( 0, 100, 0 ) );
		Assert.IsTrue( v.Length > 1f, $"spin should give the point a velocity: {v}" );

		Assert.AreEqual( 10f, rb.GetWorldBounds().Size.x, 2f, $"{rb.GetWorldBounds()}" );
	}

	/// <summary>
	/// SmoothMove and SmoothRotate drive the body toward a target pose over time.
	/// </summary>
	[TestMethod]
	public void SmoothMoveAndRotate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go, rb) = CreateBody( scene, Vector3.Zero );

		for ( int i = 0; i < 20; i++ )
		{
			rb.SmoothMove( new Vector3( 100, 0, 0 ), 0.3f, 0.02f );
			scene.GameTick();
		}

		Assert.IsTrue( go.WorldPosition.x > 50f, $"SmoothMove should approach the target: {go.WorldPosition}" );

		var (go2, rb2) = CreateBody( scene, new Vector3( 0, 200, 0 ) );
		var target = new Transform( new Vector3( 0, 200, 0 ), Rotation.FromYaw( 90 ) );

		for ( int i = 0; i < 20; i++ )
		{
			rb2.SmoothMove( target, 0.3f, 0.02f );
			scene.GameTick();
		}

		Assert.IsTrue( MathF.Abs( go2.WorldRotation.Angles().yaw ) > 30f,
			$"the transform overload should also rotate: {go2.WorldRotation.Angles()}" );

		var (go3, rb3) = CreateBody( scene, new Vector3( 0, 400, 0 ) );

		for ( int i = 0; i < 20; i++ )
		{
			rb3.SmoothRotate( Rotation.FromYaw( 90 ), 0.3f, 0.02f );
			scene.GameTick();
		}

		Assert.IsTrue( MathF.Abs( go3.WorldRotation.Angles().yaw ) > 30f,
			$"SmoothRotate should approach the target: {go3.WorldRotation.Angles()}" );
	}

	/// <summary>
	/// Buoyancy applies an immediate upward impulse to a submerged body. The
	/// impulse scales with the body's gravity scale, so it only works on bodies
	/// that have gravity enabled.
	/// </summary>
	[TestMethod]
	public void BuoyancyPushesSubmergedBodyUp()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (_, rb) = CreateBody( scene, Vector3.Zero, gravity: true );
		var water = new Plane( new Vector3( 0, 0, 500 ), Vector3.Up );

		// No ticks run here, so gravity never integrates - any velocity is the impulse
		rb.ApplyBuoyancy( water, 0.02f );
		rb.ApplyBuoyancy( water, 10_000f, 0.1f, 0.5f, Vector3.Zero, 0.02f );

		Assert.IsTrue( rb.Velocity.z > 0f, $"buoyancy should push the body up: {rb.Velocity}" );

		// A body with gravity disabled has gravity scale zero - buoyancy nets to nothing
		var (_, floating) = CreateBody( scene, new Vector3( 0, 100, 0 ) );
		floating.ApplyBuoyancy( water, 10_000f, 0.1f, 0.5f, Vector3.Zero, 0.02f );
		Assert.IsTrue( floating.Velocity.AlmostEqual( Vector3.Zero ), $"{floating.Velocity}" );
	}

	/// <summary>
	/// Touching lists the triggers the body overlaps.
	/// </summary>
	[TestMethod]
	public void TouchingListsTriggers()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var triggerGo = scene.CreateObject();
		var trigger = triggerGo.Components.Create<BoxCollider>();
		trigger.Scale = new Vector3( 100 );
		trigger.IsTrigger = true;

		var (go, rb) = CreateBody( scene, Vector3.Zero );

		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		Assert.IsTrue( rb.Touching.Contains( trigger ), "the rigidbody should report the trigger it's inside" );

		// The collider answers the same through the rigidbody
		var collider = go.Components.Get<BoxCollider>();
		Assert.IsTrue( collider.Touching.Contains( trigger ) );
	}

	/// <summary>
	/// TargetTransform (used by the editor to drag bodies around) drives the body
	/// toward the target with velocity each physics step.
	/// </summary>
	[TestMethod]
	public void TargetTransformDrivesBody()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go, rb) = CreateBody( scene, Vector3.Zero );
		rb.TargetTransform = new Transform( new Vector3( 100, 0, 0 ) );

		for ( int i = 0; i < 20; i++ ) scene.GameTick();

		Assert.IsTrue( go.WorldPosition.x > 50f, $"the body should chase the target: {go.WorldPosition}" );
	}
}
