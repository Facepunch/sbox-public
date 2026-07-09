namespace Sandbox;

/// <summary>
/// Defines a type of ammo that weapons share. Weapons referencing the same ammo type draw from the
/// same reserve pool on the holder's inventory (see <see cref="BaseInventoryComponent.GetAmmo"/>).
/// Derive from this to define your own ammo types with extra data or overridden behaviour.
/// </summary>
[AssetType( Name = "Ammo Type", Extension = "ammo", Category = "Game" )]
[Alias( "Sandbox.AmmoResource" )]
public class BaseAmmoResource : GameResource
{
	/// <summary>Display name, for HUDs and pickup messages.</summary>
	public virtual string Title { get; set; }

	/// <summary>Optional HUD/inventory icon.</summary>
	public virtual Texture Icon { get; set; }

	/// <summary>Maximum reserve ammo an inventory can hold of this type.</summary>
	public virtual int MaxReserve { get; set; } = 120;
}
