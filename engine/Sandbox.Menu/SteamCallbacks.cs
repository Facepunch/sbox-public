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
		ConsoleSystem.Run( "connect", connectStr.Split( ' ' ).Last() );
	}

	private static void SteamFriends_OnGameLobbyJoinRequested( Sandbox.SteamId steamId )
	{
		IGameInstanceDll.Current.CloseGame();
		_ = TryJoinLobby( steamId );
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
		ConsoleSystem.Run( "connect", steamId.Value );
	}

	private static void SteamFriends_OnPersonaStateChange( Steamworks.Friend obj )
	{
		using var scope = GlobalContext.MenuScope();
		Event.Run( "friend.change", new Sandbox.Friend( obj ) );
	}
}
