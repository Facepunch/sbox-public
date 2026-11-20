using Sandbox.Engine.Resources;
using NavigationTests;
using Sandbox.Npcs.Schedules;
using Sandbox.Npcs.Tasks;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;
using System.Collections.Generic;

namespace NpcTests;

public static class NpcTestKit
{
	public sealed class TestNpc : BaseNpc
	{
		public List<string> Log = new();
		public BaseSchedule Wanted;
		internal NpcAwareness Sensed;
		public GameObject HurtBy;
		public GameObject KilledBy;
		public int RagdollCalls;
		public Action<Component.IDamageable.DamageEvent> Shaping;
		public Action<Component.IDamageable.DamageEvent> Hurting;
		public Action<Component.IDamageable.DeathEvent> Dying;

		public T Schedule<T>() where T : BaseSchedule, new() => GetSchedule<T>();
		public void Interrupt() => EndCurrentSchedule();

		protected override void OnCarryStarted( GameObject carrier ) => Log.Add( $"carried by {carrier.Name}" );

		protected override void OnCarryStopped( GameObject carrier )
		{
			Log.Add( $"let go by {carrier.Name}" );
			base.OnCarryStopped( carrier );
		}

		protected override void OnDamaging( Component.IDamageable.DamageEvent damage )
		{
			if ( Shaping is null )
				return;

			Log.Add( "own" );
			Shaping( damage );
		}

		protected override void OnDamaged( Component.IDamageable.DamageEvent damage )
		{
			Log.Add( "hurt" );
			HurtBy = damage.Instigator;
			Hurting?.Invoke( damage );
		}

		protected override void OnDeath( Component.IDamageable.DeathEvent death )
		{
			Log.Add( "die" );
			KilledBy = death.Instigator;
			Dying?.Invoke( death );
			base.OnDeath( death );
		}

		protected override BaseSchedule SelectSchedule() => Wanted;
		protected override GameObject CreateRagdoll()
		{
			RagdollCalls++;
			return base.CreateRagdoll();
		}
		internal override NpcAwareness GatherAwareness() => base.GatherAwareness() | Sensed;
	}

	public sealed class Listener : Component, Component.IDamageable.IEvents
	{
		public List<string> Log;
		public Action<Component.IDamageable.DamageEvent> Damaging;
		public Action<Component.IDamageable.DamageEvent> AfterDamage;
		public Action<Component.IDamageable.DeathEvent> AfterDeath;
		public Action<Component.IDamageable.DyingEvent> BeforeDeath;
		public Component.IDamageable.DamageEvent Damaged;
		public Component.IDamageable.DeathEvent Died;

		void IDamageable.IEvents.OnDamaging( Component.IDamageable.DamageEvent e )
		{
			Log.Add( "damaging" );
			Damaging?.Invoke( e );
		}

		void IDamageable.IEvents.OnDamaged( Component.IDamageable.DamageEvent e )
		{
			Log.Add( "damaged" );
			Damaged = e;
			AfterDamage?.Invoke( e );
		}

		void IDamageable.IEvents.OnDying( Component.IDamageable.DyingEvent e ) => BeforeDeath?.Invoke( e );

		void IDamageable.IEvents.OnDied( Component.IDamageable.DeathEvent e )
		{
			Log.Add( "died" );
			Died = e;
			AfterDeath?.Invoke( e );
		}
	}

	public static (TestNpc Npc, Listener Listener) Spawn( Scene scene )
	{
		// Test types aren't in the type library, so the required animation layer is added by hand
		var go = scene.CreateObject();
		var animation = go.Components.Create<AnimationLayer>();
		var npc = go.Components.Create<TestNpc>();
		npc.Animation ??= animation;

		var listener = scene.CreateObject().Components.Create<Listener>();
		listener.Log = npc.Log;
		scene.GameTick();
		return (npc, listener);
	}

	public static void Hurt( BaseNpc npc, float amount, GameObject attacker = null )
	{
		((Component.IDamageable)npc).OnDamage( new DamageInfo( amount, attacker, null ) );
	}

	public static int Times( this List<string> log, string entry ) => log.Count( x => x == entry );
}

/// <summary>
/// Damage and death are one transaction each: <see cref="Component.IDamageable.IEvents"/> in order, one settled hit for everyone,
/// nested hits defined, and a death that happens once whatever the callbacks do.
/// </summary>
[TestClass]
public class NpcDamageTests
{
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void DyingCanCancelDamageOrDirectHealthDeath( bool direct )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, listener) = NpcTestKit.Spawn( scene );
		listener.BeforeDeath = e => e.Cancelled = true;

		if ( direct ) npc.Health = 0;
		else NpcTestKit.Hurt( npc, 500 );

		Assert.AreEqual( 1, npc.Health );
		Assert.IsFalse( npc.GameObject.IsDestroyed );
		Assert.AreEqual( 0, npc.RagdollCalls );
		Assert.IsNull( listener.Died );

		listener.BeforeDeath = null;
		NpcTestKit.Hurt( npc, 1 );
		Assert.AreEqual( 1, npc.Log.Times( "die" ) );
		Assert.IsNotNull( listener.Died );
	}

	[TestMethod]
	public void DyingCanSuppressRagdollBeforeItIsCreated()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, listener) = NpcTestKit.Spawn( scene );
		var attacker = scene.CreateObject();
		listener.BeforeDeath = e =>
		{
			Assert.AreEqual( attacker, e.Instigator );
			Assert.AreEqual( npc.GameObject, e.Victim );
			e.CreateRagdoll = false;
			npc.Log.Add( "dying" );
		};
		bool victimPresent = false;
		listener.AfterDeath = e => victimPresent = !e.Victim.IsDestroyed;

		NpcTestKit.Hurt( npc, 500, attacker );

		CollectionAssert.AreEqual( new[] { "damaging", "hurt", "damaged", "dying", "die", "died" }, npc.Log );
		Assert.AreEqual( 0, npc.RagdollCalls );
		Assert.IsNull( listener.Died.Ragdoll );
		Assert.IsTrue( victimPresent );
	}

	[TestMethod]
	public void DyingDoesNotReenterOnDamageOrHealthAssignment()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, listener) = NpcTestKit.Spawn( scene );
		int decisions = 0;
		listener.BeforeDeath = e =>
		{
			decisions++;
			npc.Health = 0;
			NpcTestKit.Hurt( npc, 500 );
		};

		NpcTestKit.Hurt( npc, 500 );

		Assert.AreEqual( 1, decisions );
		Assert.AreEqual( 1, npc.RagdollCalls );
		Assert.AreEqual( 1, npc.Log.Times( "died" ) );
	}

	[TestMethod]
	public void DamageRaisesEventsInOrder()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, listener) = NpcTestKit.Spawn( scene );
		var attacker = scene.CreateObject();

		NpcTestKit.Hurt( npc, 30, attacker );

		CollectionAssert.AreEqual( new[] { "damaging", "hurt", "damaged" }, npc.Log );
		Assert.AreEqual( 70, npc.Health );
		Assert.AreEqual( npc.GameObject, listener.Damaged.Victim );
		Assert.AreEqual( 30, listener.Damaged.AcceptedDamage );
		Assert.AreEqual( attacker, listener.Damaged.Instigator );
		Assert.AreEqual( attacker, npc.HurtBy );
	}

	[TestMethod]
	public void EveryoneSeesTheSettledHit()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, listener) = NpcTestKit.Spawn( scene );
		var blamed = scene.CreateObject();
		float seenByNpc = 0;
		listener.Damaging = e =>
		{
			e.AcceptedDamage *= 2;
			e.Instigator = blamed;
		};
		npc.Hurting = e => seenByNpc = e.AcceptedDamage;

		// Changes after the hit is settled are ignored
		listener.AfterDamage = e =>
		{
			e.AcceptedDamage = 0;
			e.Cancelled = true;
			e.Instigator = null;
		};

		NpcTestKit.Hurt( npc, 60, scene.CreateObject() );

		Assert.AreEqual( 120, seenByNpc, "The NPC reacts to the damage the listeners settled on" );
		Assert.AreEqual( blamed, npc.HurtBy, "and to whoever they blamed" );
		Assert.AreEqual( blamed, listener.Died.Instigator, "The hit went through as it was settled, whatever a listener did to the event afterwards" );
		Assert.AreEqual( blamed, npc.KilledBy );
	}

	[TestMethod]
	public void TheNpcShapesItsOwnHitBeforeTheScene()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, listener) = NpcTestKit.Spawn( scene );
		var blamed = scene.CreateObject();
		float seenByScene = 0;
		GameObject blamedForScene = null;
		npc.Shaping = e =>
		{
			e.AcceptedDamage *= 0.5f;
			e.Instigator = blamed;
		};
		listener.Damaging = e =>
		{
			seenByScene = e.AcceptedDamage;
			blamedForScene = e.Instigator;
			e.AcceptedDamage += 5;
		};

		NpcTestKit.Hurt( npc, 40, scene.CreateObject() );

		CollectionAssert.AreEqual( new[] { "own", "damaging", "hurt", "damaged" }, npc.Log );
		Assert.AreEqual( 20, seenByScene, "The scene's listeners see what the NPC made of the hit" );
		Assert.AreEqual( blamed, blamedForScene );
		Assert.AreEqual( 75, npc.Health, "and may still change it" );
		Assert.AreEqual( 25, listener.Damaged.AcceptedDamage );
	}

	[TestMethod]
	public void AHitTheNpcCancelsItselfSkipsEverythingAfter()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, listener) = NpcTestKit.Spawn( scene );
		Component.IDamageable.DamageEvent hit = null;
		npc.Shaping = e =>
		{
			hit = e;
			e.Cancelled = true;
		};

		NpcTestKit.Hurt( npc, 500 );

		CollectionAssert.AreEqual( new[] { "own" }, npc.Log, "The scene stage is not raised for it" );
		Assert.AreEqual( 100, npc.Health );
		Assert.IsTrue( hit.Cancelled );
	}

	[TestMethod]
	public void KillingOrDestroyingTheNpcInItsOwnPreDamageStopsTheHit()
	{
		foreach ( var how in new[] { "destroy", "kill" } )
		{
			var scene = new Scene();
			using var scope = scene.Push();
			var (npc, listener) = NpcTestKit.Spawn( scene );
			var done = false;
			npc.Shaping = e =>
			{
				if ( done ) return;
				done = true;
				if ( how == "destroy" ) npc.GameObject.Destroy();
				else NpcTestKit.Hurt( npc, 500 );
			};

			NpcTestKit.Hurt( npc, 10 );

			Assert.AreEqual( how == "destroy" ? "own" : "died", npc.Log[^1], $"Nothing of the outer hit runs after that ({how})" );
			Assert.AreEqual( how == "destroy" ? 0 : 1, npc.Log.Times( "died" ), how );
			Assert.AreEqual( how == "destroy" ? 0 : 1, npc.Log.Times( "damaging" ), $"Only the nested hit reaches the scene ({how})" );
		}
	}

	[TestMethod]
	public void AThrowingOwnPreDamageLeavesTheHitAsItWas()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, listener) = NpcTestKit.Spawn( scene );
		npc.Shaping = e => throw new InvalidOperationException( "own" );

		NpcTestKit.Hurt( npc, 30 );

		CollectionAssert.AreEqual( new[] { "own", "damaging", "hurt", "damaged" }, npc.Log );
		Assert.AreEqual( 70, npc.Health );
	}

	[TestMethod]
	public void CancelledDamageDoesNothing()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, listener) = NpcTestKit.Spawn( scene );
		listener.Damaging = e => e.Cancelled = true;

		NpcTestKit.Hurt( npc, 500 );

		CollectionAssert.AreEqual( new[] { "damaging" }, npc.Log );
		Assert.AreEqual( 100, npc.Health );
		Assert.IsTrue( npc.IsValid() );
	}

	[TestMethod]
	public void NegativeAndInvalidDamageTakesNothing()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, listener) = NpcTestKit.Spawn( scene );

		NpcTestKit.Hurt( npc, -50 );
		Assert.AreEqual( 100, npc.Health, "Damage does not heal" );
		Assert.AreEqual( 0, listener.Damaged.AcceptedDamage );

		NpcTestKit.Hurt( npc, float.NaN );
		Assert.AreEqual( 100, npc.Health );

		listener.Damaging = e => e.AcceptedDamage = float.NaN;
		NpcTestKit.Hurt( npc, 10 );
		Assert.AreEqual( 100, npc.Health );
		Assert.AreEqual( 0, npc.Log.Times( "die" ) );
	}

	[TestMethod]
	public void DeathRaisesEventsInOrderAndDestroysTheNpc()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, listener) = NpcTestKit.Spawn( scene );
		var attacker = scene.CreateObject();

		NpcTestKit.Hurt( npc, 500, attacker );

		CollectionAssert.AreEqual( new[] { "damaging", "hurt", "damaged", "die", "died" }, npc.Log );
		Assert.AreEqual( npc.GameObject, listener.Died.Victim );
		Assert.AreEqual( attacker, listener.Died.Instigator );
		Assert.IsNull( listener.Died.Ragdoll, "An NPC without a model leaves no ragdoll" );

		NpcTestKit.Hurt( npc, 10 );
		Assert.AreEqual( 5, npc.Log.Count, "A dead NPC takes no more damage" );

		scene.GameTick();
		Assert.IsFalse( npc.IsValid() );
	}

	[TestMethod]
	public void ANestedLethalHitKillsOnceAndStopsTheOuterHit()
	{
		foreach ( var where in new[] { "damaging", "hurt", "damaged" } )
		{
			var scene = new Scene();
			using var scope = scene.Push();
			var (npc, listener) = NpcTestKit.Spawn( scene );
			var nested = false;

			void Again()
			{
				if ( nested ) return;
				nested = true;
				NpcTestKit.Hurt( npc, 500 );
			}

			if ( where == "damaging" ) listener.Damaging = e => Again();
			if ( where == "hurt" ) npc.Hurting = e => Again();
			if ( where == "damaged" ) listener.AfterDamage = e => Again();

			NpcTestKit.Hurt( npc, 500 );

			Assert.AreEqual( 1, npc.Log.Times( "die" ), $"nested in {where}" );
			Assert.AreEqual( 1, npc.Log.Times( "died" ), $"nested in {where}" );
			Assert.AreEqual( "died", npc.Log[^1], $"Nothing of the outer hit runs after the death, nested in {where}" );
		}
	}

	[TestMethod]
	public void ANestedHitThatDoesNotKillRunsInFullFirst()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, listener) = NpcTestKit.Spawn( scene );
		var nested = false;
		listener.Damaging = e =>
		{
			if ( nested ) return;
			nested = true;
			NpcTestKit.Hurt( npc, 20 );
		};

		NpcTestKit.Hurt( npc, 30 );

		CollectionAssert.AreEqual( new[] { "damaging", "damaging", "hurt", "damaged", "hurt", "damaged" }, npc.Log );
		Assert.AreEqual( 50, npc.Health, "Both hits count" );
	}

	[TestMethod]
	public void HitsWhileItDiesAreIgnored()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, listener) = NpcTestKit.Spawn( scene );
		npc.Dying = e => NpcTestKit.Hurt( npc, 10 );
		listener.AfterDeath = e => NpcTestKit.Hurt( npc, 10 );

		NpcTestKit.Hurt( npc, 500 );

		CollectionAssert.AreEqual( new[] { "damaging", "hurt", "damaged", "die", "died" }, npc.Log );
	}

	[TestMethod]
	public void DestroyingTheNpcInsideACallbackStopsTheHit()
	{
		foreach ( var where in new[] { "damaging", "hurt", "damaged" } )
		{
			var scene = new Scene();
			using var scope = scene.Push();
			var (npc, listener) = NpcTestKit.Spawn( scene );

			if ( where == "damaging" ) listener.Damaging = e => npc.GameObject.Destroy();
			if ( where == "hurt" ) npc.Hurting = e => npc.GameObject.Destroy();
			if ( where == "damaged" ) listener.AfterDamage = e => npc.GameObject.Destroy();

			NpcTestKit.Hurt( npc, 500 );

			Assert.AreEqual( where, npc.Log[^1], $"Nothing runs after the NPC was destroyed in {where}" );
			Assert.AreEqual( 0, npc.Log.Times( "died" ), $"destroyed in {where}" );

			scene.GameTick();
			Assert.IsFalse( npc.IsValid() );
		}
	}

	[TestMethod]
	public void DestroyingTheNpcWhileItDiesStillFinishesTheDeathOnce()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, listener) = NpcTestKit.Spawn( scene );
		npc.Dying = e => npc.GameObject.Destroy();

		NpcTestKit.Hurt( npc, 500 );

		CollectionAssert.AreEqual( new[] { "damaging", "hurt", "damaged", "die", "died" }, npc.Log );
	}

	[TestMethod]
	public void AThrowingReactionDoesNotBreakTheDeath()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, listener) = NpcTestKit.Spawn( scene );
		npc.Hurting = e => throw new InvalidOperationException( "hurt" );
		npc.Dying = e => throw new InvalidOperationException( "die" );

		NpcTestKit.Hurt( npc, 500 );

		CollectionAssert.AreEqual( new[] { "damaging", "hurt", "damaged", "die", "died" }, npc.Log );

		scene.GameTick();
		Assert.IsFalse( npc.IsValid() );
	}

	[TestMethod]
	public void SettingHealthBelowOneKillsThroughTheDeathPath()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, listener) = NpcTestKit.Spawn( scene );

		npc.Health = float.NaN;
		Assert.AreEqual( 100, npc.Health, "Not a number is ignored" );

		npc.Health = 40;
		Assert.AreEqual( 0, npc.Log.Count );

		npc.Health = -5;
		Assert.AreEqual( 0, npc.Health, "Health is never negative" );
		CollectionAssert.AreEqual( new[] { "die", "died" }, npc.Log );
		Assert.IsNotNull( listener.Died.DamageInfo );
		Assert.IsNull( listener.Died.Instigator );

		npc.Health = 0;
		Assert.AreEqual( 1, npc.Log.Times( "died" ), "Only once" );
	}

	[TestMethod]
	public void HealthThroughTheDamageableInterfaceIsTheNpcsOwn()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, _) = NpcTestKit.Spawn( scene );
		NpcTestKit.Hurt( npc, 60 );
		npc.Log.Clear();

		// Typed as the interface: a member that failed to bind would answer with the interface's defaults
		Component.IDamageable damageable = npc;
		Assert.AreEqual( 40, damageable.Health );
		Assert.AreEqual( 100, damageable.MaxHealth );

		damageable.Health = MathF.Min( damageable.MaxHealth, damageable.Health + 25 );
		Assert.AreEqual( 65, npc.Health );
		Assert.AreEqual( 0, npc.Log.Count, "Healing is not a hit" );

		npc.MaxHealth = 150;
		Assert.AreEqual( 150, damageable.MaxHealth );
	}

	[TestMethod]
	public void SettingHealthInsideAHitLeavesTheDeathToTheHit()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, listener) = NpcTestKit.Spawn( scene );
		var attacker = scene.CreateObject();
		listener.Damaging = e => npc.Health = 0;

		NpcTestKit.Hurt( npc, 10, attacker );

		CollectionAssert.AreEqual( new[] { "damaging", "hurt", "damaged", "die", "died" }, npc.Log );
		Assert.AreEqual( attacker, listener.Died.Instigator );
	}

	[TestMethod]
	public void HealthSetToZeroInsideACancelledHitStillKills()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, listener) = NpcTestKit.Spawn( scene );
		listener.Damaging = e =>
		{
			npc.Health = 0;
			e.Cancelled = true;
		};

		NpcTestKit.Hurt( npc, 10, scene.CreateObject() );

		CollectionAssert.AreEqual( new[] { "damaging", "die", "died" }, npc.Log );
		Assert.IsNull( listener.Died.Instigator, "The cancelled hit is not blamed for it" );

		scene.GameTick();
		Assert.IsFalse( npc.IsValid() );
	}

	[TestMethod]
	public void HealthSetToZeroInsideANestedCancelledHitIsSettledByTheOuterHit()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, listener) = NpcTestKit.Spawn( scene );
		var attacker = scene.CreateObject();
		var depth = 0;
		listener.Damaging = e =>
		{
			if ( depth++ == 0 )
			{
				NpcTestKit.Hurt( npc, 5 );
				return;
			}

			npc.Health = 0;
			e.Cancelled = true;
		};

		NpcTestKit.Hurt( npc, 10, attacker );

		CollectionAssert.AreEqual( new[] { "damaging", "damaging", "hurt", "damaged", "die", "died" }, npc.Log );
		Assert.AreEqual( attacker, listener.Died.Instigator );
	}

	[TestMethod]
	public void ListenersAfterOneThatDestroyedOrKilledTheNpcAreSkipped()
	{
		foreach ( var how in new[] { "destroy", "kill" } )
		{
			var scene = new Scene();
			using var scope = scene.Push();
			var (npc, listener) = NpcTestKit.Spawn( scene );
			var other = scene.CreateObject().Components.Create<NpcTestKit.Listener>();
			other.Log = npc.Log;
			var done = false;

			void Act( Component.IDamageable.DamageEvent e )
			{
				if ( done ) return;
				done = true;
				if ( how == "destroy" ) npc.GameObject.Destroy();
				else NpcTestKit.Hurt( npc, 500 );
			}

			listener.AfterDamage = Act;
			other.AfterDamage = Act;

			NpcTestKit.Hurt( npc, 10 );

			// The lethal nested hit is heard by both listeners, the outer hit only by the one that dealt it
			Assert.AreEqual( how == "destroy" ? 1 : 3, npc.Log.Times( "damaged" ), $"Listeners after the one the NPC did not outlive are skipped ({how})" );
		}
	}
}

/// <summary>
/// The schedule runner: selection and preemption, and a lifecycle that ends everything once whatever a callback
/// does, including ending the schedule, disabling the NPC or throwing.
/// </summary>
[TestClass]
public class NpcScheduleTests
{
	public sealed class Probe : BaseTask
	{
		public Func<Probe, TaskResult> Update = _ => TaskResult.Running;
		public Action End;
		public int Starts, Ends;
		public TaskResult EndedAs;

		protected override void OnStart() => Starts++;
		protected override TaskResult OnUpdate() => Update( this );

		protected override void OnEnd()
		{
			Ends++;
			EndedAs = Status;
			End?.Invoke();
		}
	}

	public class ProbeSchedule : BaseSchedule
	{
		public List<Probe> Tasks = new();
		public Action End;
		public int Starts, Ends;

		protected override void OnStart()
		{
			Starts++;
			foreach ( var task in Tasks ) AddTask( task );
		}

		protected override void OnEnd()
		{
			Ends++;
			End?.Invoke();
		}
	}

	public sealed class AlertProbeSchedule : ProbeSchedule
	{
		public override int Priority => SchedulePriorities.Alert;
	}

	static (NpcTestKit.TestNpc Npc, ProbeSchedule Schedule, Probe First, Probe Second) Running( Scene scene )
	{
		var (npc, _) = NpcTestKit.Spawn( scene );
		var schedule = npc.Schedule<ProbeSchedule>();
		var first = new Probe();
		var second = new Probe();
		schedule.Tasks = [first, second];
		npc.Wanted = schedule;
		scene.GameTick();
		Assert.AreEqual( schedule, npc.ActiveSchedule );
		Assert.AreEqual( 1, first.Starts );
		return (npc, schedule, first, second);
	}

	[TestMethod]
	public void TheSelectedScheduleRunsItsTasksInOrderAndIsSelectedAgain()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, schedule, first, second) = Running( scene );
		first.Update = _ => TaskResult.Success;
		second.Update = _ => TaskResult.Success;

		for ( int i = 0; i < 6; i++ ) scene.GameTick();

		Assert.IsTrue( schedule.Starts > 1, "A finished schedule is selected again" );
		Assert.IsTrue( second.Ends >= 1 );
		Assert.AreEqual( TaskResult.Success, first.EndedAs );
		Assert.AreEqual( TaskResult.Success, second.EndedAs );

		npc.Wanted = null;
		for ( int i = 0; i < 6; i++ ) scene.GameTick();
		Assert.IsNull( npc.ActiveSchedule );
		Assert.AreEqual( schedule.Starts, schedule.Ends );
		Assert.AreEqual( first.Starts, first.Ends );
		Assert.AreEqual( second.Starts, second.Ends );
	}

	[TestMethod]
	public void NewAwarenessLetsAHigherPriorityPreempt()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, idle, first, _) = Running( scene );
		var alert = npc.Schedule<AlertProbeSchedule>();
		alert.Tasks = [new Probe()];

		// Wanting something more important is not enough, the running schedule is only asked again when awareness rises
		npc.Wanted = alert;
		scene.GameTick();
		Assert.AreEqual( idle, npc.ActiveSchedule );

		npc.Sensed = NpcAwareness.HeardDisturbance;
		scene.GameTick();
		Assert.AreEqual( alert, npc.ActiveSchedule );
		Assert.AreEqual( 1, idle.Ends );
		Assert.AreEqual( 1, first.Ends );
		Assert.AreEqual( TaskResult.Failed, first.EndedAs, "A task cut short is not told it succeeded" );

		npc.Wanted = idle;
		npc.Sensed |= NpcAwareness.SeesHostile;
		scene.GameTick();
		Assert.AreEqual( alert, npc.ActiveSchedule );
	}

	[TestMethod]
	public void EndingTheScheduleFromItsOwnCallbacksEndsItOnce()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, schedule, first, second) = Running( scene );
		npc.Wanted = null;

		// The task interrupts from its update and again from its end, and so does the schedule's end
		first.Update = _ =>
		{
			npc.Interrupt();
			return TaskResult.Success;
		};
		first.End = npc.Interrupt;
		schedule.End = npc.Interrupt;

		scene.GameTick();

		Assert.IsNull( npc.ActiveSchedule );
		Assert.AreEqual( 1, first.Ends );
		Assert.AreEqual( 1, schedule.Ends );
		Assert.AreEqual( 0, second.Starts, "The sequence does not carry on after the schedule ended" );

		scene.GameTick();
		Assert.AreEqual( 1, schedule.Ends );
	}

	[TestMethod]
	public void DisablingTheNpcInsideATaskStopsTheSequence()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, schedule, first, second) = Running( scene );
		first.Update = _ =>
		{
			npc.Enabled = false;
			return TaskResult.Success;
		};

		scene.GameTick();
		scene.GameTick();

		Assert.IsNull( npc.ActiveSchedule );
		Assert.AreEqual( 1, first.Ends );
		Assert.AreEqual( 1, schedule.Ends );
		Assert.AreEqual( 0, second.Starts );
		Assert.AreEqual( 1, schedule.Starts, "A disabled NPC starts nothing new" );
	}

	[TestMethod]
	public void AThrowingTaskEndsTheScheduleCleanly()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, schedule, first, second) = Running( scene );
		npc.Wanted = null;
		first.Update = _ => throw new InvalidOperationException( "task" );

		scene.GameTick();

		Assert.IsNull( npc.ActiveSchedule );
		Assert.AreEqual( 1, first.Ends, "The task that threw is still ended" );
		Assert.AreEqual( TaskResult.Failed, first.EndedAs );
		Assert.AreEqual( 1, schedule.Ends );
		Assert.AreEqual( 0, second.Starts );

		var next = npc.Schedule<AlertProbeSchedule>();
		next.Tasks = [new Probe()];
		npc.Wanted = next;
		scene.GameTick();
		Assert.AreEqual( next, npc.ActiveSchedule );
	}

	[TestMethod]
	public void AThrowingEndLeavesNoScheduleActive()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, schedule, first, _) = Running( scene );
		npc.Wanted = null;
		first.End = () => throw new InvalidOperationException( "task end" );
		schedule.End = () => throw new InvalidOperationException( "schedule end" );

		npc.Interrupt();

		Assert.IsNull( npc.ActiveSchedule );
		Assert.AreEqual( 1, first.Ends );
		Assert.AreEqual( 1, schedule.Ends, "The schedule's end still runs when its task's end threw" );

		npc.Interrupt();
		Assert.AreEqual( 1, schedule.Ends );
	}

	[TestMethod]
	public void ATaskThatKillsOrDestroysItsNpcStopsTheSequence()
	{
		foreach ( var how in new[] { "kill", "destroy" } )
		{
			var scene = new Scene();
			using var scope = scene.Push();
			var (npc, schedule, first, second) = Running( scene );
			first.Update = _ =>
			{
				if ( how == "kill" ) NpcTestKit.Hurt( npc, 500 );
				else npc.GameObject.Destroy();

				return TaskResult.Success;
			};

			scene.GameTick();

			Assert.AreEqual( 0, second.Starts, $"No task starts on an NPC that is dead or going ({how})" );
			Assert.AreEqual( 1, first.Ends, how );
			Assert.AreEqual( 1, schedule.Ends, how );
			Assert.AreEqual( 1, schedule.Starts, how );
		}
	}

	[TestMethod]
	public void ADyingNpcEndsItsScheduleAndStartsNoOther()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, schedule, first, _) = Running( scene );

		NpcTestKit.Hurt( npc, 500 );
		scene.GameTick();
		scene.GameTick();

		Assert.AreEqual( 1, schedule.Starts );
		Assert.AreEqual( 1, schedule.Ends );
		Assert.AreEqual( 1, first.Ends );
	}
}

[TestClass]
public class CombatNpcTests
{
	static BaseCombatNpc Spawn( Scene scene )
	{
		var go = scene.CreateObject();
		var animation = go.Components.Create<AnimationLayer>();
		var npc = go.Components.Create<BaseCombatNpc>();
		npc.Animation ??= animation;
		npc.PainLines = ["ow"];
		npc.SpotLines = ["there"];
		npc.PatrolLines = ["quiet"];
		return npc;
	}

	[TestMethod]
	public void WorksWithoutSensesSpeechOrMovement()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var npc = Spawn( scene );
		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		Assert.IsNull( npc.Senses );
		Assert.IsNull( npc.Speech );
		Assert.IsNotNull( npc.ActiveSchedule, "It thinks, and holds its post" );

		// Its reaction to a hit ends the schedule last, after the pain line it cannot say
		NpcTestKit.Hurt( npc, 10, scene.CreateObject() );
		Assert.IsNull( npc.ActiveSchedule );

		npc.IdleBehaviour = BaseCombatNpc.IdleBehaviours.Patrol;
		for ( int i = 0; i < 5; i++ ) scene.GameTick();
		Assert.IsNotNull( npc.ActiveSchedule );

		NpcTestKit.Hurt( npc, 500 );
		scene.GameTick();
		Assert.IsFalse( npc.IsValid() );
	}

	[TestMethod]
	public void ChangingFactionChangesWhoItHates()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var npc = Spawn( scene );
		var other = Spawn( scene );
		var player = scene.CreateObject();
		player.Tags.Add( Factions.Player );
		scene.GameTick();

		Assert.AreEqual( Factions.Enemy, npc.Faction );
		Assert.AreEqual( Disposition.Hostile, npc.GetDisposition( player ) );
		Assert.AreEqual( Disposition.Neutral, npc.GetDisposition( other.GameObject ) );

		npc.Faction = Factions.Ally;
		Assert.AreEqual( Disposition.Friendly, npc.GetDisposition( player ) );
		Assert.AreEqual( Disposition.Hostile, npc.GetDisposition( other.GameObject ) );
		Assert.AreEqual( Disposition.Hostile, other.GetDisposition( npc.GameObject ) );

		npc.Faction = Factions.Citizen;
		Assert.AreEqual( Disposition.Neutral, npc.GetDisposition( player ) );
		Assert.AreEqual( Disposition.Fearful, npc.GetDisposition( other.GameObject ) );

		npc.Faction = Factions.Monster;
		Assert.AreEqual( Disposition.Hostile, npc.GetDisposition( player ) );
		Assert.AreEqual( Disposition.Hostile, npc.GetDisposition( other.GameObject ) );

		npc.Faction = "bystander";
		Assert.AreEqual( Disposition.Neutral, npc.GetDisposition( player ) );

		npc.Faction = Factions.Enemy;
		Assert.AreEqual( Disposition.Hostile, npc.GetDisposition( player ) );
	}
}

[TestClass]
public class NpcLayerTests
{
	public sealed class Hurtable : Component, Component.IDamageable
	{
		public void OnDamage( in DamageInfo damage ) { }
	}

	static GameObject Thing( Scene scene, Vector3 position, bool hurtable = true )
	{
		var go = scene.CreateObject();
		go.WorldPosition = position;
		go.Components.Create<PlayerController>();
		if ( hurtable ) go.Components.Create<Hurtable>();
		return go;
	}

	public sealed class ProbeNpc : BaseNpc
	{
		public int Updates;
		public bool SkipBaseUpdate;
		protected override void OnUpdate()
		{
			Updates++;
			if ( !SkipBaseUpdate ) base.OnUpdate();
		}
	}

	static (ProbeNpc Npc, SensesLayer Senses) Spawn( Scene scene, bool sensingEnabled = true )
	{
		scene.Floor( Vector3.Zero, 800 );

		// The layers go on first, the NPC looks them up when it wakes
		var go = scene.CreateObject();
		var animation = go.Components.Create<AnimationLayer>();
		var senses = go.Components.Create<SensesLayer>( sensingEnabled );
		senses.ScanInterval = 0;
		var npc = go.Components.Create<ProbeNpc>();
		npc.Animation ??= animation;
		scene.GameTick();
		Assert.AreEqual( senses, npc.Senses );
		return (npc, senses);
	}

	static void Scan( Scene scene )
	{
		for ( int i = 0; i < 3; i++ ) scene.GameTick();
	}

	[TestMethod]
	public void OnlyWhatCanBeHurtIsSensed()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, senses) = Spawn( scene );
		var thing = Thing( scene, new Vector3( 200, 0, 0 ), hurtable: false );
		npc.SetRelationship( thing, Disposition.Hostile );

		Assert.IsFalse( BaseNpc.IsLiveTarget( thing ) );
		Scan( scene );
		Assert.IsFalse( senses.VisibleHostiles.Contains( thing ), "Nothing to hurt on it" );
		Assert.IsFalse( senses.AudibleTargets.Contains( thing ) );

		var hurtable = thing.Components.Create<Hurtable>();
		Assert.IsTrue( BaseNpc.IsLiveTarget( thing ) );
		Scan( scene );
		Assert.IsTrue( senses.VisibleHostiles.Contains( thing ) );
		Assert.AreEqual( thing, senses.FindBestTarget() );

		hurtable.Enabled = false;
		Assert.IsFalse( BaseNpc.IsLiveTarget( thing ) );
		Assert.IsNull( senses.FindBestTarget(), "Not a target from this moment, before the next scan" );
		Scan( scene );
		Assert.IsFalse( senses.VisibleHostiles.Contains( thing ) );

		hurtable.Enabled = true;
		thing.Enabled = false;
		Assert.IsFalse( BaseNpc.IsLiveTarget( thing ), "Nor while its GameObject is off" );
	}

	[TestMethod]
	public void SightAndHearingRespectIndependentRangesAndViewCone()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, senses) = Spawn( scene );
		var behind = Thing( scene, new Vector3( -200, 0, 0 ) );
		npc.SetRelationship( behind, Disposition.Hostile );

		Scan( scene );
		Assert.IsFalse( senses.VisibleHostiles.Contains( behind ), "Behind the NPC is outside the default view cone" );
		Assert.IsTrue( senses.AudibleTargets.Contains( behind ), "but inside the default hearing range" );

		senses.FieldOfView = 360;
		senses.HearingRange = 0;
		scene.GetSystem<NpcSystem>().EmitStimulus( "noise", behind.WorldPosition, behind, 500, 100 );
		Scan( scene );
		Assert.IsTrue( senses.VisibleHostiles.Contains( behind ) );
		Assert.IsFalse( senses.AudibleTargets.Contains( behind ) );
		Assert.AreEqual( "noise", senses.Disturbance?.Kind, "Target hearing range leaves world stimuli independent" );

		var neutral = Thing( scene, new Vector3( -100, 0, 0 ) );
		neutral.Tags.Add( "thing" );
		Scan( scene );
		Assert.AreEqual( neutral, senses.FindNearestVisible( "thing" ) );

		senses.FieldOfView = 90;
		senses.HearingRange = 300;
		Scan( scene );
		Assert.IsNull( senses.FindNearestVisible( "thing" ) );
		Assert.IsFalse( senses.VisibleHostiles.Contains( behind ) );
		Assert.IsTrue( senses.AudibleTargets.Contains( behind ), "The view cone leaves hearing independent" );

		senses.FieldOfView = 360;
		senses.SightRange = 150;
		senses.HearingRange = 150;
		Scan( scene );
		Assert.IsFalse( senses.VisibleHostiles.Contains( behind ), "A full view cone still respects sight range" );
		Assert.IsFalse( senses.AudibleTargets.Contains( behind ), "Hearing respects its own range" );
	}

	[TestMethod]
	public void SightRequiresAClearLineOfSight()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, senses) = Spawn( scene );
		var target = Thing( scene, Vector3.Forward * 200 );
		npc.SetRelationship( target, Disposition.Hostile );
		var wall = scene.CreateObject();
		wall.WorldPosition = new Vector3( 100, 0, 100 );
		wall.Components.Create<BoxCollider>().Scale = new Vector3( 20, 200, 200 );

		Scan( scene );
		Assert.IsFalse( senses.VisibleHostiles.Contains( target ) );
		wall.Destroy();
		Scan( scene );
		Assert.IsTrue( senses.VisibleHostiles.Contains( target ) );
	}

	[TestMethod]
	public void FirstEnableWaitsForSensesOnStartBeforeScanning()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (_, senses) = Spawn( scene, sensingEnabled: false );
		var target = Thing( scene, Vector3.Forward * 200 );
		scene.GameTick();
		scene.GetSystem<NpcSystem>().EmitStimulus( "noise", target.WorldPosition, target, 500, 100 );

		senses.Enabled = true;
		Assert.IsFalse( senses.StartCalled );
		scene.GetSystem<NpcSystem>().ScanSenses(); // The central phase runs before pending component starts.
		Assert.IsNull( senses.Disturbance, "A first enable must not scan before OnStart initializes the layer" );

		Scan( scene );
		Assert.IsTrue( senses.StartCalled );
		Assert.AreEqual( "noise", senses.Disturbance?.Kind );
	}

	[TestMethod]
	public void CentralScanningRunsWhenNpcOverridesOnUpdate()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, senses) = Spawn( scene );
		npc.SkipBaseUpdate = true;
		var target = Thing( scene, Vector3.Forward * 200 );
		npc.SetRelationship( target, Disposition.Hostile );
		var updates = npc.Updates;

		Scan( scene );
		Assert.IsTrue( npc.Updates > updates );
		Assert.IsTrue( senses.VisibleHostiles.Contains( target ) );

		target.WorldPosition = Vector3.Backward * 200;
		Scan( scene );
		Assert.IsFalse( senses.VisibleHostiles.Contains( target ), "The central scan refreshes after the NPC's own update stops calling base" );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void DisablingSensingOrItsNpcImmediatelyClearsResultsAndResumesWhenEnabled( bool disableNpc )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, senses) = Spawn( scene );
		senses.QuietSpeed = 0;
		var hostile = Thing( scene, new Vector3( 200, -120, 0 ) );
		var threat = Thing( scene, new Vector3( 250, 120, 0 ) );
		var neutral = Thing( scene, new Vector3( 150, 0, 0 ) );
		neutral.Tags.Add( "neutral" );
		npc.SetRelationship( hostile, Disposition.Hostile );
		npc.SetRelationship( threat, Disposition.Fearful );
		scene.GetSystem<NpcSystem>().EmitStimulus( "noise", hostile.WorldPosition, hostile, 500, 100 );
		Scan( scene );
		Assert.IsTrue( senses.VisibleHostiles.Contains( hostile ), "The hostile has an unobstructed sight line" );
		Assert.IsTrue( senses.AudibleTargets.Contains( hostile ), "The hostile is inside hearing range" );
		Assert.IsTrue( senses.VisibleThreats.Contains( threat ), "The feared target has its own unobstructed sight line" );
		Assert.AreEqual( hostile, senses.GetHeardHostile() );
		Assert.AreEqual( "noise", senses.Disturbance?.Kind );

		if ( disableNpc ) npc.Enabled = false;
		else senses.Enabled = false;
		Assert.AreEqual( 0, senses.VisibleHostiles.Count );
		Assert.AreEqual( 0, senses.AudibleTargets.Count );
		Assert.AreEqual( 0, senses.VisibleThreats.Count );
		Assert.IsNull( senses.FindBestTarget() );
		Assert.IsNull( senses.FindNearestVisible( "neutral" ), "Pending lazy sight candidates are cleared too" );
		Assert.IsNull( senses.GetHeardHostile() );
		Assert.IsNull( senses.GetPushingPlayer() );
		Assert.IsNull( senses.Disturbance );
		Scan( scene );
		Assert.AreEqual( 0, senses.VisibleHostiles.Count );
		Assert.IsNull( senses.Disturbance );

		if ( disableNpc ) npc.Enabled = true;
		else senses.Enabled = true;
		Scan( scene );
		Assert.IsTrue( senses.VisibleHostiles.Contains( hostile ) );
		Assert.AreEqual( "noise", senses.Disturbance?.Kind );
	}

	[TestMethod]
	public void AMoverPublishesItsOwnGroundedState()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, _) = Spawn( scene );
		Assert.IsTrue( npc.Animation.IsGrounded );

		// No mover that uses the ground, so the NPC leaves the state to whoever moves it
		npc.Animation.IsGrounded = false;
		var climb = NavigationTestScene.ShippedLinkType( "climb" );
		npc.Animation.Traversal = climb;
		npc.Animation.TraversalDirection = 1;
		Scan( scene );

		Assert.IsFalse( npc.Animation.IsGrounded );
		Assert.AreEqual( climb, npc.Animation.Traversal );
		Assert.AreEqual( 1, npc.Animation.TraversalDirection );
	}

	[TestMethod]
	public void BodyTurningCanBeSwitchedOff()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, _) = Spawn( scene );
		npc.Renderer = npc.GameObject.Components.Create<SkinnedModelRenderer>();
		npc.Animation.SetLookTarget( new Vector3( -200, 50, 0 ) );

		npc.Animation.TurnsBody = false;
		for ( int i = 0; i < 5; i++ ) scene.GameTick();
		Assert.AreEqual( 0f, npc.WorldRotation.Yaw(), 0.01f, "The layer leaves the body alone" );
		Assert.IsTrue( npc.Animation.IsLooking, "while the look target still stands" );

		npc.Animation.TurnsBody = true;
		for ( int i = 0; i < 5; i++ ) scene.GameTick();
		Assert.IsTrue( MathF.Abs( npc.WorldRotation.Yaw() ) > 10f, "By default it turns the body toward the look target" );
	}
}

/// <summary>
/// The flying layer: speed is gained and lost at its acceleration, a target that jumps bends the flight instead
/// of stopping it, and a knock is flown off, not teleported.
/// </summary>
[TestClass]
public class NpcFlyingTests
{
	const float Tick = 0.02f;

	static (NpcTestKit.TestNpc Npc, FlyingLayer Flying) Spawn( Scene scene )
	{
		// The layers go on first, the NPC looks them up when it wakes
		var go = scene.CreateObject();
		var animation = go.Components.Create<AnimationLayer>();
		var flying = go.Components.Create<FlyingLayer>();
		var npc = go.Components.Create<NpcTestKit.TestNpc>();
		npc.Animation ??= animation;
		flying.HoverHeight = 0;
		flying.Acceleration = 400;
		scene.GameTick( Tick );
		Assert.AreEqual( flying, npc.Movement );
		return (npc, flying);
	}

	static float Step( Scene scene, BaseNpc npc )
	{
		var from = npc.WorldPosition;
		scene.GameTick( Tick );
		return npc.WorldPosition.Distance( from ) / Tick;
	}

	[TestMethod]
	public void SpeedIsGainedAndLostAtTheAcceleration()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, flying) = Spawn( scene );
		var target = new Vector3( 600, 0, 0 );
		flying.MoveTo( target, 10f, 200f );

		var most = flying.Acceleration * Tick + 0.5f;
		var last = 0f;
		var top = 0f;
		var arrivedAfter = -1;

		for ( int i = 0; i < 600 && arrivedAfter < 0; i++ )
		{
			var speed = Step( scene, npc );
			Assert.IsTrue( MathF.Abs( speed - last ) <= most, $"tick {i}: speed went from {last} to {speed}" );
			last = speed;
			top = MathF.Max( top, speed );

			if ( npc.WorldPosition.Distance( target ) <= 10f )
				arrivedAfter = i;
		}

		Assert.IsTrue( arrivedAfter > 0, "It gets there" );
		Assert.AreEqual( 200f, top, 1f, "and reaches its speed on the way" );
		Assert.IsTrue( last < 60f, $"It is slow by the time it arrives, not at full speed ({last})" );
		Assert.AreEqual( MovementLayer.MovementStatus.Arrived, flying.Status );

		for ( int i = 0; i < 50; i++ ) last = Step( scene, npc );
		Assert.AreEqual( 0f, last, 0.5f, "Then it hovers" );
		Assert.IsTrue( npc.WorldPosition.Distance( target ) <= 10f );
	}

	[TestMethod]
	public void ATargetThatJumpsBendsTheFlight()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, flying) = Spawn( scene );
		var most = flying.Acceleration * Tick + 0.5f;
		var last = 0f;

		for ( int i = 0; i < 100; i++ )
		{
			// Move the goal sideways every few ticks.
			var side = (i / 10) % 2 == 0 ? 40f : -40f;
			flying.MoveTo( new Vector3( 2000, side, 0 ), 10f, 200f );

			var speed = Step( scene, npc );
			Assert.IsTrue( MathF.Abs( speed - last ) <= most, $"tick {i}: speed went from {last} to {speed}" );
			if ( i > 40 ) Assert.IsTrue( speed > 150f, $"tick {i}: it keeps flying ({speed})" );
			last = speed;
		}
	}

	[TestMethod]
	public void AKnockIsFlownOff()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, flying) = Spawn( scene );
		var home = npc.WorldPosition;
		flying.MoveTo( home, 5f, 200f );
		for ( int i = 0; i < 5; i++ ) Step( scene, npc );

		flying.Velocity = new Vector3( -200, 0, 0 );

		var furthest = 0f;
		var fastest = 0f;
		for ( int i = 0; i < 150; i++ )
		{
			fastest = MathF.Max( fastest, Step( scene, npc ) );
			furthest = MathF.Max( furthest, npc.WorldPosition.Distance( home ) );
		}

		Assert.IsTrue( fastest <= 200.5f, $"No frame moves it faster than the knock ({fastest})" );
		Assert.IsTrue( furthest > 20f && furthest < 120f, $"It is carried off a way ({furthest})" );
		Assert.IsTrue( npc.WorldPosition.Distance( home ) <= 5f, "and flies back" );
	}

	[TestMethod]
	public void AFlyerFacesTheWayItFliesAndKeepsItsFacingWhenItHovers()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (npc, flying) = Spawn( scene );
		flying.MoveTo( new Vector3( 0, 400, 0 ), 10f, 200f );
		for ( int i = 0; i < 200; i++ ) Step( scene, npc );

		var yaw = npc.WorldRotation.Yaw();
		Assert.AreEqual( 90f, yaw, 5f, "It turned to face its flight" );

		// A goal a little behind it, inside the stop distance: nothing to turn for
		flying.MoveTo( npc.WorldPosition + new Vector3( 0, -4, 0 ), 10f, 200f );
		for ( int i = 0; i < 50; i++ ) Step( scene, npc );
		Assert.AreEqual( yaw, npc.WorldRotation.Yaw(), 0.5f );
	}
}

/// <summary>
/// Ladder and climb links are crossed on the link's own line, wherever inside its reach the NPC walked onto it.
/// </summary>
[TestClass]
public class NpcClimbTests
{
	const float Tick = 0.02f;

	[TestMethod]
	[DataRow( "ladder", 0f )]
	[DataRow( "climb", 0f )]
	[DataRow( "ladder", 89f )]
	[DataRow( "climb", 89f )]
	public async Task AClimbFollowsTheLinkLine( string name, float pitch )
	{
		var type = NavigationTestScene.ShippedLinkType( name );

		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		scene.Floor( new Vector3( 0, 0, 200 ) );

		var foot = new Vector3( 100, 0, 0 );
		var link = scene.CreateObject().Components.Create<NavMeshLink>( false );
		link.WorldRotation = Rotation.FromPitch( pitch );
		link.WorldStartPosition = foot;
		link.WorldEndPosition = new Vector3( 50, 0, 200 );
		link.Type = type;
		link.Enabled = true;
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );

		// From the side, so it walks onto the link away from the line
		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 150, 0 );
		go.Components.Create<NavMeshAgent>();
		go.Components.Create<AnimationLayer>();
		var navigation = go.Components.Create<NavigationLayer>();
		var npc = go.Components.Create<NpcTestKit.TestNpc>();
		scene.GameTick( Tick );
		Assert.AreEqual( navigation, npc.Movement );

		Cross( scene, npc, navigation, foot, new Vector3( -100, 0, 200 ), type, "up" );
		Assert.AreEqual( 200f, npc.WorldPosition.z, 4f, "It is on the upper floor" );

		Cross( scene, npc, navigation, foot, new Vector3( 100, 150, 0 ), type, "down" );
		Assert.AreEqual( 0f, npc.WorldPosition.z, 4f, "and back on the lower one" );
	}

	static void Cross( Scene scene, BaseNpc npc, NavigationLayer navigation, Vector3 foot, Vector3 goal, NavMeshLinkType type, string way )
	{
		navigation.MoveTo( goal );

		var most = MathF.Max( navigation.WalkSpeed, MathF.Max( navigation.LadderSpeed, navigation.ClimbSpeed ) ) * Tick + 0.5f;
		var entered = -1f;
		var vertical = 0;
		var crossed = false;

		for ( int i = 0; i < 2000 && !(crossed && !navigation.IsTraversingLink); i++ )
		{
			var from = npc.WorldPosition;
			var was = navigation.IsTraversingLink;
			scene.GameTick( Tick );
			if ( navigation.LinkTraversal is not { } crossing ) continue;

			crossed = true;
			Assert.AreEqual( type, crossing.Type );

			var offset = (npc.WorldPosition - foot).WithZ( 0 ).Length;
			if ( entered < 0 ) entered = (from - foot).WithZ( 0 ).Length;
			if ( was ) Assert.IsTrue( npc.WorldPosition.Distance( from ) <= most, $"{way}, tick {i}: moved {npc.WorldPosition.Distance( from )} in one tick" );

			if ( npc.WorldPosition.z > 8 && npc.WorldPosition.z < 192 )
			{
				vertical++;
				Assert.IsTrue( offset <= 0.5f, $"{way}, tick {i}: {offset} from the link's line at height {npc.WorldPosition.z}" );
			}
		}

		Assert.IsTrue( crossed, $"{way}: the route goes over the link" );
		Assert.IsTrue( vertical > 20, $"{way}: it spent {vertical} ticks on the vertical part" );
		if ( way == "up" ) Assert.IsTrue( entered > 4f, $"It walked onto the link {entered} from its line, or this tests nothing" );
	}
}

/// <summary>
/// A flyer keeps to the air or to water volumes when its bounds say so.
/// </summary>
[TestClass]
public class NpcFlyingBoundsTests
{
	const float Tick = 0.02f;

	[TestMethod]
	public void BoundsKeepItToAirOrWater()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.CreateObject().Components.Create<WaterBody>();
		scene.GameTick( Tick );

		if ( !WaterBody.Find( scene, new Vector3( 0, 0, -150 ) ).IsValid() || WaterBody.Find( scene, new Vector3( 0, 0, 150 ) ).IsValid() )
			Assert.Inconclusive( "The water volume is not there in this test run" );

		Assert.IsTrue( Fly( scene, FlyingLayer.FlyingBounds.Anywhere, 150, -150 ) < -140f, "Anywhere flies into the water" );
		Assert.IsTrue( Fly( scene, FlyingLayer.FlyingBounds.Anywhere, -150, 150 ) > 140f, "and out of it" );
		Assert.IsTrue( Fly( scene, FlyingLayer.FlyingBounds.AirOnly, 150, -150 ) > -40f, "AirOnly stops at the surface" );
		Assert.IsTrue( Fly( scene, FlyingLayer.FlyingBounds.WaterOnly, -150, 150 ) < 40f, "WaterOnly stops below it" );
	}

	// Where a flyer sent straight up or down from one height to another ends up
	static float Fly( Scene scene, FlyingLayer.FlyingBounds bounds, float from, float to )
	{
		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 0, 0, from );
		var animation = go.Components.Create<AnimationLayer>();
		var flying = go.Components.Create<FlyingLayer>();
		var npc = go.Components.Create<NpcTestKit.TestNpc>();
		npc.Animation ??= animation;
		flying.HoverHeight = 0;
		flying.Bounds = bounds;
		scene.GameTick( Tick );

		flying.MoveTo( new Vector3( 0, 0, to ), 5f, 200f );
		for ( int i = 0; i < 200; i++ ) scene.GameTick( Tick );

		var height = go.WorldPosition.z;
		go.Destroy();
		scene.GameTick( Tick );
		return height;
	}
}

/// <summary>
/// One NPC with two movers: it walks the navmesh, is handed to the flyer, flies, is handed back and walks again.
/// </summary>
[TestClass]
public class NpcMoverSwitchTests
{
	const float Tick = 0.02f;

	[TestMethod]
	public void StartingWithNavigationDisabledReleasesTheAgent()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var go = scene.CreateObject();
		var walking = go.Components.Create<NavigationLayer>( false );
		var agent = go.Components.Create<NavMeshAgent>();
		var animation = go.Components.Create<AnimationLayer>();
		var flying = go.Components.Create<FlyingLayer>();
		var npc = go.Components.Create<NpcTestKit.TestNpc>();
		npc.Animation ??= animation;
		scene.GameTick( Tick );

		Assert.AreEqual( flying, npc.Movement );
		Assert.IsFalse( walking.Enabled );
		Assert.IsFalse( agent.Enabled, "An initially disabled walker must let the flyer control the body" );
		Assert.IsFalse( agent.AutoTraverseLinks, "The disabled walker still initializes its agent" );
	}

	[TestMethod]
	public async Task AnNpcWalksFliesAndWalksAgain()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 800 );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );

		var go = scene.CreateObject();
		var agent = go.Components.Create<NavMeshAgent>();
		var animation = go.Components.Create<AnimationLayer>();
		var walking = go.Components.Create<NavigationLayer>();
		var flying = go.Components.Create<FlyingLayer>( false );
		flying.HoverHeight = 0;

		// A body for physics to drop when the walker gets it back in the air
		var body = go.Components.Create<Rigidbody>();
		body.MotionEnabled = false;
		var npc = go.Components.Create<NpcTestKit.TestNpc>();
		npc.Animation ??= animation;
		for ( int i = 0; i < 5; i++ ) scene.GameTick( Tick );

		Assert.AreEqual( walking, npc.Movement, "The enabled mover is the one in use" );
		Assert.IsTrue( npc.IsOnGround );

		walking.MoveTo( new Vector3( 200, 0, 0 ) );
		Run( scene, () => npc.WorldPosition.Distance( new Vector3( 200, 0, 0 ) ) < 30f, "walked there" );

		npc.SetMovement( flying );
		Assert.AreEqual( flying, npc.Movement );
		Assert.IsFalse( walking.Enabled );
		Assert.IsFalse( agent.Enabled, "The navmesh agent lets go" );
		Assert.IsNull( walking.MoveTarget );

		flying.MoveTo( new Vector3( 200, 0, 150 ), 5f, 200f );
		Run( scene, () => npc.WorldPosition.Distance( new Vector3( 200, 0, 150 ) ) < 8f, "flew up" );
		Assert.IsFalse( npc.IsOnGround );
		Assert.IsFalse( npc.IsAirborne, "Flying is not falling" );
		Assert.IsFalse( animation.IsGrounded );

		flying.MoveTo( new Vector3( 300, 100, 60 ), 5f, 200f );
		Run( scene, () => npc.WorldPosition.Distance( new Vector3( 300, 100, 60 ) ) < 10f, "flew on" );

		// Handed back in the air: it falls, lands and is on the navmesh again
		npc.SetMovement( walking );
		Assert.AreEqual( walking, npc.Movement );
		Assert.IsFalse( flying.Enabled );
		Assert.IsTrue( npc.IsAirborne );
		Assert.IsTrue( body.MotionEnabled && body.Gravity, "Physics has it, with its weight back" );

		// The body has no shape to fall with here, so it is set down by hand
		go.WorldPosition = new Vector3( 300, 100, 2 );
		Run( scene, () => npc.IsOnGround && agent.Enabled, "landed on the navmesh" );
		Assert.IsFalse( body.MotionEnabled );
		Assert.IsTrue( animation.IsGrounded );

		walking.MoveTo( new Vector3( 0, 100, 0 ) );
		Run( scene, () => npc.WorldPosition.Distance( new Vector3( 0, 100, 0 ) ) < 30f, "walked again" );
		Assert.AreNotEqual( MovementLayer.MovementStatus.Failed, walking.Status );
	}

	[TestMethod]
	public void MoversEnabledAndDisabledByHandAreFollowed()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var go = scene.CreateObject();
		var animation = go.Components.Create<AnimationLayer>();
		var first = go.Components.Create<FlyingLayer>();
		var second = go.Components.Create<FlyingLayer>( false );
		var npc = go.Components.Create<NpcTestKit.TestNpc>();
		npc.Animation ??= animation;
		scene.GameTick( Tick );
		Assert.AreEqual( first, npc.Movement );

		first.Enabled = false;
		second.Enabled = true;
		scene.GameTick( Tick );
		Assert.AreEqual( second, npc.Movement, "By the next update" );

		// With none enabled it keeps the last one, which does nothing
		second.Enabled = false;
		scene.GameTick( Tick );
		Assert.AreEqual( second, npc.Movement );
	}

	static void Run( Scene scene, Func<bool> done, string what )
	{
		for ( int i = 0; i < 1500; i++ )
		{
			if ( done() ) return;
			scene.GameTick( Tick );
		}

		Assert.Fail( $"It never {what}" );
	}
}

[TestClass]
public class NpcCarryTests
{
	[TestMethod]
	public void CarryingTellsTheNpcOnceEachWay()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var go = scene.CreateObject();
		var animation = go.Components.Create<AnimationLayer>();
		var npc = go.Components.Create<NpcTestKit.TestNpc>();
		npc.Animation ??= animation;
		scene.GameTick();

		var first = scene.CreateObject();
		first.Name = "first";
		var second = scene.CreateObject();
		second.Name = "second";

		npc.StartCarry( first );
		npc.StartCarry( first );
		Assert.AreEqual( first, npc.PhysicsCarrier );
		CollectionAssert.AreEqual( new[] { "carried by first" }, npc.Log );

		// Someone else lets go of nothing, and a new carrier takes it from the last
		npc.StopCarry( second );
		npc.StartCarry( second );
		CollectionAssert.AreEqual( new[] { "carried by first", "let go by first", "carried by second" }, npc.Log );

		npc.StopCarry( second );
		npc.StopCarry( second );
		Assert.IsFalse( npc.PhysicsCarrier.IsValid() );
		CollectionAssert.AreEqual( new[] { "carried by first", "let go by first", "carried by second", "let go by second" }, npc.Log );
	}
}

/// <summary>
/// Speech options: a line said with none gets the documented defaults, priority decides who talks over whom, and
/// a tag cooldown holds back the lines that carry the tag.
/// </summary>
[TestClass]
public class NpcSpeechTests
{
	static SpeechLayer Spawn( Scene scene, Vector3 position )
	{
		var go = scene.CreateObject();
		go.WorldPosition = position;
		var animation = go.Components.Create<AnimationLayer>();
		var speech = go.Components.Create<SpeechLayer>();
		var npc = go.Components.Create<NpcTestKit.TestNpc>();
		npc.Animation ??= animation;
		return speech;
	}

	[TestMethod]
	public void OptionsLeftUnsetHaveTheirDefaults()
	{
		Assert.AreEqual( 1000, default( SpeechOptions ).Priority );
		Assert.AreEqual( 1500f, default( SpeechOptions ).Radius );
		Assert.IsNull( default( SpeechOptions ).Duration );

		Assert.AreEqual( 1000, new SpeechOptions { Duration = 2f }.Priority );
		Assert.AreEqual( 5, new SpeechOptions { Priority = 5 }.Priority );
		Assert.AreEqual( 0f, new SpeechOptions { Radius = 0f }.Radius );
	}

	[TestMethod]
	public void PriorityAndTagCooldownsDecideWhoSpeaks()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var first = Spawn( scene, Vector3.Zero );
		var second = Spawn( scene, new Vector3( 100, 0, 0 ) );
		scene.GameTick();

		Assert.IsTrue( first.TrySay( "hello" ), "A line with no options is said" );
		Assert.AreEqual( "hello", first.CurrentSpeech );

		// Someone nearby talks at the default priority: a lower one waits, a higher one talks over it
		Assert.IsFalse( second.TrySay( "quiet", new SpeechOptions { Priority = 500 } ) );
		Assert.IsTrue( second.TrySay( "loud", new SpeechOptions { Priority = 2000 } ) );
		Assert.IsNull( first.CurrentSpeech );
		Assert.AreEqual( "loud", second.CurrentSpeech );
		second.Stop();

		// A tag cooldown holds back the lines that carry the tag, not the others
		scene.GetSystem<NpcSystem>().SetTagCooldown( Vector3.Zero, "greeting", 5f );
		Assert.IsFalse( first.TrySay( "hi", new SpeechOptions { Tags = ["greeting"] } ) );
		Assert.IsTrue( first.TrySay( "watch out", new SpeechOptions { Tags = ["warning"] } ) );
	}
}

[TestClass]
public class NpcEnumTests
{
	[TestMethod]
	public void NestedEnumsAreSentByTheNetworkSerializer()
	{
		// The path a synced property takes: a type the serializer does not know throws here
		Assert.AreEqual( AnimationLayer.WeaponStances.Lowered, Game.TypeLibrary.FromBytes<AnimationLayer.WeaponStances>( Game.TypeLibrary.ToBytes( AnimationLayer.WeaponStances.Lowered ) ) );
		Assert.AreEqual( BaseCombatNpc.IdleBehaviours.Patrol, Game.TypeLibrary.FromBytes<BaseCombatNpc.IdleBehaviours>( Game.TypeLibrary.ToBytes( BaseCombatNpc.IdleBehaviours.Patrol ) ) );
		Assert.AreEqual( FlyingLayer.FlyingBounds.WaterOnly, Game.TypeLibrary.FromBytes<FlyingLayer.FlyingBounds>( Game.TypeLibrary.ToBytes( FlyingLayer.FlyingBounds.WaterOnly ) ) );
		Assert.AreEqual( SensesLayer.SenseTargets.Npcs, Game.TypeLibrary.FromBytes<SensesLayer.SenseTargets>( Game.TypeLibrary.ToBytes( SensesLayer.SenseTargets.Npcs ) ) );
		Assert.AreEqual( MovementLayer.MovementStatus.Arrived, Game.TypeLibrary.FromBytes<MovementLayer.MovementStatus>( Game.TypeLibrary.ToBytes( MovementLayer.MovementStatus.Arrived ) ) );
	}

	[TestMethod]
	public void TheLinkTypeBeingCrossedIsSentAsAReference()
	{
		var ladder = NavigationTestScene.ShippedLinkType( "ladder" );
		Assert.AreSame( ladder, NavMeshLinkType.Ladder );
		Assert.AreSame( ladder, Game.TypeLibrary.FromBytes<NavMeshLinkType>( Game.TypeLibrary.ToBytes( ladder ) ) );
		Assert.IsNull( Game.TypeLibrary.FromBytes<NavMeshLinkType>( Game.TypeLibrary.ToBytes<NavMeshLinkType>( null ) ) );
	}

	[TestMethod]
	public void NestedEnumPropertiesSurviveASave()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var go = scene.CreateObject();
		go.Components.Create<AnimationLayer>();
		go.Components.Create<FlyingLayer>().Bounds = FlyingLayer.FlyingBounds.WaterOnly;
		var npc = go.Components.Create<BaseCombatNpc>();
		go.Components.Create<SensesLayer>().Targets = SensesLayer.SenseTargets.Npcs;
		npc.IdleBehaviour = BaseCombatNpc.IdleBehaviours.Patrol;

		var json = go.Serialize().ToJsonString();
		StringAssert.Contains( json, "\"IdleBehaviour\": \"Patrol\"".Replace( ": ", ":" ) );

		var copy = scene.CreateObject();
		copy.Deserialize( System.Text.Json.Nodes.JsonNode.Parse( json ).AsObject() );

		Assert.AreEqual( BaseCombatNpc.IdleBehaviours.Patrol, copy.GetComponent<BaseCombatNpc>( true ).IdleBehaviour );
		Assert.AreEqual( FlyingLayer.FlyingBounds.WaterOnly, copy.GetComponent<FlyingLayer>( true ).Bounds );
		Assert.AreEqual( SensesLayer.SenseTargets.Npcs, copy.GetComponent<SensesLayer>( true ).Targets );
	}
}
