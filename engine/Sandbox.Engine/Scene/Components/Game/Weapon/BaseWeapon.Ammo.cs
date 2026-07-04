namespace Sandbox;

public partial class BaseWeapon
{
	//
	// Ammo, modelled on GMod's SWEP.Primary / SWEP.Secondary. A weapon can feed from a magazine
	// (clip) and/or a reserve pool. The reserve pool lives on the holding inventory, not the weapon -
	// GetReserveAmmo / TakeReserveAmmo read and spend from it, and a game can override them to plug
	// in its own ammo store.
	//

	/// <summary>
	/// Does primary fire consume ammo at all? When false the weapon never runs dry - for melee, tools and
	/// the like. Switches the whole Ammo feature off.
	/// </summary>
	[Property, FeatureEnabled( "Ammo" )] public bool UsesAmmo { get; set; } = true;

	/// <summary>Feed primary fire from a magazine (true) or straight from the reserve pool (false).</summary>
	[Property, Feature( "Ammo" )] public bool UsesClips { get; set; } = true;

	/// <summary>Primary magazine size.</summary>
	[Property, Feature( "Ammo" ), ShowIf( nameof( UsesClips ), true )] public int ClipMaxSize { get; set; } = 30;

	/// <summary>Reserve ammo granted on first pickup, seeded into the holder's pool.</summary>
	[Property, Feature( "Ammo" )] public int StartingAmmo { get; set; } = 0;

	// Set once the weapon has had its chance to seed the reserve - a dropped and re-taken gun
	// doesn't grant its StartingAmmo again.
	[Sync( SyncFlags.FromHost ), Expose] private bool ReserveSeeded { get; set; }

	/// <summary>
	/// Magazine size for primary fire, or -1 when it feeds straight from reserve (GMod's Primary.ClipSize).
	/// Derived from <see cref="UsesAmmo"/> / <see cref="UsesClips"/> / <see cref="ClipMaxSize"/>.
	/// </summary>
	public int PrimaryClipSize => (UsesAmmo && UsesClips) ? ClipMaxSize : -1;

	/// <summary>
	/// The reserve ammo type primary fire draws from. Null means a bottomless reserve - the magazine
	/// still forces the reload rhythm, it just never runs out. Assign a type for a finite reserve,
	/// shared with other weapons of the same type.
	/// </summary>
	[Property, Feature( "Ammo" )] public AmmoResource PrimaryAmmoType { get; set; }

	/// <summary>
	/// Magazine size for secondary fire, or -1 when it doesn't use one (GMod's Secondary.ClipSize).
	/// Set directly - unlike <see cref="PrimaryClipSize"/>, it isn't derived from the clip settings.
	/// </summary>
	[Property, Feature( "Ammo" )] public int SecondaryClipSize { get; set; } = -1;

	/// <summary>Rounds loaded into the secondary magazine when first given. -1 fills it.</summary>
	[Property, Feature( "Ammo" )] public int SecondaryDefaultClip { get; set; } = -1;

	/// <inheritdoc cref="PrimaryAmmoType"/>
	[Property, Feature( "Ammo" )] public AmmoResource SecondaryAmmoType { get; set; }

	/// <summary>
	/// Rounds currently in the primary magazine, or -1 when the weapon doesn't use one (GMod's Clip1).
	/// Host owned - the owner spends it locally for instant feedback and the spend is mirrored to the
	/// host (see <see cref="TakePrimaryAmmo"/>), whose count is the truth.
	/// </summary>
	[Sync( SyncFlags.FromHost ), Change( nameof( OnClip1Changed ) )] public int Clip1 { get; set; } = -1;

	/// <summary>
	/// Rounds currently in the secondary magazine, or -1 when unused (GMod's Clip2). Host authoritative.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public int Clip2 { get; set; } = -1;

	/// <summary>True when primary fire feeds from a magazine rather than straight from reserve.</summary>
	public bool UsesPrimaryClip => PrimaryClipSize >= 0;

	/// <summary>True when secondary fire feeds from a magazine.</summary>
	public bool UsesSecondaryClip => SecondaryClipSize >= 0;

	/// <summary>Reserve ammo available to primary fire - the holder's pool of <see cref="PrimaryAmmoType"/> (GMod's Ammo1).</summary>
	public int Ammo1 => GetReserveAmmo( PrimaryAmmoType );

	/// <summary>Reserve ammo available to secondary fire (GMod's Ammo2).</summary>
	public int Ammo2 => GetReserveAmmo( SecondaryAmmoType );

	/// <summary>Most reserve ammo the holder can carry for primary fire (from the ammo type).</summary>
	public int MaxReserveAmmo => PrimaryAmmoType?.MaxReserve ?? 0;

	/// <summary>
	/// Reserve ammo of the given type available to this weapon. Reads the owning inventory's shared
	/// pool (<see cref="InventoryComponent.GetAmmo"/>) - reserve ammo lives on the inventory, not the
	/// weapon, so guns of the same type share it. A null ammo type is a bottomless reserve. Returns 0
	/// when the weapon isn't in an inventory. Override to use a different store.
	/// </summary>
	protected virtual int GetReserveAmmo( AmmoResource ammoType )
	{
		if ( ammoType is null )
			return int.MaxValue;

		return Inventory?.GetAmmo( ammoType ) ?? 0;
	}

	/// <summary>
	/// Take up to <paramref name="amount"/> reserve ammo of the given type from the owning inventory's
	/// pool, returning how much was actually taken. Pairs with <see cref="GetReserveAmmo"/>.
	/// </summary>
	protected virtual int TakeReserveAmmo( AmmoResource ammoType, int amount )
	{
		if ( ammoType is null )
			return amount;

		return Inventory?.TakeAmmo( ammoType, amount ) ?? 0;
	}

	/// <summary>
	/// True if primary fire has a round ready - in the magazine, or in reserve for a clipless weapon.
	/// Weapons with neither a magazine nor an ammo type are treated as unlimited (melee, tools).
	/// </summary>
	public virtual bool HasPrimaryAmmo()
	{
		if ( !IsHeld ) return true;

		if ( !UsesAmmo ) return true;
		if ( UsesPrimaryClip ) return Clip1 > 0;
		if ( PrimaryAmmoType is not null ) return Ammo1 > 0;
		return true;
	}

	/// <inheritdoc cref="HasPrimaryAmmo"/>
	public virtual bool HasSecondaryAmmo()
	{
		if ( !IsHeld ) return true;

		if ( !UsesAmmo ) return true;
		if ( UsesSecondaryClip ) return Clip2 > 0;
		if ( SecondaryAmmoType is not null ) return Ammo2 > 0;
		return true;
	}

	/// <summary>
	/// Avoid auto-switching to a gun with nothing left to fire or load (see
	/// <see cref="BaseInventoryItem.ShouldAvoid"/>).
	/// </summary>
	public override bool ShouldAvoid
	{
		get
		{
			if ( !UsesAmmo )
				return false;

			// Something in the primary magazine, or reserve to fire or load from?
			if ( UsesPrimaryClip && Clip1 > 0 )
				return false;

			if ( PrimaryAmmoType is null || Ammo1 > 0 )
				return false;

			// A secondary with rounds left keeps the gun worth holding.
			if ( UsesSecondaryClip && Clip2 > 0 )
				return false;

			if ( SecondaryAmmoType is not null && Ammo2 > 0 )
				return false;

			return true;
		}
	}

	/// <summary>
	/// Spend <paramref name="amount"/> rounds for a primary shot - from the magazine if it has one,
	/// otherwise reserve, otherwise free. Returns false if there wasn't enough (GMod's TakePrimaryAmmo).
	/// </summary>
	public bool TakePrimaryAmmo( int amount = 1 )
	{
		if ( !UsesAmmo ) return true;

		if ( UsesPrimaryClip )
		{
			if ( Clip1 < amount ) return false;
			Clip1 -= amount;

			// The clip is host owned - mirror the spend so it sticks.
			if ( !Networking.IsHost )
				HostTakeClip1( amount );

			return true;
		}

		// Check before taking - a partial take would destroy rounds without firing.
		if ( PrimaryAmmoType is not null )
		{
			if ( GetReserveAmmo( PrimaryAmmoType ) < amount ) return false;
			return TakeReserveAmmo( PrimaryAmmoType, amount ) >= amount;
		}

		return true;
	}

	/// <inheritdoc cref="TakePrimaryAmmo"/>
	public bool TakeSecondaryAmmo( int amount = 1 )
	{
		if ( !UsesAmmo ) return true;

		if ( UsesSecondaryClip )
		{
			if ( Clip2 < amount ) return false;
			Clip2 -= amount;

			if ( !Networking.IsHost )
				HostTakeClip2( amount );

			return true;
		}

		if ( SecondaryAmmoType is not null )
		{
			if ( GetReserveAmmo( SecondaryAmmoType ) < amount ) return false;
			return TakeReserveAmmo( SecondaryAmmoType, amount ) >= amount;
		}

		return true;
	}

	// The owner's clip spends arrive here. Trusted for the amount, but a spend can only ever take -
	// a negative amount would add rounds. Rejecting impossible spend rates is anticheat's job
	// (see OnValidateShotClaim).
	[Rpc.Host( NetFlags.OwnerOnly )]
	private void HostTakeClip1( int amount )
	{
		if ( amount > 0 )
			Clip1 = Math.Max( 0, Clip1 - amount );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void HostTakeClip2( int amount )
	{
		if ( amount > 0 )
			Clip2 = Math.Max( 0, Clip2 - amount );
	}

	// Drives the per-round reload insert presentation on every peer - the magazine going up during
	// an incremental reload means a round was just loaded. Whole-magazine reloads don't get the
	// per-shell hook. [Expose] so the [Change] resolver finds this private handler.
	[Expose]
	private void OnClip1Changed( int oldValue, int newValue )
	{
		if ( IsReloading && IncrementalReloading && newValue > oldValue )
			OnReloadInserted();
	}

	/// <summary>
	/// One of each - picking up a weapon the inventory already holds donates its ammo to the
	/// reserve instead (GMod's duplicate pickup), when there's an ammo type with room. The
	/// duplicate is consumed by the donation, or left where it is.
	/// </summary>
	protected override bool OnAdding( InventoryComponent inventory )
	{
		var existing = FindSameWeapon( inventory );
		if ( existing is null )
			return true;

		if ( existing.UsesAmmo && existing.PrimaryAmmoType is not null && existing.Ammo1 < existing.MaxReserveAmmo )
		{
			// Donate what's actually in the magazine - a never-seeded pickup counts as full.
			var donation = UsesClips ? (Clip1 < 0 ? ClipMaxSize : Clip1) : StartingAmmo;
			inventory.GiveAmmo( existing.PrimaryAmmoType, donation );
			GameObject.Destroy();
		}

		return false;
	}

	// A weapon of the same type already in the inventory, if any. Prefab weapons only match their
	// own prefab - two data-configured guns sharing a class aren't the same weapon.
	BaseWeapon FindSameWeapon( InventoryComponent inventory )
	{
		foreach ( var item in inventory.GetComponentsInChildren<BaseInventoryItem>( true ) )
		{
			if ( item == this || item.GetType() != GetType() || item.GameObject.IsDestroyed || item.Inventory != inventory )
				continue;

			if ( item.GameObject.PrefabInstanceSource != GameObject.PrefabInstanceSource )
				continue;

			return item as BaseWeapon;
		}

		return null;
	}

	/// <summary>
	/// Seed the magazines with their default contents, and the inventory's reserve pool with
	/// <see cref="StartingAmmo"/>. Host only, runs once when the weapon enters an inventory.
	/// </summary>
	protected override void OnAdded( InventoryComponent inventory )
	{
		base.OnAdded( inventory );

		if ( UsesPrimaryClip && Clip1 < 0 )
			Clip1 = PrimaryClipSize;

		if ( UsesSecondaryClip && Clip2 < 0 )
			Clip2 = SecondaryDefaultClip < 0 ? SecondaryClipSize : SecondaryDefaultClip;

		// Seed the reserve at the first pickup only, and only when the pool for this ammo type is
		// empty - two weapons sharing a type don't double-seed, and drop churn doesn't inflate it.
		if ( !ReserveSeeded )
		{
			ReserveSeeded = true;

			if ( UsesAmmo && StartingAmmo > 0 && PrimaryAmmoType is not null && !inventory.HasAmmo( PrimaryAmmoType ) )
				inventory.GiveAmmo( PrimaryAmmoType, StartingAmmo );
		}
	}
}
