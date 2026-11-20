/// <summary>
/// A pickup that gives the player reserve ammo for a matching weapon.
/// </summary>
public sealed class AmmoPickup : BasePickup
{
	/// <summary>
	/// The ammo resource this pickup gives ammo for.
	/// When set, ammo is added directly to the player's shared pool for that resource.
	/// </summary>
	[Property, Group( "Ammo" )] public BaseAmmoResource AmmoType { get; set; }

	/// <summary>
	/// The quantity of ammo to give.
	/// </summary>
	[Property, Group( "Ammo" )] public int AmmoAmount { get; set; }

	public override bool CanPickup( Player player, PlayerInventory inventory )
	{
		if ( AmmoType is null || inventory is null ) return false;
		return inventory.GetAmmo( AmmoType ) < AmmoType.MaxReserve;
	}

	protected override bool OnPickup( Player player, PlayerInventory inventory )
	{
		if ( AmmoType is null || inventory is null ) return false;

		// GiveAmmo clamps to the resource's max and reports what actually went in.
		return inventory.GiveAmmo( AmmoType, AmmoAmount ) > 0;
	}
}
