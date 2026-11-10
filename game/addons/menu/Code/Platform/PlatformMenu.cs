using Sandbox;

namespace MenuProject.Platform;

/// <summary>
/// The platform menu (<c>menu_platform 1</c>): s&amp;box opens on a game's own menu, with a dock along the bottom to
/// switch games. A game with a <c>MenuScene</c> is loaded onto it (<see cref="MenuUtility.OpenGameMenu"/>); one
/// without isn't loaded until it's played, and gets the default front made from its Workshop item
/// (<see cref="GameFront"/>). Games are remembered by their Workshop id.
/// </summary>
public static class PlatformMenu
{
	/// <summary>
	/// On - the platform menu runs instead of the main menu. Not in the editor, which has no menu. The menu_platform
	/// ConVar as it was when the menu last started (MenuSystem.Init) - changing it restarts the menu, so everything's
	/// built for one or the other, never half of each. The one check for being on the platform.
	/// </summary>
	public static bool Enabled { get; private set; }

	/// <summary>
	/// The menu's starting - on the platform or not, from scratch. Whatever the last run left goes: what game it was on,
	/// and a switch it was part way through.
	/// </summary>
	public static void Reset()
	{
		Enabled = MenuUtility.PlatformMenu && !Application.IsEditor;

		Current = null;
		Details = null;
		IsSwitching = false;
		IsCovering = false;
	}

	/// <summary>
	/// Not playing - on a game's menu or no game at all. Games are only switched from here, never out of one that's
	/// being played.
	/// </summary>
	public static bool CanSwitch => !Game.InGame || Game.IsOnMenu;

	public static string WhyCantStart( Package game )
	{
		if ( !MenuHelpers.HasAuthority ) return "Only the party leader can start a game";
		if ( game?.Info.IsVrOnly == true && !Application.IsVR ) return "This game needs a VR headset";

		return null;
	}

	/// <summary>
	/// The game the platform's on. Null until the first switch.
	/// </summary>
	public static Package Current { get; private set; }

	/// <summary>
	/// What the Workshop says about <see cref="Current"/> - its art, votes, owner. Null for a local project, or until
	/// it's fetched.
	/// </summary>
	public static WorkshopItemDetails Details { get; private set; }

	public static string Art => Details?.Images.FirstOrDefault() ?? Current?.ThumbWide ?? Current?.Thumb;

	/// <summary>
	/// Local projects opened this run (<c>-rungame path/to/game.sbproj</c>). Never remembered.
	/// </summary>
	public static List<Package> LocalGames { get; } = new();

	/// <summary>
	/// Whether <paramref name="package"/> brings its own menu scene, rather than the default front - its settings say
	/// once it's installed (or for a local project), its Workshop item before then
	/// </summary>
	public static bool HasOwnMenu( Package package )
	{
		if ( package is null ) return false;
		if ( !string.IsNullOrWhiteSpace( package.GetMeta<string>( "MenuScene", null ) ) ) return true;

		return package == Current && (Details?.KeyValues.Keys.Contains( "MenuScene", StringComparer.OrdinalIgnoreCase ) ?? false);
	}

	/// <summary>
	/// The same game - a local project is never the published game of the same name
	/// </summary>
	public static bool SameGame( Package a, Package b )
	{
		if ( a is null || b is null || a.IsRemote != b.IsRemote ) return false;

		return string.Equals( a.FullIdent.Split( '#' )[0], b.FullIdent.Split( '#' )[0], StringComparison.OrdinalIgnoreCase );
	}

	//
	// Remembered by Workshop id
	//

	const string GameCookie = "platform.game";
	const string RecentCookie = "platform.recent";
	const int MaxRecent = 16;

	static List<ulong> Recent
	{
		get => Game.Cookies.Get( RecentCookie, new List<ulong>() );
		set => Game.Cookies.Set( RecentCookie, value );
	}

	static async Task Remember( Package package )
	{
		if ( !package.IsRemote ) return;

		var id = await MenuUtility.GetWorkshopIdAsync( package );
		if ( id == 0 ) return;

		Game.Cookies.Set( GameCookie, id );

		var recent = Recent;
		recent.Remove( id );
		recent.Insert( 0, id );
		Recent = recent.Take( MaxRecent ).ToList();

		// Your games has it first straight away, and fetches again for one it hadn't got - ready before it's opened
		YourGames.MoveToFront( id );
		_ = YourGames.RefreshAsync();
	}

	//
	// Switching
	//

	/// <summary>
	/// Something's being switched - the blur's up, or on its way
	/// </summary>
	public static bool IsSwitching { get; private set; }

	/// <summary>
	/// The switch is going from a game's own scene - it's faded right out before it's torn down, so it doesn't cut
	/// </summary>
	public static bool IsCovering { get; private set; }

	/// <summary>
	/// At startup - a local project or game given on the command line, the game picked last time, or Sandbox.
	/// </summary>
	public static void Start()
	{
		// Switched on in the middle of a game - on the one being played, rather than out of it for the one picked last
		// time
		if ( Game.InGame && !Game.IsOnMenu && MenuUtility.GamePackage is { } playing )
		{
			Current = playing;
			_ = Remember( playing );
			_ = FetchDetailsAsync( playing );
			RefreshShelves();
			return;
		}

		var startup = StartupGame();

		Run( async () =>
		{
			var package = await startup;
			if ( package is not null ) await Switch( package );
		} );

		RefreshShelves();
	}

	/// <summary>
	/// The shelves fetched, so they're there when they're opened
	/// </summary>
	static void RefreshShelves()
	{
		foreach ( var row in Discover.Prepend( Favourites ).Prepend( YourGames ) )
			_ = row.RefreshAsync();
	}

	static async Task<Package> StartupGame()
	{
		var startup = MenuUtility.StartupGameIdent;

		if ( startup?.EndsWith( ".sbproj", StringComparison.OrdinalIgnoreCase ) == true )
			return await MenuUtility.AddLocalGame( startup );

		if ( !string.IsNullOrWhiteSpace( startup ) )
			return await Package.FetchAsync( startup, false );

		if ( Game.Cookies.Get<ulong>( GameCookie, 0 ) is var id and not 0 && await MenuUtility.FetchWorkshopGameAsync( id ) is { } remembered )
			return remembered;

		return await MenuUtility.FetchDefaultGameAsync();
	}

	/// <summary>
	/// Switch to <paramref name="package"/>, behind the blur
	/// </summary>
	public static void SwitchTo( Package package )
	{
		if ( package is null ) return;

		Run( () => Switch( package ) );
	}

	/// <summary>
	/// Back onto <see cref="Current"/>'s menu after a game closed itself - once it's gone. Its teardown runs a tick
	/// after it closes, and would reset everything under a load started before then.
	/// </summary>
	public static void ReturnAfterClose( Package closing )
	{
		Run( async () =>
		{
			await WaitUntil( () => !Game.InGame );
			await Switch( Current ?? closing );
		} );
	}

	static async Task Switch( Package package )
	{
		// The picker's copy is partial - the full one has its meta
		if ( package.IsRemote )
			package = await Package.FetchAsync( package.FullIdent, false ) ?? package;

		if ( !package.IsRemote && !LocalGames.Any( x => SameGame( x, package ) ) )
			LocalGames.Add( package );

		Current = package;
		Details = null;

		_ = Remember( package );

		// The front's made from what the Workshop says, which comes in on its own. It's only waited on when it's
		// what says whether the game has a menu of its own - not when its meta already does
		var details = FetchDetailsAsync( package );
		if ( !HasOwnMenu( package ) )
			await details;

		// Nothing to load for the default front - it's loaded when it's played. Nor in someone else's party, where
		// the leader picks what's played. Just out of whatever's running - and downloaded, behind the front, so it's
		// there to play.
		if ( !HasOwnMenu( package ) || !MenuHelpers.HasAuthority )
		{
			_ = DownloadAsync( package );

			var art = PreloadArtAsync();

			if ( Game.InGame )
			{
				Game.Close();
				await WaitUntil( () => !Game.InGame );
			}

			await art;
			return;
		}

		// The load leaves whatever's running itself, before it starts - not closed first, which tears down a tick
		// later and can reset everything under the load
		await MenuUtility.OpenGameMenu( package.FullIdent );
		await WaitUntil( () => !LoadingScreen.IsVisible );
	}

	/// <summary>
	/// A game picked from the Workshop, installed if it isn't yet - the Workshop downloads overlay shows it coming down.
	/// One with a menu of its own comes down as that's loaded instead.
	/// </summary>
	static async Task DownloadAsync( Package package )
	{
		if ( package is not { InstallsFromWorkshop: true } || MenuUtility.IsInstalled( package.WorkshopId ) ) return;

		if ( !await MenuUtility.InstallWorkshopItemAsync( package ) )
			Log.Warning( $"Couldn't download {package.FullIdent}" );
	}

	static async Task FetchDetailsAsync( Package package )
	{
		var id = await MenuUtility.GetWorkshopIdAsync( package );
		if ( id == 0 ) return;

		var details = await MenuUtility.FetchWorkshopItemAsync( id );
		if ( Current != package ) return;

		Details = details;
	}

	/// <summary>
	/// Games' change notes, by Workshop id - they take a few seconds to read off the Steam Community, so they're kept
	/// for ten minutes
	/// </summary>
	static readonly Dictionary<ulong, (Task<WorkshopChangeNotes> Task, RealTimeSince Fetched)> changeNotes = new();

	internal static Task<WorkshopChangeNotes> FetchChangeNotesAsync( ulong id )
	{
		// Still coming, or read - not one that couldn't be
		if ( changeNotes.TryGetValue( id, out var kept ) && kept.Fetched < 600 && (!kept.Task.IsCompleted || kept.Task is { IsCompletedSuccessfully: true, Result: not null }) )
			return kept.Task;

		var task = MenuUtility.FetchWorkshopChangeNotesAsync( id );
		changeNotes[id] = (task, 0);
		return task;
	}

	/// <summary>
	/// The front's art, held so it's still loaded when the front comes back in
	/// </summary>
	static Texture[] preloadedArt;

	/// <summary>
	/// Download the front's art before it's revealed, so it comes in with the front rather than popping in after - as
	/// much as there is after a few seconds
	/// </summary>
	static async Task PreloadArtAsync()
	{
		var loads = new[] { Art }
			.Where( x => !string.IsNullOrWhiteSpace( x ) )
			.Distinct()
			.Select( x => Texture.LoadAsync( x, false ) )
			.ToArray();

		await Task.WhenAny( Task.WhenAll( loads ), GameTask.DelayRealtime( 3000 ) );

		preloadedArt = loads.Where( x => x.IsCompletedSuccessfully ).Select( x => x.Result ).ToArray();
	}

	/// <summary>
	/// Run a switch behind the blur - in, the work, then out as soon as it's loaded. Ignored if one's already going.
	/// </summary>
	static async void Run( Func<Task> work )
	{
		if ( IsSwitching ) return;
		IsSwitching = true;
		IsCovering = Game.InGame;

		try
		{
			// The front's gone out first, so it's not seen changing - GameFront's :outro. A game's own scene, the veil
			// over it (PlatformRoot .veil.cover) - a frame longer than its fade, so it's all the way in.
			await GameTask.DelayRealtime( 250 );
			await work();
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Switching games failed: {e.Message}" );
		}
		finally
		{
			IsSwitching = false;
			IsCovering = false;
		}
	}

	/// <summary>
	/// A frame at a time until <paramref name="condition"/>, or <paramref name="timeout"/> seconds
	/// </summary>
	static async Task WaitUntil( Func<bool> condition, float timeout = 120 )
	{
		RealTimeSince waiting = 0;

		while ( !condition() && waiting < timeout )
			await GameTask.Yield();
	}

	//
	// Shelves - fetched at startup, and again when they're opened if they're getting old
	//

	/// <summary>
	/// Your games - played here lately, most recent first, then everything else installed. Sandbox always - last, when
	/// it hasn't been played here lately.
	/// </summary>
	public static GameRow YourGames { get; } = new( "Recent", "history", FindYourGames );

	/// <summary>
	/// The games you've favourited on the Workshop - under your recent ones, when there are any. Asked for again every time
	/// your games are opened, as they're favourited on Steam, out of sight.
	/// </summary>
	public static GameRow Favourites { get; } = new( "Favourites", "star", MenuUtility.FindFavouriteWorkshopGamesAsync );

	static async Task<WorkshopItemDetails[]> FindYourGames()
	{
		var recent = Recent;
		var games = await MenuUtility.FindYourWorkshopGamesAsync( recent );

		// Sandbox is always there - where it was played if it was, otherwise on the end, after everything installed
		if ( await SandboxWorkshopIdAsync() is not (not 0 and var id) || recent.Contains( id ) ) return games;

		var sandbox = games.FirstOrDefault( x => x.Id == id ) ?? await MenuUtility.FetchWorkshopItemAsync( id );
		if ( sandbox is null ) return games;

		return [.. games.Where( x => x.Id != id ), sandbox];
	}

	static ulong sandboxWorkshopId;

	/// <summary>
	/// Sandbox's Workshop id - asked once, then kept
	/// </summary>
	static async Task<ulong> SandboxWorkshopIdAsync()
	{
		if ( sandboxWorkshopId != 0 ) return sandboxWorkshopId;

		if ( await MenuUtility.FetchDefaultGameAsync() is { } sandbox )
			sandboxWorkshopId = await MenuUtility.GetWorkshopIdAsync( sandbox );

		return sandboxWorkshopId;
	}

	/// <summary>
	/// Discover's rows, top to bottom - just the first until View all
	/// </summary>
	public static GameRow[] Discover { get; } =
	[
		new( "Featured", "star", () => MenuUtility.FindFeaturedWorkshopGamesAsync(), 300 ),
		new( "Most played", "sports_esports", () => MenuUtility.FindWorkshopGamesAsync( sort: Storage.SortOrder.RankedByTotalPlaytime ), 300, Storage.SortOrder.RankedByTotalPlaytime ),
		new( "Trending", "trending_up", () => MenuUtility.FindWorkshopGamesAsync( sort: Storage.SortOrder.RankedByTrend ), 300, Storage.SortOrder.RankedByTrend ),
		new( "Newest", "new_releases", () => MenuUtility.FindWorkshopGamesAsync( sort: Storage.SortOrder.RankedByPublicationDate ), 300, Storage.SortOrder.RankedByPublicationDate ),
	];
}
