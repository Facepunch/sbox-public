using Sandbox;

namespace MenuProject.Platform;

/// <summary>
/// The shelves that come up out of the dock, by url - pages of the <see cref="ShelfPanel"/>, each a panel with its
/// route. What's up is the shelf's url; null when none is.
/// </summary>
public static class PlatformShelf
{
	public const string YourGames = "/shelf/games";

	public const string Discover = "/shelf/discover";

	/// <summary>
	/// Searching the Workshop - sorted, filtered by tag. From the dock's search, or a Discover row's See more. What it
	/// starts on comes in its query (<see cref="BrowseFor"/>).
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

	const string GamePrefix = "/shelf/game/";

	/// <summary>
	/// A game's page - its gallery, what it is, and the way into it. In place of the game modal. Opened on one of its
	/// pages - "changes" or "achievements" - rather than its overview, given <paramref name="page"/>.
	/// </summary>
	public static string Game( string ident, string page = null ) => GamePrefix + ident + (page is null ? "" : $"?Show={Uri.EscapeDataString( page )}");

	public static bool IsGame( string url ) => url?.StartsWith( GamePrefix ) == true;

	public static string BrowseFor( string query, Storage.SortOrder? sort )
	{
		var args = new List<string>();
		if ( !string.IsNullOrWhiteSpace( query ) ) args.Add( $"SearchFor={Uri.EscapeDataString( query )}" );
		if ( sort is { } order ) args.Add( $"SortBy={order}" );

		return args.Count == 0 ? Browse : $"{Browse}?{string.Join( "&", args )}";
	}
}
