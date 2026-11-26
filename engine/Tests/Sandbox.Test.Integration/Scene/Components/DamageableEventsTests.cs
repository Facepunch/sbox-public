using System;
using System.Collections.Generic;

namespace SceneTests.Components;

[TestClass]
public class DamageableEventsTests
{
	public sealed class Crate : Component, Component.IDamageable
	{
		public float Health = 100;

		public void OnDamage( in DamageInfo damage )
		{
			var e = new IDamageable.DamageEvent { Victim = GameObject, DamageInfo = damage, Instigator = damage.Attacker, AcceptedDamage = damage.Damage };
			Scene.RunEvent<IDamageable.IEvents>( x => x.OnDamaging( e ) );
			if ( e.Cancelled )
				return;

			// Read damage before publishing the settled event.
			var amount = e.AcceptedDamage;
			var attacker = e.Instigator;
			Health -= amount;
			Scene.RunEvent<IDamageable.IEvents>( x => x.OnDamaged( e ) );

			if ( Health > 0 )
				return;

			var death = new IDamageable.DeathEvent { Victim = GameObject, DamageInfo = damage, Instigator = attacker };
			Scene.RunEvent<IDamageable.IEvents>( x => x.OnDied( death ) );
		}
	}

	public sealed class SilentCrate : Component, Component.IDamageable
	{
		public float Health = 100;

		public void OnDamage( in DamageInfo damage ) => Health -= damage.Damage;
	}

	// A health others can read but not set: only the getter binds to the interface
	public sealed class GuardedCrate : Component, Component.IDamageable
	{
		public float Health { get; private set; } = 50;
		public float MaxHealth => 50;

		public void OnDamage( in DamageInfo damage ) => Health -= damage.Damage;
	}

	public sealed class ArmouredCrate : Component, Component.IDamageable
	{
		public float Armour { get; set; } = 10;
		public float MaxArmour => 100;

		public void OnDamage( in DamageInfo damage ) => Armour -= damage.Damage;
	}

	public sealed class Rules : Component, Component.IDamageable.IEvents
	{
		public List<string> Log = new();
		public Action<IDamageable.DamageEvent> Damaging;
		public IDamageable.DamageEvent Damaged;
		public IDamageable.DeathEvent Died;

		void IDamageable.IEvents.OnDamaging( IDamageable.DamageEvent e )
		{
			Log.Add( "damaging" );
			Damaging?.Invoke( e );
		}

		void IDamageable.IEvents.OnDamaged( IDamageable.DamageEvent e )
		{
			Log.Add( "damaged" );
			Damaged = e;
		}

		void IDamageable.IEvents.OnDied( IDamageable.DeathEvent e )
		{
			Log.Add( "died" );
			Died = e;
		}
	}

	[TestMethod]
	public void AListenerHearsChangesAndCancelsTheHitsOfAnyDamageable()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var crate = scene.CreateObject().Components.Create<Crate>();
		var rules = scene.CreateObject().Components.Create<Rules>();
		var attacker = scene.CreateObject();
		var blamed = scene.CreateObject();

		((Component.IDamageable)crate).OnDamage( new DamageInfo( 20, attacker, null ) );
		CollectionAssert.AreEqual( new[] { "damaging", "damaged" }, rules.Log );
		Assert.AreEqual( 80, crate.Health );
		Assert.AreEqual( crate.GameObject, rules.Damaged.Victim );
		Assert.AreEqual( attacker, rules.Damaged.Instigator );
		Assert.AreEqual( 20, rules.Damaged.AcceptedDamage );

		rules.Damaging = e =>
		{
			e.AcceptedDamage *= 0.5f;
			e.Instigator = blamed;
		};
		((Component.IDamageable)crate).OnDamage( new DamageInfo( 40, attacker, null ) );
		Assert.AreEqual( 60, crate.Health );
		Assert.AreEqual( 20, rules.Damaged.AcceptedDamage );
		Assert.AreEqual( 40, rules.Damaged.DamageInfo.Damage );
		Assert.AreEqual( blamed, rules.Damaged.Instigator );

		rules.Log.Clear();
		rules.Damaging = e => e.Cancelled = true;
		((Component.IDamageable)crate).OnDamage( new DamageInfo( 500, attacker, null ) );
		CollectionAssert.AreEqual( new[] { "damaging" }, rules.Log );
		Assert.AreEqual( 60, crate.Health );

		rules.Log.Clear();
		rules.Damaging = e => e.Instigator = blamed;
		((Component.IDamageable)crate).OnDamage( new DamageInfo( 500, attacker, null ) );
		CollectionAssert.AreEqual( new[] { "damaging", "damaged", "died" }, rules.Log );
		Assert.AreEqual( crate.GameObject, rules.Died.Victim );
		Assert.AreEqual( blamed, rules.Died.Instigator );
		Assert.IsNull( rules.Died.Ragdoll );
	}

	[TestMethod]
	public void ADamageableThatPublishesNothingIsNotHeard()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var crate = scene.CreateObject().Components.Create<SilentCrate>();
		var rules = scene.CreateObject().Components.Create<Rules>();

		((Component.IDamageable)crate).OnDamage( new DamageInfo( 500, null, null ) );

		Assert.AreEqual( -400, crate.Health );
		Assert.AreEqual( 0, rules.Log.Count );
	}

	[TestMethod]
	public void HealthAnswersThroughTheInterfaceOnlyWhereItBinds()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		// A prop has a health and no maximum, so none that others may restore
		var prop = scene.CreateObject().Components.Create<Prop>();
		prop.Health = 30;
		Component.IDamageable damageable = prop;
		Assert.AreEqual( 30, damageable.Health );
		Assert.AreEqual( 0, damageable.MaxHealth );

		damageable = scene.CreateObject().Components.Create<BaseChair>();
		Assert.AreEqual( 0, damageable.Health );
		Assert.AreEqual( 0, damageable.MaxHealth );

		// A field does not bind: the defaults answer and a write goes nowhere
		var crate = scene.CreateObject().Components.Create<Crate>();
		damageable = crate;
		damageable.Health = 5;
		Assert.AreEqual( 0, damageable.Health );
		Assert.AreEqual( 0, damageable.MaxHealth );
		Assert.AreEqual( 100, crate.Health );

		// A private setter binds the read and drops the write
		damageable = scene.CreateObject().Components.Create<GuardedCrate>();
		damageable.Health = 5;
		Assert.AreEqual( 50, damageable.Health );
		Assert.AreEqual( 50, damageable.MaxHealth );
	}

	[TestMethod]
	public void ArmourAnswersThroughTheInterfaceOnlyWhereItBinds()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var suit = scene.CreateObject().Components.Create<ArmouredCrate>();
		Component.IDamageable damageable = suit;
		Assert.AreEqual( 10, damageable.Armour );
		Assert.AreEqual( 100, damageable.MaxArmour );
		damageable.Armour = 60;
		Assert.AreEqual( 60, suit.Armour );

		// Without armour the defaults answer and a write goes nowhere
		damageable = scene.CreateObject().Components.Create<GuardedCrate>();
		damageable.Armour = 5;
		Assert.AreEqual( 0, damageable.Armour );
		Assert.AreEqual( 0, damageable.MaxArmour );
	}
}
