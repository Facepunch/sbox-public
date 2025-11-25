using Sandbox;

public static class MenuHelpers
{
	public static string SANDBOX_IDENT => "facepunch.sandbox";

	public static MenuPanel OpenFriendMenu( Panel source, Friend friend )
	{
		var menu = MenuPanel.Open( source );

		menu.AddOption( "contact_page", "View Profile", () => Game.Overlay.ShowPlayer( (long)friend.Id ) );

		if ( !friend.IsFriend && !friend.IsMe )
		{
			menu.AddOption( "person_add", "Send Friend Request", friend.OpenAddFriendOverlay );
		}

		Friend Me = new Friend( Game.SteamId );
		string ConnectString = friend.GetRichPresence( "connect" );
		bool IsInGame = !string.IsNullOrEmpty( ConnectString );
		bool InSameGame = IsInGame && ConnectString == Me.GetRichPresence( "connect" );
		bool CanJoinGame = !string.IsNullOrEmpty( ConnectString );

		if ( CanJoinGame && !InSameGame )
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

		menu.AddOption( "play_arrow", "Open Game", () => LaunchGame( package.FullIdent ) );

		if ( package.Tags.Contains( "maplaunch" ) )
		{
			menu.AddOption( "folder", "Open With Map..", () =>
			{
				Game.Overlay.ShowPackageSelector( $"type:map sort:trending target:{package.FullIdent}", ( p ) => MenuUtility.OpenGameWithMap( package.FullIdent, p.FullIdent ) );
			} );
		}

		var maxPlayers = package.GetMeta<int>( "MaxPlayers", 1 );

		if ( multiplayerOverride || package.Tags.Contains( "multiplayer" ) || maxPlayers > 1 )
		{
			menu.AddSpacer();
			menu.AddOption( "list", "View servers", () =>
			{
				Game.Overlay.ShowServerList( new Sandbox.Modals.ServerListConfig( package.FullIdent ) );
			} );
		}

		menu.AddSpacer();
		menu.AddOption( "corporate_fare", $"View Creator", () => Game.Overlay.ShowOrganizationModal( package.Org ) );
		menu.AddOption( "star", "Rate Game", () => Game.Overlay.ShowReviewModal( package ) );
	}

	static void OpenMapMenu( Panel source, Package package )
	{
		var menu = MenuPanel.Open( source );

		async void OnPackageSelected( Package package )
		{
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

		menu.AddOption( "play_arrow", "Join existing session", () => OnPackageSelected( package ) );
		menu.AddOption( "playlist_add", "Create own game", () => CreateGameWithMap( SANDBOX_IDENT, package ) );

		menu.AddSpacer();

		menu.AddOption( "list", "View servers", () => ViewGameList( package ) );

		//   menu.AddOption( "folder", "Launch With Map..", OnLaunchWithMap );

		menu.AddSpacer();
		menu.AddOption( "info", $"View Map Details", () => Game.Overlay.ShowPackageModal( package.FullIdent ) );
		menu.AddOption( "corporate_fare", $"View Creator", () => Game.Overlay.ShowOrganizationModal( package.Org ) );
		menu.AddOption( "star", "Rate Map", () => Game.Overlay.ShowReviewModal( package ) );
	}

	public static async void LoadMap( Package package )
	{
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
}
