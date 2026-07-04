namespace Sandbox;

public partial class InventoryComponent
{
	//
	// Picking up items lying in the world - dropped by someone, or placed in the scene. Touch sweeps
	// around the inventory on the host; Use lets the item be pressed (see BaseInventoryItem's
	// IPressable). Both land in PickupWorldItem, which a None-mode game calls itself.
	//

	/// <summary>How an inventory takes items lying in the world.</summary>
	public enum PickupBehaviour
	{
		/// <summary>Never picks items up by itself.</summary>
		[Title( "None" ), Icon( "block" )]
		[Description( "Never picks items up by itself - the game calls PickupWorldItem when it wants to." )]
		None,

		/// <summary>Walking near a world item picks it up.</summary>
		[Title( "Touch" ), Icon( "directions_walk" )]
		[Description( "Walking near a world item picks it up - like GMod or Quake." )]
		Touch,

		/// <summary>Pressing use on a world item picks it up.</summary>
		[Title( "Use" ), Icon( "touch_app" )]
		[Description( "Pressing use on a world item picks it up." )]
		Use,
	}

	/// <summary>
	/// How this inventory takes items lying in the world - dropped, or placed in the scene.
	/// </summary>
	[Property] public PickupBehaviour PickupMode { get; set; } = PickupBehaviour.None;

	/// <summary>How close a world item has to be for Touch pickup, from the inventory's origin.</summary>
	[Property, ShowIf( nameof( PickupMode ), PickupBehaviour.Touch )] public float PickupRadius { get; set; } = 48f;

	/// <summary>
	/// Switch to a picked up item when it's better than the active one (see
	/// <see cref="ShouldAutoSwitchTo"/>). Empty hands always deploy the pickup.
	/// </summary>
	[Property] public bool AutoSwitchOnPickup { get; set; } = true;

	/// <summary>
	/// Switch away from the active item when it's spent (<see cref="BaseInventoryItem.ShouldAvoid"/>)
	/// and something that isn't is available - the classic arena-shooter auto-switch. Voluntary, so
	/// the item can still refuse the holster.
	/// </summary>
	[Property] public bool AutoSwitchOnEmpty { get; set; }

	// The last item this inventory dropped - don't vacuum it straight back up.
	BaseInventoryItem _lastDropped;
	TimeSince _sinceDropped;

	TimeUntil _nextPickupSweep;

	protected override void OnFixedUpdate()
	{
		// Both jobs here are host authoritative - clients see the results through the synced state.
		if ( !Networking.IsHost )
			return;

		if ( AutoSwitchOnEmpty )
			SwitchIfActiveEmpty();

		if ( PickupMode != PickupBehaviour.Touch )
			return;

		if ( !_nextPickupSweep )
			return;

		_nextPickupSweep = 0.25f;

		PickupSweep();
	}

	// Put a spent active item away for something that can still fire.
	void SwitchIfActiveEmpty()
	{
		if ( !ActiveItem.IsValid() || !ActiveItem.ShouldAvoid )
			return;

		if ( GetBestItem() is { ShouldAvoid: false } best && best != ActiveItem )
			Switch( best );
	}

	void PickupSweep()
	{
		foreach ( var go in Scene.FindInPhysics( new Sphere( WorldPosition, PickupRadius ) ) )
		{
			// The hit is the collider's object, which can be a child of the item.
			if ( go.GetComponentInParent<BaseInventoryItem>() is not { } item )
				continue;

			PickupWorldItem( item );
		}
	}

	/// <summary>
	/// Take an item lying in the world into this inventory - the path Touch and Use pickup share,
	/// and the one to call from game code in None mode. Routed through the host. Refuses anything
	/// <see cref="CanPickupWorldItem"/> does. The usual add hooks apply, and the item becomes active
	/// when nothing else is. Override to change what a pickup does (ammo from duplicates, notices).
	/// </summary>
	public virtual void PickupWorldItem( BaseInventoryItem item )
	{
		if ( !Networking.IsHost )
		{
			HostPickupWorldItem( item );
			return;
		}

		if ( !CanPickupWorldItem( item ) )
			return;

		if ( !Add( item ) )
			return;

		if ( !ActiveItem.IsValid() )
			SwitchToBest();
		else if ( ShouldAutoSwitchTo( item ) )
			Switch( item );
	}

	/// <summary>
	/// Should picking this item up make it active? Base: <see cref="AutoSwitchOnPickup"/> is on,
	/// the item is better (higher Value) than the active one, and it isn't avoided (an empty gun).
	/// The active item can still refuse the holster. Override for game policy.
	/// </summary>
	protected virtual bool ShouldAutoSwitchTo( BaseInventoryItem item )
	{
		if ( !AutoSwitchOnPickup )
			return false;

		if ( item.ShouldAvoid )
			return false;

		return item.Value > ActiveItem.Value;
	}

	/// <summary>
	/// Is this an item lying in the world that we could take right now? It's valid, it isn't in an
	/// inventory, it doesn't refuse us (<see cref="BaseInventoryItem.OnCanPickup"/>), and it isn't
	/// something we dropped a moment ago. Runs on the host for routed pickup requests, making it
	/// the place to validate them - the base deliberately has no range check (like shot claims,
	/// what's plausible is game policy), so override to add range or line-of-sight rules.
	/// </summary>
	protected virtual bool CanPickupWorldItem( BaseInventoryItem item )
	{
		if ( !item.IsValid() || item.Inventory is not null )
			return false;

		if ( !item.CanPickup( this ) )
			return false;

		if ( item == _lastDropped && _sinceDropped < 2f )
			return false;

		return true;
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void HostPickupWorldItem( BaseInventoryItem item ) => PickupWorldItem( item );
}
