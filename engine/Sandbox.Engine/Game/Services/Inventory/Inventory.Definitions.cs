using System.Threading;

namespace Sandbox.Services;

/// <summary>
/// Allows access to the Steam Inventory system
/// </summary>
public static partial class Inventory
{
	static Dictionary<int, ItemDefinition> _all = new();
	static PriceCatalog _prices = new( null, new() );
	static readonly TaskCompletionSource definitionsLoaded = new( TaskCreationOptions.RunContinuationsAsynchronously );

	/// <summary>
	/// All item definitions
	/// </summary>
	public static IReadOnlyCollection<ItemDefinition> Definitions => _all.Values;

	/// <summary>
	/// Find a definition by id
	/// </summary>
	public static ItemDefinition FindDefinition( int definitionId )
	{
		return _all.GetValueOrDefault( definitionId );
	}

	/// <summary>
	/// Wait until Steam supplies the first set of item definitions or startup times out.
	/// </summary>
	internal static async Task WaitForSteamInventoryItems( CancellationToken token )
	{
		try
		{
			await definitionsLoaded.Task.WaitAsync( token );
		}
		catch ( OperationCanceledException ) when ( token.IsCancellationRequested )
		{
			// Startup can continue without inventory definitions.
		}
	}

	internal static void LoadDefinitions( ReadOnlySpan<int> ids )
	{
		var all = new Dictionary<int, ItemDefinition>( ids.Length );
		foreach ( var id in ids )
		{
			all[id] = new ItemDefinition( id );
		}

		_all = all;
		definitionsLoaded.TrySetResult();
		_ = Refresh();
	}

	readonly record struct ItemPrice( ulong Price, ulong BasePrice );

	sealed record PriceCatalog( string Currency, Dictionary<int, ItemPrice> Items )
	{
		internal CurrencyValue GetPrice( int id, bool basePrice )
		{
			if ( !Items.TryGetValue( id, out var price ) ) return default;

			return new CurrencyValue( (long)(basePrice ? price.BasePrice : price.Price), Currency );
		}
	}

	internal static void LoadPrices( string catalog )
	{
		_prices = System.Text.Json.JsonSerializer.Deserialize<PriceCatalog>( catalog );
	}
}
