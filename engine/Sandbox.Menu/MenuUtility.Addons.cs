namespace Sandbox;

/// <summary>
/// Workshop addon browsing and per-game preferences for the menu.
/// </summary>
public static partial class MenuUtility
{
	/// <summary>
	/// Browse Workshop addons targeting this game, or just its subscribed addons. <paramref name="addonType"/> narrows it
	/// to one of the game's addon types.
	/// </summary>
	public static WorkshopItemSearch SearchWorkshopAddons( string gameIdent, string search = null, string source = "subscribed", string addonType = null )
	{
		var query = new Workshop.Query( search )
			.WithType( Workshop.ItemType.Addon )
			.WithTag( $"target_ident:{gameIdent}" );

		if ( !string.IsNullOrEmpty( addonType ) )
			query.WithTag( addonType );

		switch ( source )
		{
			case "subscribed": query.WithSubscribed(); break;
			case "trending": query.WithSort( Workshop.SortOrder.Trending ); break;
			case "rated": query.WithSort( Workshop.SortOrder.Votes ); break;
			case "newest": query.WithSort( Workshop.SortOrder.Newest ); break;
			case "followed": query.WithSort( Workshop.SortOrder.Followed ); break;
			case "favourites": query.WithFavourited(); break;
			case "mine": query.WithAuthor( Game.SteamId ); break;
			case "downloaded": query.WithInstalled(); break;
			default: throw new System.ArgumentOutOfRangeException( nameof( source ) );
		}

		return new WorkshopItemSearch( query );
	}

	/// <summary>
	/// Whether this addon is enabled for the next time the user hosts this game.
	/// </summary>
	public static bool IsAddonEnabled( string gameIdent, ulong id ) => Workshop.IsAddonEnabled( gameIdent, id );

	/// <summary>
	/// Enable or disable an addon for this game without unsubscribing from it.
	/// </summary>
	public static void SetAddonEnabled( string gameIdent, ulong id, bool enabled ) => Workshop.SetAddonEnabled( gameIdent, id, enabled );
}
