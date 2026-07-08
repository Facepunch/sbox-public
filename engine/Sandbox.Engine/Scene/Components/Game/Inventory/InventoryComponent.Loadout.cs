namespace Sandbox;

public partial class InventoryComponent
{
	//
	// The starting loadout - items and reserve ammo granted by the host when the inventory starts.
	// GMod's loadout hook as editor config. Games running their own spawn flow (saved loadouts,
	// classes) turn GiveOnStart off and call GiveLoadout themselves.
	//

	/// <summary>An amount of one ammo type, for the starting loadout.</summary>
	public record struct AmmoGrant()
	{
		/// <summary>The ammo type to grant.</summary>
		public AmmoResource Type { get; set; }

		/// <summary>How much reserve to grant.</summary>
		public int Amount { get; set; }
	}

	/// <summary>Does this inventory start with a loadout?</summary>
	[Property, FeatureEnabled( "Loadout" )] public bool UsesLoadout { get; set; }

	/// <summary>
	/// Grant the loadout automatically when the inventory starts. Turn off to decide when yourself
	/// (respawn logic, saved loadouts) and call <see cref="GiveLoadout"/>.
	/// </summary>
	[Property, Feature( "Loadout" )] public bool GiveOnStart { get; set; } = true;

	/// <summary>Item prefabs granted by the loadout, in order. Each needs a <see cref="BaseInventoryItem"/>.</summary>
	[Property, Feature( "Loadout" )] public List<GameObject> StartingItems { get; set; } = new();

	/// <summary>Reserve ammo granted by the loadout.</summary>
	[Property, Feature( "Loadout" )] public List<AmmoGrant> StartingAmmo { get; set; } = new();

	protected override void OnStart()
	{
		if ( UsesLoadout && GiveOnStart && Networking.IsHost )
			GiveLoadout();
	}

	/// <summary>
	/// Grants the starting loadout - picks up every <see cref="StartingItems"/> prefab, grants the
	/// <see cref="StartingAmmo"/>, then switches to the best item if nothing is active. Host only.
	/// Grants unconditionally - it's the caller's job to only ask once per life.
	/// </summary>
	public void GiveLoadout()
	{
		if ( !Networking.IsHost )
			return;

		foreach ( var prefab in StartingItems )
			Pickup( prefab );

		foreach ( var grant in StartingAmmo )
			GiveAmmo( grant.Type, grant.Amount );

		if ( !ActiveItem.IsValid() )
			SwitchToBest();
	}
}
