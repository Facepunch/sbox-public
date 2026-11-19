using Sandbox;
using Sandbox.DataModel;
using Sandbox.Diagnostics;
using Sandbox.Modals;
using MenuProject.Platform;
using MenuProject.MenuUI.Front;
using MenuPanel = MenuProject.UI.MenuPanel;

public static class MenuHelpers
{
	/// <summary>
	/// One hash for everything in <paramref name="items"/>, in order - for a BuildHash, instead of joining them into a
	/// string every frame
	/// </summary>
	public static int HashOf<T>( IEnumerable<T> items )
	{
		var hash = new HashCode();

		if ( items is not null )
			foreach ( var item in items ) hash.Add( item );

		return hash.ToHashCode();
	}

	/// <summary>
	/// One hash for what <paramref name="pick"/> takes from each of <paramref name="items"/>, in order. Pass a static
	/// lambda, so it isn't made again every frame.
	/// </summary>
	public static int HashOf<T, TValue>( IEnumerable<T> items, Func<T, TValue> pick )
	{
		var hash = new HashCode();

		if ( items is not null )
			foreach ( var item in items ) hash.Add( pick( item ) );

		return hash.ToHashCode();
	}

	/// <summary>
	/// Your avatar - a page up out of the dock, from a game too
	/// </summary>
	public static void OpenAvatarEditor() => MenuProject.Platform.PlatformRoot.Instance?.OpenShelf( MenuProject.Platform.PlatformShelf.Avatar );

	/// <summary>
	/// Do we have authority to start or join games.
	/// If we're in a party, only the party owner can start or join games.
	/// </summary>
	public static bool HasAuthority => PartyRoom.Current?.Owner.IsMe ?? true;

	/// <summary>
	/// General-purpose method to play a game package. Handles quickplay, dedicated servers,
	/// create-game modal, VR-only checks, default map fetching, and direct launch.
	/// <para>
	/// Reports the play to <see cref="Discovery"/> when it's given the button that was pressed
	/// (<paramref name="via"/>, and the tile it was on, <paramref name="source"/>) - as it goes, or for a
	/// game with something to set up first, once that's started (see GameModal.StartSetup). Not when
	/// the button's pressed and the setup's only opened: backed out of, it was never played.
	/// </para>
	/// <para>
	/// A game with something to set up first is set up in the game setup modal - or by
	/// <paramref name="openSetup"/>, where that's somewhere else (the platform menu's own page).
	/// </para>
	/// </summary>
	public static async void PlayGame( Package package, Package mapPackage = null, string via = null, Panel source = null, bool fitChecked = false, Action<Package> openSetup = null )
	{
		Assert.True( HasAuthority, "You do not have authority to start a game, only the party owner can do that." );

		// List results (hover cards, shelves) come without the package's meta - LaunchMode, MaxPlayers,
		// GameSettings - so get the full package before deciding how to launch it
		if ( package.IsRemote )
			package = await Package.FetchAsync( package.FullIdent, false ) ?? package;

		// Your party isn't what it's made for - say so, and only go on if you want to. On your own it always is, so
		// its workshop item's only asked about for a party.
		if ( !fitChecked && MenuProject.PlayerModes.PartySize > 1 )
		{
			var workshop = await MenuUtility.FetchWorkshopItemAsync( MenuUtility.KnownWorkshopId( package ) );
			if ( !ConfirmPartyFit( package, () => PlayGame( package, mapPackage, via, source, fitChecked: true, openSetup ), workshop ) )
				return;
		}

		// VR-only game but not in VR
		if ( package.Info.IsVrOnly && !Application.IsVR )
			return;

		var party = PartyRoom.Current;
		if ( !package.Info.IsDedicatedServerOnly && party?.IsGameSelected( package ) == true && party.SelectedGameSettings is not null )
		{
			if ( openSetup is not null ) openSetup( package );
			else ModalSystem.Instance?.SetUpGame( package );
			return;
		}

		// QuickPlay: try to join an existing lobby first - not for a game played on your own, whatever its launch mode
		if ( package.Info.IsQuickPlay && !IsSingleplayer( package ) )
		{
			// A lobby search is a go at playing it, whether it finds one or makes its own after
			ReportPlay( package, via, source );
			via = null;

			await PrepareForLoad( "#menu.games.finding_game", "#menu.games.please_wait_while_we_find_a_game_for_you_to_join" );

			if ( await MenuUtility.TryJoinLobby( package.FullIdent ) )
				return;

			Log.Info( $"Couldn't join a lobby - making a game" );
			LoadingScreen.IsVisible = false;
		}
		else if ( package.Info.IsDedicatedServerOnly )
		{
			// Dedicated server only: show server list
			Game.Overlay.ShowServerList( new ServerListConfig( package.FullIdent ) );
			return;
		}

		// Something to set up first - on its game page, then started from there, the play reported then
		if ( NeedsSetup( package ) )
		{
			if ( openSetup is not null ) openSetup( package );
			else ModalSystem.Instance?.SetUpGame( package, mapPackage?.FullIdent );
			return;
		}

		// Already on its own menu, with no map to load it with - it's started from there, by the game
		if ( mapPackage is null && string.IsNullOrWhiteSpace( package.Info.DefaultMap ) && Game.IsOnMenu && MenuUtility.GamePackage?.FullIdent == package.FullIdent )
			return;

		// Direct launch
		ReportPlay( package, via, source );

		await PrepareForLoad();

		if ( mapPackage is null )
		{
			// Fetch the default map if one is configured
			var defaultMap = package.Info.DefaultMap;
			if ( !string.IsNullOrWhiteSpace( defaultMap ) )
			{
				Log.Info( $"DefaultMap configured, launching game with map: {defaultMap}" );
				mapPackage = await Package.FetchAsync( defaultMap, false );
			}
		}

		if ( PartyRoom.Current is { } currentParty ) LaunchArguments.ServerName = currentParty.Name;

		if ( mapPackage is not null )
		{
			MenuUtility.OpenGameWithMap( package.FullIdent, mapPackage.FullIdent );
		}
		else
		{
			MenuUtility.OpenGame( package.FullIdent, true );
		}
	}

	internal static async Task StartConfiguredGame( Package package, CreateGameResults settings, PartyRoom party = null )
	{
		if ( !HasAuthority ) return;
		var startingParty = PartyRoom.Current;

		await PrepareForLoad();

		// Party membership or settings may change while the loading screen settles.
		var serverSlots = GameSetup.ClampServerSlots( package, settings.MaxPlayers );
		if ( PartyRoom.Current != startingParty )
		{
			LoadingScreen.IsVisible = false;
			throw new InvalidOperationException( Language.GetPhrase( "menu.common.your_party_has_changed_check_the_game_setup_and" ) );
		}

		if ( party is not null && (PartyRoom.Current != party || !party.Owner.IsMe || !party.IsGameSelected( package )
			|| party.SelectedGameSettings is not { } currentSettings || !GameSetup.SettingsEqual( settings, currentSettings )
			|| serverSlots < party.MemberCount) )
		{
			LoadingScreen.IsVisible = false;
			throw new InvalidOperationException( Language.GetPhrase( "menu.common.the_party_or_game_setup_changed_check_the_game" ) );
		}

		if ( !HasAuthority )
		{
			LoadingScreen.IsVisible = false;
			return;
		}

		LaunchArguments.MaxPlayers = serverSlots;

		if ( !string.IsNullOrEmpty( settings.ServerName ) ) LaunchArguments.ServerName = settings.ServerName;
		LaunchArguments.Privacy = settings.Privacy;

		// A party's copy of the setup leaves the password out - it's in the leader's own
		LaunchArguments.Password = settings.Password ?? GameSetup.GetInitialSettings( package ).Password;
		LaunchArguments.Map = null;

		if ( !string.IsNullOrEmpty( settings.Map ) )
		{
			MenuUtility.OpenGameWithMap( package.FullIdent, settings.Map, settings.GameSettings ?? new() );
		}
		else
		{
			MenuUtility.OpenGame( package.FullIdent, true, settings.GameSettings ?? new() );
		}
	}

	/// <summary>
	/// Whether your party's what the game's made for - true to go straight on. If it isn't, pops up why,
	/// with Play anyway (which runs <paramref name="playAnyway"/>) and Return, and gives false. Its
	/// <paramref name="workshop"/> item says how it's played, for one on the workshop.
	/// </summary>
	public static bool ConfirmPartyFit( Package package, Action playAnyway, WorkshopItemDetails workshop = null )
	{
		var size = MenuProject.PlayerModes.PartySize;
		var modes = MenuProject.PlayerModes.For( package, workshop );
		var (fit, why) = modes.FitFor( size );
		if ( fit != MenuProject.PlayerModes.FitKind.Poor ) return true;

		// Short of what it's made for, or past it - singleplayer, a full lobby, more than it's for
		var tooSmall = modes.Multiplayer && modes.RecommendedMin > size && (modes.MaxPlayers <= 0 || size <= modes.MaxPlayers);

		ModalSystem.Instance?.Open( new MenuProject.Modals.QuestionModal
		{
			Color = Color.Parse( "#f5a623" ) ?? Color.Orange,
			Title = tooSmall ? "#menu.games.your_party_is_too_small" : "#menu.games.your_party_is_too_big",
			Message = why,
			ConfirmText = "#menu.games.play_anyway",
			ConfirmIcon = "play_arrow",
			CancelText = "#menu.games.return",
			OnConfirm = playAnyway
		} );

		return false;
	}

	static void ReportPlay( Package package, string via, Panel source )
	{
		if ( via is not null )
			Discovery.Launching( package, via, source );
	}

	/// <summary>
	/// How long the screen gets to settle before a load starts - see <see cref="PrepareForLoad"/>.
	/// </summary>
	const int LoadWarmUpMilliseconds = 200;

	/// <summary>
	/// Get the screen ready for a load before starting it - modals closed, the loading screen up,
	/// then a moment for both to actually draw. The first steps of a load can hold the main thread
	/// for a while, and anything still animating when it does (a modal halfway through closing)
	/// freezes on screen until it lets go.
	/// </summary>
	public static async Task PrepareForLoad( string title = "#menu.common.loading_dots", string subtitle = "" )
	{
		MenuUtility.CloseAllModals();

		LoadingScreen.IsVisible = true;
		LoadingScreen.Title = title;
		LoadingScreen.Subtitle = subtitle;

		await Task.Delay( LoadWarmUpMilliseconds );
	}

	/// <summary>
	/// Played on your own - one player at most, or made for one (its RecommendedMaxPlayers, coop and versus, or failing
	/// those its tags - PlayerModes). Never a lobby search or a server's settings, whatever its launch mode or player
	/// count says.
	/// </summary>
	public static bool IsSingleplayer( Package package )
	{
		if ( package.Info.MaxPlayers <= 1 ) return true;

		var modes = MenuProject.PlayerModes.For( package );
		return modes.Solo && !modes.Multiplayer;
	}

	static bool NeedsSetup( Package package )
	{
		if ( !IsSingleplayer( package ) && (package.Tags.Contains( "multiplayer" ) || package.Info.MaxPlayers > 1) )
			return true;

		if ( package.Info.UsesCreateGameModal )
			return true;

		if ( package.Info.HasGameSettings )
			return true;

		return false;
	}


	/// <summary>
	/// A package by ident, if it's been fetched - <see cref="FetchPackages"/> fetches them
	/// </summary>
	public static Package CachedPackage( string ident ) => !string.IsNullOrEmpty( ident ) && Package.TryGetCached( ident, out var package ) ? package : null;

	/// <summary>
	/// A package's title, or its ident if it hasn't been fetched
	/// </summary>
	public static string CachedTitle( string ident ) => CachedPackage( ident )?.Title ?? ident;

	/// <summary>
	/// Fetches these packages for <see cref="CachedPackage"/> - published ones only: not a local project, nor a map
	/// that's a game's own file rather than on the Workshop
	/// </summary>
	public static async Task FetchPackages( IEnumerable<string> idents )
	{
		var fetches = idents
			.Where( x => x is not null && x.Contains( '.' ) && !x.Contains( "local." ) && !x.EndsWith( ".vmap" ) )
			.Distinct()
			.Select( x => Package.FetchAsync( x, true ) )
			.ToArray();

		try
		{
			await Task.WhenAll( fetches );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't fetch packages: {e.Message}" );
		}
	}

	/// <summary>
	/// A style setting an image as the background - empty without one
	/// </summary>
	public static string BackgroundImage( string url ) => url is { Length: > 0 } ? $"background-image: url( {url} );" : "";

	static (string Key, Dictionary<string, List<Friend>> ByGame, Dictionary<ulong, List<Friend>> ByLobby) friendsPlaying = ("", new(), new());
	static RealTimeSince friendsPlayingChecked = 10;

	/// <summary>
	/// Your friends playing a game - not in the editor - by game and by server, from their presence, and a key that
	/// changes when any of them move. Looked at again at most once a second.
	/// </summary>
	public static (string Key, Dictionary<string, List<Friend>> ByGame, Dictionary<ulong, List<Friend>> ByLobby) FriendsPlaying
	{
		get
		{
			if ( friendsPlayingChecked < 1 ) return friendsPlaying;
			friendsPlayingChecked = 0;

			var playing = MenuUtility.Friends
				.Where( x => !x.IsMe && x.IsOnline && x.IsPlayingThisGame && x.GetRichPresence( "in_editor" ) != "1" )
				.Select( x => (Friend: x, Game: x.GetRichPresence( "gamename" ), Lobby: x.GetRichPresence( "steam_player_group" )) )
				.Where( x => !string.IsNullOrEmpty( x.Game ) )
				.OrderBy( x => x.Friend.Id )
				.ToList();

			var key = string.Join( ",", playing.Select( x => $"{x.Friend.Id}:{x.Game}:{x.Lobby}" ) );
			if ( key == friendsPlaying.Key ) return friendsPlaying;

			friendsPlaying = (key,
				playing.GroupBy( x => x.Game ).ToDictionary( x => x.Key, x => x.Select( y => y.Friend ).ToList() ),
				playing.Where( x => ulong.TryParse( x.Lobby, out var id ) && id != 0 )
					.GroupBy( x => ulong.Parse( x.Lobby ) )
					.ToDictionary( x => x.Key, x => x.Select( y => y.Friend ).ToList() ));
			return friendsPlaying;
		}
	}

	/// <summary>
	/// The servers of a game you've joined from the menu, the latest first
	/// </summary>
	public static List<ulong> ServerHistory( string game ) => Game.Cookies.Get<List<ulong>>( $"platform.history.{game}", null ) ?? new();

	/// <summary>
	/// Into a server - remembered in its game's history first
	/// </summary>
	public static void JoinServer( Sandbox.Network.LobbyInformation server )
	{
		if ( !MenuProject.Platform.PlatformMenu.CanChangeGame ) return;

		if ( !string.IsNullOrEmpty( server.Game ) )
		{
			var history = ServerHistory( server.Game );
			history.Remove( server.LobbyId );
			history.Insert( 0, server.LobbyId );
			Game.Cookies.Set( $"platform.history.{server.Game}", history.Take( 50 ).ToList() );
		}

		MenuUtility.Connect( server.LobbyId );
	}

	public static void ConfirmQuit() => ModalSystem.Instance?.Open( new MenuProject.Modals.QuestionModal
	{
		Color = Color.Parse( "#ff5d6a" ) ?? Color.Red,
		Title = "#menu.home.quit_to_desktop",
		Message = "#menu.home.are_you_sure_you_want_to_close_s_box",
		ConfirmText = "#menu.home.quit_to_desktop_sentence",
		ConfirmIcon = "power_settings_new",
		OnConfirm = () => MenuUtility.RunCommand( "quit" )
	} );

	/// <summary>
	/// Facepunch's Steam account - what it publishes is ours
	/// </summary>
	const long VerifiedOwner = 76561198611402165;

	/// <summary>
	/// One of ours - published by Facepunch's account, so marked verified wherever its maker's shown
	/// </summary>
	public static bool IsVerified( WorkshopItemDetails item ) => item?.OwnerId == VerifiedOwner;

	/// <summary>
	/// A drop's waiting in <paramref name="state"/> - one to open, or this week's earned and not over yet. The rail's
	/// and the dock's pip.
	/// </summary>
	public static bool RewardReady( RewardState state )
	{
		var window = state?.Windows.FirstOrDefault();
		return state?.Pending is not null || (window?.IsEligible == true && (window.EndsAt == default || window.EndsAt > DateTimeOffset.UtcNow));
	}

	/// <summary>
	/// How a ping's holding up, for its colour (the platform-ping mixin) - good, okay or bad, or unknown when there isn't
	/// one (under 0). The server list's and the players'.
	/// </summary>
	public static string PingClass( float ms ) => ms < 0 ? "unknown" : ms < 50 ? "good" : ms < 100 ? "okay" : "bad";

	/// <summary>
	/// Asks before leaving the game - it's the game's unsaved progress - then <paramref name="leave"/>s
	/// </summary>
	public static void ConfirmLeave( Action leave ) => ModalSystem.Instance?.Open( new MenuProject.Modals.QuestionModal
	{
		Color = Color.Parse( "#ff5d6a" ) ?? Color.Red,
		Title = "#menu.pause.leave_game_label",
		Message = "#menu.pause.any_unsaved_progress_will_be_lost",
		ConfirmText = "#menu.pause.leave",
		ConfirmIcon = "logout",
		OnConfirm = leave
	} );

	/// <summary>
	/// How much of its votes are up, as a whole percent - null if nobody's voted
	/// </summary>
	public static int? Rating( int up, int down ) => up + down > 0 ? up * 100 / (up + down) : null;

	/// <summary>
	/// Whole days since <paramref name="time"/>, formatted compactly - e.g. "1d", "7d", "764d".
	/// </summary>
	public static string DaysAgo( System.DateTimeOffset time )
	{
		var days = (int)System.Math.Floor( (System.DateTimeOffset.UtcNow - time).TotalDays );
		if ( days < 0 ) days = 0;
		return $"{days}d";
	}

	/// <summary>
	/// How long something's been played - in minutes under an hour, hours to a decimal place past that,
	/// Steam's way: "Under a minute", "23 minutes", "4.6 hours".
	/// </summary>
	public static string PlayTime( System.TimeSpan time )
	{
		if ( time.TotalHours >= 1 )
		{
			var hours = System.Math.Floor( time.TotalHours * 10 ) / 10;
			return $"{hours:0.#} hour{(hours == 1 ? "" : "s")}";
		}

		var minutes = (int)time.TotalMinutes;
		if ( minutes < 1 ) return "Under a minute";
		return $"{minutes} minute{(minutes == 1 ? "" : "s")}";
	}

	public static string FormatSize( long bytes )
	{
		var formatted = bytes.FormatBytes();
		var suffixLength = bytes >= 1024 ? 2 : 1;
		return $"{formatted[..^suffixLength]} {formatted[^suffixLength..].ToUpperInvariant()}";
	}

	/// <summary>
	/// "3 days ago", "2 weeks ago", "5 months ago", "2 years ago" - the biggest unit that fits, so
	/// half a year reads as months, not 26 weeks.
	/// </summary>
	public static string TimeAgo( System.DateTimeOffset time )
	{
		var span = System.DateTimeOffset.UtcNow - time;
		if ( span.TotalSeconds < 0 ) span = System.TimeSpan.Zero;

		static string Plural( int n, string unit ) => $"{n} {unit}{(n == 1 ? "" : "s")} ago";

		if ( span.TotalMinutes < 1 ) return Language.GetPhrase( "menu.common.just_now" );
		if ( span.TotalHours < 1 ) return Plural( (int)span.TotalMinutes, "minute" );
		if ( span.TotalDays < 1 ) return Plural( (int)span.TotalHours, "hour" );
		if ( span.TotalDays < 7 ) return Plural( (int)span.TotalDays, "day" );
		if ( span.TotalDays < 30 ) return Plural( (int)(span.TotalDays / 7), "week" );
		if ( span.TotalDays < 365 ) return Plural( System.Math.Max( (int)(span.TotalDays / 30.44), 1 ), "month" );
		return Plural( (int)(span.TotalDays / 365.25), "year" );
	}

	/// <summary>
	/// <see cref="TimeAgo(System.DateTimeOffset)"/> for a UTC <see cref="System.DateTime"/>.
	/// </summary>
	public static string TimeAgo( System.DateTime utc ) => TimeAgo( new System.DateTimeOffset( System.DateTime.SpecifyKind( utc, System.DateTimeKind.Utc ) ) );

	/// <summary>
	/// Opens a game's menu page, or launches it directly in VR.
	/// </summary>
	public static void LaunchGame( string gameIdent )
	{
		// alex: in VR we don't show modals properly (this needs some thought as to how we're going to do it)
		// so for the purposes of being able to play tech jam games, we'll just launch games directly
		if ( Application.IsVR )
		{
			MenuUtility.OpenGame( gameIdent, true );
			return;
		}

		Game.Overlay.ShowGameModal( gameIdent );
	}
}
