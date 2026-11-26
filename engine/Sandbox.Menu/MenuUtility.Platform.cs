using System;

namespace Sandbox;

public static partial class MenuUtility
{
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
	/// Games registered in the shared project list, including projects from addon paths.
	/// </summary>
	public static Package[] FindInstalledGames() => FindInstalledProjects( "game" );

	/// <summary>
	/// Map packages available to the chooser from registered local projects.
	/// </summary>
	public static Package[] FindInstalledMaps() => FindInstalledProjects( "map" );

	static Package[] FindInstalledProjects( string type ) => Project.All
		.Where( x => x.Config.Type == type && !x.Broken )
		.OrderBy( x => x.Config.Title, StringComparer.OrdinalIgnoreCase )
		.Select( x => x.Package ).ToArray();

	/// <summary>
	/// The game the platform menu opens on before anything's been picked - the installed Sandbox addon.
	/// </summary>
	public static Package GetDefaultGame() => Project.FindByIdent( DefaultGameIdent ) is { Broken: false, Config.Type: "game" } project ? project.Package : null;

	/// <summary>
	/// The package the platform menu opens at startup.
	/// </summary>
	public const string DefaultGameIdent = "facepunch.sandbox";

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
	/// The first couple of dozen games on the Workshop in <paramref name="sort"/>'s order.
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
	/// scrolled. With <paramref name="mine"/>, just what you've published - friends only and private too. With
	/// <paramref name="subscribed"/>, just the ones you're subscribed to.
	/// </summary>
	public static WorkshopItemSearch SearchWorkshopGames( string search = null, Sandbox.Storage.SortOrder? sort = null, IEnumerable<string> tags = null, bool mine = false, bool subscribed = false )
	{
		var query = new Workshop.Query( search ).WithType( Workshop.ItemType.Game );

		if ( mine )
			query.WithAuthor( Game.SteamId );

		if ( subscribed )
			query.WithSubscribed();

		foreach ( var tag in tags ?? [] )
			query.WithTag( tag );

		if ( sort is { } order )
			query.WithSort( WorkshopSort( order ) );

		return new WorkshopItemSearch( query );
	}

	static Workshop.SortOrder WorkshopSort( Sandbox.Storage.SortOrder sort ) => sort switch
	{
		Sandbox.Storage.SortOrder.RankedByTrend => Workshop.SortOrder.Trending,
		Sandbox.Storage.SortOrder.RankedByPublicationDate => Workshop.SortOrder.Newest,
		Sandbox.Storage.SortOrder.RankedByLastUpdatedDate => Workshop.SortOrder.Updated,
		Sandbox.Storage.SortOrder.RankedByVotesUp => Workshop.SortOrder.Votes,
		Sandbox.Storage.SortOrder.RankedByTotalUniqueSubscriptions => Workshop.SortOrder.Subscriptions,
		Sandbox.Storage.SortOrder.RankedByTotalPlaytime => Workshop.SortOrder.Playtime,
		_ => Workshop.SortOrder.Popular
	};

	/// <summary>
	/// Maps on the Workshop, a page at a time - for a list that loads more as it's scrolled. With
	/// <paramref name="subscribed"/>, just the ones you're subscribed to.
	/// </summary>
	public static WorkshopItemSearch SearchWorkshopMaps( string search = null, Sandbox.Storage.SortOrder? sort = null, bool subscribed = false )
	{
		var query = new Workshop.Query( search ).WithType( Workshop.ItemType.Map );

		if ( subscribed )
			query.WithSubscribed();

		if ( sort is { } order )
			query.WithSort( WorkshopSort( order ) );

		return new WorkshopItemSearch( query );
	}

	/// <summary>
	/// The Workshop item ID the map chooser passes to <see cref="LaunchArguments.Map"/>. Null for a missing item.
	/// </summary>
	public static string LaunchMapOf( WorkshopItemDetails map ) => map is { Id: > 0 } ? map.Id.ToString() : null;

	/// <summary>
	/// Swaps a map's package ident for the Workshop item ID it's published as. Anything else comes back as it is.
	/// </summary>
	public static async Task<string> ResolveMapAsync( string map ) => (await MapReference.ResolveAsync( map )).Text;

	/// <summary>
	/// A map's name and picture, from its local project, its Workshop item or the mounted game it's in. The picture is
	/// a URL or a texture. Looking up these details does not install the map.
	/// </summary>
	public static async Task<(string Title, string Thumb, Texture Thumbnail)> FetchMapDetailsAsync( string map )
	{
		var details = await LookupMapAsync( map );
		return (details.Title, details.Thumb, details.Thumbnail);
	}

	/// <summary>
	/// Resolve a menu selection and get its display details without installing it.
	/// </summary>
	public static async Task<(string Map, string Title, string Thumb, Texture Thumbnail)> LookupMapAsync( string map )
	{
		var reference = await MapReference.ResolveAsync( map );
		var info = await reference.GetInfoAsync();
		return (reference.Text, info.Title, info.Thumb, info.Thumbnail);
	}

	/// <summary>
	/// The registered map project a map loads from, or null. It's found by the Workshop item it's published as, or by its identifier.
	/// </summary>
	public static Package FindMapProject( string map ) => new MapReference( map ).LocalProject;

	/// <summary>
	/// The Workshop item a map string names, or 0 if it isn't one.
	/// </summary>
	public static ulong GetMapWorkshopId( string map ) => ulong.TryParse( map, out var id ) ? id : 0;

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
	[Obsolete( "The Featured menu page has been retired. Use SearchWorkshopGames to browse games." )]
	public static async Task<WorkshopItemDetails[]> FindFeaturedWorkshopGamesAsync( int count = 24 )
	{
		var featured = await Package.FindAsync( "type:game in:facepunch.featured-games", count );
		var ids = await Task.WhenAll( (featured?.Packages ?? []).Select( GetWorkshopIdAsync ) );

		return await DetailsOfItemsAsync( await FetchWorkshopItemsInOrderAsync( ids.Where( x => x != 0 ).Distinct().ToArray() ) );
	}

	/// <summary>
	/// Your Workshop games - the ones in <paramref name="recent"/> first, in that order, then everything else installed
	/// here.
	/// </summary>
	public static async Task<WorkshopItemDetails[]> FindYourWorkshopGamesAsync( IReadOnlyCollection<ulong> recent )
	{
		var played = await FetchWorkshopItemsInOrderAsync( recent );
		var installed = await new Workshop.Query().WithType( Workshop.ItemType.Game ).WithInstalled().RunAsync();

		return await DetailsOfItemsAsync( played.Concat( installed?.Items ?? [] ) );
	}

	/// <summary>
	/// The games you've favourited on the Workshop.
	/// </summary>
	public static async Task<WorkshopItemDetails[]> FindFavouriteWorkshopGamesAsync()
	{
		var favourites = await new Workshop.Query().WithType( Workshop.ItemType.Game ).WithFavourited().RunAsync();
		return await DetailsOfItemsAsync( favourites?.Items ?? [] );
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
	/// The details of Workshop items, once each, with their creators' names requested from Steam.
	/// </summary>
	internal static async Task<WorkshopItemDetails[]> DetailsOfItemsAsync( IEnumerable<Workshop.Item> items )
	{
		var uniqueItems = items.Where( x => x is not null ).DistinctBy( x => x.Id ).ToArray();

		await RequestOwnerNamesAsync( uniqueItems );
		return uniqueItems.Select( WorkshopItemDetails.From ).ToArray();
	}
}
