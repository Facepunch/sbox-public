namespace Sandbox.Mounting;

/// <summary>
/// A mount's entry in config/mounts.json.
/// </summary>
internal class MountSettings
{
	/// <summary>
	/// Whether to mount this game at startup. An unset preference enables it when installed.
	/// </summary>
	public bool? Enabled { get; set; }

	/// <summary>
	/// The game's install directory, dedicated servers use this to locate the game without SteamApps.
	/// </summary>
	public string Path { get; set; }
}
