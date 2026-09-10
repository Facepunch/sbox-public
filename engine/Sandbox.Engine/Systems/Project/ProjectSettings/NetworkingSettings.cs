namespace Sandbox;

/// <summary>
/// A class that holds all configured networking settings for a game.
/// This is serialized as a config and shared from the server to the client.
/// </summary>
[Expose]
public class NetworkingSettings : ConfigData
{
	/// <summary>
	/// Whether to end the game when the host leaves. By default the host hands the game to
	/// another player when they leave, and everyone carries on from a snapshot of the game.
	/// Anything a late joiner would see correctly survives the handoff.
	/// </summary>
	public bool DestroyLobbyWhenHostLeaves { get; set; }

	/// <summary>
	/// No longer does anything. The host only changes when the current host leaves.
	/// </summary>
	[Hide, Obsolete( "The host only changes when the current host leaves" )]
	public bool AutoSwitchToBestHost { get; set; } = true;

	/// <summary>
	/// By default, can clients create objects. This can be changed per connection after join.
	/// </summary>
	[Title( "Client Object Spawning" )]
	[Group( "Default Client Permissions" )]
	public bool ClientsCanSpawnObjects { get; set; } = true;

	/// <summary>
	/// By default, can clients refresh objects. This can be changed per connection after join.
	/// </summary>
	[Title( "Client Object Refreshing" )]
	[Group( "Default Client Permissions" )]
	public bool ClientsCanRefreshObjects { get; set; } = true;

	/// <summary>
	/// By default, can clients destroy objects. This can be changed per connection after join.
	/// </summary>
	[Title( "Client Object Destroying" )]
	[Group( "Default Client Permissions" )]
	public bool ClientsCanDestroyObjects { get; set; } = true;

	/// <summary>
	/// The frequency at which the network system will send updates to clients. Higher is better but
	/// you probably want to stay in the 10-60 range.
	/// </summary>
	public float UpdateRate { get; set; } = 30;

}
