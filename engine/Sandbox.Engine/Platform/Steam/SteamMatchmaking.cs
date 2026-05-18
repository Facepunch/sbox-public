using Steamworks.Data;

namespace Steamworks;

/// <summary>
/// Functions for clients to access matchmaking services, favorites, and to operate on game lobbies
/// </summary>
internal class SteamMatchmaking : SteamClientClass<SteamMatchmaking>
{
	internal static ISteamMatchmaking Internal => Interface as ISteamMatchmaking;

	internal override void InitializeInterface( bool server )
	{
		SetInterface( server, new ISteamMatchmaking( server ) );
	}

	/// <summary>
	/// Maximum number of characters a lobby metadata key can be
	/// </summary>
	internal static int MaxLobbyKeyLength => 255;

	internal static LobbyQuery LobbyList => new LobbyQuery();

	/// <summary>
	/// Creates a new lobby
	/// </summary>
	internal static async Task<Lobby?> CreateLobbyAsync( LobbyType type, int maxMembers = 100 )
	{
		Assert.NotNull( Internal );

		var lobby = await Internal.CreateLobby( type, maxMembers );
		if ( !lobby.HasValue || lobby.Value.Result != Result.OK ) return null;

		return new Lobby { Id = lobby.Value.SteamIDLobby };
	}

	/// <summary>
	/// Attempts to directly join the specified lobby
	/// </summary>
	internal static async Task<(RoomEnter Response, Lobby? Lobby)> JoinLobbyAsync( SteamId lobbyId )
	{
		Assert.NotNull( Internal );

		var lobby = await Internal.JoinLobby( lobbyId );
		if ( !lobby.HasValue ) return ((RoomEnter)lobby.Value.EChatRoomEnterResponse, null);

		return ((RoomEnter)lobby.Value.EChatRoomEnterResponse,
			new Lobby { Id = lobby.Value.SteamIDLobby });
	}

}
