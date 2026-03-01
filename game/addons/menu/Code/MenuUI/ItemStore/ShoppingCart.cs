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
	}

	public void RemoveFromCart( Sandbox.Services.Inventory.ItemDefinition item )
	{
		Items.Remove( item );
	}

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

		CheckingOut = true;

		try
		{
			var success = await MenuUtility.Inventory.CheckOut( Items );

			Log.Info( $"Checkout Success: {success}" );

			if ( success )
			{
				var itemNames = Items.Select( x => x.Name ).ToList();
				Items.Clear();

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
		}
	}

	public bool CheckingOut { get; set; }
}
