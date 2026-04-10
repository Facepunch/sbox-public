using Sandbox.Engine;
using Sandbox.Modals;

namespace Sandbox;

public static partial class Game
{
	/// <summary>
	/// Provides static methods for displaying various modal overlays in the game UI.
	/// <para>
	/// The <see cref="Overlay"/> class allows you to open modals for packages, maps, news, organizations, reviews, friends lists, server lists, settings, input bindings, and player profiles.
	/// It serves as a central point for invoking user interface overlays that interact with core game and community features.
	/// </para>
	/// </summary>
	/// <example>
	/// <code>
	/// // Show a modal for a specific game package
	/// Game.Overlay.ShowGameModal("facepunch.sandbox");
	///
	/// // Check if any overlay is currently open
	/// if (Game.Overlay.IsOpen)
	/// {
	///     // Pause game logic or input
	/// }
	/// </code>
	/// </example>
	public partial class Overlay
	{
		/// <summary>
		/// Opens a modal for the specified game package
		/// </summary>
		/// <param name="packageIdent"></param>
		public static void ShowGameModal( string packageIdent )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.Game( packageIdent );
		}

		/// <summary>
		/// Opens a modal for the specified map package
		/// </summary>
		/// <param name="packageIdent"></param>
		public static void ShowMapModal( string packageIdent )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.Map( packageIdent );
		}

		/// <summary>
		/// Opens a modal for the specified package
		/// </summary>
		/// <param name="ident"></param>
		public static void ShowPackageModal( string ident )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.Package( ident, "" );
		}

		/// <summary>
		/// Opens a modal for the specified package on the specified page
		/// </summary>
		/// <param name="ident"></param>
		/// <param name="page"></param>
		public static void ShowPackageModal( string ident, string page )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.Package( ident, page );
		}

		/// <summary>
		/// Opens a modal for the news item
		/// </summary>
		public static void ShowNewsModal( Sandbox.Services.News newsitem )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.News( newsitem );
		}

		/// <summary>
		/// Opens a modal for the specified organization. 
		/// This is most likely called from a Package - so get the organization from there.
		/// </summary>
		/// <param name="org"></param>
		public static void ShowOrganizationModal( Package.Organization org )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.Organization( org );
		}

		/// <summary>
		/// Opens a modal to review the specified package
		/// </summary>
		/// <param name="package"></param>
		public static void ShowReviewModal( Package package )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.Review( package );
		}

		/// <summary>
		/// Opens a modal for selecting a package
		/// </summary>
		public static void ShowPackageSelector( string query, Action<Package> onSelect, Action<string> onFilterChanged = null )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.PackageSelect( query, onSelect, onFilterChanged );
		}

		[Obsolete( "Use ShowFriendsList with FriendsListModalOptions instead." )]
		public static void ShowFriendsList() => ShowFriendsList( new() );

		/// <summary>
		/// Opens a modal that shows the user's friends list
		/// </summary>
		/// <param name="options"></param>
		public static void ShowFriendsList( in FriendsListModalOptions options )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.FriendsList( options );
		}

		/// <summary>
		/// Opens a modal that shows a list of active servers
		/// </summary>
		public static void ShowServerList( in ServerListConfig config )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.ServerList( config );
		}

		/// <summary>
		/// Opens a modal that lets you modify your settings
		/// Optionally, you can specify a page to open directly to: "keybinds", "video", "input", "audio", "game", "storage", "developer"
		/// </summary>
		public static void ShowSettingsModal( string page = "" )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.Settings( page );
		}

		/// <summary>
		/// Opens a modal that lets you view and rebind game input actions.
		/// </summary>
		public static void ShowBinds()
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.Settings( "keybinds" );
		}

		/// <summary>
		/// Opens a modal to create a game with a bunch of settings. We use this in the menu when you click "Create Game"
		/// and the game has options.
		/// </summary>
		public static void CreateGame( in CreateGameOptions options )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.CreateGame( options );
		}

		/// <summary>
		/// View a selected user's profile
		/// </summary>
		public static void ShowPlayer( SteamId steamid, string page = "" )
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.Player( steamid, page );
		}

		/// <summary>
		/// Open a modal that shows a list of players currently in the game
		/// </summary>
		public static void ShowPlayerList()
		{
			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.PlayerList();
		}

		/// <summary>
		/// Open a modal that prompts the user to publish content to the workshop
		/// </summary>
		public static void WorkshopPublish( in WorkshopPublishOptions options )
		{
			using var scope = GlobalContext.MenuScope();
			IModalSystem.Current?.WorkshopPublish( options );
		}

		/// <summary>
		/// Opens the pause menu overlay. This is the same menu that appears when pressing ESC.
		/// </summary>
		public static void ShowPauseMenu()
		{
			if ( IModalSystem.Current?.IsModalOpen == true )
				return;

			using var scope = GlobalContext.MenuScope();

			IModalSystem.Current?.PauseMenu();
		}

		/// <summary>
		/// Closes the top overlay if one exists
		/// </summary>
		public static void Close()
		{
			if ( IModalSystem.Current?.IsModalOpen != true )
				return;

			IModalSystem.Current?.PauseMenu();
		}

		/// <summary>
		/// Close all open overlays
		/// </summary>
		/// <param name="immediate">If <see langword="true"/>, will skip any outros</param>
		public static void CloseAll( bool immediate = false )
		{
			IModalSystem.Current?.CloseAll( immediate );
		}

		/// <summary>
		/// Returns true if any overlay is open
		/// </summary>
		public bool IsOpen => IModalSystem.Current?.IsModalOpen ?? false;

		/// <summary>
		/// Returns true if the pause menu overlay is open
		/// </summary>
		public bool IsPauseMenuOpen => IModalSystem.Current?.IsPauseMenuOpen ?? false;
	}
}
