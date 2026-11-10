using System;

namespace Sandbox;

public static partial class MenuUtility
{
	/// <summary>
	/// The platform menu - s&amp;box opens on a game's own menu, with a dock to switch games - instead of the main menu.
	/// Changed, the menu restarts onto the other one (MenuDll).
	/// </summary>
	[MenuConVar( "menu_platform", Help = "Use the platform menu - games open on their own menu, with a dock to switch games. Switches straight away.", Saved = true )]
	public static bool PlatformMenu { get; set; }

	/// <summary>
	/// The part of the screen that's the game's (<see cref="Screen.SafeArea"/>) - what the platform menu leaves clear,
	/// its dock over a game's own menu. Null gives it the whole screen again.
	/// </summary>
	public static void SetSafeArea( Rect? area ) => Screen.SafeAreaOverride = area;

	/// <summary>
	/// Add a game project from disk (its .sbproj) and compile it here, against this engine - so a game can be tried in
	/// the menu without publishing it. Its local package, or null if it didn't load.
	/// </summary>
	public static async Task<Package> AddLocalGame( string sbprojPath )
	{
		var project = await Project.AddGameFromFile( sbprojPath );
		if ( project is null )
		{
			Log.Warning( $"Couldn't load the game project {sbprojPath}" );
			return null;
		}

		return project.Package;
	}

	/// <summary>
	/// The game the platform menu opens on before anything's been picked - Sandbox
	/// </summary>
	public static Task<Package> FetchDefaultGameAsync() => Package.FetchAsync( "facepunch.sandbox", false );

	/// <summary>
	/// The game published as this Workshop item, to play. Null if there isn't one.
	/// </summary>
	public static async Task<Package> FetchWorkshopGameAsync( ulong id )
	{
		if ( id == 0 ) return null;

		// As current as loading it checks for updates with, so Steam answers that check from its cache
		var item = await Workshop.FetchAsync( id, default, Workshop.UpdateCheckCacheAge );
		if ( item?.PackageIdent is not { } ident ) return null;

		return await Package.FetchAsync( ident, false );
	}

	/// <summary>
	/// The first couple of dozen games on the Workshop in <paramref name="sort"/>'s order - just the ones that can be
	/// played here, through <see cref="FetchWorkshopGameAsync"/>
	/// </summary>
	public static async Task<WorkshopItemDetails[]> FindWorkshopGamesAsync( Sandbox.Storage.SortOrder sort )
	{
		const int count = 24;

		var found = new List<WorkshopItemDetails>();
		var pages = SearchWorkshopGames( sort: sort );

		while ( found.Count < count && pages.HasMore )
			found.AddRange( await pages.NextAsync() );

		return found.Take( count ).ToArray();
	}

	/// <summary>
	/// Games on the Workshop, a page at a time - for a list that loads more as it's
	/// scrolled. With <paramref name="mine"/>, just what you've published - friends only and private too.
	/// </summary>
	public static WorkshopGameSearch SearchWorkshopGames( string search = null, Sandbox.Storage.SortOrder? sort = null, IEnumerable<string> tags = null, bool mine = false )
	{
		var query = new Workshop.Query( search ).WithType( Workshop.ItemType.Game );

		if ( mine )
			query.WithAuthor( Game.SteamId );

		foreach ( var tag in tags ?? [] )
			query.WithTag( tag );

		if ( sort is { } order )
		{
			query.WithSort( order switch
			{
				Sandbox.Storage.SortOrder.RankedByTrend => Workshop.SortOrder.Trending,
				Sandbox.Storage.SortOrder.RankedByPublicationDate => Workshop.SortOrder.Newest,
				Sandbox.Storage.SortOrder.RankedByLastUpdatedDate => Workshop.SortOrder.Updated,
				Sandbox.Storage.SortOrder.RankedByVotesUp => Workshop.SortOrder.Votes,
				Sandbox.Storage.SortOrder.RankedByTotalUniqueSubscriptions => Workshop.SortOrder.Subscriptions,
				Sandbox.Storage.SortOrder.RankedByTotalPlaytime => Workshop.SortOrder.Playtime,
				_ => Workshop.SortOrder.Popular
			} );
		}

		return new WorkshopGameSearch( query );
	}

	/// <summary>
	/// The tags a game can be found by - what it is, as its publisher picked
	/// </summary>
	public static IReadOnlyList<string> WorkshopGameTags => Workshop.GameTags;

	/// <summary>
	/// The tags a game can be found by - how it's played, from its player settings
	/// </summary>
	public static IReadOnlyList<string> WorkshopPlayerTags => Workshop.PlayerTags;

	/// <summary>
	/// The games sbox.game features, in its order, as the Workshop has them
	/// </summary>
	public static async Task<WorkshopItemDetails[]> FindFeaturedWorkshopGamesAsync( int count = 24 )
	{
		var featured = await Package.FindAsync( "type:game in:facepunch.featured-games", count );
		var ids = await Task.WhenAll( (featured?.Packages ?? []).Select( GetWorkshopIdAsync ) );

		return await DetailsOfGamesAsync( await FetchWorkshopItemsInOrderAsync( ids.Where( x => x != 0 ).Distinct().ToArray() ) );
	}

	/// <summary>
	/// Your Workshop games - the ones in <paramref name="recent"/> first, in that order, then everything else installed
	/// here. Just the ones that can be played here, through <see cref="FetchWorkshopGameAsync"/>.
	/// </summary>
	public static async Task<WorkshopItemDetails[]> FindYourWorkshopGamesAsync( IReadOnlyCollection<ulong> recent )
	{
		var played = await FetchWorkshopItemsInOrderAsync( recent );
		var installed = await new Workshop.Query().WithType( Workshop.ItemType.Game ).WithInstalled().RunAsync();

		return await DetailsOfGamesAsync( played.Concat( installed?.Items ?? [] ) );
	}

	/// <summary>
	/// The games you've favourited on the Workshop - just the ones that can be played here
	/// </summary>
	public static async Task<WorkshopItemDetails[]> FindFavouriteWorkshopGamesAsync()
	{
		var favourites = await new Workshop.Query().WithType( Workshop.ItemType.Game ).WithFavourited().RunAsync();
		return await DetailsOfGamesAsync( favourites?.Items ?? [] );
	}

	/// <summary>
	/// These items from the Workshop, in this order - leaving out any it hasn't got
	/// </summary>
	static async Task<Workshop.Item[]> FetchWorkshopItemsInOrderAsync( IReadOnlyCollection<ulong> ids )
	{
		if ( ids.Count == 0 ) return [];

		var found = (await Workshop.FetchAsync( ids, default )).ToDictionary( x => x.Id );
		return ids.Select( found.GetValueOrDefault ).Where( x => x is not null ).ToArray();
	}

	/// <summary>
	/// The details of the ones of these that are games that can be played here, once each - who made them asked of Steam
	/// </summary>
	internal static async Task<WorkshopItemDetails[]> DetailsOfGamesAsync( IEnumerable<Workshop.Item> items )
	{
		var games = items.Where( x => x?.PackageIdent is not null ).DistinctBy( x => x.Id ).ToArray();

		await RequestOwnerNamesAsync( games );
		return games.Select( WorkshopItemDetails.From ).ToArray();
	}
}
