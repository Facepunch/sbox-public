using System;
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
	/// OnCollisionUpdate only fires while the collision is awake: updates stop when
	/// the body falls asleep, resume on wake, and the sleep transition raises
	/// neither a stop nor a new start.
	/// </summary>
	[TestMethod]
	public void CollisionUpdatesStopWhileSleeping()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );
		var (go, rb, _) = CreateBody( scene, new Vector3( 0, 0, 20 ) );
		rb.CollisionUpdateEventsEnabled = true;
		var probe = go.Components.Create<CollisionProbe>();

		for ( int i = 0; i < 300 && !rb.Sleeping; i++ ) scene.GameTick();
		Assert.IsTrue( rb.Sleeping, "the body should have fallen asleep" );

		var startsAtSleep = probe.Starts;
		var updatesAtSleep = probe.Updates;
		Assert.IsTrue( updatesAtSleep >= 1, "updates should have fired while settling" );

		for ( int i = 0; i < 30; i++ ) scene.GameTick();

		Assert.AreEqual( updatesAtSleep, probe.Updates, "a sleeping contact should not send updates" );
		Assert.AreEqual( 0, probe.Stops, "falling asleep should not end the touch" );

		rb.Sleeping = false;
		for ( int i = 0; i < 10; i++ ) scene.GameTick();

		Assert.IsTrue( probe.Updates > updatesAtSleep, "updates should resume after waking" );
		Assert.AreEqual( startsAtSleep, probe.Starts, "waking should not re-fire start" );
		Assert.AreEqual( 0, probe.Stops, "the contact should still be alive" );
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
	/// Every trigger touching a collider sees the exit when it's disabled, and a trigger that was
	/// disabled first doesn't hear about it at all.
	/// </summary>
	[TestMethod]
	public void TriggerExitFiresForEveryTriggerOnColliderDisable()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var exits = new List<Collider>[3];
		var triggers = new BoxCollider[3];
		for ( int i = 0; i < triggers.Length; i++ )
		{
			var list = exits[i] = new List<Collider>();
			triggers[i] = scene.CreateObject().Components.Create<BoxCollider>();
			triggers[i].Scale = new Vector3( 100 );
			triggers[i].IsTrigger = true;
			triggers[i].OnTriggerExit = c => list.Add( c );
		}

		var (_, rb, bodyCollider) = CreateBody( scene, Vector3.Zero );
		rb.Gravity = false;

		for ( int i = 0; i < 5; i++ ) scene.GameTick();
		Assert.IsTrue( triggers.All( t => t.Touching.Contains( bodyCollider ) ), "every trigger should be touching" );

		triggers[2].Enabled = false;
		bodyCollider.Enabled = false;

		// The overlapping triggers also see each other, only count the body
		Assert.AreEqual( 1, exits[0].Count( c => c == bodyCollider ) );
		Assert.AreEqual( 1, exits[1].Count( c => c == bodyCollider ) );
		Assert.AreEqual( 0, exits[2].Count, "a disabled trigger shouldn't see exits" );
	}

	/// <summary>
	/// A GameObject with two colliders in a trigger enters once, and only exits once both colliders left.
	/// </summary>
	[TestMethod]
	public void ObjectExitWaitsForTheLastCollider()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var trigger = scene.CreateObject().Components.Create<BoxCollider>();
		trigger.Scale = new Vector3( 100 );
		trigger.IsTrigger = true;
		var probe = trigger.Components.Create<TriggerProbe>();

		var (bodyGo, rb, first) = CreateBody( scene, Vector3.Zero );
		rb.Gravity = false;
		var second = bodyGo.Components.Create<BoxCollider>();
		second.Scale = new Vector3( 10 );
		second.Center = new Vector3( 40, 0, 0 );

		for ( int i = 0; i < 5; i++ ) scene.GameTick();
		Assert.AreEqual( 2, probe.ColliderEnters.Count );
		Assert.AreEqual( 1, probe.ObjectEnters.Count );

		// Only the second collider leaves
		bodyGo.WorldPosition = new Vector3( 30, 0, 0 );
		for ( int i = 0; i < 5; i++ ) scene.GameTick();
		CollectionAssert.AreEqual( new[] { second }, probe.ColliderExits );
		Assert.AreEqual( 0, probe.ObjectExits.Count, "still touching through the first collider" );
		CollectionAssert.AreEquivalent( new[] { first }, trigger.Touching.ToArray() );

		bodyGo.WorldPosition = new Vector3( 500, 0, 0 );
		for ( int i = 0; i < 5; i++ ) scene.GameTick();
		Assert.AreEqual( 2, probe.ColliderExits.Count );
		CollectionAssert.AreEqual( new[] { bodyGo }, probe.ObjectExits );
		Assert.IsFalse( trigger.Touching.Any() );
	}

	/// <summary>
	/// Disabling, re-enabling and disabling a collider in a trigger fires a matching exit every time.
	/// </summary>
	[TestMethod]
	public void TriggerExitFiresAgainAfterReenable()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var trigger = scene.CreateObject().Components.Create<BoxCollider>();
		trigger.Scale = new Vector3( 100 );
		trigger.IsTrigger = true;
		var probe = trigger.Components.Create<TriggerProbe>();

		var (_, rb, bodyCollider) = CreateBody( scene, Vector3.Zero );
		rb.Gravity = false;

		for ( int cycle = 1; cycle <= 2; cycle++ )
		{
			for ( int i = 0; i < 5; i++ ) scene.GameTick();
			Assert.AreEqual( cycle, probe.ColliderEnters.Count, $"enter {cycle}" );

			bodyCollider.Enabled = false;
			Assert.AreEqual( cycle, probe.ColliderExits.Count, $"exit {cycle}" );
			Assert.IsFalse( trigger.Touching.Any() );

			bodyCollider.Enabled = true;
		}
	}

	/// <summary>
	/// A pickup style trigger that turns itself off from its object enter callback doesn't break event dispatch.
	/// </summary>
	[TestMethod]
	public void TriggerCanDisableItselfOnObjectEnter()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var messages = new List<string>();
		void OnMessage( LogEvent e ) => messages.Add( e.Message );
		Sandbox.Diagnostics.Logging.OnMessage += OnMessage;

		try
		{
			var trigger = scene.CreateObject().Components.Create<BoxCollider>();
			trigger.Scale = new Vector3( 100 );
			trigger.IsTrigger = true;
			var enters = 0;
			trigger.OnObjectTriggerEnter = _ =>
			{
				enters++;
				trigger.Enabled = false;
			};
			var colliderEnters = 0;
			trigger.OnTriggerEnter = _ => colliderEnters++;

			var (_, rb, _) = CreateBody( scene, Vector3.Zero );
			rb.Gravity = false;

			for ( int i = 0; i < 5; i++ ) scene.GameTick();

			Assert.AreEqual( 1, enters );
			Assert.AreEqual( 1, colliderEnters, "the collider enter still fires, like before" );
			Assert.AreEqual( 0, messages.Count, string.Join( "\n", messages ) );
		}
		finally
		{
			Sandbox.Diagnostics.Logging.OnMessage -= OnMessage;
		}
	}

	/// <summary>
	/// A throwing disable callback on the collider doesn't keep it touching the trigger.
	/// </summary>
	[TestMethod]
	public void TriggerExitFiresDespiteAThrowingDisableCallback()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var trigger = scene.CreateObject().Components.Create<BoxCollider>();
		trigger.Scale = new Vector3( 100 );
		trigger.IsTrigger = true;
		var exits = 0;
		trigger.OnTriggerExit = _ => exits++;

		var (_, rb, bodyCollider) = CreateBody( scene, Vector3.Zero );
		rb.Gravity = false;
		for ( int i = 0; i < 5; i++ ) scene.GameTick();
		Assert.IsTrue( trigger.Touching.Contains( bodyCollider ) );

		bodyCollider.OnComponentDisabled += () => throw new InvalidOperationException( "callback failed" );
		try { bodyCollider.Enabled = false; }
		catch ( InvalidOperationException ) { }

		Assert.AreEqual( 1, exits );
		Assert.IsFalse( trigger.Touching.Any() );
	}

	sealed class ThrowingTriggerListener : Component, Component.ITriggerListener
	{
		public void OnTriggerExit( Collider self, Collider other ) => throw new InvalidOperationException( "listener failed" );
	}

	/// <summary>
	/// A listener throwing on exit doesn't stop the other triggers from seeing the exit when a collider is disabled.
	/// </summary>
	[TestMethod]
	public void ThrowingExitListenerDoesntBlockOtherTriggers()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var probes = new List<TriggerProbe>();
		for ( int i = 0; i < 3; i++ )
		{
			var trigger = scene.CreateObject().Components.Create<BoxCollider>();
			trigger.Scale = new Vector3( 100 );
			trigger.IsTrigger = true;
			probes.Add( trigger.Components.Create<TriggerProbe>() );
			if ( i == 1 ) trigger.Components.Create<ThrowingTriggerListener>();
		}

		var (_, rb, bodyCollider) = CreateBody( scene, Vector3.Zero );
		rb.Gravity = false;
		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		bodyCollider.Enabled = false;

		foreach ( var probe in probes )
			Assert.AreEqual( 1, probe.ColliderExits.Count( c => c == bodyCollider ), "every trigger should see the exit" );
	}

	sealed class SceneCollisionProbe : Component, ISceneCollisionEvents
	{
		public List<Collision> Starts { get; } = new();
		public void OnCollisionStart( Collision collision ) => Starts.Add( collision );
	}

	/// <summary>
	/// One contact reaches the scene listeners, both colliders' listeners and the physics body callback, each with
	/// the right side as Self.
	/// </summary>
	[TestMethod]
	public void ContactReachesEveryListenerWithTheRightSides()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var sceneProbe = scene.CreateObject().Components.Create<SceneCollisionProbe>();
		var floor = CreateFloor( scene );
		var floorCollider = floor.Components.Get<BoxCollider>();
		var floorProbe = floor.Components.Create<CollisionProbe>();

		var (bodyGo, rb, bodyCollider) = CreateBody( scene, new Vector3( 0, 0, 20 ) );
		var bodyProbe = bodyGo.Components.Create<CollisionProbe>();

		var bodyIntersections = new List<PhysicsIntersection>();
		rb.PhysicsBody.OnIntersectionStart = bodyIntersections.Add;

		for ( int i = 0; i < 60 && bodyProbe.Starts == 0; i++ ) scene.GameTick();

		Assert.AreEqual( 1, bodyProbe.Starts );
		Assert.AreEqual( bodyCollider, bodyProbe.LastStart.Self.Collider );
		Assert.AreEqual( floorCollider, bodyProbe.LastStart.Other.Collider );
		Assert.AreEqual( floor, bodyProbe.LastStart.Other.GameObject );

		Assert.AreEqual( 1, floorProbe.Starts );
		Assert.AreEqual( floorCollider, floorProbe.LastStart.Self.Collider );
		Assert.AreEqual( bodyCollider, floorProbe.LastStart.Other.Collider );
		Assert.AreEqual( bodyProbe.LastStart.Contact.Point, floorProbe.LastStart.Contact.Point );

		Assert.AreEqual( 1, sceneProbe.Starts.Count );
		var sceneCollision = sceneProbe.Starts[0];
		CollectionAssert.AreEquivalent( new Collider[] { bodyCollider, floorCollider }, new[] { sceneCollision.Self.Collider, sceneCollision.Other.Collider } );

		Assert.AreEqual( 1, bodyIntersections.Count );
		Assert.AreEqual( rb.PhysicsBody, bodyIntersections[0].Self.Body );
		Assert.AreEqual( bodyCollider, bodyIntersections[0].Self.Shape.Collider );
		Assert.AreEqual( floorCollider, bodyIntersections[0].Other.Shape.Collider );
		Assert.AreEqual( bodyProbe.LastStart.Other.Surface, bodyIntersections[0].Other.Surface );
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
