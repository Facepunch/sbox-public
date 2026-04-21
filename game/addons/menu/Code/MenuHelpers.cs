using Sandbox;
using Sandbox.DataModel;
using Sandbox.Diagnostics;
using Sandbox.Modals;
using Sandbox.Network;

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
	public static async void PlayGame( Package package )
	{
		Assert.True( HasAuthority, "You do not have authority to start a game, only the party owner can do that." );

		// VR-only game but not in VR
		var isVrOnly = package.GetMeta<ControlModeSettings>( "ControlModes" )?.IsVROnly ?? false;
		if ( isVrOnly && !Application.IsVR )
			return;

		// QuickPlay: try to join an existing lobby first
		var launchMode = package.GetMeta( "LaunchMode", "default" ).ToLower();
		if ( launchMode == "quickplay" )
		{
			LoadingScreen.IsVisible = true;
			LoadingScreen.Title = "Finding Game..";
			LoadingScreen.Subtitle = "Please wait while we find a game for you to join.";

			if ( await MenuUtility.TryJoinLobby( package.FullIdent ) )
				return;
		}

		MenuUtility.CloseAllModals();

		// Dedicated server only: show server list
		if ( launchMode == "dedicatedserveronly" )
		{
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

				if ( !string.IsNullOrEmpty( x.MapIdent ) )
					MenuUtility.OpenGameWithMap( package.FullIdent, x.MapIdent, x.GameSettings );
				else
					MenuUtility.OpenGame( package.FullIdent, true, x.GameSettings );
			} ) );
			return;
		}

		// Direct launch
		LoadingScreen.IsVisible = true;
		LoadingScreen.Title = "Loading..";
		LoadingScreen.Subtitle = "";

		// Fetch the default map if one is configured
		var defaultMap = package.GetValue( "DefaultMap", "" );
		if ( !string.IsNullOrWhiteSpace( defaultMap ) )
		{
			var mapPackage = await Package.FetchAsync( defaultMap, false );
			if ( mapPackage is not null )
			{
				Log.Info( $"Default map configured ({defaultMap}), launching game with map." );
				MenuUtility.OpenGameWithMap( package.FullIdent, mapPackage.FullIdent );
				return;
			}
		}

		Log.Info( "No default map configured, launching game directly: " + package.FullIdent );

		MenuUtility.OpenGame( package.FullIdent, true );
	}

	static bool ShouldUseCreateGameModal( Package package )
	{
		if ( package.GetValue( "UseCreateGameModal", false ) )
			return true;

		var settings = package.GetMeta<List<GameSetting>>( "GameSettings", null );
		if ( settings is not null && settings.Count > 0 )
			return true;

		return false;
	}

	public static string SANDBOX_IDENT => "facepunch.sandbox";

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
}
