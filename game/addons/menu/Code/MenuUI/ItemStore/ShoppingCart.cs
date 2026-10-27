using Sandbox;

public class ShoppingCart
{
	public Sandbox.CurrencyValue CartValue
	{
		get
		{
			if ( Items.Count() == 0 ) return default;

			var x = Items.First().Price;

			foreach ( var i in Items.Skip( 1 ) )
			{
				x = x + i.Price;
			}

			return x;
		}
	}
	public List<Sandbox.Services.Inventory.ItemDefinition> Items = new();

	public void AddToCart( Sandbox.Services.Inventory.ItemDefinition item )
	{
		Items.Add( item );
	}

	public void RemoveAllFromCart( Sandbox.Services.Inventory.ItemDefinition item )
	{
		Items.RemoveAll( x => x.Id == item.Id );
		checkoutEntries?.RemoveAll( x => x.Id == item.Id );
	}

	public void RemoveFromCart( Sandbox.Services.Inventory.ItemDefinition item )
	{
		// Removes the first entry, which is one being checked out if there are any left
		if ( Items.Remove( item ) )
		{
			checkoutEntries?.Remove( item );
		}
	}

	/// <summary>
	/// Set how many of this item are in the cart, adding or removing to match.
	/// </summary>
	public void SetCount( Sandbox.Services.Inventory.ItemDefinition item, int count )
	{
		count = Math.Clamp( count, 0, MaxPerItem );

		while ( Count( item ) < count ) AddToCart( item );
		while ( Count( item ) > count ) RemoveFromCart( item );
	}

	/// <summary>
	/// The most of one item a single checkout will take.
	/// </summary>
	public const int MaxPerItem = 99;

	public bool Contains( Sandbox.Services.Inventory.ItemDefinition item )
	{
		return Items.Contains( item );
	}

	public int Count( Sandbox.Services.Inventory.ItemDefinition item )
	{
		return Items.Count( x => x == item );
	}

	public async Task CheckOut()
	{
		if ( CheckingOut ) return;

		// Steam only buys what's in the cart right now, items added while checking out stay in the cart
		var purchasing = Items.ToList();
		if ( purchasing.Count == 0 ) return;

		CheckingOut = true;
		checkoutEntries = purchasing.ToList();

		try
		{
			var success = await MenuUtility.Inventory.CheckOut( purchasing );

			Log.Info( $"Checkout Success: {success}" );

			if ( success )
			{
				var itemNames = purchasing.Select( x => x.Name ).ToList();

				// Entries being checked out always come before ones added since, so this leaves those alone
				foreach ( var item in checkoutEntries )
				{
					Items.Remove( item );
				}

				// Show a confirmation popup with the purchased items
				var itemCount = itemNames.Count;
				var message = itemCount == 1
					? $"You purchased {itemNames.First()}!"
					: $"You purchased {itemCount} items!";

				MenuOverlay.Show( message, "check_circle" );
			}
		}
		finally
		{
			CheckingOut = false;
			checkoutEntries = null;
		}
	}

	public bool CheckingOut { get; set; }

	/// <summary>
	/// Entries being checked out that are still in the cart.
	/// </summary>
	List<Sandbox.Services.Inventory.ItemDefinition> checkoutEntries;
}
