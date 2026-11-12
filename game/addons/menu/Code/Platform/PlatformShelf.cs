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

/// <summary>
/// A page on the shelf - how it sits on it. By default it fills the shelf up to the top of the screen, scrolling its
/// own columns.
/// </summary>
public interface IShelfPage
{
	/// <summary>
	/// Brought up on the shelf - from the dock, or from another page on it. Not when it's gone back to. It starts again
	/// from what it was opened with.
	/// </summary>
	void OnShelfShown() { }

	/// <summary>
	/// Up to the top of the screen, whatever's on it. Otherwise as tall as <see cref="Height"/> says.
	/// </summary>
	bool Fills => true;

	/// <summary>
	/// How tall it wants the shelf, when it doesn't fill it - given how tall it is (<paramref name="tall"/>). The shelf's
	/// never taller than <paramref name="room"/>.
	/// </summary>
	float Height( float room, float tall ) => room;

	/// <summary>
	/// When it doesn't fill the shelf, whether the shelf scrolls it past its height - or just shows what fits
	/// </summary>
	bool Scrolls => true;

	/// <summary>
	/// Right down to the dock's top - fading off into it
	/// </summary>
	bool ToDock => false;

	/// <summary>
	/// Clicks off its own panels go through to the blur behind the shelf, which puts it away - for a page that doesn't
	/// fill the shelf's width
	/// </summary>
	bool ClicksThrough => false;
}
