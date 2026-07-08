namespace Sandbox;

/// <summary>
/// A slot based inventory of <see cref="BaseInventoryItem"/>s, modelled on the sandbox game's
/// inventory. Items are stored as child GameObjects; the inventory tracks which one is active and
/// enables/disables them as you switch. Host authoritative - clients request changes and the host
/// applies them, replicating the result back down.
/// </summary>
/// <remarks>
/// This is deliberately animation agnostic and knows nothing about players. Driving hold-type
/// animations belongs in layers built on top of this - see <see cref="BaseWeapon"/>.
/// </remarks>
[Expose]
[Icon( "inventory_2" )]
[Group( "Game" )]
[Title( "Inventory" )]
public partial class InventoryComponent : Component
{
	/// <summary>
	/// How an inventory assigns its slots.
	/// </summary>
	public enum InventoryBehaviour
	{
		/// <summary>One item per slot, selected directly.</summary>
		[Title( "Hotbar" ), Icon( "view_column" )]
		[Description( "One item per slot, selected directly - like Rust or Minecraft. An item added without an explicit slot takes its preferred slot if free, otherwise the first empty one, and a full inventory refuses new items." )]
		Hotbar,

		/// <summary>Slots are category buckets that any number of items share.</summary>
		[Title( "Buckets" ), Icon( "view_module" )]
		[Description( "Slots are category buckets that any number of items share, sorted by their SlotOrder - like Half-Life 2's weapon list. Items always land in their preferred slot and buckets never fill up." )]
		Buckets,
	}

	/// <summary>
	/// How this inventory assigns its slots - exclusive hotbar slots or shared buckets.
	/// </summary>
	[Property] public InventoryBehaviour Behaviour { get; set; } = InventoryBehaviour.Hotbar;

	/// <summary>
	/// How many slots this inventory has. Items occupy slots 0..MaxSlots-1.
	/// </summary>
	[Property] public int MaxSlots { get; set; } = 6;

	/// <summary>
	/// All items currently in the inventory, ordered by slot then <see cref="BaseInventoryItem.SlotOrder"/>.
	/// Includes disabled (inactive) items but not ones waiting to be destroyed - a removed item is
	/// gone immediately, even though its GameObject lives until the end of the frame. Items inside a
	/// nested inventory (a held backpack) belong to that inventory, not this one.
	/// </summary>
	public IEnumerable<BaseInventoryItem> Items =>
		GetComponentsInChildren<BaseInventoryItem>( true ).Where( x => !x.GameObject.IsDestroyed && x.Inventory == this ).OrderBy( x => x.Slot ).ThenBy( x => x.SlotOrder );

	/// <summary>
	/// The item that is currently active (deployed), or null when nothing is held. Setting this is
	/// host authoritative - use <see cref="Switch"/>.
	/// </summary>
	[Sync( SyncFlags.FromHost ), Change( nameof( OnActiveItemChanged ) )]
	public BaseInventoryItem ActiveItem { get; private set; }

	/// <summary>
	/// Fired on every peer when the active item changes, with (old, new). Either may be null.
	/// </summary>
	public event Action<BaseInventoryItem, BaseInventoryItem> ActiveItemChanged;

	[Expose]
	private void OnActiveItemChanged( BaseInventoryItem oldItem, BaseInventoryItem newItem )
	{
		if ( oldItem.IsValid() )
		{
			oldItem.GameObject.Enabled = false;
			oldItem.Holster();
		}

		if ( newItem.IsValid() )
		{
			newItem.GameObject.Enabled = true;
			newItem.Equip();
		}

		ActiveItemChanged?.Invoke( oldItem, newItem );
	}
}
