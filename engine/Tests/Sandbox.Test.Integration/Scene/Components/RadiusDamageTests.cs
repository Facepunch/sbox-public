using System;
using System.Collections.Generic;

namespace SceneTests.Components;

[TestClass]
public class RadiusDamageTests
{
	public sealed class Victim : Component, Component.IDamageable
	{
		public int Hits;
		public float Amount;
		public Action Callback;

		public void OnDamage( in DamageInfo damage )
		{
			Hits++;
			Amount = damage.Damage;
			Callback?.Invoke();
		}
	}

	static Victim CreateVictim( Scene scene, Vector3 position )
	{
		var go = scene.CreateObject();
		go.WorldPosition = position;
		// Exclude the victims themselves from occlusion without excluding them from the query.
		go.Tags.Add( "gib" );
		var box = go.Components.Create<BoxCollider>();
		box.Static = true;
		box.Scale = new Vector3( 4 );
		return go.Components.Create<Victim>();
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void NestedBlastsKeepIndependentTargetsAndVisibility( bool occlusion )
	{
		using var scope = new Scene().Push();
		var scene = Game.ActiveScene;
		var first = CreateVictim( scene, new Vector3( 10, 0, 0 ) );
		var second = CreateVictim( scene, new Vector3( 20, 0, 0 ) );
		var third = CreateVictim( scene, new Vector3( 30, 0, 0 ) );
		var inner = CreateVictim( scene, new Vector3( 1010, 0, 0 ) );
		var outerDamage = new DamageInfo { Damage = 100 };
		var innerDamage = new DamageInfo { Damage = 40 };
		first.Callback = () => RadiusDamage.ApplyDamage( new Sphere( new Vector3( 1000, 0, 0 ), 100 ), innerDamage, occlusion: occlusion );

		RadiusDamage.ApplyDamage( new Sphere( Vector3.Zero, 100 ), outerDamage, occlusion: occlusion );

		Assert.AreEqual( 1, first.Hits );
		Assert.AreEqual( 1, second.Hits );
		Assert.AreEqual( 1, third.Hits );
		Assert.AreEqual( 1, inner.Hits );
		Assert.AreEqual( 90f, first.Amount, 0.01f );
		Assert.AreEqual( 80f, second.Amount, 0.01f );
		Assert.AreEqual( 70f, third.Amount, 0.01f );
		Assert.AreEqual( 36f, inner.Amount, 0.01f );
		Assert.AreEqual( 100f, outerDamage.Damage );
		Assert.AreEqual( 40f, innerDamage.Damage );
	}

	[TestMethod]
	public void CallbackExceptionDoesNotLeakTargetsIntoLaterBlasts()
	{
		using var scope = new Scene().Push();
		var scene = Game.ActiveScene;
		var victim = CreateVictim( scene, new Vector3( 10, 0, 0 ) );
		victim.Callback = () => throw new InvalidOperationException( "damage callback" );
		Assert.ThrowsException<InvalidOperationException>( () => RadiusDamage.ApplyDamage( new Sphere( Vector3.Zero, 100 ), new DamageInfo { Damage = 100 } ) );
		victim.Callback = null;

		// Exercise both an early return and a subsequent populated query after the exception.
		RadiusDamage.ApplyDamage( new Sphere( new Vector3( 2000, 0, 0 ), 100 ), new DamageInfo { Damage = 100 } );
		var other = CreateVictim( scene, new Vector3( 1010, 0, 0 ) );
		for ( var i = 0; i < 8; i++ )
			RadiusDamage.ApplyDamage( new Sphere( new Vector3( 1000, 0, 0 ), 100 ), new DamageInfo { Damage = 100 } );

		Assert.AreEqual( 1, victim.Hits );
		Assert.AreEqual( 8, other.Hits );
	}

	[TestMethod]
	[DataRow( ScenePhysicsMode.Physics3D )]
	[DataRow( ScenePhysicsMode.Physics2D )]
	public void CallerOwnedQueryMatchesPublicQueryAndDeduplicatesShapes( ScenePhysicsMode mode )
	{
		using var scope = new Scene { PhysicsMode = mode }.Push();
		var scene = Game.ActiveScene;
		var victim = CreateVictim( scene, new Vector3( 10, 0, 0 ) );
		victim.GameObject.Components.Create<SphereCollider>().Radius = 3;
		var far = CreateVictim( scene, new Vector3( 1000, 0, 0 ) );
		var sphere = new Sphere( Vector3.Zero, 100 );
		var results = new HashSet<GameObject>();
		scene.FindInPhysics( sphere, results );
		Assert.IsTrue( results.SetEquals( scene.FindInPhysics( sphere ) ) );
		Assert.AreEqual( 1, results.Count );
		Assert.IsTrue( results.Contains( victim.GameObject ) );

		// Caller storage is additive; public queries still own independent snapshots.
		var snapshot = scene.FindInPhysics( sphere );
		scene.FindInPhysics( new Sphere( far.WorldPosition, 100 ), results );
		Assert.AreEqual( 2, results.Count );
		Assert.IsTrue( new HashSet<GameObject>( snapshot ).SetEquals( new[] { victim.GameObject } ) );

		RadiusDamage.ApplyDamage( sphere, new DamageInfo { Damage = 100 }, occlusion: false );
		Assert.AreEqual( 1, victim.Hits );
		Assert.AreEqual( 0, far.Hits );
	}

	[TestMethod]
	public void ReusedWorkspaceRecomputesOcclusionAndIgnore()
	{
		using var scope = new Scene().Push();
		var scene = Game.ActiveScene;
		var victim = CreateVictim( scene, new Vector3( 100, 0, 0 ) );
		var sphere = new Sphere( Vector3.Zero, 200 );
		var damage = new DamageInfo { Damage = 100 };
		RadiusDamage.ApplyDamage( sphere, damage );
		Assert.AreEqual( 1, victim.Hits );

		var wall = scene.CreateObject();
		wall.WorldPosition = new Vector3( 50, 0, 0 );
		var box = wall.Components.Create<BoxCollider>();
		box.Static = true;
		box.Scale = new Vector3( 10, 100, 100 );
		RadiusDamage.ApplyDamage( sphere, damage );
		Assert.AreEqual( 1, victim.Hits, "The new wall must block the previously visible target" );
		RadiusDamage.ApplyDamage( sphere, damage, occlusion: false );
		Assert.AreEqual( 2, victim.Hits );
		RadiusDamage.ApplyDamage( sphere, damage, ignore: victim.GameObject, occlusion: false );
		Assert.AreEqual( 2, victim.Hits );

		wall.Enabled = false;
		RadiusDamage.ApplyDamage( sphere, damage );
		Assert.AreEqual( 3, victim.Hits );
	}

	[TestMethod, TestCategory( Allocations.Category )]
	[DataRow( false )]
	[DataRow( true )]
	public void RepeatedDenseQueriesReuseStorage( bool occlusion )
	{
		using var scope = new Scene().Push();
		var scene = Game.ActiveScene;
		for ( var i = 0; i < 150; i++ )
			CreateVictim( scene, new Vector3( (i % 10) * 8, (i / 10) * 8, 0 ) );

		var damage = new DamageInfo { Damage = 1 };
		var sphere = new Sphere( Vector3.Zero, 512 );
		Allocations.AssertSettles( "Repeated dense radius damage", () => RadiusDamage.ApplyDamage( sphere, damage, occlusion: occlusion ), allowedPerPass: occlusion ? 1024 : 0 );
	}
}
