namespace Sandbox.SteamApi;

/// <summary>
/// Functions for clients to access matchmaking services, favorites, and to operate on game lobbies
/// </summary>
internal static class SteamMatchmaking
{
	internal static bool IsAvailable => SteamClient.IsValid && SteamNative.Matchmaking.IsAvailable();

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
		if ( !IsAvailable ) return null;

		var call = SteamNative.Matchmaking.CreateLobby( type, maxMembers );
		var id = await SteamCalls.GetResultAsync( call, SteamNative.Matchmaking.GetLobbyCreatedResult );
		return id != 0 ? new Lobby( id ) : null;
	}

	/// <summary>
	/// Attempts to directly join the specified lobby
	/// </summary>
	internal static async Task<(RoomEnter Response, Lobby? Lobby)> JoinLobbyAsync( SteamId lobbyId )
	{
		if ( !IsAvailable ) return (RoomEnter.Error, null);

		var call = SteamNative.Matchmaking.JoinLobby( lobbyId );
		return await SteamCalls.GetResultAsync( call, ReadJoinResult, fallback: (RoomEnter.Error, (Lobby?)null) );
	}

	static (RoomEnter, Lobby?) ReadJoinResult( ulong call )
	{
		if ( !SteamNative.Matchmaking.GetLobbyEnterResult( call, out var id, out var response ) )
			return (RoomEnter.Error, null);

		return ((RoomEnter)response, new Lobby( id ));
	}

}
