using Sandbox;

namespace MenuProject.Platform;

/// <summary>
/// The menu: s&amp;box opens on a game's own menu, with a dock along the bottom to
/// switch games. A game with a <c>MenuScene</c> is loaded onto it (<see cref="MenuUtility.OpenGameMenu"/>); one
/// without isn't loaded until it's played, and gets the default front made from its Workshop item
/// (<see cref="GameFront"/>). Games are remembered by their Workshop id, local projects by their ident.
/// </summary>
public static class PlatformMenu
{
	/// <summary>
	/// The menu's starting from scratch. Whatever the last run left goes: what game it was on, and a switch it was part
	/// way through.
	/// </summary>
	public static void Reset()
	{
		CurrentId = 0;
		Current = null;
		Details = null;
		PackageFailed = false;
		IsSwitching = false;
		IsCovering = false;
		LocalGames = [];
	}

	/// <summary>
	/// Not playing - on a game's menu or no game at all. Games are only switched from here, never out of one that's
	/// being played. In the editor, not in play mode.
	/// </summary>
	public static bool CanSwitch => Application.IsEditor ? !Game.IsPlaying : !Game.InGame || Game.IsOnMenu;

	/// <summary>
	/// Another game can be gone to - switched to, or a server joined. Not in the editor, where it's the game being worked
	/// on that's played; its menu is up just for what it has, a game's page instead of the way into it.
	/// </summary>
	public static bool CanChangeGame => !Application.IsEditor;

	/// <summary>
	/// Why the game the platform's on can't be started - a Workshop game's item says, a local project's package does
	/// </summary>
	public static string WhyCantStart()
	{
		if ( CurrentId == 0 ) return WhyCantStart( Current );

		if ( !MenuHelpers.HasAuthority ) return "Only the party leader can start a game";
		if ( Details?.GetMeta<ControlModeSettings>( "ControlModes", null )?.IsVROnly == true && !Application.IsVR ) return "This game needs a VR headset";

		return null;
	}

	public static string WhyCantStart( Package game )
	{
		if ( !MenuHelpers.HasAuthority ) return "Only the party leader can start a game";
		if ( game?.Info.IsVrOnly == true && !Application.IsVR ) return "This game needs a VR headset";

		return null;
	}

	/// <summary>
	/// The game the platform's on, by its Workshop id - 0 for a local project, or until the first switch
	/// </summary>
	public static ulong CurrentId { get; private set; }

	/// <summary>
	/// The game the platform's on, as a package - for a Workshop game, fetched alongside its Workshop item, so null
	/// until it's in, or if it couldn't be. Only playing it needs it: everything else about a Workshop game is from its
	/// item.
	/// </summary>
	public static Package Current { get; private set; }

	/// <summary>
	/// The Workshop game's package couldn't be got - it can't be played
	/// </summary>
	public static bool PackageFailed { get; private set; }

	/// <summary>
	/// What the Workshop says about the game the platform's on - its name, art, votes, owner. Null for a local
	/// project, or until it's fetched.
	/// </summary>
	public static WorkshopItemDetails Details { get; private set; }

	/// <summary>
	/// On a game - by its Workshop id, or a local project
	/// </summary>
	public static bool HasGame => CurrentId != 0 || Current is not null;

	//
	// About the game the platform's on - a Workshop game's from its item only, a local project's from its package
	//

	public static string Title => CurrentId != 0 ? Details?.Title : Current?.Title;

	public static string Art => CurrentId != 0 ? Details?.Images.FirstOrDefault() ?? Details?.Thumb : Current?.Screenshots?.FirstOrDefault()?.Url ?? Current?.ThumbWide ?? Current?.Thumb;

	public static string LaunchMode => (CurrentId != 0 ? Details?.GetMeta( "LaunchMode", "default" ) : Current?.Info.LaunchMode) ?? "default";

	public static int MaxPlayers => (CurrentId != 0 ? Details?.GetMeta( "MaxPlayers", 1 ) : Current?.Info.MaxPlayers) ?? 1;

	public static bool AllowsAddons => (CurrentId != 0 ? Details?.GetMeta( "AllowAddons", true ) : Current?.Info.AllowsAddons) ?? true;

	public static string[] AddonTypes => (CurrentId != 0 ? Details?.GetMeta<string[]>( "AddonTypes", null ) : Current?.Info.AddonTypes) ?? [];

	public static bool IsDedicatedServerOnly => string.Equals( LaunchMode, "dedicatedserveronly", StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// The ident the game the platform's on is played under - what its servers are found by
	/// </summary>
	public static string Ident => CurrentId != 0 ? Details?.KeyValues.GetValueOrDefault( "package" ) : Current?.FullIdent;

	/// <summary>
	/// Games from the shared project list, refreshed with the Your games shelf.
	/// </summary>
	public static IReadOnlyList<Package> LocalGames { get; private set; } = [];

	/// <summary>
	/// Whether <paramref name="package"/> brings its own menu scene, rather than the default front - its settings say
	/// once it's installed (or for a local project), its Workshop item before then
	/// </summary>
	public static bool HasOwnMenu( Package package )
	{
		if ( package is null ) return false;
		return !string.IsNullOrWhiteSpace( package.GetMeta<string>( "MenuScene", null ) );
	}

	/// <summary>
	/// The game the platform's on brings its own menu scene - its Workshop item says, before its package is in
	/// </summary>
	public static bool CurrentHasOwnMenu => CurrentId != 0 ? Details?.KeyValues.Keys.Contains( "MenuScene", StringComparer.OrdinalIgnoreCase ) ?? false : HasOwnMenu( Current );

	/// <summary>
	/// <paramref name="package"/> is the game the platform's on
	/// </summary>
	public static bool IsCurrent( Package package )
	{
		if ( package is null ) return false;

		return SameGame( package, Current ) || (package.IsRemote && CurrentId != 0 && MenuUtility.KnownWorkshopId( package ) == CurrentId);
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
	// Remembered by Workshop id - or a local project, such as the bundled Sandbox, by its ident
	//

	const string GameCookie = "platform.game";
	const string LocalGameCookie = "platform.game.local";
	const string RecentCookie = "platform.recent";
	const int MaxRecent = 7;

	static List<ulong> Recent
	{
		get => Game.Cookies.Get( RecentCookie, new List<ulong>() ).Take( MaxRecent ).ToList();
		set => Game.Cookies.Set( RecentCookie, value );
	}

	/// <summary>
	/// The game can come out of your games - not the one you're on
	/// </summary>
	public static bool CanRemoveFromLibrary( ulong id ) => id != 0 && id != CurrentId;

	/// <summary>
	/// Out of your recent games - playing it again puts it back. Still under Downloaded while it's installed.
	/// </summary>
	public static void RemoveFromLibrary( ulong id )
	{
		if ( !CanRemoveFromLibrary( id ) ) return;

		Recent = Recent.Where( x => x != id ).ToList();
		YourGames.Remove( id );
	}

	/// <summary>
	/// Off this machine - unsubscribed, or a download that was only to play let go; Steam deletes it once the game's
	/// closed. Not the one you're on.
	/// </summary>
	public static void DeleteDownload( WorkshopItemDetails item )
	{
		if ( item is null || !CanRemoveFromLibrary( item.Id ) ) return;

		MenuUtility.RemoveWorkshopDownload( new WorkshopStorageItem( item, "Game", 0, MenuUtility.IsSubscribed( item.Id ) ) );
		Downloaded.Remove( item.Id );
	}

	static void Remember( ulong id )
	{
		if ( id == 0 ) return;

		Game.Cookies.Set( GameCookie, id );
		Game.Cookies.Remove( LocalGameCookie );

		var recent = Recent;
		recent.Remove( id );
		recent.Insert( 0, id );
		Recent = recent.Take( MaxRecent ).ToList();

		// Your games has it first straight away, and fetches again for one it hadn't got - ready before it's opened
		YourGames.MoveToFront( id );
		_ = YourGames.RefreshAsync();

		// And it's downloaded now, or will be
		_ = Downloaded.RefreshAsync();
	}

	/// <summary>
	/// A local project picked - opened on next time, while it's still registered, over the Workshop game picked before it
	/// </summary>
	static void Remember( Package local )
	{
		if ( local is null || local.IsRemote || !Package.TryParseIdentParts( local.FullIdent, out var ident ) ) return;

		Game.Cookies.Set( LocalGameCookie, Package.FormatIdent( ident.org, ident.package ) );
	}

	/// <summary>
	/// The local project picked last time, if it's still one of the installed games the dock offers - registered, and
	/// not broken
	/// </summary>
	static Package RememberedLocal()
	{
		if ( !Package.TryParseIdentParts( Game.Cookies.Get<string>( LocalGameCookie, null ), out var remembered ) ) return null;

		return MenuUtility.FindInstalledGames().FirstOrDefault( x => Package.TryParseIdentParts( x.FullIdent, out var ident )
			&& string.Equals( ident.org, remembered.org, StringComparison.OrdinalIgnoreCase )
			&& string.Equals( ident.package, remembered.package, StringComparison.OrdinalIgnoreCase ) );
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
		if ( AdoptPlaying() )
		{
			RefreshShelves();
			return;
		}

		Run( async () =>
		{
			// Launched into a join - a local instance joining the editor, or a friend's invite.
			// Wait for the connection to finish / fail, then try to adopt that game.
			await WaitUntil( () => !MenuUtility.IsJoining, float.PositiveInfinity );

			if ( AdoptPlaying() ) return;

			var startup = MenuUtility.StartupGameIdent;

			// A local project, or a game by its ident - on the command line
			if ( startup?.EndsWith( ".sbproj", StringComparison.OrdinalIgnoreCase ) == true )
			{
				if ( await MenuUtility.AddLocalGame( startup ) is { } local ) await Switch( local );
				return;
			}

			if ( !string.IsNullOrWhiteSpace( startup ) )
			{
				if ( await Package.FetchAsync( startup, false ) is { } given ) await Switch( given );
				return;
			}

			if ( RememberedLocal() is { } rememberedLocal )
			{
				await Switch( rememberedLocal );
				return;
			}

			// Existing Sandbox selections now open the bundled addon, including older Workshop cookies.
			var remembered = Game.Cookies.Get<ulong>( GameCookie, 0 );
			if ( remembered != 0 && remembered != 3832911306 )
			{
				await Switch( remembered );
			}
			else
			{
				await SwitchDefault();
			}
		} );

		RefreshShelves();
	}

	/// <summary>
	/// On the game being played, if there is one, instead of switching to another
	/// </summary>
	static bool AdoptPlaying()
	{
		if ( !Game.InGame || Game.IsOnMenu ) return false;

		if ( MenuUtility.GamePackage is { } playing )
		{
			Current = playing;
			_ = AdoptAsync( playing );
		}

		return true;
	}

	/// <summary>
	/// The game being played when the platform came on - its Workshop item, to show it by
	/// </summary>
	static async Task AdoptAsync( Package playing )
	{
		var id = await MenuUtility.GetWorkshopIdAsync( playing );
		if ( id == 0 || Current != playing ) return;

		CurrentId = id;
		Remember( id );

		var details = await MenuUtility.FetchWorkshopItemAsync( id );
		if ( CurrentId == id ) Details = details;
	}

	/// <summary>
	/// The shelves fetched, so they're there when they're opened
	/// </summary>
	static void RefreshShelves()
	{
		_ = YourGames.RefreshAsync();
		_ = Favourites.RefreshAsync();
		_ = Downloaded.RefreshAsync();
		_ = RefreshFavouriteIdsAsync();
	}

	//
	// Favourites - on the Workshop, anything you can favourite there, games and maps alike
	//

	/// <summary>
	/// What you've favourited on the Workshop, by id - fetched with the shelves, and changed straight away when you
	/// favourite something here (<see cref="SetFavourite"/>)
	/// </summary>
	public static HashSet<ulong> FavouriteIds { get; private set; } = new();

	static async Task RefreshFavouriteIdsAsync()
	{
		FavouriteIds = await MenuUtility.FindWorkshopFavouriteIdsAsync();
	}

	public static bool IsFavourite( ulong id ) => id != 0 && FavouriteIds.Contains( id );

	/// <summary>
	/// Favourite Workshop item <paramref name="id"/>, or take it off - shown as done straight away, and put back if
	/// Steam doesn't take it. The Favourites row's fetched again, to have it.
	/// </summary>
	public static async void SetFavourite( ulong id, bool favourite )
	{
		if ( id == 0 ) return;

		if ( favourite ) FavouriteIds.Add( id );
		else FavouriteIds.Remove( id );

		// Off the row straight away - the refresh below puts it back if the Workshop didn't take it
		if ( !favourite ) Favourites.Remove( id );

		if ( !await MenuUtility.FavouriteOnWorkshopAsync( id, favourite ) )
		{
			if ( favourite ) FavouriteIds.Remove( id );
			else FavouriteIds.Add( id );

			MenuOverlay.Instance?.BottomRight?.Queue( new MenuProject.Toast { Title = "Couldn't change your favourites", Icon = "star" } );
			_ = Favourites.RefreshAsync();
			return;
		}

		_ = Favourites.RefreshAsync();
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
	/// Switch to the game published as Workshop item <paramref name="id"/>, behind the blur
	/// </summary>
	public static void SwitchTo( ulong id )
	{
		if ( id == 0 ) return;

		Run( () => Switch( id ) );
	}

	/// <summary>
	/// Out of whatever's being played, onto Sandbox - the way out of a game, whatever it does with its scenes
	/// </summary>
	public static void LeaveToDefault() => Run( SwitchDefault );

	/// <summary>
	/// Open the default Sandbox game.
	/// </summary>
	static async Task SwitchDefault()
	{
		var package = await Package.FetchAsync( MenuUtility.DefaultGameIdent, false );
		if ( package is not { TypeName: "game" } )
		{
			Log.Warning( "Could not find the Sandbox game package." );
			return;
		}

		await Switch( package );
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

			if ( CurrentId != 0 ) await Switch( CurrentId );
			else await Switch( Current ?? closing );
		} );
	}

	/// <summary>
	/// Onto a local project - or a game by its package, which goes by its Workshop id if it has one
	/// </summary>
	static async Task Switch( Package package )
	{
		if ( package is null ) return;
		package = await Package.FetchAsync( package.FullIdent, false ) ?? package;

		if ( package.IsRemote )
		{
			if ( await MenuUtility.GetWorkshopIdAsync( package ) is var id and not 0 )
			{
				await Switch( id );
				return;
			}
		}

		CurrentId = 0;
		Current = package;
		Details = null;
		PackageFailed = false;

		Remember( package );

		// Not in someone else's party, where the leader picks what's played
		if ( HasOwnMenu( package ) && MenuHelpers.HasAuthority )
		{
			await OpenOwnMenu( package );
			return;
		}

		_ = DownloadAsync( package );
		await ShowFront();
	}

	/// <summary>
	/// Onto the game published as Workshop item <paramref name="id"/> - its front's made from the Workshop item, and
	/// its package comes alongside (<see cref="SideloadAsync"/>). Only a game with its own menu scene waits on that,
	/// to load it.
	/// </summary>
	static async Task Switch( ulong id )
	{
		CurrentId = id;
		Current = null;
		Details = null;
		PackageFailed = false;

		Remember( id );

		var details = await MenuUtility.FetchWorkshopItemAsync( id );
		if ( CurrentId != id ) return;

		// Workshop selections still use the installed game's presentation and launch settings.
		if ( Package.TryGetCached( details?.KeyValues.GetValueOrDefault( "package" ), out var local ) && local is { IsRemote: false } )
		{
			await Switch( local );
			return;
		}

		Details = details;

		// Not in someone else's party, where the leader picks what's played
		var ownMenu = CurrentHasOwnMenu && MenuHelpers.HasAuthority;
		var package = SideloadAsync( id, download: !ownMenu );

		if ( ownMenu && await package is { } loaded && CurrentId == id )
		{
			await OpenOwnMenu( loaded );
			return;
		}

		await ShowFront();
	}

	/// <summary>
	/// The package for Workshop item <paramref name="id"/>, as <see cref="Current"/> once it's in - downloaded, behind
	/// the front, so it's there to play
	/// </summary>
	static async Task<Package> SideloadAsync( ulong id, bool download )
	{
		var package = await MenuUtility.FetchWorkshopGameAsync( id );
		if ( CurrentId != id ) return null;

		if ( package is null )
		{
			Log.Warning( $"Couldn't get the package for Workshop item {id}" );
			PackageFailed = true;
			return null;
		}

		Current = package;

		if ( download ) _ = DownloadAsync( package );
		return package;
	}

	/// <summary>
	/// The default front - nothing to load, it's loaded when it's played. Just out of whatever's running, with its art
	/// ready.
	/// </summary>
	static async Task ShowFront()
	{
		var art = PreloadArtAsync();

		if ( Game.InGame )
		{
			Game.Close();
			await WaitUntil( () => !Game.InGame );
		}

		await art;
	}

	/// <summary>
	/// Loaded onto its own menu scene. The load leaves whatever's running itself, before it starts - not closed first,
	/// which tears down a tick later and can reset everything under the load.
	/// </summary>
	static async Task OpenOwnMenu( Package package )
	{
		// Not over a join that's started meanwhile
		if ( MenuUtility.IsJoining ) return;

		// A notice stays up, such as why a join at launch failed
		ModalSystem.Instance?.CloseAll( false, keepNotice: true );

		if ( !await MenuUtility.OpenGameMenu( package.FullIdent ) ) return;
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
	/// Your library - installed local addons, then the Workshop games played here lately. Everything else installed is
	/// under <see cref="Downloaded"/>.
	/// </summary>
	public static GameRow YourGames { get; } = new( "Recents", "sports_esports", FindYourGames );

	/// <summary>
	/// The games you've favourited on the Workshop - under your recent ones, when there are any. Asked for again every time
	/// your games are opened, as they're favourited on Steam, out of sight.
	/// </summary>
	public static GameRow Favourites { get; } = new( "Favourites", "star", MenuUtility.FindFavouriteWorkshopGamesAsync );

	/// <summary>
	/// Every Workshop game downloaded here - under your favourites, for the ones the library's first row hasn't room for,
	/// and those taken out of the library: it's what's on disk, whatever's in your library
	/// </summary>
	public static GameRow Downloaded { get; } = new( "Downloaded", "download_done", () => FindGames( [], false ) );

	static Task<WorkshopItemDetails[]> FindYourGames() => FindGames( Recent, true );

	/// <summary>
	/// The Workshop games - <paramref name="recent"/> first, then everything installed, or with <paramref name="onlyRecent"/>
	/// just them - less those that are a local game already
	/// </summary>
	static async Task<WorkshopItemDetails[]> FindGames( List<ulong> recent, bool onlyRecent )
	{
		LocalGames = MenuUtility.FindInstalledGames();

		var games = await MenuUtility.FindYourWorkshopGamesAsync( recent );
		return games.Where( x => (!onlyRecent || recent.Contains( x.Id )) && !LocalGames.Any( local => string.Equals( local.FullIdent.Split( '#' )[0],
			x.KeyValues.GetValueOrDefault( "package" ), StringComparison.OrdinalIgnoreCase ) ) ).ToArray();
	}

}
