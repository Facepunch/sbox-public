namespace Sandbox.Network;

[Expose]
public struct LobbyConfig
{
	/// <summary>
	/// Whether to end the game when the host leaves. By default the host hands the game to
	/// another player instead. This is only applicable to P2P lobbies.
	/// </summary>
	public bool DestroyWhenHostLeaves { get; set; }

	/// <summary>
	/// No longer does anything. The host only changes when the current host leaves.
	/// </summary>
	[Obsolete( "The host only changes when the current host leaves" )]
	public bool AutoSwitchToBestHost { get; set; }

	/// <summary>
	/// Whether to hide this lobby from appearing in the server list. It will still be
	/// queryable programatically, so long as the <see cref="Privacy"/> mode allows it.
	/// </summary>
	public bool Hidden { get; set; }

	/// <summary>
	/// Determines who is able to connect to this lobby. This will be public by default.
	/// </summary>
	public LobbyPrivacy Privacy { get; set; }

	/// <summary>
	/// The maximum amount of players this lobby can hold. By default, this will be
	/// the Max Players set in the current Game Package's project settings.
	/// </summary>
	public int MaxPlayers { get; set; }

	/// <summary>
	/// The name of this lobby. If this isn't set, a default lobby name will be chosen instead.
	/// </summary>
	public string Name { get; set; }

	public LobbyConfig()
	{
		DestroyWhenHostLeaves = ProjectSettings.Networking.DestroyLobbyWhenHostLeaves;
		MaxPlayers = Application.GamePackage?.GetCachedMeta( "MaxPlayers", 32 ) ?? 32;
	}
}
