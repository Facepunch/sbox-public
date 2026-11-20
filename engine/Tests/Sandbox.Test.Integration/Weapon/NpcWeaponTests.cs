using Sandbox.Network;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using Sandbox.Npcs.Schedules;
using Sandbox.Npcs.Tasks;
using SceneTests;
using SceneTests.Components;
using System;
using static WeaponTests.WeaponHolderTests;
using TestWeapon = WeaponTests.WeaponHolderTests.TestWeapon;

namespace WeaponTests;

[TestClass]
public class NpcWeaponTests
{
	sealed class EmptySchedule : BaseSchedule
	{
		protected override void OnStart() { }
	}

	[TestMethod]
	[DataRow( "holster" )]
	[DataRow( "drop" )]
	[DataRow( "transfer" )]
	public void AFireTaskStopsWhenItsNpcNoLongerHoldsTheActiveWeapon( string action )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var npc = SpawnNpc( scene, senses: true );
		var weapon = CreateWeapon<TestWeapon>( scene, npc.GameObject );
		npc.Weapon = weapon;
		scene.GameTick();
		var schedule = new EmptySchedule();
		schedule.InternalInit( npc );
		var task = new FireWeaponTask( weapon, scene.CreateObject() );
		task.Initialize( schedule );
		Assert.AreEqual( TaskResult.Running, task.InternalUpdate() );

		if ( action == "holster" ) npc.Inventory.ForceHolster();
		if ( action == "drop" ) npc.Inventory.Drop( weapon );
		if ( action == "transfer" )
		{
			var (_, other) = CreateHolder( scene );
			Assert.IsTrue( npc.Inventory.Transfer( weapon, other ) );
			other.Switch( weapon );
			Assert.IsTrue( weapon.IsActive );
		}

		Assert.IsTrue( weapon.IsValid() );
		Assert.AreEqual( TaskResult.Failed, task.InternalUpdate() );
		task.InternalEnd( TaskResult.Failed );
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

	/// <summary>Something that isn't a player and holds weapons in an NPC inventory.</summary>
	static (TestHolder Holder, BaseNpcInventory Inventory) CreateHolder( Scene scene )
	{
		var go = scene.CreateObject();
		return (go.Components.Create<TestHolder>(), go.Components.Create<BaseNpcInventory>());
	}

	static T CreateWeapon<T>( Scene scene, GameObject parent = null ) where T : BaseCombatWeapon, new()
	{
		var go = scene.CreateObject();
		if ( parent.IsValid() )
			go.Parent = parent;

		var weapon = go.Components.Create<T>();
		weapon.PrimaryAmmoType = TestAmmoResource.Pistol;
		return weapon;
	}

	/// <summary>A stand-in weapon prefab: a disabled object to clone from.</summary>
	static GameObject CreateWeaponPrefab<T>( Scene scene ) where T : BaseCombatWeapon, new()
	{
		var prefab = scene.CreateObject( false );
		prefab.Components.Create<T>();
		return prefab;
	}

	static BaseCombatNpc SpawnNpc( Scene scene, bool senses = false )
	{
		// Test types aren't in the type library, so the required animation layer is added by hand
		var go = scene.CreateObject();
		var animation = go.Components.Create<AnimationLayer>();
		if ( senses ) go.Components.Create<SensesLayer>();
		var npc = go.Components.Create<BaseCombatNpc>();
		npc.Animation ??= animation;
		return npc;
	}

	[TestMethod]
	public void TheInventorySeedsTheMagazinesAndNeverRunsOut()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (holder, inventory) = CreateHolder( scene );
		var weapon = CreateWeapon<TestWeapon>( scene );
		weapon.StartingAmmo = 24;
		Assert.AreEqual( -1, weapon.Clip1 );

		Assert.IsTrue( inventory.Add( weapon ) );

		Assert.AreEqual( holder, weapon.Holder );
		Assert.IsTrue( weapon.IsHeld );
		Assert.AreEqual( weapon.ClipMaxSize, weapon.Clip1 );
		Assert.AreEqual( int.MaxValue, weapon.Ammo1 );
		Assert.AreEqual( int.MaxValue, inventory.GetAmmo( TestAmmoResource.Pistol ) );
		Assert.IsTrue( inventory.HasAmmo( TestAmmoResource.Rifle, 1000 ) );
		Assert.AreEqual( 0, inventory.GetAmmo( null ) );

		Assert.IsTrue( weapon.TakePrimaryAmmo( weapon.ClipMaxSize ) );
		Assert.IsFalse( weapon.HasPrimaryAmmo(), "The magazine still runs dry" );
		Assert.IsTrue( weapon.CanReload() );

		Assert.AreEqual( 30, inventory.TakeAmmo( TestAmmoResource.Pistol, 30 ) );
		Assert.AreEqual( int.MaxValue, inventory.GetAmmo( TestAmmoResource.Pistol ), "Taking leaves the reserve as it was" );
		Assert.AreEqual( 0, inventory.TakeAmmo( TestAmmoResource.Pistol, -5 ) );
		Assert.AreEqual( 0, inventory.GiveAmmo( TestAmmoResource.Pistol, 10 ), "There's nothing to top up" );

		Assert.IsTrue( inventory.ManualPumping, "Nobody's input drives the active weapon" );
	}

	[TestMethod]
	public void AReloadRefillsTheMagazine()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (_, inventory) = CreateHolder( scene );
		var weapon = CreateWeapon<TestWeapon>( scene );
		weapon.ReloadTime = 0.05f;
		inventory.Add( weapon );

		weapon.TakePrimaryAmmo( 6 );

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			// The reload runs on the weapon's task source, which only pumps while it is deployed.
			inventory.Switch( weapon );
			weapon.Reload();
		}

		Assert.IsTrue( weapon.IsReloading );

		var deadline = System.Diagnostics.Stopwatch.StartNew();
		while ( weapon.IsReloading && deadline.ElapsedMilliseconds < 2000 )
		{
			System.Threading.Thread.Sleep( 5 );
			scene.GameTick();

			using ( GameComponentTestUtils.PushSceneClock( scene ) )
			{
				Sandbox.Tasks.SyncContext.MainThread.ProcessQueue();
			}
		}

		Assert.IsFalse( weapon.IsReloading );
		Assert.AreEqual( weapon.ClipMaxSize, weapon.Clip1 );
	}

	[TestMethod]
	public void TheInventoryDeploysWithoutAViewModelAndDropsTheWeapon()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (holder, inventory) = CreateHolder( scene );
		var weapon = CreateWeapon<TestWeapon>( scene );
		weapon.ViewModelPrefab = scene.CreateObject( false );
		inventory.Add( weapon );

		inventory.Switch( weapon );
		Assert.IsTrue( weapon.IsActive );
		Assert.IsTrue( weapon.GameObject.Enabled );
		Assert.IsFalse( weapon.ViewModel.IsValid(), "A view model is for a player" );

		inventory.Drop( weapon );
		Assert.IsNull( inventory.ActiveItem );
		Assert.IsNull( weapon.Inventory );
		Assert.IsNull( weapon.Holder );
		Assert.AreNotEqual( holder.GameObject, weapon.GameObject.Parent );
		Assert.IsTrue( weapon.GameObject.Enabled );
		Assert.AreEqual( -1, weapon.Slot );
		Assert.AreEqual( weapon.ClipMaxSize, weapon.Clip1, "The dropped weapon keeps its rounds" );
	}

	[TestMethod]
	public void AWeaponAnNpcDroppedStillGrantsItsStartingReserve()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var (_, npcInventory) = CreateHolder( scene );
		var weapon = CreateWeapon<TestWeapon>( scene );
		weapon.StartingAmmo = 24;

		npcInventory.Add( weapon );
		npcInventory.Switch( weapon );
		npcInventory.Drop( weapon );

		// The NPC's bottomless reserve took nothing, so the first real inventory still gets the starting ammo
		var player = scene.CreateObject().Components.Create<BaseInventoryComponent>();
		Assert.IsTrue( player.Add( weapon ) );
		Assert.AreEqual( 24, player.GetAmmo( TestAmmoResource.Pistol ) );

		// Once: dropping it and taking it again grants nothing more
		player.Drop( weapon );
		player.TakeAmmo( TestAmmoResource.Pistol, 24 );
		Assert.IsTrue( player.Add( weapon ) );
		Assert.AreEqual( 0, player.GetAmmo( TestAmmoResource.Pistol ) );

		player.Drop( weapon );
		Assert.IsTrue( npcInventory.Add( weapon ) );
		npcInventory.Drop( weapon );
		Assert.IsTrue( player.Add( weapon ) );
		Assert.AreEqual( 0, player.GetAmmo( TestAmmoResource.Pistol ), "Passing through an NPC must not grant starting ammo again" );
	}

	[TestMethod]
	public void ACombatNpcTakesItsWeaponThroughAnInventoryItAdds()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var npc = SpawnNpc( scene );
		var weapon = CreateWeapon<TestWeapon>( scene, npc.GameObject );
		weapon.ViewModelPrefab = scene.CreateObject( false );
		npc.Weapon = weapon;
		Assert.IsNull( npc.Inventory );

		scene.GameTick();

		Assert.IsInstanceOfType( npc.Inventory, typeof( BaseNpcInventory ) );
		Assert.IsTrue( npc.Inventory.ManualPumping );
		Assert.AreEqual( weapon, npc.Inventory.ActiveItem );
		Assert.AreEqual( weapon, npc.Weapon );
		Assert.AreEqual( npc, weapon.Holder );
		Assert.IsTrue( weapon.GameObject.Enabled );
		Assert.AreEqual( weapon.ClipMaxSize, weapon.Clip1 );
		Assert.AreEqual( int.MaxValue, weapon.Ammo1 );
		Assert.AreEqual( weapon.Npc.SpreadScale * npc.AimSpreadScale, weapon.SpreadScale );
		Assert.IsFalse( weapon.ViewModel.IsValid() );
	}

	[TestMethod]
	public void ACombatNpcUsesTheInventoryItsPrefabHas()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var npc = SpawnNpc( scene );
		var own = npc.GameObject.Components.Create<BaseInventoryComponent>();
		var weapon = CreateWeapon<TestWeapon>( scene, npc.GameObject );
		npc.Weapon = weapon;

		scene.GameTick();

		Assert.AreEqual( own, npc.Inventory );
		Assert.AreEqual( weapon, own.ActiveItem );
		Assert.IsTrue( own.ManualPumping );
		Assert.AreEqual( weapon.ClipMaxSize, weapon.Clip1 );
		Assert.AreEqual( 0, weapon.Ammo1, "A plain inventory's reserve is finite" );
	}

	[TestMethod]
	public void ACombatNpcArmedBeforeItStartsTakesThatWeapon()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var npc = SpawnNpc( scene );
		npc.ArmWith( CreateWeaponPrefab<TestWeapon>( scene ) );

		var weapon = npc.Weapon;
		Assert.IsTrue( weapon.IsValid() );
		Assert.AreEqual( npc.GameObject, weapon.GameObject.Parent );

		scene.GameTick();

		Assert.AreEqual( weapon, npc.Inventory.ActiveItem );
		Assert.AreEqual( weapon.ClipMaxSize, weapon.Clip1 );
	}

	[TestMethod]
	public void ACombatNpcRearmsThroughItsInventory()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var npc = SpawnNpc( scene );
		var first = CreateWeapon<TestWeapon>( scene, npc.GameObject );
		npc.Weapon = first;
		scene.GameTick();

		npc.ArmWith( CreateWeaponPrefab<OtherWeapon>( scene ) );

		var second = npc.Weapon;
		Assert.IsInstanceOfType( second, typeof( OtherWeapon ) );
		Assert.AreEqual( second, npc.Inventory.ActiveItem );
		Assert.AreEqual( npc.Inventory, second.Inventory );
		Assert.AreEqual( second.ClipMaxSize, second.Clip1 );
		Assert.AreEqual( second.Npc.SpreadScale * npc.AimSpreadScale, second.SpreadScale );

		scene.GameTick();
		Assert.IsFalse( first.IsValid(), "The inventory removed the old weapon" );
	}

	[TestMethod]
	public void ADyingCombatNpcDropsItsWeaponOutOfTheInventory()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var npc = SpawnNpc( scene );
		var weapon = CreateWeapon<TestWeapon>( scene, npc.GameObject );
		npc.Weapon = weapon;
		scene.GameTick();
		weapon.TakePrimaryAmmo( 4 );

		((Component.IDamageable)npc).OnDamage( new DamageInfo( 500, null, null ) );
		scene.GameTick();

		Assert.IsFalse( npc.IsValid() );
		Assert.IsTrue( weapon.IsValid(), "The weapon outlives the NPC" );
		Assert.IsNull( weapon.Inventory );
		Assert.IsNull( weapon.Holder );
		Assert.IsTrue( weapon.GameObject.Enabled );
		Assert.AreEqual( -1, weapon.Slot );
		Assert.AreEqual( weapon.ClipMaxSize - 4, weapon.Clip1 );
		Assert.AreEqual( 1f, weapon.SpreadScale, "It shoots as it was made to again" );
	}

	// A weapon prefab a mount would make: an engine weapon on a root of its own
	static GameObject BuildWeaponPrefab( string name )
	{
		var builder = new Sandbox.Mounting.PrefabBuilder().WithName( $"test/npc_weapon_{name}.prefab" );

		using ( builder.Scope() )
		{
			new GameObject().AddComponent<BaseCombatWeapon>();
		}

		return SceneUtility.GetPrefabScene( builder.Create() );
	}

	[TestMethod]
	public void ACombatNpcBuiltIntoAPrefabKeepsItsChoicesAndArmsWhenItGoesLive()
	{
		var choices = new[] { "a", "b", "c" }.Select( BuildWeaponPrefab ).ToList();

		// The NPC as text, the way a mount describes one
		System.Text.Json.Nodes.JsonObject json;
		{
			var template = new Scene();
			using var templateScope = template.Push();
			var go = template.CreateObject();
			var animation = go.Components.Create<AnimationLayer>();
			var described = go.Components.Create<BaseCombatNpc>();
			described.Animation ??= animation;
			described.WeaponChoices = choices;
			json = go.Serialize();
			template.Destroy();
		}

		var builder = new Sandbox.Mounting.PrefabBuilder().WithName( "test/armed_npc.prefab" );

		using ( builder.Scope() )
		{
			new GameObject().Deserialize( json );
		}

		var prefab = builder.Create();
		var built = SceneUtility.GetPrefabScene( prefab );
		var builtNpc = built.Components.Get<BaseCombatNpc>( FindMode.EverythingInSelf );

		Assert.AreEqual( 3, builtNpc.WeaponChoices.Count, "The prefab keeps every choice" );
		Assert.IsFalse( builtNpc.Weapon.IsValid(), "Building the prefab does not arm it" );
		Assert.IsNull( built.Components.Get<BaseInventoryComponent>( FindMode.EverythingInSelf ), "Nor give it an inventory" );
		Assert.AreEqual( 0, built.Children.Count );

		var scene = new Scene();
		using var scope = scene.Push();

		// Network spawned: it is armed by the time the spawn goes out
		var spawned = built.Clone( Vector3.Zero );
		var npc = spawned.Components.Get<BaseCombatNpc>( FindMode.EverythingInSelf );
		spawned.NetworkSpawn();

		Assert.IsTrue( npc.Weapon.IsValid(), "It chose a weapon when it was spawned" );
		Assert.IsNotNull( npc.Inventory, "And has the inventory that will hold it" );
		Assert.AreEqual( 3, npc.WeaponChoices.Count );
		Assert.AreEqual( 1, spawned.Components.GetAll<BaseCombatWeapon>( FindMode.EverythingInSelfAndDescendants ).Count() );

		scene.GameTick();
		Assert.AreEqual( npc.Weapon, npc.Inventory.ActiveItem, "It takes that weapon out when it starts" );
		Assert.AreEqual( 1, spawned.Components.GetAll<BaseCombatWeapon>( FindMode.EverythingInSelfAndDescendants ).Count(), "And chooses only once" );

		// Never network spawned: it arms when it starts
		var local = built.Clone( Vector3.Zero );
		var localNpc = local.Components.Get<BaseCombatNpc>( FindMode.EverythingInSelf );
		Assert.IsFalse( localNpc.Weapon.IsValid() );

		scene.GameTick();
		Assert.IsTrue( localNpc.Weapon.IsValid() );
		Assert.AreEqual( localNpc.Weapon, localNpc.Inventory.ActiveItem );

		Sandbox.Mounting.PrefabBuilder.Destroy( prefab );
	}
}
