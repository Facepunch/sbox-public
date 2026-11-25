using Sandbox.Citizen;

public sealed partial class PlayerInventory : BaseInventoryComponent, Local.IPlayerEvents
{
	// Sandbox events, notices and undo handling use the engine inventory's hooks.

	[RequireComponent] public Player Player { get; set; }

	/// <summary>
	/// All weapons currently in the inventory, ordered by slot. Narrowing shim over engine Items.
	/// </summary>
	public IEnumerable<BaseCombatWeapon> Weapons => Items.OfType<BaseCombatWeapon>();

	/// <summary>
	/// The currently active weapon. Narrowing shim over the engine's <see cref="BaseInventoryComponent.ActiveItem"/>.
	/// </summary>
	public BaseCombatWeapon ActiveWeapon => ActiveItem as BaseCombatWeapon;

	/// <summary>
	/// Returns the weapon in the given slot, or null if the slot is empty.
	/// </summary>
	public new BaseCombatWeapon GetSlot( int slot ) => base.GetSlot( slot ) as BaseCombatWeapon;

	/// <summary>
	/// A weapon of the same class we already carry, if any. Duplicate handling itself is the
	/// engine's (a duplicate donates its magazine to the reserve, see BaseCombatWeapon.OnAdding) - this
	/// just finds the weapon the donation lands on, for the pickup notices.
	/// </summary>
	private BaseCombatWeapon FindExistingWeapon( BaseCombatWeapon like )
	{
		if ( !like.IsValid() )
			return null;

		// Same rule as the engine's duplicate check. A prefab template names its prefab through its scene, not PrefabInstanceSource.
		var source = like.GameObject.PrefabInstanceSource;
		if ( string.IsNullOrEmpty( source ) && like.GameObject is PrefabScene template )
			source = template.Source?.ResourcePath;

		return Weapons.FirstOrDefault( x => x.GetType() == like.GetType() && x.GameObject.PrefabInstanceSource == source );
	}

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
		if ( Rpc.Caller != Network.Owner ) return;

		SetToolMode( toolModeName );
	}

	public bool Pickup( string prefabName, bool notice = true ) => Pickup( prefabName, -1, notice );

	public bool HasWeapon( GameObject prefab )
	{
		var baseCarry = prefab.GetComponent<BaseCombatWeapon>( true );
		if ( !baseCarry.IsValid() )
			return false;

		return Weapons.Where( x => x.GetType() == baseCarry.GetType() )
			.FirstOrDefault()
			.IsValid();
	}

	public bool HasWeapon<T>() where T : BaseCombatWeapon
	{
		return GetWeapon<T>().IsValid();
	}

	public T GetWeapon<T>() where T : BaseCombatWeapon => GetItem<T>();

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
		var existing = FindExistingWeapon( prefab.GetComponent<BaseCombatWeapon>( true ) );
		var ammoBefore = existing.IsValid() ? existing.Ammo1 : 0;

		// The engine destroys the clone if pickup is refused.
		if ( base.Pickup( prefab, targetSlot ) is BaseCombatWeapon weapon )
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

	public bool Take( BaseCombatWeapon item, bool includeNotices )
	{
		if ( !item.IsValid() )
			return false;

		var existing = FindExistingWeapon( item );
		var ammoBefore = existing.IsValid() ? existing.Ammo1 : 0;

		// Add can consume a duplicate as an ammo donation.
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

		if ( item is not BaseCombatWeapon weapon )
			return;

		// A weapon wired into a contraption stays put, as a sandbox weapon refuses itself
		if ( weapon.GetComponent<WeaponControls>() is { IsWired: true } )
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
		if ( item is not BaseCombatWeapon weapon )
			return true;

		var pickupEvent = new PlayerPickupEvent { Player = Player, Weapon = weapon, Slot = slot };
		Local.IPlayerEvents.PostToGameObject( Player.GameObject, e => e.OnPickup( pickupEvent ) );
		Global.IPlayerEvents.Post( e => e.OnPlayerPickup( pickupEvent ) );

		return !pickupEvent.Cancelled;
	}

	protected override void OnItemDropped( BaseInventoryItem item )
	{
		if ( item is BaseCombatWeapon weapon )
			WeaponControls.PrepareDroppedWeapon( weapon, Player.Network.Owner );
	}

	protected override bool OnDropping( Sandbox.BaseInventoryItem item )
	{
		if ( item is not BaseCombatWeapon weapon )
			return true;

		var dropEvent = new PlayerDropEvent { Player = Player, Weapon = weapon };
		Local.IPlayerEvents.PostToGameObject( Player.GameObject, e => e.OnDrop( dropEvent ) );
		Global.IPlayerEvents.Post( e => e.OnPlayerDrop( dropEvent ) );

		return !dropEvent.Cancelled;
	}

	private static SoundEvent AmmoPickupSound = ResourceLibrary.Get<SoundEvent>( "sounds/weapons/ammo_pickup.sound" );
	private static SoundEvent GunPickupSound = ResourceLibrary.Get<SoundEvent>( "sounds/weapons/gun_pickup.sound" );

	[Rpc.Owner]
	private void OnClientPickup( BaseCombatWeapon weapon, bool justAmmo = false, bool given = false )
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
	private bool ShouldAutoswitchTo( BaseCombatWeapon item, bool given = false )
	{
		Assert.True( item.IsValid(), "item invalid" );

		if ( !ActiveWeapon.IsValid() )
			return true;

		if ( !GamePreferences.AutoSwitch )
			return false;

		if ( ActiveWeapon is BaseSandboxWeapon { } active && active.IsInUse() )
			return false;

		if ( given )
			return true;

		if ( item.ShouldAvoid )
			return false;

		return item.Value > ActiveWeapon.Value;
	}

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
	public BaseCombatWeapon GetBestWeapon() => GetBestItem() as BaseCombatWeapon;

	/// <summary>
	/// Asks whether this player may switch to <paramref name="weapon"/> (null means holster). Checks the
	/// weapon's own <see cref="Sandbox.BaseInventoryItem.CanSwitchTo"/>, then fires the cancellable
	/// switch event on the player and scene-wide, so addons can refuse. Runs wherever it's called - the
	/// hotbar uses it client-side to filter and <see cref="SwitchWeapon"/> uses it as the gate.
	/// The inventory also enforces seat holstering on the host.
	/// </summary>
	public bool CanSwitchTo( BaseCombatWeapon weapon )
	{
		if ( weapon.IsValid() && !weapon.CanSwitchTo() )
			return false;

		if ( !OnSwitching( weapon ) )
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
	public void SwitchWeapon( BaseCombatWeapon weapon, bool allowHolster = false )
	{
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
		if ( Rpc.Caller != Network.Owner ) return;
		if ( !ActiveWeapon.IsValid() ) return;
		Drop( ActiveWeapon );
	}

	/// <summary>
	/// Removes a weapon from the inventory and destroys it without dropping it into the world. The
	/// engine holsters it, destroys it and switches to the best remaining weapon. The cancellable
	/// remove event fires from <see cref="OnRemoving"/>.
	/// </summary>
	public void Remove( BaseCombatWeapon weapon )
	{
		if ( weapon.IsValid() && weapon.Owner != Player.Controller )
			return;

		base.Remove( weapon );
	}

	protected override bool OnRemoving( Sandbox.BaseInventoryItem item )
	{
		if ( item is not BaseCombatWeapon weapon )
			return true;

		var removeEvent = new PlayerRemoveWeaponEvent { Player = Player, Weapon = weapon };
		Local.IPlayerEvents.PostToGameObject( Player.GameObject, e => e.OnRemoveWeapon( removeEvent ) );
		Global.IPlayerEvents.Post( e => e.OnPlayerRemoveWeapon( removeEvent ) );

		return !removeEvent.Cancelled;
	}

	void Local.IPlayerEvents.OnDied( PlayerDiedParams args )
	{
		if ( ActiveWeapon is BaseSandboxWeapon weapon && weapon.IsValid() )
		{
			weapon.OnPlayerDeath( args );
		}
	}

	void Local.IPlayerEvents.OnJump()
	{
		if ( ActiveWeapon is BaseSandboxWeapon weapon && weapon.IsValid() )
		{
			weapon.OnJump();
		}
	}

	void Local.IPlayerEvents.OnCameraMove( ref Angles angles )
	{
		if ( ActiveWeapon is not BaseSandboxWeapon weapon || !weapon.IsValid() ) return;

		weapon.OnCameraMove( Player, ref angles );
	}

	void Local.IPlayerEvents.OnCameraPostSetup( Sandbox.CameraComponent camera )
	{
		if ( ActiveWeapon is not BaseSandboxWeapon weapon || !weapon.IsValid() ) return;

		weapon.OnCameraSetup( Player, camera );
	}
}
