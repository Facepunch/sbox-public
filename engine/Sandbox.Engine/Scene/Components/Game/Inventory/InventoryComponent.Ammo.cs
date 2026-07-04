namespace Sandbox;

public partial class InventoryComponent
{
	//
	// Reserve ammo, modelled on GMod where ammo lives on the player rather than the gun, keyed by
	// AmmoResource. Two pistols using the same ammo type share one pool. Weapons read and spend from
	// here through BaseWeapon's reserve hooks.
	//
	// The pool is FromHost, so only the host's writes replicate. The owning client spends locally for
	// instant feedback and mirrors the spend to the host (TakeAmmo), whose pool is the truth. Grants
	// (pickups) are host-only.
	//

	[Sync( SyncFlags.FromHost )] private NetDictionary<string, int> Ammo { get; set; } = new();

	// The pool is keyed by the resource's path - readable on the wire and stable across sessions.
	private static string AmmoKey( AmmoResource type ) => type?.ResourcePath;

	/// <summary>
	/// How much reserve ammo of the given type this inventory holds. Null is 0.
	/// </summary>
	public int GetAmmo( AmmoResource type )
	{
		var key = AmmoKey( type );
		if ( string.IsNullOrEmpty( key ) )
			return 0;

		return Ammo.TryGetValue( key, out var amount ) ? amount : 0;
	}

	/// <summary>Does this inventory hold at least <paramref name="amount"/> reserve ammo of the given type?</summary>
	public bool HasAmmo( AmmoResource type, int amount = 1 ) => GetAmmo( type ) >= amount;

	/// <summary>
	/// Add reserve ammo of the given type, clamped to the type's <see cref="AmmoResource.MaxReserve"/>.
	/// Returns how much was actually added. Call this from host game logic (pickups) - it's
	/// authoritative on the host.
	/// </summary>
	public int GiveAmmo( AmmoResource type, int amount )
	{
		var key = AmmoKey( type );
		if ( string.IsNullOrEmpty( key ) || amount <= 0 )
			return 0;

		var have = GetAmmo( type );
		var added = Math.Min( amount, type.MaxReserve - have );
		if ( added <= 0 )
			return 0;

		Ammo[key] = have + added;
		return added;
	}

	/// <summary>
	/// Set the reserve ammo of the given type to an exact value (clamped to zero). Ignores the
	/// type's max - the escape hatch for game logic that wants to exceed it.
	/// </summary>
	public void SetAmmo( AmmoResource type, int amount )
	{
		var key = AmmoKey( type );
		if ( string.IsNullOrEmpty( key ) )
			return;

		Ammo[key] = Math.Max( 0, amount );
	}

	/// <summary>
	/// Take up to <paramref name="amount"/> reserve ammo of the given type, returning how much was
	/// actually removed. The owning client spends locally and the spend is mirrored to the host,
	/// whose pool is the truth.
	/// </summary>
	public int TakeAmmo( AmmoResource type, int amount )
	{
		var key = AmmoKey( type );
		if ( string.IsNullOrEmpty( key ) || amount <= 0 )
			return 0;

		var taken = TakeAmmoInternal( key, amount );
		if ( taken <= 0 )
			return 0;

		// Mirror what was actually taken - the host pool may hold more than we've seen yet.
		if ( !Networking.IsHost )
			HostTakeAmmo( key, taken );

		return taken;
	}

	private int TakeAmmoInternal( string key, int amount )
	{
		var have = Ammo.TryGetValue( key, out var val ) ? val : 0;
		var taken = Math.Min( have, amount );
		if ( taken > 0 )
			Ammo[key] = have - taken;

		return taken;
	}

	// The owner's reserve spends arrive here and re-run against the host's pool, which clamps them.
	[Rpc.Host( NetFlags.OwnerOnly )]
	private void HostTakeAmmo( string type, int amount ) => TakeAmmoInternal( type, amount );
}
