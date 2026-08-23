using Sandbox;
using Sandbox.Services;

/// <summary>
/// Store-facing reads on an inventory item definition: rarity, sale, time left, category label.
/// </summary>
public static class StoreItem
{
	extension( Inventory.ItemDefinition item )
	{
		/// <summary>Parsed rarity grade, None when the item has no rarity.</summary>
		public ItemRarity RarityGrade => ItemRarityExtensions.ParseRarity( item.Rarity );

		/// <summary>Rarity colour as a css hex string, or a neutral grey when there's no rarity.</summary>
		public string RarityColor => item.RarityGrade.GetColor() ?? "#8b93a3";

		/// <summary>Rarity colour at the given opacity, as a css rgba() string.</summary>
		public string RarityColorAlpha( float alpha ) => (Color.Parse( item.RarityColor ) ?? Color.Gray).WithAlpha( alpha ).Rgba;

		/// <summary>A soft radial wash of the rarity colour, for use as a background-image behind big art.</summary>
		public string RarityGlow( float alpha ) => $"radial-gradient( circle at 50% 55%, {item.RarityColorAlpha( alpha )} 0%, {item.RarityColorAlpha( alpha * 0.35f )} 40%, {item.RarityColorAlpha( 0 )} 72% )";

		/// <summary>A top-down tint of the rarity colour, fading to nothing - a full-bleed wash for a tile's art area.</summary>
		public string RarityWash( float alpha ) => $"linear-gradient( to bottom, {item.RarityColorAlpha( alpha )} 0%, {item.RarityColorAlpha( alpha * 0.4f )} 55%, {item.RarityColorAlpha( 0 )} 100% )";

		/// <summary>Whether the current price is lower than the regular price.</summary>
		public bool IsOnSale => item.BasePrice.Value > item.Price.Value;

		/// <summary>Whole-number percentage off the regular price, 0 when not on sale.</summary>
		public int DiscountPercent => item.IsOnSale ? (int)System.Math.Round( 100.0 - (item.Price.Value * 100.0 / item.BasePrice.Value) ) : 0;

		/// <summary>Days until the item stops selling, null when it isn't time limited.</summary>
		public double? DaysLeft => item.SellEnd is null ? null : (item.SellEnd.Value - DateTime.UtcNow).TotalDays;

		/// <summary>Whether this item stops selling within a couple of days.</summary>
		public bool IsLeavingSoon => item.DaysLeft is <= 1.5;

		/// <summary>Short countdown text, e.g. "Leaving in 3 days", or null when not time limited.</summary>
		public string LeavingText
		{
			get
			{
				var days = item.DaysLeft;
				if ( days is null ) return null;
				if ( days <= 0 ) return "Leaving now";
				if ( days < 1 )
				{
					var hours = System.Math.Max( 1, (int)(days * 24) );
					return hours == 1 ? "Leaving in 1 hour" : $"Leaving in {hours} hours";
				}
				if ( days < 2 ) return "Leaving tomorrow";
				return $"Leaving in {System.Math.Floor( days.Value )} days";
			}
		}

		/// <summary>The category name with spaces between words, e.g. "Hair Long".</summary>
		public string CategoryLabel
		{
			get
			{
				if ( string.IsNullOrEmpty( item.Category ) ) return "Other";

				var sb = new System.Text.StringBuilder();
				foreach ( var c in item.Category )
				{
					if ( char.IsUpper( c ) && sb.Length > 0 ) sb.Append( ' ' );
					sb.Append( c );
				}
				return sb.ToString();
			}
		}

		/// <summary>The large icon when there is one, otherwise the regular icon.</summary>
		public string BestIconUrl => string.IsNullOrEmpty( item.IconUrlLarge ) ? item.IconUrl : item.IconUrlLarge;
	}
}
