using System;
using Sandbox.Mapping;
using Sandbox.Network;

namespace SceneTests.Components;

/// <summary>
/// Shared helpers for the gameplay component tests.
/// </summary>
internal static class GameComponentTestUtils
{
	/// <summary>
	/// Pushes a Time scope pinned to the scene's own clock. TimeSince fields captured
	/// outside of a GameTick (component construction, calls like Door.Open) record the
	/// ambient global Time.Now, which has no relation to the scene clock that drives
	/// OnFixedUpdate - this scope makes those captures line up with the scene clock so
	/// timed state machines behave deterministically in tests.
	/// </summary>
	public static IDisposable PushSceneClock( Scene scene )
	{
		return Time.Scope( scene.TimeNow, 0.02 );
	}

	/// <summary>
	/// Ticks the scene until the condition holds, giving up after maxTicks. The caller
	/// asserts the condition afterwards so a failure reports properly.
	/// </summary>
	public static void TickUntil( Scene scene, Func<bool> condition, int maxTicks )
	{
		for ( int i = 0; i < maxTicks && !condition(); i++ )
		{
			scene.GameTick();
		}
	}

	/// <summary>
	/// Serializes a GameObject and deserializes it into a fresh disabled GameObject in
	/// the same scene, re-iding the copy so both can coexist.
	/// </summary>
	public static GameObject RoundTrip( GameObject go )
	{
		var node = go.Serialize();
		SceneUtility.MakeIdGuidsUnique( node );

		var copy = new GameObject( false );
		copy.Deserialize( node );
		return copy;
	}
}

/// <summary>
/// Pins the Prop component contract: procedural renderer/collider/rigidbody creation
/// from the model, the static and ragdoll paths, health and damage handling through
/// IDamageable, break callbacks, and serialization of the configured values.
/// </summary>
[TestClass]
public class PropDamageTest
{
	static Model ArrowModel => Model.Load( "models/arrow.vmdl" );
	static Model CitizenModel => Model.Load( "models/citizen/citizen.vmdl" );

	/// <summary>
	/// A model with a single physics part builds a renderer, a non-static
	/// ModelCollider and a Rigidbody. A model without prop data leaves Health at zero.
	/// </summary>
	[TestMethod]
	public void SingleBodyModelBuildsRendererAndRigidbody()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var prop = go.Components.Create<Prop>();
		prop.Model = ArrowModel;

		var renderer = go.Components.Get<ModelRenderer>();
		Assert.IsNotNull( renderer, "a renderer should be created from the model" );
		Assert.AreEqual( ArrowModel, renderer.Model );

		var collider = go.Components.Get<ModelCollider>();
		Assert.IsNotNull( collider, "a model collider should be created" );
		Assert.IsFalse( collider.Static );

		var rb = go.Components.Get<Rigidbody>();
		Assert.IsNotNull( rb, "a rigidbody should be created for a dynamic prop" );
		Assert.IsTrue( rb.PhysicsBody.IsValid() );

		Assert.AreEqual( 0f, prop.Health, "a model without prop data shouldn't set health" );
	}

	/// <summary>
	/// A static prop builds only a static collider - no rigidbody is created.
	/// </summary>
	[TestMethod]
	public void StaticPropBuildsStaticColliderOnly()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var prop = go.Components.Create<Prop>( false );
		prop.IsStatic = true;
		prop.Model = ArrowModel;
		prop.Enabled = true;

		Assert.IsNotNull( go.Components.Get<ModelRenderer>() );

		var collider = go.Components.Get<ModelCollider>();
		Assert.IsNotNull( collider );
		Assert.IsTrue( collider.Static, "a static prop's collider should be static" );

		Assert.IsNull( go.Components.Get<Rigidbody>(), "a static prop must not create a rigidbody" );
	}

	/// <summary>
	/// A model with multiple physics parts (a ragdoll) builds a SkinnedModelRenderer
	/// and a ModelPhysics instead of the single collider/rigidbody pair.
	/// </summary>
	[TestMethod]
	public void RagdollModelBuildsModelPhysics()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var prop = go.Components.Create<Prop>();
		prop.Model = CitizenModel;

		Assert.IsNotNull( go.Components.Get<SkinnedModelRenderer>(), "a skinned model needs a skinned renderer" );

		var physics = go.Components.Get<ModelPhysics>();
		Assert.IsNotNull( physics, "a multi-part model should create ModelPhysics" );
		Assert.AreEqual( CitizenModel, physics.Model );
		Assert.IsTrue( physics.Bodies.Count > 0 );
	}

	/// <summary>
	/// Setting Tint on the prop flows through to the procedural renderer.
	/// </summary>
	[TestMethod]
	public void TintFlowsToRenderer()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var prop = go.Components.Create<Prop>();
		prop.Model = ArrowModel;

		prop.Tint = Color.Red;

		Assert.AreEqual( Color.Red, go.Components.Get<ModelRenderer>().Tint );
	}

	/// <summary>
	/// Non-lethal damage reduces Health, fires the take-damage callback and records
	/// the attacker, without breaking the prop.
	/// </summary>
	[TestMethod]
	public void DamageReducesHealthAndTracksAttacker()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var attacker = scene.CreateObject();

		var go = scene.CreateObject();
		var prop = go.Components.Create<Prop>();
		prop.Model = ArrowModel;
		prop.Health = 100f;

		int damaged = 0;
		int broken = 0;
		prop.OnPropTakeDamage = _ => damaged++;
		prop.OnPropBreak = () => broken++;

		prop.OnDamage( new DamageInfo { Damage = 30f, Attacker = attacker } );

		Assert.AreEqual( 70f, prop.Health, 0.01f );
		Assert.AreEqual( 1, damaged );
		Assert.AreEqual( 0, broken );
		Assert.AreEqual( attacker, prop.LastAttacker );

		scene.ProcessDeletes();
		Assert.IsTrue( go.IsValid(), "a surviving prop must not be destroyed" );
	}

	/// <summary>
	/// A prop at zero health ignores further damage - no callback fires and health
	/// stays at zero - but the attacker is still recorded before the health check.
	/// </summary>
	[TestMethod]
	public void DamageIgnoredAtZeroHealth()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var attacker = scene.CreateObject();

		var go = scene.CreateObject();
		var prop = go.Components.Create<Prop>();
		prop.Model = ArrowModel;

		int damaged = 0;
		prop.OnPropTakeDamage = _ => damaged++;

		Assert.AreEqual( 0f, prop.Health );
		prop.OnDamage( new DamageInfo { Damage = 50f, Attacker = attacker } );

		Assert.AreEqual( 0f, prop.Health );
		Assert.AreEqual( 0, damaged, "the dead feel nothing" );
		Assert.AreEqual( attacker, prop.LastAttacker, "the attacker is recorded even on a dead prop" );

		scene.ProcessDeletes();
		Assert.IsTrue( go.IsValid() );
	}

	/// <summary>
	/// Damage that takes health to zero kills the prop: the break callback runs,
	/// health clamps to zero and the GameObject is destroyed.
	/// </summary>
	[TestMethod]
	public void LethalDamageBreaksAndDestroys()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var prop = go.Components.Create<Prop>();
		prop.Model = ArrowModel;
		prop.Health = 10f;

		int damaged = 0;
		int broken = 0;
		prop.OnPropTakeDamage = _ => damaged++;
		prop.OnPropBreak = () => broken++;

		prop.OnDamage( new DamageInfo { Damage = 25f } );

		Assert.AreEqual( 0f, prop.Health, "health clamps to zero on death" );
		Assert.AreEqual( 1, damaged, "the take damage callback fires before the kill" );
		Assert.AreEqual( 1, broken );

		scene.ProcessDeletes();
		Assert.IsFalse( go.IsValid(), "a killed prop destroys its GameObject" );
	}

	/// <summary>
	/// A prop's configured values survive a serialize/deserialize round trip.
	/// </summary>
	[TestMethod]
	public void SerializedPropKeepsConfiguredValues()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var prop = go.Components.Create<Prop>();
		prop.Model = ArrowModel;
		prop.Health = 42f;
		prop.Tint = Color.Red;
		prop.StartAsleep = true;

		var copy = GameComponentTestUtils.RoundTrip( go );
		var prop2 = copy.Components.Get<Prop>( true );

		Assert.IsNotNull( prop2 );
		Assert.AreEqual( ArrowModel, prop2.Model );
		Assert.AreEqual( 42f, prop2.Health, 0.01f );
		Assert.AreEqual( Color.Red, prop2.Tint );
		Assert.IsTrue( prop2.StartAsleep );
	}
}

/// <summary>
/// Pins the damage-dealing components: TriggerHurt periodic damage with tag filters,
/// RadiusDamage distance falloff and physics push, and FireDamage's fixed-interval
/// burn against the root object.
/// </summary>
[TestClass]
public class TriggerDamageTest
{
	Connection _previousLocalConnection;
	NetworkSystem _previousNetworkSystem;

	/// <summary>
	/// Pins Connection.Local to a host connection and clears Networking.System so the
	/// host-gated damage paths (TriggerHurt's Networking.IsHost check) run locally
	/// instead of being silently skipped when another test in the assembly has leaked
	/// a non-host client connection. Same idiom as ChairTests.cs.
	/// </summary>
	[TestInitialize]
	public void PinHostNetworkingState()
	{
		_previousLocalConnection = Connection.Local;
		_previousNetworkSystem = Networking.System;

		Connection.Local = new TestConnection( Guid.NewGuid(), isHost: true );
		Networking.System = null;
	}

	/// <summary>
	/// Restores whatever global networking state existed before the test, leaving the
	/// rest of the assembly exactly as it was.
	/// </summary>
	[TestCleanup]
	public void RestoreNetworkingState()
	{
		Connection.Local = _previousLocalConnection;
		Networking.System = _previousNetworkSystem;
	}

	/// <summary>
	/// Counts damage delivered through IDamageable, recording the per-event amount
	/// because RadiusDamage restores the DamageInfo's Damage after the loop.
	/// </summary>
	public sealed class DamageCounter : Component, Component.IDamageable
	{
		public int Events { get; private set; }
		public float TotalDamage { get; private set; }
		public float LastAmount { get; private set; }
		public DamageInfo Last { get; private set; }

		public void OnDamage( in DamageInfo damage )
		{
			Events++;
			TotalDamage += damage.Damage;
			LastAmount = damage.Damage;
			Last = damage;
		}
	}

	/// <summary>
	/// Creates a non-falling rigidbody box with a damage counter at the position.
	/// </summary>
	static (GameObject go, DamageCounter counter) CreateVictim( Scene scene, Vector3 position, params string[] tags )
	{
		var go = scene.CreateObject();
		go.WorldPosition = position;

		foreach ( var tag in tags )
		{
			go.Tags.Add( tag );
		}

		var rb = go.Components.Create<Rigidbody>();
		rb.Gravity = false;

		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 10 );

		var counter = go.Components.Create<DamageCounter>();
		return (go, counter);
	}

	/// <summary>
	/// A body resting inside a TriggerHurt takes damage periodically - more than once
	/// over time, but rate limited well below once per fixed update - and the damage
	/// info carries the attacker, amount and configured tags.
	/// </summary>
	[TestMethod]
	public void TriggerHurtAppliesPeriodicDamage()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var triggerGo = scene.CreateObject();
		var triggerBox = triggerGo.Components.Create<BoxCollider>();
		triggerBox.Scale = new Vector3( 100 );
		triggerBox.IsTrigger = true;

		TriggerHurt hurt;
		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			hurt = triggerGo.Components.Create<TriggerHurt>();
		}

		hurt.Damage = 5f;
		hurt.Rate = 0.5f;
		hurt.DamageTags.Add( "burny" );

		var (_, counter) = CreateVictim( scene, Vector3.Zero );

		for ( int i = 0; i < 35; i++ ) scene.GameTick();

		Assert.IsTrue( counter.Events >= 2, $"damage should repeat over time: {counter.Events}" );
		Assert.IsTrue( counter.Events <= 9, $"damage must be rate limited: {counter.Events}" );
		Assert.AreEqual( 5f, counter.LastAmount, 0.01f );
		Assert.AreEqual( triggerGo, counter.Last.Attacker );
		Assert.IsTrue( counter.Last.Tags.Has( "burny" ) );
	}

	/// <summary>
	/// With Include tags set, only targets carrying one of those tags take damage.
	/// </summary>
	[TestMethod]
	public void TriggerHurtIncludeFilter()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var triggerGo = scene.CreateObject();
		var triggerBox = triggerGo.Components.Create<BoxCollider>();
		triggerBox.Scale = new Vector3( 100 );
		triggerBox.IsTrigger = true;

		TriggerHurt hurt;
		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			hurt = triggerGo.Components.Create<TriggerHurt>();
		}

		hurt.Rate = 0.1f;
		hurt.Include.Add( "food" );

		var (_, tagged) = CreateVictim( scene, new Vector3( 20, 0, 0 ), "food" );
		var (_, untagged) = CreateVictim( scene, new Vector3( -20, 0, 0 ) );

		for ( int i = 0; i < 20; i++ ) scene.GameTick();

		Assert.IsTrue( tagged.Events > 0, "the included target should be damaged" );
		Assert.AreEqual( 0, untagged.Events, "targets without an include tag are skipped" );
	}

	/// <summary>
	/// With Exclude tags set, targets carrying one of those tags are spared.
	/// </summary>
	[TestMethod]
	public void TriggerHurtExcludeFilter()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var triggerGo = scene.CreateObject();
		var triggerBox = triggerGo.Components.Create<BoxCollider>();
		triggerBox.Scale = new Vector3( 100 );
		triggerBox.IsTrigger = true;

		TriggerHurt hurt;
		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			hurt = triggerGo.Components.Create<TriggerHurt>();
		}

		hurt.Rate = 0.1f;
		hurt.Exclude.Add( "god" );

		var (_, godly) = CreateVictim( scene, new Vector3( 20, 0, 0 ), "god" );
		var (_, mortal) = CreateVictim( scene, new Vector3( -20, 0, 0 ) );

		for ( int i = 0; i < 20; i++ ) scene.GameTick();

		Assert.AreEqual( 0, godly.Events, "excluded targets are spared" );
		Assert.IsTrue( mortal.Events > 0, "everyone else gets hurt" );
	}

	/// <summary>
	/// RadiusDamage applies on enable by default and falls off linearly with distance.
	/// The shared DamageInfo's Damage is restored to the full amount after the loop -
	/// only the value passed at call time carries the falloff.
	/// </summary>
	[TestMethod]
	public void RadiusDamageFalloffByDistance()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var attacker = scene.CreateObject();

		var (_, near) = CreateVictim( scene, new Vector3( 10, 0, 0 ) );
		var (_, far) = CreateVictim( scene, new Vector3( 100, 0, 0 ) );

		var rdGo = scene.CreateObject();
		var rd = rdGo.Components.Create<RadiusDamage>( false );
		rd.Radius = 200f;
		rd.DamageAmount = 100f;
		rd.Occlusion = false;
		rd.Attacker = attacker;
		rd.DamageTags.Add( "explosion" );
		rd.Enabled = true;

		Assert.AreEqual( 1, near.Events );
		Assert.AreEqual( 1, far.Events );
		Assert.AreEqual( 95f, near.LastAmount, 0.5f, "a target near the center takes almost full damage" );
		Assert.AreEqual( 50f, far.LastAmount, 0.5f, "a target at half the radius takes half damage" );

		Assert.AreEqual( attacker, far.Last.Attacker );
		Assert.AreEqual( rdGo, far.Last.Weapon );
		Assert.IsTrue( far.Last.Origin.AlmostEqual( Vector3.Zero ) );
		Assert.IsTrue( far.Last.Tags.Has( "explosion" ) );

		// The DamageInfo instance is shared - its Damage is restored to the full
		// amount after all targets have been damaged.
		Assert.AreEqual( 100f, far.Last.Damage, 0.5f );
	}

	/// <summary>
	/// RadiusDamage pushes dynamic rigidbodies away from the center.
	/// </summary>
	[TestMethod]
	public void RadiusDamagePushesRigidbodies()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 50, 0, 0 );
		var rb = go.Components.Create<Rigidbody>();
		rb.Gravity = false;
		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 10 );

		var rdGo = scene.CreateObject();
		var rd = rdGo.Components.Create<RadiusDamage>( false );
		rd.Radius = 200f;
		rd.DamageAmount = 0f;
		rd.Occlusion = false;
		rd.Enabled = true;

		// The push is a force - it integrates on the next physics step
		scene.GameTick();

		Assert.IsTrue( rb.Velocity.x > 0f, $"the body should be pushed away from the center: {rb.Velocity}" );
	}

	/// <summary>
	/// With DamageOnEnabled off nothing happens on enable - damage only applies when
	/// Apply is called explicitly.
	/// </summary>
	[TestMethod]
	public void RadiusDamageManualApply()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (_, counter) = CreateVictim( scene, new Vector3( 50, 0, 0 ) );

		var rdGo = scene.CreateObject();
		var rd = rdGo.Components.Create<RadiusDamage>( false );
		rd.Radius = 200f;
		rd.DamageAmount = 40f;
		rd.DamageOnEnabled = false;
		rd.Occlusion = false;
		rd.Enabled = true;

		Assert.AreEqual( 0, counter.Events, "no damage should apply on enable" );

		rd.Apply();

		Assert.AreEqual( 1, counter.Events );
		Assert.AreEqual( 30f, counter.LastAmount, 0.5f, "40 damage at a quarter of the radius leaves 30" );
	}

	/// <summary>
	/// With occlusion enabled and nothing tagged "map" in the way, the line of sight
	/// check passes and damage still applies.
	/// </summary>
	[TestMethod]
	public void RadiusDamageWithOcclusionStillHits()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (_, counter) = CreateVictim( scene, new Vector3( 60, 0, 0 ) );

		var rdGo = scene.CreateObject();
		var rd = rdGo.Components.Create<RadiusDamage>( false );
		rd.Radius = 200f;
		rd.DamageAmount = 50f;
		rd.Enabled = true;

		Assert.AreEqual( 1, counter.Events, "an unoccluded target should take damage" );
		Assert.IsTrue( counter.LastAmount > 0f );
	}

	/// <summary>
	/// FireDamage burns every IDamageable under the root object on a fixed interval:
	/// each event is DamagePerSecond * 0.2 damage, tagged fire and burn.
	/// </summary>
	[TestMethod]
	public void FireDamageBurnsRootPeriodically()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		var counter = root.Components.Create<DamageCounter>();

		var fireGo = scene.CreateObject();
		fireGo.Parent = root;
		fireGo.LocalPosition = new Vector3( 5, 0, 0 );

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			fireGo.Components.Create<FireDamage>();
		}

		for ( int i = 0; i < 20; i++ ) scene.GameTick();

		Assert.IsTrue( counter.Events >= 4, $"the fire should burn repeatedly: {counter.Events}" );
		Assert.IsTrue( counter.Events <= 12, $"the burn is limited to the damage interval: {counter.Events}" );
		Assert.AreEqual( 4f, counter.LastAmount, 0.01f, "each event deals DamagePerSecond * interval" );
		Assert.IsTrue( counter.Last.Tags.Has( "fire" ) );
		Assert.IsTrue( counter.Last.Tags.Has( "burn" ) );
		Assert.IsTrue( counter.Last.Origin.AlmostEqual( new Vector3( 5, 0, 0 ) ) );
	}

	/// <summary>
	/// TriggerHurt and RadiusDamage keep their configured values through a
	/// serialization round trip, and FireDamage survives as a component.
	/// </summary>
	[TestMethod]
	public void DamageComponentSerialization()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var hurt = go.Components.Create<TriggerHurt>();
		hurt.Damage = 25f;
		hurt.Rate = 2.5f;
		hurt.Include.Add( "victim" );

		var rd = go.Components.Create<RadiusDamage>( false );
		rd.Radius = 99f;
		rd.DamageAmount = 12f;
		rd.PhysicsForceScale = 3f;
		rd.DamageOnEnabled = false;
		rd.Occlusion = false;

		go.Components.Create<FireDamage>( false );

		var copy = GameComponentTestUtils.RoundTrip( go );

		var hurt2 = copy.Components.Get<TriggerHurt>( true );
		Assert.IsNotNull( hurt2 );
		Assert.AreEqual( 25f, hurt2.Damage, 0.01f );
		Assert.AreEqual( 2.5f, hurt2.Rate, 0.01f );
		Assert.IsTrue( hurt2.Include.Has( "victim" ) );

		var rd2 = copy.Components.Get<RadiusDamage>( true );
		Assert.IsNotNull( rd2 );
		Assert.AreEqual( 99f, rd2.Radius, 0.01f );
		Assert.AreEqual( 12f, rd2.DamageAmount, 0.01f );
		Assert.AreEqual( 3f, rd2.PhysicsForceScale, 0.01f );
		Assert.IsFalse( rd2.DamageOnEnabled );
		Assert.IsFalse( rd2.Occlusion );

		Assert.IsNotNull( copy.Components.Get<FireDamage>( true ) );
	}
}

/// <summary>
/// Pins the mapping logic components: the Door state machine (rotating and sliding,
/// locking, auto close, linked doors, pressing) and the Button state machine
/// (toggle, auto reset, immediate, continuous and movement animation).
/// </summary>
[TestClass]
public class MapLogicTest
{
	Connection _previousLocalConnection;
	NetworkSystem _previousNetworkSystem;

	/// <summary>
	/// Pins Connection.Local to a host connection and clears Networking.System so the
	/// host-gated entry points (Door.Toggle is [Rpc.Host], and the door/button RPCs
	/// route through Rpc dispatch) execute locally instead of being forwarded or
	/// silently dropped when another test in the assembly has leaked a non-host
	/// client connection. Same idiom as ChairTests.cs.
	/// </summary>
	[TestInitialize]
	public void PinHostNetworkingState()
	{
		_previousLocalConnection = Connection.Local;
		_previousNetworkSystem = Networking.System;

		Connection.Local = new TestConnection( Guid.NewGuid(), isHost: true );
		Networking.System = null;
	}

	/// <summary>
	/// Restores whatever global networking state existed before the test, leaving the
	/// rest of the assembly exactly as it was.
	/// </summary>
	[TestCleanup]
	public void RestoreNetworkingState()
	{
		Connection.Local = _previousLocalConnection;
		Networking.System = _previousNetworkSystem;
	}

	/// <summary>
	/// Creates a door on a fresh GameObject and ticks once so OnStart captures the
	/// start transform and settles the initial state.
	/// </summary>
	static (GameObject go, Door door) CreateDoor( Scene scene )
	{
		var go = scene.CreateObject();
		var door = go.Components.Create<Door>();
		return (go, door);
	}

	/// <summary>
	/// A rotating door starts Closed after OnStart (before that the state defaults to
	/// Open - the enum's zero value), opens through the Opening state to a yaw of
	/// TargetAngle, and closes back through Closing to its start rotation.
	/// </summary>
	[TestMethod]
	public void DoorRotatingOpenCloseStateMachine()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go, door) = CreateDoor( scene );

		// Before OnStart the state is the enum default - Open
		Assert.AreEqual( Door.DoorState.Open, door.State );

		scene.GameTick();
		Assert.AreEqual( Door.DoorState.Closed, door.State, "the door starts closed" );

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			door.Open();
		}

		Assert.AreEqual( Door.DoorState.Opening, door.State, "opening starts synchronously" );

		// Part way through the animation the door has rotated but isn't done
		for ( int i = 0; i < 3; i++ ) scene.GameTick();
		Assert.AreEqual( Door.DoorState.Opening, door.State );
		var midYaw = MathF.Abs( go.WorldRotation.Angles().yaw );
		Assert.IsTrue( midYaw > 1f && midYaw < 89f, $"the door should be mid swing: {midYaw}" );

		GameComponentTestUtils.TickUntil( scene, () => door.State == Door.DoorState.Open, 40 );
		Assert.AreEqual( Door.DoorState.Open, door.State );
		Assert.AreEqual( 90f, MathF.Abs( go.WorldRotation.Angles().yaw ), 1f );

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			door.Close();
		}

		Assert.AreEqual( Door.DoorState.Closing, door.State );

		GameComponentTestUtils.TickUntil( scene, () => door.State == Door.DoorState.Closed, 40 );
		Assert.AreEqual( Door.DoorState.Closed, door.State );
		Assert.AreEqual( 0f, MathF.Abs( go.WorldRotation.Angles().yaw ), 1f );
	}

	/// <summary>
	/// A sliding door moves to its SlideOffset when open and back when closed, at
	/// Speed units per second.
	/// </summary>
	[TestMethod]
	public void DoorSlidingMovesBySlideOffset()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go, door) = CreateDoor( scene );
		door.Mode = Door.DoorMode.Sliding;
		door.SlideOffset = new Vector3( 0, 0, 50 );
		door.Speed = 100f;

		scene.GameTick();
		Assert.AreEqual( Door.DoorState.Closed, door.State );

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			door.Open();
		}

		GameComponentTestUtils.TickUntil( scene, () => door.State == Door.DoorState.Open, 40 );
		Assert.AreEqual( Door.DoorState.Open, door.State );
		Assert.AreEqual( 50f, go.LocalPosition.z, 1f, $"{go.LocalPosition}" );

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			door.Close();
		}

		GameComponentTestUtils.TickUntil( scene, () => door.State == Door.DoorState.Closed, 40 );
		Assert.AreEqual( 0f, go.LocalPosition.z, 1f, $"{go.LocalPosition}" );
	}

	/// <summary>
	/// A locked door refuses to open until unlocked.
	/// </summary>
	[TestMethod]
	public void DoorLockedRefusesToOpen()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (_, door) = CreateDoor( scene );
		door.IsLocked = true;

		scene.GameTick();

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			door.Open();
		}

		Assert.AreEqual( Door.DoorState.Closed, door.State, "a locked door stays closed" );

		for ( int i = 0; i < 5; i++ ) scene.GameTick();
		Assert.AreEqual( Door.DoorState.Closed, door.State );

		door.IsLocked = false;

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			door.Open();
		}

		Assert.AreEqual( Door.DoorState.Opening, door.State, "an unlocked door opens" );
	}

	/// <summary>
	/// With AutoClose enabled the door closes itself after the delay.
	/// </summary>
	[TestMethod]
	public void DoorAutoClosesAfterDelay()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (_, door) = CreateDoor( scene );
		door.AutoClose = true;
		door.AutoCloseDelay = 0.2f;

		scene.GameTick();

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			door.Open();
		}

		Assert.AreEqual( Door.DoorState.Opening, door.State );

		// The door swings fully open, waits out the delay and closes itself again.
		// The open state can be passed within a single tick, so wait for the round trip.
		GameComponentTestUtils.TickUntil( scene, () => door.State == Door.DoorState.Closed, 80 );
		Assert.AreEqual( Door.DoorState.Closed, door.State, "the door should close itself" );
	}

	/// <summary>
	/// StartOpen places the door in the open pose at start, and it can be closed
	/// back to its captured start transform from there.
	/// </summary>
	[TestMethod]
	public void DoorStartOpenBeginsOpen()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go, door) = CreateDoor( scene );
		door.StartOpen = true;

		scene.GameTick();

		Assert.AreEqual( Door.DoorState.Open, door.State );
		Assert.AreEqual( 90f, MathF.Abs( go.WorldRotation.Angles().yaw ), 1f );

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			door.Close();
		}

		GameComponentTestUtils.TickUntil( scene, () => door.State == Door.DoorState.Closed, 40 );
		Assert.AreEqual( Door.DoorState.Closed, door.State );
		Assert.AreEqual( 0f, MathF.Abs( go.WorldRotation.Angles().yaw ), 1f );
	}

	/// <summary>
	/// A linked door back-links itself on start and opens and closes together with
	/// its partner.
	/// </summary>
	[TestMethod]
	public void DoorLinkedDoorFollows()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (_, doorA) = CreateDoor( scene );
		var (_, doorB) = CreateDoor( scene );
		doorA.LinkedDoor = doorB;

		scene.GameTick();

		Assert.AreEqual( doorA, doorB.LinkedDoor, "the linked door should back-link on start" );

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			doorA.Open();
		}

		Assert.AreEqual( Door.DoorState.Opening, doorA.State );
		Assert.AreEqual( Door.DoorState.Opening, doorB.State, "the linked door opens too" );

		GameComponentTestUtils.TickUntil( scene,
			() => doorA.State == Door.DoorState.Open && doorB.State == Door.DoorState.Open, 40 );

		Assert.AreEqual( Door.DoorState.Open, doorA.State );
		Assert.AreEqual( Door.DoorState.Open, doorB.State );

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			doorA.Close();
		}

		Assert.AreEqual( Door.DoorState.Closing, doorB.State, "the linked door closes too" );
	}

	/// <summary>
	/// Pressing the door through IPressable toggles it. While the door is animating
	/// CanPress reports false.
	/// </summary>
	[TestMethod]
	public void DoorPressToggles()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (_, door) = CreateDoor( scene );

		var presserGo = scene.CreateObject();
		presserGo.WorldPosition = new Vector3( 50, 0, 0 );
		var presser = presserGo.Components.Create<BoxCollider>();
		var pressEvent = new Component.IPressable.Event( presser );

		scene.GameTick();

		var pressable = (Component.IPressable)door;
		Assert.IsTrue( pressable.CanPress( pressEvent ), "a resting door can be pressed" );

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			Assert.IsTrue( pressable.Press( pressEvent ) );
		}

		Assert.AreEqual( Door.DoorState.Opening, door.State );
		Assert.IsFalse( pressable.CanPress( pressEvent ), "an animating door can't be pressed" );

		GameComponentTestUtils.TickUntil( scene, () => door.State == Door.DoorState.Open, 40 );
		Assert.IsTrue( pressable.CanPress( pressEvent ) );

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			pressable.Press( pressEvent );
		}

		Assert.AreEqual( Door.DoorState.Closing, door.State, "pressing an open door closes it" );
	}

	/// <summary>
	/// A toggle button with AutoReset turns itself back off after the reset time.
	/// TurnOff during the on-animation is ignored.
	/// </summary>
	[TestMethod]
	public void ButtonToggleAutoResets()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var button = go.Components.Create<Button>();
		button.AnimationTime = 0.2f;
		button.ResetTime = 0.3f;

		scene.GameTick();

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			button.TurnOn();
		}

		Assert.IsTrue( button.IsOn );
		Assert.IsTrue( button.IsAnimating );

		// While the on-animation runs, TurnOff is refused
		button.TurnOff();
		Assert.IsTrue( button.IsOn, "TurnOff must be ignored while animating" );

		GameComponentTestUtils.TickUntil( scene, () => !button.IsOn, 60 );
		Assert.IsFalse( button.IsOn, "the button should auto reset" );
	}

	/// <summary>
	/// Without AutoReset a toggle button stays on until it's explicitly toggled off.
	/// </summary>
	[TestMethod]
	public void ButtonToggleStaysOnWithoutAutoReset()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var button = go.Components.Create<Button>();
		button.AutoReset = false;
		button.AnimationTime = 0.2f;

		scene.GameTick();

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			button.TurnOn();
		}

		for ( int i = 0; i < 15; i++ ) scene.GameTick();

		Assert.IsTrue( button.IsOn, "without auto reset the button stays on" );
		Assert.IsFalse( button.IsAnimating );

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			button.Toggle( null );
		}

		GameComponentTestUtils.TickUntil( scene, () => !button.IsAnimating, 40 );
		Assert.IsFalse( button.IsOn, "toggling an on button turns it off" );
	}

	/// <summary>
	/// An immediate mode button turns itself off right after its on-animation.
	/// </summary>
	[TestMethod]
	public void ButtonImmediateTurnsOffByItself()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var button = go.Components.Create<Button>();
		button.Mode = Button.ButtonMode.Immediate;
		button.AnimationTime = 0.2f;

		scene.GameTick();

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			button.TurnOn();
		}

		Assert.IsTrue( button.IsOn );

		GameComponentTestUtils.TickUntil( scene, () => !button.IsOn, 60 );
		Assert.IsFalse( button.IsOn, "an immediate button turns off on its own" );
	}

	/// <summary>
	/// A continuous mode button only stays on while actively pressed - with nobody
	/// holding it, it turns itself off.
	/// </summary>
	[TestMethod]
	public void ButtonContinuousReleasesWhenNotHeld()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var button = go.Components.Create<Button>();
		button.Mode = Button.ButtonMode.Continuous;
		button.AnimationTime = 0.1f;

		var presserGo = scene.CreateObject();
		var presser = presserGo.Components.Create<BoxCollider>();
		var pressEvent = new Component.IPressable.Event( presser );

		scene.GameTick();

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			((Component.IPressable)button).Press( pressEvent );
		}

		Assert.IsTrue( button.IsOn );

		GameComponentTestUtils.TickUntil( scene, () => !button.IsOn, 60 );
		Assert.IsFalse( button.IsOn, "a continuous button releases when nobody holds it" );
	}

	/// <summary>
	/// With Move enabled the button animates its GameObject by MoveDelta when turned
	/// on, and back to the start position when turned off.
	/// </summary>
	[TestMethod]
	public void ButtonMoveAnimatesPosition()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var button = go.Components.Create<Button>();
		button.AutoReset = false;
		button.AnimationTime = 0.2f;
		button.Move = true;
		button.MoveDelta = new Vector3( 0, 0, -10 );

		scene.GameTick();

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			button.TurnOn();
		}

		for ( int i = 0; i < 10; i++ ) scene.GameTick();

		Assert.AreEqual( -10f, go.LocalPosition.z, 0.5f, $"the button should be fully depressed: {go.LocalPosition}" );

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			button.TurnOff();
		}

		for ( int i = 0; i < 10; i++ ) scene.GameTick();

		Assert.AreEqual( 0f, go.LocalPosition.z, 0.5f, $"the button should return to its start: {go.LocalPosition}" );
	}

	/// <summary>
	/// Door and Button keep their configured values through a serialization round trip.
	/// </summary>
	[TestMethod]
	public void MapComponentSerialization()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var door = go.Components.Create<Door>();
		door.Mode = Door.DoorMode.Sliding;
		door.SlideOffset = new Vector3( 1, 2, 3 );
		door.Speed = 42f;
		door.TargetAngle = 45f;
		door.IsLocked = true;
		door.StartOpen = true;
		door.AutoClose = true;
		door.AutoCloseDelay = 2.5f;

		var button = go.Components.Create<Button>();
		button.Mode = Button.ButtonMode.Continuous;
		button.AutoReset = false;
		button.ResetTime = 3f;
		button.AnimationTime = 1.5f;
		button.Move = true;
		button.MoveDelta = new Vector3( 0, 0, 7 );

		var copy = GameComponentTestUtils.RoundTrip( go );

		var door2 = copy.Components.Get<Door>( true );
		Assert.IsNotNull( door2 );
		Assert.AreEqual( Door.DoorMode.Sliding, door2.Mode );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), door2.SlideOffset );
		Assert.AreEqual( 42f, door2.Speed, 0.01f );
		Assert.AreEqual( 45f, door2.TargetAngle, 0.01f );
		Assert.IsTrue( door2.IsLocked );
		Assert.IsTrue( door2.StartOpen );
		Assert.IsTrue( door2.AutoClose );
		Assert.AreEqual( 2.5f, door2.AutoCloseDelay, 0.01f );

		var button2 = copy.Components.Get<Button>( true );
		Assert.IsNotNull( button2 );
		Assert.AreEqual( Button.ButtonMode.Continuous, button2.Mode );
		Assert.IsFalse( button2.AutoReset );
		Assert.AreEqual( 3f, button2.ResetTime, 0.01f );
		Assert.AreEqual( 1.5f, button2.AnimationTime, 0.01f );
		Assert.IsTrue( button2.Move );
		Assert.AreEqual( new Vector3( 0, 0, 7 ), button2.MoveDelta );
	}
}

/// <summary>
/// Pins the world simulation components: VerletRope sagging between anchors,
/// WaterVolume buoyancy on touching rigidbodies, and ManualHitbox/ModelHitboxes
/// shape creation and hitbox-only tracing.
/// </summary>
[TestClass]
public class WorldComponentTest
{
	static Model CitizenModel => Model.Load( "models/citizen/citizen.vmdl" );

	/// <summary>
	/// A rope with slack between two anchors keeps its endpoints attached and its
	/// middle points sag below the straight line under gravity.
	/// </summary>
	[TestMethod]
	public void RopeSagsBetweenAnchors()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var anchor = scene.CreateObject();
		anchor.WorldPosition = new Vector3( 100, 0, 100 );

		var ropeGo = scene.CreateObject();
		ropeGo.WorldPosition = new Vector3( 0, 0, 100 );

		var line = ropeGo.Components.Create<LineRenderer>();

		var rope = ropeGo.Components.Create<VerletRope>( false );
		rope.Attachment = anchor;
		rope.SegmentCount = 8;
		rope.Slack = 30f;
		rope.Enabled = true;

		for ( int i = 0; i < 20; i++ ) scene.GameTick();

		Assert.IsNotNull( line.VectorPoints, "the rope should publish its points to the renderer" );
		Assert.AreEqual( 9, line.VectorPoints.Count, "SegmentCount + 1 points" );

		Assert.IsTrue( line.VectorPoints[0].AlmostEqual( new Vector3( 0, 0, 100 ), 0.5f ), $"{line.VectorPoints[0]}" );
		Assert.IsTrue( line.VectorPoints[^1].AlmostEqual( new Vector3( 100, 0, 100 ), 0.5f ), $"{line.VectorPoints[^1]}" );

		var minZ = line.VectorPoints.Min( p => p.z );
		Assert.IsTrue( minZ < 95f, $"the rope should sag below its anchors: {minZ}" );
		Assert.IsTrue( minZ > 0f, $"the sag should stay sane: {minZ}" );
	}

	/// <summary>
	/// A dynamic body inside a WaterVolume receives buoyancy and ends up far above
	/// an identical free-falling body, while bodies carrying an ignore tag fall
	/// like there's no water at all. Note GameTick advances 0.1s per call but
	/// ProjectSettings.Physics.MaxFixedUpdates (default 2) clamps each tick to two
	/// 50Hz fixed steps, so 50 ticks only simulate ~2 seconds of physics.
	/// </summary>
	[TestMethod]
	public void WaterVolumeFloatsBodies()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var water = scene.CreateObject();
		var waterBox = water.Components.Create<BoxCollider>();
		waterBox.Scale = new Vector3( 400, 400, 800 );
		waterBox.IsTrigger = true;

		var volume = water.Components.Create<WaterVolume>();
		volume.FluidDensity = 5000f;
		volume.IgnoreTags.Add( "cork" );

		static GameObject CreateFloater( Scene s, Vector3 position, params string[] tags )
		{
			var go = s.CreateObject();
			go.WorldPosition = position;

			foreach ( var tag in tags )
			{
				go.Tags.Add( tag );
			}

			var rb = go.Components.Create<Rigidbody>();
			rb.Gravity = true;

			var box = go.Components.Create<BoxCollider>();
			box.Scale = new Vector3( 10 );

			return go;
		}

		var floater = CreateFloater( scene, new Vector3( 0, 0, -200 ) );
		var corked = CreateFloater( scene, new Vector3( 50, 50, -200 ), "cork" );
		var control = CreateFloater( scene, new Vector3( 2000, 0, -200 ) );

		// 50 ticks = 100 fixed steps at 50Hz (~2s of physics) - free fall covers ~1600 units
		for ( int i = 0; i < 50; i++ ) scene.GameTick();

		Assert.IsTrue( control.WorldPosition.z < -1000f, $"the control should free fall: {control.WorldPosition}" );
		Assert.IsTrue( floater.WorldPosition.z > control.WorldPosition.z + 500f,
			$"buoyancy should hold the floater up: {floater.WorldPosition} vs {control.WorldPosition}" );
		Assert.IsTrue( corked.WorldPosition.z < floater.WorldPosition.z - 500f,
			$"ignored tags fall like there's no water: {corked.WorldPosition} vs {floater.WorldPosition}" );
	}

	/// <summary>
	/// A manual hitbox only registers on traces with UseHitboxes - a plain physics
	/// trace passes straight through. The trace result reports the hitbox and the
	/// owning GameObject, or the Target override when set.
	/// </summary>
	[TestMethod]
	public void ManualHitboxTraceableOnlyWithUseHitboxes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );

		var hb = go.Components.Create<ManualHitbox>( false );
		hb.Shape = ManualHitbox.HitboxShape.Box;
		hb.CenterA = Vector3.Zero;
		hb.CenterB = new Vector3( 50, 50, 50 );
		hb.Enabled = true;

		var plain = scene.Trace.Ray( Vector3.Zero, new Vector3( 300, 0, 0 ) ).Run();
		Assert.IsFalse( plain.Hit, "hitboxes live outside the physics world" );

		var result = scene.Trace.Ray( Vector3.Zero, new Vector3( 300, 0, 0 ) ).UseHitboxes().Run();
		Assert.IsTrue( result.Hit );
		Assert.IsNotNull( result.Hitbox );
		Assert.AreEqual( go, result.GameObject, "the hitbox reports its own GameObject by default" );
		Assert.AreEqual( 75f, result.EndPosition.x, 0.5f, "the box face should be at x=75" );

		// A target override redirects what the trace reports
		var target = scene.CreateObject();
		hb.Target = target;

		result = scene.Trace.Ray( Vector3.Zero, new Vector3( 300, 0, 0 ) ).UseHitboxes().Run();
		Assert.IsTrue( result.Hit );
		Assert.AreEqual( target, result.GameObject, "the trace should report the target override" );
	}

	/// <summary>
	/// Each manual hitbox shape type builds exactly one physics shape on its body,
	/// and disabling the component destroys the hitbox.
	/// </summary>
	[TestMethod]
	public void ManualHitboxShapes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		static ManualHitbox CreateShape( Scene s, ManualHitbox.HitboxShape shape )
		{
			var go = s.CreateObject();
			var hb = go.Components.Create<ManualHitbox>( false );
			hb.Shape = shape;
			hb.CenterA = Vector3.Zero;
			hb.CenterB = new Vector3( 40, 30, 20 );
			hb.Radius = 10f;
			hb.Enabled = true;
			return hb;
		}

		var sphere = CreateShape( scene, ManualHitbox.HitboxShape.Sphere );
		Assert.IsNotNull( sphere.Hitbox );
		Assert.AreEqual( 1, sphere.Hitbox.Body.Shapes.Count() );
		Assert.IsTrue( sphere.Hitbox.Body.Shapes.First().IsSphereShape );

		var capsule = CreateShape( scene, ManualHitbox.HitboxShape.Capsule );
		Assert.IsNotNull( capsule.Hitbox );
		Assert.AreEqual( 1, capsule.Hitbox.Body.Shapes.Count() );
		Assert.IsTrue( capsule.Hitbox.Body.Shapes.First().IsCapsuleShape );

		var box = CreateShape( scene, ManualHitbox.HitboxShape.Box );
		Assert.IsNotNull( box.Hitbox );
		Assert.AreEqual( 1, box.Hitbox.Body.Shapes.Count() );

		var cylinder = CreateShape( scene, ManualHitbox.HitboxShape.Cylinder );
		Assert.IsNotNull( cylinder.Hitbox );
		Assert.AreEqual( 1, cylinder.Hitbox.Body.Shapes.Count() );

		// Disabling destroys the hitbox
		sphere.Enabled = false;
		Assert.IsNull( sphere.Hitbox );
	}

	/// <summary>
	/// ModelHitboxes builds hitboxes from the model's hitbox set on the renderer's
	/// bones, traces report the owning GameObject, and disabling clears them again.
	/// </summary>
	[TestMethod]
	public void ModelHitboxesBuildFromModel()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var renderer = go.Components.Create<SkinnedModelRenderer>();
		renderer.Model = CitizenModel;

		var hitboxes = go.Components.Create<ModelHitboxes>();
		hitboxes.Renderer = renderer;

		Assert.IsTrue( hitboxes.Hitboxes.Count > 0, "the citizen model should produce hitboxes" );
		Assert.IsTrue( hitboxes.Hitboxes.All( h => h.Bone is not null ), "every hitbox hangs off a bone" );

		// Straight down through the body - this only hits via the hitbox path
		var result = scene.Trace.Ray( new Vector3( 0, 0, 100 ), new Vector3( 0, 0, -10 ) ).UseHitboxes().Run();
		Assert.IsTrue( result.Hit, "the trace should hit a hitbox" );
		Assert.IsNotNull( result.Hitbox );
		Assert.AreEqual( go, result.GameObject );

		hitboxes.Enabled = false;
		Assert.AreEqual( 0, hitboxes.Hitboxes.Count, "disabling clears the hitboxes" );

		var after = scene.Trace.Ray( new Vector3( 0, 0, 100 ), new Vector3( 0, 0, -10 ) ).UseHitboxes().Run();
		Assert.IsFalse( after.Hit, "no hitboxes remain to be hit" );
	}

	/// <summary>
	/// VerletRope, WaterVolume and ManualHitbox keep their configured values through
	/// a serialization round trip; the rope's attachment reference resolves back to
	/// the same scene object.
	/// </summary>
	[TestMethod]
	public void WorldComponentSerialization()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var anchor = scene.CreateObject();
		anchor.WorldPosition = new Vector3( 100, 0, 100 );

		var go = scene.CreateObject();

		var rope = go.Components.Create<VerletRope>( false );
		rope.Attachment = anchor;
		rope.SegmentCount = 12;
		rope.Slack = 25f;
		rope.Radius = 2f;
		rope.Stiffness = 0.9f;

		var volume = go.Components.Create<WaterVolume>( false );
		volume.FluidDensity = 1234f;
		volume.LinearDrag = 0.4f;
		volume.AngularDrag = 1.2f;
		volume.FluidVelocity = new Vector3( 10, 0, 0 );
		volume.SurfaceOffset = 5f;

		var hb = go.Components.Create<ManualHitbox>( false );
		hb.Shape = ManualHitbox.HitboxShape.Capsule;
		hb.Radius = 7f;
		hb.CenterA = new Vector3( 1, 2, 3 );
		hb.CenterB = new Vector3( 4, 5, 6 );

		var copy = GameComponentTestUtils.RoundTrip( go );

		var rope2 = copy.Components.Get<VerletRope>( true );
		Assert.IsNotNull( rope2 );
		Assert.AreEqual( 12, rope2.SegmentCount );
		Assert.AreEqual( 25f, rope2.Slack, 0.01f );
		Assert.AreEqual( 2f, rope2.Radius, 0.01f );
		Assert.AreEqual( 0.9f, rope2.Stiffness, 0.01f );
		Assert.AreEqual( anchor, rope2.Attachment, "the attachment should resolve to the same scene object" );

		var volume2 = copy.Components.Get<WaterVolume>( true );
		Assert.IsNotNull( volume2 );
		Assert.AreEqual( 1234f, volume2.FluidDensity, 0.01f );
		Assert.AreEqual( 0.4f, volume2.LinearDrag, 0.01f );
		Assert.AreEqual( 1.2f, volume2.AngularDrag, 0.01f );
		Assert.AreEqual( new Vector3( 10, 0, 0 ), volume2.FluidVelocity );
		Assert.AreEqual( 5f, volume2.SurfaceOffset, 0.01f );

		var hb2 = copy.Components.Get<ManualHitbox>( true );
		Assert.IsNotNull( hb2 );
		Assert.AreEqual( ManualHitbox.HitboxShape.Capsule, hb2.Shape );
		Assert.AreEqual( 7f, hb2.Radius, 0.01f );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), hb2.CenterA );
		Assert.AreEqual( new Vector3( 4, 5, 6 ), hb2.CenterB );
	}
}
