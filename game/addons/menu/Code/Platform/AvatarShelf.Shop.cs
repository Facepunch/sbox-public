using Sandbox;

namespace MenuProject.Platform;

/// <summary>
/// The Avatar page's shop - what's listed, a shop item's card and buying it, and the cart
/// </summary>
partial class AvatarShelf
{
	record Section( string Title, string Icon, List<Clothing> Items, bool Shop, bool Wide );

	/// <summary>
	/// What the panel lists. On the Shop tab, what's only here for a while (the soonest to go first), then the rest of
	/// it - searched, what's in the shop by that name, then what's yours that isn't. On Inventory, what you've bought.
	/// A slot picked, what's in the shop for it, then all yours for it.
	/// </summary>
	List<Section> Sections
	{
		get
		{
			var sections = new List<Section>();

			void Add( string title, string icon, IEnumerable<Clothing> items, bool shop, bool wide = false )
			{
				var list = items.ToList();
				if ( list.Count > 0 ) sections.Add( new( title, icon, list, shop, wide ) );
			}

			// The Workshop's its own grid (WorkshopItems)
			if ( Category is null && On == Tab.Workshop ) return sections;

			if ( Category is null && On == Tab.Inventory )
			{
				Add( "Your items", "inventory_2", AvatarCatalog.All.Where( x => x.SteamItemDefinitionId is not null && AvatarCatalog.IsOwned( x ) && Matches( x ) ).OrderBy( x => x.Title ), false );
			}
			else if ( Category is null && HasQuery )
			{
				Add( "In the shop", "storefront", AvatarCatalog.All.Where( x => AvatarCatalog.InShop( x ) && Matches( x ) ).OrderBy( AvatarCatalog.IsOwned ).ThenBy( x => x.Title ), true );
				Add( "Yours", "checkroom", AvatarCatalog.All.Where( x => AvatarCatalog.IsOwned( x ) && !AvatarCatalog.InShop( x ) && Matches( x ) ).OrderBy( x => x.Title ), false );
			}
			else if ( Category is null )
			{
				var forSale = AvatarCatalog.All.Where( AvatarCatalog.InShop ).ToList();

				Add( "Limited", "schedule", forSale.Where( x => AvatarCatalog.Definition( x )?.SellEnd is not null ).OrderBy( AvatarCatalog.IsOwned ).ThenBy( x => AvatarCatalog.Definition( x ).SellEnd ), true, wide: true );
				Add( "In the shop", "storefront", forSale.Where( x => AvatarCatalog.Definition( x )?.SellEnd is null ).OrderBy( AvatarCatalog.IsOwned ).ThenBy( x => x.Title ), true );
			}
			else
			{
				Add( "In the shop", "storefront", Shown.Where( x => !AvatarCatalog.IsOwned( x ) && Matches( x ) ), true );
				Add( "Yours", "checkroom", Shown.Where( x => AvatarCatalog.IsOwned( x ) && Matches( x ) ), false );
			}

			return sections;
		}
	}

	IEnumerable<Clothing> Shown
	{
		get
		{
			AvatarClothingCategory.SubCategory[] kinds = Kind is not null ? [Kind] : Category?.SubCategories ?? [];
			var categories = kinds.SelectMany( x => x.Categories ?? [] ).ToHashSet();

			return AvatarCatalog.All.Where( x => categories.Contains( x.Category ) ).OrderBy( x => x.Title );
		}
	}

	/// <summary>
	/// How long a limited one's got, short - "2d 4h", "5h", "40m". Null for one that isn't limited.
	/// </summary>
	static string TimeLeft( Sandbox.Services.Inventory.ItemDefinition definition )
	{
		if ( definition?.SellEnd is not { } end ) return null;

		var left = end - DateTime.UtcNow;
		if ( left.TotalMinutes < 1 ) return "<1m";
		if ( left.TotalHours < 1 ) return $"{(int)left.TotalMinutes}m";
		if ( left.TotalDays < 1 ) return $"{(int)left.TotalHours}h";
		return left.Hours > 0 ? $"{(int)left.TotalDays}d {left.Hours}h" : $"{(int)left.TotalDays}d";
	}

	/// <summary>
	/// The minute it is - the countdowns on limited ones move on with it
	/// </summary>
	static long Minute => DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute;

	/// <summary>
	/// The shop item picked, its card up over the list's bottom - worn while it is
	/// </summary>
	Clothing Viewing;

	bool Buying;

	/// <summary>
	/// Bought, and to be put on once it's yours - the inventory says so a moment after the checkout does
	/// </summary>
	Clothing bought;

	void OpenItem( Clothing item )
	{
		if ( Viewing == item )
		{
			PutDown();
			return;
		}

		Picked = null;
		Viewing = item;
		Draft.Preview( item );
		FrameOnItem( item );
	}

	async void Buy()
	{
		if ( Buying || Viewing is not { } item || AvatarCatalog.Definition( item ) is not { } definition ) return;

		Buying = true;

		try
		{
			if ( !await MenuUtility.Inventory.CheckOut( [definition] ) ) return;

			bought = item;
			PutDown();
		}
		finally
		{
			Buying = false;
		}
	}

	void TickBought()
	{
		if ( bought is null || !AvatarCatalog.IsOwned( bought ) ) return;

		if ( !Draft.IsWorn( bought ) ) Draft.Toggle( bought );
		bought = null;
	}

	/// <summary>
	/// What you're buying together, kept while the menu is - the old store's (ShoppingCart)
	/// </summary>
	static readonly ShoppingCart Cart = new();

	bool CartOpen;

	void ShowCart()
	{
		PutDown();
		Head?.CloseSearch();
		Query = null;
		CartOpen = true;
		ScrollToTop();
	}

	void CloseCart()
	{
		CartOpen = false;
		ScrollToTop();
	}

	async void CheckOut()
	{
		if ( Cart.CheckingOut ) return;

		await Cart.CheckOut();

		if ( CartOpen && Cart.Items.Count == 0 )
			CloseCart();
	}
}
