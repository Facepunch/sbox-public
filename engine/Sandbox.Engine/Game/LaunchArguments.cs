using Sandbox.Network;

namespace Sandbox;

/// <summary>
/// These are arguments that were set when launching the current game.
/// This is used to pre-configure the game from the menu
/// </summary>
public static class LaunchArguments
{
	/// <summary>
	/// The map to start with. It's really up to the game to use this
	/// </summary>
	public static string Map { get; set; }

	/// <summary>
	/// Preferred max players for multiplayer games. Used by games, but not enforced.
	/// </summary>
	public static int MaxPlayers { get; set; }

	/// <summary>
	/// Default privacy for lobbies created on game start.
	/// </summary>
	public static LobbyPrivacy Privacy { get; set; } = LobbyPrivacy.Public;

	/// <summary>
	/// The game settings to apply on join. These are a list of convars.
	/// </summary>
	public static Dictionary<string, string> GameSettings { get; set; }

	/// <summary>
	/// The hostname for the server.
	/// </summary>
	public static string ServerName { get; set; }

	/// <summary>
	/// Should be called when leaving a game to set the properties back to default. We need to be
	/// aware and prevent these leaking between games.
	/// </summary>
	internal static void Reset()
	{
		GameSettings = default;
		Map = default;
		Privacy = default;
		ServerName = default;
		MaxPlayers = 0;
	}

	// TODO - save launch arguments and restore them, per game.
}
