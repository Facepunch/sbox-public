using Sandbox;

namespace MenuProject.Platform;

/// <summary>
/// The shelves that come up out of the dock, by url - pages of the <see cref="ShelfPanel"/>, each a panel with its
/// route. What's up is the shelf's url; null when none is.
/// </summary>
public static class PlatformShelf
{
	public const string YourGames = "/shelf/games";

	/// <summary>
	/// The Games tab browses the Workshop with the search and sort supplied by <see cref="BrowseFor"/>.
	/// </summary>
	public const string Browse = "/shelf/browse";

	public const string Multiplayer = "/shelf/multiplayer";

	public const string Mounts = "/shelf/mounts";

	/// <summary>
	/// Your avatar - what you wear, tried on and put on, and the shop's items to buy, in place of the avatar editor's
	/// scene and the item store
	/// </summary>
	public const string Avatar = "/shelf/avatar";

	/// <summary>
	/// Opening your reward drop - its crate and cards in a scene on the shelf (<see cref="UnboxShelf"/>)
	/// </summary>
	public const string Unbox = "/shelf/unbox";

	const string DetailPrefix = "/shelf/details/";

	/// <summary>
	/// A game's page - its gallery, what it is, and the way into it. In place of the game modal. Opened on one of its
	/// pages - "changes" or "achievements" - rather than its overview, given <paramref name="page"/>.
	/// </summary>
	public static string Game( string ident, string page = null ) => DetailPrefix + "game/" + Uri.EscapeDataString( ident ) + (page is null ? "" : $"?Show={Uri.EscapeDataString( page )}");

	/// <summary>
	/// A map's page, addressed by Workshop ID or the local project's primary asset.
	/// </summary>
	public static string Map( string map ) => DetailPrefix + "map/" + Uri.EscapeDataString( map );

	/// <summary>
	/// Whether this shelf shows an item's details.
	/// </summary>
	public static bool IsDetail( string url ) => url?.StartsWith( DetailPrefix ) == true;

	public static string BrowseFor( string query, Storage.SortOrder? sort )
	{
		var args = new List<string>();
		if ( !string.IsNullOrWhiteSpace( query ) ) args.Add( $"SearchFor={Uri.EscapeDataString( query )}" );
		if ( sort is { } order ) args.Add( $"SortBy={order}" );

		return args.Count == 0 ? Browse : $"{Browse}?{string.Join( "&", args )}";
	}
}
