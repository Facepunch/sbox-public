using Sandbox.Engine;
using Sandbox.Modals;
using Sandbox.SteamApi;
using System.Threading.Tasks;

namespace Sandbox;

internal sealed partial class MenuDll
{
	// Startup pumps Steam callbacks as soon as the menu DLL exists. Only handle them once
	// Initialize enables menu callbacks, and stop as soon as teardown begins.
	bool steamCallbacksReady;

	/// <summary>
	/// Steam was asked to run us again while we're already running, e.g. the Play button on sbox.game.
	/// Startup reads the same launch command line in <see cref="MenuUtility.StartupGameIdent"/>.
	/// </summary>
	void IMenuDll.OnNewSteamUrlLaunchParameters()
	{
		if ( !steamCallbacksReady ) return;

		using var scope = GlobalContext.MenuScope();

		if ( Application.IsEditor ) return;

#pragma warning disable CS0612 // Type or member is obsolete
		var switches = Utility.CommandLine.Parse( SteamNative.Apps.GetCommandLine() );
#pragma warning restore CS0612 // Type or member is obsolete

		// Any web page can send this, so only honour opening a game page - never run other switches
		var ident = switches.GetValueOrDefault( "rungame" );
		if ( string.IsNullOrWhiteSpace( ident ) ) return;

		var referrer = Api.Activity.NormalizeWebReferrer( switches.GetValueOrDefault( "webreferrer" ) );
		Discovery.Clicked( new DiscoveryContext { Surface = "web", Referrer = referrer }, ident );
		Game.Overlay.ShowGameModal( ident );

		// The browser has focus, so get the window in front or the modal opens where nobody sees it
		if ( GameWindow.Current?.Handle is { } handle && handle != nint.Zero )
		{
			NativeEngine.Sdl.RaiseWindow( handle );
			NativeEngine.Sdl.FlashWindow( handle, NativeEngine.Sdl.FlashOperation.UntilFocused );
		}
	}

	void IMenuDll.OnSteamGameJoinRequested( string connectStr )
	{
		if ( !steamCallbacksReady ) return;

		using var scope = GlobalContext.MenuScope();
		Api.Activity.GameRequested( new( "invite" ) );
		MenuUtility.RunCommand( "connect", connectStr.Split( ' ' ).Last() );
	}

	void IMenuDll.OnSteamLobbyJoinRequested( SteamId steamId )
	{
		if ( !steamCallbacksReady ) return;

		IGameInstanceDll.Current.CloseGame();
		_ = TryJoinLobby( steamId );
	}

	/// <summary>
	/// Steam launches the game with <c>+connect_lobby &lt;id&gt;</c> when an invite is accepted while it isn't
	/// running - the launch equivalent of <see cref="IMenuDll.OnSteamLobbyJoinRequested"/>.
	/// </summary>
	[MenuConCmd( "connect_lobby", ConVarFlags.Protected )]
	public static void ConnectLobby( string lobbyId )
	{
		if ( !ulong.TryParse( lobbyId, out var id ) )
		{
			Log.Warning( $"connect_lobby: '{lobbyId}' isn't a lobby id" );
			return;
		}

		_ = TryJoinLobby( id );
	}

	private static async Task TryJoinLobby( Sandbox.SteamId steamId )
	{
		using var scope = GlobalContext.MenuScope();

		// A join from here on, though the lobby's still being looked up
		using var joining = Networking.JoinScope();

		var lobby = new Lobby( steamId.ValueUnsigned );
		if ( await lobby.Refresh() == false )
		{
			IModalSystem.Current?.Notice( "Joining failed", "The lobby doesn't exist anymore.", "heart_broken" );
			return;
		}

		if ( lobby.IsParty )
		{
			_ = PartyRoom.Join( lobby );

			// doesn't matter if they're also in a game already - the PartyRoom will handle connecting to that
			return;
		}

		PartyRoom.Current?.Leave();
		Api.Activity.GameRequested( new( "invite" ) );
		MenuUtility.RunCommand( "connect", steamId.Value );
	}

	void IMenuDll.OnSteamFriendChanged( Friend friend )
	{
		if ( !steamCallbacksReady ) return;

		using var scope = GlobalContext.MenuScope();
		Event.Run( "friend.change", friend );
	}
}
