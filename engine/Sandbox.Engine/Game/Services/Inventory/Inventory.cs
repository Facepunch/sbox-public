using System.Threading;
using Sandbox.SteamApi;

namespace Sandbox.Services;

/// <summary>
/// Allows access to the Steam Inventory system
/// </summary>
public static partial class Inventory
{
	static List<Item> _items = new();

	/// <summary>
	/// Whether or not the inventory has been loaded from Steam.
	/// </summary>
	internal static bool HasLoaded { get; private set; }

	/// <summary>
	/// All of the items the user has in their inventory
	/// </summary>
	public static IReadOnlyCollection<Item> Items => _items.AsReadOnly();

	/// <summary>
	/// Get the user's inventory. This is called on startup - and shouldn't really need to be
	/// called again unless the user buys something. Returns a list of items added since the last refresh.
	/// An unsuccessful Steam result returns no new items and preserves the last successful inventory.
	/// </summary>
	[ConCmd( "inv_refresh", ConVarFlags.Protected )]
	internal static async Task<Item[]> Refresh( CancellationToken token = default )
	{
		using var result = SteamNative.Inventory.GetAllItems();
		if ( result.IsNull ) return Array.Empty<Item>();

		while ( result.IsPending() )
		{
			await Task.Delay( 10, token );
		}

		if ( !result.IsOk() ) return Array.Empty<Item>();

		var wasLoaded = HasLoaded;
		var previousItems = _items;

		var items = ReadItems( result ).Select( x => new Item( x.ItemId, x.DefinitionId ) ).ToList();

		// This can run on the thread pool, so swap in a whole list rather than editing the one being read
		using var proof = result.Serialize();
		CurrentBlob = proof.IsNull ? [] : proof.ToArray();
		_items = items;
		HasLoaded = true;

		// The initial load isn't a purchase, but an already loaded empty inventory can gain new items.
		if ( !wasLoaded ) return Array.Empty<Item>();

		return items.Where( x => !previousItems.Any( y => y.ItemId == x.ItemId ) ).ToArray();
	}

	/// <summary>
	/// That last serialized inventory proof for the local user, sent as part of UserInfo during connection
	/// </summary>
	internal static byte[] CurrentBlob { get; private set; } = Array.Empty<byte>();

	/// <summary>
	/// Returns true if we have this item
	/// </summary>
	public static bool HasItem( int inventoryDefinitionId )
	{
		return _items.Any( x => x.DefinitionId == inventoryDefinitionId );
	}

	internal static async Task<bool> CheckOut( List<ItemDefinition> cart )
	{
		if ( cart is not { Count: > 0 } || !SteamClient.IsValid ) return false;

		var ids = cart.Select( x => x.Id ).ToArray();
		ulong call;
		unsafe
		{
			fixed ( int* definitions = ids )
			{
				call = SteamNative.Inventory.StartPurchase( definitions, ids.Length );
			}
		}

		if ( !await SteamCalls.GetResultAsync( call, SteamNative.Inventory.GetPurchaseResult ) )
			return false;

		FastTimer timer = FastTimer.StartNew();

		Log.Info( $"Waiting for new items.." );

		while ( true )
		{
			await Task.Delay( 500 );

			var newItems = await Refresh();
			if ( newItems.Length > 0 )
			{
				foreach ( var item in newItems )
				{
					Log.Info( $"Got item {item.ItemId} ({item.Definition?.Name})" );
				}

				return true;
			}

			if ( timer.ElapsedSeconds > 20 )
			{
				Log.Warning( "Checkout timed out waiting for new items. Purchase may have been cancelled, or items may appear shortly." );
				return false;
			}
		}
	}
}
