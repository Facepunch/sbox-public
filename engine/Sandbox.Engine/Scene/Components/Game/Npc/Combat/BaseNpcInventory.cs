namespace Sandbox.Npcs;

/// <summary>
/// The inventory an NPC holds its weapons in. It works like a player's, with a reserve that never runs out and
/// no input driving the active weapon. Derive from it to give NPCs their own ammo rules.
/// </summary>
[Title( "NPC Inventory" )]
[Group( "Game" )]
[Icon( "inventory_2" )]
public class BaseNpcInventory : BaseInventoryComponent
{
	public BaseNpcInventory()
	{
		// The active item's control reads input, and an NPC has none.
		ManualPumping = true;
	}

	/// <summary>
	/// An NPC never runs out: every ammo type has a bottomless reserve.
	/// </summary>
	public override int GetAmmo( BaseAmmoResource type ) => type is null ? 0 : int.MaxValue;

	/// <summary>
	/// Takes all that was asked for and leaves the reserve as it was.
	/// </summary>
	public override int TakeAmmo( BaseAmmoResource type, int amount ) => type is null ? 0 : Math.Max( amount, 0 );
}
