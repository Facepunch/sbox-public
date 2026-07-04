namespace Sandbox;

/// <summary>
/// Defines a type of ammo that weapons share. Weapons referencing the same ammo type draw from the
/// same reserve pool on the holder's inventory (see <see cref="InventoryComponent.GetAmmo"/>).
/// </summary>
[AssetType( Name = "Ammo Type", Extension = "ammo", Category = "Game" )]
public class AmmoResource : GameResource
{
	/// <summary>Display name, for HUDs and pickup messages.</summary>
	public string Title { get; set; }

	/// <summary>Optional HUD/inventory icon.</summary>
	public Texture Icon { get; set; }

	/// <summary>Maximum reserve ammo an inventory can hold of this type.</summary>
	public int MaxReserve { get; set; } = 120;
}
