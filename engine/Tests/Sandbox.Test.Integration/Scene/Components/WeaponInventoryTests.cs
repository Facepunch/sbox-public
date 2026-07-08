using System;
using Sandbox.Network;

namespace SceneTests.Components;

/// <summary>
/// An inventory item that records its lifecycle hooks and lets tests veto switching,
/// holstering and dropping.
/// </summary>
public sealed class TestItem : BaseInventoryItem
{
	public bool AllowSwitchTo = true;
	public bool AllowHolster = true;
	public bool AllowDrop = true;
	public bool Avoid = false;

	public int Equips { get; private set; }
	public int Holsters { get; private set; }
	public int Controls { get; private set; }
	public int Drops { get; private set; }
	public InventoryComponent AddedTo { get; private set; }
	public InventoryComponent RemovedFrom { get; private set; }

	public override bool ShouldAvoid => Avoid;

	protected override bool OnCanSwitchTo() => AllowSwitchTo;
	protected override bool OnHolstering( BaseInventoryItem next ) => AllowHolster;
	protected override void OnEquipped() => Equips++;
	protected override void OnHolstered() => Holsters++;
	protected override void OnControl() => Controls++;
	protected override void OnAdded( InventoryComponent inventory ) => AddedTo = inventory;
	protected override void OnRemoved( InventoryComponent inventory ) => RemovedFrom = inventory;

	protected override bool OnDrop()
	{
		if ( !AllowDrop )
			return false;

		Drops++;
		return base.OnDrop();
	}
}

/// <summary>
/// An inventory whose game hooks can be vetoed, for testing the extension points.
/// </summary>
public sealed class TestInventory : InventoryComponent
{
	public bool AllowAdd = true;
	public bool AllowMove = true;

	protected override bool OnAdding( BaseInventoryItem item, int slot ) => AllowAdd;
	protected override bool OnMovingSlot( int fromSlot, int toSlot ) => AllowMove;
}

/// <summary>
/// Pins the InventoryComponent contract: slot assignment and refusal, the equip/holster
/// lifecycle with switch vetoes, remove and drop flows, slot moving, best-item selection
/// and the per-frame control pump.
/// </summary>
[TestClass]
public class InventoryComponentTest
{
	Connection _previousLocalConnection;
	NetworkSystem _previousNetworkSystem;

	/// <summary>
	/// Pins Connection.Local to a host connection and clears Networking.System so the
	/// host-gated inventory paths (Add, Remove, the Switch RPC) run locally instead of
	/// being silently skipped. Same idiom as GameComponentTests.cs.
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
	/// Restores whatever global networking state existed before the test.
	/// </summary>
	[TestCleanup]
	public void RestoreNetworkingState()
	{
		Connection.Local = _previousLocalConnection;
		Networking.System = _previousNetworkSystem;
	}

	/// <summary>
	/// Creates an inventory on a fresh GameObject.
	/// </summary>
	static InventoryComponent CreateInventory( Scene scene, int maxSlots = 6 )
	{
		var go = scene.CreateObject();
		var inventory = go.Components.Create<InventoryComponent>();
		inventory.MaxSlots = maxSlots;
		return inventory;
	}

	/// <summary>
	/// Creates an item on its own GameObject, ready to be added to an inventory.
	/// </summary>
	static TestItem CreateItem( Scene scene, int value = 0 )
	{
		var go = scene.CreateObject();
		var item = go.Components.Create<TestItem>();
		item.Value = value;
		return item;
	}

	/// <summary>
	/// Adding items assigns the first empty slots in order, reparents them under the
	/// inventory disabled, and fires the item's added hook. GetSlot and FindEmptySlot
	/// report the layout.
	/// </summary>
	[TestMethod]
	public void AddAssignsSlotsAndParents()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventory = CreateInventory( scene );
		var a = CreateItem( scene );
		var b = CreateItem( scene );

		Assert.IsTrue( inventory.Add( a ) );
		Assert.IsTrue( inventory.Add( b ) );

		Assert.AreEqual( 0, a.Slot );
		Assert.AreEqual( 1, b.Slot );
		Assert.AreEqual( inventory.GameObject, a.GameObject.Parent );
		Assert.IsFalse( a.GameObject.Enabled, "items are added holstered" );
		Assert.AreEqual( inventory, a.AddedTo, "the added hook fires with the inventory" );
		Assert.AreEqual( inventory, a.Inventory, "the item resolves its inventory from the hierarchy" );

		Assert.AreEqual( a, inventory.GetSlot( 0 ) );
		Assert.AreEqual( b, inventory.GetSlot( 1 ) );
		Assert.AreEqual( 2, inventory.FindEmptySlot() );
	}

	/// <summary>
	/// An occupied slot refuses a second item, a full inventory refuses entirely, and
	/// the OnAdding hook can veto an add before anything changes.
	/// </summary>
	[TestMethod]
	public void AddRefusalPaths()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventory = CreateInventory( scene, maxSlots: 2 );

		Assert.IsTrue( inventory.Add( CreateItem( scene ), 0 ) );
		Assert.IsFalse( inventory.Add( CreateItem( scene ), 0 ), "an occupied slot refuses" );
		Assert.IsFalse( inventory.Add( CreateItem( scene ), 99 ), "an out of range slot refuses" );

		Assert.IsTrue( inventory.Add( CreateItem( scene ) ) );
		Assert.IsFalse( inventory.Add( CreateItem( scene ) ), "a full inventory refuses" );

		var vetoGo = scene.CreateObject();
		var veto = vetoGo.Components.Create<TestInventory>();
		veto.MaxSlots = 2;
		veto.AllowAdd = false;

		var item = CreateItem( scene );
		Assert.IsFalse( veto.Add( item ), "the OnAdding hook vetoes" );
		Assert.AreNotEqual( veto.GameObject, item.GameObject.Parent, "a vetoed item is untouched" );
		Assert.AreEqual( -1, item.Slot );
	}

	/// <summary>
	/// Switching makes the item active and enabled and fires Equip; switching away
	/// holsters and disables it. Switch(null, allowHolster) holsters everything.
	/// </summary>
	[TestMethod]
	public void SwitchEquipsAndHolsters()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventory = CreateInventory( scene );
		var a = CreateItem( scene );
		var b = CreateItem( scene );
		inventory.Add( a );
		inventory.Add( b );

		inventory.Switch( a );
		Assert.AreEqual( a, inventory.ActiveItem );
		Assert.IsTrue( a.GameObject.Enabled );
		Assert.IsTrue( a.IsActive );
		Assert.AreEqual( 1, a.Equips );

		inventory.Switch( b );
		Assert.AreEqual( b, inventory.ActiveItem );
		Assert.IsFalse( a.GameObject.Enabled, "the outgoing item is disabled" );
		Assert.AreEqual( 1, a.Holsters );
		Assert.AreEqual( 1, b.Equips );

		inventory.Switch( null, allowHolster: true );
		Assert.IsNull( inventory.ActiveItem, "null with allowHolster holsters everything" );
		Assert.AreEqual( 1, b.Holsters );
	}

	/// <summary>
	/// PickupWorldItem takes an item lying in the world and makes it active when nothing else is -
	/// but won't re-take something this inventory just dropped.
	/// </summary>
	[TestMethod]
	public void PickupWorldItemAndDropGrace()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventory = CreateInventory( scene );
		var item = CreateItem( scene );

		inventory.PickupWorldItem( item );

		Assert.AreEqual( inventory, item.Inventory, "a world item is taken" );
		Assert.AreEqual( item, inventory.ActiveItem, "picked up into empty hands, so it deploys" );

		inventory.Drop( item );
		Assert.IsNull( item.Inventory );

		inventory.PickupWorldItem( item );
		Assert.IsNull( item.Inventory, "a just-dropped item isn't vacuumed straight back up" );
	}

	/// <summary>
	/// An item can refuse to become active (OnCanSwitchTo) and the active item can
	/// refuse to be switched away from (OnHolstering).
	/// </summary>
	[TestMethod]
	public void SwitchVetoes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventory = CreateInventory( scene );
		var stubborn = CreateItem( scene );
		var other = CreateItem( scene );
		inventory.Add( stubborn );
		inventory.Add( other );

		stubborn.AllowSwitchTo = false;
		inventory.Switch( stubborn );
		Assert.IsNull( inventory.ActiveItem, "an item can refuse to be switched to" );

		stubborn.AllowSwitchTo = true;
		inventory.Switch( stubborn );
		Assert.AreEqual( stubborn, inventory.ActiveItem );

		stubborn.AllowHolster = false;
		inventory.Switch( other );
		Assert.AreEqual( stubborn, inventory.ActiveItem, "the active item can refuse to holster" );

		inventory.Switch( null, allowHolster: true );
		Assert.AreEqual( stubborn, inventory.ActiveItem, "a voluntary holster respects the veto" );
	}

	/// <summary>
	/// Remove force-holsters (the veto can't stop it), destroys the item's GameObject,
	/// fires the removed hook and switches to the best remaining item.
	/// </summary>
	[TestMethod]
	public void RemoveDestroysAndSwitchesToBest()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventory = CreateInventory( scene );
		var doomed = CreateItem( scene, value: 10 );
		var backup = CreateItem( scene, value: 5 );
		inventory.Add( doomed );
		inventory.Add( backup );

		inventory.Switch( doomed );
		doomed.AllowHolster = false;

		inventory.Remove( doomed );
		scene.ProcessDeletes();

		Assert.IsFalse( doomed.GameObject.IsValid(), "the removed item is destroyed despite its holster veto" );
		Assert.AreEqual( inventory, doomed.RemovedFrom, "the removed hook fires" );
		Assert.AreEqual( backup, inventory.ActiveItem, "the best remaining item is equipped" );
	}

	/// <summary>
	/// Transfer moves an item between inventories without dropping or destroying it - it lands in
	/// a destination slot disabled, fires the removed/added hooks, and the source holsters and
	/// re-equips its best remaining item.
	/// </summary>
	[TestMethod]
	public void TransferMovesBetweenInventories()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var source = CreateInventory( scene );
		var chest = CreateInventory( scene );
		var item = CreateItem( scene, value: 10 );
		var backup = CreateItem( scene, value: 5 );
		source.Add( item );
		source.Add( backup );
		source.Switch( item );

		Assert.IsTrue( source.Transfer( item, chest ) );

		Assert.AreEqual( chest, item.Inventory, "the item belongs to the destination" );
		Assert.AreEqual( chest.GameObject, item.GameObject.Parent );
		Assert.IsFalse( item.GameObject.Enabled, "transferred items arrive holstered" );
		Assert.AreEqual( 0, item.Slot );
		Assert.AreEqual( source, item.RemovedFrom, "the removed hook fires on the source" );
		Assert.AreEqual( chest, item.AddedTo, "the added hook fires on the destination" );
		Assert.IsNull( chest.ActiveItem, "the destination doesn't auto-deploy" );
		Assert.AreEqual( backup, source.ActiveItem, "the source re-equips its best remaining item" );
	}

	/// <summary>
	/// A refused transfer changes nothing - the destination's OnAdding veto and an occupied
	/// hotbar slot both leave the item exactly where it was.
	/// </summary>
	[TestMethod]
	public void TransferRefusalChangesNothing()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var source = CreateInventory( scene );
		var item = CreateItem( scene );
		source.Add( item );
		source.Switch( item );

		var vetoGo = scene.CreateObject();
		var veto = vetoGo.Components.Create<TestInventory>();
		veto.MaxSlots = 2;
		veto.AllowAdd = false;

		Assert.IsFalse( source.Transfer( item, veto ), "the destination's OnAdding vetoes" );
		Assert.AreEqual( source, item.Inventory, "a refused item stays put" );
		Assert.AreEqual( item, source.ActiveItem, "a refused transfer doesn't holster" );

		var crowded = CreateInventory( scene );
		crowded.Add( CreateItem( scene ) );
		Assert.IsFalse( source.Transfer( item, crowded, 0 ), "an occupied hotbar slot refuses" );
		Assert.AreEqual( source, item.Inventory );
	}

	/// <summary>
	/// Drop unparents the item into the world enabled and clears its slot; an item
	/// refusing the drop is restored as the active item.
	/// </summary>
	[TestMethod]
	public void DropReleasesOrRestores()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventory = CreateInventory( scene );
		var keeper = CreateItem( scene );
		var dropper = CreateItem( scene );
		inventory.Add( keeper );
		inventory.Add( dropper );

		keeper.AllowDrop = false;
		inventory.Switch( keeper );
		inventory.Drop( keeper );
		Assert.AreEqual( keeper, inventory.ActiveItem, "an item that refuses the drop is re-equipped" );
		Assert.AreEqual( 0, keeper.Slot, "and keeps its slot" );

		inventory.Drop( dropper );
		Assert.AreEqual( 1, dropper.Drops, "the drop hook fires" );
		Assert.AreNotEqual( inventory.GameObject, dropper.GameObject.Parent, "a dropped item leaves the inventory" );
		Assert.IsTrue( dropper.GameObject.Enabled );
		Assert.AreEqual( -1, dropper.Slot );
		Assert.IsFalse( inventory.Items.Contains( dropper ) );
	}

	/// <summary>
	/// MoveSlot swaps occupied slots, relocates into empty ones, refuses invalid input
	/// and respects the OnMovingSlot veto.
	/// </summary>
	[TestMethod]
	public void MoveSlotSwapsAndVetoes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var inventory = go.Components.Create<TestInventory>();
		inventory.MaxSlots = 4;

		var a = CreateItem( scene );
		var b = CreateItem( scene );
		inventory.Add( a );
		inventory.Add( b );

		inventory.MoveSlot( 0, 1 );
		Assert.AreEqual( 1, a.Slot, "occupied slots swap" );
		Assert.AreEqual( 0, b.Slot );

		inventory.MoveSlot( 1, 3 );
		Assert.AreEqual( 3, a.Slot, "moving into an empty slot relocates" );

		inventory.MoveSlot( 2, 0 );
		Assert.AreEqual( 0, b.Slot, "moving from an empty slot does nothing" );

		inventory.AllowMove = false;
		inventory.MoveSlot( 0, 1 );
		Assert.AreEqual( 0, b.Slot, "the OnMovingSlot hook vetoes" );
	}

	/// <summary>
	/// GetBestItem picks the highest Value item that allows switching, only falling
	/// back to ShouldAvoid items (empty guns) when nothing better exists.
	/// </summary>
	[TestMethod]
	public void GetBestItemOrdersAndAvoids()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventory = CreateInventory( scene );
		var low = CreateItem( scene, value: 1 );
		var high = CreateItem( scene, value: 10 );
		var refused = CreateItem( scene, value: 100 );
		inventory.Add( low );
		inventory.Add( high );
		inventory.Add( refused );

		refused.AllowSwitchTo = false;
		Assert.AreEqual( high, inventory.GetBestItem(), "unswitchable items are never best" );

		high.Avoid = true;
		Assert.AreEqual( low, inventory.GetBestItem(), "avoided items lose to anything usable" );

		low.Avoid = true;
		Assert.AreEqual( high, inventory.GetBestItem(), "with only avoided items, the best of them wins" );
	}

	/// <summary>
	/// The inventory pumps the active item's control hook every frame, stops when
	/// ManualPumping is on, and never pumps inactive items.
	/// </summary>
	[TestMethod]
	public void PumpDrivesActiveItemControl()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventory = CreateInventory( scene );
		var active = CreateItem( scene );
		var holstered = CreateItem( scene );
		inventory.Add( active );
		inventory.Add( holstered );
		inventory.Switch( active );

		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		Assert.IsTrue( active.Controls > 0, "the active item is pumped" );
		Assert.AreEqual( 0, holstered.Controls, "holstered items are not" );

		var pumped = active.Controls;
		inventory.ManualPumping = true;

		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		Assert.AreEqual( pumped, active.Controls, "manual pumping stops the automatic pump" );

		inventory.Pump();
		Assert.AreEqual( pumped + 1, active.Controls, "Pump still drives it by hand" );
	}

	/// <summary>
	/// An item with a PreferredSlot lands there when added without an explicit slot, and
	/// falls back to the first empty slot when it's taken or out of range.
	/// </summary>
	[TestMethod]
	public void PreferredSlotWinsWhenFree()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventory = CreateInventory( scene );

		var pinned = CreateItem( scene );
		pinned.PreferredSlot = 3;
		inventory.Add( pinned );
		Assert.AreEqual( 3, pinned.Slot, "a free preferred slot wins" );

		var second = CreateItem( scene );
		second.PreferredSlot = 3;
		inventory.Add( second );
		Assert.AreEqual( 0, second.Slot, "a taken preferred slot falls back to first empty" );

		var outOfRange = CreateItem( scene );
		outOfRange.PreferredSlot = 99;
		inventory.Add( outOfRange );
		Assert.AreEqual( 1, outOfRange.Slot, "an out-of-range preference falls back too" );

		var explicitSlot = CreateItem( scene );
		explicitSlot.PreferredSlot = 5;
		inventory.Add( explicitSlot, 2 );
		Assert.AreEqual( 2, explicitSlot.Slot, "an explicit slot overrides the preference" );
	}

	/// <summary>
	/// A buckets inventory shares slots: items always land in their preferred slot, sorted
	/// by SlotOrder, and moving into an occupied slot doesn't swap.
	/// </summary>
	[TestMethod]
	public void BucketsShareSlots()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventory = CreateInventory( scene );
		inventory.Behaviour = InventoryComponent.InventoryBehaviour.Buckets;

		var late = CreateItem( scene );
		late.PreferredSlot = 2;
		late.SlotOrder = 5;

		var early = CreateItem( scene );
		early.PreferredSlot = 2;
		early.SlotOrder = 1;

		var unplaced = CreateItem( scene );

		Assert.IsTrue( inventory.Add( late ) );
		Assert.IsTrue( inventory.Add( early ), "buckets never refuse a taken slot" );
		Assert.IsTrue( inventory.Add( unplaced ) );

		Assert.AreEqual( 2, late.Slot );
		Assert.AreEqual( 2, early.Slot );
		Assert.AreEqual( 0, unplaced.Slot, "no preference lands in the first bucket" );

		CollectionAssert.AreEqual( new[] { early, late }, inventory.GetSlotItems( 2 ).ToArray(), "the bucket sorts by SlotOrder" );
		Assert.AreEqual( early, inventory.GetSlot( 2 ), "GetSlot returns the bucket's first item" );

		inventory.MoveSlot( 0, 2 );
		Assert.AreEqual( 2, unplaced.Slot, "moving into a bucket doesn't swap" );
		Assert.AreEqual( 2, early.Slot );
		Assert.AreEqual( 2, late.Slot );
	}

	/// <summary>
	/// GiveLoadout spawns every starting item prefab, grants the starting ammo and deploys
	/// the best item. Auto-granting on start is gated behind GiveOnStart.
	/// </summary>
	[TestMethod]
	public void LoadoutGrantsItemsAndAmmo()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventory = CreateInventory( scene );
		inventory.UsesLoadout = true;

		// Stand-in prefabs - disabled source objects the loadout clones from.
		var pistol = scene.CreateObject( false );
		pistol.Components.Create<TestItem>( false );
		var crowbar = scene.CreateObject( false );
		crowbar.Components.Create<TestItem>( false );

		inventory.StartingItems = [pistol, crowbar];
		inventory.StartingAmmo = [new InventoryComponent.AmmoGrant { Type = TestAmmoResource.Pistol, Amount = 48 }];

		inventory.GiveLoadout();

		Assert.AreEqual( 2, inventory.Items.Count(), "every starting item is granted" );
		Assert.AreEqual( 48, inventory.GetAmmo( TestAmmoResource.Pistol ), "the starting ammo is granted" );
		Assert.IsTrue( inventory.ActiveItem.IsValid(), "a loadout item deploys" );
	}
}

/// <summary>
/// Pins the BaseWeapon ammo contract: magazine seeding on pickup, clip and reserve
/// spending with the host mirror, the empty checks, reserve pool arithmetic and the
/// timed host reload loop.
/// </summary>
[TestClass]
public class BaseWeaponAmmoTest
{
	Connection _previousLocalConnection;
	NetworkSystem _previousNetworkSystem;

	/// <summary>
	/// Pins host networking state so FromHost ammo writes and the host-gated inventory
	/// paths run locally. Same idiom as GameComponentTests.cs.
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
	/// Restores whatever global networking state existed before the test.
	/// </summary>
	[TestCleanup]
	public void RestoreNetworkingState()
	{
		Connection.Local = _previousLocalConnection;
		Networking.System = _previousNetworkSystem;
	}

	/// <summary>
	/// Creates an inventory with a PlayerController on the same GameObject, so weapons
	/// added to it count as held (IsHeld resolves the controller from the hierarchy).
	/// </summary>
	static InventoryComponent CreateHeldInventory( Scene scene )
	{
		var go = scene.CreateObject();
		go.Components.Create<PlayerController>();
		return go.Components.Create<InventoryComponent>();
	}

	/// <summary>
	/// Creates a weapon with a magazine and a reserve type, not yet in an inventory.
	/// </summary>
	static BaseWeapon CreateWeapon( Scene scene, int clipSize = 8, AmmoResource ammoType = null, int startingAmmo = 0 )
	{
		var go = scene.CreateObject();
		var weapon = go.Components.Create<BaseWeapon>();
		weapon.ClipMaxSize = clipSize;
		weapon.PrimaryAmmoType = ammoType ?? TestAmmoResource.Pistol;
		weapon.StartingAmmo = startingAmmo;
		return weapon;
	}

	/// <summary>
	/// A clip weapon with no ammo type has a bottomless reserve - the magazine forces the
	/// reload rhythm, but there's always reserve to load.
	/// </summary>
	[TestMethod]
	public void NoAmmoTypeIsBottomlessReserve()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventory = CreateHeldInventory( scene );

		var go = scene.CreateObject();
		var weapon = go.Components.Create<BaseWeapon>();
		weapon.ClipMaxSize = 8;

		inventory.Add( weapon );

		Assert.AreEqual( 8, weapon.Clip1, "the magazine seeds full" );

		Assert.IsTrue( weapon.TakePrimaryAmmo( 8 ) );
		Assert.IsFalse( weapon.HasPrimaryAmmo(), "the magazine is empty" );
		Assert.IsTrue( weapon.CanReload(), "no ammo type means there's always reserve to load" );
	}

	/// <summary>
	/// Entering an inventory seeds the magazine full and the reserve pool with
	/// StartingAmmo - and a duplicate weapon donates its magazine to the reserve
	/// instead of joining.
	/// </summary>
	[TestMethod]
	public void PickupSeedsClipAndReserve()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventory = CreateHeldInventory( scene );
		var weapon = CreateWeapon( scene, clipSize: 8, startingAmmo: 24 );

		Assert.AreEqual( -1, weapon.Clip1, "no magazine before pickup" );

		inventory.Add( weapon );

		Assert.AreEqual( 8, weapon.Clip1, "the magazine seeds full" );
		Assert.AreEqual( 24, inventory.GetAmmo( TestAmmoResource.Pistol ), "the reserve seeds from StartingAmmo" );
		Assert.AreEqual( 24, weapon.Ammo1, "the weapon reads the pool through Ammo1" );

		var second = CreateWeapon( scene, startingAmmo: 24 );
		Assert.IsFalse( inventory.Add( second ), "a duplicate weapon is never added" );

		Assert.AreEqual( 32, inventory.GetAmmo( TestAmmoResource.Pistol ), "the duplicate donates its magazine to the reserve" );
		Assert.IsTrue( second.GameObject.IsDestroyed, "the duplicate is consumed by the donation" );
	}

	/// <summary>
	/// A weapon seeds the reserve once, at its first pickup - spending the pool to zero and
	/// dropping and re-taking the weapon isn't a free ammo source.
	/// </summary>
	[TestMethod]
	public void DropAndRePickupDoesNotReseedReserve()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventory = CreateHeldInventory( scene );
		var weapon = CreateWeapon( scene, clipSize: 8, startingAmmo: 24 );

		inventory.Add( weapon );
		inventory.SetAmmo( TestAmmoResource.Pistol, 0 );

		inventory.Drop( weapon );
		inventory.Add( weapon );

		Assert.AreEqual( 0, inventory.GetAmmo( TestAmmoResource.Pistol ), "re-pickup must not seed the reserve again" );
	}

	/// <summary>
	/// TakePrimaryAmmo spends from the magazine and refuses without changing anything
	/// when there isn't enough. A weapon with UsesAmmo off always fires free.
	/// </summary>
	[TestMethod]
	public void TakePrimaryAmmoSpendsClip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventory = CreateHeldInventory( scene );
		var weapon = CreateWeapon( scene, clipSize: 8 );
		inventory.Add( weapon );

		Assert.IsTrue( weapon.TakePrimaryAmmo( 3 ) );
		Assert.AreEqual( 5, weapon.Clip1 );

		Assert.IsFalse( weapon.TakePrimaryAmmo( 6 ), "an oversized spend refuses" );
		Assert.AreEqual( 5, weapon.Clip1, "and leaves the magazine untouched" );

		Assert.IsTrue( weapon.HasPrimaryAmmo() );
		weapon.TakePrimaryAmmo( 5 );
		Assert.AreEqual( 0, weapon.Clip1 );
		Assert.IsFalse( weapon.HasPrimaryAmmo(), "an empty magazine reports no ammo" );

		weapon.UsesAmmo = false;
		Assert.IsTrue( weapon.HasPrimaryAmmo(), "UsesAmmo off never runs dry" );
		Assert.IsTrue( weapon.TakePrimaryAmmo() );
	}

	/// <summary>
	/// A clipless weapon spends straight from the inventory's reserve pool.
	/// </summary>
	[TestMethod]
	public void CliplessWeaponSpendsReserve()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventory = CreateHeldInventory( scene );
		var weapon = CreateWeapon( scene );
		weapon.UsesClips = false;
		inventory.Add( weapon );

		inventory.SetAmmo( TestAmmoResource.Pistol, 3 );

		Assert.IsTrue( weapon.TakePrimaryAmmo() );
		Assert.AreEqual( 2, inventory.GetAmmo( TestAmmoResource.Pistol ), "the spend comes from the pool" );

		inventory.SetAmmo( TestAmmoResource.Pistol, 0 );
		Assert.IsFalse( weapon.HasPrimaryAmmo(), "an empty pool reports no ammo" );
		Assert.IsFalse( weapon.TakePrimaryAmmo() );
	}

	/// <summary>
	/// The reserve pool clamps and guards its arithmetic: negative grants are ignored,
	/// grants clamp to the type's MaxReserve, SetAmmo clamps to zero and TakeAmmo
	/// returns only what was actually there.
	/// </summary>
	[TestMethod]
	public void ReservePoolArithmetic()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventory = CreateHeldInventory( scene );
		var rifle = TestAmmoResource.Rifle;

		Assert.AreEqual( 0, inventory.GetAmmo( rifle ), "an unknown type is empty" );
		Assert.AreEqual( 0, inventory.GetAmmo( null ), "a null type is empty" );

		Assert.AreEqual( 10, inventory.GiveAmmo( rifle, 10 ), "a grant returns what was added" );
		Assert.AreEqual( 0, inventory.GiveAmmo( rifle, -5 ), "negative grants are ignored" );
		Assert.AreEqual( 10, inventory.GetAmmo( rifle ) );

		Assert.AreEqual( rifle.MaxReserve - 10, inventory.GiveAmmo( rifle, 9999 ), "grants clamp to the type's MaxReserve" );
		Assert.AreEqual( rifle.MaxReserve, inventory.GetAmmo( rifle ) );
		Assert.AreEqual( 0, inventory.GiveAmmo( rifle, 1 ), "a full pool takes nothing" );

		inventory.SetAmmo( rifle, -3 );
		Assert.AreEqual( 0, inventory.GetAmmo( rifle ), "SetAmmo clamps to zero" );

		inventory.SetAmmo( rifle, 4 );
		Assert.AreEqual( 4, inventory.TakeAmmo( rifle, 10 ), "a take returns only what exists" );
		Assert.AreEqual( 0, inventory.GetAmmo( rifle ) );
		Assert.AreEqual( 0, inventory.TakeAmmo( rifle, 1 ), "an empty pool gives nothing" );
	}

	/// <summary>
	/// Reload refills the magazine from the reserve pool over the reload time on the
	/// host, toggling IsReloading while it runs. CanReload refuses when full, empty of
	/// reserve, or already reloading.
	/// </summary>
	[TestMethod]
	public void ReloadRefillsClipFromReserve()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventory = CreateHeldInventory( scene );
		var weapon = CreateWeapon( scene, clipSize: 8 );
		weapon.ReloadTime = 0.05f;
		inventory.Add( weapon );
		inventory.SetAmmo( TestAmmoResource.Pistol, 10 );

		Assert.IsFalse( weapon.CanReload(), "a full magazine can't reload" );

		weapon.TakePrimaryAmmo( 6 );
		Assert.AreEqual( 2, weapon.Clip1 );
		Assert.IsTrue( weapon.CanReload() );

		using ( GameComponentTestUtils.PushSceneClock( scene ) )
		{
			// The reload runs on the weapon's task source, which only pumps while its GameObject is
			// enabled - equip it like a player would.
			inventory.Switch( weapon );
			weapon.Reload();
		}

		Assert.IsTrue( weapon.IsReloading, "the reload starts synchronously on the host" );
		Assert.IsFalse( weapon.CanReload(), "a running reload can't start another" );

		// The reload's delay sleeps in real time, then polls game time from the main thread queue -
		// pump that queue under the scene's clock (which GameTick advances) so the poll can see time
		// pass. In the real engine the frame loop does all of this.
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

		Assert.IsFalse( weapon.IsReloading, "the reload should finish" );
		Assert.AreEqual( 8, weapon.Clip1, "the magazine refills" );
		Assert.AreEqual( 4, inventory.GetAmmo( TestAmmoResource.Pistol ), "the rounds come out of the reserve" );

		inventory.SetAmmo( TestAmmoResource.Pistol, 0 );
		weapon.TakePrimaryAmmo( 1 );
		Assert.IsFalse( weapon.CanReload(), "no reserve means no reload" );
	}
}

/// <summary>
/// An ammo type built in code. Real ones load from disk and get their path from the asset
/// system - the reserve pool is keyed by that path, so tests set one directly.
/// </summary>
public sealed class TestAmmoResource : AmmoResource
{
	public static readonly TestAmmoResource Pistol = new( "test/pistol.ammo" );
	public static readonly TestAmmoResource Rifle = new( "test/rifle.ammo" );

	public TestAmmoResource( string path )
	{
		ResourcePath = path;
	}
}

/// <summary>
/// A weapon exposing the protected shooting API so tests can drive it directly.
/// </summary>
public sealed class TestWeapon : BaseWeapon
{
	public SceneTraceResult Shoot( float distance, float radius, float damage, float force, TagSet tags = null )
		=> ShootBullet( distance, radius, damage, force, tags );

	public SceneTraceResult[] ShootVolley( int count, Vector2 spread, float distance, float radius, float damage, float force )
		=> ShootBullets( count, spread, distance, radius, damage, force );

	public float PrimaryCooldown => NextPrimaryFire;
}

/// <summary>
/// Pins the host-side shooting path: bullet damage, attribution, hitbox tag merging,
/// physics impulse, volleys, the data-driven base attack and the purity of the attack
/// gates. The owner-client claim path needs a real network session, so it isn't
/// covered here - only the host resolution both paths share.
/// </summary>
[TestClass]
public class BaseWeaponShootingTest
{
	Connection _previousLocalConnection;
	NetworkSystem _previousNetworkSystem;

	/// <summary>
	/// Pins host networking state so instance shots resolve damage directly instead of
	/// trying to claim to a host. Same idiom as GameComponentTests.cs.
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
	/// Restores whatever global networking state existed before the test.
	/// </summary>
	[TestCleanup]
	public void RestoreNetworkingState()
	{
		Connection.Local = _previousLocalConnection;
		Networking.System = _previousNetworkSystem;
	}

	/// <summary>
	/// Creates a damageable target with a box hitbox, a damage counter and a
	/// non-falling rigidbody, so shots register through the hitbox path and impulses
	/// are observable.
	/// </summary>
	static (GameObject go, TriggerDamageTest.DamageCounter counter, Rigidbody body) CreateTarget( Scene scene, Vector3 position, float size = 50f, params string[] hitboxTags )
	{
		var go = scene.CreateObject();
		go.WorldPosition = position;

		// For a box hitbox CenterA is the centre and CenterB the full size.
		var hb = go.Components.Create<ManualHitbox>( false );
		hb.Shape = ManualHitbox.HitboxShape.Box;
		hb.CenterA = Vector3.Zero;
		hb.CenterB = new Vector3( size * 2f );

		foreach ( var tag in hitboxTags )
			hb.HitboxTags.Add( tag );

		hb.Enabled = true;

		var body = go.Components.Create<Rigidbody>();
		body.Gravity = false;

		// A small collider gives the body mass so impulses move it. It sits well inside the hitbox,
		// so traces still resolve through the hitbox (nearer face) and carry its tags.
		var collider = go.Components.Create<BoxCollider>();
		collider.Scale = new Vector3( 10 );

		var counter = go.Components.Create<TriggerDamageTest.DamageCounter>();
		return (go, counter, body);
	}

	/// <summary>
	/// Creates an unheld weapon at the origin facing +x, so its aim ray is its own
	/// forward.
	/// </summary>
	static TestWeapon CreateWeapon( Scene scene )
	{
		var go = scene.CreateObject();
		var weapon = go.Components.Create<TestWeapon>();
		return weapon;
	}

	/// <summary>
	/// A bullet damages what it hits through IDamageable with the weapon credited,
	/// merges the hitbox's tags into the damage (headshots), pushes the rigidbody
	/// along the shot, and reports the trace back to the caller.
	/// </summary>
	[TestMethod]
	public void ShootBulletDamagesAndTags()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var weapon = CreateWeapon( scene );
		var (targetGo, counter, body) = CreateTarget( scene, new Vector3( 200, 0, 0 ), hitboxTags: "head" );

		var tr = weapon.Shoot( distance: 500f, radius: 0f, damage: 25f, force: 5000f );

		Assert.IsTrue( tr.Hit, "the shot should hit the target" );
		Assert.AreEqual( targetGo, tr.GameObject );

		Assert.AreEqual( 1, counter.Events );
		Assert.AreEqual( 25f, counter.LastAmount, 0.01f );
		Assert.AreEqual( weapon.GameObject, counter.Last.Attacker, "an unheld weapon credits itself" );
		Assert.AreEqual( weapon.GameObject, counter.Last.Weapon );
		Assert.IsTrue( counter.Last.Tags.Has( "head" ), "the hitbox tags merge into the damage" );
		Assert.IsNotNull( counter.Last.Hitbox, "a host-side shot carries the hitbox" );

		Assert.IsTrue( body.Velocity.x > 0f, $"the impulse pushes the target along the shot: {body.Velocity}" );
	}

	/// <summary>
	/// A shot past everything misses without damaging anyone, and a shot short of the
	/// target is out of range.
	/// </summary>
	[TestMethod]
	public void ShootBulletRespectsRangeAndMisses()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var weapon = CreateWeapon( scene );
		var (_, counter, _) = CreateTarget( scene, new Vector3( 200, 0, 0 ) );

		var shortShot = weapon.Shoot( distance: 100f, radius: 0f, damage: 25f, force: 0f );
		Assert.IsFalse( shortShot.Hit, "the target is beyond the shot's range" );
		Assert.AreEqual( 0, counter.Events );

		weapon.GameObject.WorldRotation = Rotation.FromYaw( 180f );
		var missed = weapon.Shoot( distance: 500f, radius: 0f, damage: 25f, force: 0f );
		Assert.IsFalse( missed.Hit, "shooting the other way misses" );
		Assert.AreEqual( 0, counter.Events );
	}

	/// <summary>
	/// A volley fires one trace per pellet and every pellet that hits deals its own
	/// damage.
	/// </summary>
	[TestMethod]
	public void ShootBulletsFiresEveryPellet()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var weapon = CreateWeapon( scene );
		var (_, counter, _) = CreateTarget( scene, new Vector3( 200, 0, 0 ), size: 150f );

		var pellets = weapon.ShootVolley( 8, new Vector2( 2f, 2f ), 500f, 0f, 5f, 0f );

		Assert.AreEqual( 8, pellets.Length );
		Assert.IsTrue( pellets.All( p => p.Hit ), "a huge target catches every pellet of a tight volley" );
		Assert.AreEqual( 8, counter.Events, "every pellet deals its own damage" );
		Assert.AreEqual( 5f, counter.LastAmount, 0.01f );
	}

	/// <summary>
	/// The data-driven base PrimaryAttack fires the configured ballistics: pellet
	/// count, damage and range, spending no ammo while unheld.
	/// </summary>
	[TestMethod]
	public void BasePrimaryAttackFiresBallistics()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var weapon = CreateWeapon( scene );
		weapon.Ballistics = new BaseWeapon.BallisticConfig
		{
			Damage = 12f,
			Pellets = 4,
			Range = 500f,
			Radius = 0f,
			Force = 0f,
			SpreadBase = new Vector2( 1f, 1f ),
			SpreadGrowth = Vector2.Zero,
		};

		var (_, counter, _) = CreateTarget( scene, new Vector3( 200, 0, 0 ), size: 150f );

		weapon.PrimaryAttack();

		Assert.AreEqual( 4, counter.Events, "the attack fires the configured pellet count" );
		Assert.AreEqual( 12f, counter.LastAmount, 0.01f );
		Assert.AreEqual( -1, weapon.Clip1, "an unheld weapon spends nothing" );
	}

	/// <summary>
	/// CanPrimaryAttack is a pure check - asking an empty weapon doesn't dry-fire,
	/// start a cooldown or kick off a reload, no matter how often a HUD asks.
	/// </summary>
	[TestMethod]
	public void CanPrimaryAttackHasNoSideEffects()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var inventoryGo = scene.CreateObject();
		inventoryGo.Components.Create<PlayerController>();
		var inventory = inventoryGo.Components.Create<InventoryComponent>();

		var weaponGo = scene.CreateObject();
		var weapon = weaponGo.Components.Create<TestWeapon>();
		weapon.ClipMaxSize = 4;
		weapon.PrimaryAmmoType = TestAmmoResource.Pistol;
		inventory.Add( weapon );
		inventory.SetAmmo( TestAmmoResource.Pistol, 20 );

		weapon.TakePrimaryAmmo( 4 );
		Assert.IsFalse( weapon.HasPrimaryAmmo() );

		var cooldown = weapon.PrimaryCooldown;

		for ( int i = 0; i < 10; i++ )
			Assert.IsFalse( weapon.CanPrimaryAttack(), "an empty weapon can't fire" );

		Assert.AreEqual( cooldown, weapon.PrimaryCooldown, 0.001f, "asking must not start a cooldown" );
		Assert.IsFalse( weapon.IsReloading, "asking must not start a reload" );
	}

	/// <summary>
	/// SpreadScale multiplies the spread cone - the lever NPCs use to be worse shots than
	/// players (weapon NpcUsage scale times NPC skill).
	/// </summary>
	[TestMethod]
	public void SpreadScaleWidensTheCone()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var weaponGo = scene.CreateObject();
		var weapon = weaponGo.Components.Create<TestWeapon>();

		var settled = weapon.CurrentSpread;

		weapon.SpreadScale = 3f;
		Assert.AreEqual( settled * 3f, weapon.CurrentSpread, "the scale multiplies the cone" );

		weapon.SpreadScale = 1f;
		Assert.AreEqual( settled, weapon.CurrentSpread );
	}
}
