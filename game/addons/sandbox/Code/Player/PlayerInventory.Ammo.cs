public sealed partial class PlayerInventory
{
	public sealed class AmmoPickupNotice
	{
		public BaseAmmoResource Type { get; init; }
		public int Amount { get; set; }
		public float ExpiresAt { get; set; }
	}

	private readonly List<AmmoPickupNotice> _ammoPickupNotices = new();
	public IReadOnlyList<AmmoPickupNotice> AmmoPickupNotices => _ammoPickupNotices;

	/// <summary>Give reserve ammo and report the amount actually accepted to its owner.</summary>
	public new int GiveAmmo( BaseAmmoResource type, int amount )
	{
		var added = base.GiveAmmo( type, amount );
		NotifyAmmoPickup( type, added );
		return added;
	}

	private void NotifyAmmoPickup( BaseAmmoResource type, int amount )
	{
		if ( !Networking.IsHost || type is null || amount <= 0 ) return;
		ReceiveAmmoPickup( type.ResourcePath, amount );
	}

	[Rpc.Owner]
	private void ReceiveAmmoPickup( string resourcePath, int amount )
	{
		if ( !Player.IsValid() || !Player.IsLocalPlayer || amount <= 0 ) return;
		var type = ResourceLibrary.Get<BaseAmmoResource>( resourcePath );
		if ( type is null ) return;

		var now = RealTime.Now;
		_ammoPickupNotices.RemoveAll( notice => notice.ExpiresAt <= now );
		var existing = _ammoPickupNotices.FirstOrDefault( notice => notice.Type.ResourcePath == resourcePath );
		if ( existing is not null )
		{
			existing.Amount += amount;
			existing.ExpiresAt = now + 1.5f;
			return;
		}

		if ( _ammoPickupNotices.Count >= 4 ) _ammoPickupNotices.RemoveAt( 0 );
		_ammoPickupNotices.Add( new AmmoPickupNotice { Type = type, Amount = amount, ExpiresAt = now + 1.5f } );
	}
}
