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
	/// Privacy for lobbies created on game start. The more private of this and the lobby's own config wins,
	/// so neither the menu nor the game can open up a lobby the other made private. If not explicitly set,
	/// lobbies use their own config.
	/// </summary>
	public static LobbyPrivacy Privacy
	{
		get => PrivacyOverride ?? LobbyPrivacy.Public;
		set => PrivacyOverride = value;
	}

	internal static LobbyPrivacy? PrivacyOverride { get; private set; }

	/// <summary>
	/// The privacy a lobby asking for <paramref name="requested"/> ends up with - the more private of that and
	/// <see cref="PrivacyOverride"/>.
	/// </summary>
	internal static LobbyPrivacy ResolvePrivacy( LobbyPrivacy requested )
	{
		if ( PrivacyOverride is not { } privacy ) return requested;
		return Rank( privacy ) > Rank( requested ) ? privacy : requested;
	}

	/// <summary>
	/// How closed a privacy mode is. Not the enum's own order, which has Private before FriendsOnly.
	/// </summary>
	static int Rank( LobbyPrivacy privacy ) => privacy switch
	{
		LobbyPrivacy.Public => 0,
		LobbyPrivacy.FriendsOnly => 1,
		_ => 2
	};

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
		PrivacyOverride = null;
		ServerName = default;
		MaxPlayers = 0;
	}

	// TODO - save launch arguments and restore them, per game.
}
