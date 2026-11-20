using Sandbox.Citizen;

public sealed partial class PlayerInventory : BaseInventoryComponent, Local.IPlayerEvents
{
	// MaxSlots, ActiveItem, the active-item enable/disable + equip/holster and the add/remove/drop/
	// move-slot flows come from the engine BaseInventoryComponent. This adds the sandbox's events, ammo
	// merging, notices and undo handling through the engine's hooks.

	[RequireComponent] public Player Player { get; set; }

	/// <summary>
	/// All weapons currently in the inventory, ordered by slot. Narrowing shim over engine Items.
	/// </summary>
	public IEnumerable<BaseSandboxWeapon> Weapons => Items.OfType<BaseSandboxWeapon>();

	/// <summary>
	/// The currently active weapon. Narrowing shim over the engine's <see cref="BaseInventoryComponent.ActiveItem"/>.
	/// </summary>
	public BaseSandboxWeapon ActiveWeapon => ActiveItem as BaseSandboxWeapon;

	/// <summary>
	/// Returns the weapon in the given slot, or null if the slot is empty.
	/// </summary>
	public new BaseSandboxWeapon GetSlot( int slot ) => base.GetSlot( slot ) as BaseSandboxWeapon;

	/// <summary>
	/// A weapon of the same class we already carry, if any. Duplicate handling itself is the
	/// engine's (a duplicate donates its magazine to the reserve, see BaseCombatWeapon.OnAdding) - this
	/// just finds the weapon the donation lands on, for the pickup notices.
	/// </summary>
	private BaseSandboxWeapon FindExistingWeapon( BaseSandboxWeapon like )
		=> like.IsValid() ? Weapons.FirstOrDefault( x => x.GetType() == like.GetType() ) : null;

	// FindEmptySlot is inherited from the engine BaseInventoryComponent.

	// The default weapons come from the engine's Loadout feature, configured on the player prefab -
	// PlayerLoadout calls GiveLoadout() when there's no saved hotbar to restore.

	/// <summary>
	/// Activates the named tool mode, giving and equipping the toolgun first if the player doesn't have one.
	/// </summary>
	public void SetToolMode( string name )
	{
		if ( !Networking.IsHost )
		{
			HostSetToolMode( name );
			return;
		}

		if ( !HasWeapon<Toolgun>() )
		{
			Pickup( "weapons/toolgun/toolgun.prefab", false );
		}

		var toolGun = GetWeapon<Toolgun>();
		if ( !toolGun.IsValid() ) 
			return;

		SwitchWeapon( toolGun );
		toolGun.SetToolMode( name );
	}

	[Rpc.Host]
	private void HostSetToolMode( string toolModeName )
	{
		SetToolMode( toolModeName );
	}

	public bool Pickup( string prefabName, bool notice = true ) => Pickup( prefabName, -1, notice );

	public bool HasWeapon( GameObject prefab )
	{
		var baseCarry = prefab.GetComponent<BaseSandboxWeapon>( true );
		if ( !baseCarry.IsValid() )
			return false;

		return Weapons.Where( x => x.GetType() == baseCarry.GetType() )
			.FirstOrDefault()
			.IsValid();
	}

	public bool HasWeapon<T>() where T : BaseSandboxWeapon
	{
		return GetWeapon<T>().IsValid();
	}

	public T GetWeapon<T>() where T : BaseSandboxWeapon
	{
		// Same pick as the first of Items (lowest slot, then slot order), without sorting them all
		T best = null;
		foreach ( var weapon in GetComponentsInChildren<T>( true ) )
		{
			if ( weapon.GameObject.IsDestroyed || weapon.Inventory != this ) continue;
			if ( best is not null && (weapon.Slot, weapon.SlotOrder).CompareTo( (best.Slot, best.SlotOrder) ) >= 0 ) continue;

			best = weapon;
		}

		return best;
	}

	public bool Pickup( GameObject prefab, bool notice = true ) => Pickup( prefab, -1, notice );

	public bool Pickup( string prefabName, int targetSlot, bool notice = true )
	{
		if ( !Networking.IsHost )
			return false;

		var prefab = GameObject.GetPrefab( prefabName );
		if ( prefab is null )
		{
			Log.Warning( $"Prefab not found: {prefabName}" );
			return false;
		}

		return Pickup( prefab, targetSlot, notice );
	}

	public bool Pickup( GameObject prefab, int targetSlot, bool notice = true )
	{
		if ( !Networking.IsHost )
			return false;

		// The engine consumes a duplicate as an ammo donation (see BaseCombatWeapon.OnAdding) - watch the
		// pool so the ammo notice can fire.
		var existing = FindExistingWeapon( prefab.GetComponent<BaseSandboxWeapon>( true ) );
		var ammoBefore = existing.IsValid() ? existing.Ammo1 : 0;

		// Engine pickup: clone, network spawn, parent, slot, ownership. The cancellable pickup event
		// fires from OnAdding; the engine destroys the clone if it's refused.
		if ( base.Pickup( prefab, targetSlot ) is BaseSandboxWeapon weapon )
		{
			if ( notice )
				OnClientPickup( weapon, given: true );

			return true;
		}

		// Refused as a duplicate - donated or already topped up, either way it counts as taken.
		if ( existing.IsValid() )
		{
			if ( notice && existing.Ammo1 > ammoBefore )
			{
				NotifyAmmoPickup( existing.PrimaryAmmoType, existing.Ammo1 - ammoBefore );
				OnClientPickup( existing, true );
			}

			return true;
		}

		return false;
	}

	public bool Take( BaseSandboxWeapon item, bool includeNotices )
	{
		if ( !item.IsValid() )
			return false;

		var existing = FindExistingWeapon( item );
		var ammoBefore = existing.IsValid() ? existing.Ammo1 : 0;

		// Engine add: parent, slot, ownership, disable. The cancellable pickup event fires from
		// OnAdding, and a duplicate donates its magazine to the reserve and is consumed there.
		if ( Add( item ) )
		{
			// Remove from undo stacks so the weapon can't be undone out of our hands
			UndoSystem.Current.Remove( item.GameObject );

			if ( includeNotices )
				OnClientPickup( item );

			return true;
		}

		// Consumed by the donation - that's a take too. A refused item stays in the world.
		if ( item.GameObject.IsDestroyed )
		{
			if ( includeNotices && existing.IsValid() && existing.Ammo1 > ammoBefore )
			{
				NotifyAmmoPickup( existing.PrimaryAmmoType, existing.Ammo1 - ammoBefore );
				OnClientPickup( existing, true );
			}

			return true;
		}

		return false;
	}

	/// <summary>
	/// Engine Touch pickup lands here (see <see cref="BaseInventoryComponent.PickupMode"/>). Routes into
	/// <see cref="Take"/>, so duplicates donate their ammo and the pickup notices fire. Contraption-
	/// wired weapons refuse themselves (see <see cref="BaseSandboxWeapon"/>'s OnCanPickup).
	/// </summary>
	public override void PickupWorldItem( Sandbox.BaseInventoryItem item )
	{
		if ( !Networking.IsHost )
		{
			base.PickupWorldItem( item );
			return;
		}

		if ( item is not BaseSandboxWeapon weapon )
			return;

		if ( !CanPickupWorldItem( weapon ) )
			return;

		Take( weapon, true );
	}

	/// <summary>
	/// Fires the cancellable pickup events before the engine adds an item.
	/// </summary>
	protected override bool OnAdding( Sandbox.BaseInventoryItem item, int slot )
	{
		if ( item is not BaseSandboxWeapon weapon )
			return true;

		var pickupEvent = new PlayerPickupEvent { Player = Player, Weapon = weapon, Slot = slot };
		Local.IPlayerEvents.PostToGameObject( Player.GameObject, e => e.OnPickup( pickupEvent ) );
		Global.IPlayerEvents.Post( e => e.OnPlayerPickup( pickupEvent ) );

		return !pickupEvent.Cancelled;
	}

	/// <summary>
	/// Drops the given weapon from the inventory. The engine holsters it, the weapon throws itself
	/// into the world (see <see cref="BaseSandboxWeapon.OnDrop"/>) and we switch to the best remaining
	/// weapon. The cancellable drop event fires from <see cref="OnDropping"/>.
	/// </summary>
	public void Drop( BaseSandboxWeapon weapon )
	{
		if ( weapon.IsValid() && weapon.Owner != Player )
			return;

		base.Drop( weapon );
	}

	protected override bool OnDropping( Sandbox.BaseInventoryItem item )
	{
		if ( item is not BaseSandboxWeapon weapon )
			return true;

		var dropEvent = new PlayerDropEvent { Player = Player, Weapon = weapon };
		Local.IPlayerEvents.PostToGameObject( Player.GameObject, e => e.OnDrop( dropEvent ) );
		Global.IPlayerEvents.Post( e => e.OnPlayerDrop( dropEvent ) );

		return !dropEvent.Cancelled;
	}

	private static SoundEvent AmmoPickupSound = ResourceLibrary.Get<SoundEvent>( "sounds/weapons/ammo_pickup.sound" );
	private static SoundEvent GunPickupSound = ResourceLibrary.Get<SoundEvent>( "sounds/weapons/gun_pickup.sound" );

	[Rpc.Owner]
	private void OnClientPickup( BaseSandboxWeapon weapon, bool justAmmo = false, bool given = false )
	{
		if ( !weapon.IsValid() ) return;

		if ( !justAmmo && ShouldAutoswitchTo( weapon, given ) )
		{
			SwitchWeapon( weapon );
		}

		if ( Player.IsLocalPlayer )
		{
			GameObject.PlaySound( justAmmo ? AmmoPickupSound : GunPickupSound );
			Global.IPlayerEvents.Post( e => e.OnPlayerPickup( new PlayerPickupEvent { Player = Player, Weapon = weapon, Slot = weapon.Slot } ) );
		}
	}

	/// <summary>
	/// Whether picking up <paramref name="item"/> should switch to it. <paramref name="given"/> means it was handed over explicitly
	/// (spawn menu, give): that still honours AutoSwitch and a weapon in use, but skips the value comparison.
	/// </summary>
	private bool ShouldAutoswitchTo( BaseSandboxWeapon item, bool given = false )
	{
		Assert.True( item.IsValid(), "item invalid" );

		if ( !ActiveWeapon.IsValid() )
			return true;

		if ( !GamePreferences.AutoSwitch )
			return false;

		if ( ActiveWeapon.IsInUse() )
			return false;

		if ( given )
			return true;

		// Nothing to fire or load - the engine flags spent guns.
		if ( item.ShouldAvoid )
			return false;

		return item.Value > ActiveWeapon.Value;
	}

	// MoveSlot comes from the engine BaseInventoryComponent - the cancellable move event fires from this
	// hook.
	protected override bool OnMovingSlot( int fromSlot, int toSlot )
	{
		var moveEvent = new PlayerMoveSlotEvent { Player = Player, FromSlot = fromSlot, ToSlot = toSlot };
		Local.IPlayerEvents.PostToGameObject( Player.GameObject, e => e.OnMoveSlot( moveEvent ) );
		Global.IPlayerEvents.Post( e => e.OnPlayerMoveSlot( moveEvent ) );

		return !moveEvent.Cancelled;
	}

	/// <summary>
	/// The weapon the inventory would auto-switch to. The engine's <see cref="BaseInventoryComponent.GetBestItem"/>
	/// handles the Value ordering and the avoid-empty-guns fallback.
	/// </summary>
	public BaseSandboxWeapon GetBestWeapon() => GetBestItem() as BaseSandboxWeapon;

	/// <summary>
	/// Asks whether this player may switch to <paramref name="weapon"/> (null means holster). Checks the
	/// weapon's own <see cref="Sandbox.BaseInventoryItem.CanSwitchTo"/>, then fires the cancellable
	/// switch event on the player and scene-wide, so addons can refuse. Runs wherever it's called - the
	/// hotbar uses it client-side to filter, <see cref="SwitchWeapon"/> uses it as the gate. Voluntary
	/// switches are therefore vetoed on the initiating client; the engine's host path isn't hookable.
	/// </summary>
	public bool CanSwitchTo( BaseSandboxWeapon weapon )
	{
		if ( weapon.IsValid() && !weapon.CanSwitchTo() )
			return false;

		var switchEvent = new PlayerSwitchWeaponEvent { Player = Player, From = ActiveWeapon, To = weapon };
		Local.IPlayerEvents.PostToGameObject( Player.GameObject, e => e.OnSwitchWeapon( switchEvent ) );
		Global.IPlayerEvents.Post( e => e.OnPlayerSwitchWeapon( switchEvent ) );

		return !switchEvent.Cancelled;
	}

	/// <summary>
	/// Switches to the given weapon after asking <see cref="CanSwitchTo"/>. Wraps the engine inventory's
	/// <see cref="BaseInventoryComponent.Switch"/>, which handles host-routing and the outgoing weapon's
	/// holster veto. Forced switches (death, drop, removal) don't come through here and can't be vetoed.
	/// </summary>
	public void SwitchWeapon( BaseSandboxWeapon weapon, bool allowHolster = false )
	{
		// Re-selecting the active weapon is a no-op unless it's a holster toggle
		if ( weapon.IsValid() && weapon == ActiveWeapon && !allowHolster )
			return;

		// Toggling the active slot holsters, so the event sees that as switching to nothing
		var to = weapon == ActiveWeapon ? null : weapon;
		if ( !CanSwitchTo( to ) )
			return;

		Switch( weapon, allowHolster );
	}

	public void OnControl()
	{
		if ( Input.Pressed( "drop" ) && ActiveWeapon.IsValid() )
			DropActiveWeapon();
	}

	/// <summary>
	/// Called by the owning client to drop their currently held weapon.
	/// </summary>
	[Rpc.Host]
	private void DropActiveWeapon()
	{
		if ( !ActiveWeapon.IsValid() ) return;
		Drop( ActiveWeapon );
	}

	/// <summary>
	/// Removes a weapon from the inventory and destroys it without dropping it into the world. The
	/// engine holsters it, destroys it and switches to the best remaining weapon. The cancellable
	/// remove event fires from <see cref="OnRemoving"/>.
	/// </summary>
	public void Remove( BaseSandboxWeapon weapon )
	{
		if ( weapon.IsValid() && weapon.Owner != Player )
			return;

		base.Remove( weapon );
	}

	protected override bool OnRemoving( Sandbox.BaseInventoryItem item )
	{
		if ( item is not BaseSandboxWeapon weapon )
			return true;

		var removeEvent = new PlayerRemoveWeaponEvent { Player = Player, Weapon = weapon };
		Local.IPlayerEvents.PostToGameObject( Player.GameObject, e => e.OnRemoveWeapon( removeEvent ) );
		Global.IPlayerEvents.Post( e => e.OnPlayerRemoveWeapon( removeEvent ) );

		return !removeEvent.Cancelled;
	}

	void Local.IPlayerEvents.OnDied( PlayerDiedParams args )
	{
		if ( ActiveWeapon.IsValid() )
		{
			ActiveWeapon.OnPlayerDeath( args );
		}
	}

	void Local.IPlayerEvents.OnJump()
	{
		if ( ActiveWeapon.IsValid() )
		{
			ActiveWeapon.OnJump();
		}
	}

	void Local.IPlayerEvents.OnCameraMove( ref Angles angles )
	{
		if ( !ActiveWeapon.IsValid() ) return;

		ActiveWeapon.OnCameraMove( Player, ref angles );
	}

	void Local.IPlayerEvents.OnCameraPostSetup( Sandbox.CameraComponent camera )
	{
		if ( !ActiveWeapon.IsValid() ) return;

		ActiveWeapon.OnCameraSetup( Player, camera );
	}
}
