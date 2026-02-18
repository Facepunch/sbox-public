using System.Threading;

namespace Sandbox.Services;

/// <summary>
/// Allows access to the Steam Inventory system
/// </summary>
public static partial class Inventory
{
	static List<Item> _items = new();

	/// <summary>
	/// All of the items the user has in their inventory
	/// </summary>
	public static IReadOnlyCollection<Item> Items => _items.AsReadOnly();

	/// <summary>
	/// Get the user's inventory. This is called on startup - and shouldn't really need to be
	/// called again unless the user buys something. Returns a list of items added since the last refresh.
	/// </summary>
	[ConCmd( "inv_refresh", ConVarFlags.Protected )]
	internal static async Task<Item[]> Refresh( CancellationToken token = default )
	{
		var result = NativeEngine.SteamInventory.GetAllItems();
		if ( result.IsNull ) return Array.Empty<Item>();

		while ( result.IsPending() )
		{
			await Task.Delay( 10 );
		}

		var previousItems = _items.ToArray();

		_items.Clear();

		for ( int i = 0; i < result.Count(); i++ )
		{
			_items.Add( new Item( result.Get( i ) ) );
		}

		// If we had items previously then notify of new items. This is usually caused by a call to Refresh after an in-game purchase.
		if ( previousItems.Count() > 0 )
		{
			var newItems = _items.ToList();
			newItems.RemoveAll( x => previousItems.Any( y => y.ItemId == x.ItemId ) );

			return newItems.ToArray();
		}

		result.Destroy();
		return Array.Empty<Item>();
	}

	/// <summary>
	/// Returns true if we have this item
	/// </summary>
	public static bool HasItem( int inventoryDefinitionId )
	{
		return _items.Any( x => x.DefinitionId == inventoryDefinitionId );
	}

	internal static async Task<bool> CheckOut( List<ItemDefinition> cart )
	{
		var ids = cart.Select( x => x.Id ).ToArray();

		unsafe
		{
			fixed ( int* idptr = &ids[0] )
			{
				NativeEngine.SteamInventory.CheckOut( idptr, ids.Length );
			}
		}

		while ( NativeEngine.SteamInventory.IsCheckingOut() )
		{
			await Task.Delay( 200 );
		}

		if ( !NativeEngine.SteamInventory.WasCheckoutSuccessful() )
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
				// Timed out waiting for items, but the checkout was successful.
				// Do one final refresh to ensure we have the latest inventory state, even if we didn't detect the new items specifically.
				Log.Warning( "Checkout succeeded but timed out waiting for new items to appear in inventory. Items may appear shortly." );
				await Refresh();
				return true;
			}
		}
	}

	//
	// called from engine via steam callbacks
	//
	internal static void OnDefinitionUpdate()
	{
		_ = LoadDefinitions();
	}

	internal static void OnPricesUpdate( bool success, string v )
	{
		Log.Info( $"Prices Updated! {success} - {v}" );
	}

	internal static void OnPurchaseResult( bool v, ulong orderid, ulong transid )
	{
		Log.Info( "Purchase Result!" );
	}


}
