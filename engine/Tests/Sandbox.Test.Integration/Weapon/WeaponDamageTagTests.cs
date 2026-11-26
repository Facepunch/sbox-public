using Sandbox.Network;
using SceneTests;
using System;
using System.Text.Json.Nodes;

namespace WeaponTests;

[TestClass]
public class WeaponDamageTagTests
{
	/// <summary>Something to shoot that remembers the tags of the last thing that hit it.</summary>
	public sealed class ShotTarget : Component, Component.IDamageable
	{
		public int Hits;
		public TagSet HitTags;
		public string AddsTag;

		public void OnDamage( in DamageInfo damage )
		{
			Hits++;
			HitTags = damage.Tags;

			if ( AddsTag is not null )
				damage.Tags.Add( AddsTag );
		}
	}

	/// <summary>A weapon whose own attack code shoots, with or without tags of its own.</summary>
	public sealed class ShootingWeapon : BaseCombatWeapon
	{
		public void Shoot( TagSet tags = null ) => ShootBullet( 500f, 0f, 10f, 0f, tags );
		public void ShootVolley( TagSet tags = null ) => ShootBullets( 3, Vector2.Zero, 500f, 0f, 10f, 0f, tags );
	}

	Connection _previousLocalConnection;
	NetworkSystem _previousNetworkSystem;

	[TestInitialize]
	public void PinHostNetworkingState()
	{
		_previousLocalConnection = Connection.Local;
		_previousNetworkSystem = Networking.System;

		Connection.Local = new TestConnection( Guid.NewGuid(), isHost: true );
		Networking.System = null;
	}

	[TestCleanup]
	public void RestoreNetworkingState()
	{
		Connection.Local = _previousLocalConnection;
		Networking.System = _previousNetworkSystem;
	}

	static ShotTarget CreateTarget( Scene scene )
	{
		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 200, 0, 0 );
		go.Components.Create<BoxCollider>().Scale = new Vector3( 50 );
		return go.Components.Create<ShotTarget>();
	}

	/// <summary>A weapon nobody holds: it fires along its own forward and needs no ammunition.</summary>
	static T CreateWeapon<T>( Scene scene ) where T : BaseCombatWeapon, new()
	{
		var weapon = scene.CreateObject().Components.Create<T>();
		weapon.Ballistics = weapon.Ballistics with { SpreadBase = Vector2.Zero, SpreadGrowth = Vector2.Zero };
		return weapon;
	}

	static string[] Tags( TagSet tags ) => tags.TryGetAll().OrderBy( x => x ).ToArray();

	[TestMethod]
	public void ADefaultWeaponsHitsCarryBullet()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var target = CreateTarget( scene );

		var weapon = CreateWeapon<BaseCombatWeapon>( scene );
		CollectionAssert.AreEqual( new[] { "bullet" }, Tags( weapon.AttackDamageTags ) );

		weapon.PrimaryAttack();
		Assert.AreEqual( 1, target.Hits );
		CollectionAssert.AreEqual( new[] { "bullet" }, Tags( target.HitTags ) );

		var custom = CreateWeapon<ShootingWeapon>( scene );
		custom.Shoot();
		Assert.AreEqual( 2, target.Hits );
		CollectionAssert.AreEqual( new[] { "bullet" }, Tags( target.HitTags ) );

		custom.ShootVolley();
		Assert.AreEqual( 5, target.Hits );
		CollectionAssert.AreEqual( new[] { "bullet" }, Tags( target.HitTags ) );
	}

	[TestMethod]
	public void AWeaponsOwnTagsReplaceTheDefault()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var target = CreateTarget( scene );

		var weapon = CreateWeapon<ShootingWeapon>( scene );
		weapon.AttackDamageTags = ["energy", "burn"];

		weapon.PrimaryAttack();
		CollectionAssert.AreEqual( new[] { "burn", "energy" }, Tags( target.HitTags ) );

		weapon.Shoot();
		CollectionAssert.AreEqual( new[] { "burn", "energy" }, Tags( target.HitTags ) );
	}

	[TestMethod]
	public void AWeaponWithClearedTagsLeavesItsHitsUntagged()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var target = CreateTarget( scene );

		var weapon = CreateWeapon<ShootingWeapon>( scene );
		weapon.AttackDamageTags = [];

		weapon.PrimaryAttack();
		Assert.AreEqual( 1, target.Hits );
		Assert.IsTrue( target.HitTags.IsEmpty );

		weapon.Shoot();
		Assert.IsTrue( target.HitTags.IsEmpty );

		weapon.AttackDamageTags = null;
		weapon.PrimaryAttack();
		Assert.AreEqual( 3, target.Hits );
		Assert.IsTrue( target.HitTags.IsEmpty );
	}

	[TestMethod]
	public void TagsPassedWithAShotWinOverTheWeapons()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var target = CreateTarget( scene );
		var weapon = CreateWeapon<ShootingWeapon>( scene );

		weapon.Shoot( ["melee"] );
		CollectionAssert.AreEqual( new[] { "melee" }, Tags( target.HitTags ) );

		weapon.ShootVolley( ["blast"] );
		CollectionAssert.AreEqual( new[] { "blast" }, Tags( target.HitTags ) );

		// An empty set says no tags, where passing nothing says the weapon's
		weapon.Shoot( [] );
		Assert.IsTrue( target.HitTags.IsEmpty );
	}

	[TestMethod]
	public void WhoeverHandlesTheDamageCannotChangeTheWeaponsTags()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var target = CreateTarget( scene );
		target.AddsTag = "handled";

		var weapon = CreateWeapon<ShootingWeapon>( scene );
		weapon.PrimaryAttack();
		weapon.ShootVolley();

		Assert.AreEqual( 4, target.Hits );
		CollectionAssert.AreEqual( new[] { "bullet" }, Tags( weapon.AttackDamageTags ) );
	}

	/// <summary>
	/// Saves a GameObject as a prefab or scene would and loads it into a fresh one, after <paramref name="edit"/>
	/// had its way with the saved weapon.
	/// </summary>
	static BaseCombatWeapon RoundTrip( BaseCombatWeapon weapon, Action<JsonObject> edit = null )
	{
		var json = Json.ParseToJsonObject( weapon.GameObject.Serialize().ToJsonString() );
		SceneUtility.MakeIdGuidsUnique( json );

		var saved = json["Components"].AsArray().Select( x => x.AsObject() ).Single( x => x.ContainsKey( nameof( BaseCombatWeapon.Ballistics ) ) );
		edit?.Invoke( saved );

		var copy = new GameObject( false );
		copy.Deserialize( json );
		return copy.GetComponent<BaseCombatWeapon>( true );
	}

	[TestMethod]
	public void SavedTagsComeBackAsTheyWere()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var weapon = CreateWeapon<BaseCombatWeapon>( scene );
		CollectionAssert.AreEqual( new[] { "bullet" }, Tags( RoundTrip( weapon ).AttackDamageTags ) );

		weapon.AttackDamageTags = ["energy", "burn"];
		CollectionAssert.AreEqual( new[] { "burn", "energy" }, Tags( RoundTrip( weapon ).AttackDamageTags ) );

		// Cleared stays cleared: an empty set is saved as one, it isn't left out for the default to come back
		weapon.AttackDamageTags = [];
		var cleared = RoundTrip( weapon, saved => Assert.IsTrue( saved.ContainsKey( nameof( BaseCombatWeapon.AttackDamageTags ) ) ) );
		Assert.IsNotNull( cleared.AttackDamageTags );
		Assert.IsTrue( cleared.AttackDamageTags.IsEmpty );

		var clone = weapon.GameObject.Clone().GetComponent<BaseCombatWeapon>( true );
		Assert.IsTrue( clone.AttackDamageTags.IsEmpty );
	}

	[TestMethod]
	public void AWeaponSavedBeforeTheTagsExistedGetsTheDefault()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var weapon = CreateWeapon<BaseCombatWeapon>( scene );
		weapon.AttackDamageTags = ["energy"];

		var old = RoundTrip( weapon, saved => Assert.IsTrue( saved.Remove( nameof( BaseCombatWeapon.AttackDamageTags ) ) ) );
		CollectionAssert.AreEqual( new[] { "bullet" }, Tags( old.AttackDamageTags ) );
	}
}
