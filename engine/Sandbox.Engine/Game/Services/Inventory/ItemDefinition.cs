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
		public string Name { get { LoadProperties(); return field; } private set; }
		public string Description { get { LoadProperties(); return field; } private set; }
		public string DescriptionWithMeta { get { LoadProperties(); return field; } private set; }
		public string IconUrl { get { LoadProperties(); return field; } private set; }
		public string IconUrlLarge { get { LoadProperties(); return field; } private set; }
		public string PackageIdent { get { LoadProperties(); return field; } private set; }
		public string Category { get { LoadProperties(); return field; } private set; }
		public bool StoreHidden { get { LoadProperties(); return field; } private set; }
		public string Asset { get { LoadProperties(); return field; } private set; }
		public string Rarity { get { LoadProperties(); return field; } private set; }
		public DateTime? SellStart { get { LoadProperties(); return field; } private set; }
		public DateTime? SellEnd { get { LoadProperties(); return field; } private set; }

		/// <summary>
		/// If we're for sale, this is our price
		/// </summary>
		public CurrencyValue Price { get; private set; }

		/// <summary>
		/// If we're for sale but on sale, this is our regular price
		/// </summary>
		public CurrencyValue BasePrice { get; private set; }

		volatile bool propertiesLoaded;
		// The native property getter shares one string buffer between all definitions
		static readonly object propertiesLock = new();

		public ItemDefinition( int id )
		{
			DefinitionId = id;
		}

		// Reading every property from Steam is slow across the whole catalog, so only do it for definitions that are used.
		void LoadProperties()
		{
			if ( propertiesLoaded ) return;

			lock ( propertiesLock )
			{
				if ( propertiesLoaded ) return;
				ReadProperties();
			}
		}

		void ReadProperties()
		{
			var props = new Dictionary<string, string>();
			var properties = NativeEngine.SteamInventory.GetDefinitionProperty( DefinitionId, "" );
			if ( properties is not null )
			{
				var parray = properties.Split( ",", StringSplitOptions.RemoveEmptyEntries );

				foreach ( var entry in parray )
				{
					props[entry] = NativeEngine.SteamInventory.GetDefinitionProperty( DefinitionId, entry );
				}
			}

			Name = props.GetValueOrDefault( "name" );
			Description = props.GetValueOrDefault( "summary" );
			DescriptionWithMeta = props.GetValueOrDefault( "description" );
			IconUrl = props.GetValueOrDefault( "icon_url" );
			IconUrlLarge = props.GetValueOrDefault( "icon_url_large" );
			PackageIdent = props.GetValueOrDefault( "package" );
			Category = props.GetValueOrDefault( "clothing_cat" );
			StoreHidden = props.GetValueOrDefault( "store_hidden" ).ToBool();
			Asset = props.GetValueOrDefault( "asset" );
			Rarity = props.GetValueOrDefault( "rarity" );

			if ( props.GetValueOrDefault( "sell_start" ) is string sellStartStr && int.TryParse( sellStartStr, out var sellStartInt ) )
			{
				SellStart = sellStartInt.ToDateTime();
			}

			if ( props.GetValueOrDefault( "sell_end" ) is string sellEndStr && int.TryParse( sellEndStr, out var sellEndInt ) )
			{
				SellEnd = sellEndInt.ToDateTime();
			}

			propertiesLoaded = true;
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
