using Sandbox.Engine;
using Sandbox.Modals;
using Steamworks;
using Steamworks.Data;
using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Handles callbacks from Steam lobbies and translates them to our Global, Party or Game lobbies.
/// </summary>
internal static class SteamCallbacks
{
	internal static void InitSteamCallbacks()
	{
		SteamFriends.OnPersonaStateChange += SteamFriends_OnPersonaStateChange;
		SteamFriends.OnFriendRichPresenceUpdate += SteamFriends_OnPersonaStateChange;
		SteamFriends.OnGameRichPresenceJoinRequested += SteamFriends_OnGameRichPresenceJoinRequested;
		SteamFriends.OnGameLobbyJoinRequested += SteamFriends_OnGameLobbyJoinRequested;
	}

	private static void SteamFriends_OnGameRichPresenceJoinRequested( Steamworks.Friend friend, string connectStr )
	{
		using var scope = GlobalContext.MenuScope();
		Api.Activity.GameRequested( new( "invite" ) );
		ConsoleSystem.Run( "connect", connectStr.Split( ' ' ).Last() );
	}

	private static void SteamFriends_OnGameLobbyJoinRequested( Sandbox.SteamId steamId )
	{
		IGameInstanceDll.Current.CloseGame();
		_ = TryJoinLobby( steamId );
	}

	/// <summary>
	/// Steam launches the game with <c>+connect_lobby &lt;id&gt;</c> when an invite is accepted while it isn't
	/// running - the launch equivalent of <see cref="SteamFriends_OnGameLobbyJoinRequested"/>.
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
		ConsoleSystem.Run( "connect", steamId.Value );
	}

	private static void SteamFriends_OnPersonaStateChange( Steamworks.Friend obj )
	{
		using var scope = GlobalContext.MenuScope();
		Event.Run( "friend.change", new Sandbox.Friend( obj ) );
	}
}
