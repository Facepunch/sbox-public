using System.Threading;

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
		var result = NativeEngine.SteamInventory.GetAllItems();
		if ( result.IsNull ) return Array.Empty<Item>();

		try
		{
			while ( result.IsPending() )
			{
				await Task.Delay( 10, token );
			}

			if ( !result.IsOk() ) return Array.Empty<Item>();

			var wasLoaded = HasLoaded;
			var previousItems = _items.ToArray();

			_items.Clear();

			for ( int i = 0; i < result.Count(); i++ )
			{
				_items.Add( new Item( result.Get( i ) ) );
			}

			CurrentBlob = SerializeResult( result );
			HasLoaded = true;

			// The initial load isn't a purchase, but an already loaded empty inventory can gain new items.
			if ( !wasLoaded ) return Array.Empty<Item>();

			return _items.Where( x => !previousItems.Any( y => y.ItemId == x.ItemId ) ).ToArray();
		}
		finally
		{
			result.Destroy();
		}
	}

	/// <summary>
	/// That last serialized inventory proof for the local user, sent as part of UserInfo during connection
	/// </summary>
	internal static byte[] CurrentBlob { get; private set; } = Array.Empty<byte>();

	private static unsafe byte[] SerializeResult( CSteamInventoryResult result )
	{
		var size = result.GetSerializedSize();
		if ( size == 0 )
			return Array.Empty<byte>();

		var buffer = new byte[size];
		fixed ( byte* ptr = buffer )
		{
			result.Serialize( ptr, size );
		}
		return buffer;
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
				Log.Warning( "Checkout timed out waiting for new items. Purchase may have been cancelled, or items may appear shortly." );
				return false;
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
