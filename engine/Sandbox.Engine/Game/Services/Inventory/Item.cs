namespace Sandbox.Services;

/// <summary>
/// One row returned by Steam's GetResultItems, with the field layout of SteamItemDetails_t.
/// </summary>
internal readonly record struct InventoryItem( ulong ItemId, int DefinitionId, ushort Quantity, ushort Flags );

public static partial class Inventory
{
	/// <summary>
	/// Describes a type of item that can be in the inventory
	/// </summary>
	public sealed class Item
	{
		public ulong ItemId { get; private set; }
		public int DefinitionId { get; private set; }

		public ItemDefinition Definition => FindDefinition( DefinitionId );

		internal Item( ulong itemId, int definitionId )
		{
			ItemId = itemId;
			DefinitionId = definitionId;
		}
	}

	/// <summary>
	/// Copies the result's items so they can be used after the native result is disposed.
	/// </summary>
	internal static unsafe InventoryItem[] ReadItems( SteamNative.InventoryResult result )
	{
		var data = result.GetItemData( out var count );
		return new ReadOnlySpan<InventoryItem>( data, count ).ToArray();
	}
}
