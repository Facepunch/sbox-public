namespace Sandbox.Services;

public static partial class Inventory
{
	/// <summary>
	/// Describes a type of item that can be in the inventory
	/// </summary>
	public sealed class ItemDefinition
	{
		internal int DefinitionId;

		public int Id => DefinitionId;
		public string Name { get; private set; }
		public string Description { get; private set; }
		public string DescriptionWithMeta { get; private set; }
		public string IconUrl { get; private set; }
		public string IconUrlLarge { get; private set; }
		public string PackageIdent { get; private set; }
		public string Category { get; private set; }
		public bool StoreHidden { get; private set; }
		public string Asset { get; private set; }
		public string Rarity { get; private set; }
		public DateTime? SellStart { get; private set; }
		public DateTime? SellEnd { get; private set; }

		/// <summary>
		/// If we're for sale, this is our price
		/// </summary>
		public CurrencyValue Price { get; private set; }

		/// <summary>
		/// If we're for sale but on sale, this is our regular price
		/// </summary>
		public CurrencyValue BasePrice { get; private set; }

		Dictionary<string, string> _props = new();

		public ItemDefinition( int id )
		{
			DefinitionId = id;

			UpdateProperties();
		}

		void UpdateProperties()
		{
			var properties = NativeEngine.SteamInventory.GetDefinitionProperty( DefinitionId, "" );
			if ( properties is not null )
			{
				var parray = properties.Split( ",", StringSplitOptions.RemoveEmptyEntries );

				foreach ( var entry in parray )
				{
					var val = NativeEngine.SteamInventory.GetDefinitionProperty( DefinitionId, entry );
					_props[entry] = val;
				}
			}

			Name = _props.GetValueOrDefault( "name" );
			Description = _props.GetValueOrDefault( "summary" );
			DescriptionWithMeta = _props.GetValueOrDefault( "description" );
			IconUrl = _props.GetValueOrDefault( "icon_url" );
			IconUrlLarge = _props.GetValueOrDefault( "icon_url_large" );
			PackageIdent = _props.GetValueOrDefault( "package" );
			Category = _props.GetValueOrDefault( "clothing_cat" );
			StoreHidden = _props.GetValueOrDefault( "store_hidden" ).ToBool();
			Asset = _props.GetValueOrDefault( "asset" );
			Rarity = _props.GetValueOrDefault( "rarity" );

			if ( _props.GetValueOrDefault( "sell_start" ) is string sellStartStr && int.TryParse( sellStartStr, out var sellStartInt ) )
			{
				SellStart = sellStartInt.ToDateTime();
			}

			if ( _props.GetValueOrDefault( "sell_end" ) is string sellEndStr && int.TryParse( sellEndStr, out var sellEndInt ) )
			{
				SellEnd = sellEndInt.ToDateTime();
			}
		}

		internal void FetchPriceInformation( string currency )
		{
			Price = default;
			BasePrice = default;

			if ( NativeEngine.SteamInventory.GetDefinitionPrice( DefinitionId, out var price, out var baseprice ) )
			{
				Price = new CurrencyValue( (long)price, currency );
				BasePrice = new CurrencyValue( (long)price, currency );
			}
		}
	}
}
