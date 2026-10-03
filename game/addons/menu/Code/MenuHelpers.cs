using Sandbox;
using Sandbox.DataModel;
using Sandbox.Diagnostics;
using Sandbox.Modals;
using MenuProject.MenuUI.Front;
using MenuProject.Modals.GameModalComponents;
using MenuPanel = MenuProject.UI.MenuPanel;

public static class MenuHelpers
{
	/// <summary>
	/// <c>menu_mock_new_account 1</c> - treat this account as under a week old, to see the menu without
	/// anything for sale.
	/// </summary>
	[MenuConVar( "menu_mock_new_account", Help = "Treat this account as under a week old - no item store, no unowned store items: 0 or 1" )]
	public static bool MockNewAccount { get; set; }

	/// <summary>
	/// How old an account has to be before anything's offered for sale.
	/// </summary>
	public static readonly TimeSpan MicrotransactionsMinimumAccountAge = TimeSpan.FromDays( 7 );

	/// <summary>
	/// The item store and everything else that sells - not until the account's a week old. The s&amp;box
	/// account, from when the backend first saw it, not the Steam account. Not known yet (not logged in)
	/// counts as new.
	/// </summary>
	public static bool ShowMicrotransactions
	{
		get
		{
			if ( MockNewAccount ) return false;

			var firstSeen = Sandbox.MenuEngine.Account.FirstSeen;
			return firstSeen != default && DateTimeOffset.UtcNow - firstSeen >= MicrotransactionsMinimumAccountAge;
		}
	}

	/// <summary>
	/// Do we have authority to start or join games.
	/// If we're in a party, only the party owner can start or join games.
	/// </summary>
	public static bool HasAuthority => PartyRoom.Current?.Owner.IsMe ?? true;

	/// <summary>
	/// Go to one of the menu's pages, in whichever menu's showing - the pause menu's, mid-game with it
	/// up, or the main menu's. For things that live over the pages rather than in them (popups, modals),
	/// which have no navigator of their own to find.
	/// </summary>
	public static void Navigate( string url )
	{
		if ( MenuProject.Modals.PauseMenuModal.PauseModal.Open?.Navigate( url ) ?? false )
			return;

		MenuProject.MainMenu.Instance?.Navigator?.Navigate( url );
	}

	/// <summary>
	/// True when a discovery query lists a jam's entries, e.g. "jam:three type:game".
	/// </summary>
	public static bool IsJamQuery( string query )
	{
		if ( string.IsNullOrEmpty( query ) ) return false;

		return query.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).Any( x => x.StartsWith( "jam:", StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>
	/// General-purpose method to play a game package. Handles quickplay, dedicated servers,
	/// create-game modal, VR-only checks, default map fetching, and direct launch.
	/// <para>
	/// Reports the play to <see cref="Discovery"/> when it's given the button that was pressed
	/// (<paramref name="via"/>, and the tile it was on, <paramref name="source"/>) - as it goes, or for a
	/// game with something to set up first, once that's started (see GameModal.StartSetup). Not when
	/// the button's pressed and the setup's only opened: backed out of, it was never played.
	/// </para>
	/// </summary>
	public static async void PlayGame( Package package, Package mapPackage = null, string via = null, Panel source = null, bool fitChecked = false )
	{
		Assert.True( HasAuthority, "You do not have authority to start a game, only the party owner can do that." );

		// Your party isn't what it's made for - say so, and only go on if you want to
		if ( !fitChecked && !ConfirmPartyFit( package, () => PlayGame( package, mapPackage, via, source, fitChecked: true ) ) )
			return;

		// VR-only game but not in VR
		if ( package.Info.IsVrOnly && !Application.IsVR )
			return;

		var party = PartyRoom.Current;
		if ( !package.Info.IsDedicatedServerOnly && party?.IsGameSelected( package ) == true && party.SelectedGameSettings is not null )
		{
			ModalSystem.Instance?.OpenGameSetup( package, via, source );
			return;
		}

		// QuickPlay: try to join an existing lobby first
		if ( package.Info.IsQuickPlay )
		{
			// A lobby search is a go at playing it, whether it finds one or makes its own after
			ReportPlay( package, via, source );
			via = null;

			await PrepareForLoad( "Finding Game..", "Please wait while we find a game for you to join." );

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
			ModalSystem.Instance?.OpenGameSetup( package, via, source, initialMap: mapPackage?.FullIdent );
			return;
		}

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
			throw new InvalidOperationException( "Your party has changed. Check the game setup and try again." );
		}

		if ( party is not null && (PartyRoom.Current != party || !party.Owner.IsMe || !party.IsGameSelected( package )
			|| party.SelectedGameSettings is not { } currentSettings || !GameSetup.SettingsEqual( settings, currentSettings )
			|| serverSlots < party.MemberCount) )
		{
			LoadingScreen.IsVisible = false;
			throw new InvalidOperationException( "The party or game setup changed. Check the game settings and try again." );
		}

		if ( !HasAuthority )
		{
			LoadingScreen.IsVisible = false;
			return;
		}

		LaunchArguments.MaxPlayers = serverSlots;

		if ( !string.IsNullOrEmpty( settings.ServerName ) ) LaunchArguments.ServerName = settings.ServerName;
		LaunchArguments.Privacy = settings.Privacy;
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
	/// with Play anyway (which runs <paramref name="playAnyway"/>) and Return, and gives false.
	/// </summary>
	public static bool ConfirmPartyFit( Package package, Action playAnyway )
	{
		var size = MenuProject.PlayerModes.PartySize;
		var modes = MenuProject.PlayerModes.For( package );
		var (fit, why) = modes.FitFor( size );
		if ( fit != MenuProject.PlayerModes.FitKind.Poor ) return true;

		// Short of what it's made for, or past it - singleplayer, a full lobby, more than it's for
		var tooSmall = modes.Multiplayer && modes.RecommendedMin > size && (modes.MaxPlayers <= 0 || size <= modes.MaxPlayers);

		ModalSystem.Instance?.Open( new MenuProject.Modals.QuestionModal
		{
			Color = Color.Parse( "#f5a623" ) ?? Color.Orange,
			Title = tooSmall ? "Your party is too small" : "Your party is too big",
			Message = why,
			ConfirmText = "Play anyway",
			ConfirmIcon = "play_arrow",
			CancelText = "Return",
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
	public static async Task PrepareForLoad( string title = "Loading..", string subtitle = "" )
	{
		MenuUtility.CloseAllModals();

		LoadingScreen.IsVisible = true;
		LoadingScreen.Title = title;
		LoadingScreen.Subtitle = subtitle;

		await Task.Delay( LoadWarmUpMilliseconds );
	}

	static bool NeedsSetup( Package package )
	{
		if ( package.Tags.Contains( "multiplayer" ) || package.Info.MaxPlayers > 1 )
			return true;

		if ( package.Info.UsesCreateGameModal )
			return true;

		if ( package.Info.HasGameSettings )
			return true;

		return false;
	}

	public static string SANDBOX_IDENT => "facepunch.sandbox";

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

	/// <summary>
	/// "3 days ago", "2 weeks ago", "5 months ago", "2 years ago" - the biggest unit that fits, so
	/// half a year reads as months, not 26 weeks.
	/// </summary>
	public static string TimeAgo( System.DateTimeOffset time )
	{
		var span = System.DateTimeOffset.UtcNow - time;
		if ( span.TotalSeconds < 0 ) span = System.TimeSpan.Zero;

		static string Plural( int n, string unit ) => $"{n} {unit}{(n == 1 ? "" : "s")} ago";

		if ( span.TotalMinutes < 1 ) return "just now";
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

	public static MenuPanel OpenFriendMenu( Panel source, Friend friend )
	{
		var menu = MenuPanel.Open( source );

		menu.AddOption( "contact_page", "View Profile", () => Game.Overlay.ShowPlayer( (long)friend.Id ) );

		if ( !friend.IsFriend && !friend.IsMe )
		{
			menu.AddOption( "person_add", "Send Friend Request", friend.OpenAddFriendOverlay );
		}

		var me = new Friend( Game.SteamId );
		var connectString = friend.GetRichPresence( "connect" );
		var isInGame = !string.IsNullOrEmpty( connectString );
		var inSameGame = isInGame && connectString == me.GetRichPresence( "connect" );
		var canJoinGame = !string.IsNullOrEmpty( connectString );

		if ( canJoinGame && !inSameGame )
		{
			menu.AddOption( "sports_esports", "Join Game", () => MenuUtility.JoinFriendGame( friend ) );
		}

		return menu;
	}

	public static void OpenPackageMenu( Panel source, Package package, bool multiplayerOverride = false )
	{
		if ( package.TypeName == "game" )
			OpenGameMenu( source, package, multiplayerOverride );
		else if ( package.TypeName == "map" )
			OpenMapMenu( source, package );
		else
			Log.Info( $"Unknown package type: {package.TypeName}" );
	}

	static void OpenGameMenu( Panel source, Package package, bool multiplayerOverride = false )
	{
		var menu = MenuPanel.Open( source );

		menu.AddOption( "play_arrow", "Open Game", () =>
		{
			Discovery.Clicked( source, package );
			LaunchGame( package.FullIdent );
		} );

		if ( package.Tags.Contains( "maplaunch" ) )
		{
			menu.AddOption( "folder", "Open With Map..", () =>
			{
				Game.Overlay.ShowPackageSelector( $"type:map sort:trending target:{package.FullIdent}", ( p ) => MenuUtility.OpenGameWithMap( package.FullIdent, p.FullIdent ) );
			} );
		}

		if ( multiplayerOverride || package.Tags.Contains( "multiplayer" ) || package.Info.MaxPlayers > 1 )
		{
			menu.AddSpacer();
			menu.AddOption( "list", "View servers", () =>
			{
				Game.Overlay.ShowServerList( new Sandbox.Modals.ServerListConfig( package.FullIdent ) );
			} );
		}

		menu.AddSpacer();
		var liked = package.Interaction.Rating == 0;
		var disliked = package.Interaction.Rating == 1;
		var favourite = package.Interaction.Favourite;
		menu.AddOption( "thumb_up", liked ? "Liked" : "Like", () => _ = package.SetVoteAsync( true ) );
		menu.AddOption( "thumb_down", disliked ? "Disliked" : "Dislike", () => _ = package.SetVoteAsync( false ) );
		menu.AddOption( favourite ? "favorite" : "favorite_border", favourite ? "Remove from Favourites" : "Add to Favourites", () => _ = package.SetFavouriteAsync( !favourite ) );

		menu.AddSpacer();
		menu.AddOption( "corporate_fare", $"View Creator", () => Game.Overlay.ShowOrganizationModal( package.Org ) );
		menu.AddOption( "rate_review", "Review Game", () => Game.Overlay.ShowReviewModal( package ) );
		menu.AddOption( "flag", "Report Game", () => Game.Overlay.ShowReportModal( package.FullIdent ) );
		menu.AddOption( "block", "Hide Game", () => _ = HidePackage( source, package ) );
	}

	/// <summary>
	/// Hide a game from this player's discovery and search. Toasts the result and drops the
	/// tile from the front-page shelf it came from.
	/// </summary>
	public static async Task<bool> HidePackage( Panel source, Package package )
	{
		// Hover cards float in the root; their shelf is behind the hovered card
		if ( source is MenuProject.UI.PackageHoverCard hoverCard )
		{
			source = hoverCard.Source;
			hoverCard.Close();
		}

		var hidden = await package.SetHiddenAsync( true );

		if ( hidden )
		{
			var putBack = source?.AncestorsAndSelf.OfType<FrontPageGames>().FirstOrDefault()?.RemovePackage( package );

			// Hid the wrong one - a way straight back, on the toast saying so
			MenuOverlay.Instance?.BottomRight?.Queue( new MenuProject.Toast
			{
				Title = $"{package.Title} hidden",
				Icon = "visibility_off",
				ActionText = "Undo",
				OnAction = () => _ = UnhidePackage( package, putBack )
			}, duration: UndoSeconds );
		}
		else
		{
			Toast( $"Couldn't hide {package.Title} right now", "visibility_off" );
		}

		return hidden;
	}

	/// <summary>
	/// How long the hidden toast stays up - longer than most, it's got an Undo on it.
	/// </summary>
	const float UndoSeconds = 7f;

	/// <summary>
	/// Show a hidden game again - and put it back on the shelf it came off, if there's a way to.
	/// </summary>
	public static async Task<bool> UnhidePackage( Package package, Action putBack = null )
	{
		var shown = await package.SetHiddenAsync( false );

		if ( shown )
		{
			putBack?.Invoke();
			Toast( $"{package.Title} is back", "visibility" );
		}
		else
		{
			Toast( $"Couldn't unhide {package.Title} right now", "visibility" );
		}

		return shown;
	}

	static void Toast( string title, string icon ) => MenuOverlay.Instance?.BottomRight?.Queue( new MenuProject.Toast() { Title = title, Icon = icon } );

	static void OpenMapMenu( Panel source, Package package )
	{
		var menu = MenuPanel.Open( source );

		async void OnPackageSelected( Package package )
		{
			Assert.True( HasAuthority, "You do not have authority to start a game, only the party owner can do that." );
			LaunchArguments.Map = null;

			var filters = new Dictionary<string, string>
			{
				{ "game", SANDBOX_IDENT },
				{ "map", package.FullIdent },
			};

			var lobbies = await Networking.QueryLobbies( filters );

			foreach ( var lobby in lobbies ) // TODO - order by most attractive
			{
				if ( lobby.IsFull ) continue;

				if ( await MenuUtility.TryJoinLobby( lobby.LobbyId ) )
					return;
			}

			CreateGameWithMap( SANDBOX_IDENT, package );
		}

		void ViewGameList( Package package )
		{
			Game.Overlay.ShowServerList( new Sandbox.Modals.ServerListConfig( null, package.FullIdent ) );
		}

		if ( HasAuthority )
		{
			menu.AddOption( "play_arrow", "Join existing session", () => OnPackageSelected( package ) );
			menu.AddOption( "playlist_add", "Create own game", () => CreateGameWithMap( SANDBOX_IDENT, package ) );

			menu.AddSpacer();
		}

		menu.AddOption( "list", "View servers", () => ViewGameList( package ) );

		menu.AddSpacer();
		menu.AddOption( "info", $"View Map Details", () => Game.Overlay.ShowPackageModal( package.FullIdent ) );
		menu.AddOption( "corporate_fare", $"View Creator", () => Game.Overlay.ShowOrganizationModal( package.Org ) );
		menu.AddOption( "star", "Rate Map", () => Game.Overlay.ShowReviewModal( package ) );
	}

	public static void CreateGameWithMap( string gameIdent, Package mapPackage )
	{
		Assert.True( HasAuthority, "You do not have authority to start a game, only the party owner can do that." );

		LaunchArguments.Map = mapPackage.FullIdent;
		MenuUtility.OpenGame( gameIdent, false );
	}

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
