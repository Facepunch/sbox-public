using Sandbox;
using Sandbox.DataModel;
using Sandbox.Diagnostics;
using Sandbox.Modals;

public static class MenuHelpers
{
	/// <summary>
	/// Do we have authority to start or join games.
	/// If we're in a party, only the party owner can start or join games.
	/// </summary>
	public static bool HasAuthority => PartyRoom.Current?.Owner.IsMe ?? true;

	/// <summary>
	/// General-purpose method to play a game package. Handles quickplay, dedicated servers,
	/// create-game modal, VR-only checks, default map fetching, and direct launch.
	/// </summary>
	public static async void PlayGame( Package package, Package mapPackage = null )
	{
		Assert.True( HasAuthority, "You do not have authority to start a game, only the party owner can do that." );

		// VR-only game but not in VR
		if ( package.Info.IsVrOnly && !Application.IsVR )
			return;

		// QuickPlay: try to join an existing lobby first
		if ( package.Info.IsQuickPlay )
		{
			LoadingScreen.IsVisible = true;
			LoadingScreen.Title = Localize( "menuhelpers", "loadingscreen.title" );
			LoadingScreen.Subtitle = Localize( "menuhelpers", "loadingscreen.subtitle" );

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

		// Show create game modal if the package requires it
		if ( ShouldUseCreateGameModal( package ) )
		{
			Game.Overlay.CreateGame( new CreateGameOptions( package, x =>
			{
				if ( x.MaxPlayers > 0 ) LaunchArguments.MaxPlayers = x.MaxPlayers;

				if ( !string.IsNullOrEmpty( x.ServerName ) )
					LaunchArguments.ServerName = x.ServerName;

				LaunchArguments.Privacy = x.Privacy;

				if ( !string.IsNullOrEmpty( x.Map ) )
					MenuUtility.OpenGameWithMap( package.FullIdent, x.Map, x.GameSettings );
				else
					MenuUtility.OpenGame( package.FullIdent, true, x.GameSettings );
			} ) );
			return;
		}

		// Direct launch
		MenuUtility.CloseAllModals();
		LoadingScreen.IsVisible = true;
		LoadingScreen.Title = Localize( "menuhelpers", "loadingscreen.loading" );
		LoadingScreen.Subtitle = "";

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

		if ( mapPackage is not null )
		{
			MenuUtility.OpenGameWithMap( package.FullIdent, mapPackage.FullIdent );
		}
		else
		{
			MenuUtility.OpenGame( package.FullIdent, true );
		}
	}

	static bool ShouldUseCreateGameModal( Package package )
	{
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
		return Game.Language.GetPhrase( "menuhelpers.time.days", new() { { "num", days } } );
	}

	public static MenuPanel OpenFriendMenu( Panel source, Friend friend )
	{
		var menu = MenuPanel.Open( source );

		menu.AddOption( "contact_page", Localize( "menuhelpers", "friendmenu.view_profile" ), () => Game.Overlay.ShowPlayer( (long)friend.Id ) );

		if ( !friend.IsFriend && !friend.IsMe )
		{
			menu.AddOption( "person_add", Localize( "menuhelpers", "friendmenu.send_friend_request" ), friend.OpenAddFriendOverlay );
		}

		var me = new Friend( Game.SteamId );
		var connectString = friend.GetRichPresence( "connect" );
		var isInGame = !string.IsNullOrEmpty( connectString );
		var inSameGame = isInGame && connectString == me.GetRichPresence( "connect" );
		var canJoinGame = !string.IsNullOrEmpty( connectString );

		if ( canJoinGame && !inSameGame )
		{
			menu.AddOption( "sports_esports", Localize( "menuhelpers", "friendmenu.join_game" ), () => MenuUtility.JoinFriendGame( friend ) );
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

		menu.AddOption( "play_arrow", Localize( "menuhelpers", "gamemenu.open_game" ), () => LaunchGame( package.FullIdent ) );

		if ( package.Tags.Contains( "maplaunch" ) )
		{
			menu.AddOption( "folder", Localize( "menuhelpers", "gamemenu.open_with_map" ), () =>
			{
				Game.Overlay.ShowPackageSelector( $"type:map sort:trending target:{package.FullIdent}", ( p ) => MenuUtility.OpenGameWithMap( package.FullIdent, p.FullIdent ) );
			} );
		}

		if ( multiplayerOverride || package.Tags.Contains( "multiplayer" ) || package.Info.MaxPlayers > 1 )
		{
			menu.AddSpacer();
			menu.AddOption( "list", Localize( "menuhelpers", "gamemenu.view_servers" ), () =>
			{
				Game.Overlay.ShowServerList( new Sandbox.Modals.ServerListConfig( package.FullIdent ) );
			} );
		}

		menu.AddSpacer();
		menu.AddOption( "corporate_fare", Localize( "menuhelpers", "gamemenu.view_creator" ), () => Game.Overlay.ShowOrganizationModal( package.Org ) );
		menu.AddOption( "star", Localize( "menuhelpers", "gamemenu.review_game" ), () => Game.Overlay.ShowReviewModal( package ) );
		menu.AddOption( "flag", Localize( "menuhelpers", "gamemenu.report_game" ), () => Game.Overlay.ShowReportModal( package.FullIdent ) );
	}

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

				if ( await Networking.TryConnectSteamId( lobby.LobbyId ) )
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
			menu.AddOption( "play_arrow", Localize( "menuhelpers", "mapmenu.join_existing_session" ), () => OnPackageSelected( package ) );
			menu.AddOption( "playlist_add", Localize( "menuhelpers", "mapmenu.create_own_game" ), () => CreateGameWithMap( SANDBOX_IDENT, package ) );

			menu.AddSpacer();
		}

		menu.AddOption( "list", Localize( "menuhelpers", "mapmenu.view_servers" ), () => ViewGameList( package ) );

		menu.AddSpacer();
		menu.AddOption( "info", Localize( "menuhelpers", "mapmenu.view_map_details" ), () => Game.Overlay.ShowPackageModal( package.FullIdent ) );
		menu.AddOption( "corporate_fare", Localize( "menuhelpers", "mapmenu.view_creator" ), () => Game.Overlay.ShowOrganizationModal( package.Org ) );
		menu.AddOption( "star", Localize( "menuhelpers", "mapmenu.rate_map" ), () => Game.Overlay.ShowReviewModal( package ) );
	}

	public static async void LoadMap( Package package )
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

			if ( await Networking.TryConnectSteamId( lobby.LobbyId ) )
				return;
		}

		CreateGameWithMap( SANDBOX_IDENT, package );
	}

	public static void CreateGameWithMap( string gameIdent, Package mapPackage )
	{
		Assert.True( HasAuthority, "You do not have authority to start a game, only the party owner can do that." );

		LaunchArguments.Map = mapPackage.FullIdent;
		MenuUtility.OpenGame( gameIdent, false );
	}

	public static void LaunchGame( string gameIdent, bool allowLaunchOverride = true )
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

	/// <summary>
	/// Look up a localized phrase by fullKey.
	/// When <paramref name="returnOriginKey"/> is true, returns <paramref name="fullKey"/>
	/// untouched instead of the full key on lookup failure.
	/// </summary>
	public static string Localize( string fullKey, bool returnOriginKey = false )
	{
		var normalized = fullKey.Trim().ToLower().Replace( " ", "" ).Replace( "#", "" ).Replace( "-", "" );
		var result = Game.Language.GetPhrase( normalized );
		return returnOriginKey && result == normalized ? fullKey : result;
	}

	/// <summary>
	/// Look up a localized phrase with template data.
	/// When <paramref name="returnOriginKey"/> is true, returns <paramref name="fullKey"/>
	/// untouched instead of the full key on lookup failure.
	/// </summary>
	public static string Localize( string fullKey, Dictionary<string, object> data, bool returnOriginKey = false )
	{
		var normalized = fullKey.Trim().ToLower().Replace( " ", "" ).Replace( "#", "" ).Replace( "-", "" );
		var result = Game.Language.GetPhrase( normalized, data );
		return returnOriginKey && result == normalized ? fullKey : result;
	}

	/// <summary>
	/// Look up a localized phrase with a prefix appended to the key, plus optional template data.
	/// When <paramref name="returnOriginKey"/> is true, returns <paramref name="key"/>
	/// untouched instead of the full prefixed key on lookup failure.
	/// </summary>
	public static string Localize( string prefix, string key, Dictionary<string, object> data = null, bool returnOriginKey = false )
	{
		if ( !prefix.EndsWith( "." ) )
			prefix += ".";

		var normalized = (prefix + key).Trim().ToLower().Replace( " ", "" ).Replace( "#", "" ).Replace( "-", "" );
		var result = Game.Language.GetPhrase( normalized, data );
		return returnOriginKey && result == normalized ? key : result;
	}
}
