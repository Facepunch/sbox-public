namespace Sandbox;

/// <summary>
/// A world pickup that tops up the collector's reserve ammo pool instead of taking a slot. Give it
/// an <see cref="AmmoResource"/> and an amount; picking it up donates into the pool
/// (<see cref="InventoryComponent.GiveAmmo"/>) and the pickup is consumed. When the pool is full it
/// refuses - no prompt, and Touch leaves it lying there.
/// </summary>
[Title( "Ammo Pickup" )]
[Group( "Game" )]
[Icon( "workspaces" )]
public class AmmoPickup : BaseInventoryItem
{
	/// <summary>The reserve ammo type this grants.</summary>
	[Property] public AmmoResource AmmoType { get; set; }

	/// <summary>Rounds granted on pickup.</summary>
	[Property] public int Amount { get; set; } = 30;

	/// <summary>No point offering ammo the collector can't carry.</summary>
	protected override bool OnCanPickup( InventoryComponent inventory )
	{
		if ( AmmoType is null || Amount <= 0 )
			return false;

		return inventory.GetAmmo( AmmoType ) < AmmoType.MaxReserve;
	}

	// Never joins the inventory - donates into the reserve pool and is consumed. Runs on the host
	// for every add, so a partial fill (pool nearly full) still consumes the pickup.
	protected override bool OnAdding( InventoryComponent inventory )
	{
		if ( AmmoType is not null && inventory.GiveAmmo( AmmoType, Amount ) > 0 )
			GameObject.Destroy();

		return false;
	}
}
