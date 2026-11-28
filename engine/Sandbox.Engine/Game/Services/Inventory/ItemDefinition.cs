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
		public CurrencyValue Price => _prices.GetPrice( DefinitionId, basePrice: false );

		/// <summary>
		/// If we're for sale but on sale, this is our regular price
		/// </summary>
		public CurrencyValue BasePrice => _prices.GetPrice( DefinitionId, basePrice: true );

		volatile bool propertiesLoaded;
		readonly object propertiesLock = new();

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
			var json = SteamNative.Inventory.GetDefinitionProperties( DefinitionId );
			var props = Json.Deserialize<Dictionary<string, string>>( json );

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
	}
}
