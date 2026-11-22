namespace Sandbox;

/// <summary>
/// A slot based inventory of <see cref="BaseInventoryItem"/>s, modelled on the sandbox game's
/// inventory. Items are stored as child GameObjects; the inventory tracks which one is active and
/// enables/disables them as you switch. Host authoritative - clients request changes and the host
/// applies them, replicating the result back down.
/// </summary>
/// <remarks>
/// This is deliberately animation agnostic and knows nothing about players. Driving hold-type
/// animations belongs in layers built on top of this - see <see cref="BaseCombatWeapon"/>.
/// </remarks>
[Expose]
[Icon( "inventory_2" )]
[Group( "Game" )]
[Title( "Inventory" )]
[Alias( "Sandbox.InventoryComponent" )]
public partial class BaseInventoryComponent : Component
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
	public IEnumerable<BaseInventoryItem> Items
	{
		get
		{
			// HUDs read this every frame: one list, no LINQ ordering buffers
			var items = new List<BaseInventoryItem>();
			Components.GetAll( items, FindMode.EverythingInSelfAndDescendants );

			// Insertion sort keeps equal items in hierarchy order, like OrderBy did
			int count = 0;
			for ( int i = 0; i < items.Count; i++ )
			{
				var item = items[i];
				if ( item.GameObject.IsDestroyed || item.Inventory != this ) continue;

				int j = count - 1;
				for ( ; j >= 0 && CompareSlots( items[j], item ) > 0; j-- )
					items[j + 1] = items[j];
				items[j + 1] = item;
				count++;
			}

			items.RemoveRange( count, items.Count - count );
			return items;
		}
	}

	static int CompareSlots( BaseInventoryItem a, BaseInventoryItem b )
	{
		var slot = a.Slot.CompareTo( b.Slot );
		return slot != 0 ? slot : a.SlotOrder.CompareTo( b.SlotOrder );
	}

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

	BaseInventoryItem _equippedItem;

	[Expose]
	internal void OnActiveItemChanged( BaseInventoryItem _, BaseInventoryItem newItem )
	{
		// Initial snapshots don't run Change callbacks. Track what was actually equipped locally.
		var oldItem = _equippedItem;
		if ( oldItem == newItem )
			return;

		_equippedItem = newItem;

		if ( oldItem.IsValid() )
		{
			// One that was dropped already has no slot and is the world's: it is put away but stays enabled
			if ( oldItem.Slot >= 0 )
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
