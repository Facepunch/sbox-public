namespace Sandbox.Mounting;

/// <summary>
/// A mount's entry in config/mounts.json.
/// </summary>
internal class MountSettings
{
	/// <summary>
	/// Is this mount currently enabled.
	/// </summary>
	public bool Enabled { get; set; }

	/// <summary>
	/// The game's install directory, dedicated servers use this to locate the game without SteamApps.
	/// </summary>
	public string Path { get; set; }
}
