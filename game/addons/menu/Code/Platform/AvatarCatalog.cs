using Sandbox;
using Sandbox.Services;

namespace MenuProject.Platform;

/// <summary>
/// Everything there is to wear, for the Avatar page - the citizen's clothes, and the shop's items as clothing - with
/// whether you own each, and its shop item. As the old avatar editor builds it (AvatarEditManager), which the old menu
/// keeps.
/// </summary>
public static class AvatarCatalog
{
	static List<Clothing> all;

	/// <summary>
	/// How many shop items there were when it was made - they come in after it can first be asked for
	/// </summary>
	static int madeWith = -1;

	public static IEnumerable<Clothing> All
	{
		get
		{
			if ( all is null || madeWith != Inventory.Definitions.Count )
				Make();

			return all.Where( x => IsOwned( x ) || IsForSale( x ) );
		}
	}

	static void Make()
	{
		madeWith = Inventory.Definitions.Count;
		all = new();

		foreach ( var clothing in ResourceLibrary.GetAll<Clothing>() )
		{
			if ( !clothing.ResourcePath.StartsWith( "models/citizen_clothes/" ) ) continue;

			all.Add( clothing );
		}

		foreach ( var item in Inventory.Definitions )
		{
			if ( all.Any( x => x.SteamItemDefinitionId == item.Id ) )
				continue;

			if ( item.StoreHidden && !Inventory.HasItem( item.Id ) )
				continue;

			var clothing = new Clothing
			{
				Title = item.Name,
				Category = Enum.TryParse<Clothing.ClothingCategory>( item.Category, out var category ) ? category : Clothing.ClothingCategory.HairLong,
				Icon = new Clothing.IconSetup() { Path = item.IconUrl },
				SteamItemDefinitionId = item.Id
			};

			if ( item.SellStart != null && item.SellStart > DateTime.UtcNow && !IsOwned( clothing ) )
				continue;

			all.Add( clothing );
		}
	}

	public static bool IsOwned( Clothing clothing ) => clothing.SteamItemDefinitionId is not { } id || Inventory.HasItem( id );

	public static bool IsForSale( Clothing clothing )
	{
		if ( Definition( clothing ) is not { } item ) return false;
		if ( item.StoreHidden || string.IsNullOrEmpty( item.Price.Currency ) || item.Price.Value <= 0 ) return false;

		var now = DateTime.UtcNow;
		return (item.SellStart is null || item.SellStart <= now) && (item.SellEnd is null || item.SellEnd > now);
	}

	/// <summary>
	/// In the shop - yours already or not: more can be bought
	/// </summary>
	public static bool InShop( Clothing clothing ) => IsForSale( clothing );

	public static int OwnedCount( Clothing clothing ) =>
		clothing.SteamItemDefinitionId is { } id ? Inventory.Items.Count( x => x.DefinitionId == id ) : 0;

	public static Inventory.ItemDefinition Definition( Clothing clothing ) =>
		clothing.SteamItemDefinitionId is { } id ? Inventory.Definitions.FirstOrDefault( x => x.Id == id ) : null;

	static Dictionary<string, Inventory.ItemDefinition> fromWorkshop;
	static int fromWorkshopWith = -1;

	/// <summary>
	/// The shop item a Workshop package was made into - it's been voted in, and added to the game. Null if it
	/// hasn't been.
	/// </summary>
	public static Inventory.ItemDefinition AddedFrom( Package package )
	{
		if ( package is null ) return null;

		if ( fromWorkshop is null || fromWorkshopWith != Inventory.Definitions.Count )
		{
			fromWorkshopWith = Inventory.Definitions.Count;
			fromWorkshop = new( StringComparer.OrdinalIgnoreCase );

			foreach ( var item in Inventory.Definitions )
			{
				if ( Ident( item.PackageIdent ) is { } ident )
					fromWorkshop.TryAdd( ident, item );
			}
		}

		return Ident( package.FullIdent ) is { } key ? fromWorkshop.GetValueOrDefault( key ) : null;
	}

	/// <summary>
	/// An ident without its version, if it's got one - "org.thing#3" is "org.thing"
	/// </summary>
	static string Ident( string ident )
	{
		if ( string.IsNullOrWhiteSpace( ident ) ) return null;

		var hash = ident.IndexOf( '#' );
		return (hash < 0 ? ident : ident[..hash]).Trim();
	}

	/// <summary>
	/// Its picture - a url for a shop item, a path for the rest
	/// </summary>
	public static string IconOf( Clothing clothing ) => clothing.Icon.Path?.Trim( '/' );
}
