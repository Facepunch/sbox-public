using System.Collections.Generic;

namespace SceneTests.GameObjects;

/// <summary>
/// Pins the collision and trigger event pipeline: ICollisionListener start, update
/// and stop, ITriggerListener and the OnObjectTrigger actions, the
/// CollisionEventsEnabled switch, exit events on collider disable, and
/// velocity-based impact damage.
/// </summary>
[TestClass]
public class CollisionEventTest
{
	/// <summary>
	/// Counts ICollisionListener callbacks and remembers the last start collision.
	/// </summary>
	public sealed class CollisionProbe : Component, Component.ICollisionListener
	{
		public int Starts { get; private set; }
		public int Updates { get; private set; }
		public int Stops { get; private set; }
		public Collision LastStart { get; private set; }

		public void OnCollisionStart( Collision collision )
		{
			Starts++;
			LastStart = collision;
		}

		public void OnCollisionUpdate( Collision collision ) => Updates++;

		public void OnCollisionStop( CollisionStop collision ) => Stops++;
	}

	/// <summary>
	/// Counts ITriggerListener callbacks for both the collider and object overloads.
	/// </summary>
	public sealed class TriggerProbe : Component, Component.ITriggerListener
	{
		public List<Collider> ColliderEnters { get; } = new();
		public List<Collider> ColliderExits { get; } = new();
		public List<GameObject> ObjectEnters { get; } = new();
		public List<GameObject> ObjectExits { get; } = new();

		public void OnTriggerEnter( Collider self, Collider other ) => ColliderEnters.Add( other );
		public void OnTriggerExit( Collider self, Collider other ) => ColliderExits.Add( other );
		public void OnTriggerEnter( Collider self, GameObject other ) => ObjectEnters.Add( other );
		public void OnTriggerExit( Collider self, GameObject other ) => ObjectExits.Add( other );
	}

	/// <summary>
	/// Accumulates damage dealt to the object through IDamageable.
	/// </summary>
	public sealed class DamageProbe : Component, Component.IDamageable
	{
		public float TotalDamage { get; private set; }
		public DamageInfo Last { get; private set; }

		public void OnDamage( in DamageInfo damage )
		{
			TotalDamage += damage.Damage;
			Last = damage;
		}
	}

	static GameObject CreateFloor( Scene scene )
	{
		var floor = scene.CreateObject();
		floor.Name = "Floor";
		floor.WorldPosition = new Vector3( 0, 0, -50 );

		var box = floor.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 1000, 1000, 100 );
		box.Static = true;

		return floor;
	}

	static (GameObject go, Rigidbody rb, BoxCollider collider) CreateBody( Scene scene, Vector3 position )
	{
		var go = scene.CreateObject();
		go.Name = "Body";
		go.WorldPosition = position;

		var rb = go.Components.Create<Rigidbody>();
		var collider = go.Components.Create<BoxCollider>();
		collider.Scale = new Vector3( 10 );

		return (go, rb, collider);
	}

	/// <summary>
	/// A body falling onto a floor raises OnCollisionStart, OnCollisionUpdate
	/// while resting (when update events are enabled), and OnCollisionStop when
	/// the contact ends.
	/// </summary>
	[TestMethod]
	public void CollisionStartUpdateStop()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var floor = CreateFloor( scene );
		var (go, rb, collider) = CreateBody( scene, new Vector3( 0, 0, 20 ) );
		rb.CollisionUpdateEventsEnabled = true;
		var probe = go.Components.Create<CollisionProbe>();

		for ( int i = 0; i < 60; i++ ) scene.GameTick();

		Assert.IsTrue( probe.Starts >= 1, $"start should have fired: {probe.Starts}" );
		Assert.IsTrue( probe.Updates >= 1, $"update should have fired: {probe.Updates}" );
		Assert.AreEqual( floor, probe.LastStart.Other.GameObject );
		Assert.AreEqual( collider, probe.LastStart.Self.Collider );

		// Teleport away - the contact ends. The resting body may have fallen
		// asleep, and a sleeping body doesn't report the separation, so wake it.
		go.WorldPosition = new Vector3( 0, 0, 500 );
		rb.Velocity = Vector3.Zero;
		rb.Sleeping = false;
		for ( int i = 0; i < 10; i++ ) scene.GameTick();

		Assert.IsTrue( probe.Stops >= 1, $"stop should have fired: {probe.Stops}" );
	}

	/// <summary>
	/// Touch events are pair-level: as long as the other collider has them enabled
	/// the listener still hears the contact, so silencing a collision needs
	/// CollisionEventsEnabled off on both bodies before their shapes are built.
	/// </summary>
	[TestMethod]
	public void CollisionEventsCanBeDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		// A keyframed floor with touch events off - a plain static collider would
		// keep the contact pair alive with its own enabled shapes
		var floor = scene.CreateObject();
		floor.WorldPosition = new Vector3( 0, 0, -50 );
		var floorRb = floor.Components.Create<Rigidbody>();
		floorRb.MotionEnabled = false;
		floorRb.CollisionEventsEnabled = false;
		floor.Components.Create<BoxCollider>().Scale = new Vector3( 1000, 1000, 100 );

		// The flag must be set before the collider creates its shapes - it's
		// baked into the shapes when they're configured
		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 0, 0, 20 );
		var rb = go.Components.Create<Rigidbody>();
		rb.CollisionEventsEnabled = false;
		go.Components.Create<BoxCollider>().Scale = new Vector3( 10 );
		var probe = go.Components.Create<CollisionProbe>();

		for ( int i = 0; i < 60; i++ ) scene.GameTick();

		Assert.IsTrue( go.WorldPosition.z > 0f, "the body still collides physically" );
		Assert.AreEqual( 0, probe.Starts, "no collision events should fire" );
	}

	/// <summary>
	/// A trigger reports a body entering and leaving through ITriggerListener
	/// components and the OnObjectTriggerEnter/Exit actions.
	/// </summary>
	[TestMethod]
	public void TriggerListenersAndObjectEvents()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var triggerGo = scene.CreateObject();
		triggerGo.Name = "Trigger";
		var trigger = triggerGo.Components.Create<BoxCollider>();
		trigger.Scale = new Vector3( 100 );
		trigger.IsTrigger = true;
		var probe = triggerGo.Components.Create<TriggerProbe>();

		var objectEnters = 0;
		var objectExits = 0;
		trigger.OnObjectTriggerEnter = _ => objectEnters++;
		trigger.OnObjectTriggerExit = _ => objectExits++;

		var (bodyGo, rb, bodyCollider) = CreateBody( scene, Vector3.Zero );
		rb.Gravity = false;

		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		Assert.AreEqual( 1, objectEnters, "the object enter action should have fired" );
		CollectionAssert.Contains( probe.ColliderEnters, bodyCollider );
		CollectionAssert.Contains( probe.ObjectEnters, bodyGo );

		bodyGo.WorldPosition = new Vector3( 1000, 0, 0 );
		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		Assert.AreEqual( 1, objectExits, "the object exit action should have fired" );
		CollectionAssert.Contains( probe.ColliderExits, bodyCollider );
		CollectionAssert.Contains( probe.ObjectExits, bodyGo );
	}

	/// <summary>
	/// Disabling a collider that's inside a trigger fires the exit events
	/// immediately, without waiting for physics to notice.
	/// </summary>
	[TestMethod]
	public void TriggerExitFiresOnColliderDisable()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var triggerGo = scene.CreateObject();
		var trigger = triggerGo.Components.Create<BoxCollider>();
		trigger.Scale = new Vector3( 100 );
		trigger.IsTrigger = true;

		var enters = 0;
		var exits = 0;
		trigger.OnTriggerEnter = _ => enters++;
		trigger.OnTriggerExit = _ => exits++;

		var (_, rb, bodyCollider) = CreateBody( scene, Vector3.Zero );
		rb.Gravity = false;

		for ( int i = 0; i < 5; i++ ) scene.GameTick();
		Assert.AreEqual( 1, enters );

		bodyCollider.Enabled = false;

		Assert.AreEqual( 1, exits, "disabling the collider should fire the exit" );
		Assert.IsFalse( trigger.Touching.Any() );
	}

	/// <summary>
	/// A high-speed impact deals damage through IDamageable to both sides, with the
	/// impacted object taking more than the impacting body, tagged "impact".
	/// </summary>
	[TestMethod]
	public void ImpactDamageOnHighSpeedImpact()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var floor = CreateFloor( scene );
		var floorProbe = floor.Components.Create<DamageProbe>();

		var (go, rb, _) = CreateBody( scene, new Vector3( 0, 0, 30 ) );
		var bodyProbe = go.Components.Create<DamageProbe>();
		rb.ImpactDamage = 50f;
		rb.MinImpactDamageSpeed = 0f; // falls back to the 500 default
		rb.Velocity = new Vector3( 0, 0, -1500 );

		for ( int i = 0; i < 30; i++ ) scene.GameTick();

		Assert.IsTrue( bodyProbe.TotalDamage > 0f, "the impacting body should damage itself" );
		Assert.IsTrue( floorProbe.TotalDamage > bodyProbe.TotalDamage,
			$"the impacted object takes 1.2x damage: {floorProbe.TotalDamage} vs {bodyProbe.TotalDamage}" );
		Assert.IsTrue( bodyProbe.Last.Tags.Has( "impact" ) );
	}

	/// <summary>
	/// With no explicit ImpactDamage the damage is derived from the body's mass.
	/// </summary>
	[TestMethod]
	public void ImpactDamageDefaultsFromMass()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var floor = CreateFloor( scene );
		var floorProbe = floor.Components.Create<DamageProbe>();

		var (_, rb, _) = CreateBody( scene, new Vector3( 0, 0, 30 ) );
		rb.Velocity = new Vector3( 0, 0, -1500 );

		for ( int i = 0; i < 30; i++ ) scene.GameTick();

		Assert.IsTrue( floorProbe.TotalDamage > 0f, "mass-derived impact damage should apply" );
	}

	/// <summary>
	/// Impacting something that isn't damageable still damages the body itself.
	/// </summary>
	[TestMethod]
	public void ImpactDamageIgnoresNonDamageables()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );

		var (go, rb, _) = CreateBody( scene, new Vector3( 0, 0, 30 ) );
		var bodyProbe = go.Components.Create<DamageProbe>();
		rb.Velocity = new Vector3( 0, 0, -1500 );

		for ( int i = 0; i < 30; i++ ) scene.GameTick();

		Assert.IsTrue( bodyProbe.TotalDamage > 0f, "the body should still damage itself" );
	}

	/// <summary>
	/// EnableImpactDamage false suppresses impact damage entirely.
	/// </summary>
	[TestMethod]
	public void ImpactDamageCanBeDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var floor = CreateFloor( scene );
		var floorProbe = floor.Components.Create<DamageProbe>();

		var (go, rb, _) = CreateBody( scene, new Vector3( 0, 0, 30 ) );
		var bodyProbe = go.Components.Create<DamageProbe>();
		rb.EnableImpactDamage = false;
		rb.Velocity = new Vector3( 0, 0, -1500 );

		for ( int i = 0; i < 30; i++ ) scene.GameTick();

		Assert.AreEqual( 0f, floorProbe.TotalDamage );
		Assert.AreEqual( 0f, bodyProbe.TotalDamage );
	}
}
