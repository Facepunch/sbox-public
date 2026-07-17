namespace Sandbox;

public partial class BaseInventoryComponent
{
	/// <summary>
	/// Adds an already-spawned item to the inventory. Slot -1 picks one: the item's
	/// <see cref="BaseInventoryItem.PreferredSlot"/> when free, otherwise the first empty slot
	/// (buckets always take the preference). Host only. The item is reparented under this inventory
	/// and disabled until switched to.
	/// </summary>
	public bool Add( BaseInventoryItem item, int slot = -1 )
	{
		if ( !Networking.IsHost )
			return false;

		if ( !item.IsValid() )
			return false;

		// Can't lift an item out of another inventory - that's what Transfer is for.
		if ( item.Inventory is { } current && current != this )
			return false;

		// The item gets a say first - a duplicate weapon donates its ammo and refuses (see
		// BaseInventoryItem.OnAdding), which mustn't depend on a free slot.
		if ( !item.Adding( this ) )
			return false;

		if ( slot < 0 )
			slot = PickSlotFor( item );

		if ( slot < 0 || slot >= MaxSlots )
			return false;

		// Hotbar slots are exclusive; buckets share.
		if ( Behaviour == InventoryBehaviour.Hotbar && GetSlot( slot ).IsValid() )
			return false;

		if ( !OnAdding( item, slot ) )
			return false;

		item.GameObject.SetParent( GameObject, false );
		item.GameObject.LocalTransform = global::Transform.Zero;
		item.GameObject.Enabled = false;
		item.Slot = slot;

		// Keep the item owned by whoever owns the inventory.
		if ( Network.Owner is not null )
			item.GameObject.Network.AssignOwnership( Network.Owner );

		item.Added( this );
		OnItemAdded( item );
		return true;
	}

	// Where an item lands when added without an explicit slot.
	private int PickSlotFor( BaseInventoryItem item )
	{
		// Buckets always take the preference - they never fill up.
		if ( Behaviour == InventoryBehaviour.Buckets )
			return MaxSlots > 0 ? Math.Clamp( item.PreferredSlot, 0, MaxSlots - 1 ) : -1;

		// Hotbar: the preference wins if it's free, otherwise first empty.
		if ( item.PreferredSlot >= 0 && item.PreferredSlot < MaxSlots && !GetSlot( item.PreferredSlot ).IsValid() )
			return item.PreferredSlot;

		return FindEmptySlot();
	}

	/// <summary>
	/// Spawns an item from a prefab and adds it to the inventory in the given slot (-1 picks one,
	/// see <see cref="Add"/>). Host only. Returns the spawned item, or null if it couldn't be added
	/// (no room, no <see cref="BaseInventoryItem"/> on the prefab, slot taken).
	/// </summary>
	public BaseInventoryItem Pickup( GameObject prefab, int slot = -1 )
	{
		if ( !Networking.IsHost )
			return null;

		if ( prefab is null )
			return null;

		var clone = prefab.Clone( new CloneConfig { Parent = GameObject, StartEnabled = false } );
		clone.NetworkSpawn( false, Network.Owner );

		var item = clone.GetComponent<BaseInventoryItem>( true );
		if ( !item.IsValid() || !Add( item, slot ) )
		{
			clone.Destroy();
			return null;
		}

		return item;
	}

	/// <inheritdoc cref="Pickup(GameObject, int)"/>
	public BaseInventoryItem Pickup( string prefabPath, int slot = -1 )
	{
		var prefab = GameObject.GetPrefab( prefabPath );
		if ( prefab is null )
		{
			Log.Warning( $"Inventory.Pickup: prefab not found: {prefabPath}" );
			return null;
		}

		return Pickup( prefab, slot );
	}

	/// <summary>
	/// Removes an item from the inventory and destroys it, then switches to the best remaining item.
	/// Routed through the host.
	/// </summary>
	public void Remove( BaseInventoryItem item )
	{
		if ( !Networking.IsHost )
		{
			HostRemove( item );
			return;
		}

		if ( !item.IsValid() || item.Inventory != this )
			return;

		if ( !OnRemoving( item ) )
			return;

		if ( ActiveItem == item )
			ForceSwitch( null, true );

		item.Removed( this );
		item.GameObject.Destroy();

		SwitchToBest();
	}

	/// <summary>
	/// Drops an item out of the inventory and into the world. Holsters it first if it's active, asks
	/// the item to place itself (see <see cref="BaseInventoryItem.Drop"/>), then switches to the best
	/// remaining item. Routed through the host.
	/// </summary>
	public void Drop( BaseInventoryItem item )
	{
		if ( !Networking.IsHost )
		{
			HostDrop( item );
			return;
		}

		if ( !item.IsValid() || item.Inventory != this )
			return;

		if ( !OnDropping( item ) )
			return;

		// Dropping force-holsters: the holster gate must not be able to block a drop. The item's own
		// OnDrop is the gate for refusing a drop.
		var wasActive = ActiveItem == item;
		if ( wasActive )
			ForceSwitch( null, true );

		// The item may refuse to be dropped - if so, put it back the way it was.
		if ( !item.Drop() )
		{
			if ( wasActive )
				ForceSwitch( item );

			return;
		}

		// A drop is a removal too.
		item.Removed( this );

		// Don't let Touch pickup vacuum it straight back up.
		_lastDropped = item;
		_sinceDropped = 0;

		SwitchToBest();
	}

	/// <summary>
	/// Moves an item from this inventory into another - no world drop, no destroy. The usual gates
	/// get a say (<see cref="OnRemoving"/> here, <see cref="OnAdding"/> and the item's own say
	/// there); any refusal leaves everything as it was. Reserve ammo stays behind - the pool lives
	/// on the inventory, not the item. The destination doesn't auto-deploy (it may be a chest).
	/// Host only - who may move items between which inventories is game policy, so games route
	/// their own requests here. Returns whether the item moved.
	/// </summary>
	public bool Transfer( BaseInventoryItem item, BaseInventoryComponent to, int slot = -1 )
	{
		if ( !Networking.IsHost )
			return false;

		if ( !item.IsValid() || item.Inventory != this )
			return false;

		if ( !to.IsValid() || to == this )
			return false;

		// Resolve and validate the destination slot before anything changes.
		if ( slot < 0 )
			slot = to.PickSlotFor( item );

		if ( slot < 0 || slot >= to.MaxSlots )
			return false;

		if ( to.Behaviour == InventoryBehaviour.Hotbar && to.GetSlot( slot ).IsValid() )
			return false;

		// Gates - ours, the destination's, then the item's own say last, because it can have side
		// effects (a duplicate weapon donates its ammo and is consumed).
		if ( !OnRemoving( item ) )
			return false;

		if ( !to.OnAdding( item, slot ) )
			return false;

		if ( !item.Adding( to ) )
		{
			// The item's gate may have consumed it - don't leave a dead item deployed.
			if ( !item.IsValid() && ActiveItem == item )
			{
				ForceSwitch( null, true );
				SwitchToBest();
			}

			return false;
		}

		if ( ActiveItem == item )
			ForceSwitch( null, true );

		item.GameObject.SetParent( to.GameObject, false );
		item.GameObject.LocalTransform = global::Transform.Zero;
		item.GameObject.Enabled = false;
		item.Slot = slot;

		// Owned by whoever owns the destination - or nobody, for a chest.
		if ( to.Network.Owner is not null )
			item.GameObject.Network.AssignOwnership( to.Network.Owner );
		else
			item.GameObject.Network.DropOwnership();

		item.Removed( this );
		item.Added( to );
		to.OnItemAdded( item );

		SwitchToBest();
		return true;
	}

	/// <summary>
	/// Makes the given item active, holstering whatever was active. Pass null with
	/// <paramref name="allowHolster"/> to holster everything. Routed through the host.
	/// </summary>
	public void Switch( BaseInventoryItem item, bool allowHolster = false )
	{
		// The owner switches locally for instant feedback; the host runs it authoritatively and the
		// synced ActiveItem reconciles. Voluntary switches only - the forced variants (death, drop,
		// removal) go through ForceSwitch, which is host-only.
		if ( Networking.IsHost || !IsProxy )
			SwitchInternal( item, allowHolster, force: false );

		if ( !Networking.IsHost )
			SwitchHost( item, allowHolster );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void SwitchHost( BaseInventoryItem item, bool allowHolster ) => SwitchInternal( item, allowHolster, force: false );

	/// <summary>
	/// Switches without giving the outgoing active item a say (see <see cref="BaseInventoryItem.OnHolstering"/>).
	/// Used for forced holsters - death, removal, dropping - where the item must not be able to refuse.
	/// Host only; never exposed over RPC, so a client can only ever request a voluntary switch.
	/// </summary>
	internal void ForceSwitch( BaseInventoryItem item, bool allowHolster = false )
	{
		SwitchInternal( item, allowHolster, force: true );
	}

	/// <summary>
	/// Holsters the active item without giving it a say (<see cref="BaseInventoryItem.OnHolstering"/>
	/// isn't consulted) - for host-decreed empty hands: arrests, vehicles, cutscenes. Host only.
	/// </summary>
	public void ForceHolster()
	{
		if ( !Networking.IsHost )
			return;

		ForceSwitch( null, true );
	}

	private void SwitchInternal( BaseInventoryItem item, bool allowHolster, bool force )
	{
		// Switching to nothing is a holster - only when the caller asked for one.
		if ( item is null && !allowHolster )
			return;

		// Only our own items can be made active.
		if ( item is not null && (!item.IsValid() || item.Inventory != this) )
			return;

		if ( item == ActiveItem )
		{
			if ( allowHolster )
			{
				// The active item may refuse to be holstered (e.g. mid-charge), unless forced.
				if ( !force && ActiveItem.IsValid() && !ActiveItem.Holstering( null ) )
					return;

				ActiveItem = null;
			}

			return;
		}

		// The incoming item may refuse to be switched to, unless forced (a refused drop re-equips).
		if ( !force && item.IsValid() && !item.CanSwitchTo() )
			return;

		// The outgoing item may refuse to be switched away from, unless forced.
		if ( !force && ActiveItem.IsValid() && !ActiveItem.Holstering( item ) )
			return;

		ActiveItem = item;
	}

	/// <summary>
	/// Switches to the best available item (see <see cref="GetBestItem"/>).
	/// </summary>
	public void SwitchToBest()
	{
		var best = GetBestItem();
		if ( best.IsValid() )
			Switch( best );
	}

	/// <summary>
	/// Moves the item in <paramref name="fromSlot"/> to <paramref name="toSlot"/>, swapping if the
	/// destination is occupied. Routed through the host.
	/// </summary>
	public void MoveSlot( int fromSlot, int toSlot )
	{
		if ( !Networking.IsHost )
		{
			HostMoveSlot( fromSlot, toSlot );
			return;
		}

		if ( fromSlot == toSlot )
			return;

		if ( fromSlot < 0 || fromSlot >= MaxSlots )
			return;

		if ( toSlot < 0 || toSlot >= MaxSlots )
			return;

		var fromItem = GetSlot( fromSlot );
		if ( !fromItem.IsValid() )
			return;

		if ( !OnMovingSlot( fromSlot, toSlot ) )
			return;

		var toItem = GetSlot( toSlot );

		fromItem.Slot = toSlot;

		// Hotbar slots swap their occupants; buckets share, so the destination stays put.
		if ( Behaviour == InventoryBehaviour.Hotbar && toItem.IsValid() )
			toItem.Slot = fromSlot;
	}

	//
	// Host-side hooks for games to veto and react to inventory changes - post events, play notices,
	// apply rules. The On*ing gates run after validation, before anything changes; return false to
	// refuse. Base allows everything and does nothing.
	//

	/// <summary>An item is about to be added to <paramref name="slot"/>. Return false to refuse.</summary>
	protected virtual bool OnAdding( BaseInventoryItem item, int slot ) => true;

	/// <summary>An item was added to the inventory.</summary>
	protected virtual void OnItemAdded( BaseInventoryItem item ) { }

	/// <summary>An item is about to be removed and destroyed. Return false to refuse.</summary>
	protected virtual bool OnRemoving( BaseInventoryItem item ) => true;

	/// <summary>An item is about to be dropped into the world. Return false to refuse.</summary>
	protected virtual bool OnDropping( BaseInventoryItem item ) => true;

	/// <summary>The items in these slots are about to move/swap. Return false to refuse.</summary>
	protected virtual bool OnMovingSlot( int fromSlot, int toSlot ) => true;
}
